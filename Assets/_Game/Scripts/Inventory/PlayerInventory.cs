using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using ZombieSurvival.Combat;
using ZombieSurvival.Items;
using ZombieSurvival.Player;

namespace ZombieSurvival.Inventory
{
    // 플레이어가 소유한 루트 격자, 장비 슬롯과 가방/조끼 내부 격자의 실제 상태를 관리합니다.
    // 배치 계산은 InventoryGridUtility, 드래그 판단은 InventoryMoveResolver, 전체 정리는 InventoryAutoSorter에 위임합니다.
    // 이 클래스는 검증된 계획을 목록·수량·좌표에 적용하고 InventoryChanged 이벤트를 한 번 통지하는 조정자 역할만 담당합니다.
    public sealed class PlayerInventory : MonoBehaviour
    {
        [Serializable]
        public sealed class GridData
        {
            // Root, 가방과 조끼가 공통으로 사용하는 직사각형 보관 공간입니다.
            // Entries 변경은 PlayerInventory의 Try 메서드를 통해 수행해야 충돌 검증과 이벤트가 유지됩니다.
            public int Width { get; }
            public int Height { get; }
            public List<InventoryEntry> Entries { get; } = new List<InventoryEntry>();

            public GridData(int width, int height)
            {
                Width = Mathf.Max(1, width);
                Height = Mathf.Max(1, height);
            }
        }

        [SerializeField, Min(1)] private int gridWidth = 10;
        [SerializeField, Min(1)] private int gridHeight = 6;
        [Header("소모품 사용 시간")]
        [SerializeField, Min(0f)] private float healingUseDuration = 1.5f;
        [SerializeField, Min(0f)] private float foodUseDuration = 1.2f;
        private GridData root;
        private readonly Dictionary<EquipmentSlotType, InventoryEntry> equipment =
            new Dictionary<EquipmentSlotType, InventoryEntry>();
        private Coroutine itemUseCoroutine;

        // 일반 플레이에서는 Awake가 먼저 호출되어 Root가 준비되지만, Unity EditMode 테스트나 에디터 도구는 Awake 전에 공개 API를 호출할 수 있습니다.
        // 그래서 Root를 읽는 순간에도 초기화를 보장하여 null 때문에 명령이 실패하지 않게 하고, 한 번 만든 격자는 계속 같은 객체를 반환합니다.
        public GridData Root
        {
            get
            {
                EnsureRoot();
                return root;
            }
        }
        public IReadOnlyList<InventoryEntry> Entries => Root.Entries;
        public IReadOnlyDictionary<EquipmentSlotType, InventoryEntry> Equipment => equipment;
        public int GridWidth => Root.Width;
        public int GridHeight => Root.Height;
        public bool IsUsingItem => itemUseCoroutine != null;
        public float ItemUseProgress { get; private set; }
        public string CurrentItemUseName { get; private set; } = string.Empty;
        public event Action InventoryChanged;
        public event Action<ItemDefinition, int> DropRequested;
        // 사용 시작·취소·완료 때 UI가 즉시 상태 문구를 다시 그릴 수 있도록 별도 알림을 제공합니다.
        // 진행률 자체는 매 프레임 이벤트를 보내지 않고 HUD가 공개 값을 읽어 불필요한 전체 격자 재생성을 막습니다.
        public event Action ItemUseStateChanged;

        // 탄창이나 방어구 내구도처럼 목록·좌표가 아닌 아이템 내부 상태만 바뀌었을 때 화면과 저장 계층에 알립니다.
        // 상태를 바꾼 담당 코드가 이 메서드를 한 번 호출하면 모든 HUD가 같은 최종 값을 다시 읽습니다.
        public void NotifyItemStateChanged() => InventoryChanged?.Invoke();

        private void Awake()
        {
            // Inspector에서 지정한 크기로 플레이어의 기본 보관 격자를 준비합니다.
            // 에디터 도구가 Awake보다 먼저 Root를 읽었다면 이미 만든 객체를 유지하여 그 안의 아이템이 초기화 과정에서 사라지지 않게 합니다.
            EnsureRoot();
        }

