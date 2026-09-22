using UnityEngine;

namespace ZombieSurvival.Combat
{
    // Hitscan 무기가 한 방향으로 발사됐을 때 자기 몸을 제외한 가장 가까운 Collider를 찾는 물리 검색 도우미입니다.
    // MonoBehaviour가 아니므로 Player에 새 컴포넌트를 붙이지 않으며 PlayerCombat 한 개마다 재사용 명중 배열 하나만 보관합니다.
    internal sealed class HitscanRaycaster
    {
        // 일반 레이드 한 발의 선 위에 32개보다 많은 Collider가 겹치는 경우는 드물어 이 고정 배열로 대부분의 발사 할당을 없앱니다.
        // 배열이 가득 찬 예외 상황에서는 RaycastAll로 다시 확인해 가까운 대상을 놓치지 않습니다.
        private const int HitBufferSize = 32;

        private readonly RaycastHit[] hitBuffer = new RaycastHit[HitBufferSize];

        // 단일 Raycast는 첫 Collider가 플레이어 자신의 몸이면 그 뒤의 적이나 벽을 확인할 수 없습니다.
        // 사거리 안 후보를 모두 검사하고 shooterRoot와 그 자식을 건너뛴 뒤 가장 가까운 유효 명중을 반환합니다.
        internal bool TryGetFirstNonSelfHit(
            Transform shooterRoot,
            Vector3 origin,
            Vector3 direction,
            float range,
            LayerMask hitMask,
            out RaycastHit validHit)
        {
            validHit = default;
            if (shooterRoot == null || direction.sqrMagnitude <= 0.0001f || range <= 0f) return false;

            int hitCount = Physics.RaycastNonAlloc(
                origin,
                direction,
                hitBuffer,
                range,
                hitMask,
                QueryTriggerInteraction.Ignore);

            RaycastHit[] hits = hitBuffer;
            if (hitCount == hitBuffer.Length)
            {
                // 버퍼가 가득 차면 더 가까운 충돌체가 배열 밖에 남았을 가능성이 있으므로 이 드문 경우만 전체 결과를 다시 받습니다.
                // 일반 발사에서는 재사용 배열을 쓰고, 복잡한 장면에서도 정확도를 희생하지 않는 안전장치입니다.
                hits = Physics.RaycastAll(
                    origin,
                    direction,
                    range,
                    hitMask,
                    QueryTriggerInteraction.Ignore);
                hitCount = hits.Length;
            }

            // Raycast 결과 순서는 보장되지 않으므로 distance를 비교해 가장 가까운 유효 후보를 직접 선택합니다.
            float nearestDistance = float.PositiveInfinity;
            bool foundHit = false;
            for (int i = 0; i < hitCount; i++)
            {
                RaycastHit hit = hits[i];
                Transform hitTransform = hit.collider != null ? hit.collider.transform : null;
                if (hitTransform == null || hitTransform == shooterRoot || hitTransform.IsChildOf(shooterRoot))
                    continue;

                if (hit.distance >= nearestDistance) continue;
                nearestDistance = hit.distance;
                validHit = hit;
                foundHit = true;
            }

            return foundHit;
        }
    }
}
