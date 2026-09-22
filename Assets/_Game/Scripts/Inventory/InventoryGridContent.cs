using System.Collections.Generic;
using UnityEngine;
using ZombieSurvival.Combat;
using ZombieSurvival.Items;

namespace ZombieSurvival.Inventory
{
    // 하나의 격자에 아이템을 추가하거나 격자 안의 전체 수량을 조회하는 공통 규칙입니다.
    // 플레이어 인벤토리, 시체와 필드 상자가 이 코드를 함께 사용하므로 중첩 순서와 공간 검사가 서로 달라지지 않습니다.
    // MonoBehaviour나 UI 이벤트를 전혀 사용하지 않으며, 실제 화면 갱신은 이 함수를 호출한 소유자가 성공 뒤 한 번만 처리합니다.
    internal static class InventoryGridContent
    {
        // 기존 중첩 묶음의 남은 공간과 새 묶음이 들어갈 빈칸을 가상으로 계산해 전체 수량을 받을 수 있는지 확인합니다.
        // 실제 Entries를 바꾸지 않으므로 false가 반환된 경우 기존 수량과 위치가 그대로 유지됩니다.
        internal static bool CanAdd(
            PlayerInventory.GridData grid,
            ItemDefinition item,
            int quantity)
        {
            return TryCreateAddPlan(grid, item, quantity, out _);
        }

        // CanAdd가 전체 수량을 받을 수 있다고 확인한 뒤 기존 묶음을 먼저 채우고 남은 수량을 새 묶음으로 배치합니다.
        // 사전 검사가 끝난 상태에서만 데이터를 변경하므로 공간 부족 때문에 일부 수량만 들어가는 중간 상태가 생기지 않습니다.
        internal static bool TryAdd(
            PlayerInventory.GridData grid,
            ItemDefinition item,
            int quantity)
        {
            if (!TryCreateAddPlan(grid, item, quantity, out List<Vector2Int> newStackPositions))
                return false;

            int remaining = quantity;
            foreach (InventoryEntry entry in grid.Entries)
            {
                if (entry.Item != item || entry.Quantity >= item.MaxStack) continue;
                remaining -= entry.AddQuantity(remaining);
                if (remaining <= 0) return true;
            }

            foreach (Vector2Int position in newStackPositions)
            {
                int stackQuantity = Mathf.Min(remaining, item.MaxStack);
                grid.Entries.Add(new InventoryEntry(item, stackQuantity, position));
                remaining -= stackQuantity;
            }
            return true;
        }

        // 기존 묶음의 여유를 제외하고 새로 필요한 각 묶음의 좌표를 실제 데이터 변경 전에 모두 계산합니다.
        // 이 계획이 완성된 뒤에만 TryAdd가 수량을 바꾸므로 도중 공간 부족이 발견되어 일부만 추가되는 상황을 구조적으로 막습니다.
        private static bool TryCreateAddPlan(
            PlayerInventory.GridData grid,
            ItemDefinition item,
            int quantity,
            out List<Vector2Int> newStackPositions)
        {
            newStackPositions = null;
            if (grid == null || item == null || quantity <= 0) return false;

            int remaining = quantity;
            foreach (InventoryEntry entry in grid.Entries)
            {
                if (entry.Item != item) continue;
                remaining -= Mathf.Max(0, item.MaxStack - entry.Quantity);
            }

            var positions = new List<Vector2Int>();
            if (remaining <= 0)
            {
                // 기존 묶음의 빈 수량만으로 모두 받을 수 있으면 격자 크기의 점유 배열을 만들 필요가 없습니다.
                newStackPositions = positions;
                return true;
            }

            bool[,] occupied = InventoryGridUtility.BuildOccupancy(grid, null);
            while (remaining > 0)
            {
                if (!InventoryGridUtility.FindSpace(grid, item, false, occupied, out Vector2Int position))
                    return false;

                // 아직 실제 엔트리를 만들지 않고 점유 배열만 채워 다음 묶음도 함께 들어갈 수 있는지 끝까지 검사합니다.
                positions.Add(position);
                InventoryGridUtility.Fill(occupied, item, position, false);
                remaining -= item.MaxStack;
            }

            newStackPositions = positions;
            return true;
        }

        // 하나의 격자와 그 안에 든 모든 컨테이너를 순회해 지정 ItemId의 실제 보유 수량을 합산합니다.
        internal static int CountItem(PlayerInventory.GridData grid, string itemId)
        {
            if (grid == null || string.IsNullOrWhiteSpace(itemId)) return 0;

            int total = 0;
            foreach (InventoryEntry entry in grid.Entries)
            {
                if (entry.Item.ItemId == itemId) total += entry.Quantity;
                total += CountItem(entry.Contents, itemId);
            }
            return total;
        }

