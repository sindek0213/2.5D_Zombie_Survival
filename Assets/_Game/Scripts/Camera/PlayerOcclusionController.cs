using System.Collections.Generic;
using UnityEngine;

namespace ZombieSurvival.CameraSystem
{
    // 카메라에서 플레이어 몸 중심까지 구를 쏘아 PlayerOccluder 레이어의 건물 조각을 찾습니다.
    // CameraFollow가 플레이어를 이동시킨 뒤 최신 카메라 위치를 검사하도록 실행 순서를 일반 컴포넌트보다 뒤로 지정합니다.
    [DefaultExecutionOrder(100)]
    [DisallowMultipleComponent]
    public sealed class PlayerOcclusionController : MonoBehaviour
    {
        [SerializeField] private Camera targetCamera;
        [SerializeField] private Transform player;
        // 한 줄짜리 Ray 대신 작은 구를 사용하면 얇은 천장 모서리 사이로 검사가 빠져 프레임마다 깜빡이는 상황을 줄일 수 있습니다.
        [SerializeField, Min(0f)] private float castRadius = 0.28f;
        // 발 위치를 직접 겨냥하면 바닥에 닿은 Ray가 먼저 끝날 수 있으므로 캐릭터 몸통 높이만큼 목표점을 올립니다.
        [SerializeField, Min(0f)] private float playerTargetHeight = 0.9f;
        [SerializeField] private LayerMask occluderLayers;

        // RaycastNonAlloc으로 매 프레임 배열 할당을 피합니다. 한 건물의 여러 벽과 지붕을 동시에 통과할 수 있어 여유 있게 32개를 보관합니다.
        private readonly RaycastHit[] hits = new RaycastHit[32];
        private readonly HashSet<OccluderFadeTarget> activeTargets = new HashSet<OccluderFadeTarget>();
        private readonly HashSet<OccluderFadeTarget> seenTargets = new HashSet<OccluderFadeTarget>();

        // 씬 생성기가 학교의 실제 카메라·플레이어·전용 레이어를 연결할 때 사용합니다.
        public void Configure(Camera cameraValue, Transform playerValue, LayerMask layerMaskValue)
        {
            targetCamera = cameraValue;
            player = playerValue;
            occluderLayers = layerMaskValue;
        }

        private void LateUpdate()
        {
            if (targetCamera == null || player == null || occluderLayers.value == 0)
            {
                RestoreAllTargets();
                return;
            }

            Vector3 origin = targetCamera.transform.position;
            Vector3 target = player.position + Vector3.up * playerTargetHeight;
            Vector3 offset = target - origin;
            float distance = offset.magnitude;
            if (distance <= 0.001f)
            {
                RestoreAllTargets();
                return;
            }

            seenTargets.Clear();
            int hitCount = Physics.SphereCastNonAlloc(
                origin,
                castRadius,
                offset / distance,
                hits,
                distance,
                occluderLayers,
                QueryTriggerInteraction.Ignore);

            for (int i = 0; i < hitCount; i++)
            {
                OccluderFadeTarget fadeTarget = FindFadeTarget(hits[i].collider);
                if (fadeTarget != null) seenTargets.Add(fadeTarget);
            }

            // 지난 프레임에는 가렸지만 이번 선분에는 없는 건물은 다시 선명하게 복구합니다.
            foreach (OccluderFadeTarget activeTarget in activeTargets)
            {
                if (activeTarget != null && !seenTargets.Contains(activeTarget))
                    activeTarget.SetOccluded(false);
            }

            // 같은 지붕에 Collider가 여러 개 있어도 HashSet 덕분에 컴포넌트에는 한 번만 상태를 전달합니다.
            foreach (OccluderFadeTarget seenTarget in seenTargets)
            {
                seenTarget.SetOccluded(true);
                activeTargets.Add(seenTarget);
            }

            activeTargets.RemoveWhere(targetValue => targetValue == null || !targetValue.IsOccluded);
        }

        private void OnDisable()
        {
            RestoreAllTargets();
        }

        private void RestoreAllTargets()
        {
            foreach (OccluderFadeTarget activeTarget in activeTargets)
            {
                if (activeTarget != null) activeTarget.SetOccluded(false);
            }
            activeTargets.Clear();
            seenTargets.Clear();
        }

        // Collider 자신이 아닌 여러 조각의 공통 부모에 FadeTarget을 두므로 부모 방향으로 검색합니다.
        // 정적 함수로 분리해 잘못된 레이어 오브젝트에 컴포넌트가 없을 때도 안전하게 null을 반환하는지 테스트할 수 있습니다.
        public static OccluderFadeTarget FindFadeTarget(Collider hitCollider)
        {
            return hitCollider != null ? hitCollider.GetComponentInParent<OccluderFadeTarget>() : null;
        }
    }
}
