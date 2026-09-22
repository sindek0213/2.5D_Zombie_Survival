using System.Collections.Generic;
using UnityEngine;
using ZombieSurvival.Items;

namespace ZombieSurvival.Inventory
{
    // 인벤토리 격자의 경계, 실제 모양 셀 충돌, 점유 배열과 빈자리 탐색만 담당하는 순수 계산 클래스입니다.
    // Entries, 수량과 위치를 직접 변경하지 않으므로 이동·자동 정리·테스트가 같은 판정 규칙을 안전하게 공유할 수 있습니다.
    internal static class InventoryGridUtility
    {
        // 아이템의 왼쪽 위 기준 좌표와 회전 후 바운딩 크기가 격자 경계 안에 전부 들어오는지 검사합니다.
        internal static bool FitsInsideGrid(
            PlayerInventory.GridData grid,
            InventoryEntry entry,
            Vector2Int position,
            bool rotated)
        {
            if (grid == null || entry == null) return false;
            int width = rotated ? entry.Item.GridHeight : entry.Item.GridWidth;
            int height = rotated ? entry.Item.GridWidth : entry.Item.GridHeight;
            return position.x >= 0 &&
                   position.y >= 0 &&
                   position.x + width <= grid.Width &&
                   position.y + height <= grid.Height;
        }

        // ignoredA와 ignoredB를 점유 계산에서 제외하고 entry를 지정 위치에 배치할 수 있는지 확인합니다.
        // 같은 격자 이동은 자기 자신을, 두 아이템 교환은 양쪽 엔트리를 제외해야 기존 위치를 충돌로 오인하지 않습니다.
        internal static bool CanPlace(
            PlayerInventory.GridData grid,
            InventoryEntry entry,
            Vector2Int position,
            bool rotated,
            InventoryEntry ignoredA,
            InventoryEntry ignoredB = null)
        {
            if (!FitsInsideGrid(grid, entry, position, rotated)) return false;
            bool[,] occupied = BuildOccupancy(grid, ignoredA, ignoredB);
            return CanFitShapeAt(grid, entry.Item, rotated, occupied, position);
        }

        // 이미 만들어진 occupied 배열을 기준으로 ItemDefinition의 true 셀만 충돌 검사합니다.
        // 마스크가 꺼진 L자 안쪽 같은 빈 셀은 다른 아이템이 사용해도 충돌로 처리하지 않습니다.
        internal static bool CanFitShapeAt(
            PlayerInventory.GridData grid,
            ItemDefinition item,
            bool rotated,
            bool[,] occupied,
            Vector2Int position)
        {
            if (grid == null || item == null || occupied == null) return false;
            int width = rotated ? item.GridHeight : item.GridWidth;
            int height = rotated ? item.GridWidth : item.GridHeight;
            if (position.x < 0 || position.y < 0 ||
                position.x + width > grid.Width || position.y + height > grid.Height) return false;

            for (int localY = 0; localY < height; localY++)
            for (int localX = 0; localX < width; localX++)
            {
                if (!item.IsGridCellOccupied(localX, localY, rotated)) continue;
                if (occupied[position.x + localX, position.y + localY]) return false;
            }

            return true;
        }

        // movingEntry를 새 위치에 두었을 때 실제 점유 셀이 겹치는 기존 엔트리를 모두 반환합니다.
        // 바운딩 사각형만 겹치고 true 셀이 만나지 않는 맞물림 배치는 정상 배치로 인정합니다.
        internal static List<InventoryEntry> FindOverlappingEntries(
            PlayerInventory.GridData grid,
            InventoryEntry movingEntry,
            Vector2Int position,
            bool rotated,
            InventoryEntry ignored)
        {
            var overlaps = new List<InventoryEntry>();
            int movingWidth = rotated ? movingEntry.Item.GridHeight : movingEntry.Item.GridWidth;
            int movingHeight = rotated ? movingEntry.Item.GridWidth : movingEntry.Item.GridHeight;

            foreach (InventoryEntry other in grid.Entries)
            {
                if (other == ignored) continue;

                bool foundOverlap = false;
                for (int localY = 0; localY < movingHeight && !foundOverlap; localY++)
                for (int localX = 0; localX < movingWidth; localX++)
                {
                    if (!movingEntry.Item.IsGridCellOccupied(localX, localY, rotated)) continue;
                    if (!other.OccupiesGridCell(position.x + localX, position.y + localY)) continue;
                    foundOverlap = true;
                    break;
                }

                if (foundOverlap) overlaps.Add(other);
            }

            return overlaps;
        }

        // 현재 Entries를 occupied 배열로 바꾼 뒤 item 모양이 들어가는 첫 번째 위치를 찾습니다.
        internal static bool TryFindSpace(
            PlayerInventory.GridData grid,
            ItemDefinition item,
            bool rotated,
            InventoryEntry ignored,
            out Vector2Int position)
        {
            if (grid == null || item == null)
            {
                position = default;
                return false;
            }

            return FindSpace(grid, item, rotated, BuildOccupancy(grid, ignored), out position);
        }

        // 위에서 아래, 왼쪽에서 오른쪽 순서로 모든 기준 좌표를 검사해 가장 앞쪽의 유효 위치를 반환합니다.
        // 자동 추가와 분할이 동일한 순서를 사용하므로 같은 입력에서는 항상 같은 위치가 선택됩니다.
        internal static bool FindSpace(
            PlayerInventory.GridData grid,
            ItemDefinition item,
            bool rotated,
            bool[,] occupied,
            out Vector2Int position)
        {
            int width = rotated ? item.GridHeight : item.GridWidth;
            int height = rotated ? item.GridWidth : item.GridHeight;
            for (int y = 0; y <= grid.Height - height; y++)
            for (int x = 0; x <= grid.Width - width; x++)
            {
                var candidate = new Vector2Int(x, y);
                if (!CanFitShapeAt(grid, item, rotated, occupied, candidate)) continue;
                position = candidate;
                return true;
            }

            position = default;
            return false;
        }

