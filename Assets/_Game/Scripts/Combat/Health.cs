using System;
using UnityEngine;

namespace ZombieSurvival.Combat
{
    // 플레이어와 좀비가 같은 피해·회복·사망 규칙을 재사용하도록 만든 공통 체력 컴포넌트입니다.
    // UI와 AI는 값을 매 프레임 조회하는 대신 Damaged, HealthChanged와 Died 이벤트를 구독할 수 있습니다.
    public sealed class Health : MonoBehaviour, IDamageable, IDetailedDamageable
    {
        // 체력 HUD는 정수로 표시되므로 1보다 작은 회복은 사용자 눈에 보이지 않습니다.
        // 실제 회복량이 이 값보다 작으면 의료품을 소비하지 않습니다.
        private const float MinimumUsefulRecovery = 1f;

        [SerializeField, Min(1f)] private float maxHealth = 100f;
        [SerializeField] private bool destroyOnDeath = true;

        // 방탄복과 시간제 피해 감소 버프가 동시에 존재할 수 있으므로 한 개가 아니라 같은 오브젝트의 모든 MonoBehaviour를 순서대로 검사합니다.
        private MonoBehaviour[] damageModifierComponents = Array.Empty<MonoBehaviour>();
        // 돌진처럼 짧은 무적을 사용하는 기능은 종료 시점을 저장합니다.
        // 여러 무적 요청이 겹치면 더 늦은 종료 시각을 유지해 새 요청이 기존 무적을 짧게 잘라 버리지 않게 합니다.
        private float invulnerableUntil;
        // 구르기는 동작이 중단되면 즉시 해제해야 하므로 시간제 무적과 별도의 상태로 관리합니다.
        // 두 종류를 분리하면 인벤토리를 열어 구르기를 취소하더라도 다른 기능이 준 시간제 무적은 지워지지 않습니다.
        private bool evasionInvulnerable;

        // UI나 AI가 체력 변화를 구독할 수 있도록 이벤트를 제공합니다.
        // Damaged는 직접 공격의 피격 연출 전용입니다. 출혈 같은 상태 피해는 이 이벤트를 발생시키지 않습니다.
        // 모든 피해의 실제 감소량은 DamageResolved, 체력 수치 변화는 HealthChanged로 전달해 통계·HUD·사망 계산을 유지합니다.
        public event Action<float, Vector3> Damaged;
        public event Action<DamageInfo> DamageResolved;
        public event Action<float, float> HealthChanged;
        public event Action Died;
        // 사망한 Health와 마지막 피해 정보를 함께 전달해 레이드가 처치 무기를 정확히 기록할 수 있게 합니다.
        // 기존 Died 이벤트는 AI·전리품 코드 호환을 위해 그대로 유지합니다.
        public event Action<Health, DamageInfo> DiedDetailed;

        public float CurrentHealth { get; private set; }
        public float MaxHealth => maxHealth;
        public bool IsDead { get; private set; }
        public bool IsInvulnerable => !IsDead && (evasionInvulnerable || Time.time < invulnerableUntil);
        // RaidSession은 플레이어 오브젝트가 사라지기 전에 이 문자열을 결과 데이터로 복사합니다.
        // 평상시에는 비어 있고 체력이 0이 된 정확한 피해 요청을 기준으로 한 번만 결정됩니다.
        public string DeathCauseDescription { get; private set; } = string.Empty;

        // duration초 동안 모든 피해를 무시합니다. 0 이하의 요청과 사망 뒤 요청은 상태를 바꾸지 않습니다.
        // 무적 여부는 TakeDamage의 가장 앞에서 검사하므로 방어구 내구도와 상태 이상도 함께 소모되지 않습니다.
        public void BeginInvulnerability(float duration)
        {
            if (IsDead || duration <= 0f) return;
            invulnerableUntil = Mathf.Max(invulnerableUntil, Time.time + duration);
        }

        // PlayerMovement가 구르기 시작·완료·조작 잠금 시 호출합니다. 실제 피해 처리는 기존 IsInvulnerable 검사 하나로 통일합니다.
        public void SetEvasionInvulnerability(bool value) => evasionInvulnerable = value && !IsDead;

