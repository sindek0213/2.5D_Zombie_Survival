using System.Text;
using UnityEngine;
using UnityEngine.UI;
using ZombieSurvival.Hideout;
using ZombieSurvival.Inventory;

namespace ZombieSurvival.UI
{
    // 현재 열어 둔 시설의 레벨, 실제 효과와 다음 강화 비용을 보여 주는 공용 Canvas 화면입니다.
    // 창고·임무 보드 위에는 작은 보조 패널로 함께 뜨고, 전용 화면이 없는 시설에서는 이 패널이 주 화면 역할을 합니다.
    public sealed class HideoutFacilityUpgradeHud : MonoBehaviour
    {
        [SerializeField] private HideoutController hideoutController;
        [SerializeField] private HideoutWorldController worldController;
        [SerializeField] private Text titleText;
        [SerializeField] private Text descriptionText;
        [SerializeField] private Text effectText;
        [SerializeField] private Text costText;
        [SerializeField] private Text recipeText;
        [SerializeField] private Text statusText;
        [SerializeField] private Button previousRecipeButton;
        [SerializeField] private Button nextRecipeButton;
        [SerializeField] private Button craftButton;
        [SerializeField] private Text craftButtonText;
        [SerializeField] private Button upgradeButton;
        [SerializeField] private Text upgradeButtonText;
        [SerializeField] private Button closeButton;

        private HideoutFacility facility;
        private int selectedRecipeIndex;
        private bool upgradeStationMode;
        private int selectedFacilityIndex;
        // 창고 화면과 강화 화면은 서로 다른 Canvas이므로 한쪽의 드래그가 끝나는 프레임에
        // 이벤트 순서가 달라도 수량 표시가 오래 남지 않게 짧은 간격으로 실제 창고를 다시 읽습니다.
        private float nextInventoryRefreshTime;
        private int lastObservedStateSignature = int.MinValue;
        private const float InventoryRefreshInterval = 0.2f;

        public void Configure(
            HideoutController controller,
            HideoutWorldController world,
            Text title,
            Text description,
            Text effect,
            Text cost,
            Text recipe,
            Text status,
            Button previousRecipe,
            Button nextRecipe,
            Button craft,
            Text craftLabel,
            Button upgrade,
            Text upgradeLabel,
            Button close)
        {
            hideoutController = controller;
            worldController = world;
            titleText = title;
            descriptionText = description;
            effectText = effect;
            costText = cost;
            recipeText = recipe;
            statusText = status;
            previousRecipeButton = previousRecipe;
            nextRecipeButton = nextRecipe;
            craftButton = craft;
            craftButtonText = craftLabel;
            upgradeButton = upgrade;
            upgradeButtonText = upgradeLabel;
            closeButton = close;
            ConfigureButtons();
            Refresh();
        }

        // 프리팹의 Text와 Button 참조는 유지하고 씬마다 달라지는 컨트롤러만 다시 연결합니다.
        public void SetContext(HideoutController controller, HideoutWorldController world)
        {
            Unsubscribe();
            hideoutController = controller;
            worldController = world;
            Subscribe();
            ConfigureButtons();
        }

        public void ShowFacility(HideoutFacility selectedFacility)
        {
            facility = selectedFacility;
            upgradeStationMode = false;
            selectedRecipeIndex = 0;
            gameObject.SetActive(true);
            Refresh();
        }

        // 강화소에서는 현재 앞에 선 시설 자체가 아니라 거점의 모든 강화 가능한 시설을 순서대로 선택합니다.
        // 기존 이전·다음 버튼을 대상 시설 선택에 재사용해 별도 전체 화면 목록을 추가하지 않고도 한곳에서 통합 관리합니다.
        public void ShowUpgradeStation()
        {
            facility = null;
            upgradeStationMode = true;
            selectedFacilityIndex = Mathf.Clamp(selectedFacilityIndex, 0, Mathf.Max(0, GetUpgradeableFacilityCount() - 1));
            selectedRecipeIndex = 0;
            gameObject.SetActive(true);
            Refresh();
        }

        public void HideFacility()
        {
            facility = null;
            upgradeStationMode = false;
            gameObject.SetActive(false);
        }

