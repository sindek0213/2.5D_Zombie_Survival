#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using ZombieSurvival.AI;
using ZombieSurvival.CameraSystem;
using ZombieSurvival.Combat;
using ZombieSurvival.Extraction;
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

        // 프로젝트를 처음 열었을 때 핵심 Raid 씬이 없으면 한 번만 자동 생성합니다.
        [InitializeOnLoadMethod]
        private static void BuildMissingPrototypeContent()
        {
            if (Application.isBatchMode) return;

            // 전투 컴포넌트가 없는 이전 프리팹도 새 단계 콘텐츠로 자동 업그레이드합니다.
            GameObject playerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(Root + "/Prefabs/Characters/Player.prefab");
            bool combatIsReady = playerPrefab != null && playerPrefab.GetComponent<PlayerCombat>() != null;
            bool playerHealthIsReady = playerPrefab != null && playerPrefab.GetComponent<Health>() != null;
            bool zombieIsReady = AssetDatabase.LoadAssetAtPath<GameObject>(Root + "/Prefabs/Enemies/Zombie.prefab") != null;
            bool itemsAreReady = AssetDatabase.LoadAssetAtPath<ItemDefinition>(Root + "/Data/Items/ammo_9mm.asset") != null;
            ItemDefinition radioDefinition = AssetDatabase.LoadAssetAtPath<ItemDefinition>(Root + "/Data/Items/quest_radio.asset");
            bool gridItemsAreReady = radioDefinition != null && radioDefinition.GridWidth == 2 && radioDefinition.GridHeight == 2;
            ItemDefinition backpackDefinition = AssetDatabase.LoadAssetAtPath<ItemDefinition>(Root + "/Data/Items/backpack_small.asset");
            bool equipmentIsReady = backpackDefinition != null &&
                                    backpackDefinition.EquipmentSlot == EquipmentSlotType.Backpack &&
                                    backpackDefinition.ContainerWidth == 5 && backpackDefinition.ContainerHeight == 4;
            bool extractionIsReady = AssetDatabase.LoadAssetAtPath<ExtractionRequirement>(
                Root + "/Data/Extraction/quest_radio_extraction.asset") != null;
            PlayerAim prefabAim = playerPrefab != null ? playerPrefab.GetComponent<PlayerAim>() : null;
            bool aimOriginIsReady = prefabAim != null &&
                                    new SerializedObject(prefabAim).FindProperty("aimOrigin").objectReferenceValue != null;
            if (File.Exists(Root + "/Scenes/RaidPrototype.unity") && combatIsReady && playerHealthIsReady &&
                zombieIsReady && itemsAreReady && gridItemsAreReady && equipmentIsReady && extractionIsReady && aimOriginIsReady &&
                playerPrefab.GetComponent<PlayerInventory>() != null) return;
            EditorApplication.delayCall += Rebuild;
        }

        // 폴더 → 정의 데이터 → 프리팹 → 씬 → Build Settings 순서로 전체 콘텐츠를 생성하는 진입점입니다.
        // 뒤 단계가 앞에서 생성한 에셋을 참조하므로 이 호출 순서를 임의로 바꾸면 null 참조가 생길 수 있습니다.
        // SaveAssets와 Refresh는 메모리의 변경을 디스크와 Unity AssetDatabase에 최종 반영합니다.
        [MenuItem("Tools/2.5D Zombie Survival/Rebuild Prototype Content")]
        public static void Rebuild()
        {
            // 의존 순서에 맞춰 폴더와 에셋을 만든 뒤 Build Settings까지 갱신합니다.
            EnsureFolders();
            Sprite sprite = CreatePlayerSprite();
            ItemDefinition[] itemDefinitions = CreateItemDefinitions();
            ExtractionRequirement extractionRequirement = CreateExtractionRequirement();
            GameObject prefab = CreatePlayerPrefab(sprite);
            GameObject zombiePrefab = CreateZombiePrefab(sprite);
            CreateSimpleScene("Bootstrap", "BOOTSTRAP\nPrototype entry scene");
            CreateSimpleScene("Hideout", "HIDEOUT\nRaid systems arrive in a later milestone");
            CreateRaidScene(prefab, zombiePrefab, itemDefinitions, extractionRequirement);
            SetBuildScenes();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("2.5D prototype content rebuilt successfully.");
        }

        // 지시문에 정의된 프로젝트 폴더 구조를 AssetDatabase를 통해 생성합니다.
        private static void EnsureFolders()
        {
            string[] paths = {
                "Art/Materials", "Art/Sprites/Player", "Art/Sprites/Zombies", "Art/Sprites/Items", "Art/Sprites/UI", "Art/Models",
                "Audio/Music", "Audio/SFX", "Data/Items", "Data/Weapons", "Data/Zombies", "Data/Extraction",
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

        // 레이드에 배치할 모든 ItemDefinition을 고정 배열 순서로 생성합니다.
        // CreateRaidScene이 배열 인덱스로 아이템을 선택하므로 여기의 순서를 바꾸면 월드 배치 인덱스도 함께 수정해야 합니다.
        // ItemId는 저장용 고유 키이고 표시 이름과 설명은 UI에서 사용자에게 보여 줄 값입니다.
        private static ItemDefinition[] CreateItemDefinitions()
        {
            return new[]
            {
                CreateItemDefinition("ammo_9mm", "9mm 탄약", ItemType.Ammo, 30, 1, 1, true, "권총 탄약입니다."),
                CreateItemDefinition("medkit_small", "소형 구급상자", ItemType.Healing, 3, 2, 1, true, "체력을 회복하는 의료품입니다."),
                CreateItemDefinition("food_canned", "통조림", ItemType.Food, 5, 1, 2, true, "보존 가능한 식량입니다."),
                CreateItemDefinition("quest_radio", "고장 난 무전기", ItemType.Quest, 1, 2, 2, false, "탈출 조건용 퀘스트 아이템입니다."),
                CreateItemDefinition("weapon_rifle", "낡은 소총", ItemType.Weapon, 1, 5, 2, false, "주무기입니다.", EquipmentSlotType.PrimaryWeapon),
                CreateItemDefinition("weapon_pistol", "권총", ItemType.Weapon, 1, 2, 1, false, "보조무기입니다.", EquipmentSlotType.SecondaryWeapon),
                CreateItemDefinition("armor_light", "경량 방탄복", ItemType.Armor, 1, 3, 3, false, "기본 방탄복입니다.", EquipmentSlotType.Armor),
                CreateItemDefinition("rig_small", "소형 전술 조끼", ItemType.Container, 1, 3, 2, false, "4x2 내부 격자를 제공합니다.", EquipmentSlotType.Rig, 4, 2),
                CreateItemDefinition("backpack_small", "소형 가방", ItemType.Container, 1, 4, 4, false, "5x4 내부 격자를 제공합니다.", EquipmentSlotType.Backpack, 5, 4)
            };
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
            int containerHeight = 0)
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
            serialized.FindProperty("maxStack").intValue = maxStack;
            serialized.FindProperty("gridWidth").intValue = gridWidth;
            serialized.FindProperty("gridHeight").intValue = gridHeight;
            serialized.FindProperty("usable").boolValue = usable;
            serialized.FindProperty("description").stringValue = description;
            serialized.FindProperty("equipmentSlot").enumValueIndex = (int)equipmentSlot;
            serialized.FindProperty("containerWidth").intValue = containerWidth;
            serialized.FindProperty("containerHeight").intValue = containerHeight;
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
            player.AddComponent<Health>();
            player.AddComponent<PlayerInventory>();
            player.AddComponent<PlayerInteraction>();

            // 스프라이트는 충돌체 회전에 영향을 주지 않도록 Visual 자식으로 분리합니다.
            var visual = new GameObject("Visual");
            visual.transform.SetParent(player.transform, false);
            visual.transform.localPosition = new Vector3(0f, 0.85f, 0f);
            visual.AddComponent<SpriteRenderer>().sprite = sprite;
            visual.AddComponent<SpriteBillboard>();

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
            zombie.AddComponent<Health>();
            zombie.AddComponent<ZombieMovement>();
            zombie.AddComponent<ZombieAttack>();
            zombie.AddComponent<ZombieBrain>();

            var visual = new GameObject("Visual");
            visual.transform.SetParent(zombie.transform, false);
            visual.transform.localPosition = new Vector3(0f, 0.85f, 0f);
            SpriteRenderer renderer = visual.AddComponent<SpriteRenderer>();
            renderer.sprite = sprite;
            renderer.color = new Color(0.48f, 0.72f, 0.36f);
            visual.AddComponent<SpriteBillboard>();

            string path = Root + "/Prefabs/Enemies/Zombie.prefab";
            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(zombie, path);
            Object.DestroyImmediate(zombie);
            return prefab;
        }

        // 빈 씬에 바닥, 벽, 플레이어, 좀비, 아이템, 카메라와 HUD를 배치해 모든 현재 시스템을 함께 검증합니다.
        // 완성형 레벨이 아니라 기능 통합 테스트 맵이며, 같은 경로로 SaveScene하므로 Rebuild 시 기존 씬이 교체됩니다.
        private static void CreateRaidScene(
            GameObject playerPrefab,
            GameObject zombiePrefab,
            ItemDefinition[] itemDefinitions,
            ExtractionRequirement extractionRequirement)
        {
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            scene.name = "RaidPrototype";
            CreateLighting();

            GameObject floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
            floor.name = "Ground";
            floor.transform.localScale = new Vector3(2.4f, 1f, 2.4f);
            SetColor(floor, new Color(0.19f, 0.22f, 0.20f));

            // 외곽 벽과 두 개의 엄폐물을 배치해 CharacterController 충돌을 확인할 수 있게 합니다.
            CreateWall("Wall_North", new Vector3(0f, 1f, 12f), new Vector3(25f, 2f, 1f));
            CreateWall("Wall_South", new Vector3(0f, 1f, -12f), new Vector3(25f, 2f, 1f));
            CreateWall("Wall_East", new Vector3(12f, 1f, 0f), new Vector3(1f, 2f, 25f));
            CreateWall("Wall_West", new Vector3(-12f, 1f, 0f), new Vector3(1f, 2f, 25f));
            CreateWall("Cover_A", new Vector3(-3f, 0.75f, 2f), new Vector3(4f, 1.5f, 1f));
            CreateWall("Cover_B", new Vector3(4f, 0.75f, -2f), new Vector3(1f, 1.5f, 4f));

            GameObject player = (GameObject)PrefabUtility.InstantiatePrefab(playerPrefab, scene);
            player.transform.position = Vector3.zero;

            // 서로 다른 위치에 좀비를 배치해 감지, 추적, 공격과 사망을 확인합니다.
            CreateZombie(zombiePrefab, "Zombie_A", new Vector3(-6f, 0f, 6f), scene);
            CreateZombie(zombiePrefab, "Zombie_B", new Vector3(0f, 0f, 8f), scene);
            CreateZombie(zombiePrefab, "Zombie_C", new Vector3(7f, 0f, 5f), scene);

            // 맵 곳곳에 서로 다른 종류와 수량의 루팅 아이템을 배치합니다.
            CreateWorldItem("AmmoPickup", new Vector3(-3f, 0.3f, -4f), itemDefinitions[0], 12, new Color(0.85f, 0.72f, 0.2f));
            CreateWorldItem("MedkitPickup", new Vector3(3f, 0.3f, 3f), itemDefinitions[1], 1, new Color(0.85f, 0.2f, 0.2f));
            CreateWorldItem("FoodPickup", new Vector3(6f, 0.3f, -5f), itemDefinitions[2], 2, new Color(0.85f, 0.48f, 0.18f));
            CreateWorldItem("QuestRadioPickup", new Vector3(-7f, 0.3f, 1f), itemDefinitions[3], 1, new Color(0.25f, 0.65f, 1f));
            CreateWorldItem("RiflePickup", new Vector3(-8f, 0.3f, -6f), itemDefinitions[4], 1, new Color(0.3f, 0.3f, 0.3f));
            CreateWorldItem("PistolPickup", new Vector3(8f, 0.3f, -6f), itemDefinitions[5], 1, new Color(0.25f, 0.25f, 0.25f));
            CreateWorldItem("ArmorPickup", new Vector3(-8f, 0.3f, 8f), itemDefinitions[6], 1, new Color(0.25f, 0.45f, 0.55f));
            CreateWorldItem("RigPickup", new Vector3(8f, 0.3f, 8f), itemDefinitions[7], 1, new Color(0.35f, 0.5f, 0.3f));
            CreateWorldItem("BackpackPickup", new Vector3(0f, 0.3f, -8f), itemDefinitions[8], 1, new Color(0.3f, 0.4f, 0.3f));

            // 북쪽 벽 안쪽에 반경이 넉넉한 탈출 Trigger와 눈에 보이는 녹색 원통을 배치합니다.
            // Trigger 루트와 Visual을 분리해 시각용 Mesh Collider가 진입 판정에 중복 참여하지 않게 합니다.
            ExtractionController extraction = CreateExtractionPoint(extractionRequirement);

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

            // 현재 탄창과 예비탄을 표시하는 프로토타입 HUD를 씬에 추가합니다.
            var hudObject = new GameObject("CombatHUD");
            AmmoHud hud = hudObject.AddComponent<AmmoHud>();
            hud.SetCombat(player.GetComponent<PlayerCombat>());
            hud.SetPlayerHealth(player.GetComponent<Health>());

            var inventoryHudObject = new GameObject("InventoryHUD");
            inventoryHudObject.AddComponent<InventoryHud>().Configure(
                player.GetComponent<PlayerInventory>(),
                player.GetComponent<PlayerInteraction>());

            var extractionHudObject = new GameObject("ExtractionHUD");
            extractionHudObject.AddComponent<ExtractionHud>().SetController(extraction);

            EditorSceneManager.SaveScene(scene, Root + "/Scenes/RaidPrototype.unity");
        }

        // 아직 게임 로직이 없는 Bootstrap과 Hideout에 구분 가능한 안내 씬을 만듭니다.
        private static void CreateSimpleScene(string name, string label)
        {
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            scene.name = name;
            CreateLighting();
            GameObject floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
            floor.name = "Ground";
            floor.transform.localScale = new Vector3(1.5f, 1f, 1.5f);
            SetColor(floor, new Color(0.12f, 0.14f, 0.16f));
            var textObject = new GameObject("SceneInfo");
            var text = textObject.AddComponent<TextMesh>();
            text.text = label;
            text.anchor = TextAnchor.MiddleCenter;
            text.alignment = TextAlignment.Center;
            text.characterSize = 0.3f;
            text.fontSize = 48;
            text.color = Color.white;
            textObject.transform.position = new Vector3(0f, 0.05f, 0f);
            textObject.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            var cameraObject = new GameObject("Main Camera");
            cameraObject.tag = "MainCamera";
            cameraObject.transform.position = new Vector3(0f, 9f, -6f);
            cameraObject.transform.rotation = Quaternion.Euler(55f, 0f, 0f);
            cameraObject.AddComponent<Camera>().backgroundColor = new Color(0.03f, 0.04f, 0.05f);
            cameraObject.AddComponent<AudioListener>();
            EditorSceneManager.SaveScene(scene, Root + "/Scenes/" + name + ".unity");
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

        // Cube 프리미티브를 이용해 3D 충돌 가능한 벽이나 엄폐물을 만듭니다.
        private static void CreateWall(string name, Vector3 position, Vector3 scale)
        {
            GameObject wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            wall.name = name;
            wall.transform.position = position;
            wall.transform.localScale = scale;
            SetColor(wall, new Color(0.28f, 0.30f, 0.32f));
        }

        // 지정 위치에 좀비 프리팹 인스턴스를 배치합니다.
        private static void CreateZombie(GameObject prefab, string name, Vector3 position, Scene scene)
        {
            GameObject zombie = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
            zombie.name = name;
            zombie.transform.position = position;
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

            WorldItem worldItem = item.AddComponent<WorldItem>();
            var serialized = new SerializedObject(worldItem);
            serialized.FindProperty("definition").objectReferenceValue = definition;
            serialized.FindProperty("quantity").intValue = quantity;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        // BoxCollider Trigger가 플레이어 진입과 이탈을 감지하고 ExtractionController가 조건과 시간을 계산합니다.
        // Visual 원통은 Collider를 제거해 오직 구역 위치를 보여 주는 역할만 하도록 구성합니다.
        private static ExtractionController CreateExtractionPoint(ExtractionRequirement requirement)
        {
            var root = new GameObject("ExtractionPoint");
            root.transform.position = new Vector3(0f, 0f, 10f);
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
