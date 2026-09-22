using System.Collections.Generic;
using ZombieSurvival.Core;

namespace ZombieSurvival.Hideout
{
    // 씬 전환 사이에서 거점 창고와 다음 출격 준비 목록을 유지하는 메모리 상태입니다.
    // HideoutSaveService가 이 목록을 버전 5 JSON으로 저장하고, 게임 시작 때 다시 깊은 복사본으로 복원합니다.
    public static class HideoutStateStore
    {
        private static readonly List<RaidItemSnapshot> stashItems = new List<RaidItemSnapshot>();
        private static readonly List<RaidItemSnapshot> loadoutItems = new List<RaidItemSnapshot>();

        public static IReadOnlyList<RaidItemSnapshot> StashItems => stashItems;
        public static IReadOnlyList<RaidItemSnapshot> LoadoutItems => loadoutItems;

        // Hideout에 도착한 직전 레이드 결과를 정확히 한 번 소비하고 탈출 성공 아이템만 창고에 추가합니다.
        // 장착 중이던 아이템도 독립된 최상위 창고 항목으로 회수하며 컨테이너 contents는 깊은 복사로 그대로 보존합니다.
        public static RaidResult ApplyPendingRaidResult()
        {
            RaidResult result = RaidResultStore.PendingResult;
            if (result == null) return null;

            if (result.succeeded)
            {
                if (result.inventory?.items != null)
                    foreach (RaidItemSnapshot item in result.inventory.items) AddClone(stashItems, item);

                if (result.equipment != null)
                    foreach (RaidEquipmentSnapshot equipped in result.equipment)
                        if (equipped?.item != null) AddClone(stashItems, equipped.item);
            }
            else
            {
                // 이 분기는 사망 완전 손실에만 들어옵니다. 정상 흐름에서는 RaidLoadoutApplier가 출격 순간 준비 목록을 이미 비웁니다.
                // 그래도 씬 초기화 순서나 저장 실패로 목록이 남아 있다면 사망 뒤 장비가 되살아나는 복제 버그가 되므로 여기서 한 번 더 제거합니다.
                loadoutItems.Clear();
            }

            // 성공과 실패 모두 결과를 처리한 뒤 비워야 Hideout 씬을 다시 열었을 때 같은 보상이나 실패 메시지가 반복되지 않습니다.
            RaidResultStore.Clear();
            return result;
        }

        // 창고의 한 묶음 전체를 다음 출격 준비 목록으로 옮깁니다.
        // 인덱스가 오래되어 범위를 벗어나면 아무 목록도 수정하지 않고 false를 반환합니다.
        public static bool MoveStashItemToLoadout(int index)
            => MoveItem(stashItems, loadoutItems, index);

        // 출격 준비에서 뺀 아이템 묶음을 contents까지 유지한 채 창고로 되돌립니다.
        public static bool MoveLoadoutItemToStash(int index)
            => MoveItem(loadoutItems, stashItems, index);

        // RaidPrototype이 시작할 때 현재 출격 목록의 깊은 복사본을 넘기고 원본 목록을 비웁니다.
        // 이후 탈출하면 플레이어 인벤토리 스냅샷으로 다시 창고에 들어오고 사망하면 복구되지 않으므로 손실 흐름이 자연스럽게 완성됩니다.
        public static List<RaidItemSnapshot> TakeLoadoutForRaid()
        {
            var result = new List<RaidItemSnapshot>();
            foreach (RaidItemSnapshot item in loadoutItems) AddClone(result, item);
            loadoutItems.Clear();
            return result;
        }

        // 잘못된 ItemId나 공간 부족으로 레이드 인벤토리에 넣지 못한 출격품은 조용히 삭제하지 않고 창고로 돌려보냅니다.
        public static void ReturnItemsToStash(IEnumerable<RaidItemSnapshot> items)
        {
            if (items == null) return;
            foreach (RaidItemSnapshot item in items) AddClone(stashItems, item);
        }

        // EditMode 테스트가 정적 상태를 다음 테스트에 남기지 않도록 제공하는 명시적 초기화 진입점입니다.
        // 실제 게임 흐름에서는 새 게임이나 저장 데이터 불러오기 기능이 같은 역할을 담당하게 됩니다.
        public static void ResetForTests()
        {
            ClearInMemoryState();
            HideoutFacilityStateStore.ResetForTests();
            HideoutMissionStateStore.ResetForTests();
        }

        // 저장 파일을 성공적으로 읽은 뒤에만 기존 메모리 목록을 교체하는 내부 진입점입니다.
        // SaveService가 JSON 검증 전에 이 메서드를 호출하지 않으므로 손상 파일 때문에 정상 창고가 먼저 지워지지 않습니다.
        internal static void ReplaceState(
            IEnumerable<RaidItemSnapshot> savedStashItems,
            IEnumerable<RaidItemSnapshot> savedLoadoutItems)
        {
            ClearInMemoryState();
            if (savedStashItems != null)
                foreach (RaidItemSnapshot item in savedStashItems) AddClone(stashItems, item);
            if (savedLoadoutItems != null)
                foreach (RaidItemSnapshot item in savedLoadoutItems) AddClone(loadoutItems, item);
        }

        // 도메인 리로드를 끈 Unity 에디터에서도 새 Play 세션이 이전 정적 목록을 이어받지 않도록 런타임 초기화에서 사용합니다.
        internal static void ClearInMemoryState()
        {
            stashItems.Clear();
            loadoutItems.Clear();
        }

        private static bool MoveItem(
            List<RaidItemSnapshot> source,
            List<RaidItemSnapshot> target,
            int index)
        {
            if (index < 0 || index >= source.Count) return false;
            RaidItemSnapshot item = source[index];
            source.RemoveAt(index);
            target.Add(item);
            return true;
        }

        private static void AddClone(List<RaidItemSnapshot> target, RaidItemSnapshot item)
        {
            RaidItemSnapshot clone = RaidSnapshotUtility.CloneItem(item);
            if (clone != null) target.Add(clone);
        }
    }
}
