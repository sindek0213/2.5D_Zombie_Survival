using System;
using UnityEngine;
using ZombieSurvival.Inventory;
using ZombieSurvival.Items;

namespace ZombieSurvival.Combat
{
    // 플레이어의 방탄복 장비 슬롯을 읽어 들어오는 피해 일부를 내구도로 대신 받습니다.
    // 피해를 실제로 줄이는 계산만 담당하고, 방탄복 장착·해제와 저장은 기존 인벤토리 시스템에 맡깁니다.
    [RequireComponent(typeof(PlayerInventory))]
    public sealed class ArmorProtection : MonoBehaviour, IIncomingDamageModifier
    {
        private PlayerInventory inventory;

        public InventoryEntry EquippedArmor => FindEquippedArmor();
        public float CurrentDurability => EquippedArmor != null ? EquippedArmor.ArmorDurability : 0f;
        public float MaximumDurability => EquippedArmor != null ? EquippedArmor.Item.ArmorMaxDurability : 0f;
        public float DamageReduction => EquippedArmor != null ? EquippedArmor.Item.ArmorDamageReduction : 0f;

        // HUD나 효과가 방탄복 손상 직후 값을 갱신할 수 있도록 현재·최대 내구도를 전달합니다.
        public event Action<float, float> ArmorChanged;

        private void Awake() => inventory = GetComponent<PlayerInventory>();

        public float ModifyIncomingDamage(float incomingDamage, Vector3 hitPoint)
        {
            InventoryEntry armor = FindEquippedArmor();
            // 방어구를 착용하지 않았거나 내구도가 모두 닳았다면 피해를 줄이지 않습니다.
            // 내구도가 0인 방어구도 장비 슬롯에는 남아 있으므로 HUD에서는 0 / 최대 내구도로 계속 표시할 수 있습니다.
            if (armor == null || armor.ArmorDurability <= 0f) return Mathf.Max(0f, incomingDamage);

            float finalDamage = CalculateDamageAfterArmor(
                incomingDamage,
                armor.Item.ArmorDamageReduction,
                armor.ArmorDurability,
                out float absorbedDamage);
            if (absorbedDamage <= 0f) return finalDamage;

            // 계산에서 정한 흡수량과 실제 내구도 감소량을 같게 유지하면 피해가 사라지거나 내구도만 과도하게 줄지 않습니다.
            armor.ConsumeArmorDurability(absorbedDamage);
            inventory?.NotifyItemStateChanged();
            ArmorChanged?.Invoke(armor.ArmorDurability, armor.Item.ArmorMaxDurability);
            return finalDamage;
        }

        private InventoryEntry FindEquippedArmor()
        {
            if (inventory == null) inventory = GetComponent<PlayerInventory>();
            if (inventory == null ||
                !inventory.Equipment.TryGetValue(EquipmentSlotType.Armor, out InventoryEntry armor) ||
                !armor.HasArmorState)
                return null;
            return armor;
        }

        // 감소율로 계산한 흡수 예정량과 남은 내구도 중 작은 값만 실제로 막습니다.
        // 예를 들어 피해 20, 감소율 40%, 내구도 3이면 8이 아니라 3만 흡수하여 체력에는 17 피해가 들어갑니다.
        public static float CalculateDamageAfterArmor(
            float incomingDamage,
            float reduction,
            float durability,
            out float absorbedDamage)
        {
            float safeDamage = Mathf.Max(0f, incomingDamage);
            float requestedAbsorption = safeDamage * Mathf.Clamp01(reduction);
            absorbedDamage = Mathf.Min(requestedAbsorption, Mathf.Max(0f, durability));
            return Mathf.Max(0f, safeDamage - absorbedDamage);
        }
    }
}
