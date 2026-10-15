using System;
using UnityEngine;
using ZombieSurvival.Inventory;
using ZombieSurvival.Items;

namespace ZombieSurvival.Player
{
    // 숫자 1~0의 열 개 퀵슬롯을 인벤토리 ItemId와 연결하고 실제 사용 요청을 PlayerInventory에 전달합니다.
    // 기본 두 칸만 열리며 장착한 방탄복의 QuickSlotBonus만큼 추가로 사용할 수 있습니다.
    [RequireComponent(typeof(PlayerInventory))]
    [RequireComponent(typeof(PlayerStatusEffects))]
    public sealed class PlayerQuickSlots : MonoBehaviour
    {
        public const int DefaultUnlockedSlots = 2;

        // 배열 순서가 화면 슬롯 순서와 같아 1~9 다음 마지막 칸에서 0키를 처리합니다.
        private static readonly KeyCode[] SlotKeys =
        {
            KeyCode.Alpha1,
            KeyCode.Alpha2,
            KeyCode.Alpha3,
            KeyCode.Alpha4,
            KeyCode.Alpha5,
            KeyCode.Alpha6,
            KeyCode.Alpha7,
            KeyCode.Alpha8,
            KeyCode.Alpha9,
            KeyCode.Alpha0
        };

        private PlayerInventory inventory;
        private PlayerControlGate controlGate;
        private float messageExpiresAt;

        public string LastMessage { get; private set; } = string.Empty;
        public bool HasVisibleMessage => !string.IsNullOrEmpty(LastMessage) && Time.unscaledTime < messageExpiresAt;

        // 방탄복을 교체하면 InventoryChanged가 발생하므로 HUD가 이 값을 다시 읽고 잠금 칸을 즉시 갱신합니다.
        public int UnlockedSlotCount
        {
            get
            {
                ResolveReferences();
                int armorBonus = 0;
                if (inventory != null &&
                    inventory.Equipment.TryGetValue(EquipmentSlotType.Armor, out InventoryEntry armor) &&
                    armor?.Item != null)
                    armorBonus = armor.Item.QuickSlotBonus;
                return Mathf.Clamp(DefaultUnlockedSlots + armorBonus, DefaultUnlockedSlots, QuickSlotAssignmentStore.SlotCount);
            }
        }

        public event Action SlotsChanged;
        public event Action MessageChanged;

        private void Awake() => ResolveReferences();

        private void OnEnable()
        {
            ResolveReferences();
            QuickSlotAssignmentStore.Changed += HandleAssignmentsChanged;
            if (inventory != null) inventory.InventoryChanged += HandleInventoryChanged;
        }

        private void OnDisable()
        {
            QuickSlotAssignmentStore.Changed -= HandleAssignmentsChanged;
            if (inventory != null) inventory.InventoryChanged -= HandleInventoryChanged;
        }

        private void Update()
        {
            // 인벤토리나 종료 화면이 조작을 잠근 동안에는 숫자 키가 아이템 사용으로 새어 나가지 않습니다.
            // 인벤토리 등록은 숫자 단축키가 아니라 슬롯 UI로 직접 드래그하는 방식만 사용합니다.
            if (controlGate != null && controlGate.IsLocked) return;
            for (int index = 0; index < SlotKeys.Length; index++)
            {
                if (!Input.GetKeyDown(SlotKeys[index])) continue;
                TryUseSlotAndReport(index);
                break;
            }
        }

        public bool IsSlotUnlocked(int slotIndex)
            => slotIndex >= 0 && slotIndex < UnlockedSlotCount;

        public bool TryAssign(int slotIndex, ItemDefinition item, out string message)
        {
            if (!IsValidSlot(slotIndex))
            {
                message = "존재하지 않는 퀵슬롯입니다.";
                return false;
            }
            if (!IsSlotUnlocked(slotIndex))
            {
                message = $"{GetSlotKeyLabel(slotIndex)}번 슬롯은 잠겨 있습니다. 추가 퀵슬롯이 있는 방탄복을 장착해 주세요.";
                return false;
            }
            if (item == null || !item.Usable ||
                (item.Type != ItemType.Healing && item.Type != ItemType.Food && item.Type != ItemType.BuffConsumable))
            {
                message = "치료품·음식·버프 아이템만 퀵슬롯에 등록할 수 있습니다.";
                return false;
            }
            ResolveReferences();
            if (inventory == null || inventory.CountItem(item.ItemId) <= 0)
            {
                message = "플레이어 인벤토리에 있는 아이템만 등록할 수 있습니다.";
                return false;
            }

            QuickSlotAssignmentStore.Assign(slotIndex, item.ItemId, item.DisplayName);
            message = $"{GetSlotKeyLabel(slotIndex)}번 퀵슬롯에 {item.DisplayName}을(를) 등록했습니다.";
            SetMessage(message);
            return true;
        }

