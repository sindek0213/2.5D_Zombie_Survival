using System;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using ZombieSurvival.Core;
using ZombieSurvival.Hideout;
using ZombieSurvival.Player;

namespace ZombieSurvival.UI
{
    // ESC 입력을 다른 화면보다 먼저 확인해 인벤토리·시설 화면을 우선 닫고, 남은 경우에만 게임을 일시정지합니다.
    // InventoryHud의 실행 순서가 -100이므로 더 이른 -500에서 처리하면 같은 ESC 입력으로 창을 닫은 뒤 일시정지까지 열리는 일을 막을 수 있습니다.
    [DefaultExecutionOrder(-500)]
    public sealed class PauseMenuHud : MonoBehaviour
    {
        [Serializable]
        public sealed class ViewReferences
        {
            [Header("화면 패널")]
            public GameObject pausePanel;
            public GameObject mainPanel;
            public GameObject settingsPanel;
            public GameObject confirmationPanel;

            [Header("표시 문구")]
            public Text contextText;
            public Text masterVolumeText;
            public Text effectsVolumeText;
            public Text mouseSensitivityText;
            public Text aimSensitivityText;
            public Text resolutionText;
            public Text fullscreenText;
            public Text fontScaleText;
            public Text damageEffectText;
            public Text screenShakeText;
            public Text confirmationText;
            public Text returnButtonText;

            [Header("주 메뉴 버튼")]
            public Button continueButton;
            public Button settingsButton;
            public Button returnButton;
            public Button resetSaveButton;
            public Button quitButton;

            [Header("설정 버튼")]
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
            public Button tutorialReplayButton;
            public Button settingsBackButton;

            [Header("확인 버튼")]
            public Button confirmButton;
            public Button cancelButton;
        }

        private enum ConfirmationAction
        {
            None,
            Return,
            ResetSave,
            Quit
        }

        [SerializeField] private ViewReferences views = new ViewReferences();
        [SerializeField] private InventoryHud inventoryHud;
        [SerializeField] private HideoutWorldController hideoutWorldController;
        [SerializeField] private RaidSession raidSession;
        [SerializeField] private PlayerControlGate controlGate;

        private Resolution[] resolutions;
        private int resolutionIndex;
        private float previousTimeScale = 1f;
        private bool isPaused;
        private ConfirmationAction pendingConfirmation;

        public bool IsPaused => isPaused;

        // 에디터 생성기가 만든 모든 패널과 버튼 참조를 한 묶음으로 저장합니다.
        // 런타임에는 오브젝트 이름 검색에 의존하지 않으므로 UI 이름을 바꾸더라도 직렬화된 연결이 유지됩니다.
        public void ConfigureViews(ViewReferences value)
        {
            views = value ?? new ViewReferences();
            ApplyClosedStateImmediately();
        }

        // 씬 생성 시 알고 있는 실제 플레이어·거점·레이드 참조를 전달해 FindObjectOfType 사용을 최소화합니다.
        // 일부 오래된 씬에서 이 호출이 빠져도 ResolveContext가 시작 시 한 번 찾아 기능을 복구합니다.
        public void SetContext(
            InventoryHud inventory,
            HideoutWorldController hideoutWorld,
            RaidSession session,
            PlayerControlGate gate)
        {
            inventoryHud = inventory;
            hideoutWorldController = hideoutWorld;
            raidSession = session;
            controlGate = gate;
            RefreshContextText();
        }

        private void Awake()
        {
            ResolveContext();
            BindButtons();
            GameSettings.Changed += RefreshSettingsText;
            ApplyClosedStateImmediately();
            RefreshSettingsText();
            RefreshContextText();
        }

        private void Update()
        {
            if (!Input.GetKeyDown(KeyCode.Escape)) return;

            // 거점 관리자가 추적하는 창고·임무·상인·출격·인벤토리는 일시정지보다 먼저 닫습니다.
            // 여기서 return해야 같은 키 입력이 아래 TogglePause까지 내려가 두 화면이 연달아 바뀌지 않습니다.
            if (!isPaused && hideoutWorldController != null && hideoutWorldController.IsFacilityOpen)
            {
                hideoutWorldController.CloseFacility();
                return;
            }

            // 레이드 인벤토리는 거점 관리자의 activeScreen에 포함되지 않으므로 별도로 먼저 닫습니다.
            if (!isPaused && inventoryHud != null && inventoryHud.IsOpen)
            {
                inventoryHud.SetOpen(false);
                return;
            }

            if (!isPaused)
            {
                OpenPause();
                return;
            }

            // 일시정지 안쪽에서는 ESC가 한 단계씩 뒤로 갑니다. 확인창 → 설정창 → 게임 복귀 순서입니다.
            if (views.confirmationPanel != null && views.confirmationPanel.activeSelf)
            {
                HideConfirmation();
                return;
            }
            if (views.settingsPanel != null && views.settingsPanel.activeSelf)
            {
                ShowMainPanel();
                return;
            }
            ClosePause();
        }

