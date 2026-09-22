using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using ZombieSurvival.Art;
using ZombieSurvival.Inventory;
using ZombieSurvival.Items;

namespace ZombieSurvival.UI
{
    // InventoryEntry 하나를 실제 점유 모양대로 여러 Image 칸으로 그리는 재사용 UI 프리팹입니다.
    // 루트 전체에 클릭 이미지를 두지 않고 점유 칸 Image만 Raycast를 받으므로 L자 아이템의 빈 모서리를 다른 아이템이 정상적으로 사용할 수 있습니다.
    public sealed class InventoryItemView : MonoBehaviour,
        IPointerClickHandler, IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        [SerializeField] private RectTransform occupiedLayer;
        [SerializeField] private Image occupiedCellPrefab;
        // iconImage는 ItemDefinition에 연결된 실제 원화를 점유 칸 위에 한 번만 표시합니다.
        // 칸마다 아이콘을 복제하지 않으므로 소총처럼 여러 칸인 아이템도 한 장의 온전한 그림으로 보입니다.
        [SerializeField] private Image iconImage;
        [SerializeField] private Text labelText;
        [SerializeField] private CanvasGroup canvasGroup;
        [SerializeField] private Color normalColor = new Color(0.25f, 0.48f, 0.34f, 1f);
        [SerializeField] private Color selectedColor = new Color(0.15f, 0.8f, 0.86f, 1f);
        [SerializeField] private Color dragColor = new Color(0.3f, 0.9f, 0.65f, 0.78f);

        private readonly List<GameObject> generatedCells = new List<GameObject>();
        private RectTransform rootRect;
        private InventoryEntry entry;
        private InventoryGridView gridView;
        private IInventoryGridViewOwner owner;
        private Color currentColor;
        private Vector3 dragScreenDelta;

        public InventoryEntry Entry => entry;

        // 프리팹 생성 시 모양 칸을 넣을 부모, 칸 이미지와 한 번만 표시할 이름 Text를 연결합니다.
        public void Configure(RectTransform shapeRoot, Image cellTemplate, Text label)
        {
            occupiedLayer = shapeRoot;
            occupiedCellPrefab = cellTemplate;
            labelText = label;
            canvasGroup = GetComponent<CanvasGroup>();
        }

        // 생성된 리소스 적용 도구가 기존 인벤토리 프리팹에 새 Icon 자식을 추가한 뒤 참조를 연결할 때 사용합니다.
        // 이미지 생성과 데이터 처리는 에디터에서 한 번만 하고, 런타임 Bind는 연결된 Image의 Sprite만 교체합니다.
        public void SetIconView(Image image)
        {
            iconImage = image;
            if (iconImage != null) iconImage.raycastTarget = false;
        }

        private void Awake()
        {
            rootRect = (RectTransform)transform;
            if (canvasGroup == null) canvasGroup = GetComponent<CanvasGroup>();
            EnsureIconView();
        }

