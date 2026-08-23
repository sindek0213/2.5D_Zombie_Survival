using System;
using ZombieSurvival.Items;

namespace ZombieSurvival.Inventory
{
    // 한 인벤토리 슬롯에 들어 있는 아이템 정의와 수량을 보관합니다.
    [Serializable]
    public sealed class InventorySlot
    {
        public ItemDefinition Item { get; private set; }
        public int Quantity { get; private set; }
        public bool IsEmpty => Item == null || Quantity <= 0;

        // 빈 슬롯을 지정 아이템과 수량으로 채웁니다.
        public void Set(ItemDefinition item, int quantity)
        {
            Item = item;
            Quantity = Math.Max(0, quantity);
        }

        // 기존 중첩에 수량을 더하고 실제 추가된 양을 반환합니다.
        public int Add(int quantity)
        {
            if (IsEmpty || quantity <= 0) return 0;
            int accepted = Math.Min(quantity, Item.MaxStack - Quantity);
            Quantity += accepted;
            return accepted;
        }
    }
}