        // 시설 강화와 제작이 특정 ItemId 재료를 루트와 모든 컨테이너에서 요청 수량만큼 제거할 때 사용합니다.
        // 먼저 전체 수량을 확인해 부족하면 아무것도 바꾸지 않으므로 일부 재료만 사라지는 실패 상태가 생기지 않습니다.
        internal static bool TryConsumeItem(
            PlayerInventory.GridData grid,
            string itemId,
            int requestedQuantity)
        {
            if (grid == null || string.IsNullOrWhiteSpace(itemId) || requestedQuantity <= 0) return false;
            if (CountItem(grid, itemId) < requestedQuantity) return false;

            int remaining = requestedQuantity;
            ConsumeItemRecursive(grid, itemId, ref remaining);
            return remaining == 0;
        }

        private static void ConsumeItemRecursive(
            PlayerInventory.GridData grid,
            string itemId,
            ref int remaining)
        {
            for (int i = 0; i < grid.Entries.Count && remaining > 0;)
            {
                InventoryEntry entry = grid.Entries[i];
                if (entry.Item.ItemId == itemId)
                {
                    int amount = Mathf.Min(remaining, entry.Quantity);
                    entry.Consume(amount);
                    remaining -= amount;
                    if (entry.Quantity == 0)
                    {
                        grid.Entries.RemoveAt(i);
                        continue;
                    }
                }

                if (entry.Contents != null)
                    ConsumeItemRecursive(entry.Contents, itemId, ref remaining);
                i++;
            }
        }

        // 루트나 컨테이너 내부에 나뉘어 있는 같은 탄종을 모두 찾아 현재 재장전 가능한 총 수량을 반환합니다.
        internal static int CountAmmo(PlayerInventory.GridData grid, AmmoType caliber)
        {
            if (grid == null || caliber == AmmoType.None) return 0;

            int total = 0;
            foreach (InventoryEntry entry in grid.Entries)
            {
                if (entry.Item.Type == ItemType.Ammo && entry.Item.Caliber == caliber)
                    total += entry.Quantity;
                total += CountAmmo(entry.Contents, caliber);
            }
            return total;
        }

        // 격자 앞쪽 묶음부터 요청 수량만큼 같은 탄종을 소비하고 실제로 제거한 수량을 반환합니다.
        // 빈 묶음은 즉시 목록에서 제거하며 컨테이너 안쪽도 같은 순서로 깊이 탐색합니다.
        internal static int ConsumeAmmo(
            PlayerInventory.GridData grid,
            AmmoType caliber,
            int requestedQuantity)
        {
            if (grid == null || caliber == AmmoType.None || requestedQuantity <= 0) return 0;

            int remaining = requestedQuantity;
            int consumed = 0;
            ConsumeAmmoRecursive(grid, caliber, ref remaining, ref consumed);
            return consumed;
        }

        private static void ConsumeAmmoRecursive(
            PlayerInventory.GridData grid,
            AmmoType caliber,
            ref int remaining,
            ref int consumed)
        {
            for (int i = 0; i < grid.Entries.Count && remaining > 0;)
            {
                InventoryEntry entry = grid.Entries[i];
                if (entry.Item.Type == ItemType.Ammo && entry.Item.Caliber == caliber)
                {
                    int amount = Mathf.Min(remaining, entry.Quantity);
                    entry.Consume(amount);
                    remaining -= amount;
                    consumed += amount;
                    if (entry.Quantity == 0)
                    {
                        grid.Entries.RemoveAt(i);
                        continue;
                    }
                }

                if (entry.Contents != null)
                    ConsumeAmmoRecursive(entry.Contents, caliber, ref remaining, ref consumed);
                i++;
            }
        }

        // 거점 요약과 레이드 손실 계산이 저장용 스냅샷을 만들지 않고 런타임 엔트리에서 바로 전체 수량을 셉니다.
        // excludeRaidProvided가 true이면 무료 테스트 장비와 그 안의 내용물 전체를 결과에서 제외합니다.
        internal static int CountAllQuantities(
            PlayerInventory.GridData grid,
            bool excludeRaidProvided = false)
        {
            if (grid == null) return 0;

            int total = 0;
            foreach (InventoryEntry entry in grid.Entries)
            {
                if (excludeRaidProvided && entry.IsRaidProvided) continue;
                total += entry.Quantity;
                total += CountAllQuantities(entry.Contents, excludeRaidProvided);
            }
            return total;
        }
    }
}