        private void Awake()
        {
            // 예전 씬이나 수동 배치 프리팹에 참조가 비어 있어도 같은 씬의 거점 컨트롤러를 찾아
            // 재료 수량을 항상 실제 런타임 창고에서 읽게 합니다.
            if (hideoutController == null) hideoutController = FindObjectOfType<HideoutController>();
            if (worldController == null) worldController = FindObjectOfType<HideoutWorldController>();
            ConfigureButtons();
        }

        private void OnEnable()
        {
            Subscribe();
            nextInventoryRefreshTime = Time.unscaledTime;
            Refresh();
        }

        private void Update()
        {
            if ((!upgradeStationMode && facility == null) || Time.unscaledTime < nextInventoryRefreshTime) return;

            // StateChanged 이벤트가 정상 경로의 즉시 갱신을 담당하고, 이 재확인은 UI 수명 주기나
            // 같은 프레임의 Canvas 활성화 순서 때문에 신호를 놓친 경우를 복구하는 안전망입니다.
            nextInventoryRefreshTime = Time.unscaledTime + InventoryRefreshInterval;
            int currentSignature = BuildObservedStateSignature();
            if (currentSignature != lastObservedStateSignature) Refresh(false);
        }

        private void OnDisable() => Unsubscribe();

        private void ConfigureButtons()
        {
            if (upgradeButton != null)
            {
                upgradeButton.onClick.RemoveListener(Upgrade);
                upgradeButton.onClick.AddListener(Upgrade);
            }
            if (previousRecipeButton != null)
            {
                previousRecipeButton.onClick.RemoveListener(SelectPreviousRecipe);
                previousRecipeButton.onClick.AddListener(SelectPreviousRecipe);
            }
            if (nextRecipeButton != null)
            {
                nextRecipeButton.onClick.RemoveListener(SelectNextRecipe);
                nextRecipeButton.onClick.AddListener(SelectNextRecipe);
            }
            if (craftButton != null)
            {
                craftButton.onClick.RemoveListener(CraftSelectedRecipe);
                craftButton.onClick.AddListener(CraftSelectedRecipe);
            }
            if (closeButton != null)
            {
                closeButton.onClick.RemoveListener(Close);
                closeButton.onClick.AddListener(Close);
            }
        }

        private void Upgrade()
        {
            HideoutFacilityDefinition target = upgradeStationMode
                ? GetSelectedUpgradeableFacility()
                : facility?.Definition;
            bool upgraded = hideoutController != null && hideoutController.TryUpgradeFacility(target);
            if (statusText != null)
                statusText.text = upgraded
                    ? hideoutController.OperationStatus
                    : hideoutController != null ? hideoutController.OperationStatus : "거점 컨트롤러가 연결되지 않았습니다.";
            Refresh(false);
        }

        private void SelectPreviousRecipe()
        {
            if (upgradeStationMode)
            {
                int facilityCount = GetUpgradeableFacilityCount();
                if (facilityCount <= 0) return;
                selectedFacilityIndex = (selectedFacilityIndex - 1 + facilityCount) % facilityCount;
                Refresh(false);
                return;
            }
            int count = GetRecipeCount();
            if (count <= 0) return;
            selectedRecipeIndex = (selectedRecipeIndex - 1 + count) % count;
            Refresh(false);
        }

        private void SelectNextRecipe()
        {
            if (upgradeStationMode)
            {
                int facilityCount = GetUpgradeableFacilityCount();
                if (facilityCount <= 0) return;
                selectedFacilityIndex = (selectedFacilityIndex + 1) % facilityCount;
                Refresh(false);
                return;
            }
            int count = GetRecipeCount();
            if (count <= 0) return;
            selectedRecipeIndex = (selectedRecipeIndex + 1) % count;
            Refresh(false);
        }

        private void CraftSelectedRecipe()
        {
            HideoutRecipeDefinition recipe = GetSelectedRecipe();
            if (hideoutController != null) hideoutController.TryCraft(facility, recipe);
            if (statusText != null)
                statusText.text = hideoutController != null
                    ? hideoutController.OperationStatus
                    : "거점 컨트롤러가 연결되지 않았습니다.";
            Refresh(false);
        }

        private void Close() => worldController?.CloseFacility();

        private void Refresh() => Refresh(true);

