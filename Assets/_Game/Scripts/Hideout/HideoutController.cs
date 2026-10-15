using System;
using System.Collections.Generic;
using UnityEngine;
using ZombieSurvival.Combat;
using ZombieSurvival.Core;
using ZombieSurvival.Inventory;
using ZombieSurvival.Items;

namespace ZombieSurvival.Hideout
{
    // Hideout의 창고와 출격 준비를 실제 격자로 복원하고 드래그·회전·자동 정리 명령을 처리합니다.
    // 디스크에는 여전히 Unity 오브젝트가 아닌 스냅샷을 저장하며, 성공한 격자 변경 뒤에만 메모리 저장소와 JSON을 갱신합니다.
    public sealed class HideoutController : MonoBehaviour
    {
        [SerializeField] private ItemCatalog itemCatalog;
        [SerializeField] private HideoutMissionDefinition[] missionDefinitions;
        [SerializeField] private HideoutFacilityDefinition[] facilityDefinitions;
        [SerializeField] private HideoutRecipeDefinition[] recipeDefinitions;
        // 거점 플레이어의 실제 인벤토리를 출격 준비 격자와 같은 객체로 사용합니다.
        // 별도 사본을 두지 않아 Tab 인벤토리, 창고 화면과 다음 레이드가 항상 같은 장비·가방 내용을 보게 됩니다.
        [SerializeField] private PlayerInventory playerInventory;
        [SerializeField] private string raidSceneName = GameSceneNavigator.RaidSceneName;
        [SerializeField, Min(1)] private int stashWidth = 12;
        [SerializeField, Min(1)] private int stashHeight = 10;
        [SerializeField, Min(1)] private int loadoutWidth = 10;
        [SerializeField, Min(1)] private int loadoutHeight = 6;

        private readonly List<RaidItemSnapshot> unresolvedStashItems = new List<RaidItemSnapshot>();
        private readonly List<RaidItemSnapshot> unresolvedLoadoutItems = new List<RaidItemSnapshot>();
        private float nextCraftAutosaveTime;
        private float nextCraftUiRefreshTime;

        // 남은 제작 시간을 화면에 초 단위로 보여 주는 데는 매 프레임 갱신이 필요하지 않습니다.
        // 0.2초 간격이면 숫자는 충분히 자연스럽게 바뀌면서 여러 UI 패널의 문자열 재생성 비용을 크게 줄일 수 있습니다.
        private const float CraftUiRefreshInterval = 0.2f;

        public PlayerInventory.GridData StashGrid { get; private set; }
        public PlayerInventory.GridData LoadoutGrid { get; private set; }
        public RaidResult ProcessedResult { get; private set; }
        public string PersistenceStatus { get; private set; }
        public string OperationStatus { get; private set; }
        public ItemCatalog ItemCatalog => itemCatalog;
        public PlayerInventory PlayerInventory => playerInventory;
        public IReadOnlyList<HideoutFacilityDefinition> FacilityDefinitions
            => facilityDefinitions ?? Array.Empty<HideoutFacilityDefinition>();
        public int Credits => HideoutEconomyStateStore.Credits;
        public IReadOnlyList<ItemDefinition> MarketItems
            => itemCatalog != null ? itemCatalog.Definitions : Array.Empty<ItemDefinition>();
        public HideoutMissionDefinition ActiveMissionDefinition
            => FindMission(HideoutMissionStateStore.ActiveMissionId);
        public string ActiveMissionSummary
        {
            get
            {
                HideoutMissionDefinition mission = ActiveMissionDefinition;
                return mission == null
                    ? "진행 중인 임무 없음"
                    : $"임무: {mission.DisplayName} · {mission.BuildProgressDescription(HideoutMissionStateStore.ObjectiveProgress)}";
            }
        }
        public event Action StateChanged;

        // 에디터 생성기가 Hideout 씬에 공용 카탈로그와 임무 목록을 연결하는 명시적인 설정 진입점입니다.
        // 임무 목록을 컨트롤러도 가지는 이유는 씬 Awake 시점에 직전 레이드 결과를 목표 종류에 맞게 계산해야 하기 때문입니다.
        public void Configure(
            ItemCatalog catalog,
            HideoutMissionDefinition[] missions = null,
            HideoutFacilityDefinition[] facilities = null,
            HideoutRecipeDefinition[] recipes = null)
        {
            itemCatalog = catalog;
            if (missions != null) missionDefinitions = missions;
            if (facilities != null) facilityDefinitions = facilities;
            if (recipes != null) recipeDefinitions = recipes;
        }

        // 씬 생성기가 거점 플레이어를 명시적으로 연결합니다. 런타임 Awake 전에 직렬화된 참조가 준비되므로 저장 복원도 이 인벤토리에 바로 적용됩니다.
        public void ConfigurePlayerInventory(PlayerInventory inventory) => playerInventory = inventory;

