using System;
using System.Collections.Generic;
using UnityEngine;
using ZombieSurvival.Items;

namespace ZombieSurvival.Hideout
{
    // 거점 시설의 역할을 코드 전체에서 문자열 대신 안전한 값으로 구분합니다.
    // 기존 저장은 enum 숫자를 기록하지 않고 FacilityId를 쓰므로 아래 기존 두 값의 순서만 유지하면 새 시설을 뒤에 추가해도 안전합니다.
    public enum HideoutFacilityType
    {
        Storage,
        MissionBoard,
        CraftingBench,
        Infirmary,
        Generator,
        Trader,
        Training,
        // 출격 터미널은 창고에서 아이템을 정리하는 기능과 지역 선택·출발 기능을 분리하기 위한 전용 시설입니다.
        // 기존 enum 숫자가 바뀌지 않도록 항상 마지막에 추가합니다.
        Deployment,
        // 모든 시설 강화 기능을 한곳에 모으는 전용 강화소입니다.
        // 기존 저장과 enum 숫자 호환을 위해 새 종류는 항상 마지막에 추가합니다.
        UpgradeStation
    }

    // 시설 강화 한 단계에 필요한 아이템 종류와 수량입니다.
    // 같은 목표 레벨에 여러 항목을 넣으면 모든 재료가 충분할 때만 한 번에 소비됩니다.
    [Serializable]
    public sealed class HideoutFacilityItemCost
    {
        public ItemDefinition item;
        [Min(1)] public int quantity = 1;

        public HideoutFacilityItemCost()
        {
        }

        public HideoutFacilityItemCost(ItemDefinition costItem, int costQuantity)
        {
            item = costItem;
            quantity = Mathf.Max(1, costQuantity);
        }
    }

    // targetLevel로 올라갈 때 필요한 모든 재료를 한 묶음으로 보관합니다.
    // 예를 들어 targetLevel 2 항목은 1레벨에서 2레벨로 강화할 때만 사용됩니다.
    [Serializable]
    public sealed class HideoutFacilityUpgradeStep
    {
        [Min(2)] public int targetLevel = 2;
        public HideoutFacilityItemCost[] costs = Array.Empty<HideoutFacilityItemCost>();

        public HideoutFacilityUpgradeStep()
        {
        }

        public HideoutFacilityUpgradeStep(int level, params HideoutFacilityItemCost[] itemCosts)
        {
            targetLevel = Mathf.Max(2, level);
            costs = itemCosts ?? Array.Empty<HideoutFacilityItemCost>();
        }
    }

    // 시설 이름, 종류, 레벨별 효과와 강화 비용을 씬 오브젝트에서 분리한 ScriptableObject입니다.
    // 여러 씬이나 여러 배치가 같은 정의를 참조해도 현재 레벨은 HideoutFacilityStateStore가 FacilityId별로 따로 저장합니다.
    [CreateAssetMenu(menuName = "Zombie Survival/Hideout Facility")]
    public sealed class HideoutFacilityDefinition : ScriptableObject
    {
        [SerializeField] private string facilityId = "facility";
        [SerializeField] private string displayName = "거점 시설";
        [SerializeField, TextArea] private string description;
        [SerializeField] private HideoutFacilityType facilityType;
        [SerializeField, Min(1)] private int startingLevel = 1;
        [SerializeField, Min(1)] private int maximumLevel = 3;
        // 효과 값은 시설마다 의미가 다릅니다. 창고는 추가 행, 임무 보드는 공개 임무 수, 제작대는 공개 제작법 수로 사용합니다.
        [SerializeField] private int baseEffectValue;
        [SerializeField] private int effectPerLevel = 1;
        [SerializeField] private string effectLabel = "시설 효과";
        [SerializeField] private HideoutFacilityUpgradeStep[] upgradeSteps = Array.Empty<HideoutFacilityUpgradeStep>();

        public string FacilityId => facilityId;
        public string DisplayName => string.IsNullOrWhiteSpace(displayName) ? facilityId : displayName;
        public string Description => description;
        public HideoutFacilityType FacilityType => facilityType;
        public int StartingLevel => Mathf.Clamp(startingLevel, 1, MaximumLevel);
        public int MaximumLevel => Mathf.Max(1, maximumLevel);
        public string EffectLabel => string.IsNullOrWhiteSpace(effectLabel) ? "시설 효과" : effectLabel;

        public int GetEffectValue(int level)
        {
            int safeLevel = Mathf.Clamp(level, 1, MaximumLevel);
            return baseEffectValue + (safeLevel - 1) * effectPerLevel;
        }

        public IReadOnlyList<HideoutFacilityItemCost> GetUpgradeCosts(int currentLevel)
        {
            int targetLevel = currentLevel + 1;
            if (upgradeSteps != null)
                foreach (HideoutFacilityUpgradeStep step in upgradeSteps)
                    if (step != null && step.targetLevel == targetLevel)
                        return step.costs ?? Array.Empty<HideoutFacilityItemCost>();
            return Array.Empty<HideoutFacilityItemCost>();
        }

        public string BuildEffectDescription(int level)
            => $"{EffectLabel}: {GetEffectValue(level)}";

        public string BuildUpgradeCostDescription(int currentLevel)
        {
            if (currentLevel >= MaximumLevel) return "최대 레벨";

            IReadOnlyList<HideoutFacilityItemCost> costs = GetUpgradeCosts(currentLevel);
            if (costs.Count == 0) return "강화 비용 설정 없음";
            var parts = new List<string>();
            foreach (HideoutFacilityItemCost cost in costs)
            {
                if (cost?.item == null || cost.quantity <= 0) continue;
                parts.Add($"{cost.item.DisplayName} x{cost.quantity}");
            }
            return parts.Count > 0 ? string.Join(" · ", parts) : "강화 비용 설정 오류";
        }

