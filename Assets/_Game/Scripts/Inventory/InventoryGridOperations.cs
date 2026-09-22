using System.Collections.Generic;
using UnityEngine;

namespace ZombieSurvival.Inventory
{
    // 드래그 중 표시할 배치 결과입니다. 실제 이동 계획의 종류를 UI가 안전하게 읽을 수 있는 공개 값으로 바꿉니다.
    // Invalid는 빨간색, Move는 초록색, Merge는 청록색, Swap은 노란색 미리보기에 사용합니다.
    public enum InventoryPlacementPreview
    {
        Invalid,
        Move,
        Merge,
        Swap
    }

    // 플레이어 인벤토리와 거점 창고가 함께 사용하는 격자 변경 명령입니다.
    // 계산기는 먼저 완성된 계획을 만들고 이 클래스가 성공한 계획만 한 번에 적용하므로 어느 화면에서도 중간 상태가 남지 않습니다.
    public static class InventoryGridOperations
    {
        public static bool TryAutoSort(PlayerInventory.GridData grid)
        {
            if (!InventoryAutoSorter.TryCreatePlan(grid, out List<InventorySortPlacement> placements)) return false;

            grid.Entries.Clear();
            foreach (InventorySortPlacement placement in placements)
            {
                placement.Entry.SetQuantityAfterSort(placement.Quantity);
                placement.Entry.Place(placement.Position, placement.Rotated);
                grid.Entries.Add(placement.Entry);
            }
            return true;
        }

        public static bool TryMove(
            PlayerInventory.GridData source,
            PlayerInventory.GridData target,
            InventoryEntry entry,
            Vector2Int position)
        {
            // 가방을 자기 내부 또는 더 안쪽의 중첩 컨테이너로 옮기면 객체 참조가 순환해 저장 재귀가 끝나지 않습니다.
            // 실제 Entries를 바꾸기 전에 이 위험한 목표를 거부해 버전 5 저장 트리를 항상 단방향으로 유지합니다.
            if (IsGridOwnedByEntry(entry, target)) return false;
            if (!InventoryMoveResolver.TryResolve(source, target, entry, position, out InventoryMovePlan plan)) return false;

            switch (plan.Kind)
            {
                case InventoryMoveKind.Move:
                    if (source != target)
                    {
                        source.Entries.Remove(entry);
                        target.Entries.Add(entry);
                    }
                    entry.Place(plan.DraggedPosition, entry.Rotated);
                    return true;

                case InventoryMoveKind.Merge:
                    int mergedQuantity = plan.TargetEntry.AddQuantity(entry.Quantity);
                    if (mergedQuantity <= 0) return false;
                    entry.Consume(mergedQuantity);
                    if (entry.Quantity == 0) source.Entries.Remove(entry);
                    return true;

                case InventoryMoveKind.Swap:
                    if (plan.Placements == null || plan.Placements.Count < 2) return false;

                    // 계획을 만든 뒤 다른 명령이 목록을 바꿨다면 일부만 옮기지 않고 전체 이동을 취소합니다.
                    foreach (InventoryMovePlacement placement in plan.Placements)
                        if (!placement.SourceGrid.Entries.Contains(placement.Entry)) return false;

                    foreach (InventoryMovePlacement placement in plan.Placements)
                        if (placement.SourceGrid != placement.TargetGrid)
                            placement.SourceGrid.Entries.Remove(placement.Entry);
                    foreach (InventoryMovePlacement placement in plan.Placements)
                        if (placement.SourceGrid != placement.TargetGrid)
                            placement.TargetGrid.Entries.Add(placement.Entry);
                    foreach (InventoryMovePlacement placement in plan.Placements)
                        placement.Entry.Place(placement.Position, placement.Entry.Rotated);
                    return true;

                default:
                    return false;
            }
        }

        public static bool TryRotate(PlayerInventory.GridData grid, InventoryEntry entry)
        {
            if (grid == null || entry == null || !grid.Entries.Contains(entry) ||
                !InventoryGridUtility.CanPlace(grid, entry, entry.Position, !entry.Rotated, entry)) return false;

            entry.Place(entry.Position, !entry.Rotated);
            return true;
        }

        // 실제 드롭과 똑같은 Resolver를 실행하되 계획을 적용하지 않고 종류만 반환합니다.
        // 따라서 마우스를 놓기 전에는 좌표·수량·회전 상태가 전혀 바뀌지 않으며 미리보기와 실제 결과가 일치합니다.
        public static InventoryPlacementPreview GetPlacementPreview(
            PlayerInventory.GridData source,
            PlayerInventory.GridData target,
            InventoryEntry entry,
            Vector2Int position)
        {
            if (IsGridOwnedByEntry(entry, target) ||
                !InventoryMoveResolver.TryResolve(source, target, entry, position, out InventoryMovePlan plan))
                return InventoryPlacementPreview.Invalid;

            switch (plan.Kind)
            {
                case InventoryMoveKind.Move: return InventoryPlacementPreview.Move;
                case InventoryMoveKind.Merge: return InventoryPlacementPreview.Merge;
                case InventoryMoveKind.Swap: return InventoryPlacementPreview.Swap;
                default: return InventoryPlacementPreview.Invalid;
            }
        }