        private void Awake()
        {
            bool loaded = HideoutSaveService.LoadDefaultIfNeeded();
            if (!loaded) PersistenceStatus = "저장 파일을 읽지 못해 현재 메모리 상태를 사용합니다.";

            // 저장된 목록에 새 레이드 보상을 먼저 합친 뒤 넓은 창고 격자와 출격 격자로 복원합니다.
            HideoutMissionStateStore.RegisterDefinitions(missionDefinitions);
            ProcessedResult = HideoutStateStore.ApplyPendingRaidResult();
            if (ProcessedResult != null) HideoutEconomyStateStore.AdvanceStockRotation();
            // 임무는 거점에서 받은 뒤 여러 레이드를 거칠 수 있으므로 직전 결과의 실제 처치 수를 창고 복원과 같은 시점에 한 번만 누적합니다.
            HideoutMissionStateStore.ApplyRaidResult(ProcessedResult, FindMission(HideoutMissionStateStore.ActiveMissionId));
            RestoreRuntimeGrids();
            if (ProcessedResult != null) PersistRuntimeState();
        }

        private void OnEnable()
        {
            if (playerInventory == null) playerInventory = FindObjectOfType<PlayerInventory>();
            if (playerInventory != null)
            {
                playerInventory.InventoryChanged -= HandlePlayerInventoryChanged;
                playerInventory.InventoryChanged += HandlePlayerInventoryChanged;
            }
        }

        private void OnDisable()
        {
            if (playerInventory != null) playerInventory.InventoryChanged -= HandlePlayerInventoryChanged;
        }

        private void Update()
        {
            // 일시정지 메뉴에서도 제작이 흐르도록 unscaledDeltaTime을 사용하되 발전기가 꺼지거나 연료가 없으면 저장소가 시간을 줄이지 않습니다.
            if (!HideoutCraftQueueStateStore.Tick(Time.unscaledDeltaTime)) return;

            // 제작 중 매 프레임 모든 거점 UI를 다시 그리지 않고 최대 초당 다섯 번만 남은 시간을 알려 줍니다.
            // 제작 완료와 연료 소진 자체는 저장소에서 즉시 상태를 바꾸며, 이 알림도 최대 0.2초 안에 화면에 반영됩니다.
            if (Time.unscaledTime >= nextCraftUiRefreshTime)
            {
                nextCraftUiRefreshTime = Time.unscaledTime + CraftUiRefreshInterval;
                StateChanged?.Invoke();
            }

            // 매 프레임 디스크에 쓰지 않고 5초마다 남은 시간·연료를 저장해 I/O 비용과 데이터 손실 범위를 함께 제한합니다.
            if (Time.unscaledTime < nextCraftAutosaveTime) return;
            nextCraftAutosaveTime = Time.unscaledTime + 5f;
            PersistRuntimeState();
        }

        public ItemDefinition FindDefinition(string itemId) => itemCatalog?.Find(itemId);

        public int CountStashItem(string itemId)
            => InventoryGridContent.CountItem(StashGrid, itemId);

        // 강화·제작 UI가 버튼 활성화 여부와 실제 제작 실행에 똑같은 규칙을 사용하도록 공개한 사전 검사입니다.
        // 재료 수량, 발전기 할인, 시설 레벨과 제작 결과를 놓을 창고 공간까지 모두 검사하므로
        // 화면에서는 제작 가능으로 보였지만 버튼을 누르면 실패하는 서로 다른 판정이 생기지 않습니다.
        public bool CanCraft(
            HideoutFacility facility,
            HideoutRecipeDefinition recipe,
            out string failureReason)
        {
            HideoutCraftJobState job = recipe != null ? HideoutCraftQueueStateStore.FindJob(recipe.RecipeId) : null;
            if (job != null)
            {
                if (!job.completed)
                {
                    failureReason = $"제작 중입니다. 남은 시간 {job.remainingSeconds:0.0}초";
                    return false;
                }
                ItemDefinition output = itemCatalog != null ? itemCatalog.Find(job.outputItemId) : null;
                bool canClaim = output != null && InventoryGridContent.CanAdd(StashGrid, output, job.outputQuantity);
                failureReason = canClaim ? string.Empty : "완성품을 수령할 창고 공간이 없습니다.";
                return canClaim;
            }
            return HideoutCraftingService.CanCraft(
                StashGrid,
                facility?.Definition,
                FindFacility(HideoutFacilityType.Generator),
                recipe,
                out failureReason);
        }

        public HideoutCraftJobState GetCraftJob(HideoutRecipeDefinition recipe)
            => recipe != null ? HideoutCraftQueueStateStore.FindJob(recipe.RecipeId) : null;

        // 현재 시설 레벨에서 사용할 수 있는 제작법 개수입니다. 배열 순서는 HUD의 이전·다음 선택 순서가 됩니다.
        public int GetUnlockedRecipeCount(HideoutFacilityDefinition facility)
        {
            if (facility == null || recipeDefinitions == null) return 0;
            int level = HideoutFacilityStateStore.GetLevel(facility);
            int count = 0;
            foreach (HideoutRecipeDefinition recipe in recipeDefinitions)
                if (recipe != null && recipe.FacilityType == facility.FacilityType && recipe.RequiredFacilityLevel <= level)
                    count++;
            return count;
        }

