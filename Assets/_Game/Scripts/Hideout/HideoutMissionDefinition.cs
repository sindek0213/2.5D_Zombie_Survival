using System;
using System.Collections.Generic;
using UnityEngine;
using ZombieSurvival.Items;

namespace ZombieSurvival.Hideout
{
    // 임무 목표의 계산 방법을 구분합니다.
    // 모든 목표를 int 진행도 하나로 저장하되, 어떤 레이드 결과를 더할지는 이 값에 따라 달라집니다.
    public enum HideoutMissionObjectiveType
    {
        ZombieKills,
        CollectedItem,
        SuccessfulExtraction
    }

    // 임무 완료 때 창고로 지급할 아이템 한 종류와 수량입니다.
    // ItemDefinition 참조는 에디터에서 보상을 고르기 쉽게 하고, 실제 저장은 기존 창고 시스템이 ItemId로 변환합니다.
    [Serializable]
    public sealed class HideoutMissionItemReward
    {
        public ItemDefinition item;
        [Min(1)] public int quantity = 1;

        public HideoutMissionItemReward()
        {
        }

        // 프로토타입 생성기와 테스트가 짧고 읽기 쉬운 형태로 보상 데이터를 만들 수 있게 제공하는 생성자입니다.
        public HideoutMissionItemReward(ItemDefinition rewardItem, int rewardQuantity)
        {
            item = rewardItem;
            quantity = Mathf.Max(1, rewardQuantity);
        }
    }

    // 임무의 표시 정보, 목표 계산 규칙과 실제 아이템 보상을 보관하는 ScriptableObject입니다.
    // 실행 중 진행도와 완료 횟수는 저장 가능한 정적 상태인 HideoutMissionStateStore가 따로 담당합니다.
    [CreateAssetMenu(menuName = "Zombie Survival/Hideout Mission")]
    public sealed class HideoutMissionDefinition : ScriptableObject
    {
        [SerializeField] private string missionId = "mission";
        [SerializeField] private string displayName = "임무";
        [SerializeField, TextArea] private string description;
        [SerializeField] private HideoutMissionObjectiveType objectiveType;
        // 아이템 회수 임무만 이 참조를 사용합니다. 다른 목표 종류에서는 null이어도 정상입니다.
        [SerializeField] private ItemDefinition objectiveItem;
        [SerializeField, Min(1)] private int requiredAmount = 3;
        // false인 임무는 완료 기록이 한 번 생기면 다시 받을 수 없고, true인 임무는 반복해서 보상을 받을 수 있습니다.
        [SerializeField] private bool repeatable;
        [SerializeField] private HideoutMissionItemReward[] itemRewards = Array.Empty<HideoutMissionItemReward>();

        public string MissionId => missionId;
        public string DisplayName => string.IsNullOrWhiteSpace(displayName) ? missionId : displayName;
        public string Description => description;
        public HideoutMissionObjectiveType ObjectiveType => objectiveType;
        public ItemDefinition ObjectiveItem => objectiveItem;
        public int RequiredAmount => Mathf.Max(1, requiredAmount);
        public bool Repeatable => repeatable;
        public IReadOnlyList<HideoutMissionItemReward> ItemRewards => itemRewards ?? Array.Empty<HideoutMissionItemReward>();

        // 이전 처치 임무 코드와 테스트가 바로 깨지지 않게 남겨 둔 호환용 이름입니다.
        // 새 코드는 목표 종류와 관계없이 RequiredAmount를 사용합니다.
        public int RequiredZombieKills => RequiredAmount;

        // UI와 이전 코드가 보상 문자열을 직접 저장하지 않아도 실제 보상 배열에서 항상 최신 설명을 만들게 합니다.
        public string RewardDescription => BuildRewardDescription();

        // 기존 5개 매개변수 구성은 좀비 처치 임무로 해석합니다.
        // rewardDescription은 옛 테스트 호출 호환을 위해 받지만 실제 보상은 없는 테스트용 임무가 됩니다.
        public void Configure(string id, string name, string missionDescription, int requiredKills, string rewardDescription)
        {
            Configure(
                id,
                name,
                missionDescription,
                HideoutMissionObjectiveType.ZombieKills,
                null,
                requiredKills,
                true,
                Array.Empty<HideoutMissionItemReward>());
        }

        // 생성기나 커스텀 에디터가 임무의 모든 실행 규칙을 한 번에 설정하는 주 구성 메서드입니다.
        public void Configure(
            string id,
            string name,
            string missionDescription,
            HideoutMissionObjectiveType type,
            ItemDefinition targetItem,
            int targetAmount,
            bool canRepeat,
            params HideoutMissionItemReward[] rewards)
        {
            missionId = string.IsNullOrWhiteSpace(id) ? "mission" : id.Trim();
            displayName = string.IsNullOrWhiteSpace(name) ? missionId : name.Trim();
            description = missionDescription ?? string.Empty;
            objectiveType = type;
            objectiveItem = type == HideoutMissionObjectiveType.CollectedItem ? targetItem : null;
            requiredAmount = Mathf.Max(1, targetAmount);
            repeatable = canRepeat;
            itemRewards = rewards ?? Array.Empty<HideoutMissionItemReward>();
            NormalizeRewards();
        }

        // 현재 진행도를 함께 받아 HUD에 그대로 넣을 수 있는 목표 문장을 만듭니다.
        public string BuildProgressDescription(int progress)
        {
            int safeProgress = Mathf.Max(0, progress);
            switch (objectiveType)
            {
                case HideoutMissionObjectiveType.CollectedItem:
                    string itemName = objectiveItem != null ? objectiveItem.DisplayName : "설정되지 않은 아이템";
                    return $"아이템 회수: {itemName} {safeProgress}/{RequiredAmount}";
                case HideoutMissionObjectiveType.SuccessfulExtraction:
                    return $"레이드 탈출: {safeProgress}/{RequiredAmount}";
                default:
                    return $"좀비 처치: {safeProgress}/{RequiredAmount}";
            }
        }

        // 보상 배열을 사람이 읽는 한 줄로 합칩니다. 실제 지급 데이터와 UI 문구가 서로 달라지는 문제를 막는 역할입니다.
        public string BuildRewardDescription()
        {
            if (itemRewards == null || itemRewards.Length == 0) return "보상 없음";

            var parts = new List<string>();
            foreach (HideoutMissionItemReward reward in itemRewards)
            {
                if (reward?.item == null || reward.quantity <= 0) continue;
                parts.Add($"{reward.item.DisplayName} x{reward.quantity}");
            }
            return parts.Count > 0 ? string.Join(" · ", parts) : "보상 설정 오류";
        }

        // Inspector에서 잘못된 수량이나 아이템 없는 항목이 저장되지 않도록 에셋 자체를 정리합니다.
        private void OnValidate()
        {
            requiredAmount = Mathf.Max(1, requiredAmount);
            if (objectiveType != HideoutMissionObjectiveType.CollectedItem) objectiveItem = null;
            NormalizeRewards();
        }

        private void NormalizeRewards()
        {
            if (itemRewards == null)
            {
                itemRewards = Array.Empty<HideoutMissionItemReward>();
                return;
            }

            foreach (HideoutMissionItemReward reward in itemRewards)
                if (reward != null) reward.quantity = Mathf.Max(1, reward.quantity);
        }
    }
}
