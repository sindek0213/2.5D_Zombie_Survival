using UnityEngine;
using ZombieSurvival.Combat;

namespace ZombieSurvival.AI
{
    // ZombieBrain이 Attack 상태라고 판단했을 때 호출되어 일정 간격으로 근접 피해를 적용합니다.
    // 공격 거리 판정은 Brain이 담당하고 이 클래스는 쿨다운과 IDamageable 호출에만 책임을 둡니다.
    public sealed class ZombieAttack : MonoBehaviour
    {
        [SerializeField, Min(0f)] private float damage = 12f;
        [SerializeField, Min(0.05f)] private float attackInterval = 1f;

        private float nextAttackTime;

        // 스폰된 좀비 종류의 한 번 공격 피해량을 설정합니다.
        // 공격 주기와 쿨다운 계산은 그대로 유지되므로 종류 데이터는 피해량만 책임집니다.
        public void ConfigureDamage(float newDamage)
        {
            damage = Mathf.Max(0f, newDamage);
        }

        // target이 없거나 Time.time이 nextAttackTime보다 작으면 아직 다음 공격 시점이 아니므로 실패합니다.
        // 부모 방향으로 IDamageable을 검색해 캐릭터의 자식 Transform이 목표로 전달되어도 루트 Health를 찾습니다.
        // 실제 피해가 적용됐을 때만 다음 공격 가능 시간을 갱신합니다.
        public bool TryAttack(Transform target)
        {
            if (target == null || Time.time < nextAttackTime) return false;

            IDamageable damageable = target.GetComponentInParent<IDamageable>();
            if (damageable == null) return false;

            nextAttackTime = Time.time + attackInterval;
            damageable.TakeDamage(damage, target.position);
            return true;
        }

        // 공격 간격 조건을 EditMode에서 검증하기 위한 순수 함수입니다.
        public static bool IsCooldownReady(float currentTime, float nextAllowedTime)
        {
            return currentTime >= nextAllowedTime;
        }
    }
}
