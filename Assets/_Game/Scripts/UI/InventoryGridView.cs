using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using ZombieSurvival.Inventory;

namespace ZombieSurvival.UI
{
    // 하나의 PlayerInventory.GridData를 Canvas의 격자 칸과 아이템 프리팹으로 바꿔 보여 줍니다.
    // 루트 인벤토리, 가방 내부와 조끼 내부가 모두 같은 구조이므로 이 프리팹 하나를 재사용할 수 있습니다.
    public sealed class InventoryGridView : MonoBehaviour
    {
        [SerializeField] private RectTransform gridArea;
        [SerializeField] private RectTransform cellLayer;
        [SerializeField] private RectTransform itemLayer;
        [SerializeField] private Image cellPrefab;
        [SerializeField] private InventoryItemView itemPrefab;
        [SerializeField] private Text titleText;
        [SerializeField] private Button autoSortButton;
        [SerializeField, Min(16f)] private float cellSize = 50f;

        private readonly List<GameObject> generatedObjects = new List<GameObject>();
        private readonly List<GameObject> previewObjects = new List<GameObject>();
        private IInventoryGridViewOwner owner;

        public PlayerInventory.GridData Data { get; private set; }
        public float CellSize => cellSize;

        // 에디터 생성기가 자식 오브젝트와 재사용 프리팹 참조를 연결합니다.
        // 생성된 InventoryGrid 프리팹에서 이 값들을 바꾸면 모든 루트/가방/조끼 화면에 같은 디자인이 적용됩니다.
        public void Configure(
            RectTransform area,
            RectTransform cells,
            RectTransform items,
            Image cellTemplate,
            InventoryItemView itemTemplate,
            Text title,
            Button sortButton)
        {
            gridArea = area;
            cellLayer = cells;
            itemLayer = items;
            cellPrefab = cellTemplate;
            itemPrefab = itemTemplate;
            titleText = title;
            autoSortButton = sortButton;
        }

        // 전달받은 격자 크기에 맞춰 바닥 칸을 만들고 현재 InventoryEntry마다 아이템 프리팹을 하나씩 생성합니다.
        // InventoryChanged가 올 때 전체를 다시 그리므로 복잡한 교환 뒤에도 화면과 실제 데이터가 어긋나지 않습니다.
        public void Bind(PlayerInventory.GridData data, IInventoryGridViewOwner hud, string title, InventoryEntry selected)
        {
            Data = data;
            owner = hud;
            ClearGeneratedObjects();
            if (Data == null || gridArea == null || cellLayer == null || itemLayer == null) return;

            if (titleText != null) titleText.text = title;
            Vector2 pixelSize = new Vector2(Data.Width * cellSize, Data.Height * cellSize);
            gridArea.sizeDelta = pixelSize;
            cellLayer.sizeDelta = pixelSize;
            itemLayer.sizeDelta = pixelSize;

            if (cellPrefab != null)
            {
                for (int y = 0; y < Data.Height; y++)
                for (int x = 0; x < Data.Width; x++)
                {
                    Image cell = Instantiate(cellPrefab, cellLayer);
                    cell.name = $"Cell_{x}_{y}";
                    RectTransform rect = cell.rectTransform;
                    SetTopLeft(rect);
                    rect.anchoredPosition = new Vector2(x * cellSize + 1f, -y * cellSize - 1f);
                    rect.sizeDelta = new Vector2(cellSize - 2f, cellSize - 2f);
                    generatedObjects.Add(cell.gameObject);
                }
            }

            if (itemPrefab != null)
            {
                foreach (InventoryEntry entry in Data.Entries)
                {
                    InventoryItemView item = Instantiate(itemPrefab, itemLayer);
                    item.name = "Item_" + entry.Item.ItemId;
                    item.Bind(entry, this, owner, entry == selected, cellSize);
                    generatedObjects.Add(item.gameObject);
                }
            }

            if (autoSortButton != null)
            {
                autoSortButton.onClick.RemoveAllListeners();
                autoSortButton.onClick.AddListener(() => owner?.HandleAutoSort(Data));
            }
        }

        // 드래그를 시작한 화면 좌표를 아이템 왼쪽 위 기준의 픽셀 거리로 변환합니다.
        // 클릭한 지점을 보존해야 큰 아이템을 옮길 때 마우스를 놓은 칸과 실제 배치 칸이 자연스럽게 일치합니다.
        public Vector2 GetPointerOffset(InventoryEntry entry, Vector2 screenPoint, Camera eventCamera)
        {
            if (entry == null || gridArea == null ||
                !RectTransformUtility.ScreenPointToLocalPointInRectangle(gridArea, screenPoint, eventCamera, out Vector2 localPoint))
                return Vector2.zero;

            return new Vector2(
                localPoint.x - entry.Position.x * cellSize,
                -localPoint.y - entry.Position.y * cellSize);
        }