        private void Refresh(bool clearStatus)
        {
            HideoutFacilityDefinition definition = upgradeStationMode
                ? GetSelectedUpgradeableFacility()
                : facility?.Definition;
            if (definition == null)
            {
                if (titleText != null) titleText.text = "시설 정보 없음";
                if (descriptionText != null) descriptionText.text = "가까운 시설에서 E키를 눌러 주세요.";
                if (effectText != null) effectText.text = string.Empty;
                if (costText != null) costText.text = string.Empty;
                if (recipeText != null) recipeText.text = string.Empty;
                if (upgradeButton != null) upgradeButton.interactable = false;
                SetRecipeButtons(false, 0);
                return;
            }

            int level = HideoutFacilityStateStore.GetLevel(definition);
            bool maximum = level >= definition.MaximumLevel;
            bool canUpgrade = !maximum &&
                              hideoutController != null &&
                              hideoutController.CanUpgradeFacility(definition);
            if (titleText != null) titleText.text = upgradeStationMode
                ? $"강화소 · {definition.DisplayName}  Lv.{level}/{definition.MaximumLevel}"
                : $"{definition.DisplayName}  Lv.{level}/{definition.MaximumLevel}";
            if (descriptionText != null) descriptionText.text = definition.Description;
            if (effectText != null) effectText.text = definition.BuildEffectDescription(level);
            if (costText != null)
            {
                costText.gameObject.SetActive(upgradeStationMode);
                costText.text = upgradeStationMode ? BuildCostText(definition, level) : string.Empty;
            }
            RefreshRecipeSection(definition, level);
            if (clearStatus && statusText != null)
                statusText.text = upgradeStationMode
                    ? maximum
                        ? "최대 레벨에 도달했습니다. 이전·다음 버튼으로 다른 시설을 선택하세요."
                        : canUpgrade
                            ? "창고의 재료를 사용해 강화할 수 있습니다."
                            : "창고의 강화 재료가 부족합니다."
                    : "시설 강화는 강화소에서 할 수 있습니다.";
            if (upgradeButton != null)
            {
                upgradeButton.gameObject.SetActive(upgradeStationMode);
                upgradeButton.interactable = upgradeStationMode && canUpgrade;
            }
            if (upgradeButtonText != null) upgradeButtonText.text = maximum ? "강화 완료" : $"Lv.{level + 1} 강화";
            lastObservedStateSignature = BuildObservedStateSignature();
        }

        private void RefreshRecipeSection(HideoutFacilityDefinition definition, int facilityLevel)
        {
            int count = GetRecipeCount();
            selectedRecipeIndex = count > 0 ? Mathf.Clamp(selectedRecipeIndex, 0, count - 1) : 0;
            HideoutRecipeDefinition recipe = GetSelectedRecipe();
            bool supportsCrafting = !upgradeStationMode &&
                                    (definition.FacilityType == HideoutFacilityType.CraftingBench ||
                                     definition.FacilityType == HideoutFacilityType.Infirmary);

            if (recipeText != null)
            {
                recipeText.gameObject.SetActive(supportsCrafting);
                recipeText.text = recipe != null
                    ? BuildRecipeText(recipe, facilityLevel)
                    : supportsCrafting ? "현재 레벨에서 사용할 수 있는 제작법이 없습니다." : string.Empty;
            }
            bool canCraft = recipe != null &&
                            hideoutController != null &&
                            hideoutController.CanCraft(facility, recipe, out _);
            if (upgradeStationMode)
            {
                int facilityCount = GetUpgradeableFacilityCount();
                SetFacilitySelectionButtons(facilityCount);
            }
            else
            {
                SetRecipeButtons(supportsCrafting, count, canCraft);
            }
            if (craftButtonText != null)
            {
                HideoutCraftJobState job = hideoutController?.GetCraftJob(recipe);
                craftButtonText.text = recipe == null
                    ? "제작 불가"
                    : job?.completed == true
                        ? "완성품 수령"
                        : job != null ? $"제작 중 {job.remainingSeconds:0}초" : $"{recipe.OutputItem.DisplayName} 제작 시작";
            }
        }

