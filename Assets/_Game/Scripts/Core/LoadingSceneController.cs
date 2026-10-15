using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using ZombieSurvival.Hideout;
using ZombieSurvival.UI;

namespace ZombieSurvival.Core
{
    // 모든 주요 게임 씬 전환을 실제 비동기 로드로 처리하는 Loading 씬 컨트롤러입니다.
    // Title은 목적지만 지정하고, 이 클래스가 저장 준비·진행률 표시·씬 활성화를 순서대로 완료합니다.
    public sealed class LoadingSceneController : MonoBehaviour
    {
        [SerializeField] private LoadingScreenHud loadingHud;
        [SerializeField] private string defaultTargetSceneName = GameSceneNavigator.HideoutSceneName;
        // 작은 씬도 이미지와 로딩 바를 인식할 수 있도록 최소 표시 시간을 두되 실제 로드가 더 길면 완료될 때까지 계속 기다립니다.
        [SerializeField, Min(0f)] private float minimumDisplayDuration = 1.2f;
        [SerializeField, Min(0f)] private float completionHoldDuration = 0.12f;

        public string TargetSceneName { get; private set; }
        public bool SaveLoadSucceeded { get; private set; }
        public float DisplayedProgress { get; private set; }

        // 에디터 생성기가 같은 씬의 LoadingCanvas를 명시적으로 연결할 때 사용합니다.
        public void Configure(LoadingScreenHud hud)
        {
            loadingHud = hud;
        }

        private void Start()
        {
            if (loadingHud == null) loadingHud = FindObjectOfType<LoadingScreenHud>();
            StartCoroutine(LoadTargetSceneRoutine());
        }

        private IEnumerator LoadTargetSceneRoutine()
        {
            // 일시정지 상태에서 로딩 씬으로 들어왔더라도 진행 애니메이션과 다음 씬이 멈추지 않도록 정상 시간 배율로 복구합니다.
            Time.timeScale = 1f;
            TargetSceneName = GameSceneNavigator.ConsumeLoadingTarget(defaultTargetSceneName);

            if (string.IsNullOrWhiteSpace(TargetSceneName) ||
                TargetSceneName == GameSceneNavigator.LoadingSceneName ||
                !Application.CanStreamedLevelBeLoaded(TargetSceneName))
            {
                loadingHud?.SetProgress(0f, $"이동할 씬 '{TargetSceneName}'을 찾을 수 없습니다. Build Settings를 확인해 주세요.");
                yield break;
            }

            // 한 실행에서 이미 저장을 읽었다면 즉시 기존 결과를 돌려주므로 레이드 전환마다 파일을 반복해서 읽지 않습니다.
            SaveLoadSucceeded = HideoutSaveService.LoadDefaultIfNeeded();
            string preparingMessage = SaveLoadSucceeded
                ? BuildLoadingMessage(TargetSceneName)
                : "저장 데이터를 복구하지 못했습니다. 현재 메모리 상태로 이동합니다.";
            loadingHud?.SetProgress(0.04f, preparingMessage);

            AsyncOperation operation = SceneManager.LoadSceneAsync(TargetSceneName);
            if (operation == null)
            {
                loadingHud?.SetProgress(0f, "비동기 씬 로드를 시작하지 못했습니다.");
                yield break;
            }

            // Unity의 비동기 씬 로드는 활성화 직전 0.9에서 멈춥니다. 화면 연출이 끝날 때까지 활성화를 잠가 로딩 바가 갑자기 사라지지 않게 합니다.
            operation.allowSceneActivation = false;
            float elapsed = 0f;
            DisplayedProgress = 0.04f;

            while (true)
            {
                elapsed += Time.unscaledDeltaTime;
                float actualProgress = NormalizeSceneProgress(operation.progress);
                // 저장 준비 상태로 먼저 표시한 4%보다 바가 뒤로 물러나지 않게 하고, 실제 진행률까지 일정한 속도로 따라가게 합니다.
                // 프레임마다 최소 12%씩 건너뛰던 방식은 작은 씬에서 거의 즉시 가득 차 보이므로 시간 기반 속도로 계산합니다.
                float targetProgress = Mathf.Max(0.04f, actualProgress);
                DisplayedProgress = Mathf.MoveTowards(
                    DisplayedProgress,
                    targetProgress,
                    Mathf.Max(0.001f, Time.unscaledDeltaTime * 0.9f));
                loadingHud?.SetProgress(DisplayedProgress, preparingMessage);

                bool loadPrepared = operation.progress >= 0.9f;
                bool minimumTimePassed = elapsed >= minimumDisplayDuration;
                if (loadPrepared && minimumTimePassed && DisplayedProgress >= 0.999f) break;
                yield return null;
            }

            DisplayedProgress = 1f;
            loadingHud?.SetProgress(1f, "준비 완료");
            if (completionHoldDuration > 0f)
                yield return new WaitForSecondsRealtime(completionHoldDuration);

            // 목적지를 지운 뒤 씬을 활성화해야 다음 전환이 이전 목적지를 잘못 재사용하지 않습니다.
            GameSceneNavigator.CompleteLoadingTransition();
            operation.allowSceneActivation = true;
        }

        // Unity가 보고하는 0~0.9 준비 진행률을 사용자에게 표시할 0~1 범위로 변환합니다.
        public static float NormalizeSceneProgress(float unityProgress)
        {
            return Mathf.Clamp01(unityProgress / 0.9f);
        }

        private static string BuildLoadingMessage(string targetSceneName)
        {
            if (GameSceneNavigator.IsRaidScene(targetSceneName)) return "레이드 지역을 준비하는 중입니다...";
            if (targetSceneName == GameSceneNavigator.HideoutSceneName) return "거점을 준비하는 중입니다...";
            return "다음 지역을 준비하는 중입니다...";
        }
    }
}
