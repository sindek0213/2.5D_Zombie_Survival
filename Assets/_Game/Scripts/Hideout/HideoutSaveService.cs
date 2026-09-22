using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using ZombieSurvival.Core;

namespace ZombieSurvival.Hideout
{
    // 새 저장 형식은 재귀 contents 대신 parentIndex로 부모를 가리키는 평평한 목록을 사용합니다.
    // Unity JsonUtility의 재귀 직렬화 깊이 경고를 없애면서도 가방 안의 가방 같은 계층을 그대로 복원할 수 있습니다.
    [Serializable]
    public sealed class HideoutSaveItemData
    {
        public string itemId;
        public int quantity;
        public int gridX;
        public int gridY;
        public bool rotated;
        public bool hasMagazineState;
        public int magazineRounds;
        public bool hasArmorState;
        public float armorDurability;
        public int parentIndex = -1;
    }

    [Serializable]
    public sealed class HideoutSaveData
    {
        public int version = HideoutSaveService.CurrentVersion;
        public List<HideoutSaveItemData> stashItems = new List<HideoutSaveItemData>();
        public List<HideoutSaveItemData> loadoutItems = new List<HideoutSaveItemData>();
        // 버전 4부터 시설 레벨과 현재 임무를 아이템 목록과 함께 저장합니다.
        public List<HideoutFacilityLevelState> facilityLevels = new List<HideoutFacilityLevelState>();
        public string activeMissionId;
        // 버전 4까지는 처치 임무만 존재해 아래 옛 필드에 진행도를 저장했습니다.
        // 버전 5도 호환을 위해 같은 값을 함께 기록하지만, 새 코드는 목표 종류와 무관한 activeMissionProgress를 사용합니다.
        public int activeMissionZombieKills;
        public int activeMissionProgress;
        public List<HideoutMissionCompletionState> missionCompletions = new List<HideoutMissionCompletionState>();
    }

    // HideoutStateStore의 순수 메모리 목록을 로컬 JSON 파일로 저장하고 게임 시작 시 복원합니다.
    // 파일 읽기와 모든 항목 검증이 성공한 뒤에만 메모리 상태를 교체하여 손상 파일로 정상 데이터를 지우지 않습니다.
    public static class HideoutSaveService
    {
        // 버전 5는 공통 임무 진행도와 임무별 완료 횟수를 추가했습니다. 버전 1~4 저장도 계속 읽고 다음 저장 때 자동 변환합니다.
        public const int CurrentVersion = 5;
        private const int OldestSupportedVersion = 1;
        private const string SaveFileName = "hideout_save.json";
        private static bool defaultSaveLoaded;
        private static bool defaultLoadSucceeded;
        private static string defaultPathOverride;

        [Serializable]
        private sealed class SaveVersionHeader
        {
            // JsonUtility가 파일 값을 덮어쓰기 전에도 명시적인 기본값을 두어 일반 C# 컴파일 경고를 없앱니다.
            public int version = 0;
        }

        // 버전 1·2 파일을 읽기 위한 이전 재귀 구조입니다. 새 파일 저장에는 사용하지 않아 평소 직렬화 경고가 발생하지 않습니다.
        [Serializable]
        private sealed class LegacyHideoutSaveData
        {
            public int version = 0;
            public List<RaidItemSnapshot> stashItems = new List<RaidItemSnapshot>();
            public List<RaidItemSnapshot> loadoutItems = new List<RaidItemSnapshot>();
        }

        public static string DefaultSavePath => string.IsNullOrWhiteSpace(defaultPathOverride)
            ? Path.Combine(Application.persistentDataPath, SaveFileName)
            : defaultPathOverride;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetRuntimeSession()
        {
            defaultSaveLoaded = false;
            defaultLoadSucceeded = false;
            defaultPathOverride = null;
            HideoutStateStore.ClearInMemoryState();
            HideoutFacilityStateStore.ResetForTests();
            HideoutMissionStateStore.ResetForTests();
        }

