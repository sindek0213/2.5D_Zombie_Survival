using System.Text;
using UnityEngine;
using UnityEngine.UI;
using ZombieSurvival.Hideout;

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
            selectedRecipeIndex = 0;
            gameObject.SetActive(true);
            Refresh();
        }

        public void HideFacility()
        {
            facility = null;
            gameObject.SetActive(false);
        }

        private void Awake() => ConfigureButtons();

        private void OnEnable()
        {
            Subscribe();
            Refresh();
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
            bool upgraded = hideoutController != null && hideoutController.TryUpgradeFacility(facility);
            if (statusText != null)
                statusText.text = upgraded
                    ? hideoutController.OperationStatus
                    : hideoutController != null ? hideoutController.OperationStatus : "거점 컨트롤러가 연결되지 않았습니다.";
            Refresh(false);
        }

        private void SelectPreviousRecipe()
        {
            int count = GetRecipeCount();
            if (count <= 0) return;
            selectedRecipeIndex = (selectedRecipeIndex - 1 + count) % count;
            Refresh(false);
        }

        private void SelectNextRecipe()
        {
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
            HideoutFacilityDefinition definition = facility?.Definition;
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
            if (titleText != null) titleText.text = $"{definition.DisplayName}  Lv.{level}/{definition.MaximumLevel}";
            if (descriptionText != null) descriptionText.text = definition.Description;
            if (effectText != null) effectText.text = definition.BuildEffectDescription(level);
            if (costText != null) costText.text = BuildCostText(definition, level);
            RefreshRecipeSection(definition, level);
            if (clearStatus && statusText != null)
                statusText.text = maximum ? "최대 레벨에 도달했습니다." : "창고의 재료를 사용해 강화할 수 있습니다.";
            if (upgradeButton != null) upgradeButton.interactable = !maximum && hideoutController != null;
            if (upgradeButtonText != null) upgradeButtonText.text = maximum ? "강화 완료" : $"Lv.{level + 1} 강화";
        }

        private void RefreshRecipeSection(HideoutFacilityDefinition definition, int facilityLevel)
        {
            int count = GetRecipeCount();
            selectedRecipeIndex = count > 0 ? Mathf.Clamp(selectedRecipeIndex, 0, count - 1) : 0;
            HideoutRecipeDefinition recipe = GetSelectedRecipe();
            bool supportsCrafting = definition.FacilityType == HideoutFacilityType.CraftingBench ||
                                    definition.FacilityType == HideoutFacilityType.Infirmary;

            if (recipeText != null)
            {
                recipeText.gameObject.SetActive(supportsCrafting);
                recipeText.text = recipe != null
                    ? BuildRecipeText(recipe, facilityLevel)
                    : supportsCrafting ? "현재 레벨에서 사용할 수 있는 제작법이 없습니다." : string.Empty;
            }
            SetRecipeButtons(supportsCrafting, count);
            if (craftButtonText != null)
                craftButtonText.text = recipe != null
                    ? $"{recipe.OutputItem.DisplayName} 제작"
                    : "제작 불가";
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

        private void SetRecipeButtons(bool visible, int count)
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
                craftButton.interactable = visible && count > 0;
            }
        }

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
