using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using ZombieSurvival.Art;
using ZombieSurvival.Inventory;
using ZombieSurvival.Items;
using ZombieSurvival.Player;

namespace ZombieSurvival.UI
{
    // 인벤토리 창 전체의 열기/닫기, 단축키와 여러 UI 조각 사이의 연결을 담당합니다.
    // 실제 아이템 배치 가능 여부와 교환 규칙은 PlayerInventory가 계속 판단하며,
    // 이 클래스는 Canvas 화면에서 받은 사용자의 명령만 안전한 인벤토리 API로 전달합니다.
    [DefaultExecutionOrder(-100)]
    public sealed class InventoryHud : MonoBehaviour, IInventoryGridViewOwner
    {
        private enum AuxiliaryGridTab
        {
            None,
            Loot,
            Backpack,
            Rig
        }

        [Header("플레이어 시스템")]
        [SerializeField] private PlayerInventory inventory;
        [SerializeField] private PlayerInteraction interaction;
        [SerializeField] private PlayerControlGate controlGate;
        [SerializeField] private PlayerCombat combat;

        [Header("Canvas 화면")]
        [SerializeField] private GameObject inventoryPanel;
        [SerializeField] private RectTransform dragLayer;
        [SerializeField] private InventoryGridView rootGridView;
        [SerializeField] private GameObject containerSection;
        [SerializeField] private InventoryGridView containerGridView;
        [SerializeField] private EquipmentSlotView[] equipmentSlotViews;
        [SerializeField] private Text interactionPromptText;
        [SerializeField] private Text interactionStatusText;
        [SerializeField] private Text itemDetailText;

        [Header("캐릭터와 장비 배치")]
        // 왼쪽 위에는 전신 그림을 크게 표시하고, 장비 슬롯은 전신 그림 아래에서 세로로 이어지도록 배치합니다.
        // Inspector에서 이 값만 바꾸면 코드의 배치 계산을 고치지 않고도 왼쪽 영역을 다시 디자인할 수 있습니다.
        [SerializeField] private Vector2 characterPreviewPosition = new Vector2(43f, -86f);
        [SerializeField] private Vector2 characterPreviewSize = new Vector2(190f, 408f);
        [SerializeField] private Vector2 equipmentTitlePosition = new Vector2(40f, -520f);
        [SerializeField] private Vector2 equipmentFirstSlotPosition = new Vector2(40f, -556f);
        [SerializeField] private Vector2 equipmentSlotSize = new Vector2(205f, 58f);
        [SerializeField, Min(1f)] private float equipmentSlotVerticalStep = 62f;

        [Header("열기/닫기 효과")]
        [SerializeField] private CanvasGroup inventoryPanelGroup;
        [SerializeField] private RectTransform inventoryPanelRect;
        [SerializeField, Min(0.01f)] private float openAnimationDuration = 0.16f;
        [SerializeField, Min(0.01f)] private float closeAnimationDuration = 0.12f;
        [SerializeField, Range(0.75f, 1f)] private float closedScale = 0.94f;
        [SerializeField] private int canvasSortingOrder = 20;

        private bool open;
        private InventoryEntry selected;
        private PlayerInventory.GridData selectedGrid;
        // 레이드와 거점 HUD가 같은 드래그 상태 처리를 사용하도록 공통 일반 C# 객체에 진행 상태를 맡깁니다.
        private readonly InventoryDragSession dragSession = new InventoryDragSession();
        private Coroutine panelTransition;
        private bool panelTransitionRunning;
        private string inventoryFeedbackMessage = string.Empty;
        private float inventoryFeedbackExpiresAt;
        // 시체나 필드 상자를 수색하는 동안 오른쪽 격자에 표시할 월드 컨테이너입니다.
        // null이면 기존 규칙대로 장착한 가방 또는 전술 조끼 내부를 표시합니다.
        private WorldLootContainer activeLootContainer;
        private RawImage generatedCharacterPreview;
        private AuxiliaryGridTab activeAuxiliaryTab;
        private GameObject auxiliaryTabRoot;
        private Button lootTabButton;
        private Button backpackTabButton;
        private Button rigTabButton;
        private InventoryGridView previewGridView;

        // 장비 슬롯에서 격자로 끌어낼 때는 일반 격자 드래그와 출발 데이터 구조가 다르므로 별도 상태로 보관합니다.
        // equipmentDragEntry는 실제 슬롯에 계속 남아 있고, 마우스를 놓아 검증이 성공한 순간에만 격자로 이동합니다.
        private EquipmentSlotType equipmentDragSlot = EquipmentSlotType.None;
        private InventoryEntry equipmentDragEntry;
        private Image equipmentDragGhost;

        // Delete 한 번으로 아이템이 사라지지 않도록 같은 아이템에 대한 두 번째 입력을 기다리는 확인 상태입니다.
        private InventoryEntry pendingDropEntry;
        private PlayerInventory.GridData pendingDropGrid;
        private float pendingDropExpiresAt;

        public bool IsOpen => open;
        public RectTransform DragLayer => dragLayer;

        // 씬 생성기가 플레이어의 실제 인벤토리와 상호작용 시스템을 이 HUD에 연결할 때 사용합니다.
        // Configure가 OnEnable 뒤에 호출될 수도 있으므로 이전 이벤트를 해제한 다음 새 인벤토리 이벤트를 다시 연결합니다.
        public void Configure(PlayerInventory value, PlayerInteraction interactionValue)
        {
            // 플레이 중 참조를 교체하는 테스트나 씬 도구가 이전 인벤토리의 드래그 상태를 새 인벤토리에 적용하지 않도록 먼저 취소합니다.
            dragSession.Cancel();
            UnsubscribeInventory();
            UnsubscribeInteraction();
            inventory = value;
            interaction = interactionValue;
            controlGate = inventory != null ? inventory.GetComponent<PlayerControlGate>() : null;
            combat = inventory != null ? inventory.GetComponent<PlayerCombat>() : null;
            SubscribeInventory();
            SubscribeInteraction();
            RefreshAll();
        }