        // Root 생성 경로를 한곳으로 모아 런타임 Awake와 EditMode의 지연 초기화가 서로 다른 격자를 만들지 않게 합니다.
        // 이 메서드는 root가 null일 때만 객체를 만들므로 여러 번 호출해도 기존 Entries와 참조가 그대로 보존됩니다.
        private void EnsureRoot()
        {
            if (root == null) root = new GridData(gridWidth, gridHeight);
        }

        public bool TryAdd(ItemDefinition item, int quantity) => TryAddTo(Root, item, quantity);
        public bool CanAdd(ItemDefinition item, int quantity) => CanAddTo(Root, item, quantity);

        // 테스트 레이드 시작 장비를 일반 격자를 거치지 않고 지정된 장비 슬롯에 직접 장착합니다.
        // 이미 해당 슬롯에 플레이어가 준비한 장비가 있으면 덮어쓰지 않으며, 지급 표시는 결과 보상에서 제외하는 데 사용됩니다.
        public bool TryEquipRaidProvidedItem(ItemDefinition item)
        {
            EquipmentSlotType slot = item != null ? item.EquipmentSlot : EquipmentSlotType.None;
            if (slot == EquipmentSlotType.None || equipment.ContainsKey(slot)) return false;

            var entry = new InventoryEntry(item, 1, Vector2Int.zero, false, true);
            equipment.Add(slot, entry);
            InventoryChanged?.Invoke();
            return true;
        }

        // 자동 정리 계산이 전부 성공한 뒤에만 기존 목록을 새 계획으로 교체합니다.
        // 계산 실패 시 placements가 적용되지 않으므로 원래 수량, 위치와 회전 상태가 그대로 보존됩니다.
        public bool TryAutoSort(GridData grid)
        {
            if (!InventoryGridOperations.TryAutoSort(grid)) return false;
            InventoryChanged?.Invoke();
            return true;
        }

        // 탈출 조건과 퀘스트가 ItemId 기준으로 플레이어의 전체 보유 수량을 조회할 때 사용합니다.
        // 루트, 장착 장비와 모든 중첩 컨테이너 내부까지 재귀적으로 포함합니다.
        public int CountItem(string itemId)
        {
            if (string.IsNullOrWhiteSpace(itemId)) return 0;

            int total = InventoryGridContent.CountItem(Root, itemId);
            foreach (InventoryEntry equipped in equipment.Values)
            {
                if (equipped.Item.ItemId == itemId) total += equipped.Quantity;
                total += InventoryGridContent.CountItem(equipped.Contents, itemId);
            }
            return total;
        }

        // 현재 플레이어가 가진 특정 탄종의 전체 수량을 반환합니다.
        // 루트 격자뿐 아니라 루트 안의 가방과 장착한 조끼·가방 내부까지 재귀적으로 찾아 실제 재장전 가능 수량과 HUD 숫자를 일치시킵니다.
        public int CountAmmo(AmmoType caliber)
        {
            if (caliber == AmmoType.None) return 0;

            int total = InventoryGridContent.CountAmmo(Root, caliber);
            foreach (InventoryEntry equipped in equipment.Values)
                total += InventoryGridContent.CountAmmo(equipped.Contents, caliber);
            return total;
        }

        // 재장전이 요청한 수량까지 같은 탄종의 실제 InventoryEntry에서 제거합니다.
        // 가진 수량이 부족하면 남아 있는 만큼만 소비하며, 여러 묶음과 컨테이너에 나뉜 탄약도 하나의 재장전으로 이어서 사용합니다.
        public bool TryConsumeAmmo(AmmoType caliber, int requestedQuantity, out int consumedQuantity)
        {
            consumedQuantity = 0;
            if (caliber == AmmoType.None || requestedQuantity <= 0) return false;

            consumedQuantity = InventoryGridContent.ConsumeAmmo(Root, caliber, requestedQuantity);
            int remaining = requestedQuantity - consumedQuantity;
            if (remaining > 0)
            {
                foreach (InventoryEntry equipped in equipment.Values)
                {
                    if (equipped.Contents == null) continue;
                    int consumedFromContainer = InventoryGridContent.ConsumeAmmo(
                        equipped.Contents,
                        caliber,
                        remaining);
                    consumedQuantity += consumedFromContainer;
                    remaining -= consumedFromContainer;
                    if (remaining <= 0) break;
                }
            }

            if (consumedQuantity <= 0) return false;
            // 여러 묶음을 소비해도 마지막에 한 번만 알려 HUD와 전투 코드가 완성된 최종 수량을 읽게 합니다.
            InventoryChanged?.Invoke();
            return true;
        }

