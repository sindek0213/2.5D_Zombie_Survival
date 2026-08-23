using UnityEngine;
using ZombieSurvival.Inventory;
using ZombieSurvival.Items;
using ZombieSurvival.Player;

namespace ZombieSurvival.UI
{
    // PlayerInventory의 루트 격자, 장비 슬롯과 장착 컨테이너 내부를 화면에 그립니다.
    // 이 클래스는 마우스 좌표를 격자 좌표로 변환하고 사용자 명령만 전달하며 실제 배치 가능 판정은 PlayerInventory가 담당합니다.
    // 따라서 UI에서 잘못된 위치를 요청해도 데이터 계층이 거부해 아이템 겹침이나 유실을 방지합니다.
    public sealed class InventoryHud : MonoBehaviour
    {
        private const float Cell = 50f;
        [SerializeField] private PlayerInventory inventory;
        [SerializeField] private PlayerInteraction interaction;
        private bool open;
        private InventoryEntry selected;
        private PlayerInventory.GridData selectedGrid;
        private InventoryEntry dragging;
        private PlayerInventory.GridData dragSource;
        private Vector2 dragOffset;
        private GUIStyle text;

        public void Configure(PlayerInventory value, PlayerInteraction interactionValue) { inventory = value; interaction = interactionValue; }

        private void Awake() { text = new GUIStyle { fontSize = 16 }; text.normal.textColor = Color.white; }

        private void Update()
        {
            // 키 입력은 프레임마다 한 번 처리되는 Update에서 읽습니다.
            // 선택된 장비 슬롯은 격자에 속하지 않으므로 selectedGrid가 null일 때 회전과 버리기를 실행하지 않습니다.
            if (Input.GetKeyDown(KeyCode.Tab)) open = !open;
            if (!open || selected == null) return;
            if (Input.GetKeyDown(KeyCode.R) && selectedGrid != null) inventory.TryRotate(selectedGrid, selected);
            if (Input.GetKeyDown(KeyCode.Delete) && selectedGrid != null) { inventory.TryDrop(selectedGrid, selected); selected = null; }
        }

        private void OnGUI()
        {
            if (inventory == null) return;
            if (!open && interaction?.Current != null) GUI.Label(new Rect(Screen.width / 2f - 170f, Screen.height - 80f, 400f, 30f), interaction.Current.InteractionPrompt, text);
            if (!open) return;

            GUI.Box(new Rect(20f, 20f, Screen.width - 40f, Screen.height - 40f), GUIContent.none);
            GUI.Label(new Rect(40f, 35f, 800f, 28f), "Tab 닫기 | 드래그 이동 | R 회전 | Shift+클릭 분할 | 우클릭 사용/장착 | Delete 버리기", text);
            DrawEquipment(new Rect(45f, 85f, 190f, 360f));

            Rect rootRect = new Rect(260f, 85f, inventory.Root.Width * Cell, inventory.Root.Height * Cell);
            DrawGrid(inventory.Root, rootRect, "주 인벤토리");

            InventoryEntry container = SelectedEquippedContainer();
            if (container?.Contents != null)
            {
                Rect containerRect = new Rect(260f, rootRect.yMax + 70f, container.Contents.Width * Cell, container.Contents.Height * Cell);
                DrawGrid(container.Contents, containerRect, container.Item.DisplayName + " 내부");
            }

            if (dragging != null)
                DrawEntry(dragging, new Rect(Event.current.mousePosition - dragOffset, new Vector2(dragging.Width * Cell, dragging.Height * Cell)));

            // 어느 격자에도 놓지 않고 버튼을 놓으면 드래그만 취소하고 원래 배치는 유지합니다.
            if (Event.current.type == EventType.MouseUp && Event.current.button == 0 && dragging != null)
            {
                dragging = null;
                dragSource = null;
                Event.current.Use();
            }
        }

        private void DrawEquipment(Rect area)
        {
            GUI.Label(new Rect(area.x, area.y - 28f, area.width, 25f), "장비", text);
            int row = 0;
            foreach (EquipmentSlotType slot in new[] { EquipmentSlotType.PrimaryWeapon, EquipmentSlotType.SecondaryWeapon, EquipmentSlotType.Armor, EquipmentSlotType.Rig, EquipmentSlotType.Backpack })
            {
                inventory.Equipment.TryGetValue(slot, out InventoryEntry entry);
                string label = entry == null ? SlotName(slot) + "\n-" : SlotName(slot) + "\n" + entry.Item.DisplayName;
                if (GUI.Button(new Rect(area.x, area.y + row * 64f, area.width, 56f), label) && entry != null)
                {
                    selected = entry;
                    selectedGrid = null;
                    if (Event.current.button == 1) inventory.TryUnequip(slot);
                }
                row++;
            }
        }