        // 드롭 실패를 단순 false로 끝내지 않고 UI에 표시할 수 있는 짧은 이유로 변환합니다.
        // 성공 가능한 경우에는 빈 문자열을 반환하므로 호출자는 같은 함수로 사전 검사와 안내를 함께 처리할 수 있습니다.
        public static string GetMoveFailureReason(
            PlayerInventory.GridData source,
            PlayerInventory.GridData target,
            InventoryEntry entry,
            Vector2Int position)
        {
            if (source == null || target == null) return "드롭할 대상 격자가 없습니다.";
            if (entry == null || !source.Entries.Contains(entry)) return "이 아이템은 출발 격자에 없습니다.";
            if (IsGridOwnedByEntry(entry, target)) return "컨테이너를 자기 내부에 넣을 수 없습니다.";
            if (!InventoryGridUtility.FitsInsideGrid(target, entry, position, entry.Rotated))
                return "아이템이 격자 경계를 벗어납니다.";
            return GetPlacementPreview(source, target, entry, position) == InventoryPlacementPreview.Invalid
                ? "그 위치에는 배치·중첩·교환할 수 없습니다."
                : string.Empty;
        }

        // 버튼으로 한 격자에서 다른 격자로 보낼 때 현재 회전을 우선하고, 없으면 반대 회전의 첫 빈칸을 찾습니다.
        public static bool TryTransferToFirstSpace(
            PlayerInventory.GridData source,
            PlayerInventory.GridData target,
            InventoryEntry entry)
        {
            if (source == null || target == null || entry == null || !source.Entries.Contains(entry)) return false;
            if (IsGridOwnedByEntry(entry, target)) return false;

            // 먼저 현재 회전 상태 그대로 들어갈 자리를 찾습니다.
            if (InventoryGridUtility.TryFindSpace(target, entry.Item, entry.Rotated, null, out Vector2Int position))
                return TryMove(source, target, entry, position);

            // 현재 방향으로 자리가 없을 때만 반대 방향을 시험합니다.
            // 시험 이동이 실패하면 반드시 원래 회전 상태로 돌려놓아, 실패한 조작이 아이템 상태를 바꾸지 않게 합니다.
            bool originalRotation = entry.Rotated;
            if (!InventoryGridUtility.TryFindSpace(target, entry.Item, !originalRotation, null, out position)) return false;

            entry.Place(entry.Position, !originalRotation);
            if (TryMove(source, target, entry, position)) return true;

            entry.Place(entry.Position, originalRotation);
            return false;
        }

        // Shift+클릭 수량 분할을 레이드와 거점이 같은 규칙으로 사용합니다.
        // 빈자리를 먼저 찾은 뒤에만 원본 수량을 줄이므로 공간 부족 시 어떤 데이터도 바뀌지 않습니다.
        public static bool TrySplit(PlayerInventory.GridData grid, InventoryEntry entry)
        {
            if (grid == null || entry == null || !grid.Entries.Contains(entry) || entry.Quantity < 2 ||
                !InventoryGridUtility.TryFindSpace(grid, entry.Item, entry.Rotated, null, out Vector2Int position))
                return false;

            InventoryEntry split = entry.Split(entry.Quantity / 2);
            if (split == null) return false;
            split.Place(position, entry.Rotated);
            grid.Entries.Add(split);
            return true;
        }

        // target이 entry가 소유한 Contents 또는 그 안쪽 컨테이너의 Contents인지 재귀적으로 검사합니다.
        // 손상 데이터가 이미 순환하더라도 무한 재귀하지 않도록 방문한 격자는 HashSet으로 한 번만 확인합니다.
        public static bool IsGridOwnedByEntry(InventoryEntry entry, PlayerInventory.GridData target)
        {
            if (entry?.Contents == null || target == null) return false;
            return ContainsGrid(entry.Contents, target, new HashSet<PlayerInventory.GridData>());
        }

        private static bool ContainsGrid(
            PlayerInventory.GridData current,
            PlayerInventory.GridData target,
            HashSet<PlayerInventory.GridData> visited)
        {
            if (current == null || !visited.Add(current)) return false;
            if (current == target) return true;
            foreach (InventoryEntry child in current.Entries)
                if (child?.Contents != null && ContainsGrid(child.Contents, target, visited)) return true;
            return false;
        }
    }
}
