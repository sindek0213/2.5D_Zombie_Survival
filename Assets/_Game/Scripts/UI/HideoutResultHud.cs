using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using ZombieSurvival.Art;
using ZombieSurvival.Combat;
using ZombieSurvival.Core;
using ZombieSurvival.Hideout;
using ZombieSurvival.Inventory;
using ZombieSurvival.Items;

namespace ZombieSurvival.UI
{
    // Hideout의 창고와 출격 준비를 목록이 아닌 두 개의 실제 격자로 표시하고 드래그 명령을 컨트롤러에 전달합니다.
    // 격자 프리팹은 레이드 인벤토리와 공유하지만 저장·출격 규칙은 HideoutController가 담당해 화면 코드에 섞이지 않습니다.
    public sealed class HideoutResultHud : MonoBehaviour, IInventoryGridViewOwner, IEquipmentSlotViewOwner
    {
        [SerializeField] private HideoutController controller;
        [SerializeField] private Text resultSummaryText;
        [SerializeField] private Text stashTitleText;
        [SerializeField] private Text loadoutTitleText;
        [SerializeField] private InventoryGridView stashGridView;
        [SerializeField] private InventoryGridView loadoutGridView;
        [SerializeField] private EquipmentSlotView[] equipmentSlotViews;
        [SerializeField] private RectTransform dragLayer;
        [SerializeField] private Text itemDetailText;
        [SerializeField] private ItemDetailPopup itemDetailPopup;
        [SerializeField] private Text operationStatusText;
        [SerializeField] private WeaponManagementPanel weaponManagementPanel;

        private InventoryEntry selected;
        private PlayerInventory.GridData selectedGrid;
        // 레이드 인벤토리와 같은 드래그 상태 객체를 사용해 시작·드롭·취소 규칙이 두 화면에서 달라지지 않게 합니다.
        private readonly InventoryDragSession dragSession = new InventoryDragSession();
        private InventoryEntry openedContainer;
        private PlayerInventory.GridData openedContainerParent;
        private InventoryGridView previewGridView;
        private string localStatusMessage = string.Empty;
        private float localStatusExpiresAt;
        private InventoryEntry popupEntry;
        private PlayerInventory playerInventory;
        private EquipmentSlotType equipmentDragSlot = EquipmentSlotType.None;
        private InventoryEntry equipmentDragEntry;
        private Image equipmentDragGhost;

        public void Configure(HideoutController value)
        {
            // 다른 거점 컨트롤러로 연결을 바꾸기 전에 기존 격자에서 시작한 드래그를 취소해 잘못된 저장소로 이동하지 않게 합니다.
            dragSession.Cancel();
            UnsubscribeController();
            controller = value;
            playerInventory = controller != null ? controller.PlayerInventory : null;
            SubscribeController();
            Refresh();
        }

        // 거점 창고 프리팹 아래쪽에 배치한 장비 슬롯 View를 연결합니다.
        // 기존 ConfigureViews 시그니처를 유지해 이전 테스트와 수동 프리팹도 장비 슬롯 없이 계속 열릴 수 있게 합니다.
        public void ConfigureEquipmentViews(EquipmentSlotView[] slotViews)
        {
            equipmentSlotViews = slotViews;
            Refresh();
        }

        // 창고 프리팹의 전용 무기 관리 패널을 연결합니다.
        // 별도 메서드로 둬 기존 ConfigureViews를 호출하는 테스트와 이전 프리팹의 호환성을 유지합니다.
        public void ConfigureWeaponManagementPanel(WeaponManagementPanel panel)
        {
            weaponManagementPanel = panel;
            Refresh();
        }

        // 생성기가 두 격자, 드래그 레이어와 상세 설명을 한 번에 연결합니다.
        // 실제 출격은 별도 출격 터미널 화면이 담당하므로 이 창고 화면에는 씬 전환 버튼을 두지 않습니다.
        public void ConfigureViews(
            Text summary,
            Text stashTitle,
            Text loadoutTitle,
            InventoryGridView stashGrid,
            InventoryGridView loadoutGrid,
            RectTransform dragRoot,
            Text detail,
            Text operationStatus)
        {
            resultSummaryText = summary;
            stashTitleText = stashTitle;
            loadoutTitleText = loadoutTitle;
            stashGridView = stashGrid;
            loadoutGridView = loadoutGrid;
            dragLayer = dragRoot;
            itemDetailText = detail;
            operationStatusText = operationStatus;
            EnsureItemDetailPopup();
            Refresh();
        }