        // 공개된 제작법 중 화면 인덱스에 해당하는 하나를 반환합니다.
        // 잠긴 제작법은 건너뛰므로 시설 레벨이 오르면 기존 순서를 유지한 채 뒤에 새 항목이 나타납니다.
        public HideoutRecipeDefinition GetUnlockedRecipe(HideoutFacilityDefinition facility, int unlockedIndex)
        {
            if (unlockedIndex < 0 || facility == null || recipeDefinitions == null) return null;
            int level = HideoutFacilityStateStore.GetLevel(facility);
            int current = 0;
            foreach (HideoutRecipeDefinition recipe in recipeDefinitions)
            {
                if (recipe == null || recipe.FacilityType != facility.FacilityType || recipe.RequiredFacilityLevel > level)
                    continue;
                if (current == unlockedIndex) return recipe;
                current++;
            }
            return null;
        }

        public int GetCraftingDiscountPercent()
            => HideoutCraftingService.GetGeneratorDiscount(FindFacility(HideoutFacilityType.Generator));

        // 상인 레벨 효과는 구매 할인율로 사용하고, 판매 보너스는 할인율의 절반만 적용합니다.
        // 구매·판매 보정을 다르게 두면 높은 상인 레벨에서도 같은 상품을 왕복 거래해 화폐를 늘릴 수 없습니다.
        public int GetTraderDiscountPercent()
        {
            HideoutFacilityDefinition trader = FindFacility(HideoutFacilityType.Trader);
            return trader == null
                ? 0
                : Mathf.Clamp(trader.GetEffectValue(HideoutFacilityStateStore.GetLevel(trader)), 0, 35);
        }

        public int GetTraderSellBonusPercent()
            => Mathf.Clamp(GetTraderDiscountPercent() / 2, 0, 20);

        public bool TryBuy(ItemDefinition item, int quantity)
        {
            bool bought = HideoutTradeService.TryBuy(
                StashGrid,
                item,
                quantity,
                GetTraderDiscountPercent(),
                out string resultMessage);
            OperationStatus = resultMessage;
            if (bought) PersistRuntimeState();
            else StateChanged?.Invoke();
            return bought;
        }

        public bool TrySell(PlayerInventory.GridData source, InventoryEntry entry, int quantity)
        {
            // 프로토타입 상인은 창고 안의 아이템만 매입합니다. 출격 준비품은 창고로 되돌린 뒤 판매해야
            // 실수로 다음 레이드 장비를 버튼 한 번에 없애는 일을 줄일 수 있습니다.
            if (source != StashGrid)
            {
                OperationStatus = "출격 준비품은 판매할 수 없습니다. 먼저 창고로 되돌려 주세요.";
                StateChanged?.Invoke();
                return false;
            }

            bool sold = HideoutTradeService.TrySell(
                source,
                entry,
                quantity,
                GetTraderSellBonusPercent(),
                out string resultMessage);
            OperationStatus = resultMessage;
            if (sold) PersistRuntimeState();
            else StateChanged?.Invoke();
            return sold;
        }

        // 거점 격자의 부착물을 다른 격자에 있는 무기에 설치합니다.
        // 교체될 부착물의 빈자리를 먼저 찾은 뒤 최종 상태를 한 번에 적용해 공간 부족 시 원본을 그대로 보존합니다.
        public bool TryInstallAttachment(
            PlayerInventory.GridData source,
            InventoryEntry attachmentEntry,
            PlayerInventory.GridData weaponGrid,
            InventoryEntry weapon)
        {
            if (source == null || weaponGrid == null || attachmentEntry == null || weapon == null ||
                !source.Entries.Contains(attachmentEntry) || !weaponGrid.Entries.Contains(weapon) ||
                attachmentEntry.Quantity != 1 || !weapon.CanInstallAttachment(attachmentEntry.Item))
            {
                OperationStatus = "이 부착물은 선택한 무기와 호환되지 않습니다.";
                StateChanged?.Invoke();
                return false;
            }

            WeaponAttachmentSlot slot = attachmentEntry.Item.WeaponAttachmentDefinition.Slot;
            ItemDefinition previous = weapon.GetAttachment(slot);
            Vector2Int returnPosition = Vector2Int.zero;
            bool returnRotated = false;
            if (previous != null && !TryFindItemSpace(source, attachmentEntry, previous, out returnPosition, out returnRotated))
            {
                OperationStatus = "교체될 부착물을 돌려놓을 격자 공간이 없습니다.";
                StateChanged?.Invoke();
                return false;
            }

            source.Entries.Remove(attachmentEntry);
            if (!weapon.TryInstallAttachment(attachmentEntry.Item, out ItemDefinition replaced))
            {
                source.Entries.Add(attachmentEntry);
                OperationStatus = "부착물 설치에 실패해 원래 상태로 되돌렸습니다.";
                StateChanged?.Invoke();
                return false;
            }
            if (replaced != null)
                source.Entries.Add(new InventoryEntry(replaced, 1, returnPosition, returnRotated));

            OperationStatus = $"{attachmentEntry.Item.DisplayName}을 {weapon.Item.DisplayName}에 장착했습니다.";
            PersistRuntimeState();
            return true;
        }

