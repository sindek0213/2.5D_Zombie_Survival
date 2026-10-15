#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using ZombieSurvival.UI;

namespace ZombieSurvival.EditorTools
{
    // 기존 UI 프리팹의 기능 연결과 배치를 유지한 채 공용 생존 게임 테마만 입히는 Editor 전용 도구입니다.
    // 화면을 다시 생성하지 않으므로 HUD 스크립트에 연결된 Text, Button, Image 참조나 사용자가 조정한 RectTransform 값이 사라지지 않습니다.
    public static class SurvivalUiThemeApplicator
    {
        private const string UiPrefabRoot = "Assets/_Game/Prefabs/UI/";
        private const string PanelTexturePath = "Assets/_Game/Art/Sprites/UI/SurvivalPanelTexture_v1.png";
        private const string AutoApplySessionKey = "ZombieSurvival.SurvivalUiThemeAppliedThisSession";

        // 현재 게임에서 실제로 사용하는 UI 프리팹을 한 목록에 모읍니다.
        // 새 UI 프리팹을 추가하면 이 배열에 경로를 넣는 것만으로 같은 테마를 적용할 수 있습니다.
        private static readonly string[] PrefabPaths =
        {
            UiPrefabRoot + "TitleCanvas.prefab",
            UiPrefabRoot + "LoadingCanvas.prefab",
            UiPrefabRoot + "EquipmentSlot.prefab",
            UiPrefabRoot + "HideoutCanvas.prefab",
            UiPrefabRoot + "HideoutDeploymentCanvas.prefab",
            UiPrefabRoot + "HideoutFacilityUpgradeCanvas.prefab",
            UiPrefabRoot + "HideoutMissionCanvas.prefab",
            UiPrefabRoot + "HideoutTraderCanvas.prefab",
            UiPrefabRoot + "HideoutWorldCanvas.prefab",
            UiPrefabRoot + "InventoryCanvas.prefab",
            UiPrefabRoot + "InventoryGrid.prefab",
            UiPrefabRoot + "InventoryGridCell.prefab",
            UiPrefabRoot + "InventoryItem.prefab",
            UiPrefabRoot + "InventoryItemCell.prefab",
            UiPrefabRoot + "PauseCanvas.prefab",
            UiPrefabRoot + "RaidHudCanvas.prefab"
        };

        // 새 스크립트가 컴파일되거나 프로젝트를 다시 열었을 때 현재 프리팹에도 테마를 자동 적용합니다.
        // SessionState를 사용하므로 같은 Unity 실행 중에는 한 번만 동작하고, Play 진입이나 일반 에셋 저장 때마다 프리팹을 반복해서 저장하지 않습니다.
        [InitializeOnLoadMethod]
        private static void ScheduleAutomaticApplication()
        {
            if (SessionState.GetBool(AutoApplySessionKey, false)) return;
            EditorApplication.delayCall += TryAutomaticApplication;
        }

        private static void TryAutomaticApplication()
        {
            // 컴파일이나 임포트 도중 PrefabUtility를 실행하면 Unity가 다시 도메인 리로드를 시작할 수 있으므로 준비가 끝난 다음 프레임까지 기다립니다.
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                EditorApplication.delayCall += TryAutomaticApplication;
                return;
            }

            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            if (AssetDatabase.LoadAssetAtPath<Sprite>(PanelTexturePath) == null) return;

            SessionState.SetBool(AutoApplySessionKey, true);
            ApplyTheme(true);
        }

        // 자동 생성 뒤에도 테마를 다시 입힐 수 있도록 메뉴에서 언제든 반복 실행할 수 있는 진입점입니다.
        // 같은 이름의 장식 오브젝트를 찾아 재사용하므로 여러 번 실행해도 선이나 그림자가 중복 생성되지 않습니다.
        [MenuItem("Tools/2.5D Zombie Survival/Apply Survival UI Theme")]
        public static void ApplyThemeFromMenu()
        {
            ApplyTheme(true);
        }