        // 프로토타입 생성기가 만든 Canvas의 각 부분을 한 번에 등록합니다.
        // 필드가 직렬화되므로 생성 뒤에는 InventoryCanvas 프리팹을 열어 이미지, 위치와 색상을 Inspector에서 직접 바꿀 수 있습니다.
        public void ConfigureViews(
            GameObject panel,
            RectTransform dragRoot,
            InventoryGridView rootView,
            GameObject containerRoot,
            InventoryGridView containerView,
            EquipmentSlotView[] slotViews,
            Text promptText,
            Text statusText,
            Text detailText = null)
        {
            inventoryPanel = panel;
            dragLayer = dragRoot;
            rootGridView = rootView;
            containerSection = containerRoot;
            containerGridView = containerView;
            equipmentSlotViews = slotViews;
            interactionPromptText = promptText;
            interactionStatusText = statusText;
            itemDetailText = detailText;
            ApplyCharacterAndEquipmentLayout();
            EnsureGeneratedCharacterPreview();
            EnsureAuxiliaryTabs();
            EnsurePanelAnimationComponents();
            ApplyOpenStateImmediately();
            RefreshAll();
        }

        private void Awake()
        {
            ResolvePlayerSystems();
            ApplyCharacterAndEquipmentLayout();
            EnsureGeneratedCharacterPreview();
            EnsureAuxiliaryTabs();
            EnsurePanelAnimationComponents();
            ApplyOpenStateImmediately();
        }

        private void OnEnable()
        {
            SubscribeInventory();
            SubscribeInteraction();
            RefreshAll();
        }

        private void Update()
        {
            // Tab은 인벤토리를 열고 닫으며, 창이 열린 동안에만 선택 아이템 단축키를 처리합니다.
            // 회전과 버리기는 장비 슬롯이 아닌 실제 격자 안의 아이템을 선택했을 때만 실행할 수 있습니다.
            if (Input.GetKeyDown(KeyCode.Tab)) SetOpen(!open);

            if (open && selected != null && selectedGrid != null && inventory != null && !inventory.IsUsingItem)
            {
                if (Input.GetKeyDown(KeyCode.R))
                {
                    CancelDropConfirmation();
                    if (!inventory.TryRotate(selectedGrid, selected))
                        ShowInventoryFeedback("회전한 모양이 격자 경계 또는 다른 아이템과 겹칩니다.");
                    RefreshAll();
                }
                if (Input.GetKeyDown(KeyCode.Delete)) HandleDeleteRequest();
            }

            if (pendingDropEntry != null && Time.unscaledTime >= pendingDropExpiresAt)
                CancelDropConfirmation();

            UpdateInteractionFeedback();
            // 아이템 사용 진행률은 매 프레임 달라지지만 격자 전체를 다시 만들 필요는 없으므로 상세 문구만 가볍게 갱신합니다.
            // 실패 안내 시간이 끝난 첫 프레임에는 문자열을 비우고 한 번 더 그려, 만료된 문구가 화면에 계속 남는 문제를 막습니다.
            if (open && (inventory?.IsUsingItem == true || !string.IsNullOrEmpty(inventoryFeedbackMessage)))
            {
                if (inventory?.IsUsingItem != true && Time.unscaledTime >= inventoryFeedbackExpiresAt)
                    inventoryFeedbackMessage = string.Empty;
                RefreshItemDetail();
            }
        }

        // 인벤토리를 열면 이동, 조준, 발사와 상호작용을 잠그고 닫으면 이 HUD가 요청한 잠금만 해제합니다.
        // 다른 화면도 동시에 잠금을 보유할 수 있으므로 PlayerControlGate가 마지막 잠금이 사라질 때 원래 조작 상태를 복구합니다.
        public void SetOpen(bool value)
        {
            if (open == value)
            {
                return;
            }

            open = value;
            ResolvePlayerSystems();

            if (open)
            {
                controlGate?.AcquireLock(this);
                RefreshAll();
                StartPanelTransition(true);
            }
            else
            {
                // 사용 중 창을 닫으면 취소하는 규칙을 명확히 해, 닫은 뒤 효과만 적용되는 혼란을 막습니다.
                inventory?.CancelItemUse();
                CancelDropConfirmation();
                // 수색 화면을 닫으면 다음 Tab 열기에서는 다시 장착 가방/조끼를 보여 주도록 외부 대상 참조를 비웁니다.
                if (activeLootContainer != null && selectedGrid == activeLootContainer.LootGrid)
                {
                    selected = null;
                    selectedGrid = null;
                }
                activeLootContainer = null;
                activeAuxiliaryTab = AuxiliaryGridTab.None;
                controlGate?.ReleaseLock(this);
                dragSession.Cancel();
                CancelEquipmentDrag();
                ClearPlacementPreview();
                StartPanelTransition(false);
            }
        }

