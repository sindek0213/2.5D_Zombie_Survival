using System;
using UnityEngine;

namespace ZombieSurvival.Combat
{
    // HUD에서 이로운 효과와 해로운 효과를 구분할 때 사용하는 표시 분류입니다.
    // 실제 수치 적용 여부는 아래 EffectStatToggle이 담당하므로 Buff와 Debuff를 바꿔도 계산 코드가 달라지지는 않습니다.
    public enum StatusEffectDisposition
    {
        Buff,
        Debuff
    }

    // 한 효과가 어떤 능력치를 바꾸는지 Inspector의 체크박스로 선택합니다.
    // Flags를 사용하므로 이동 속도와 받는 피해처럼 여러 능력치를 한 효과가 동시에 변경할 수도 있습니다.
    [Flags]
    public enum EffectStatToggle
    {
        None = 0,
        MovementSpeed = 1 << 0,
        IncomingDamage = 1 << 1,
        OutgoingDamage = 1 << 2
    }

    // 일정 시간 유지되는 버프·디버프의 정적 데이터를 저장합니다.
    // 같은 effectGroupId 안에서는 multiplier 수치를 직접 비교하지 않고 priority가 높은 정의만 활성화됩니다.
    [CreateAssetMenu(menuName = "Zombie Survival/Status Effect Definition")]
    public sealed class StatusEffectDefinition : ScriptableObject
    {
        [SerializeField] private string effectId;
        [SerializeField] private string displayName = "상태 효과";
        [SerializeField] private StatusEffectDisposition disposition = StatusEffectDisposition.Buff;
        // 같은 계열의 약물이나 디버프는 같은 그룹 ID를 사용합니다. 예: speed_boost, damage_guard.
        [SerializeField] private string effectGroupId;
        // 같은 그룹에서는 수치가 아니라 이 우선순위를 비교합니다. 값이 큰 효과가 기존의 약한 효과를 교체합니다.
        [SerializeField, Min(0)] private int priority;
        [SerializeField, Min(0.1f)] private float durationSeconds = 30f;

        [Header("적용 능력치 토글")]
        [SerializeField] private EffectStatToggle affectedStats = EffectStatToggle.None;
        // 배율 1은 변화 없음, 1.25는 25% 증가, 0.75는 25% 감소입니다.
        [SerializeField, Min(0f)] private float movementSpeedMultiplier = 1f;
        [SerializeField, Min(0f)] private float incomingDamageMultiplier = 1f;
        [SerializeField, Min(0f)] private float outgoingDamageMultiplier = 1f;

        public string EffectId => string.IsNullOrWhiteSpace(effectId) ? name : effectId;
        public string DisplayName => string.IsNullOrWhiteSpace(displayName) ? "상태 효과" : displayName;
        public StatusEffectDisposition Disposition => disposition;
        public string EffectGroupId => string.IsNullOrWhiteSpace(effectGroupId) ? EffectId : effectGroupId;
        public int Priority => Mathf.Max(0, priority);
        public float DurationSeconds => Mathf.Max(0.1f, durationSeconds);
        public EffectStatToggle AffectedStats => affectedStats;
        public float MovementSpeedMultiplier => Uses(EffectStatToggle.MovementSpeed)
            ? Mathf.Max(0f, movementSpeedMultiplier)
            : 1f;
        public float IncomingDamageMultiplier => Uses(EffectStatToggle.IncomingDamage)
            ? Mathf.Max(0f, incomingDamageMultiplier)
            : 1f;
        public float OutgoingDamageMultiplier => Uses(EffectStatToggle.OutgoingDamage)
            ? Mathf.Max(0f, outgoingDamageMultiplier)
            : 1f;

        public bool Uses(EffectStatToggle toggle) => (affectedStats & toggle) != 0;

        // 에디터 생성기와 테스트가 같은 안전 범위로 효과를 구성하도록 모든 설정을 한 진입점에 모았습니다.
        public void Configure(
            string id,
            string effectName,
            StatusEffectDisposition effectDisposition,
            string groupId,
            int effectPriority,
            float duration,
            EffectStatToggle statToggles,
            float movementMultiplier = 1f,
            float incomingMultiplier = 1f,
            float outgoingMultiplier = 1f)
        {
            effectId = id;
            displayName = effectName;
            disposition = effectDisposition;
            effectGroupId = groupId;
            priority = Mathf.Max(0, effectPriority);
            durationSeconds = Mathf.Max(0.1f, duration);
            affectedStats = statToggles;
            movementSpeedMultiplier = Mathf.Max(0f, movementMultiplier);
            incomingDamageMultiplier = Mathf.Max(0f, incomingMultiplier);
            outgoingDamageMultiplier = Mathf.Max(0f, outgoingMultiplier);
        }

        private void OnValidate()
        {
            priority = Mathf.Max(0, priority);
            durationSeconds = Mathf.Max(0.1f, durationSeconds);
            movementSpeedMultiplier = Mathf.Max(0f, movementSpeedMultiplier);
            incomingDamageMultiplier = Mathf.Max(0f, incomingDamageMultiplier);
            outgoingDamageMultiplier = Mathf.Max(0f, outgoingDamageMultiplier);
        }
    }
}
