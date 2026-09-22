using System;
using System.Collections.Generic;
using UnityEngine;
using ZombieSurvival.Core;

namespace ZombieSurvival.Hideout
{
    // 임무 ID별 누적 완료 횟수를 JSON으로 저장하기 위한 단순 데이터 묶음입니다.
    // 횟수를 함께 저장하므로 일회성 임무 차단과 반복 임무 기록을 같은 구조로 처리할 수 있습니다.
    [Serializable]
    public sealed class HideoutMissionCompletionState
    {
        public string missionId;
        public int completionCount;
    }

    // 현재 받은 임무 한 개, 공통 진행도와 임무별 완료 횟수를 씬 전환 사이에서 유지합니다.
    // ScriptableObject 참조 대신 ID와 숫자만 보관하므로 에셋 설명을 고쳐도 기존 저장 파일을 계속 읽을 수 있습니다.
    public static class HideoutMissionStateStore
    {
        private static readonly Dictionary<string, int> completionCounts = new Dictionary<string, int>();

        public static string ActiveMissionId { get; private set; }
        public static int ObjectiveProgress { get; private set; }
        public static bool HasActiveMission => !string.IsNullOrWhiteSpace(ActiveMissionId);

        // 이전 좀비 처치 전용 코드가 읽던 이름은 공통 진행도를 반환하도록 유지합니다.
        // 저장 버전 4의 activeMissionZombieKills도 새 ObjectiveProgress로 이관됩니다.
        public static int ZombieKillProgress => ObjectiveProgress;

        public static bool TryAccept(HideoutMissionDefinition mission)
        {
            if (!CanAccept(mission)) return false;
            ActiveMissionId = mission.MissionId;
            ObjectiveProgress = 0;
            return true;
        }

        public static bool CanAccept(HideoutMissionDefinition mission)
        {
            if (mission == null || string.IsNullOrWhiteSpace(mission.MissionId) || HasActiveMission) return false;
            // 반복 불가 임무는 완료 횟수가 한 번이라도 기록돼 있으면 다시 수락하지 못합니다.
            return mission.Repeatable || GetCompletionCount(mission.MissionId) == 0;
        }

        // 레이드 결과 중 현재 임무에 해당하는 값만 공통 진행도에 더합니다.
        // 처치는 사망한 레이드도 인정하고, 회수 아이템과 성공 탈출은 실제 탈출 성공 결과에서만 인정합니다.
        public static void ApplyRaidResult(RaidResult result, HideoutMissionDefinition activeMission)
        {
            if (!MatchesActive(activeMission) || result == null) return;

            int gainedProgress;
            switch (activeMission.ObjectiveType)
            {
                case HideoutMissionObjectiveType.CollectedItem:
                    gainedProgress = result.succeeded
                        ? CountExtractedItem(result, activeMission.ObjectiveItem?.ItemId)
                        : 0;
                    break;
                case HideoutMissionObjectiveType.SuccessfulExtraction:
                    gainedProgress = result.succeeded ? 1 : 0;
                    break;
                default:
                    gainedProgress = Mathf.Max(0, result.zombieKills);
                    break;
            }

            ObjectiveProgress += Mathf.Max(0, gainedProgress);
        }

        // 이전 호출부는 목표 정의를 전달하지 않았으므로 좀비 처치 진행도로 처리합니다.
        // 새 게임 흐름에서는 반드시 위 오버로드에 현재 임무 정의를 넘겨 목표 종류를 정확히 계산합니다.
        public static void ApplyRaidResult(RaidResult result)
        {
            if (!HasActiveMission || result == null) return;
            ObjectiveProgress += Mathf.Max(0, result.zombieKills);
        }

        public static bool CanComplete(HideoutMissionDefinition mission)
        {
            return MatchesActive(mission) && ObjectiveProgress >= mission.RequiredAmount;
        }

        // 보상 지급이 성공한 뒤에만 호출되어 완료 횟수를 올리고 활성 임무를 비웁니다.
        // 보상이 창고에 들어가지 않으면 이 메서드가 호출되지 않으므로 다음에 공간을 만든 뒤 다시 완료할 수 있습니다.
        public static bool TryComplete(HideoutMissionDefinition mission)
        {
            if (!CanComplete(mission)) return false;

            string id = mission.MissionId;
            completionCounts[id] = GetCompletionCount(id) + 1;
            ActiveMissionId = string.Empty;
            ObjectiveProgress = 0;
            return true;
        }

        public static bool Abandon()
        {
            if (!HasActiveMission) return false;
            ActiveMissionId = string.Empty;
            ObjectiveProgress = 0;
            return true;
        }

        public static int GetCompletionCount(string missionId)
        {
            if (string.IsNullOrWhiteSpace(missionId)) return 0;
            return completionCounts.TryGetValue(missionId, out int count) ? Mathf.Max(0, count) : 0;
        }

        public static List<HideoutMissionCompletionState> CaptureCompletionState()
        {
            var result = new List<HideoutMissionCompletionState>();
            foreach (KeyValuePair<string, int> pair in completionCounts)
            {
                if (pair.Value <= 0) continue;
                result.Add(new HideoutMissionCompletionState
                {
                    missionId = pair.Key,
                    completionCount = pair.Value
                });
            }
            return result;
        }

        // 버전 1~4 저장과 기존 테스트가 사용하던 복원 진입점입니다.
        public static void Restore(string missionId, int progress)
            => Restore(missionId, progress, null);

        public static void Restore(
            string missionId,
            int progress,
            IEnumerable<HideoutMissionCompletionState> savedCompletions)
        {
            ActiveMissionId = string.IsNullOrWhiteSpace(missionId) ? string.Empty : missionId.Trim();
            ObjectiveProgress = HasActiveMission ? Mathf.Max(0, progress) : 0;

            completionCounts.Clear();
            if (savedCompletions == null) return;
            foreach (HideoutMissionCompletionState completion in savedCompletions)
            {
                if (completion == null || string.IsNullOrWhiteSpace(completion.missionId) || completion.completionCount <= 0)
                    continue;
                completionCounts[completion.missionId.Trim()] = completion.completionCount;
            }
        }

        public static void ResetForTests() => Restore(string.Empty, 0, null);

        private static bool MatchesActive(HideoutMissionDefinition mission)
        {
            return mission != null &&
                   HasActiveMission &&
                   mission.MissionId == ActiveMissionId;
        }

        // 성공 결과의 루트 인벤토리와 장착 장비를 모두 확인하고, 가방 안 내용물까지 재귀적으로 셉니다.
        private static int CountExtractedItem(RaidResult result, string itemId)
        {
            if (result == null || string.IsNullOrWhiteSpace(itemId)) return 0;

            int total = 0;
            if (result.inventory?.items != null)
                foreach (RaidItemSnapshot item in result.inventory.items) total += CountSnapshotItem(item, itemId);
            if (result.equipment != null)
                foreach (RaidEquipmentSnapshot equipment in result.equipment)
                    if (equipment?.item != null) total += CountSnapshotItem(equipment.item, itemId);
            return total;
        }

        private static int CountSnapshotItem(RaidItemSnapshot snapshot, string itemId)
        {
            if (snapshot == null) return 0;

            int total = snapshot.itemId == itemId ? Mathf.Max(0, snapshot.quantity) : 0;
            if (snapshot.contents != null)
                foreach (RaidItemSnapshot child in snapshot.contents) total += CountSnapshotItem(child, itemId);
            return total;
        }
    }
}