        private string BuildRecipeText(HideoutRecipeDefinition recipe, int facilityLevel)
        {
            var builder = new StringBuilder();
            int discount = GetGeneratorDiscount();
            builder.Append("제작: ")
                .Append(recipe.DisplayName)
                .Append("  (")
                .Append(selectedRecipeIndex + 1)
                .Append('/')
                .Append(GetRecipeCount())
                .Append(")\n결과: ")
                .Append(recipe.OutputItem.DisplayName)
                .Append(" x")
                .Append(recipe.GetOutputQuantity(facilityLevel))
                .Append("\n시간: ")
                .Append(recipe.GetCraftDuration(facilityLevel).ToString("0.#"))
                .Append("초")
                .Append("\n재료: ");

            bool first = true;
            foreach (HideoutFacilityItemCost ingredient in recipe.Ingredients)
            {
                if (ingredient?.item == null) continue;
                if (!first) builder.Append(" · ");
                int required = HideoutCraftingService.GetDiscountedQuantity(ingredient.quantity, discount);
                int owned = hideoutController != null ? hideoutController.CountStashItem(ingredient.item.ItemId) : 0;
                builder.Append(ingredient.item.DisplayName).Append(' ').Append(owned).Append('/').Append(required);
                first = false;
            }
            if (discount > 0) builder.Append($"\n발전기 할인 {discount}% 적용");
            builder.Append($"\n발전기: {(HideoutCraftQueueStateStore.GeneratorEnabled ? "켜짐" : "꺼짐")} · 연료 {HideoutCraftQueueStateStore.GeneratorFuelSeconds:0}초");
            HideoutCraftJobState job = hideoutController?.GetCraftJob(recipe);
            if (job != null)
                builder.Append(job.completed
                    ? "\n상태: 제작 완료 · 수령 대기"
                    : $"\n상태: 제작 중 · {job.remainingSeconds:0.0}초 남음");
            return builder.ToString();
        }

        private int GetRecipeCount()
            => hideoutController != null && facility?.Definition != null
                ? hideoutController.GetUnlockedRecipeCount(facility.Definition)
                : 0;

        private HideoutRecipeDefinition GetSelectedRecipe()
            => hideoutController != null && facility?.Definition != null
                ? hideoutController.GetUnlockedRecipe(facility.Definition, selectedRecipeIndex)
                : null;

        private int GetGeneratorDiscount()
        {
            // 표시와 실제 제작이 같은 컨트롤러 계산을 읽으므로 발전기 강화 직후 필요한 재료 수가 즉시 같이 바뀝니다.
            return hideoutController != null ? hideoutController.GetCraftingDiscountPercent() : 0;
        }

        private void SetRecipeButtons(bool visible, int count, bool canCraft = false)
        {
            if (previousRecipeButton != null)
            {
                previousRecipeButton.gameObject.SetActive(visible);
                previousRecipeButton.interactable = count > 1;
            }
            if (nextRecipeButton != null)
            {
                nextRecipeButton.gameObject.SetActive(visible);
                nextRecipeButton.interactable = count > 1;
            }
            if (craftButton != null)
            {
                craftButton.gameObject.SetActive(visible);
                // 제작법이 공개되었어도 재료나 결과 공간이 부족하면 버튼을 비활성화합니다.
                // 표시된 보유 수량과 실제 실행 가능 상태가 한눈에 일치하게 됩니다.
                craftButton.interactable = visible && count > 0 && canCraft;
            }
        }

        private void SetFacilitySelectionButtons(int count)
        {
            if (previousRecipeButton != null)
            {
                previousRecipeButton.gameObject.SetActive(true);
                previousRecipeButton.interactable = count > 1;
            }
            if (nextRecipeButton != null)
            {
                nextRecipeButton.gameObject.SetActive(true);
                nextRecipeButton.interactable = count > 1;
            }
            if (craftButton != null) craftButton.gameObject.SetActive(false);
            if (recipeText != null)
            {
                recipeText.gameObject.SetActive(true);
                recipeText.text = count > 0
                    ? $"강화 대상 {selectedFacilityIndex + 1}/{count} · 이전/다음 버튼으로 시설 선택"
                    : "강화 가능한 시설이 없습니다.";
            }
        }