        // 아이템 칸을 눌렀을 때 좌클릭 선택, Shift+좌클릭 분할, Ctrl+좌클릭 빠른 이동, 우클릭 사용/장착을 구분합니다.
        // 장착한 무기가 성공적으로 슬롯으로 이동하면 PlayerCombat에도 같은 슬롯을 현재 발사 무기로 선택하라고 알려 줍니다.
        public void HandleItemClick(
            InventoryEntry entry,
            PlayerInventory.GridData grid,
            PointerEventData.InputButton button,
            bool shiftPressed,
            bool controlPressed)
        {
            if (inventory == null || entry == null || grid == null || dragSession.IsActive || inventory.IsUsingItem) return;

            // 이전 아이템에서 발생한 실패 안내를 지워 새 선택의 상세 설명과 섞이지 않게 합니다.
            inventoryFeedbackMessage = string.Empty;
            if (selected != entry) CancelDropConfirmation();
            selected = entry;
            selectedGrid = grid;

            if (button == PointerEventData.InputButton.Left && shiftPressed)
            {
                CancelDropConfirmation();
                if (!inventory.TrySplit(grid, entry))
                    ShowInventoryFeedback(entry.Quantity < 2
                        ? "한 개짜리 아이템은 나눌 수 없습니다."
                        : "분할한 묶음을 놓을 공간이 없습니다.");
                RefreshAll();
                return;
            }

            if (button == PointerEventData.InputButton.Left && controlPressed)
            {
                CancelDropConfirmation();
                PlayerInventory.GridData target = GetQuickTransferTarget(grid);
                if (target == null)
                {
                    ShowInventoryFeedback("빠른 이동 대상이 없습니다.");
                }
                else if (!inventory.TryTransferToFirstSpace(grid, target, entry))
                {
                    ShowInventoryFeedback("빠른 이동 대상에 들어갈 공간이 없습니다.");
                }
                RefreshAll();
                return;
            }

            if (button == PointerEventData.InputButton.Left)
            {
                // 드래그하지 않고 짧게 선택만 한 경우에도 청록색 선택 표시가 즉시 보이도록 화면을 다시 그립니다.
                RefreshAll();
                return;
            }

            if (button != PointerEventData.InputButton.Right) return;
            CancelDropConfirmation();
            if (entry.Item.Usable)
            {
                if (!inventory.TryStartUse(grid, entry))
                    ShowInventoryFeedback(inventory.GetUseFailureReason(grid, entry));
                RefreshAll();
                return;
            }

            EquipmentSlotType slot = entry.Item.EquipmentSlot;
            if (inventory.TryEquipOrSwap(grid, entry, slot, out string equipFailure))
            {
                if (entry.Item.Type == ItemType.Weapon) combat?.TrySelectWeaponSlot(slot);
                RefreshAll();
            }
            else
            {
                ShowInventoryFeedback(slot == EquipmentSlotType.None
                    ? "이 아이템은 사용할 수도 장착할 수도 없습니다."
                    : equipFailure);
                RefreshAll();
            }
        }

        // 장비 슬롯은 좌클릭으로 무기를 선택하고 우클릭으로 장비를 루트 격자에 해제합니다.
        // 가방이나 조끼를 좌클릭하면 해당 장비를 선택 상태로 만들어 아래쪽 내부 공간을 우선 표시합니다.
        public void HandleEquipmentClick(EquipmentSlotType slot, PointerEventData.InputButton button)
        {
            if (inventory == null || inventory.IsUsingItem ||
                !inventory.Equipment.TryGetValue(slot, out InventoryEntry entry)) return;

            inventoryFeedbackMessage = string.Empty;
            if (selected != entry) CancelDropConfirmation();
            selected = entry;
            selectedGrid = null;

            if (button == PointerEventData.InputButton.Right)
            {
                if (!inventory.TryUnequip(slot))
                    ShowInventoryFeedback("장비를 주 인벤토리에 돌려놓을 공간이 없습니다.");
            }
            else if (button == PointerEventData.InputButton.Left && entry.Item.Type == ItemType.Weapon)
                combat?.TrySelectWeaponSlot(slot);

            if (button == PointerEventData.InputButton.Left && slot == EquipmentSlotType.Backpack)
                activeAuxiliaryTab = AuxiliaryGridTab.Backpack;
            else if (button == PointerEventData.InputButton.Left && slot == EquipmentSlotType.Rig)
                activeAuxiliaryTab = AuxiliaryGridTab.Rig;

            RefreshAll();
        }

        // 그리드마다 놓인 자동 정리 버튼이 어떤 보관 공간을 정리할지 이 메서드를 통해 전달합니다.
        public void HandleAutoSort(PlayerInventory.GridData grid)
        {
            if (inventory == null || grid == null || inventory.IsUsingItem) return;
            inventory.TryAutoSort(grid);
        }

        // 드래그 시작 위치에서 아이템의 왼쪽 위까지 떨어진 픽셀 거리를 기억합니다.
        // 이 거리를 드롭 계산에도 그대로 사용하므로 아이템의 가운데를 잡아도 갑자기 왼쪽 위 칸으로 튀지 않습니다.
        public void BeginItemDrag(
            InventoryItemView view,
            InventoryEntry entry,
            PlayerInventory.GridData source,
            Vector2 pointerOffset,
            PointerEventData eventData)
        {
            if (!open || inventory == null || inventory.IsUsingItem || dragSession.IsActive || equipmentDragEntry != null) return;
            if (!dragSession.Begin(view, entry, source, pointerOffset, dragLayer, eventData)) return;

            CancelDropConfirmation();
            selected = entry;
            selectedGrid = source;
        }

        // 드래그 중에는 원본 격자를 다시 계산하지 않고 화면 위의 미리보기만 마우스를 따라 움직입니다.
        // 실제 InventoryEntry 데이터는 마우스를 놓고 PlayerInventory 검증이 성공할 때까지 전혀 변경하지 않습니다.
        public void UpdateItemDrag(PointerEventData eventData)
        {
            dragSession.Update(eventData);
            UpdateItemPlacementPreview(eventData);
        }