        // 드롭한 마우스 위치에서 처음 잡은 지점의 오프셋을 빼 아이템 왼쪽 위가 들어갈 격자 좌표를 계산합니다.
        // 최종 범위, 겹침, 교환 가능 여부는 여기서 임의로 판단하지 않고 PlayerInventory.TryMove가 결정합니다.
        public bool TryGetDropPosition(Vector2 screenPoint, Vector2 pointerOffset, Camera eventCamera, out Vector2Int position)
        {
            position = default;
            if (gridArea == null ||
                !RectTransformUtility.ScreenPointToLocalPointInRectangle(gridArea, screenPoint, eventCamera, out Vector2 localPoint))
                return false;

            Vector2 itemTopLeft = new Vector2(localPoint.x - pointerOffset.x, -localPoint.y - pointerOffset.y);
            position = CalculateDropCell(itemTopLeft, cellSize);
            return true;
        }

        // 픽셀 좌표를 가장 가까운 격자 칸으로 반올림하는 순수 계산 함수입니다.
        // 별도 함수로 두어 Canvas 없이도 EditMode 테스트에서 드롭 좌표 규칙을 확인할 수 있습니다.
        public static Vector2Int CalculateDropCell(Vector2 itemTopLeft, float size)
        {
            float safeSize = Mathf.Max(1f, size);
            return new Vector2Int(
                Mathf.RoundToInt(itemTopLeft.x / safeSize),
                Mathf.RoundToInt(itemTopLeft.y / safeSize));
        }

        // 실제 이동 계획의 종류에 따라 아이템이 차지할 true 셀만 반투명 색으로 표시합니다.
        // 이 메서드는 Image만 만들고 GridData나 InventoryEntry를 수정하지 않으므로 마우스를 놓기 전 데이터는 그대로 유지됩니다.
        public void ShowPlacementPreview(
            InventoryEntry entry,
            Vector2Int position,
            InventoryPlacementPreview preview)
        {
            ClearPlacementPreview();
            if (entry == null || itemLayer == null) return;

            Color color = PreviewColor(preview);
            for (int localY = 0; localY < entry.Height; localY++)
            for (int localX = 0; localX < entry.Width; localX++)
            {
                if (!entry.OccupiesLocalCell(localX, localY)) continue;

                int gridX = position.x + localX;
                int gridY = position.y + localY;
                // 격자 밖 셀은 화면에 그릴 위치가 없으므로 생략하되 남은 안쪽 셀은 빨간색으로 보여 경계 실패를 알립니다.
                if (Data == null || gridX < 0 || gridY < 0 || gridX >= Data.Width || gridY >= Data.Height) continue;

                var previewObject = new GameObject(
                    $"PlacementPreview_{gridX}_{gridY}",
                    typeof(RectTransform),
                    typeof(CanvasRenderer),
                    typeof(Image));
                previewObject.transform.SetParent(itemLayer, false);
                Image image = previewObject.GetComponent<Image>();
                image.color = color;
                image.raycastTarget = false;
                RectTransform rect = image.rectTransform;
                SetTopLeft(rect);
                rect.anchoredPosition = new Vector2(gridX * cellSize + 2f, -gridY * cellSize - 2f);
                rect.sizeDelta = new Vector2(cellSize - 4f, cellSize - 4f);
                previewObject.transform.SetAsLastSibling();
                previewObjects.Add(previewObject);
            }
        }

        public void ClearPlacementPreview()
        {
            foreach (GameObject previewObject in previewObjects)
            {
                if (previewObject == null) continue;
                if (Application.isPlaying) Destroy(previewObject);
                else DestroyImmediate(previewObject);
            }
            previewObjects.Clear();
        }

        // 이동 종류를 색으로 구분해 빈칸 이동, 중첩과 교환이 같은 초록색으로 보이지 않게 합니다.
        // Invalid=빨강, Move=초록, Merge=청록, Swap=노랑 규칙은 레이드와 거점에서 공통으로 사용됩니다.
        public static Color PreviewColor(InventoryPlacementPreview preview)
        {
            switch (preview)
            {
                // 이동 가능은 녹색, 합치기는 정보색, 교환은 주황색, 불가능은 적색으로 기능 의미를 그대로 유지합니다.
                // 공용 테마색의 알파만 낮춰 아이템 원화와 격자 위치를 함께 확인할 수 있게 합니다.
                case InventoryPlacementPreview.Move: return WithAlpha(SurvivalUiTheme.Success, 0.58f);
                case InventoryPlacementPreview.Merge: return WithAlpha(SurvivalUiTheme.Info, 0.62f);
                case InventoryPlacementPreview.Swap: return WithAlpha(SurvivalUiTheme.Accent, 0.62f);
                default: return WithAlpha(SurvivalUiTheme.Danger, 0.62f);
            }
        }

        // 정해 둔 RGB 의미색은 유지하고, 드래그 미리보기에서 필요한 투명도만 바꾸는 작은 보조 함수입니다.
        private static Color WithAlpha(Color color, float alpha)
        {
            color.a = alpha;
            return color;
        }

        private void ClearGeneratedObjects()
        {
            ClearPlacementPreview();
            foreach (GameObject generated in generatedObjects)
            {
                if (generated == null) continue;
                if (Application.isPlaying) Destroy(generated);
                else DestroyImmediate(generated);
            }
            generatedObjects.Clear();
        }

        private static void SetTopLeft(RectTransform rect)
        {
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
        }

        private void OnDestroy()
        {
            ClearPlacementPreview();
            if (autoSortButton != null) autoSortButton.onClick.RemoveAllListeners();
        }
    }
}
