using UnityEngine;
using UnityEngine.EventSystems;
using ZombieSurvival.Inventory;

namespace ZombieSurvival.UI
{
    // 같은 격자 프리팹을 플레이어 인벤토리와 거점 창고가 함께 사용하기 위한 화면 명령 약속입니다.
    // InventoryGridView는 자신이 어느 화면에 놓였는지 몰라도 이 인터페이스를 통해 클릭·드래그·자동 정리 요청을 전달할 수 있습니다.
    public interface IInventoryGridViewOwner
    {
        void HandleItemClick(
            InventoryEntry entry,
            PlayerInventory.GridData grid,
            PointerEventData.InputButton button,
            bool shiftPressed,
            bool controlPressed);

        void HandleAutoSort(PlayerInventory.GridData grid);

        void BeginItemDrag(
            InventoryItemView view,
            InventoryEntry entry,
            PlayerInventory.GridData source,
            Vector2 pointerOffset,
            PointerEventData eventData);

        void UpdateItemDrag(PointerEventData eventData);
        void EndItemDrag(PointerEventData eventData);
    }
}
