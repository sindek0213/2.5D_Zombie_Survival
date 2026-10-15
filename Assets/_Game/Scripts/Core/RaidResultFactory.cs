using System.Collections.Generic;
using ZombieSurvival.Inventory;

namespace ZombieSurvival.Core
{
    // 현재 PlayerInventory를 씬과 무관한 RaidResult 데이터로 변환합니다.
    // 변환 과정은 원본 인벤토리를 수정하지 않으므로 성공 화면을 표시하는 동안 기존 UI도 안전하게 읽을 수 있습니다.
    public static class RaidResultFactory
    {
        public static RaidResult Create(
            PlayerInventory inventory,
            RaidEndReason reason,
            int zombieKills,
            float raidDurationSeconds = 0f,
            IEnumerable<string> visitedZoneIds = null,
            string deathCause = null,
            int shotsFired = 0,
            int successfulShots = 0,
            float damageTaken = 0f,
            IEnumerable<RaidWeaponKillStat> weaponKills = null)
        {
            bool succeeded = reason == RaidEndReason.Extracted;
            var result = new RaidResult
            {
                endReason = reason,
                succeeded = succeeded,
                zombieKills = zombieKills,
                raidDurationSeconds = UnityEngine.Mathf.Max(0f, raidDurationSeconds),
                lostItemCount = succeeded ? 0 : CountAllItems(inventory),
                deathCause = reason == RaidEndReason.PlayerDied ? deathCause ?? string.Empty : string.Empty,
                shotsFired = UnityEngine.Mathf.Max(0, shotsFired),
                successfulShots = UnityEngine.Mathf.Clamp(successfulShots, 0, UnityEngine.Mathf.Max(0, shotsFired)),
                damageTaken = UnityEngine.Mathf.Max(0f, damageTaken),
                // 정상 씬에서는 항상 인벤토리가 있지만 참조 누락 상황에서도 결과 생성 자체가 멈추지 않도록 빈 결과를 사용합니다.
                // 이렇게 하면 사망 뒤 RaidSession이 영원히 종료되지 않는 것보다 안전하게 거점으로 복귀할 수 있습니다.
                inventory = succeeded && inventory != null
                    ? RaidSnapshotUtility.CaptureGrid(inventory.Root, true)
                    : new RaidGridSnapshot()
            };

            if (weaponKills != null)
                foreach (RaidWeaponKillStat stat in weaponKills)
                    if (stat != null && !string.IsNullOrWhiteSpace(stat.weaponItemId) && stat.kills > 0)
                        result.weaponKills.Add(new RaidWeaponKillStat
                        {
                            weaponItemId = stat.weaponItemId.Trim(),
                            kills = stat.kills
                        });

            // 가치 통계는 현재 인벤토리의 실제 ItemDefinition을 사용하므로 카탈로그를 다시 검색하지 않아도 됩니다.
            int carriedValue = CountAllValue(inventory);
            result.extractedItemValue = succeeded ? carriedValue : 0;
            result.lostItemValue = succeeded ? 0 : carriedValue;

            // 방문 기록은 사망 여부와 관계없이 결과에 남깁니다. 장소 확인 임무가 사망 시에도 인정할지는 임무 규칙에서 결정합니다.
            if (visitedZoneIds != null)
            {
                var uniqueZoneIds = new HashSet<string>();
                foreach (string zoneId in visitedZoneIds)
                    if (!string.IsNullOrWhiteSpace(zoneId) && uniqueZoneIds.Add(zoneId.Trim()))
                        result.visitedZoneIds.Add(zoneId.Trim());
            }

            // 사망했거나 인벤토리 참조가 없으면 복원할 실제 장비가 없으므로 빈 목록을 유지합니다.
            if (!succeeded || inventory == null) return result;

            foreach (var pair in inventory.Equipment)
            {
                // 테스트 시작용 무료 장비는 플레이어가 획득한 전리품이 아니므로 Hideout 보상에 넣지 않습니다.
                if (pair.Value.IsRaidProvided) continue;
                result.equipment.Add(new RaidEquipmentSnapshot
                {
                    slot = pair.Key.ToString(),
                    item = RaidSnapshotUtility.CaptureItem(pair.Value, true)
                });
            }
            return result;
        }

        private static int CountAllItems(PlayerInventory inventory)
        {
            // 참조가 빠진 비정상 씬에서도 손실 수량을 0으로 기록하고 종료 흐름은 계속 진행합니다.
            if (inventory == null) return 0;

            int count = InventoryGridContent.CountAllQuantities(inventory.Root, true);
            foreach (InventoryEntry equipped in inventory.Equipment.Values)
            {
                if (equipped.IsRaidProvided) continue;
                count += equipped.Quantity;
                count += InventoryGridContent.CountAllQuantities(equipped.Contents, true);
            }
            return count;
        }

        private static int CountAllValue(PlayerInventory inventory)
        {
            if (inventory == null) return 0;
            long total = CountGridValue(inventory.Root);
            foreach (InventoryEntry equipped in inventory.Equipment.Values)
            {
                if (equipped.IsRaidProvided) continue;
                total += (long)equipped.Item.BaseValue * equipped.Quantity;
                total += CountGridValue(equipped.Contents);
            }
            if (total <= 0L) return 0;
            return total >= int.MaxValue ? int.MaxValue : (int)total;
        }

        private static long CountGridValue(PlayerInventory.GridData grid)
        {
            if (grid == null) return 0L;
            long total = 0L;
            foreach (InventoryEntry entry in grid.Entries)
            {
                total += (long)entry.Item.BaseValue * entry.Quantity;
                total += CountGridValue(entry.Contents);
            }
            return total;
        }
    }
}
