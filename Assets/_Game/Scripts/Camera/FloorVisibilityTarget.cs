using System.Collections.Generic;
using UnityEngine;
using ZombieSurvival.Core;
using ZombieSurvival.Player;

namespace ZombieSurvival.CameraSystem
{
    // 아이템·전리품 상자·시체·좀비처럼 현재 층에서만 보여야 하는 월드 오브젝트의 Renderer를 관리합니다.
    // 건물 천장 투과와 별도로 동작하므로 1층 천장이 흐려져도 2층 아이템이나 좀비가 함께 비치는 문제를 막습니다.
    [DisallowMultipleComponent]
    public sealed class FloorVisibilityTarget : MonoBehaviour
    {
        [Header("층 표시 설정")]
        // 학교의 1층 바닥부터 2층 바닥까지의 높이입니다. 이후 층고가 다른 맵에서는 Inspector에서 이 값만 바꾸면 됩니다.
        [SerializeField, Min(0.1f)] private float floorHeight = SchoolRaidMapLayout.ClassroomSecondFloorHeight;
        // 매 프레임 Renderer 배열을 검사할 필요가 없으므로 짧은 간격으로만 플레이어 층을 다시 확인합니다.
        [SerializeField, Min(0.02f)] private float refreshInterval = 0.1f;
        // 비워 두면 이 오브젝트 아래 Renderer를 자동으로 수집합니다. 일부 메시만 숨길 때 Inspector에서 직접 지정할 수 있습니다.
        [SerializeField] private Renderer[] targetRenderers;
        [SerializeField] private Transform player;

        // 같은 씬의 모든 대상이 PlayerMovement를 반복 검색하지 않도록 첫 검색 결과를 공유합니다.
        // 씬 전환으로 플레이어가 파괴되면 Unity의 null 판정으로 다음 대상이 새 플레이어를 다시 찾습니다.
        private static Transform cachedPlayer;

        private readonly List<bool> statesBeforeHide = new List<bool>();
        private bool hiddenByFloorRule;
        private float nextRefreshTime;

        public bool IsVisibleOnCurrentFloor => !hiddenByFloorRule;
        public int TargetFloor => FloorLevelUtility.ResolveFloorLevel(transform.position.y, floorHeight);
        public int PlayerFloor => player != null
            ? FloorLevelUtility.ResolveFloorLevel(player.position.y, floorHeight)
            : TargetFloor;

        private void Awake()
        {
            ResolveRenderers();
            ResolvePlayer();
            RefreshVisibilityImmediately();
        }

        private void OnEnable()
        {
            nextRefreshTime = 0f;
            RefreshVisibilityImmediately();
        }

        private void Update()
        {
            if (Time.unscaledTime < nextRefreshTime) return;
            nextRefreshTime = Time.unscaledTime + Mathf.Max(0.02f, refreshInterval);
            RefreshVisibilityImmediately();
        }

        private void OnDisable()
        {
            // 오브젝트 풀이나 씬 전환으로 비활성화될 때 우리가 숨긴 Renderer만 원래 상태로 되돌립니다.
            // 다른 시스템이 원래부터 꺼 둔 Renderer까지 임의로 켜지 않도록 숨기기 직전 상태를 각각 저장합니다.
            RestoreRendererStates();
        }

        // 씬 생성기나 테스트가 플레이어와 층고를 명시적으로 연결할 때 사용합니다.
        public void Configure(Transform playerTransform, float mapFloorHeight)
        {
            player = playerTransform;
            if (player != null) cachedPlayer = player;
            floorHeight = Mathf.Max(0.1f, mapFloorHeight);
            RefreshVisibilityImmediately();
        }

        public void RefreshVisibilityImmediately()
        {
            ResolvePlayer();
            ResolveRenderers();
            if (player == null)
            {
                RestoreRendererStates();
                return;
            }

            bool shouldShow = ShouldShowOnFloor(transform.position.y, player.position.y, floorHeight);
            if (shouldShow) RestoreRendererStates();
            else HideRenderers();
        }

        private void ResolvePlayer()
        {
            if (player != null) return;
            if (cachedPlayer != null)
            {
                player = cachedPlayer;
                return;
            }

            PlayerMovement movement = FindObjectOfType<PlayerMovement>();
            cachedPlayer = movement != null ? movement.transform : null;
            player = cachedPlayer;
        }

        private void ResolveRenderers()
        {
            if (targetRenderers != null && targetRenderers.Length > 0) return;
            targetRenderers = GetComponentsInChildren<Renderer>(true);
        }

        private void HideRenderers()
        {
            if (hiddenByFloorRule) return;

            statesBeforeHide.Clear();
            foreach (Renderer targetRenderer in targetRenderers)
            {
                bool wasEnabled = targetRenderer != null && targetRenderer.enabled;
                statesBeforeHide.Add(wasEnabled);
                if (targetRenderer != null) targetRenderer.enabled = false;
            }
            hiddenByFloorRule = true;
        }

        private void RestoreRendererStates()
        {
            if (!hiddenByFloorRule) return;

            for (int i = 0; i < targetRenderers.Length; i++)
            {
                Renderer targetRenderer = targetRenderers[i];
                if (targetRenderer != null && i < statesBeforeHide.Count)
                    targetRenderer.enabled = statesBeforeHide[i];
            }
            statesBeforeHide.Clear();
            hiddenByFloorRule = false;
        }

        // 월드 Y좌표 두 개만으로 같은 층인지 판단하는 순수 함수라 실제 Camera나 Renderer 없이도 자동 테스트할 수 있습니다.
        public static bool ShouldShowOnFloor(float targetWorldY, float playerWorldY, float mapFloorHeight)
        {
            return FloorLevelUtility.ResolveFloorLevel(targetWorldY, mapFloorHeight) ==
                   FloorLevelUtility.ResolveFloorLevel(playerWorldY, mapFloorHeight);
        }

        // Enter Play Mode Options에서 도메인 리로드를 꺼도 이전 플레이어 참조가 다음 실행에 남지 않게 초기화합니다.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticPlayerReference() => cachedPlayer = null;
    }
}
