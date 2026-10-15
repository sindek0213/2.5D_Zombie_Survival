using System.Text;
using UnityEngine;
using UnityEngine.UI;
using ZombieSurvival.Hideout;
using ZombieSurvival.Player;

namespace ZombieSurvival.UI
{
    // 거점 월드 위에 필요한 최소 안내만 표시하는 HUD입니다.
    // 시설 안내는 화면 아래에 고정하지 않고 대상 시설의 월드 위치를 화면 좌표로 바꿔 시설 위에 따라다니게 합니다.
    public sealed class HideoutWorldHud : MonoBehaviour
    {
        [SerializeField] private HideoutWorldController worldController;
        [SerializeField] private PlayerInteraction playerInteraction;
        // 이전 프리팹과의 직렬화 호환을 위해 필드는 남겨 두지만, 거점 제목은 더 이상 화면에 표시하지 않습니다.
        [SerializeField] private Text titleText;
        [SerializeField] private Text promptText;
        [SerializeField] private GameObject missionJournalPanel;
        [SerializeField] private Text missionJournalTitleText;
        [SerializeField] private Text missionJournalContentText;
        [SerializeField] private Text missionJournalGuideText;

        private Canvas rootCanvas;
        private RectTransform rootRect;
        private RectTransform promptPanelRect;
        private Camera worldCamera;

        // 컨트롤러가 J키 화면을 일반 시설 화면과 같은 방식으로 열고 닫을 때 사용하는 루트 오브젝트입니다.
        public GameObject MissionJournalScreen
        {
            get
            {
                EnsureViewStructure();
                return missionJournalPanel;
            }
        }

        public bool IsMissionJournalOpen => missionJournalPanel != null && missionJournalPanel.activeSelf;

        public void Configure(
            HideoutWorldController world,
            PlayerInteraction interaction,
            Text title,
            Text prompt)
        {
            worldController = world;
            playerInteraction = interaction;
            titleText = title;
            promptText = prompt;
            InitializeView();
            RefreshImmediately();
        }

        // 에디터에서 미리 만든 임무수첩 UI를 연결할 수 있게 별도 메서드를 제공합니다.
        // 연결하지 않은 기존 프리팹은 EnsureViewStructure가 같은 구조를 한 번만 만들어 이전 씬도 그대로 실행됩니다.
        public void ConfigureMissionJournal(GameObject panel, Text title, Text content, Text guide)
        {
            missionJournalPanel = panel;
            missionJournalTitleText = title;
            missionJournalContentText = content;
            missionJournalGuideText = guide;
            SetMissionJournalOpen(false);
        }

        // 프리팹의 외형은 유지하면서 현재 씬에 생성된 플레이어와 컨트롤러 참조만 다시 연결합니다.
        public void SetContext(HideoutWorldController world, PlayerInteraction interaction)
        {
            worldController = world;
            playerInteraction = interaction;
            InitializeView();
            RefreshImmediately();
        }

        private void Awake()
        {
            InitializeView();
            SetMissionJournalOpen(false);
            SetPromptVisible(false);
        }

        private void Update() => RefreshImmediately();

        private void InitializeView()
        {
            EnsureViewStructure();

            // 기존의 "거점 · 임무" 제목은 요청에 따라 숨깁니다.
            // 오브젝트를 삭제하지 않고 비활성화하므로 이전 프리팹의 직렬화 참조가 끊기지 않고 필요하면 Inspector에서 다시 활용할 수 있습니다.
            if (titleText != null) titleText.gameObject.SetActive(false);

            rootCanvas = GetComponentInParent<Canvas>();
            rootRect = rootCanvas != null ? rootCanvas.transform as RectTransform : transform as RectTransform;
            promptPanelRect = promptText != null ? promptText.transform.parent as RectTransform : null;
            worldCamera = Camera.main;

            if (promptPanelRect != null)
            {
                // 기존 하단 고정 앵커를 화면 중앙 기준 앵커로 바꿔 월드 좌표를 그대로 배치할 수 있게 합니다.
                promptPanelRect.anchorMin = new Vector2(0.5f, 0.5f);
                promptPanelRect.anchorMax = new Vector2(0.5f, 0.5f);
                promptPanelRect.pivot = new Vector2(0.5f, 0.5f);
                promptPanelRect.sizeDelta = new Vector2(520f, 58f);
            }
        }

