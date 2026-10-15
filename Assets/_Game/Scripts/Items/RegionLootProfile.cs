using System;
using UnityEngine;
using ZombieSurvival.Core;
using ZombieSurvival.Hideout;

namespace ZombieSurvival.Items
{
    // 같은 지역 안에서도 전리품을 발견하는 장소에 따라 서로 다른 표를 사용할 수 있게 구분합니다.
    // Container는 아이템 상자, Ground는 바닥 생성 지점, Monster는 좀비 시체 전리품을 뜻합니다.
    public enum RegionLootSourceType
    {
        Container,
        Ground,
        Monster
    }

    // 지역 드랍 테이블에서 아이템 한 종류가 가진 확률, 가중치와 수량 범위를 저장합니다.
    // dropChance는 실제 생성 성공 확률이고 weight는 여러 후보 중 이 아이템이 먼저 선택될 상대 비율입니다.
    [Serializable]
    public sealed class RegionLootEntry
    {
        public ItemDefinition item;
        [Range(0f, 1f)] public float dropChance = 1f;
        [Min(0f)] public float weight = 1f;
        [Min(1)] public int minimumQuantity = 1;
        [Min(1)] public int maximumQuantity = 1;

        public RegionLootEntry()
        {
        }

        // 에디터 생성기와 테스트에서 한 항목을 읽기 쉬운 한 줄로 만들기 위한 생성자입니다.
        public RegionLootEntry(
            ItemDefinition definition,
            float chance,
            float selectionWeight,
            int minimum,
            int maximum)
        {
            item = definition;
            dropChance = Mathf.Clamp01(chance);
            weight = Mathf.Max(0f, selectionWeight);
            minimumQuantity = Mathf.Max(1, minimum);
            maximumQuantity = Mathf.Max(minimumQuantity, maximum);
        }

        // Inspector에서 잘못 입력한 음수나 뒤집힌 수량 범위를 런타임 전에 안전한 값으로 고칩니다.
        public void Normalize()
        {
            dropChance = Mathf.Clamp01(dropChance);
            weight = Mathf.Max(0f, weight);
            minimumQuantity = Mathf.Max(1, minimumQuantity);
            maximumQuantity = Mathf.Max(minimumQuantity, maximumQuantity);
        }
    }

    // 한 지역 안의 한 출처를 나타내는 독립된 드랍 표입니다.
    // 예를 들어 Container + weapon_crate와 Monster + brute는 같은 지역 에셋 안에서도 완전히 다른 후보를 가집니다.
    [Serializable]
    public sealed class RegionLootSourceTable
    {
        [SerializeField] private RegionLootSourceType sourceType;
        [SerializeField] private string sourceId = "default";
        [SerializeField, Min(1)] private int minimumRolls = 1;
        [SerializeField, Min(1)] private int maximumRolls = 1;
        [SerializeField] private RegionLootEntry[] entries = Array.Empty<RegionLootEntry>();

        public RegionLootSourceType SourceType => sourceType;
        public string SourceId => NormalizeSourceId(sourceId);
        public int MinimumRolls => Mathf.Max(1, minimumRolls);
        public int MaximumRolls => Mathf.Max(MinimumRolls, maximumRolls);
        public RegionLootEntry[] Entries => entries ?? Array.Empty<RegionLootEntry>();

        public RegionLootSourceTable()
        {
        }

        // sourceId는 상자 종류나 좀비 유형을 연결하는 안정적인 ID입니다.
        // 별표(*)를 넣은 표은 정확히 맞는 ID가 없을 때 해당 출처 종류 전체가 공유하는 기본 표가 됩니다.
        public RegionLootSourceTable(
            RegionLootSourceType type,
            string id,
            int minimum,
            int maximum,
            params RegionLootEntry[] newEntries)
        {
            sourceType = type;
            sourceId = NormalizeSourceId(id);
            minimumRolls = Mathf.Max(1, minimum);
            maximumRolls = Mathf.Max(minimumRolls, maximum);
            entries = newEntries ?? Array.Empty<RegionLootEntry>();
            Normalize();
        }

        public void Normalize()
        {
            sourceId = NormalizeSourceId(sourceId);
            minimumRolls = Mathf.Max(1, minimumRolls);
            maximumRolls = Mathf.Max(minimumRolls, maximumRolls);
            if (entries == null) entries = Array.Empty<RegionLootEntry>();
            for (int i = 0; i < entries.Length; i++) entries[i]?.Normalize();
        }

