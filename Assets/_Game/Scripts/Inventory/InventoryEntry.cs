using System;
using System.Collections.Generic;
using UnityEngine;
using ZombieSurvival.Combat;
using ZombieSurvival.Items;

namespace ZombieSurvival.Inventory
{
    // ItemDefinition이 "아이템 종류"라면 InventoryEntry는 플레이어가 실제로 보유한 한 묶음입니다.
    // 같은 탄약 정의라도 서로 다른 위치에 두 묶음이 있을 수 있으므로 위치, 수량과 회전 상태를 별도로 저장합니다.
    // 가방이나 조끼인 경우 Contents에 독립적인 내부 격자를 만들어 그 안의 아이템까지 함께 보존합니다.
    [Serializable]
    public sealed class InventoryEntry
    {
        public ItemDefinition Item { get; private set; }
        public int Quantity { get; private set; }
        public Vector2Int Position { get; private set; }
        public bool Rotated { get; private set; }
        public PlayerInventory.GridData Contents { get; private set; }
        // 무기 인스턴스 안에 실제로 장전된 탄 수입니다.
        // 같은 종류의 총 두 자루도 서로 다른 InventoryEntry이므로 각자 독립된 탄창 상태를 가질 수 있습니다.
        public int MagazineRounds { get; private set; }
        public bool HasMagazineState => Item.Type == ItemType.Weapon && Item.WeaponDefinition != null;
        // 탄창과 마찬가지로 같은 종류의 무기라도 현재 내구도와 선택한 발사 모드는 각 무기 인스턴스가 따로 보관합니다.
        // 이 값을 ItemDefinition에 두면 소총 한 자루가 닳을 때 플레이어가 가진 모든 소총이 함께 닳는 오류가 생깁니다.
        public float WeaponDurability { get; private set; }
        public WeaponTriggerMode SelectedTriggerMode { get; private set; }
        public bool HasWeaponDurabilityState => HasMagazineState;
        public bool HasTriggerModeState => HasMagazineState;
        public float WeaponDurabilityNormalized => HasWeaponDurabilityState
            ? Mathf.Clamp01(WeaponDurability / Item.WeaponDefinition.MaxDurability)
            : 0f;
        public bool IsWeaponBroken => HasWeaponDurabilityState && WeaponDurability <= 0f;
        // 방탄복 한 벌이 현재 얼마나 더 피해를 흡수할 수 있는지 저장합니다.
        // 같은 방탄복 정의를 사용해도 각각의 InventoryEntry가 별도 값을 가지므로 손상 상태가 서로 섞이지 않습니다.
        public float ArmorDurability { get; private set; }
        public bool HasArmorState => Item.Type == ItemType.Armor && Item.ArmorMaxDurability > 0f;
        // 테스트 레이드가 무료로 지급한 장비인지 표시하는 실행 중 전용 값입니다.
        // 이 값은 저장 스냅샷에 기록하지 않으며 RaidResultFactory가 보상과 분실 수량에서 제외할 때만 사용합니다.
        public bool IsRaidProvided { get; private set; }
        // 장착된 부착물은 무기 InventoryEntry 안에 포함되는 개별 상태입니다.
        // 격자에 따로 남지 않으며 레이드 결과와 거점 저장에서는 각 부착물의 ItemId 목록으로 함께 보존합니다.
        private readonly List<ItemDefinition> installedAttachments = new List<ItemDefinition>();
        public IReadOnlyList<ItemDefinition> InstalledAttachments => installedAttachments;
        public int EffectiveMagazineSize => HasMagazineState
            ? Mathf.Max(1, Item.WeaponDefinition.MagazineSize + SumMagazineCapacityBonus())
            : 0;
        public float AttachmentDamageMultiplier => MultiplyAttachmentValue(a => a.DamageMultiplier);
        public float AttachmentFireIntervalMultiplier => MultiplyAttachmentValue(a => a.FireIntervalMultiplier);
        public float AttachmentAccuracyMultiplier => MultiplyAttachmentValue(a => a.AccuracySpreadMultiplier);
        public float AttachmentReloadMultiplier => MultiplyAttachmentValue(a => a.ReloadDurationMultiplier);
        public int Width => Rotated ? Item.GridHeight : Item.GridWidth;
        public int Height => Rotated ? Item.GridWidth : Item.GridHeight;