        public void RefreshImmediately()
        {
            EnsureViewStructure();
            if (IsMissionJournalOpen) RefreshMissionJournal();

            // 시설 화면이나 임무수첩이 열려 있을 때는 시설 위 E 안내를 감춥니다.
            // 닫기 조작은 각 화면 내부의 안내로 제공하므로 월드 안내와 겹치지 않습니다.
            if (worldController != null && worldController.IsFacilityOpen)
            {
                SetPromptVisible(false);
                return;
            }

            HideoutFacility facility = playerInteraction?.Current as HideoutFacility;
            if (facility == null || promptText == null)
            {
                SetPromptVisible(false);
                return;
            }

            promptText.text = facility.InteractionPrompt;
            SetPromptVisible(true);
            PositionPromptAboveFacility(facility);
        }

        public void SetMissionJournalOpen(bool open)
        {
            EnsureViewStructure();
            if (missionJournalPanel == null) return;
            missionJournalPanel.SetActive(open);
            if (open) RefreshMissionJournal();
        }

        public bool OwnsMissionJournalScreen(GameObject screen)
        {
            EnsureViewStructure();
            return screen != null && screen == missionJournalPanel;
        }

        public void RefreshMissionJournal()
        {
            EnsureViewStructure();
            if (missionJournalTitleText != null) missionJournalTitleText.text = "임무수첩";
            if (missionJournalContentText != null)
            {
                missionJournalContentText.text = BuildMissionJournalText(
                    HideoutMissionStateStore.ActiveMissionDefinition,
                    HideoutMissionStateStore.ActiveMissionId,
                    HideoutMissionStateStore.ObjectiveProgress);
            }
            if (missionJournalGuideText != null)
                missionJournalGuideText.text = "J / Esc / Tab · 닫기";
        }

        // 현재 임무 시스템은 동시에 한 임무만 수주할 수 있으므로 목록의 첫 번째 항목으로 표시합니다.
        // 나중에 다중 임무 저장 구조가 생기면 이 메서드에 임무 배열을 넘겨 항목을 반복 생성하는 방식으로 확장할 수 있습니다.
        public static string BuildMissionJournalText(
            HideoutMissionDefinition mission,
            string activeMissionId,
            int progress)
        {
            if (string.IsNullOrWhiteSpace(activeMissionId))
                return "현재 수주한 임무가 없습니다.\n\n거점의 임무 보드에서 임무를 받을 수 있습니다.";

            if (mission == null)
                return $"1. 불러오지 못한 임무\n\n임무 ID: {activeMissionId}\n현재 진행도: {Mathf.Max(0, progress)}\n\n임무 에셋 연결 상태를 확인해 주세요.";

            var builder = new StringBuilder();
            builder.Append("1. ").AppendLine(mission.DisplayName);
            if (!string.IsNullOrWhiteSpace(mission.Description))
                builder.AppendLine(mission.Description.Trim()).AppendLine();
            builder.Append("목표  · ").AppendLine(mission.BuildProgressDescription(progress));
            builder.Append("보상  · ").AppendLine(mission.BuildRewardDescription());
            builder.Append("위험도 · ").Append(mission.RiskRating).Append(" / 5");
            return builder.ToString();
        }

        private void PositionPromptAboveFacility(HideoutFacility facility)
        {
            if (facility == null || promptPanelRect == null || rootRect == null) return;
            if (worldCamera == null) worldCamera = Camera.main;
            if (worldCamera == null) return;

            Vector3 screenPoint = worldCamera.WorldToScreenPoint(facility.PromptWorldPosition);
            // 카메라 뒤에 있는 시설은 좌표가 화면에 뒤집혀 나타날 수 있으므로 안내를 숨깁니다.
            if (screenPoint.z <= 0f)
            {
                SetPromptVisible(false);
                return;
            }

            Camera eventCamera = rootCanvas != null && rootCanvas.renderMode != RenderMode.ScreenSpaceOverlay
                ? rootCanvas.worldCamera
                : null;
            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(rootRect, screenPoint, eventCamera, out Vector2 localPoint))
                promptPanelRect.anchoredPosition = localPoint;
        }

