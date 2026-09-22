using System.Collections.Generic;
using UnityEngine;

namespace ZombieSurvival.Inventory
{
    // PlayerInventory가 적용해야 할 데이터 변경 종류입니다.
    // Resolver는 계획만 만들고 실제 목록·수량·좌표는 변경하지 않으므로 실패 시 기존 인벤토리가 그대로 유지됩니다.
    internal enum InventoryMoveKind
    {
        Move,
        Merge,
        Swap
    }

    // 다중 교환에 참여하는 한 엔트리가 어느 격자에서 어느 격자로 이동하고 최종 좌표가 무엇인지 기록합니다.
    // 모든 엔트리의 배치가 검증된 뒤 PlayerInventory가 이 목록 전체를 한 번에 적용하므로 일부 아이템만 먼저 움직이지 않습니다.
    internal sealed class InventoryMovePlacement
    {
        internal InventoryEntry Entry;
        internal PlayerInventory.GridData SourceGrid;
        internal PlayerInventory.GridData TargetGrid;
        internal Vector2Int Position;
    }

    // 한 번의 드래그 결과를 PlayerInventory에 전달하는 읽기 전용 계획 객체입니다.
    // Move는 DraggedPosition, Merge는 TargetEntry, Swap은 여러 개가 될 수 있는 Placements를 사용합니다.
    internal sealed class InventoryMovePlan
    {
        internal InventoryMoveKind Kind;
        internal InventoryEntry TargetEntry;
        internal Vector2Int DraggedPosition;
        internal List<InventoryMovePlacement> Placements;
    }

    // 빈칸 이동, 같은 아이템 중첩과 하나 이상의 아이템을 밀어내는 수동 교환 규칙을 판단합니다.
    // 이 클래스는 UI나 MonoBehaviour 이벤트를 모르며 전달받은 격자를 읽어서 안전한 최종 계획만 반환합니다.
    internal static class InventoryMoveResolver
    {
        // 드롭 위치의 실제 점유 셀을 확인하고 Move, Merge 또는 Swap 중 하나의 계획을 만듭니다.
        internal static bool TryResolve(
            PlayerInventory.GridData source,
            PlayerInventory.GridData target,
            InventoryEntry draggedEntry,
            Vector2Int droppedPosition,
            out InventoryMovePlan plan)
        {
            plan = null;
            if (source == null || target == null || draggedEntry == null ||
                !source.Entries.Contains(draggedEntry)) return false;
            if (!InventoryGridUtility.FitsInsideGrid(
                    target,
                    draggedEntry,
                    droppedPosition,
                    draggedEntry.Rotated)) return false;

            List<InventoryEntry> overlaps = InventoryGridUtility.FindOverlappingEntries(
                target,
                draggedEntry,
                droppedPosition,
                draggedEntry.Rotated,
                source == target ? draggedEntry : null);

            if (overlaps.Count == 0)
            {
                // 아무 아이템도 없는 곳은 사용자가 요청한 좌표에 정확히 들어갈 때만 일반 이동으로 인정합니다.
                if (!InventoryGridUtility.CanPlace(
                        target,
                        draggedEntry,
                        droppedPosition,
                        draggedEntry.Rotated,
                        source == target ? draggedEntry : null)) return false;

                plan = new InventoryMovePlan
                {
                    Kind = InventoryMoveKind.Move,
                    DraggedPosition = droppedPosition
                };
                return true;
            }

            // 하나의 같은 아이템 묶음에 드롭한 경우에는 위치 교환보다 중첩 합치기를 우선합니다.
            // 두 개 이상과 겹치면 어느 한 묶음만 먼저 합치지 않고 아래의 다중 재배치 규칙으로 전체를 함께 처리합니다.
            if (overlaps.Count == 1 &&
                overlaps[0].Item == draggedEntry.Item &&
                overlaps[0].Quantity < overlaps[0].Item.MaxStack)
            {
                plan = new InventoryMovePlan
                {
                    Kind = InventoryMoveKind.Merge,
                    TargetEntry = overlaps[0]
                };
                return true;
            }

            if (!TryPlanDisplacementSwap(
                    source,
                    target,
                    draggedEntry,
                    droppedPosition,
                    overlaps,
                    out List<InventoryMovePlacement> placements)) return false;

            plan = new InventoryMovePlan
            {
                Kind = InventoryMoveKind.Swap,
                Placements = placements
            };
            return true;
        }