        // 아이템 바운딩 박스 안의 특정 셀이 실제로 채워진 모양인지 반환합니다.
        // PlayerInventory의 충돌 검사와 InventoryHud의 그리기/클릭 검사가 같은 함수를 사용하므로 보이는 모양과 판정이 일치합니다.
        public bool OccupiesLocalCell(int localX, int localY) => Item.IsGridCellOccupied(localX, localY, Rotated);

        // 인벤토리 전체 좌표를 이 엔트리의 로컬 좌표로 변환한 뒤 실제 점유 여부를 확인합니다.
        // L자 모양의 빈 모서리처럼 바운딩 박스 안이지만 마스크가 꺼진 셀은 false를 반환합니다.
        public bool OccupiesGridCell(int gridX, int gridY)
            => OccupiesLocalCell(gridX - Position.x, gridY - Position.y);

        public InventoryEntry(
            ItemDefinition item,
            int quantity,
            Vector2Int position,
            bool rotated = false,
            bool isRaidProvided = false,
            int magazineRounds = -1,
            float armorDurability = -1f,
            IEnumerable<ItemDefinition> attachments = null,
            float weaponDurability = -1f,
            bool hasSelectedTriggerMode = false,
            WeaponTriggerMode selectedTriggerMode = WeaponTriggerMode.SemiAutomatic)
        {
            // 정의가 없는 엔트리는 크기, 중첩 한도와 저장 ID를 어느 코드에서도 안전하게 계산할 수 없습니다.
            // 잘못된 엔트리를 나중에 NullReferenceException으로 발견하지 않고 생성 지점에서 원인을 바로 알 수 있게 명시적으로 거부합니다.
            if (item == null) throw new ArgumentNullException(nameof(item));

            Item = item;
            // 직접 생성하는 코드도 아이템 정의의 중첩 한도를 넘지 못하게 해 무기·방어구·컨테이너가 여러 개짜리 한 묶음이 되는 것을 막습니다.
            Quantity = Mathf.Clamp(quantity, 1, item.MaxStack);
            Position = position;
            Rotated = rotated;
            IsRaidProvided = isRaidProvided;
            if (attachments != null)
            {
                // 저장 복원은 부착물 ID를 ItemCatalog로 먼저 해결한 뒤 이 생성자에 전달합니다.
                // 같은 슬롯이 중복된 손상 데이터는 첫 항목만 유지하지 않고 TryRestoreItem 단계에서 전체 복원을 실패시킵니다.
                foreach (ItemDefinition attachment in attachments)
                    if (CanInstallAttachment(attachment) && GetAttachment(attachment.WeaponAttachmentDefinition.Slot) == null)
                        installedAttachments.Add(attachment);
            }
            if (HasMagazineState)
            {
                // 새로 생성하거나 이전 버전 저장에서 복원한 무기는 가득 찬 탄창으로 시작합니다.
                // 새 저장 형식은 0발도 유효한 값이므로 magazineRounds가 0 이상이면 그대로 제한 범위에 맞춰 복원합니다.
                int initialRounds = magazineRounds < 0 ? Item.WeaponDefinition.MagazineSize : magazineRounds;
                MagazineRounds = Mathf.Clamp(initialRounds, 0, EffectiveMagazineSize);
                // -1은 내구도 필드가 없던 구버전 저장 또는 새 무기를 뜻하므로 최대 내구도로 시작합니다.
                // 0은 완전히 파손된 정상 저장값이므로 0 이상 값은 그대로 복원합니다.
                float initialWeaponDurability = weaponDurability < 0f
                    ? Item.WeaponDefinition.MaxDurability
                    : weaponDurability;
                WeaponDurability = Mathf.Clamp(initialWeaponDurability, 0f, Item.WeaponDefinition.MaxDurability);
                SelectedTriggerMode = Item.WeaponDefinition.ClampTriggerMode(
                    hasSelectedTriggerMode ? selectedTriggerMode : Item.WeaponDefinition.TriggerMode);
            }
            if (HasArmorState)
            {
                // -1은 새 아이템 또는 내구도 필드가 없던 이전 저장을 뜻하므로 최대 내구도로 시작합니다.
                // 0은 완전히 파손된 방탄복의 정상 상태이므로 0 이상 값은 그대로 범위 제한해 복원합니다.
                float initialDurability = armorDurability < 0f ? Item.ArmorMaxDurability : armorDurability;
                ArmorDurability = Mathf.Clamp(initialDurability, 0f, Item.ArmorMaxDurability);
            }
            if (item.IsContainer)
                Contents = new PlayerInventory.GridData(item.ContainerWidth, item.ContainerHeight);
        }

