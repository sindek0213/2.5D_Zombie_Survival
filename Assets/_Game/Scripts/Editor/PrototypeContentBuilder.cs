#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using ZombieSurvival.AI;
using ZombieSurvival.CameraSystem;
using ZombieSurvival.Combat;
using ZombieSurvival.Core;
using ZombieSurvival.Extraction;
using ZombieSurvival.Hideout;
using ZombieSurvival.Inventory;
using ZombieSurvival.Items;
using ZombieSurvival.Player;
using ZombieSurvival.UI;

namespace ZombieSurvival.EditorTools
{
    // 사람이 Hierarchy와 Inspector에서 반복할 폴더, 에셋, 프리팹과 씬 설정을 코드로 재현하는 Editor 전용 도구입니다.
    // 같은 입력으로 항상 같은 테스트 콘텐츠를 만들기 때문에 새 환경이나 손상된 씬에서도 프로토타입을 복구할 수 있습니다.
    // 실제 게임 빌드에는 포함되지 않으며 Rebuild 실행 시 자동 생성 씬과 프리팹의 수동 변경은 덮어써집니다.
    public static class PrototypeContentBuilder
    {
        private const string Root = "Assets/_Game";

        // 폴더 → 정의 데이터 → 프리팹 → 씬 → Build Settings 순서로 전체 콘텐츠를 생성하는 진입점입니다.
        // 뒤 단계가 앞에서 생성한 에셋을 참조하므로 이 호출 순서를 임의로 바꾸면 null 참조가 생길 수 있습니다.
        // SaveAssets와 Refresh는 메모리의 변경을 디스크와 Unity AssetDatabase에 최종 반영합니다.
        [MenuItem("Tools/2.5D Zombie Survival/Rebuild Prototype Content")]
        public static void Rebuild()
        {
            // 의존 순서에 맞춰 폴더와 에셋을 만든 뒤 Build Settings까지 갱신합니다.
            EnsureFolders();
            Sprite sprite = CreatePlayerSprite();
            WeaponDefinition[] weaponDefinitions = CreateWeaponDefinitions();
            MeleeWeaponDefinition[] meleeWeaponDefinitions = CreateMeleeWeaponDefinitions();
            StatusEffectDefinition[] statusEffects = CreateStatusEffectDefinitions();
            WeaponAttachmentDefinition[] attachmentDefinitions = CreateWeaponAttachmentDefinitions();
            ItemDefinition[] itemDefinitions = CreateItemDefinitions(weaponDefinitions, meleeWeaponDefinitions, statusEffects, attachmentDefinitions);
            ItemCatalog itemCatalog = CreateItemCatalog(itemDefinitions);
            LootTableDefinition[] lootTables = CreateLootTables(itemDefinitions);
            ExtractionRequirement extractionRequirement = CreateExtractionRequirement();
            RaidRules raidRules = CreateRaidRules();
            RaidRegionDefinition[] raidRegions = CreateRaidRegionDefinitions();
            RegionLootProfile schoolLootProfile = CreateSchoolRegionLootProfile(raidRegions, itemCatalog);
            LoadingScreenCatalog loadingCatalog = CreateLoadingScreenCatalog(raidRegions);
            HideoutFacilityDefinition[] hideoutFacilities = CreateHideoutFacilityDefinitions(itemCatalog);
            HideoutMissionDefinition[] hideoutMissions = CreateHideoutMissionDefinitions(itemCatalog);
            HideoutRecipeDefinition[] hideoutRecipes = CreateHideoutRecipeDefinitions(itemCatalog);
            GameObject prefab = CreatePlayerPrefab(sprite);
            GameObject zombiePrefab = CreateZombiePrefab(sprite);
            ZombieArchetype[] zombieArchetypes = CreateZombieArchetypes(zombiePrefab, itemDefinitions);
            GameObject inventoryCanvasPrefab = CreateInventoryUiPrefabs();
            GameObject raidHudCanvasPrefab = CreateRaidHudCanvasPrefab();
            CreatePauseCanvasPrefab();
            GameObject titleCanvasPrefab = CreateTitleCanvasPrefab();
            GameObject loadingCanvasPrefab = CreateLoadingCanvasPrefab(loadingCatalog);
            GameObject hideoutCanvasPrefab = CreateHideoutCanvasPrefab();
            GameObject hideoutMissionCanvasPrefab = CreateHideoutMissionCanvasPrefab();
            GameObject hideoutFacilityUpgradeCanvasPrefab = CreateHideoutFacilityUpgradeCanvasPrefab();
            GameObject hideoutTraderCanvasPrefab = CreateHideoutTraderCanvasPrefab();
            GameObject hideoutDeploymentCanvasPrefab = CreateHideoutDeploymentCanvasPrefab(raidRegions);
            GameObject hideoutWorldCanvasPrefab = CreateHideoutWorldCanvasPrefab();
            CreateTitleScene(titleCanvasPrefab);
            CreateLoadingScene(loadingCanvasPrefab);
            CreateHideoutScene(
                prefab,
                itemCatalog,
                inventoryCanvasPrefab,
                hideoutCanvasPrefab,
                hideoutMissionCanvasPrefab,
                hideoutFacilityUpgradeCanvasPrefab,
                hideoutTraderCanvasPrefab,
                hideoutDeploymentCanvasPrefab,
                hideoutWorldCanvasPrefab,
                hideoutFacilities,
                hideoutMissions,
                hideoutRecipes);
            // 기능 검증도 실제 학교에서 진행하므로 작은 RaidPrototype 씬은 더 이상 재생성하지 않습니다.
            // 파일은 이전 배치를 참고할 수 있게 보존하지만 전체 Rebuild의 출력과 Build Settings에는 포함하지 않습니다.
            CreateRaidScene(GameSceneNavigator.SchoolRaidSceneName, true, false, prefab, zombiePrefab, inventoryCanvasPrefab, raidHudCanvasPrefab, itemDefinitions, itemCatalog, extractionRequirement, raidRules, zombieArchetypes, lootTables, schoolLootProfile);
            SetBuildScenes();

            // 전체 프로토타입 재생성은 Player와 UI 프리팹을 새로 저장하므로 생성 원화 연결이 빠질 수 있습니다.
            // 프로젝트 루트에 ArtRemake가 있을 때만 마지막에 그림 적용 도구를 다시 실행해 데이터·씬 생성 후에도 최신 원화를 유지합니다.
            string generatedArtSource = Path.GetFullPath(Path.Combine(Application.dataPath, "../ArtRemake"));
            if (Directory.Exists(generatedArtSource)) GeneratedArtApplicator.Apply();
            // UI 프리팹 생성이 모두 끝난 뒤 공용 테마를 다시 입혀, Rebuild를 실행해도 수동으로 맞춘 색과 장식이 사라지지 않게 합니다.
            SurvivalUiThemeApplicator.ApplyTheme(false);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("2.5D prototype content rebuilt successfully.");
        }

        // 플레이어의 다른 배치와 학교 씬은 건드리지 않고 상태 이상 컴포넌트와 레이드 HUD만 현재 규칙으로 갱신합니다.
        // 부위별 손상 시스템을 제거한 뒤 기존 프리팹에 남은 직렬화 데이터와 네 부위 UI를 정리할 때 사용하는 안전한 전용 메뉴입니다.
        [MenuItem("Tools/2.5D Zombie Survival/Upgrade Player Conditions Only")]
        public static void UpgradePlayerConditionsOnly()
        {
            const string playerPath = Root + "/Prefabs/Characters/Player.prefab";
            const string hudPath = Root + "/Prefabs/UI/RaidHudCanvas.prefab";
            GameObject playerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(playerPath);
            GameObject hudPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(hudPath);
            if (playerPrefab == null || hudPrefab == null)
            {
                Debug.LogError("상태 이상 UI 갱신 실패: Player 또는 RaidHudCanvas 프리팹을 찾을 수 없습니다.");
                return;
            }

            // 프리팹을 다시 저장하면 삭제된 옛 부위 손상 필드가 YAML에 유령 데이터로 남지 않습니다.
            EnsurePlayerConditionPrefab(playerPrefab);
            UpgradeRaidHudForPlayerCondition(hudPath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("플레이어 상태 이상과 출혈 화면 테두리 갱신을 완료했습니다.");
        }

        // 씬·프리팹·이미지는 다시 만들지 않고 단검·도끼 데이터와 드롭 목록만 현재 프로젝트에 추가합니다.
        // 근접무기 기능을 반영할 때 전체 Rebuild로 사용자가 수정한 학교 배치를 덮어쓰지 않기 위한 안전한 전용 메뉴입니다.
        [MenuItem("Tools/2.5D Zombie Survival/Upgrade Melee Weapons Only")]
        public static void UpgradeMeleeWeaponsOnly()
        {
            EnsureFolders();
            WeaponDefinition[] firearms =
            {
                AssetDatabase.LoadAssetAtPath<WeaponDefinition>(Root + "/Data/Weapons/rifle.asset"),
                AssetDatabase.LoadAssetAtPath<WeaponDefinition>(Root + "/Data/Weapons/pistol.asset"),
                AssetDatabase.LoadAssetAtPath<WeaponDefinition>(Root + "/Data/Weapons/smg.asset"),
                AssetDatabase.LoadAssetAtPath<WeaponDefinition>(Root + "/Data/Weapons/shotgun.asset")
            };
            if (firearms[0] == null || firearms[1] == null || firearms[2] == null || firearms[3] == null)
            {
                Debug.LogError("근접무기 갱신 실패: 기본 총기 에셋이 없습니다. 전체 Rebuild를 먼저 실행해 주세요.");
                return;
            }

            MeleeWeaponDefinition[] meleeWeapons = CreateMeleeWeaponDefinitions();
            StatusEffectDefinition[] statusEffects = CreateStatusEffectDefinitions();
            WeaponAttachmentDefinition[] attachments = CreateWeaponAttachmentDefinitions();
            ItemDefinition[] items = CreateItemDefinitions(firearms, meleeWeapons, statusEffects, attachments);
            ItemCatalog catalog = CreateItemCatalog(items);
            CreateLootTables(items);
            CreateSchoolRegionLootProfile(CreateRaidRegionDefinitions(), catalog);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("단검·도끼 전투 데이터, 아이템 카탈로그와 학교 무기 상자 드롭 갱신을 완료했습니다.");
        }

        // 현재 학교 씬이나 사용자가 손본 UI를 통째로 재생성하지 않고 버프 데이터, 퀵슬롯 컴포넌트와 HUD만 보강합니다.
        // 기능 추가를 확인할 때 이 메뉴를 사용하면 전체 Rebuild가 학교 오브젝트 배치를 덮어쓰는 일을 피할 수 있습니다.
        [MenuItem("Tools/2.5D Zombie Survival/Upgrade Buffs and Quick Slots Only")]
        public static void UpgradeBuffsAndQuickSlotsOnly()
        {
            EnsureFolders();
            WeaponDefinition[] firearms =
            {
                AssetDatabase.LoadAssetAtPath<WeaponDefinition>(Root + "/Data/Weapons/rifle.asset"),
                AssetDatabase.LoadAssetAtPath<WeaponDefinition>(Root + "/Data/Weapons/pistol.asset"),
                AssetDatabase.LoadAssetAtPath<WeaponDefinition>(Root + "/Data/Weapons/smg.asset"),
                AssetDatabase.LoadAssetAtPath<WeaponDefinition>(Root + "/Data/Weapons/shotgun.asset")
            };
            GameObject playerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(Root + "/Prefabs/Characters/Player.prefab");
            const string hudPath = Root + "/Prefabs/UI/RaidHudCanvas.prefab";
            const string inventoryPath = Root + "/Prefabs/UI/InventoryCanvas.prefab";
            if (playerPrefab == null || AssetDatabase.LoadAssetAtPath<GameObject>(hudPath) == null ||
                AssetDatabase.LoadAssetAtPath<GameObject>(inventoryPath) == null ||
                firearms[0] == null || firearms[1] == null || firearms[2] == null || firearms[3] == null)
            {
                Debug.LogError("버프·퀵슬롯 갱신 실패: Player, RaidHudCanvas 또는 기본 총기 에셋이 없습니다. 전체 Rebuild를 먼저 실행해 주세요.");
                return;
            }

            // 기존 아이템 배열 순서와 GUID는 유지하면서 새 버프 아이템 세 개를 마지막에 추가하고 드롭 데이터만 갱신합니다.
            MeleeWeaponDefinition[] meleeWeapons = CreateMeleeWeaponDefinitions();
            StatusEffectDefinition[] statusEffects = CreateStatusEffectDefinitions();
            WeaponAttachmentDefinition[] attachments = CreateWeaponAttachmentDefinitions();
            ItemDefinition[] items = CreateItemDefinitions(firearms, meleeWeapons, statusEffects, attachments);
            ItemCatalog catalog = CreateItemCatalog(items);
            CreateLootTables(items);
            CreateSchoolRegionLootProfile(CreateRaidRegionDefinitions(), catalog);

            EnsurePlayerBuffAndQuickSlotsPrefab(playerPrefab);
            UpgradeInventoryCanvasAnimation(inventoryPath);
            EnsureQuickSlotHud(hudPath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("버프 아이템, 방탄복 확장형 1~0 퀵슬롯, 인벤토리 드롭 UI와 레이드 HUD 갱신을 완료했습니다.");
        }

        // 플레이어 스프라이트나 레이드 씬을 다시 만들지 않고 경제 데이터와 거점 콘텐츠만 갱신하는 전용 메뉴입니다.
        // 사용자가 나중에 직접 제공할 이미지 리소스는 그대로 보존하면서 새 아이템, 드롭 목록, 시설, 임무, 제작법을 실제 에셋에 반영합니다.
        [MenuItem("Tools/2.5D Zombie Survival/Rebuild Hideout Only")]
        public static void RebuildHideoutOnly()
        {
            EnsureFolders();

            // 기존 무기 에셋은 새로 생성하거나 수치를 덮어쓰지 않고 그대로 불러옵니다.
            // ItemDefinition이 무기 에셋을 참조해야 하므로 네 무기 중 하나라도 없으면 잘못된 아이템 에셋을 만들지 않고 즉시 중단합니다.
            WeaponDefinition[] weaponDefinitions =
            {
                AssetDatabase.LoadAssetAtPath<WeaponDefinition>(Root + "/Data/Weapons/rifle.asset"),
                AssetDatabase.LoadAssetAtPath<WeaponDefinition>(Root + "/Data/Weapons/pistol.asset"),
                AssetDatabase.LoadAssetAtPath<WeaponDefinition>(Root + "/Data/Weapons/smg.asset"),
                AssetDatabase.LoadAssetAtPath<WeaponDefinition>(Root + "/Data/Weapons/shotgun.asset")
            };
            GameObject playerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(Root + "/Prefabs/Characters/Player.prefab");
            GameObject zombiePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(Root + "/Prefabs/Enemies/Zombie.prefab");
            if (playerPrefab == null || zombiePrefab == null ||
                weaponDefinitions[0] == null || weaponDefinitions[1] == null ||
                weaponDefinitions[2] == null || weaponDefinitions[3] == null)
            {
                Debug.LogError("거점 생성 실패: Player, Zombie 또는 기본 무기 에셋이 없습니다. 전체 Rebuild를 먼저 실행해 주세요.");
                return;
            }

            // 거점 전용 재생성에서도 레이드가 참조하는 기존 Player/HUD 프리팹에 새 상태와 튜토리얼 컴포넌트만 안전하게 보강합니다.
            playerPrefab = EnsurePlayerConditionPrefab(playerPrefab);
            playerPrefab = EnsurePlayerBuffAndQuickSlotsPrefab(playerPrefab);
            ApplyWeaponPenetrationDefaults(weaponDefinitions);
            ApplyWeaponFinishingDefaults(weaponDefinitions);
            CreateRaidHudCanvasPrefab();
            CreatePauseCanvasPrefab();

            // 새 재료 아이템까지 포함한 ItemDefinition과 ItemCatalog를 먼저 갱신해야 시설 비용과 제작법의 참조가 null이 되지 않습니다.
            // 이 과정은 데이터 에셋만 수정하며 CreatePlayerSprite를 호출하지 않으므로 현재 프로젝트의 이미지 리소스에는 손대지 않습니다.
            // 부착물 정의를 먼저 만들어야 각 부착물 아이템이 실제 성능 데이터 에셋을 참조할 수 있습니다.
            MeleeWeaponDefinition[] meleeWeaponDefinitions = CreateMeleeWeaponDefinitions();
            StatusEffectDefinition[] statusEffects = CreateStatusEffectDefinitions();
            WeaponAttachmentDefinition[] attachmentDefinitions = CreateWeaponAttachmentDefinitions();
            ItemDefinition[] itemDefinitions = CreateItemDefinitions(weaponDefinitions, meleeWeaponDefinitions, statusEffects, attachmentDefinitions);
            ItemCatalog itemCatalog = CreateItemCatalog(itemDefinitions);

            // 기존 레이드 씬 자체는 재생성하지 않고, 씬이 이미 참조 중인 드롭 테이블과 좀비 유형 에셋의 내용만 최신 상태로 갱신합니다.
            // 따라서 재료가 상자와 좀비 시체에서 나오도록 연결하면서 사용자가 씬에서 직접 조정한 오브젝트 배치는 보존합니다.
            CreateLootTables(itemDefinitions);
            ZombieArchetype[] zombieArchetypes = CreateZombieArchetypes(zombiePrefab, itemDefinitions);
            UpgradeRaidSceneArchetypes(zombieArchetypes);

            HideoutFacilityDefinition[] facilities = CreateHideoutFacilityDefinitions(itemCatalog);
            HideoutMissionDefinition[] missions = CreateHideoutMissionDefinitions(itemCatalog);
            HideoutRecipeDefinition[] recipes = CreateHideoutRecipeDefinitions(itemCatalog);
            RaidRegionDefinition[] raidRegions = CreateRaidRegionDefinitions();
            CreateSchoolRegionLootProfile(raidRegions, itemCatalog);
            GameObject storageCanvas = CreateHideoutCanvasPrefab();
            // 기존 디자인은 UpgradeInventoryCanvasAnimation이 보존하고 새 무기 관리 패널 참조만 보충합니다.
            GameObject inventoryCanvas = CreateInventoryUiPrefabs();
            GameObject missionCanvas = CreateHideoutMissionCanvasPrefab();
            GameObject upgradeCanvas = CreateHideoutFacilityUpgradeCanvasPrefab();
            GameObject traderCanvas = CreateHideoutTraderCanvasPrefab();
            GameObject deploymentCanvas = CreateHideoutDeploymentCanvasPrefab(raidRegions);
            GameObject worldCanvas = CreateHideoutWorldCanvasPrefab();
            CreateHideoutScene(playerPrefab, itemCatalog, inventoryCanvas, storageCanvas, missionCanvas, upgradeCanvas, traderCanvas, deploymentCanvas, worldCanvas, facilities, missions, recipes);
            // 거점 전용 재생성으로 일부 Canvas가 다시 저장된 경우에도 전체 UI의 색·테두리 규칙을 즉시 복구합니다.
            SurvivalUiThemeApplicator.ApplyTheme(false);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("Hideout world content rebuilt successfully.");
        }

        // 현재 작업 씬과 이미지 리소스를 보존한 채 임시 학교 레이드 씬만 다시 만드는 전용 메뉴입니다.
        // 전체 Rebuild가 필요 없는 맵 동선 수정 때 사용하며, 기존 Player·Zombie·UI 프리팹과 데이터 에셋을 불러와 실제 게임 시스템을 그대로 연결합니다.
        [MenuItem("Tools/2.5D Zombie Survival/Rebuild School Raid Only")]
        public static void RebuildSchoolRaidOnly()
        {
            EnsureFolders();
            // 학교 설명과 위험도 같은 출격 지역 데이터도 맵과 함께 갱신해 출격 화면이 이전 소형 학교 설명을 계속 표시하지 않게 합니다.
            RaidRegionDefinition[] raidRegions = CreateRaidRegionDefinitions();
            WeaponDefinition[] weaponDefinitions =
            {
                AssetDatabase.LoadAssetAtPath<WeaponDefinition>(Root + "/Data/Weapons/rifle.asset"),
                AssetDatabase.LoadAssetAtPath<WeaponDefinition>(Root + "/Data/Weapons/pistol.asset"),
                AssetDatabase.LoadAssetAtPath<WeaponDefinition>(Root + "/Data/Weapons/smg.asset"),
                AssetDatabase.LoadAssetAtPath<WeaponDefinition>(Root + "/Data/Weapons/shotgun.asset")
            };
            GameObject playerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(Root + "/Prefabs/Characters/Player.prefab");
            GameObject zombiePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(Root + "/Prefabs/Enemies/Zombie.prefab");
            GameObject inventoryCanvas = AssetDatabase.LoadAssetAtPath<GameObject>(Root + "/Prefabs/UI/InventoryCanvas.prefab");
            GameObject raidHudCanvas = AssetDatabase.LoadAssetAtPath<GameObject>(Root + "/Prefabs/UI/RaidHudCanvas.prefab");
            if (playerPrefab == null || zombiePrefab == null || inventoryCanvas == null || raidHudCanvas == null ||
                weaponDefinitions[0] == null || weaponDefinitions[1] == null ||
                weaponDefinitions[2] == null || weaponDefinitions[3] == null)
            {
                Debug.LogError("학교 맵 생성 실패: Player, Zombie, InventoryCanvas, RaidHudCanvas 또는 기본 무기 에셋이 없습니다. 전체 Rebuild를 먼저 실행해 주세요.");
                return;
            }

            // 학교 씬만 재생성해도 기존 프리팹에 새 버프·퀵슬롯 기능이 빠지지 않도록 먼저 안전 업그레이드를 적용합니다.
            playerPrefab = EnsurePlayerBuffAndQuickSlotsPrefab(playerPrefab);
            raidHudCanvas = EnsureQuickSlotHud(Root + "/Prefabs/UI/RaidHudCanvas.prefab");

            // 학교 전용 재생성만 실행해도 새 ESC 메뉴 프리팹이 준비되어 씬에 함께 저장되도록 합니다.
            CreatePauseCanvasPrefab();

            // 아이템 배열은 CreateRaidScene의 테스트 배치 인덱스와 같은 순서여야 하므로 기존 생성 메서드로 정의 참조만 다시 수집합니다.
            MeleeWeaponDefinition[] meleeWeaponDefinitions = CreateMeleeWeaponDefinitions();
            StatusEffectDefinition[] statusEffects = CreateStatusEffectDefinitions();
            WeaponAttachmentDefinition[] attachments = CreateWeaponAttachmentDefinitions();
            ItemDefinition[] itemDefinitions = CreateItemDefinitions(weaponDefinitions, meleeWeaponDefinitions, statusEffects, attachments);
            ItemCatalog itemCatalog = CreateItemCatalog(itemDefinitions);
            LootTableDefinition[] lootTables = CreateLootTables(itemDefinitions);
            ZombieArchetype[] zombieArchetypes = CreateZombieArchetypes(zombiePrefab, itemDefinitions);
            RegionLootProfile schoolLootProfile = CreateSchoolRegionLootProfile(raidRegions, itemCatalog);
            CreateRaidScene(
                GameSceneNavigator.SchoolRaidSceneName,
                true,
                true,
                playerPrefab,
                zombiePrefab,
                inventoryCanvas,
                raidHudCanvas,
                itemDefinitions,
                itemCatalog,
                CreateExtractionRequirement(),
                CreateRaidRules(),
                zombieArchetypes,
                lootTables,
                schoolLootProfile);
            SetBuildScenes();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("Temporary school raid content rebuilt successfully.");
        }

        // 현재 인벤토리 디자인과 이미지 리소스는 그대로 두고 아이템 설명 패널에 고정 표시·바깥 클릭 닫기·상단 드래그 구조를 보충합니다.
        // 전체 콘텐츠 Rebuild를 실행하지 않으므로 사용자가 직접 수정한 다른 프리팹과 씬을 덮어쓰지 않습니다.
        [MenuItem("Tools/2.5D Zombie Survival/Upgrade Item Detail Popups Only")]
        public static void UpgradeItemDetailPopupsOnly()
        {
            const string inventoryPath = Root + "/Prefabs/UI/InventoryCanvas.prefab";
            const string hideoutPath = Root + "/Prefabs/UI/HideoutCanvas.prefab";
            UpgradeInventoryCanvasAnimation(inventoryPath);
            UpgradeHideoutCanvasItemDetailPopup(hideoutPath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("Inventory and hideout item detail popups upgraded with persistent, draggable Canvas views.");
        }

        // 기존 게임 데이터와 거점·레이드 배치를 유지하면서 별도의 Title 씬과 공용 Loading 씬만 다시 만듭니다.
        // 타이틀 UI를 수정한 뒤 빠르게 재생성할 때 전체 아이템과 학교 맵을 덮어쓰지 않도록 분리한 메뉴입니다.
        [MenuItem("Tools/2.5D Zombie Survival/Rebuild Title and Loading Scenes Only")]
        public static void RebuildTitleAndLoadingScenesOnly()
        {
            EnsureFolders();
            RaidRegionDefinition[] raidRegions = CreateRaidRegionDefinitions();
            LoadingScreenCatalog loadingCatalog = CreateLoadingScreenCatalog(raidRegions);
            GameObject titleCanvasPrefab = CreateTitleCanvasPrefab();
            GameObject loadingCanvasPrefab = CreateLoadingCanvasPrefab(loadingCatalog);
            CreateTitleScene(titleCanvasPrefab);
            CreateLoadingScene(loadingCanvasPrefab);
            SetBuildScenes();
            PlayerSettings.productName = TitleMenuHud.WorkingGameTitle;
            SurvivalUiThemeApplicator.ApplyTheme(false);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("Separate Title scene and asynchronous Loading scene rebuilt successfully.");
        }

        // 지시문에 정의된 프로젝트 폴더 구조를 AssetDatabase를 통해 생성합니다.
        private static void EnsureFolders()
        {
            string[] paths = {
                "Art/Materials", "Art/Sprites/Player", "Art/Sprites/Zombies", "Art/Sprites/Items", "Art/Sprites/UI", "Art/Loading", "Art/Models",
                "Audio/Music", "Audio/SFX", "Data/Items", "Data/Weapons", "Data/Effects", "Data/Zombies", "Data/Loot", "Data/RegionalLoot", "Data/Extraction", "Data/Raid", "Data/Hideout", "Data/UI",
                "Prefabs/Characters", "Prefabs/Enemies", "Prefabs/Weapons", "Prefabs/Items", "Prefabs/World", "Prefabs/UI",
                "Scenes", "Scripts/Core", "Scripts/Input", "Scripts/Player", "Scripts/Camera", "Scripts/Combat", "Scripts/AI",
                "Scripts/Items", "Scripts/Inventory", "Scripts/Extraction", "Scripts/Hideout", "Scripts/UI", "Scripts/Editor",
                "Tests/EditMode", "Tests/PlayMode"
            };
            EnsureFolder("Assets", "_Game");
            EnsureFolder("Assets", "ThirdParty");
            foreach (string path in paths)
            {
                string current = Root;
                foreach (string part in path.Split('/'))
                {
                    string next = current + "/" + part;
                    if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(current, part);
                    current = next;
                }
            }
        }

        private static void EnsureFolder(string parent, string name)
        {
            string path = parent + "/" + name;
            if (!AssetDatabase.IsValidFolder(path)) AssetDatabase.CreateFolder(parent, name);
        }

        // 외부 원화 없이 플레이 가능한 단색 플레이어 PNG와 Sprite 에셋을 만듭니다.
        private static Sprite CreatePlayerSprite()
        {
            const string assetPath = Root + "/Art/Sprites/Player/PlayerPlaceholder.png";
            var texture = new Texture2D(64, 96, TextureFormat.RGBA32, false);
            Color clear = new Color(0f, 0f, 0f, 0f);
            Color body = new Color(0.18f, 0.78f, 0.66f, 1f);
            Color edge = new Color(0.04f, 0.18f, 0.18f, 1f);
            // 투명 배경 위에 머리, 몸통, 다리로 구성된 간단한 실루엣을 그립니다.
            for (int y = 0; y < texture.height; y++)
            for (int x = 0; x < texture.width; x++)
            {
                float nx = (x - 31.5f) / 31.5f;
                float ny = (y - 47.5f) / 47.5f;
                bool head = nx * nx + (ny - 0.42f) * (ny - 0.42f) < 0.12f;
                bool torso = Mathf.Abs(nx) < 0.42f && ny > -0.45f && ny < 0.35f;
                bool legs = ny <= -0.35f && ny > -0.9f && (Mathf.Abs(nx - 0.2f) < 0.16f || Mathf.Abs(nx + 0.2f) < 0.16f);
                bool silhouette = head || torso || legs;
                bool border = silhouette && (x < 2 || y < 2 || x > 61 || y > 93);
                texture.SetPixel(x, y, border ? edge : silhouette ? body : clear);
            }
            texture.Apply();
            // 생성된 PNG를 픽셀 아트용 Sprite 설정으로 다시 임포트합니다.
            File.WriteAllBytes(assetPath, texture.EncodeToPNG());
            Object.DestroyImmediate(texture);
            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);
            var importer = (TextureImporter)AssetImporter.GetAtPath(assetPath);
            importer.textureType = TextureImporterType.Sprite;
            importer.spritePixelsPerUnit = 64f;
            importer.alphaIsTransparency = true;
            importer.filterMode = FilterMode.Point;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Sprite>(assetPath);
        }

        // 네 종류 총기의 전투 수치를 별도 에셋으로 생성해 인벤토리 크기와 전투 밸런스를 독립적으로 수정할 수 있게 합니다.
        // 기존 저장과 연결을 보호하기 위해 0 소총·1 권총 순서는 그대로 두고 2 기관단총·3 산탄총을 뒤에 추가합니다.
        private static WeaponDefinition[] CreateWeaponDefinitions()
        {
            return new[]
            {
                // 소총은 권총보다 높은 지속 화력을 갖지만 한 발 피해를 낮춰 모든 일반 좀비를 두 발에 처치하던 과도한 성능을 막습니다.
                CreateWeaponDefinition("rifle", "낡은 소총", WeaponTriggerMode.Automatic, 28f, 38f, 0.14f, 1.7f, 20, AmmoType.FiveFiveSix, accuracySpreadAngle: 1.6f, knockbackForce: 1.1f, armorPenetration: 3, supportsModeSwitch: true, durabilityLossPerShot: 0.18f, repairCostPerPoint: 3),
                // 기본 지급 권총은 일반 좀비를 약 세 발에 처치하며 작은 탄창 대신 좋은 정확도로 초반 생존을 담당합니다.
                CreateWeaponDefinition("pistol", "권총", WeaponTriggerMode.SemiAutomatic, 32f, 30f, 0.26f, 1.2f, 10, AmmoType.NineMillimeter, accuracySpreadAngle: 0.75f, knockbackForce: 1.6f, armorPenetration: 1, durabilityLossPerShot: 0.12f, repairCostPerPoint: 2),
                // 기관단총은 피해량과 사거리를 낮추는 대신 빠른 자동 사격과 큰 탄창으로 근거리 다수전을 담당합니다.
                CreateWeaponDefinition("smg", "9mm 기관단총", WeaponTriggerMode.Automatic, 17f, 22f, 0.085f, 1.5f, 30, AmmoType.NineMillimeter, accuracySpreadAngle: 3.2f, knockbackForce: 0.65f, armorPenetration: 1, supportsModeSwitch: true, durabilityLossPerShot: 0.22f, repairCostPerPoint: 2),
                // 현재 펌프 산탄총은 한 번에 쉘 한 발만 소비하지만 16도 안에 피해 14인 펠릿 다섯 개를 발사합니다.
                // 다른 샷건을 추가할 때 아래 pelletCount와 spreadAngle 인수만 바꾸면 발사 수와 산탄도를 별도로 조정할 수 있습니다.
                CreateWeaponDefinition("shotgun", "펌프 산탄총", WeaponTriggerMode.SemiAutomatic, 14f, 14f, 0.85f, 2.3f, 5, AmmoType.TwelveGauge, 5, 16f, 1.5f, 0.35f, 1, durabilityLossPerShot: 0.3f, repairCostPerPoint: 3)
            };
        }

        // 기존 에셋이 있으면 GUID를 유지한 채 기본 전투 수치만 갱신하여 ItemDefinition과 프리팹 참조가 끊어지지 않게 합니다.
        private static WeaponDefinition CreateWeaponDefinition(
            string id,
            string displayName,
            WeaponTriggerMode triggerMode,
            float damage,
            float range,
            float fireInterval,
            float reloadDuration,
            int magazineSize,
            AmmoType ammoType,
            int pelletCount = 1,
            float spreadAngle = 0f,
            float accuracySpreadAngle = 1f,
            float knockbackForce = 1f,
            int armorPenetration = 0,
            bool supportsModeSwitch = false,
            float durabilityLossPerShot = 0.2f,
            int repairCostPerPoint = 2)
        {
            string path = Root + "/Data/Weapons/" + id + ".asset";
            WeaponDefinition definition = AssetDatabase.LoadAssetAtPath<WeaponDefinition>(path);
            if (definition == null)
            {
                definition = ScriptableObject.CreateInstance<WeaponDefinition>();
                AssetDatabase.CreateAsset(definition, path);
            }

            var serialized = new SerializedObject(definition);
            serialized.FindProperty("displayName").stringValue = displayName;
            serialized.FindProperty("triggerMode").enumValueIndex = (int)triggerMode;
            // 소총과 기관단총은 B키로 단발·연사를 전환하고, 권총과 펌프 산탄총은 단발 전용으로 유지합니다.
            serialized.FindProperty("supportsSemiAutomatic").boolValue = supportsModeSwitch || triggerMode == WeaponTriggerMode.SemiAutomatic;
            serialized.FindProperty("supportsAutomatic").boolValue = supportsModeSwitch || triggerMode == WeaponTriggerMode.Automatic;
            serialized.FindProperty("damage").floatValue = damage;
            serialized.FindProperty("range").floatValue = range;
            serialized.FindProperty("fireInterval").floatValue = fireInterval;
            serialized.FindProperty("reloadDuration").floatValue = reloadDuration;
            serialized.FindProperty("magazineSize").intValue = magazineSize;
            serialized.FindProperty("ammoType").enumValueIndex = (int)ammoType;
            // 일반 총기는 기본값 1발·0도를 사용하고, 산탄총만 여러 펠릿과 확산 각도를 에셋에 기록합니다.
            serialized.FindProperty("pelletCount").intValue = pelletCount;
            serialized.FindProperty("spreadAngle").floatValue = spreadAngle;
            // 정확도는 조준 중심의 무작위 오차, 밀림은 명중 대상에 더할 수평 속도입니다.
            serialized.FindProperty("accuracySpreadAngle").floatValue = accuracySpreadAngle;
            serialized.FindProperty("knockbackForce").floatValue = knockbackForce;
            // 관통력은 방어 등급과 비교되어 방탄복의 피해 감소율을 얼마나 무시할지 결정합니다.
            serialized.FindProperty("armorPenetration").intValue = Mathf.Max(0, armorPenetration);
            serialized.FindProperty("maxDurability").floatValue = 100f;
            serialized.FindProperty("durabilityLossPerShot").floatValue = Mathf.Max(0f, durabilityLossPerShot);
            serialized.FindProperty("wornAccuracySpreadMultiplier").floatValue = 2f;
            serialized.FindProperty("wornReloadDurationMultiplier").floatValue = 1.5f;
            serialized.FindProperty("repairCostPerPoint").intValue = Mathf.Max(0, repairCostPerPoint);
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(definition);
            return definition;
        }

        // 근접무기마다 피해·사거리·판정 폭·공격 속도를 별도 에셋으로 만듭니다.
        // 새 근접무기를 추가할 때 이 배열에 정의를 하나 넣고 아래 ItemDefinition만 연결하면 공통 공격 코드는 그대로 재사용됩니다.
        private static MeleeWeaponDefinition[] CreateMeleeWeaponDefinitions()
        {
            return new[]
            {
                // 단검은 짧고 좁은 찌르기지만 공격 간격이 짧아 한 대상을 빠르게 처리하는 보조무기입니다.
                CreateMeleeWeaponDefinition(
                    "dagger",
                    "전술 단검",
                    MeleeAttackStyle.Stab,
                    32f,
                    1.55f,
                    0.24f,
                    0.38f,
                    1,
                    1.3f,
                    1,
                    2.5f),
                // 도끼는 느리지만 넓은 판정과 높은 피해를 가지며 한 번의 휘두르기로 가까운 적 최대 세 명을 공격합니다.
                CreateMeleeWeaponDefinition(
                    "axe",
                    "소방 도끼",
                    MeleeAttackStyle.Swing,
                    58f,
                    1.9f,
                    0.68f,
                    0.86f,
                    3,
                    3.8f,
                    2,
                    5.5f)
            };
        }

        // 같은 경로의 에셋이 이미 있으면 GUID는 유지하고 기본 수치만 갱신해 아이템과 저장 참조가 끊어지지 않게 합니다.
        private static MeleeWeaponDefinition CreateMeleeWeaponDefinition(
            string id,
            string displayName,
            MeleeAttackStyle attackStyle,
            float damage,
            float range,
            float hitRadius,
            float attackInterval,
            int maximumTargets,
            float knockbackForce,
            int armorPenetration,
            float noiseRadius)
        {
            string path = Root + "/Data/Weapons/" + id + ".asset";
            MeleeWeaponDefinition definition = AssetDatabase.LoadAssetAtPath<MeleeWeaponDefinition>(path);
            if (definition == null)
            {
                definition = ScriptableObject.CreateInstance<MeleeWeaponDefinition>();
                AssetDatabase.CreateAsset(definition, path);
            }

            definition.Configure(
                displayName,
                attackStyle,
                damage,
                range,
                hitRadius,
                attackInterval,
                maximumTargets,
                knockbackForce,
                armorPenetration,
                noiseRadius);
            EditorUtility.SetDirty(definition);
            return definition;
        }

        // 같은 이동 계열의 에너지 음료와 아드레날린은 배율을 직접 비교하지 않고 우선순위 10·20으로 강약을 결정합니다.
        // 진통제는 별도 damage_guard 그룹이므로 이동 버프와 동시에 유지될 수 있습니다.
        private static StatusEffectDefinition[] CreateStatusEffectDefinitions()
        {
            return new[]
            {
                CreateStatusEffectDefinition(
                    "effect_energy_speed",
                    "각성",
                    StatusEffectDisposition.Buff,
                    "movement_boost",
                    10,
                    60f,
                    EffectStatToggle.MovementSpeed,
                    movementMultiplier: 1.15f),
                CreateStatusEffectDefinition(
                    "effect_adrenaline_speed",
                    "아드레날린",
                    StatusEffectDisposition.Buff,
                    "movement_boost",
                    20,
                    25f,
                    EffectStatToggle.MovementSpeed,
                    movementMultiplier: 1.3f),
                CreateStatusEffectDefinition(
                    "effect_painkiller_guard",
                    "통증 억제",
                    StatusEffectDisposition.Buff,
                    "damage_guard",
                    10,
                    45f,
                    EffectStatToggle.IncomingDamage,
                    incomingMultiplier: 0.75f)
            };
        }

        private static StatusEffectDefinition CreateStatusEffectDefinition(
            string id,
            string displayName,
            StatusEffectDisposition disposition,
            string groupId,
            int priority,
            float duration,
            EffectStatToggle affectedStats,
            float movementMultiplier = 1f,
            float incomingMultiplier = 1f,
            float outgoingMultiplier = 1f)
        {
            string path = Root + "/Data/Effects/" + id + ".asset";
            StatusEffectDefinition definition = AssetDatabase.LoadAssetAtPath<StatusEffectDefinition>(path);
            if (definition == null)
            {
                definition = ScriptableObject.CreateInstance<StatusEffectDefinition>();
                AssetDatabase.CreateAsset(definition, path);
            }
            definition.Configure(
                id,
                displayName,
                disposition,
                groupId,
                priority,
                duration,
                affectedStats,
                movementMultiplier,
                incomingMultiplier,
                outgoingMultiplier);
            EditorUtility.SetDirty(definition);
            return definition;
        }

        // 레이드에 배치할 모든 ItemDefinition을 고정 배열 순서로 생성합니다.
        // CreateRaidScene이 배열 인덱스로 아이템을 선택하므로 여기의 순서를 바꾸면 월드 배치 인덱스도 함께 수정해야 합니다.
        // ItemId는 저장용 고유 키이고 표시 이름과 설명은 UI에서 사용자에게 보여 줄 값입니다.
        private static ItemDefinition[] CreateItemDefinitions(
            WeaponDefinition[] weaponDefinitions,
            MeleeWeaponDefinition[] meleeWeaponDefinitions,
            StatusEffectDefinition[] statusEffects,
            WeaponAttachmentDefinition[] attachmentDefinitions)
        {
            return new[]
            {
                CreateItemDefinition("ammo_9mm", "9mm 탄약", ItemType.Ammo, 30, 1, 1, false, "9mm 권총용 탄약입니다.", ammoType: AmmoType.NineMillimeter, baseValue: 2),
                CreateItemDefinition("medkit_small", "소형 구급상자", ItemType.Healing, 3, 2, 1, true, "사용하면 체력을 35 회복하는 의료품입니다.", healthRestoreAmount: 35f, rarity: ItemRarity.Uncommon, baseValue: 80),
                CreateItemDefinition("food_canned", "통조림", ItemType.Food, 5, 1, 2, true, "사용하면 포만감을 50 회복하는 보존 식량입니다.", satietyRestoreAmount: 50f, baseValue: 25),
                CreateItemDefinition("quest_radio", "고장 난 무전기", ItemType.Quest, 1, 2, 2, false, "탈출 조건용 퀘스트 아이템입니다.", rarity: ItemRarity.Quest, baseValue: 250),
                CreateItemDefinition("weapon_rifle", "낡은 소총", ItemType.Weapon, 1, 5, 2, false, "자동 사격이 가능한 희귀 주무기입니다.", EquipmentSlotType.PrimaryWeapon, weaponDefinition: weaponDefinitions[0], rarity: ItemRarity.Rare, baseValue: 800),
                CreateItemDefinition("weapon_pistol", "권총", ItemType.Weapon, 1, 2, 1, false, "반자동 보조무기입니다.", EquipmentSlotType.SecondaryWeapon, weaponDefinition: weaponDefinitions[1], rarity: ItemRarity.Uncommon, baseValue: 300),
                CreateItemDefinition("armor_light", "경량 방탄복", ItemType.Armor, 1, 3, 3, false, "피해의 40%를 내구도로 흡수하고 퀵슬롯 두 칸을 추가하는 희귀 방탄복입니다.", EquipmentSlotType.Armor, armorDamageReduction: 0.4f, armorMaxDurability: 100f, quickSlotBonus: 2, rarity: ItemRarity.Rare, baseValue: 600),
                CreateItemDefinition("rig_small", "소형 전술 조끼", ItemType.Container, 1, 3, 2, false, "4x2 내부 격자를 제공합니다.", EquipmentSlotType.Rig, 4, 2, rarity: ItemRarity.Uncommon, baseValue: 250),
                CreateItemDefinition("backpack_small", "소형 가방", ItemType.Container, 1, 4, 4, false, "5x4 내부 격자를 제공합니다.", EquipmentSlotType.Backpack, 5, 4, rarity: ItemRarity.Uncommon, baseValue: 400),
                // 기존 0~8번 인덱스는 씬 배치 코드와 테스트가 사용하므로 새 항목은 항상 배열 마지막에 추가합니다.
                CreateItemDefinition("ammo_556", "5.56mm 탄약", ItemType.Ammo, 30, 1, 1, false, "5.56mm 소총용 탄약입니다.", ammoType: AmmoType.FiveFiveSix, rarity: ItemRarity.Uncommon, baseValue: 3),
                CreateItemDefinition("ammo_12g", "12게이지 산탄", ItemType.Ammo, 20, 1, 1, false, "산탄총에 사용하는 12게이지 쉘입니다.", ammoType: AmmoType.TwelveGauge, rarity: ItemRarity.Uncommon, baseValue: 5),
                CreateItemDefinition("weapon_smg", "9mm 기관단총", ItemType.Weapon, 1, 4, 2, false, "근거리에서 빠르게 연사하는 희귀 9mm 주무기입니다.", EquipmentSlotType.PrimaryWeapon, weaponDefinition: weaponDefinitions[2], rarity: ItemRarity.Rare, baseValue: 650),
                CreateItemDefinition("weapon_shotgun", "펌프 산탄총", ItemType.Weapon, 1, 5, 2, false, "가까운 거리에서 여러 펠릿으로 큰 피해를 주는 희귀 주무기입니다.", EquipmentSlotType.PrimaryWeapon, weaponDefinition: weaponDefinitions[3], rarity: ItemRarity.Rare, baseValue: 700),
                // 시설 강화와 제작 순환에 사용하는 기본 재료입니다. 기존 0~12 인덱스 뒤에 추가해 레이드 배치 참조를 보호합니다.
                CreateItemDefinition("material_scrap", "고철", ItemType.Material, 20, 1, 1, false, "거점 시설과 탄약 제작에 사용하는 금속 재료입니다.", baseValue: 15),
                CreateItemDefinition("material_electronics", "전자 부품", ItemType.Material, 10, 1, 1, false, "발전기와 고급 시설 강화에 사용하는 전자 재료입니다.", rarity: ItemRarity.Uncommon, baseValue: 45),
                CreateItemDefinition("material_cloth", "천 조각", ItemType.Material, 20, 1, 1, false, "의료품 제작과 치료소 강화에 사용하는 재료입니다.", baseValue: 10),
                // 출혈과 골절 치료를 분리해 전투 중 어떤 상태를 먼저 해결할지 선택하게 합니다.
                CreateItemDefinition("medical_bandage", "붕대", ItemType.Healing, 5, 1, 1, true, "출혈을 멈추는 일회용 붕대입니다.", treatsBleeding: true, baseValue: 35),
                CreateItemDefinition("medical_splint", "부목", ItemType.Healing, 3, 1, 2, true, "골절을 치료해 이동 속도를 정상으로 되돌립니다.", treatsFracture: true, rarity: ItemRarity.Uncommon, baseValue: 55),
                // 부착물 아이템의 격자 크기는 실제 보관 크기이고, 성능과 장착 부위는 별도 Definition에 보관합니다.
                CreateItemDefinition("attachment_reflex", "반사 조준경", ItemType.Attachment, 1, 2, 1, false, "조준 오차를 줄이는 범용 조준경입니다.", weaponAttachmentDefinition: attachmentDefinitions[0], rarity: ItemRarity.Uncommon, baseValue: 180),
                CreateItemDefinition("attachment_compensator", "총구 보정기", ItemType.Attachment, 1, 2, 1, false, "반동과 탄 퍼짐을 줄이는 범용 총구 부착물입니다.", weaponAttachmentDefinition: attachmentDefinitions[1], rarity: ItemRarity.Uncommon, baseValue: 240),
                CreateItemDefinition("attachment_mag_9mm", "9mm 확장 탄창", ItemType.Attachment, 1, 2, 1, false, "9mm 무기의 탄창을 10발 늘리지만 재장전 시간이 조금 길어집니다.", weaponAttachmentDefinition: attachmentDefinitions[2], rarity: ItemRarity.Rare, baseValue: 220),
                CreateItemDefinition("attachment_mag_556", "5.56mm 확장 탄창", ItemType.Attachment, 1, 2, 1, false, "5.56mm 소총 탄창을 10발 늘리지만 재장전 시간이 조금 길어집니다.", weaponAttachmentDefinition: attachmentDefinitions[3], rarity: ItemRarity.Rare, baseValue: 280),
                // 단검은 보조무기 슬롯, 도끼는 주무기 슬롯을 사용합니다. 전투 수치는 별도 MeleeWeaponDefinition에서 읽습니다.
                CreateItemDefinition("weapon_dagger", "전술 단검", ItemType.Weapon, 1, 2, 1, false, "빠르고 조용한 찌르기로 한 대상을 공격하는 근접 보조무기입니다.", EquipmentSlotType.SecondaryWeapon, meleeWeaponDefinition: meleeWeaponDefinitions[0], rarity: ItemRarity.Uncommon, baseValue: 220),
                CreateItemDefinition("weapon_axe", "소방 도끼", ItemType.Weapon, 1, 2, 3, false, "느리지만 강하고 넓은 휘두르기로 여러 대상을 공격하는 근접 주무기입니다.", EquipmentSlotType.PrimaryWeapon, meleeWeaponDefinition: meleeWeaponDefinitions[1], rarity: ItemRarity.Rare, baseValue: 480),
                // 같은 movement_boost 그룹에서 아드레날린 우선순위가 더 높으므로 에너지 음료 효과를 교체하며, 반대 순서는 소비되지 않습니다.
                CreateItemDefinition("buff_energy_drink", "각성 에너지 음료", ItemType.BuffConsumable, 5, 1, 2, true, "60초 동안 이동 속도를 15% 높입니다.", statusEffectDefinition: statusEffects[0], baseValue: 65),
                CreateItemDefinition("buff_adrenaline", "아드레날린 주사기", ItemType.BuffConsumable, 3, 1, 2, true, "25초 동안 이동 속도를 30% 높이는 강한 이동 버프입니다.", statusEffectDefinition: statusEffects[1], rarity: ItemRarity.Uncommon, baseValue: 130),
                CreateItemDefinition("buff_painkiller", "강화 진통제", ItemType.BuffConsumable, 3, 1, 1, true, "45초 동안 받는 피해를 25% 줄입니다.", statusEffectDefinition: statusEffects[2], rarity: ItemRarity.Uncommon, baseValue: 110)
            };
        }

        // 부착물 성능을 아이템과 분리된 ScriptableObject로 만들면 같은 부착물을 상점, 드롭, 저장 데이터에서 안전하게 공유할 수 있습니다.
        private static WeaponAttachmentDefinition[] CreateWeaponAttachmentDefinitions()
        {
            return new[]
            {
                CreateWeaponAttachmentDefinition("optic_reflex", "반사 조준경", WeaponAttachmentSlot.Optic, AmmoType.None, 1f, 1f, 0.65f, 1f, 0),
                CreateWeaponAttachmentDefinition("muzzle_compensator", "총구 보정기", WeaponAttachmentSlot.Muzzle, AmmoType.None, 1f, 1f, 0.82f, 1f, 0),
                CreateWeaponAttachmentDefinition("mag_9mm_extended", "9mm 확장 탄창", WeaponAttachmentSlot.Magazine, AmmoType.NineMillimeter, 1f, 1f, 1f, 1.15f, 10),
                CreateWeaponAttachmentDefinition("mag_556_extended", "5.56mm 확장 탄창", WeaponAttachmentSlot.Magazine, AmmoType.FiveFiveSix, 1f, 1f, 1f, 1.18f, 10)
            };
        }

        // 기존 부착물 에셋은 GUID를 유지하고 값만 갱신하므로 저장된 ItemDefinition 참조가 재생성 뒤에도 끊기지 않습니다.
        private static WeaponAttachmentDefinition CreateWeaponAttachmentDefinition(
            string id,
            string displayName,
            WeaponAttachmentSlot slot,
            AmmoType compatibleAmmoType,
            float damageMultiplier,
            float fireIntervalMultiplier,
            float accuracyMultiplier,
            float reloadMultiplier,
            int magazineCapacityBonus)
        {
            string path = Root + "/Data/Weapons/" + id + ".asset";
            WeaponAttachmentDefinition definition = AssetDatabase.LoadAssetAtPath<WeaponAttachmentDefinition>(path);
            if (definition == null)
            {
                definition = ScriptableObject.CreateInstance<WeaponAttachmentDefinition>();
                AssetDatabase.CreateAsset(definition, path);
            }

            definition.Configure(
                displayName,
                slot,
                compatibleAmmoType,
                damageMultiplier,
                fireIntervalMultiplier,
                accuracyMultiplier,
                reloadMultiplier,
                magazineCapacityBonus);
            EditorUtility.SetDirty(definition);
            return definition;
        }

        // 저장된 ItemId를 런타임 ItemDefinition으로 복원할 수 있도록 모든 기본 정의를 하나의 카탈로그 에셋에 연결합니다.
        // 기존 에셋이 있으면 GUID를 유지한 채 배열만 갱신하므로 Hideout과 Raid 씬의 참조가 Rebuild 뒤에도 끊어지지 않습니다.
        private static ItemCatalog CreateItemCatalog(ItemDefinition[] itemDefinitions)
        {
            string path = Root + "/Data/Items/item_catalog.asset";
            ItemCatalog catalog = AssetDatabase.LoadAssetAtPath<ItemCatalog>(path);
            if (catalog == null)
            {
                catalog = ScriptableObject.CreateInstance<ItemCatalog>();
                AssetDatabase.CreateAsset(catalog, path);
            }

            var serialized = new SerializedObject(catalog);
            SerializedProperty definitions = serialized.FindProperty("definitions");
            definitions.arraySize = itemDefinitions.Length;
            for (int i = 0; i < itemDefinitions.Length; i++)
                definitions.GetArrayElementAtIndex(i).objectReferenceValue = itemDefinitions[i];
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(catalog);
            return catalog;
        }

        // 필드에 배치할 보급 상자, 무기 상자와 은닉물의 서로 다른 전리품 표를 생성합니다.
        // 반환 순서는 0 보급, 1 무기, 2 은닉물이며 CreateRaidScene의 상자 배치 코드가 같은 순서를 사용합니다.
        private static LootTableDefinition[] CreateLootTables(ItemDefinition[] itemDefinitions)
        {
            return new[]
            {
                CreateLootTable(
                    "supply_crate",
                    "생활 보급 상자",
                    2,
                    4,
                    new LootTableEntry { item = itemDefinitions[0], weight = 4f, minimumQuantity = 5, maximumQuantity = 12 },
                    new LootTableEntry { item = itemDefinitions[10], weight = 1.2f, minimumQuantity = 2, maximumQuantity = 6 },
                    new LootTableEntry { item = itemDefinitions[1], weight = 2f, minimumQuantity = 1, maximumQuantity = 1 },
                    new LootTableEntry { item = itemDefinitions[2], weight = 3f, minimumQuantity = 1, maximumQuantity = 2 },
                    new LootTableEntry { item = itemDefinitions[13], weight = 3.2f, minimumQuantity = 2, maximumQuantity = 5 },
                    new LootTableEntry { item = itemDefinitions[15], weight = 2.4f, minimumQuantity = 1, maximumQuantity = 4 },
                    new LootTableEntry { item = itemDefinitions[14], weight = 0.65f, minimumQuantity = 1, maximumQuantity = 2 },
                    new LootTableEntry { item = itemDefinitions[16], weight = 1.6f, minimumQuantity = 1, maximumQuantity = 2 },
                    new LootTableEntry { item = itemDefinitions[17], weight = 0.8f, minimumQuantity = 1, maximumQuantity = 1 },
                    new LootTableEntry { item = itemDefinitions[24], weight = 1.5f, minimumQuantity = 1, maximumQuantity = 2 },
                    new LootTableEntry { item = itemDefinitions[26], weight = 0.75f, minimumQuantity = 1, maximumQuantity = 1 },
                    new LootTableEntry { item = itemDefinitions[8], weight = 0.35f, minimumQuantity = 1, maximumQuantity = 1 }),
                CreateLootTable(
                    "weapon_crate",
                    "무기 상자",
                    2,
                    4,
                    new LootTableEntry { item = itemDefinitions[9], weight = 5f, minimumQuantity = 6, maximumQuantity = 15 },
                    new LootTableEntry { item = itemDefinitions[10], weight = 3f, minimumQuantity = 3, maximumQuantity = 8 },
                    new LootTableEntry { item = itemDefinitions[5], weight = 1.2f, minimumQuantity = 1, maximumQuantity = 1 },
                    new LootTableEntry { item = itemDefinitions[4], weight = 0.6f, minimumQuantity = 1, maximumQuantity = 1 },
                    new LootTableEntry { item = itemDefinitions[11], weight = 0.8f, minimumQuantity = 1, maximumQuantity = 1 },
                    new LootTableEntry { item = itemDefinitions[12], weight = 0.65f, minimumQuantity = 1, maximumQuantity = 1 },
                    new LootTableEntry { item = itemDefinitions[13], weight = 3.5f, minimumQuantity = 2, maximumQuantity = 5 },
                    new LootTableEntry { item = itemDefinitions[14], weight = 1.4f, minimumQuantity = 1, maximumQuantity = 2 },
                    new LootTableEntry { item = itemDefinitions[18], weight = 0.7f, minimumQuantity = 1, maximumQuantity = 1 },
                    new LootTableEntry { item = itemDefinitions[19], weight = 0.45f, minimumQuantity = 1, maximumQuantity = 1 },
                    new LootTableEntry { item = itemDefinitions[20], weight = 0.4f, minimumQuantity = 1, maximumQuantity = 1 },
                    new LootTableEntry { item = itemDefinitions[21], weight = 0.3f, minimumQuantity = 1, maximumQuantity = 1 },
                    new LootTableEntry { item = itemDefinitions[25], weight = 0.5f, minimumQuantity = 1, maximumQuantity = 1 },
                    new LootTableEntry { item = itemDefinitions[22], weight = 1.1f, minimumQuantity = 1, maximumQuantity = 1 },
                    new LootTableEntry { item = itemDefinitions[23], weight = 0.55f, minimumQuantity = 1, maximumQuantity = 1 },
                    new LootTableEntry { item = itemDefinitions[6], weight = 0.8f, minimumQuantity = 1, maximumQuantity = 1 }),
                CreateLootTable(
                    "hidden_stash",
                    "은닉 보급품",
                    2,
                    3,
                    new LootTableEntry { item = itemDefinitions[3], weight = 1.2f, minimumQuantity = 1, maximumQuantity = 1 },
                    new LootTableEntry { item = itemDefinitions[0], weight = 3f, minimumQuantity = 5, maximumQuantity = 12 },
                    new LootTableEntry { item = itemDefinitions[10], weight = 1.5f, minimumQuantity = 2, maximumQuantity = 5 },
                    new LootTableEntry { item = itemDefinitions[1], weight = 2f, minimumQuantity = 1, maximumQuantity = 1 },
                    new LootTableEntry { item = itemDefinitions[2], weight = 3f, minimumQuantity = 1, maximumQuantity = 2 },
                    new LootTableEntry { item = itemDefinitions[13], weight = 2.6f, minimumQuantity = 2, maximumQuantity = 6 },
                    new LootTableEntry { item = itemDefinitions[14], weight = 1.1f, minimumQuantity = 1, maximumQuantity = 3 },
                    new LootTableEntry { item = itemDefinitions[15], weight = 2.8f, minimumQuantity = 2, maximumQuantity = 6 },
                    new LootTableEntry { item = itemDefinitions[16], weight = 1.2f, minimumQuantity = 1, maximumQuantity = 2 },
                    new LootTableEntry { item = itemDefinitions[17], weight = 0.65f, minimumQuantity = 1, maximumQuantity = 1 },
                    new LootTableEntry { item = itemDefinitions[18], weight = 0.4f, minimumQuantity = 1, maximumQuantity = 1 },
                    new LootTableEntry { item = itemDefinitions[19], weight = 0.25f, minimumQuantity = 1, maximumQuantity = 1 },
                    new LootTableEntry { item = itemDefinitions[24], weight = 1.1f, minimumQuantity = 1, maximumQuantity = 1 },
                    new LootTableEntry { item = itemDefinitions[25], weight = 0.38f, minimumQuantity = 1, maximumQuantity = 1 },
                    new LootTableEntry { item = itemDefinitions[26], weight = 0.65f, minimumQuantity = 1, maximumQuantity = 1 },
                    new LootTableEntry { item = itemDefinitions[7], weight = 0.5f, minimumQuantity = 1, maximumQuantity = 1 })
            };
        }

        // 같은 ID의 전리품 표가 있으면 GUID를 유지한 채 기본 추첨 횟수와 후보 목록만 갱신합니다.
        // 씬의 FieldLootSource는 이 에셋을 참조하므로 Rebuild 뒤에도 연결이 유지됩니다.
        private static LootTableDefinition CreateLootTable(
            string id,
            string displayName,
            int minimumRolls,
            int maximumRolls,
            params LootTableEntry[] entries)
        {
            string path = Root + "/Data/Loot/" + id + ".asset";
            LootTableDefinition table = AssetDatabase.LoadAssetAtPath<LootTableDefinition>(path);
            if (table == null)
            {
                table = ScriptableObject.CreateInstance<LootTableDefinition>();
                AssetDatabase.CreateAsset(table, path);
            }

            table.Configure(displayName, minimumRolls, maximumRolls, entries);
            EditorUtility.SetDirty(table);
            return table;
        }

        // 같은 ID 에셋이 없으면 새 ScriptableObject를 만들고, 있으면 기존 GUID를 유지한 채 기본값만 갱신합니다.
        // private SerializeField는 일반 코드에서 접근할 수 없으므로 Editor API인 SerializedObject로 Inspector와 같은 방식으로 기록합니다.
        // 기존 GUID를 유지하면 프리팹과 씬이 가진 ItemDefinition 참조가 재생성 뒤에도 끊어지지 않습니다.
        private static ItemDefinition CreateItemDefinition(
            string id,
            string displayName,
            ItemType type,
            int maxStack,
            int gridWidth,
            int gridHeight,
            bool usable,
            string description,
            EquipmentSlotType equipmentSlot = EquipmentSlotType.None,
            int containerWidth = 0,
            int containerHeight = 0,
            WeaponDefinition weaponDefinition = null,
            MeleeWeaponDefinition meleeWeaponDefinition = null,
            StatusEffectDefinition statusEffectDefinition = null,
            AmmoType ammoType = AmmoType.None,
            float armorDamageReduction = 0f,
            float armorMaxDurability = 0f,
            int quickSlotBonus = 0,
            float healthRestoreAmount = 0f,
            float satietyRestoreAmount = 0f,
            ItemRarity rarity = ItemRarity.Common,
            int baseValue = 10,
            WeaponAttachmentDefinition weaponAttachmentDefinition = null,
            ArmorClass armorClass = ArmorClass.Soft,
            bool treatsBleeding = false,
            bool treatsFracture = false)
        {
            string path = Root + "/Data/Items/" + id + ".asset";
            ItemDefinition definition = AssetDatabase.LoadAssetAtPath<ItemDefinition>(path);
            if (definition == null)
            {
                definition = ScriptableObject.CreateInstance<ItemDefinition>();
                AssetDatabase.CreateAsset(definition, path);
            }

            var serialized = new SerializedObject(definition);
            serialized.FindProperty("itemId").stringValue = id;
            serialized.FindProperty("displayName").stringValue = displayName;
            serialized.FindProperty("itemType").enumValueIndex = (int)type;
            serialized.FindProperty("rarity").enumValueIndex = (int)rarity;
            serialized.FindProperty("ammoType").enumValueIndex = (int)ammoType;
            serialized.FindProperty("maxStack").intValue = maxStack;
            serialized.FindProperty("gridWidth").intValue = gridWidth;
            serialized.FindProperty("gridHeight").intValue = gridHeight;
            serialized.FindProperty("usable").boolValue = usable;
            serialized.FindProperty("description").stringValue = description;
            serialized.FindProperty("equipmentSlot").enumValueIndex = (int)equipmentSlot;
            serialized.FindProperty("weaponDefinition").objectReferenceValue = weaponDefinition;
            // 총기 아이템은 weaponDefinition만, 단검·도끼는 meleeWeaponDefinition만 연결합니다.
            // 둘을 분리하면 근접무기에 불필요한 탄창·탄종 상태가 생기지 않습니다.
            serialized.FindProperty("meleeWeaponDefinition").objectReferenceValue = meleeWeaponDefinition;
            serialized.FindProperty("statusEffectDefinition").objectReferenceValue = statusEffectDefinition;
            serialized.FindProperty("weaponAttachmentDefinition").objectReferenceValue = weaponAttachmentDefinition;
            serialized.FindProperty("armorDamageReduction").floatValue = armorDamageReduction;
            serialized.FindProperty("armorMaxDurability").floatValue = armorMaxDurability;
            serialized.FindProperty("armorClass").enumValueIndex = (int)armorClass;
            serialized.FindProperty("quickSlotBonus").intValue = Mathf.Clamp(quickSlotBonus, 0, 8);
            serialized.FindProperty("containerWidth").intValue = containerWidth;
            serialized.FindProperty("containerHeight").intValue = containerHeight;
            // 의료품과 음식만 각자 사용하는 회복량이며, 다른 아이템은 기본값 0으로 기록됩니다.
            serialized.FindProperty("healthRestoreAmount").floatValue = healthRestoreAmount;
            serialized.FindProperty("satietyRestoreAmount").floatValue = satietyRestoreAmount;
            serialized.FindProperty("treatsBleeding").boolValue = treatsBleeding;
            serialized.FindProperty("treatsFracture").boolValue = treatsFracture;
            serialized.FindProperty("baseValue").intValue = Mathf.Max(0, baseValue);
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(definition);
            return definition;
        }

        // 첫 테스트 탈출은 quest_radio 아이템 한 개를 요구하도록 별도 ScriptableObject 에셋으로 정의합니다.
        // 조건을 씬 컴포넌트에 직접 문자열로 반복해서 넣지 않아 여러 탈출 지점이 같은 규칙을 공유할 수 있습니다.
        private static ExtractionRequirement CreateExtractionRequirement()
        {
            string path = Root + "/Data/Extraction/quest_radio_extraction.asset";
            ExtractionRequirement requirement = AssetDatabase.LoadAssetAtPath<ExtractionRequirement>(path);
            if (requirement == null)
            {
                requirement = ScriptableObject.CreateInstance<ExtractionRequirement>();
                AssetDatabase.CreateAsset(requirement, path);
            }

            var serialized = new SerializedObject(requirement);
            serialized.FindProperty("requiredItemId").stringValue = "quest_radio";
            serialized.FindProperty("displayName").stringValue = "고장 난 무전기";
            serialized.FindProperty("requiredQuantity").intValue = 1;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(requirement);
            return requirement;
        }

        // 기본 레이드는 제한 시간 없이 좀비 3마리를 처치하고 시작 후 25초가 지난 뒤 탈출할 수 있도록 설정합니다.
        private static RaidRules CreateRaidRules()
        {
            string path = Root + "/Data/Raid/default_raid_rules.asset";
            RaidRules rules = AssetDatabase.LoadAssetAtPath<RaidRules>(path);
            if (rules == null)
            {
                rules = ScriptableObject.CreateInstance<RaidRules>();
                AssetDatabase.CreateAsset(rules, path);
            }

            var serialized = new SerializedObject(rules);
            // 자동 시간 초과는 없지만 오래 머물수록 스폰 난이도가 상승하므로 귀환 시점은 플레이어가 위험을 보고 선택합니다.
            serialized.FindProperty("requiredZombieKills").intValue = 3;
            serialized.FindProperty("minimumExtractionTimeSeconds").floatValue = 25f;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(rules);
            return rules;
        }

        // 출격 가능한 실제 플레이 지역을 ScriptableObject로 만듭니다.
        // 현재는 임시 학교 한 곳만 등록하고, 공장·숲은 지형과 난이도 규칙이 확정된 뒤 같은 배열에 추가합니다.
        private static RaidRegionDefinition[] CreateRaidRegionDefinitions()
        {
            const string path = Root + "/Data/Raid/region_school.asset";
            RaidRegionDefinition school = AssetDatabase.LoadAssetAtPath<RaidRegionDefinition>(path);
            if (school == null)
            {
                school = ScriptableObject.CreateInstance<RaidRegionDefinition>();
                AssetDatabase.CreateAsset(school, path);
            }

            school.Configure(
                "school",
                "폐쇄된 학교",
                GameSceneNavigator.SchoolRaidSceneName,
                "정문에서 시작해 행정동, 2층 교실동, 특별실·도서관, 급식실과 체육관을 수색한 뒤 운동장 비상문으로 탈출합니다. " +
                "구역마다 위험도와 전리품 종류가 다르며 체육관과 교실 2층은 고위험·고보상 구역입니다.",
                2,
                true);
            EditorUtility.SetDirty(school);
            return new[] { school };
        }

        // 학교 한 지역에서 상자 종류, 바닥 생성 지점과 좀비 유형별 드랍 수치를 모두 조절하는 데이터 에셋을 만듭니다.
        // 한 아이템을 특정 출처 배열에만 넣으면 "상자 전용", "바닥 전용", "몬스터 전용"처럼 코드 수정 없이 출처를 제한할 수 있습니다.
        private static RegionLootProfile CreateSchoolRegionLootProfile(
            RaidRegionDefinition[] raidRegions,
            ItemCatalog itemCatalog)
        {
            RaidRegionDefinition schoolRegion = null;
            if (raidRegions != null)
            {
                for (int i = 0; i < raidRegions.Length; i++)
                {
                    if (raidRegions[i] != null && raidRegions[i].RegionId == "school")
                    {
                        schoolRegion = raidRegions[i];
                        break;
                    }
                }
            }

            const string path = Root + "/Data/RegionalLoot/region_school_loot.asset";
            RegionLootProfile profile = AssetDatabase.LoadAssetAtPath<RegionLootProfile>(path);
            if (profile == null)
            {
                profile = ScriptableObject.CreateInstance<RegionLootProfile>();
                AssetDatabase.CreateAsset(profile, path);
            }

            // Container 표는 추첨 횟수만큼 후보 하나를 가중치로 고른 뒤 그 후보의 dropChance를 굴립니다.
            // 부착물 네 종류는 weapon_crate에만 넣어 현재 기본 밸런스에서 "아이템 상자 전용" 예시로 사용합니다.
            var supplyCrate = new RegionLootSourceTable(
                RegionLootSourceType.Container,
                "supply_crate",
                2,
                4,
                CreateRegionLootEntry(itemCatalog, "ammo_9mm", 0.95f, 4f, 5, 12),
                CreateRegionLootEntry(itemCatalog, "ammo_12g", 0.72f, 1.2f, 2, 6),
                CreateRegionLootEntry(itemCatalog, "medkit_small", 0.62f, 2f, 1, 1),
                CreateRegionLootEntry(itemCatalog, "food_canned", 0.88f, 3f, 1, 2),
                CreateRegionLootEntry(itemCatalog, "material_scrap", 0.92f, 3.2f, 2, 5),
                CreateRegionLootEntry(itemCatalog, "material_electronics", 0.42f, 0.65f, 1, 2),
                CreateRegionLootEntry(itemCatalog, "medical_bandage", 0.74f, 1.6f, 1, 2),
                CreateRegionLootEntry(itemCatalog, "medical_splint", 0.38f, 0.8f, 1, 1),
                CreateRegionLootEntry(itemCatalog, "buff_energy_drink", 0.58f, 1.5f, 1, 2),
                CreateRegionLootEntry(itemCatalog, "buff_painkiller", 0.30f, 0.75f, 1, 1),
                CreateRegionLootEntry(itemCatalog, "backpack_small", 0.14f, 0.35f, 1, 1));

            var weaponCrate = new RegionLootSourceTable(
                RegionLootSourceType.Container,
                "weapon_crate",
                2,
                4,
                CreateRegionLootEntry(itemCatalog, "ammo_556", 0.95f, 5f, 6, 15),
                CreateRegionLootEntry(itemCatalog, "ammo_12g", 0.82f, 3f, 3, 8),
                CreateRegionLootEntry(itemCatalog, "weapon_pistol", 0.48f, 1.2f, 1, 1),
                CreateRegionLootEntry(itemCatalog, "weapon_rifle", 0.20f, 0.6f, 1, 1),
                CreateRegionLootEntry(itemCatalog, "weapon_smg", 0.26f, 0.8f, 1, 1),
                CreateRegionLootEntry(itemCatalog, "weapon_shotgun", 0.23f, 0.65f, 1, 1),
                CreateRegionLootEntry(itemCatalog, "weapon_dagger", 0.42f, 1.1f, 1, 1),
                CreateRegionLootEntry(itemCatalog, "weapon_axe", 0.20f, 0.55f, 1, 1),
                CreateRegionLootEntry(itemCatalog, "buff_adrenaline", 0.18f, 0.5f, 1, 1),
                CreateRegionLootEntry(itemCatalog, "armor_light", 0.18f, 0.8f, 1, 1),
                CreateRegionLootEntry(itemCatalog, "attachment_reflex", 0.32f, 0.7f, 1, 1),
                CreateRegionLootEntry(itemCatalog, "attachment_compensator", 0.24f, 0.45f, 1, 1),
                CreateRegionLootEntry(itemCatalog, "attachment_mag_9mm", 0.18f, 0.3f, 1, 1),
                CreateRegionLootEntry(itemCatalog, "attachment_mag_556", 0.16f, 0.4f, 1, 1));

            var hiddenStash = new RegionLootSourceTable(
                RegionLootSourceType.Container,
                "hidden_stash",
                2,
                3,
                CreateRegionLootEntry(itemCatalog, "ammo_9mm", 0.92f, 3f, 5, 12),
                CreateRegionLootEntry(itemCatalog, "ammo_12g", 0.72f, 1.5f, 2, 5),
                CreateRegionLootEntry(itemCatalog, "medkit_small", 0.68f, 2f, 1, 1),
                CreateRegionLootEntry(itemCatalog, "food_canned", 0.90f, 3f, 1, 2),
                CreateRegionLootEntry(itemCatalog, "material_scrap", 0.90f, 2.6f, 2, 6),
                CreateRegionLootEntry(itemCatalog, "material_electronics", 0.54f, 1.1f, 1, 3),
                CreateRegionLootEntry(itemCatalog, "medical_bandage", 0.72f, 1.2f, 1, 2),
                CreateRegionLootEntry(itemCatalog, "medical_splint", 0.34f, 0.65f, 1, 1),
                CreateRegionLootEntry(itemCatalog, "buff_energy_drink", 0.46f, 1.1f, 1, 1),
                CreateRegionLootEntry(itemCatalog, "buff_adrenaline", 0.14f, 0.38f, 1, 1),
                CreateRegionLootEntry(itemCatalog, "buff_painkiller", 0.28f, 0.65f, 1, 1),
                CreateRegionLootEntry(itemCatalog, "rig_small", 0.18f, 0.5f, 1, 1));

            // Ground 표는 학교 곳곳의 빈 생성 지점마다 후보 한 개를 고릅니다.
            // quest_radio는 이 표에만 있으므로 현재 학교 밸런스에서 "바닥 전용"이며, 관련 임무 중 확률이 5%p 자동 상승합니다.
            var schoolFloor = new RegionLootSourceTable(
                RegionLootSourceType.Ground,
                "school_floor",
                1,
                1,
                CreateRegionLootEntry(itemCatalog, "ammo_9mm", 0.92f, 4f, 4, 10),
                CreateRegionLootEntry(itemCatalog, "ammo_556", 0.72f, 2.4f, 4, 10),
                CreateRegionLootEntry(itemCatalog, "ammo_12g", 0.66f, 1.8f, 2, 5),
                CreateRegionLootEntry(itemCatalog, "medkit_small", 0.44f, 1.4f, 1, 1),
                CreateRegionLootEntry(itemCatalog, "food_canned", 0.78f, 2.8f, 1, 2),
                CreateRegionLootEntry(itemCatalog, "material_scrap", 0.82f, 3.2f, 1, 4),
                CreateRegionLootEntry(itemCatalog, "quest_radio", 0.12f, 0.8f, 1, 1),
                CreateRegionLootEntry(itemCatalog, "weapon_pistol", 0.18f, 0.5f, 1, 1),
                CreateRegionLootEntry(itemCatalog, "weapon_rifle", 0.08f, 0.18f, 1, 1),
                CreateRegionLootEntry(itemCatalog, "weapon_smg", 0.09f, 0.22f, 1, 1),
                CreateRegionLootEntry(itemCatalog, "weapon_shotgun", 0.08f, 0.2f, 1, 1));

            // Monster 표는 후보 선택 없이 각 줄의 확률을 독립적으로 굴리므로 한 시체에 여러 품목이 함께 들어갈 수 있습니다.
            // material_cloth는 네 몬스터 표에만 넣어 현재 학교 밸런스에서 "몬스터 전용" 재료로 구성했습니다.
            var walker = new RegionLootSourceTable(
                RegionLootSourceType.Monster,
                "walker",
                1,
                1,
                CreateRegionLootEntry(itemCatalog, "ammo_9mm", 0.46f, 1f, 2, 6),
                CreateRegionLootEntry(itemCatalog, "food_canned", 0.14f, 1f, 1, 1),
                CreateRegionLootEntry(itemCatalog, "material_cloth", 0.36f, 1f, 1, 3));
            var runner = new RegionLootSourceTable(
                RegionLootSourceType.Monster,
                "runner",
                1,
                1,
                CreateRegionLootEntry(itemCatalog, "ammo_9mm", 0.55f, 1f, 3, 8),
                CreateRegionLootEntry(itemCatalog, "medical_bandage", 0.24f, 1f, 1, 1),
                CreateRegionLootEntry(itemCatalog, "material_cloth", 0.48f, 1f, 1, 4));
            var brute = new RegionLootSourceTable(
                RegionLootSourceType.Monster,
                "brute",
                1,
                1,
                CreateRegionLootEntry(itemCatalog, "ammo_12g", 0.52f, 1f, 2, 6),
                CreateRegionLootEntry(itemCatalog, "material_scrap", 0.76f, 1f, 2, 5),
                CreateRegionLootEntry(itemCatalog, "material_cloth", 0.30f, 1f, 2, 4),
                CreateRegionLootEntry(itemCatalog, "armor_light", 0.08f, 1f, 1, 1));
            var screamer = new RegionLootSourceTable(
                RegionLootSourceType.Monster,
                "screamer",
                1,
                1,
                CreateRegionLootEntry(itemCatalog, "material_electronics", 0.56f, 1f, 1, 3),
                CreateRegionLootEntry(itemCatalog, "material_cloth", 0.42f, 1f, 1, 3),
                CreateRegionLootEntry(itemCatalog, "medical_splint", 0.20f, 1f, 1, 1));

            profile.Configure(
                schoolRegion,
                0.05f,
                supplyCrate,
                weaponCrate,
                hiddenStash,
                schoolFloor,
                walker,
                runner,
                brute,
                screamer);
            EditorUtility.SetDirty(profile);
            return profile;
        }

        // 잘못 적은 ItemId가 조용히 누락되지 않도록 생성 시 Console에 정확한 ID를 남깁니다.
        private static RegionLootEntry CreateRegionLootEntry(
            ItemCatalog itemCatalog,
            string itemId,
            float chance,
            float weight,
            int minimumQuantity,
            int maximumQuantity)
        {
            ItemDefinition item = itemCatalog?.Find(itemId);
            if (item == null) Debug.LogError($"지역 드랍 항목 생성 실패: ItemCatalog에서 '{itemId}'를 찾지 못했습니다.");
            return new RegionLootEntry(item, chance, weight, minimumQuantity, maximumQuantity);
        }

        // 전체 화면 로딩 원화를 Sprite로 가져오고 공용 이미지와 지역 해금 이미지를 하나의 데이터 에셋에 연결합니다.
        // 지역이 늘어나면 해당 RaidRegionDefinition과 Sprite 배열을 LoadingRegionImagePool에 한 줄 추가하면 같은 HUD가 자동으로 후보를 확장합니다.
        private static LoadingScreenCatalog CreateLoadingScreenCatalog(RaidRegionDefinition[] raidRegions)
        {
            const string path = Root + "/Data/UI/loading_screen_catalog.asset";
            LoadingScreenCatalog catalog = AssetDatabase.LoadAssetAtPath<LoadingScreenCatalog>(path);
            if (catalog == null)
            {
                catalog = ScriptableObject.CreateInstance<LoadingScreenCatalog>();
                AssetDatabase.CreateAsset(catalog, path);
            }

            Sprite commonHideout = ImportLoadingSprite(Root + "/Art/Loading/Loading_Common_Hideout.png");
            Sprite schoolExterior = ImportLoadingSprite(Root + "/Art/Loading/Loading_School_Exterior.png");
            Sprite schoolCorridor = ImportLoadingSprite(Root + "/Art/Loading/Loading_School_Corridor.png");

            RaidRegionDefinition schoolRegion = null;
            if (raidRegions != null)
            {
                foreach (RaidRegionDefinition region in raidRegions)
                {
                    if (region != null && region.RegionId == "school")
                    {
                        schoolRegion = region;
                        break;
                    }
                }
            }

            catalog.Configure(
                new[] { commonHideout },
                new[]
                {
                    new LoadingRegionImagePool(
                        schoolRegion,
                        new[] { schoolExterior, schoolCorridor })
                });
            EditorUtility.SetDirty(catalog);
            return catalog;
        }

        // 화면 비율이 달라도 선명한 전체 화면 배경으로 사용할 수 있도록 PNG를 단일 Sprite로 임포트합니다.
        // 원본 파일의 픽셀은 수정하지 않고 Unity의 임포트 설정만 바꾸므로 나중에 같은 경로의 그림을 교체해도 연결은 유지됩니다.
        private static Sprite ImportLoadingSprite(string assetPath)
        {
            if (!File.Exists(Path.GetFullPath(assetPath)))
            {
                Debug.LogError($"로딩 이미지 임포트 실패: '{assetPath}' 파일이 없습니다.");
                return null;
            }

            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);
            TextureImporter importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
            if (importer != null)
            {
                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.mipmapEnabled = false;
                importer.alphaIsTransparency = true;
                importer.sRGBTexture = true;
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.filterMode = FilterMode.Bilinear;
                importer.maxTextureSize = 2048;
                importer.textureCompression = TextureImporterCompression.CompressedHQ;
                importer.SaveAndReimport();
            }

            return AssetDatabase.LoadAssetAtPath<Sprite>(assetPath);
        }

        // 모든 거점 시설을 같은 자료형으로 만들고 레벨별 실제 효과와 재료 비용을 데이터로 연결합니다.
        private static HideoutFacilityDefinition[] CreateHideoutFacilityDefinitions(ItemCatalog itemCatalog)
        {
            ItemDefinition scrap = itemCatalog?.Find("material_scrap");
            ItemDefinition electronics = itemCatalog?.Find("material_electronics");
            ItemDefinition cloth = itemCatalog?.Find("material_cloth");

            return new[]
            {
                CreateHideoutFacilityDefinition(
                    "storage",
                    "창고",
                    "보관품을 정리하고 다음 레이드 출격 장비를 준비합니다.",
                    HideoutFacilityType.Storage,
                    1,
                    5,
                    0,
                    2,
                    "추가 창고 행",
                    UpgradeStep(2, Cost(scrap, 4), Cost(cloth, 2)),
                    UpgradeStep(3, Cost(scrap, 7), Cost(electronics, 2)),
                    UpgradeStep(4, Cost(scrap, 10), Cost(electronics, 4)),
                    UpgradeStep(5, Cost(scrap, 14), Cost(electronics, 7))),
                CreateHideoutFacilityDefinition(
                    "mission_board",
                    "임무 보드",
                    "거점에서 수행할 임무를 확인하고 하나를 선택합니다.",
                    HideoutFacilityType.MissionBoard,
                    1,
                    3,
                    2,
                    1,
                    "공개 임무 수",
                    UpgradeStep(2, Cost(scrap, 3), Cost(electronics, 1)),
                    UpgradeStep(3, Cost(scrap, 6), Cost(electronics, 3))),
                CreateHideoutFacilityDefinition(
                    "crafting_bench",
                    "제작대",
                    "레이드에서 가져온 재료를 탄약과 생존 물품으로 바꿉니다.",
                    HideoutFacilityType.CraftingBench,
                    1,
                    3,
                    1,
                    1,
                    "공개 제작법 수",
                    UpgradeStep(2, Cost(scrap, 5), Cost(electronics, 2)),
                    UpgradeStep(3, Cost(scrap, 9), Cost(electronics, 5))),
                CreateHideoutFacilityDefinition(
                    "infirmary",
                    "치료소",
                    "천 조각을 사용해 의료품을 만들고 치료 효율을 높입니다.",
                    HideoutFacilityType.Infirmary,
                    1,
                    3,
                    1,
                    1,
                    "의료품 제작 수량",
                    UpgradeStep(2, Cost(cloth, 5), Cost(scrap, 2)),
                    UpgradeStep(3, Cost(cloth, 9), Cost(electronics, 3))),
                CreateHideoutFacilityDefinition(
                    "generator",
                    "발전기",
                    "제작 시설에 전력을 공급하고 레벨마다 제작 재료 소모를 줄입니다.",
                    HideoutFacilityType.Generator,
                    1,
                    3,
                    0,
                    10,
                    "제작 비용 감소율(%)",
                    UpgradeStep(2, Cost(scrap, 6), Cost(electronics, 3)),
                    UpgradeStep(3, Cost(scrap, 10), Cost(electronics, 6))),
                // 상인 레벨은 구매 가격 할인과 판매 가격 보너스에 함께 사용됩니다.
                CreateHideoutFacilityDefinition(
                    "trader",
                    "보급 상인",
                    "크레딧으로 보급품을 사고 창고의 전리품을 판매합니다.",
                    HideoutFacilityType.Trader,
                    1,
                    3,
                    5,
                    5,
                    "거래 가격 보정률(%)",
                    UpgradeStep(2, Cost(scrap, 6), Cost(electronics, 2)),
                    UpgradeStep(3, Cost(scrap, 10), Cost(electronics, 5))),
                // 출격 터미널은 지역을 고르는 기능 시설이며 강화 대상이 아니므로 1레벨 고정으로 만듭니다.
                CreateHideoutFacilityDefinition(
                    "deployment",
                    "출격 터미널",
                    "레이드 지역을 선택하고 준비한 출격 인벤토리로 출발합니다.",
                    HideoutFacilityType.Deployment,
                    1,
                    1,
                    1,
                    0,
                    "선택 가능한 지역"),
                // 강화소 자체는 레벨을 올리지 않으며 다른 모든 시설의 강화 기능을 한 화면에 모읍니다.
                CreateHideoutFacilityDefinition(
                    "upgrade_station",
                    "강화소",
                    "창고의 재료를 사용해 거점 시설을 선택하고 강화합니다.",
                    HideoutFacilityType.UpgradeStation,
                    1,
                    1,
                    1,
                    0,
                    "통합 강화 관리")
            };
        }

        private static HideoutFacilityDefinition CreateHideoutFacilityDefinition(
            string id,
            string displayName,
            string description,
            HideoutFacilityType type,
            int startingLevel,
            int maximumLevel,
            int baseEffect,
            int effectPerLevel,
            string effectLabel,
            params HideoutFacilityUpgradeStep[] upgradeSteps)
        {
            string path = Root + "/Data/Hideout/facility_" + id + ".asset";
            HideoutFacilityDefinition definition = AssetDatabase.LoadAssetAtPath<HideoutFacilityDefinition>(path);
            if (definition == null)
            {
                definition = ScriptableObject.CreateInstance<HideoutFacilityDefinition>();
                AssetDatabase.CreateAsset(definition, path);
            }
            definition.Configure(
                id,
                displayName,
                description,
                type,
                startingLevel,
                maximumLevel,
                baseEffect,
                effectPerLevel,
                effectLabel,
                upgradeSteps);
            EditorUtility.SetDirty(definition);
            return definition;
        }

        // 시설 정의 배열을 읽기 쉽게 유지하기 위한 짧은 생성 보조 함수입니다.
        private static HideoutFacilityUpgradeStep UpgradeStep(int targetLevel, params HideoutFacilityItemCost[] costs)
            => new HideoutFacilityUpgradeStep(targetLevel, costs);

        private static HideoutFacilityItemCost Cost(ItemDefinition item, int quantity)
            => new HideoutFacilityItemCost(item, quantity);

        // 처치·아이템 회수·성공 탈출·학교 구역 방문 목표와 일회성·반복 가능 규칙을 확인할 기본 임무를 만듭니다.
        // 보상은 문자열이 아니라 ItemDefinition과 수량으로 연결하므로 완료할 때 실제 창고에 같은 아이템이 들어갑니다.
        private static HideoutMissionDefinition[] CreateHideoutMissionDefinitions(ItemCatalog itemCatalog)
        {
            ItemDefinition medkit = itemCatalog?.Find("medkit_small");
            ItemDefinition food = itemCatalog?.Find("food_canned");
            ItemDefinition ammo9mm = itemCatalog?.Find("ammo_9mm");
            ItemDefinition ammo556 = itemCatalog?.Find("ammo_556");
            ItemDefinition bandage = itemCatalog?.Find("medical_bandage");
            ItemDefinition questRadio = itemCatalog?.Find("quest_radio");
            ItemDefinition cloth = itemCatalog?.Find("material_cloth");
            ItemDefinition pistol = itemCatalog?.Find("weapon_pistol");

            return new[]
            {
                ConfigureMissionProgression(CreateHideoutMissionDefinition(
                    "first_sweep",
                    "주변 정리",
                    "거점 주변의 위험을 낮추기 위해 레이드에서 좀비 3마리를 처치하세요.",
                    HideoutMissionObjectiveType.ZombieKills,
                    null,
                    3,
                    false,
                    new HideoutMissionItemReward(medkit, 1)), null, 150, 1),
                ConfigureMissionProgression(CreateHideoutZoneMissionDefinition(
                    "school_recon",
                    "보건실 정찰",
                    "폐쇄된 학교의 행정동·보건실 구역에 들어가 내부 상태를 확인하세요. 방문 기록은 사망해도 남습니다.",
                    SchoolRaidMapLayout.AdministrationZoneId,
                    1,
                    false,
                    new HideoutMissionItemReward(bandage, 2)), "first_sweep", 220, 2),
                ConfigureMissionProgression(CreateHideoutMissionDefinition(
                    "supply_recovery",
                    "식량 회수",
                    "레이드에서 통조림 2개를 가지고 탈출하세요. 성공한 레이드의 회수품만 진행도에 포함됩니다.",
                    HideoutMissionObjectiveType.CollectedItem,
                    food,
                    2,
                    false,
                    new HideoutMissionItemReward(ammo9mm, 20)), "school_recon", 260, 2),
                ConfigureMissionProgression(CreateHideoutZoneItemMissionDefinition(
                    "school_radio_recovery",
                    "방송 장비 회수",
                    "학교 특별실·방송 구역에서 휴대용 무전기를 찾아 소지한 채 탈출하세요.",
                    SchoolRaidMapLayout.SpecialClassZoneId,
                    questRadio,
                    1,
                    false,
                    new HideoutMissionItemReward(cloth, 4),
                    new HideoutMissionItemReward(ammo556, 15)), "supply_recovery", 340, 3),
                ConfigureMissionProgression(CreateHideoutMissionDefinition(
                    "safe_return",
                    "무사 귀환",
                    "피해를 한 번도 받지 않고 레이드에서 탈출하세요.",
                    HideoutMissionObjectiveType.NoDamageExtraction,
                    null,
                    1,
                    false,
                    new HideoutMissionItemReward(medkit, 1),
                    new HideoutMissionItemReward(ammo9mm, 10)), "school_radio_recovery", 400, 3),
                ConfigureMissionProgression(CreateHideoutMissionDefinition(
                    "deep_sweep",
                    "대대적인 소탕",
                    "여러 차례 출격해 권총으로 좀비 8마리를 처치하세요. 처치 수는 레이드 사이에도 누적됩니다.",
                    HideoutMissionObjectiveType.WeaponKills,
                    pistol,
                    8,
                    true,
                    new HideoutMissionItemReward(ammo556, 20)), "safe_return", 550, 4)
            };
        }

        private static HideoutMissionDefinition CreateHideoutZoneMissionDefinition(
            string id,
            string displayName,
            string description,
            string zoneId,
            int requiredAmount,
            bool repeatable,
            params HideoutMissionItemReward[] rewards)
        {
            string path = Root + "/Data/Hideout/mission_" + id + ".asset";
            HideoutMissionDefinition definition = AssetDatabase.LoadAssetAtPath<HideoutMissionDefinition>(path);
            if (definition == null)
            {
                definition = ScriptableObject.CreateInstance<HideoutMissionDefinition>();
                AssetDatabase.CreateAsset(definition, path);
            }

            // 장소 임무 전용 API를 사용해야 일반 Configure가 objectiveZoneId를 비우는 안전 규칙과 충돌하지 않습니다.
            definition.ConfigureZoneVisit(
                id,
                displayName,
                description,
                zoneId,
                requiredAmount,
                repeatable,
                rewards);
            EditorUtility.SetDirty(definition);
            return definition;
        }

        // 구역 방문과 특정 아이템 회수를 동시에 요구하는 임무는 전용 설정 함수를 사용합니다.
        // 일반 아이템 임무로 만들면 어디서 주웠는지 확인할 수 없으므로 두 조건을 에셋에 함께 저장해야 합니다.
        private static HideoutMissionDefinition CreateHideoutZoneItemMissionDefinition(
            string id,
            string displayName,
            string description,
            string zoneId,
            ItemDefinition objectiveItem,
            int requiredAmount,
            bool repeatable,
            params HideoutMissionItemReward[] rewards)
        {
            string path = Root + "/Data/Hideout/mission_" + id + ".asset";
            HideoutMissionDefinition definition = AssetDatabase.LoadAssetAtPath<HideoutMissionDefinition>(path);
            if (definition == null)
            {
                definition = ScriptableObject.CreateInstance<HideoutMissionDefinition>();
                AssetDatabase.CreateAsset(definition, path);
            }

            definition.ConfigureZoneItemRecovery(
                id,
                displayName,
                description,
                zoneId,
                objectiveItem,
                requiredAmount,
                repeatable,
                rewards);
            EditorUtility.SetDirty(definition);
            return definition;
        }

        // 생성 직후 연속 임무 조건과 위험·화폐 보상을 붙여 데이터 배열에서 진행 순서를 한눈에 읽을 수 있게 합니다.
        private static HideoutMissionDefinition ConfigureMissionProgression(
            HideoutMissionDefinition definition,
            string prerequisiteMissionId,
            int creditReward,
            int riskRating)
        {
            definition.ConfigureProgression(prerequisiteMissionId, creditReward, riskRating);
            EditorUtility.SetDirty(definition);
            return definition;
        }

        private static HideoutMissionDefinition CreateHideoutMissionDefinition(
            string id,
            string displayName,
            string description,
            HideoutMissionObjectiveType objectiveType,
            ItemDefinition objectiveItem,
            int requiredAmount,
            bool repeatable,
            params HideoutMissionItemReward[] rewards)
        {
            string path = Root + "/Data/Hideout/mission_" + id + ".asset";
            HideoutMissionDefinition definition = AssetDatabase.LoadAssetAtPath<HideoutMissionDefinition>(path);
            if (definition == null)
            {
                definition = ScriptableObject.CreateInstance<HideoutMissionDefinition>();
                AssetDatabase.CreateAsset(definition, path);
            }
            definition.Configure(
                id,
                displayName,
                description,
                objectiveType,
                objectiveItem,
                requiredAmount,
                repeatable,
                rewards);
            EditorUtility.SetDirty(definition);
            return definition;
        }

        // 제작대와 치료소 레벨에 따라 순서대로 공개되는 기본 제작법을 만듭니다.
        // 발전기 레벨 할인은 제작법 에셋을 바꾸지 않고 런타임 서비스가 모든 재료 수량에 공통 적용합니다.
        private static HideoutRecipeDefinition[] CreateHideoutRecipeDefinitions(ItemCatalog itemCatalog)
        {
            ItemDefinition scrap = itemCatalog?.Find("material_scrap");
            ItemDefinition electronics = itemCatalog?.Find("material_electronics");
            ItemDefinition cloth = itemCatalog?.Find("material_cloth");
            ItemDefinition ammo9mm = itemCatalog?.Find("ammo_9mm");
            ItemDefinition ammo556 = itemCatalog?.Find("ammo_556");
            ItemDefinition ammo12g = itemCatalog?.Find("ammo_12g");
            ItemDefinition medkit = itemCatalog?.Find("medkit_small");

            return new[]
            {
                CreateHideoutRecipeDefinition("craft_9mm", "9mm 탄약 묶음", HideoutFacilityType.CraftingBench, 1, ammo9mm, 15, 0, Cost(scrap, 3)),
                CreateHideoutRecipeDefinition("craft_556", "5.56mm 탄약 묶음", HideoutFacilityType.CraftingBench, 2, ammo556, 12, 0, Cost(scrap, 4), Cost(electronics, 1)),
                CreateHideoutRecipeDefinition("craft_12g", "12게이지 산탄 묶음", HideoutFacilityType.CraftingBench, 3, ammo12g, 8, 0, Cost(scrap, 5), Cost(electronics, 2)),
                // 치료소는 레벨이 오를 때마다 한 번 제작으로 나오는 구급상자 수가 1개씩 늘어납니다.
                CreateHideoutRecipeDefinition("craft_medkit", "소형 구급상자", HideoutFacilityType.Infirmary, 1, medkit, 1, 1, Cost(cloth, 4), Cost(scrap, 1))
            };
        }

        private static HideoutRecipeDefinition CreateHideoutRecipeDefinition(
            string id,
            string displayName,
            HideoutFacilityType facilityType,
            int requiredLevel,
            ItemDefinition outputItem,
            int outputQuantity,
            int bonusOutputPerLevel,
            params HideoutFacilityItemCost[] ingredients)
        {
            string path = Root + "/Data/Hideout/recipe_" + id + ".asset";
            HideoutRecipeDefinition definition = AssetDatabase.LoadAssetAtPath<HideoutRecipeDefinition>(path);
            if (definition == null)
            {
                definition = ScriptableObject.CreateInstance<HideoutRecipeDefinition>();
                AssetDatabase.CreateAsset(definition, path);
            }

            definition.Configure(
                id,
                displayName,
                facilityType,
                requiredLevel,
                outputItem,
                outputQuantity,
                bonusOutputPerLevel,
                ingredients);
            EditorUtility.SetDirty(definition);
            return definition;
        }

        // 물리와 로직은 Player 루트에, 2D 표현은 Visual 자식에 배치한 프리팹을 생성합니다.
        // 이 분리는 스프라이트 좌우 반전과 Billboard 회전이 CharacterController의 충돌 방향에 영향을 주지 않게 합니다.
        // 임시 GameObject 구성을 프리팹 에셋으로 저장한 뒤 임시 원본은 DestroyImmediate로 즉시 정리합니다.
        private static GameObject CreatePlayerPrefab(Sprite sprite)
        {
            // 물리와 이동/조준 로직은 Player 루트에 둡니다.
            var player = new GameObject("Player");
            player.AddComponent<CharacterController>().height = 1.7f;
            player.GetComponent<CharacterController>().radius = 0.35f;
            player.GetComponent<CharacterController>().center = new Vector3(0f, 0.85f, 0f);
            player.AddComponent<PlayerMovement>();
            var aim = player.AddComponent<PlayerAim>();
            var combat = player.AddComponent<PlayerCombat>();
            Health playerHealth = player.AddComponent<Health>();
            PlayerInventory playerInventory = player.AddComponent<PlayerInventory>();
            // 이전 Player 프리팹에 저장된 1.5초 공통 치료 시간이 새 스크립트 기본값보다 우선될 수 있으므로 생성 시 값을 명시합니다.
            // 의료품별 준비 시간이 서로 달라야 붕대는 빠른 응급처치, 부목은 중간 치료, 구급상자는 긴 종합 치료라는 역할이 플레이에도 드러납니다.
            var serializedInventory = new SerializedObject(playerInventory);
            serializedInventory.FindProperty("healingUseDuration").floatValue = 4f;
            serializedInventory.FindProperty("bandageUseDuration").floatValue = 2.2f;
            serializedInventory.FindProperty("splintUseDuration").floatValue = 3.5f;
            serializedInventory.ApplyModifiedPropertiesWithoutUndo();
            // 출혈과 골절은 Health의 총 체력과 분리해 관리하되 같은 플레이어 루트에서 피해 이벤트를 구독합니다.
            player.AddComponent<PlayerCondition>();
            // 포만감은 플레이어마다 한 개만 존재하며 PlayerMovement와 음식 사용 코드가 같은 상태를 읽습니다.
            player.AddComponent<PlayerSurvival>();
            // Health가 실제 체력을 줄이기 전에 장착 방탄복의 피해 감소와 내구도를 적용합니다.
            player.AddComponent<ArmorProtection>();
            player.AddComponent<PlayerInteraction>();
            // 인벤토리와 레이드 종료가 같은 조작 컴포넌트를 안전하게 잠그도록 공용 Gate를 플레이어 루트에 둡니다.
            player.AddComponent<PlayerControlGate>();
            // 버프·디버프의 남은 시간과 능력치 배율은 한 컴포넌트가 그룹별로 관리하고, 퀵슬롯은 기존 인벤토리 사용 흐름만 호출합니다.
            player.AddComponent<PlayerStatusEffects>();
            player.AddComponent<PlayerQuickSlots>();
            player.AddComponent<WeaponFireFeedback>();
            player.AddComponent<WeaponAudioFeedback>();
            player.AddComponent<HealthAudioFeedback>().Configure(playerHealth, HealthAudioRole.Player);

            // 스프라이트는 충돌체 회전에 영향을 주지 않도록 Visual 자식으로 분리합니다.
            var visual = new GameObject("Visual");
            visual.transform.SetParent(player.transform, false);
            visual.transform.localPosition = new Vector3(0f, 0.85f, 0f);
            SpriteRenderer playerRenderer = visual.AddComponent<SpriteRenderer>();
            playerRenderer.sprite = sprite;
            visual.AddComponent<SpriteBillboard>();

            // 루트 Health의 피해 이벤트를 Visual 스프라이트의 짧은 붉은 점멸로 표현하도록 정확한 참조를 연결합니다.
            player.AddComponent<DamageFlash>().Configure(playerHealth, playerRenderer);

            var socket = new GameObject("WeaponSocket");
            socket.transform.SetParent(player.transform, false);
            socket.transform.localPosition = new Vector3(0.4f, 1f, 0f);

            var marker = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            marker.name = "AimMarker";
            Object.DestroyImmediate(marker.GetComponent<Collider>());
            marker.transform.SetParent(player.transform, false);
            marker.transform.localScale = new Vector3(0.18f, 0.015f, 0.18f);
            SetColor(marker, new Color(0.1f, 1f, 0.8f));

            // SerializedObject로 private SerializeField 참조를 프리팹에 안전하게 기록합니다.
            var serializedAim = new SerializedObject(aim);
            serializedAim.FindProperty("visual").objectReferenceValue = visual.transform;
            serializedAim.FindProperty("aimMarker").objectReferenceValue = marker.transform;
            serializedAim.FindProperty("aimOrigin").objectReferenceValue = socket.transform;
            serializedAim.ApplyModifiedPropertiesWithoutUndo();

            // PlayerCombat이 총구 위치로 WeaponSocket을 사용하도록 프리팹 참조를 연결합니다.
            var serializedCombat = new SerializedObject(combat);
            serializedCombat.FindProperty("weaponSocket").objectReferenceValue = socket.transform;
            serializedCombat.ApplyModifiedPropertiesWithoutUndo();

            string path = Root + "/Prefabs/Characters/Player.prefab";
            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(player, path);
            Object.DestroyImmediate(player);
            return prefab;
        }

        // 기존 Player 프리팹을 통째로 다시 만들지 않고 PlayerCondition이 없을 때만 추가해 사용자가 바꾼 스프라이트와 배치를 보존합니다.
        private static GameObject EnsurePlayerConditionPrefab(GameObject playerPrefab)
        {
            if (playerPrefab == null) return null;
            string path = AssetDatabase.GetAssetPath(playerPrefab);
            GameObject root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                if (root.GetComponent<PlayerCondition>() == null)
                    root.AddComponent<PlayerCondition>();
                PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
            return AssetDatabase.LoadAssetAtPath<GameObject>(path);
        }

        // 기존 Player 프리팹의 그림·소켓·수치를 보존하면서 버프 관리자와 퀵슬롯 입력 컴포넌트만 없을 때 추가합니다.
        // RequireComponent의 자동 추가에 의존하지 않고 두 타입을 명시해 프리팹 Hierarchy와 Inspector에서도 구조가 분명하게 보이게 합니다.
        private static GameObject EnsurePlayerBuffAndQuickSlotsPrefab(GameObject playerPrefab)
        {
            if (playerPrefab == null) return null;
            string path = AssetDatabase.GetAssetPath(playerPrefab);
            GameObject root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                if (root.GetComponent<PlayerStatusEffects>() == null)
                    root.AddComponent<PlayerStatusEffects>();
                if (root.GetComponent<PlayerQuickSlots>() == null)
                    root.AddComponent<PlayerQuickSlots>();
                PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
            return AssetDatabase.LoadAssetAtPath<GameObject>(path);
        }

        // 기존 무기 에셋의 다른 밸런스 값은 건드리지 않고 이번 전투 시스템에 필요한 관통력만 기본값으로 채웁니다.
        private static void ApplyWeaponPenetrationDefaults(WeaponDefinition[] definitions)
        {
            int[] penetrationValues = { 3, 1, 1, 1 };
            for (int i = 0; i < definitions.Length && i < penetrationValues.Length; i++)
            {
                if (definitions[i] == null) continue;
                var serialized = new SerializedObject(definitions[i]);
                serialized.FindProperty("armorPenetration").intValue = penetrationValues[i];
                serialized.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(definitions[i]);
            }
        }

        // 거점 전용 재생성은 사용자가 조정한 피해·사거리 수치를 보존하면서 새 발사 모드와 내구도 필드만 안전한 기본값으로 채웁니다.
        // 배열 순서는 CreateWeaponDefinitions와 같으므로 소총·기관단총만 단발/연사 전환을 지원하고 권총·산탄총은 단발 전용입니다.
        private static void ApplyWeaponFinishingDefaults(WeaponDefinition[] definitions)
        {
            float[] durabilityLoss = { 0.18f, 0.12f, 0.22f, 0.3f };
            int[] repairCosts = { 3, 2, 2, 3 };
            for (int i = 0; i < definitions.Length && i < durabilityLoss.Length; i++)
            {
                if (definitions[i] == null) continue;
                var serialized = new SerializedObject(definitions[i]);
                bool supportsAutomatic = i == 0 || i == 2;
                serialized.FindProperty("supportsSemiAutomatic").boolValue = true;
                serialized.FindProperty("supportsAutomatic").boolValue = supportsAutomatic;
                serialized.FindProperty("maxDurability").floatValue = 100f;
                serialized.FindProperty("durabilityLossPerShot").floatValue = durabilityLoss[i];
                serialized.FindProperty("wornAccuracySpreadMultiplier").floatValue = 2f;
                serialized.FindProperty("wornReloadDurationMultiplier").floatValue = 1.5f;
                serialized.FindProperty("repairCostPerPoint").intValue = repairCosts[i];
                serialized.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(definitions[i]);
            }
        }

        // 3D 이동 루트와 카메라를 향하는 2D 스프라이트로 좀비 프리팹을 생성합니다.
        private static GameObject CreateZombiePrefab(Sprite sprite)
        {
            var zombie = new GameObject("Zombie");
            CharacterController controller = zombie.AddComponent<CharacterController>();
            controller.height = 1.7f;
            controller.radius = 0.35f;
            controller.center = new Vector3(0f, 0.85f, 0f);
            Health zombieHealth = zombie.AddComponent<Health>();
            // ZombieMovement가 런타임에 NavMeshAgent를 준비하고 Unity 길찾기 결과를 CharacterController 이동에 사용합니다.
            zombie.AddComponent<ZombieMovement>();
            zombie.AddComponent<ZombieAttack>();
            zombie.AddComponent<ZombieBrain>();
            // 프리팹에 드롭 담당을 두고 씬의 각 좀비 인스턴스에서 서로 다른 목록을 설정합니다.
            zombie.AddComponent<ZombieLootDrop>();

            var visual = new GameObject("Visual");
            visual.transform.SetParent(zombie.transform, false);
            visual.transform.localPosition = new Vector3(0f, 0.85f, 0f);
            SpriteRenderer renderer = visual.AddComponent<SpriteRenderer>();
            renderer.sprite = sprite;
            renderer.color = new Color(0.48f, 0.72f, 0.36f);
            visual.AddComponent<SpriteBillboard>();

            // 좀비 고유의 녹색은 DamageFlash가 원래 색으로 저장하므로 피격 점멸이 끝나면 정확히 복원됩니다.
            zombie.AddComponent<DamageFlash>().Configure(zombieHealth, renderer);
            zombie.AddComponent<HealthAudioFeedback>().Configure(zombieHealth, HealthAudioRole.Zombie);

            string path = Root + "/Prefabs/Enemies/Zombie.prefab";
            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(zombie, path);
            Object.DestroyImmediate(zombie);
            return prefab;
        }

        // 하나의 공용 좀비 프리팹에 서로 다른 수치, 감각, 특수 행동과 전리품을 적용하는 데이터 에셋을 만듭니다.
        // 기존 0 일반형·1 빠른형·2 튼튼한형 순서는 씬 참조를 보호하고, 비명형은 마지막에 추가합니다.
        private static ZombieArchetype[] CreateZombieArchetypes(
            GameObject zombiePrefab,
            ItemDefinition[] itemDefinitions)
        {
            ZombieArchetype walker = CreateZombieArchetype(
                    "walker",
                    "일반 좀비",
                    zombiePrefab,
                    1f,
                    0f,
                    90f,
                    2.35f,
                    12f,
                    new Color(0.48f, 0.72f, 0.36f, 1f),
                    new ZombieLootEntry { item = itemDefinitions[0], dropChance = 0.55f, minimumQuantity = 2, maximumQuantity = 6 },
                    new ZombieLootEntry { item = itemDefinitions[15], dropChance = 0.24f, minimumQuantity = 1, maximumQuantity = 2 },
                    new ZombieLootEntry { item = itemDefinitions[2], dropChance = 0.18f, minimumQuantity = 1, maximumQuantity = 1 });
            walker.ConfigureBehavior(ZombieBehaviorTrait.Standard, 9f, 18f, 14f, 4f);

            ZombieArchetype runner = CreateZombieArchetype(
                    "runner",
                    "빠른 좀비",
                    zombiePrefab,
                    0.55f,
                    50f,
                    60f,
                    5.2f,
                    8f,
                    new Color(0.78f, 0.8f, 0.22f, 1f),
                    new ZombieLootEntry { item = itemDefinitions[0], dropChance = 0.65f, minimumQuantity = 3, maximumQuantity = 8 },
                    new ZombieLootEntry { item = itemDefinitions[13], dropChance = 0.32f, minimumQuantity = 1, maximumQuantity = 3 },
                    new ZombieLootEntry { item = itemDefinitions[1], dropChance = 0.15f, minimumQuantity = 1, maximumQuantity = 1 });
            runner.ConfigureBehavior(ZombieBehaviorTrait.Runner, 12f, 22f, 18f, 3.5f);

            ZombieArchetype brute = CreateZombieArchetype(
                    "brute",
                    "튼튼한 좀비",
                    zombiePrefab,
                    0.25f,
                    120f,
                    190f,
                    1.65f,
                    22f,
                    new Color(0.58f, 0.24f, 0.18f, 1f),
                    new ZombieLootEntry { item = itemDefinitions[9], dropChance = 0.7f, minimumQuantity = 5, maximumQuantity = 12 },
                    // 튼튼한 좀비는 낮은 확률로 산탄도 남겨 산탄총을 획득한 뒤 현장에서 보급할 길을 제공합니다.
                    new ZombieLootEntry { item = itemDefinitions[10], dropChance = 0.25f, minimumQuantity = 2, maximumQuantity = 4 },
                    new ZombieLootEntry { item = itemDefinitions[13], dropChance = 0.58f, minimumQuantity = 2, maximumQuantity = 5 },
                    new ZombieLootEntry { item = itemDefinitions[14], dropChance = 0.24f, minimumQuantity = 1, maximumQuantity = 2 },
                    new ZombieLootEntry { item = itemDefinitions[6], dropChance = 0.08f, minimumQuantity = 1, maximumQuantity = 1 });
            brute.ConfigureBehavior(ZombieBehaviorTrait.Brute, 8f, 20f, 13f, 5f);

            // 비명형은 플레이어를 발견하면 넓은 소음 신호를 내 주변 좀비가 같은 위치를 조사하게 만듭니다.
            ZombieArchetype screamer = CreateZombieArchetype(
                    "screamer",
                    "비명 좀비",
                    zombiePrefab,
                    0.18f,
                    90f,
                    80f,
                    2.7f,
                    10f,
                    new Color(0.58f, 0.28f, 0.72f, 1f),
                    new ZombieLootEntry { item = itemDefinitions[14], dropChance = 0.62f, minimumQuantity = 1, maximumQuantity = 3 },
                    new ZombieLootEntry { item = itemDefinitions[16], dropChance = 0.3f, minimumQuantity = 1, maximumQuantity = 1 },
                    new ZombieLootEntry { item = itemDefinitions[18], dropChance = 0.08f, minimumQuantity = 1, maximumQuantity = 1 });
            screamer.ConfigureBehavior(ZombieBehaviorTrait.Screamer, 11f, 26f, 16f, 6f);

            EditorUtility.SetDirty(walker);
            EditorUtility.SetDirty(runner);
            EditorUtility.SetDirty(brute);
            EditorUtility.SetDirty(screamer);
            return new[] { walker, runner, brute, screamer };
        }

        // 기존 에셋이 있으면 GUID를 보존한 채 기본값을 갱신해 씬의 참조가 Rebuild 뒤에도 끊어지지 않게 합니다.
        // 밸런스를 직접 조정하고 싶다면 생성 후 Data/Zombies의 에셋을 Inspector에서 수정할 수 있습니다.
        private static ZombieArchetype CreateZombieArchetype(
            string id,
            string displayName,
            GameObject zombiePrefab,
            float spawnWeight,
            float unlockTimeSeconds,
            float maximumHealth,
            float movementSpeed,
            float attackDamage,
            Color visualColor,
            params ZombieLootEntry[] lootEntries)
        {
            string path = Root + "/Data/Zombies/" + id + ".asset";
            ZombieArchetype archetype = AssetDatabase.LoadAssetAtPath<ZombieArchetype>(path);
            if (archetype == null)
            {
                archetype = ScriptableObject.CreateInstance<ZombieArchetype>();
                AssetDatabase.CreateAsset(archetype, path);
            }

            archetype.Configure(
                displayName,
                zombiePrefab,
                spawnWeight,
                unlockTimeSeconds,
                maximumHealth,
                movementSpeed,
                attackDamage,
                visualColor,
                lootEntries);
            EditorUtility.SetDirty(archetype);
            return archetype;
        }

        // 기존 학교 레이드 맵 배치를 재생성하지 않고 ZombieSpawner의 후보 배열에 새 특수형만 반영합니다.
        // 사용자가 RaidSchool 씬을 열어 수정 중이면 저장되지 않은 배치를 보호하기 위해 작업을 건너뜁니다.
        private static void UpgradeRaidSceneArchetypes(ZombieArchetype[] archetypes)
        {
            const string scenePath = Root + "/Scenes/RaidSchool.unity";
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene loaded = SceneManager.GetSceneAt(i);
                if (loaded.isLoaded && loaded.path == scenePath)
                {
                    // 디스크에서 막 연 깨끗한 씬은 배열 하나만 바꿔 안전하게 저장할 수 있습니다.
                    // 사용자가 수정 중인 dirty 씬은 다른 변경까지 함께 저장될 수 있으므로 절대 자동 저장하지 않습니다.
                    if (loaded.isDirty)
                    {
                        Debug.LogWarning("레이드 좀비 목록 갱신 생략: 저장되지 않은 RaidSchool 씬 변경을 보호했습니다.");
                        return;
                    }
                    ApplyArchetypesToSpawner(loaded, archetypes);
                    EditorSceneManager.SaveScene(loaded);
                    return;
                }
            }

            Scene previous = SceneManager.GetActiveScene();
            Scene raidScene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Additive);
            try
            {
                ApplyArchetypesToSpawner(raidScene, archetypes);
                EditorSceneManager.SaveScene(raidScene);
            }
            finally
            {
                if (previous.IsValid() && previous.isLoaded) SceneManager.SetActiveScene(previous);
                if (raidScene.IsValid() && raidScene.isLoaded) EditorSceneManager.CloseScene(raidScene, true);
            }
        }

        private static bool ApplyArchetypesToSpawner(Scene scene, ZombieArchetype[] archetypes)
        {
            ZombieSpawner spawner = null;
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                spawner = root.GetComponentInChildren<ZombieSpawner>(true);
                if (spawner != null) break;
            }
            if (spawner == null)
            {
                Debug.LogWarning("레이드 좀비 목록 갱신 생략: RaidSchool 씬에서 ZombieSpawner를 찾지 못했습니다.");
                return false;
            }

            var serialized = new SerializedObject(spawner);
            SerializedProperty entries = serialized.FindProperty("archetypes");
            entries.arraySize = archetypes != null ? archetypes.Length : 0;
            for (int i = 0; i < entries.arraySize; i++)
                entries.GetArrayElementAtIndex(i).objectReferenceValue = archetypes[i];
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(spawner);
            return true;
        }

        // 인벤토리 UI를 이루는 작은 재사용 프리팹들을 먼저 만든 뒤, 그것들을 조립한 전체 Canvas 프리팹을 반환합니다.
        // 각 프리팹을 분리해 두었으므로 사용자는 코드를 수정하지 않고 해당 프리팹의 Image, Sprite와 색상을 Inspector에서 교체할 수 있습니다.
        private static GameObject CreateInventoryUiPrefabs()
        {
            // 이미 만들어진 전체 Canvas는 사용자가 Inspector에서 수정한 디자인일 수 있으므로 다시 생성해 덮어쓰지 않습니다.
            // 기본 디자인으로 완전히 되돌리고 싶을 때만 Prefabs/UI의 인벤토리 프리팹들을 직접 삭제한 뒤 Rebuild를 실행하면 됩니다.
            string canvasPath = Root + "/Prefabs/UI/InventoryCanvas.prefab";
            GameObject existingCanvas = AssetDatabase.LoadAssetAtPath<GameObject>(canvasPath);
            if (existingCanvas != null)
            {
                InventoryHud existingHud = existingCanvas.GetComponent<InventoryHud>();
                SerializedProperty detailProperty = existingHud != null
                    ? new SerializedObject(existingHud).FindProperty("itemDetailText")
                    : null;
                // 상세 패널이 있는 최신 프리팹만 그대로 보존하고, 이전 구조는 이번 UI 확장을 위해 한 번 새 구조로 교체합니다.
                if (detailProperty != null && detailProperty.objectReferenceValue != null)
                    return UpgradeInventoryCanvasAnimation(canvasPath);
            }

            Image gridCellPrefab = CreateImagePrefab(
                "InventoryGridCell",
                Root + "/Prefabs/UI/InventoryGridCell.prefab",
                new Color(0.08f, 0.1f, 0.11f, 0.98f));
            Image itemCellPrefab = CreateImagePrefab(
                "InventoryItemCell",
                Root + "/Prefabs/UI/InventoryItemCell.prefab",
                new Color(0.25f, 0.48f, 0.34f, 1f));
            InventoryItemView itemPrefab = CreateInventoryItemPrefab(itemCellPrefab);
            EquipmentSlotView equipmentPrefab = CreateEquipmentSlotPrefab();
            InventoryGridView gridPrefab = CreateInventoryGridPrefab(gridCellPrefab, itemPrefab);
            return CreateInventoryCanvasPrefab(gridPrefab, equipmentPrefab);
        }

        // 이전 버전에서 만든 InventoryCanvas에는 열기·닫기 효과용 CanvasGroup 참조가 없을 수 있습니다.
        // 프리팹의 기존 이미지와 배치는 건드리지 않고 필요한 컴포넌트와 참조만 보충해 사용자 디자인을 그대로 보존합니다.
        private static GameObject UpgradeInventoryCanvasAnimation(string path)
        {
            GameObject root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                InventoryHud hud = root.GetComponent<InventoryHud>();
                if (hud == null) return AssetDatabase.LoadAssetAtPath<GameObject>(path);

                var serializedHud = new SerializedObject(hud);
                SerializedProperty panelProperty = serializedHud.FindProperty("inventoryPanel");
                GameObject panel = panelProperty.objectReferenceValue as GameObject;
                if (panel == null)
                {
                    Transform namedPanel = root.transform.Find("InventoryPanel");
                    panel = namedPanel != null ? namedPanel.gameObject : null;
                    panelProperty.objectReferenceValue = panel;
                }
                if (panel == null) return AssetDatabase.LoadAssetAtPath<GameObject>(path);

                CanvasGroup panelGroup = panel.GetComponent<CanvasGroup>();
                if (panelGroup == null) panelGroup = panel.AddComponent<CanvasGroup>();
                serializedHud.FindProperty("inventoryPanelGroup").objectReferenceValue = panelGroup;
                serializedHud.FindProperty("inventoryPanelRect").objectReferenceValue = panel.transform as RectTransform;

                // 기존 아래쪽 고정 설명 패널을 삭제하지 않고 DragLayer 자식으로 옮겨 팝업 배경과 글꼴 디자인을 그대로 재사용합니다.
                Text detailText = serializedHud.FindProperty("itemDetailText").objectReferenceValue as Text;
                RectTransform dragRoot = serializedHud.FindProperty("dragLayer").objectReferenceValue as RectTransform;
                RectTransform detailPanel = detailText != null ? detailText.transform.parent as RectTransform : null;
                ItemDetailPopup popup = root.GetComponent<ItemDetailPopup>();
                if (popup == null) popup = root.AddComponent<ItemDetailPopup>();
                if (detailPanel != null && dragRoot != null)
                {
                    popup.Configure(detailPanel, detailText, dragRoot);
                    serializedHud.FindProperty("itemDetailPopup").objectReferenceValue = popup;
                }

                WeaponManagementPanel weaponPanel = EnsureWeaponManagementPanel(panel.transform, false);
                serializedHud.FindProperty("weaponManagementPanel").objectReferenceValue = weaponPanel;
                EnsureInventoryQuickSlotPanel(panel.transform);
                if (dragRoot != null) dragRoot.SetAsLastSibling();

                Text controls = panel.transform.Find("Controls")?.GetComponent<Text>();
                if (controls != null && !controls.text.Contains("팝업 상단 드래그"))
                    controls.text += "  |  아이템 길게 누르기 정보 · 팝업 상단 드래그";
                if (controls != null && !controls.text.Contains("퀵슬롯으로 드래그"))
                    controls.text += "  |  소모품을 퀵슬롯으로 드래그";
                serializedHud.ApplyModifiedPropertiesWithoutUndo();

                Canvas canvas = root.GetComponent<Canvas>();
                if (canvas != null) canvas.sortingOrder = 20;
                PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
            return AssetDatabase.LoadAssetAtPath<GameObject>(path);
        }

        // 빈 Image 하나를 프리팹으로 저장해 격자 바닥과 아이템 점유 칸의 공통 모양 원본으로 사용합니다.
        // 나중에 이 프리팹의 Source Image를 바꾸면 런타임에 복제되는 모든 칸에 같은 그림이 적용됩니다.
        private static Image CreateImagePrefab(string name, string path, Color color)
        {
            GameObject root = CreateUiObject(name, null);
            Image image = root.AddComponent<Image>();
            image.color = color;
            image.raycastTarget = true;
            GameObject saved = PrefabUtility.SaveAsPrefabAsset(root, path);
            Object.DestroyImmediate(root);
            return saved.GetComponent<Image>();
        }

        // 아이템 하나의 바운딩 영역, 실제 모양 칸을 담을 레이어와 이름/수량 Text를 가진 프리팹을 만듭니다.
        // 루트에는 전체 사각형 Image를 넣지 않아 비정형 아이템의 빈 칸이 마우스 입력을 가로막지 않게 합니다.
        private static InventoryItemView CreateInventoryItemPrefab(Image itemCellPrefab)
        {
            GameObject root = CreateUiObject("InventoryItem", null);
            RectTransform rootRect = (RectTransform)root.transform;
            SetTopLeft(rootRect, Vector2.zero, new Vector2(50f, 50f));
            root.AddComponent<CanvasGroup>();
            InventoryItemView view = root.AddComponent<InventoryItemView>();

            RectTransform occupiedLayer = (RectTransform)CreateUiObject("OccupiedCells", root.transform).transform;
            Stretch(occupiedLayer);
            Text label = CreateUiText("ItemLabel", root.transform, "아이템\nx1", 13, TextAnchor.UpperLeft);
            Stretch(label.rectTransform, new Vector2(5f, 4f), new Vector2(-5f, -4f));
            label.horizontalOverflow = HorizontalWrapMode.Wrap;
            label.verticalOverflow = VerticalWrapMode.Truncate;
            label.raycastTarget = false;
            view.Configure(occupiedLayer, itemCellPrefab, label);

            GameObject saved = PrefabUtility.SaveAsPrefabAsset(root, Root + "/Prefabs/UI/InventoryItem.prefab");
            Object.DestroyImmediate(root);
            return saved.GetComponent<InventoryItemView>();
        }

        // 장비 슬롯의 배경 Image와 가운데 설명 Text를 하나의 재사용 프리팹으로 묶습니다.
        // 실제 주무기/보조무기 같은 슬롯 종류는 전체 Canvas를 조립할 때 각 인스턴스에 따로 지정합니다.
        private static EquipmentSlotView CreateEquipmentSlotPrefab()
        {
            GameObject root = CreateUiObject("EquipmentSlot", null);
            RectTransform rootRect = (RectTransform)root.transform;
            SetTopLeft(rootRect, Vector2.zero, new Vector2(205f, 64f));
            Image background = root.AddComponent<Image>();
            background.color = new Color(0.13f, 0.16f, 0.18f, 0.96f);

            Text label = CreateUiText("Label", root.transform, "장비\n-", 15, TextAnchor.MiddleCenter);
            Stretch(label.rectTransform, new Vector2(6f, 4f), new Vector2(-6f, -4f));
            label.raycastTarget = false;

            EquipmentSlotView view = root.AddComponent<EquipmentSlotView>();
            view.Configure(EquipmentSlotType.None, background, label);
            GameObject saved = PrefabUtility.SaveAsPrefabAsset(root, Root + "/Prefabs/UI/EquipmentSlot.prefab");
            Object.DestroyImmediate(root);
            return saved.GetComponent<EquipmentSlotView>();
        }

        // 제목과 자동 정리 버튼 아래에 셀 레이어와 아이템 레이어가 겹쳐진 공용 격자 프리팹을 만듭니다.
        // CellLayer는 빈 칸 배경, ItemLayer는 실제 아이템만 담당하여 그림 순서와 마우스 판정을 예측하기 쉽게 유지합니다.
        private static InventoryGridView CreateInventoryGridPrefab(Image gridCellPrefab, InventoryItemView itemPrefab)
        {
            GameObject root = CreateUiObject("InventoryGrid", null);
            RectTransform rootRect = (RectTransform)root.transform;
            SetTopLeft(rootRect, Vector2.zero, new Vector2(540f, 350f));
            InventoryGridView gridView = root.AddComponent<InventoryGridView>();

            Text title = CreateUiText("Title", root.transform, "인벤토리", 18, TextAnchor.MiddleLeft);
            SetTopLeft(title.rectTransform, new Vector2(0f, 0f), new Vector2(300f, 32f));

            GameObject sortObject = CreateUiObject("AutoSortButton", root.transform);
            RectTransform sortRect = (RectTransform)sortObject.transform;
            sortRect.anchorMin = new Vector2(1f, 1f);
            sortRect.anchorMax = new Vector2(1f, 1f);
            sortRect.pivot = new Vector2(1f, 1f);
            sortRect.anchoredPosition = Vector2.zero;
            sortRect.sizeDelta = new Vector2(112f, 32f);
            Image sortImage = sortObject.AddComponent<Image>();
            sortImage.color = new Color(0.2f, 0.31f, 0.29f, 1f);
            Button sortButton = sortObject.AddComponent<Button>();
            sortButton.targetGraphic = sortImage;
            Text sortLabel = CreateUiText("Text", sortObject.transform, "자동 정리", 14, TextAnchor.MiddleCenter);
            Stretch(sortLabel.rectTransform);
            sortLabel.raycastTarget = false;

            GameObject areaObject = CreateUiObject("GridArea", root.transform);
            RectTransform area = (RectTransform)areaObject.transform;
            SetTopLeft(area, new Vector2(0f, -42f), new Vector2(500f, 300f));
            Image areaBackground = areaObject.AddComponent<Image>();
            areaBackground.color = new Color(0.03f, 0.04f, 0.045f, 0.9f);
            areaBackground.raycastTarget = true;

            RectTransform cellLayer = (RectTransform)CreateUiObject("CellLayer", area).transform;
            RectTransform itemLayer = (RectTransform)CreateUiObject("ItemLayer", area).transform;
            SetTopLeft(cellLayer, Vector2.zero, area.sizeDelta);
            SetTopLeft(itemLayer, Vector2.zero, area.sizeDelta);
            gridView.Configure(area, cellLayer, itemLayer, gridCellPrefab, itemPrefab, title, sortButton);

            GameObject saved = PrefabUtility.SaveAsPrefabAsset(root, Root + "/Prefabs/UI/InventoryGrid.prefab");
            Object.DestroyImmediate(root);
            return saved.GetComponent<InventoryGridView>();
        }

        // 1600x900 기준 화면에 장비 슬롯, 루트 격자, 가방/조끼 격자와 상호작용 안내를 배치합니다.
        // CanvasScaler가 실제 해상도에 맞춰 전체를 함께 확대/축소하므로 해상도가 바뀌어도 칸 비율과 입력 좌표가 같이 유지됩니다.
        private static GameObject CreateInventoryCanvasPrefab(InventoryGridView gridPrefab, EquipmentSlotView equipmentPrefab)
        {
            GameObject root = CreateUiObject("InventoryCanvas", null);
            Canvas canvas = root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            // 인벤토리는 전투 HUD보다 위에 떠야 하므로 더 높은 정렬 순서를 사용합니다.
            canvas.sortingOrder = 20;
            CanvasScaler scaler = root.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1600f, 900f);
            scaler.matchWidthOrHeight = 0.5f;
            root.AddComponent<GraphicRaycaster>();
            InventoryHud hud = root.AddComponent<InventoryHud>();

            GameObject panel = CreateUiObject("InventoryPanel", root.transform);
            RectTransform panelRect = (RectTransform)panel.transform;
            Stretch(panelRect);
            Image panelBackground = panel.AddComponent<Image>();
            panelBackground.color = new Color(0.025f, 0.035f, 0.04f, 0.96f);

            Text header = CreateUiText(
                "Controls",
                panel.transform,
                "Tab 닫기  |  좌클릭 선택/드래그  |  소모품을 아래 퀵슬롯으로 드래그  |  슬롯 우클릭 해제  |  아이템 길게 누르기 정보  |  Ctrl+좌클릭 빠른 이동  |  Shift+좌클릭 절반 분할  |  R 회전  |  우클릭 사용/장착  |  Delete 두 번 버리기",
                18,
                TextAnchor.MiddleLeft);
            SetTopLeft(header.rectTransform, new Vector2(36f, -24f), new Vector2(1480f, 42f));

            Text equipmentTitle = CreateUiText("EquipmentTitle", panel.transform, "장비", 20, TextAnchor.MiddleLeft);
            // 왼쪽 위 190×408 영역은 전신 그림에 양보하고 장비 목록은 그 바로 아래에서 시작합니다.
            SetTopLeft(equipmentTitle.rectTransform, new Vector2(40f, -520f), new Vector2(210f, 30f));

            EquipmentSlotType[] slotTypes = {
                EquipmentSlotType.PrimaryWeapon,
                EquipmentSlotType.SecondaryWeapon,
                EquipmentSlotType.Armor,
                EquipmentSlotType.Rig,
                EquipmentSlotType.Backpack
            };
            var slotViews = new EquipmentSlotView[slotTypes.Length];
            for (int i = 0; i < slotTypes.Length; i++)
            {
                GameObject slotObject = (GameObject)PrefabUtility.InstantiatePrefab(equipmentPrefab.gameObject);
                slotObject.name = slotTypes[i] + "Slot";
                slotObject.transform.SetParent(panel.transform, false);
                // 다섯 슬롯을 58픽셀 높이와 4픽셀 간격으로 배치하면 아이콘 높이를 유지하면서 화면 안에 모두 들어갑니다.
                SetTopLeft((RectTransform)slotObject.transform, new Vector2(40f, -556f - i * 62f), new Vector2(205f, 58f));
                PrefabUtility.RecordPrefabInstancePropertyModifications(slotObject.transform);
                EquipmentSlotView slotView = slotObject.GetComponent<EquipmentSlotView>();
                Text label = slotObject.GetComponentInChildren<Text>();
                slotView.Configure(slotTypes[i], slotObject.GetComponent<Image>(), label);
                PrefabUtility.RecordPrefabInstancePropertyModifications(slotView);
                slotViews[i] = slotView;
            }

            GameObject rootGridObject = (GameObject)PrefabUtility.InstantiatePrefab(gridPrefab.gameObject);
            rootGridObject.name = "RootInventoryGrid";
            rootGridObject.transform.SetParent(panel.transform, false);
            SetTopLeft((RectTransform)rootGridObject.transform, new Vector2(285f, -86f), new Vector2(540f, 350f));
            PrefabUtility.RecordPrefabInstancePropertyModifications(rootGridObject.transform);
            InventoryGridView rootGrid = rootGridObject.GetComponent<InventoryGridView>();

            GameObject containerGridObject = (GameObject)PrefabUtility.InstantiatePrefab(gridPrefab.gameObject);
            containerGridObject.name = "EquippedContainerGrid";
            containerGridObject.transform.SetParent(panel.transform, false);
            SetTopLeft((RectTransform)containerGridObject.transform, new Vector2(880f, -86f), new Vector2(540f, 350f));
            PrefabUtility.RecordPrefabInstancePropertyModifications(containerGridObject.transform);
            InventoryGridView containerGrid = containerGridObject.GetComponent<InventoryGridView>();

            RectTransform dragLayer = (RectTransform)CreateUiObject("DragLayer", panel.transform).transform;
            Stretch(dragLayer);
            dragLayer.SetAsLastSibling();

            Text statusText = CreateUiText("InteractionStatus", root.transform, string.Empty, 17, TextAnchor.MiddleCenter);
            SetBottomCenter(statusText.rectTransform, new Vector2(0f, 106f), new Vector2(600f, 38f));
            statusText.gameObject.SetActive(false);

            Text promptText = CreateUiText("InteractionPrompt", root.transform, string.Empty, 19, TextAnchor.MiddleCenter);
            SetBottomCenter(promptText.rectTransform, new Vector2(0f, 58f), new Vector2(540f, 42f));
            promptText.gameObject.AddComponent<Outline>().effectColor = new Color(0f, 0f, 0f, 0.85f);
            promptText.gameObject.SetActive(false);

            // 이 패널은 화면 아래를 계속 차지하지 않고, 아이템을 길게 누르면 DragLayer 위에 고정되는 팝업으로 표시됩니다.
            // ItemDetailPopup이 제목 바·닫기 버튼·바깥 클릭 영역을 Canvas 자식으로 보충하므로 Inspector에서 각각 디자인할 수 있습니다.
            GameObject detailPanel = CreateUiObject("ItemDetailPanel", panel.transform);
            SetTopLeft((RectTransform)detailPanel.transform, Vector2.zero, new Vector2(360f, 140f));
            Image detailBackground = detailPanel.AddComponent<Image>();
            detailBackground.color = new Color(0.045f, 0.06f, 0.07f, 0.98f);
            detailBackground.raycastTarget = true;
            Text detailText = CreateUiText("ItemDetailText", detailPanel.transform, "아이템을 길게 누르면 상세 정보가 표시됩니다.", 17, TextAnchor.UpperLeft);
            Stretch(detailText.rectTransform, new Vector2(18f, 14f), new Vector2(-18f, -14f));
            detailText.horizontalOverflow = HorizontalWrapMode.Wrap;
            detailText.verticalOverflow = VerticalWrapMode.Truncate;
            detailText.raycastTarget = false;
            WeaponManagementPanel weaponPanel = EnsureWeaponManagementPanel(panel.transform, false);
            EnsureInventoryQuickSlotPanel(panel.transform);
            dragLayer.SetAsLastSibling();

            hud.ConfigureViews(panel, dragLayer, rootGrid, containerGridObject, containerGrid, slotViews, promptText, statusText, detailText);
            hud.ConfigureWeaponManagementPanel(weaponPanel);
            GameObject saved = PrefabUtility.SaveAsPrefabAsset(root, Root + "/Prefabs/UI/InventoryCanvas.prefab");
            Object.DestroyImmediate(root);
            return saved;
        }

        // 전투 중 항상 보이는 정보와 상황에 따라 나타나는 피격·탈출·결과 화면을 하나의 Canvas 프리팹으로 묶습니다.
        // 기능별 MonoBehaviour는 같은 루트에 있지만 서로 다른 자식 View만 제어하므로 한 화면을 수정해도 다른 기능의 계산에는 영향을 주지 않습니다.
        private static GameObject CreateRaidHudCanvasPrefab()
        {
            const string path = Root + "/Prefabs/UI/RaidHudCanvas.prefab";
            GameObject existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (existing != null && existing.transform.Find("RaidTimer") != null)
            {
                RectTransform existingHealth = existing.transform.Find("Health") as RectTransform;
                if (existingHealth == null || existingHealth.sizeDelta.x < 800f)
                    UpgradeRaidHudForSurvival(path);
                // 기존 HUD 프리팹에도 상태 이상 표시와 출혈 테두리만 추가하고 사용자가 수정한 나머지 배치는 유지합니다.
                UpgradeRaidHudForPlayerCondition(path);
                EnsureTutorialHud(path, true);
                return EnsureQuickSlotHud(path);
            }

            GameObject root = CreateCanvasRoot("RaidHudCanvas", 0);
            AmmoHud ammoHud = root.AddComponent<AmmoHud>();
            ExtractionHud extractionHud = root.AddComponent<ExtractionHud>();
            PlayerDamageHud damageHud = root.AddComponent<PlayerDamageHud>();
            RaidResultHud resultHud = root.AddComponent<RaidResultHud>();
            PlayerConditionHud conditionHud = root.AddComponent<PlayerConditionHud>();

            // 화면 왼쪽 위에는 플레이 중 자주 확인하는 기본 조작법을 작게 고정합니다.
            Text guideText = CreateUiText(
                "ControlGuide",
                root.transform,
                "[현재 지역]  WASD 이동  |  Shift 달리기  |  Space 구르기(기력 25%)  |  좌클릭 발사  |  R 재장전  |  휠 무기 교체  |  1~0 퀵슬롯  |  Tab 인벤토리",
                16,
                TextAnchor.MiddleLeft);
            // 현재 지역 이름까지 붙으므로 중앙 타이머와 같은 줄을 피하고, 긴 한글 조작 문구가 잘리지 않도록 두 번째 줄에 넓게 배치합니다.
            SetTopLeft(guideText.rectTransform, new Vector2(24f, -58f), new Vector2(1100f, 32f));
            guideText.raycastTarget = false;
            guideText.gameObject.AddComponent<Outline>().effectColor = new Color(0f, 0f, 0f, 0.85f);

            // 무기와 탄약은 시야를 가리지 않으면서 빠르게 읽을 수 있도록 오른쪽 위에 크게 배치합니다.
            Text weaponText = CreateUiText("Weapon", root.transform, "[2] 권총", 20, TextAnchor.MiddleRight);
            SetTopRight(weaponText.rectTransform, new Vector2(-28f, -22f), new Vector2(380f, 34f));
            weaponText.raycastTarget = false;
            Text ammoText = CreateUiText("Ammo", root.transform, "15 / 45", 32, TextAnchor.MiddleRight);
            SetTopRight(ammoText.rectTransform, new Vector2(-28f, -58f), new Vector2(320f, 48f));
            ammoText.raycastTarget = false;

            // 체력은 전투 중 시선 이동이 짧도록 화면 왼쪽 아래에 고정합니다.
            Text healthText = CreateUiText("Health", root.transform, "HP 100 / 100  |  포만 75/100  |  기력 100/100", 23, TextAnchor.MiddleLeft);
            SetBottomLeft(healthText.rectTransform, new Vector2(28f, 24f), new Vector2(820f, 48f));
            healthText.raycastTarget = false;
            healthText.gameObject.AddComponent<Outline>().effectColor = new Color(0f, 0f, 0f, 0.9f);

            // 작은 상태 이상 창은 화면 오른쪽에 두어 왼쪽 체력과 중앙 탈출 안내를 가리지 않습니다.
            // 출혈·골절 여부와 실제 이동·조준·재장전 배율을 함께 보여 줘 필요한 의료품을 바로 판단할 수 있습니다.
            EnsurePlayerConditionPanel(root, conditionHud);

            // 화면 위 중앙에는 제한 없는 레이드의 경과 시간과 처치·최소 시간 조건을 계속 표시합니다.
            Text timerText = CreateUiText("RaidTimer", root.transform, "레이드 시간 00:00  |  처치 0/3  |  탈출 대기 25초", 20, TextAnchor.MiddleCenter);
            SetTopCenter(timerText.rectTransform, new Vector2(0f, -18f), new Vector2(720f, 38f));
            timerText.raycastTarget = false;
            timerText.gameObject.AddComponent<Outline>().effectColor = new Color(0f, 0f, 0f, 0.9f);

            // 탈출 안내는 상태 색상을 보여 주는 왼쪽 강조선과 문구를 한 패널로 묶습니다.
            GameObject extractionPanel = CreateUiObject("ExtractionPanel", root.transform);
            SetBottomCenter((RectTransform)extractionPanel.transform, new Vector2(0f, 24f), new Vector2(660f, 52f));
            Image extractionBackground = extractionPanel.AddComponent<Image>();
            extractionBackground.color = new Color(0.025f, 0.035f, 0.04f, 0.9f);
            extractionBackground.raycastTarget = false;
            GameObject accentObject = CreateUiObject("StateAccent", extractionPanel.transform);
            RectTransform accentRect = (RectTransform)accentObject.transform;
            accentRect.anchorMin = Vector2.zero;
            accentRect.anchorMax = new Vector2(0f, 1f);
            accentRect.pivot = new Vector2(0f, 0.5f);
            accentRect.sizeDelta = new Vector2(7f, 0f);
            Image accent = accentObject.AddComponent<Image>();
            accent.color = new Color(0.8f, 0.25f, 0.22f, 1f);
            accent.raycastTarget = false;
            Text extractionText = CreateUiText("Message", extractionPanel.transform, "탈출 조건", 19, TextAnchor.MiddleCenter);
            Stretch(extractionText.rectTransform, new Vector2(18f, 4f), new Vector2(-12f, -4f));
            extractionText.raycastTarget = false;

            // 피격 Image는 알파가 0일 때 꺼지고 피해 이벤트가 들어온 순간에만 화면 전체를 붉게 덮습니다.
            GameObject damageOverlayObject = CreateUiObject("DamageOverlay", root.transform);
            Stretch((RectTransform)damageOverlayObject.transform);
            Image damageOverlay = damageOverlayObject.AddComponent<Image>();
            damageOverlay.color = new Color(0.75f, 0.02f, 0.01f, 0f);
            damageOverlay.raycastTarget = false;

            // 레이드 종료 패널은 가장 마지막 형제로 두어 다른 전투 정보 위에 표시합니다.
            GameObject resultPanel = CreateUiObject("RaidResultPanel", root.transform);
            Stretch((RectTransform)resultPanel.transform);
            Image resultBackground = resultPanel.AddComponent<Image>();
            resultBackground.color = new Color(0.015f, 0.02f, 0.025f, 0.92f);
            resultBackground.raycastTarget = false;
            Text resultTitle = CreateUiText("ResultTitle", resultPanel.transform, "탈출 성공", 52, TextAnchor.MiddleCenter);
            SetCentered(resultTitle.rectTransform, new Vector2(0f, 80f), new Vector2(800f, 80f));
            resultTitle.raycastTarget = false;
            Text resultDetail = CreateUiText("ResultDetail", resultPanel.transform, string.Empty, 25, TextAnchor.UpperCenter);
            SetCentered(resultDetail.rectTransform, new Vector2(0f, -65f), new Vector2(760f, 180f));
            resultDetail.raycastTarget = false;

            ammoHud.ConfigureViews(weaponText, ammoText, healthText, guideText);
            extractionHud.ConfigureViews(extractionPanel, extractionText, accent);
            damageHud.ConfigureView(damageOverlay);
            resultHud.ConfigureViews(resultPanel, resultTitle, resultDetail, timerText);

            GameObject saved = PrefabUtility.SaveAsPrefabAsset(root, path);
            Object.DestroyImmediate(root);
            EnsureTutorialHud(path, true);
            return EnsureQuickSlotHud(path);
        }

        // 레이드와 거점이 함께 사용하는 ESC 메뉴를 하나의 높은 정렬 순서 Canvas로 만듭니다.
        // 설정은 슬라이더 대신 -/+ 버튼을 사용해 마우스 드래그 없이도 정확한 단계로 바꿀 수 있고, 각 기능 연결은 PauseMenuHud가 담당합니다.
        private static GameObject CreatePauseCanvasPrefab()
        {
            const string path = Root + "/Prefabs/UI/PauseCanvas.prefab";
            GameObject root = CreateCanvasRoot("PauseCanvas", 200);
            PauseMenuHud hud = root.AddComponent<PauseMenuHud>();

            GameObject pausePanel = CreateUiObject("PausePanel", root.transform);
            Stretch((RectTransform)pausePanel.transform);

            // 반투명 배경이 월드를 어둡게 만들고 raycast를 받아 뒤쪽 인벤토리나 시설 버튼이 눌리지 않게 합니다.
            GameObject backgroundObject = CreateUiObject("Background", pausePanel.transform);
            Stretch((RectTransform)backgroundObject.transform);
            Image background = backgroundObject.AddComponent<Image>();
            background.color = new Color(0.01f, 0.012f, 0.01f, 0.82f);
            background.raycastTarget = true;

            GameObject mainPanel = CreateUiObject("PauseMainPanel", pausePanel.transform);
            SetCentered((RectTransform)mainPanel.transform, Vector2.zero, new Vector2(520f, 690f));
            Image mainBackground = mainPanel.AddComponent<Image>();
            mainBackground.color = new Color(0.055f, 0.062f, 0.052f, 0.98f);

            Text title = CreateUiText("Title", mainPanel.transform, "일시정지", 42, TextAnchor.MiddleCenter);
            SetTopLeft(title.rectTransform, new Vector2(40f, -34f), new Vector2(440f, 70f));
            Text contextText = CreateUiText("Context", mainPanel.transform, "거점 일시정지", 20, TextAnchor.MiddleCenter);
            SetTopLeft(contextText.rectTransform, new Vector2(40f, -108f), new Vector2(440f, 38f));

            Button continueButton = CreateUiButton("ContinueButton", mainPanel.transform, "계속하기", out _);
            SetTopLeft((RectTransform)continueButton.transform, new Vector2(60f, -168f), new Vector2(400f, 58f));
            Button settingsButton = CreateUiButton("SettingsButton", mainPanel.transform, "설정", out _);
            SetTopLeft((RectTransform)settingsButton.transform, new Vector2(60f, -242f), new Vector2(400f, 58f));
            Button returnButton = CreateUiButton("ReturnButton", mainPanel.transform, "시작 화면으로", out Text returnButtonText);
            SetTopLeft((RectTransform)returnButton.transform, new Vector2(60f, -316f), new Vector2(400f, 58f));
            Button resetSaveButton = CreateUiButton("ResetSaveButton", mainPanel.transform, "새 게임 / 저장 초기화", out _);
            SetTopLeft((RectTransform)resetSaveButton.transform, new Vector2(60f, -390f), new Vector2(400f, 58f));
            Button quitButton = CreateUiButton("QuitButton", mainPanel.transform, "게임 종료", out _);
            SetTopLeft((RectTransform)quitButton.transform, new Vector2(60f, -464f), new Vector2(400f, 58f));
            Text escapeGuide = CreateUiText("Guide", mainPanel.transform, "ESC  뒤로 / 계속", 16, TextAnchor.MiddleCenter);
            SetTopLeft(escapeGuide.rectTransform, new Vector2(60f, -560f), new Vector2(400f, 32f));

            GameObject settingsPanel = CreateUiObject("SettingsPanel", pausePanel.transform);
            SetCentered((RectTransform)settingsPanel.transform, Vector2.zero, new Vector2(820f, 920f));
            Image settingsBackground = settingsPanel.AddComponent<Image>();
            settingsBackground.color = new Color(0.055f, 0.062f, 0.052f, 0.98f);
            Text settingsTitle = CreateUiText("Title", settingsPanel.transform, "설정", 38, TextAnchor.MiddleCenter);
            SetTopLeft(settingsTitle.rectTransform, new Vector2(40f, -28f), new Vector2(740f, 64f));

            CreatePauseSettingRow(settingsPanel.transform, "MasterVolume", "전체 음량", -112f, out Button masterDown, out Text masterValue, out Button masterUp);
            CreatePauseSettingRow(settingsPanel.transform, "EffectsVolume", "효과음 음량", -190f, out Button effectsDown, out Text effectsValue, out Button effectsUp);
            CreatePauseSettingRow(settingsPanel.transform, "MouseSensitivity", "주시 감도", -268f, out Button mouseDown, out Text mouseValue, out Button mouseUp);
            CreatePauseSettingRow(settingsPanel.transform, "AimSensitivity", "정밀 조준 감도", -346f, out Button aimDown, out Text aimValue, out Button aimUp);
            CreatePauseSettingRow(settingsPanel.transform, "Resolution", "해상도", -424f, out Button resolutionPrevious, out Text resolutionValue, out Button resolutionNext);
            CreatePauseSettingRow(settingsPanel.transform, "FontScale", "글자 크기", -502f, out Button fontScaleDown, out Text fontScaleValue, out Button fontScaleUp);
            CreatePauseSettingRow(settingsPanel.transform, "DamageEffect", "피격 화면 효과", -580f, out Button damageEffectDown, out Text damageEffectValue, out Button damageEffectUp);
            CreatePauseSettingRow(settingsPanel.transform, "ScreenShake", "화면 흔들림", -658f, out Button screenShakeDown, out Text screenShakeValue, out Button screenShakeUp);

            Text fullscreenTitle = CreateUiText("FullscreenTitle", settingsPanel.transform, "화면 모드", 20, TextAnchor.MiddleLeft);
            SetTopLeft(fullscreenTitle.rectTransform, new Vector2(54f, -736f), new Vector2(280f, 52f));
            Button fullscreenButton = CreateUiButton("FullscreenButton", settingsPanel.transform, "전체 화면", out Text fullscreenValue);
            SetTopLeft((RectTransform)fullscreenButton.transform, new Vector2(352f, -736f), new Vector2(398f, 52f));

            Button tutorialReplayButton = CreateUiButton("TutorialReplayButton", settingsPanel.transform, "튜토리얼 다시 보기", out _);
            SetTopLeft((RectTransform)tutorialReplayButton.transform, new Vector2(70f, -822f), new Vector2(300f, 58f));
            Button settingsBackButton = CreateUiButton("SettingsBackButton", settingsPanel.transform, "뒤로", out _);
            SetTopLeft((RectTransform)settingsBackButton.transform, new Vector2(450f, -822f), new Vector2(300f, 58f));

            GameObject confirmationPanel = CreateUiObject("ConfirmationPanel", pausePanel.transform);
            SetCentered((RectTransform)confirmationPanel.transform, Vector2.zero, new Vector2(680f, 330f));
            Image confirmationBackground = confirmationPanel.AddComponent<Image>();
            confirmationBackground.color = new Color(0.07f, 0.055f, 0.045f, 0.99f);
            Text confirmationTitle = CreateUiText("Title", confirmationPanel.transform, "확인", 34, TextAnchor.MiddleCenter);
            SetTopLeft(confirmationTitle.rectTransform, new Vector2(32f, -24f), new Vector2(616f, 58f));
            Text confirmationText = CreateUiText("Description", confirmationPanel.transform, string.Empty, 20, TextAnchor.MiddleCenter);
            SetTopLeft(confirmationText.rectTransform, new Vector2(42f, -94f), new Vector2(596f, 110f));
            confirmationText.horizontalOverflow = HorizontalWrapMode.Wrap;
            confirmationText.verticalOverflow = VerticalWrapMode.Overflow;
            Button confirmButton = CreateUiButton("ConfirmButton", confirmationPanel.transform, "확인", out _);
            SetTopLeft((RectTransform)confirmButton.transform, new Vector2(70f, -238f), new Vector2(250f, 58f));
            Button cancelButton = CreateUiButton("CancelButton", confirmationPanel.transform, "취소", out _);
            SetTopLeft((RectTransform)cancelButton.transform, new Vector2(360f, -238f), new Vector2(250f, 58f));

            hud.ConfigureViews(new PauseMenuHud.ViewReferences
            {
                pausePanel = pausePanel,
                mainPanel = mainPanel,
                settingsPanel = settingsPanel,
                confirmationPanel = confirmationPanel,
                contextText = contextText,
                masterVolumeText = masterValue,
                effectsVolumeText = effectsValue,
                mouseSensitivityText = mouseValue,
                aimSensitivityText = aimValue,
                resolutionText = resolutionValue,
                fullscreenText = fullscreenValue,
                fontScaleText = fontScaleValue,
                damageEffectText = damageEffectValue,
                screenShakeText = screenShakeValue,
                confirmationText = confirmationText,
                returnButtonText = returnButtonText,
                continueButton = continueButton,
                settingsButton = settingsButton,
                returnButton = returnButton,
                resetSaveButton = resetSaveButton,
                quitButton = quitButton,
                masterVolumeDownButton = masterDown,
                masterVolumeUpButton = masterUp,
                effectsVolumeDownButton = effectsDown,
                effectsVolumeUpButton = effectsUp,
                mouseSensitivityDownButton = mouseDown,
                mouseSensitivityUpButton = mouseUp,
                aimSensitivityDownButton = aimDown,
                aimSensitivityUpButton = aimUp,
                resolutionPreviousButton = resolutionPrevious,
                resolutionNextButton = resolutionNext,
                fullscreenButton = fullscreenButton,
                fontScaleDownButton = fontScaleDown,
                fontScaleUpButton = fontScaleUp,
                damageEffectDownButton = damageEffectDown,
                damageEffectUpButton = damageEffectUp,
                screenShakeDownButton = screenShakeDown,
                screenShakeUpButton = screenShakeUp,
                tutorialReplayButton = tutorialReplayButton,
                settingsBackButton = settingsBackButton,
                confirmButton = confirmButton,
                cancelButton = cancelButton
            });

            GameObject saved = PrefabUtility.SaveAsPrefabAsset(root, path);
            Object.DestroyImmediate(root);
            return saved;
        }

        // 음량·감도·해상도처럼 좌우로 값을 바꾸는 설정 한 줄을 같은 간격과 크기로 생성합니다.
        // valueText만 PauseMenuHud가 현재 저장값으로 갱신하고 제목과 -/+ 버튼은 프리팹에서 자유롭게 꾸밀 수 있습니다.
        private static void CreatePauseSettingRow(
            Transform parent,
            string name,
            string titleValue,
            float top,
            out Button decreaseButton,
            out Text valueText,
            out Button increaseButton)
        {
            Text rowTitle = CreateUiText(name + "Title", parent, titleValue, 20, TextAnchor.MiddleLeft);
            SetTopLeft(rowTitle.rectTransform, new Vector2(54f, top), new Vector2(280f, 52f));
            decreaseButton = CreateUiButton(name + "DownButton", parent, "-", out _);
            SetTopLeft((RectTransform)decreaseButton.transform, new Vector2(352f, top), new Vector2(62f, 52f));
            valueText = CreateUiText(name + "Value", parent, "100%", 20, TextAnchor.MiddleCenter);
            SetTopLeft(valueText.rectTransform, new Vector2(428f, top), new Vector2(246f, 52f));
            increaseButton = CreateUiButton(name + "UpButton", parent, "+", out _);
            SetTopLeft((RectTransform)increaseButton.transform, new Vector2(688f, top), new Vector2(62f, 52f));
        }

        // 기존 HUD 프리팹의 이미지와 배치를 전부 다시 만들지 않고 체력 Text의 너비만 넓혀 포만감·기력 문구가 잘리지 않게 합니다.
        // LoadPrefabContents로 프리팹 편집 복사본을 연 뒤 정확한 Health 자식만 바꾸므로 사용자가 수정한 다른 UI 디자인은 유지됩니다.
        private static GameObject UpgradeRaidHudForSurvival(string path)
        {
            GameObject root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                RectTransform healthRect = root.transform.Find("Health") as RectTransform;
                if (healthRect != null)
                {
                    healthRect.sizeDelta = new Vector2(820f, Mathf.Max(48f, healthRect.sizeDelta.y));
                    Text healthText = healthRect.GetComponent<Text>();
                    if (healthText != null) healthText.fontSize = Mathf.Min(healthText.fontSize, 23);
                }

                PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }

            return AssetDatabase.LoadAssetAtPath<GameObject>(path);
        }

        // 이미 저장된 RaidHudCanvas에 상태 이상 컴포넌트·패널과 출혈 테두리를 추가하거나 현재 구조로 갱신합니다.
        // 프리팹 편집 복사본 안에서 작업하므로 씬 인스턴스가 아니라 원본 에셋과 이후 생성되는 모든 학교 씬에 반영됩니다.
        private static GameObject UpgradeRaidHudForPlayerCondition(string path)
        {
            GameObject root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                PlayerConditionHud conditionHud = root.GetComponent<PlayerConditionHud>();
                if (conditionHud == null) conditionHud = root.AddComponent<PlayerConditionHud>();
                EnsurePlayerConditionPanel(root, conditionHud);
                PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }

            return AssetDatabase.LoadAssetAtPath<GameObject>(path);
        }

        // 인벤토리 아래에 소모품을 직접 드롭할 수 있는 열 칸 퀵슬롯 바를 추가합니다.
        // 두 칸은 기본 해금되고 나머지는 방탄복에 설정된 보너스에 따라 잠금 표시가 바뀌며, 우클릭은 등록만 해제합니다.
        private static QuickSlotHud EnsureInventoryQuickSlotPanel(Transform inventoryPanel)
        {
            if (inventoryPanel == null) return null;
            Transform existing = inventoryPanel.Find("InventoryQuickSlotPanel");
            bool created = existing == null;
            GameObject panel = created
                ? CreateUiObject("InventoryQuickSlotPanel", inventoryPanel)
                : existing.gameObject;
            RectTransform panelRect = (RectTransform)panel.transform;
            if (created)
            {
                panelRect.anchorMin = new Vector2(0.5f, 0f);
                panelRect.anchorMax = new Vector2(0.5f, 0f);
                panelRect.pivot = new Vector2(0.5f, 0f);
                panelRect.anchoredPosition = new Vector2(0f, 18f);
                panelRect.sizeDelta = new Vector2(720f, 126f);
            }
            Image background = panel.GetComponent<Image>();
            if (background == null) background = panel.AddComponent<Image>();
            if (created) background.color = new Color(0.02f, 0.028f, 0.032f, 0.97f);
            background.raycastTarget = true;

            Text title = panel.transform.Find("Title")?.GetComponent<Text>();
            if (title == null)
            {
                title = CreateUiText("Title", panel.transform, "퀵슬롯 · 소모품을 슬롯으로 드래그 / 우클릭 해제", 15, TextAnchor.MiddleLeft);
                SetTopLeft(title.rectTransform, new Vector2(12f, -3f), new Vector2(410f, 26f));
                title.raycastTarget = false;
            }
            Text message = panel.transform.Find("Message")?.GetComponent<Text>();
            if (message == null)
            {
                message = CreateUiText("Message", panel.transform, string.Empty, 14, TextAnchor.MiddleRight);
                SetTopRight(message.rectTransform, new Vector2(-12f, -3f), new Vector2(300f, 26f));
                message.color = SurvivalUiTheme.Warning;
                message.raycastTarget = false;
            }

            QuickSlotView[] views = EnsureQuickSlotViews(panel.transform, 32f);
            QuickSlotHud hud = panel.GetComponent<QuickSlotHud>();
            if (hud == null) hud = panel.AddComponent<QuickSlotHud>();
            hud.ConfigureBar(views, null, message, false, true);
            return hud;
        }

        // 필드와 인벤토리가 같은 프리팹 구조를 공유하도록 열 개 슬롯 자식의 생성과 참조 연결을 한곳에서 처리합니다.
        // 슬롯은 실제 Image Raycast 대상이므로 InventoryHud가 드래그 종료 위치에서 QuickSlotView를 안정적으로 찾을 수 있습니다.
        private static QuickSlotView[] EnsureQuickSlotViews(Transform panel, float topOffset)
        {
            var views = new QuickSlotView[QuickSlotAssignmentStore.SlotCount];
            for (int index = 0; index < views.Length; index++)
            {
                string key = PlayerQuickSlots.GetSlotKeyLabel(index);
                string objectName = "Slot_" + key;
                Transform existing = panel.Find(objectName);
                GameObject slot = existing != null ? existing.gameObject : CreateUiObject(objectName, panel);
                RectTransform slotRect = (RectTransform)slot.transform;
                SetTopLeft(slotRect, new Vector2(12f + index * 68f, -topOffset), new Vector2(64f, 76f));

                Image slotBackground = slot.GetComponent<Image>();
                if (slotBackground == null) slotBackground = slot.AddComponent<Image>();
                slotBackground.raycastTarget = true;

                Image icon = slot.transform.Find("Icon")?.GetComponent<Image>();
                if (icon == null)
                {
                    GameObject iconObject = CreateUiObject("Icon", slot.transform);
                    icon = iconObject.AddComponent<Image>();
                    Stretch(icon.rectTransform, new Vector2(9f, 20f), new Vector2(-9f, -22f));
                    icon.raycastTarget = false;
                }
                Text keyText = slot.transform.Find("Key")?.GetComponent<Text>();
                if (keyText == null)
                {
                    keyText = CreateUiText("Key", slot.transform, key, 14, TextAnchor.UpperLeft);
                    SetTopLeft(keyText.rectTransform, new Vector2(5f, -3f), new Vector2(20f, 18f));
                    keyText.color = SurvivalUiTheme.Accent;
                    keyText.raycastTarget = false;
                }
                Text count = slot.transform.Find("Count")?.GetComponent<Text>();
                if (count == null)
                {
                    count = CreateUiText("Count", slot.transform, string.Empty, 13, TextAnchor.UpperRight);
                    SetTopRight(count.rectTransform, new Vector2(-4f, -3f), new Vector2(32f, 18f));
                    count.raycastTarget = false;
                }
                Text name = slot.transform.Find("Name")?.GetComponent<Text>();
                if (name == null)
                {
                    name = CreateUiText("Name", slot.transform, "비어 있음", 10, TextAnchor.LowerCenter);
                    SetTopLeft(name.rectTransform, new Vector2(3f, -53f), new Vector2(58f, 19f));
                    name.horizontalOverflow = HorizontalWrapMode.Wrap;
                    name.verticalOverflow = VerticalWrapMode.Truncate;
                    name.raycastTarget = false;
                }

                QuickSlotView view = slot.GetComponent<QuickSlotView>();
                if (view == null) view = slot.AddComponent<QuickSlotView>();
                view.Configure(slotBackground, icon, keyText, name, count);
                views[index] = view;
            }
            return views;
        }

        // 기존 레이드 HUD의 배치와 이미지를 보존하면서 Canvas 안에 항상 보이는 열 칸 퀵슬롯 패널을 보강합니다.
        // 필드에서는 숫자 키뿐 아니라 열린 슬롯을 좌클릭해도 사용할 수 있고, 등록 변경은 인벤토리 화면에서만 허용합니다.
        private static GameObject EnsureQuickSlotHud(string path)
        {
            GameObject root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                QuickSlotHud hud = root.GetComponent<QuickSlotHud>();
                if (hud == null) hud = root.AddComponent<QuickSlotHud>();

                Transform existingPanel = root.transform.Find("QuickSlotPanel");
                bool createdPanel = existingPanel == null;
                GameObject panel = createdPanel
                    ? CreateUiObject("QuickSlotPanel", root.transform)
                    : existingPanel.gameObject;
                RectTransform panelRect = (RectTransform)panel.transform;
                if (createdPanel)
                {
                    // 중앙 탈출 안내보다 위, 화면 오른쪽 아래에 놓아 체력·상태 이상 창과 겹치지 않게 합니다.
                    panelRect.anchorMin = new Vector2(1f, 0f);
                    panelRect.anchorMax = new Vector2(1f, 0f);
                    panelRect.pivot = new Vector2(1f, 0f);
                    panelRect.anchoredPosition = new Vector2(-24f, 82f);
                    panelRect.sizeDelta = new Vector2(704f, 144f);
                }
                panelRect.sizeDelta = new Vector2(Mathf.Max(704f, panelRect.sizeDelta.x), Mathf.Max(144f, panelRect.sizeDelta.y));
                Image background = panel.GetComponent<Image>();
                if (background == null) background = panel.AddComponent<Image>();
                if (createdPanel) background.color = new Color(0.025f, 0.035f, 0.04f, 0.9f);
                background.raycastTarget = false;

                Text slots = panel.transform.Find("Slots")?.GetComponent<Text>();
                if (slots != null) slots.gameObject.SetActive(false);
                QuickSlotView[] views = EnsureQuickSlotViews(panel.transform, 8f);
                Text effects = panel.transform.Find("Effects")?.GetComponent<Text>();
                if (effects == null)
                {
                    effects = CreateUiText("Effects", panel.transform, string.Empty, 16, TextAnchor.MiddleRight);
                    SetTopLeft(effects.rectTransform, new Vector2(12f, -86f), new Vector2(680f, 24f));
                    effects.color = new Color(0.48f, 0.95f, 0.68f, 1f);
                    effects.raycastTarget = false;
                }
                Text message = panel.transform.Find("Message")?.GetComponent<Text>();
                if (message == null)
                {
                    message = CreateUiText("Message", panel.transform, string.Empty, 15, TextAnchor.MiddleRight);
                    SetTopLeft(message.rectTransform, new Vector2(12f, -112f), new Vector2(680f, 24f));
                    message.color = new Color(0.94f, 0.84f, 0.43f, 1f);
                    message.raycastTarget = false;
                }

                // 기존 Text의 사용자가 바꾼 폰트와 색은 보존하되 새 슬롯 아래에 들어오도록 위치만 최신 구조에 맞춥니다.
                SetTopLeft(effects.rectTransform, new Vector2(12f, -86f), new Vector2(680f, 24f));
                SetTopLeft(message.rectTransform, new Vector2(12f, -112f), new Vector2(680f, 24f));
                hud.ConfigureBar(views, effects, message, true, false);

                // 예전에 만든 HUD 프리팹에는 아직 숫자 1·2가 무기 교체라고 적혀 있을 수 있습니다.
                // 프리팹 전체를 재생성하지 않고 안내 문구만 새 입력 규칙으로 바꿔 사용자가 실제 조작과 다른 설명을 보지 않게 합니다.
                Text controlGuide = root.transform.Find("ControlGuide")?.GetComponent<Text>();
                if (controlGuide != null)
                    controlGuide.text = "[현재 지역]  WASD 이동  |  Shift 달리기  |  Space 구르기(기력 25%)  |  좌클릭 발사  |  R 재장전  |  휠 무기 교체  |  1~0 퀵슬롯  |  Tab 인벤토리";
                PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
            return AssetDatabase.LoadAssetAtPath<GameObject>(path);
        }

        // 새 HUD와 이전 HUD 업그레이드가 동일한 상태 이상 창 구조를 사용하도록 생성 코드를 한곳에 둡니다.
        // 이미 패널이 있으면 옛 네 부위 문구를 제거하고 새 참조를 다시 연결하므로 여러 번 실행해도 중복 UI가 생기지 않습니다.
        private static void EnsurePlayerConditionPanel(GameObject root, PlayerConditionHud conditionHud)
        {
            if (root == null || conditionHud == null) return;

            Transform existing = root.transform.Find("PlayerConditionPanel");
            GameObject panel = existing != null ? existing.gameObject : CreateUiObject("PlayerConditionPanel", root.transform);
            RectTransform panelRect = (RectTransform)panel.transform;
            panelRect.anchorMin = Vector2.right;
            panelRect.anchorMax = Vector2.right;
            panelRect.pivot = Vector2.right;
            panelRect.anchoredPosition = new Vector2(-24f, 88f);
            panelRect.sizeDelta = new Vector2(360f, 144f);

            Image background = panel.GetComponent<Image>();
            if (background == null) background = panel.AddComponent<Image>();
            background.color = new Color(0.025f, 0.035f, 0.04f, 0.9f);
            background.raycastTarget = false;

            // 삭제된 부위별 상태 Text가 기존 프리팹에 남으면 화면과 Hierarchy 모두 혼란스러우므로 정확한 옛 자식만 정리합니다.
            string[] obsoletePartLabels = { "Head", "Torso", "Arms", "Legs" };
            foreach (string label in obsoletePartLabels)
            {
                Transform obsolete = panel.transform.Find(label);
                if (obsolete != null) Object.DestroyImmediate(obsolete.gameObject);
            }

            Text title = GetOrCreateConditionText("Title", panel.transform, "상태 이상", 19, new Vector2(16f, -10f), new Vector2(328f, 28f));
            title.color = SurvivalUiTheme.Accent;
            Text summary = GetOrCreateConditionText("Summary", panel.transform, "상태 정상", 14, new Vector2(16f, -38f), new Vector2(328f, 42f));
            Text bleeding = GetOrCreateConditionText("Bleeding", panel.transform, "출혈  없음", 16, new Vector2(16f, -82f), new Vector2(328f, 25f));
            Text fracture = GetOrCreateConditionText("Fracture", panel.transform, "골절  없음", 16, new Vector2(16f, -110f), new Vector2(328f, 25f));
            CanvasGroup bleedingBorder = EnsureBleedingScreenBorder(root);
            conditionHud.ConfigureViews(panel, summary, bleeding, fracture, bleedingBorder);
        }

        // 출혈 경고는 화면 중앙을 덮는 단색 오버레이가 아니라 네 가장자리만 감싸는 별도 CanvasGroup으로 만듭니다.
        // HUD 스크립트는 이 그룹의 활성화와 알파만 바꾸므로 나중에 각 Image의 색·두께·스프라이트를 Inspector에서 자유롭게 수정할 수 있습니다.
        private static CanvasGroup EnsureBleedingScreenBorder(GameObject root)
        {
            Transform existing = root.transform.Find("BleedingScreenBorder");
            GameObject border = existing != null ? existing.gameObject : CreateUiObject("BleedingScreenBorder", root.transform);
            RectTransform borderRect = (RectTransform)border.transform;
            Stretch(borderRect);
            border.transform.SetAsLastSibling();

            CanvasGroup group = border.GetComponent<CanvasGroup>();
            if (group == null) group = border.AddComponent<CanvasGroup>();
            group.alpha = 0f;
            group.interactable = false;
            group.blocksRaycasts = false;

            Color edgeColor = new Color(0.72f, 0.02f, 0.01f, 0.9f);
            ConfigureBleedingBorderEdge(border.transform, "Top", new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, 30f), edgeColor);
            ConfigureBleedingBorderEdge(border.transform, "Bottom", Vector2.zero, new Vector2(1f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 30f), edgeColor);
            ConfigureBleedingBorderEdge(border.transform, "Left", Vector2.zero, new Vector2(0f, 1f), new Vector2(0f, 0.5f), new Vector2(30f, 0f), edgeColor);
            ConfigureBleedingBorderEdge(border.transform, "Right", Vector2.right, Vector2.one, new Vector2(1f, 0.5f), new Vector2(30f, 0f), edgeColor);
            return group;
        }

        // 같은 생성 함수를 네 번 사용해 화면 비율이 바뀌어도 위·아래는 가로로, 좌·우는 세로로 끝까지 늘어납니다.
        private static void ConfigureBleedingBorderEdge(
            Transform parent,
            string name,
            Vector2 anchorMin,
            Vector2 anchorMax,
            Vector2 pivot,
            Vector2 size,
            Color color)
        {
            Transform existing = parent.Find(name);
            GameObject edge = existing != null ? existing.gameObject : CreateUiObject(name, parent);
            RectTransform rect = (RectTransform)edge.transform;
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.pivot = pivot;
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = size;

            Image image = edge.GetComponent<Image>();
            if (image == null) image = edge.AddComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
        }

        private static Text GetOrCreateConditionText(
            string name,
            Transform parent,
            string value,
            int fontSize,
            Vector2 position,
            Vector2 size)
        {
            Transform existing = parent.Find(name);
            Text text = existing != null ? existing.GetComponent<Text>() : null;
            if (text == null) text = CreateUiText(name, parent, value, fontSize, TextAnchor.MiddleLeft);
            text.text = value;
            text.fontSize = fontSize;
            text.alignment = TextAnchor.MiddleLeft;
            text.raycastTarget = false;
            SetTopLeft(text.rectTransform, position, size);
            return text;
        }

        // 게임 실행 직후 보이는 별도 타이틀 Canvas를 일반 uGUI 오브젝트로 구성합니다.
        // 제목, 버튼, 설정 행을 모두 프리팹 자식으로 저장하므로 나중에 Inspector에서 위치·글꼴·이미지를 직접 바꿀 수 있습니다.
        private static GameObject CreateTitleCanvasPrefab()
        {
            const string path = Root + "/Prefabs/UI/TitleCanvas.prefab";
            GameObject root = CreateCanvasRoot("TitleCanvas", 0);
            TitleMenuHud hud = root.AddComponent<TitleMenuHud>();
            CreateFullScreenBackground(root.transform, new Color(0.012f, 0.018f, 0.022f, 1f));

            GameObject mainPanel = CreateUiObject("MainMenuPanel", root.transform);
            SetCentered((RectTransform)mainPanel.transform, Vector2.zero, new Vector2(820f, 760f));
            Image mainBackground = mainPanel.AddComponent<Image>();
            mainBackground.color = new Color(0.04f, 0.055f, 0.06f, 0.97f);

            Text title = CreateUiText("Title", mainPanel.transform, TitleMenuHud.WorkingGameTitle, 58, TextAnchor.MiddleCenter);
            title.fontStyle = FontStyle.Bold;
            title.raycastTarget = false;
            SetTopCenter(title.rectTransform, new Vector2(0f, -42f), new Vector2(740f, 82f));
            Text subtitle = CreateUiText("Subtitle", mainPanel.transform, TitleMenuHud.WorkingGameSubtitle, 24, TextAnchor.MiddleCenter);
            subtitle.fontStyle = FontStyle.Bold;
            subtitle.raycastTarget = false;
            SetTopCenter(subtitle.rectTransform, new Vector2(0f, -122f), new Vector2(700f, 40f));
            Text tagline = CreateUiText("Tagline", mainPanel.transform, TitleMenuHud.WorkingGameTagline, 16, TextAnchor.MiddleCenter);
            tagline.raycastTarget = false;
            SetTopCenter(tagline.rectTransform, new Vector2(0f, -162f), new Vector2(700f, 32f));

            GameObject dividerObject = CreateUiObject("TitleDivider", mainPanel.transform);
            Image divider = dividerObject.AddComponent<Image>();
            divider.color = SurvivalUiTheme.AccentSoft;
            divider.raycastTarget = false;
            SetTopCenter(divider.rectTransform, new Vector2(0f, -208f), new Vector2(320f, 3f));

            Button startButton = CreateUiButton("StartGameButton", mainPanel.transform, "게임 시작", out _);
            SetTopCenter((RectTransform)startButton.transform, new Vector2(0f, -248f), new Vector2(420f, 64f));
            Button loadButton = CreateUiButton("LoadGameButton", mainPanel.transform, "게임 로드", out _);
            SetTopCenter((RectTransform)loadButton.transform, new Vector2(0f, -326f), new Vector2(420f, 64f));
            Button settingsButton = CreateUiButton("SettingsButton", mainPanel.transform, "설정", out _);
            SetTopCenter((RectTransform)settingsButton.transform, new Vector2(0f, -404f), new Vector2(420f, 64f));
            Button quitButton = CreateUiButton("QuitButton", mainPanel.transform, "종료", out _);
            SetTopCenter((RectTransform)quitButton.transform, new Vector2(0f, -482f), new Vector2(420f, 64f));

            Text status = CreateUiText("Status", mainPanel.transform, "저장 데이터를 확인하는 중입니다...", 17, TextAnchor.MiddleCenter);
            status.horizontalOverflow = HorizontalWrapMode.Wrap;
            status.verticalOverflow = VerticalWrapMode.Overflow;
            status.raycastTarget = false;
            SetTopCenter(status.rectTransform, new Vector2(0f, -574f), new Vector2(680f, 74f));
            Text buildLabel = CreateUiText("BuildLabel", mainPanel.transform, "PROTOTYPE BUILD", 13, TextAnchor.MiddleCenter);
            buildLabel.raycastTarget = false;
            SetBottomCenter(buildLabel.rectTransform, new Vector2(0f, 24f), new Vector2(620f, 24f));

            GameObject settingsPanel = CreateUiObject("SettingsPanel", root.transform);
            SetCentered((RectTransform)settingsPanel.transform, Vector2.zero, new Vector2(860f, 880f));
            Image settingsBackground = settingsPanel.AddComponent<Image>();
            settingsBackground.color = new Color(0.04f, 0.055f, 0.06f, 0.99f);
            Text settingsTitle = CreateUiText("Title", settingsPanel.transform, "설정", 38, TextAnchor.MiddleCenter);
            SetTopLeft(settingsTitle.rectTransform, new Vector2(40f, -22f), new Vector2(780f, 58f));

            CreatePauseSettingRow(settingsPanel.transform, "MasterVolume", "전체 음량", -94f, out Button masterDown, out Text masterValue, out Button masterUp);
            CreatePauseSettingRow(settingsPanel.transform, "EffectsVolume", "효과음 음량", -166f, out Button effectsDown, out Text effectsValue, out Button effectsUp);
            CreatePauseSettingRow(settingsPanel.transform, "MouseSensitivity", "주시 감도", -238f, out Button mouseDown, out Text mouseValue, out Button mouseUp);
            CreatePauseSettingRow(settingsPanel.transform, "AimSensitivity", "정밀 조준 감도", -310f, out Button aimDown, out Text aimValue, out Button aimUp);
            CreatePauseSettingRow(settingsPanel.transform, "Resolution", "해상도", -382f, out Button resolutionPrevious, out Text resolutionValue, out Button resolutionNext);
            CreatePauseSettingRow(settingsPanel.transform, "FontScale", "글자 크기", -454f, out Button fontScaleDown, out Text fontScaleValue, out Button fontScaleUp);
            CreatePauseSettingRow(settingsPanel.transform, "DamageEffect", "피격 화면 효과", -526f, out Button damageEffectDown, out Text damageEffectValue, out Button damageEffectUp);
            CreatePauseSettingRow(settingsPanel.transform, "ScreenShake", "화면 흔들림", -598f, out Button screenShakeDown, out Text screenShakeValue, out Button screenShakeUp);

            Text fullscreenTitle = CreateUiText("FullscreenTitle", settingsPanel.transform, "화면 모드", 20, TextAnchor.MiddleLeft);
            SetTopLeft(fullscreenTitle.rectTransform, new Vector2(54f, -670f), new Vector2(280f, 52f));
            Button fullscreenButton = CreateUiButton("FullscreenButton", settingsPanel.transform, "전체 화면", out Text fullscreenValue);
            SetTopLeft((RectTransform)fullscreenButton.transform, new Vector2(352f, -670f), new Vector2(398f, 52f));
            Button settingsBackButton = CreateUiButton("SettingsBackButton", settingsPanel.transform, "뒤로", out _);
            SetBottomCenter((RectTransform)settingsBackButton.transform, new Vector2(0f, 28f), new Vector2(300f, 58f));

            GameObject confirmationPanel = CreateUiObject("ConfirmationPanel", root.transform);
            SetCentered((RectTransform)confirmationPanel.transform, Vector2.zero, new Vector2(680f, 330f));
            Image confirmationBackground = confirmationPanel.AddComponent<Image>();
            confirmationBackground.color = new Color(0.07f, 0.055f, 0.045f, 0.995f);
            Text confirmationTitle = CreateUiText("Title", confirmationPanel.transform, "확인", 34, TextAnchor.MiddleCenter);
            SetTopLeft(confirmationTitle.rectTransform, new Vector2(32f, -24f), new Vector2(616f, 58f));
            Text confirmationText = CreateUiText("Description", confirmationPanel.transform, string.Empty, 20, TextAnchor.MiddleCenter);
            confirmationText.horizontalOverflow = HorizontalWrapMode.Wrap;
            confirmationText.verticalOverflow = VerticalWrapMode.Overflow;
            SetTopLeft(confirmationText.rectTransform, new Vector2(42f, -90f), new Vector2(596f, 116f));
            Button confirmButton = CreateUiButton("ConfirmButton", confirmationPanel.transform, "확인", out _);
            SetTopLeft((RectTransform)confirmButton.transform, new Vector2(70f, -238f), new Vector2(250f, 58f));
            Button cancelButton = CreateUiButton("CancelButton", confirmationPanel.transform, "취소", out _);
            SetTopLeft((RectTransform)cancelButton.transform, new Vector2(360f, -238f), new Vector2(250f, 58f));

            hud.ConfigureViews(new TitleMenuHud.ViewReferences
            {
                mainPanel = mainPanel,
                settingsPanel = settingsPanel,
                confirmationPanel = confirmationPanel,
                statusText = status,
                startGameButton = startButton,
                loadGameButton = loadButton,
                settingsButton = settingsButton,
                quitButton = quitButton,
                masterVolumeText = masterValue,
                effectsVolumeText = effectsValue,
                mouseSensitivityText = mouseValue,
                aimSensitivityText = aimValue,
                resolutionText = resolutionValue,
                fullscreenText = fullscreenValue,
                fontScaleText = fontScaleValue,
                damageEffectText = damageEffectValue,
                screenShakeText = screenShakeValue,
                masterVolumeDownButton = masterDown,
                masterVolumeUpButton = masterUp,
                effectsVolumeDownButton = effectsDown,
                effectsVolumeUpButton = effectsUp,
                mouseSensitivityDownButton = mouseDown,
                mouseSensitivityUpButton = mouseUp,
                aimSensitivityDownButton = aimDown,
                aimSensitivityUpButton = aimUp,
                resolutionPreviousButton = resolutionPrevious,
                resolutionNextButton = resolutionNext,
                fullscreenButton = fullscreenButton,
                fontScaleDownButton = fontScaleDown,
                fontScaleUpButton = fontScaleUp,
                damageEffectDownButton = damageEffectDown,
                damageEffectUpButton = damageEffectUp,
                screenShakeDownButton = screenShakeDown,
                screenShakeUpButton = screenShakeUp,
                settingsBackButton = settingsBackButton,
                confirmationText = confirmationText,
                confirmButton = confirmButton,
                cancelButton = cancelButton
            });

            GameObject saved = PrefabUtility.SaveAsPrefabAsset(root, path);
            Object.DestroyImmediate(root);
            return saved;
        }

        // 전체 화면 원화와 화면 아래에서 살짝 띄운 진행 막대를 가진 공용 Loading Canvas를 만듭니다.
        // 배경 Image와 진행 UI를 프리팹의 실제 자식으로 저장하므로 이후 Inspector에서 위치·색·폰트를 자유롭게 수정할 수 있습니다.
        private static GameObject CreateLoadingCanvasPrefab(LoadingScreenCatalog catalog)
        {
            const string path = Root + "/Prefabs/UI/LoadingCanvas.prefab";
            GameObject root = CreateCanvasRoot("LoadingCanvas", 0);
            LoadingScreenHud hud = root.AddComponent<LoadingScreenHud>();

            // AspectRatioFitter의 EnvelopeParent가 원본 비율을 보존한 채 화면을 꽉 채우고 넘치는 가장자리만 잘라 냅니다.
            GameObject artworkObject = CreateUiObject("LoadingArtwork", root.transform);
            Stretch((RectTransform)artworkObject.transform);
            Image artwork = artworkObject.AddComponent<Image>();
            artwork.sprite = catalog != null && catalog.CommonImages.Count > 0 ? catalog.CommonImages[0] : null;
            artwork.color = artwork.sprite != null ? Color.white : new Color(0.025f, 0.035f, 0.04f, 1f);
            artwork.raycastTarget = false;
            AspectRatioFitter artworkFitter = artworkObject.AddComponent<AspectRatioFitter>();
            artworkFitter.aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;
            if (artwork.sprite != null && artwork.sprite.rect.height > 0f)
                artworkFitter.aspectRatio = artwork.sprite.rect.width / artwork.sprite.rect.height;

            // 원화가 밝아도 아래쪽 흰 글자와 청록색 진행 막대가 읽히도록 화면 전체에 얇은 암막을 겹칩니다.
            GameObject overlayObject = CreateUiObject("LoadingDimOverlay", root.transform);
            Stretch((RectTransform)overlayObject.transform);
            Image overlay = overlayObject.AddComponent<Image>();
            overlay.color = new Color(0.01f, 0.018f, 0.022f, 0.36f);
            overlay.raycastTarget = false;

            // 화면 끝에 붙이지 않고 58px 위에 놓아 16:9와 울트라와이드 모두에서 여유가 보이게 합니다.
            GameObject panel = CreateUiObject("LoadingPanel", root.transform);
            SetBottomCenter((RectTransform)panel.transform, new Vector2(0f, 58f), new Vector2(1120f, 146f));
            Image panelImage = panel.AddComponent<Image>();
            panelImage.sprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
            panelImage.type = Image.Type.Sliced;
            panelImage.color = new Color(0.02f, 0.035f, 0.04f, 0.9f);
            panelImage.raycastTarget = false;

            Text status = CreateUiText("Status", panel.transform, "이동할 지역을 준비하는 중입니다...", 20, TextAnchor.MiddleLeft);
            SetTopLeft(status.rectTransform, new Vector2(24f, -12f), new Vector2(850f, 34f));
            status.raycastTarget = false;

            GameObject frameObject = CreateUiObject("ProgressFrame", panel.transform);
            SetTopLeft((RectTransform)frameObject.transform, new Vector2(24f, -58f), new Vector2(1072f, 30f));
            Image frame = frameObject.AddComponent<Image>();
            frame.sprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
            frame.type = Image.Type.Sliced;
            frame.color = new Color(0.01f, 0.018f, 0.022f, 0.98f);
            frame.raycastTarget = false;

            GameObject fillObject = CreateUiObject("ProgressFill", frameObject.transform);
            Stretch((RectTransform)fillObject.transform, new Vector2(4f, 4f), new Vector2(-4f, -4f));
            Image fill = fillObject.AddComponent<Image>();
            fill.sprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
            fill.type = Image.Type.Filled;
            fill.fillMethod = Image.FillMethod.Horizontal;
            fill.fillOrigin = 0;
            fill.fillAmount = 0f;
            fill.color = new Color(0.2f, 0.82f, 0.68f, 1f);
            fill.raycastTarget = false;

            Text progress = CreateUiText("ProgressText", panel.transform, "0%", 18, TextAnchor.MiddleRight);
            SetTopLeft(progress.rectTransform, new Vector2(886f, -12f), new Vector2(210f, 34f));
            progress.raycastTarget = false;

            Text hint = CreateUiText("Hint", panel.transform, "해금된 지역의 이미지가 무작위로 표시됩니다.", 15, TextAnchor.MiddleLeft);
            SetTopLeft(hint.rectTransform, new Vector2(24f, -100f), new Vector2(1072f, 28f));
            hint.color = new Color(0.72f, 0.78f, 0.78f, 1f);
            hint.raycastTarget = false;

            hud.ConfigureViews(catalog, artwork, artworkFitter, fill, progress, status, hint);
            GameObject saved = PrefabUtility.SaveAsPrefabAsset(root, path);
            Object.DestroyImmediate(root);
            return saved;
        }

        // 창고와 다음 출격품을 좌우 격자로 나누고 상세 정보와 정리 기능을 둔 거점 인벤토리 화면을 만듭니다.
        // 실제 출발 버튼은 출격 터미널 Canvas에만 두어 창고 정리와 지역 선택의 역할을 화면 수준에서도 분리합니다.
        private static GameObject CreateHideoutCanvasPrefab()
        {
            const string path = Root + "/Prefabs/UI/HideoutCanvas.prefab";
            GameObject existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (existing != null &&
                existing.transform.Find("StashInventoryGrid") != null &&
                existing.transform.Find("LaunchRaidButton") == null)
                return UpgradeHideoutCanvasItemDetailPopup(path);

            InventoryGridView gridPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                Root + "/Prefabs/UI/InventoryGrid.prefab")?.GetComponent<InventoryGridView>();
            EquipmentSlotView equipmentPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                Root + "/Prefabs/UI/EquipmentSlot.prefab")?.GetComponent<EquipmentSlotView>();
            GameObject root = CreateCanvasRoot("HideoutCanvas", 0);
            HideoutResultHud hud = root.AddComponent<HideoutResultHud>();
            CreateFullScreenBackground(root.transform, new Color(0.025f, 0.035f, 0.04f, 0.98f));

            Text title = CreateUiText("Title", root.transform, "HIDEOUT · 내 인벤토리 / 창고", 32, TextAnchor.MiddleCenter);
            SetTopCenter(title.rectTransform, new Vector2(0f, -20f), new Vector2(720f, 52f));
            title.raycastTarget = false;
            Text summary = CreateUiText("ResultSummary", root.transform, "창고에서 출격 아이템을 선택하세요.", 18, TextAnchor.MiddleCenter);
            SetTopCenter(summary.rectTransform, new Vector2(0f, -72f), new Vector2(1300f, 58f));
            summary.raycastTarget = false;

            Text stashTitle = CreateUiText("StashTitle", root.transform, "내 인벤토리", 22, TextAnchor.MiddleLeft);
            SetTopLeft(stashTitle.rectTransform, new Vector2(48f, -128f), new Vector2(700f, 38f));
            stashTitle.raycastTarget = false;
            Text loadoutTitle = CreateUiText("LoadoutTitle", root.transform, "창고", 22, TextAnchor.MiddleLeft);
            SetTopLeft(loadoutTitle.rectTransform, new Vector2(852f, -128f), new Vector2(650f, 38f));
            loadoutTitle.raycastTarget = false;

            GameObject stashGridObject = (GameObject)PrefabUtility.InstantiatePrefab(gridPrefab.gameObject);
            stashGridObject.name = "StashInventoryGrid";
            stashGridObject.transform.SetParent(root.transform, false);
            SetTopLeft((RectTransform)stashGridObject.transform, new Vector2(48f, -168f), new Vector2(700f, 330f));
            PrefabUtility.RecordPrefabInstancePropertyModifications(stashGridObject.transform);
            InventoryGridView stashGrid = stashGridObject.GetComponent<InventoryGridView>();

            GameObject loadoutGridObject = (GameObject)PrefabUtility.InstantiatePrefab(gridPrefab.gameObject);
            loadoutGridObject.name = "LoadoutInventoryGrid";
            loadoutGridObject.transform.SetParent(root.transform, false);
            SetTopLeft((RectTransform)loadoutGridObject.transform, new Vector2(852f, -168f), new Vector2(650f, 570f));
            PrefabUtility.RecordPrefabInstancePropertyModifications(loadoutGridObject.transform);
            InventoryGridView loadoutGrid = loadoutGridObject.GetComponent<InventoryGridView>();

            // 플레이어 인벤토리 바로 아래에 장비 슬롯을 배치해 창고를 연 상태에서도 착용·해제와 가방 내부 열기가 가능합니다.
            Text equipmentTitle = CreateUiText("EquipmentTitle", root.transform, "장비 · 우클릭 해제 / 가방·조끼 좌클릭 내부 열기", 18, TextAnchor.MiddleLeft);
            SetTopLeft(equipmentTitle.rectTransform, new Vector2(48f, -506f), new Vector2(700f, 30f));
            equipmentTitle.raycastTarget = false;
            EquipmentSlotView[] equipmentSlots = CreateHideoutEquipmentSlots(root.transform, equipmentPrefab);

            // 거점도 길게 누른 뒤 유지되고 상단을 드래그할 수 있는 Canvas 팝업을 사용합니다.
            GameObject detailPanel = CreateUiObject("ItemDetailPanel", root.transform);
            SetTopLeft((RectTransform)detailPanel.transform, Vector2.zero, new Vector2(360f, 140f));
            Image detailBackground = detailPanel.AddComponent<Image>();
            detailBackground.color = new Color(0.05f, 0.07f, 0.08f, 0.98f);
            detailBackground.raycastTarget = true;
            Text detailText = CreateUiText("ItemDetailText", detailPanel.transform, "아이템을 길게 누르면 상세 정보가 표시됩니다.", 16, TextAnchor.UpperLeft);
            Stretch(detailText.rectTransform, new Vector2(16f, 12f), new Vector2(-16f, -12f));
            detailText.horizontalOverflow = HorizontalWrapMode.Wrap;
            detailText.verticalOverflow = VerticalWrapMode.Truncate;
            detailText.raycastTarget = false;

            Text operationStatus = CreateUiText("OperationStatus", root.transform, "좌클릭 선택/드래그 · 길게 누르기 정보 · 팝업 상단 드래그 · Ctrl+좌클릭 빠른 이동 · Shift+좌클릭 절반 분할 · R 회전 · Tab 닫기", 17, TextAnchor.MiddleCenter);
            SetBottomCenter(operationStatus.rectTransform, new Vector2(0f, 34f), new Vector2(1100f, 38f));
            operationStatus.raycastTarget = false;

            WeaponManagementPanel weaponPanel = EnsureWeaponManagementPanel(root.transform, true);

            RectTransform dragLayer = (RectTransform)CreateUiObject("DragLayer", root.transform).transform;
            Stretch(dragLayer);
            dragLayer.SetAsLastSibling();

            hud.ConfigureViews(
                summary,
                stashTitle,
                loadoutTitle,
                stashGrid,
                loadoutGrid,
                dragLayer,
                detailText,
                operationStatus);
            hud.ConfigureEquipmentViews(equipmentSlots);
            hud.ConfigureWeaponManagementPanel(weaponPanel);

            GameObject saved = PrefabUtility.SaveAsPrefabAsset(root, path);
            Object.DestroyImmediate(root);
            return saved;
        }

        // 기존 거점 Canvas의 두 격자와 사용자 디자인은 유지하면서 설명 패널만 DragLayer 팝업으로 전환합니다.
        private static GameObject UpgradeHideoutCanvasItemDetailPopup(string path)
        {
            GameObject root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                HideoutResultHud hud = root.GetComponent<HideoutResultHud>();
                if (hud == null) return AssetDatabase.LoadAssetAtPath<GameObject>(path);

                var serializedHud = new SerializedObject(hud);
                Text detailText = serializedHud.FindProperty("itemDetailText").objectReferenceValue as Text;
                RectTransform dragRoot = serializedHud.FindProperty("dragLayer").objectReferenceValue as RectTransform;
                RectTransform detailPanel = detailText != null ? detailText.transform.parent as RectTransform : null;
                ItemDetailPopup popup = root.GetComponent<ItemDetailPopup>();
                if (popup == null) popup = root.AddComponent<ItemDetailPopup>();
                if (detailPanel != null && dragRoot != null)
                {
                    popup.Configure(detailPanel, detailText, dragRoot);
                    serializedHud.FindProperty("itemDetailPopup").objectReferenceValue = popup;
                }

                WeaponManagementPanel weaponPanel = EnsureWeaponManagementPanel(root.transform, true);
                serializedHud.FindProperty("weaponManagementPanel").objectReferenceValue = weaponPanel;
                if (dragRoot != null) dragRoot.SetAsLastSibling();

                Text operationStatus = serializedHud.FindProperty("operationStatusText").objectReferenceValue as Text;
                if (operationStatus != null && !operationStatus.text.Contains("팝업 상단 드래그"))
                    operationStatus.text += " · 팝업 상단 드래그";

                // 예전 창고 프리팹은 왼쪽이 창고, 오른쪽이 출격 격자였으므로 위치는 유지한 채 의미와 높이를 새 구조로 바꿉니다.
                // 왼쪽 아래 공간에 장비 슬롯을 추가하고 오른쪽 창고는 전체 높이를 사용합니다.
                Text rootTitle = root.transform.Find("Title")?.GetComponent<Text>();
                if (rootTitle != null) rootTitle.text = "HIDEOUT · 내 인벤토리 / 창고";
                Text stashTitle = root.transform.Find("StashTitle")?.GetComponent<Text>();
                if (stashTitle != null) stashTitle.text = "내 인벤토리";
                Text loadoutTitle = root.transform.Find("LoadoutTitle")?.GetComponent<Text>();
                if (loadoutTitle != null) loadoutTitle.text = "창고";
                RectTransform playerGrid = root.transform.Find("StashInventoryGrid") as RectTransform;
                if (playerGrid != null) playerGrid.sizeDelta = new Vector2(700f, 330f);
                RectTransform storageGrid = root.transform.Find("LoadoutInventoryGrid") as RectTransform;
                if (storageGrid != null) storageGrid.sizeDelta = new Vector2(650f, 570f);

                Transform equipmentTitleTransform = root.transform.Find("EquipmentTitle");
                if (equipmentTitleTransform == null)
                {
                    Text equipmentTitle = CreateUiText(
                        "EquipmentTitle",
                        root.transform,
                        "장비 · 우클릭 해제 / 가방·조끼 좌클릭 내부 열기",
                        18,
                        TextAnchor.MiddleLeft);
                    SetTopLeft(equipmentTitle.rectTransform, new Vector2(48f, -506f), new Vector2(700f, 30f));
                    equipmentTitle.raycastTarget = false;
                }

                EquipmentSlotView equipmentPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                    Root + "/Prefabs/UI/EquipmentSlot.prefab")?.GetComponent<EquipmentSlotView>();
                EquipmentSlotView[] slots = new EquipmentSlotView[5];
                EquipmentSlotType[] slotTypes = {
                    EquipmentSlotType.PrimaryWeapon,
                    EquipmentSlotType.SecondaryWeapon,
                    EquipmentSlotType.Armor,
                    EquipmentSlotType.Rig,
                    EquipmentSlotType.Backpack
                };
                for (int i = 0; i < slotTypes.Length; i++)
                {
                    Transform existingSlot = root.transform.Find(slotTypes[i] + "HideoutSlot");
                    if (existingSlot != null) slots[i] = existingSlot.GetComponent<EquipmentSlotView>();
                }
                if (slots[0] == null && equipmentPrefab != null)
                    slots = CreateHideoutEquipmentSlots(root.transform, equipmentPrefab);
                serializedHud.FindProperty("equipmentSlotViews").arraySize = slots.Length;
                for (int i = 0; i < slots.Length; i++)
                    serializedHud.FindProperty("equipmentSlotViews").GetArrayElementAtIndex(i).objectReferenceValue = slots[i];

                serializedHud.ApplyModifiedPropertiesWithoutUndo();
                PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
            return AssetDatabase.LoadAssetAtPath<GameObject>(path);
        }

        private static EquipmentSlotView[] CreateHideoutEquipmentSlots(Transform parent, EquipmentSlotView equipmentPrefab)
        {
            EquipmentSlotType[] slotTypes = {
                EquipmentSlotType.PrimaryWeapon,
                EquipmentSlotType.SecondaryWeapon,
                EquipmentSlotType.Armor,
                EquipmentSlotType.Rig,
                EquipmentSlotType.Backpack
            };
            var result = new EquipmentSlotView[slotTypes.Length];
            if (equipmentPrefab == null) return result;

            for (int i = 0; i < slotTypes.Length; i++)
            {
                GameObject slotObject = (GameObject)PrefabUtility.InstantiatePrefab(equipmentPrefab.gameObject);
                slotObject.name = slotTypes[i] + "HideoutSlot";
                slotObject.transform.SetParent(parent, false);
                int column = i % 3;
                int row = i / 3;
                SetTopLeft(
                    (RectTransform)slotObject.transform,
                    new Vector2(48f + column * 226f, -540f - row * 66f),
                    new Vector2(214f, 58f));
                PrefabUtility.RecordPrefabInstancePropertyModifications(slotObject.transform);
                EquipmentSlotView view = slotObject.GetComponent<EquipmentSlotView>();
                Text label = slotObject.GetComponentInChildren<Text>();
                view.Configure(slotTypes[i], slotObject.GetComponent<Image>(), label);
                PrefabUtility.RecordPrefabInstancePropertyModifications(view);
                result[i] = view;
            }
            return result;
        }

        // 임무 선택과 수락·포기를 담당하는 전체 화면 Canvas를 실제 프리팹으로 만들어 Inspector에서 자유롭게 꾸밀 수 있게 합니다.
        private static GameObject CreateHideoutMissionCanvasPrefab()
        {
            const string path = Root + "/Prefabs/UI/HideoutMissionCanvas.prefab";
            GameObject existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (existing != null) return existing;

            GameObject root = CreateCanvasRoot("HideoutMissionCanvas", 10);
            HideoutMissionBoardHud hud = root.AddComponent<HideoutMissionBoardHud>();
            CreateFullScreenBackground(root.transform, new Color(0.018f, 0.025f, 0.03f, 0.94f));

            GameObject panel = CreateUiObject("MissionPanel", root.transform);
            SetCentered((RectTransform)panel.transform, Vector2.zero, new Vector2(900f, 610f));
            Image panelImage = panel.AddComponent<Image>();
            panelImage.color = new Color(0.07f, 0.09f, 0.1f, 0.99f);

            Text header = CreateUiText("Header", panel.transform, "임무 보드", 32, TextAnchor.MiddleCenter);
            SetTopCenter(header.rectTransform, new Vector2(0f, -28f), new Vector2(520f, 52f));
            header.raycastTarget = false;

            Text title = CreateUiText("MissionTitle", panel.transform, "등록된 임무 없음", 27, TextAnchor.MiddleCenter);
            SetTopCenter(title.rectTransform, new Vector2(0f, -104f), new Vector2(700f, 48f));
            title.raycastTarget = false;
            Text description = CreateUiText("Description", panel.transform, "임무를 선택하세요.", 20, TextAnchor.UpperLeft);
            SetCentered(description.rectTransform, new Vector2(0f, 72f), new Vector2(700f, 130f));
            description.horizontalOverflow = HorizontalWrapMode.Wrap;
            description.verticalOverflow = VerticalWrapMode.Truncate;
            description.raycastTarget = false;
            Text progress = CreateUiText("Progress", panel.transform, "목표 정보", 22, TextAnchor.MiddleCenter);
            SetCentered(progress.rectTransform, new Vector2(0f, -34f), new Vector2(700f, 90f));
            progress.raycastTarget = false;
            Text status = CreateUiText("Status", panel.transform, "받을 임무를 선택하세요.", 18, TextAnchor.MiddleCenter);
            SetCentered(status.rectTransform, new Vector2(0f, -116f), new Vector2(700f, 42f));
            status.raycastTarget = false;

            Text previousLabel;
            Button previous = CreateUiButton("PreviousButton", panel.transform, "이전", out previousLabel);
            SetBottomCenter((RectTransform)previous.transform, new Vector2(-300f, 108f), new Vector2(150f, 52f));
            Text nextLabel;
            Button next = CreateUiButton("NextButton", panel.transform, "다음", out nextLabel);
            SetBottomCenter((RectTransform)next.transform, new Vector2(300f, 108f), new Vector2(150f, 52f));
            Text acceptLabel;
            Button accept = CreateUiButton("AcceptButton", panel.transform, "임무 받기", out acceptLabel);
            SetBottomCenter((RectTransform)accept.transform, new Vector2(-105f, 108f), new Vector2(190f, 52f));
            Text abandonLabel;
            Button abandon = CreateUiButton("AbandonButton", panel.transform, "임무 포기", out abandonLabel);
            SetBottomCenter((RectTransform)abandon.transform, new Vector2(105f, 108f), new Vector2(190f, 52f));
            Text closeLabel;
            Button close = CreateUiButton("CloseButton", panel.transform, "닫기", out closeLabel);
            SetBottomCenter((RectTransform)close.transform, new Vector2(0f, 34f), new Vector2(220f, 52f));

            hud.Configure(null, null, title, description, progress, status, previous, next, accept, acceptLabel, abandon, close);
            GameObject saved = PrefabUtility.SaveAsPrefabAsset(root, path);
            Object.DestroyImmediate(root);
            return saved;
        }

        // 모든 시설 화면 위에 함께 사용할 레벨·효과·강화 비용 패널을 별도 Canvas 프리팹으로 만듭니다.
        // 배경 전체를 막지 않는 오른쪽 패널이므로 창고나 임무 보드의 본래 화면을 보면서도 바로 강화할 수 있습니다.
        private static GameObject CreateHideoutFacilityUpgradeCanvasPrefab()
        {
            const string path = Root + "/Prefabs/UI/HideoutFacilityUpgradeCanvas.prefab";
            GameObject existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (existing != null) return existing;

            GameObject root = CreateCanvasRoot("HideoutFacilityUpgradeCanvas", 20);
            HideoutFacilityUpgradeHud hud = root.AddComponent<HideoutFacilityUpgradeHud>();

            GameObject panel = CreateUiObject("UpgradePanel", root.transform);
            RectTransform panelRect = (RectTransform)panel.transform;
            panelRect.anchorMin = new Vector2(1f, 0.5f);
            panelRect.anchorMax = new Vector2(1f, 0.5f);
            panelRect.pivot = new Vector2(1f, 0.5f);
            panelRect.anchoredPosition = new Vector2(-24f, 0f);
            panelRect.sizeDelta = new Vector2(390f, 760f);
            Image panelImage = panel.AddComponent<Image>();
            panelImage.color = new Color(0.045f, 0.06f, 0.065f, 0.98f);

            Text title = CreateUiText("FacilityTitle", panel.transform, "시설 정보", 25, TextAnchor.MiddleCenter);
            SetTopCenter(title.rectTransform, new Vector2(0f, -22f), new Vector2(340f, 48f));
            Text description = CreateUiText("FacilityDescription", panel.transform, "시설 설명", 17, TextAnchor.UpperLeft);
            SetTopCenter(description.rectTransform, new Vector2(0f, -88f), new Vector2(340f, 92f));
            description.horizontalOverflow = HorizontalWrapMode.Wrap;
            description.verticalOverflow = VerticalWrapMode.Truncate;
            Text effect = CreateUiText("FacilityEffect", panel.transform, "시설 효과", 19, TextAnchor.MiddleLeft);
            SetTopCenter(effect.rectTransform, new Vector2(0f, -188f), new Vector2(340f, 42f));
            Text recipe = CreateUiText("Recipe", panel.transform, "제작 정보", 16, TextAnchor.UpperLeft);
            SetTopCenter(recipe.rectTransform, new Vector2(0f, -234f), new Vector2(340f, 132f));
            recipe.horizontalOverflow = HorizontalWrapMode.Wrap;
            recipe.verticalOverflow = VerticalWrapMode.Truncate;

            Text previousRecipeLabel;
            Button previousRecipe = CreateUiButton("PreviousRecipeButton", panel.transform, "이전 제작법", out previousRecipeLabel);
            SetBottomCenter((RectTransform)previousRecipe.transform, new Vector2(-92f, 330f), new Vector2(160f, 40f));
            Text nextRecipeLabel;
            Button nextRecipe = CreateUiButton("NextRecipeButton", panel.transform, "다음 제작법", out nextRecipeLabel);
            SetBottomCenter((RectTransform)nextRecipe.transform, new Vector2(92f, 330f), new Vector2(160f, 40f));
            Text craftLabel;
            Button craft = CreateUiButton("CraftButton", panel.transform, "제작", out craftLabel);
            SetBottomCenter((RectTransform)craft.transform, new Vector2(0f, 280f), new Vector2(250f, 44f));

            Text cost = CreateUiText("UpgradeCost", panel.transform, "다음 강화 비용", 17, TextAnchor.UpperLeft);
            SetBottomCenter(cost.rectTransform, new Vector2(0f, 150f), new Vector2(340f, 112f));
            cost.horizontalOverflow = HorizontalWrapMode.Wrap;
            cost.verticalOverflow = VerticalWrapMode.Truncate;
            Text status = CreateUiText("UpgradeStatus", panel.transform, "창고의 재료를 사용합니다.", 15, TextAnchor.UpperLeft);
            SetBottomCenter(status.rectTransform, new Vector2(0f, 94f), new Vector2(340f, 52f));
            status.horizontalOverflow = HorizontalWrapMode.Wrap;
            status.verticalOverflow = VerticalWrapMode.Truncate;

            Text upgradeLabel;
            Button upgrade = CreateUiButton("UpgradeButton", panel.transform, "시설 강화", out upgradeLabel);
            SetBottomCenter((RectTransform)upgrade.transform, new Vector2(0f, 48f), new Vector2(250f, 44f));
            Text closeLabel;
            Button close = CreateUiButton("CloseButton", panel.transform, "닫기", out closeLabel);
            SetBottomCenter((RectTransform)close.transform, new Vector2(0f, 2f), new Vector2(250f, 40f));

            hud.Configure(
                null,
                null,
                title,
                description,
                effect,
                cost,
                recipe,
                status,
                previousRecipe,
                nextRecipe,
                craft,
                craftLabel,
                upgrade,
                upgradeLabel,
                close);
            GameObject saved = PrefabUtility.SaveAsPrefabAsset(root, path);
            Object.DestroyImmediate(root);
            return saved;
        }

        // 상인 화면은 상품과 창고 아이템을 각각 한 개씩 선택하는 가벼운 프로토타입 구조입니다.
        // 최종 디자인 단계에서는 이 프리팹의 패널과 버튼 이미지만 교체해도 거래 계산 코드는 그대로 재사용할 수 있습니다.
        private static GameObject CreateHideoutTraderCanvasPrefab()
        {
            const string path = Root + "/Prefabs/UI/HideoutTraderCanvas.prefab";
            GameObject existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (existing != null) return existing;

            GameObject root = CreateCanvasRoot("HideoutTraderCanvas", 10);
            HideoutTraderHud hud = root.AddComponent<HideoutTraderHud>();
            CreateFullScreenBackground(root.transform, new Color(0.018f, 0.025f, 0.03f, 0.94f));

            GameObject panel = CreateUiObject("TraderPanel", root.transform);
            SetCentered((RectTransform)panel.transform, new Vector2(-150f, 0f), new Vector2(1040f, 650f));
            panel.AddComponent<Image>().color = new Color(0.055f, 0.075f, 0.085f, 0.99f);

            Text header = CreateUiText("Header", panel.transform, "보급 상인", 32, TextAnchor.MiddleCenter);
            SetTopCenter(header.rectTransform, new Vector2(0f, -24f), new Vector2(500f, 48f));
            Text credits = CreateUiText("Credits", panel.transform, "보유 크레딧", 19, TextAnchor.MiddleCenter);
            SetTopCenter(credits.rectTransform, new Vector2(0f, -76f), new Vector2(850f, 38f));

            Text market = CreateUiText("MarketItem", panel.transform, "상품", 20, TextAnchor.UpperLeft);
            SetTopLeft(market.rectTransform, new Vector2(48f, -142f), new Vector2(430f, 260f));
            market.horizontalOverflow = HorizontalWrapMode.Wrap;
            market.verticalOverflow = VerticalWrapMode.Truncate;
            Text stash = CreateUiText("StashItem", panel.transform, "창고", 20, TextAnchor.UpperLeft);
            SetTopLeft(stash.rectTransform, new Vector2(562f, -142f), new Vector2(430f, 260f));
            stash.horizontalOverflow = HorizontalWrapMode.Wrap;
            stash.verticalOverflow = VerticalWrapMode.Truncate;

            Text ignored;
            Button previousMarket = CreateUiButton("PreviousMarket", panel.transform, "이전 상품", out ignored);
            SetTopLeft((RectTransform)previousMarket.transform, new Vector2(48f, -420f), new Vector2(130f, 46f));
            Button nextMarket = CreateUiButton("NextMarket", panel.transform, "다음 상품", out ignored);
            SetTopLeft((RectTransform)nextMarket.transform, new Vector2(188f, -420f), new Vector2(130f, 46f));
            Button buyOne = CreateUiButton("BuyOne", panel.transform, "1개 구매", out ignored);
            SetTopLeft((RectTransform)buyOne.transform, new Vector2(48f, -478f), new Vector2(190f, 48f));
            Button buyStack = CreateUiButton("BuyStack", panel.transform, "묶음 구매", out ignored);
            SetTopLeft((RectTransform)buyStack.transform, new Vector2(248f, -478f), new Vector2(190f, 48f));

            Button previousStash = CreateUiButton("PreviousStash", panel.transform, "이전 물품", out ignored);
            SetTopLeft((RectTransform)previousStash.transform, new Vector2(562f, -420f), new Vector2(130f, 46f));
            Button nextStash = CreateUiButton("NextStash", panel.transform, "다음 물품", out ignored);
            SetTopLeft((RectTransform)nextStash.transform, new Vector2(702f, -420f), new Vector2(130f, 46f));
            Button sellOne = CreateUiButton("SellOne", panel.transform, "1개 판매", out ignored);
            SetTopLeft((RectTransform)sellOne.transform, new Vector2(562f, -478f), new Vector2(190f, 48f));
            Button sellStack = CreateUiButton("SellStack", panel.transform, "묶음 판매", out ignored);
            SetTopLeft((RectTransform)sellStack.transform, new Vector2(762f, -478f), new Vector2(190f, 48f));

            Text status = CreateUiText("Status", panel.transform, "거래 결과", 17, TextAnchor.MiddleCenter);
            SetBottomCenter(status.rectTransform, new Vector2(0f, 72f), new Vector2(900f, 42f));
            Button close = CreateUiButton("CloseButton", panel.transform, "닫기", out ignored);
            SetBottomCenter((RectTransform)close.transform, new Vector2(0f, 20f), new Vector2(220f, 48f));

            hud.Configure(null, null, credits, market, stash, status, previousMarket, nextMarket, buyOne, buyStack,
                previousStash, nextStash, sellOne, sellStack, close);
            GameObject saved = PrefabUtility.SaveAsPrefabAsset(root, path);
            Object.DestroyImmediate(root);
            return saved;
        }

        // 창고 화면에서 제거한 실제 출격 기능을 전용 전체 화면 Canvas로 만듭니다.
        // 지역 목록은 ScriptableObject 참조로 저장하므로 최종 학교 맵이나 공장·숲을 추가해도 버튼 동작 코드를 다시 만들 필요가 없습니다.
        private static GameObject CreateHideoutDeploymentCanvasPrefab(RaidRegionDefinition[] raidRegions)
        {
            const string path = Root + "/Prefabs/UI/HideoutDeploymentCanvas.prefab";
            GameObject existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (existing != null) return existing;

            GameObject root = CreateCanvasRoot("HideoutDeploymentCanvas", 10);
            HideoutDeploymentHud hud = root.AddComponent<HideoutDeploymentHud>();
            CreateFullScreenBackground(root.transform, new Color(0.015f, 0.023f, 0.03f, 0.96f));

            GameObject panel = CreateUiObject("DeploymentPanel", root.transform);
            SetCentered((RectTransform)panel.transform, Vector2.zero, new Vector2(940f, 650f));
            panel.AddComponent<Image>().color = new Color(0.055f, 0.075f, 0.085f, 0.99f);

            Text header = CreateUiText("Header", panel.transform, "출격 터미널", 34, TextAnchor.MiddleCenter);
            SetTopCenter(header.rectTransform, new Vector2(0f, -28f), new Vector2(600f, 54f));
            header.raycastTarget = false;

            Text regionTitle = CreateUiText("RegionTitle", panel.transform, "폐쇄된 학교", 30, TextAnchor.MiddleCenter);
            SetTopCenter(regionTitle.rectTransform, new Vector2(0f, -112f), new Vector2(720f, 52f));
            regionTitle.raycastTarget = false;

            Text description = CreateUiText("Description", panel.transform, "지역 설명", 20, TextAnchor.UpperLeft);
            SetTopCenter(description.rectTransform, new Vector2(0f, -184f), new Vector2(760f, 130f));
            description.horizontalOverflow = HorizontalWrapMode.Wrap;
            description.verticalOverflow = VerticalWrapMode.Truncate;
            description.raycastTarget = false;

            Text difficulty = CreateUiText("Difficulty", panel.transform, "위험도", 23, TextAnchor.MiddleCenter);
            SetCentered(difficulty.rectTransform, new Vector2(0f, 24f), new Vector2(650f, 48f));
            difficulty.raycastTarget = false;

            Text loadout = CreateUiText("LoadoutSummary", panel.transform, "출격 인벤토리 준비 수량", 19, TextAnchor.MiddleCenter);
            SetCentered(loadout.rectTransform, new Vector2(0f, -54f), new Vector2(740f, 72f));
            loadout.raycastTarget = false;

            Text status = CreateUiText("Status", panel.transform, "지역을 확인한 뒤 출격하세요.", 18, TextAnchor.MiddleCenter);
            SetBottomCenter(status.rectTransform, new Vector2(0f, 142f), new Vector2(760f, 42f));
            status.raycastTarget = false;

            Text ignored;
            Button previous = CreateUiButton("PreviousRegionButton", panel.transform, "이전 지역", out ignored);
            SetBottomCenter((RectTransform)previous.transform, new Vector2(-310f, 84f), new Vector2(170f, 50f));
            Button next = CreateUiButton("NextRegionButton", panel.transform, "다음 지역", out ignored);
            SetBottomCenter((RectTransform)next.transform, new Vector2(310f, 84f), new Vector2(170f, 50f));
            Button launch = CreateUiButton("LaunchButton", panel.transform, "선택 지역으로 출격", out ignored);
            SetBottomCenter((RectTransform)launch.transform, new Vector2(0f, 84f), new Vector2(300f, 56f));
            Button close = CreateUiButton("CloseButton", panel.transform, "닫기", out ignored);
            SetBottomCenter((RectTransform)close.transform, new Vector2(0f, 20f), new Vector2(220f, 48f));

            hud.Configure(raidRegions, regionTitle, description, difficulty, loadout, status, previous, next, launch, close);
            GameObject saved = PrefabUtility.SaveAsPrefabAsset(root, path);
            Object.DestroyImmediate(root);
            return saved;
        }

        // 거점 시설 위 상호작용 안내와 J키 임무수첩을 가진 작은 Canvas 프리팹입니다.
        // 게임 이름처럼 항상 필요한 정보가 아닌 거점 제목은 제거해 실제 플레이 화면을 더 넓게 사용합니다.
        private static GameObject CreateHideoutWorldCanvasPrefab()
        {
            const string path = Root + "/Prefabs/UI/HideoutWorldCanvas.prefab";
            GameObject existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (existing != null)
            {
                EnsureHideoutWorldHud(path);
                return EnsureTutorialHud(path, false);
            }

            GameObject root = CreateCanvasRoot("HideoutWorldCanvas", 5);
            HideoutWorldHud hud = root.AddComponent<HideoutWorldHud>();

            GameObject promptPanel = CreateUiObject("PromptPanel", root.transform);
            // 실제 위치는 HideoutWorldHud가 가까운 시설의 월드 좌표를 화면 좌표로 바꿔 매 프레임 갱신합니다.
            // 중앙 앵커를 사용하면 해상도가 바뀌어도 변환된 좌표가 하단 앵커의 영향을 받지 않습니다.
            SetCentered((RectTransform)promptPanel.transform, Vector2.zero, new Vector2(520f, 58f));
            Image promptBackground = promptPanel.AddComponent<Image>();
            promptBackground.color = new Color(0.02f, 0.03f, 0.035f, 0.86f);
            promptBackground.raycastTarget = false;
            Text prompt = CreateUiText("Prompt", promptPanel.transform, "[E] 시설 사용", 20, TextAnchor.MiddleCenter);
            Stretch(prompt.rectTransform, new Vector2(12f, 6f), new Vector2(-12f, -6f));
            prompt.raycastTarget = false;

            // Configure를 호출하면 임무수첩 패널도 프리팹 자식으로 생성됩니다.
            // 따라서 런타임 전용 UI가 아니라 Unity Hierarchy와 Inspector에서 직접 꾸밀 수 있는 일반 Canvas 구조로 저장됩니다.
            hud.Configure(null, null, null, prompt);
            GameObject saved = PrefabUtility.SaveAsPrefabAsset(root, path);
            Object.DestroyImmediate(root);
            EnsureHideoutWorldHud(path);
            return EnsureTutorialHud(path, false);
        }

        // 이미 만들어진 HideoutWorldCanvas는 사용자가 바꾼 색과 폰트를 보존하면서 필요한 구조만 보완합니다.
        // 프리팹 전체를 삭제하고 다시 만들지 않으므로 이후 아트 작업을 해도 이 업그레이드가 외형을 덮어쓰지 않습니다.
        private static void EnsureHideoutWorldHud(string path)
        {
            GameObject root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                HideoutWorldHud hud = root.GetComponent<HideoutWorldHud>();
                if (hud == null) hud = root.AddComponent<HideoutWorldHud>();

                Text title = root.transform.Find("HideoutTitle")?.GetComponent<Text>();
                if (title != null) title.gameObject.SetActive(false);

                Transform promptPanelTransform = root.transform.Find("PromptPanel");
                GameObject promptPanel = promptPanelTransform != null
                    ? promptPanelTransform.gameObject
                    : CreateUiObject("PromptPanel", root.transform);
                RectTransform promptPanelRect = (RectTransform)promptPanel.transform;
                SetCentered(promptPanelRect, Vector2.zero, new Vector2(520f, 58f));

                Image promptBackground = promptPanel.GetComponent<Image>();
                if (promptBackground == null) promptBackground = promptPanel.AddComponent<Image>();
                promptBackground.raycastTarget = false;

                Text prompt = promptPanel.transform.Find("Prompt")?.GetComponent<Text>();
                if (prompt == null)
                    prompt = CreateUiText("Prompt", promptPanel.transform, "[E] 시설 사용", 20, TextAnchor.MiddleCenter);
                Stretch(prompt.rectTransform, new Vector2(12f, 6f), new Vector2(-12f, -6f));
                prompt.raycastTarget = false;

                // 새 HUD의 Configure가 누락된 MissionJournalPanel을 만들고 모든 직렬화 참조를 연결합니다.
                // 만들어진 패널은 프리팹 저장 전에 닫아 씬 시작 시 월드를 가리지 않게 합니다.
                hud.Configure(null, null, title, prompt);
                hud.SetMissionJournalOpen(false);
                PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        // 기존 HUD 프리팹을 통째로 다시 만들지 않고 튜토리얼 패널과 현재 목표만 필요한 경우에 추가합니다.
        // 따라서 사용자가 이미 교체한 폰트·이미지·배치는 유지되고, 생성기가 소유하는 이름의 자식만 한 번 만들어집니다.
        private static GameObject EnsureTutorialHud(string path, bool raidLayout)
        {
            GameObject root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                TutorialHud hud = root.GetComponent<TutorialHud>();
                if (hud == null) hud = root.AddComponent<TutorialHud>();
                if (root.GetComponent<AccessibilityTextScaler>() == null)
                    root.AddComponent<AccessibilityTextScaler>();

                Transform existingPanel = root.transform.Find("TutorialPanel");
                GameObject panel = existingPanel != null ? existingPanel.gameObject : CreateUiObject("TutorialPanel", root.transform);
                RectTransform panelRect = (RectTransform)panel.transform;
                SetTopLeft(panelRect, new Vector2(24f, raidLayout ? -102f : -116f), new Vector2(530f, 142f));
                Image panelImage = panel.GetComponent<Image>();
                if (panelImage == null) panelImage = panel.AddComponent<Image>();
                panelImage.color = new Color(0.02f, 0.035f, 0.04f, 0.9f);
                panelImage.raycastTarget = false;

                Text title = panel.transform.Find("Title")?.GetComponent<Text>();
                if (title == null) title = CreateUiText("Title", panel.transform, "초보자 안내 · F1", 18, TextAnchor.MiddleLeft);
                SetTopLeft(title.rectTransform, new Vector2(14f, -8f), new Vector2(500f, 30f));
                title.raycastTarget = false;
                Text message = panel.transform.Find("Message")?.GetComponent<Text>();
                if (message == null) message = CreateUiText("Message", panel.transform, string.Empty, 15, TextAnchor.UpperLeft);
                SetTopLeft(message.rectTransform, new Vector2(14f, -42f), new Vector2(500f, 86f));
                message.horizontalOverflow = HorizontalWrapMode.Wrap;
                message.verticalOverflow = VerticalWrapMode.Truncate;
                message.raycastTarget = false;

                Text goal = root.transform.Find("CurrentGoal")?.GetComponent<Text>();
                if (goal == null) goal = CreateUiText("CurrentGoal", root.transform, "현재 목표", 18, TextAnchor.MiddleCenter);
                SetTopCenter(goal.rectTransform, new Vector2(0f, raidLayout ? -60f : -76f), new Vector2(820f, 36f));
                goal.raycastTarget = false;
                Outline outline = goal.GetComponent<Outline>();
                if (outline == null) outline = goal.gameObject.AddComponent<Outline>();
                outline.effectColor = new Color(0f, 0f, 0f, 0.9f);

                var serialized = new SerializedObject(hud);
                serialized.FindProperty("tutorialPanel").objectReferenceValue = panel;
                serialized.FindProperty("tutorialTitleText").objectReferenceValue = title;
                serialized.FindProperty("tutorialMessageText").objectReferenceValue = message;
                serialized.FindProperty("currentGoalText").objectReferenceValue = goal;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
            return AssetDatabase.LoadAssetAtPath<GameObject>(path);
        }

        // Canvas에 필요한 해상도 보정과 마우스 입력 컴포넌트를 공통 설정해 모든 화면의 크기 규칙을 통일합니다.
        private static GameObject CreateCanvasRoot(string name, int sortingOrder)
        {
            GameObject root = CreateUiObject(name, null);
            Canvas canvas = root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = sortingOrder;
            CanvasScaler scaler = root.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1600f, 900f);
            scaler.matchWidthOrHeight = 0.5f;
            root.AddComponent<GraphicRaycaster>();
            // 글자 크기 접근성 설정은 모든 Canvas가 같은 컴포넌트를 사용해 화면별 누락을 막습니다.
            root.AddComponent<AccessibilityTextScaler>();
            return root;
        }

        // 화면 전체를 덮는 배경 Image를 만들며, 버튼을 가로막지 않도록 마우스 판정은 끕니다.
        private static Image CreateFullScreenBackground(Transform parent, Color color)
        {
            GameObject backgroundObject = CreateUiObject("Background", parent);
            backgroundObject.transform.SetAsFirstSibling();
            Stretch((RectTransform)backgroundObject.transform);
            Image image = backgroundObject.AddComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        // Image, Button, 가운데 Text를 항상 같은 구조로 만들어 Inspector에서 배경과 글자를 쉽게 교체할 수 있게 합니다.
        private static Button CreateUiButton(string name, Transform parent, string label, out Text labelText)
        {
            GameObject buttonObject = CreateUiObject(name, parent);
            Image image = buttonObject.AddComponent<Image>();
            image.color = new Color(0.18f, 0.42f, 0.34f, 1f);
            Button button = buttonObject.AddComponent<Button>();
            button.targetGraphic = image;
            labelText = CreateUiText("Label", buttonObject.transform, label, 18, TextAnchor.MiddleCenter);
            Stretch(labelText.rectTransform, new Vector2(8f, 4f), new Vector2(-8f, -4f));
            labelText.raycastTarget = false;
            return button;
        }

        // 인벤토리와 거점이 공유하는 무기 관리 Canvas를 실제 Text와 Button 자식으로 구성합니다.
        // 이미 존재하면 그대로 반환하므로 업그레이드를 여러 번 실행해도 중복 패널이 생기지 않고 사용자가 수정한 디자인도 유지됩니다.
        private static WeaponManagementPanel EnsureWeaponManagementPanel(Transform parent, bool hideoutLayout)
        {
            Transform existing = parent != null ? parent.Find("WeaponManagementPanel") : null;
            if (existing != null)
            {
                WeaponManagementPanel existingPanel = existing.GetComponent<WeaponManagementPanel>();
                if (existingPanel != null) return existingPanel;
            }

            GameObject panel = CreateUiObject("WeaponManagementPanel", parent);
            RectTransform panelRect = (RectTransform)panel.transform;
            if (hideoutLayout)
                SetTopLeft(panelRect, new Vector2(425f, -260f), new Vector2(750f, 350f));
            else
                SetTopLeft(panelRect, new Vector2(285f, -470f), new Vector2(1135f, 300f));
            Image background = panel.AddComponent<Image>();
            background.color = new Color(0.035f, 0.055f, 0.06f, 0.985f);

            Text title = CreateUiText("Title", panel.transform, "무기 관리", 22, TextAnchor.MiddleLeft);
            SetTopLeft(title.rectTransform, new Vector2(18f, -12f), new Vector2(panelRect.sizeDelta.x - 36f, 34f));
            title.raycastTarget = false;

            float statsWidth = hideoutLayout ? 320f : 430f;
            Text stats = CreateUiText("Stats", panel.transform, string.Empty, 17, TextAnchor.UpperLeft);
            SetTopLeft(stats.rectTransform, new Vector2(18f, -58f), new Vector2(statsWidth, 220f));
            stats.horizontalOverflow = HorizontalWrapMode.Wrap;
            stats.verticalOverflow = VerticalWrapMode.Truncate;
            stats.raycastTarget = false;

            float buttonX = statsWidth + 42f;
            float buttonWidth = panelRect.sizeDelta.x - buttonX - 18f;
            Button optic = CreateUiButton("OpticButton", panel.transform, "조준경: 비어 있음", out _);
            SetTopLeft(optic.GetComponent<RectTransform>(), new Vector2(buttonX, -58f), new Vector2(buttonWidth, 58f));
            Button muzzle = CreateUiButton("MuzzleButton", panel.transform, "총구: 비어 있음", out _);
            SetTopLeft(muzzle.GetComponent<RectTransform>(), new Vector2(buttonX, -122f), new Vector2(buttonWidth, 58f));
            Button magazine = CreateUiButton("MagazineButton", panel.transform, "탄창: 비어 있음", out _);
            SetTopLeft(magazine.GetComponent<RectTransform>(), new Vector2(buttonX, -186f), new Vector2(buttonWidth, 58f));
            Button repair = CreateUiButton("RepairButton", panel.transform, "완전 수리", out Text repairLabel);
            SetTopLeft(repair.GetComponent<RectTransform>(), new Vector2(buttonX, -250f), new Vector2(buttonWidth, 58f));

            WeaponManagementPanel component = panel.AddComponent<WeaponManagementPanel>();
            component.ConfigureViews(panel, title, stats, optic, muzzle, magazine, repair, repairLabel);
            return component;
        }

        // 제목과 세로 스크롤 영역을 가진 Hideout 목록 하나를 만들고, 실제 행이 들어갈 Content를 반환합니다.
        private static RectTransform CreateHideoutList(
            string name,
            Transform parent,
            Vector2 topLeftPosition,
            string titleValue,
            out Text title)
        {
            GameObject panel = CreateUiObject(name, parent);
            SetTopLeft((RectTransform)panel.transform, topLeftPosition, new Vector2(700f, 650f));
            Image panelImage = panel.AddComponent<Image>();
            panelImage.color = new Color(0.055f, 0.075f, 0.085f, 0.98f);

            title = CreateUiText("Title", panel.transform, titleValue, 22, TextAnchor.MiddleLeft);
            SetTopLeft(title.rectTransform, new Vector2(18f, -10f), new Vector2(650f, 42f));
            title.raycastTarget = false;

            GameObject scrollObject = CreateUiObject("ScrollArea", panel.transform);
            SetTopLeft((RectTransform)scrollObject.transform, new Vector2(14f, -62f), new Vector2(672f, 570f));
            Image scrollBackground = scrollObject.AddComponent<Image>();
            scrollBackground.color = new Color(0.025f, 0.035f, 0.04f, 0.9f);
            ScrollRect scrollRect = scrollObject.AddComponent<ScrollRect>();
            scrollRect.horizontal = false;
            scrollRect.vertical = true;
            scrollRect.movementType = ScrollRect.MovementType.Clamped;
            scrollRect.scrollSensitivity = 26f;

            GameObject viewportObject = CreateUiObject("Viewport", scrollObject.transform);
            RectTransform viewport = (RectTransform)viewportObject.transform;
            Stretch(viewport, new Vector2(8f, 8f), new Vector2(-8f, -8f));
            Image viewportImage = viewportObject.AddComponent<Image>();
            viewportImage.color = new Color(1f, 1f, 1f, 0.01f);
            viewportObject.AddComponent<RectMask2D>();

            RectTransform content = (RectTransform)CreateUiObject("Content", viewportObject.transform).transform;
            content.anchorMin = new Vector2(0f, 1f);
            content.anchorMax = new Vector2(1f, 1f);
            content.pivot = new Vector2(0.5f, 1f);
            content.anchoredPosition = Vector2.zero;
            content.sizeDelta = new Vector2(0f, 1f);
            scrollRect.viewport = viewport;
            scrollRect.content = content;
            return content;
        }

        // 모든 UI 오브젝트가 RectTransform으로 시작하게 만들어 일반 Transform을 나중에 교체하는 실수를 방지합니다.
        private static GameObject CreateUiObject(string name, Transform parent)
        {
            var result = new GameObject(name, typeof(RectTransform));
            if (parent != null) result.transform.SetParent(parent, false);
            return result;
        }

        // 프로토타입 전용 Text를 같은 글꼴, 흰색과 기본 스타일로 생성합니다.
        // 최종 아트 단계에서는 만들어진 프리팹에서 Font, 크기와 색상을 자유롭게 교체하면 됩니다.
        private static Text CreateUiText(string name, Transform parent, string value, int fontSize, TextAnchor alignment)
        {
            GameObject textObject = CreateUiObject(name, parent);
            Text text = textObject.AddComponent<Text>();
            // Unity 2022.3에서는 기존 Arial.ttf 별칭 대신 LegacyRuntime.ttf 이름으로 내장 글꼴을 불러와야 합니다.
            // 프로젝트 외부 폰트 에셋이 없어도 기본 프리팹 생성이 실패하지 않도록 이 내장 글꼴을 사용합니다.
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.text = value;
            text.fontSize = fontSize;
            text.color = Color.white;
            text.alignment = alignment;
            return text;
        }

        // RectTransform을 부모 전체에 맞춰 늘리고 네 방향 여백을 offsetMin/offsetMax로 적용합니다.
        private static void Stretch(RectTransform rect, Vector2 minimumOffset = default, Vector2 maximumOffset = default)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.offsetMin = minimumOffset;
            rect.offsetMax = maximumOffset;
        }

        // 왼쪽 위를 기준으로 UI 요소의 위치와 크기를 지정해 격자처럼 고정된 배치를 읽기 쉽게 만듭니다.
        private static void SetTopLeft(RectTransform rect, Vector2 position, Vector2 size)
        {
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
        }

        // 화면 오른쪽 위를 기준으로 무기와 탄약처럼 우측 정렬되는 정보를 고정 배치합니다.
        private static void SetTopRight(RectTransform rect, Vector2 position, Vector2 size)
        {
            rect.anchorMin = Vector2.one;
            rect.anchorMax = Vector2.one;
            rect.pivot = Vector2.one;
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
        }

        // 화면 왼쪽 아래를 기준으로 체력처럼 하단에 머무는 정보를 고정 배치합니다.
        private static void SetBottomLeft(RectTransform rect, Vector2 position, Vector2 size)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.zero;
            rect.pivot = Vector2.zero;
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
        }

        // 시작 화면 제목처럼 화면 위쪽 가운데를 기준으로 배치되는 요소의 위치와 크기를 지정합니다.
        private static void SetTopCenter(RectTransform rect, Vector2 position, Vector2 size)
        {
            rect.anchorMin = new Vector2(0.5f, 1f);
            rect.anchorMax = new Vector2(0.5f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
        }

        // 화면 또는 부모 정중앙을 기준으로 결과 제목과 중앙 카드 같은 요소를 배치합니다.
        private static void SetCentered(RectTransform rect, Vector2 position, Vector2 size)
        {
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
        }

        // 상호작용 안내 문구처럼 화면 아래 가운데를 기준으로 놓이는 요소의 위치와 크기를 지정합니다.
        private static void SetBottomCenter(RectTransform rect, Vector2 position, Vector2 size)
        {
            rect.anchorMin = new Vector2(0.5f, 0f);
            rect.anchorMax = new Vector2(0.5f, 0f);
            rect.pivot = new Vector2(0.5f, 0f);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
        }

        // 빈 씬에 바닥, 벽, 플레이어, 좀비, 아이템, 카메라와 HUD를 배치해 모든 현재 시스템을 함께 검증합니다.
        // 완성형 레벨이 아니라 기능 통합 테스트 맵이며, 같은 경로로 SaveScene하므로 Rebuild 시 기존 씬이 교체됩니다.
        private static void CreateRaidScene(
            string sceneName,
            bool useSchoolGeometry,
            bool preserveCurrentWorkspace,
            GameObject playerPrefab,
            GameObject zombiePrefab,
            GameObject inventoryCanvasPrefab,
            GameObject raidHudCanvasPrefab,
            ItemDefinition[] itemDefinitions,
            ItemCatalog itemCatalog,
            ExtractionRequirement extractionRequirement,
            RaidRules raidRules,
            ZombieArchetype[] zombieArchetypes,
            LootTableDefinition[] lootTables,
            RegionLootProfile regionLootProfile)
        {
            Scene previousActiveScene = SceneManager.GetActiveScene();
            bool replaceEmptyUntitledScene = string.IsNullOrWhiteSpace(previousActiveScene.path) &&
                                             previousActiveScene.rootCount == 0;
            Scene scene = EditorSceneManager.NewScene(
                NewSceneSetup.EmptyScene,
                preserveCurrentWorkspace && !replaceEmptyUntitledScene ? NewSceneMode.Additive : NewSceneMode.Single);
            scene.name = sceneName;
            SceneManager.SetActiveScene(scene);
            CreateLighting();

            // 플레이어·아이템·좀비·탈출 시스템은 두 레이드 씬이 공유하고 지형만 지역에 맞는 생성 메서드로 바꿉니다.
            // 임시 학교도 실제 게임 루프 전체를 사용하므로 기능 테스트 맵과 다른 코드 경로 때문에 생기는 회귀를 줄일 수 있습니다.
            if (useSchoolGeometry) CreateSchoolRaidMapGeometry();
            else CreateRaidMapGeometry();

            if (useSchoolGeometry && regionLootProfile != null)
            {
                // 씬 안의 상자·바닥 생성 지점·좀비가 같은 학교 드랍 데이터를 읽도록 단일 컨트롤러를 먼저 배치합니다.
                // 컨트롤러의 실행 순서가 빠르므로 실제 플레이가 시작되면 다른 전리품 Start보다 먼저 활성 프로필을 등록합니다.
                var lootControllerObject = new GameObject("RegionLootController");
                lootControllerObject.AddComponent<RegionLootController>().Configure(regionLootProfile);
            }

            GameObject player = (GameObject)PrefabUtility.InstantiatePrefab(playerPrefab, scene);
            player.transform.position = useSchoolGeometry
                ? SchoolRaidMapLayout.PlayerStartPosition
                : RaidMapLayout.PlayerStartPosition;

            if (useSchoolGeometry)
            {
                // 학교는 구역 위험도에 맞는 종류를 처음부터 명시적으로 배치합니다.
                // 정문과 행정동은 일반형, 중앙 우회로는 빠른형, 고가치 실내에는 특수형을 한 마리씩 두어 동선 선택에 차이를 만듭니다.
                CreateArchetypeZombie(zombiePrefab, "SchoolWalker_Entrance", new Vector3(-14f, 0f, -33f), scene, zombieArchetypes, ZombieBehaviorTrait.Standard);
                CreateArchetypeZombie(zombiePrefab, "SchoolWalker_Administration", new Vector3(-23f, 0f, -20f), scene, zombieArchetypes, ZombieBehaviorTrait.Standard);
                CreateArchetypeZombie(zombiePrefab, "SchoolRunner_Courtyard", new Vector3(7f, 0f, 8f), scene, zombieArchetypes, ZombieBehaviorTrait.Runner);
                CreateArchetypeZombie(zombiePrefab, "SchoolScreamer_SpecialClass", new Vector3(-7f, 0f, 24f), scene, zombieArchetypes, ZombieBehaviorTrait.Screamer);
                CreateArchetypeZombie(zombiePrefab, "SchoolBrute_Gym", new Vector3(25f, 0f, 5f), scene, zombieArchetypes, ZombieBehaviorTrait.Brute);
                CreateArchetypeZombie(zombiePrefab, "SchoolWalker_Playground", new Vector3(10f, 0f, 34f), scene, zombieArchetypes, ZombieBehaviorTrait.Standard);
            }
            else
            {
                // 작은 구형 테스트 맵은 기존 세 마리와 개별 전리품 후보를 그대로 유지합니다.
                CreateZombie(
                    zombiePrefab,
                    "Zombie_A",
                    new Vector3(-7f, 0f, 1f),
                    scene,
                    new ZombieLootEntry { item = itemDefinitions[0], dropChance = 0.7f, minimumQuantity = 3, maximumQuantity = 6 },
                    new ZombieLootEntry { item = itemDefinitions[2], dropChance = 0.18f, minimumQuantity = 1, maximumQuantity = 1 });
                CreateZombie(
                    zombiePrefab,
                    "Zombie_B",
                    new Vector3(0f, 0f, 10f),
                    scene,
                    new ZombieLootEntry { item = itemDefinitions[1], dropChance = 0.2f, minimumQuantity = 1, maximumQuantity = 1 },
                    new ZombieLootEntry { item = itemDefinitions[9], dropChance = 0.55f, minimumQuantity = 4, maximumQuantity = 8 });
                CreateZombie(
                    zombiePrefab,
                    "Zombie_C",
                    new Vector3(8f, 0f, 4f),
                    scene,
                    new ZombieLootEntry { item = itemDefinitions[0], dropChance = 0.5f, minimumQuantity = 3, maximumQuantity = 6 },
                    new ZombieLootEntry { item = itemDefinitions[6], dropChance = 0.08f, minimumQuantity = 1, maximumQuantity = 1 });
            }

            // 학교 배치는 ItemId 기반 전용 표를 사용해 아이템 배열 순서가 바뀌어도 엉뚱한 장비가 놓이지 않게 합니다.
            // 작은 테스트 맵만 이전 배열 인덱스 배치를 보존합니다.
            if (useSchoolGeometry)
            {
                CreateSchoolGroundLootPoints();
            }
            else
            {
                CreateWorldItem("AmmoPickup", new Vector3(-2f, 0.3f, -12.5f), itemDefinitions[0], 12, new Color(0.85f, 0.72f, 0.2f));
                CreateWorldItem("MedkitPickup", new Vector3(-10.5f, 0.3f, -3.5f), itemDefinitions[1], 1, new Color(0.85f, 0.2f, 0.2f));
                CreateWorldItem("FoodPickup", new Vector3(-4f, 0.3f, 2f), itemDefinitions[2], 2, new Color(0.85f, 0.48f, 0.18f));
                CreateWorldItem("QuestRadioPickup", new Vector3(-13f, 0.3f, 7f), itemDefinitions[3], 1, new Color(0.25f, 0.65f, 1f));
                CreateWorldItem("RiflePickup", new Vector3(9f, 0.3f, 7f), itemDefinitions[4], 1, new Color(0.3f, 0.3f, 0.3f));
                CreateWorldItem("PistolPickup", new Vector3(4f, 0.3f, -13f), itemDefinitions[5], 1, new Color(0.25f, 0.25f, 0.25f));
                CreateWorldItem("ArmorPickup", new Vector3(-12f, 0.3f, 5.5f), itemDefinitions[6], 1, new Color(0.25f, 0.45f, 0.55f));
                CreateWorldItem("RigPickup", new Vector3(14f, 0.3f, 7f), itemDefinitions[7], 1, new Color(0.35f, 0.5f, 0.3f));
                CreateWorldItem("BackpackPickup", new Vector3(3.5f, 0.3f, 4.5f), itemDefinitions[8], 1, new Color(0.3f, 0.4f, 0.3f));
                CreateWorldItem("RifleAmmoPickup", new Vector3(9f, 0.3f, 9f), itemDefinitions[9], 30, new Color(0.72f, 0.58f, 0.18f));
                CreateWorldItem("ShotgunAmmoPickup", new Vector3(11f, 0.3f, -6f), itemDefinitions[10], 12, new Color(0.72f, 0.38f, 0.16f));
                CreateWorldItem("SmgPickup", new Vector3(-13f, 0.3f, -8f), itemDefinitions[11], 1, new Color(0.18f, 0.24f, 0.28f));
                CreateWorldItem("ShotgunPickup", new Vector3(14f, 0.3f, -5f), itemDefinitions[12], 1, new Color(0.35f, 0.2f, 0.16f));
            }

            // 학교는 각 기능 구역을 수색할 이유가 생기도록 보건실·급식실·도서관·과학실·체육관·2층에 상자를 나눠 둡니다.
            // 작은 테스트 맵은 기존 세 상자 배치를 유지해 두 맵의 크기 차이 때문에 전리품 밀도가 지나치게 달라지지 않게 합니다.
            if (useSchoolGeometry)
            {
                CreateSchoolLootContainers(scene, lootTables);
            }
            else
            {
                CreateFieldLootBox(scene, "FieldLoot_Supply", new Vector3(-12f, 0f, -2f), 6, 4, lootTables[0], new Color(0.35f, 0.5f, 0.28f));
                CreateFieldLootBox(scene, "FieldLoot_Weapon", new Vector3(12f, 0f, 4f), 8, 5, lootTables[1], new Color(0.28f, 0.32f, 0.38f));
                CreateFieldLootBox(scene, "FieldLoot_HiddenStash", new Vector3(0f, 0f, 8f), 6, 4, lootTables[2], new Color(0.42f, 0.34f, 0.2f));
            }

            // 북쪽 벽 안쪽에 반경이 넉넉한 탈출 Trigger와 눈에 보이는 녹색 원통을 배치합니다.
            // Trigger 루트와 Visual을 분리해 시각용 Mesh Collider가 진입 판정에 중복 참여하지 않게 합니다.
            ExtractionController extraction = CreateExtractionPoint(
                extractionRequirement,
                useSchoolGeometry ? SchoolRaidMapLayout.ExtractionPosition : RaidMapLayout.ExtractionPosition);

            // CameraRig가 플레이어를 추적하고 Main Camera는 고정된 탑다운 각도를 유지합니다.
            var rig = new GameObject("CameraRig");
            var cameraObject = new GameObject("Main Camera");
            cameraObject.tag = "MainCamera";
            cameraObject.transform.SetParent(rig.transform, false);
            cameraObject.transform.position = new Vector3(0f, 11f, -8f);
            cameraObject.transform.rotation = Quaternion.Euler(55f, 0f, 0f);
            Camera camera = cameraObject.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.035f, 0.045f, 0.055f);
            cameraObject.AddComponent<AudioListener>();
            rig.AddComponent<CameraFollow>().SetTarget(player.transform);

            if (useSchoolGeometry)
            {
                // 현재는 학교 교실동 한 채에만 PlayerOccluder 레이어를 붙였으므로 다른 레이드와 거점 카메라에는 검사 컴포넌트를 추가하지 않습니다.
                // 레이어 번호를 코드에 고정하지 않고 이름으로 찾으면 Project Settings에서 순서가 달라져도 올바른 비트 마스크를 만들 수 있습니다.
                int occluderLayer = LayerMask.NameToLayer("PlayerOccluder");
                if (occluderLayer >= 0)
                {
                    rig.AddComponent<PlayerOcclusionController>().Configure(camera, player.transform, 1 << occluderLayer);
                }
                else
                {
                    Debug.LogError("학교 카메라 가림 처리 실패: Project Settings > Tags and Layers에 PlayerOccluder 레이어가 없습니다.");
                }
            }

            // 코드로 즉석 사각형을 그리던 기존 HUD 대신 수정 가능한 Canvas 프리팹을 씬에 배치합니다.
            // EventSystem은 Image와 Button이 마우스 클릭/드래그 이벤트를 받을 수 있게 해 주며 씬에는 하나만 둡니다.
            InventoryHud inventoryHud = null;
            if (inventoryCanvasPrefab != null)
            {
                GameObject inventoryCanvas = (GameObject)PrefabUtility.InstantiatePrefab(inventoryCanvasPrefab, scene);
                inventoryHud = inventoryCanvas.GetComponent<InventoryHud>();
                inventoryHud.Configure(
                    player.GetComponent<PlayerInventory>(),
                    player.GetComponent<PlayerInteraction>());
            }

            CreateEventSystem();

            // Hideout에서 준비한 출격품을 플레이어 인벤토리에 복원하고 실패한 항목은 창고로 반환하도록 공용 카탈로그를 연결합니다.
            var loadoutApplierObject = new GameObject("RaidLoadoutApplier");
            loadoutApplierObject.AddComponent<RaidLoadoutApplier>().Configure(
                player.GetComponent<PlayerInventory>(),
                itemCatalog);

            // RaidSession이 탈출 성공과 플레이어 사망을 한곳에서 감시하고 결과 스냅샷을 생성하도록 참조를 연결합니다.
            var raidSessionObject = new GameObject("RaidSession");
            RaidSession raidSession = raidSessionObject.AddComponent<RaidSession>();
            raidSession.Configure(player.GetComponent<PlayerInventory>(), player.GetComponent<Health>(), extraction, raidRules);
            // 장소 방문 임무는 플레이어가 지나간 구역 ID를 레이드 결과에 넣어야 하므로 세션과 같은 오브젝트에 가벼운 추적기를 둡니다.
            // 학교가 아닌 레이드는 trackSchoolZones=false로 연결되어 좌표가 우연히 겹쳐도 학교 방문으로 기록되지 않습니다.
            RaidVisitTracker visitTracker = raidSessionObject.AddComponent<RaidVisitTracker>();
            visitTracker.Configure(player.transform, useSchoolGeometry);
            raidSession.SetVisitTracker(visitTracker);

            // 맵 가장자리의 표시 지점 중 플레이어와 충분히 멀고 막히지 않은 곳에서 새 좀비가 나타나게 합니다.
            // 시작 시 3마리에서 후반 7마리까지 늘어나며 생성 간격은 9초에서 4초까지 점차 짧아집니다.
            CreateZombieSpawner(scene, zombieArchetypes, player.transform, raidSession, useSchoolGeometry);

            // 전투 HUD 프리팹 안의 네 표시 기능에 현재 씬의 플레이어·탈출 지점·레이드 세션을 연결합니다.
            // 프리팹은 화면 모양만 보관하고 씬마다 달라지는 실제 게임 객체 참조는 생성 시 주입합니다.
            if (raidHudCanvasPrefab != null)
            {
                GameObject raidHudCanvas = (GameObject)PrefabUtility.InstantiatePrefab(raidHudCanvasPrefab, scene);
                AmmoHud ammoHud = raidHudCanvas.GetComponent<AmmoHud>();
                ammoHud.SetCombat(player.GetComponent<PlayerCombat>());
                ammoHud.SetPlayerHealth(player.GetComponent<Health>());
                raidHudCanvas.GetComponent<PlayerDamageHud>().SetPlayerHealth(player.GetComponent<Health>());
                raidHudCanvas.GetComponent<ExtractionHud>().SetController(extraction);
                raidHudCanvas.GetComponent<RaidResultHud>().SetSession(raidSession);
                raidHudCanvas.GetComponent<PlayerConditionHud>()?.SetPlayerCondition(player.GetComponent<PlayerCondition>());
            }

            // ESC 메뉴는 가장 마지막에 배치하고 현재 씬의 인벤토리·레이드·플레이어 잠금 참조를 명시적으로 연결합니다.
            // 프리팹이 없는 오래된 프로젝트에서도 레이드 씬 생성 자체는 계속되도록 null이면 건너뜁니다.
            GameObject pauseCanvasPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(Root + "/Prefabs/UI/PauseCanvas.prefab");
            if (pauseCanvasPrefab != null)
            {
                GameObject pauseCanvas = (GameObject)PrefabUtility.InstantiatePrefab(pauseCanvasPrefab, scene);
                pauseCanvas.GetComponent<PauseMenuHud>()?.SetContext(
                    inventoryHud,
                    null,
                    raidSession,
                    player.GetComponent<PlayerControlGate>());
            }

            EditorSceneManager.SaveScene(scene, Root + "/Scenes/" + sceneName + ".unity");

            // 전용 학교 재생성 메뉴에서는 사용자가 편집하던 씬을 닫지 않고 임시로 추가한 학교 씬만 저장 후 정리합니다.
            if (preserveCurrentWorkspace && !replaceEmptyUntitledScene)
            {
                if (previousActiveScene.IsValid() && previousActiveScene.isLoaded)
                    SceneManager.SetActiveScene(previousActiveScene);
                if (scene.IsValid() && scene.isLoaded)
                    EditorSceneManager.CloseScene(scene, true);
            }
        }

        // ZombieSpawner와 지역별 외곽 스폰 지점을 한 묶음으로 생성해 Hierarchy에서 위치를 쉽게 찾고 옮길 수 있게 합니다.
        private static void CreateZombieSpawner(
            Scene scene,
            ZombieArchetype[] zombieArchetypes,
            Transform player,
            RaidSession raidSession,
            bool useSchoolLayout)
        {
            var root = new GameObject("ZombieSpawner");
            SceneManager.MoveGameObjectToScene(root, scene);

            // 각 맵의 전용 배치 기준을 사용해 학교를 키워도 스폰 지점이 작은 프로토타입 중앙에 몰리지 않게 합니다.
            int pointCount = useSchoolLayout
                ? SchoolRaidMapLayout.ZombieSpawnPointCount
                : RaidMapLayout.ZombieSpawnPointCount;
            var points = new ZombieSpawnPoint[pointCount];
            for (int i = 0; i < points.Length; i++)
            {
                var pointObject = new GameObject("SpawnPoint_" + (i + 1).ToString("D2"));
                pointObject.transform.SetParent(root.transform, false);
                pointObject.transform.position = useSchoolLayout
                    ? SchoolRaidMapLayout.GetZombieSpawnPosition(i)
                    : RaidMapLayout.GetZombieSpawnPosition(i);
                pointObject.transform.LookAt(useSchoolLayout
                    ? SchoolRaidMapLayout.CourtyardCenter
                    : RaidMapLayout.CentralCheckpointCenter);
                points[i] = pointObject.AddComponent<ZombieSpawnPoint>();

                if (useSchoolLayout)
                {
                    int danger = SchoolRaidMapLayout.GetZombieSpawnDangerRating(i);
                    // 2단계부터 빠른형, 3단계부터 비명형, 4단계부터 튼튼한형을 허용합니다.
                    // 각 종류의 ZombieArchetype 해금 시간도 별도로 적용되므로 위험 구역이라고 레이드 시작 즉시 특수형이 나오지는 않습니다.
                    points[i].Configure(
                        danger,
                        SchoolRaidMapLayout.GetZombieSpawnUnlockTime(i),
                        danger >= 2,
                        danger >= 4,
                        danger >= 3);
                }
                else
                {
                    // 작은 구형 테스트 맵은 모든 종류를 허용해 기존 시간 기반 난이도 동작을 보존합니다.
                    points[i].Configure(1, 0f, true, true, true);
                }
            }

            ZombieSpawner spawner = root.AddComponent<ZombieSpawner>();
            spawner.Configure(
                zombieArchetypes,
                points,
                player,
                raidSession,
                useSchoolLayout ? 5 : 3,
                useSchoolLayout ? 10 : 7,
                9f,
                4f,
                200f,
                useSchoolLayout ? 14f : 10f,
                6f);
        }

        // 기존의 전체 화면 창고만 있던 Hideout을 플레이어가 직접 걸어 다니는 작은 3D 거점으로 만듭니다.
        // 별도의 Additive 씬에서 제작한 뒤 저장하므로 이 메뉴를 눌렀을 때 현재 열어 둔 씬을 닫거나 저장하지 않은 작업을 덮어쓰지 않습니다.
        private static void CreateHideoutScene(
            GameObject playerPrefab,
            ItemCatalog itemCatalog,
            GameObject inventoryCanvasPrefab,
            GameObject storageCanvasPrefab,
            GameObject missionCanvasPrefab,
            GameObject facilityUpgradeCanvasPrefab,
            GameObject traderCanvasPrefab,
            GameObject deploymentCanvasPrefab,
            GameObject worldCanvasPrefab,
            HideoutFacilityDefinition[] facilityDefinitions,
            HideoutMissionDefinition[] missionDefinitions,
            HideoutRecipeDefinition[] recipeDefinitions)
        {
            const string scenePath = Root + "/Scenes/Hideout.unity";

            // 같은 Hideout 씬을 사용자가 직접 열어 수정 중이라면 디스크 파일을 생성기로 덮어쓰지 않습니다.
            // 씬을 닫은 뒤 메뉴를 다시 실행하면 현재 작업 씬은 그대로 둔 채 새 Hideout 씬만 안전하게 만들어집니다.
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene loadedScene = SceneManager.GetSceneAt(i);
                if (loadedScene.isLoaded && loadedScene.path == scenePath)
                {
                    Debug.LogError("거점 생성 중단: Hideout 씬이 열려 있습니다. 해당 씬을 닫고 Rebuild Hideout Only를 다시 실행해 주세요.");
                    return;
                }
            }

            Scene previousActiveScene = SceneManager.GetActiveScene();
            // 배치 모드의 새 임시 프로젝트는 저장되지 않은 빈 Untitled 씬 하나로 시작해 Additive 씬을 바로 만들 수 없습니다.
            // 그 경우에만 빈 씬을 교체하고, 사용자가 다른 씬을 열어 둔 일반 편집기에서는 기존 씬을 보존하는 Additive 방식을 유지합니다.
            bool replaceEmptyUntitledScene = string.IsNullOrWhiteSpace(previousActiveScene.path) &&
                                             previousActiveScene.rootCount == 0;
            Scene scene = EditorSceneManager.NewScene(
                NewSceneSetup.EmptyScene,
                replaceEmptyUntitledScene ? NewSceneMode.Single : NewSceneMode.Additive);
            scene.name = "Hideout";
            SceneManager.SetActiveScene(scene);

            try
            {
                CreateLighting();

                // 맵 루트를 따로 두면 이후 거점 규모를 늘릴 때 건물과 장식을 한 묶음으로 이동하거나 비활성화할 수 있습니다.
                var mapRootObject = new GameObject("HideoutMap");
                Transform mapRoot = mapRootObject.transform;

                // Unity Plane의 기본 한 변은 10이므로 2.4배를 적용해 24×24 크기의 이동 공간을 만듭니다.
                GameObject floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
                floor.name = "Ground";
                floor.transform.SetParent(mapRoot, false);
                floor.transform.localScale = new Vector3(2.4f, 1f, 2.4f);
                SetColor(floor, new Color(0.14f, 0.16f, 0.15f));

                // 플레이어가 카메라 밖으로 빠져나가지 않도록 네 면에 실제 Collider가 있는 벽을 배치합니다.
                CreateWall("Wall_North", new Vector3(0f, 1.25f, 12f), new Vector3(24f, 2.5f, 0.7f), mapRoot);
                CreateWall("Wall_South", new Vector3(0f, 1.25f, -12f), new Vector3(24f, 2.5f, 0.7f), mapRoot);
                CreateWall("Wall_East", new Vector3(12f, 1.25f, 0f), new Vector3(0.7f, 2.5f, 24f), mapRoot);
                CreateWall("Wall_West", new Vector3(-12f, 1.25f, 0f), new Vector3(0.7f, 2.5f, 24f), mapRoot);

                // 중앙 통로는 비워 두고 가장자리에만 간단한 가구를 놓아 두 시설 사이를 편하게 이동할 수 있게 합니다.
                CreateWall("WorkTable", new Vector3(-7.8f, 0.65f, -6.8f), new Vector3(3.2f, 1.3f, 1.3f), mapRoot, new Color(0.30f, 0.23f, 0.15f));
                CreateWall("SupplyShelf", new Vector3(8.8f, 1.2f, -4f), new Vector3(1.2f, 2.4f, 4f), mapRoot, new Color(0.24f, 0.27f, 0.29f));
                CreateZoneFloor("FutureFacilityPad_A", new Vector3(-7f, 0f, -1f), new Vector2(4f, 4f), new Color(0.16f, 0.20f, 0.19f), mapRoot);
                CreateZoneFloor("FutureFacilityPad_B", new Vector3(7f, 0f, -7.5f), new Vector2(4f, 3f), new Color(0.16f, 0.20f, 0.19f), mapRoot);

                // 레이드와 같은 Player 프리팹을 사용해야 이동 감각과 상호작용 반경이 두 씬에서 달라지지 않습니다.
                GameObject player = (GameObject)PrefabUtility.InstantiatePrefab(playerPrefab, scene);
                player.name = "Player";
                player.transform.position = new Vector3(0f, 0.05f, -6f);
                PlayerInteraction interaction = player.GetComponent<PlayerInteraction>();
                PlayerControlGate controlGate = player.GetComponent<PlayerControlGate>();
                // 거점에서는 조준 코드를 끄기 때문에 마지막 조준 위치에 녹색 표식이 남지 않도록 시각 오브젝트도 함께 숨깁니다.
                Transform aimMarker = player.transform.Find("AimMarker");
                if (aimMarker != null) aimMarker.gameObject.SetActive(false);

                // 카메라 자체가 아니라 빈 Rig를 따라가게 하여 고정된 55도 시점과 부드러운 위치 추적을 서로 분리합니다.
                var cameraRig = new GameObject("HideoutCameraRig");
                cameraRig.transform.position = player.transform.position + new Vector3(0f, 11f, -8f);
                var cameraObject = new GameObject("Main Camera");
                cameraObject.tag = "MainCamera";
                cameraObject.transform.SetParent(cameraRig.transform, false);
                cameraObject.transform.localPosition = Vector3.zero;
                cameraObject.transform.localRotation = Quaternion.Euler(55f, 0f, 0f);
                Camera camera = cameraObject.AddComponent<Camera>();
                camera.backgroundColor = new Color(0.025f, 0.035f, 0.04f);
                cameraObject.AddComponent<AudioListener>();

                // 창고 데이터 처리와 월드 시설 화면 전환은 역할이 다르므로 같은 오브젝트의 서로 다른 컴포넌트로 둡니다.
                var controllerObject = new GameObject("HideoutControllers");
                HideoutController hideoutController = controllerObject.AddComponent<HideoutController>();
                hideoutController.Configure(itemCatalog, missionDefinitions, facilityDefinitions, recipeDefinitions);
                hideoutController.ConfigurePlayerInventory(player.GetComponent<PlayerInventory>());
                HideoutWorldController worldController = controllerObject.AddComponent<HideoutWorldController>();

                // 창고는 낮고 넓은 상자, 임무 보드는 세로로 선 게시판 형태로 만들어 멀리서도 역할을 구분할 수 있게 합니다.
                HideoutFacility storageFacility = CreateHideoutFacilityObject(
                    "StorageFacility",
                    new Vector3(6.8f, 0.75f, 4.5f),
                    new Vector3(3.2f, 1.5f, 2.2f),
                    new Color(0.25f, 0.48f, 0.56f),
                    FindFacilityDefinition(facilityDefinitions, HideoutFacilityType.Storage),
                    worldController,
                    mapRoot);
                HideoutFacility missionFacility = CreateHideoutFacilityObject(
                    "MissionBoardFacility",
                    new Vector3(-6.8f, 1.35f, 4.8f),
                    new Vector3(4f, 2.7f, 0.45f),
                    new Color(0.58f, 0.42f, 0.18f),
                    FindFacilityDefinition(facilityDefinitions, HideoutFacilityType.MissionBoard),
                    worldController,
                    mapRoot);
                HideoutFacility craftingFacility = CreateHideoutFacilityObject(
                    "CraftingBenchFacility",
                    new Vector3(-7f, 0.75f, -1f),
                    new Vector3(3.8f, 1.5f, 2.2f),
                    new Color(0.45f, 0.28f, 0.14f),
                    FindFacilityDefinition(facilityDefinitions, HideoutFacilityType.CraftingBench),
                    worldController,
                    mapRoot);
                HideoutFacility infirmaryFacility = CreateHideoutFacilityObject(
                    "InfirmaryFacility",
                    new Vector3(7f, 0.9f, -7.5f),
                    new Vector3(3.6f, 1.8f, 2.5f),
                    new Color(0.68f, 0.70f, 0.66f),
                    FindFacilityDefinition(facilityDefinitions, HideoutFacilityType.Infirmary),
                    worldController,
                    mapRoot);
                HideoutFacility generatorFacility = CreateHideoutFacilityObject(
                    "GeneratorFacility",
                    new Vector3(8.2f, 0.85f, 0.2f),
                    new Vector3(2.4f, 1.7f, 2.4f),
                    new Color(0.52f, 0.46f, 0.18f),
                    FindFacilityDefinition(facilityDefinitions, HideoutFacilityType.Generator),
                    worldController,
                    mapRoot);
                HideoutFacility traderFacility = CreateHideoutFacilityObject(
                    "TraderFacility",
                    new Vector3(0f, 1f, 8.5f),
                    new Vector3(3.4f, 2f, 2.2f),
                    new Color(0.20f, 0.48f, 0.36f),
                    FindFacilityDefinition(facilityDefinitions, HideoutFacilityType.Trader),
                    worldController,
                    mapRoot);
                // 출격 터미널은 남쪽 벽의 차량 출입구처럼 배치해 창고와 다른 시설이라는 점을 월드에서도 분명히 보이게 합니다.
                HideoutFacility deploymentFacility = CreateHideoutFacilityObject(
                    "DeploymentFacility",
                    new Vector3(0f, 1.1f, -9.8f),
                    new Vector3(5.2f, 2.2f, 0.8f),
                    new Color(0.26f, 0.40f, 0.62f),
                    FindFacilityDefinition(facilityDefinitions, HideoutFacilityType.Deployment),
                    worldController,
                    mapRoot);
                HideoutFacility upgradeStationFacility = CreateHideoutFacilityObject(
                    "UpgradeStationFacility",
                    new Vector3(-1.8f, 0.9f, 3.6f),
                    new Vector3(2.8f, 1.8f, 1.5f),
                    new Color(0.48f, 0.31f, 0.62f),
                    FindFacilityDefinition(facilityDefinitions, HideoutFacilityType.UpgradeStation),
                    worldController,
                    mapRoot);

                // 임무 목록은 보드 오브젝트가 보관하고 같은 Collider의 HideoutFacility가 E키 입력을 월드 관리자에게 전달합니다.
                HideoutMissionBoard missionBoard = missionFacility.gameObject.AddComponent<HideoutMissionBoard>();
                missionBoard.Configure(missionDefinitions, hideoutController, missionFacility.Definition);

                HideoutResultHud storageHud = null;
                if (storageCanvasPrefab != null)
                {
                    GameObject storageCanvas = (GameObject)PrefabUtility.InstantiatePrefab(storageCanvasPrefab, scene);
                    storageHud = storageCanvas.GetComponent<HideoutResultHud>();
                    storageHud?.Configure(hideoutController);
                }

                InventoryHud playerInventoryHud = null;
                if (inventoryCanvasPrefab != null)
                {
                    GameObject inventoryCanvas = (GameObject)PrefabUtility.InstantiatePrefab(inventoryCanvasPrefab, scene);
                    playerInventoryHud = inventoryCanvas.GetComponent<InventoryHud>();
                    playerInventoryHud?.Configure(player.GetComponent<PlayerInventory>(), interaction);
                    playerInventoryHud?.SetKeyboardToggleEnabled(false);
                }

                HideoutMissionBoardHud missionHud = null;
                if (missionCanvasPrefab != null)
                {
                    GameObject missionCanvas = (GameObject)PrefabUtility.InstantiatePrefab(missionCanvasPrefab, scene);
                    missionHud = missionCanvas.GetComponent<HideoutMissionBoardHud>();
                    missionHud?.SetContext(missionBoard, worldController);
                }

                HideoutFacilityUpgradeHud upgradeHud = null;
                if (facilityUpgradeCanvasPrefab != null)
                {
                    GameObject upgradeCanvas = (GameObject)PrefabUtility.InstantiatePrefab(facilityUpgradeCanvasPrefab, scene);
                    upgradeHud = upgradeCanvas.GetComponent<HideoutFacilityUpgradeHud>();
                    upgradeHud?.SetContext(hideoutController, worldController);
                }

                HideoutTraderHud traderHud = null;
                if (traderCanvasPrefab != null)
                {
                    GameObject traderCanvas = (GameObject)PrefabUtility.InstantiatePrefab(traderCanvasPrefab, scene);
                    traderHud = traderCanvas.GetComponent<HideoutTraderHud>();
                    traderHud?.SetContext(hideoutController, worldController);
                }

                HideoutDeploymentHud deploymentHud = null;
                if (deploymentCanvasPrefab != null)
                {
                    GameObject deploymentCanvas = (GameObject)PrefabUtility.InstantiatePrefab(deploymentCanvasPrefab, scene);
                    deploymentHud = deploymentCanvas.GetComponent<HideoutDeploymentHud>();
                    deploymentHud?.SetContext(hideoutController, worldController);
                }

                HideoutWorldHud worldHud = null;
                if (worldCanvasPrefab != null)
                {
                    GameObject worldCanvas = (GameObject)PrefabUtility.InstantiatePrefab(worldCanvasPrefab, scene);
                    worldHud = worldCanvas.GetComponent<HideoutWorldHud>();
                    worldHud?.SetContext(worldController, interaction);
                }

                // 모든 참조를 연결한 뒤 시설 Canvas만 닫아 첫 화면에는 걸어 다니는 거점과 조작 안내만 보이게 합니다.
                worldController.Configure(storageHud, missionHud, upgradeHud, worldHud, hideoutController, interaction, controlGate, player.transform, cameraRig.transform);
                // ConfigureTrader는 내부에서 상인 HUD에 현재 컨트롤러를 전달하므로 기본 Configure 뒤에 호출해야 null 컨텍스트가 남지 않습니다.
                worldController.ConfigureTrader(traderHud);
                // 출격 Canvas도 기본 Configure 뒤에 연결해야 HideoutController와 worldController 두 참조를 모두 안전하게 전달할 수 있습니다.
                worldController.ConfigureDeployment(deploymentHud);
                worldController.ConfigurePlayerInventoryHud(playerInventoryHud);

                // 거점에서도 같은 ESC 메뉴를 사용하지만 레이드 세션 대신 시설 관리자와 거점 플레이어 잠금 참조를 전달합니다.
                GameObject pauseCanvasPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(Root + "/Prefabs/UI/PauseCanvas.prefab");
                PauseMenuHud pauseMenuHud = null;
                if (pauseCanvasPrefab != null)
                {
                    GameObject pauseCanvas = (GameObject)PrefabUtility.InstantiatePrefab(pauseCanvasPrefab, scene);
                    pauseMenuHud = pauseCanvas.GetComponent<PauseMenuHud>();
                    pauseMenuHud?.SetContext(playerInventoryHud, worldController, null, controlGate);
                }
                if (storageHud != null) storageHud.gameObject.SetActive(false);
                if (missionHud != null) missionHud.gameObject.SetActive(false);
                if (traderHud != null) traderHud.gameObject.SetActive(false);
                if (deploymentHud != null) deploymentHud.gameObject.SetActive(false);
                if (upgradeHud != null) upgradeHud.gameObject.SetActive(false);

                // Configure로 넣은 씬 참조를 직렬화 변경으로 표시해야 씬을 저장한 뒤에도 연결이 유지됩니다.
                EditorUtility.SetDirty(hideoutController);
                EditorUtility.SetDirty(worldController);
                EditorUtility.SetDirty(storageFacility);
                EditorUtility.SetDirty(missionFacility);
                EditorUtility.SetDirty(craftingFacility);
                EditorUtility.SetDirty(infirmaryFacility);
                EditorUtility.SetDirty(generatorFacility);
                EditorUtility.SetDirty(traderFacility);
                EditorUtility.SetDirty(deploymentFacility);
                EditorUtility.SetDirty(upgradeStationFacility);
                EditorUtility.SetDirty(missionBoard);
                if (storageHud != null) EditorUtility.SetDirty(storageHud);
                if (missionHud != null) EditorUtility.SetDirty(missionHud);
                if (upgradeHud != null) EditorUtility.SetDirty(upgradeHud);
                if (traderHud != null) EditorUtility.SetDirty(traderHud);
                if (deploymentHud != null) EditorUtility.SetDirty(deploymentHud);
                if (worldHud != null) EditorUtility.SetDirty(worldHud);
                if (playerInventoryHud != null) EditorUtility.SetDirty(playerInventoryHud);
                if (pauseMenuHud != null) EditorUtility.SetDirty(pauseMenuHud);

                // 버튼 입력을 처리할 EventSystem을 하나만 만들고 완성된 Additive 씬을 지정 경로에 저장합니다.
                CreateEventSystem();
                EditorSceneManager.SaveScene(scene, scenePath);
            }
            finally
            {
                // 생성이 성공하거나 예외가 발생해도 원래 작업하던 씬으로 되돌아가도록 정리합니다.
                if (!replaceEmptyUntitledScene)
                {
                    if (previousActiveScene.IsValid() && previousActiveScene.isLoaded)
                        SceneManager.SetActiveScene(previousActiveScene);
                    if (scene.IsValid() && scene.isLoaded)
                        EditorSceneManager.CloseScene(scene, true);
                }
            }
        }

        // 공용 시설 오브젝트 생성 규칙입니다. Primitive의 BoxCollider를 그대로 사용해 플레이어가 시설을 뚫고 지나가지 못하게 합니다.
        // 상호작용 반경 검사는 이 Collider의 가장 가까운 표면을 기준으로 하므로 크기가 다른 시설도 같은 E키 규칙을 사용할 수 있습니다.
        private static HideoutFacility CreateHideoutFacilityObject(
            string name,
            Vector3 position,
            Vector3 scale,
            Color color,
            HideoutFacilityDefinition definition,
            HideoutWorldController worldController,
            Transform parent)
        {
            GameObject facilityObject = GameObject.CreatePrimitive(PrimitiveType.Cube);
            facilityObject.name = name;
            facilityObject.transform.SetParent(parent, true);
            facilityObject.transform.position = position;
            facilityObject.transform.localScale = scale;
            SetColor(facilityObject, color);

            HideoutFacility facility = facilityObject.AddComponent<HideoutFacility>();
            facility.Configure(definition, worldController);
            return facility;
        }

        // 시설 배열의 순서를 믿지 않고 종류로 검색하므로 나중에 제작대나 치료소가 배열 중간에 추가되어도 기존 배치가 잘못 연결되지 않습니다.
        private static HideoutFacilityDefinition FindFacilityDefinition(
            HideoutFacilityDefinition[] definitions,
            HideoutFacilityType type)
        {
            if (definitions == null) return null;
            foreach (HideoutFacilityDefinition definition in definitions)
                if (definition != null && definition.FacilityType == type) return definition;
            return null;
        }

        // 타이틀은 월드 오브젝트나 게임 상태 컨트롤러 없이 메뉴 Canvas, 카메라, 입력 EventSystem만 가진 독립 씬으로 만듭니다.
        // 이 씬이 Build Settings의 첫 번째 항목이므로 빌드를 실행하면 항상 네 가지 시작 메뉴부터 표시됩니다.
        private static void CreateTitleScene(GameObject titleCanvasPrefab)
        {
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            scene.name = GameSceneNavigator.TitleSceneName;

            var cameraObject = new GameObject("Main Camera");
            cameraObject.tag = "MainCamera";
            Camera titleCamera = cameraObject.AddComponent<Camera>();
            titleCamera.clearFlags = CameraClearFlags.SolidColor;
            titleCamera.backgroundColor = new Color(0.008f, 0.012f, 0.016f, 1f);
            cameraObject.AddComponent<AudioListener>();

            if (titleCanvasPrefab != null)
                PrefabUtility.InstantiatePrefab(titleCanvasPrefab, scene);
            else
                Debug.LogError("Title 씬 생성 실패: TitleCanvas 프리팹이 없습니다.");

            CreateEventSystem();
            EditorSceneManager.SaveScene(scene, Root + "/Scenes/" + GameSceneNavigator.TitleSceneName + ".unity");
        }

        // Loading 씬은 월드 지형 없이 전체 화면 Canvas, 카메라, 비동기 로드 컨트롤러만 가진 전환 전용 씬입니다.
        // 목적지는 GameSceneNavigator가 전달하며 씬을 직접 실행했을 때는 기본값인 Hideout으로 이동합니다.
        private static void CreateLoadingScene(GameObject loadingCanvasPrefab)
        {
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            scene.name = GameSceneNavigator.LoadingSceneName;

            var cameraObject = new GameObject("Main Camera");
            cameraObject.tag = "MainCamera";
            Camera loadingCamera = cameraObject.AddComponent<Camera>();
            loadingCamera.clearFlags = CameraClearFlags.SolidColor;
            loadingCamera.backgroundColor = new Color(0.008f, 0.012f, 0.016f, 1f);
            cameraObject.AddComponent<AudioListener>();

            LoadingScreenHud hud = null;
            if (loadingCanvasPrefab != null)
            {
                GameObject canvas = (GameObject)PrefabUtility.InstantiatePrefab(loadingCanvasPrefab, scene);
                hud = canvas.GetComponent<LoadingScreenHud>();
            }
            else
                Debug.LogError("Loading 씬 생성 실패: LoadingCanvas 프리팹이 없습니다.");

            var controllerObject = new GameObject("LoadingSceneController");
            LoadingSceneController controller = controllerObject.AddComponent<LoadingSceneController>();
            controller.Configure(hud);

            // 현재 화면에는 버튼이 없지만 향후 팁 넘기기나 취소 UI가 추가되어도 같은 씬 구조를 그대로 사용할 수 있게 둡니다.
            CreateEventSystem();
            EditorSceneManager.SaveScene(scene, Root + "/Scenes/" + GameSceneNavigator.LoadingSceneName + ".unity");
        }

        // 현재 Input Manager 기반 UI 모듈을 사용하는 공용 EventSystem을 생성합니다.
        private static void CreateEventSystem()
        {
            var eventSystemObject = new GameObject("EventSystem");
            eventSystemObject.AddComponent<EventSystem>();
            eventSystemObject.AddComponent<StandaloneInputModule>();
        }

        // 각 씬이 기본적으로 보이도록 공통 방향광을 추가합니다.
        private static void CreateLighting()
        {
            var lightObject = new GameObject("Directional Light");
            lightObject.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
            Light light = lightObject.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.2f;
        }

        // 36×36 크기의 레이드 바닥과 구역별 통로형 엄폐물을 만듭니다.
        // NavMeshAgent 여러 마리가 동시에 통과할 수 있도록 각 구역에 충분한 폭의 출입구를 남깁니다.
        private static void CreateRaidMapGeometry()
        {
            var mapRoot = new GameObject("RaidMap");
            // 이 루트 아래의 바닥과 벽 Collider만 수집해 플레이어와 좀비 몸 자체가 NavMesh 장애물로 구워지지 않게 합니다.
            mapRoot.AddComponent<RaidNavigationSurface>();

            GameObject floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
            floor.name = "Ground";
            floor.transform.SetParent(mapRoot.transform, false);
            floor.transform.localScale = new Vector3(3.6f, 1f, 3.6f);
            SetColor(floor, new Color(0.16f, 0.18f, 0.17f));

            // 외곽 벽은 바닥 가장자리를 막되 카메라에서 구역 내부가 잘 보이도록 높이를 2로 유지합니다.
            float edge = RaidMapLayout.MapHalfExtent;
            CreateWall("Wall_North", new Vector3(0f, 1f, edge), new Vector3(37f, 2f, 1f), mapRoot.transform);
            CreateWall("Wall_South", new Vector3(0f, 1f, -edge), new Vector3(37f, 2f, 1f), mapRoot.transform);
            CreateWall("Wall_East", new Vector3(edge, 1f, 0f), new Vector3(1f, 2f, 37f), mapRoot.transform);
            CreateWall("Wall_West", new Vector3(-edge, 1f, 0f), new Vector3(1f, 2f, 37f), mapRoot.transform);

            // 남북 도로와 동서 연결로는 Collider가 없는 얇은 색상 표식이라 이동이나 총알을 막지 않습니다.
            CreateZoneFloor("Road_NorthSouth", Vector3.zero, new Vector2(5f, 35f), new Color(0.22f, 0.23f, 0.24f), mapRoot.transform, 0.006f);
            CreateZoneFloor("Road_EastWest", new Vector3(0f, 0f, 3f), new Vector2(35f, 4f), new Color(0.22f, 0.23f, 0.24f), mapRoot.transform, 0.006f);

            Transform southCamp = CreateZoneRoot("Zone_SouthCamp", mapRoot.transform);
            CreateZoneFloor("Floor_SouthCamp", RaidMapLayout.SouthCampCenter, new Vector2(15f, 7f), new Color(0.24f, 0.28f, 0.22f), southCamp);
            CreateWall("Fence_SouthCamp_West", new Vector3(-7f, 0.75f, -13.5f), new Vector3(1f, 1.5f, 7f), southCamp);
            CreateWall("Fence_SouthCamp_East", new Vector3(7f, 0.75f, -13.5f), new Vector3(1f, 1.5f, 7f), southCamp);
            CreateWall("Barrier_SouthCamp_Left", new Vector3(-5f, 0.65f, -9.5f), new Vector3(5f, 1.3f, 1f), southCamp);
            CreateWall("Barrier_SouthCamp_Right", new Vector3(5f, 0.65f, -9.5f), new Vector3(5f, 1.3f, 1f), southCamp);
            CreateWall("SupplyPallet_SouthCamp", new Vector3(-3.5f, 0.5f, -14f), new Vector3(2.5f, 1f, 1.2f), southCamp, new Color(0.32f, 0.25f, 0.16f));

            Transform westResidential = CreateZoneRoot("Zone_WestResidential", mapRoot.transform);
            CreateZoneFloor("Floor_WestResidential", RaidMapLayout.WestResidentialCenter, new Vector2(11f, 20f), new Color(0.28f, 0.24f, 0.20f), westResidential);
            CreateWall("House_South_NorthWall", new Vector3(-13f, 1f, -6f), new Vector3(8f, 2f, 1f), westResidential, new Color(0.38f, 0.30f, 0.24f));
            CreateWall("House_South_EastWall", new Vector3(-9f, 1f, -3.5f), new Vector3(1f, 2f, 6f), westResidential, new Color(0.38f, 0.30f, 0.24f));
            CreateWall("House_North_SouthWall", new Vector3(-13f, 1f, 4f), new Vector3(8f, 2f, 1f), westResidential, new Color(0.38f, 0.30f, 0.24f));
            CreateWall("House_North_EastWall", new Vector3(-9f, 1f, 6.5f), new Vector3(1f, 2f, 6f), westResidential, new Color(0.38f, 0.30f, 0.24f));
            CreateWall("ResidentialCar", new Vector3(-12f, 0.55f, 0.5f), new Vector3(3f, 1.1f, 1.5f), westResidential, new Color(0.24f, 0.30f, 0.32f), 12f);

            Transform centralCheckpoint = CreateZoneRoot("Zone_CentralCheckpoint", mapRoot.transform);
            CreateZoneFloor("Floor_CentralCheckpoint", RaidMapLayout.CentralCheckpointCenter, new Vector2(8f, 9f), new Color(0.26f, 0.26f, 0.22f), centralCheckpoint);
            CreateWall("CheckpointBarrier_West", new Vector3(-2.5f, 0.55f, 3.8f), new Vector3(3.5f, 1.1f, 0.8f), centralCheckpoint, new Color(0.48f, 0.42f, 0.20f), -12f);
            CreateWall("CheckpointBarrier_East", new Vector3(2.5f, 0.55f, 1.8f), new Vector3(3.5f, 1.1f, 0.8f), centralCheckpoint, new Color(0.48f, 0.42f, 0.20f), -12f);
            CreateWall("CheckpointBooth", new Vector3(4f, 1f, 6f), new Vector3(2.5f, 2f, 2.5f), centralCheckpoint, new Color(0.30f, 0.34f, 0.30f));

            Transform eastWarehouse = CreateZoneRoot("Zone_EastWarehouse", mapRoot.transform);
            CreateZoneFloor("Floor_EastWarehouse", RaidMapLayout.EastWarehouseCenter, new Vector2(12f, 21f), new Color(0.20f, 0.24f, 0.28f), eastWarehouse);
            CreateWall("Warehouse_NorthWall", new Vector3(12.5f, 1f, 10.5f), new Vector3(10f, 2f, 1f), eastWarehouse, new Color(0.30f, 0.34f, 0.38f));
            CreateWall("Warehouse_EastWall", new Vector3(16.5f, 1f, 5f), new Vector3(1f, 2f, 11f), eastWarehouse, new Color(0.30f, 0.34f, 0.38f));
            CreateWall("WarehouseRack_A", new Vector3(10f, 0.75f, 1.5f), new Vector3(1f, 1.5f, 6f), eastWarehouse, new Color(0.26f, 0.28f, 0.30f));
            CreateWall("WarehouseRack_B", new Vector3(14f, 0.75f, -1f), new Vector3(1f, 1.5f, 6f), eastWarehouse, new Color(0.26f, 0.28f, 0.30f));
            CreateWall("WarehouseCrates", new Vector3(12f, 0.75f, 7.5f), new Vector3(3f, 1.5f, 2f), eastWarehouse, new Color(0.35f, 0.27f, 0.17f));

            Transform northExtraction = CreateZoneRoot("Zone_NorthExtraction", mapRoot.transform);
            CreateZoneFloor("Floor_NorthExtraction", RaidMapLayout.NorthExtractionCenter, new Vector2(10f, 6f), new Color(0.18f, 0.32f, 0.22f), northExtraction);
            CreateWall("ExtractionFence_West", new Vector3(-5f, 1f, 14.5f), new Vector3(1f, 2f, 7f), northExtraction);
            CreateWall("ExtractionFence_East", new Vector3(5f, 1f, 14.5f), new Vector3(1f, 2f, 7f), northExtraction);
        }

        // 학교 지역의 첫 플레이 테스트를 위한 대형 회색 상자 지형을 만듭니다.
        // 실제 학교에서 자주 보이는 정문·행정동·교실동·특별실동·급식실·체육관·운동장 구성을 한 캠퍼스 안에 배치합니다.
        // 최종 모델이 없어도 실내 수색, 긴 복도 교전, 중앙정원 우회, 운동장 탈출처럼 서로 다른 전투 흐름을 먼저 검증할 수 있습니다.
        private static void CreateSchoolRaidMapGeometry()
        {
            var mapRoot = new GameObject("RaidMap");
            // 이 컴포넌트는 아래에 만든 바닥과 벽 Collider만 수집해 학교 복도와 출입구를 따라 NavMesh를 실행 시 다시 굽습니다.
            mapRoot.AddComponent<RaidNavigationSurface>();

            GameObject floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
            floor.name = "SchoolGround";
            floor.transform.SetParent(mapRoot.transform, false);
            // Unity Plane의 기본 크기는 10m이므로 8.4배로 늘리면 학교 전용 84m × 84m 부지가 됩니다.
            floor.transform.localScale = new Vector3(8.4f, 1f, 8.4f);
            SetColor(floor, new Color(0.15f, 0.17f, 0.16f));

            float edge = SchoolRaidMapLayout.MapHalfExtent;
            Color boundaryColor = new Color(0.25f, 0.28f, 0.27f);
            CreateWall("Boundary_North", new Vector3(0f, 1f, edge), new Vector3(85f, 2f, 1f), mapRoot.transform, boundaryColor);
            CreateWall("Boundary_South", new Vector3(0f, 1f, -edge), new Vector3(85f, 2f, 1f), mapRoot.transform, boundaryColor);
            CreateWall("Boundary_East", new Vector3(edge, 1f, 0f), new Vector3(1f, 2f, 85f), mapRoot.transform, boundaryColor);
            CreateWall("Boundary_West", new Vector3(-edge, 1f, 0f), new Vector3(1f, 2f, 85f), mapRoot.transform, boundaryColor);

            // 정문 구역은 레이드 시작 지점과 주차장을 포함합니다. 건물 앞 펜스에는 중앙 출입구를 넓게 남겨 대규모 추격 상황에서도 막히지 않습니다.
            Transform entrance = CreateZoneRoot("Zone_SchoolEntrance", mapRoot.transform);
            CreateZoneFloor("EntranceAndParking", new Vector3(0f, 0f, -35f), new Vector2(80f, 13f), new Color(0.24f, 0.25f, 0.23f), entrance);
            CreateHorizontalWallWithDoor("FrontFence", -29.5f, -41f, 41f, 0f, 9f, 1.6f, entrance, new Color(0.30f, 0.34f, 0.32f), 0.5f);
            CreateWall("SecurityOffice", new Vector3(7f, 1.2f, -33.5f), new Vector3(5f, 2.4f, 4.5f), entrance, new Color(0.38f, 0.40f, 0.36f));
            CreateWall("ParkingCar_A", new Vector3(-29f, 0.65f, -35f), new Vector3(4f, 1.3f, 2f), entrance, new Color(0.30f, 0.34f, 0.38f));
            CreateWall("ParkingCar_B", new Vector3(-20f, 0.65f, -38f), new Vector3(4f, 1.3f, 2f), entrance, new Color(0.36f, 0.30f, 0.28f));
            CreateWall("BicycleRack", new Vector3(26f, 0.55f, -35f), new Vector3(7f, 1.1f, 1f), entrance, new Color(0.31f, 0.34f, 0.35f));

            // 행정동은 교장실·교무실·보건실을 하나의 건물로 묶고 남쪽 정문과 북쪽 교실동 양쪽에서 들어갈 수 있게 합니다.
            Transform administration = CreateZoneRoot("Zone_Administration", mapRoot.transform);
            CreateZoneFloor("AdministrationFloor", new Vector3(-27.5f, 0f, -21f), new Vector2(23f, 12f), new Color(0.33f, 0.29f, 0.24f), administration);
            Color administrationWall = new Color(0.48f, 0.42f, 0.34f);
            CreateHorizontalWallWithDoor("AdministrationSouth", -27f, -39f, -16f, -28f, 3.5f, 2.6f, administration, administrationWall);
            CreateHorizontalWallWithDoor("AdministrationNorth", -15f, -39f, -16f, -23f, 3.5f, 2.6f, administration, administrationWall);
            CreateWall("AdministrationWest", new Vector3(-39f, 1.3f, -21f), new Vector3(0.8f, 2.6f, 12f), administration, administrationWall);
            CreateVerticalWallWithDoor("AdministrationEast", -16f, -27f, -15f, -21f, 3.5f, 2.6f, administration, administrationWall);
            CreateVerticalWallWithDoor("AdministrationRoomDivider", -31f, -26.5f, -15.5f, -21f, 2.5f, 2.3f, administration, new Color(0.42f, 0.36f, 0.30f), 0.55f);
            CreateWall("AdministrationDesk_A", new Vector3(-35f, 0.55f, -23f), new Vector3(3.4f, 1.1f, 1.1f), administration, new Color(0.31f, 0.23f, 0.16f));
            CreateWall("AdministrationDesk_B", new Vector3(-23f, 0.55f, -18f), new Vector3(4f, 1.1f, 1.1f), administration, new Color(0.31f, 0.23f, 0.16f));

            // 급식동은 식당과 조리실을 나누되 남쪽 외부, 서쪽 중앙 현관, 북쪽 체육관 방향에 모두 문을 둡니다.
            Transform cafeteria = CreateZoneRoot("Zone_Cafeteria", mapRoot.transform);
            CreateZoneFloor("CafeteriaFloor", new Vector3(24.5f, 0f, -21f), new Vector2(27f, 12f), new Color(0.34f, 0.29f, 0.21f), cafeteria);
            Color cafeteriaWall = new Color(0.46f, 0.39f, 0.29f);
            CreateHorizontalWallWithDoor("CafeteriaSouth", -27f, 11f, 38f, 22f, 4f, 2.6f, cafeteria, cafeteriaWall);
            CreateHorizontalWallWithDoor("CafeteriaNorth", -15f, 11f, 38f, 28f, 4f, 2.6f, cafeteria, cafeteriaWall);
            CreateVerticalWallWithDoor("CafeteriaWest", 11f, -27f, -15f, -21f, 4f, 2.6f, cafeteria, cafeteriaWall);
            CreateWall("CafeteriaEast", new Vector3(38f, 1.3f, -21f), new Vector3(0.8f, 2.6f, 12f), cafeteria, cafeteriaWall);
            CreateVerticalWallWithDoor("KitchenDivider", 31f, -26.5f, -15.5f, -20f, 2.5f, 2.3f, cafeteria, new Color(0.40f, 0.35f, 0.29f), 0.55f);
            CreateWall("ServingCounter", new Vector3(28.5f, 0.7f, -18f), new Vector3(4f, 1.4f, 1f), cafeteria, new Color(0.38f, 0.31f, 0.22f));
            CreateWall("CafeteriaTable_A", new Vector3(17f, 0.45f, -18.5f), new Vector3(3.8f, 0.9f, 1.1f), cafeteria, new Color(0.34f, 0.27f, 0.18f));
            CreateWall("CafeteriaTable_B", new Vector3(23f, 0.45f, -22f), new Vector3(3.8f, 0.9f, 1.1f), cafeteria, new Color(0.34f, 0.27f, 0.18f));
            CreateWall("CafeteriaTable_C", new Vector3(17f, 0.45f, -24.5f), new Vector3(3.8f, 0.9f, 1.1f), cafeteria, new Color(0.34f, 0.27f, 0.18f));

            // 서쪽 교실동은 긴 복도와 여러 교실로 나눕니다. 벽 사이의 3m 이상 문은 좀비와 플레이어가 함께 통과할 수 있는 폭입니다.
            Transform classroomWing = CreateZoneRoot("Zone_ClassroomWing", mapRoot.transform);
            CreateZoneFloor("ClassroomFloor", new Vector3(-27f, 0f, 2f), new Vector2(22f, 32f), new Color(0.28f, 0.25f, 0.21f), classroomWing);
            Color classroomWall = new Color(0.44f, 0.39f, 0.32f);
            CreateWall("ClassroomOuterWest", new Vector3(-38f, 1.3f, 2f), new Vector3(0.8f, 2.6f, 32f), classroomWing, classroomWall);
            CreateHorizontalWallWithDoor("ClassroomOuterSouth", -14f, -38f, -16f, -23f, 4f, 2.6f, classroomWing, classroomWall);
            CreateHorizontalWallWithDoor("ClassroomOuterNorth", 18f, -38f, -16f, -24f, 4f, 2.6f, classroomWing, classroomWall);
            // 동쪽 벽을 여러 조각으로 만들어 중앙정원으로 이어지는 출입구를 두 곳 확보합니다.
            CreateWall("ClassroomEast_A", new Vector3(-16f, 1.3f, -11f), new Vector3(0.8f, 2.6f, 6f), classroomWing, classroomWall);
            CreateWall("ClassroomEast_B", new Vector3(-16f, 1.3f, 1.5f), new Vector3(0.8f, 2.6f, 13f), classroomWing, classroomWall);
            CreateWall("ClassroomEast_C", new Vector3(-16f, 1.3f, 14.5f), new Vector3(0.8f, 2.6f, 7f), classroomWing, classroomWall);
            CreateHorizontalWallWithDoor("ClassroomDivider_South", -4f, -37.5f, -20f, -24f, 3f, 2.3f, classroomWing, new Color(0.40f, 0.35f, 0.29f), 0.55f);
            CreateHorizontalWallWithDoor("ClassroomDivider_North", 7f, -37.5f, -20f, -24f, 3f, 2.3f, classroomWing, new Color(0.40f, 0.35f, 0.29f), 0.55f);
            CreateWall("ClassroomDesk_South_A", new Vector3(-32f, 0.5f, -9f), new Vector3(4f, 1f, 1f), classroomWing, new Color(0.31f, 0.23f, 0.15f));
            CreateWall("ClassroomDesk_South_B", new Vector3(-25f, 0.5f, -9f), new Vector3(4f, 1f, 1f), classroomWing, new Color(0.31f, 0.23f, 0.15f));
            CreateWall("ClassroomDesk_Center", new Vector3(-29f, 0.5f, 2f), new Vector3(5f, 1f, 1f), classroomWing, new Color(0.31f, 0.23f, 0.15f));
            CreateWall("ClassroomDesk_North_A", new Vector3(-33f, 0.5f, 12f), new Vector3(4f, 1f, 1f), classroomWing, new Color(0.31f, 0.23f, 0.15f));
            CreateWall("ClassroomDesk_North_B", new Vector3(-26f, 0.5f, 12f), new Vector3(4f, 1f, 1f), classroomWing, new Color(0.31f, 0.23f, 0.15f));
            // 기존 1층 교실 위에 실제 보행 가능한 2층과 옥상을 얹습니다. 다른 학교 건물은 아직 단층 상태로 두어 투과 기능의 범위를 한 채로 제한합니다.
            CreateSchoolTwoStoreyClassroom(classroomWing, classroomWall);

            // 중앙정원은 여러 건물을 잇는 허브입니다. 낮은 화단은 엄폐물이 되지만 양옆 통로와 중앙 길은 항상 열어 둡니다.
            Transform courtyard = CreateZoneRoot("Zone_Courtyard", mapRoot.transform);
            CreateZoneFloor("CourtyardFloor", new Vector3(0f, 0f, 1f), new Vector2(30f, 30f), new Color(0.19f, 0.28f, 0.19f), courtyard);
            CreateZoneFloor("CourtyardCrossPath_NS", new Vector3(0f, 0f, 1f), new Vector2(4f, 30f), new Color(0.34f, 0.34f, 0.31f), courtyard, 0.018f);
            CreateZoneFloor("CourtyardCrossPath_EW", new Vector3(0f, 0f, 1f), new Vector2(30f, 4f), new Color(0.34f, 0.34f, 0.31f), courtyard, 0.018f);
            CreateWall("CourtyardPlanter_SW", new Vector3(-8f, 0.45f, -7f), new Vector3(4f, 0.9f, 3f), courtyard, new Color(0.32f, 0.30f, 0.21f));
            CreateWall("CourtyardPlanter_SE", new Vector3(8f, 0.45f, -7f), new Vector3(4f, 0.9f, 3f), courtyard, new Color(0.32f, 0.30f, 0.21f));
            CreateWall("CourtyardPlanter_NW", new Vector3(-8f, 0.45f, 9f), new Vector3(4f, 0.9f, 3f), courtyard, new Color(0.32f, 0.30f, 0.21f));
            CreateWall("CourtyardPlanter_NE", new Vector3(8f, 0.45f, 9f), new Vector3(4f, 0.9f, 3f), courtyard, new Color(0.32f, 0.30f, 0.21f));
            CreateWall("CourtyardMonument", new Vector3(0f, 0.9f, 1f), new Vector3(1.5f, 1.8f, 1.5f), courtyard, new Color(0.48f, 0.48f, 0.44f));

            // 북쪽 특별실동은 도서관·과학실·음악실·컴퓨터실을 긴 한 동에 배치합니다.
            Transform specialClassWing = CreateZoneRoot("Zone_SpecialClassWing", mapRoot.transform);
            CreateZoneFloor("SpecialClassFloor", new Vector3(-11f, 0f, 23.5f), new Vector2(54f, 11f), new Color(0.25f, 0.27f, 0.31f), specialClassWing);
            Color specialWall = new Color(0.37f, 0.40f, 0.46f);
            CreateWall("SpecialClassWest", new Vector3(-38f, 1.3f, 23.5f), new Vector3(0.8f, 2.6f, 11f), specialClassWing, specialWall);
            CreateWall("SpecialClassEast", new Vector3(16f, 1.3f, 23.5f), new Vector3(0.8f, 2.6f, 11f), specialClassWing, specialWall);
            CreateHorizontalWallWithDoor("SpecialClassSouth", 18f, -38f, 16f, -10f, 5f, 2.6f, specialClassWing, specialWall);
            CreateHorizontalWallWithDoor("SpecialClassNorth", 29f, -38f, 16f, 0f, 5f, 2.6f, specialClassWing, specialWall);
            CreateVerticalWallWithDoor("LibraryDivider", -21f, 18.5f, 28.5f, 23.5f, 2.5f, 2.3f, specialClassWing, new Color(0.34f, 0.36f, 0.41f), 0.55f);
            CreateVerticalWallWithDoor("ScienceRoomDivider", -4f, 18.5f, 28.5f, 23.5f, 2.5f, 2.3f, specialClassWing, new Color(0.34f, 0.36f, 0.41f), 0.55f);
            CreateVerticalWallWithDoor("MusicRoomDivider", 8f, 18.5f, 28.5f, 23.5f, 2.5f, 2.3f, specialClassWing, new Color(0.34f, 0.36f, 0.41f), 0.55f);
            CreateWall("LibraryShelf_A", new Vector3(-32f, 0.8f, 21f), new Vector3(1.1f, 1.6f, 3.5f), specialClassWing, new Color(0.29f, 0.23f, 0.18f));
            CreateWall("LibraryShelf_B", new Vector3(-27f, 0.8f, 26f), new Vector3(1.1f, 1.6f, 3.5f), specialClassWing, new Color(0.29f, 0.23f, 0.18f));
            CreateWall("ScienceBench", new Vector3(-12f, 0.55f, 24f), new Vector3(5f, 1.1f, 1.2f), specialClassWing, new Color(0.28f, 0.32f, 0.32f));

            // 동쪽 체육관은 교실동과 다른 넓은 실내 전투 공간입니다. 사방 출입구로 급식실·정원·운동장을 연결합니다.
            Transform gym = CreateZoneRoot("Zone_Gymnasium", mapRoot.transform);
            CreateZoneFloor("GymFloor", new Vector3(27.5f, 0f, 3.5f), new Vector2(21f, 31f), new Color(0.20f, 0.24f, 0.30f), gym);
            Color gymWall = new Color(0.30f, 0.35f, 0.42f);
            CreateVerticalWallWithDoor("GymWest", 17f, -12f, 19f, 4f, 5f, 3f, gym, gymWall);
            CreateWall("GymEast", new Vector3(38f, 1.5f, 3.5f), new Vector3(0.8f, 3f, 31f), gym, gymWall);
            CreateHorizontalWallWithDoor("GymSouth", -12f, 17f, 38f, 28f, 4f, 3f, gym, gymWall);
            CreateHorizontalWallWithDoor("GymNorth", 19f, 17f, 38f, 27f, 5f, 3f, gym, gymWall);
            CreateZoneFloor("BasketballCourtMarking", new Vector3(27.5f, 0f, 3.5f), new Vector2(14f, 23f), new Color(0.43f, 0.29f, 0.18f), gym, 0.018f);
            CreateWall("GymBleachers_North", new Vector3(35.5f, 0.8f, 8f), new Vector3(2.5f, 1.6f, 9f), gym, new Color(0.34f, 0.31f, 0.26f));
            CreateWall("GymBleachers_South", new Vector3(35.5f, 0.8f, -7f), new Vector3(2.5f, 1.6f, 6f), gym, new Color(0.34f, 0.31f, 0.26f));
            CreateWall("GymEquipmentRack", new Vector3(20f, 0.65f, 15.5f), new Vector3(4f, 1.3f, 1.2f), gym, new Color(0.28f, 0.30f, 0.33f));

            // 북쪽 운동장은 건물 내부보다 시야가 길고 엄폐물이 적은 마지막 위험 구간입니다.
            Transform playground = CreateZoneRoot("Zone_PlaygroundExtraction", mapRoot.transform);
            CreateZoneFloor("PlaygroundTrack", new Vector3(0f, 0f, 34.5f), new Vector2(76f, 12f), new Color(0.34f, 0.22f, 0.17f), playground);
            CreateZoneFloor("PlaygroundField", new Vector3(0f, 0f, 34.5f), new Vector2(58f, 8f), new Color(0.20f, 0.31f, 0.18f), playground, 0.018f);
            CreateWall("GoalPost_West", new Vector3(-29f, 0.9f, 34.5f), new Vector3(0.5f, 1.8f, 5f), playground, new Color(0.75f, 0.75f, 0.70f));
            CreateWall("GoalPost_East", new Vector3(29f, 0.9f, 34.5f), new Vector3(0.5f, 1.8f, 5f), playground, new Color(0.75f, 0.75f, 0.70f));
            CreateWall("PlaygroundBleachers", new Vector3(-34.5f, 0.8f, 34.5f), new Vector3(3f, 1.6f, 8f), playground, new Color(0.34f, 0.31f, 0.26f));
            // 탈출 지점 주변은 비워 두고 좌우 펜스만 설치해 카운트다운 중 회피할 최소 공간을 남깁니다.
            CreateWall("ExtractionFence_West", new Vector3(-8f, 0.9f, 40f), new Vector3(11f, 1.8f, 0.5f), playground, new Color(0.28f, 0.32f, 0.30f));
            CreateWall("ExtractionFence_East", new Vector3(8f, 0.9f, 40f), new Vector3(11f, 1.8f, 0.5f), playground, new Color(0.28f, 0.32f, 0.30f));
        }

        // 구역 루트를 따로 만들면 Hierarchy에서 특정 지역의 엄폐물과 바닥 표시를 한 번에 접거나 이동할 수 있습니다.
        private static Transform CreateZoneRoot(string name, Transform parent)
        {
            var zone = new GameObject(name);
            zone.transform.SetParent(parent, false);
            return zone.transform;
        }

        // 서쪽 교실동을 2층 테스트 건물로 확장합니다.
        // 2층 구조와 옥상을 서로 다른 FadeTarget으로 분리해 1층에서는 둘 다 흐려지고 2층에서는 카메라를 가리는 옥상만 주로 흐려지게 합니다.
        private static void CreateSchoolTwoStoreyClassroom(Transform classroomWing, Color wallColor)
        {
            var buildingRootObject = new GameObject("Building_TwoStoreyClassroom_Test");
            buildingRootObject.transform.SetParent(classroomWing, false);
            Transform buildingRoot = buildingRootObject.transform;

            float secondFloorY = SchoolRaidMapLayout.ClassroomSecondFloorHeight;
            float roofY = SchoolRaidMapLayout.ClassroomRoofHeight;
            Color secondFloorColor = new Color(0.31f, 0.28f, 0.24f);
            Color upperWallColor = Color.Lerp(wallColor, Color.white, 0.08f);

            // 계단실은 동쪽 벽 가까이에 둡니다. 완만한 20도 경사라 CharacterController와 런타임 NavMesh가 모두 2층으로 올라갈 수 있습니다.
            // 경사로의 북쪽 끝이 2층 높이에 닿도록 X축을 음수 방향으로 기울이며, 2층 바닥에는 같은 크기의 구멍을 남깁니다.
            GameObject stairRamp = CreateWall(
                "StairRamp_1F_to_2F",
                new Vector3(-18.5f, secondFloorY * 0.5f, 11f),
                new Vector3(3.5f, 0.35f, 10f),
                buildingRoot,
                new Color(0.39f, 0.37f, 0.34f));
            stairRamp.transform.rotation = Quaternion.Euler(-18.1f, 0f, 0f);

            // 2층 보행면을 세 조각으로 나눠 x=-21~-16, z=6~16 범위를 계단 구멍으로 비웁니다.
            // 하나의 큰 Cube를 쓰면 경사로 끝을 바닥 Collider가 막아 플레이어가 2층에 올라설 수 없습니다.
            var secondFloorRootObject = new GameObject("Occluder_SecondFloor");
            secondFloorRootObject.transform.SetParent(buildingRoot, false);
            Transform secondFloorRoot = secondFloorRootObject.transform;
            float slabCenterY = secondFloorY - 0.15f;
            CreateWall("SecondFloorSlab_West", new Vector3(-29.5f, slabCenterY, 2f), new Vector3(17f, 0.3f, 32f), secondFloorRoot, secondFloorColor);
            CreateWall("SecondFloorSlab_EastSouth", new Vector3(-18.5f, slabCenterY, -4f), new Vector3(5f, 0.3f, 20f), secondFloorRoot, secondFloorColor);
            CreateWall("SecondFloorSlab_EastNorth", new Vector3(-18.5f, slabCenterY, 17f), new Vector3(5f, 0.3f, 2f), secondFloorRoot, secondFloorColor);

            const float upperWallHeight = 2.7f;
            float upperWallCenterY = secondFloorY + upperWallHeight * 0.5f;
            CreateWall("SecondFloorOuterWest", new Vector3(-38f, upperWallCenterY, 2f), new Vector3(0.8f, upperWallHeight, 32f), secondFloorRoot, upperWallColor);
            CreateWall("SecondFloorOuterEast", new Vector3(-16f, upperWallCenterY, 2f), new Vector3(0.8f, upperWallHeight, 32f), secondFloorRoot, upperWallColor);
            CreateWall("SecondFloorOuterSouth", new Vector3(-27f, upperWallCenterY, -14f), new Vector3(22f, upperWallHeight, 0.8f), secondFloorRoot, upperWallColor);
            CreateWall("SecondFloorOuterNorth", new Vector3(-27f, upperWallCenterY, 18f), new Vector3(22f, upperWallHeight, 0.8f), secondFloorRoot, upperWallColor);
            // 공용 문 벽 함수는 1층 Y=0을 기준으로 만들기 때문에, 2층 칸막이는 문 왼쪽·오른쪽 조각을 2층 높이에 직접 배치합니다.
            float dividerCenterY = secondFloorY + 1.15f;
            Color dividerColor = new Color(0.42f, 0.38f, 0.33f);
            CreateWall("SecondFloorRoomDivider_South_Left", new Vector3(-34f, dividerCenterY, -4f), new Vector3(7f, 2.3f, 0.55f), secondFloorRoot, dividerColor);
            CreateWall("SecondFloorRoomDivider_South_Right", new Vector3(-24.25f, dividerCenterY, -4f), new Vector3(6.5f, 2.3f, 0.55f), secondFloorRoot, dividerColor);
            CreateWall("SecondFloorRoomDivider_North_Left", new Vector3(-34f, dividerCenterY, 7f), new Vector3(7f, 2.3f, 0.55f), secondFloorRoot, dividerColor);
            CreateWall("SecondFloorRoomDivider_North_Right", new Vector3(-24.25f, dividerCenterY, 7f), new Vector3(6.5f, 2.3f, 0.55f), secondFloorRoot, dividerColor);

            // 계단 구멍의 서쪽과 남쪽 가장자리에 낮은 난간을 두어 올라온 플레이어가 즉시 1층으로 떨어지지 않게 합니다.
            CreateWall("StairRailing_West", new Vector3(-21f, secondFloorY + 0.5f, 11f), new Vector3(0.18f, 1f, 10f), secondFloorRoot, new Color(0.28f, 0.30f, 0.31f));
            CreateWall("StairRailing_South", new Vector3(-18.5f, secondFloorY + 0.5f, 6f), new Vector3(5f, 1f, 0.18f), secondFloorRoot, new Color(0.28f, 0.30f, 0.31f));
            CreateWall("SecondFloorDesk_A", new Vector3(-32f, secondFloorY + 0.5f, -9f), new Vector3(4f, 1f, 1f), secondFloorRoot, new Color(0.31f, 0.23f, 0.15f));
            CreateWall("SecondFloorDesk_B", new Vector3(-28f, secondFloorY + 0.5f, 12f), new Vector3(4f, 1f, 1f), secondFloorRoot, new Color(0.31f, 0.23f, 0.15f));

            // 옥상은 별도 루트라 2층에 있는 플레이어를 가릴 때 2층 바닥까지 함께 사라지지 않습니다.
            var roofRootObject = new GameObject("Occluder_Roof");
            roofRootObject.transform.SetParent(buildingRoot, false);
            Transform roofRoot = roofRootObject.transform;
            CreateWall("ClassroomRoof", new Vector3(-27f, roofY, 2f), new Vector3(22.8f, 0.35f, 32.8f), roofRoot, new Color(0.22f, 0.24f, 0.25f));
            CreateWall("RoofParapet_West", new Vector3(-38.2f, roofY + 0.5f, 2f), new Vector3(0.4f, 1f, 32.8f), roofRoot, new Color(0.27f, 0.29f, 0.30f));
            CreateWall("RoofParapet_East", new Vector3(-15.8f, roofY + 0.5f, 2f), new Vector3(0.4f, 1f, 32.8f), roofRoot, new Color(0.27f, 0.29f, 0.30f));

            int occluderLayer = LayerMask.NameToLayer("PlayerOccluder");
            if (occluderLayer < 0)
            {
                Debug.LogError("2층 교실 생성 실패: PlayerOccluder 레이어가 없어 투과 대상을 지정할 수 없습니다.");
                return;
            }

            // Collider가 붙은 자식 조각까지 같은 레이어여야 SphereCast가 감지하므로 루트 아래 전체에 재귀적으로 적용합니다.
            SetLayerRecursively(secondFloorRootObject, occluderLayer);
            SetLayerRecursively(roofRootObject, occluderLayer);
            secondFloorRootObject.AddComponent<OccluderFadeTarget>();
            roofRootObject.AddComponent<OccluderFadeTarget>();
        }

        // Unity는 부모의 Layer를 바꿔도 기존 자식 Layer를 자동으로 바꾸지 않으므로 모든 Transform을 직접 순회합니다.
        // 이 함수 덕분에 나중에 지붕 조각을 더 추가해도 한 번의 호출로 카메라 검사 대상에 포함할 수 있습니다.
        private static void SetLayerRecursively(GameObject root, int layer)
        {
            root.layer = layer;
            for (int i = 0; i < root.transform.childCount; i++)
                SetLayerRecursively(root.transform.GetChild(i).gameObject, layer);
        }

        // 구역 바닥은 실제 Ground보다 아주 조금 위에 두고 Collider를 제거해 색상으로만 위치를 구분합니다.
        // 도로를 구역보다 낮게 배치하면 두 표시가 겹치는 곳에서도 깜빡임 없이 구역 색상이 위에 보입니다.
        // Collider가 없으므로 CharacterController 이동, 월드 아이템 상호작용과 총알 Raycast에는 영향을 주지 않습니다.
        private static void CreateZoneFloor(
            string name,
            Vector3 center,
            Vector2 size,
            Color color,
            Transform parent,
            float surfaceHeight = 0.012f)
        {
            GameObject marker = GameObject.CreatePrimitive(PrimitiveType.Cube);
            marker.name = name;
            marker.transform.SetParent(parent, true);
            float safeHeight = Mathf.Max(0.002f, surfaceHeight);
            marker.transform.position = new Vector3(center.x, safeHeight * 0.5f, center.z);
            marker.transform.localScale = new Vector3(size.x, safeHeight, size.y);
            Object.DestroyImmediate(marker.GetComponent<Collider>());
            SetColor(marker, color);
        }

        // 수평 벽 전체를 하나의 Cube로 만들면 문이 없어 실내에 들어갈 수 없으므로, 문 왼쪽과 오른쪽을 각각 별도 벽으로 만듭니다.
        // startX와 endX는 벽의 양 끝, doorCenterX는 문 중심, doorWidth는 실제로 비워 둘 통로 너비입니다.
        private static void CreateHorizontalWallWithDoor(
            string name,
            float z,
            float startX,
            float endX,
            float doorCenterX,
            float doorWidth,
            float height,
            Transform parent,
            Color color,
            float thickness = 0.8f)
        {
            float minX = Mathf.Min(startX, endX);
            float maxX = Mathf.Max(startX, endX);
            float safeDoorWidth = Mathf.Clamp(doorWidth, 0.5f, maxX - minX);
            float doorLeft = Mathf.Clamp(doorCenterX - safeDoorWidth * 0.5f, minX, maxX);
            float doorRight = Mathf.Clamp(doorCenterX + safeDoorWidth * 0.5f, minX, maxX);
            float leftLength = doorLeft - minX;
            float rightLength = maxX - doorRight;

            // 너무 짧은 조각은 Collider 모서리만 남겨 이동을 방해할 수 있으므로 10cm보다 긴 부분만 생성합니다.
            if (leftLength > 0.1f)
            {
                CreateWall(name + "_Left", new Vector3(minX + leftLength * 0.5f, height * 0.5f, z), new Vector3(leftLength, height, thickness), parent, color);
            }

            if (rightLength > 0.1f)
            {
                CreateWall(name + "_Right", new Vector3(doorRight + rightLength * 0.5f, height * 0.5f, z), new Vector3(rightLength, height, thickness), parent, color);
            }
        }

        // 세로 방향 벽도 문 아래쪽과 위쪽을 나눠 생성해 z축 방향 통로를 남깁니다.
        // 수평 벽 함수와 같은 규칙을 사용하므로 건물마다 문 폭을 바꾸더라도 벽 중심과 크기가 자동으로 계산됩니다.
        private static void CreateVerticalWallWithDoor(
            string name,
            float x,
            float startZ,
            float endZ,
            float doorCenterZ,
            float doorWidth,
            float height,
            Transform parent,
            Color color,
            float thickness = 0.8f)
        {
            float minZ = Mathf.Min(startZ, endZ);
            float maxZ = Mathf.Max(startZ, endZ);
            float safeDoorWidth = Mathf.Clamp(doorWidth, 0.5f, maxZ - minZ);
            float doorBottom = Mathf.Clamp(doorCenterZ - safeDoorWidth * 0.5f, minZ, maxZ);
            float doorTop = Mathf.Clamp(doorCenterZ + safeDoorWidth * 0.5f, minZ, maxZ);
            float bottomLength = doorBottom - minZ;
            float topLength = maxZ - doorTop;

            if (bottomLength > 0.1f)
            {
                CreateWall(name + "_Bottom", new Vector3(x, height * 0.5f, minZ + bottomLength * 0.5f), new Vector3(thickness, height, bottomLength), parent, color);
            }

            if (topLength > 0.1f)
            {
                CreateWall(name + "_Top", new Vector3(x, height * 0.5f, doorTop + topLength * 0.5f), new Vector3(thickness, height, topLength), parent, color);
            }
        }

        // Cube 프리미티브를 이용해 3D 충돌 가능한 벽이나 엄폐물을 만듭니다.
        // parent와 색상·회전값은 선택 사항이라 외곽 벽과 구역별 사물을 같은 생성 규칙으로 만들 수 있습니다.
        private static GameObject CreateWall(
            string name,
            Vector3 position,
            Vector3 scale,
            Transform parent = null,
            Color? color = null,
            float yRotation = 0f)
        {
            GameObject wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            wall.name = name;
            if (parent != null) wall.transform.SetParent(parent, true);
            wall.transform.position = position;
            wall.transform.rotation = Quaternion.Euler(0f, yRotation, 0f);
            wall.transform.localScale = scale;
            SetColor(wall, color ?? new Color(0.28f, 0.30f, 0.32f));
            return wall;
        }

        // 지정 위치에 좀비 프리팹 인스턴스를 배치합니다.
        // 학교의 초기 배치는 행동 유형으로 데이터 에셋을 찾아 체력·속도·감각·전리품을 모두 같은 규칙으로 적용합니다.
        private static void CreateArchetypeZombie(
            GameObject prefab,
            string name,
            Vector3 position,
            Scene scene,
            ZombieArchetype[] archetypes,
            ZombieBehaviorTrait requestedTrait)
        {
            ZombieArchetype selected = null;
            if (archetypes != null)
                foreach (ZombieArchetype archetype in archetypes)
                    if (archetype != null && archetype.BehaviorTrait == requestedTrait)
                    {
                        selected = archetype;
                        break;
                    }

            GameObject zombie = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
            zombie.name = name;
            zombie.transform.position = position;
            if (selected == null || !selected.ApplyTo(zombie))
            {
                Debug.LogError($"학교 초기 좀비 생성 실패: {requestedTrait} 유형 데이터나 필수 컴포넌트가 없습니다.");
                Object.DestroyImmediate(zombie);
                return;
            }

            // ScriptableObject가 바꾼 프리팹 인스턴스 필드를 Scene override로 기록해 저장 후 다시 열어도 종류별 수치가 유지되게 합니다.
            foreach (Component component in zombie.GetComponentsInChildren<Component>(true))
                if (component != null) PrefabUtility.RecordPrefabInstancePropertyModifications(component);
        }

        // 지정 위치에 좀비 프리팹 인스턴스를 배치합니다.
        private static void CreateZombie(
            GameObject prefab,
            string name,
            Vector3 position,
            Scene scene,
            params ZombieLootEntry[] lootEntries)
        {
            GameObject zombie = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
            zombie.name = name;
            zombie.transform.position = position;
            ZombieLootDrop lootDrop = zombie.GetComponent<ZombieLootDrop>();
            if (lootDrop != null)
            {
                lootDrop.Configure(lootEntries);
                PrefabUtility.RecordPrefabInstancePropertyModifications(lootDrop);
            }
        }

        // 현재 URP 프로젝트에 맞는 Lit Material을 즉석에서 적용합니다.
        // 단순한 Cube 외형과 Collider를 가진 월드 아이템을 씬에 배치하고 정의 데이터를 연결합니다.
        private static void CreateWorldItem(
            string name,
            Vector3 position,
            ItemDefinition definition,
            int quantity,
            Color color)
        {
            GameObject item = GameObject.CreatePrimitive(PrimitiveType.Cube);
            item.name = name;
            item.transform.position = position;
            item.transform.localScale = new Vector3(0.45f, 0.45f, 0.45f);
            SetColor(item, color);

            // 단일 루팅 아이템은 이동을 막는 장애물이 아니므로 보이는 Cube의 Collider를 상호작용 감지용 Trigger로 바꿉니다.
            // 실제 아이템 상자는 별도의 단단한 Collider를 사용해 통과와 Step Offset 올라타기를 막습니다.
            Collider pickupCollider = item.GetComponent<Collider>();
            if (pickupCollider != null) pickupCollider.isTrigger = true;

            WorldItem worldItem = item.AddComponent<WorldItem>();
            var serialized = new SerializedObject(worldItem);
            serialized.FindProperty("definition").objectReferenceValue = definition;
            serialized.FindProperty("quantity").intValue = quantity;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void CreateSchoolGroundLootPoints()
        {
            foreach (SchoolWorldItemPlacement placement in SchoolRaidContentLayout.WorldItems)
            {
                // 기존 고정 아이템 배치의 이름과 위치는 파밍 지점으로 재사용하되 실제 품목·수량은 학교 Ground 표가 결정합니다.
                // 빈 GameObject라 플레이어 충돌이나 보이지 않는 Collider를 만들지 않고, 재생 시작 시 성공한 지점에만 WorldItem이 생깁니다.
                var point = new GameObject(placement.ObjectName + "_LootPoint");
                point.transform.position = placement.Position;
                point.AddComponent<RegionGroundLootPoint>().Configure(
                    "school_floor",
                    SchoolRaidMapLayout.GetZoneDangerRating(placement.Position));
            }
        }

        // 단단한 Cube 상자에 외부 격자와 첫 수색 1회용 전리품 생성기를 연결합니다.
        // 생성된 상자는 Scene에 명시적으로 이동시키고 색을 적용해 씬을 다시 저장했을 때 참조와 외형이 유지되게 합니다.
        private static void CreateFieldLootBox(
            Scene scene,
            string boxName,
            Vector3 position,
            int gridWidth,
            int gridHeight,
            LootTableDefinition lootTable,
            Color color,
            int dangerRating = 1)
        {
            string displayName = lootTable != null ? lootTable.DisplayName : boxName;
            WorldLootContainer container = WorldLootContainer.SpawnLootBox(
                displayName,
                position,
                gridWidth,
                gridHeight);
            // Hierarchy 이름은 자동 생성 여부를 검사할 고정 ID로 유지하고, 화면 안내에는 전리품 표의 한글 이름을 사용합니다.
            container.gameObject.name = "LootBox_" + boxName;
            SceneManager.MoveGameObjectToScene(container.gameObject, scene);
            SetColor(container.gameObject, color);

            FieldLootSource source = container.gameObject.AddComponent<FieldLootSource>();
            source.Configure(lootTable, false, dangerRating);
        }

        // 학교의 역할이 다른 방마다 전리품 종류를 나눠 플레이어가 한 동선만 반복하지 않게 합니다.
        // 상자 이름에는 구역을 넣어 Hierarchy와 플레이 중 상호작용 문구에서 위치를 바로 구분할 수 있게 합니다.
        private static void CreateSchoolLootContainers(Scene scene, LootTableDefinition[] lootTables)
        {
            if (lootTables == null || lootTables.Length < 3)
            {
                Debug.LogError("학교 전리품 상자 생성 실패: 보급·무기·은닉물 LootTable 세 개가 필요합니다.");
                return;
            }

            foreach (SchoolLootBoxPlacement placement in SchoolRaidContentLayout.LootBoxes)
            {
                int tableIndex = Mathf.Clamp(placement.LootTableIndex, 0, lootTables.Length - 1);
                CreateFieldLootBox(
                    scene,
                    placement.ObjectName,
                    placement.Position,
                    placement.GridWidth,
                    placement.GridHeight,
                    lootTables[tableIndex],
                    placement.Color,
                    SchoolRaidMapLayout.GetZoneDangerRating(placement.Position));
            }
        }

        // BoxCollider Trigger가 플레이어 진입과 이탈을 감지하고 ExtractionController가 조건과 시간을 계산합니다.
        // Visual 원통은 Collider를 제거해 오직 구역 위치를 보여 주는 역할만 하도록 구성합니다.
        private static ExtractionController CreateExtractionPoint(ExtractionRequirement requirement, Vector3 position)
        {
            var root = new GameObject("ExtractionPoint");
            // 위치는 RaidMapLayout에서 받아 플레이어 시작점·스폰 지점과 같은 맵 기준을 공유합니다.
            root.transform.position = position;
            BoxCollider trigger = root.AddComponent<BoxCollider>();
            trigger.isTrigger = true;
            trigger.size = new Vector3(4f, 2f, 3f);

            ExtractionController controller = root.AddComponent<ExtractionController>();
            var serialized = new SerializedObject(controller);
            serialized.FindProperty("requirement").objectReferenceValue = requirement;
            serialized.FindProperty("countdownDuration").floatValue = 3f;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            GameObject visual = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            visual.name = "Visual";
            visual.transform.SetParent(root.transform, false);
            visual.transform.localPosition = new Vector3(0f, 0.03f, 0f);
            visual.transform.localScale = new Vector3(2f, 0.03f, 1.5f);
            Object.DestroyImmediate(visual.GetComponent<Collider>());
            SetColor(visual, new Color(0.1f, 0.8f, 0.3f));
            serialized.Update();
            serialized.FindProperty("visualRenderer").objectReferenceValue = visual.GetComponent<Renderer>();
            serialized.ApplyModifiedPropertiesWithoutUndo();
            return controller;
        }

        private static void SetColor(GameObject target, Color color)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            var material = new Material(shader) { color = color };
            target.GetComponent<Renderer>().sharedMaterial = material;
        }

        // 게임 흐름 순서대로 Title, Loading, Hideout과 현재 실제 테스트 지역인 학교를 Build Settings에 등록합니다.
        // RaidPrototype 씬 파일은 참고용으로 남아 있어도 빌드에는 포함되지 않으므로 용량과 중복 테스트 시간을 차지하지 않습니다.
        private static void SetBuildScenes()
        {
            EditorBuildSettings.scenes = new[] {
                new EditorBuildSettingsScene(Root + "/Scenes/Title.unity", true),
                new EditorBuildSettingsScene(Root + "/Scenes/Loading.unity", true),
                new EditorBuildSettingsScene(Root + "/Scenes/Hideout.unity", true),
                new EditorBuildSettingsScene(Root + "/Scenes/RaidSchool.unity", true)
            };
        }
    }
}
#endif
