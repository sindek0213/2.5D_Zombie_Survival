using System.Collections;
using UnityEngine;
using ZombieSurvival.Hideout;

namespace ZombieSurvival.Core
{
    // 게임의 최초 진입점에서 이전 Play 세션의 임시 결과를 정리하고 영구 창고를 불러온 뒤 Hideout으로 이동합니다.
    // Bootstrap이 초기화를 전담하므로 빌드 실행과 에디터 테스트가 같은 순서로 게임 상태를 준비할 수 있습니다.
    public sealed class BootstrapController : MonoBehaviour
    {
        [SerializeField] private string hideoutSceneName = GameSceneNavigator.HideoutSceneName;
        [SerializeField] private bool enterHideoutAutomatically = true;
        [SerializeField, Min(0f)] private float automaticTransitionDelay = 0.75f;

        public bool IsInitialized { get; private set; }
        public bool SaveLoadSucceeded { get; private set; }
        public string StatusMessage { get; private set; }
        public int StashStackCount => HideoutStateStore.StashItems.Count;
        public int LoadoutStackCount => HideoutStateStore.LoadoutItems.Count;

        private void Awake() => Initialize();

        private void Start()
        {
            // 한 프레임도 보이지 않는 즉시 전환 대신 짧은 실시간 대기를 사용해 초기화 상태를 확인할 수 있게 합니다.
            // realtime 대기이므로 다른 시스템이 Time.timeScale을 0으로 남겼더라도 Bootstrap 진행이 멈추지 않습니다.
            if (enterHideoutAutomatically) StartCoroutine(EnterHideoutAfterDelay());
        }

        // 테스트나 수동 진입에서도 같은 초기화 순서를 재사용할 수 있도록 공개 메서드로 분리했습니다.
        // 여러 번 호출되어도 저장 파일을 다시 읽거나 현재 창고를 중복 변경하지 않고 최초 결과만 반환합니다.
        public bool Initialize()
        {
            if (IsInitialized) return SaveLoadSucceeded;

            // PendingResult는 한 레이드 안에서 Hideout으로 전달할 임시 값이므로 새로운 게임 실행에서는 남아 있으면 안 됩니다.
            RaidResultStore.Clear();
            SaveLoadSucceeded = HideoutSaveService.LoadDefaultIfNeeded();
            IsInitialized = true;

            StatusMessage = SaveLoadSucceeded
                ? "저장 데이터를 불러왔습니다. 거점으로 이동합니다."
                : "저장 파일을 읽지 못했습니다. 파일을 덮어쓰지 않고 빈 상태로 거점에 진입합니다.";
            return SaveLoadSucceeded;
        }

        // 자동 전환이 실패했을 때 Bootstrap 화면의 재시도 버튼도 이 메서드를 그대로 사용합니다.
        public bool EnterHideout()
        {
            Initialize();
            if (GameSceneNavigator.TryLoadScene(hideoutSceneName)) return true;

            StatusMessage = "Hideout 씬을 열 수 없습니다. Build Settings와 씬 파일을 확인해 주세요.";
            return false;
        }

        private IEnumerator EnterHideoutAfterDelay()
        {
            yield return new WaitForSecondsRealtime(automaticTransitionDelay);
            EnterHideout();
        }
    }
}
