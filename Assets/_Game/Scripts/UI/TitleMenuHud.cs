using System;
using UnityEngine;
using UnityEngine.UI;
using ZombieSurvival.Core;
using ZombieSurvival.Hideout;

namespace ZombieSurvival.UI
{
    // 별도의 Title 씬에서 새 게임, 저장 불러오기, 설정, 종료의 네 가지 시작 메뉴를 관리합니다.
    // Loading 씬은 이 메뉴가 정한 목적지를 비동기로 준비하면서 전체 화면 이미지와 진행률을 보여 줍니다.
    public sealed class TitleMenuHud : MonoBehaviour
    {
        // 아직 최종 상표 검토 전인 가제입니다. 제목을 바꿀 때 이 상수와 Player Settings의 Product Name을 함께 바꾸면 됩니다.
        public const string WorkingGameTitle = "LAST SALVAGE";
        public const string WorkingGameSubtitle = "마지막 회수 작전";
        public const string WorkingGameTagline = "수색하라 · 살아남아라 · 반드시 돌아와라";

        [Serializable]
        public sealed class ViewReferences
        {
            [Header("화면 패널")]
            public GameObject mainPanel;
            public GameObject settingsPanel;
            public GameObject confirmationPanel;

            [Header("주 메뉴")]
            public Text statusText;
            public Button startGameButton;
            public Button loadGameButton;
            public Button settingsButton;
            public Button quitButton;

            [Header("설정 표시 문구")]
            public Text masterVolumeText;
            public Text effectsVolumeText;
            public Text mouseSensitivityText;
            public Text aimSensitivityText;
            public Text resolutionText;
            public Text fullscreenText;
            public Text fontScaleText;
            public Text damageEffectText;
            public Text screenShakeText;

            [Header("설정 조작 버튼")]
            public Button masterVolumeDownButton;
            public Button masterVolumeUpButton;
            public Button effectsVolumeDownButton;
            public Button effectsVolumeUpButton;
            public Button mouseSensitivityDownButton;
            public Button mouseSensitivityUpButton;
            public Button aimSensitivityDownButton;
            public Button aimSensitivityUpButton;
            public Button resolutionPreviousButton;
            public Button resolutionNextButton;
            public Button fullscreenButton;
            public Button fontScaleDownButton;
            public Button fontScaleUpButton;
            public Button damageEffectDownButton;
            public Button damageEffectUpButton;
            public Button screenShakeDownButton;
            public Button screenShakeUpButton;
            public Button settingsBackButton;

            [Header("확인창")]
            public Text confirmationText;
            public Button confirmButton;
            public Button cancelButton;
        }

        private enum ConfirmationAction
        {
            None,
            NewGame,
            Quit
        }

        [SerializeField] private ViewReferences views = new ViewReferences();

        private Resolution[] resolutions;
        private int resolutionIndex;
        private ConfirmationAction pendingConfirmation;
        private bool transitionRequested;

        // 테스트와 UI 갱신 코드가 파일 시스템 세부 경로를 알지 않고도 불러오기 가능 여부를 확인할 수 있습니다.
        public bool CanLoadSavedGame => HideoutSaveService.DefaultSaveExists;

        // 에디터 생성기가 만든 패널, 글자, 버튼 참조를 프리팹에 직렬화해 오브젝트 이름 검색 없이 연결합니다.
        // 생성 시점에는 PlayerPrefs나 해상도를 건드리지 않고 어떤 패널을 처음 보여 줄지만 정리합니다.
        public void ConfigureViews(ViewReferences value)
        {
            views = value ?? new ViewReferences();
            SetPanelState(showMain: true, showSettings: false, showConfirmation: false);
        }

        private void Awake()
        {
            BindButtons();
            GameSettings.Changed += RefreshSettingsText;
            SetPanelState(showMain: true, showSettings: false, showConfirmation: false);
            RefreshSettingsText();
            RefreshMainMenu();
        }

        private void Update()
        {
            if (!Input.GetKeyDown(KeyCode.Escape)) return;

            // 설정이나 확인창에서 ESC를 누르면 게임을 종료하지 않고 한 단계 앞인 주 메뉴로 돌아갑니다.
            if ((views.settingsPanel != null && views.settingsPanel.activeSelf) ||
                (views.confirmationPanel != null && views.confirmationPanel.activeSelf))
                ShowMainPanel();
        }

        private void OnDestroy()
        {
            GameSettings.Changed -= RefreshSettingsText;
        }

        private void RequestStartGame()
        {
            if (transitionRequested) return;

            // 저장이 없으면 곧바로 새 게임을 준비하고, 저장이 있으면 실수로 진행도를 지우지 않도록 확인을 받습니다.
            if (!HideoutSaveService.DefaultSaveExists)
            {
                BeginNewGame();
                return;
            }

            pendingConfirmation = ConfirmationAction.NewGame;
            if (views.confirmationText != null)
                views.confirmationText.text = "새 게임을 시작하면 기존 창고·장비·시설·임무·화폐·제작 진행이 삭제됩니다.\n정말 새 게임을 시작할까요?";
            ShowConfirmationPanel();
        }