        public bool TryDetachAttachment(
            PlayerInventory.GridData weaponGrid,
            InventoryEntry weapon,
            WeaponAttachmentSlot slot,
            PlayerInventory.GridData target)
        {
            ItemDefinition installed = weapon?.GetAttachment(slot);
            if (weaponGrid == null || weapon == null || !weaponGrid.Entries.Contains(weapon) || installed == null)
            {
                OperationStatus = "해제할 부착물이 없습니다.";
                StateChanged?.Invoke();
                return false;
            }
            if (!TryFindItemSpace(target, null, installed, out Vector2Int position, out bool rotated))
            {
                OperationStatus = "해제한 부착물을 놓을 공간이 없습니다.";
                StateChanged?.Invoke();
                return false;
            }
            if (!weapon.TryDetachAttachment(slot, out ItemDefinition detached) || detached == null)
            {
                OperationStatus = "부착물을 해제하지 못했습니다.";
                StateChanged?.Invoke();
                return false;
            }

            target.Entries.Add(new InventoryEntry(detached, 1, position, rotated));
            OperationStatus = $"{detached.DisplayName}을 무기에서 해제했습니다.";
            PersistRuntimeState();
            return true;
        }

        // 선택한 무기가 현재 거점의 창고·플레이어 인벤토리·장비 중 하나에 실제로 존재할 때만 완전 수리를 수행합니다.
        // 비용을 먼저 계산하고 전액을 지불할 수 있을 때만 내구도를 바꾸므로 돈만 빠지거나 일부만 수리되는 중간 상태가 생기지 않습니다.
        public bool TryRepairWeapon(InventoryEntry weapon)
        {
            WeaponDefinition definition = weapon?.Item.WeaponDefinition;
            if (weapon == null || definition == null || !weapon.HasWeaponDurabilityState || !ContainsManagedEntry(weapon))
            {
                OperationStatus = "수리할 무기를 거점 보관 공간에서 찾을 수 없습니다.";
                StateChanged?.Invoke();
                return false;
            }

            if (weapon.WeaponDurability >= definition.MaxDurability)
            {
                OperationStatus = "이미 최대 내구도인 무기입니다.";
                StateChanged?.Invoke();
                return false;
            }

            int cost = definition.CalculateRepairCost(weapon.WeaponDurability);
            if (cost > 0 && !HideoutEconomyStateStore.TrySpend(cost))
            {
                OperationStatus = $"수리 비용 {cost} 크레딧이 필요합니다. 현재 보유: {Credits}";
                StateChanged?.Invoke();
                return false;
            }

            float restored = weapon.RepairWeaponDurability();
            OperationStatus = $"{weapon.Item.DisplayName} 내구도를 {restored:0.#} 회복했습니다. 수리 비용: {cost} 크레딧";
            PersistRuntimeState();
            return true;
        }

        public bool TryCraft(HideoutFacility facility, HideoutRecipeDefinition recipe)
        {
            HideoutFacilityDefinition definition = facility?.Definition;
            HideoutFacilityDefinition generator = FindFacility(HideoutFacilityType.Generator);
            HideoutCraftJobState existingJob = recipe != null ? HideoutCraftQueueStateStore.FindJob(recipe.RecipeId) : null;
            bool crafted;
            string resultMessage;
            if (existingJob?.completed == true)
                crafted = HideoutCraftQueueStateStore.TryClaim(recipe.RecipeId, StashGrid, itemCatalog, out resultMessage);
            else if (existingJob == null)
                crafted = HideoutCraftQueueStateStore.TryEnqueue(
                    StashGrid,
                    definition,
                    generator,
                    recipe,
                    out resultMessage);
            else
            {
                crafted = false;
                resultMessage = "이미 제작 중인 제작법입니다.";
            }
            OperationStatus = resultMessage;
            if (crafted)
            {
                // 게임 메모리에서 제작은 이미 성공했으므로 저장 실패 여부는 PersistenceStatus로 별도 표시합니다.
                PersistRuntimeState();
                return true;
            }
            StateChanged?.Invoke();
            return false;
        }

        // 현재 시설을 다음 레벨로 올릴 재료가 모두 있는지 실제 창고 전체에서 확인합니다.
        // 루트 창고뿐 아니라 창고 안 컨테이너의 내용물도 같은 ItemId라면 재료 수량에 포함됩니다.
        public bool CanUpgradeFacility(HideoutFacilityDefinition definition)
        {
            if (definition == null || StashGrid == null) return false;
            int currentLevel = HideoutFacilityStateStore.GetLevel(definition);
            if (currentLevel >= definition.MaximumLevel) return false;

            IReadOnlyList<HideoutFacilityItemCost> costs = definition.GetUpgradeCosts(currentLevel);
            if (costs.Count == 0) return false;
            foreach (HideoutFacilityItemCost cost in costs)
            {
                if (cost?.item == null || cost.quantity <= 0) return false;

                // 같은 재료가 비용 배열에 실수로 두 번 들어가도 각 항목만 따로 비교해 통과하지 않도록 총 요구량을 합산합니다.
                int totalRequired = 0;
                foreach (HideoutFacilityItemCost candidate in costs)
                    if (candidate?.item != null && candidate.item.ItemId == cost.item.ItemId)
                        totalRequired += Mathf.Max(0, candidate.quantity);
                if (InventoryGridContent.CountItem(StashGrid, cost.item.ItemId) < totalRequired) return false;
            }
            return true;
        }

