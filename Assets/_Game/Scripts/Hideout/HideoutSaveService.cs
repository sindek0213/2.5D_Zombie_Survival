using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using ZombieSurvival.Combat;
using ZombieSurvival.Core;
using ZombieSurvival.Items;

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
        public bool hasWeaponDurabilityState;
        public float weaponDurability;
        public bool hasTriggerModeState;
        public int selectedTriggerMode;
        public bool hasArmorState;
        public float armorDurability;
        public List<string> attachmentItemIds = new List<string>();
        public int parentIndex = -1;
        // 비어 있으면 일반 격자 아이템이고, 최상위 항목에 슬롯 이름이 있으면 거점에서 장착한 출격 장비입니다.
        // 자식 contents에는 슬롯을 기록하지 않아 한 장비 아래의 모든 내부 아이템이 같은 트리로 복원됩니다.
        public string equipmentSlot;
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
        // 버전 6부터 상점에서 사용하는 공용 화폐를 저장합니다.
        // 이전 저장에는 이 필드가 없으므로 LoadFromPath가 명시적으로 시작 자금을 적용합니다.
        public int credits = HideoutEconomyStateStore.DefaultCredits;
        public int tutorialStep;
        // 버전 9부터 상인 진행도와 재고 회차를 저장해 재실행 후 가격·해금 상품이 되돌아가지 않게 합니다.
        public int traderReputation;
        public int stockRefreshIndex;
        // 버전 10부터 제작 대기열과 발전기 상태를 저장합니다.
        public List<HideoutCraftJobState> craftJobs = new List<HideoutCraftJobState>();
        public bool generatorEnabled = true;
        public float generatorFuelSeconds = HideoutCraftQueueStateStore.DefaultFuelSeconds;
    }

    // HideoutStateStore의 순수 메모리 목록을 로컬 JSON 파일로 저장하고 게임 시작 시 복원합니다.
    // 파일 읽기와 모든 항목 검증이 성공한 뒤에만 메모리 상태를 교체하여 손상 파일로 정상 데이터를 지우지 않습니다.
    public static class HideoutSaveService
    {
        // 버전 10은 제작 대기열과 발전기 연료를 추가합니다. 이전 버전은 빈 대기열·기본 연료로 자동 이관됩니다.
        public const int CurrentVersion = 10;
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
        public static string DefaultBackupPath => DefaultSavePath + ".bak";

        // 타이틀 화면의 '게임 로드' 버튼은 실제로 불러올 파일이 있을 때만 활성화되어야 합니다.
        // 주 저장 파일이 손상되거나 교체 중이어도 백업 파일로 복구할 수 있으므로 둘 중 하나만 존재해도 true를 반환합니다.
        public static bool DefaultSaveExists
        {
            get
            {
                try
                {
                    return File.Exists(DefaultSavePath) || File.Exists(DefaultBackupPath);
                }
                catch (Exception exception)
                {
                    // 경로 접근 자체가 실패한 환경에서는 로드 버튼을 비활성화하고 Console에 원인을 남깁니다.
                    Debug.LogWarning($"저장 파일 존재 여부 확인 실패: {exception.Message}");
                    return false;
                }
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetRuntimeSession()
        {
            defaultSaveLoaded = false;
            defaultLoadSucceeded = false;
            defaultPathOverride = null;
            HideoutStateStore.ClearInMemoryState();
            HideoutFacilityStateStore.ResetForTests();
            HideoutMissionStateStore.ResetForTests();
            HideoutEconomyStateStore.ResetForTests();
            HideoutCraftQueueStateStore.ResetForTests();
            TutorialProgressStore.ResetForTests();
        }

        // 한 Play 세션에서 기본 파일을 한 번만 읽고, 파일이 없는 첫 실행은 정상적인 빈 창고로 처리합니다.
        public static bool LoadDefaultIfNeeded()
        {
            if (defaultSaveLoaded) return defaultLoadSucceeded;
            defaultSaveLoaded = true;
            if (!File.Exists(DefaultSavePath))
                defaultLoadSucceeded = !File.Exists(DefaultBackupPath) || LoadFromPath(DefaultBackupPath);
            else
                defaultLoadSucceeded = LoadFromPath(DefaultSavePath) ||
                                       (File.Exists(DefaultBackupPath) && LoadFromPath(DefaultBackupPath));
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
                FlattenEquipment(HideoutStateStore.LoadoutEquipment, data.loadoutItems);
                data.facilityLevels.AddRange(HideoutFacilityStateStore.CaptureState());
                data.activeMissionId = HideoutMissionStateStore.ActiveMissionId;
                data.activeMissionProgress = HideoutMissionStateStore.ObjectiveProgress;
                data.activeMissionZombieKills = HideoutMissionStateStore.ZombieKillProgress;
                data.missionCompletions.AddRange(HideoutMissionStateStore.CaptureCompletionState());
                data.credits = HideoutEconomyStateStore.Credits;
                data.tutorialStep = (int)TutorialProgressStore.CurrentStep;
                data.traderReputation = HideoutEconomyStateStore.TraderReputation;
                data.stockRefreshIndex = HideoutEconomyStateStore.StockRefreshIndex;
                data.craftJobs.AddRange(HideoutCraftQueueStateStore.CaptureState());
                data.generatorEnabled = HideoutCraftQueueStateStore.GeneratorEnabled;
                data.generatorFuelSeconds = HideoutCraftQueueStateStore.GeneratorFuelSeconds;

                string json = JsonUtility.ToJson(data, true);
                temporaryPath = fullPath + ".tmp";
                File.WriteAllText(temporaryPath, json);

                // 기존 최종 파일은 새 임시 파일 작성이 완전히 끝난 뒤 한 번에 교체합니다.
                if (File.Exists(fullPath))
                {
                    string backupPath = fullPath + ".bak";
                    if (File.Exists(backupPath)) File.Delete(backupPath);
                    File.Replace(temporaryPath, fullPath, backupPath);
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
                var loadoutEquipment = new List<RaidEquipmentSnapshot>();
                var facilityLevels = new List<HideoutFacilityLevelState>();
                string activeMissionId = string.Empty;
                int activeMissionProgress = 0;
                var missionCompletions = new List<HideoutMissionCompletionState>();
                int savedCredits = HideoutEconomyStateStore.DefaultCredits;
                int savedTutorialStep = 0;
                int savedTraderReputation = 0;
                int savedStockRefreshIndex = 0;
                var savedCraftJobs = new List<HideoutCraftJobState>();
                bool savedGeneratorEnabled = true;
                float savedGeneratorFuel = HideoutCraftQueueStateStore.DefaultFuelSeconds;
                if (header.version >= 3)
                {
                    HideoutSaveData data = JsonUtility.FromJson<HideoutSaveData>(json);
                    if (!TryUnflattenSave(data, out stash, out loadout, out loadoutEquipment)) return false;
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

                    // 버전 1~5에는 화폐가 없으므로 새 게임과 같은 시작 자금을 제공합니다.
                    // 버전 6 파일의 음수 잔액은 정상 플레이로 만들 수 없는 손상 데이터이므로 전체 불러오기를 거부합니다.
                    if (header.version >= 6)
                    {
                        if (data.credits < 0 || data.tutorialStep < 0 ||
                            data.tutorialStep > (int)TutorialStep.Completed) return false;
                        savedCredits = data.credits;
                        savedTutorialStep = data.tutorialStep;
                    }
                    if (header.version >= 9)
                    {
                        if (data.traderReputation < 0 || data.stockRefreshIndex < 0) return false;
                        savedTraderReputation = data.traderReputation;
                        savedStockRefreshIndex = data.stockRefreshIndex;
                    }
                    if (header.version >= 10)
                    {
                        if (data.craftJobs == null || data.craftJobs.Count > HideoutCraftQueueStateStore.MaximumQueueSize ||
                            data.generatorFuelSeconds < 0f || data.generatorFuelSeconds > 86400f) return false;
                        foreach (HideoutCraftJobState job in data.craftJobs)
                        {
                            if (job == null || string.IsNullOrWhiteSpace(job.recipeId) ||
                                string.IsNullOrWhiteSpace(job.outputItemId) || job.outputQuantity <= 0 ||
                                job.remainingSeconds < 0f) return false;
                            savedCraftJobs.Add(job);
                        }
                        savedGeneratorEnabled = data.generatorEnabled;
                        savedGeneratorFuel = data.generatorFuelSeconds;
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

                HideoutStateStore.ReplaceState(stash, loadout, loadoutEquipment);
                HideoutFacilityStateStore.ReplaceState(facilityLevels);
                HideoutMissionStateStore.Restore(activeMissionId, activeMissionProgress, missionCompletions);
                HideoutEconomyStateStore.Restore(savedCredits, savedTraderReputation, savedStockRefreshIndex);
                HideoutCraftQueueStateStore.Restore(savedCraftJobs, savedGeneratorEnabled, savedGeneratorFuel);
                TutorialProgressStore.Restore(savedTutorialStep);
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

        // 새 게임 확인 UI가 호출하는 진입점입니다. 메인·백업 파일을 지우고 모든 메모리 진행도를 시작 상태로 되돌립니다.
        // 호출 전에 UI에서 별도 확인을 받아야 하며, 삭제 실패 시 false를 반환해 빈 상태를 저장하지 않습니다.
        public static bool TryResetDefaultSave()
        {
            try
            {
                if (File.Exists(DefaultSavePath)) File.Delete(DefaultSavePath);
                if (File.Exists(DefaultBackupPath)) File.Delete(DefaultBackupPath);
                HideoutStateStore.ClearInMemoryState();
                HideoutFacilityStateStore.ResetForTests();
                HideoutMissionStateStore.ResetForTests();
                HideoutEconomyStateStore.ResetForTests();
                HideoutCraftQueueStateStore.ResetForTests();
                TutorialProgressStore.ResetForTests();
                defaultSaveLoaded = true;
                defaultLoadSucceeded = true;
                return true;
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"새 게임 데이터 초기화 실패: {exception.Message}");
                return false;
            }
        }

        private static void FlattenRoots(
            IReadOnlyList<RaidItemSnapshot> roots,
            List<HideoutSaveItemData> target)
        {
            if (roots == null) return;
            foreach (RaidItemSnapshot root in roots) FlattenItem(root, -1, string.Empty, target);
        }

        // 장비는 일반 출격 아이템과 같은 평탄 목록에 저장하되 최상위 항목에만 장비 슬롯 이름을 붙입니다.
        // 기존 버전의 loadoutItems 배열을 유지하므로 별도 재귀 자료형을 추가하지 않고도 이전 저장과 호환됩니다.
        private static void FlattenEquipment(
            IReadOnlyList<RaidEquipmentSnapshot> equipment,
            List<HideoutSaveItemData> target)
        {
            if (equipment == null) return;
            foreach (RaidEquipmentSnapshot equipped in equipment)
                if (equipped?.item != null && !string.IsNullOrWhiteSpace(equipped.slot))
                    FlattenItem(equipped.item, -1, equipped.slot, target);
        }

        private static void FlattenItem(
            RaidItemSnapshot item,
            int parentIndex,
            string equipmentSlot,
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
                hasWeaponDurabilityState = item.hasWeaponDurabilityState,
                weaponDurability = item.weaponDurability,
                hasTriggerModeState = item.hasTriggerModeState,
                selectedTriggerMode = item.selectedTriggerMode,
                hasArmorState = item.hasArmorState,
                armorDurability = item.armorDurability,
                attachmentItemIds = item.attachmentItemIds != null
                    ? new List<string>(item.attachmentItemIds)
                    : new List<string>(),
                parentIndex = parentIndex,
                equipmentSlot = equipmentSlot ?? string.Empty
            });

            if (item.contents != null)
                foreach (RaidItemSnapshot child in item.contents) FlattenItem(child, ownIndex, string.Empty, target);
        }

        private static bool TryUnflattenSave(
            HideoutSaveData data,
            out List<RaidItemSnapshot> stash,
            out List<RaidItemSnapshot> loadout,
            out List<RaidEquipmentSnapshot> loadoutEquipment)
        {
            stash = null;
            loadout = null;
            loadoutEquipment = null;
            if (data == null || data.version < 3 || data.version > CurrentVersion ||
                data.stashItems == null || data.loadoutItems == null)
                return false;
            return TryUnflattenList(data.stashItems, false, out stash, out _) &&
                   TryUnflattenList(data.loadoutItems, true, out loadout, out loadoutEquipment);
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
            bool allowEquipment,
            out List<RaidItemSnapshot> roots,
            out List<RaidEquipmentSnapshot> equipment)
        {
            roots = new List<RaidItemSnapshot>();
            equipment = new List<RaidEquipmentSnapshot>();
            var created = new List<RaidItemSnapshot>(flat.Count);
            var usedEquipmentSlots = new HashSet<EquipmentSlotType>();
            for (int i = 0; i < flat.Count; i++)
            {
                HideoutSaveItemData item = flat[i];
                // 부모는 반드시 자신보다 먼저 기록되어야 하므로 순환 참조와 잘못된 인덱스를 동시에 차단합니다.
                if (!IsValidFlatItem(item) || item.parentIndex >= i || item.parentIndex < -1) return false;
                if (item.parentIndex != -1 && !string.IsNullOrWhiteSpace(item.equipmentSlot)) return false;

                var snapshot = new RaidItemSnapshot
                {
                    itemId = item.itemId,
                    quantity = item.quantity,
                    gridX = item.gridX,
                    gridY = item.gridY,
                    rotated = item.rotated,
                    hasMagazineState = item.hasMagazineState,
                    magazineRounds = item.magazineRounds,
                    hasWeaponDurabilityState = item.hasWeaponDurabilityState,
                    weaponDurability = item.weaponDurability,
                    hasTriggerModeState = item.hasTriggerModeState,
                    selectedTriggerMode = item.selectedTriggerMode,
                    hasArmorState = item.hasArmorState,
                    armorDurability = item.armorDurability
                };
                if (item.attachmentItemIds != null)
                    foreach (string attachmentId in item.attachmentItemIds)
                        if (!string.IsNullOrWhiteSpace(attachmentId)) snapshot.attachmentItemIds.Add(attachmentId);
                created.Add(snapshot);
                if (item.parentIndex == -1)
                {
                    if (string.IsNullOrWhiteSpace(item.equipmentSlot))
                    {
                        roots.Add(snapshot);
                    }
                    else
                    {
                        if (!allowEquipment ||
                            !Enum.TryParse(item.equipmentSlot, out EquipmentSlotType slot) ||
                            slot == EquipmentSlotType.None ||
                            !usedEquipmentSlots.Add(slot)) return false;
                        equipment.Add(new RaidEquipmentSnapshot { slot = slot.ToString(), item = snapshot });
                    }
                }
                else created[item.parentIndex].contents.Add(snapshot);
            }
            return true;
        }

        private static bool IsValidFlatItem(HideoutSaveItemData item)
        {
            return item != null && !string.IsNullOrWhiteSpace(item.itemId) && item.quantity > 0 &&
                   (!item.hasMagazineState || item.magazineRounds >= 0) &&
                   (!item.hasWeaponDurabilityState || item.weaponDurability >= 0f) &&
                   (!item.hasTriggerModeState ||
                    Enum.IsDefined(typeof(WeaponTriggerMode), item.selectedTriggerMode)) &&
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
                (item.hasWeaponDurabilityState && item.weaponDurability < 0f) ||
                (item.hasTriggerModeState &&
                 !Enum.IsDefined(typeof(WeaponTriggerMode), item.selectedTriggerMode)) ||
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