        private void RequestLoadGame()
        {
            if (transitionRequested) return;
            if (!HideoutSaveService.DefaultSaveExists)
            {
                // 버튼은 평소 비활성화되지만 저장 파일이 외부에서 사라지는 순간에도 안전하게 현재 화면을 유지합니다.
                if (views.statusText != null) views.statusText.text = "불러올 저장 데이터가 없습니다.";
                RefreshLoadButton();
                return;
            }

            EnterLoading("저장 데이터를 불러오는 중입니다.");
        }

        private void BeginNewGame()
        {
            // 주 저장과 백업, 메모리 진행도를 모두 초기화한 뒤에만 Loading으로 이동합니다.
            // 파일 삭제가 실패하면 현재 화면에 남아 기존 저장을 잃지 않도록 합니다.
            if (!HideoutSaveService.TryResetDefaultSave())
            {
                ShowMainPanel(refreshStatus: false);
                if (views.statusText != null)
                    views.statusText.text = "새 게임 데이터를 준비하지 못했습니다. 저장 경로 권한과 Console을 확인해 주세요.";
                return;
            }

            EnterLoading("새 게임을 준비하는 중입니다.");
        }

        private void EnterLoading(string statusMessage)
        {
            // 레이드 결과는 Raid→Hideout 전환에서만 사용하는 임시 전달값입니다.
            // 타이틀에서 새 실행을 시작할 때 이전 Play 세션의 값이 남아 있으면 창고에 보상이 한 번 더 들어갈 수 있으므로 여기서 비웁니다.
            RaidResultStore.Clear();
            transitionRequested = true;
            SetMainButtonsInteractable(false);
            if (views.statusText != null) views.statusText.text = statusMessage;

            if (GameSceneNavigator.TryLoadSceneThroughLoading(GameSceneNavigator.HideoutSceneName)) return;

            // Build Settings 누락처럼 씬 이동에 실패하면 버튼을 다시 열어 사용자가 수정 후 재시도할 수 있게 합니다.
            transitionRequested = false;
            RefreshMainMenu();
            if (views.statusText != null)
                views.statusText.text = "Loading 씬을 열 수 없습니다. Build Settings와 Console을 확인해 주세요.";
        }

        private void ShowSettings()
        {
            pendingConfirmation = ConfirmationAction.None;
            SetPanelState(showMain: false, showSettings: true, showConfirmation: false);
            RefreshSettingsText();
        }

        private void ShowMainPanel()
        {
            ShowMainPanel(refreshStatus: true);
        }

        private void ShowMainPanel(bool refreshStatus)
        {
            pendingConfirmation = ConfirmationAction.None;
            SetPanelState(showMain: true, showSettings: false, showConfirmation: false);
            if (refreshStatus) RefreshMainMenu();
        }

        private void RequestQuit()
        {
            pendingConfirmation = ConfirmationAction.Quit;
            if (views.confirmationText != null) views.confirmationText.text = "게임을 종료할까요?";
            ShowConfirmationPanel();
        }

        private void ShowConfirmationPanel()
        {
            SetPanelState(showMain: false, showSettings: false, showConfirmation: true);
        }

        private void ConfirmPendingAction()
        {
            if (pendingConfirmation == ConfirmationAction.NewGame)
            {
                BeginNewGame();
                return;
            }

            if (pendingConfirmation != ConfirmationAction.Quit) return;
            // 에디터에서는 Play Mode가 유지될 수 있지만 실제 Windows 빌드에서는 이 호출로 정상 종료됩니다.
            Time.timeScale = 1f;
            Application.Quit();
        }

        private void RefreshMainMenu()
        {
            SetMainButtonsInteractable(!transitionRequested);
            RefreshLoadButton();
            if (views.statusText != null)
            {
                views.statusText.text = HideoutSaveService.DefaultSaveExists
                    ? "저장 데이터가 있습니다. 게임 로드로 이어서 플레이할 수 있습니다."
                    : "저장 데이터가 없습니다. 게임 시작으로 새 생존 기록을 만드세요.";
            }
        }

        private void RefreshLoadButton()
        {
            if (views.loadGameButton != null)
                views.loadGameButton.interactable = !transitionRequested && HideoutSaveService.DefaultSaveExists;
        }

        private void SetMainButtonsInteractable(bool interactable)
        {
            if (views.startGameButton != null) views.startGameButton.interactable = interactable;
            if (views.settingsButton != null) views.settingsButton.interactable = interactable;
            if (views.quitButton != null) views.quitButton.interactable = interactable;
        }

        private void SetPanelState(bool showMain, bool showSettings, bool showConfirmation)
        {
            if (views.mainPanel != null) views.mainPanel.SetActive(showMain);
            if (views.settingsPanel != null) views.settingsPanel.SetActive(showSettings);
            if (views.confirmationPanel != null) views.confirmationPanel.SetActive(showConfirmation);
        }

