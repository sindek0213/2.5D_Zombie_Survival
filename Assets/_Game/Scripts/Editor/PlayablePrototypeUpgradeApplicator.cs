#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace ZombieSurvival.EditorTools
{
    // 이번 플레이용 학교·임무·UI 업그레이드를 기존 프로젝트에 정확히 한 번 반영하는 Editor 진입점입니다.
    // 코드만 바뀌고 이미 저장된 Scene과 ScriptableObject가 이전 상태로 남는 일을 막되, 매번 Unity를 열 때 씬을 덮어쓰지는 않습니다.
    public static class PlayablePrototypeUpgradeApplicator
    {
        // 버전 5는 시야·소음 AI, 위험도 전리품, 확장 임무, 상인 진행도, 제작 대기열과 접근성 UI를 실제 에셋·씬에 반영합니다.
        private const int UpgradeVersion = 5;
        private const string MenuPath = "Tools/2.5D Zombie Survival/Apply Playable Prototype Upgrade";

        // Unity 프로젝트의 고유 GUID를 키에 포함해 같은 컴퓨터의 다른 프로젝트가 이 버전 기록을 공유하지 않게 합니다.
        private static string AppliedVersionKey
            => "ZombieSurvival.PlayablePrototypeUpgrade." + PlayerSettings.productGUID;

        [InitializeOnLoadMethod]
        private static void ScheduleAutomaticUpgrade()
        {
            if (EditorPrefs.GetInt(AppliedVersionKey, 0) >= UpgradeVersion) return;
            EditorApplication.delayCall += TryApplyAutomatically;
        }

        private static void TryApplyAutomatically()
        {
            // 메뉴나 배치 명령이 먼저 같은 버전을 적용했다면 예약된 delayCall은 아무 작업도 하지 않습니다.
            // 이 재검사가 없으면 한 Unity 실행에서 자동 적용과 수동 적용이 연달아 씬을 두 번 재생성할 수 있습니다.
            if (EditorPrefs.GetInt(AppliedVersionKey, 0) >= UpgradeVersion) return;

            // AssetDatabase가 파일을 읽거나 스크립트를 컴파일하는 동안 씬을 만들면 참조가 빠질 수 있으므로 준비가 끝난 다음 프레임까지 기다립니다.
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                EditorApplication.delayCall += TryApplyAutomatically;
                return;
            }
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;

            // 실행 전에 버전을 기록해 씬 저장 중 도메인 리로드가 발생해도 같은 업그레이드가 동시에 두 번 시작되지 않게 합니다.
            EditorPrefs.SetInt(AppliedVersionKey, UpgradeVersion);
            if (!ApplyUpgrade()) EditorPrefs.DeleteKey(AppliedVersionKey);
        }

        [MenuItem(MenuPath)]
        public static void ApplyUpgradeFromMenu()
        {
            int previousVersion = EditorPrefs.GetInt(AppliedVersionKey, 0);
            EditorPrefs.SetInt(AppliedVersionKey, UpgradeVersion);
            if (ApplyUpgrade()) return;

            // 적용에 실패하면 이전 버전 기록을 복원해 문제를 고친 다음 다시 실행할 수 있게 합니다.
            if (previousVersion > 0) EditorPrefs.SetInt(AppliedVersionKey, previousVersion);
            else EditorPrefs.DeleteKey(AppliedVersionKey);
        }

        private static bool ApplyUpgrade()
        {
            // 거점·학교 재생성은 기존 Player 프리팹을 재사용하므로, 새 의료품별 사용 시간은 프리팹 원본에 먼저 직접 반영합니다.
            // 전체 Player 프리팹을 새로 만들지 않아 사용자가 Inspector에서 조정한 다른 이동·전투 설정은 그대로 보존됩니다.
            UpgradePlayerMedicalUseSettings();
            // 거점을 먼저 재생성해야 새 학교 임무 에셋과 UI 참조가 준비된 뒤 학교 레이드 씬을 저장할 수 있습니다.
            PrototypeContentBuilder.RebuildHideoutOnly();
            PrototypeContentBuilder.RebuildSchoolRaidOnly();
            SurvivalUiThemeApplicator.ApplyTheme(true);
            bool hasSchoolScene = AssetDatabase.LoadAssetAtPath<SceneAsset>("Assets/_Game/Scenes/RaidSchool.unity") != null;
            bool hasVisitMission = AssetDatabase.LoadAssetAtPath<ZombieSurvival.Hideout.HideoutMissionDefinition>(
                "Assets/_Game/Data/Hideout/mission_school_recon.asset") != null;
            if (!hasSchoolScene || !hasVisitMission)
            {
                Debug.LogError("Playable prototype upgrade did not finish. Fix the earlier Console error and run the menu again.");
                return false;
            }

            Debug.Log("Playable prototype upgrade applied: themed UI, school content, raid flow and missions.");
            return true;
        }

        private static void UpgradePlayerMedicalUseSettings()
        {
            const string playerPrefabPath = "Assets/_Game/Prefabs/Characters/Player.prefab";
            GameObject root = PrefabUtility.LoadPrefabContents(playerPrefabPath);
            try
            {
                ZombieSurvival.Inventory.PlayerInventory inventory =
                    root.GetComponent<ZombieSurvival.Inventory.PlayerInventory>();
                if (inventory == null)
                {
                    Debug.LogWarning("Player medical settings upgrade skipped: PlayerInventory component was not found.");
                    return;
                }

                var serializedInventory = new SerializedObject(inventory);
                serializedInventory.FindProperty("healingUseDuration").floatValue = 4f;
                serializedInventory.FindProperty("bandageUseDuration").floatValue = 2.2f;
                serializedInventory.FindProperty("splintUseDuration").floatValue = 3.5f;
                serializedInventory.ApplyModifiedPropertiesWithoutUndo();
                PrefabUtility.SaveAsPrefabAsset(root, playerPrefabPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }
    }
}
#endif
