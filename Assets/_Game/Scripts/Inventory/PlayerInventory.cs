using System;
using System.Collections.Generic;
using UnityEngine;
using ZombieSurvival.Combat;
using ZombieSurvival.Items;
using ZombieSurvival.Player;

namespace ZombieSurvival.Inventory
{
    // 플레이어의 루트 인벤토리, 장비 슬롯과 가방/조끼 내부 격자를 관리하는 데이터 중심 컴포넌트입니다.
    // UI는 직접 목록을 수정하지 않고 이 클래스의 Try 메서드를 호출해야 충돌 검사와 이벤트 통지가 유지됩니다.
    // Try로 시작하는 메서드는 실패 시 false를 반환하며 가능한 한 기존 데이터를 변경하지 않습니다.
    public sealed class PlayerInventory : MonoBehaviour
    {
        [Serializable]
        public sealed class GridData
        {
            // GridData는 하나의 직사각형 보관 공간입니다.
            // Root뿐 아니라 장착한 가방과 조끼도 같은 구조를 사용하므로 이동 알고리즘을 재사용할 수 있습니다.
            public int Width { get; }
            public int Height { get; }
            public List<InventoryEntry> Entries { get; } = new List<InventoryEntry>();

            public GridData(int width, int height)
            {
                Width = Mathf.Max(1, width);
                Height = Mathf.Max(1, height);
            }
        }

        [SerializeField, Min(1)] private int gridWidth = 10;
        [SerializeField, Min(1)] private int gridHeight = 6;
        private readonly Dictionary<EquipmentSlotType, InventoryEntry> equipment = new Dictionary<EquipmentSlotType, InventoryEntry>();

        public GridData Root { get; private set; }
        public IReadOnlyList<InventoryEntry> Entries => Root.Entries;
        public IReadOnlyDictionary<EquipmentSlotType, InventoryEntry> Equipment => equipment;
        public int GridWidth => Root.Width;
        public int GridHeight => Root.Height;
        public event Action InventoryChanged;
        public event Action<ItemDefinition, int> DropRequested;

        private void Awake() => Root = new GridData(gridWidth, gridHeight);

        public bool TryAdd(ItemDefinition item, int quantity) => TryAddTo(Root, item, quantity);
        public bool CanAdd(ItemDefinition item, int quantity) => CanAddTo(Root, item, quantity);

        // 탈출 조건이나 퀘스트 시스템이 고유 ID 기준으로 현재 보유 수량을 확인할 때 사용합니다.
        // 루트 인벤토리뿐 아니라 장착한 장비와 가방/조끼 내부를 재귀적으로 검사하므로,
        // 퀘스트 아이템을 가방 안에 넣어도 정상적으로 보유한 것으로 판정됩니다.
        public int CountItem(string itemId)
        {
            if (string.IsNullOrWhiteSpace(itemId)) return 0;

            int total = CountInGrid(Root, itemId);
            foreach (InventoryEntry equipped in equipment.Values)
            {
                if (equipped.Item.ItemId == itemId) total += equipped.Quantity;
                if (equipped.Contents != null) total += CountInGrid(equipped.Contents, itemId);
            }
            return total;
        }

        // 하나의 격자와 그 안에 든 모든 컨테이너를 깊이 우선으로 순회해 수량을 합산합니다.
        // 현재 UI는 장착 컨테이너 한 단계만 보여 주지만 데이터에 중첩 컨테이너가 생겨도 조건 판정은 누락하지 않습니다.
        private static int CountInGrid(GridData grid, string itemId)
        {
            int total = 0;
            foreach (InventoryEntry entry in grid.Entries)
            {
                if (entry.Item.ItemId == itemId) total += entry.Quantity;
                if (entry.Contents != null) total += CountInGrid(entry.Contents, itemId);
            }
            return total;
        }

