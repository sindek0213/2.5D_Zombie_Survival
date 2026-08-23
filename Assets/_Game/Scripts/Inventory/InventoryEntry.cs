using System;
using UnityEngine;
using ZombieSurvival.Items;

namespace ZombieSurvival.Inventory
{
    // ItemDefinition이 "아이템 종류"라면 InventoryEntry는 플레이어가 실제로 보유한 한 묶음입니다.
    // 같은 탄약 정의라도 서로 다른 위치에 두 묶음이 있을 수 있으므로 위치, 수량과 회전 상태를 별도로 저장합니다.
    // 가방이나 조끼인 경우 Contents에 독립적인 내부 격자를 만들어 그 안의 아이템까지 함께 보존합니다.
    [Serializable]
    public sealed class InventoryEntry
    {
        public ItemDefinition Item { get; private set; }
        public int Quantity { get; private set; }
        public Vector2Int Position { get; private set; }
        public bool Rotated { get; private set; }
        public PlayerInventory.GridData Contents { get; private set; }
        public int Width => Rotated ? Item.GridHeight : Item.GridWidth;
        public int Height => Rotated ? Item.GridWidth : Item.GridHeight;

        public InventoryEntry(ItemDefinition item, int quantity, Vector2Int position, bool rotated = false)
        {
            Item = item;
            Quantity = Mathf.Max(1, quantity);
            Position = position;
            Rotated = rotated;
            if (item.IsContainer)
                Contents = new PlayerInventory.GridData(item.ContainerWidth, item.ContainerHeight);
        }

        // 전달 수량 전체가 아니라 MaxStack까지 들어갈 수 있는 양만 Quantity에 더합니다.
        // 호출자는 반환된 실제 추가량을 원래 수량에서 빼고, 남은 수량을 새 엔트리로 배치할 수 있습니다.
        public int AddQuantity(int quantity)
        {
            int accepted = Mathf.Min(Mathf.Max(0, quantity), Item.MaxStack - Quantity);
            Quantity += accepted;
            return accepted;
        }

        // 인벤토리 검사가 성공한 뒤 위치와 회전 상태를 확정합니다.
        public void Place(Vector2Int position, bool rotated)
        {
            Position = position;
            Rotated = rotated;
        }

        // amount가 0 이하이거나 전체 수량 이상이면 원본을 비우는 잘못된 분할이므로 null을 반환합니다.
        // 성공하면 원본 Quantity를 감소시키고 같은 정의를 가진 새 엔트리를 반환하며,
        // 새 엔트리의 최종 위치는 PlayerInventory가 빈 공간을 확인한 뒤 다시 지정합니다.
        public InventoryEntry Split(int amount)
        {
            if (amount <= 0 || amount >= Quantity) return null;
            Quantity -= amount;
            return new InventoryEntry(Item, amount, Position, Rotated);
        }

        // 사용하거나 버린 수량만큼 감소했는지 반환합니다.
        public bool Consume(int amount)
        {
            if (amount <= 0 || amount > Quantity) return false;
            Quantity -= amount;
            return true;
        }
    }
}