        // 전체 수량이 들어갈 수 있는지 먼저 가상 검사한 뒤 기존 중첩을 채우고 남은 묶음을 빈자리에 생성합니다.
        // 사전 검사가 실패하면 일부 수량만 들어가는 일이 없으므로 월드 아이템과 인벤토리 수량이 어긋나지 않습니다.
        public bool TryAddTo(GridData grid, ItemDefinition item, int quantity)
        {
            if (!InventoryGridContent.TryAdd(grid, item, quantity)) return false;

            InventoryChanged?.Invoke();
            return true;
        }

        // 실제 Entries를 바꾸지 않고 중첩 여유와 모양 셀 빈자리를 사용해 전체 수량 수용 가능 여부를 계산합니다.
        public bool CanAddTo(GridData grid, ItemDefinition item, int quantity)
        {
            return InventoryGridContent.CanAdd(grid, item, quantity);
        }

        // Resolver가 만든 Move, Merge 또는 Swap 계획을 실제 데이터에 적용합니다.
        // 계획 단계에서는 아무 데이터도 바뀌지 않으므로 아래 commit 전까지 실패해도 아이템이 유실되지 않습니다.
        public bool TryMove(GridData source, GridData target, InventoryEntry entry, Vector2Int position)
        {
            if (!InventoryGridOperations.TryMove(source, target, entry, position)) return false;
            InventoryChanged?.Invoke();
            return true;
        }

        // Ctrl+클릭 빠른 이동이 사용하는 공용 진입점입니다.
        // 현재 회전을 먼저 시도하고 필요할 때만 반대 회전을 시험하는 InventoryGridOperations의 안전 규칙을 그대로 사용합니다.
        public bool TryTransferToFirstSpace(GridData source, GridData target, InventoryEntry entry)
        {
            if (!InventoryGridOperations.TryTransferToFirstSpace(source, target, entry)) return false;
            InventoryChanged?.Invoke();
            return true;
        }

        public bool TryMove(InventoryEntry entry, Vector2Int position) => TryMove(Root, Root, entry, position);

        // 격자에 실제로 소유한 엔트리만 현재 위치에서 90도 회전할 수 있습니다.
        public bool TryRotate(GridData grid, InventoryEntry entry)
        {
            if (!InventoryGridOperations.TryRotate(grid, entry)) return false;
            InventoryChanged?.Invoke();
            return true;
        }

        public bool TryRotate(InventoryEntry entry) => TryRotate(Root, entry);

        // 이전 호출 코드와 테스트를 위한 호환 메서드입니다. 실제 장착은 아래 원자적 교체 메서드가 처리합니다.
        public bool TryEquip(GridData source, InventoryEntry entry)
            => TryEquipOrSwap(source, entry, entry?.Item.EquipmentSlot ?? EquipmentSlotType.None, out _);

