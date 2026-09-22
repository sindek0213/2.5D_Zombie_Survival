using System;
using UnityEngine;

namespace ZombieSurvival.Combat
{
    // 플레이어와 좀비가 같은 피해·회복·사망 규칙을 재사용하도록 만든 공통 체력 컴포넌트입니다.
    // UI와 AI는 값을 매 프레임 조회하는 대신 Damaged, HealthChanged와 Died 이벤트를 구독할 수 있습니다.
    public sealed class Health : MonoBehaviour, IDamageable
    {
        // 체력 HUD는 정수로 표시되므로 1보다 작은 회복은 사용자 눈에 보이지 않습니다.
        // 실제 회복량이 이 값보다 작으면 의료품을 소비하지 않습니다.
        private const float MinimumUsefulRecovery = 1f;

        [SerializeField, Min(1f)] private float maxHealth = 100f;
        [SerializeField] private bool destroyOnDeath = true;

        private IIncomingDamageModifier incomingDamageModifier;

        // UI나 AI가 체력 변화를 구독할 수 있도록 이벤트를 제공합니다.
        // Damaged는 실제로 감소한 체력과 충돌 위치를 전달하므로 피격 효과가 피해 판정을 다시 계산할 필요가 없습니다.
        public event Action<float, Vector3> Damaged;
        public event Action<float, float> HealthChanged;
        public event Action Died;

        public float CurrentHealth { get; private set; }
        public float MaxHealth => maxHealth;
        public bool IsDead { get; private set; }

        // 씬이 시작될 때 최대 체력과 선택적인 방어구 피해 조정 컴포넌트를 한 번 준비합니다.
        // 피격될 때마다 GetComponent를 반복하지 않으므로 좀비와 총알 수가 늘어도 같은 오브젝트 검색 비용이 계속 생기지 않습니다.
        private void Awake()
        {
            CurrentHealth = maxHealth;
            incomingDamageModifier = GetComponent<IIncomingDamageModifier>();
        }

        // 같은 좀비 프리팹을 일반형·빠른형·튼튼한형으로 생성한 직후 종류별 최대 체력을 적용합니다.
        // refillCurrentHealth가 true이면 현재 체력도 새 최대치로 채우며, 전투 전 설정 용도라 사망 상태도 초기화합니다.
        public void ConfigureMaximumHealth(float maximum, bool refillCurrentHealth)
        {
            maxHealth = Mathf.Max(1f, maximum);
            if (refillCurrentHealth)
            {
                CurrentHealth = maxHealth;
                IsDead = false;
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
        {
            if (IsDead || amount <= 0f) return;

            // 같은 오브젝트에 방어구 같은 피해 조정 컴포넌트가 있으면 체력을 줄이기 전에 먼저 계산합니다.
            // 방어구가 모든 피해를 막아 최종값이 0이 되면 피격 이벤트와 사망 판정도 발생시키지 않습니다.
            float finalDamage = incomingDamageModifier != null
                ? Mathf.Max(0f, incomingDamageModifier.ModifyIncomingDamage(amount, hitPoint))
                : amount;
            if (finalDamage <= 0f) return;

            float previousHealth = CurrentHealth;
            CurrentHealth = Mathf.Max(0f, CurrentHealth - finalDamage);
            float appliedDamage = previousHealth - CurrentHealth;

            // 피격 시각 효과를 체력 HUD보다 먼저 시작시키고, 넘친 피해량이 아니라 실제로 줄어든 체력만 전달합니다.
            Damaged?.Invoke(appliedDamage, hitPoint);
            HealthChanged?.Invoke(CurrentHealth, maxHealth);

            if (CurrentHealth > 0f) return;
            IsDead = true;
            Died?.Invoke();

            // 현재 표적은 단순 프로토타입이므로 사망 시 오브젝트를 제거합니다.
            if (destroyOnDeath) Destroy(gameObject);
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