        public bool TryUseSlotAndReport(int slotIndex)
        {
            bool succeeded = TryUseSlot(slotIndex, out string message);
            SetMessage(message);
            return succeeded;
        }

        public bool TryUseSlot(int slotIndex, out string message)
        {
            if (!IsValidSlot(slotIndex))
            {
                message = "존재하지 않는 퀵슬롯입니다.";
                return false;
            }
            if (!IsSlotUnlocked(slotIndex))
            {
                message = $"{GetSlotKeyLabel(slotIndex)}번 퀵슬롯은 현재 잠겨 있습니다.";
                return false;
            }
            ResolveReferences();
            if (controlGate != null && controlGate.IsLocked)
            {
                message = "현재는 퀵슬롯 아이템을 사용할 수 없습니다.";
                return false;
            }

            string itemId = QuickSlotAssignmentStore.GetItemId(slotIndex);
            if (string.IsNullOrEmpty(itemId))
            {
                message = $"{GetSlotKeyLabel(slotIndex)}번 퀵슬롯이 비어 있습니다.";
                return false;
            }
            if (inventory == null || !inventory.TryFindItem(itemId, out PlayerInventory.GridData grid, out InventoryEntry entry))
            {
                message = $"{QuickSlotAssignmentStore.GetDisplayName(slotIndex)}을(를) 가지고 있지 않습니다.";
                return false;
            }
            if (!inventory.TryStartUse(grid, entry))
            {
                message = inventory.GetUseFailureReason(grid, entry);
                return false;
            }

            message = $"{entry.Item.DisplayName} 사용을 시작했습니다.";
            return true;
        }

        // 인벤토리 퀵슬롯 UI에서 우클릭했을 때 등록만 해제하며 실제 아이템은 인벤토리에 그대로 둡니다.
        public bool TryClearSlot(int slotIndex, out string message)
        {
            if (!IsValidSlot(slotIndex))
            {
                message = "존재하지 않는 퀵슬롯입니다.";
                return false;
            }
            if (!QuickSlotAssignmentStore.Clear(slotIndex))
            {
                message = $"{GetSlotKeyLabel(slotIndex)}번 퀵슬롯이 이미 비어 있습니다.";
                SetMessage(message);
                return false;
            }
            message = $"{GetSlotKeyLabel(slotIndex)}번 퀵슬롯 등록을 해제했습니다.";
            SetMessage(message);
            return true;
        }

        // HUD가 현재 수량과 아이콘을 표시할 때 등록 ID에 해당하는 실제 인벤토리 아이템을 다시 찾습니다.
        public bool TryGetAssignedItem(int slotIndex, out ItemDefinition item, out int count)
        {
            item = null;
            count = 0;
            if (!IsValidSlot(slotIndex)) return false;
            string itemId = QuickSlotAssignmentStore.GetItemId(slotIndex);
            if (string.IsNullOrEmpty(itemId)) return false;
            ResolveReferences();
            count = inventory != null ? inventory.CountItem(itemId) : 0;
            if (inventory != null && inventory.TryFindItem(itemId, out _, out InventoryEntry entry))
                item = entry.Item;
            return true;
        }

        public string BuildSlotLabel(int slotIndex)
        {
            if (!IsValidSlot(slotIndex)) return string.Empty;
            string key = GetSlotKeyLabel(slotIndex);
            if (!IsSlotUnlocked(slotIndex)) return $"[{key}] 잠김";
            string name = QuickSlotAssignmentStore.GetDisplayName(slotIndex);
            if (string.IsNullOrEmpty(name)) return $"[{key}] 비어 있음";
            ResolveReferences();
            int count = inventory != null ? inventory.CountItem(QuickSlotAssignmentStore.GetItemId(slotIndex)) : 0;
            return $"[{key}] {name} ×{count}";
        }

        public static string GetSlotKeyLabel(int slotIndex)
            => slotIndex == 9 ? "0" : (slotIndex + 1).ToString();

        private static bool IsValidSlot(int slotIndex)
            => slotIndex >= 0 && slotIndex < QuickSlotAssignmentStore.SlotCount;

        private void ResolveReferences()
        {
            if (inventory == null) inventory = GetComponent<PlayerInventory>();
            if (controlGate == null) controlGate = GetComponent<PlayerControlGate>();
        }

        private void HandleAssignmentsChanged() => SlotsChanged?.Invoke();
        private void HandleInventoryChanged() => SlotsChanged?.Invoke();

        private void SetMessage(string message)
        {
            LastMessage = message ?? string.Empty;
            messageExpiresAt = Time.unscaledTime + 2.5f;
            MessageChanged?.Invoke();
        }
    }
}