        // 마우스를 놓은 위치의 부모에서 InventoryGridView를 찾아 서로 다른 격자 사이 이동과 교환도 같은 TryMove로 처리합니다.
        // 배치가 실패하면 PlayerInventory가 원본 데이터를 유지하고, 화면을 다시 그려 아이템이 원래 위치로 돌아온 것처럼 보이게 합니다.
        public void EndItemDrag(PointerEventData eventData)
        {
            if (!dragSession.IsActive) return;

            bool handled = false;
            GameObject hit = eventData != null ? eventData.pointerCurrentRaycast.gameObject : null;
            EquipmentSlotView targetSlotView = hit != null ? hit.GetComponentInParent<EquipmentSlotView>() : null;
            if (inventory != null && targetSlotView != null)
            {
                handled = true;
                InventoryEntry draggedEntry = dragSession.Entry;
                if (inventory.TryEquipOrSwap(
                        dragSession.SourceGrid,
                        draggedEntry,
                        targetSlotView.Slot,
                        out string equipFailure))
                {
                    if (draggedEntry.Item.Type == ItemType.Weapon)
                        combat?.TrySelectWeaponSlot(targetSlotView.Slot);
                    ShowInventoryFeedback("장비 슬롯에 장착했습니다.");
                }
                else
                {
                    ShowInventoryFeedback(equipFailure);
                }
            }

            if (!handled && inventory != null &&
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
                if (!string.IsNullOrEmpty(failure))
                    ShowInventoryFeedback(failure);
                else if (!inventory.TryMove(dragSession.SourceGrid, targetGrid, dragSession.Entry, position))
                    ShowInventoryFeedback("드롭 위치에 배치·중첩·교환할 수 없습니다.");
            }
            else if (!handled && hit != null && hit.GetComponentInParent<InventoryGridView>() == null)
                ShowInventoryFeedback("아이템을 놓을 격자나 장비 슬롯이 없습니다.");

            ClearPlacementPreview();
            dragSession.Cancel();
            RefreshAll();
        }

        // 장비 슬롯 자체는 GridData가 없으므로 별도 드래그 상태와 작은 아이콘 유령을 사용합니다.
        // 원래 장비는 슬롯에 그대로 두고 최종 드롭 성공 시 PlayerInventory.TryUnequipTo가 한 번에 옮깁니다.
        public void BeginEquipmentDrag(
            EquipmentSlotView view,
            EquipmentSlotType slot,
            PointerEventData eventData)
        {
            if (!open || inventory == null || inventory.IsUsingItem || dragSession.IsActive ||
                equipmentDragEntry != null || !inventory.Equipment.TryGetValue(slot, out InventoryEntry entry)) return;

            CancelDropConfirmation();
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
            UpdateEquipmentPlacementPreview(eventData);
        }

        public void EndEquipmentDrag(PointerEventData eventData)
        {
            if (equipmentDragEntry == null) return;

            if (TryGetTargetGrid(eventData, out _, out PlayerInventory.GridData targetGrid, out Vector2Int position))
            {
                if (!inventory.TryUnequipTo(equipmentDragSlot, targetGrid, position, out string failure))
                    ShowInventoryFeedback(failure);
                else
                    ShowInventoryFeedback("장비를 격자로 해제했습니다.");
            }
            else
            {
                ShowInventoryFeedback("장비를 놓을 대상 격자가 없습니다.");
            }

            CancelEquipmentDrag();
            ClearPlacementPreview();
            RefreshAll();
        }

        private void RefreshAll()
        {
            if (inventory == null) return;

            NormalizeSelection();
            rootGridView?.Bind(inventory.Root, this, "주 인벤토리", selected);

            if (equipmentSlotViews != null)
            {
                foreach (EquipmentSlotView slotView in equipmentSlotViews)
                {
                    if (slotView == null) continue;
                    inventory.Equipment.TryGetValue(slotView.Slot, out InventoryEntry entry);
                    slotView.Bind(entry, combat != null && combat.ActiveWeaponEntry == entry, this);
                }
            }

            EnsureAuxiliaryTabs();
            NormalizeAuxiliaryTab();
            PlayerInventory.GridData visibleContainerGrid = GetVisibleAuxiliaryGrid(out string auxiliaryTitle);
            bool hasContainer = visibleContainerGrid != null;
            if (containerSection != null) containerSection.SetActive(hasContainer);
            if (hasContainer)
            {
                containerGridView?.Bind(visibleContainerGrid, this, auxiliaryTitle, selected);
            }
            RefreshAuxiliaryTabs();

            RefreshItemDetail();
        }

        // 기본 상세 설명 아래에 현재 사용 진행률이나 최근 실패 이유를 붙입니다.
        // 진행률은 0~100%로 제한하고 실패 안내는 잠시 뒤 자동으로 사라져 다음 선택의 설명을 가리지 않게 합니다.
        private void RefreshItemDetail()
        {
            if (itemDetailText == null) return;

            string detail = ItemDetailFormatter.Build(selected, FindWeaponComparison(selected));
            detail += "\n\n좌클릭 선택/드래그 · Ctrl+좌클릭 빠른 이동 · Shift+좌클릭 절반 분할 · R 회전 · Delete 두 번 버리기";
            if (inventory?.IsUsingItem == true)
            {
                int percentage = Mathf.RoundToInt(Mathf.Clamp01(inventory.ItemUseProgress) * 100f);
                detail += $"\n\n{inventory.CurrentItemUseName} 사용 중... {percentage}%\n(Tab을 누르면 취소)";
            }
            else if (!string.IsNullOrEmpty(inventoryFeedbackMessage) && Time.unscaledTime < inventoryFeedbackExpiresAt)
            {
                detail += "\n\n안내: " + inventoryFeedbackMessage;
            }

            itemDetailText.text = detail;
        }

        private void ShowInventoryFeedback(string message)
        {
            inventoryFeedbackMessage = message ?? string.Empty;
            inventoryFeedbackExpiresAt = Time.unscaledTime + 2.5f;
        }

        // 선택한 무기와 같은 슬롯에 이미 장착된 다른 무기를 비교 대상으로 찾습니다.
        private InventoryEntry FindWeaponComparison(InventoryEntry entry)
        {
            if (entry?.Item.Type != ItemType.Weapon || inventory == null) return null;
            return inventory.Equipment.TryGetValue(entry.Item.EquipmentSlot, out InventoryEntry equipped) && equipped != entry
                ? equipped
                : null;
        }