        // 비용 확인, 재료 소비, 레벨 변경, 실제 효과 반영과 저장을 한 명령으로 처리합니다.
        // 재료가 부족하거나 최대 레벨이면 변경 전에 종료하므로 일부 재료만 사라지지 않습니다.
        public bool TryUpgradeFacility(HideoutFacility facility)
            => TryUpgradeFacility(facility?.Definition);

        // 강화소 화면은 실제 대상 시설 앞에 서 있지 않아도 목록에서 선택한 정의를 강화할 수 있습니다.
        // 비용·레벨·창고 확장 규칙은 기존 한 경로를 그대로 사용해 어느 화면에서 호출해도 결과가 같습니다.
        public bool TryUpgradeFacility(HideoutFacilityDefinition definition)
        {
            if (definition == null)
            {
                OperationStatus = "강화할 시설 정보가 없습니다.";
                StateChanged?.Invoke();
                return false;
            }

            int currentLevel = HideoutFacilityStateStore.GetLevel(definition);
            if (currentLevel >= definition.MaximumLevel)
            {
                OperationStatus = $"{definition.DisplayName}은 이미 최대 레벨입니다.";
                StateChanged?.Invoke();
                return false;
            }
            if (!CanUpgradeFacility(definition))
            {
                OperationStatus = $"강화 재료가 부족합니다: {definition.BuildUpgradeCostDescription(currentLevel)}";
                StateChanged?.Invoke();
                return false;
            }

            IReadOnlyList<HideoutFacilityItemCost> costs = definition.GetUpgradeCosts(currentLevel);
            foreach (HideoutFacilityItemCost cost in costs)
                InventoryGridContent.TryConsumeItem(StashGrid, cost.item.ItemId, cost.quantity);

            if (!HideoutFacilityStateStore.TryUpgrade(definition))
            {
                OperationStatus = "시설 레벨 변경에 실패했습니다.";
                StateChanged?.Invoke();
                return false;
            }

            // 창고 강화는 저장 가능한 행을 늘립니다. 기존 InventoryEntry 참조와 좌표는 그대로 옮겨 아이템 상태를 보존합니다.
            if (definition.FacilityType == HideoutFacilityType.Storage) ResizeStashForCurrentLevel();
            // 현재 씬에 놓인 같은 정의의 외형만 새 레벨로 갱신합니다. 강화소에서 다른 시설을 골라도 대상 시설 표시가 즉시 바뀝니다.
            foreach (HideoutFacility placedFacility in FindObjectsOfType<HideoutFacility>())
                if (placedFacility != null && placedFacility.Definition == definition)
                    placedFacility.RefreshAppearance();
            OperationStatus = $"{definition.DisplayName}을 Lv.{HideoutFacilityStateStore.GetLevel(definition)}로 강화했습니다.";
            PersistRuntimeState();
            return true;
        }

        // 현재 목표를 달성한 임무의 보상을 창고에 넣고 완료 기록을 확정합니다.
        // 전체 보상 공간을 먼저 검사하므로 창고가 가득 찬 경우 아무것도 바꾸지 않고 false를 반환합니다.
        public bool TryCompleteMission(HideoutMissionDefinition mission)
        {
            if (!HideoutMissionStateStore.CanComplete(mission))
            {
                OperationStatus = "아직 임무 목표를 달성하지 못했습니다.";
                StateChanged?.Invoke();
                return false;
            }

            if (!HideoutMissionRewardService.TryGrant(StashGrid, mission))
            {
                OperationStatus = "임무 보상 전체가 들어갈 창고 공간이 없습니다. 공간을 만든 뒤 다시 완료해 주세요.";
                StateChanged?.Invoke();
                return false;
            }

            // 보상 지급 뒤 완료 처리가 실패하면 데이터 규칙이 어긋난 상태이므로 명확한 오류를 남깁니다.
            // 정상 흐름에서는 바로 앞 CanComplete 검사를 통과했기 때문에 이 분기로 들어오지 않습니다.
            if (!HideoutMissionStateStore.TryComplete(mission))
            {
                OperationStatus = "임무 완료 상태를 기록하지 못했습니다.";
                StateChanged?.Invoke();
                return false;
            }

            // 아이템 보상과 완료 기록이 모두 확정된 뒤 화폐를 더해, 창고 공간 부족으로 실패한 임무가 크레딧만 지급하는 일을 막습니다.
            if (mission.CreditReward > 0) HideoutEconomyStateStore.AddCredits(mission.CreditReward);
            HideoutEconomyStateStore.AddTraderReputation(mission.RiskRating * 10);

            OperationStatus = $"임무 완료 보상을 창고에 받았습니다: {mission.RewardDescription}";
            PersistRuntimeState();
            return true;
        }