        private void OnDestroy()
        {
            GameSettings.Changed -= RefreshSettingsText;
            ReleasePauseState();
        }

        public void OpenPause()
        {
            ResolveContext();
            // 결과가 확정된 레이드는 자체 결과 화면과 자동 복귀 흐름을 사용하므로 그 위에 일시정지 메뉴를 겹치지 않습니다.
            if (isPaused || (raidSession != null && raidSession.HasEnded)) return;

            isPaused = true;
            previousTimeScale = Time.timeScale;
            Time.timeScale = 0f;
            controlGate?.AcquireLock(this);
            if (views.pausePanel != null) views.pausePanel.SetActive(true);
            ShowMainPanel();
            RefreshContextText();
        }

        public void ClosePause()
        {
            if (!isPaused) return;
            ReleasePauseState();
            ApplyClosedStateImmediately();
        }

        private void ShowSettings()
        {
            if (views.mainPanel != null) views.mainPanel.SetActive(false);
            if (views.settingsPanel != null) views.settingsPanel.SetActive(true);
            if (views.confirmationPanel != null) views.confirmationPanel.SetActive(false);
            pendingConfirmation = ConfirmationAction.None;
            RefreshSettingsText();
        }

        private void ShowMainPanel()
        {
            if (views.mainPanel != null) views.mainPanel.SetActive(true);
            if (views.settingsPanel != null) views.settingsPanel.SetActive(false);
            if (views.confirmationPanel != null) views.confirmationPanel.SetActive(false);
            pendingConfirmation = ConfirmationAction.None;
        }

        private void RequestReturnConfirmation()
        {
            pendingConfirmation = ConfirmationAction.Return;
            if (views.confirmationText != null)
            {
                views.confirmationText.text = raidSession != null
                    ? "레이드를 포기하면 현재 소지품과 장착 장비를 모두 잃습니다.\n정말 거점으로 돌아갈까요?"
                    : "현재 거점 상태를 저장하고 시작 화면으로 돌아갈까요?";
            }
            ShowConfirmationPanel();
        }

        private void RequestQuitConfirmation()
        {
            pendingConfirmation = ConfirmationAction.Quit;
            if (views.confirmationText != null)
            {
                views.confirmationText.text = raidSession != null
                    ? "게임을 종료하면 진행 중인 레이드의 소지품을 잃습니다.\n정말 종료할까요?"
                    : "현재 거점 상태를 저장하고 게임을 종료할까요?";
            }
            ShowConfirmationPanel();
        }

        private void RequestResetSaveConfirmation()
        {
            pendingConfirmation = ConfirmationAction.ResetSave;
            if (views.confirmationText != null)
                views.confirmationText.text = "창고·장비·시설·임무·화폐·제작 진행을 모두 초기화합니다.\n이 작업은 되돌릴 수 없습니다. 정말 새 게임을 시작할까요?";
            ShowConfirmationPanel();
        }

        private void ShowConfirmationPanel()
        {
            if (views.mainPanel != null) views.mainPanel.SetActive(false);
            if (views.settingsPanel != null) views.settingsPanel.SetActive(false);
            if (views.confirmationPanel != null) views.confirmationPanel.SetActive(true);
        }

        private void HideConfirmation()
        {
            pendingConfirmation = ConfirmationAction.None;
            ShowMainPanel();
        }

        private void ConfirmPendingAction()
        {
            if (pendingConfirmation == ConfirmationAction.Return)
            {
                ReturnFromCurrentScene();
                return;
            }
            if (pendingConfirmation == ConfirmationAction.ResetSave)
            {
                ResetSaveAndReturnToHideout();
                return;
            }
            if (pendingConfirmation == ConfirmationAction.Quit) QuitGame();
        }

        private void ResetSaveAndReturnToHideout()
        {
            if (raidSession != null)
            {
                if (views.confirmationText != null) views.confirmationText.text = "레이드 중에는 새 게임을 시작할 수 없습니다. 먼저 거점으로 돌아가세요.";
                return;
            }
            if (!HideoutSaveService.TryResetDefaultSave())
            {
                if (views.confirmationText != null) views.confirmationText.text = "저장 데이터 초기화에 실패했습니다. 파일 권한과 Console을 확인해 주세요.";
                return;
            }
            ClosePause();
            // 초기화 뒤 곧바로 거점을 열지 않고 공용 Loading 화면에서 저장 준비와 씬 로드를 처리합니다.
            GameSceneNavigator.TryLoadSceneThroughLoading(GameSceneNavigator.HideoutSceneName);
        }

