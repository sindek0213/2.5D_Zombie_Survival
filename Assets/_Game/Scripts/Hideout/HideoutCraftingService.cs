using UnityEngine;
using ZombieSurvival.Inventory;

namespace ZombieSurvival.Hideout
{
    // 거점 제작의 재료 할인, 전량 소비와 결과 공간 검사를 담당하는 순수 서비스입니다.
    // UI는 이 클래스가 반환한 결과 문구만 보여 주고 실제 InventoryEntry 변경 규칙은 여기에 한 번만 존재합니다.
    public static class HideoutCraftingService
    {
        public static bool CanCraft(
            PlayerInventory.GridData stash,
            HideoutFacilityDefinition facility,
            HideoutFacilityDefinition generator,
            HideoutRecipeDefinition recipe,
            out string failureReason)
        {
            failureReason = string.Empty;
            if (stash == null || facility == null || recipe == null || recipe.OutputItem == null)
            {
                failureReason = "제작 데이터가 올바르게 연결되지 않았습니다.";
                return false;
            }

            int facilityLevel = HideoutFacilityStateStore.GetLevel(facility);
            if (recipe.FacilityType != facility.FacilityType || facilityLevel < recipe.RequiredFacilityLevel)
            {
                failureReason = $"{recipe.RequiredFacilityLevel}레벨 시설이 필요합니다.";
                return false;
            }

            int discountPercent = GetGeneratorDiscount(generator);
            foreach (HideoutFacilityItemCost ingredient in recipe.Ingredients)
            {
                if (ingredient?.item == null)
                {
                    failureReason = "제작 재료 데이터가 비어 있습니다.";
                    return false;
                }

                int required = GetDiscountedQuantity(ingredient.quantity, discountPercent);
                int owned = InventoryGridContent.CountItem(stash, ingredient.item.ItemId);
                if (owned < required)
                {
                    failureReason = $"{ingredient.item.DisplayName}이 부족합니다. ({owned}/{required})";
                    return false;
                }
            }

            // 재료를 꺼내면 비는 셀까지 고려해야 하므로 깊게 복제한 임시 창고에서 소비와 결과 추가를 실제 순서대로 시험합니다.
            PlayerInventory.GridData simulation = CloneGrid(stash);
            if (!ConsumeIngredients(simulation, recipe, discountPercent))
            {
                failureReason = "제작 재료를 계산하지 못했습니다.";
                return false;
            }

            int outputQuantity = recipe.GetOutputQuantity(facilityLevel);
            if (!InventoryGridContent.TryAdd(simulation, recipe.OutputItem, outputQuantity))
            {
                failureReason = "제작 결과가 들어갈 창고 공간이 없습니다.";
                return false;
            }
            return true;
        }

        public static bool TryCraft(
            PlayerInventory.GridData stash,
            HideoutFacilityDefinition facility,
            HideoutFacilityDefinition generator,
            HideoutRecipeDefinition recipe,
            out string resultMessage)
        {
            if (!CanCraft(stash, facility, generator, recipe, out resultMessage)) return false;

            int discountPercent = GetGeneratorDiscount(generator);
            int facilityLevel = HideoutFacilityStateStore.GetLevel(facility);
            // 사전 시뮬레이션과 같은 순서로 처리하므로 아래 두 호출은 정상 데이터에서 반드시 성공합니다.
            if (!ConsumeIngredients(stash, recipe, discountPercent) ||
                !InventoryGridContent.TryAdd(stash, recipe.OutputItem, recipe.GetOutputQuantity(facilityLevel)))
            {
                resultMessage = "제작 적용 중 데이터가 변경되어 작업을 완료하지 못했습니다.";
                return false;
            }

            resultMessage = $"{recipe.OutputItem.DisplayName} x{recipe.GetOutputQuantity(facilityLevel)} 제작 완료";
            if (discountPercent > 0) resultMessage += $" · 발전기 할인 {discountPercent}% 적용";
            return true;
        }

        public static int GetGeneratorDiscount(HideoutFacilityDefinition generator)
        {
            if (generator == null || generator.FacilityType != HideoutFacilityType.Generator) return 0;
            return Mathf.Clamp(
                generator.GetEffectValue(HideoutFacilityStateStore.GetLevel(generator)),
                0,
                80);
        }

        public static int GetDiscountedQuantity(int originalQuantity, int discountPercent)
        {
            // 재료 하나짜리 제작이 무료가 되지 않도록 할인 뒤에도 최소 1개를 소비합니다.
            float multiplier = 1f - Mathf.Clamp(discountPercent, 0, 80) / 100f;
            return Mathf.Max(1, Mathf.CeilToInt(Mathf.Max(1, originalQuantity) * multiplier));
        }

        private static bool ConsumeIngredients(
            PlayerInventory.GridData stash,
            HideoutRecipeDefinition recipe,
            int discountPercent)
        {
            foreach (HideoutFacilityItemCost ingredient in recipe.Ingredients)
            {
                int required = GetDiscountedQuantity(ingredient.quantity, discountPercent);
                if (!InventoryGridContent.TryConsumeItem(stash, ingredient.item.ItemId, required)) return false;
            }
            return true;
        }

        private static PlayerInventory.GridData CloneGrid(PlayerInventory.GridData source)
        {
            var clone = new PlayerInventory.GridData(source.Width, source.Height);
            foreach (InventoryEntry entry in source.Entries) clone.Entries.Add(CloneEntry(entry));
            return clone;
        }

        private static InventoryEntry CloneEntry(InventoryEntry source)
        {
            var clone = new InventoryEntry(
                source.Item,
                source.Quantity,
                source.Position,
                source.Rotated,
                source.IsRaidProvided,
                source.MagazineRounds,
                source.ArmorDurability);
            if (source.Contents != null && clone.Contents != null)
                foreach (InventoryEntry child in source.Contents.Entries) clone.Contents.Entries.Add(CloneEntry(child));
            return clone;
        }
    }
}
