using UnityEngine;
using ZombieSurvival.Inventory;
using ZombieSurvival.Items;

namespace ZombieSurvival.Hideout
{
    // 상인 거래의 가격 계산과 실제 아이템·화폐 변경을 한곳에서 처리합니다.
    // HUD가 직접 수량이나 돈을 바꾸지 않으므로 버튼을 추가하거나 화면을 교체해도 동일한 원자적 거래 규칙을 사용합니다.
    public static class HideoutTradeService
    {
        private const float DefaultSellRatio = 0.55f;
        // 프로토타입 테스트 동안 상점 구매만 무료로 만드는 임시 스위치입니다.
        // 경제 밸런스를 다시 적용할 때 false로 바꾸면 아래의 기존 가격·할인·화폐 차감 로직이 그대로 복구됩니다.
        public static readonly bool PrototypeFreePurchases = true;

        public static bool IsTradable(ItemDefinition item)
            => item != null && item.Type != ItemType.Quest && item.BaseValue > 0;

        // 상인 시설 레벨은 0~35% 할인 값으로 사용합니다. 반올림 뒤에도 최소 1 크레딧은 지불하게 합니다.
        public static int CalculateBuyUnitPrice(ItemDefinition item, int discountPercent)
        {
            if (!IsTradable(item)) return 0;
            if (PrototypeFreePurchases) return 0;
            float multiplier = 1f - Mathf.Clamp(discountPercent, 0, 35) / 100f;
            return Mathf.Max(1, Mathf.CeilToInt(item.BaseValue * multiplier));
        }

        // 판매가는 기본 가치의 55%이며 상인 시설 성장으로 최대 20% 가격 보너스를 받을 수 있습니다.
        // 구매가보다 항상 낮게 유지되어 같은 물건을 반복 구매·판매하는 것만으로 돈을 복제할 수 없습니다.
        public static int CalculateSellUnitPrice(ItemDefinition item, int bonusPercent)
        {
            if (!IsTradable(item)) return 0;
            float multiplier = DefaultSellRatio *
                               GetRarityValueMultiplier(item.Rarity) *
                               (1f + Mathf.Clamp(bonusPercent, 0, 20) / 100f);
            return Mathf.Max(1, Mathf.FloorToInt(item.BaseValue * multiplier));
        }

        // 실제 인스턴스 판매가는 희귀도 외에 남은 무기·방탄 내구도와 탄창 속 탄약 가치까지 반영합니다.
        // 파손 직전 장비가 새 장비와 같은 가격에 팔리거나, 장전된 탄약이 거래 중 사라지는 문제를 줄입니다.
        public static int CalculateSellUnitPrice(InventoryEntry entry, int bonusPercent)
        {
            if (entry == null) return 0;
            int basePrice = CalculateSellUnitPrice(entry.Item, bonusPercent);
            float condition = entry.HasWeaponDurabilityState
                ? Mathf.Lerp(0.25f, 1f, entry.WeaponDurabilityNormalized)
                : entry.HasArmorState && entry.Item.ArmorMaxDurability > 0f
                    ? Mathf.Lerp(0.25f, 1f, Mathf.Clamp01(entry.ArmorDurability / entry.Item.ArmorMaxDurability))
                    : 1f;
            int magazineValue = entry.HasMagazineState ? Mathf.Max(0, entry.MagazineRounds) : 0;
            return Mathf.Max(1, Mathf.FloorToInt(basePrice * condition) + magazineValue);
        }

        public static float GetRarityValueMultiplier(ItemRarity rarity)
        {
            switch (rarity)
            {
                case ItemRarity.Uncommon: return 1.12f;
                case ItemRarity.Rare: return 1.3f;
                default: return 1f;
            }
        }

        // 상인 레벨에 따라 고급 장비를 해금하고, 회차 해시로 일반 물품 일부만 교대 판매합니다.
        // 탄약·치료품·음식은 초반 진행이 막히지 않도록 항상 재고에 남깁니다.
        public static bool IsStockAvailable(ItemDefinition item, int traderLevel, int stockRefreshIndex)
        {
            if (!IsTradable(item)) return false;
            int safeLevel = Mathf.Clamp(traderLevel, 1, 3);
            if (item.Rarity == ItemRarity.Rare && safeLevel < 3) return false;
            if ((item.Type == ItemType.Weapon || item.Type == ItemType.Armor || item.Type == ItemType.Attachment) && safeLevel < 2)
                return false;
            if (item.Type == ItemType.Ammo || item.Type == ItemType.Healing || item.Type == ItemType.Food) return true;

            uint stableHash = 17u;
            foreach (char character in item.ItemId ?? string.Empty)
            {
                // uint의 오버플로는 정의된 순환 연산이므로 어떤 아이템 ID에서도 예외 없이 같은 해시를 만듭니다.
                stableHash = unchecked(stableHash * 31u + character);
            }

            // int.MinValue에 Mathf.Abs를 적용하면 여전히 음수가 되는 모서리 사례를 피하려고 처음부터 부호 없는 값으로 계산합니다.
            uint rotationValue = stableHash + (uint)Mathf.Max(0, stockRefreshIndex);
            return rotationValue % 3u != 0u;
        }

