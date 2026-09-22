using System.Collections.Generic;
using UnityEngine;

namespace ZombieSurvival.Inventory
{
    // 자동 정리가 성공했을 때 PlayerInventory가 실제 엔트리에 적용할 수량, 위치와 회전 결과입니다.
    internal sealed class InventorySortPlacement
    {
        internal InventoryEntry Entry;
        internal int Quantity;
        internal Vector2Int Position;
        internal bool Rotated;
    }

    // 자동 정리 계산 중에만 사용하는 우선순위 정보와 가상 배치 결과입니다.
    internal sealed class InventorySortUnit
    {
        internal InventoryEntry Entry;
        internal int Quantity;
        internal int OccupiedCellCount;
        internal int CandidateCount;
        internal Vector2Int Position;
        internal bool Rotated;
    }

    // 중첩 통합, 큰 아이템 우선순위와 배치 점수를 사용해 격자 전체의 새 배치를 계산합니다.
    // 실제 GridData를 수정하지 않고 모든 아이템 배치가 성공한 경우에만 완성된 placements를 반환합니다.
    internal static class InventoryAutoSorter
    {
        internal static bool TryCreatePlan(
            PlayerInventory.GridData grid,
            out List<InventorySortPlacement> placements)
        {
            placements = null;
            if (grid == null || grid.Entries.Count == 0) return false;

            List<InventorySortUnit> units = CreateUnits(grid);
            units.Sort(CompareUnits);
            var occupied = new bool[grid.Width, grid.Height];

            foreach (InventorySortUnit unit in units)
            {
                if (!TryFindBestPlacement(grid, unit, occupied, out Vector2Int position, out bool rotated))
                    return false;

                unit.Position = position;
                unit.Rotated = rotated;
                InventoryGridUtility.Fill(occupied, unit.Entry.Item, position, rotated);
            }

            // 최종 목록 순서도 위치 기준으로 고정해 자동 정리를 반복해도 같은 결과와 같은 UI 선택 순서를 유지합니다.
            units.Sort((first, second) =>
            {
                int yCompare = first.Position.y.CompareTo(second.Position.y);
                return yCompare != 0 ? yCompare : first.Position.x.CompareTo(second.Position.x);
            });

            placements = new List<InventorySortPlacement>(units.Count);
            foreach (InventorySortUnit unit in units)
            {
                placements.Add(new InventorySortPlacement
                {
                    Entry = unit.Entry,
                    Quantity = unit.Quantity,
                    Position = unit.Position,
                    Rotated = unit.Rotated
                });
            }

            return true;
        }

        // 같은 ItemDefinition의 중첩 가능한 묶음을 가상으로 합치고 생존할 기존 엔트리를 선택합니다.
        // 내용물이 든 컨테이너는 내부 데이터 손실을 막기 위해 수량과 관계없이 항상 독립 유닛으로 보존합니다.
        private static List<InventorySortUnit> CreateUnits(PlayerInventory.GridData grid)
        {
            var result = new List<InventorySortUnit>();
            var processed = new HashSet<InventoryEntry>();

            foreach (InventoryEntry entry in grid.Entries)
            {
                if (processed.Contains(entry)) continue;
                if (entry.Item.MaxStack <= 1 || entry.Contents != null)
                {
                    processed.Add(entry);
                    result.Add(CreateUnit(grid, entry, entry.Quantity));
                    continue;
                }

                var group = new List<InventoryEntry>();
                int totalQuantity = 0;
                foreach (InventoryEntry candidate in grid.Entries)
                {
                    if (candidate.Item != entry.Item || candidate.Contents != null) continue;
                    group.Add(candidate);
                    processed.Add(candidate);
                    totalQuantity += candidate.Quantity;
                }

                int groupIndex = 0;
                while (totalQuantity > 0)
                {
                    int quantity = Mathf.Min(totalQuantity, entry.Item.MaxStack);
                    // 정상 데이터보다 큰 비정상 중첩도 정리 과정에서 수량 손실 없이 필요한 묶음 수로 복구합니다.
                    InventoryEntry survivor = groupIndex < group.Count
                        ? group[groupIndex]
                        : new InventoryEntry(entry.Item, quantity, entry.Position, entry.Rotated);
                    result.Add(CreateUnit(grid, survivor, quantity));
                    groupIndex++;
                    totalQuantity -= quantity;
                }
            }

            return result;
        }

        private static InventorySortUnit CreateUnit(
            PlayerInventory.GridData grid,
            InventoryEntry entry,
            int quantity)
        {
            return new InventorySortUnit
            {
                Entry = entry,
                Quantity = quantity,
                OccupiedCellCount = InventoryGridUtility.CountOccupiedShapeCells(entry.Item),
                CandidateCount = CountEmptyGridCandidates(grid, entry)
            };
        }