        private void Awake()
        {
            if (controller == null) controller = FindObjectOfType<HideoutController>();
            if (playerInventory == null) playerInventory = controller != null
                ? controller.PlayerInventory
                : FindObjectOfType<PlayerInventory>();
            EnsureItemDetailPopup();
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
                // 장비 아이템은 우클릭으로 즉시 해당 슬롯에 착용합니다. 가방 내부를 보려면 좌클릭을 사용합니다.
                if (entry.Item.EquipmentSlot != EquipmentSlotType.None && playerInventory != null)
                {
                    if (!playerInventory.TryEquipOrSwap(grid, entry, entry.Item.EquipmentSlot, out string failure))
                        ShowLocalStatus(failure);
                    else
                        ShowLocalStatus($"{entry.Item.DisplayName}을 장착했습니다.");
                }
                else if (entry.Item.Type == ItemType.Weapon && entry.InstalledAttachments.Count > 0)
                {
                    ItemDefinition lastAttachment = entry.InstalledAttachments[entry.InstalledAttachments.Count - 1];
                    controller.TryDetachAttachment(
                        grid,
                        entry,
                        lastAttachment.WeaponAttachmentDefinition.Slot,
                        grid);
                    ShowLocalStatus(controller.OperationStatus);
                }
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

            GameObject hit = eventData != null ? eventData.pointerCurrentRaycast.gameObject : null;
            EquipmentSlotView targetSlot = hit != null ? hit.GetComponentInParent<EquipmentSlotView>() : null;
            if (targetSlot != null && playerInventory != null)
            {
                InventoryEntry dragged = dragSession.Entry;
                if (!playerInventory.TryEquipOrSwap(
                        dragSession.SourceGrid,
                        dragged,
                        targetSlot.Slot,
                        out string equipFailure))
                    ShowLocalStatus(equipFailure);
                else
                    ShowLocalStatus($"{dragged.Item.DisplayName}을 장착했습니다.");

                ClearPlacementPreview();
                dragSession.Cancel();
                Refresh();
                return;
            }

            if (controller != null &&
                dragSession.TryGetDrop(
                    eventData,
                    out _,
                    out PlayerInventory.GridData targetGrid,
                    out Vector2Int position))
            {
                InventoryEntry targetWeapon = FindEntryAt(targetGrid, position, dragSession.Entry);
                if (dragSession.Entry.Item.Type == ItemType.Attachment && targetWeapon?.Item.Type == ItemType.Weapon)
                {
                    controller.TryInstallAttachment(
                        dragSession.SourceGrid,
                        dragSession.Entry,
                        targetGrid,
                        targetWeapon);
                    ShowLocalStatus(controller.OperationStatus);
                    ClearPlacementPreview();
                    dragSession.Cancel();
                    Refresh();
                    return;
                }

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

        // 거점 창고도 레이드 인벤토리와 같은 길게 누르기 후 고정 팝업 규칙을 사용합니다.
        // 무기 비교 대상은 거점의 창고와 출격 인벤토리에서 찾아야 하므로 이 HUD가 상세 문자열을 완성합니다.
        public void ShowItemDetailPopup(InventoryEntry entry, Vector2 screenPosition, Camera eventCamera)
        {
            if (entry == null) return;
            EnsureItemDetailPopup();
            popupEntry = entry;
            itemDetailPopup?.Show(
                entry.Item.DisplayName,
                ItemDetailFormatter.Build(entry, FindWeaponComparison(entry)),
                screenPosition,
                eventCamera);
        }

        public void HideItemDetailPopup(InventoryEntry entry)
        {
            if (popupEntry != null && entry != null && popupEntry != entry) return;
            itemDetailPopup?.Hide();
            popupEntry = null;
        }

        // 장착 슬롯은 좌클릭으로 가방·조끼 내부를 열고, 우클릭으로 플레이어 인벤토리에 해제합니다.
        // 해제할 공간이 없으면 PlayerInventory가 장비 Dictionary를 바꾸지 않으므로 아이템이 사라지지 않습니다.
        public void HandleEquipmentClick(EquipmentSlotType slot, PointerEventData.InputButton button)
        {
            if (playerInventory == null ||
                !playerInventory.Equipment.TryGetValue(slot, out InventoryEntry entry)) return;

            selected = entry;
            selectedGrid = null;
            if (button == PointerEventData.InputButton.Right)
            {
                if (!playerInventory.TryUnequip(slot))
                    ShowLocalStatus("장비를 내 인벤토리에 돌려놓을 공간이 없습니다.");
                else
                    ShowLocalStatus($"{entry.Item.DisplayName}을 해제했습니다.");
            }
            else if (button == PointerEventData.InputButton.Left && entry.Contents != null)
            {
                ToggleContainer(entry, null);
            }
            Refresh();
        }

        // 장비 슬롯에서 격자로 끌 때 실제 장비는 드롭 성공 전까지 슬롯에 남겨 둡니다.
        // 화면에는 별도 유령 이미지만 움직여 실패하거나 창 밖에 놓아도 원본 장비가 안전하게 유지됩니다.
        public void BeginEquipmentDrag(
            EquipmentSlotView view,
            EquipmentSlotType slot,
            PointerEventData eventData)
        {
            if (playerInventory == null || dragSession.IsActive || equipmentDragEntry != null ||
                !playerInventory.Equipment.TryGetValue(slot, out InventoryEntry entry)) return;

            equipmentDragSlot = slot;
            equipmentDragEntry = entry;
            selected = entry;
            selectedGrid = null;
            CreateEquipmentDragGhost(entry, eventData != null ? eventData.position : Vector2.zero);
        }

        public void UpdateEquipmentDrag(PointerEventData eventData)
        {
            if (equipmentDragEntry == null || eventData == null) return;
            if (equipmentDragGhost != null) equipmentDragGhost.rectTransform.position = eventData.position;

            if (!TryGetTargetGrid(eventData, out InventoryGridView targetView, out PlayerInventory.GridData target, out Vector2Int position))
            {
                ClearPlacementPreview();
                return;
            }

            if (previewGridView != targetView) ClearPlacementPreview();
            previewGridView = targetView;
            bool canPlace = !InventoryGridOperations.IsGridOwnedByEntry(equipmentDragEntry, target) &&
                            playerInventory.CanPlace(target, equipmentDragEntry, position, equipmentDragEntry.Rotated, null);
            targetView.ShowPlacementPreview(
                equipmentDragEntry,
                position,
                canPlace ? InventoryPlacementPreview.Move : InventoryPlacementPreview.Invalid);
        }

        public void EndEquipmentDrag(PointerEventData eventData)
        {
            if (equipmentDragEntry == null) return;
            if (!TryGetTargetGrid(eventData, out _, out PlayerInventory.GridData target, out Vector2Int position))
            {
                ShowLocalStatus("장비를 놓을 대상 격자가 없습니다.");
            }
            else if (!playerInventory.TryUnequipTo(equipmentDragSlot, target, position, out string failure))
            {
                ShowLocalStatus(failure);
            }
            else
            {
                ShowLocalStatus("장비를 격자로 해제했습니다.");
            }

            CancelEquipmentDrag();
            ClearPlacementPreview();
            Refresh();
        }

        private void Refresh()
        {
            if (controller == null || controller.StashGrid == null || controller.LoadoutGrid == null)
            {
                if (resultSummaryText != null) resultSummaryText.text = "HideoutController 또는 아이템 카탈로그가 연결되지 않았습니다.";
                return;
            }

            if (playerInventory == null) playerInventory = controller.PlayerInventory;
            NormalizeSelection();
            NormalizeOpenedContainer();
            if (openedContainer != null && openedContainerParent != null)
            {
                // 왼쪽은 항상 플레이어 인벤토리로 유지하고, 오른쪽만 선택한 가방·조끼 내부로 잠시 전환합니다.
                // 같은 컨테이너를 다시 클릭하면 오른쪽이 창고로 돌아옵니다.
                stashGridView?.Bind(controller.LoadoutGrid, this, "내 인벤토리", selected);
                loadoutGridView?.Bind(openedContainer.Contents, this, openedContainer.Item.DisplayName + " 내부", selected);
            }
            else
            {
                stashGridView?.Bind(controller.LoadoutGrid, this, "내 인벤토리", selected);
                loadoutGridView?.Bind(controller.StashGrid, this, "창고", selected);
            }

            if (equipmentSlotViews != null && playerInventory != null)
                foreach (EquipmentSlotView slotView in equipmentSlotViews)
                {
                    if (slotView == null) continue;
                    playerInventory.Equipment.TryGetValue(slotView.Slot, out InventoryEntry equipped);
                    slotView.Bind(equipped, false, this);
                }

            int stashQuantity = CountGrid(controller.StashGrid);
            int loadoutQuantity = CountGrid(controller.LoadoutGrid);
            if (stashTitleText != null)
                stashTitleText.text = openedContainer != null
                    ? "내 인벤토리  (장비와 가방을 함께 관리)"
                    : $"내 인벤토리  ({controller.LoadoutGrid.Entries.Count}묶음 / {loadoutQuantity}개)";
            if (loadoutTitleText != null)
                loadoutTitleText.text = openedContainer != null
                    ? openedContainer.Item.DisplayName + " 내부  (다시 클릭하면 창고로 복귀)"
                    : $"창고  ({controller.StashGrid.Entries.Count}묶음 / {stashQuantity}개)";
            if (resultSummaryText != null) resultSummaryText.text = BuildResultSummary(controller.ProcessedResult, controller.PersistenceStatus);
            if (operationStatusText != null)
                operationStatusText.text = !string.IsNullOrEmpty(localStatusMessage) && Time.unscaledTime < localStatusExpiresAt
                    ? localStatusMessage
                    : string.IsNullOrWhiteSpace(controller.OperationStatus)
                        ? "좌클릭 선택/드래그 · 길게 누르기 정보 · Ctrl+좌클릭 빠른 이동 · Shift+좌클릭 절반 분할 · R 회전"
                        : controller.OperationStatus;
            // 거점 패널은 부착물 해제와 함께 크레딧을 사용하는 완전 수리 버튼도 활성화합니다.
            // 선택 무기가 바뀔 때마다 다시 Bind해 수리 후 내구도·비용·버튼 상태가 즉시 갱신됩니다.
            weaponManagementPanel?.Bind(
                selected,
                FindWeaponComparison(selected),
                true,
                HandleWeaponPanelDetach,
                HandleWeaponPanelRepair);
            // 창고 상태가 갱신되어 수량·탄창·내구도가 바뀌면 열려 있는 팝업만 가볍게 다시 그립니다.
            if (itemDetailPopup?.IsVisible == true && popupEntry != null)
                itemDetailPopup.SetContent(ItemDetailFormatter.Build(popupEntry, FindWeaponComparison(popupEntry)));
        }

        private void HandleWeaponPanelDetach(WeaponAttachmentSlot slot)
        {
            if (playerInventory == null || controller == null || selected?.Item.Type != ItemType.Weapon) return;
            PlayerInventory.GridData target = selectedGrid ?? controller.LoadoutGrid;
            ItemDefinition installed = selected.GetAttachment(slot);
            if (!playerInventory.TryDetachAttachment(selected, slot, target, out string failure))
                ShowLocalStatus(failure);
            else
                ShowLocalStatus($"{installed.DisplayName}을 무기에서 해제했습니다.");
            Refresh();
        }

        private void HandleWeaponPanelRepair()
        {
            if (controller == null || selected?.Item.Type != ItemType.Weapon) return;
            controller.TryRepairWeapon(selected);
            ShowLocalStatus(controller.OperationStatus);
            Refresh();
        }

        private void EnsureItemDetailPopup()
        {
            if (itemDetailText == null || dragLayer == null) return;
            RectTransform panel = itemDetailText.transform.parent as RectTransform;
            if (panel == null) return;

            if (itemDetailPopup == null) itemDetailPopup = GetComponent<ItemDetailPopup>();
            if (itemDetailPopup == null) itemDetailPopup = gameObject.AddComponent<ItemDetailPopup>();
            itemDetailPopup.Configure(panel, itemDetailText, dragLayer);
            itemDetailPopup.Closed -= HandleItemDetailPopupClosed;
            itemDetailPopup.Closed += HandleItemDetailPopupClosed;
        }

        private void HandleItemDetailPopupClosed()
        {
            // 팝업 스스로 닫힌 경로에서도 현재 표시 항목을 비워 이후 Refresh가 숨은 창을 다시 계산하지 않게 합니다.
            popupEntry = null;
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
                string failure = result.endReason == RaidEndReason.Abandoned
                    ? "레이드 포기"
                    : string.IsNullOrWhiteSpace(result.deathCause) ? "플레이어 사망" : result.deathCause;
                message = $"직전 레이드 실패: {failure} · 소지품과 장착 장비 전부 분실 ({result.lostItemCount}개) · 보상 없음";
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
            else if (IsEquipped(selected))
                selectedGrid = null;
            else
            {
                selected = null;
                selectedGrid = null;
            }
        }

        private void ToggleContainer(InventoryEntry entry, PlayerInventory.GridData parent)
        {
            if (entry?.Contents == null) return;
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
            if (IsEquipped(openedContainer))
            {
                // 장착 가방과 조끼에는 부모 GridData가 없지만 열린 상태를 유지하기 위한 표시 값으로 플레이어 루트를 사용합니다.
                openedContainerParent = controller.LoadoutGrid;
                return;
            }
            openedContainer = null;
            openedContainerParent = null;
        }

        private bool TryGetTargetGrid(
            PointerEventData eventData,
            out InventoryGridView targetView,
            out PlayerInventory.GridData targetGrid,
            out Vector2Int position)
        {
            targetView = null;
            targetGrid = null;
            position = default;
            GameObject hit = eventData != null ? eventData.pointerCurrentRaycast.gameObject : null;
            targetView = hit != null ? hit.GetComponentInParent<InventoryGridView>() : null;
            if (targetView == null || targetView.Data == null) return false;
            if (!targetView.TryGetDropPosition(eventData.position, Vector2.zero, eventData.pressEventCamera, out position))
                return false;
            targetGrid = targetView.Data;
            return true;
        }

        private bool IsEquipped(InventoryEntry entry)
        {
            if (playerInventory == null || entry == null) return false;
            foreach (InventoryEntry equipped in playerInventory.Equipment.Values)
                if (equipped == entry) return true;
            return false;
        }

        private void CreateEquipmentDragGhost(InventoryEntry entry, Vector2 screenPosition)
        {
            if (dragLayer == null || entry == null) return;
            var ghostObject = new GameObject(
                "EquipmentDragGhost",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image));
            ghostObject.transform.SetParent(dragLayer, false);
            equipmentDragGhost = ghostObject.GetComponent<Image>();
            Sprite generated = GeneratedArtResources.GetItemIcon(entry.Item.ItemId);
            equipmentDragGhost.sprite = generated != null ? generated : entry.Item.Icon;
            equipmentDragGhost.color = new Color(1f, 1f, 1f, 0.76f);
            equipmentDragGhost.preserveAspect = true;
            equipmentDragGhost.raycastTarget = false;
            equipmentDragGhost.rectTransform.sizeDelta = new Vector2(84f, 84f);
            equipmentDragGhost.rectTransform.position = screenPosition;
            ghostObject.transform.SetAsLastSibling();
        }

        private void CancelEquipmentDrag()
        {
            if (equipmentDragGhost != null)
            {
                if (Application.isPlaying) Destroy(equipmentDragGhost.gameObject);
                else DestroyImmediate(equipmentDragGhost.gameObject);
            }
            equipmentDragGhost = null;
            equipmentDragEntry = null;
            equipmentDragSlot = EquipmentSlotType.None;
        }

        private PlayerInventory.GridData GetQuickTransferTarget(PlayerInventory.GridData source)
        {
            if (openedContainer != null && openedContainerParent != null)
            {
                if (source == controller.LoadoutGrid) return openedContainer.Contents;
                if (source == openedContainer.Contents) return controller.LoadoutGrid;
                return null;
            }
            if (source == controller.LoadoutGrid) return controller.StashGrid;
            if (source == controller.StashGrid) return controller.LoadoutGrid;
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
            InventoryEntry targetWeapon = FindEntryAt(targetGrid, position, dragSession.Entry);
            if (dragSession.Entry.Item.Type == ItemType.Attachment && targetWeapon?.Item.Type == ItemType.Weapon)
            {
                InventoryPlacementPreview attachmentPreview = targetWeapon.CanInstallAttachment(dragSession.Entry.Item)
                    ? InventoryPlacementPreview.Swap
                    : InventoryPlacementPreview.Invalid;
                targetView.ShowPlacementPreview(dragSession.Entry, position, attachmentPreview);
                ShowLocalStatus(attachmentPreview == InventoryPlacementPreview.Swap
                    ? $"{targetWeapon.Item.DisplayName}에 부착물을 설치할 수 있습니다."
                    : "이 무기와 부착물은 호환되지 않습니다.");
                return;
            }
            InventoryPlacementPreview preview = InventoryGridOperations.GetPlacementPreview(
                dragSession.SourceGrid,
                targetGrid,
                dragSession.Entry,
                position);
            targetView.ShowPlacementPreview(dragSession.Entry, position, preview);
            ShowLocalStatus(PreviewMessage(preview));
        }

        private static InventoryEntry FindEntryAt(
            PlayerInventory.GridData grid,
            Vector2Int position,
            InventoryEntry ignored)
        {
            if (grid == null) return null;
            foreach (InventoryEntry candidate in grid.Entries)
                if (candidate != null && candidate != ignored &&
                    candidate.OccupiesGridCell(position.x, position.y)) return candidate;
            return null;
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
            CancelEquipmentDrag();
            openedContainer = null;
            openedContainerParent = null;
            localStatusMessage = string.Empty;
            HideItemDetailPopup(popupEntry);
        }

        private void OnDestroy()
        {
            UnsubscribeController();
        }
    }
}
