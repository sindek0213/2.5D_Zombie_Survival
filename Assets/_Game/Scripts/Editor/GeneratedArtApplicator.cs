#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using ZombieSurvival.Items;
using ZombieSurvival.Player;
using ZombieSurvival.UI;

namespace ZombieSurvival.EditorTools
{
    // ArtRemake 폴더에 준비된 원화를 Unity 프로젝트로 복사하고 현재 아이템·플레이어·UI 프리팹에 연결하는 전용 에디터 도구입니다.
    // 프로토타입 전체 생성기와 분리했기 때문에 게임 데이터나 씬을 다시 만들지 않고 그림만 여러 번 안전하게 갱신할 수 있습니다.
    public static class GeneratedArtApplicator
    {
        private const string GeneratedRoot = "Assets/_Game/Art/Generated";
        private const string MaterialRoot = "Assets/_Game/Art/Materials";
        private const string PlayerPrefabPath = "Assets/_Game/Prefabs/Characters/Player.prefab";
        private const string InventoryItemPrefabPath = "Assets/_Game/Prefabs/UI/InventoryItem.prefab";
        private const string EquipmentSlotPrefabPath = "Assets/_Game/Prefabs/UI/EquipmentSlot.prefab";
        private const string InventoryCanvasPrefabPath = "Assets/_Game/Prefabs/UI/InventoryCanvas.prefab";
        private const string RaidHudPrefabPath = "Assets/_Game/Prefabs/UI/RaidHudCanvas.prefab";
        private const string SpriteMaterialPath = MaterialRoot + "/GeneratedSpriteWhiteKey.mat";
        private const string UiMaterialPath = MaterialRoot + "/GeneratedUiWhiteKey.mat";

        [MenuItem("Tools/2.5D Zombie Survival/Apply Generated Art Resources")]
        public static void Apply()
        {
            // 외부 ArtRemake 원본을 Assets 아래로 복사해야 Unity가 빌드에 포함하고 GUID로 참조할 수 있습니다.
            // 복사는 덮어쓰기 방식이지만 원본을 이동하거나 삭제하지 않으므로 다시 수정된 리소스를 받아도 같은 메뉴로 갱신할 수 있습니다.
            if (!CopyGeneratedPngFiles()) return;

            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            ConfigureImportedTextures();

            Material spriteMaterial = CreateOrUpdateMaterial(
                SpriteMaterialPath,
                "Assets/_Game/Resources/GeneratedArt/Shaders/GeneratedSpriteWhiteKey.shader");
            Material uiMaterial = CreateOrUpdateMaterial(
                UiMaterialPath,
                "Assets/_Game/Resources/GeneratedArt/Shaders/GeneratedUiWhiteKey.shader");
            if (spriteMaterial == null || uiMaterial == null)
            {
                Debug.LogError("생성 리소스 적용 실패: 흰 배경 제거 셰이더를 불러오지 못했습니다.");
                return;
            }

            AssignItemIcons();
            UpgradeInventoryItemPrefab(uiMaterial);
            UpgradeEquipmentSlotPrefab(uiMaterial);
            UpgradeInventoryCanvas(uiMaterial);
            UpgradeRaidHud(uiMaterial);
            UpgradePlayerPrefab(spriteMaterial);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("생성된 원화 적용 완료: 플레이어 8방향 이동·권총/소총 발사, 장비 아이콘, 인벤토리 전신 그림과 HUD 초상화를 연결했습니다.");
        }