        private void SetPromptVisible(bool visible)
        {
            GameObject panel = promptText != null && promptText.transform.parent != null
                ? promptText.transform.parent.gameObject
                : promptText != null ? promptText.gameObject : null;
            if (panel != null && panel.activeSelf != visible) panel.SetActive(visible);
        }

        private void EnsureViewStructure()
        {
            if (titleText == null) titleText = FindText("HideoutTitle");
            if (promptText == null) promptText = FindText("Prompt");

            if (missionJournalPanel == null)
            {
                Transform existing = transform.Find("MissionJournalPanel");
                missionJournalPanel = existing != null ? existing.gameObject : CreateMissionJournalPanel();
            }

            if (missionJournalPanel == null) return;
            if (missionJournalTitleText == null) missionJournalTitleText = FindTextUnder(missionJournalPanel.transform, "Title");
            if (missionJournalContentText == null) missionJournalContentText = FindTextUnder(missionJournalPanel.transform, "Content");
            if (missionJournalGuideText == null) missionJournalGuideText = FindTextUnder(missionJournalPanel.transform, "Guide");
        }

        private GameObject CreateMissionJournalPanel()
        {
            // 오래된 HideoutWorldCanvas 프리팹에도 바로 동작하도록 누락된 수첩 UI만 런타임에 보완합니다.
            // 새 프리팹은 에디터 생성기에서 같은 이름의 오브젝트를 저장할 수 있어 아트 작업 시 각 요소를 직접 수정할 수 있습니다.
            GameObject panel = CreateUiObject("MissionJournalPanel", transform);
            RectTransform panelRect = panel.GetComponent<RectTransform>();
            panelRect.anchorMin = panelRect.anchorMax = new Vector2(0.5f, 0.5f);
            panelRect.pivot = new Vector2(0.5f, 0.5f);
            panelRect.sizeDelta = new Vector2(680f, 520f);

            Image background = panel.AddComponent<Image>();
            background.color = new Color(0.035f, 0.04f, 0.045f, 0.97f);

            missionJournalTitleText = CreateText("Title", panel.transform, "임무수첩", 30, FontStyle.Bold,
                new Vector2(0.05f, 0.86f), new Vector2(0.95f, 0.97f));
            missionJournalTitleText.color = new Color(0.93f, 0.63f, 0.18f, 1f);
            missionJournalContentText = CreateText("Content", panel.transform, string.Empty, 21, FontStyle.Normal,
                new Vector2(0.06f, 0.16f), new Vector2(0.94f, 0.83f));
            missionJournalContentText.alignment = TextAnchor.UpperLeft;
            missionJournalGuideText = CreateText("Guide", panel.transform, "J / Esc / Tab · 닫기", 17, FontStyle.Normal,
                new Vector2(0.06f, 0.04f), new Vector2(0.94f, 0.12f));
            missionJournalGuideText.alignment = TextAnchor.MiddleRight;
            panel.SetActive(false);
            return panel;
        }

        private static GameObject CreateUiObject(string objectName, Transform parent)
        {
            var gameObject = new GameObject(objectName, typeof(RectTransform));
            gameObject.transform.SetParent(parent, false);
            return gameObject;
        }

        private static Text CreateText(
            string objectName,
            Transform parent,
            string value,
            int fontSize,
            FontStyle style,
            Vector2 anchorMin,
            Vector2 anchorMax)
        {
            GameObject child = CreateUiObject(objectName, parent);
            RectTransform rect = child.GetComponent<RectTransform>();
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            Text text = child.AddComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.text = value;
            text.fontSize = fontSize;
            text.fontStyle = style;
            text.color = new Color(0.9f, 0.91f, 0.88f, 1f);
            text.alignment = TextAnchor.MiddleCenter;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            text.raycastTarget = false;
            return text;
        }

        private Text FindText(string objectName)
        {
            Text[] texts = GetComponentsInChildren<Text>(true);
            foreach (Text text in texts)
                if (text != null && text.gameObject.name == objectName) return text;
            return null;
        }

        private static Text FindTextUnder(Transform parent, string objectName)
        {
            if (parent == null) return null;
            Text[] texts = parent.GetComponentsInChildren<Text>(true);
            foreach (Text text in texts)
                if (text != null && text.gameObject.name == objectName) return text;
            return null;
        }
    }
}
