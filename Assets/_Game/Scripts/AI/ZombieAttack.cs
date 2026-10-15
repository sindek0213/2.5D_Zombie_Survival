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

        [Header("공격 경로 판정")]
        // 좀비와 플레이어 몸통 높이 사이로 Ray를 쏘아 벽과 층 바닥이 공격을 막는지 확인합니다.
        [SerializeField, Min(0f)] private float attackOriginHeight = 0.9f;
        [SerializeField, Min(0f)] private float targetBodyHeight = 0.9f;
        // 계단 중간에서 약간 높이가 다른 상대는 공격할 수 있지만, 1층과 2층처럼 큰 높이 차이가 있으면 공격하지 못하게 합니다.
        [SerializeField, Min(0.1f)] private float maximumAttackHeightDifference = 1.25f;
        [SerializeField] private LayerMask attackObstructionMask = ~0;

        private float nextAttackTime;
        // RaycastNonAlloc 결과를 재사용해 공격 가능 여부를 확인할 때마다 새 배열이 생기는 메모리 할당을 막습니다.
        private readonly RaycastHit[] attackHitBuffer = new RaycastHit[16];

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

            IDetailedDamageable detailedDamageable = target.GetComponentInParent<IDetailedDamageable>();
            IDamageable damageable = target.GetComponentInParent<IDamageable>();
            if (detailedDamageable == null && damageable == null) return false;
            if (!CanReachTarget(target)) return false;

            nextAttackTime = Time.time + attackInterval;
            if (detailedDamageable != null)
            {
                // 근접 공격은 관통력이 없지만 출혈과 단일 골절 디버프가 발생할 수 있습니다.
                // BodyPart는 Health의 피해 배율 전달 형식 때문에 몸통으로 보내며 PlayerCondition에는 부위 손상을 저장하지 않습니다.
                detailedDamageable.TakeDamage(new DamageInfo(
                    damage,
                    target.position,
                    BodyPart.Torso,
                    0,
                    0.28f,
                    0.1f,
                    false,
                    DamageSource.ZombieAttack));
            }
            else damageable.TakeDamage(damage, target.position);
            return true;
        }

        // 목표와 실제 근접 공격을 주고받을 수 있는 높이인지, 벽이나 바닥이 사이를 가리지 않는지 검사합니다.
        // ZombieBrain도 이 결과를 사용해 막혀 있을 때 Attack에서 Chase로 돌아가므로 좀비가 벽 앞에서 영원히 멈추지 않습니다.
        public bool CanReachTarget(Transform target)
        {
            if (target == null) return false;
            if (Mathf.Abs(target.position.y - transform.position.y) > maximumAttackHeightDifference) return false;

            Vector3 origin = transform.position + Vector3.up * attackOriginHeight;
            Vector3 targetPoint = target.position + Vector3.up * targetBodyHeight;
            Vector3 offset = targetPoint - origin;
            float distance = offset.magnitude;
            if (distance <= 0.001f) return true;

            int hitCount = Physics.RaycastNonAlloc(
                origin,
                offset / distance,
                attackHitBuffer,
                distance,
                attackObstructionMask,
                QueryTriggerInteraction.Ignore);

            Collider closestCollider = null;
            float closestDistance = float.PositiveInfinity;
            for (int i = 0; i < hitCount; i++)
            {
                Collider hitCollider = attackHitBuffer[i].collider;
                if (hitCollider == null || IsPartOfTransform(hitCollider.transform, transform)) continue;
                if (attackHitBuffer[i].distance >= closestDistance) continue;

                closestDistance = attackHitBuffer[i].distance;
                closestCollider = hitCollider;
            }

            // 목표에 Collider가 없는 단순 개발 오브젝트는 중간 장애물이 없으면 공격을 허용합니다.
            // Collider가 있다면 가장 먼저 맞은 물체가 목표 자신이나 목표의 자식일 때만 공격할 수 있습니다.
            return closestCollider == null || IsPartOfTransform(closestCollider.transform, target);
        }

        // CharacterController가 루트에 있거나 피격 Collider가 자식에 있는 두 구조를 모두 같은 캐릭터로 인식합니다.
        private static bool IsPartOfTransform(Transform candidate, Transform root)
        {
            return candidate != null && root != null &&
                   (candidate == root || candidate.IsChildOf(root) || root.IsChildOf(candidate));
        }

        // 공격 간격 조건을 EditMode에서 검증하기 위한 순수 함수입니다.
        public static bool IsCooldownReady(float currentTime, float nextAllowedTime)
        {
            return currentTime >= nextAllowedTime;
        }
    }
}
