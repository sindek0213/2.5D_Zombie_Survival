using System;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ZombieSurvival.Core
{
    // 게임 진행에 사용되는 씬 이름을 한곳에서 관리합니다.
    // 여러 컨트롤러가 문자열을 각각 직접 적으면 오타나 씬 이름 변경 시 일부 전환만 고장 날 수 있으므로 공용 상수로 통일합니다.
    public static class GameSceneNavigator
    {
        public const string BootstrapSceneName = "Bootstrap";
        public const string HideoutSceneName = "Hideout";
        public const string RaidSceneName = "RaidPrototype";

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