        private void DrawGrid(PlayerInventory.GridData grid, Rect area, string title)
        {
            // 먼저 빈 셀을 모두 그리고 그 위에 각 InventoryEntry의 현재 점유 크기만큼 아이템 상자를 그립니다.
            // dragging 엔트리는 원래 위치에 중복 표시하지 않고 OnGUI 마지막에 마우스를 따라 별도로 그립니다.
            GUI.Label(new Rect(area.x, area.y - 26f, area.width, 24f), title, text);
            for (int y = 0; y < grid.Height; y++) for (int x = 0; x < grid.Width; x++) GUI.Box(CellRect(area, x, y, 1, 1), GUIContent.none);
            foreach (InventoryEntry entry in grid.Entries) if (entry != dragging) DrawEntry(entry, CellRect(area, entry.Position.x, entry.Position.y, entry.Width, entry.Height));
            HandleGridInput(grid, area);
        }

        private void HandleGridInput(PlayerInventory.GridData grid, Rect area)
        {
            // MouseDown 시 클릭한 엔트리를 선택하고 Shift, 우클릭, 좌클릭 우선순서로 명령을 구분합니다.
            // MouseUp 시 화면 픽셀을 Cell 크기로 나눠 목표 격자 칸을 계산하고 source/target 격자를 함께 전달합니다.
            // TryMove가 false이면 엔트리 위치를 변경하지 않았으므로 화면에도 원래 자리로 되돌아갑니다.
            Event e = Event.current;
            if (e.type == EventType.MouseDown)
            {
                for (int i = grid.Entries.Count - 1; i >= 0; i--)
                {
                    InventoryEntry entry = grid.Entries[i]; Rect rect = CellRect(area, entry.Position.x, entry.Position.y, entry.Width, entry.Height);
                    if (!rect.Contains(e.mousePosition)) continue;
                    selected = entry; selectedGrid = grid;
                    if (e.shift) inventory.TrySplit(grid, entry);
                    else if (e.button == 1 && !inventory.TryUse(grid, entry)) inventory.TryEquip(grid, entry);
                    else if (e.button == 0) { dragging = entry; dragSource = grid; dragOffset = e.mousePosition - rect.position; }
                    e.Use(); return;
                }
            }
            if (e.type == EventType.MouseUp && e.button == 0 && dragging != null && area.Contains(e.mousePosition))
            {
                Vector2 p = e.mousePosition - dragOffset - area.position;
                inventory.TryMove(dragSource, grid, dragging, new Vector2Int(Mathf.RoundToInt(p.x / Cell), Mathf.RoundToInt(p.y / Cell)));
                dragging = null; dragSource = null; e.Use();
            }
        }

        private void DrawEntry(InventoryEntry entry, Rect rect)
        {
            Color old = GUI.color; GUI.color = entry == selected ? Color.cyan : new Color(0.35f, 0.55f, 0.42f);
            GUI.Box(new Rect(rect.x + 2f, rect.y + 2f, rect.width - 4f, rect.height - 4f), entry.Item.DisplayName + "\nx" + entry.Quantity); GUI.color = old;
        }

        private InventoryEntry SelectedEquippedContainer()
        {
            if (selected?.Contents != null && !inventory.Root.Entries.Contains(selected)) return selected;
            if (inventory.Equipment.TryGetValue(EquipmentSlotType.Backpack, out InventoryEntry bag)) return bag;
            return inventory.Equipment.TryGetValue(EquipmentSlotType.Rig, out InventoryEntry rig) ? rig : null;
        }

        private static Rect CellRect(Rect area, int x, int y, int w, int h) => new Rect(area.x + x * Cell, area.y + y * Cell, w * Cell, h * Cell);
        private static string SlotName(EquipmentSlotType slot) => slot == EquipmentSlotType.PrimaryWeapon ? "주무기" : slot == EquipmentSlotType.SecondaryWeapon ? "보조무기" : slot == EquipmentSlotType.Armor ? "방탄복" : slot == EquipmentSlotType.Rig ? "전술 조끼" : "가방";
    }
}