        // 배치 모드에서 이 메서드를 직접 호출하면 UI 프리팹만 갱신할 수 있습니다.
        // 프로토타입 전체 씬을 다시 만들지 않기 때문에 맵 배치와 게임 데이터에는 영향을 주지 않습니다.
        public static void ApplyThemeFromCommandLine()
        {
            ApplyTheme(true);
        }

        // PrototypeContentBuilder에서도 호출할 수 있도록 저장·로그 여부만 선택 가능한 공용 적용 함수입니다.
        // 전체 Rebuild 중에는 마지막 SaveAssets가 따로 실행되므로 saveAndRefresh를 false로 전달해 중복 저장을 피할 수 있습니다.
        public static void ApplyTheme(bool saveAndRefresh)
        {
            Sprite panelTexture = AssetDatabase.LoadAssetAtPath<Sprite>(PanelTexturePath);
            if (panelTexture == null)
            {
                Debug.LogError("UI 테마 적용 실패: SurvivalPanelTexture_v1.png가 Sprite로 임포트되어 있지 않습니다.");
                return;
            }

            int changedPrefabCount = 0;
            foreach (string path in PrefabPaths)
            {
                if (AssetDatabase.LoadAssetAtPath<GameObject>(path) == null) continue;
                ApplyThemeToPrefab(path, panelTexture);
                changedPrefabCount++;
            }

            if (saveAndRefresh)
            {
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
            }

            Debug.Log($"Survival UI theme applied to {changedPrefabCount} prefabs.");
        }