        // 발사할 탄이 있을 때 이 무기 인스턴스의 탄창에서 정확히 한 발을 제거합니다.
        public bool TryConsumeMagazineRound()
        {
            if (!HasMagazineState || MagazineRounds <= 0) return false;
            MagazineRounds--;
            return true;
        }

        // 재장전 때 인벤토리에서 실제로 제거한 탄을 빈 탄창 공간까지만 추가하고 장전량을 반환합니다.
        public int AddMagazineRounds(int quantity)
        {
            if (!HasMagazineState || quantity <= 0) return 0;
            int accepted = Mathf.Min(quantity, EffectiveMagazineSize - MagazineRounds);
            MagazineRounds += accepted;
            return accepted;
        }

        // 실제로 발사에 성공한 경우에만 무기 정의의 발사당 감소량만큼 내구도를 줄입니다.
        // 탄약 부족이나 재장전 중처럼 발사가 실패한 입력은 이 메서드를 호출하지 않으므로 내구도를 소모하지 않습니다.
        public float ConsumeWeaponDurability(float amount)
        {
            if (!HasWeaponDurabilityState || amount <= 0f || WeaponDurability <= 0f) return 0f;
            float consumed = Mathf.Min(amount, WeaponDurability);
            WeaponDurability -= consumed;
            return consumed;
        }

        // 거점 수리 성공 뒤 현재 무기만 최대 내구도로 복구하고 실제 회복량을 반환합니다.
        public float RepairWeaponDurability()
        {
            if (!HasWeaponDurabilityState) return 0f;
            float restored = Mathf.Max(0f, Item.WeaponDefinition.MaxDurability - WeaponDurability);
            WeaponDurability = Item.WeaponDefinition.MaxDurability;
            return restored;
        }

        // B키 입력은 현재 무기 정의가 단발과 연사를 모두 지원할 때만 실제 값을 바꿉니다.
        // 반환값으로 UI가 전환 불가능한 무기인지 구분해 정확한 안내를 보여 줄 수 있습니다.
        public bool TryCycleTriggerMode()
        {
            if (!HasTriggerModeState) return false;
            WeaponTriggerMode next = Item.WeaponDefinition.GetNextTriggerMode(SelectedTriggerMode);
            if (next == SelectedTriggerMode) return false;
            SelectedTriggerMode = next;
            return true;
        }

        // 부착물 아이템과 무기 탄종이 맞는지, 실제 슬롯 데이터가 있는지 확인합니다.
        public bool CanInstallAttachment(ItemDefinition attachmentItem)
        {
            return Item.Type == ItemType.Weapon && Item.WeaponDefinition != null &&
                   attachmentItem != null && attachmentItem.Type == ItemType.Attachment &&
                   attachmentItem.WeaponAttachmentDefinition != null &&
                   attachmentItem.WeaponAttachmentDefinition.IsCompatible(Item.WeaponDefinition);
        }

        public ItemDefinition GetAttachment(WeaponAttachmentSlot slot)
        {
            foreach (ItemDefinition installed in installedAttachments)
                if (installed?.WeaponAttachmentDefinition != null && installed.WeaponAttachmentDefinition.Slot == slot)
                    return installed;
            return null;
        }

        // 같은 위치의 부착물이 이미 있으면 목록에서 빼서 replaced로 반환합니다.
        // 호출자는 교체 전에 기존 부착물을 돌려놓을 격자 공간을 먼저 확보해야 하므로 이 메서드는 최종 적용 단계에서만 사용합니다.
        public bool TryInstallAttachment(ItemDefinition attachmentItem, out ItemDefinition replaced)
        {
            replaced = null;
            if (!CanInstallAttachment(attachmentItem)) return false;

            WeaponAttachmentSlot slot = attachmentItem.WeaponAttachmentDefinition.Slot;
            replaced = GetAttachment(slot);
            if (replaced != null) installedAttachments.Remove(replaced);
            installedAttachments.Add(attachmentItem);
            ClampMagazineToEffectiveCapacity();
            return true;
        }