        private void BindButtons()
        {
            AddListener(views.startGameButton, RequestStartGame);
            AddListener(views.loadGameButton, RequestLoadGame);
            AddListener(views.settingsButton, ShowSettings);
            AddListener(views.quitButton, RequestQuit);
            AddListener(views.settingsBackButton, ShowMainPanel);
            AddListener(views.confirmButton, ConfirmPendingAction);
            AddListener(views.cancelButton, ShowMainPanel);

            AddListener(views.masterVolumeDownButton, () => GameSettings.SetMasterVolume(GameSettings.MasterVolume - 0.1f));
            AddListener(views.masterVolumeUpButton, () => GameSettings.SetMasterVolume(GameSettings.MasterVolume + 0.1f));
            AddListener(views.effectsVolumeDownButton, () => GameSettings.SetEffectsVolume(GameSettings.EffectsVolume - 0.1f));
            AddListener(views.effectsVolumeUpButton, () => GameSettings.SetEffectsVolume(GameSettings.EffectsVolume + 0.1f));
            AddListener(views.mouseSensitivityDownButton, () => GameSettings.SetMouseSensitivity(GameSettings.MouseSensitivity - 0.1f));
            AddListener(views.mouseSensitivityUpButton, () => GameSettings.SetMouseSensitivity(GameSettings.MouseSensitivity + 0.1f));
            AddListener(views.aimSensitivityDownButton, () => GameSettings.SetAimSensitivity(GameSettings.AimSensitivity - 0.1f));
            AddListener(views.aimSensitivityUpButton, () => GameSettings.SetAimSensitivity(GameSettings.AimSensitivity + 0.1f));
            AddListener(views.resolutionPreviousButton, () => ChangeResolution(-1));
            AddListener(views.resolutionNextButton, () => ChangeResolution(1));
            AddListener(views.fullscreenButton, () => GameSettings.SetFullscreen(!GameSettings.Fullscreen));
            AddListener(views.fontScaleDownButton, () => GameSettings.SetFontScale(GameSettings.FontScale - 0.1f));
            AddListener(views.fontScaleUpButton, () => GameSettings.SetFontScale(GameSettings.FontScale + 0.1f));
            AddListener(views.damageEffectDownButton, () => GameSettings.SetDamageEffectIntensity(GameSettings.DamageEffectIntensity - 0.1f));
            AddListener(views.damageEffectUpButton, () => GameSettings.SetDamageEffectIntensity(GameSettings.DamageEffectIntensity + 0.1f));
            AddListener(views.screenShakeDownButton, () => GameSettings.SetScreenShakeIntensity(GameSettings.ScreenShakeIntensity - 0.1f));
            AddListener(views.screenShakeUpButton, () => GameSettings.SetScreenShakeIntensity(GameSettings.ScreenShakeIntensity + 0.1f));
        }

        private static void AddListener(Button button, UnityEngine.Events.UnityAction action)
        {
            if (button != null) button.onClick.AddListener(action);
        }

        private void ChangeResolution(int direction)
        {
            EnsureResolutionList();
            if (resolutions.Length == 0) return;

            resolutionIndex = (resolutionIndex + direction + resolutions.Length) % resolutions.Length;
            Resolution selected = resolutions[resolutionIndex];
            GameSettings.SetResolution(selected.width, selected.height);
        }

        private void RefreshSettingsText()
        {
            EnsureResolutionList();
            if (views.masterVolumeText != null) views.masterVolumeText.text = Mathf.RoundToInt(GameSettings.MasterVolume * 100f) + "%";
            if (views.effectsVolumeText != null) views.effectsVolumeText.text = Mathf.RoundToInt(GameSettings.EffectsVolume * 100f) + "%";
            if (views.mouseSensitivityText != null) views.mouseSensitivityText.text = GameSettings.MouseSensitivity.ToString("0.0");
            if (views.aimSensitivityText != null) views.aimSensitivityText.text = GameSettings.AimSensitivity.ToString("0.0");
            if (views.resolutionText != null) views.resolutionText.text = GameSettings.ResolutionWidth + " × " + GameSettings.ResolutionHeight;
            if (views.fullscreenText != null) views.fullscreenText.text = GameSettings.Fullscreen ? "전체 화면" : "창 모드";
            if (views.fontScaleText != null) views.fontScaleText.text = Mathf.RoundToInt(GameSettings.FontScale * 100f) + "%";
            if (views.damageEffectText != null) views.damageEffectText.text = Mathf.RoundToInt(GameSettings.DamageEffectIntensity * 100f) + "%";
            if (views.screenShakeText != null) views.screenShakeText.text = Mathf.RoundToInt(GameSettings.ScreenShakeIntensity * 100f) + "%";
        }

        private void EnsureResolutionList()
        {
            if (resolutions == null || resolutions.Length == 0)
                resolutions = GameSettings.GetAvailableResolutions();
            resolutionIndex = GameSettings.FindSelectedResolutionIndex(resolutions);
        }
    }
}
