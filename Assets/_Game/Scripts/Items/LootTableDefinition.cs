using System;
using UnityEngine;

namespace ZombieSurvival.Items
{
    // 전리품 추첨 후보 한 줄을 나타냅니다.
    // weight가 클수록 선택될 가능성이 높고, 선택되면 minimumQuantity~maximumQuantity 사이 수량이 생성됩니다.
    [Serializable]
    public sealed class LootTableEntry
    {
        public ItemDefinition item;
        [Min(0f)] public float weight = 1f;
        [Min(1)] public int minimumQuantity = 1;
        [Min(1)] public int maximumQuantity = 1;
    }

    // 의료 상자·무기 상자·은닉물처럼 상자 종류마다 다른 전리품 후보와 추첨 횟수를 저장하는 데이터 에셋입니다.
    // ScriptableObject로 분리했기 때문에 같은 표를 여러 상자가 공유하고 Inspector에서 확률과 수량만 조정할 수 있습니다.
    [CreateAssetMenu(menuName = "Zombie Survival/Loot Table")]
    public sealed class LootTableDefinition : ScriptableObject
    {
        [SerializeField] private string displayName = "보급 상자";
        [SerializeField, Min(1)] private int minimumRolls = 2;
        [SerializeField, Min(1)] private int maximumRolls = 4;
        [SerializeField] private LootTableEntry[] entries = Array.Empty<LootTableEntry>();

        public string DisplayName => string.IsNullOrWhiteSpace(displayName) ? name : displayName;
        public int MinimumRolls => Mathf.Max(1, minimumRolls);
        public int MaximumRolls => Mathf.Max(MinimumRolls, maximumRolls);
        public LootTableEntry[] Entries => entries;

        // 에디터 자동 생성기와 테스트가 상자 종류의 모든 기본값을 한 번에 설정할 때 사용합니다.
        // 잘못된 최소·최대 순서는 즉시 보정하고 null 배열은 빈 배열로 바꿔 런타임 예외를 막습니다.
        public void Configure(
            string newDisplayName,
            int newMinimumRolls,
            int newMaximumRolls,
            params LootTableEntry[] newEntries)
        {
            displayName = newDisplayName;
            minimumRolls = Mathf.Max(1, newMinimumRolls);
            maximumRolls = Mathf.Max(minimumRolls, newMaximumRolls);
            entries = newEntries ?? Array.Empty<LootTableEntry>();
        }

        // 0~1 난수 하나를 최소~최대 범위의 추첨 횟수로 바꿉니다.
        // 난수를 밖에서 전달받는 순수 함수라 최소값과 최대값 경계를 기다리지 않고 EditMode 테스트할 수 있습니다.
        public static int CalculateRollCount(int minimum, int maximum, float normalizedRoll)
        {
            int safeMinimum = Mathf.Max(1, minimum);
            int safeMaximum = Mathf.Max(safeMinimum, maximum);
            int range = safeMaximum - safeMinimum + 1;
            int offset = Mathf.Min(
                range - 1,
                Mathf.FloorToInt(Mathf.Clamp01(normalizedRoll) * range));
            return safeMinimum + offset;
        }

        // null 아이템과 가중치 0인 항목을 제외하고 가중치 비율에 따라 한 후보의 배열 인덱스를 선택합니다.
        // 예를 들어 가중치가 1과 3이면 첫 후보는 25%, 두 번째 후보는 75% 범위를 차지합니다.
        public static int SelectWeightedEntryIndex(LootTableEntry[] candidates, float normalizedRoll)
            => SelectWeightedEntryIndex(candidates, normalizedRoll, 1);

        // 위험도 1은 에셋에 적힌 원래 확률을 그대로 사용합니다.
        // 위험도가 높아질수록 희귀 등급의 유효 가중치만 늘려, 같은 상자 종류라도 위험한 구역에서 더 좋은 물건을 기대할 수 있습니다.
        public static int SelectWeightedEntryIndex(
            LootTableEntry[] candidates,
            float normalizedRoll,
            int dangerRating)
        {
            if (candidates == null || candidates.Length == 0) return -1;

            float totalWeight = 0f;
            int lastValidIndex = -1;
            for (int i = 0; i < candidates.Length; i++)
            {
                LootTableEntry candidate = candidates[i];
                if (candidate?.item == null || candidate.weight <= 0f) continue;
                totalWeight += candidate.weight * GetDangerWeightMultiplier(candidate.item.Rarity, dangerRating);
                lastValidIndex = i;
            }
            if (totalWeight <= 0f) return -1;

            float target = Mathf.Clamp01(normalizedRoll) * totalWeight;
            float accumulated = 0f;
            for (int i = 0; i < candidates.Length; i++)
            {
                LootTableEntry candidate = candidates[i];
                if (candidate?.item == null || candidate.weight <= 0f) continue;
                accumulated += candidate.weight * GetDangerWeightMultiplier(candidate.item.Rarity, dangerRating);
                if (target < accumulated) return i;
            }

            // normalizedRoll이 정확히 1이면 target과 전체 가중치가 같으므로 마지막 유효 후보를 반환합니다.
            return lastValidIndex;
        }

        // Common은 어느 구역에서도 기준 확률을 유지하고 Uncommon·Rare만 단계적으로 증가시킵니다.
        // Quest 아이템은 임무 진행을 운에 과도하게 맡기지 않도록 위험도 보정을 받지 않습니다.
        public static float GetDangerWeightMultiplier(ItemRarity rarity, int dangerRating)
        {
            int extraDanger = Mathf.Max(0, dangerRating - 1);
            switch (rarity)
            {
                case ItemRarity.Rare: return 1f + extraDanger * 0.45f;
                case ItemRarity.Uncommon: return 1f + extraDanger * 0.2f;
                default: return 1f;
            }
        }

        // 선택된 후보의 0~1 난수를 최소~최대 정수 수량으로 변환합니다.
        // 최대보다 큰 최소값처럼 잘못된 설정도 안전하게 보정해 항상 1개 이상의 유효 수량을 반환합니다.
        public static int CalculateQuantity(int minimum, int maximum, float normalizedRoll)
        {
            int safeMinimum = Mathf.Max(1, minimum);
            int safeMaximum = Mathf.Max(safeMinimum, maximum);
            int range = safeMaximum - safeMinimum + 1;
            int offset = Mathf.Min(
                range - 1,
                Mathf.FloorToInt(Mathf.Clamp01(normalizedRoll) * range));
            return safeMinimum + offset;
        }

        // Inspector에서 값을 직접 바꿨을 때도 음수 가중치나 뒤집힌 수량 범위가 저장되지 않도록 정리합니다.
        private void OnValidate()
        {
            minimumRolls = Mathf.Max(1, minimumRolls);
            maximumRolls = Mathf.Max(minimumRolls, maximumRolls);
            if (entries == null) entries = Array.Empty<LootTableEntry>();

            for (int i = 0; i < entries.Length; i++)
            {
                LootTableEntry entry = entries[i];
                if (entry == null) continue;
                entry.weight = Mathf.Max(0f, entry.weight);
                entry.minimumQuantity = Mathf.Max(1, entry.minimumQuantity);
                entry.maximumQuantity = Mathf.Max(entry.minimumQuantity, entry.maximumQuantity);
            }
        }
    }
}
