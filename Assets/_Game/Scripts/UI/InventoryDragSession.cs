using UnityEngine;
using UnityEngine.EventSystems;
using ZombieSurvival.Inventory;

namespace ZombieSurvival.UI
{
    // 레이드 인벤토리와 거점 창고가 공통으로 사용하는 한 번의 UI 드래그 상태입니다.
    // 실제 아이템 이동 규칙은 전혀 판단하지 않고 잡은 엔트리, 원본 격자와 마우스 오프셋만 보존해 각 화면의 컨트롤러에 전달합니다.
    // MonoBehaviour가 아니므로 Canvas나 Player에 새 컴포넌트를 붙이지 않으며 각 HUD가 일반 C# 객체로 하나씩 소유합니다.
    internal sealed class InventoryDragSession
    {
        private InventoryEntry entry;
        private PlayerInventory.GridData sourceGrid;
        private InventoryItemView draggedView;
        private Vector2 pointerOffset;

        internal bool IsActive => entry != null;
        internal InventoryEntry Entry => entry;
        internal PlayerInventory.GridData SourceGrid => sourceGrid;

        // 드래그에 필요한 참조가 모두 있고 다른 드래그가 진행 중이 아닐 때만 미리보기를 맨 위 레이어로 옮깁니다.
        // 성공 뒤에는 End 또는 Cancel이 호출될 때까지 같은 엔트리와 원본 격자를 유지합니다.
        internal bool Begin(
            InventoryItemView view,
            InventoryEntry draggedEntry,
            PlayerInventory.GridData source,
            Vector2 draggedPointerOffset,
            RectTransform dragLayer,
            PointerEventData eventData)
        {
            if (IsActive || view == null || draggedEntry == null || source == null || eventData == null)
                return false;

            entry = draggedEntry;
            sourceGrid = source;
            draggedView = view;
            pointerOffset = draggedPointerOffset;
            draggedView.SetDragging(true, dragLayer, eventData.position);
            return true;
        }

        // 실제 InventoryEntry 좌표는 바꾸지 않고 화면 위 미리보기만 현재 마우스 위치로 옮깁니다.
        internal void Update(PointerEventData eventData)
        {
            if (!IsActive || draggedView == null || eventData == null) return;
            draggedView.SetDragScreenPosition(eventData.position);
        }

        // 마우스 아래의 InventoryGridView를 찾고 처음 잡은 지점의 오프셋을 적용해 최종 격자 좌표를 계산합니다.
        // 겹침·교환·공간 부족 판정은 호출한 PlayerInventory 또는 HideoutController가 처리하도록 대상과 좌표만 반환합니다.
        internal bool TryGetDrop(
            PointerEventData eventData,
            out PlayerInventory.GridData targetGrid,
            out Vector2Int position)
            => TryGetDrop(eventData, out _, out targetGrid, out position);

        // 미리보기 색을 실제 마우스 아래의 격자에 표시할 수 있도록 대상 InventoryGridView도 함께 반환합니다.
        // 기존 드롭 호출은 위 오버로드를 계속 사용할 수 있어 화면 코드의 변경 범위를 작게 유지합니다.
        internal bool TryGetDrop(
            PointerEventData eventData,
            out InventoryGridView targetView,
            out PlayerInventory.GridData targetGrid,
            out Vector2Int position)
        {
            targetView = null;
            targetGrid = null;
            position = default;
            if (!IsActive || eventData == null) return false;

            GameObject hit = eventData.pointerCurrentRaycast.gameObject;
            targetView = hit != null
                ? hit.GetComponentInParent<InventoryGridView>()
                : null;
            if (targetView == null || targetView.Data == null) return false;
            if (!targetView.TryGetDropPosition(
                    eventData.position,
                    pointerOffset,
                    eventData.pressEventCamera,
                    out position)) return false;

            targetGrid = targetView.Data;
            return true;
        }

        // 성공 여부와 관계없이 미리보기를 숨기고 모든 참조를 비워 다음 드래그가 이전 상태를 이어받지 않게 합니다.
        internal void Cancel()
        {
            if (draggedView != null) draggedView.gameObject.SetActive(false);
            entry = null;
            sourceGrid = null;
            draggedView = null;
            pointerOffset = Vector2.zero;
        }
    }
}
