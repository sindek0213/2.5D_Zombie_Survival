using UnityEngine;
using UnityEngine.UI;
using ZombieSurvival.Extraction;

namespace ZombieSurvival.UI
{
    // 탈출 조건, 활성화 상태, 카운트다운과 성공 결과를 화면 아래 중앙의 Canvas Text로 표시합니다.
    // ExtractionController의 상태만 읽고 카운트다운이나 성공 로직은 수정하지 않으므로 화면 디자인과 게임 규칙이 분리됩니다.
    public sealed class ExtractionHud : MonoBehaviour
    {
        [SerializeField] private ExtractionController controller;
        [SerializeField] private GameObject panel;
        [SerializeField] private Text messageText;
        [SerializeField] private Image stateAccent;
        [SerializeField] private Color lockedColor = new Color(0.8f, 0.25f, 0.22f, 1f);
        [SerializeField] private Color availableColor = new Color(0.2f, 0.85f, 0.35f, 1f);
        [SerializeField] private Color succeededColor = new Color(0.2f, 0.9f, 0.9f, 1f);

        public void SetController(ExtractionController value)
        {
            controller = value;
            Refresh();
        }

        public void ConfigureViews(GameObject rootPanel, Text message, Image accent)
        {
            panel = rootPanel;
            messageText = message;
            stateAccent = accent;
            Refresh();
        }

        private void Update() => Refresh();

        private void Refresh()
        {
            bool visible = controller != null && controller.Requirement != null;
            if (panel != null) panel.SetActive(visible);
            if (!visible) return;

            if (messageText != null)
                messageText.text = BuildMessage(
                    controller.State,
                    controller.RemainingTime,
                    controller.Requirement.DisplayName,
                    controller.Requirement.RequiredQuantity,
                    controller.ItemRequirementMet,
                    controller.RaidSession != null ? controller.RaidSession.ZombieKills : 0,
                    controller.RaidSession != null ? controller.RaidSession.RequiredZombieKills : 0,
                    controller.RaidSession != null ? controller.RaidSession.ElapsedTime : 0f,
                    controller.RaidSession != null ? controller.RaidSession.MinimumExtractionTime : 0f);

            if (stateAccent != null)
            {
                stateAccent.color = controller.State == ExtractionController.ExtractionState.Locked
                    ? lockedColor
                    : controller.State == ExtractionController.ExtractionState.Succeeded
                        ? succeededColor
                        : availableColor;
            }
        }

        // 탈출 상태별 문구 생성을 한곳에 모아 화면 코드와 상태 문구 규칙이 흩어지지 않게 합니다.
        public static string BuildMessage(
            ExtractionController.ExtractionState state,
            float remainingTime,
            string requiredItemName,
            int requiredQuantity,
            bool itemRequirementMet = false,
            int zombieKills = 0,
            int requiredZombieKills = 0,
            float elapsedTime = 0f,
            float minimumExtractionTime = 0f)
        {
            switch (state)
            {
                case ExtractionController.ExtractionState.Available:
                    return "탈출 가능 - 녹색 구역으로 이동하세요";
                case ExtractionController.ExtractionState.CountingDown:
                    return $"탈출까지 {remainingTime:0.0}초";
                case ExtractionController.ExtractionState.Succeeded:
                    return "탈출 성공";
                default:
                    if (!itemRequirementMet) return $"탈출 조건: {requiredItemName} x{requiredQuantity}";
                    if (zombieKills < requiredZombieKills)
                        return $"탈출 조건: 좀비 처치 {zombieKills} / {requiredZombieKills}";
                    if (elapsedTime < minimumExtractionTime)
                        return $"탈출 활성까지 {minimumExtractionTime - elapsedTime:0.0}초";
                    return "탈출 조건을 확인하는 중입니다.";
            }
        }
    }
}
