using UnityEngine;
using UnityEngine.UI;
using ZombieSurvival.Core;

namespace ZombieSurvival.UI
{
    // 게임 시작 시 저장 불러오기 결과와 창고 요약을 Canvas 패널에 표시합니다.
    // 초기화와 씬 이동은 BootstrapController가 담당하고, 이 UI는 상태 표시와 수동 입장 버튼 명령만 전달합니다.
    public sealed class BootstrapHud : MonoBehaviour
    {
        [SerializeField] private BootstrapController controller;
        [SerializeField] private Text statusText;
        [SerializeField] private Button enterButton;

        public void Configure(BootstrapController value)
        {
            controller = value;
            Refresh();
        }

        // BootstrapCanvas 프리팹의 상태 Text와 입장 Button을 연결합니다.
        public void ConfigureViews(Text status, Button enter)
        {
            statusText = status;
            enterButton = enter;
            ConfigureButton();
            Refresh();
        }

        private void Awake()
        {
            // 생성기 연결이 빠진 테스트 씬에서도 화면이 멈추지 않도록 같은 씬의 컨트롤러를 한 번 찾습니다.
            if (controller == null) controller = FindObjectOfType<BootstrapController>();
            ConfigureButton();
            Refresh();
        }

        private void Update() => Refresh();

        private void ConfigureButton()
        {
            if (enterButton == null) return;
            enterButton.onClick.RemoveListener(EnterHideout);
            enterButton.onClick.AddListener(EnterHideout);
        }

        private void EnterHideout() => controller?.EnterHideout();

        private void Refresh()
        {
            if (statusText == null) return;
            statusText.text = controller == null
                ? "BootstrapController가 연결되지 않아 게임을 초기화할 수 없습니다."
                : controller.StatusMessage + $"\n창고 {controller.StashStackCount}묶음 · 출격 준비 {controller.LoadoutStackCount}묶음";
            if (enterButton != null) enterButton.interactable = controller != null;
        }

        private void OnDestroy()
        {
            if (enterButton != null) enterButton.onClick.RemoveListener(EnterHideout);
        }
    }
}