        // 배치 후보가 적은 아이템, 실제 점유 칸이 큰 아이템, 바운딩이 큰 아이템 순으로 먼저 처리합니다.
        // 모든 조건이 같으면 ItemId로 순서를 고정해 실행 환경과 목록 순서에 따른 결과 변화를 줄입니다.
        private static int CompareUnits(InventorySortUnit first, InventorySortUnit second)
        {
            int candidateCompare = first.CandidateCount.CompareTo(second.CandidateCount);
            if (candidateCompare != 0) return candidateCompare;

            int occupiedCompare = second.OccupiedCellCount.CompareTo(first.OccupiedCellCount);
            if (occupiedCompare != 0) return occupiedCompare;

            int firstArea = first.Entry.Item.GridWidth * first.Entry.Item.GridHeight;
            int secondArea = second.Entry.Item.GridWidth * second.Entry.Item.GridHeight;
            int areaCompare = secondArea.CompareTo(firstArea);
            if (areaCompare != 0) return areaCompare;
            return string.CompareOrdinal(first.Entry.Item.ItemId, second.Entry.Item.ItemId);
        }

        // 빈 격자에서 원본 방향과 회전 방향이 가질 수 있는 기준 좌표 수를 합쳐 배치 제약 정도를 계산합니다.
        private static int CountEmptyGridCandidates(PlayerInventory.GridData grid, InventoryEntry entry)
        {
            int normal = Mathf.Max(0, grid.Width - entry.Item.GridWidth + 1) *
                         Mathf.Max(0, grid.Height - entry.Item.GridHeight + 1);
            int rotated = Mathf.Max(0, grid.Width - entry.Item.GridHeight + 1) *
                          Mathf.Max(0, grid.Height - entry.Item.GridWidth + 1);
            return normal + rotated;
        }

        // 현재까지 채워진 occupied에 대해 두 회전 상태의 모든 위치를 평가해 가장 낮은 점수 후보를 선택합니다.
        private static bool TryFindBestPlacement(
            PlayerInventory.GridData grid,
            InventorySortUnit unit,
            bool[,] occupied,
            out Vector2Int position,
            out bool rotated)
        {
            int bestScore = int.MaxValue;
            position = default;
            rotated = unit.Entry.Rotated;

            for (int rotationIndex = 0; rotationIndex < 2; rotationIndex++)
            {
                bool candidateRotation = rotationIndex == 0 ? unit.Entry.Rotated : !unit.Entry.Rotated;
                int width = candidateRotation ? unit.Entry.Item.GridHeight : unit.Entry.Item.GridWidth;
                int height = candidateRotation ? unit.Entry.Item.GridWidth : unit.Entry.Item.GridHeight;

                for (int y = 0; y <= grid.Height - height; y++)
                for (int x = 0; x <= grid.Width - width; x++)
                {
                    var candidate = new Vector2Int(x, y);
                    if (!InventoryGridUtility.CanFitShapeAt(
                            grid,
                            unit.Entry.Item,
                            candidateRotation,
                            occupied,
                            candidate)) continue;

                    var simulated = (bool[,])occupied.Clone();
                    InventoryGridUtility.Fill(simulated, unit.Entry.Item, candidate, candidateRotation);
                    int score = CalculateScore(
                        grid,
                        simulated,
                        candidate,
                        candidateRotation != unit.Entry.Rotated);
                    if (score >= bestScore) continue;

                    bestScore = score;
                    position = candidate;
                    rotated = candidateRotation;
                }
            }

            return bestScore != int.MaxValue;
        }

        // 왼쪽 위에서 시작한 전체 사용 영역을 가장 크게 평가하고 고립된 한 칸 구멍과 불필요한 회전에 추가 비용을 줍니다.
        private static int CalculateScore(
            PlayerInventory.GridData grid,
            bool[,] occupied,
            Vector2Int candidatePosition,
            bool rotationChanged)
        {
            int maxX = -1;
            int maxY = -1;
            int isolatedEmptyCells = 0;

            for (int y = 0; y < grid.Height; y++)
            for (int x = 0; x < grid.Width; x++)
            {
                if (occupied[x, y])
                {
                    maxX = Mathf.Max(maxX, x);
                    maxY = Mathf.Max(maxY, y);
                    continue;
                }

                bool blockedLeft = x == 0 || occupied[x - 1, y];
                bool blockedRight = x == grid.Width - 1 || occupied[x + 1, y];
                bool blockedUp = y == 0 || occupied[x, y - 1];
                bool blockedDown = y == grid.Height - 1 || occupied[x, y + 1];
                if (blockedLeft && blockedRight && blockedUp && blockedDown) isolatedEmptyCells++;
            }

            int usedBoundingArea = (maxX + 1) * (maxY + 1);
            int topLeftOrder = candidatePosition.y * grid.Width + candidatePosition.x;
            int rotationPenalty = rotationChanged ? 1 : 0;
            return usedBoundingArea * 10000 + isolatedEmptyCells * 500 + topLeftOrder * 2 + rotationPenalty;
        }
    }
}