        // 아이템이 사용, 장착, 해제 또는 이동된 뒤 선택 참조가 현재 어느 격자에 속하는지 다시 맞춥니다.
        // 이미 완전히 소비되어 어느 곳에도 없는 엔트리는 선택을 해제해 다음 R/Delete 입력이 사라진 대상을 건드리지 않게 합니다.
        private void NormalizeSelection()
        {
            if (selected == null || inventory == null) return;
            if (activeLootContainer != null && activeLootContainer.LootGrid.Entries.Contains(selected))
            {
                selectedGrid = activeLootContainer.LootGrid;
                return;
            }
            if (inventory.Root.Entries.Contains(selected))
            {
                selectedGrid = inventory.Root;
                return;
            }

            foreach (InventoryEntry equipped in inventory.Equipment.Values)
            {
                if (equipped == selected)
                {
                    selectedGrid = null;
                    return;
                }

                if (equipped.Contents != null && equipped.Contents.Entries.Contains(selected))
                {
                    selectedGrid = equipped.Contents;
                    return;
                }
            }

            selected = null;
            selectedGrid = null;
        }

        // 현재 탭이 나타내는 보조 격자를 반환합니다. 수색 중에도 Loot만 강제하지 않고 가방과 조끼 탭을 계속 선택할 수 있습니다.
        private PlayerInventory.GridData GetVisibleAuxiliaryGrid(out string title)
        {
            title = string.Empty;
            if (inventory == null) return null;

            if (activeAuxiliaryTab == AuxiliaryGridTab.Loot && activeLootContainer != null)
            {
                title = activeLootContainer.DisplayName;
                return activeLootContainer.LootGrid;
            }
            if (activeAuxiliaryTab == AuxiliaryGridTab.Backpack &&
                inventory.Equipment.TryGetValue(EquipmentSlotType.Backpack, out InventoryEntry backpack))
            {
                title = backpack.Item.DisplayName + " 내부";
                return backpack.Contents;
            }
            if (activeAuxiliaryTab == AuxiliaryGridTab.Rig &&
                inventory.Equipment.TryGetValue(EquipmentSlotType.Rig, out InventoryEntry rig))
            {
                title = rig.Item.DisplayName + " 내부";
                return rig.Contents;
            }

            // 전리품이 없고 사용자가 루트 안의 컨테이너를 선택한 경우에도 내부를 확인할 수 있게 기존 동작을 유지합니다.
            if (selected?.Contents != null && selectedGrid == inventory.Root)
            {
                title = selected.Item.DisplayName + " 내부";
                return selected.Contents;
            }
            return null;
        }

        private void NormalizeAuxiliaryTab()
        {
            bool hasLoot = activeLootContainer != null;
            bool hasBackpack = inventory != null && inventory.Equipment.ContainsKey(EquipmentSlotType.Backpack);
            bool hasRig = inventory != null && inventory.Equipment.ContainsKey(EquipmentSlotType.Rig);

            if (activeAuxiliaryTab == AuxiliaryGridTab.Loot && hasLoot) return;
            if (activeAuxiliaryTab == AuxiliaryGridTab.Backpack && hasBackpack) return;
            if (activeAuxiliaryTab == AuxiliaryGridTab.Rig && hasRig) return;

            activeAuxiliaryTab = hasLoot
                ? AuxiliaryGridTab.Loot
                : hasBackpack ? AuxiliaryGridTab.Backpack : hasRig ? AuxiliaryGridTab.Rig : AuxiliaryGridTab.None;
        }

        // Ctrl+좌클릭은 현재 보이는 양쪽 영역 사이에서 전체 묶음을 옮깁니다.
        // 외부 전리품·가방·조끼 어느 탭이든 보조 격자에서 누르면 플레이어 루트가 우선 목적지가 됩니다.
        private PlayerInventory.GridData GetQuickTransferTarget(PlayerInventory.GridData source)
        {
            PlayerInventory.GridData auxiliary = GetVisibleAuxiliaryGrid(out _);
            if (source == inventory.Root) return auxiliary;
            if (source == auxiliary || source == activeLootContainer?.LootGrid) return inventory.Root;

            foreach (InventoryEntry equipped in inventory.Equipment.Values)
                if (source == equipped.Contents) return inventory.Root;
            return null;
        }

        private void UpdateItemPlacementPreview(PointerEventData eventData)
        {
            if (!dragSession.IsActive) return;

            GameObject hit = eventData != null ? eventData.pointerCurrentRaycast.gameObject : null;
            EquipmentSlotView slotView = hit != null ? hit.GetComponentInParent<EquipmentSlotView>() : null;
            if (slotView != null)
            {
                ClearPlacementPreview();
                bool compatible = dragSession.Entry.Item.EquipmentSlot == slotView.Slot &&
                                  slotView.Slot != EquipmentSlotType.None;
                ShowInventoryFeedback(compatible
                    ? "장비 슬롯에 놓으면 장착하거나 기존 장비와 교체합니다."
                    : "이 아이템과 장비 슬롯 종류가 맞지 않습니다.");
                return;
            }

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
            ShowInventoryFeedback(PreviewMessage(preview));
        }