        // 새 장비를 슬롯에 넣고 기존 장비를 출발 격자 또는 루트 격자의 빈칸으로 돌려보내는 전체 계획을 먼저 검사합니다.
        // 되돌릴 공간이 없으면 source, equipment와 두 InventoryEntry의 위치를 하나도 바꾸지 않으므로 부분 교체가 남지 않습니다.
        public bool TryEquipOrSwap(
            GridData source,
            InventoryEntry entry,
            EquipmentSlotType targetSlot,
            out string failureReason)
        {
            EquipmentSlotType slot = entry?.Item.EquipmentSlot ?? EquipmentSlotType.None;
            failureReason = string.Empty;
            if (source == null || entry == null || !source.Entries.Contains(entry))
            {
                failureReason = "장착할 아이템이 출발 격자에 없습니다.";
                return false;
            }
            if (slot == EquipmentSlotType.None || slot != targetSlot)
            {
                failureReason = "이 아이템과 장비 슬롯 종류가 맞지 않습니다.";
                return false;
            }

            equipment.TryGetValue(slot, out InventoryEntry previousEquipment);
            GridData returnGrid = null;
            Vector2Int returnPosition = default;
            bool returnRotated = previousEquipment != null && previousEquipment.Rotated;
            if (previousEquipment != null)
            {
                // 새 장비가 빠질 출발 격자를 먼저 후보로 삼아 사용자가 끌어온 자리 주변 흐름을 유지합니다.
                if (InventoryGridOperations.IsGridOwnedByEntry(previousEquipment, source) ||
                    !TryFindEquipmentReturnSpace(source, entry, previousEquipment, out returnPosition, out returnRotated))
                {
                    // 외부 전리품이나 가방 내부에서 장착한 경우 출발 격자에 자리가 없으면 플레이어 루트를 두 번째 안전 장소로 사용합니다.
                    if (source == Root || !TryFindEquipmentReturnSpace(Root, null, previousEquipment, out returnPosition, out returnRotated))
                    {
                        failureReason = "장비 슬롯은 사용 중이며 기존 장비를 돌려놓을 공간이 없습니다.";
                        return false;
                    }
                    returnGrid = Root;
                }
                else
                {
                    returnGrid = source;
                }
            }

            // 위의 모든 검사가 끝난 뒤 실제 목록을 한 번에 변경합니다.
            source.Entries.Remove(entry);
            if (previousEquipment != null)
            {
                equipment.Remove(slot);
                previousEquipment.Place(returnPosition, returnRotated);
                returnGrid.Entries.Add(previousEquipment);
            }

            equipment[slot] = entry;
            InventoryChanged?.Invoke();
            return true;
        }

        // 장비를 해제하기 전에 현재 회전 모양이 루트에 들어갈 빈자리를 찾고 성공한 뒤에만 슬롯에서 제거합니다.
        public bool TryUnequip(EquipmentSlotType slot)
        {
            if (!equipment.TryGetValue(slot, out InventoryEntry entry) ||
                !TryFindEquipmentReturnSpace(Root, null, entry, out Vector2Int position, out bool rotated))
                return false;

            equipment.Remove(slot);
            entry.Place(position, rotated);
            Root.Entries.Add(entry);
            InventoryChanged?.Invoke();
            return true;
        }

        // 장비 슬롯에서 특정 격자 위치로 드래그해 해제할 때 사용합니다.
        // 목표 칸이 비어 있는지 먼저 확인한 뒤 Dictionary와 Entries를 함께 변경하므로 실패 시 장비는 그대로 남습니다.
        public bool TryUnequipTo(
            EquipmentSlotType slot,
            GridData target,
            Vector2Int position,
            out string failureReason)
        {
            failureReason = string.Empty;
            if (!equipment.TryGetValue(slot, out InventoryEntry entry))
            {
                failureReason = "해제할 장비가 없습니다.";
                return false;
            }
            if (target == null)
            {
                failureReason = "장비를 놓을 대상 격자가 없습니다.";
                return false;
            }
            if (InventoryGridOperations.IsGridOwnedByEntry(entry, target))
            {
                failureReason = "컨테이너를 자기 내부에 넣을 수 없습니다.";
                return false;
            }
            if (!InventoryGridUtility.FitsInsideGrid(target, entry, position, entry.Rotated))
            {
                failureReason = "아이템이 격자 경계를 벗어납니다.";
                return false;
            }
            if (!InventoryGridUtility.CanPlace(target, entry, position, entry.Rotated, null))
            {
                failureReason = "그 위치에는 장비를 해제할 수 없습니다.";
                return false;
            }

            equipment.Remove(slot);
            entry.Place(position, entry.Rotated);
            target.Entries.Add(entry);
            InventoryChanged?.Invoke();
            return true;
        }

        // 수량을 절반으로 나누기 전에 같은 모양이 들어갈 별도 공간과 엔트리 소유 여부를 확인합니다.
        public bool TrySplit(GridData grid, InventoryEntry entry)
        {
            if (!InventoryGridOperations.TrySplit(grid, entry)) return false;
            InventoryChanged?.Invoke();
            return true;
        }