        // 씬이 시작될 때 최대 체력과 선택적인 방어구 피해 조정 컴포넌트를 한 번 준비합니다.
        // 피격될 때마다 GetComponent를 반복하지 않으므로 좀비와 총알 수가 늘어도 같은 오브젝트 검색 비용이 계속 생기지 않습니다.
        private void Awake()
        {
            CurrentHealth = maxHealth;
            RefreshDamageModifiers();
        }

        // 에디터 업그레이드나 테스트가 Awake 뒤 피해 조정 컴포넌트를 추가한 경우에도 목록을 명시적으로 다시 만들 수 있습니다.
        public void RefreshDamageModifiers() => damageModifierComponents = GetComponents<MonoBehaviour>();

        // 같은 좀비 프리팹을 일반형·빠른형·튼튼한형으로 생성한 직후 종류별 최대 체력을 적용합니다.
        // refillCurrentHealth가 true이면 현재 체력도 새 최대치로 채우며, 전투 전 설정 용도라 사망 상태도 초기화합니다.
        public void ConfigureMaximumHealth(float maximum, bool refillCurrentHealth)
        {
            maxHealth = Mathf.Max(1f, maximum);
            if (refillCurrentHealth)
            {
                CurrentHealth = maxHealth;
                IsDead = false;
                DeathCauseDescription = string.Empty;
            }
            else
            {
                CurrentHealth = Mathf.Min(CurrentHealth, maxHealth);
            }

            HealthChanged?.Invoke(CurrentHealth, maxHealth);
        }

        // 이미 사망했거나 피해량이 0 이하이면 중복 사망과 잘못된 회복성 피해를 막기 위해 무시합니다.
        // 체력은 0 아래로 내려가지 않게 제한하고, 변경 이벤트를 먼저 보낸 뒤 0이면 사망 이벤트를 한 번 발생시킵니다.
        public void TakeDamage(float amount, Vector3 hitPoint)
            => TakeDamage(new DamageInfo(amount, hitPoint));

        // 상세 피해는 명중 부위 배율을 적용한 뒤 방어구의 관통 판정을 거칩니다.
        // 명중 부위는 적 머리 사격 같은 전투 피해 배율에만 사용하고 플레이어에게 별도 부위 체력을 만들지는 않습니다.
        public void TakeDamage(DamageInfo damageInfo)
        {
            if (IsDead || IsInvulnerable || damageInfo.Amount <= 0f) return;

            float bodyMultiplier = damageInfo.IsStatusDamage
                ? 1f
                : GetBodyPartDamageMultiplier(damageInfo.BodyPart);
            DamageInfo adjustedInfo = damageInfo.WithAmount(damageInfo.Amount * bodyMultiplier);

            // 방어구와 버프처럼 여러 피해 조정기를 컴포넌트 순서대로 연결합니다.
            // 한 컴포넌트가 상세·단순 인터페이스를 모두 구현하면 상세 버전만 호출해 같은 감소가 두 번 적용되지 않게 합니다.
            float finalDamage = adjustedInfo.Amount;
            if (damageModifierComponents == null || damageModifierComponents.Length == 0)
                RefreshDamageModifiers();
            foreach (MonoBehaviour component in damageModifierComponents)
            {
                if (component == null || component == this) continue;
                DamageInfo currentInfo = adjustedInfo.WithAmount(finalDamage);
                if (component is IDetailedIncomingDamageModifier detailedModifier)
                    finalDamage = Mathf.Max(0f, detailedModifier.ModifyIncomingDamage(currentInfo));
                else if (component is IIncomingDamageModifier simpleModifier)
                    finalDamage = Mathf.Max(0f, simpleModifier.ModifyIncomingDamage(finalDamage, adjustedInfo.HitPoint));
                if (finalDamage <= 0f) break;
            }
            if (finalDamage <= 0f) return;

            float previousHealth = CurrentHealth;
            CurrentHealth = Mathf.Max(0f, CurrentHealth - finalDamage);
            float appliedDamage = previousHealth - CurrentHealth;

            // 직접 맞았을 때만 화면 점멸·스프라이트 점멸·카메라 흔들림·피격음을 재생합니다.
            // 출혈의 체력 감소는 아래 두 이벤트로 계속 전달되지만, 시각 경고는 PlayerConditionHud의 화면 테두리로 표시합니다.
            if (!adjustedInfo.IsStatusDamage) Damaged?.Invoke(appliedDamage, adjustedInfo.HitPoint);
            DamageResolved?.Invoke(adjustedInfo.WithAmount(appliedDamage));
            HealthChanged?.Invoke(CurrentHealth, maxHealth);

            if (CurrentHealth > 0f) return;
            IsDead = true;
            // Died 이벤트를 먼저 보내면 RaidSession이 아직 비어 있는 원인을 읽을 수 있으므로 이벤트 직전에 원인을 확정합니다.
            DeathCauseDescription = BuildDeathCauseDescription(adjustedInfo.WithAmount(appliedDamage));
            DiedDetailed?.Invoke(this, adjustedInfo.WithAmount(appliedDamage));
            Died?.Invoke();

            // 현재 표적은 단순 프로토타입이므로 사망 시 오브젝트를 제거합니다.
            if (destroyOnDeath) Destroy(gameObject);
        }

