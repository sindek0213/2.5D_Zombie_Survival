using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using ZombieSurvival.Core;
using ZombieSurvival.Hideout;
using ZombieSurvival.Inventory;
using ZombieSurvival.Items;

namespace ZombieSurvival.UI
{
    // Hideout의 창고와 출격 준비를 목록이 아닌 두 개의 실제 격자로 표시하고 드래그 명령을 컨트롤러에 전달합니다.
    // 격자 프리팹은 레이드 인벤토리와 공유하지만 저장·출격 규칙은 HideoutController가 담당해 화면 코드에 섞이지 않습니다.
    public sealed class HideoutResultHud : MonoBehaviour, IInventoryGridViewOwner
    {
        [SerializeField] private HideoutController controller;
        [SerializeField] private Text resultSummaryText;
        [SerializeField] private Text stashTitleText;
        [SerializeField] private Text loadoutTitleText;
        [SerializeField] private InventoryGridView stashGridView;
        [SerializeField] private InventoryGridView loadoutGridView;
        [SerializeField] private RectTransform dragLayer;
        [SerializeField] private Text itemDetailText;
        [SerializeField] private Text operationStatusText;
        [SerializeField] private Button launchButton;
        [SerializeField] private Text launchButtonText;

        private InventoryEntry selected;
        private PlayerInventory.GridData selectedGrid;
        // 레이드 인벤토리와 같은 드래그 상태 객체를 사용해 시작·드롭·취소 규칙이 두 화면에서 달라지지 않게 합니다.
        private readonly InventoryDragSession dragSession = new InventoryDragSession();
        private InventoryEntry openedContainer;
        private PlayerInventory.GridData openedContainerParent;
        private InventoryGridView previewGridView;
        private string localStatusMessage = string.Empty;
        private float localStatusExpiresAt;

        public void Configure(HideoutController value)
        {
            // 다른 거점 컨트롤러로 연결을 바꾸기 전에 기존 격자에서 시작한 드래그를 취소해 잘못된 저장소로 이동하지 않게 합니다.
            dragSession.Cancel();
            UnsubscribeController();
            controller = value;
            SubscribeController();
            Refresh();
        }

        // 생성기가 두 격자, 드래그 레이어, 상세 설명과 출격 버튼을 한 번에 연결합니다.
        public void ConfigureViews(
            Text summary,
            Text stashTitle,
            Text loadoutTitle,
            InventoryGridView stashGrid,
            InventoryGridView loadoutGrid,
            RectTransform dragRoot,
            Text detail,
            Text operationStatus,
            Button launch,
            Text launchLabel)
        {
            resultSummaryText = summary;
            stashTitleText = stashTitle;
            loadoutTitleText = loadoutTitle;
            stashGridView = stashGrid;
            loadoutGridView = loadoutGrid;
            dragLayer = dragRoot;
            itemDetailText = detail;
            operationStatusText = operationStatus;
            launchButton = launch;
            launchButtonText = launchLabel;
            ConfigureLaunchButton();
            Refresh();
        }

        private void Awake()
        {
            if (controller == null) controller = FindObjectOfType<HideoutController>();
            ConfigureLaunchButton();
        }

        private void OnEnable()
        {
            SubscribeController();
            Refresh();
        }

        private void Update()
        {
            // 거점도 레이드와 같은 R 회전 규칙을 사용합니다. Shift와 Ctrl 동작은 HandleItemClick에서 서로 분리합니다.
            if (selected != null && selectedGrid != null && Input.GetKeyDown(KeyCode.R))
            {
                if (controller?.TryRotate(selectedGrid, selected) != true)
                    ShowLocalStatus("회전한 모양이 격자 경계 또는 다른 아이템과 겹칩니다.");
            }

            // 거점에서는 바닥 드롭 대상이 없으므로 Delete를 눌러도 데이터를 삭제하지 않고 이유만 표시합니다.
            if (selected != null && Input.GetKeyDown(KeyCode.Delete))
                ShowLocalStatus("거점에서는 아이템을 버릴 수 없습니다.");

            if (!string.IsNullOrEmpty(localStatusMessage) && Time.unscaledTime >= localStatusExpiresAt)
            {
                localStatusMessage = string.Empty;
                Refresh();
            }
        }