        public bool TryDetachAttachment(WeaponAttachmentSlot slot, out ItemDefinition detached)
        {
            detached = GetAttachment(slot);
            if (detached == null) return false;
            installedAttachments.Remove(detached);
            // 확장 탄창을 제거했을 때 새 용량보다 많은 탄은 프로토타입 규칙상 탄창에서 제거됩니다.
            // 실제 게임에서 탄창 아이템 자체에 탄을 저장하도록 확장하면 이 위치에서 탄창 인스턴스로 옮길 수 있습니다.
            ClampMagazineToEffectiveCapacity();
            return true;
        }

        private void ClampMagazineToEffectiveCapacity()
        {
            if (HasMagazineState) MagazineRounds = Mathf.Clamp(MagazineRounds, 0, EffectiveMagazineSize);
        }

        private int SumMagazineCapacityBonus()
        {
            int total = 0;
            foreach (ItemDefinition installed in installedAttachments)
                if (installed?.WeaponAttachmentDefinition != null)
                    total += installed.WeaponAttachmentDefinition.MagazineCapacityBonus;
            return total;
        }

        private float MultiplyAttachmentValue(Func<WeaponAttachmentDefinition, float> selector)
        {
            float result = 1f;
            foreach (ItemDefinition installed in installedAttachments)
                if (installed?.WeaponAttachmentDefinition != null)
                    result *= selector(installed.WeaponAttachmentDefinition);
            return Mathf.Clamp(result, 0.25f, 4f);
        }

        // 방어구가 실제로 흡수한 피해만큼 내구도를 줄이고 감소한 양을 반환합니다.
        // 남은 내구도보다 큰 값이 들어와도 0 아래로 내려가지 않으므로 파손 상태를 안정적으로 저장할 수 있습니다.
        public float ConsumeArmorDurability(float amount)
        {
            if (!HasArmorState || amount <= 0f || ArmorDurability <= 0f) return 0f;
            float consumed = Mathf.Min(amount, ArmorDurability);
            ArmorDurability -= consumed;
            return consumed;
        }

        // 전달 수량 전체가 아니라 MaxStack까지 들어갈 수 있는 양만 Quantity에 더합니다.
        // 호출자는 반환된 실제 추가량을 원래 수량에서 빼고, 남은 수량을 새 엔트리로 배치할 수 있습니다.
        public int AddQuantity(int quantity)
        {
            int accepted = Mathf.Min(Mathf.Max(0, quantity), Item.MaxStack - Quantity);
            Quantity += accepted;
            return accepted;
        }

        // 인벤토리 검사가 성공한 뒤 위치와 회전 상태를 확정합니다.
        public void Place(Vector2Int position, bool rotated)
        {
            Position = position;
            Rotated = rotated;
        }

        // amount가 0 이하이거나 전체 수량 이상이면 원본을 비우는 잘못된 분할이므로 null을 반환합니다.
        // 성공하면 원본 Quantity를 감소시키고 같은 정의를 가진 새 엔트리를 반환하며,
        // 새 엔트리의 최종 위치는 PlayerInventory가 빈 공간을 확인한 뒤 다시 지정합니다.
        public InventoryEntry Split(int amount)
        {
            if (Item.MaxStack <= 1 || amount <= 0 || amount >= Quantity) return null;
            Quantity -= amount;
            // 일반 아이템 분할에서는 false가 유지되고, 향후 중첩 가능한 지급품이 생겨도 지급 상태가 일반 보상으로 바뀌지 않게 전달합니다.
            return new InventoryEntry(
                Item,
                amount,
                Position,
                Rotated,
                IsRaidProvided,
                MagazineRounds,
                ArmorDurability,
                installedAttachments,
                WeaponDurability,
                true,
                SelectedTriggerMode);
        }

        // 사용하거나 버린 수량만큼 감소했는지 반환합니다.
        public bool Consume(int amount)
        {
            if (amount <= 0 || amount > Quantity) return false;
            Quantity -= amount;
            return true;
        }

        // 자동 정리는 같은 아이템 묶음들을 합친 결과 수량을 가상으로 계산한 뒤 모든 배치가 성공했을 때만 이 값을 호출합니다.
        // PlayerInventory와 같은 런타임 어셈블리 안에서만 사용할 수 있게 internal로 제한해 UI가 검증 없이 수량을 바꾸지 못하게 합니다.
        internal void SetQuantityAfterSort(int quantity)
        {
            Quantity = Mathf.Clamp(quantity, 1, Item.MaxStack);
        }
    }
}