        private void UpdateEquipmentPlacementPreview(PointerEventData eventData)
        {
            if (!TryGetTargetGrid(eventData, out InventoryGridView targetView, out PlayerInventory.GridData targetGrid, out Vector2Int position))
            {
                ClearPlacementPreview();
                return;
            }

            if (previewGridView != targetView) ClearPlacementPreview();
            previewGridView = targetView;
            bool canPlace = !InventoryGridOperations.IsGridOwnedByEntry(equipmentDragEntry, targetGrid) &&
                            inventory.CanPlace(targetGrid, equipmentDragEntry, position, equipmentDragEntry.Rotated, null);
            targetView.ShowPlacementPreview(
                equipmentDragEntry,
                position,
                canPlace ? InventoryPlacementPreview.Move : InventoryPlacementPreview.Invalid);
            ShowInventoryFeedback(canPlace ? "장비 해제 가능" : "그 위치에는 장비를 해제할 수 없습니다.");
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

        private void ClearPlacementPreview()
        {
            previewGridView?.ClearPlacementPreview();
            previewGridView = null;
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

        private void HandleDeleteRequest()
        {
            string failure = inventory.GetDropFailureReason(selectedGrid, selected);
            if (!string.IsNullOrEmpty(failure))
            {
                CancelDropConfirmation();
                ShowInventoryFeedback(failure);
                RefreshItemDetail();
                return;
            }

            if (pendingDropEntry == selected && pendingDropGrid == selectedGrid && Time.unscaledTime < pendingDropExpiresAt)
            {
                if (inventory.TryDrop(selectedGrid, selected))
                {
                    selected = null;
                    selectedGrid = null;
                    ShowInventoryFeedback("아이템을 바닥에 버렸습니다.");
                }
                else
                {
                    ShowInventoryFeedback(inventory.GetDropFailureReason(selectedGrid, selected));
                }
                CancelDropConfirmation();
                RefreshAll();
                return;
            }

            pendingDropEntry = selected;
            pendingDropGrid = selectedGrid;
            bool needsLongerConfirmation = selected.Item.Rarity != ItemRarity.Common ||
                                           selected.Item.EquipmentSlot != EquipmentSlotType.None;
            pendingDropExpiresAt = Time.unscaledTime + (needsLongerConfirmation ? 4f : 2.5f);
            ShowInventoryFeedback("정말 버리려면 제한 시간 안에 Delete를 다시 누르세요.");
            RefreshItemDetail();
        }

        private void CancelDropConfirmation()
        {
            pendingDropEntry = null;
            pendingDropGrid = null;
            pendingDropExpiresAt = 0f;
        }

        private void EnsureAuxiliaryTabs()
        {
            if (containerSection == null || auxiliaryTabRoot != null) return;
            Transform existing = containerSection.transform.Find("AuxiliaryTabs");
            if (existing != null) auxiliaryTabRoot = existing.gameObject;
            if (auxiliaryTabRoot == null)
            {
                auxiliaryTabRoot = new GameObject("AuxiliaryTabs", typeof(RectTransform));
                auxiliaryTabRoot.transform.SetParent(containerSection.transform, false);
            }

            RectTransform rootRect = auxiliaryTabRoot.transform as RectTransform;
            rootRect.anchorMin = new Vector2(0f, 1f);
            rootRect.anchorMax = new Vector2(0f, 1f);
            rootRect.pivot = new Vector2(0f, 1f);
            rootRect.anchoredPosition = new Vector2(8f, -4f);
            rootRect.sizeDelta = new Vector2(390f, 32f);

            lootTabButton = EnsureTabButton(auxiliaryTabRoot.transform, "LootTab", "전리품", 0f, AuxiliaryGridTab.Loot);
            backpackTabButton = EnsureTabButton(auxiliaryTabRoot.transform, "BackpackTab", "가방", 128f, AuxiliaryGridTab.Backpack);
            rigTabButton = EnsureTabButton(auxiliaryTabRoot.transform, "RigTab", "조끼", 256f, AuxiliaryGridTab.Rig);
            auxiliaryTabRoot.transform.SetAsLastSibling();
        }

        private Button EnsureTabButton(
            Transform parent,
            string objectName,
            string label,
            float x,
            AuxiliaryGridTab tab)
        {
            Transform existing = parent.Find(objectName);
            Button button = existing != null ? existing.GetComponent<Button>() : null;
            if (button == null)
            {
                var buttonObject = new GameObject(objectName, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
                buttonObject.transform.SetParent(parent, false);
                button = buttonObject.GetComponent<Button>();
                button.targetGraphic = buttonObject.GetComponent<Image>();
                var labelObject = new GameObject("Label", typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
                labelObject.transform.SetParent(buttonObject.transform, false);
                Text text = labelObject.GetComponent<Text>();
                text.font = itemDetailText != null ? itemDetailText.font : Resources.GetBuiltinResource<Font>("Arial.ttf");
                text.alignment = TextAnchor.MiddleCenter;
                text.color = Color.white;
                text.raycastTarget = false;
                RectTransform labelRect = text.rectTransform;
                labelRect.anchorMin = Vector2.zero;
                labelRect.anchorMax = Vector2.one;
                labelRect.offsetMin = Vector2.zero;
                labelRect.offsetMax = Vector2.zero;
            }

            RectTransform rect = button.transform as RectTransform;
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(x, 0f);
            rect.sizeDelta = new Vector2(120f, 30f);
            Text buttonText = button.GetComponentInChildren<Text>();
            if (buttonText != null) buttonText.text = label;
            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(() => SelectAuxiliaryTab(tab));
            return button;
        }

        private void SelectAuxiliaryTab(AuxiliaryGridTab tab)
        {
            activeAuxiliaryTab = tab;
            if (selectedGrid != inventory?.Root)
            {
                selected = null;
                selectedGrid = null;
            }
            CancelDropConfirmation();
            RefreshAll();
        }

        private void RefreshAuxiliaryTabs()
        {
            if (auxiliaryTabRoot == null || inventory == null) return;
            bool hasLoot = activeLootContainer != null;
            bool hasBackpack = inventory.Equipment.ContainsKey(EquipmentSlotType.Backpack);
            bool hasRig = inventory.Equipment.ContainsKey(EquipmentSlotType.Rig);
            lootTabButton?.gameObject.SetActive(hasLoot);
            backpackTabButton?.gameObject.SetActive(hasBackpack);
            rigTabButton?.gameObject.SetActive(hasRig);
            auxiliaryTabRoot.SetActive(hasLoot || hasBackpack || hasRig);
            SetTabColor(lootTabButton, activeAuxiliaryTab == AuxiliaryGridTab.Loot);
            SetTabColor(backpackTabButton, activeAuxiliaryTab == AuxiliaryGridTab.Backpack);
            SetTabColor(rigTabButton, activeAuxiliaryTab == AuxiliaryGridTab.Rig);
        }

        private static void SetTabColor(Button button, bool active)
        {
            Image image = button != null ? button.targetGraphic as Image : null;
            if (image != null)
                image.color = active
                    ? new Color(0.16f, 0.65f, 0.68f, 0.96f)
                    : new Color(0.13f, 0.17f, 0.20f, 0.94f);
        }

        private void UpdateInteractionFeedback()
        {
            if (interactionPromptText != null)
            {
                bool showPrompt = !open && !panelTransitionRunning && interaction?.Current != null;
                interactionPromptText.gameObject.SetActive(showPrompt);
                if (showPrompt) interactionPromptText.text = interaction.Current.InteractionPrompt;
            }

            if (interactionStatusText == null) return;
            bool showStatus = !open && !panelTransitionRunning && interaction != null && interaction.HasVisibleStatus;
            interactionStatusText.gameObject.SetActive(showStatus);
            if (!showStatus) return;

            interactionStatusText.text = interaction.StatusMessage;
            interactionStatusText.color = interaction.LastInteractionSucceeded
                ? new Color(0.35f, 1f, 0.55f)
                : new Color(1f, 0.4f, 0.3f);
        }

        // 열기에서는 먼저 패널을 활성화한 뒤 알파와 크기를 1까지 올리고, 닫기에서는 반대로 줄인 뒤 마지막에 비활성화합니다.
        // unscaledDeltaTime을 사용하므로 다른 화면이 게임 시간을 멈추더라도 인벤토리 전환 효과는 끝까지 재생됩니다.
        private void StartPanelTransition(bool opening)
        {
            EnsurePanelAnimationComponents();
            if (panelTransition != null) StopCoroutine(panelTransition);

            if (!isActiveAndEnabled || inventoryPanel == null || inventoryPanelGroup == null || inventoryPanelRect == null)
            {
                ApplyOpenStateImmediately();
                return;
            }

            inventoryPanel.SetActive(true);
            inventoryPanelGroup.interactable = opening;
            inventoryPanelGroup.blocksRaycasts = opening;
            if (interactionPromptText != null) interactionPromptText.gameObject.SetActive(false);
            if (interactionStatusText != null) interactionStatusText.gameObject.SetActive(false);
            panelTransitionRunning = true;
            panelTransition = StartCoroutine(AnimatePanel(opening));
        }

        private IEnumerator AnimatePanel(bool opening)
        {
            float duration = opening ? openAnimationDuration : closeAnimationDuration;
            float startAlpha = inventoryPanelGroup.alpha;
            float targetAlpha = opening ? 1f : 0f;
            float startScale = inventoryPanelRect.localScale.x;
            float targetScale = opening ? 1f : closedScale;
            float elapsed = 0f;

            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                float eased = EaseTransition(elapsed / duration);
                inventoryPanelGroup.alpha = Mathf.Lerp(startAlpha, targetAlpha, eased);
                float scale = Mathf.Lerp(startScale, targetScale, eased);
                inventoryPanelRect.localScale = new Vector3(scale, scale, 1f);
                yield return null;
            }

            inventoryPanelGroup.alpha = targetAlpha;
            inventoryPanelRect.localScale = new Vector3(targetScale, targetScale, 1f);
            if (!opening && !open) inventoryPanel.SetActive(false);
            panelTransition = null;
            panelTransitionRunning = false;
        }

        // SmoothStep 곡선을 사용해 시작과 끝이 갑자기 꺾이지 않는 부드러운 전환 비율을 만듭니다.
        // 순수 함수이므로 애니메이션의 시작·중간·끝 값을 EditMode 테스트에서 별도로 확인할 수 있습니다.
        public static float EaseTransition(float progress)
        {
            float clamped = Mathf.Clamp01(progress);
            return clamped * clamped * (3f - 2f * clamped);
        }

        private void EnsurePanelAnimationComponents()
        {
            if (inventoryPanel == null) return;
            if (inventoryPanelRect == null) inventoryPanelRect = inventoryPanel.transform as RectTransform;
            if (inventoryPanelGroup == null) inventoryPanelGroup = inventoryPanel.GetComponent<CanvasGroup>();
            if (inventoryPanelGroup == null) inventoryPanelGroup = inventoryPanel.AddComponent<CanvasGroup>();

            // 전투 HUD보다 인벤토리가 항상 위에 보이도록 같은 루트 Canvas의 정렬 순서를 보장합니다.
            Canvas rootCanvas = GetComponent<Canvas>();
            if (rootCanvas != null) rootCanvas.sortingOrder = canvasSortingOrder;
        }

        private void EnsureGeneratedCharacterPreview()
        {
            if (inventoryPanel == null) return;
            Texture2D characterTexture = GeneratedArtResources.LoadTexture("Inventory/Inventory_Base_v2");
            if (characterTexture == null) return;

            Transform existing = inventoryPanel.transform.Find("CharacterPreview");
            if (existing != null) generatedCharacterPreview = existing.GetComponent<RawImage>();
            if (generatedCharacterPreview == null)
            {
                // 장비 목록 뒤에 전신 원화를 한 장 배치해 현재 장착 부위를 읽는 종이인형 영역처럼 보이게 합니다.
                // 장비 슬롯의 클릭은 그대로 받아야 하므로 이 RawImage의 Raycast는 끕니다.
                var previewObject = new GameObject(
                    "CharacterPreview",
                    typeof(RectTransform),
                    typeof(CanvasRenderer),
                    typeof(RawImage));
                previewObject.transform.SetParent(inventoryPanel.transform, false);
                generatedCharacterPreview = previewObject.GetComponent<RawImage>();
            }

            RectTransform rect = generatedCharacterPreview.rectTransform;
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            // 원본 캐릭터의 실제 세로 비율에 맞춘 크기를 사용하여 전신이 옆으로 눌려 보이지 않게 합니다.
            rect.anchoredPosition = characterPreviewPosition;
            rect.sizeDelta = characterPreviewSize;
            generatedCharacterPreview.texture = characterTexture;
            generatedCharacterPreview.material = GeneratedArtResources.GetUiWhiteKeyMaterial();
            generatedCharacterPreview.color = new Color(1f, 1f, 1f, 0.48f);
            generatedCharacterPreview.raycastTarget = false;
            // 좌우 체크무늬 여백을 조금 잘라 캐릭터가 장비 영역 안에서 더 크게 보이게 합니다.
            // RawImage의 UV 원점은 왼쪽 아래이므로 이 값도 왼쪽 아래 기준으로 작성되어 있습니다.
            generatedCharacterPreview.uvRect = new Rect(0.16f, 0.015f, 0.68f, 0.97f);
            generatedCharacterPreview.transform.SetSiblingIndex(0);
        }

        private void ApplyCharacterAndEquipmentLayout()
        {
            if (inventoryPanel == null) return;

            // 전신 그림이 차지하던 왼쪽 위 공간에는 장비 슬롯을 겹치지 않고,
            // "장비" 제목과 다섯 슬롯을 캐릭터 그림 바로 아래쪽에 차례로 배치합니다.
            RectTransform titleRect = inventoryPanel.transform.Find("EquipmentTitle") as RectTransform;
            if (titleRect != null)
                SetTopLeftLayout(titleRect, equipmentTitlePosition, new Vector2(210f, 30f));

            if (equipmentSlotViews == null) return;
            for (int index = 0; index < equipmentSlotViews.Length; index++)
            {
                EquipmentSlotView slotView = equipmentSlotViews[index];
                if (slotView == null) continue;

                RectTransform slotRect = slotView.transform as RectTransform;
                Vector2 position = equipmentFirstSlotPosition
                                   + Vector2.down * (index * equipmentSlotVerticalStep);
                SetTopLeftLayout(slotRect, position, equipmentSlotSize);
            }
        }

        private static void SetTopLeftLayout(RectTransform rect, Vector2 position, Vector2 size)
        {
            if (rect == null) return;
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
        }

        private void ApplyOpenStateImmediately()
        {
            EnsurePanelAnimationComponents();
            panelTransitionRunning = false;
            if (inventoryPanelGroup != null)
            {
                inventoryPanelGroup.alpha = open ? 1f : 0f;
                inventoryPanelGroup.interactable = open;
                inventoryPanelGroup.blocksRaycasts = open;
            }
            if (inventoryPanelRect != null)
            {
                float scale = open ? 1f : closedScale;
                inventoryPanelRect.localScale = new Vector3(scale, scale, 1f);
            }
            if (inventoryPanel != null) inventoryPanel.SetActive(open);
            if (open)
            {
                if (interactionPromptText != null) interactionPromptText.gameObject.SetActive(false);
                if (interactionStatusText != null) interactionStatusText.gameObject.SetActive(false);
            }
        }

        private void ResolvePlayerSystems()
        {
            if (inventory == null) return;
            if (controlGate == null) controlGate = inventory.GetComponent<PlayerControlGate>();
            if (combat == null) combat = inventory.GetComponent<PlayerCombat>();
        }

        private void SubscribeInventory()
        {
            if (inventory == null) return;
            inventory.InventoryChanged -= RefreshAll;
            inventory.InventoryChanged += RefreshAll;
            inventory.ItemUseStateChanged -= RefreshAll;
            inventory.ItemUseStateChanged += RefreshAll;
        }

        private void UnsubscribeInventory()
        {
            if (inventory == null) return;
            inventory.InventoryChanged -= RefreshAll;
            inventory.ItemUseStateChanged -= RefreshAll;
        }

        private void SubscribeInteraction()
        {
            if (interaction == null) return;
            interaction.LootContainerOpened -= HandleLootContainerOpened;
            interaction.LootContainerOpened += HandleLootContainerOpened;
        }

        private void UnsubscribeInteraction()
        {
            if (interaction != null) interaction.LootContainerOpened -= HandleLootContainerOpened;
        }

        // E키로 시체나 상자를 수색하면 오른쪽 보조 격자를 해당 전리품으로 바꾸고 인벤토리 창을 엽니다.
        // 플레이어 루트 격자와 외부 격자가 동시에 보이므로 아이템을 원하는 위치로 직접 드래그할 수 있습니다.
        private void HandleLootContainerOpened(WorldLootContainer container)
        {
            if (container == null) return;
            activeLootContainer = container;
            activeAuxiliaryTab = AuxiliaryGridTab.Loot;
            CancelDropConfirmation();
            selected = null;
            selectedGrid = null;
            if (!open) SetOpen(true);
            else RefreshAll();
        }

        private void OnDisable()
        {
            UnsubscribeInventory();
            UnsubscribeInteraction();
            if (panelTransition != null) StopCoroutine(panelTransition);
            panelTransition = null;
            if (controlGate != null) controlGate.ReleaseLock(this);
            open = false;
            activeLootContainer = null;
            activeAuxiliaryTab = AuxiliaryGridTab.None;
            dragSession.Cancel();
            CancelEquipmentDrag();
            ClearPlacementPreview();
            CancelDropConfirmation();
            ApplyOpenStateImmediately();
        }
    }
}