        public void HandleItemClick(
            InventoryEntry entry,
            PlayerInventory.GridData grid,
            PointerEventData.InputButton button,
            bool shiftPressed,
            bool controlPressed)
        {
            if (controller == null || entry == null || grid == null || dragSession.IsActive) return;
            selected = entry;
            selectedGrid = grid;

            if (button == PointerEventData.InputButton.Left && shiftPressed)
            {
                controller.TrySplit(grid, entry);
                return;
            }

            if (button == PointerEventData.InputButton.Left && controlPressed)
            {
                PlayerInventory.GridData target = GetQuickTransferTarget(grid);
                if (target == null) ShowLocalStatus("빠른 이동 대상이 없습니다.");
                else controller.TryTransfer(grid, target, entry);
                return;
            }

            if (button == PointerEventData.InputButton.Right)
            {
                if (entry.Contents != null) ToggleContainer(entry, grid);
                else ShowLocalStatus("거점 우클릭 빠른 이동은 제거되었습니다. Ctrl+좌클릭을 사용하세요.");
                return;
            }

            // 컨테이너를 좌클릭하면 내부 격자를 열고, 열린 같은 컨테이너를 다시 누르면 일반 창고/출격 화면으로 돌아갑니다.
            if (button == PointerEventData.InputButton.Left && entry.Contents != null)
                ToggleContainer(entry, grid);
            else
                Refresh();
        }

        public void HandleAutoSort(PlayerInventory.GridData grid)
        {
            controller?.TryAutoSort(grid);
        }

        public void BeginItemDrag(
            InventoryItemView view,
            InventoryEntry entry,
            PlayerInventory.GridData source,
            Vector2 pointerOffset,
            PointerEventData eventData)
        {
            if (controller == null || dragSession.IsActive) return;
            if (!dragSession.Begin(view, entry, source, pointerOffset, dragLayer, eventData)) return;

            selected = entry;
            selectedGrid = source;
        }

        public void UpdateItemDrag(PointerEventData eventData)
        {
            dragSession.Update(eventData);
            UpdatePlacementPreview(eventData);
        }

        public void EndItemDrag(PointerEventData eventData)
        {
            if (!dragSession.IsActive) return;

            if (controller != null &&
                dragSession.TryGetDrop(
                    eventData,
                    out _,
                    out PlayerInventory.GridData targetGrid,
                    out Vector2Int position))
            {
                string failure = InventoryGridOperations.GetMoveFailureReason(
                    dragSession.SourceGrid,
                    targetGrid,
                    dragSession.Entry,
                    position);
                if (!string.IsNullOrEmpty(failure)) ShowLocalStatus(failure);
                else controller.TryMove(dragSession.SourceGrid, targetGrid, dragSession.Entry, position);
            }
            else
            {
                ShowLocalStatus("아이템을 놓을 대상 격자가 없습니다.");
            }

            ClearPlacementPreview();
            dragSession.Cancel();
            Refresh();
        }