        public bool TryMove(
            PlayerInventory.GridData source,
            PlayerInventory.GridData target,
            InventoryEntry entry,
            Vector2Int position)
        {
            return CommitOperation(
                InventoryGridOperations.TryMove(source, target, entry, position),
                "아이템을 이동했습니다.",
                "그 위치에는 아이템을 놓거나 교환할 수 없습니다.");
        }

        public bool TryTransferToOtherGrid(PlayerInventory.GridData source, InventoryEntry entry)
        {
            PlayerInventory.GridData target = source == StashGrid
                ? LoadoutGrid
                : source == LoadoutGrid ? StashGrid : null;
            return CommitOperation(
                InventoryGridOperations.TryTransferToFirstSpace(source, target, entry),
                target == LoadoutGrid ? "출격 준비로 옮겼습니다." : "창고로 되돌렸습니다.",
                "반대쪽 격자에 들어갈 공간이 없습니다.");
        }

        // 컨테이너 내부를 연 상태에서는 단순 창고↔출격 규칙 대신 화면이 지정한 반대편 격자로 빠르게 옮깁니다.
        // 기존 TryTransferToFirstSpace를 그대로 사용하므로 현재 회전 우선, 반대 회전 재시도와 실패 롤백 규칙이 유지됩니다.
        public bool TryTransfer(
            PlayerInventory.GridData source,
            PlayerInventory.GridData target,
            InventoryEntry entry)
        {
            return CommitOperation(
                InventoryGridOperations.TryTransferToFirstSpace(source, target, entry),
                "아이템을 반대편 격자로 옮겼습니다.",
                target == null
                    ? "빠른 이동 대상이 없습니다."
                    : "빠른 이동 대상에 들어갈 공간이 없습니다.");
        }

        // Shift+클릭은 레이드와 거점 모두 수량 절반 분할로 통일합니다.
        // 분할할 수량과 빈자리를 먼저 확인하는 공용 연산을 사용해 공간 부족 시 원본 묶음을 그대로 보존합니다.
        public bool TrySplit(PlayerInventory.GridData grid, InventoryEntry entry)
        {
            return CommitOperation(
                InventoryGridOperations.TrySplit(grid, entry),
                "아이템 묶음을 절반으로 나눴습니다.",
                entry == null || entry.Quantity < 2
                    ? "한 개짜리 아이템은 나눌 수 없습니다."
                    : "분할한 묶음을 놓을 공간이 없습니다.");
        }

        public bool TryRotate(PlayerInventory.GridData grid, InventoryEntry entry)
        {
            return CommitOperation(
                InventoryGridOperations.TryRotate(grid, entry),
                "아이템을 회전했습니다.",
                "회전한 모양이 주변 아이템이나 격자 경계와 겹칩니다.");
        }

        public bool TryAutoSort(PlayerInventory.GridData grid)
        {
            return CommitOperation(
                InventoryGridOperations.TryAutoSort(grid),
                "격자를 자동 정리했습니다.",
                grid != null && grid.Entries.Count == 0
                    ? "정리할 아이템이 없습니다."
                    : "모든 아이템을 안전하게 배치할 수 없어 정리를 취소했습니다.");
        }

        // 이전 목록형 UI나 테스트가 인덱스 이동을 호출해도 새 격자 이동 규칙으로 처리합니다.
        public bool MoveToLoadout(int stashIndex)
            => stashIndex >= 0 && StashGrid != null && stashIndex < StashGrid.Entries.Count &&
               TryTransferToOtherGrid(StashGrid, StashGrid.Entries[stashIndex]);

        public bool MoveToStash(int loadoutIndex)
            => loadoutIndex >= 0 && LoadoutGrid != null && loadoutIndex < LoadoutGrid.Entries.Count &&
               TryTransferToOtherGrid(LoadoutGrid, LoadoutGrid.Entries[loadoutIndex]);

        // 기존 호출부와 테스트는 Inspector에 저장된 기본 씬으로 출격할 수 있게 유지합니다.
        // 새 출격 터미널은 아래 오버로드에 선택한 지역의 씬 이름을 전달해 여러 레이드 지역을 같은 저장 흐름으로 엽니다.
        public bool StartRaid() => StartRaid(raidSceneName);

        // 씬 이동 직전에 현재 격자 좌표와 상태를 스냅샷으로 확정해 RaidLoadoutApplier가 같은 배치를 읽게 합니다.
        // targetSceneName은 출격 화면에서 선택한 지역이며, 창고 UI는 이 메서드를 호출하지 않아 정리와 출발 역할이 섞이지 않습니다.
        public bool StartRaid(string targetSceneName)
        {
            if (string.IsNullOrWhiteSpace(targetSceneName))
            {
                OperationStatus = "출격할 지역의 씬 이름이 비어 있습니다.";
                StateChanged?.Invoke();
                return false;
            }
            // 현재 창고와 출격 준비를 디스크에 기록하지 못한 채 레이드로 이동하면 비정상 종료 후 이전 아이템이 다시 나타날 수 있습니다.
            // 따라서 출격 직전 저장을 필수 단계로 보고 실패하면 현재 거점에 남아 사용자가 상태를 확인하게 합니다.
            if (!PersistRuntimeState())
            {
                OperationStatus = "저장에 실패하여 출격을 취소했습니다. 저장 경로와 디스크 상태를 확인해 주세요.";
                StateChanged?.Invoke();
                return false;
            }
            // 무거운 레이드 씬은 Loading 화면에서 지역 이미지를 보여 주며 비동기로 준비합니다.
            if (GameSceneNavigator.TryLoadSceneThroughLoading(targetSceneName)) return true;

            OperationStatus = $"'{targetSceneName}' 지역을 열 수 없습니다. Build Settings를 확인해 주세요.";
            PersistenceStatus = OperationStatus;
            StateChanged?.Invoke();
            return false;
        }

