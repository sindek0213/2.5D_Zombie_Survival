using Unity.AI.Navigation;
using UnityEngine;
using UnityEngine.AI;

namespace ZombieSurvival.AI
{
    // RaidMap 자식에 있는 바닥과 벽의 Collider를 읽어 좀비가 걸을 수 있는 NavMesh를 실행 시 생성합니다.
    // 완성된 고정 맵뿐 아니라 레이드 입장 때 조립되는 랜덤 맵도 모든 조각을 만든 뒤 RebuildNavigation을 호출해 사용할 수 있습니다.
    // 아주 먼저 실행되도록 순서를 지정해 일반 좀비의 Start보다 NavMesh 준비가 앞서도록 합니다.
    [DefaultExecutionOrder(-1000)]
    [DisallowMultipleComponent]
    public sealed class RaidNavigationSurface : MonoBehaviour
    {
        [SerializeField] private LayerMask navigationLayerMask = ~0;

        private NavMeshSurface surface;

        public bool IsNavigationReady { get; private set; }

        private void Awake()
        {
            BuildNavigation();
        }

        // 현재 RaidMap 자식 구성을 기준으로 기존 데이터를 지우고 새 NavMesh를 만듭니다.
        // 랜덤 맵 생성기는 바닥·벽·건물 배치를 모두 끝낸 뒤 이 메서드를 한 번 호출하면 새 구조가 길찾기에 반영됩니다.
        public void RebuildNavigation()
        {
            RemoveNavigationData();
            BuildNavigation();
        }

        private void BuildNavigation()
        {
            surface = GetComponent<NavMeshSurface>();
            if (surface == null) surface = gameObject.AddComponent<NavMeshSurface>();

            // RaidMap의 자식만 수집하면 플레이어와 좀비의 CharacterController가 NavMesh에 장애물 구멍으로 구워지지 않습니다.
            // Renderer 모양이 아니라 Collider를 기준으로 만들기 때문에 실제 물리 충돌 벽과 길찾기 경계가 서로 일치합니다.
            surface.collectObjects = CollectObjects.Children;
            surface.useGeometry = NavMeshCollectGeometry.PhysicsColliders;
            surface.layerMask = navigationLayerMask;
            surface.BuildNavMesh();
            IsNavigationReady = surface.navMeshData != null;
        }

        private void OnDestroy()
        {
            RemoveNavigationData();
        }

        private void RemoveNavigationData()
        {
            if (surface != null && surface.navMeshData != null) surface.RemoveData();
            IsNavigationReady = false;
        }
    }
}
