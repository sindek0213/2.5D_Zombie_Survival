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
            ItemDefinition[] itemDefinitions = CreateItemDefinitions(weaponDefinitions);
            ItemCatalog itemCatalog = CreateItemCatalog(itemDefinitions);
            LootTableDefinition[] lootTables = CreateLootTables(itemDefinitions);
            ExtractionRequirement extractionRequirement = CreateExtractionRequirement();
            RaidRules raidRules = CreateRaidRules();
            HideoutFacilityDefinition[] hideoutFacilities = CreateHideoutFacilityDefinitions(itemCatalog);
            HideoutMissionDefinition[] hideoutMissions = CreateHideoutMissionDefinitions(itemCatalog);
            HideoutRecipeDefinition[] hideoutRecipes = CreateHideoutRecipeDefinitions(itemCatalog);
            GameObject prefab = CreatePlayerPrefab(sprite);
            GameObject zombiePrefab = CreateZombiePrefab(sprite);
            ZombieArchetype[] zombieArchetypes = CreateZombieArchetypes(zombiePrefab, itemDefinitions);
            GameObject inventoryCanvasPrefab = CreateInventoryUiPrefabs();
            GameObject raidHudCanvasPrefab = CreateRaidHudCanvasPrefab();
            GameObject bootstrapCanvasPrefab = CreateBootstrapCanvasPrefab();
            GameObject hideoutCanvasPrefab = CreateHideoutCanvasPrefab();
            GameObject hideoutMissionCanvasPrefab = CreateHideoutMissionCanvasPrefab();
            GameObject hideoutFacilityUpgradeCanvasPrefab = CreateHideoutFacilityUpgradeCanvasPrefab();
            GameObject hideoutWorldCanvasPrefab = CreateHideoutWorldCanvasPrefab();
            CreateSimpleScene("Bootstrap", null, bootstrapCanvasPrefab);
            CreateHideoutScene(
                prefab,
                itemCatalog,
                hideoutCanvasPrefab,
                hideoutMissionCanvasPrefab,
                hideoutFacilityUpgradeCanvasPrefab,
                hideoutWorldCanvasPrefab,
                hideoutFacilities,
                hideoutMissions,
                hideoutRecipes);
            CreateRaidScene(prefab, zombiePrefab, inventoryCanvasPrefab, raidHudCanvasPrefab, itemDefinitions, itemCatalog, extractionRequirement, raidRules, zombieArchetypes, lootTables);
            SetBuildScenes();

            // 전체 프로토타입 재생성은 Player와 UI 프리팹을 새로 저장하므로 생성 원화 연결이 빠질 수 있습니다.
            // 프로젝트 루트에 ArtRemake가 있을 때만 마지막에 그림 적용 도구를 다시 실행해 데이터·씬 생성 후에도 최신 원화를 유지합니다.
            string generatedArtSource = Path.GetFullPath(Path.Combine(Application.dataPath, "../ArtRemake"));
            if (Directory.Exists(generatedArtSource)) GeneratedArtApplicator.Apply();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("2.5D prototype content rebuilt successfully.");
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

            // 새 재료 아이템까지 포함한 ItemDefinition과 ItemCatalog를 먼저 갱신해야 시설 비용과 제작법의 참조가 null이 되지 않습니다.
            // 이 과정은 데이터 에셋만 수정하며 CreatePlayerSprite를 호출하지 않으므로 현재 프로젝트의 이미지 리소스에는 손대지 않습니다.
            ItemDefinition[] itemDefinitions = CreateItemDefinitions(weaponDefinitions);
            ItemCatalog itemCatalog = CreateItemCatalog(itemDefinitions);

            // 기존 레이드 씬 자체는 재생성하지 않고, 씬이 이미 참조 중인 드롭 테이블과 좀비 유형 에셋의 내용만 최신 상태로 갱신합니다.
            // 따라서 재료가 상자와 좀비 시체에서 나오도록 연결하면서 사용자가 씬에서 직접 조정한 오브젝트 배치는 보존합니다.
            CreateLootTables(itemDefinitions);
            CreateZombieArchetypes(zombiePrefab, itemDefinitions);

            HideoutFacilityDefinition[] facilities = CreateHideoutFacilityDefinitions(itemCatalog);
            HideoutMissionDefinition[] missions = CreateHideoutMissionDefinitions(itemCatalog);
            HideoutRecipeDefinition[] recipes = CreateHideoutRecipeDefinitions(itemCatalog);
            GameObject storageCanvas = CreateHideoutCanvasPrefab();
            GameObject missionCanvas = CreateHideoutMissionCanvasPrefab();
            GameObject upgradeCanvas = CreateHideoutFacilityUpgradeCanvasPrefab();
            GameObject worldCanvas = CreateHideoutWorldCanvasPrefab();
            CreateHideoutScene(playerPrefab, itemCatalog, storageCanvas, missionCanvas, upgradeCanvas, worldCanvas, facilities, missions, recipes);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("Hideout world content rebuilt successfully.");
        }

        // 지시문에 정의된 프로젝트 폴더 구조를 AssetDatabase를 통해 생성합니다.
        private static void EnsureFolders()
        {
            string[] paths = {
                "Art/Materials", "Art/Sprites/Player", "Art/Sprites/Zombies", "Art/Sprites/Items", "Art/Sprites/UI", "Art/Models",
                "Audio/Music", "Audio/SFX", "Data/Items", "Data/Weapons", "Data/Zombies", "Data/Loot", "Data/Extraction", "Data/Raid", "Data/Hideout",
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
                CreateWeaponDefinition("rifle", "낡은 소총", WeaponTriggerMode.Automatic, 28f, 38f, 0.14f, 1.7f, 20, AmmoType.FiveFiveSix, accuracySpreadAngle: 1.6f, knockbackForce: 1.1f),
                // 기본 지급 권총은 일반 좀비를 약 세 발에 처치하며 작은 탄창 대신 좋은 정확도로 초반 생존을 담당합니다.
                CreateWeaponDefinition("pistol", "권총", WeaponTriggerMode.SemiAutomatic, 32f, 30f, 0.26f, 1.2f, 10, AmmoType.NineMillimeter, accuracySpreadAngle: 0.75f, knockbackForce: 1.6f),
                // 기관단총은 피해량과 사거리를 낮추는 대신 빠른 자동 사격과 큰 탄창으로 근거리 다수전을 담당합니다.
                CreateWeaponDefinition("smg", "9mm 기관단총", WeaponTriggerMode.Automatic, 17f, 22f, 0.085f, 1.5f, 30, AmmoType.NineMillimeter, accuracySpreadAngle: 3.2f, knockbackForce: 0.65f),
                // 산탄총은 한 번에 쉘 한 발만 소비하지만 16도 안에 피해 14인 펠릿 일곱 개를 발사합니다.
                CreateWeaponDefinition("shotgun", "펌프 산탄총", WeaponTriggerMode.SemiAutomatic, 14f, 14f, 0.85f, 2.3f, 5, AmmoType.TwelveGauge, 7, 16f, 1.5f, 0.35f)
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
            float knockbackForce = 1f)
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
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(definition);
            return definition;
        }

        // 레이드에 배치할 모든 ItemDefinition을 고정 배열 순서로 생성합니다.
        // CreateRaidScene이 배열 인덱스로 아이템을 선택하므로 여기의 순서를 바꾸면 월드 배치 인덱스도 함께 수정해야 합니다.
        // ItemId는 저장용 고유 키이고 표시 이름과 설명은 UI에서 사용자에게 보여 줄 값입니다.
        private static ItemDefinition[] CreateItemDefinitions(WeaponDefinition[] weaponDefinitions)
        {
            return new[]
            {
                CreateItemDefinition("ammo_9mm", "9mm 탄약", ItemType.Ammo, 30, 1, 1, false, "9mm 권총용 탄약입니다.", ammoType: AmmoType.NineMillimeter, baseValue: 2),
                CreateItemDefinition("medkit_small", "소형 구급상자", ItemType.Healing, 3, 2, 1, true, "사용하면 체력을 35 회복하는 의료품입니다.", healthRestoreAmount: 35f, rarity: ItemRarity.Uncommon, baseValue: 80),
                CreateItemDefinition("food_canned", "통조림", ItemType.Food, 5, 1, 2, true, "사용하면 포만감을 50 회복하는 보존 식량입니다.", satietyRestoreAmount: 50f, baseValue: 25),
                CreateItemDefinition("quest_radio", "고장 난 무전기", ItemType.Quest, 1, 2, 2, false, "탈출 조건용 퀘스트 아이템입니다.", rarity: ItemRarity.Quest, baseValue: 250),
                CreateItemDefinition("weapon_rifle", "낡은 소총", ItemType.Weapon, 1, 5, 2, false, "자동 사격이 가능한 희귀 주무기입니다.", EquipmentSlotType.PrimaryWeapon, weaponDefinition: weaponDefinitions[0], rarity: ItemRarity.Rare, baseValue: 800),
                CreateItemDefinition("weapon_pistol", "권총", ItemType.Weapon, 1, 2, 1, false, "반자동 보조무기입니다.", EquipmentSlotType.SecondaryWeapon, weaponDefinition: weaponDefinitions[1], rarity: ItemRarity.Uncommon, baseValue: 300),
                CreateItemDefinition("armor_light", "경량 방탄복", ItemType.Armor, 1, 3, 3, false, "피해의 40%를 내구도로 흡수하는 희귀 방탄복입니다.", EquipmentSlotType.Armor, armorDamageReduction: 0.4f, armorMaxDurability: 100f, rarity: ItemRarity.Rare, baseValue: 600),
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
                CreateItemDefinition("material_cloth", "천 조각", ItemType.Material, 20, 1, 1, false, "의료품 제작과 치료소 강화에 사용하는 재료입니다.", baseValue: 10)
            };
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
            AmmoType ammoType = AmmoType.None,
            float armorDamageReduction = 0f,
            float armorMaxDurability = 0f,
            float healthRestoreAmount = 0f,
            float satietyRestoreAmount = 0f,
            ItemRarity rarity = ItemRarity.Common,
            int baseValue = 10)
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
            serialized.FindProperty("armorDamageReduction").floatValue = armorDamageReduction;
            serialized.FindProperty("armorMaxDurability").floatValue = armorMaxDurability;
            serialized.FindProperty("containerWidth").intValue = containerWidth;
            serialized.FindProperty("containerHeight").intValue = containerHeight;
            // 의료품과 음식만 각자 사용하는 회복량이며, 다른 아이템은 기본값 0으로 기록됩니다.
            serialized.FindProperty("healthRestoreAmount").floatValue = healthRestoreAmount;
            serialized.FindProperty("satietyRestoreAmount").floatValue = satietyRestoreAmount;
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
                    UpgradeStep(3, Cost(scrap, 10), Cost(electronics, 6)))
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

        // 처치·아이템 회수·성공 탈출의 세 목표 종류와 일회성·반복 가능 규칙을 확인할 기본 임무를 만듭니다.
        // 보상은 문자열이 아니라 ItemDefinition과 수량으로 연결하므로 완료할 때 실제 창고에 같은 아이템이 들어갑니다.
        private static HideoutMissionDefinition[] CreateHideoutMissionDefinitions(ItemCatalog itemCatalog)
        {
            ItemDefinition medkit = itemCatalog?.Find("medkit_small");
            ItemDefinition food = itemCatalog?.Find("food_canned");
            ItemDefinition ammo9mm = itemCatalog?.Find("ammo_9mm");
            ItemDefinition ammo556 = itemCatalog?.Find("ammo_556");

            return new[]
            {
                CreateHideoutMissionDefinition(
                    "first_sweep",
                    "주변 정리",
                    "거점 주변의 위험을 낮추기 위해 레이드에서 좀비 3마리를 처치하세요.",
                    HideoutMissionObjectiveType.ZombieKills,
                    null,
                    3,
                    false,
                    new HideoutMissionItemReward(medkit, 1)),
                CreateHideoutMissionDefinition(
                    "supply_recovery",
                    "식량 회수",
                    "레이드에서 통조림 2개를 가지고 탈출하세요. 성공한 레이드의 회수품만 진행도에 포함됩니다.",
                    HideoutMissionObjectiveType.CollectedItem,
                    food,
                    2,
                    true,
                    new HideoutMissionItemReward(ammo9mm, 20)),
                CreateHideoutMissionDefinition(
                    "safe_return",
                    "무사 귀환",
                    "레이드에서 한 번 성공적으로 탈출하세요.",
                    HideoutMissionObjectiveType.SuccessfulExtraction,
                    null,
                    1,
                    true,
                    new HideoutMissionItemReward(medkit, 1),
                    new HideoutMissionItemReward(ammo9mm, 10)),
                CreateHideoutMissionDefinition(
                    "deep_sweep",
                    "대대적인 소탕",
                    "여러 차례 출격해 좀비 8마리를 처치하세요. 처치 수는 레이드 사이에도 누적됩니다.",
                    HideoutMissionObjectiveType.ZombieKills,
                    null,
                    8,
                    true,
                    new HideoutMissionItemReward(ammo556, 20))
            };
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
            player.AddComponent<PlayerInventory>();
            // 포만감은 플레이어마다 한 개만 존재하며 PlayerMovement와 음식 사용 코드가 같은 상태를 읽습니다.
            player.AddComponent<PlayerSurvival>();
            // Health가 실제 체력을 줄이기 전에 장착 방탄복의 피해 감소와 내구도를 적용합니다.
            player.AddComponent<ArmorProtection>();
            player.AddComponent<PlayerInteraction>();
            // 인벤토리와 레이드 종료가 같은 조작 컴포넌트를 안전하게 잠그도록 공용 Gate를 플레이어 루트에 둡니다.
            player.AddComponent<PlayerControlGate>();
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

        // 하나의 공용 좀비 프리팹에 서로 다른 수치와 전리품을 적용하는 세 종류의 데이터 에셋을 만듭니다.
        // 배열 순서는 0 일반형, 1 빠른형, 2 튼튼한형이며 Raid 씬 초기 배치와 스폰 시스템이 같은 순서를 사용합니다.
        private static ZombieArchetype[] CreateZombieArchetypes(
            GameObject zombiePrefab,
            ItemDefinition[] itemDefinitions)
        {
            return new[]
            {
                CreateZombieArchetype(
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
                    new ZombieLootEntry { item = itemDefinitions[2], dropChance = 0.18f, minimumQuantity = 1, maximumQuantity = 1 }),
                CreateZombieArchetype(
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
                    new ZombieLootEntry { item = itemDefinitions[1], dropChance = 0.15f, minimumQuantity = 1, maximumQuantity = 1 }),
                CreateZombieArchetype(
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
                    new ZombieLootEntry { item = itemDefinitions[6], dropChance = 0.08f, minimumQuantity = 1, maximumQuantity = 1 })
            };
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
                "Tab 닫기  |  좌클릭 선택/드래그  |  Ctrl+좌클릭 빠른 이동  |  Shift+좌클릭 절반 분할  |  R 회전  |  우클릭 사용/장착  |  Delete 두 번 버리기",
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

            // 화면 아래쪽에는 선택 아이템 설명, 탄창·내구도와 장착 무기 비교 결과를 표시합니다.
            GameObject detailPanel = CreateUiObject("ItemDetailPanel", panel.transform);
            SetTopLeft((RectTransform)detailPanel.transform, new Vector2(285f, -500f), new Vector2(1135f, 285f));
            Image detailBackground = detailPanel.AddComponent<Image>();
            detailBackground.color = new Color(0.045f, 0.06f, 0.07f, 0.98f);
            detailBackground.raycastTarget = false;
            Text detailText = CreateUiText("ItemDetailText", detailPanel.transform, "아이템을 선택하면 상세 정보가 표시됩니다.", 17, TextAnchor.UpperLeft);
            Stretch(detailText.rectTransform, new Vector2(18f, 14f), new Vector2(-18f, -14f));
            detailText.horizontalOverflow = HorizontalWrapMode.Wrap;
            detailText.verticalOverflow = VerticalWrapMode.Truncate;
            detailText.raycastTarget = false;
            dragLayer.SetAsLastSibling();

            hud.ConfigureViews(panel, dragLayer, rootGrid, containerGridObject, containerGrid, slotViews, promptText, statusText, detailText);
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
                if (existingHealth != null && existingHealth.sizeDelta.x >= 800f) return existing;
                return UpgradeRaidHudForSurvival(path);
            }

            GameObject root = CreateCanvasRoot("RaidHudCanvas", 0);
            AmmoHud ammoHud = root.AddComponent<AmmoHud>();
            ExtractionHud extractionHud = root.AddComponent<ExtractionHud>();
            PlayerDamageHud damageHud = root.AddComponent<PlayerDamageHud>();
            RaidResultHud resultHud = root.AddComponent<RaidResultHud>();

            // 화면 왼쪽 위에는 플레이 중 자주 확인하는 기본 조작법을 작게 고정합니다.
            Text guideText = CreateUiText(
                "ControlGuide",
                root.transform,
                "[현재 지역]  WASD 이동  |  Shift 달리기  |  좌클릭 발사  |  R 재장전  |  1·2 무기  |  Tab 인벤토리",
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
            return saved;
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

        // 저장 불러오기 결과와 거점 입장 버튼을 중앙 카드 형태로 배치한 시작 화면 프리팹을 만듭니다.
        private static GameObject CreateBootstrapCanvasPrefab()
        {
            const string path = Root + "/Prefabs/UI/BootstrapCanvas.prefab";
            GameObject existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (existing != null) return existing;

            GameObject root = CreateCanvasRoot("BootstrapCanvas", 0);
            BootstrapHud hud = root.AddComponent<BootstrapHud>();
            CreateFullScreenBackground(root.transform, new Color(0.02f, 0.028f, 0.035f, 0.96f));

            GameObject panel = CreateUiObject("StartPanel", root.transform);
            SetCentered((RectTransform)panel.transform, Vector2.zero, new Vector2(660f, 360f));
            Image panelImage = panel.AddComponent<Image>();
            panelImage.color = new Color(0.08f, 0.105f, 0.115f, 0.98f);

            Text title = CreateUiText("Title", panel.transform, "2.5D ZOMBIE SURVIVAL", 34, TextAnchor.MiddleCenter);
            SetTopCenter(title.rectTransform, new Vector2(0f, -42f), new Vector2(590f, 56f));
            title.raycastTarget = false;
            Text status = CreateUiText("Status", panel.transform, "저장 데이터를 확인하는 중입니다...", 20, TextAnchor.MiddleCenter);
            SetCentered(status.rectTransform, new Vector2(0f, 12f), new Vector2(570f, 110f));
            status.raycastTarget = false;
            Text enterLabel;
            Button enterButton = CreateUiButton("EnterHideoutButton", panel.transform, "거점으로 들어가기", out enterLabel);
            SetBottomCenter((RectTransform)enterButton.transform, new Vector2(0f, 42f), new Vector2(280f, 58f));

            hud.ConfigureViews(status, enterButton);
            GameObject saved = PrefabUtility.SaveAsPrefabAsset(root, path);
            Object.DestroyImmediate(root);
            return saved;
        }

        // 창고와 다음 출격품을 좌우 격자로 나누고 상세 정보, 자동 정리와 출격 버튼을 둔 거점 화면을 만듭니다.
        private static GameObject CreateHideoutCanvasPrefab()
        {
            const string path = Root + "/Prefabs/UI/HideoutCanvas.prefab";
            GameObject existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (existing != null && existing.transform.Find("StashInventoryGrid") != null) return existing;

            InventoryGridView gridPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                Root + "/Prefabs/UI/InventoryGrid.prefab")?.GetComponent<InventoryGridView>();
            GameObject root = CreateCanvasRoot("HideoutCanvas", 0);
            HideoutResultHud hud = root.AddComponent<HideoutResultHud>();
            CreateFullScreenBackground(root.transform, new Color(0.025f, 0.035f, 0.04f, 0.98f));

            Text title = CreateUiText("Title", root.transform, "HIDEOUT · 출격 준비", 32, TextAnchor.MiddleCenter);
            SetTopCenter(title.rectTransform, new Vector2(0f, -20f), new Vector2(720f, 52f));
            title.raycastTarget = false;
            Text summary = CreateUiText("ResultSummary", root.transform, "창고에서 출격 아이템을 선택하세요.", 18, TextAnchor.MiddleCenter);
            SetTopCenter(summary.rectTransform, new Vector2(0f, -72f), new Vector2(1300f, 58f));
            summary.raycastTarget = false;

            Text stashTitle = CreateUiText("StashTitle", root.transform, "창고", 22, TextAnchor.MiddleLeft);
            SetTopLeft(stashTitle.rectTransform, new Vector2(48f, -128f), new Vector2(700f, 38f));
            stashTitle.raycastTarget = false;
            Text loadoutTitle = CreateUiText("LoadoutTitle", root.transform, "출격 인벤토리", 22, TextAnchor.MiddleLeft);
            SetTopLeft(loadoutTitle.rectTransform, new Vector2(852f, -128f), new Vector2(650f, 38f));
            loadoutTitle.raycastTarget = false;

            GameObject stashGridObject = (GameObject)PrefabUtility.InstantiatePrefab(gridPrefab.gameObject);
            stashGridObject.name = "StashInventoryGrid";
            stashGridObject.transform.SetParent(root.transform, false);
            SetTopLeft((RectTransform)stashGridObject.transform, new Vector2(48f, -168f), new Vector2(700f, 570f));
            PrefabUtility.RecordPrefabInstancePropertyModifications(stashGridObject.transform);
            InventoryGridView stashGrid = stashGridObject.GetComponent<InventoryGridView>();

            GameObject loadoutGridObject = (GameObject)PrefabUtility.InstantiatePrefab(gridPrefab.gameObject);
            loadoutGridObject.name = "LoadoutInventoryGrid";
            loadoutGridObject.transform.SetParent(root.transform, false);
            SetTopLeft((RectTransform)loadoutGridObject.transform, new Vector2(852f, -168f), new Vector2(650f, 370f));
            PrefabUtility.RecordPrefabInstancePropertyModifications(loadoutGridObject.transform);
            InventoryGridView loadoutGrid = loadoutGridObject.GetComponent<InventoryGridView>();

            GameObject detailPanel = CreateUiObject("ItemDetailPanel", root.transform);
            SetTopLeft((RectTransform)detailPanel.transform, new Vector2(852f, -545f), new Vector2(650f, 190f));
            Image detailBackground = detailPanel.AddComponent<Image>();
            detailBackground.color = new Color(0.05f, 0.07f, 0.08f, 0.98f);
            detailBackground.raycastTarget = false;
            Text detailText = CreateUiText("ItemDetailText", detailPanel.transform, "아이템을 선택하면 상세 정보가 표시됩니다.", 16, TextAnchor.UpperLeft);
            Stretch(detailText.rectTransform, new Vector2(16f, 12f), new Vector2(-16f, -12f));
            detailText.horizontalOverflow = HorizontalWrapMode.Wrap;
            detailText.verticalOverflow = VerticalWrapMode.Truncate;
            detailText.raycastTarget = false;

            Text operationStatus = CreateUiText("OperationStatus", root.transform, "좌클릭 선택/드래그 · Ctrl+좌클릭 빠른 이동 · Shift+좌클릭 절반 분할 · R 회전", 17, TextAnchor.MiddleCenter);
            SetBottomCenter(operationStatus.rectTransform, new Vector2(0f, 98f), new Vector2(1000f, 38f));
            operationStatus.raycastTarget = false;

            Text launchLabel;
            Button launchButton = CreateUiButton("LaunchRaidButton", root.transform, "빈손으로 레이드 출격", out launchLabel);
            SetBottomCenter((RectTransform)launchButton.transform, new Vector2(0f, 24f), new Vector2(380f, 62f));

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
                operationStatus,
                launchButton,
                launchLabel);

            GameObject saved = PrefabUtility.SaveAsPrefabAsset(root, path);
            Object.DestroyImmediate(root);
            return saved;
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

        // 거점 월드를 가리지 않는 제목과 상호작용 안내만 가진 작은 Canvas 프리팹입니다.
        private static GameObject CreateHideoutWorldCanvasPrefab()
        {
            const string path = Root + "/Prefabs/UI/HideoutWorldCanvas.prefab";
            GameObject existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (existing != null) return existing;

            GameObject root = CreateCanvasRoot("HideoutWorldCanvas", 5);
            HideoutWorldHud hud = root.AddComponent<HideoutWorldHud>();

            Text title = CreateUiText("HideoutTitle", root.transform, "거점", 30, TextAnchor.MiddleCenter);
            SetTopCenter(title.rectTransform, new Vector2(0f, -20f), new Vector2(500f, 52f));
            title.raycastTarget = false;

            GameObject promptPanel = CreateUiObject("PromptPanel", root.transform);
            SetBottomCenter((RectTransform)promptPanel.transform, new Vector2(0f, 28f), new Vector2(760f, 58f));
            Image promptBackground = promptPanel.AddComponent<Image>();
            promptBackground.color = new Color(0.02f, 0.03f, 0.035f, 0.86f);
            promptBackground.raycastTarget = false;
            Text prompt = CreateUiText("Prompt", promptPanel.transform, "WASD 이동 · 시설 가까이에서 E 사용", 20, TextAnchor.MiddleCenter);
            Stretch(prompt.rectTransform, new Vector2(12f, 6f), new Vector2(-12f, -6f));
            prompt.raycastTarget = false;

            hud.Configure(null, null, title, prompt);
            GameObject saved = PrefabUtility.SaveAsPrefabAsset(root, path);
            Object.DestroyImmediate(root);
            return saved;
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
            GameObject playerPrefab,
            GameObject zombiePrefab,
            GameObject inventoryCanvasPrefab,
            GameObject raidHudCanvasPrefab,
            ItemDefinition[] itemDefinitions,
            ItemCatalog itemCatalog,
            ExtractionRequirement extractionRequirement,
            RaidRules raidRules,
            ZombieArchetype[] zombieArchetypes,
            LootTableDefinition[] lootTables)
        {
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            scene.name = "RaidPrototype";
            CreateLighting();

            // 바닥, 외곽 벽, 도로와 다섯 파밍 구역을 별도 메서드에서 만들어 이 메서드는 게임 시스템 연결 흐름에 집중합니다.
            CreateRaidMapGeometry();

            GameObject player = (GameObject)PrefabUtility.InstantiatePrefab(playerPrefab, scene);
            player.transform.position = RaidMapLayout.PlayerStartPosition;

            // 서로 다른 위치에 좀비를 배치해 감지, 추적, 공격과 사망을 확인합니다.
            // 초기 세 마리는 전리품 종류를 빠르게 시험할 수 있게 서로 다른 후보를 갖되, 확정 드롭은 없애 반복 출격의 과도한 보상을 막습니다.
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

            // 맵 곳곳에 서로 다른 종류와 수량의 루팅 아이템을 배치합니다.
            CreateWorldItem("AmmoPickup", new Vector3(-2f, 0.3f, -12.5f), itemDefinitions[0], 12, new Color(0.85f, 0.72f, 0.2f));
            CreateWorldItem("MedkitPickup", new Vector3(-10.5f, 0.3f, -3.5f), itemDefinitions[1], 1, new Color(0.85f, 0.2f, 0.2f));
            CreateWorldItem("FoodPickup", new Vector3(-4f, 0.3f, 2f), itemDefinitions[2], 2, new Color(0.85f, 0.48f, 0.18f));
            CreateWorldItem("QuestRadioPickup", new Vector3(-13f, 0.3f, 7f), itemDefinitions[3], 1, new Color(0.25f, 0.65f, 1f));
            CreateWorldItem("RiflePickup", new Vector3(9f, 0.3f, 7f), itemDefinitions[4], 1, new Color(0.3f, 0.3f, 0.3f));
            CreateWorldItem("PistolPickup", new Vector3(4f, 0.3f, -13f), itemDefinitions[5], 1, new Color(0.25f, 0.25f, 0.25f));
            CreateWorldItem("ArmorPickup", new Vector3(-12f, 0.3f, 5.5f), itemDefinitions[6], 1, new Color(0.25f, 0.45f, 0.55f));
            CreateWorldItem("RigPickup", new Vector3(14f, 0.3f, 7f), itemDefinitions[7], 1, new Color(0.35f, 0.5f, 0.3f));
            CreateWorldItem("BackpackPickup", new Vector3(3.5f, 0.3f, 4.5f), itemDefinitions[8], 1, new Color(0.3f, 0.4f, 0.3f));
            // 소총은 9mm 탄약으로 재장전할 수 없으므로 별도의 5.56mm 탄약 묶음을 맵에 배치합니다.
            CreateWorldItem("RifleAmmoPickup", new Vector3(9f, 0.3f, 9f), itemDefinitions[9], 30, new Color(0.72f, 0.58f, 0.18f));
            // 새 무기와 전용 탄약을 멀리 떨어뜨려 놓아 한 자리에서 모두 얻지 않고 맵을 탐색하게 합니다.
            CreateWorldItem("ShotgunAmmoPickup", new Vector3(11f, 0.3f, -6f), itemDefinitions[10], 12, new Color(0.72f, 0.38f, 0.16f));
            CreateWorldItem("SmgPickup", new Vector3(-13f, 0.3f, -8f), itemDefinitions[11], 1, new Color(0.18f, 0.24f, 0.28f));
            CreateWorldItem("ShotgunPickup", new Vector3(14f, 0.3f, -5f), itemDefinitions[12], 1, new Color(0.35f, 0.2f, 0.16f));

            // 직접 줍는 바닥 아이템과 별도로, 첫 수색 때 확률표를 굴리는 세 종류의 단단한 필드 상자를 배치합니다.
            // 상자는 서로 다른 격자 크기와 색을 사용해 생활 보급·무기·은닉물 용도를 플레이 중에도 구분할 수 있습니다.
            CreateFieldLootBox(
                scene,
                "FieldLoot_Supply",
                new Vector3(-12f, 0f, -2f),
                6,
                4,
                lootTables[0],
                new Color(0.35f, 0.5f, 0.28f));
            CreateFieldLootBox(
                scene,
                "FieldLoot_Weapon",
                new Vector3(12f, 0f, 4f),
                8,
                5,
                lootTables[1],
                new Color(0.28f, 0.32f, 0.38f));
            CreateFieldLootBox(
                scene,
                "FieldLoot_HiddenStash",
                new Vector3(0f, 0f, 8f),
                6,
                4,
                lootTables[2],
                new Color(0.42f, 0.34f, 0.2f));

            // 북쪽 벽 안쪽에 반경이 넉넉한 탈출 Trigger와 눈에 보이는 녹색 원통을 배치합니다.
            // Trigger 루트와 Visual을 분리해 시각용 Mesh Collider가 진입 판정에 중복 참여하지 않게 합니다.
            ExtractionController extraction = CreateExtractionPoint(extractionRequirement, RaidMapLayout.ExtractionPosition);

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

            // 코드로 즉석 사각형을 그리던 기존 HUD 대신 수정 가능한 Canvas 프리팹을 씬에 배치합니다.
            // EventSystem은 Image와 Button이 마우스 클릭/드래그 이벤트를 받을 수 있게 해 주며 씬에는 하나만 둡니다.
            if (inventoryCanvasPrefab != null)
            {
                GameObject inventoryCanvas = (GameObject)PrefabUtility.InstantiatePrefab(inventoryCanvasPrefab, scene);
                inventoryCanvas.GetComponent<InventoryHud>().Configure(
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

            // 맵 가장자리의 표시 지점 중 플레이어와 충분히 멀고 막히지 않은 곳에서 새 좀비가 나타나게 합니다.
            // 시작 시 3마리에서 후반 7마리까지 늘어나며 생성 간격은 9초에서 4초까지 점차 짧아집니다.
            CreateZombieSpawner(scene, zombieArchetypes, player.transform, raidSession);

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
            }

            EditorSceneManager.SaveScene(scene, Root + "/Scenes/RaidPrototype.unity");
        }

        // ZombieSpawner와 여덟 개의 자식 스폰 지점을 한 묶음으로 생성해 Hierarchy에서 위치를 쉽게 찾고 옮길 수 있게 합니다.
        private static void CreateZombieSpawner(
            Scene scene,
            ZombieArchetype[] zombieArchetypes,
            Transform player,
            RaidSession raidSession)
        {
            var root = new GameObject("ZombieSpawner");
            SceneManager.MoveGameObjectToScene(root, scene);

            // RaidMapLayout의 외곽 좌표를 사용해 맵 크기를 바꿨을 때 생성 지점만 예전 위치에 남는 문제를 막습니다.
            var points = new ZombieSpawnPoint[RaidMapLayout.ZombieSpawnPointCount];
            for (int i = 0; i < points.Length; i++)
            {
                var pointObject = new GameObject("SpawnPoint_" + (i + 1).ToString("D2"));
                pointObject.transform.SetParent(root.transform, false);
                pointObject.transform.position = RaidMapLayout.GetZombieSpawnPosition(i);
                pointObject.transform.LookAt(RaidMapLayout.CentralCheckpointCenter);
                points[i] = pointObject.AddComponent<ZombieSpawnPoint>();
            }

            ZombieSpawner spawner = root.AddComponent<ZombieSpawner>();
            spawner.Configure(
                zombieArchetypes,
                points,
                player,
                raidSession,
                3,
                7,
                9f,
                4f,
                200f,
                10f,
                6f);
        }

        // 기존의 전체 화면 창고만 있던 Hideout을 플레이어가 직접 걸어 다니는 작은 3D 거점으로 만듭니다.
        // 별도의 Additive 씬에서 제작한 뒤 저장하므로 이 메뉴를 눌렀을 때 현재 열어 둔 씬을 닫거나 저장하지 않은 작업을 덮어쓰지 않습니다.
        private static void CreateHideoutScene(
            GameObject playerPrefab,
            ItemCatalog itemCatalog,
            GameObject storageCanvasPrefab,
            GameObject missionCanvasPrefab,
            GameObject facilityUpgradeCanvasPrefab,
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

                HideoutWorldHud worldHud = null;
                if (worldCanvasPrefab != null)
                {
                    GameObject worldCanvas = (GameObject)PrefabUtility.InstantiatePrefab(worldCanvasPrefab, scene);
                    worldHud = worldCanvas.GetComponent<HideoutWorldHud>();
                    worldHud?.SetContext(worldController, interaction);
                }

                // 모든 참조를 연결한 뒤 시설 Canvas만 닫아 첫 화면에는 걸어 다니는 거점과 조작 안내만 보이게 합니다.
                worldController.Configure(storageHud, missionHud, upgradeHud, worldHud, hideoutController, interaction, controlGate, player.transform, cameraRig.transform);
                if (storageHud != null) storageHud.gameObject.SetActive(false);
                if (missionHud != null) missionHud.gameObject.SetActive(false);
                if (upgradeHud != null) upgradeHud.gameObject.SetActive(false);

                // Configure로 넣은 씬 참조를 직렬화 변경으로 표시해야 씬을 저장한 뒤에도 연결이 유지됩니다.
                EditorUtility.SetDirty(hideoutController);
                EditorUtility.SetDirty(worldController);
                EditorUtility.SetDirty(storageFacility);
                EditorUtility.SetDirty(missionFacility);
                EditorUtility.SetDirty(craftingFacility);
                EditorUtility.SetDirty(infirmaryFacility);
                EditorUtility.SetDirty(generatorFacility);
                EditorUtility.SetDirty(missionBoard);
                if (storageHud != null) EditorUtility.SetDirty(storageHud);
                if (missionHud != null) EditorUtility.SetDirty(missionHud);
                if (upgradeHud != null) EditorUtility.SetDirty(upgradeHud);
                if (worldHud != null) EditorUtility.SetDirty(worldHud);

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

        // Bootstrap에는 저장 초기화와 거점 진입 UI만 구성합니다. 실제 Hideout은 CreateHideoutScene이 별도로 만듭니다.
        private static void CreateSimpleScene(string name, ItemCatalog itemCatalog, GameObject canvasPrefab)
        {
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            scene.name = name;
            CreateLighting();
            GameObject floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
            floor.name = "Ground";
            floor.transform.localScale = new Vector3(1.5f, 1f, 1.5f);
            SetColor(floor, new Color(0.12f, 0.14f, 0.16f));
            var cameraObject = new GameObject("Main Camera");
            cameraObject.tag = "MainCamera";
            cameraObject.transform.position = new Vector3(0f, 9f, -6f);
            cameraObject.transform.rotation = Quaternion.Euler(55f, 0f, 0f);
            cameraObject.AddComponent<Camera>().backgroundColor = new Color(0.03f, 0.04f, 0.05f);
            cameraObject.AddComponent<AudioListener>();
            if (name == "Bootstrap")
            {
                // BootstrapController가 저장 데이터를 한 번 불러오고 HUD는 초기화 상태와 수동 거점 진입 버튼만 표시합니다.
                var controllerObject = new GameObject("BootstrapController");
                BootstrapController controller = controllerObject.AddComponent<BootstrapController>();

                // 시작 화면 프리팹에 현재 씬의 초기화 컨트롤러를 연결합니다.
                if (canvasPrefab != null)
                {
                    GameObject canvas = (GameObject)PrefabUtility.InstantiatePrefab(canvasPrefab, scene);
                    canvas.GetComponent<BootstrapHud>().Configure(controller);
                }
            }
            else if (name == "Hideout")
            {
                // 컨트롤러가 성공 보상을 정확히 한 번 창고에 반영하고 HUD는 창고↔출격 목록 이동 명령만 전달합니다.
                var controllerObject = new GameObject("HideoutController");
                HideoutController controller = controllerObject.AddComponent<HideoutController>();
                controller.Configure(itemCatalog);

                // 거점 화면 프리팹에 창고 데이터를 가진 컨트롤러를 연결합니다.
                if (canvasPrefab != null)
                {
                    GameObject canvas = (GameObject)PrefabUtility.InstantiatePrefab(canvasPrefab, scene);
                    canvas.GetComponent<HideoutResultHud>().Configure(controller);
                }
            }

            // 버튼과 스크롤 목록이 마우스 입력을 받을 수 있도록 각 독립 씬에 EventSystem을 하나만 둡니다.
            CreateEventSystem();
            EditorSceneManager.SaveScene(scene, Root + "/Scenes/" + name + ".unity");
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

        // 구역 루트를 따로 만들면 Hierarchy에서 특정 지역의 엄폐물과 바닥 표시를 한 번에 접거나 이동할 수 있습니다.
        private static Transform CreateZoneRoot(string name, Transform parent)
        {
            var zone = new GameObject(name);
            zone.transform.SetParent(parent, false);
            return zone.transform;
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

        // 단단한 Cube 상자에 외부 격자와 첫 수색 1회용 전리품 생성기를 연결합니다.
        // 생성된 상자는 Scene에 명시적으로 이동시키고 색을 적용해 씬을 다시 저장했을 때 참조와 외형이 유지되게 합니다.
        private static void CreateFieldLootBox(
            Scene scene,
            string boxName,
            Vector3 position,
            int gridWidth,
            int gridHeight,
            LootTableDefinition lootTable,
            Color color)
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
            source.Configure(lootTable);
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

        // 게임 흐름 순서대로 Bootstrap, Hideout, Raid 씬을 Build Settings에 등록합니다.
        private static void SetBuildScenes()
        {
            EditorBuildSettings.scenes = new[] {
                new EditorBuildSettingsScene(Root + "/Scenes/Bootstrap.unity", true),
                new EditorBuildSettingsScene(Root + "/Scenes/Hideout.unity", true),
                new EditorBuildSettingsScene(Root + "/Scenes/RaidPrototype.unity", true)
            };
        }
    }
}
#endif
