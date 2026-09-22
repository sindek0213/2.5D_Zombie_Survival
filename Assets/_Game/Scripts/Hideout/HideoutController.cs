using System;
using System.Collections.Generic;
using UnityEngine;
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
        [SerializeField] private string raidSceneName = GameSceneNavigator.RaidSceneName;
        [SerializeField, Min(1)] private int stashWidth = 12;
        [SerializeField, Min(1)] private int stashHeight = 10;
        [SerializeField, Min(1)] private int loadoutWidth = 10;
        [SerializeField, Min(1)] private int loadoutHeight = 6;

        private readonly List<RaidItemSnapshot> unresolvedStashItems = new List<RaidItemSnapshot>();
        private readonly List<RaidItemSnapshot> unresolvedLoadoutItems = new List<RaidItemSnapshot>();

        public PlayerInventory.GridData StashGrid { get; private set; }
        public PlayerInventory.GridData LoadoutGrid { get; private set; }
        public RaidResult ProcessedResult { get; private set; }
        public string PersistenceStatus { get; private set; }
        public string OperationStatus { get; private set; }
        public ItemCatalog ItemCatalog => itemCatalog;
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

        private void Awake()
        {
            bool loaded = HideoutSaveService.LoadDefaultIfNeeded();
            if (!loaded) PersistenceStatus = "저장 파일을 읽지 못해 현재 메모리 상태를 사용합니다.";

            // 저장된 목록에 새 레이드 보상을 먼저 합친 뒤 넓은 창고 격자와 출격 격자로 복원합니다.
            ProcessedResult = HideoutStateStore.ApplyPendingRaidResult();
            // 임무는 거점에서 받은 뒤 여러 레이드를 거칠 수 있으므로 직전 결과의 실제 처치 수를 창고 복원과 같은 시점에 한 번만 누적합니다.
            HideoutMissionStateStore.ApplyRaidResult(ProcessedResult, FindMission(HideoutMissionStateStore.ActiveMissionId));
            RestoreRuntimeGrids();
            if (ProcessedResult != null) PersistRuntimeState();
        }

        public ItemDefinition FindDefinition(string itemId) => itemCatalog?.Find(itemId);

        public int CountStashItem(string itemId)
            => InventoryGridContent.CountItem(StashGrid, itemId);

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

        public bool TryCraft(HideoutFacility facility, HideoutRecipeDefinition recipe)
        {
            HideoutFacilityDefinition definition = facility?.Definition;
            HideoutFacilityDefinition generator = FindFacility(HideoutFacilityType.Generator);
            bool crafted = HideoutCraftingService.TryCraft(
                StashGrid,
                definition,
                generator,
                recipe,
                out string resultMessage);
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
        {
            HideoutFacilityDefinition definition = facility?.Definition;
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
            facility.RefreshAppearance();
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

        // 씬 이동 직전에 현재 격자 좌표와 상태를 스냅샷으로 확정해 RaidLoadoutApplier가 같은 배치를 읽게 합니다.
        public bool StartRaid()
        {
            if (string.IsNullOrWhiteSpace(raidSceneName)) return false;
            // 현재 창고와 출격 준비를 디스크에 기록하지 못한 채 레이드로 이동하면 비정상 종료 후 이전 아이템이 다시 나타날 수 있습니다.
            // 따라서 출격 직전 저장을 필수 단계로 보고 실패하면 현재 거점에 남아 사용자가 상태를 확인하게 합니다.
            if (!PersistRuntimeState())
            {
                OperationStatus = "저장에 실패하여 출격을 취소했습니다. 저장 경로와 디스크 상태를 확인해 주세요.";
                StateChanged?.Invoke();
                return false;
            }
            if (GameSceneNavigator.TryLoadScene(raidSceneName)) return true;

            PersistenceStatus = "RaidPrototype 씬을 열 수 없습니다. Build Settings를 확인해 주세요.";
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
            LoadoutGrid = new PlayerInventory.GridData(loadoutWidth, loadoutHeight);
            unresolvedStashItems.Clear();
            unresolvedLoadoutItems.Clear();

            RestoreItems(HideoutStateStore.StashItems, StashGrid, unresolvedStashItems);
            RestoreItems(HideoutStateStore.LoadoutItems, LoadoutGrid, unresolvedLoadoutItems);
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

        private bool PersistRuntimeState()
        {
            var stashSnapshots = new List<RaidItemSnapshot>();
            var loadoutSnapshots = new List<RaidItemSnapshot>();
            AddGridSnapshots(stashSnapshots, StashGrid);
            AddGridSnapshots(loadoutSnapshots, LoadoutGrid);
            AddClones(stashSnapshots, unresolvedStashItems);
            AddClones(loadoutSnapshots, unresolvedLoadoutItems);
            HideoutStateStore.ReplaceState(stashSnapshots, loadoutSnapshots);

            bool saved = HideoutSaveService.SaveDefault();
            PersistenceStatus = saved
                ? "창고 자동 저장 완료"
                : "창고 자동 저장에 실패했습니다.";
            StateChanged?.Invoke();
            return saved;
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