        // 잡은 아이템은 사용자가 놓은 좌표에 고정하고, 그 위치와 겹친 엔트리들만 원래 자리에서 가까운 빈칸으로 밀어냅니다.
        // 모든 가상 배치가 성공한 경우에만 placements를 반환하므로 중간 아이템이 들어갈 자리가 없으면 전체 드래그가 취소됩니다.
        private static bool TryPlanDisplacementSwap(
            PlayerInventory.GridData source,
            PlayerInventory.GridData target,
            InventoryEntry draggedEntry,
            Vector2Int droppedPosition,
            List<InventoryEntry> overlaps,
            out List<InventoryMovePlacement> placements)
        {
            placements = null;
            bool sameGrid = source == target;

            // 목표 격자에서는 잡은 아이템과 밀려날 엔트리들을 모두 잠시 제외해야 드롭 후의 점유 상태를 정확히 만들 수 있습니다.
            // 서로 다른 격자에서 넘어온 draggedEntry는 target 목록에 없지만 HashSet에 포함해도 결과에는 영향을 주지 않습니다.
            var removedFromTarget = new HashSet<InventoryEntry>(overlaps) { draggedEntry };
            bool[,] targetOccupancy = InventoryGridUtility.BuildOccupancyExcluding(target, removedFromTarget);
            if (!InventoryGridUtility.CanFitShapeAt(
                    target,
                    draggedEntry.Item,
                    draggedEntry.Rotated,
                    targetOccupancy,
                    droppedPosition)) return false;

            InventoryGridUtility.Fill(
                targetOccupancy,
                draggedEntry.Item,
                droppedPosition,
                draggedEntry.Rotated);

            var result = new List<InventoryMovePlacement>
            {
                new InventoryMovePlacement
                {
                    Entry = draggedEntry,
                    SourceGrid = source,
                    TargetGrid = target,
                    Position = droppedPosition
                }
            };

            // 같은 격자에서는 방금 놓은 draggedEntry가 기록된 targetOccupancy를 그대로 이어서 사용합니다.
            // 다른 격자 교환은 source에서 draggedEntry가 빠진 상태를 새로 만들어 밀려난 target 아이템들이 그 공간을 사용할 수 있게 합니다.
            bool[,] displacedOccupancy;
            if (sameGrid)
            {
                displacedOccupancy = targetOccupancy;
            }
            else
            {
                var removedFromSource = new HashSet<InventoryEntry> { draggedEntry };
                displacedOccupancy = InventoryGridUtility.BuildOccupancyExcluding(source, removedFromSource);
            }

            // 큰 엔트리를 먼저 배치하면 작은 엔트리가 남은 틈을 활용할 수 있어 반대 순서보다 성공 가능성이 높습니다.
            // 크기와 좌표가 같을 때도 ItemId로 순서를 고정하여 같은 드래그가 실행마다 다른 결과를 만들지 않게 합니다.
            overlaps.Sort(CompareDisplacedEntries);
            foreach (InventoryEntry displacedEntry in overlaps)
            {
                Vector2Int preferredPosition = sameGrid
                    ? displacedEntry.Position
                    : draggedEntry.Position;
                if (!InventoryGridUtility.TryFindClosestSpace(
                        source,
                        displacedEntry.Item,
                        displacedEntry.Rotated,
                        displacedOccupancy,
                        preferredPosition,
                        out Vector2Int displacedPosition)) return false;

                result.Add(new InventoryMovePlacement
                {
                    Entry = displacedEntry,
                    SourceGrid = target,
                    TargetGrid = source,
                    Position = displacedPosition
                });
                InventoryGridUtility.Fill(
                    displacedOccupancy,
                    displacedEntry.Item,
                    displacedPosition,
                    displacedEntry.Rotated);
            }

            placements = result;
            return true;
        }

        // 점유 셀이 많은 아이템을 먼저, 같은 크기라면 기존 행·열과 ItemId 순서로 정렬하는 결정적 비교 함수입니다.
        private static int CompareDisplacedEntries(InventoryEntry first, InventoryEntry second)
        {
            int firstCells = InventoryGridUtility.CountOccupiedShapeCells(first.Item);
            int secondCells = InventoryGridUtility.CountOccupiedShapeCells(second.Item);
            int cellComparison = secondCells.CompareTo(firstCells);
            if (cellComparison != 0) return cellComparison;

            int rowComparison = first.Position.y.CompareTo(second.Position.y);
            if (rowComparison != 0) return rowComparison;
            int columnComparison = first.Position.x.CompareTo(second.Position.x);
            if (columnComparison != 0) return columnComparison;
            return string.CompareOrdinal(first.Item.ItemId, second.Item.ItemId);
        }
    }
}