        // preferredPosition에서 가장 가까우면서 현재 점유 배열과 충돌하지 않는 위치를 격자 전체에서 찾습니다.
        // 수동 교환으로 밀려난 아이템은 전역 자동 정리처럼 왼쪽 위로 보내지 않고 원래 자리 주변의 최소 이동 후보를 선택해야 합니다.
        internal static bool TryFindClosestSpace(
            PlayerInventory.GridData grid,
            ItemDefinition item,
            bool rotated,
            bool[,] occupied,
            Vector2Int preferredPosition,
            out Vector2Int position)
        {
            if (grid == null || item == null || occupied == null)
            {
                position = default;
                return false;
            }

            int width = rotated ? item.GridHeight : item.GridWidth;
            int height = rotated ? item.GridWidth : item.GridHeight;
            int bestDistance = int.MaxValue;
            position = default;

            // 모든 후보를 확인한 뒤 맨해튼 거리가 가장 짧은 좌표를 선택합니다.
            // 거리가 같으면 위쪽 행과 왼쪽 열이 먼저 유지되므로 같은 입력에서 결과가 매번 달라지지 않습니다.
            for (int y = 0; y <= grid.Height - height; y++)
            for (int x = 0; x <= grid.Width - width; x++)
            {
                var candidate = new Vector2Int(x, y);
                if (!CanFitShapeAt(grid, item, rotated, occupied, candidate)) continue;

                int distance = GridDistance(candidate, preferredPosition);
                if (distance >= bestDistance) continue;
                bestDistance = distance;
                position = candidate;
            }

            return bestDistance != int.MaxValue;
        }

        internal static bool[,] BuildOccupancy(PlayerInventory.GridData grid, InventoryEntry ignored)
            => BuildOccupancy(grid, ignored, null);

        // 다중 교환 계획에서는 드래그 아이템과 겹친 여러 엔트리를 모두 가상으로 꺼낸 상태의 점유 배열이 필요합니다.
        // excluded에 들어 있는 객체만 건너뛰고 나머지를 기록하므로 실제 Entries를 수정하지 않은 채 최종 배치를 미리 검증할 수 있습니다.
        internal static bool[,] BuildOccupancyExcluding(
            PlayerInventory.GridData grid,
            ICollection<InventoryEntry> excluded)
        {
            var result = new bool[grid.Width, grid.Height];
            foreach (InventoryEntry entry in grid.Entries)
            {
                if (excluded != null && excluded.Contains(entry)) continue;
                Fill(result, entry);
            }
            return result;
        }

        // 현재 격자 크기와 같은 bool 배열을 만들고 제외 대상이 아닌 엔트리들의 실제 모양 셀을 true로 표시합니다.
        internal static bool[,] BuildOccupancy(
            PlayerInventory.GridData grid,
            InventoryEntry ignoredA,
            InventoryEntry ignoredB)
        {
            var result = new bool[grid.Width, grid.Height];
            foreach (InventoryEntry entry in grid.Entries)
                if (entry != ignoredA && entry != ignoredB) Fill(result, entry);
            return result;
        }

        // 엔트리의 현재 Item, Position과 Rotated 값을 읽어 occupied 배열에 실제 모양만 기록합니다.
        internal static void Fill(bool[,] occupied, InventoryEntry entry)
            => Fill(occupied, entry.Item, entry.Position, entry.Rotated);

        // 아직 InventoryEntry에 적용하지 않은 가상 위치와 회전 상태도 점유 배열에 기록할 수 있는 오버로드입니다.
        // 배열 경계를 확인해 아이템 정의 크기가 바뀐 이전 저장 데이터 때문에 예외가 발생하지 않게 합니다.
        internal static void Fill(
            bool[,] occupied,
            ItemDefinition item,
            Vector2Int position,
            bool rotated)
        {
            int width = rotated ? item.GridHeight : item.GridWidth;
            int height = rotated ? item.GridWidth : item.GridHeight;
            for (int localY = 0; localY < height; localY++)
            for (int localX = 0; localX < width; localX++)
            {
                if (!item.IsGridCellOccupied(localX, localY, rotated)) continue;

                int gridX = position.x + localX;
                int gridY = position.y + localY;
                if (gridX < 0 || gridY < 0 ||
                    gridX >= occupied.GetLength(0) || gridY >= occupied.GetLength(1)) continue;
                occupied[gridX, gridY] = true;
            }
        }

        // 정렬 우선순위와 큰·작은 아이템 교환 규칙에 사용할 실제 true 셀 개수를 계산합니다.
        internal static int CountOccupiedShapeCells(ItemDefinition item)
        {
            int count = 0;
            for (int y = 0; y < item.GridHeight; y++)
            for (int x = 0; x < item.GridWidth; x++)
                if (item.IsGridCellOccupied(x, y, false)) count++;
            return count;
        }

        // 가로·세로 칸 이동량을 더한 맨해튼 거리로 지역 교환 후보의 가까운 정도를 비교합니다.
        internal static int GridDistance(Vector2Int first, Vector2Int second)
            => Mathf.Abs(first.x - second.x) + Mathf.Abs(first.y - second.y);
    }
}
