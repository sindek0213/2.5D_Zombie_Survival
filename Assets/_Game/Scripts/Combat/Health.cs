using System;
using UnityEngine;

namespace ZombieSurvival.Combat
{
    // 플레이어와 좀비가 같은 피해·회복·사망 규칙을 재사용하도록 만든 공통 체력 컴포넌트입니다.
    // UI와 AI는 값을 매 프레임 조회하는 대신 HealthChanged와 Died 이벤트를 구독할 수 있습니다.
    public sealed class Health : MonoBehaviour, IDamageable
    {
        [SerializeField, Min(1f)] private float maxHealth = 100f;
        [SerializeField] private bool destroyOnDeath = true;

        // UI나 AI가 체력 변화를 구독할 수 있도록 이벤트를 제공합니다.
        public event Action<float, float> HealthChanged;
        public event Action Died;

        public float CurrentHealth { get; private set; }
        public float MaxHealth => maxHealth;
        public bool IsDead { get; private set; }

        // 씬이 시작될 때 최대 체력으로 초기화합니다.
        private void Awake() => CurrentHealth = maxHealth;

        // 이미 사망했거나 피해량이 0 이하이면 중복 사망과 잘못된 회복성 피해를 막기 위해 무시합니다.
        // 체력은 0 아래로 내려가지 않게 제한하고, 변경 이벤트를 먼저 보낸 뒤 0이면 사망 이벤트를 한 번 발생시킵니다.
        public void TakeDamage(float amount, Vector3 hitPoint)
        {
            if (IsDead || amount <= 0f) return;

            CurrentHealth = Mathf.Max(0f, CurrentHealth - amount);
            HealthChanged?.Invoke(CurrentHealth, maxHealth);

            if (CurrentHealth > 0f) return;
            IsDead = true;
            Died?.Invoke();

            // 현재 표적은 단순 프로토타입이므로 사망 시 오브젝트를 제거합니다.
            if (destroyOnDeath) Destroy(gameObject);
        }

        // 사망 상태, 잘못된 회복량 또는 이미 최대 체력인 경우 false를 반환해 소비 아이템이 줄지 않게 합니다.
        // 성공하면 최대 체력을 넘지 않도록 Clamp하고 HUD가 갱신되도록 HealthChanged 이벤트를 발생시킵니다.
        public bool Heal(float amount)
        {
            if (IsDead || amount <= 0f || CurrentHealth >= maxHealth) return false;
            CurrentHealth = Mathf.Min(maxHealth, CurrentHealth + amount);
            HealthChanged?.Invoke(CurrentHealth, maxHealth);
            return true;
        }
    }
}
