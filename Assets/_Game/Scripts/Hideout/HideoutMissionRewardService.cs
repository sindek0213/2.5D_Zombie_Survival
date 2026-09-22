using ZombieSurvival.Inventory;

namespace ZombieSurvival.Hideout
{
    // 임무 보상 전체가 창고에 들어가는지 검사하고 실제 지급을 적용하는 순수 계산 서비스입니다.
    // 화면이나 저장 파일을 직접 다루지 않아 보드 UI 외의 NPC·우편함 보상에도 같은 안전 규칙을 재사용할 수 있습니다.
    public static class HideoutMissionRewardService
    {
        public static bool CanGrant(PlayerInventory.GridData stash, HideoutMissionDefinition mission)
        {
            if (stash == null || mission == null || !HasValidRewards(mission)) return false;

            // 실제 창고와 같은 최상위 아이템 배치를 임시 격자에 복제합니다.
            // 보상 여러 개를 임시 격자에 순서대로 넣어 마지막 보상까지 들어갈 때만 true를 반환합니다.
            PlayerInventory.GridData simulation = CloneForPlacement(stash);
            foreach (HideoutMissionItemReward reward in mission.ItemRewards)
                if (!InventoryGridContent.TryAdd(simulation, reward.item, reward.quantity)) return false;
            return true;
        }

        public static bool TryGrant(PlayerInventory.GridData stash, HideoutMissionDefinition mission)
        {
            // 사전 검사를 통과한 동일한 순서로 실제 창고를 변경하므로 일부 보상만 지급되는 상태가 생기지 않습니다.
            if (!CanGrant(stash, mission)) return false;
            foreach (HideoutMissionItemReward reward in mission.ItemRewards)
                if (!InventoryGridContent.TryAdd(stash, reward.item, reward.quantity)) return false;
            return true;
        }

        private static bool HasValidRewards(HideoutMissionDefinition mission)
        {
            if (mission.ItemRewards == null || mission.ItemRewards.Count == 0) return false;
            foreach (HideoutMissionItemReward reward in mission.ItemRewards)
                if (reward?.item == null || reward.quantity <= 0) return false;
            return true;
        }

        private static PlayerInventory.GridData CloneForPlacement(PlayerInventory.GridData source)
        {
            var clone = new PlayerInventory.GridData(source.Width, source.Height);
            foreach (InventoryEntry entry in source.Entries)
            {
                // 보상 공간 계산에는 루트 셀 모양·위치·수량만 필요합니다.
                // 컨테이너 내용물은 컨테이너 외형 셀을 바꾸지 않으므로 복제하지 않아도 공간 결과가 원본과 정확히 같습니다.
                clone.Entries.Add(new InventoryEntry(
                    entry.Item,
                    entry.Quantity,
                    entry.Position,
                    entry.Rotated,
                    entry.IsRaidProvided,
                    entry.MagazineRounds,
                    entry.ArmorDurability));
            }
            return clone;
        }
    }
}