        // 먼저 CanAddTo로 전체 수량이 들어갈 수 있는지 시뮬레이션합니다.
        // 이 사전 검사가 없으면 일부 수량만 추가한 뒤 공간이 부족해져 월드 아이템과 수량이 어긋날 수 있습니다.
        // 검사를 통과하면 기존 중첩의 빈 수량을 채우고, 남은 양을 MaxStack 단위의 새 엔트리로 자동 배치합니다.
        public bool TryAddTo(GridData grid, ItemDefinition item, int quantity)
        {
            if (grid == null || item == null || quantity <= 0 || !CanAddTo(grid, item, quantity)) return false;
            int remaining = quantity;
            foreach (InventoryEntry entry in grid.Entries)
            {
                if (entry.Item != item || entry.Quantity >= item.MaxStack) continue;
                remaining -= entry.AddQuantity(remaining);
            }
            while (remaining > 0)
            {
                int stack = Mathf.Min(remaining, item.MaxStack);
                TryFindSpace(grid, item.GridWidth, item.GridHeight, null, out Vector2Int position);
                grid.Entries.Add(new InventoryEntry(item, stack, position));
                remaining -= stack;
            }
            InventoryChanged?.Invoke();
            return true;
        }

        // 실제 Entries를 변경하지 않고 같은 아이템의 중첩 여유와 빈 사각형 수를 계산합니다.
        // occupied[x,y]가 true면 해당 칸은 이미 다른 엔트리가 사용 중이라는 뜻입니다.
        // 필요한 모든 묶음의 자리를 찾은 경우에만 true를 반환합니다.
        public bool CanAddTo(GridData grid, ItemDefinition item, int quantity)
        {
            if (grid == null || item == null || quantity <= 0) return false;
            int remaining = quantity;
            foreach (InventoryEntry entry in grid.Entries)
                if (entry.Item == item) remaining -= item.MaxStack - entry.Quantity;
            if (remaining <= 0) return true;

            bool[,] occupied = BuildOccupancy(grid, null);
            while (remaining > 0)
            {
                if (!FindSpace(grid, item.GridWidth, item.GridHeight, occupied, out Vector2Int position)) return false;
                Fill(occupied, position, item.GridWidth, item.GridHeight);
                remaining -= item.MaxStack;
            }
            return true;
        }

        // source와 target이 같으면 같은 격자 안의 이동이고, 다르면 루트와 컨테이너 사이 이동입니다.
        // 먼저 target에서 배치 가능 여부를 확인하고 성공한 뒤에만 source 목록에서 제거하므로 이동 실패 시 아이템이 유실되지 않습니다.
        public bool TryMove(GridData source, GridData target, InventoryEntry entry, Vector2Int position)
        {
            if (source == null || target == null || !source.Entries.Contains(entry) || !CanPlace(target, entry, position, entry.Rotated, source == target ? entry : null)) return false;
            if (source != target) { source.Entries.Remove(entry); target.Entries.Add(entry); }
            entry.Place(position, entry.Rotated);
            InventoryChanged?.Invoke();
            return true;
        }

        public bool TryMove(InventoryEntry entry, Vector2Int position) => TryMove(Root, Root, entry, position);

        public bool TryRotate(GridData grid, InventoryEntry entry)
        {
            if (!CanPlace(grid, entry, entry.Position, !entry.Rotated, entry)) return false;
            entry.Place(entry.Position, !entry.Rotated);
            InventoryChanged?.Invoke();
            return true;
        }

        public bool TryRotate(InventoryEntry entry) => TryRotate(Root, entry);

        // ItemDefinition.EquipmentSlot이 None이면 장비가 아니므로 거부합니다.
        // 같은 종류의 슬롯이 이미 차 있거나 source에 실제로 존재하지 않는 엔트리도 거부합니다.
        // 검사를 통과한 뒤에만 격자 목록에서 제거하고 장비 Dictionary로 이동합니다.
        public bool TryEquip(GridData source, InventoryEntry entry)
        {
            EquipmentSlotType slot = entry?.Item.EquipmentSlot ?? EquipmentSlotType.None;
            if (slot == EquipmentSlotType.None || equipment.ContainsKey(slot) || !source.Entries.Remove(entry)) return false;
            equipment.Add(slot, entry);
            InventoryChanged?.Invoke();
            return true;
        }

        public bool TryUnequip(EquipmentSlotType slot)
        {
            if (!equipment.TryGetValue(slot, out InventoryEntry entry) ||
                !TryFindSpace(Root, entry.Width, entry.Height, null, out Vector2Int position)) return false;
            equipment.Remove(slot);
            entry.Place(position, entry.Rotated);
            Root.Entries.Add(entry);
            InventoryChanged?.Invoke();
            return true;
        }