        private void Refresh()
        {
            if (controller == null || controller.StashGrid == null || controller.LoadoutGrid == null)
            {
                if (resultSummaryText != null) resultSummaryText.text = "HideoutController 또는 아이템 카탈로그가 연결되지 않았습니다.";
                if (launchButton != null) launchButton.interactable = false;
                return;
            }

            NormalizeSelection();
            NormalizeOpenedContainer();
            if (openedContainer != null && openedContainerParent != null)
            {
                string parentTitle = openedContainerParent == controller.StashGrid ? "창고" : "출격 인벤토리";
                stashGridView?.Bind(openedContainerParent, this, parentTitle, selected);
                loadoutGridView?.Bind(openedContainer.Contents, this, openedContainer.Item.DisplayName + " 내부", selected);
            }
            else
            {
                stashGridView?.Bind(controller.StashGrid, this, "창고", selected);
                loadoutGridView?.Bind(controller.LoadoutGrid, this, "출격 인벤토리", selected);
            }

            int stashQuantity = CountGrid(controller.StashGrid);
            int loadoutQuantity = CountGrid(controller.LoadoutGrid);
            if (stashTitleText != null)
                stashTitleText.text = openedContainer != null
                    ? (openedContainerParent == controller.StashGrid ? "창고" : "출격 인벤토리") + "  (컨테이너를 다시 클릭하면 닫기)"
                    : $"창고  ({controller.StashGrid.Entries.Count}묶음 / {stashQuantity}개)";
            if (loadoutTitleText != null)
                loadoutTitleText.text = openedContainer != null
                    ? openedContainer.Item.DisplayName + " 내부"
                    : $"출격 인벤토리  ({controller.LoadoutGrid.Entries.Count}묶음 / {loadoutQuantity}개)";
            if (resultSummaryText != null) resultSummaryText.text = BuildResultSummary(controller.ProcessedResult, controller.PersistenceStatus);
            if (operationStatusText != null)
                operationStatusText.text = !string.IsNullOrEmpty(localStatusMessage) && Time.unscaledTime < localStatusExpiresAt
                    ? localStatusMessage
                    : string.IsNullOrWhiteSpace(controller.OperationStatus)
                        ? "좌클릭 선택/드래그 · Ctrl+좌클릭 빠른 이동 · Shift+좌클릭 절반 분할 · R 회전"
                        : controller.OperationStatus;
            if (itemDetailText != null)
                itemDetailText.text = ItemDetailFormatter.Build(selected, FindWeaponComparison(selected));
            if (launchButtonText != null)
                launchButtonText.text = loadoutQuantity > 0
                    ? $"레이드 출격  (준비 수량 {loadoutQuantity})"
                    : "빈손으로 레이드 출격";
            if (launchButton != null) launchButton.interactable = true;
        }

        public static string BuildResultSummary(RaidResult result, string persistenceStatus)
        {
            string message;
            if (result == null)
            {
                message = "창고에서 출격 아이템을 준비하세요.";
            }
            else if (result.succeeded)
            {
                message = $"직전 레이드 탈출 성공 · {FormatDuration(result.raidDurationSeconds)} · 좀비 {result.zombieKills} 처치";
            }
            else
            {
                message = $"직전 레이드 실패: 플레이어 사망 · 소지품과 장착 장비 전부 분실 ({result.lostItemCount}개) · 보상 없음";
            }

            if (!string.IsNullOrWhiteSpace(persistenceStatus)) message += "\n" + persistenceStatus;
            return message;
        }

        private InventoryEntry FindWeaponComparison(InventoryEntry entry)
        {
            if (entry?.Item.Type != ItemType.Weapon || controller == null) return null;
            foreach (InventoryEntry candidate in controller.LoadoutGrid.Entries)
                if (candidate != entry && candidate.Item.Type == ItemType.Weapon &&
                    candidate.Item.EquipmentSlot == entry.Item.EquipmentSlot) return candidate;
            foreach (InventoryEntry candidate in controller.StashGrid.Entries)
                if (candidate != entry && candidate.Item.Type == ItemType.Weapon &&
                    candidate.Item.EquipmentSlot == entry.Item.EquipmentSlot) return candidate;
            return null;
        }

        private void NormalizeSelection()
        {
            if (selected == null || controller == null) return;
            if (controller.StashGrid.Entries.Contains(selected)) selectedGrid = controller.StashGrid;
            else if (controller.LoadoutGrid.Entries.Contains(selected)) selectedGrid = controller.LoadoutGrid;
            else if (openedContainer?.Contents != null && openedContainer.Contents.Entries.Contains(selected))
                selectedGrid = openedContainer.Contents;
            else
            {
                selected = null;
                selectedGrid = null;
            }
        }

        private void ToggleContainer(InventoryEntry entry, PlayerInventory.GridData parent)
        {
            if (entry?.Contents == null || parent == null) return;
            if (openedContainer == entry)
            {
                openedContainer = null;
                openedContainerParent = null;
                ShowLocalStatus("컨테이너 내부 보기를 닫았습니다.");
            }
            else
            {
                openedContainer = entry;
                openedContainerParent = parent;
                ShowLocalStatus("컨테이너 내부를 열었습니다. Ctrl+좌클릭으로 넣거나 꺼낼 수 있습니다.");
            }
            Refresh();
        }

