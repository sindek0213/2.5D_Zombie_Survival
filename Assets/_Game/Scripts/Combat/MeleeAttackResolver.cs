using System;
using System.Collections.Generic;
using UnityEngine;

namespace ZombieSurvival.Combat
{
    // 한 번의 근접 판정이 끝난 위치, 적중 여부와 피해를 준 고유 대상 수를 반환합니다.
    // PlayerCombat은 이 결과만 읽어 HUD 문구와 시각 이벤트를 발생시키며 물리 검사 세부 규칙을 알 필요가 없습니다.
    public readonly struct MeleeAttackResult
    {
        public readonly Vector3 EndPoint;
        public readonly bool DamagedTarget;
        public readonly int DamagedTargetCount;

        public MeleeAttackResult(Vector3 endPoint, bool damagedTarget, int damagedTargetCount)
        {
            EndPoint = endPoint;
            DamagedTarget = damagedTarget;
            DamagedTargetCount = Mathf.Max(0, damagedTargetCount);
        }
    }

    // 근접 공격의 SphereCast, 자기 몸 제외, 벽 차단, 다중 Collider 중복 제거와 최대 대상 수 제한을 전담합니다.
    // MonoBehaviour가 아니므로 플레이어 프리팹에 컴포넌트를 추가하지 않고 PlayerCombat이 한 개 인스턴스를 재사용합니다.
    public sealed class MeleeAttackResolver
    {
        private readonly HashSet<int> damagedTargets = new HashSet<int>();

        public MeleeAttackResult Resolve(
            Transform attacker,
            Vector3 origin,
            Vector3 direction,
            float range,
            float hitRadius,
            int maximumTargets,
            float damage,
            float knockbackForce,
            int armorPenetration,
            string sourceId,
            LayerMask hitMask)
        {
            float safeRange = Mathf.Max(0.1f, range);
            float safeRadius = Mathf.Max(0.05f, hitRadius);
            int safeMaximumTargets = Mathf.Max(1, maximumTargets);
            Vector3 safeDirection = direction;
            safeDirection.y = 0f;
            if (safeDirection.sqrMagnitude <= 0.001f)
                safeDirection = attacker != null ? attacker.forward : Vector3.forward;
            safeDirection.Normalize();

            Vector3 endPoint = origin + safeDirection * safeRange;
            int damagedCount = 0;
            damagedTargets.Clear();

            RaycastHit[] hits = Physics.SphereCastAll(
                origin,
                safeRadius,
                safeDirection,
                safeRange,
                hitMask,
                QueryTriggerInteraction.Ignore);
            Array.Sort(hits, (left, right) => left.distance.CompareTo(right.distance));

            foreach (RaycastHit hit in hits)
            {
                if (hit.collider == null || IsPartOfAttacker(attacker, hit.collider.transform)) continue;

                endPoint = hit.point;
                IDetailedDamageable detailed = hit.collider.GetComponentInParent<IDetailedDamageable>();
                IDamageable simple = hit.collider.GetComponentInParent<IDamageable>();
                Component targetComponent = detailed as Component ?? simple as Component;
                int targetId = targetComponent != null
                    ? targetComponent.GetInstanceID()
                    : hit.collider.transform.root.GetInstanceID();

                // 한 적의 머리와 몸 Collider가 각각 검출돼도 첫 번째 피해 뒤 나머지는 건너뛰며, 벽으로 잘못 판단하지도 않습니다.
                if ((detailed != null || simple != null) && !damagedTargets.Add(targetId)) continue;

                bool damagedThisHit = false;
                if (detailed != null)
                {
                    detailed.TakeDamage(new DamageInfo(
                        damage,
                        hit.point,
                        BodyPart.Torso,
                        armorPenetration,
                        source: DamageSource.Melee,
                        sourceId: sourceId));
                    damagedThisHit = true;
                }
                else if (simple != null)
                {
                    simple.TakeDamage(damage, hit.point);
                    damagedThisHit = true;
                }

                if (damagedThisHit)
                {
                    damagedCount++;
                    IKnockbackReceiver knockback = hit.collider.GetComponentInParent<IKnockbackReceiver>();
                    knockback?.ApplyKnockback(safeDirection * Mathf.Max(0f, knockbackForce));
                }

                // 피해를 받을 수 없는 첫 충돌체는 벽이나 단단한 상자로 취급하고, 피해 대상은 설정된 수까지 계속 관통해 검사합니다.
                if (!damagedThisHit || damagedCount >= safeMaximumTargets) break;
            }

            return new MeleeAttackResult(endPoint, damagedCount > 0, damagedCount);
        }

        private static bool IsPartOfAttacker(Transform attacker, Transform candidate)
            => attacker != null && candidate != null &&
               (candidate == attacker || candidate.IsChildOf(attacker));
    }
}