        private void RestoreRuntimeGrids()
        {
            // 창고 시설의 효과 값은 기본 높이에 더해지는 행 수입니다. 레벨이 높아져도 가로 크기는 유지해 UI 폭이 급격히 변하지 않게 합니다.
            HideoutFacilityDefinition storageDefinition = FindFacility(HideoutFacilityType.Storage);
            int storageBonusRows = storageDefinition != null
                ? Mathf.Max(0, storageDefinition.GetEffectValue(HideoutFacilityStateStore.GetLevel(storageDefinition)))
                : 0;
            StashGrid = new PlayerInventory.GridData(stashWidth, stashHeight + storageBonusRows);
            if (playerInventory == null) playerInventory = FindObjectOfType<PlayerInventory>();
            if (playerInventory != null)
            {
                // 프리팹 기본 인벤토리와 장비를 비운 뒤 저장된 출격 상태를 같은 실제 객체에 복원합니다.
                // 이 시점에는 InventoryChanged를 아직 구독하지 않아 복원 도중 중간 상태가 자동 저장되지 않습니다.
                playerInventory.DiscardAllRaidItems();
                LoadoutGrid = playerInventory.Root;
            }
            else
            {
                // 플레이어가 없는 EditMode 테스트와 이전 단순 Hideout 씬도 기존 독립 격자로 계속 동작합니다.
                LoadoutGrid = new PlayerInventory.GridData(loadoutWidth, loadoutHeight);
            }
            unresolvedStashItems.Clear();
            unresolvedLoadoutItems.Clear();

            RestoreItems(HideoutStateStore.StashItems, StashGrid, unresolvedStashItems);
            RestoreItems(HideoutStateStore.LoadoutItems, LoadoutGrid, unresolvedLoadoutItems);
            RestoreLoadoutEquipment();
            int unresolvedCount = unresolvedStashItems.Count + unresolvedLoadoutItems.Count;
            if (unresolvedCount > 0)
                OperationStatus = $"정의 누락 또는 공간 부족으로 표시하지 못한 아이템 {unresolvedCount}묶음을 저장 데이터에 보존했습니다.";
        }

        // 저장된 활성 임무 ID를 현재 씬에 연결된 ScriptableObject로 되돌립니다.
        // 데이터 에셋이 삭제됐거나 ID가 바뀐 경우 null을 반환해 잘못된 목표 종류로 진행도를 더하지 않습니다.
        private HideoutMissionDefinition FindMission(string missionId)
        {
            if (string.IsNullOrWhiteSpace(missionId) || missionDefinitions == null) return null;
            foreach (HideoutMissionDefinition mission in missionDefinitions)
                if (mission != null && mission.MissionId == missionId) return mission;
            return null;
        }

        private HideoutFacilityDefinition FindFacility(HideoutFacilityType type)
        {
            if (facilityDefinitions == null) return null;
            foreach (HideoutFacilityDefinition definition in facilityDefinitions)
                if (definition != null && definition.FacilityType == type) return definition;
            return null;
        }

        private void ResizeStashForCurrentLevel()
        {
            HideoutFacilityDefinition storageDefinition = FindFacility(HideoutFacilityType.Storage);
            if (StashGrid == null || storageDefinition == null) return;

            int bonusRows = Mathf.Max(0, storageDefinition.GetEffectValue(HideoutFacilityStateStore.GetLevel(storageDefinition)));
            int targetHeight = stashHeight + bonusRows;
            if (targetHeight == StashGrid.Height) return;

            var expanded = new PlayerInventory.GridData(stashWidth, targetHeight);
            foreach (InventoryEntry entry in StashGrid.Entries) expanded.Entries.Add(entry);
            StashGrid = expanded;
        }

        private void RestoreItems(
            IReadOnlyList<RaidItemSnapshot> snapshots,
            PlayerInventory.GridData target,
            List<RaidItemSnapshot> unresolved)
        {
            if (snapshots == null) return;
            foreach (RaidItemSnapshot snapshot in snapshots)
                if (!RaidSnapshotUtility.TryRestoreItem(target, itemCatalog, snapshot, out _))
                    unresolved.Add(RaidSnapshotUtility.CloneItem(snapshot));
        }

        private bool CommitOperation(bool succeeded, string successMessage, string failureMessage)
        {
            OperationStatus = succeeded ? successMessage : failureMessage;
            if (succeeded) PersistRuntimeState();
            else StateChanged?.Invoke();
            return succeeded;
        }