        private static void ApplyThemeToPrefab(string prefabPath, Sprite panelTexture)
        {
            GameObject root = PrefabUtility.LoadPrefabContents(prefabPath);
            try
            {
                // Image를 먼저 정리한 뒤 Button을 덮어써야 버튼 배경이 일반 패널색으로 바뀌지 않습니다.
                foreach (Image image in root.GetComponentsInChildren<Image>(true))
                    StyleImage(image, panelTexture, prefabPath);

                foreach (Button button in root.GetComponentsInChildren<Button>(true))
                    StyleButton(button);

                foreach (Text text in root.GetComponentsInChildren<Text>(true))
                    StyleText(text);

                // 런타임에 상태에 따라 색을 다시 쓰는 컴포넌트의 직렬화 값도 같은 팔레트로 맞춥니다.
                // 이 과정을 빼면 에디터에서는 새 색으로 보이지만 Play를 누른 직후 옛 색으로 돌아가게 됩니다.
                ApplyRuntimeStateColors(root);

                PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        private static void StyleImage(Image image, Sprite panelTexture, string prefabPath)
        {
            if (image == null) return;
            string objectName = image.gameObject.name;

            // 피해 화면과 실제 아이템·캐릭터 원화는 고유 색과 투명도가 기능의 일부이므로 테마 색을 덮어쓰지 않습니다.
            if (objectName == "DamageOverlay" || IsArtworkImage(objectName)) return;

            // 버튼 배경은 중요도와 위험 동작을 구분해야 하므로 StyleButton에서 별도로 처리합니다.
            if (image.GetComponent<Button>() != null) return;

            if (IsMajorPanel(objectName))
            {
                // 큰 창은 생성한 낡은 금속·천 질감을 사용하고, 위쪽 주황 선으로 창의 시작 위치를 분명하게 보여 줍니다.
                image.sprite = panelTexture;
                image.type = Image.Type.Simple;
                image.preserveAspect = false;
                image.color = SurvivalUiTheme.TexturedPanelTint;
                AddOrUpdateOutline(image.gameObject, SurvivalUiTheme.Border, new Vector2(2f, -2f));
                AddOrUpdateAccentStrip(image.transform, "ThemeTopAccent", true, SurvivalUiTheme.Accent, 5f);
                AddOrUpdateAccentStrip(image.transform, "ThemeSideAccent", false, SurvivalUiTheme.AccentSoft, 3f);
                return;
            }

            if (objectName == "GridArea")
            {
                image.sprite = null;
                image.color = SurvivalUiTheme.GridBackground;
                AddOrUpdateOutline(image.gameObject, SurvivalUiTheme.BorderDim, new Vector2(1f, -1f));
                return;
            }

            if (objectName == "InventoryGridCell")
            {
                image.sprite = null;
                image.color = SurvivalUiTheme.GridCell;
                return;
            }

            if (objectName == "InventoryItemCell")
            {
                image.sprite = null;
                image.color = SurvivalUiTheme.ItemCommon;
                return;
            }

            if (objectName == "EquipmentSlot")
            {
                image.sprite = null;
                image.color = SurvivalUiTheme.EquipmentEmpty;
                AddOrUpdateOutline(image.gameObject, SurvivalUiTheme.BorderDim, new Vector2(1f, -1f));
                AddOrUpdateAccentStrip(image.transform, "ThemeSideAccent", false, SurvivalUiTheme.AccentSoft, 3f);
                return;
            }

            if (objectName == "StateAccent")
            {
                image.sprite = null;
                image.color = SurvivalUiTheme.Accent;
                return;
            }

            if (objectName == "Background")
            {
                // 거점과 시작 화면의 바탕은 질감이 보여도 되지만, 팝업 뒤 Background는 내용을 가리는 반투명 암막 역할만 합니다.
                bool isBaseScreen = prefabPath.EndsWith("TitleCanvas.prefab", StringComparison.Ordinal) ||
                                    prefabPath.EndsWith("LoadingCanvas.prefab", StringComparison.Ordinal) ||
                                    prefabPath.EndsWith("HideoutCanvas.prefab", StringComparison.Ordinal);
                image.sprite = isBaseScreen ? panelTexture : null;
                image.type = Image.Type.Simple;
                image.preserveAspect = false;
                image.color = isBaseScreen ? SurvivalUiTheme.TexturedPanelTint : SurvivalUiTheme.ScreenBackdrop;
                return;
            }

            // Sprite가 없는 단색 Image만 보조 패널로 취급합니다. 아이콘과 원화 Sprite는 위 조건에 없어도 그대로 보존됩니다.
            if (image.sprite == null && LooksLikeSecondarySurface(objectName))
            {
                image.color = SurvivalUiTheme.PanelDeep;
                AddOrUpdateOutline(image.gameObject, SurvivalUiTheme.BorderDim, new Vector2(1f, -1f));
            }
        }

        private static void StyleButton(Button button)
        {
            if (button == null) return;
            Image background = button.targetGraphic as Image;
            if (background == null) background = button.GetComponent<Image>();
            if (background == null) return;

            bool isDanger = button.gameObject.name.IndexOf("Abandon", StringComparison.OrdinalIgnoreCase) >= 0 ||
                            button.gameObject.name.IndexOf("Return", StringComparison.OrdinalIgnoreCase) >= 0 ||
                            button.gameObject.name.IndexOf("Quit", StringComparison.OrdinalIgnoreCase) >= 0;
            bool isPrimary = !isDanger && IsPrimaryAction(button.gameObject.name);
            background.sprite = null;
            background.color = isDanger
                ? SurvivalUiTheme.ButtonDanger
                : isPrimary ? SurvivalUiTheme.ButtonPrimary : SurvivalUiTheme.ButtonNormal;

            // Selectable의 상태색은 원래 배경색에 곱해지므로 Normal은 흰색으로 두고 Hover와 Pressed에서만 따뜻한 변화를 줍니다.
            ColorBlock colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1f, 0.86f, 0.66f, 1f);
            colors.pressedColor = new Color(0.72f, 0.60f, 0.43f, 1f);
            colors.selectedColor = new Color(1f, 0.80f, 0.54f, 1f);
            colors.disabledColor = SurvivalUiTheme.ButtonDisabled;
            colors.colorMultiplier = 1f;
            colors.fadeDuration = 0.08f;
            button.colors = colors;

            Color outlineColor = isDanger ? SurvivalUiTheme.Danger : isPrimary ? SurvivalUiTheme.Accent : SurvivalUiTheme.Border;
            AddOrUpdateOutline(button.gameObject, outlineColor, new Vector2(1.5f, -1.5f));
            if (isPrimary || isDanger)
            {
                Color stripColor = isDanger ? SurvivalUiTheme.Danger : SurvivalUiTheme.Accent;
                AddOrUpdateAccentStrip(button.transform, "ThemeButtonAccent", false, stripColor, 4f);
            }
            else
            {
                RemoveThemeDecoration(button.transform, "ThemeButtonAccent");
            }
        }

        private static void StyleText(Text text)
        {
            if (text == null) return;
            string objectName = text.gameObject.name;
            // 타이틀 화면은 주 제목만 주황색으로 강조하고 부제·태그라인·빌드 표시는 단계적으로 약하게 보여야 합니다.
            // 일반 이름 규칙만 사용하면 Subtitle 안의 "Title" 문자열까지 제목으로 인식되므로 세 전용 이름을 먼저 구분합니다.
            bool isGameSubtitle = objectName == "Subtitle";
            bool isGameTagline = objectName == "Tagline";
            bool isGameBuildLabel = objectName == "BuildLabel";
            bool isHeading = objectName.IndexOf("Title", StringComparison.OrdinalIgnoreCase) >= 0 ||
                             objectName.IndexOf("Header", StringComparison.OrdinalIgnoreCase) >= 0;
            bool isMuted = objectName.IndexOf("Description", StringComparison.OrdinalIgnoreCase) >= 0 ||
                           objectName.IndexOf("Detail", StringComparison.OrdinalIgnoreCase) >= 0 ||
                           objectName.IndexOf("Guide", StringComparison.OrdinalIgnoreCase) >= 0 ||
                           objectName.IndexOf("Progress", StringComparison.OrdinalIgnoreCase) >= 0;

            text.color = isGameTagline || isGameBuildLabel
                ? SurvivalUiTheme.TextMuted
                : isGameSubtitle
                    ? SurvivalUiTheme.TextPrimary
                    : isHeading ? SurvivalUiTheme.Accent : isMuted ? SurvivalUiTheme.TextMuted : SurvivalUiTheme.TextPrimary;
            text.fontStyle = isHeading && !isGameSubtitle ? FontStyle.Bold : text.fontStyle;

            // 어두운 질감 위에서도 한글 획이 사라지지 않도록 모든 Text에 짧고 진한 그림자를 한 개만 둡니다.
            Shadow shadow = text.GetComponent<Shadow>();
            if (shadow == null) shadow = text.gameObject.AddComponent<Shadow>();
            shadow.effectColor = new Color(0f, 0f, 0f, 0.82f);
            shadow.effectDistance = new Vector2(1.25f, -1.25f);
            shadow.useGraphicAlpha = true;
        }

        private static void ApplyRuntimeStateColors(GameObject root)
        {
            foreach (InventoryItemView view in root.GetComponentsInChildren<InventoryItemView>(true))
            {
                SetSerializedColor(view, "normalColor", SurvivalUiTheme.ItemCommon);
                SetSerializedColor(view, "selectedColor", SurvivalUiTheme.ItemSelected);
                SetSerializedColor(view, "dragColor", SurvivalUiTheme.ItemDragging);
            }

            foreach (EquipmentSlotView view in root.GetComponentsInChildren<EquipmentSlotView>(true))
            {
                SetSerializedColor(view, "normalColor", SurvivalUiTheme.EquipmentEmpty);
                SetSerializedColor(view, "occupiedColor", SurvivalUiTheme.EquipmentOccupied);
                SetSerializedColor(view, "activeWeaponColor", SurvivalUiTheme.EquipmentActive);
            }

            foreach (ExtractionHud hud in root.GetComponentsInChildren<ExtractionHud>(true))
            {
                SetSerializedColor(hud, "lockedColor", SurvivalUiTheme.Danger);
                SetSerializedColor(hud, "availableColor", SurvivalUiTheme.Success);
                SetSerializedColor(hud, "succeededColor", SurvivalUiTheme.Accent);
            }

            foreach (RaidResultHud hud in root.GetComponentsInChildren<RaidResultHud>(true))
            {
                SetSerializedColor(hud, "successColor", SurvivalUiTheme.Success);
                SetSerializedColor(hud, "failureColor", SurvivalUiTheme.Danger);
            }

            foreach (PlayerDamageHud hud in root.GetComponentsInChildren<PlayerDamageHud>(true))
                SetSerializedColor(hud, "damageColor", SurvivalUiTheme.Danger);
        }

        private static void SetSerializedColor(UnityEngine.Object target, string propertyName, Color value)
        {
            SerializedObject serialized = new SerializedObject(target);
            SerializedProperty property = serialized.FindProperty(propertyName);
            if (property == null) return;
            property.colorValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static bool IsMajorPanel(string objectName)
        {
            return objectName == "MainMenuPanel" || objectName == "StartPanel" || objectName == "InventoryPanel" ||
                   objectName == "MissionPanel" || objectName == "UpgradePanel" ||
                   objectName == "TraderPanel" || objectName == "DeploymentPanel" ||
                   objectName == "RaidResultPanel" || objectName == "ExtractionPanel" ||
                   objectName == "PromptPanel" || objectName == "TutorialPanel" ||
                   objectName == "ItemDetailPanel" || objectName == "PauseMainPanel" ||
                   objectName == "SettingsPanel" || objectName == "ConfirmationPanel";
        }

        private static bool LooksLikeSecondarySurface(string objectName)
        {
            return objectName.IndexOf("Panel", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   objectName.IndexOf("Area", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   objectName.IndexOf("Bar", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   objectName.IndexOf("Frame", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool IsArtworkImage(string objectName)
        {
            return objectName.IndexOf("Icon", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   objectName.IndexOf("Art", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   objectName.IndexOf("Portrait", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   objectName.IndexOf("Preview", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   objectName.IndexOf("Visual", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool IsPrimaryAction(string objectName)
        {
            return objectName.IndexOf("Start", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   objectName.IndexOf("Enter", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   objectName.IndexOf("Launch", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   objectName.IndexOf("Accept", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   objectName.IndexOf("Upgrade", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   objectName.IndexOf("Craft", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   objectName.IndexOf("Buy", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   objectName.IndexOf("Sell", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   objectName.IndexOf("Sort", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static void AddOrUpdateOutline(GameObject target, Color color, Vector2 distance)
        {
            Outline outline = target.GetComponent<Outline>();
            if (outline == null) outline = target.AddComponent<Outline>();
            outline.effectColor = color;
            outline.effectDistance = distance;
            outline.useGraphicAlpha = true;
        }

        private static void AddOrUpdateAccentStrip(
            Transform parent,
            string objectName,
            bool horizontal,
            Color color,
            float thickness)
        {
            Transform existing = parent.Find(objectName);
            GameObject stripObject;
            if (existing != null)
            {
                stripObject = existing.gameObject;
            }
            else
            {
                stripObject = new GameObject(objectName, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
                stripObject.transform.SetParent(parent, false);
            }

            RectTransform rect = stripObject.GetComponent<RectTransform>();
            Image image = stripObject.GetComponent<Image>();
            image.sprite = null;
            image.color = color;
            image.raycastTarget = false;

            if (horizontal)
            {
                rect.anchorMin = new Vector2(0f, 1f);
                rect.anchorMax = new Vector2(1f, 1f);
                rect.pivot = new Vector2(0.5f, 1f);
                rect.offsetMin = new Vector2(0f, -thickness);
                rect.offsetMax = Vector2.zero;
            }
            else
            {
                rect.anchorMin = new Vector2(0f, 0f);
                rect.anchorMax = new Vector2(0f, 1f);
                rect.pivot = new Vector2(0f, 0.5f);
                rect.offsetMin = Vector2.zero;
                rect.offsetMax = new Vector2(thickness, 0f);
            }

            // 장식 선이 본문과 버튼 위를 덮지 않도록 패널의 첫 번째 자식으로 보냅니다.
            stripObject.transform.SetAsFirstSibling();
        }

        private static void RemoveThemeDecoration(Transform parent, string objectName)
        {
            Transform existing = parent.Find(objectName);
            if (existing != null) UnityEngine.Object.DestroyImmediate(existing.gameObject);
        }
    }
}
#endif