        // 공간과 총 가격을 먼저 검사한 뒤 화폐와 아이템을 함께 변경합니다.
        // TryAdd가 예상 밖으로 실패하면 바로 지불 금액을 돌려주므로 어느 한쪽만 바뀐 거래가 남지 않습니다.
        public static bool TryBuy(
            PlayerInventory.GridData stash,
            ItemDefinition item,
            int quantity,
            int discountPercent,
            out string resultMessage)
        {
            resultMessage = string.Empty;
            if (stash == null || !IsTradable(item) || quantity <= 0)
            {
                resultMessage = "구매할 수 없는 상품입니다.";
                return false;
            }

            int unitPrice = CalculateBuyUnitPrice(item, discountPercent);
            long longPrice = (long)unitPrice * quantity;
            if (longPrice < 0 || longPrice > int.MaxValue)
            {
                resultMessage = "거래 수량이 너무 큽니다.";
                return false;
            }
            int totalPrice = (int)longPrice;

            if (!InventoryGridContent.CanAdd(stash, item, quantity))
            {
                resultMessage = "구매한 아이템 전체가 들어갈 창고 공간이 없습니다.";
                return false;
            }
            if (totalPrice > 0 && !HideoutEconomyStateStore.TrySpend(totalPrice))
            {
                resultMessage = $"크레딧이 부족합니다. 필요 {totalPrice}, 보유 {HideoutEconomyStateStore.Credits}";
                return false;
            }

            if (!InventoryGridContent.TryAdd(stash, item, quantity))
            {
                // 위의 CanAdd와 같은 격자 상태에서 바로 호출되므로 정상적으로는 실패하지 않습니다.
                // 그래도 향후 규칙 변경이나 외부 코드 개입에 대비해 돈을 되돌려 거래 원자성을 보장합니다.
                if (totalPrice > 0) HideoutEconomyStateStore.AddCredits(totalPrice);
                resultMessage = "아이템 배치가 변경되어 구매를 취소하고 크레딧을 돌려드렸습니다.";
                return false;
            }

            resultMessage = totalPrice == 0
                ? $"{item.DisplayName} x{quantity} 무료 구매"
                : $"{item.DisplayName} x{quantity} 구매 · {totalPrice} 크레딧 사용";
            return true;
        }

        // 선택한 묶음에서 요청 수량만 판매합니다. 컨테이너 내용물과 퀘스트 아이템은 데이터 손실을 막기 위해 거부합니다.
        public static bool TrySell(
            PlayerInventory.GridData source,
            InventoryEntry entry,
            int quantity,
            int bonusPercent,
            out string resultMessage)
        {
            resultMessage = string.Empty;
            if (source == null || entry == null || !source.Entries.Contains(entry))
            {
                resultMessage = "판매할 아이템이 현재 창고 격자에 없습니다.";
                return false;
            }
            if (!IsTradable(entry.Item))
            {
                resultMessage = "퀘스트 아이템이나 가치가 없는 아이템은 판매할 수 없습니다.";
                return false;
            }
            if (entry.IsRaidProvided)
            {
                resultMessage = "레이드 테스트 지급품은 판매할 수 없습니다.";
                return false;
            }
            if (entry.Contents != null && entry.Contents.Entries.Count > 0)
            {
                resultMessage = "내용물이 든 컨테이너는 판매할 수 없습니다. 내부를 먼저 비워 주세요.";
                return false;
            }
            if (quantity <= 0 || quantity > entry.Quantity)
            {
                resultMessage = "판매 수량이 올바르지 않습니다.";
                return false;
            }

            int unitPrice = CalculateSellUnitPrice(entry, bonusPercent);
            long longReward = (long)unitPrice * quantity;
            if (longReward <= 0 || longReward > int.MaxValue)
            {
                resultMessage = "판매 금액이 허용 범위를 벗어났습니다.";
                return false;
            }

            // 돈을 먼저 주지 않고 아이템 제거가 성공한 뒤 보상을 지급해 중복 판매를 막습니다.
            if (!entry.Consume(quantity))
            {
                resultMessage = "판매 수량을 아이템 묶음에서 제거하지 못했습니다.";
                return false;
            }
            if (entry.Quantity == 0) source.Entries.Remove(entry);

            int reward = (int)longReward;
            HideoutEconomyStateStore.AddCredits(reward);
            resultMessage = $"{entry.Item.DisplayName} x{quantity} 판매 · {reward} 크레딧 획득";
            return true;
        }
    }
}
