using UnityEngine;
using UnityEngine.UI;
using ZombieSurvival.Hideout;
using ZombieSurvival.Player;

namespace ZombieSurvival.UI
{
    // 거점을 걸어 다닐 때 기본 조작과 가까운 시설의 E키 안내를 보여 주는 작은 Canvas HUD입니다.
    // 창고·임무 화면과 별도 프리팹이므로 시설 화면을 닫아도 월드 안내는 계속 남고 디자인도 Inspector에서 독립적으로 수정할 수 있습니다.
    public sealed class HideoutWorldHud : MonoBehaviour
    {
        [SerializeField] private HideoutWorldController worldController;
        [SerializeField] private PlayerInteraction playerInteraction;
        [SerializeField] private Text titleText;
        [SerializeField] private Text promptText;

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
            RefreshImmediately();
        }

        // 프리팹에 저장된 제목과 안내 글자는 유지하고 현재 씬의 플레이어와 거점 관리자 참조만 연결합니다.
        // 이 메서드 덕분에 HUD 외형을 Canvas에서 직접 수정해도 에디터 생성기가 그 참조를 null로 지우지 않습니다.
        public void SetContext(HideoutWorldController world, PlayerInteraction interaction)
        {
            worldController = world;
            playerInteraction = interaction;
            RefreshImmediately();
        }

        private void Update() => RefreshImmediately();

        public void RefreshImmediately()
        {
            // 월드를 걷는 동안에도 현재 임무와 진행도를 계속 보여 줘 다음 레이드 목표를 잊지 않게 합니다.
            if (titleText != null)
                titleText.text = worldController != null
                    ? "거점 · " + worldController.ActiveMissionSummary
                    : "거점";
            if (promptText == null) return;

            if (worldController != null && worldController.IsFacilityOpen)
            {
                promptText.text = "Tab 또는 Esc · 시설 화면 닫기";
                return;
            }

            HideoutFacility facility = playerInteraction?.Current as HideoutFacility;
            promptText.text = facility != null
                ? facility.InteractionPrompt
                : "WASD 이동 · 시설 가까이에서 E 사용";
        }
    }
}
