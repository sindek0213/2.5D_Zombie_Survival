using System;
using UnityEngine;
using ZombieSurvival.Combat;

namespace ZombieSurvival.Player
{
    // 플레이어에게 발생한 출혈과 골절 같은 상태 이상만 관리합니다.
    // 머리·몸통·팔·다리별 별도 체력은 사용하지 않으며 전체 생존 여부와 실제 체력은 Health가 계속 담당합니다.
    [RequireComponent(typeof(Health))]
    public sealed class PlayerCondition : MonoBehaviour
    {
        [Header("출혈 디버프")]
        // 출혈은 전체 체력을 초당 이 값만큼 줄입니다. 화면 연출은 Health의 일반 피격 이벤트와 분리해 HUD 테두리가 담당합니다.
        [SerializeField, Min(0f)] private float bleedingDamagePerSecond = 1.5f;

        [Header("골절 디버프")]
        // 골절은 특정 부위와 연결하지 않고 이동·조준·재장전 전체에 단순한 불이익을 줍니다.
        // 부목을 사용하면 세 배율이 모두 즉시 정상값 1로 돌아옵니다.
        [SerializeField, Range(0.1f, 1f)] private float fractureMovementMultiplier = 0.72f;
        [SerializeField, Min(1f)] private float fractureAimSpreadMultiplier = 1.15f;
        [SerializeField, Min(1f)] private float fractureReloadMultiplier = 1.2f;

        private Health health;

        public bool IsBleeding { get; private set; }
        public bool HasFracture { get; private set; }
        public float MovementMultiplier => HasFracture
            ? Mathf.Clamp(fractureMovementMultiplier, 0.1f, 1f)
            : 1f;
        public float AimSpreadMultiplier => HasFracture
            ? Mathf.Max(1f, fractureAimSpreadMultiplier)
            : 1f;
        public float ReloadDurationMultiplier => HasFracture
            ? Mathf.Max(1f, fractureReloadMultiplier)
            : 1f;

        public event Action ConditionChanged;

        private void Awake()
        {
            health = GetComponent<Health>();
            health.DamageResolved += HandleDamage;
        }

        private void OnDestroy()
        {
            if (health != null) health.DamageResolved -= HandleDamage;
        }

        private void Update()
        {
            if (!IsBleeding || health == null || health.IsDead || Time.deltaTime <= 0f) return;

            // deltaTime을 곱해 프레임 수와 상관없이 초당 같은 피해를 줍니다. 일시정지 중에는 게임 시간이 멈춰 출혈도 멈춥니다.
            // 상태 피해임을 전달하면 추가 골절·출혈을 유발하지 않고, 일반 피격음·화면 점멸·카메라 흔들림도 재생하지 않습니다.
            health.TakeDamage(new DamageInfo(
                bleedingDamagePerSecond * Time.deltaTime,
                transform.position,
                isStatusDamage: true,
                source: DamageSource.Bleeding));
        }

        private void HandleDamage(DamageInfo damageInfo)
        {
            // 상태 이상이 만든 피해가 다시 상태 이상을 발생시키는 연쇄를 막습니다.
            // 출혈의 매 프레임 피해가 다시 골절 확률을 굴리면 한 번 다친 뒤 상태 이상이 끝없이 늘어날 수 있습니다.
            if (damageInfo.IsStatusDamage || damageInfo.Amount <= 0f) return;

            bool changed = false;
            if (!IsBleeding && ShouldApplyCondition(
                    UnityEngine.Random.value,
                    damageInfo.BleedingChance,
                    damageInfo.Amount,
                    5f))
            {
                // HUD가 이 상태를 읽어 화면 가장자리의 붉은 경고 효과를 치료 전까지 유지합니다.
                IsBleeding = true;
                changed = true;
            }

            if (!HasFracture && ShouldApplyCondition(
                    UnityEngine.Random.value,
                    damageInfo.FractureChance,
                    damageInfo.Amount,
                    8f))
            {
                // 부위별 골절을 제거했으므로 어느 위치에서 받은 피해든 설정된 확률을 통과하면 같은 골절 디버프가 됩니다.
                HasFracture = true;
                changed = true;
            }

            if (changed) ConditionChanged?.Invoke();
        }

        // 붕대 사용이 성공한 경우에만 true를 반환해 아이템 소비 코드가 빈 상태에서 붕대를 낭비하지 않게 합니다.
        public bool TreatBleeding()
        {
            if (!IsBleeding) return false;
            IsBleeding = false;
            ConditionChanged?.Invoke();
            return true;
        }

        // 부목은 단일 골절 상태를 제거합니다. 부위 체력이 없으므로 추가 회복량 매개변수도 필요하지 않습니다.
        public bool TreatFracture()
        {
            if (!HasFracture) return false;
            HasFracture = false;
            ConditionChanged?.Invoke();
            return true;
        }

        // HUD와 체력 한 줄 표시에 사용할 짧은 상태 이상 요약을 만듭니다.
        public string BuildStatusSummary()
        {
            // 두 상태의 조합은 네 가지뿐이므로 HUD가 읽을 때마다 임시 List를 만들 필요가 없습니다.
            if (IsBleeding && HasFracture) return "출혈 · 골절";
            if (IsBleeding) return "출혈";
            return HasFracture ? "골절" : "상태 정상";
        }

        // 최소 피해보다 작은 긁힘은 확률 값이 높아도 상태 이상을 만들지 않습니다.
        // 난수 값을 매개변수로 받아 EditMode 테스트가 0%, 100%와 경계값을 정확히 검사할 수 있습니다.
        public static bool ShouldApplyCondition(
            float normalizedRoll,
            float chance,
            float appliedDamage,
            float minimumDamage)
        {
            return appliedDamage >= Mathf.Max(0f, minimumDamage) &&
                   Mathf.Clamp01(normalizedRoll) < Mathf.Clamp01(chance);
        }

        private void OnValidate()
        {
            bleedingDamagePerSecond = Mathf.Max(0f, bleedingDamagePerSecond);
            fractureMovementMultiplier = Mathf.Clamp(fractureMovementMultiplier, 0.1f, 1f);
            fractureAimSpreadMultiplier = Mathf.Max(1f, fractureAimSpreadMultiplier);
            fractureReloadMultiplier = Mathf.Max(1f, fractureReloadMultiplier);
        }
    }
}