        // 한 Play 세션에서 기본 파일을 한 번만 읽고, 파일이 없는 첫 실행은 정상적인 빈 창고로 처리합니다.
        public static bool LoadDefaultIfNeeded()
        {
            if (defaultSaveLoaded) return defaultLoadSucceeded;
            defaultSaveLoaded = true;
            defaultLoadSucceeded = !File.Exists(DefaultSavePath) || LoadFromPath(DefaultSavePath);
            return defaultLoadSucceeded;
        }

        public static bool SaveDefault()
        {
            defaultSaveLoaded = true;
            defaultLoadSucceeded = SaveToPath(DefaultSavePath);
            return defaultLoadSucceeded;
        }

        public static bool SaveToPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return false;

            string temporaryPath = null;
            try
            {
                string fullPath = Path.GetFullPath(path);
                string directory = Path.GetDirectoryName(fullPath);
                if (string.IsNullOrWhiteSpace(directory)) return false;
                Directory.CreateDirectory(directory);

                var data = new HideoutSaveData();
                FlattenRoots(HideoutStateStore.StashItems, data.stashItems);
                FlattenRoots(HideoutStateStore.LoadoutItems, data.loadoutItems);
                data.facilityLevels.AddRange(HideoutFacilityStateStore.CaptureState());
                data.activeMissionId = HideoutMissionStateStore.ActiveMissionId;
                data.activeMissionProgress = HideoutMissionStateStore.ObjectiveProgress;
                data.activeMissionZombieKills = HideoutMissionStateStore.ZombieKillProgress;
                data.missionCompletions.AddRange(HideoutMissionStateStore.CaptureCompletionState());

                string json = JsonUtility.ToJson(data, true);
                temporaryPath = fullPath + ".tmp";
                File.WriteAllText(temporaryPath, json);

                // 기존 최종 파일은 새 임시 파일 작성이 완전히 끝난 뒤 한 번에 교체합니다.
                if (File.Exists(fullPath))
                {
                    File.Replace(temporaryPath, fullPath, null);
                    temporaryPath = null;
                }
                else
                {
                    File.Move(temporaryPath, fullPath);
                    temporaryPath = null;
                }
                return true;
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"Hideout 저장 실패: {exception.Message}");
                return false;
            }
            finally
            {
                try
                {
                    if (!string.IsNullOrWhiteSpace(temporaryPath) && File.Exists(temporaryPath))
                        File.Delete(temporaryPath);
                }
                catch (Exception cleanupException)
                {
                    Debug.LogWarning($"Hideout 임시 저장 파일 정리 실패: {cleanupException.Message}");
                }
            }
        }

        public static bool LoadFromPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return false;

            try
            {
                string json = File.ReadAllText(path);
                SaveVersionHeader header = JsonUtility.FromJson<SaveVersionHeader>(json);
                if (header == null || header.version < OldestSupportedVersion || header.version > CurrentVersion)
                    return false;

                List<RaidItemSnapshot> stash;
                List<RaidItemSnapshot> loadout;
                var facilityLevels = new List<HideoutFacilityLevelState>();
                string activeMissionId = string.Empty;
                int activeMissionProgress = 0;
                var missionCompletions = new List<HideoutMissionCompletionState>();
                if (header.version >= 3)
                {
                    HideoutSaveData data = JsonUtility.FromJson<HideoutSaveData>(json);
                    if (!TryUnflattenSave(data, out stash, out loadout)) return false;
                    if (header.version >= 4)
                    {
                        if (!TryReadProgression(
                                data,
                                header.version,
                                out facilityLevels,
                                out activeMissionId,
                                out activeMissionProgress,
                                out missionCompletions))
                            return false;
                    }
                }
                else
                {
                    // 이전 파일만 재귀 자료형으로 읽고 새 저장 시 자동으로 버전 3 평탄화 형식으로 변환합니다.
                    LegacyHideoutSaveData legacy = JsonUtility.FromJson<LegacyHideoutSaveData>(json);
                    if (!IsValidLegacy(legacy)) return false;
                    stash = CloneList(legacy.stashItems);
                    loadout = CloneList(legacy.loadoutItems);
                }

                HideoutStateStore.ReplaceState(stash, loadout);
                HideoutFacilityStateStore.ReplaceState(facilityLevels);
                HideoutMissionStateStore.Restore(activeMissionId, activeMissionProgress, missionCompletions);
                return true;
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"Hideout 불러오기 실패: {exception.Message}");
                return false;
            }
        }

        public static void SetDefaultPathForTests(string path)
        {
            defaultPathOverride = path;
            defaultSaveLoaded = false;
            defaultLoadSucceeded = false;
        }

        public static void ResetTestOverrides()
        {
            defaultPathOverride = null;
            defaultSaveLoaded = false;
            defaultLoadSucceeded = false;
        }

        private static void FlattenRoots(
            IReadOnlyList<RaidItemSnapshot> roots,
            List<HideoutSaveItemData> target)
        {
            if (roots == null) return;
            foreach (RaidItemSnapshot root in roots) FlattenItem(root, -1, target);
        }

        private static void FlattenItem(
            RaidItemSnapshot item,
            int parentIndex,
            List<HideoutSaveItemData> target)
        {
            if (item == null) return;
            int ownIndex = target.Count;
            target.Add(new HideoutSaveItemData
            {
                itemId = item.itemId,
                quantity = item.quantity,
                gridX = item.gridX,
                gridY = item.gridY,
                rotated = item.rotated,
                hasMagazineState = item.hasMagazineState,
                magazineRounds = item.magazineRounds,
                hasArmorState = item.hasArmorState,
                armorDurability = item.armorDurability,
                parentIndex = parentIndex
            });

            if (item.contents != null)
                foreach (RaidItemSnapshot child in item.contents) FlattenItem(child, ownIndex, target);
        }

        private static bool TryUnflattenSave(
            HideoutSaveData data,
            out List<RaidItemSnapshot> stash,
            out List<RaidItemSnapshot> loadout)
        {
            stash = null;
            loadout = null;
            if (data == null || data.version < 3 || data.version > CurrentVersion ||
                data.stashItems == null || data.loadoutItems == null)
                return false;
            return TryUnflattenList(data.stashItems, out stash) && TryUnflattenList(data.loadoutItems, out loadout);
        }

        private static bool TryReadProgression(
            HideoutSaveData data,
            int saveVersion,
            out List<HideoutFacilityLevelState> facilityLevels,
            out string activeMissionId,
            out int activeMissionProgress,
            out List<HideoutMissionCompletionState> missionCompletions)
        {
            facilityLevels = new List<HideoutFacilityLevelState>();
            activeMissionId = string.Empty;
            activeMissionProgress = 0;
            missionCompletions = new List<HideoutMissionCompletionState>();
            if (data == null || data.facilityLevels == null || data.activeMissionZombieKills < 0) return false;
            if (saveVersion >= 5 && (data.activeMissionProgress < 0 || data.missionCompletions == null)) return false;

            var uniqueFacilityIds = new HashSet<string>();
            foreach (HideoutFacilityLevelState facility in data.facilityLevels)
            {
                // 같은 시설 ID가 두 번 나오거나 레벨이 1 미만이면 어느 값을 믿어야 할지 모호하므로 손상 저장으로 판단합니다.
                if (facility == null || string.IsNullOrWhiteSpace(facility.facilityId) || facility.level < 1) return false;

                // ID 앞뒤 공백은 화면에는 보이지 않으므로 "storage"와 " storage "를 다른 시설로 허용하면 같은 시설 레벨이 두 개 생깁니다.
                // 비교와 저장 양쪽에 같은 정규화 값을 사용해 공백만 다른 중복 ID도 손상 데이터로 정확히 거부합니다.
                string normalizedFacilityId = facility.facilityId.Trim();
                if (!uniqueFacilityIds.Add(normalizedFacilityId)) return false;
                facilityLevels.Add(new HideoutFacilityLevelState
                {
                    facilityId = normalizedFacilityId,
                    level = facility.level
                });
            }

            activeMissionId = string.IsNullOrWhiteSpace(data.activeMissionId)
                ? string.Empty
                : data.activeMissionId.Trim();
            // 버전 4의 처치 수는 새 공통 진행도와 의미가 같으므로 그대로 이관합니다.
            int savedProgress = saveVersion >= 5 ? data.activeMissionProgress : data.activeMissionZombieKills;
            activeMissionProgress = string.IsNullOrEmpty(activeMissionId)
                ? 0
                : savedProgress;

            if (saveVersion < 5) return true;

            var uniqueMissionIds = new HashSet<string>();
            foreach (HideoutMissionCompletionState completion in data.missionCompletions)
            {
                // 중복 ID를 합산하면 손상 파일이 임무를 임의로 여러 번 완료한 것처럼 보일 수 있으므로 파일 전체를 거부합니다.
                if (completion == null || string.IsNullOrWhiteSpace(completion.missionId) || completion.completionCount <= 0)
                    return false;
                string normalizedMissionId = completion.missionId.Trim();
                if (!uniqueMissionIds.Add(normalizedMissionId)) return false;
                missionCompletions.Add(new HideoutMissionCompletionState
                {
                    missionId = normalizedMissionId,
                    completionCount = completion.completionCount
                });
            }
            return true;
        }

        private static bool TryUnflattenList(
            List<HideoutSaveItemData> flat,
            out List<RaidItemSnapshot> roots)
        {
            roots = new List<RaidItemSnapshot>();
            var created = new List<RaidItemSnapshot>(flat.Count);
            for (int i = 0; i < flat.Count; i++)
            {
                HideoutSaveItemData item = flat[i];
                // 부모는 반드시 자신보다 먼저 기록되어야 하므로 순환 참조와 잘못된 인덱스를 동시에 차단합니다.
                if (!IsValidFlatItem(item) || item.parentIndex >= i || item.parentIndex < -1) return false;

                var snapshot = new RaidItemSnapshot
                {
                    itemId = item.itemId,
                    quantity = item.quantity,
                    gridX = item.gridX,
                    gridY = item.gridY,
                    rotated = item.rotated,
                    hasMagazineState = item.hasMagazineState,
                    magazineRounds = item.magazineRounds,
                    hasArmorState = item.hasArmorState,
                    armorDurability = item.armorDurability
                };
                created.Add(snapshot);
                if (item.parentIndex == -1) roots.Add(snapshot);
                else created[item.parentIndex].contents.Add(snapshot);
            }
            return true;
        }

        private static bool IsValidFlatItem(HideoutSaveItemData item)
        {
            return item != null && !string.IsNullOrWhiteSpace(item.itemId) && item.quantity > 0 &&
                   (!item.hasMagazineState || item.magazineRounds >= 0) &&
                   (!item.hasArmorState || item.armorDurability >= 0f);
        }

        private static bool IsValidLegacy(LegacyHideoutSaveData data)
        {
            if (data == null || data.version < OldestSupportedVersion || data.version > 2 ||
                data.stashItems == null || data.loadoutItems == null) return false;
            foreach (RaidItemSnapshot item in data.stashItems)
                if (!IsValidLegacyItem(item)) return false;
            foreach (RaidItemSnapshot item in data.loadoutItems)
                if (!IsValidLegacyItem(item)) return false;
            return true;
        }

        private static bool IsValidLegacyItem(RaidItemSnapshot item)
        {
            if (item == null || string.IsNullOrWhiteSpace(item.itemId) || item.quantity <= 0 || item.contents == null ||
                (item.hasMagazineState && item.magazineRounds < 0) ||
                (item.hasArmorState && item.armorDurability < 0f)) return false;
            foreach (RaidItemSnapshot child in item.contents)
                if (!IsValidLegacyItem(child)) return false;
            return true;
        }

        private static List<RaidItemSnapshot> CloneList(List<RaidItemSnapshot> source)
        {
            var result = new List<RaidItemSnapshot>();
            foreach (RaidItemSnapshot item in source)
            {
                RaidItemSnapshot clone = RaidSnapshotUtility.CloneItem(item);
                if (clone != null) result.Add(clone);
            }
            return result;
        }
    }
}
