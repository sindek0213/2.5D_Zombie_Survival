using UnityEngine;
using UnityEngine.UI;
using ZombieSurvival.Player;

namespace ZombieSurvival.UI
{
    // 레이드 화면에 출혈·골절 상태와 실제 디버프를 표시하고, 출혈 중에는 화면 가장자리에 붉은 경고를 보여 줍니다.
    // 이 클래스는 화면 표현만 담당하며 상태 발생과 치료 판정은 PlayerCondition이 처리합니다.
    public sealed class PlayerConditionHud : MonoBehaviour
    {
        [SerializeField] private PlayerCondition playerCondition;
        [SerializeField] private GameObject conditionPanel;
        [SerializeField] private Text summaryText;
        [SerializeField] private Text bleedingText;
        [SerializeField] private Text fractureText;
        [SerializeField] private CanvasGroup bleedingBorderGroup;

        public void SetPlayerCondition(PlayerCondition value)
        {
            playerCondition = value;
            Refresh();
        }

        // 프리팹 생성기가 만든 상태 문구와 화면 테두리를 명시적으로 연결합니다.
        // 참조를 직렬화해 두면 런타임마다 이름으로 자식을 다시 찾지 않아도 됩니다.
        public void ConfigureViews(
            GameObject panel,
            Text summary,
            Text bleeding,
            Text fracture,
            CanvasGroup bleedingBorder)
        {
            conditionPanel = panel;
            summaryText = summary;
            bleedingText = bleeding;
            fractureText = fracture;
            bleedingBorderGroup = bleedingBorder;
            Refresh();
        }

        private void Awake()
        {
            // 수동으로 프리팹을 배치한 씬도 동작하도록 명시 참조가 없을 때만 플레이어 상태를 한 번 찾습니다.
            if (playerCondition == null) playerCondition = FindObjectOfType<PlayerCondition>();
            Refresh();
        }

        private void Update() => Refresh();

        private void Refresh()
        {
            bool hasConditionSystem = playerCondition != null;
            if (conditionPanel != null) conditionPanel.SetActive(hasConditionSystem);

            bool bleeding = hasConditionSystem && playerCondition.IsBleeding;
            RefreshBleedingBorder(bleeding);
            if (!hasConditionSystem) return;

            if (summaryText != null)
            {
                int movement = Mathf.RoundToInt(playerCondition.MovementMultiplier * 100f);
                int aimPenalty = Mathf.RoundToInt((playerCondition.AimSpreadMultiplier - 1f) * 100f);
                int reloadPenalty = Mathf.RoundToInt((playerCondition.ReloadDurationMultiplier - 1f) * 100f);
                summaryText.text = $"{playerCondition.BuildStatusSummary()}\n이동 {movement}% · 조준 오차 +{aimPenalty}% · 재장전 +{reloadPenalty}%";
                summaryText.color = playerCondition.IsBleeding || playerCondition.HasFracture
                    ? SurvivalUiTheme.Danger
                    : SurvivalUiTheme.TextPrimary;
            }

            if (bleedingText != null)
            {
                bleedingText.text = bleeding
                    ? "출혈  진행 중 · 붕대 필요"
                    : "출혈  없음";
                bleedingText.color = bleeding ? SurvivalUiTheme.Danger : SurvivalUiTheme.TextPrimary;
            }

            if (fractureText != null)
            {
                fractureText.text = playerCondition.HasFracture
                    ? "골절  진행 중 · 부목 필요"
                    : "골절  없음";
                fractureText.color = playerCondition.HasFracture
                    ? SurvivalUiTheme.Warning
                    : SurvivalUiTheme.TextPrimary;
            }
        }

        private void RefreshBleedingBorder(bool bleeding)
        {
            if (bleedingBorderGroup == null) return;
            bleedingBorderGroup.gameObject.SetActive(bleeding);
            if (!bleeding) return;

            // 완전히 고정된 테두리보다 천천히 밝기가 오르내리게 해 현재 출혈 중이라는 사실을 주변 시야로 알아차리게 합니다.
            // 화면 중앙은 투명하게 비워 조준과 아이템 확인을 방해하지 않습니다.
            bleedingBorderGroup.alpha = CalculateBleedingBorderAlpha(Time.unscaledTime);
        }

        // 시간에 따른 테두리 알파 계산을 순수 함수로 분리해 Canvas 없이도 경계 범위를 테스트할 수 있습니다.
        public static float CalculateBleedingBorderAlpha(float unscaledTime)
        {
            float pulse = (Mathf.Sin(Mathf.Max(0f, unscaledTime) * 3.5f) + 1f) * 0.5f;
            return Mathf.Lerp(0.32f, 0.58f, pulse);
        }
    }
}