        // 컨테이너가 다른 격자로 이동했거나 목록에서 사라졌다면 오래된 부모 참조를 유지하지 않습니다.
        private void NormalizeOpenedContainer()
        {
            if (openedContainer == null || controller == null) return;
            if (controller.StashGrid.Entries.Contains(openedContainer))
            {
                openedContainerParent = controller.StashGrid;
                return;
            }
            if (controller.LoadoutGrid.Entries.Contains(openedContainer))
            {
                openedContainerParent = controller.LoadoutGrid;
                return;
            }
            openedContainer = null;
            openedContainerParent = null;
        }

        private PlayerInventory.GridData GetQuickTransferTarget(PlayerInventory.GridData source)
        {
            if (openedContainer != null && openedContainerParent != null)
            {
                if (source == openedContainerParent) return openedContainer.Contents;
                if (source == openedContainer.Contents) return openedContainerParent;
                return null;
            }
            if (source == controller.StashGrid) return controller.LoadoutGrid;
            if (source == controller.LoadoutGrid) return controller.StashGrid;
            return null;
        }

        private void UpdatePlacementPreview(PointerEventData eventData)
        {
            if (!dragSession.TryGetDrop(
                    eventData,
                    out InventoryGridView targetView,
                    out PlayerInventory.GridData targetGrid,
                    out Vector2Int position))
            {
                ClearPlacementPreview();
                return;
            }

            if (previewGridView != targetView) ClearPlacementPreview();
            previewGridView = targetView;
            InventoryPlacementPreview preview = InventoryGridOperations.GetPlacementPreview(
                dragSession.SourceGrid,
                targetGrid,
                dragSession.Entry,
                position);
            targetView.ShowPlacementPreview(dragSession.Entry, position, preview);
            ShowLocalStatus(PreviewMessage(preview));
        }

        private static string PreviewMessage(InventoryPlacementPreview preview)
        {
            switch (preview)
            {
                case InventoryPlacementPreview.Move: return "배치 가능";
                case InventoryPlacementPreview.Merge: return "같은 아이템 묶음과 중첩 가능";
                case InventoryPlacementPreview.Swap: return "아이템 교환·재배치 가능";
                default: return "드롭 위치에 배치·중첩·교환할 수 없습니다.";
            }
        }

        private void ClearPlacementPreview()
        {
            previewGridView?.ClearPlacementPreview();
            previewGridView = null;
        }

        private void ShowLocalStatus(string message)
        {
            localStatusMessage = message ?? string.Empty;
            localStatusExpiresAt = Time.unscaledTime + 2.5f;
            if (operationStatusText != null) operationStatusText.text = localStatusMessage;
        }

        private static int CountGrid(PlayerInventory.GridData grid)
        {
            // 화면에 수량을 표시할 때마다 저장용 RaidItemSnapshot을 만들던 중복 변환을 제거했습니다.
            // 실제 InventoryEntry 트리를 바로 순회하므로 결과는 같지만 Refresh마다 임시 객체와 List가 생성되지 않습니다.
            return InventoryGridContent.CountAllQuantities(grid);
        }

        private static string FormatDuration(float seconds)
        {
            int total = Mathf.Max(0, Mathf.FloorToInt(seconds));
            return $"{total / 60:00}:{total % 60:00}";
        }

        private void ConfigureLaunchButton()
        {
            if (launchButton == null) return;
            launchButton.onClick.RemoveListener(StartRaid);
            launchButton.onClick.AddListener(StartRaid);
        }

        private void StartRaid() => controller?.StartRaid();

        private void SubscribeController()
        {
            if (controller == null) return;
            controller.StateChanged -= Refresh;
            controller.StateChanged += Refresh;
        }

        private void UnsubscribeController()
        {
            if (controller != null) controller.StateChanged -= Refresh;
        }

        private void OnDisable()
        {
            UnsubscribeController();
            ClearPlacementPreview();
            dragSession.Cancel();
            openedContainer = null;
            openedContainerParent = null;
            localStatusMessage = string.Empty;
        }

        private void OnDestroy()
        {
            UnsubscribeController();
            if (launchButton != null) launchButton.onClick.RemoveListener(StartRaid);
        }
    }
}
