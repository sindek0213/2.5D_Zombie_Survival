using UnityEngine;
using ZombieSurvival.Extraction;

namespace ZombieSurvival.UI
{
    // 탈출 조건, 활성화 상태, 남은 시간과 성공 결과를 화면 아래 중앙에 표시합니다.
    // ExtractionController의 상태만 읽으며 카운트다운이나 성공 로직을 UI에서 직접 변경하지 않습니다.
    public sealed class ExtractionHud : MonoBehaviour
    {
        [SerializeField] private ExtractionController controller;
        private GUIStyle style;

        public void SetController(ExtractionController value) => controller = value;

        private void Awake()
        {
            style = new GUIStyle { fontSize = 22, alignment = TextAnchor.MiddleCenter };
            style.normal.textColor = Color.white;
        }

        private void OnGUI()
        {
            if (controller == null || controller.Requirement == null) return;

            string message;
            switch (controller.State)
            {
                case ExtractionController.ExtractionState.Available:
                    message = "탈출 가능 - 녹색 구역으로 이동하세요";
                    break;
                case ExtractionController.ExtractionState.CountingDown:
                    message = $"탈출까지 {controller.RemainingTime:0.0}초";
                    break;
                case ExtractionController.ExtractionState.Succeeded:
                    message = "탈출 성공";
                    break;
                default:
                    message = $"탈출 조건: {controller.Requirement.DisplayName} x{controller.Requirement.RequiredQuantity}";
                    break;
            }

            GUI.Label(new Rect(Screen.width * 0.5f - 280f, Screen.height - 60f, 560f, 40f), message, style);
        }
    }
}

