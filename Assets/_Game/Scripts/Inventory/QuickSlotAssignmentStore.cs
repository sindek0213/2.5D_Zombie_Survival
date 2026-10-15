using System;

namespace ZombieSurvival.Inventory
{
    // 퀵슬롯은 아이템 묶음의 위치가 아니라 안정적인 ItemId와 표시 이름만 기억합니다.
    // 정적 저장소이므로 거점에서 등록한 뒤 학교 씬으로 이동해 새 Player 오브젝트가 만들어져도 같은 배치를 유지합니다.
    public static class QuickSlotAssignmentStore
    {
        // 키보드 숫자 1~9와 0을 각각 한 칸으로 사용하므로 전체 저장 공간은 열 칸입니다.
        // 실제 사용 가능한 칸 수는 PlayerQuickSlots가 기본 두 칸과 장착 방탄복 보너스로 따로 제한합니다.
        public const int SlotCount = 10;

        private static readonly string[] itemIds = new string[SlotCount];
        private static readonly string[] displayNames = new string[SlotCount];

        public static event Action Changed;

        public static string GetItemId(int slotIndex)
            => IsValidSlot(slotIndex) ? itemIds[slotIndex] ?? string.Empty : string.Empty;

        public static string GetDisplayName(int slotIndex)
            => IsValidSlot(slotIndex) ? displayNames[slotIndex] ?? string.Empty : string.Empty;

        public static bool Assign(int slotIndex, string itemId, string displayName)
        {
            if (!IsValidSlot(slotIndex) || string.IsNullOrWhiteSpace(itemId)) return false;
            itemIds[slotIndex] = itemId;
            displayNames[slotIndex] = displayName ?? itemId;
            Changed?.Invoke();
            return true;
        }

        public static bool Clear(int slotIndex)
        {
            if (!IsValidSlot(slotIndex) || string.IsNullOrEmpty(itemIds[slotIndex])) return false;
            itemIds[slotIndex] = string.Empty;
            displayNames[slotIndex] = string.Empty;
            Changed?.Invoke();
            return true;
        }

        public static void ResetForTests()
        {
            Array.Clear(itemIds, 0, itemIds.Length);
            Array.Clear(displayNames, 0, displayNames.Length);
            Changed?.Invoke();
        }

        private static bool IsValidSlot(int slotIndex) => slotIndex >= 0 && slotIndex < SlotCount;
    }
}