        private void ReturnFromCurrentScene()
        {
            if (raidSession != null)
            {
                // 레이드 포기는 사망과 같은 실패 결과를 먼저 만들어 현재 소지품을 제거한 뒤 기존 결과 화면과 거점 복귀 흐름을 사용합니다.
                ClosePause();
                raidSession.TryAbandonRaid();
                return;
            }

            // 거점에서는 창고·장비·임무·시설 상태를 먼저 저장합니다. 저장 실패 시 씬을 바꾸지 않아 메모리 상태를 잃지 않게 합니다.
            if (!HideoutSaveService.SaveDefault())
            {
                if (views.confirmationText != null)
                    views.confirmationText.text = "저장에 실패해 시작 화면으로 이동하지 않았습니다.\n저장 경로와 Console 오류를 확인해 주세요.";
                return;
            }

            ClosePause();
            // 거점에서 '시작 화면으로'를 선택하면 초기화 로딩 화면이 아니라 사용자가 메뉴를 고르는 Title 씬으로 돌아갑니다.
            GameSceneNavigator.TryLoadScene(GameSceneNavigator.TitleSceneName);
        }

        private void QuitGame()
        {
            // 거점에서만 현재 메모리 상태를 저장합니다. 레이드 중 장비는 출격 때 이미 창고에서 제거됐으므로 저장하지 않아야 포기 손실 규칙이 유지됩니다.
            if (raidSession == null && !HideoutSaveService.SaveDefault())
            {
                if (views.confirmationText != null)
                    views.confirmationText.text = "저장에 실패해 게임을 종료하지 않았습니다.\n저장 경로와 Console 오류를 확인해 주세요.";
                return;
            }

            Time.timeScale = 1f;
            Application.Quit();
        }

        private void BindButtons()
        {
            AddListener(views.continueButton, ClosePause);
            AddListener(views.settingsButton, ShowSettings);
            AddListener(views.returnButton, RequestReturnConfirmation);
            AddListener(views.resetSaveButton, RequestResetSaveConfirmation);
            AddListener(views.quitButton, RequestQuitConfirmation);
            AddListener(views.settingsBackButton, ShowMainPanel);
            AddListener(views.confirmButton, ConfirmPendingAction);
            AddListener(views.cancelButton, HideConfirmation);

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
            AddListener(views.tutorialReplayButton, RestartTutorial);
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

        private void RestartTutorial()
        {
            TutorialProgressStore.RestartTutorial();
            if (raidSession == null) HideoutSaveService.SaveDefault();
        }

        private void RefreshContextText()
        {
            bool inRaid = raidSession != null || GameSceneNavigator.IsRaidScene(SceneManager.GetActiveScene().name);
            if (views.contextText != null) views.contextText.text = inRaid ? "레이드 일시정지" : "거점 일시정지";
            if (views.returnButtonText != null) views.returnButtonText.text = inRaid ? "레이드 포기" : "시작 화면으로";
        }

        private void EnsureResolutionList()
        {
            if (resolutions == null || resolutions.Length == 0)
                resolutions = GameSettings.GetAvailableResolutions();
            resolutionIndex = GameSettings.FindSelectedResolutionIndex(resolutions);
        }

        private void ResolveContext()
        {
            if (inventoryHud == null) inventoryHud = FindObjectOfType<InventoryHud>();
            if (hideoutWorldController == null) hideoutWorldController = FindObjectOfType<HideoutWorldController>();
            if (raidSession == null) raidSession = FindObjectOfType<RaidSession>();
            if (controlGate == null) controlGate = FindObjectOfType<PlayerControlGate>();
        }

        private void ApplyClosedStateImmediately()
        {
            isPaused = false;
            pendingConfirmation = ConfirmationAction.None;
            if (views.pausePanel != null) views.pausePanel.SetActive(false);
            if (views.mainPanel != null) views.mainPanel.SetActive(true);
            if (views.settingsPanel != null) views.settingsPanel.SetActive(false);
            if (views.confirmationPanel != null) views.confirmationPanel.SetActive(false);
        }

        private void ReleasePauseState()
        {
            if (!isPaused) return;
            isPaused = false;
            Time.timeScale = previousTimeScale > 0f ? previousTimeScale : 1f;
            controlGate?.ReleaseLock(this);
        }
    }
}