        // 회전된 Width/Height로 바운딩 영역을 만들고 마스크가 켜진 칸에만 Image 프리팹을 생성합니다.
        // 아이템 이름과 수량은 첫 칸 위에 한 번만 표시하여 여러 칸짜리 아이템에서도 글자가 반복되지 않습니다.
        public void Bind(
            InventoryEntry value,
            InventoryGridView parentGrid,
            IInventoryGridViewOwner hud,
            bool isSelected,
            float cellSize)
        {
            entry = value;
            gridView = parentGrid;
            owner = hud;
            if (rootRect == null) rootRect = (RectTransform)transform;
            ClearCells();
            // 같은 View가 다른 항목에 재사용될 때 이전 아이콘이 한 프레임 남지 않도록 먼저 숨깁니다.
            if (iconImage != null) iconImage.gameObject.SetActive(false);
            if (entry == null) return;

            SetTopLeft(rootRect);
            rootRect.anchoredPosition = new Vector2(entry.Position.x * cellSize, -entry.Position.y * cellSize);
            rootRect.sizeDelta = new Vector2(entry.Width * cellSize, entry.Height * cellSize);
            ConfigureGridSizedIconLayout(cellSize);
            // 선택 중에는 공통 강조색을 사용하고, 평소에는 아이템 희귀도에 맞는 색으로 파밍 가치를 빠르게 구분합니다.
            currentColor = isSelected ? selectedColor : GetRarityColor(entry.Item.Rarity, normalColor);

            if (iconImage != null)
            {
                // 생성 원화가 있는 아이템은 큰 캔버스에서 실제 장비 부분만 잘라 만든 Sprite를 우선 사용합니다.
                // 전용 생성 원화가 없는 아이템만 ItemDefinition에 직접 연결된 기존 아이콘으로 되돌아갑니다.
                Sprite croppedGeneratedIcon = GeneratedArtResources.GetItemIcon(entry.Item.ItemId);
                iconImage.sprite = croppedGeneratedIcon != null ? croppedGeneratedIcon : entry.Item.Icon;
                // 아이콘의 표시 사각형 자체가 아이템의 Width×Height 격자 크기이므로 원본 PNG의 큰 캔버스 비율을 다시 적용하지 않습니다.
                // 예를 들어 5×2 소총은 한 칸으로 줄어들지 않고 정확히 가로 5칸·세로 2칸 영역을 채웁니다.
                iconImage.preserveAspect = false;
                iconImage.raycastTarget = false;
                iconImage.gameObject.SetActive(iconImage.sprite != null);
            }

            if (occupiedCellPrefab != null && occupiedLayer != null)
            {
                for (int y = 0; y < entry.Height; y++)
                for (int x = 0; x < entry.Width; x++)
                {
                    if (!entry.OccupiesLocalCell(x, y)) continue;
                    Image cell = Instantiate(occupiedCellPrefab, occupiedLayer);
                    cell.name = $"ShapeCell_{x}_{y}";
                    cell.color = currentColor;
                    RectTransform cellRect = cell.rectTransform;
                    SetTopLeft(cellRect);
                    cellRect.anchoredPosition = new Vector2(x * cellSize + 2f, -y * cellSize - 2f);
                    cellRect.sizeDelta = new Vector2(cellSize - 4f, cellSize - 4f);
                    generatedCells.Add(cell.gameObject);
                }
            }

            if (labelText != null)
            {
                labelText.text = entry.Item.DisplayName + "\nx" + entry.Quantity;
                labelText.raycastTarget = false;
            }
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            owner?.HandleItemClick(entry, gridView?.Data, eventData.button,
                Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift),
                Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl));
        }

        public void OnBeginDrag(PointerEventData eventData)
        {
            if (eventData.button != PointerEventData.InputButton.Left || entry == null || gridView == null) return;
            Vector2 pointerOffset = gridView.GetPointerOffset(entry, eventData.position, eventData.pressEventCamera);
            owner?.BeginItemDrag(this, entry, gridView.Data, pointerOffset, eventData);
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (eventData.button == PointerEventData.InputButton.Left) owner?.UpdateItemDrag(eventData);
        }

        public void OnEndDrag(PointerEventData eventData)
        {
            if (eventData.button == PointerEventData.InputButton.Left) owner?.EndItemDrag(eventData);
        }

        // 드래그하는 동안 다른 격자가 마우스 아래에서 감지되도록 이 아이템의 Raycast를 잠시 끕니다.
        // 미리보기는 Canvas의 맨 위 DragLayer로 옮겨 다른 아이템이나 칸 뒤에 가려지지 않게 합니다.
        public void SetDragging(bool value, RectTransform targetLayer, Vector2 screenPosition)
        {
            if (canvasGroup != null)
            {
                canvasGroup.blocksRaycasts = !value;
                canvasGroup.alpha = value ? dragColor.a : 1f;
            }

            foreach (GameObject cellObject in generatedCells)
            {
                Image cell = cellObject != null ? cellObject.GetComponent<Image>() : null;
                if (cell != null) cell.color = value ? dragColor : currentColor;
            }

            if (value && targetLayer != null)
            {
                // 부모를 바꾸기 전에 현재 아이템 왼쪽 위와 마우스 사이의 화면상 거리를 저장합니다.
                // 이 값을 계속 더해 주면 큰 아이템의 가운데를 잡아도 미리보기가 마우스 위치로 갑자기 점프하지 않습니다.
                dragScreenDelta = rootRect.position - new Vector3(screenPosition.x, screenPosition.y, 0f);
                transform.SetParent(targetLayer, true);
                transform.SetAsLastSibling();
                SetDragScreenPosition(screenPosition);
            }
        }

        public void SetDragScreenPosition(Vector2 screenPosition)
        {
            if (rootRect == null) rootRect = (RectTransform)transform;
            rootRect.position = new Vector3(screenPosition.x, screenPosition.y, rootRect.position.z) + dragScreenDelta;
        }

        // Common은 프리팹에서 사용자가 지정한 기본색을 보존하고, 그 이상 등급만 고정된 식별색을 사용합니다.
        // 순수 함수로 분리해 Canvas를 만들지 않고도 모든 등급의 색상 규칙을 테스트할 수 있습니다.
        public static Color GetRarityColor(ItemRarity rarity, Color commonColor)
        {
            switch (rarity)
            {
                case ItemRarity.Uncommon: return new Color(0.24f, 0.62f, 0.34f, 1f);
                case ItemRarity.Rare: return new Color(0.25f, 0.46f, 0.82f, 1f);
                case ItemRarity.Quest: return new Color(0.90f, 0.66f, 0.18f, 1f);
                default: return commonColor;
            }
        }

        private void ClearCells()
        {
            foreach (GameObject generated in generatedCells)
            {
                if (generated == null) continue;
                if (Application.isPlaying) Destroy(generated);
                else DestroyImmediate(generated);
            }
            generatedCells.Clear();
        }

        private void EnsureIconView()
        {
            if (iconImage == null)
            {
                Transform existing = transform.Find("ItemIcon");
                if (existing != null) iconImage = existing.GetComponent<Image>();
            }

            if (iconImage == null)
            {
                // 이전 버전의 프리팹에도 바로 표시되도록 런타임에 아이콘 전용 Image를 한 번 보충합니다.
                // RectTransform을 아이템 전체에 맞추되 Raycast는 끄므로 비정형 아이템의 빈 모서리 입력 규칙은 그대로 유지됩니다.
                var iconObject = new GameObject("ItemIcon", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
                iconObject.transform.SetParent(transform, false);
                iconImage = iconObject.GetComponent<Image>();
            }

            iconImage.material = GeneratedArtResources.GetUiWhiteKeyMaterial();
            iconImage.preserveAspect = false;
            iconImage.raycastTarget = false;
            if (labelText != null) labelText.transform.SetAsLastSibling();
        }

        private void ConfigureGridSizedIconLayout(float cellSize)
        {
            if (entry == null || iconImage == null) return;

            RectTransform iconRect = iconImage.rectTransform;
            iconRect.anchorMin = new Vector2(0.5f, 0.5f);
            iconRect.anchorMax = new Vector2(0.5f, 0.5f);
            iconRect.pivot = new Vector2(0.5f, 0.5f);
            iconRect.anchoredPosition = Vector2.zero;

            // 아이콘 한 장을 아이템이 점유하는 전체 바운딩 격자에 맞춥니다.
            // 칸마다 그림을 복제하는 방식이 아니므로 5×2 아이템은 하나의 큰 그림으로 10개 칸 위에 이어져 보입니다.
            const float totalPadding = 6f;
            float displayedWidth = Mathf.Max(1f, entry.Width * cellSize - totalPadding);
            float displayedHeight = Mathf.Max(1f, entry.Height * cellSize - totalPadding);

            if (entry.Rotated)
            {
                // 인벤토리의 회전 규칙은 시계 방향 90도입니다. 자식 Rect의 가로·세로를 먼저 뒤집은 뒤
                // 실제 그림도 같은 방향으로 회전해야 회전된 점유 마스크 안에 정확히 들어갑니다.
                iconRect.sizeDelta = new Vector2(displayedHeight, displayedWidth);
                iconRect.localRotation = Quaternion.Euler(0f, 0f, -90f);
            }
            else
            {
                iconRect.sizeDelta = new Vector2(displayedWidth, displayedHeight);
                iconRect.localRotation = Quaternion.identity;
            }
        }

        private static void SetTopLeft(RectTransform rect)
        {
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
        }
    }
}