        public static float GetBodyPartDamageMultiplier(BodyPart bodyPart)
        {
            switch (bodyPart)
            {
                case BodyPart.Head: return 1.6f;
                case BodyPart.Arms: return 0.85f;
                case BodyPart.Legs: return 0.8f;
                default: return 1f;
            }
        }

        // 명중 높이를 대상 전체 높이에 대한 비율로 바꿔 머리·몸통·팔·다리 중 하나를 고릅니다.
        // 이 결과는 총격 피해 배율 계산용일 뿐 PlayerCondition에 부위별 손상 수치를 저장하지 않습니다.
        public static BodyPart ResolveBodyPart(Transform target, Vector3 hitPoint)
        {
            if (target == null) return BodyPart.Torso;
            CharacterController controller = target.GetComponent<CharacterController>();
            float height = controller != null ? Mathf.Max(0.5f, controller.height) : 2f;
            Vector3 local = target.InverseTransformPoint(hitPoint);
            float normalizedHeight = Mathf.Clamp01(local.y / height);
            if (normalizedHeight >= 0.78f) return BodyPart.Head;
            if (normalizedHeight <= 0.34f) return BodyPart.Legs;
            float armThreshold = controller != null ? Mathf.Max(0.2f, controller.radius * 0.6f) : 0.3f;
            return Mathf.Abs(local.x) >= armThreshold ? BodyPart.Arms : BodyPart.Torso;
        }

        // 사망을 일으킨 공격 종류와 맞은 부위를 사용자가 바로 이해할 수 있는 짧은 한국어 문장으로 바꿉니다.
        // 출혈로 사망한 경우에는 명중 부위가 없으므로 공격 치명상 대신 별도 원인을 표시합니다.
        public static string BuildDeathCauseDescription(DamageInfo damageInfo)
        {
            if (damageInfo.Source == DamageSource.Bleeding) return "과다 출혈로 사망";
            if (damageInfo.IsStatusDamage) return "상태 이상 누적으로 사망";

            string bodyPart = GetBodyPartDisplayName(damageInfo.BodyPart);
            switch (damageInfo.Source)
            {
                case DamageSource.ZombieAttack: return $"좀비 공격으로 인한 {bodyPart} 치명상";
                case DamageSource.Gunfire: return $"총격으로 인한 {bodyPart} 치명상";
                case DamageSource.Melee: return $"근접 공격으로 인한 {bodyPart} 치명상";
                default: return $"{bodyPart} 치명상";
            }
        }

        private static string GetBodyPartDisplayName(BodyPart bodyPart)
        {
            switch (bodyPart)
            {
                case BodyPart.Head: return "머리";
                case BodyPart.Arms: return "팔";
                case BodyPart.Legs: return "다리";
                default: return "몸통";
            }
        }

        // 사망 상태이거나 실제 회복 가능한 양이 1보다 작으면 false를 반환해 의료품이 의미 없이 줄지 않게 합니다.
        // HUD에는 체력이 정수로 표시되므로 99.9가 100으로 보이는 상황에서도 구급상자를 낭비하지 않습니다.
        // 성공하면 최대 체력을 넘지 않도록 제한하고 HUD가 갱신되도록 HealthChanged 이벤트를 발생시킵니다.
        public bool Heal(float amount)
        {
            float actualRecovery = Mathf.Min(Mathf.Max(0f, amount), maxHealth - CurrentHealth);
            if (IsDead || actualRecovery < MinimumUsefulRecovery) return false;

            CurrentHealth = Mathf.Min(maxHealth, CurrentHealth + actualRecovery);
            HealthChanged?.Invoke(CurrentHealth, maxHealth);
            return true;
        }
    }
}