        public static string NormalizeSourceId(string id)
            => string.IsNullOrWhiteSpace(id) ? "default" : id.Trim().ToLowerInvariant();
    }

    // 지역 하나가 가진 상자·바닥·몬스터 전리품 수치를 한 에셋에서 조절하는 ScriptableObject입니다.
    // 실행 중에는 이 에셋의 원본 수치를 바꾸지 않고 임무 보정값만 계산 결과에 더하므로 임무 완료나 포기 즉시 원래 값으로 돌아옵니다.
    [CreateAssetMenu(menuName = "Zombie Survival/Region Loot Profile")]
    public sealed class RegionLootProfile : ScriptableObject
    {
        [SerializeField] private RaidRegionDefinition region;
        [SerializeField, Range(0f, 1f)] private float activeMissionChanceBonus = 0.05f;
        [SerializeField] private RegionLootSourceTable[] sourceTables = Array.Empty<RegionLootSourceTable>();

        public RaidRegionDefinition Region => region;
        public string RegionId => region != null ? region.RegionId : string.Empty;
        public float ActiveMissionChanceBonus => Mathf.Clamp01(activeMissionChanceBonus);
        public RegionLootSourceTable[] SourceTables => sourceTables ?? Array.Empty<RegionLootSourceTable>();

        // 생성기나 테스트가 지역 참조, 임무 추가 확률과 모든 출처 표를 한 번에 설정할 때 사용합니다.
        public void Configure(
            RaidRegionDefinition regionDefinition,
            float missionChanceBonus,
            params RegionLootSourceTable[] tables)
        {
            region = regionDefinition;
            activeMissionChanceBonus = Mathf.Clamp01(missionChanceBonus);
            sourceTables = tables ?? Array.Empty<RegionLootSourceTable>();
            NormalizeTables();
        }

        // 먼저 출처 종류와 ID가 모두 같은 표를 찾고, 없으면 같은 출처 종류의 별표(*) 기본 표를 사용합니다.
        // 정확한 표와 기본 표가 모두 없으면 null을 반환해 호출부가 기존 전리품 데이터로 안전하게 대체할 수 있게 합니다.
        public RegionLootSourceTable FindTable(RegionLootSourceType sourceType, string sourceId)
        {
            string normalizedId = RegionLootSourceTable.NormalizeSourceId(sourceId);
            RegionLootSourceTable fallback = null;
            RegionLootSourceTable[] tables = SourceTables;

            for (int i = 0; i < tables.Length; i++)
            {
                RegionLootSourceTable table = tables[i];
                if (table == null || table.SourceType != sourceType) continue;
                if (string.Equals(table.SourceId, normalizedId, StringComparison.Ordinal)) return table;
                if (table.SourceId == "*") fallback = table;
            }

            return fallback;
        }

        // 현재 받은 임무가 이 아이템을 회수하는 임무이고, 이 지역의 어느 표에라도 아이템이 존재할 때만 true입니다.
        // 따라서 다른 지역에서만 나오는 임무 아이템 때문에 현재 지역의 드랍 수치가 잘못 보정되는 일을 막습니다.
        public bool IsActiveMissionTarget(ItemDefinition item)
        {
            if (item == null || !ContainsItem(item)) return false;

            HideoutMissionDefinition mission = HideoutMissionStateStore.ActiveMissionDefinition;
            if (mission == null || mission.ObjectiveItem == null) return false;
            if (mission.ObjectiveType != HideoutMissionObjectiveType.CollectedItem &&
                mission.ObjectiveType != HideoutMissionObjectiveType.CollectedItemInZone) return false;

            return string.Equals(mission.ObjectiveItem.ItemId, item.ItemId, StringComparison.Ordinal);
        }

        // 에셋에 저장된 기본 확률에 진행 중인 회수 임무의 5%p 보정을 더합니다.
        // 기본 확률이 0%여도 임무 중에는 5%가 되며, 100%를 넘는 값은 100%로 제한합니다.
        public float GetEffectiveDropChance(RegionLootEntry entry)
        {
            if (entry == null || entry.item == null) return 0f;
            float bonus = IsActiveMissionTarget(entry.item) ? ActiveMissionChanceBonus : 0f;
            return Mathf.Clamp01(entry.dropChance + bonus);
        }