        private static bool TryFindItemSpace(
            PlayerInventory.GridData grid,
            InventoryEntry ignored,
            ItemDefinition item,
            out Vector2Int position,
            out bool rotated)
        {
            position = Vector2Int.zero;
            rotated = false;
            if (grid == null || item == null) return false;
            bool[,] occupied = InventoryGridUtility.BuildOccupancy(grid, ignored);
            if (InventoryGridUtility.FindSpace(grid, item, false, occupied, out position)) return true;
            rotated = true;
            return InventoryGridUtility.FindSpace(grid, item, true, occupied, out position);
        }

        private bool ContainsManagedEntry(InventoryEntry requested)
        {
            if (ContainsEntryRecursive(StashGrid, requested) || ContainsEntryRecursive(LoadoutGrid, requested)) return true;
            if (playerInventory == null) return false;
            foreach (InventoryEntry equipped in playerInventory.Equipment.Values)
                if (equipped == requested || ContainsEntryRecursive(equipped.Contents, requested)) return true;
            return false;
        }

        // 가방이나 조끼 안쪽 무기도 수리할 수 있도록 루트 격자뿐 아니라 모든 Contents를 깊이 순회합니다.
        private static bool ContainsEntryRecursive(PlayerInventory.GridData grid, InventoryEntry requested)
        {
            if (grid == null || requested == null) return false;
            foreach (InventoryEntry entry in grid.Entries)
                if (entry == requested || ContainsEntryRecursive(entry.Contents, requested)) return true;
            return false;
        }

        private bool PersistRuntimeState()
        {
            var stashSnapshots = new List<RaidItemSnapshot>();
            var loadoutSnapshots = new List<RaidItemSnapshot>();
            var equipmentSnapshots = new List<RaidEquipmentSnapshot>();
            AddGridSnapshots(stashSnapshots, StashGrid);
            AddGridSnapshots(loadoutSnapshots, LoadoutGrid);
            if (playerInventory != null)
            {
                foreach (KeyValuePair<EquipmentSlotType, InventoryEntry> equipped in playerInventory.Equipment)
                {
                    RaidItemSnapshot item = RaidSnapshotUtility.CaptureItem(equipped.Value);
                    if (item != null)
                        equipmentSnapshots.Add(new RaidEquipmentSnapshot
                        {
                            slot = equipped.Key.ToString(),
                            item = item
                        });
                }
            }
            AddClones(stashSnapshots, unresolvedStashItems);
            AddClones(loadoutSnapshots, unresolvedLoadoutItems);
            HideoutStateStore.ReplaceState(stashSnapshots, loadoutSnapshots, equipmentSnapshots);

            bool saved = HideoutSaveService.SaveDefault();
            PersistenceStatus = saved
                ? "창고 자동 저장 완료"
                : "창고 자동 저장에 실패했습니다.";
            StateChanged?.Invoke();
            return saved;
        }

        // 저장된 장비를 작은 임시 격자에 복원한 뒤 PlayerInventory의 검증된 장착 API로 슬롯에 넣습니다.
        // 정의가 사라졌거나 저장 슬롯과 아이템 슬롯이 다르면 장비를 삭제하지 않고 unresolved 목록에 보존합니다.
        private void RestoreLoadoutEquipment()
        {
            if (playerInventory == null) return;
            foreach (RaidEquipmentSnapshot saved in HideoutStateStore.LoadoutEquipment)
            {
                if (saved?.item == null ||
                    !Enum.TryParse(saved.slot, out EquipmentSlotType slot) ||
                    slot == EquipmentSlotType.None)
                {
                    if (saved?.item != null) unresolvedLoadoutItems.Add(RaidSnapshotUtility.CloneItem(saved.item));
                    continue;
                }

                var temporary = new PlayerInventory.GridData(7, 7);
                if (!RaidSnapshotUtility.TryRestoreItem(temporary, itemCatalog, saved.item, out InventoryEntry entry) ||
                    entry.Item.EquipmentSlot != slot ||
                    !playerInventory.TryEquipOrSwap(temporary, entry, slot, out _))
                {
                    unresolvedLoadoutItems.Add(RaidSnapshotUtility.CloneItem(saved.item));
                }
            }
        }

        // Tab 인벤토리는 PlayerInventory API를 직접 사용하므로 그 변경도 창고 조작과 같은 자동 저장 흐름에 연결합니다.
        private void HandlePlayerInventoryChanged()
        {
            if (LoadoutGrid == null || playerInventory == null || LoadoutGrid != playerInventory.Root) return;
            OperationStatus = "플레이어 인벤토리와 장비 변경을 저장했습니다.";
            PersistRuntimeState();
        }

        private static void AddGridSnapshots(List<RaidItemSnapshot> target, PlayerInventory.GridData grid)
        {
            RaidGridSnapshot snapshot = RaidSnapshotUtility.CaptureGrid(grid);
            foreach (RaidItemSnapshot item in snapshot.items) target.Add(item);
        }

        private static void AddClones(List<RaidItemSnapshot> target, List<RaidItemSnapshot> source)
        {
            foreach (RaidItemSnapshot item in source)
            {
                RaidItemSnapshot clone = RaidSnapshotUtility.CloneItem(item);
                if (clone != null) target.Add(clone);
            }
        }
    }
}
