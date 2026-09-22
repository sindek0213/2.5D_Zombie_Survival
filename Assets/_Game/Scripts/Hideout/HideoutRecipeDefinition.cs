using System;
using System.Collections.Generic;
using UnityEngine;
using ZombieSurvival.Items;

namespace ZombieSurvival.Hideout
{
    // 제작대나 치료소에서 사용할 제작법 한 개를 정의하는 ScriptableObject입니다.
    // 재료·결과·해금 레벨을 데이터로 분리해 새 제작법을 추가할 때 컨트롤러 코드를 수정하지 않게 합니다.
    [CreateAssetMenu(menuName = "Zombie Survival/Hideout Recipe")]
    public sealed class HideoutRecipeDefinition : ScriptableObject
    {
        [SerializeField] private string recipeId = "recipe";
        [SerializeField] private string displayName = "제작법";
        [SerializeField] private HideoutFacilityType facilityType = HideoutFacilityType.CraftingBench;
        [SerializeField, Min(1)] private int requiredFacilityLevel = 1;
        [SerializeField] private HideoutFacilityItemCost[] ingredients = Array.Empty<HideoutFacilityItemCost>();
        [SerializeField] private ItemDefinition outputItem;
        [SerializeField, Min(1)] private int baseOutputQuantity = 1;
        // 1이면 시설 레벨이 하나 오를 때마다 결과 수량이 1개씩 늘고, 0이면 레벨과 관계없이 같은 수량을 만듭니다.
        [SerializeField, Min(0)] private int bonusOutputPerLevel;

        public string RecipeId => recipeId;
        public string DisplayName => string.IsNullOrWhiteSpace(displayName) ? recipeId : displayName;
        public HideoutFacilityType FacilityType => facilityType;
        public int RequiredFacilityLevel => Mathf.Max(1, requiredFacilityLevel);
        public IReadOnlyList<HideoutFacilityItemCost> Ingredients => ingredients ?? Array.Empty<HideoutFacilityItemCost>();
        public ItemDefinition OutputItem => outputItem;

        public int GetOutputQuantity(int facilityLevel)
        {
            int extraLevels = Mathf.Max(0, facilityLevel - RequiredFacilityLevel);
            return Mathf.Max(1, baseOutputQuantity + extraLevels * bonusOutputPerLevel);
        }

        public void Configure(
            string id,
            string name,
            HideoutFacilityType type,
            int requiredLevel,
            ItemDefinition resultItem,
            int resultQuantity,
            int resultBonusPerLevel,
            params HideoutFacilityItemCost[] costs)
        {
            recipeId = string.IsNullOrWhiteSpace(id) ? "recipe" : id.Trim();
            displayName = string.IsNullOrWhiteSpace(name) ? recipeId : name.Trim();
            facilityType = type;
            requiredFacilityLevel = Mathf.Max(1, requiredLevel);
            outputItem = resultItem;
            baseOutputQuantity = Mathf.Max(1, resultQuantity);
            bonusOutputPerLevel = Mathf.Max(0, resultBonusPerLevel);
            ingredients = costs ?? Array.Empty<HideoutFacilityItemCost>();
            NormalizeIngredients();
        }

        private void OnValidate()
        {
            requiredFacilityLevel = Mathf.Max(1, requiredFacilityLevel);
            baseOutputQuantity = Mathf.Max(1, baseOutputQuantity);
            bonusOutputPerLevel = Mathf.Max(0, bonusOutputPerLevel);
            NormalizeIngredients();
        }

        private void NormalizeIngredients()
        {
            if (ingredients == null)
            {
                ingredients = Array.Empty<HideoutFacilityItemCost>();
                return;
            }

            foreach (HideoutFacilityItemCost ingredient in ingredients)
                if (ingredient != null) ingredient.quantity = Mathf.Max(1, ingredient.quantity);
        }
    }
}