        // 의료품은 체력, 음식은 포만감이 실제로 회복되었을 때만 한 개를 소비합니다.
        // 탄약은 더 이상 우클릭으로 별도 Reserve 숫자에 옮기지 않고, 재장전 완료 순간 인벤토리에서 직접 소비합니다.
        public bool TryUse(GridData grid, InventoryEntry entry)
        {
            if (grid == null || entry == null || !grid.Entries.Contains(entry) || !entry.Item.Usable) return false;

            bool used = false;
            if (entry.Item.Type == ItemType.Healing)
            {
                // 고정된 35가 아니라 ItemDefinition의 값을 사용하므로 아이템마다 서로 다른 치료량을 만들 수 있습니다.
                Health health = GetComponent<Health>();
                used = health != null && health.Heal(entry.Item.HealthRestoreAmount);
            }
            else if (entry.Item.Type == ItemType.Food)
            {
                // PlayerSurvival이 없는 메뉴용 테스트 객체에서는 음식을 소비하지 않아 효과 없이 수량만 사라지는 일을 막습니다.
                PlayerSurvival survival = GetComponent<PlayerSurvival>();
                used = survival != null && survival.RestoreSatiety(entry.Item.SatietyRestoreAmount);
            }

            if (!used) return false;

            entry.Consume(1);
            if (entry.Quantity == 0) grid.Entries.Remove(entry);
            InventoryChanged?.Invoke();
            return true;
        }

        // 우클릭 소모품 사용은 즉시 효과를 주지 않고 아이템 종류별 준비 시간이 지난 뒤 TryUse와 같은 효과를 적용합니다.
        // 기다리는 동안 인벤토리 HUD가 이동·회전·버리기를 막으므로 사용 대상의 위치와 수량이 중간에 바뀌지 않습니다.
        public bool TryStartUse(GridData grid, InventoryEntry entry)
        {
            if (!string.IsNullOrEmpty(GetUseFailureReason(grid, entry))) return false;

            float duration = entry.Item.Type == ItemType.Healing ? healingUseDuration : foodUseDuration;
            if (duration <= 0f) return TryUse(grid, entry);

            CurrentItemUseName = entry.Item.DisplayName;
            ItemUseProgress = 0f;
            itemUseCoroutine = StartCoroutine(CompleteItemUseAfterDelay(grid, entry, duration));
            ItemUseStateChanged?.Invoke();
            return true;
        }

        // 사용 요청이 실패했을 때 단순 false 대신 사용자가 바로 이해할 수 있는 이유를 반환합니다.
        // 이 검사는 수량을 줄이거나 체력을 바꾸지 않으므로 HUD가 버튼을 누른 직후 안전하게 호출할 수 있습니다.
        public string GetUseFailureReason(GridData grid, InventoryEntry entry)
        {
            if (IsUsingItem) return "이미 다른 아이템을 사용하고 있습니다.";
            if (grid == null || entry == null || !grid.Entries.Contains(entry)) return "이 아이템은 현재 인벤토리에 없습니다.";
            if (!entry.Item.Usable) return "이 아이템은 직접 사용할 수 없습니다.";

            if (entry.Item.Type == ItemType.Healing)
            {
                Health health = GetComponent<Health>();
                if (health == null) return "체력 시스템을 찾을 수 없습니다.";
                if (health.IsDead) return "사망한 상태에서는 치료할 수 없습니다.";
                if (health.MaxHealth - health.CurrentHealth < 1f) return "체력이 이미 충분합니다.";
                return string.Empty;
            }

            if (entry.Item.Type == ItemType.Food)
            {
                PlayerSurvival survival = GetComponent<PlayerSurvival>();
                if (survival == null) return "포만감 시스템을 찾을 수 없습니다.";
                if (survival.MaximumSatiety - survival.CurrentSatiety < 1f) return "포만감이 이미 충분합니다.";
                return string.Empty;
            }

            return "이 아이템에는 사용할 수 있는 효과가 없습니다.";
        }

        // 인벤토리를 닫거나 레이드가 끝나면 진행 중인 사용을 취소해, 화면이 닫힌 뒤 뒤늦게 아이템 효과가 적용되지 않게 합니다.
        public bool CancelItemUse()
        {
            if (itemUseCoroutine == null) return false;
            StopCoroutine(itemUseCoroutine);
            itemUseCoroutine = null;
            ItemUseProgress = 0f;
            CurrentItemUseName = string.Empty;
            ItemUseStateChanged?.Invoke();
            return true;
        }

