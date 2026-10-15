using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace ZombieSurvival.UI
{
    // 아이템을 길게 눌러 연 상세 정보를 유지하고, 팝업 바깥 클릭과 상단 제목 드래그 입력을 처리합니다.
    // 배경·제목 바·닫기 버튼·본문을 각각 독립된 uGUI 오브젝트로 구성해 프리팹 Inspector에서 모양과 위치를 쉽게 바꿀 수 있습니다.
    public sealed class ItemDetailPopup : MonoBehaviour,
        IPointerClickHandler, IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        [Header("Canvas 구성")]
        [SerializeField] private RectTransform popupPanel;
        [SerializeField] private Text contentText;
        [SerializeField] private RectTransform overlayRoot;
        [SerializeField] private RectTransform dismissArea;
        [SerializeField] private RectTransform dragHandle;
        [SerializeField] private Text titleText;
        [SerializeField] private Button closeButton;

        [Header("내용에 따른 크기")]
        // 짧은 이름만 있어도 읽을 수 있는 최소 크기와 긴 무기 설명이 화면 대부분을 가리지 않는 최대 크기입니다.
        [SerializeField] private Vector2 minimumSize = new Vector2(300f, 124f);
        [SerializeField] private Vector2 maximumSize = new Vector2(560f, 660f);
        [SerializeField] private Vector2 contentPadding = new Vector2(32f, 26f);
        [SerializeField, Min(28f)] private float headerHeight = 42f;
        [SerializeField, Min(0f)] private float pointerGap = 18f;

        private Vector2 lastScreenPosition;
        private Camera lastEventCamera;
        private Vector2 dragPointerOffset;
        private bool isDragging;
        private bool hasBeenDragged;

        public bool IsVisible => popupPanel != null && popupPanel.gameObject.activeSelf;
        // HUD는 바깥 클릭이나 X 버튼으로 팝업이 닫혔을 때 현재 표시 중인 InventoryEntry 참조를 함께 비웁니다.
        public event Action Closed;

        // 예전 프리팹의 패널과 본문을 그대로 받아 제목 바·닫기 버튼·바깥 클릭 영역만 보충합니다.
        // 이 메서드를 Editor 생성기에서도 호출하므로 새 구조가 런타임 임시 오브젝트가 아니라 수정 가능한 프리팹 자식으로 저장됩니다.
        public void Configure(RectTransform panel, Text text, RectTransform overlay)
        {
            popupPanel = panel;
            contentText = text;
            overlayRoot = overlay;
            if (popupPanel == null || contentText == null || overlayRoot == null) return;

            if (popupPanel.parent != overlayRoot) popupPanel.SetParent(overlayRoot, false);
            popupPanel.anchorMin = Vector2.zero;
            popupPanel.anchorMax = Vector2.zero;
            popupPanel.pivot = new Vector2(0f, 1f);
            popupPanel.localScale = Vector3.one;

            EnsureEditableViewStructure();
            ConfigureRaycastTargets();
            ConfigureContentText();

            if (closeButton != null)
            {
                closeButton.onClick.RemoveListener(Hide);
                closeButton.onClick.AddListener(Hide);
            }

            if (dismissArea != null) dismissArea.gameObject.SetActive(false);
            popupPanel.gameObject.SetActive(false);
        }

        // 이전 호출부와 수동 프리팹의 호환을 위해 제목을 생략한 오버로드는 공통 제목을 사용합니다.
        public void Show(string content, Vector2 screenPosition, Camera eventCamera)
            => Show("아이템 정보", content, screenPosition, eventCamera);

        // 마우스 위치를 기억한 뒤 내용을 먼저 배치합니다. 크기를 계산한 다음 위치를 정해야 긴 설명도 화면 밖으로 나가지 않습니다.
        public void Show(string title, string content, Vector2 screenPosition, Camera eventCamera)
        {
            if (popupPanel == null || contentText == null || overlayRoot == null) return;

            lastScreenPosition = screenPosition;
            lastEventCamera = eventCamera;
            hasBeenDragged = false;
            isDragging = false;
            if (titleText != null) titleText.text = string.IsNullOrWhiteSpace(title) ? "아이템 정보" : title;
            if (dismissArea != null)
            {
                dismissArea.gameObject.SetActive(true);
                dismissArea.SetAsLastSibling();
            }
            popupPanel.gameObject.SetActive(true);
            popupPanel.SetAsLastSibling();
            SetContent(content);
        }

        public void Hide()
        {
            bool wasVisible = IsVisible;
            isDragging = false;
            if (dismissArea != null) dismissArea.gameObject.SetActive(false);
            if (popupPanel != null) popupPanel.gameObject.SetActive(false);
            if (wasVisible) Closed?.Invoke();
        }

        // 사용 진행률처럼 팝업이 열린 동안 바뀔 수 있는 정보는 패널을 새로 만들지 않고 Text와 크기만 다시 계산합니다.
        public void SetContent(string content)
        {
            if (contentText == null || popupPanel == null) return;
            contentText.text = content ?? string.Empty;
            if (!popupPanel.gameObject.activeSelf) return;

            ResizeToContent();
            if (hasBeenDragged) ClampCurrentPosition();
            else Reposition(lastScreenPosition, lastEventCamera);
        }

        // 전체 화면 투명 영역을 클릭하면 팝업을 닫습니다. 패널 내부 클릭은 정보창 조작이므로 유지합니다.
        public void OnPointerClick(PointerEventData eventData)
        {
            if (!IsVisible || eventData == null) return;
            GameObject pressedObject = eventData.pointerPressRaycast.gameObject;
            if (pressedObject != null && pressedObject.transform.IsChildOf(popupPanel)) return;
            Hide();
        }

        public void OnBeginDrag(PointerEventData eventData)
        {
            if (!IsVisible || eventData == null || eventData.button != PointerEventData.InputButton.Left ||
                !WasPressedInsideDragHandle(eventData)) return;

            if (!TryGetPointerFromBottomLeft(eventData.position, eventData.pressEventCamera, out Vector2 pointer)) return;
            dragPointerOffset = popupPanel.anchoredPosition - pointer;
            isDragging = true;
            hasBeenDragged = true;
            popupPanel.SetAsLastSibling();
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (!isDragging || eventData == null ||
                !TryGetPointerFromBottomLeft(eventData.position, eventData.pressEventCamera, out Vector2 pointer)) return;

            popupPanel.anchoredPosition = ClampAnchoredPosition(
                pointer + dragPointerOffset,
                overlayRoot.rect.size,
                popupPanel.sizeDelta);
        }

        public void OnEndDrag(PointerEventData eventData)
        {
            if (eventData != null && eventData.button == PointerEventData.InputButton.Left) isDragging = false;
        }

        private bool WasPressedInsideDragHandle(PointerEventData eventData)
        {
            GameObject pressedObject = eventData.pointerPressRaycast.gameObject;
            if (pressedObject == null || dragHandle == null || !pressedObject.transform.IsChildOf(dragHandle)) return false;
            // X 버튼을 누른 동작이 창 이동으로 시작되지 않게 별도로 제외합니다.
            return closeButton == null || !pressedObject.transform.IsChildOf(closeButton.transform);
        }

        private void EnsureEditableViewStructure()
        {
            // 패널 바깥 전체를 덮는 투명 Image는 다른 UI를 직접 수정하지 않고도 클릭을 받아 팝업을 닫습니다.
            if (dismissArea == null)
            {
                Transform existingDismiss = overlayRoot.Find("ItemDetailDismissArea");
                if (existingDismiss != null) dismissArea = existingDismiss as RectTransform;
            }
            if (dismissArea == null)
            {
                var dismissObject = new GameObject(
                    "ItemDetailDismissArea",
                    typeof(RectTransform),
                    typeof(CanvasRenderer),
                    typeof(Image));
                dismissObject.transform.SetParent(overlayRoot, false);
                dismissArea = dismissObject.GetComponent<RectTransform>();
                Image dismissImage = dismissObject.GetComponent<Image>();
                // 알파가 0이어도 Image의 RaycastTarget은 정상 동작하므로 화면을 조금도 어둡게 만들 필요가 없습니다.
                dismissImage.color = Color.clear;
                dismissImage.raycastTarget = true;
            }
            Stretch(dismissArea);

            if (popupPanel.GetComponent<Image>() == null)
            {
                Image background = popupPanel.gameObject.AddComponent<Image>();
                background.color = new Color(0.045f, 0.06f, 0.07f, 0.98f);
            }

            if (dragHandle == null)
            {
                Transform existingHeader = popupPanel.Find("Header");
                if (existingHeader != null) dragHandle = existingHeader as RectTransform;
            }
            if (dragHandle == null)
            {
                var headerObject = new GameObject("Header", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
                headerObject.transform.SetParent(popupPanel, false);
                dragHandle = headerObject.GetComponent<RectTransform>();
                Image headerImage = headerObject.GetComponent<Image>();
                headerImage.color = new Color(0.1f, 0.14f, 0.15f, 1f);
                headerImage.raycastTarget = true;
            }
            SetTopStretch(dragHandle, headerHeight);

            if (titleText == null) titleText = dragHandle.Find("Title")?.GetComponent<Text>();
            if (titleText == null)
            {
                var titleObject = new GameObject("Title", typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
                titleObject.transform.SetParent(dragHandle, false);
                titleText = titleObject.GetComponent<Text>();
                // Unity 2022.3의 기본 폰트 이름인 LegacyRuntime.ttf를 사용해 새 프리팹이나 테스트 UI에서도 경고 없이 제목을 표시합니다.
                titleText.font = contentText.font != null
                    ? contentText.font
                    : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                titleText.fontSize = Mathf.Max(15, contentText.fontSize);
                titleText.fontStyle = FontStyle.Bold;
                titleText.color = new Color(0.88f, 0.91f, 0.85f, 1f);
                titleText.alignment = TextAnchor.MiddleLeft;
                titleText.raycastTarget = false;
            }
            Stretch(titleText.rectTransform, new Vector2(14f, 3f), new Vector2(-52f, -3f));

            if (closeButton == null) closeButton = dragHandle.Find("CloseButton")?.GetComponent<Button>();
            if (closeButton == null)
            {
                var closeObject = new GameObject(
                    "CloseButton",
                    typeof(RectTransform),
                    typeof(CanvasRenderer),
                    typeof(Image),
                    typeof(Button));
                closeObject.transform.SetParent(dragHandle, false);
                closeButton = closeObject.GetComponent<Button>();
                Image closeImage = closeObject.GetComponent<Image>();
                closeImage.color = new Color(0.28f, 0.11f, 0.1f, 1f);
                closeImage.raycastTarget = true;
                RectTransform closeRect = closeObject.GetComponent<RectTransform>();
                closeRect.anchorMin = new Vector2(1f, 0.5f);
                closeRect.anchorMax = new Vector2(1f, 0.5f);
                closeRect.pivot = new Vector2(1f, 0.5f);
                closeRect.anchoredPosition = new Vector2(-5f, 0f);
                closeRect.sizeDelta = new Vector2(34f, 30f);

                var closeTextObject = new GameObject("Label", typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
                closeTextObject.transform.SetParent(closeObject.transform, false);
                Text closeText = closeTextObject.GetComponent<Text>();
                closeText.font = titleText.font;
                closeText.fontSize = 20;
                closeText.fontStyle = FontStyle.Bold;
                closeText.color = Color.white;
                closeText.alignment = TextAnchor.MiddleCenter;
                closeText.text = "×";
                closeText.raycastTarget = false;
                Stretch(closeText.rectTransform);
            }

            dismissArea.SetAsLastSibling();
            popupPanel.SetAsLastSibling();
        }

        private void ConfigureRaycastTargets()
        {
            // 바깥 클릭 영역은 화면 전체의 포인터 입력만 받아야 하며 어떤 색도 그리면 안 됩니다.
            // 이전 프리팹에서는 이 오브젝트가 옛 설명 패널의 불투명 배경색을 가지고 있어 팝업을 열 때 화면이 검게 덮였습니다.
            // 알파를 완전히 0으로 강제해 팝업을 열기 직전의 인벤토리 화면을 그대로 보존하면서 RaycastTarget은 유지합니다.
            Image dismissImage = dismissArea != null ? dismissArea.GetComponent<Image>() : null;
            if (dismissImage != null)
            {
                dismissImage.color = Color.clear;
                dismissImage.raycastTarget = true;
            }

            // 패널 배경과 제목 바만 입력을 받고 본문 Text는 배경으로 이벤트가 전달되게 합니다.
            Image panelImage = popupPanel.GetComponent<Image>();
            if (panelImage != null) panelImage.raycastTarget = true;
            if (contentText != null) contentText.raycastTarget = false;
            if (titleText != null) titleText.raycastTarget = false;
        }

        private void ConfigureContentText()
        {
            contentText.alignment = TextAnchor.UpperLeft;
            contentText.horizontalOverflow = HorizontalWrapMode.Wrap;
            contentText.verticalOverflow = VerticalWrapMode.Truncate;
        }

        private void ResizeToContent()
        {
            RectTransform textRect = contentText.rectTransform;

            // 줄바꿈 전 자연스러운 가로 길이를 구한 뒤 최대 폭으로 제한합니다.
            contentText.horizontalOverflow = HorizontalWrapMode.Overflow;
            float preferredWidth = contentText.preferredWidth;
            contentText.horizontalOverflow = HorizontalWrapMode.Wrap;
            float width = Mathf.Clamp(preferredWidth + contentPadding.x, minimumSize.x, maximumSize.x);

            textRect.anchorMin = new Vector2(0f, 1f);
            textRect.anchorMax = new Vector2(0f, 1f);
            textRect.pivot = new Vector2(0f, 1f);
            textRect.anchoredPosition = new Vector2(contentPadding.x * 0.5f, -headerHeight - contentPadding.y * 0.5f);
            textRect.sizeDelta = new Vector2(Mathf.Max(1f, width - contentPadding.x), maximumSize.y);
            Canvas.ForceUpdateCanvases();

            float preferredHeight = contentText.preferredHeight;
            Vector2 size = CalculatePopupSize(
                width,
                preferredHeight + contentPadding.y + headerHeight,
                minimumSize,
                maximumSize);
            popupPanel.sizeDelta = size;
            textRect.sizeDelta = new Vector2(
                Mathf.Max(1f, size.x - contentPadding.x),
                Mathf.Max(1f, size.y - contentPadding.y - headerHeight));
            SetTopStretch(dragHandle, headerHeight);
        }

        private void Reposition(Vector2 screenPosition, Camera eventCamera)
        {
            if (!TryGetPointerFromBottomLeft(screenPosition, eventCamera, out Vector2 pointer)) return;
            popupPanel.anchoredPosition = CalculateAnchoredPosition(
                pointer,
                overlayRoot.rect.size,
                popupPanel.sizeDelta,
                pointerGap);
        }

        private bool TryGetPointerFromBottomLeft(Vector2 screenPosition, Camera eventCamera, out Vector2 pointer)
        {
            pointer = Vector2.zero;
            if (overlayRoot == null || !RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    overlayRoot,
                    screenPosition,
                    eventCamera,
                    out Vector2 localPoint)) return false;
            pointer = localPoint - overlayRoot.rect.min;
            return true;
        }

        private void ClampCurrentPosition()
        {
            popupPanel.anchoredPosition = ClampAnchoredPosition(
                popupPanel.anchoredPosition,
                overlayRoot.rect.size,
                popupPanel.sizeDelta);
        }

        public static Vector2 CalculatePopupSize(
            float preferredWidth,
            float preferredHeight,
            Vector2 minimum,
            Vector2 maximum)
        {
            float minWidth = Mathf.Min(minimum.x, maximum.x);
            float maxWidth = Mathf.Max(minimum.x, maximum.x);
            float minHeight = Mathf.Min(minimum.y, maximum.y);
            float maxHeight = Mathf.Max(minimum.y, maximum.y);
            return new Vector2(
                Mathf.Clamp(preferredWidth, minWidth, maxWidth),
                Mathf.Clamp(preferredHeight, minHeight, maxHeight));
        }

        // 커서 오른쪽 아래를 우선 사용하고 공간이 부족하면 반대편으로 뒤집은 뒤 최종적으로 화면 안에 고정합니다.
        // 반환값은 왼쪽 아래 Anchor와 왼쪽 위 Pivot을 사용하는 팝업의 anchoredPosition입니다.
        public static Vector2 CalculateAnchoredPosition(
            Vector2 pointerFromBottomLeft,
            Vector2 containerSize,
            Vector2 popupSize,
            float gap)
        {
            float safeGap = Mathf.Max(0f, gap);
            float x = pointerFromBottomLeft.x + safeGap;
            float topY = pointerFromBottomLeft.y - safeGap;

            if (x + popupSize.x > containerSize.x)
                x = pointerFromBottomLeft.x - popupSize.x - safeGap;
            if (topY - popupSize.y < 0f)
                topY = pointerFromBottomLeft.y + popupSize.y + safeGap;

            return ClampAnchoredPosition(new Vector2(x, topY), containerSize, popupSize);
        }

        // 드래그 중 원하는 위치가 화면 밖이어도 팝업 전체가 Canvas 안에 남도록 왼쪽 X와 위쪽 Y를 제한합니다.
        public static Vector2 ClampAnchoredPosition(Vector2 desired, Vector2 containerSize, Vector2 popupSize)
        {
            float maximumX = Mathf.Max(0f, containerSize.x - popupSize.x);
            float minimumTopY = Mathf.Min(containerSize.y, popupSize.y);
            return new Vector2(
                Mathf.Clamp(desired.x, 0f, maximumX),
                Mathf.Clamp(desired.y, minimumTopY, containerSize.y));
        }

        private static void Stretch(RectTransform rect)
            => Stretch(rect, Vector2.zero, Vector2.zero);

        private static void Stretch(RectTransform rect, Vector2 offsetMin, Vector2 offsetMax)
        {
            if (rect == null) return;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.offsetMin = offsetMin;
            rect.offsetMax = offsetMax;
            rect.localScale = Vector3.one;
        }

        private static void SetTopStretch(RectTransform rect, float height)
        {
            if (rect == null) return;
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = new Vector2(0f, Mathf.Max(1f, height));
            rect.localScale = Vector3.one;
        }
    }
}