        // 기존 코드와 테스트가 사용하던 구성 메서드는 비용 없는 기본 시설 정의를 만듭니다.
        public void Configure(
            string id,
            string name,
            string facilityDescription,
            HideoutFacilityType type,
            int initialLevel,
            int maxLevel)
        {
            Configure(id, name, facilityDescription, type, initialLevel, maxLevel, 0, 1, "시설 효과");
        }

        // 프로토타입 생성기가 시설의 실제 레벨 효과와 단계별 비용을 함께 기록하는 주 구성 메서드입니다.
        public void Configure(
            string id,
            string name,
            string facilityDescription,
            HideoutFacilityType type,
            int initialLevel,
            int maxLevel,
            int initialEffect,
            int perLevelEffect,
            string levelEffectLabel,
            params HideoutFacilityUpgradeStep[] steps)
        {
            facilityId = string.IsNullOrWhiteSpace(id) ? "facility" : id.Trim();
            displayName = string.IsNullOrWhiteSpace(name) ? facilityId : name.Trim();
            description = facilityDescription ?? string.Empty;
            facilityType = type;
            maximumLevel = Mathf.Max(1, maxLevel);
            startingLevel = Mathf.Clamp(initialLevel, 1, maximumLevel);
            baseEffectValue = initialEffect;
            effectPerLevel = perLevelEffect;
            effectLabel = levelEffectLabel ?? string.Empty;
            upgradeSteps = steps ?? Array.Empty<HideoutFacilityUpgradeStep>();
            NormalizeCosts();
        }

        private void OnValidate()
        {
            maximumLevel = Mathf.Max(1, maximumLevel);
            startingLevel = Mathf.Clamp(startingLevel, 1, maximumLevel);
            NormalizeCosts();
        }

        private void NormalizeCosts()
        {
            if (upgradeSteps == null)
            {
                upgradeSteps = Array.Empty<HideoutFacilityUpgradeStep>();
                return;
            }

            foreach (HideoutFacilityUpgradeStep step in upgradeSteps)
            {
                if (step == null) continue;
                step.targetLevel = Mathf.Clamp(step.targetLevel, 2, MaximumLevel);
                if (step.costs == null) step.costs = Array.Empty<HideoutFacilityItemCost>();
                foreach (HideoutFacilityItemCost cost in step.costs)
                    if (cost != null) cost.quantity = Mathf.Max(1, cost.quantity);
            }
        }
    }

    // JSON에 저장할 시설 ID와 현재 레벨 한 쌍입니다.
    // Dictionary는 JsonUtility가 직접 저장하지 못하므로 직렬화 가능한 목록 항목으로 변환합니다.
    [Serializable]
    public sealed class HideoutFacilityLevelState
    {
        public string facilityId;
        public int level = 1;
    }

    // 씬이 바뀌어도 시설 레벨을 유지하며 저장 서비스가 이 값을 JSON으로 내보내고 다시 복원합니다.
    // 재료 검사는 HideoutController가 담당하고 이 저장소는 검증이 끝난 최종 레벨 변경만 수행합니다.
    public static class HideoutFacilityStateStore
    {
        private static readonly Dictionary<string, int> levels = new Dictionary<string, int>();

        public static int GetLevel(HideoutFacilityDefinition definition)
        {
            if (definition == null || string.IsNullOrWhiteSpace(definition.FacilityId)) return 1;
            if (levels.TryGetValue(definition.FacilityId, out int level))
                return Mathf.Clamp(level, 1, definition.MaximumLevel);
            return definition.StartingLevel;
        }

        // 이 메서드는 재료를 직접 소비하지 않습니다. 강화 컨트롤러가 비용 지불을 확정한 뒤 호출하는 최종 레벨 변경 단계입니다.
        public static bool TryUpgrade(HideoutFacilityDefinition definition)
        {
            if (definition == null || string.IsNullOrWhiteSpace(definition.FacilityId)) return false;
            int current = GetLevel(definition);
            if (current >= definition.MaximumLevel) return false;
            levels[definition.FacilityId] = current + 1;
            return true;
        }

        public static List<HideoutFacilityLevelState> CaptureState()
        {
            var result = new List<HideoutFacilityLevelState>();
            foreach (KeyValuePair<string, int> pair in levels)
                result.Add(new HideoutFacilityLevelState { facilityId = pair.Key, level = pair.Value });
            return result;
        }

        public static void ReplaceState(IEnumerable<HideoutFacilityLevelState> savedLevels)
        {
            levels.Clear();
            if (savedLevels == null) return;
            foreach (HideoutFacilityLevelState saved in savedLevels)
            {
                if (saved == null || string.IsNullOrWhiteSpace(saved.facilityId) || saved.level < 1) continue;
                // 저장 서비스 밖에서 테스트나 향후 불러오기 코드가 직접 상태를 전달해도 동일한 ID 정규화 규칙을 적용합니다.
                // 이렇게 하면 앞뒤 공백 때문에 조회할 수 없는 유령 시설 레벨이 Dictionary에 남지 않습니다.
                levels[saved.facilityId.Trim()] = saved.level;
            }
        }

        public static void ResetForTests() => levels.Clear();
    }
}