        private static bool CopyGeneratedPngFiles()
        {
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string sourceRoot = Path.Combine(projectRoot, "ArtRemake");
            string destinationRoot = Path.Combine(projectRoot, GeneratedRoot.Replace('/', Path.DirectorySeparatorChar));
            if (!Directory.Exists(sourceRoot))
            {
                Debug.LogError($"생성 리소스 적용 실패: 프로젝트 루트에서 ArtRemake 폴더를 찾지 못했습니다.\n{sourceRoot}");
                return false;
            }

            string[] groups = { "Equipment", "Field", "Inventory", "Portraits" };
            foreach (string group in groups)
            {
                string sourceGroup = Path.Combine(sourceRoot, group);
                if (!Directory.Exists(sourceGroup)) continue;

                foreach (string sourceFile in Directory.GetFiles(sourceGroup, "*.png", SearchOption.AllDirectories))
                {
                    string relativePath = sourceFile.Substring(sourceRoot.Length)
                        .TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                    string destinationFile = Path.Combine(destinationRoot, relativePath);
                    Directory.CreateDirectory(Path.GetDirectoryName(destinationFile) ?? destinationRoot);
                    File.Copy(sourceFile, destinationFile, true);
                }
            }

            // 런타임 안전망은 Resources.Load를 사용하므로 현재 실제 화면에 연결한 파일만 작은 별도 묶음으로 복사합니다.
            // 아직 기능이 없는 근접공격·안전모 시트까지 Resources에 넣으면 테스트 빌드 용량만 커지므로 원본 보관 폴더에만 유지합니다.
            string[] runtimeFiles =
            {
                "Equipment/Weapon_Pistol_9mm_Draft.png",
                "Equipment/Weapon_AssaultRifle_Draft.png",
                "Equipment/Weapon_PumpShotgun_Draft.png",
                "Equipment/Armor_PoliceVest_Draft.png",
                "Equipment/Armor_PlateCarrier_Draft.png",
                "Equipment/Bag_Student_Draft.png",
                "Field/Movement_8Directions_Draft.png",
                "Field/Pistol_Draft.png",
                "Field/Rifle_Draft.png",
                "Inventory/Inventory_Base_v2.png",
                "Portraits/Portraits_MaskOn_Draft.png"
            };
            string resourcesRoot = Path.Combine(
                projectRoot,
                "Assets",
                "_Game",
                "Resources",
                "GeneratedArt");
            foreach (string relativePath in runtimeFiles)
            {
                string sourceFile = Path.Combine(sourceRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
                if (!File.Exists(sourceFile)) continue;
                string destinationFile = Path.Combine(resourcesRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(destinationFile) ?? resourcesRoot);
                File.Copy(sourceFile, destinationFile, true);
            }
            return true;
        }

        private static void ConfigureImportedTextures()
        {
            // 장비 원화는 ItemDefinition.Icon이 Sprite를 요구하므로 Single Sprite로 가져옵니다.
            // 나머지는 RawImage 또는 런타임 Sprite 분할에 Texture2D 그대로 사용하므로 Default 타입으로 유지합니다.
            foreach (string path in FindPngAssetPaths(GeneratedRoot + "/Equipment"))
                ConfigureTextureImporter(path, true);
            foreach (string path in FindPngAssetPaths(GeneratedRoot + "/Field"))
                ConfigureTextureImporter(path, false);
            foreach (string path in FindPngAssetPaths(GeneratedRoot + "/Inventory"))
                ConfigureTextureImporter(path, false);
            foreach (string path in FindPngAssetPaths(GeneratedRoot + "/Portraits"))
                ConfigureTextureImporter(path, false);
        }

        private static IEnumerable<string> FindPngAssetPaths(string assetFolder)
        {
            foreach (string guid in AssetDatabase.FindAssets("t:Texture2D", new[] { assetFolder }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (path.EndsWith(".png", StringComparison.OrdinalIgnoreCase)) yield return path;
            }
        }

        private static void ConfigureTextureImporter(string path, bool importAsSprite)
        {
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null) return;

            importer.textureType = importAsSprite ? TextureImporterType.Sprite : TextureImporterType.Default;
            if (importAsSprite)
            {
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.spritePixelsPerUnit = 100f;
            }

            // 원본에 실제 알파 채널이 없으므로 표시 재질이 흰색을 투명하게 처리합니다.
            // 애니메이션 시트는 Sprite.Create가 Texture 크기만 사용하므로 CPU Read/Write를 켤 필요가 없습니다.
            importer.alphaIsTransparency = false;
            importer.isReadable = false;
            importer.mipmapEnabled = false;
            importer.sRGBTexture = true;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.filterMode = FilterMode.Bilinear;
            importer.textureCompression = TextureImporterCompression.Compressed;
            importer.maxTextureSize = 2048;
            importer.SaveAndReimport();
        }

        private static Material CreateOrUpdateMaterial(string materialPath, string shaderPath)
        {
            Shader shader = AssetDatabase.LoadAssetAtPath<Shader>(shaderPath);
            if (shader == null) return null;

            Material material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            if (material == null)
            {
                material = new Material(shader);
                AssetDatabase.CreateAsset(material, materialPath);
            }
            else
            {
                material.shader = shader;
            }

            // 완전한 흰색에 가까운 좁은 범위만 제거해 눈동자·장비의 밝은 하이라이트가 과하게 사라지지 않게 합니다.
            material.SetFloat("_WhiteDistance", 0.06f);
            material.SetFloat("_EdgeSoftness", 0.08f);
            // 회색 체크무늬가 실제 픽셀로 들어간 원본도 같은 재질 하나로 배경을 제거할 수 있게 값을 저장합니다.
            material.SetFloat("_CheckerBrightness", 0.62f);
            material.SetFloat("_CheckerChroma", 0.035f);
            material.SetFloat("_CheckerSoftness", 0.06f);
            EditorUtility.SetDirty(material);
            return material;
        }

        private static void AssignItemIcons()
        {
            // 현재 구현된 아이템과 의미가 정확히 맞는 원화만 연결합니다.
            // 기관단총, 탄약과 소모품은 전용 아이콘이 아직 없으므로 다른 그림을 임시로 돌려 쓰지 않고 기존 글자 표시를 유지합니다.
            var iconMappings = new Dictionary<string, string>
            {
                { "weapon_pistol", "Weapon_Pistol_9mm_Draft.png" },
                { "weapon_rifle", "Weapon_AssaultRifle_Draft.png" },
                { "weapon_shotgun", "Weapon_PumpShotgun_Draft.png" },
                { "armor_light", "Armor_PoliceVest_Draft.png" },
                { "rig_small", "Armor_PlateCarrier_Draft.png" },
                { "backpack_small", "Bag_Student_Draft.png" }
            };

            foreach (KeyValuePair<string, string> mapping in iconMappings)
            {
                string itemPath = $"Assets/_Game/Data/Items/{mapping.Key}.asset";
                string iconPath = $"{GeneratedRoot}/Equipment/{mapping.Value}";
                ItemDefinition item = AssetDatabase.LoadAssetAtPath<ItemDefinition>(itemPath);
                Sprite icon = AssetDatabase.LoadAssetAtPath<Sprite>(iconPath);
                if (item == null || icon == null)
                {
                    Debug.LogWarning($"아이템 아이콘 연결 생략: {mapping.Key} 또는 {mapping.Value}을 불러오지 못했습니다.");
                    continue;
                }

                var serializedItem = new SerializedObject(item);
                serializedItem.FindProperty("icon").objectReferenceValue = icon;
                serializedItem.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(item);
            }
        }

        private static void UpgradeInventoryItemPrefab(Material uiMaterial)
        {
            GameObject root = PrefabUtility.LoadPrefabContents(InventoryItemPrefabPath);
            try
            {
                InventoryItemView view = root.GetComponent<InventoryItemView>();
                if (view == null) return;

                Transform existing = root.transform.Find("ItemIcon");
                Image icon = existing != null ? existing.GetComponent<Image>() : null;
                if (icon == null)
                {
                    GameObject iconObject = CreateUiObject("ItemIcon", root.transform);
                    icon = iconObject.AddComponent<Image>();
                }

                // 프리팹 기본 모습은 1×1 칸에 맞추고, 실제 생성 인스턴스의 크기와 회전은 InventoryItemView가
                // ItemDefinition의 격자 Width·Height를 읽어 전체 아이템 영역에 다시 맞춥니다.
                RectTransform iconRect = icon.rectTransform;
                iconRect.anchorMin = new Vector2(0.5f, 0.5f);
                iconRect.anchorMax = new Vector2(0.5f, 0.5f);
                iconRect.pivot = new Vector2(0.5f, 0.5f);
                iconRect.anchoredPosition = Vector2.zero;
                iconRect.sizeDelta = new Vector2(44f, 44f);
                icon.material = uiMaterial;
                icon.color = Color.white;
                icon.preserveAspect = false;
                icon.raycastTarget = false;
                icon.transform.SetSiblingIndex(Mathf.Max(0, icon.transform.parent.childCount - 2));
                view.SetIconView(icon);

                // 원화 위에서도 이름과 수량을 읽을 수 있도록 글자를 가장 앞으로 올리고 얇은 검은 외곽선을 둡니다.
                Text label = root.GetComponentInChildren<Text>(true);
                if (label != null)
                {
                    label.transform.SetAsLastSibling();
                    Outline outline = label.GetComponent<Outline>();
                    if (outline == null) outline = label.gameObject.AddComponent<Outline>();
                    outline.effectColor = new Color(0f, 0f, 0f, 0.9f);
                    outline.effectDistance = new Vector2(1f, -1f);
                }

                PrefabUtility.SaveAsPrefabAsset(root, InventoryItemPrefabPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        private static void UpgradeEquipmentSlotPrefab(Material uiMaterial)
        {
            GameObject root = PrefabUtility.LoadPrefabContents(EquipmentSlotPrefabPath);
            try
            {
                EquipmentSlotView view = root.GetComponent<EquipmentSlotView>();
                if (view == null) return;

                Transform existing = root.transform.Find("ItemIcon");
                Image icon = existing != null ? existing.GetComponent<Image>() : null;
                if (icon == null)
                {
                    GameObject iconObject = CreateUiObject("ItemIcon", root.transform);
                    icon = iconObject.AddComponent<Image>();
                }

                RectTransform iconRect = icon.rectTransform;
                iconRect.anchorMin = new Vector2(0f, 0.5f);
                iconRect.anchorMax = new Vector2(0f, 0.5f);
                iconRect.pivot = new Vector2(0f, 0.5f);
                iconRect.anchoredPosition = new Vector2(5f, 0f);
                iconRect.sizeDelta = new Vector2(62f, 56f);
                icon.material = uiMaterial;
                icon.color = Color.white;
                icon.preserveAspect = true;
                icon.raycastTarget = false;
                view.SetIconView(icon);

                Text label = root.GetComponentInChildren<Text>(true);
                if (label != null)
                {
                    Stretch(label.rectTransform, new Vector2(70f, 4f), new Vector2(-6f, -4f));
                    label.alignment = TextAnchor.MiddleLeft;
                    label.transform.SetAsLastSibling();
                }

                PrefabUtility.SaveAsPrefabAsset(root, EquipmentSlotPrefabPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        private static void UpgradeInventoryCanvas(Material uiMaterial)
        {
            Texture2D characterTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(
                GeneratedRoot + "/Inventory/Inventory_Base_v2.png");
            if (characterTexture == null) return;

            GameObject root = PrefabUtility.LoadPrefabContents(InventoryCanvasPrefabPath);
            try
            {
                Transform panel = root.transform.Find("InventoryPanel");
                if (panel == null) return;

                Transform existing = panel.Find("CharacterPreview");
                RawImage preview = existing != null ? existing.GetComponent<RawImage>() : null;
                if (preview == null)
                {
                    GameObject previewObject = CreateUiObject("CharacterPreview", panel);
                    preview = previewObject.AddComponent<RawImage>();
                }

                SetTopLeft(preview.rectTransform, new Vector2(43f, -86f), new Vector2(190f, 408f));
                preview.texture = characterTexture;
                preview.material = uiMaterial;
                // 장비 슬롯을 가리지 않는 배경형 종이인형으로 사용하며, 슬롯 사이에서는 캐릭터가 자연스럽게 보입니다.
                preview.color = new Color(1f, 1f, 1f, 0.48f);
                preview.raycastTarget = false;
                // 캐릭터의 실제 비율을 유지하면서 좌우의 불필요한 체크무늬 여백만 제외합니다.
                preview.uvRect = new Rect(0.16f, 0.015f, 0.68f, 0.97f);
                preview.transform.SetSiblingIndex(0);

                // 기존 프리팹의 장비 슬롯도 전신 그림 아래로 내려 에디터에서 보는 배치와 런타임 배치를 같게 만듭니다.
                Transform equipmentTitle = panel.Find("EquipmentTitle");
                if (equipmentTitle != null)
                    SetTopLeft((RectTransform)equipmentTitle, new Vector2(40f, -520f), new Vector2(210f, 30f));

                string[] slotNames =
                {
                    "PrimaryWeaponSlot",
                    "SecondaryWeaponSlot",
                    "ArmorSlot",
                    "RigSlot",
                    "BackpackSlot"
                };
                for (int index = 0; index < slotNames.Length; index++)
                {
                    RectTransform slotRect = panel.Find(slotNames[index]) as RectTransform;
                    if (slotRect == null) continue;
                    SetTopLeft(slotRect, new Vector2(40f, -556f - index * 62f), new Vector2(205f, 58f));
                }

                PrefabUtility.SaveAsPrefabAsset(root, InventoryCanvasPrefabPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        private static void UpgradeRaidHud(Material uiMaterial)
        {
            Texture2D portraitSheet = AssetDatabase.LoadAssetAtPath<Texture2D>(
                GeneratedRoot + "/Portraits/Portraits_MaskOn_Draft.png");
            if (portraitSheet == null) return;

            GameObject root = PrefabUtility.LoadPrefabContents(RaidHudPrefabPath);
            try
            {
                Transform existing = root.transform.Find("PlayerPortrait");
                RawImage portrait = existing != null ? existing.GetComponent<RawImage>() : null;
                if (portrait == null)
                {
                    GameObject portraitObject = CreateUiObject("PlayerPortrait", root.transform);
                    portrait = portraitObject.AddComponent<RawImage>();
                }

                SetTopLeft(portrait.rectTransform, new Vector2(22f, -12f), new Vector2(78f, 78f));
                portrait.texture = portraitSheet;
                portrait.material = uiMaterial;
                portrait.color = Color.white;
                portrait.raycastTarget = false;
                // 초상화 시트는 6열 × 4행입니다. UV 원점이 아래이므로 y=0.75부터 첫 번째 정상 표정 한 칸만 잘라 표시합니다.
                portrait.uvRect = new Rect(4f / 1536f, 772f / 1024f, 248f / 1536f, 248f / 1024f);
                portrait.transform.SetAsFirstSibling();

                RectTransform guide = root.transform.Find("ControlGuide") as RectTransform;
                if (guide != null)
                {
                    guide.anchoredPosition = new Vector2(112f, guide.anchoredPosition.y);
                    guide.sizeDelta = new Vector2(1012f, guide.sizeDelta.y);
                }

                PrefabUtility.SaveAsPrefabAsset(root, RaidHudPrefabPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        private static void UpgradePlayerPrefab(Material spriteMaterial)
        {
            Texture2D movement = LoadGeneratedTexture("Field/Movement_8Directions_Draft.png");
            Texture2D pistol = LoadGeneratedTexture("Field/Pistol_Draft.png");
            Texture2D rifle = LoadGeneratedTexture("Field/Rifle_Draft.png");
            if (movement == null) return;

            GameObject root = PrefabUtility.LoadPrefabContents(PlayerPrefabPath);
            try
            {
                SpriteRenderer renderer = root.GetComponentInChildren<SpriteRenderer>(true);
                if (renderer == null) return;

                PlayerSpriteAnimator animator = root.GetComponent<PlayerSpriteAnimator>();
                if (animator == null) animator = root.AddComponent<PlayerSpriteAnimator>();
                animator.Configure(renderer, spriteMaterial, movement, pistol, rifle);
                renderer.sharedMaterial = spriteMaterial;
                renderer.color = Color.white;

                // 이전 단일 그림 좌우 반전 값이 프리팹에 남아 있더라도 방향별 시트가 다시 뒤집히지 않게 양수 크기로 초기화합니다.
                Transform visual = renderer.transform;
                Vector3 scale = visual.localScale;
                scale.x = Mathf.Abs(scale.x) > 0.001f ? Mathf.Abs(scale.x) : 1f;
                visual.localScale = scale;

                PrefabUtility.SaveAsPrefabAsset(root, PlayerPrefabPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        private static Texture2D LoadGeneratedTexture(string relativePath)
        {
            return AssetDatabase.LoadAssetAtPath<Texture2D>($"{GeneratedRoot}/{relativePath}");
        }

        private static GameObject CreateUiObject(string name, Transform parent)
        {
            var result = new GameObject(name, typeof(RectTransform));
            result.transform.SetParent(parent, false);
            return result;
        }

        private static void Stretch(RectTransform rect, Vector2 minimumOffset, Vector2 maximumOffset)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.offsetMin = minimumOffset;
            rect.offsetMax = maximumOffset;
        }

        private static void SetTopLeft(RectTransform rect, Vector2 position, Vector2 size)
        {
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
        }
    }
}
#endif