        private IEnumerator CompleteItemUseAfterDelay(GridData grid, InventoryEntry entry, float duration)
        {
            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                ItemUseProgress = Mathf.Clamp01(elapsed / duration);
                yield return null;
            }

            // TryUse 안에서 InventoryChanged가 발생할 때 HUD가 완료된 상태를 읽도록 코루틴 참조를 먼저 비웁니다.
            itemUseCoroutine = null;
            ItemUseProgress = 0f;
            CurrentItemUseName = string.Empty;
            TryUse(grid, entry);
            ItemUseStateChanged?.Invoke();
        }

        // 내용물이 든 컨테이너는 단순 월드 아이템으로 바꾸면 내부 데이터가 사라지므로 버리기를 거부합니다.
        public bool TryDrop(GridData grid, InventoryEntry entry)
        {
            if (!string.IsNullOrEmpty(GetDropFailureReason(grid, entry)) || !grid.Entries.Remove(entry)) return false;

            DropRequested.Invoke(entry.Item, entry.Quantity);
            InventoryChanged?.Invoke();
            return true;
        }

        // 버리기 확인을 시작하기 전과 두 번째 Delete를 실제 적용하기 직전에 같은 검사를 실행합니다.
        // 컨테이너 내용물과 테스트 지급 상태를 구체적으로 알려 사용자가 키가 고장 난 것으로 오해하지 않게 합니다.
        public string GetDropFailureReason(GridData grid, InventoryEntry entry)
        {
            if (grid == null || entry == null || !grid.Entries.Contains(entry)) return "이 아이템은 현재 격자에 없습니다.";
            if (DropRequested == null) return "현재 장소에서는 아이템을 바닥에 버릴 수 없습니다.";
            if (entry.IsRaidProvided) return "테스트 지급품은 버릴 수 없습니다.";
            if (entry.Contents?.Entries.Count > 0) return "컨테이너에 내용물이 있어 버릴 수 없습니다.";
            return string.Empty;
        }

        // 장비 교체 시 기존 장비가 들어갈 위치와 회전 상태를 실제 데이터를 바꾸지 않고 찾습니다.
        // 현재 회전을 먼저 유지하고 불가능할 때만 반대 회전을 시험해 빠른 이동과 같은 예측 가능한 규칙을 사용합니다.
        private static bool TryFindEquipmentReturnSpace(
            GridData target,
            InventoryEntry ignoredEntry,
            InventoryEntry equipmentEntry,
            out Vector2Int position,
            out bool rotated)
        {
            rotated = equipmentEntry.Rotated;
            if (InventoryGridUtility.TryFindSpace(target, equipmentEntry.Item, rotated, ignoredEntry, out position))
                return true;

            rotated = !equipmentEntry.Rotated;
            if (InventoryGridUtility.TryFindSpace(target, equipmentEntry.Item, rotated, ignoredEntry, out position))
                return true;

            position = default;
            rotated = equipmentEntry.Rotated;
            return false;
        }

        // 플레이어가 사망해 레이드에 실패한 순간 현재 레이드에서 소유한 모든 아이템을 제거합니다.
        // 루트 목록에서 가방을 제거하면 그 가방이 소유한 Contents도 함께 더 이상 참조되지 않으므로 내부 아이템까지 전부 사라집니다.
        // 장착 장비 Dictionary도 별도로 비워야 손에 든 총기·방탄복·조끼·가방이 결과 화면 동안 남거나 다음 씬으로 전달되지 않습니다.
        public void DiscardAllRaidItems()
        {
            CancelItemUse();
            Root.Entries.Clear();
            equipment.Clear();

            // PlayerCombat과 모든 인벤토리 UI가 빈 장비·빈 격자를 즉시 다시 읽도록 변경 이벤트는 마지막에 한 번만 보냅니다.
            InventoryChanged?.Invoke();
        }

        private void OnDisable() => CancelItemUse();

        // HUD와 테스트가 같은 격자 판정을 사용할 수 있도록 순수 계산 유틸리티의 공개 진입점을 유지합니다.
        public bool CanPlace(
            GridData grid,
            InventoryEntry entry,
            Vector2Int position,
            bool rotated,
            InventoryEntry ignored)
            => InventoryGridUtility.CanPlace(grid, entry, position, rotated, ignored);
    }
}