        // 수량이 2 이상이어야 두 묶음으로 나눌 수 있습니다.
        // 원본도 점유물에 포함한 상태로 새 빈 공간을 먼저 찾기 때문에 새 묶음이 원본 위에 겹치지 않습니다.
        // 홀수 수량은 정수 나눗셈으로 작은 절반을 새 묶음에 넣고 나머지는 원본에 유지합니다.
        public bool TrySplit(GridData grid, InventoryEntry entry)
        {
            if (entry == null || entry.Quantity < 2 || !TryFindSpace(grid, entry.Width, entry.Height, null, out Vector2Int position)) return false;
            InventoryEntry split = entry.Split(entry.Quantity / 2);
            split.Place(position, entry.Rotated);
            grid.Entries.Add(split);
            InventoryChanged?.Invoke();
            return true;
        }

        // ItemDefinition.Usable이 false이면 아무 효과도 적용하지 않습니다.
        // 의료품은 Health.Heal이 실제로 회복에 성공했을 때만, 탄약은 예비탄 추가가 성공했을 때만 한 개 소비합니다.
        // 최대 체력처럼 효과가 필요 없는 상태에서는 수량을 줄이지 않아 아이템이 낭비되지 않습니다.
        public bool TryUse(GridData grid, InventoryEntry entry)
        {
            if (entry == null || !entry.Item.Usable) return false;
            Health health = GetComponent<Health>();
            PlayerCombat combat = GetComponent<PlayerCombat>();
            bool used = entry.Item.Type == ItemType.Healing && health != null
                ? health.Heal(35f)
                : entry.Item.Type == ItemType.Ammo && combat != null && combat.AddReserveAmmo(1);
            if (!used) return false;
            entry.Consume(1);
            if (entry.Quantity == 0) grid.Entries.Remove(entry);
            InventoryChanged?.Invoke();
            return true;
        }

        public bool TryDrop(GridData grid, InventoryEntry entry)
        {
            // 내용물이 든 컨테이너를 단순 월드 아이템으로 변환하면 내부 데이터가 손실되므로 거부합니다.
            if (entry == null || DropRequested == null || entry.Contents?.Entries.Count > 0 || !grid.Entries.Remove(entry)) return false;
            DropRequested?.Invoke(entry.Item, entry.Quantity);
            InventoryChanged?.Invoke();
            return true;
        }

        // position은 아이템 왼쪽 위 칸이며 rotated에 따라 가로와 세로를 교환합니다.
        // 먼저 격자 경계를 검사하고, 그다음 ignored를 제외한 점유 배열과 한 칸씩 비교합니다.
        // 이동 중인 자기 자신을 ignored로 넘겨야 원래 위치가 충돌로 오인되지 않습니다.
        public bool CanPlace(GridData grid, InventoryEntry entry, Vector2Int position, bool rotated, InventoryEntry ignored)
        {
            int width = rotated ? entry.Item.GridHeight : entry.Item.GridWidth;
            int height = rotated ? entry.Item.GridWidth : entry.Item.GridHeight;
            if (position.x < 0 || position.y < 0 || position.x + width > grid.Width || position.y + height > grid.Height) return false;
            bool[,] occupied = BuildOccupancy(grid, ignored);
            for (int y = position.y; y < position.y + height; y++)
            for (int x = position.x; x < position.x + width; x++) if (occupied[x, y]) return false;
            return true;
        }

        private static bool TryFindSpace(GridData grid, int width, int height, InventoryEntry ignored, out Vector2Int position)
            => FindSpace(grid, width, height, BuildOccupancy(grid, ignored), out position);

        private static bool FindSpace(GridData grid, int width, int height, bool[,] occupied, out Vector2Int position)
        {
            for (int y = 0; y <= grid.Height - height; y++)
            for (int x = 0; x <= grid.Width - width; x++)
            {
                bool free = true;
                for (int cy = y; cy < y + height && free; cy++)
                for (int cx = x; cx < x + width; cx++) if (occupied[cx, cy]) { free = false; break; }
                if (free) { position = new Vector2Int(x, y); return true; }
            }
            position = default;
            return false;
        }

        private static bool[,] BuildOccupancy(GridData grid, InventoryEntry ignored)
        {
            var result = new bool[grid.Width, grid.Height];
            foreach (InventoryEntry entry in grid.Entries)
                if (entry != ignored) Fill(result, entry.Position, entry.Width, entry.Height);
            return result;
        }

        private static void Fill(bool[,] grid, Vector2Int position, int width, int height)
        {
            for (int y = position.y; y < position.y + height; y++)
            for (int x = position.x; x < position.x + width; x++) grid[x, y] = true;
        }
    }
}