        // 임무 아이템의 선택 가중치가 실수로 0이어도 아주 작은 후보 범위를 만들어 실제 추첨 가능성을 보장합니다.
        // 일반 상황에서는 에셋에 적힌 가중치를 그대로 사용하므로 임무가 끝나는 순간 별도 복구 작업 없이 원상태가 됩니다.
        public float GetEffectiveWeight(RegionLootEntry entry)
        {
            if (entry == null || entry.item == null) return 0f;
            float baseWeight = Mathf.Max(0f, entry.weight);
            return IsActiveMissionTarget(entry.item) ? Mathf.Max(0.01f, baseWeight) : baseWeight;
        }

        // Container와 Ground 표에서 가중치 비율에 따라 후보 한 줄의 배열 인덱스를 고릅니다.
        // 위험도가 높을수록 희귀 아이템 가중치를 늘리는 기존 규칙도 그대로 적용해 지역별 밸런스와 위험도 보정이 함께 작동합니다.
        public int SelectWeightedEntryIndex(
            RegionLootSourceTable table,
            float normalizedRoll,
            int dangerRating = 1)
        {
            RegionLootEntry[] candidates = table?.Entries;
            if (candidates == null || candidates.Length == 0) return -1;

            float totalWeight = 0f;
            int lastValidIndex = -1;
            for (int i = 0; i < candidates.Length; i++)
            {
                RegionLootEntry candidate = candidates[i];
                float weight = GetEffectiveWeight(candidate);
                if (candidate?.item == null || weight <= 0f) continue;
                totalWeight += weight * LootTableDefinition.GetDangerWeightMultiplier(candidate.item.Rarity, dangerRating);
                lastValidIndex = i;
            }
            if (totalWeight <= 0f) return -1;

            float target = Mathf.Clamp01(normalizedRoll) * totalWeight;
            float accumulated = 0f;
            for (int i = 0; i < candidates.Length; i++)
            {
                RegionLootEntry candidate = candidates[i];
                float weight = GetEffectiveWeight(candidate);
                if (candidate?.item == null || weight <= 0f) continue;
                accumulated += weight * LootTableDefinition.GetDangerWeightMultiplier(candidate.item.Rarity, dangerRating);
                if (target < accumulated) return i;
            }

            // normalizedRoll이 정확히 1이면 누적값과 목표값이 같아지므로 마지막 유효 후보를 반환합니다.
            return lastValidIndex;
        }

        // 아이템이 이 지역의 상자·바닥·몬스터 표 중 한 곳에라도 등록돼 있는지 ItemId로 검사합니다.
        public bool ContainsItem(ItemDefinition item)
        {
            if (item == null || string.IsNullOrWhiteSpace(item.ItemId)) return false;
            RegionLootSourceTable[] tables = SourceTables;
            for (int tableIndex = 0; tableIndex < tables.Length; tableIndex++)
            {
                RegionLootEntry[] entries = tables[tableIndex]?.Entries;
                if (entries == null) continue;
                for (int entryIndex = 0; entryIndex < entries.Length; entryIndex++)
                {
                    ItemDefinition candidate = entries[entryIndex]?.item;
                    if (candidate != null && string.Equals(candidate.ItemId, item.ItemId, StringComparison.Ordinal)) return true;
                }
            }
            return false;
        }

        // 0~1 난수 하나를 해당 표의 최소~최대 추첨 횟수로 바꿉니다.
        public static int CalculateRollCount(RegionLootSourceTable table, float normalizedRoll)
            => table == null
                ? 0
                : LootTableDefinition.CalculateRollCount(table.MinimumRolls, table.MaximumRolls, normalizedRoll);

        // 선택된 항목의 수량 범위를 기존 LootTableDefinition과 동일한 안전 규칙으로 계산합니다.
        public static int CalculateQuantity(RegionLootEntry entry, float normalizedRoll)
            => entry == null
                ? 0
                : LootTableDefinition.CalculateQuantity(entry.minimumQuantity, entry.maximumQuantity, normalizedRoll);

        private void OnValidate()
        {
            activeMissionChanceBonus = Mathf.Clamp01(activeMissionChanceBonus);
            NormalizeTables();
        }

        private void NormalizeTables()
        {
            if (sourceTables == null) sourceTables = Array.Empty<RegionLootSourceTable>();
            for (int i = 0; i < sourceTables.Length; i++) sourceTables[i]?.Normalize();
        }
    }
}
