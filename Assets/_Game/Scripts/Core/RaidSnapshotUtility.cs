using UnityEngine;
using ZombieSurvival.Inventory;
using ZombieSurvival.Items;

namespace ZombieSurvival.Core
{
    // RaidItemSnapshot을 복제하거나 다시 런타임 InventoryEntry로 만드는 공통 변환 함수입니다.
    // Hideout 창고, 출격 준비와 버전 5 JSON 저장이 같은 복원 규칙을 사용하도록 한곳에 모았습니다.
    public static class RaidSnapshotUtility
    {
        // 현재 격자의 모든 엔트리를 저장 가능한 스냅샷으로 깊게 복사합니다.
        // 플레이어 레이드 결과와 거점 격자 저장이 같은 변환을 사용하므로 탄창·방어구·컨테이너 필드 누락을 막습니다.
        public static RaidGridSnapshot CaptureGrid(PlayerInventory.GridData grid, bool excludeRaidProvided = false)
        {
            var snapshot = new RaidGridSnapshot
            {
                width = grid != null ? grid.Width : 0,
                height = grid != null ? grid.Height : 0
            };
            if (grid == null) return snapshot;

            foreach (InventoryEntry entry in grid.Entries)
                if (!excludeRaidProvided || !entry.IsRaidProvided)
                    snapshot.items.Add(CaptureItem(entry, excludeRaidProvided));
            return snapshot;
        }

        // InventoryEntry 하나와 컨테이너 내부를 저장 가능한 데이터로 깊게 복사합니다.
        public static RaidItemSnapshot CaptureItem(InventoryEntry entry, bool excludeRaidProvided = false)
        {
            if (entry == null) return null;
            var snapshot = new RaidItemSnapshot
            {
                itemId = entry.Item.ItemId,
                quantity = entry.Quantity,
                gridX = entry.Position.x,
                gridY = entry.Position.y,
                rotated = entry.Rotated,
                hasMagazineState = entry.HasMagazineState,
                magazineRounds = entry.MagazineRounds,
                hasArmorState = entry.HasArmorState,
                armorDurability = entry.ArmorDurability
            };

            if (entry.Contents != null)
                foreach (InventoryEntry child in entry.Contents.Entries)
                    if (!excludeRaidProvided || !child.IsRaidProvided)
                        snapshot.contents.Add(CaptureItem(child, excludeRaidProvided));
            return snapshot;
        }

        // 원본과 contents List를 공유하지 않는 깊은 복사본을 만들어 창고와 출격 목록이 서로의 데이터를 우연히 수정하지 않게 합니다.
        public static RaidItemSnapshot CloneItem(RaidItemSnapshot source)
        {
            if (source == null) return null;

            var clone = new RaidItemSnapshot
            {
                itemId = source.itemId,
                quantity = source.quantity,
                gridX = source.gridX,
                gridY = source.gridY,
                rotated = source.rotated,
                hasMagazineState = source.hasMagazineState,
                magazineRounds = source.magazineRounds,
                hasArmorState = source.hasArmorState,
                armorDurability = source.armorDurability
            };

            if (source.contents != null)
                foreach (RaidItemSnapshot child in source.contents)
                {
                    RaidItemSnapshot childClone = CloneItem(child);
                    if (childClone != null) clone.contents.Add(childClone);
                }
            return clone;
        }

        // 스냅샷 하나를 카탈로그로 해석한 뒤 격자에 들어갈 수 있는 위치와 컨테이너 내부 전체를 먼저 검증합니다.
        // 자식 복원까지 전부 성공한 뒤에만 최상위 grid.Entries에 추가하므로 잘못된 ID나 공간 부족으로 반쪽 아이템이 남지 않습니다.
        public static bool TryRestoreItem(
            PlayerInventory.GridData grid,
            ItemCatalog catalog,
            RaidItemSnapshot snapshot,
            out InventoryEntry restoredEntry)
        {
            restoredEntry = null;
            if (grid == null || catalog == null || snapshot == null || snapshot.quantity <= 0) return false;

            ItemDefinition definition = catalog.Find(snapshot.itemId);
            if (definition == null || snapshot.quantity > definition.MaxStack) return false;

            bool rotated = snapshot.rotated;
            bool[,] occupied = InventoryGridUtility.BuildOccupancy(grid, null);
            var requestedPosition = new Vector2Int(snapshot.gridX, snapshot.gridY);
            Vector2Int position;

            // 저장 당시 좌표가 현재 격자에서도 유효하면 그대로 복원하고, 다른 아이템과 겹치면 동일 회전 상태의 첫 빈칸을 찾습니다.
            // 현재 회전으로 어느 곳에도 들어가지 않을 때만 반대 회전을 시도하여 출격 아이템을 불필요하게 창고로 되돌리지 않게 합니다.
            if (InventoryGridUtility.CanFitShapeAt(grid, definition, rotated, occupied, requestedPosition))
            {
                position = requestedPosition;
            }
            else if (!InventoryGridUtility.FindSpace(grid, definition, rotated, occupied, out position))
            {
                rotated = !rotated;
                if (!InventoryGridUtility.FindSpace(grid, definition, rotated, occupied, out position)) return false;
            }

            // 새 저장 데이터는 0발도 그대로 복원하고, 탄창 필드가 없던 이전 저장 데이터는 -1을 전달해 기본 가득 찬 탄창으로 호환합니다.
            int magazineRounds = snapshot.hasMagazineState ? snapshot.magazineRounds : -1;
            // 내구도 필드가 없는 1·2번 저장은 -1을 전달해 기존 방탄복을 최대 내구도로 안전하게 복원합니다.
            float armorDurability = snapshot.hasArmorState ? snapshot.armorDurability : -1f;
            var candidate = new InventoryEntry(
                definition,
                snapshot.quantity,
                position,
                rotated,
                false,
                magazineRounds,
                armorDurability);
            if (snapshot.contents != null && snapshot.contents.Count > 0)
            {
                // 내용물이 있는데 정의가 컨테이너가 아니라면 손실을 감수하고 무시하지 않고 전체 복원을 실패시킵니다.
                if (candidate.Contents == null) return false;
                foreach (RaidItemSnapshot child in snapshot.contents)
                    if (!TryRestoreItem(candidate.Contents, catalog, child, out _)) return false;
            }

            grid.Entries.Add(candidate);
            restoredEntry = candidate;
            return true;
        }

        // UI의 획득 수량과 창고 요약에 컨테이너 내부까지 포함하기 위해 하나의 스냅샷 아래 모든 Quantity를 재귀 합산합니다.
        public static int CountAllQuantities(RaidItemSnapshot item)
        {
            if (item == null) return 0;
            int total = item.quantity;
            if (item.contents != null)
                foreach (RaidItemSnapshot child in item.contents) total += CountAllQuantities(child);
            return total;
        }
    }
}
