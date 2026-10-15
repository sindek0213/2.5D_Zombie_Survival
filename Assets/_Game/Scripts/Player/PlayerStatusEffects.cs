using System;
using System.Collections.Generic;
using UnityEngine;
using ZombieSurvival.Combat;

namespace ZombieSurvival.Player
{
    // 효과 적용 결과를 구분하면 인벤토리가 낮은 우선순위 약을 소비하지 않고 정확한 실패 이유를 표시할 수 있습니다.
    public enum StatusEffectApplyResult
    {
        Applied,
        Refreshed,
        ReplacedWeakerEffect,
        RejectedLowerPriority,
        RejectedEqualPriority,
        Invalid
    }

    // 현재 활성 효과 한 개와 종료 시각을 묶은 읽기 전용 실행 상태입니다.
    // 정의 에셋은 공유하지만 종료 시각은 플레이어마다 다르므로 ScriptableObject가 아니라 이 런타임 객체가 보관합니다.
    public sealed class ActiveStatusEffect
    {
        public StatusEffectDefinition Definition { get; }
        public float ExpiresAt { get; private set; }

        public ActiveStatusEffect(StatusEffectDefinition definition, float expiresAt)
        {
            Definition = definition;
            ExpiresAt = expiresAt;
        }

        public float RemainingSeconds(float currentTime) => Mathf.Max(0f, ExpiresAt - currentTime);
        public void Refresh(float expiresAt) => ExpiresAt = expiresAt;
    }

    // 플레이어에게 적용된 버프·디버프를 그룹별 한 개씩 관리합니다.
    // 같은 그룹의 강약은 실제 배율을 비교하지 않고 StatusEffectDefinition.Priority만 비교해 데이터 의도를 명확하게 유지합니다.
    public sealed class PlayerStatusEffects : MonoBehaviour, IDetailedIncomingDamageModifier
    {
        private readonly Dictionary<string, ActiveStatusEffect> activeByGroup =
            new Dictionary<string, ActiveStatusEffect>(StringComparer.Ordinal);
        private readonly List<string> expiredGroups = new List<string>();

        public IReadOnlyCollection<ActiveStatusEffect> ActiveEffects => activeByGroup.Values;
        public float MovementSpeedMultiplier => CalculateCombinedMultiplier(EffectStatToggle.MovementSpeed);
        public float IncomingDamageMultiplier => CalculateCombinedMultiplier(EffectStatToggle.IncomingDamage);
        public float OutgoingDamageMultiplier => CalculateCombinedMultiplier(EffectStatToggle.OutgoingDamage);

        public event Action EffectsChanged;

        private void Update() => RemoveExpiredEffects(Time.time);

        // 효과를 실제로 적용하기 전에 우선순위 규칙상 소비 가능한지 확인합니다.
        // 같은 ID는 남은 시간과 관계없이 재사용해 지속시간을 갱신할 수 있고, 다른 효과는 더 높은 우선순위일 때만 교체합니다.
        public StatusEffectApplyResult PreviewApply(StatusEffectDefinition definition, float currentTime)
        {
            if (definition == null || string.IsNullOrWhiteSpace(definition.EffectId))
                return StatusEffectApplyResult.Invalid;

            RemoveExpiredEffects(currentTime);
            if (!activeByGroup.TryGetValue(definition.EffectGroupId, out ActiveStatusEffect current))
                return StatusEffectApplyResult.Applied;
            if (current.Definition.EffectId == definition.EffectId)
                return StatusEffectApplyResult.Refreshed;
            if (definition.Priority > current.Definition.Priority)
                return StatusEffectApplyResult.ReplacedWeakerEffect;
            return definition.Priority < current.Definition.Priority
                ? StatusEffectApplyResult.RejectedLowerPriority
                : StatusEffectApplyResult.RejectedEqualPriority;
        }

        public StatusEffectApplyResult TryApply(StatusEffectDefinition definition)
            => TryApply(definition, Time.time);

        // currentTime을 인수로 받는 오버로드는 실제 게임에서는 Time.time을 사용하고 테스트에서는 원하는 경계 시각을 정확히 전달하게 합니다.
        public StatusEffectApplyResult TryApply(StatusEffectDefinition definition, float currentTime)
        {
            StatusEffectApplyResult result = PreviewApply(definition, currentTime);
            if (result == StatusEffectApplyResult.Invalid ||
                result == StatusEffectApplyResult.RejectedLowerPriority ||
                result == StatusEffectApplyResult.RejectedEqualPriority)
                return result;

            float expiresAt = currentTime + definition.DurationSeconds;
            if (result == StatusEffectApplyResult.Refreshed)
                activeByGroup[definition.EffectGroupId].Refresh(expiresAt);
            else
                activeByGroup[definition.EffectGroupId] = new ActiveStatusEffect(definition, expiresAt);

            EffectsChanged?.Invoke();
            return result;
        }

        // Health가 방어구 등 다른 피해 조정기와 함께 순서대로 호출하며, 이 컴포넌트는 현재 최종 받는 피해 배율만 적용합니다.
        public float ModifyIncomingDamage(DamageInfo damageInfo)
            => Mathf.Max(0f, damageInfo.Amount) * IncomingDamageMultiplier;

        public bool RemoveExpiredEffects(float currentTime)
        {
            expiredGroups.Clear();
            foreach (KeyValuePair<string, ActiveStatusEffect> pair in activeByGroup)
                if (pair.Value.ExpiresAt <= currentTime) expiredGroups.Add(pair.Key);
            if (expiredGroups.Count == 0) return false;

            foreach (string group in expiredGroups) activeByGroup.Remove(group);
            EffectsChanged?.Invoke();
            return true;
        }

        public float GetRemainingSeconds(StatusEffectDefinition definition, float currentTime)
        {
            if (definition == null ||
                !activeByGroup.TryGetValue(definition.EffectGroupId, out ActiveStatusEffect active) ||
                active.Definition != definition)
                return 0f;
            return active.RemainingSeconds(currentTime);
        }

        private float CalculateCombinedMultiplier(EffectStatToggle toggle)
        {
            float result = 1f;
            foreach (ActiveStatusEffect active in activeByGroup.Values)
                if (active.Definition != null && active.Definition.Uses(toggle))
                    result *= GetMultiplier(active.Definition, toggle);
            // 서로 다른 그룹은 함께 적용하되 잘못된 데이터가 속도·피해를 무한히 키우지 않도록 최종 범위를 제한합니다.
            return Mathf.Clamp(result, 0f, 4f);
        }

        private static float GetMultiplier(StatusEffectDefinition definition, EffectStatToggle toggle)
        {
            if (toggle == EffectStatToggle.MovementSpeed) return definition.MovementSpeedMultiplier;
            if (toggle == EffectStatToggle.IncomingDamage) return definition.IncomingDamageMultiplier;
            if (toggle == EffectStatToggle.OutgoingDamage) return definition.OutgoingDamageMultiplier;
            return 1f;
        }
    }
}
