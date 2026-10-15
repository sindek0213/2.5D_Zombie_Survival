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

        // 아이템 View가 길게 누르기 시간을 직접 판단한 뒤, 실제 팝업 내용과 위치 표시는 현재 화면의 HUD에 요청합니다.
        // 레이드와 거점은 비교 무기를 찾는 데이터가 서로 다르므로 View가 상세 문자열을 직접 만들지 않습니다.
        void ShowItemDetailPopup(InventoryEntry entry, Vector2 screenPosition, Camera eventCamera);
        void HideItemDetailPopup(InventoryEntry entry);
    }
}