        private int GetUpgradeableFacilityCount()
        {
            if (hideoutController == null) return 0;
            int count = 0;
            foreach (HideoutFacilityDefinition definition in hideoutController.FacilityDefinitions)
                if (IsUpgradeableTarget(definition)) count++;
            return count;
        }

        private HideoutFacilityDefinition GetSelectedUpgradeableFacility()
        {
            if (hideoutController == null) return null;
            int count = GetUpgradeableFacilityCount();
            if (count <= 0) return null;
            selectedFacilityIndex = Mathf.Clamp(selectedFacilityIndex, 0, count - 1);
            int current = 0;
            foreach (HideoutFacilityDefinition definition in hideoutController.FacilityDefinitions)
            {
                if (!IsUpgradeableTarget(definition)) continue;
                if (current == selectedFacilityIndex) return definition;
                current++;
            }
            return null;
        }

        private static bool IsUpgradeableTarget(HideoutFacilityDefinition definition)
            => definition != null &&
               definition.FacilityType != HideoutFacilityType.UpgradeStation &&
               definition.FacilityType != HideoutFacilityType.Deployment &&
               definition.MaximumLevel > 1;

        private string BuildCostText(HideoutFacilityDefinition definition, int level)
        {
            if (level >= definition.MaximumLevel) return "다음 강화 비용: 없음";

            var builder = new StringBuilder("다음 강화 비용\n");
            var costs = definition.GetUpgradeCosts(level);
            if (costs.Count == 0) return builder.Append("비용 데이터가 설정되지 않았습니다.").ToString();

            foreach (HideoutFacilityItemCost cost in costs)
            {
                if (cost?.item == null) continue;
                int owned = hideoutController != null ? hideoutController.CountStashItem(cost.item.ItemId) : 0;
                builder.Append(cost.item.DisplayName)
                    .Append(' ')
                    .Append(owned)
                    .Append('/')
                    .Append(cost.quantity)
                    .Append('\n');
            }
            return builder.ToString().TrimEnd();
        }

        private int BuildObservedStateSignature()
        {
            // 0.2초마다 큰 창고를 복제해 제작 가능 여부를 다시 계산하면 불필요한 임시 객체가 생깁니다.
            // 대신 아이템 종류·수량·좌표·회전과 컨테이너 내부를 가벼운 정수값으로 합쳐 실제 상태가 바뀐 때만 전체 UI를 갱신합니다.
            unchecked
            {
                int signature = 17;
                signature = signature * 31 + selectedRecipeIndex;
                signature = signature * 31 + selectedFacilityIndex;
                HideoutFacilityDefinition observedDefinition = upgradeStationMode
                    ? GetSelectedUpgradeableFacility()
                    : facility?.Definition;
                signature = signature * 31 + (observedDefinition != null
                    ? HideoutFacilityStateStore.GetLevel(observedDefinition)
                    : 0);
                signature = signature * 31 + GetGeneratorDiscount();
                signature = signature * 31 + Mathf.CeilToInt(HideoutCraftQueueStateStore.GeneratorFuelSeconds);
                AppendGridSignature(hideoutController?.StashGrid, ref signature);
                return signature;
            }
        }

        private static void AppendGridSignature(PlayerInventory.GridData grid, ref int signature)
        {
            if (grid == null)
            {
                signature = signature * 31;
                return;
            }

            signature = signature * 31 + grid.Width;
            signature = signature * 31 + grid.Height;
            signature = signature * 31 + grid.Entries.Count;
            foreach (InventoryEntry entry in grid.Entries)
            {
                if (entry == null)
                {
                    signature = signature * 31;
                    continue;
                }

                signature = signature * 31 + (entry.Item != null ? entry.Item.GetInstanceID() : 0);
                signature = signature * 31 + entry.Quantity;
                signature = signature * 31 + entry.Position.x;
                signature = signature * 31 + entry.Position.y;
                signature = signature * 31 + (entry.Rotated ? 1 : 0);
                AppendGridSignature(entry.Contents, ref signature);
            }
        }

        private void Subscribe()
        {
            if (hideoutController == null) return;
            hideoutController.StateChanged -= Refresh;
            hideoutController.StateChanged += Refresh;
        }

        private void Unsubscribe()
        {
            if (hideoutController != null) hideoutController.StateChanged -= Refresh;
        }
    }
}
