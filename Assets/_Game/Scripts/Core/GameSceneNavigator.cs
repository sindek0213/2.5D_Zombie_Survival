using System;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ZombieSurvival.Core
{
    // 게임 진행에 사용되는 씬 이름을 한곳에서 관리합니다.
    // 여러 컨트롤러가 문자열을 각각 직접 적으면 오타나 씬 이름 변경 시 일부 전환만 고장 날 수 있으므로 공용 상수로 통일합니다.
    public static class GameSceneNavigator
    {
        // Title은 사용자가 새 게임, 불러오기, 설정, 종료를 선택하는 첫 화면입니다.
        // Loading은 선택한 목적지를 비동기로 읽으며 전체 화면 이미지와 진행률을 보여 주는 전환 전용 씬입니다.
        public const string TitleSceneName = "Title";
        public const string LoadingSceneName = "Loading";
        public const string HideoutSceneName = "Hideout";
        // RaidPrototype 이름은 이전 씬 파일을 직접 열어 참고할 때만 사용할 수 있도록 남겨 둡니다.
        // 실제 기본 출격과 자동 테스트는 모두 학교 씬을 사용하므로 RaidSceneName도 SchoolRaidSceneName을 가리킵니다.
        public const string RaidPrototypeSceneName = "RaidPrototype";
        public const string SchoolRaidSceneName = "RaidSchool";
        public const string RaidSceneName = SchoolRaidSceneName;

        private static string pendingLoadingTargetSceneName;

        public static string PendingLoadingTargetSceneName => pendingLoadingTargetSceneName;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetRuntimeState()
        {
            // 에디터에서 Play를 다시 시작할 때 이전 실행의 목적지가 남아 잘못된 씬으로 이동하지 않게 합니다.
            pendingLoadingTargetSceneName = null;
        }

        // 튜토리얼처럼 모든 레이드 지역에서 공통으로 동작해야 하는 코드는 특정 한 씬 이름 대신 이 판정을 사용합니다.
        // 새 지역을 추가할 때 이 목록에 씬 이름을 더하면 거점이 아닌 레이드 흐름으로 동일하게 인식됩니다.
        public static bool IsRaidScene(string sceneName)
        {
            return sceneName == SchoolRaidSceneName;
        }

        // 무거운 게임 씬은 목적지를 정적 상태에 기록한 뒤 Loading 씬을 먼저 엽니다.
        // 목적지와 Loading 씬이 모두 Build Settings에 있을 때만 상태를 바꾸므로 실패한 전환이 다음 요청에 남지 않습니다.
        public static bool TryLoadSceneThroughLoading(string targetSceneName)
        {
            if (string.IsNullOrWhiteSpace(targetSceneName) || targetSceneName == LoadingSceneName)
            {
                Debug.LogError("로딩 씬 전환 실패: 올바른 목적지 씬 이름이 필요합니다.");
                return false;
            }
            if (!Application.CanStreamedLevelBeLoaded(targetSceneName))
            {
                Debug.LogError($"로딩 씬 전환 실패: 목적지 '{targetSceneName}'이 Build Settings에 없습니다.");
                return false;
            }
            if (!Application.CanStreamedLevelBeLoaded(LoadingSceneName))
            {
                Debug.LogError($"로딩 씬 전환 실패: '{LoadingSceneName}' 씬이 Build Settings에 없습니다.");
                return false;
            }

            pendingLoadingTargetSceneName = targetSceneName;
            if (TryLoadScene(LoadingSceneName)) return true;

            pendingLoadingTargetSceneName = null;
            return false;
        }

        // Loading 씬이 직접 실행된 경우에는 저장된 목적지가 없으므로 전달받은 기본 목적지를 사용합니다.
        // 실제 씬 활성화가 끝날 때까지 값은 유지해 로딩 화면의 상태 문구와 테스트가 같은 목적지를 읽을 수 있게 합니다.
        public static string ConsumeLoadingTarget(string fallbackSceneName)
        {
            return string.IsNullOrWhiteSpace(pendingLoadingTargetSceneName)
                ? fallbackSceneName
                : pendingLoadingTargetSceneName;
        }

        public static void CompleteLoadingTransition()
        {
            pendingLoadingTargetSceneName = null;
        }

        // 씬 이름이 비어 있거나 Build Settings에 등록되지 않은 경우 예외를 발생시키지 않고 false를 반환합니다.
        // 호출한 화면은 반환값을 이용해 현재 씬을 유지하면서 사용자에게 전환 실패 안내를 표시할 수 있습니다.
        public static bool TryLoadScene(string sceneName)
        {
            if (string.IsNullOrWhiteSpace(sceneName))
            {
                Debug.LogError("씬 전환 실패: 이동할 씬 이름이 비어 있습니다.");
                return false;
            }

            if (!Application.CanStreamedLevelBeLoaded(sceneName))
            {
                Debug.LogError($"씬 전환 실패: '{sceneName}' 씬이 Build Settings에 등록되어 있지 않습니다.");
                return false;
            }

            // 일시정지나 슬로 모션에서 씬을 나가면 Time.timeScale은 새 씬에서도 그대로 유지됩니다.
            // 모든 게임 루프 전환을 이 메서드가 담당하므로 새 거점이나 레이드가 멈춘 채 시작하지 않도록 정상 속도로 되돌립니다.
            float previousTimeScale = Time.timeScale;
            try
            {
                Time.timeScale = 1f;
                SceneManager.LoadScene(sceneName);
                return true;
            }
            catch (Exception exception)
            {
                // 실제 로드 단계에서 실패하면 현재 씬의 시간 배율을 복원해 재시도 화면까지 멈추지 않게 합니다.
                Time.timeScale = previousTimeScale;
                Debug.LogError($"씬 전환 실패: '{sceneName}' 씬을 여는 중 오류가 발생했습니다. {exception.Message}");
                return false;
            }
        }
    }
}
