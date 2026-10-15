#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using ZombieSurvival.Combat;
using ZombieSurvival.Items;

namespace ZombieSurvival.EditorTools
{
    // ItemDefinition 에셋을 선택했을 때 기본 숫자 배열 대신 클릭 가능한 아이템 모양 격자를 표시합니다.
    // 이 클래스는 UnityEditor 전용 어셈블리에 있으므로 빌드된 게임에는 포함되지 않고 에셋 편집 과정에서만 실행됩니다.
    [CustomEditor(typeof(ItemDefinition))]
    public sealed class ItemDefinitionEditor : UnityEditor.Editor
    {
        private const float CellSize = 30f;
        private SerializedProperty gridWidth;
        private SerializedProperty gridHeight;
        private SerializedProperty gridShape;
        private SerializedProperty itemType;
        private SerializedProperty ammoType;
        private SerializedProperty weaponDefinition;
        private SerializedProperty meleeWeaponDefinition;
        private SerializedProperty statusEffectDefinition;
        private SerializedProperty armorDamageReduction;
        private SerializedProperty armorMaxDurability;
        private SerializedProperty quickSlotBonus;
        private SerializedProperty healthRestoreAmount;
        private SerializedProperty satietyRestoreAmount;

        private void OnEnable()
        {
            // 문자열 이름은 ItemDefinition의 private SerializeField와 연결됩니다.
            // SerializedProperty를 사용하면 다중 선택, Undo와 에셋 저장을 Unity 표준 방식으로 처리할 수 있습니다.
            gridWidth = serializedObject.FindProperty("gridWidth");
            gridHeight = serializedObject.FindProperty("gridHeight");
            gridShape = serializedObject.FindProperty("gridShape");
            itemType = serializedObject.FindProperty("itemType");
            ammoType = serializedObject.FindProperty("ammoType");
            weaponDefinition = serializedObject.FindProperty("weaponDefinition");
            meleeWeaponDefinition = serializedObject.FindProperty("meleeWeaponDefinition");
            statusEffectDefinition = serializedObject.FindProperty("statusEffectDefinition");
            armorDamageReduction = serializedObject.FindProperty("armorDamageReduction");
            armorMaxDurability = serializedObject.FindProperty("armorMaxDurability");
            quickSlotBonus = serializedObject.FindProperty("quickSlotBonus");
            healthRestoreAmount = serializedObject.FindProperty("healthRestoreAmount");
            satietyRestoreAmount = serializedObject.FindProperty("satietyRestoreAmount");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            // Script 참조는 어떤 컴포넌트의 에셋인지 보여 주기만 하고 실수로 교체하지 못하도록 읽기 전용으로 표시합니다.
            using (new EditorGUI.DisabledScope(true))
                EditorGUILayout.ObjectField("Script", MonoScript.FromScriptableObject((ItemDefinition)target), typeof(ItemDefinition), false);

            // 크기와 마스크는 아래의 전용 격자 UI가 담당하므로 나머지 아이템 속성만 기본 인스펙터 방식으로 그립니다.
            DrawPropertiesExcluding(
                serializedObject,
                "m_Script",
                "gridWidth",
                "gridHeight",
                "gridShape",
                "ammoType",
                "weaponDefinition",
                "meleeWeaponDefinition",
                "statusEffectDefinition",
                "armorDamageReduction",
                "armorMaxDurability",
                "quickSlotBonus",
                "healthRestoreAmount",
                "satietyRestoreAmount");

            // 무기가 아닌 아이템에는 불필요한 전투 데이터 입력칸을 숨기고 Weapon 종류에서만 총기·근접무기 연결 에셋을 보여 줍니다.
            // 두 참조 중 정확히 하나만 연결해야 하므로 비어 있거나 둘 다 연결된 잘못된 설정을 즉시 경고합니다.
            if ((ItemType)itemType.enumValueIndex == ItemType.Weapon)
            {
                EditorGUILayout.Space(6f);
                EditorGUILayout.PropertyField(weaponDefinition, new GUIContent("총기 전투 데이터"));
                EditorGUILayout.PropertyField(meleeWeaponDefinition, new GUIContent("근접무기 전투 데이터"));
                bool hasFirearm = weaponDefinition.objectReferenceValue != null;
                bool hasMelee = meleeWeaponDefinition.objectReferenceValue != null;
                if (!hasFirearm && !hasMelee)
                    EditorGUILayout.HelpBox("총기 또는 근접무기 전투 데이터 중 하나가 없으면 장착 무기로 선택할 수 없습니다.", MessageType.Warning);
                else if (hasFirearm && hasMelee)
                    EditorGUILayout.HelpBox("총기와 근접무기 데이터는 동시에 연결하지 말고 무기 종류에 맞는 하나만 남겨 주세요.", MessageType.Warning);
            }
            else if ((ItemType)itemType.enumValueIndex == ItemType.Ammo)
            {
                // 탄약 아이템에서만 탄종을 보여 주어 의료품이나 가방에 의미 없는 설정이 나타나지 않게 합니다.
                EditorGUILayout.Space(6f);
                EditorGUILayout.PropertyField(ammoType, new GUIContent("탄종"));
                if ((AmmoType)ammoType.enumValueIndex == AmmoType.None)
                    EditorGUILayout.HelpBox("탄종이 없음이면 어떤 무기도 이 탄약으로 재장전할 수 없습니다.", MessageType.Warning);
            }
            else if ((ItemType)itemType.enumValueIndex == ItemType.Armor)
            {
                // 방탄복에서만 피해 감소율과 최대 내구도를 보여 주어 다른 아이템에 의미 없는 수치가 섞이지 않게 합니다.
                EditorGUILayout.Space(6f);
                EditorGUILayout.PropertyField(armorDamageReduction, new GUIContent("피해 감소율"));
                EditorGUILayout.PropertyField(armorMaxDurability, new GUIContent("최대 내구도"));
                EditorGUILayout.PropertyField(quickSlotBonus, new GUIContent("추가 퀵슬롯 수"));
                if (armorDamageReduction.floatValue <= 0f || armorMaxDurability.floatValue <= 0f)
                    EditorGUILayout.HelpBox("감소율과 내구도가 0보다 커야 방탄 효과가 작동합니다.", MessageType.Warning);
            }
            else if ((ItemType)itemType.enumValueIndex == ItemType.Healing)
            {
                // 의료품을 선택했을 때만 체력 회복량을 보여 주어 음식의 포만감 수치와 혼동하지 않게 합니다.
                EditorGUILayout.Space(6f);
                EditorGUILayout.PropertyField(healthRestoreAmount, new GUIContent("체력 회복량"));
                if (healthRestoreAmount.floatValue <= 0f)
                    EditorGUILayout.HelpBox("체력 회복량이 0이면 사용해도 효과가 없어 아이템이 소비되지 않습니다.", MessageType.Warning);
            }
            else if ((ItemType)itemType.enumValueIndex == ItemType.Food)
            {
                // 음식 종류에서는 PlayerSurvival에 더할 포만감만 편집합니다.
                EditorGUILayout.Space(6f);
                EditorGUILayout.PropertyField(satietyRestoreAmount, new GUIContent("포만감 회복량"));
                if (satietyRestoreAmount.floatValue <= 0f)
                    EditorGUILayout.HelpBox("포만감 회복량이 0이면 사용해도 효과가 없어 아이템이 소비되지 않습니다.", MessageType.Warning);
            }
            else if ((ItemType)itemType.enumValueIndex == ItemType.BuffConsumable)
            {
                // 버프 소모품에서만 적용 효과 에셋을 노출해 일반 재료나 퀘스트 아이템에 불필요한 필드가 나타나지 않게 합니다.
                EditorGUILayout.Space(6f);
                EditorGUILayout.PropertyField(statusEffectDefinition, new GUIContent("시간제 효과 데이터"));
                if (statusEffectDefinition.objectReferenceValue == null)
                    EditorGUILayout.HelpBox("시간제 효과 데이터가 없으면 사용해도 효과를 적용할 수 없습니다.", MessageType.Warning);
            }

            EditorGUILayout.Space(12f);
            EditorGUILayout.LabelField("아이템 그리드 모양", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "가로·세로는 최대 7칸입니다. 초록색 셀만 실제 인벤토리 공간을 차지하며, 빈 셀에는 다른 아이템이 들어갈 수 있습니다.",
                MessageType.Info);

            int oldWidth = Mathf.Clamp(gridWidth.intValue, 1, ItemDefinition.MaximumGridSize);
            int oldHeight = Mathf.Clamp(gridHeight.intValue, 1, ItemDefinition.MaximumGridSize);

            // 이전 버전 에셋처럼 마스크가 없는 경우 현재 가로×세로 전체가 채워진 모양으로 한 번 초기화합니다.
            // 덕분에 기존 아이템의 크기와 배치 규칙은 에디터를 추가한 뒤에도 갑자기 변하지 않습니다.
            EnsureShapeMatchesDimensions(oldWidth, oldHeight);

            int newWidth = EditorGUILayout.IntSlider("가로 칸 수", oldWidth, 1, ItemDefinition.MaximumGridSize);
            int newHeight = EditorGUILayout.IntSlider("세로 칸 수", oldHeight, 1, ItemDefinition.MaximumGridSize);
            if (newWidth != oldWidth || newHeight != oldHeight)
                ResizeShape(oldWidth, oldHeight, newWidth, newHeight);

            DrawShapeGrid(newWidth, newHeight);
            DrawShapeButtons(newWidth, newHeight);

            // 위 UI에서 변경된 셀과 크기를 에셋에 기록하고 Unity의 Undo/저장 시스템에 변경 사실을 전달합니다.
            serializedObject.ApplyModifiedProperties();
        }

        // 배열 길이가 현재 가로×세로와 다르거나 선택 셀이 하나도 없으면 이전 사각형 규칙으로 초기화합니다.
        private void EnsureShapeMatchesDimensions(int width, int height)
        {
            bool hasSelectedCell = false;
            if (gridShape.arraySize == width * height)
            {
                for (int i = 0; i < gridShape.arraySize; i++)
                {
                    if (!gridShape.GetArrayElementAtIndex(i).boolValue) continue;
                    hasSelectedCell = true;
                    break;
                }
            }

            if (gridShape.arraySize == width * height && hasSelectedCell) return;

            gridShape.arraySize = width * height;
            for (int i = 0; i < gridShape.arraySize; i++)
                gridShape.GetArrayElementAtIndex(i).boolValue = true;
        }

        // 가로·세로 변경 전의 셀 값을 2차원 좌표로 읽어 새 배열의 겹치는 범위에 복사합니다.
        // 새로 늘어난 셀은 비워 두어 사용자가 원하는 부분만 직접 추가로 그릴 수 있게 합니다.
        private void ResizeShape(int oldWidth, int oldHeight, int newWidth, int newHeight)
        {
            var previous = new bool[oldWidth * oldHeight];
            for (int i = 0; i < previous.Length; i++)
                previous[i] = gridShape.GetArrayElementAtIndex(i).boolValue;

            gridWidth.intValue = newWidth;
            gridHeight.intValue = newHeight;
            gridShape.arraySize = newWidth * newHeight;

            bool keptSelectedCell = false;
            for (int y = 0; y < newHeight; y++)
            for (int x = 0; x < newWidth; x++)
            {
                bool value = x < oldWidth && y < oldHeight && previous[y * oldWidth + x];
                gridShape.GetArrayElementAtIndex(y * newWidth + x).boolValue = value;
                keptSelectedCell |= value;
            }

            // 크기를 줄이면서 선택된 부분이 전부 잘려 나갔다면 첫 칸을 켜서 유효한 모양을 유지합니다.
            if (!keptSelectedCell) gridShape.GetArrayElementAtIndex(0).boolValue = true;
        }

        // true 셀은 초록색, false 셀은 어두운색 버튼으로 표시하며 클릭할 때마다 상태를 반전합니다.
        private void DrawShapeGrid(int width, int height)
        {
            EditorGUILayout.Space(4f);
            Color previousBackground = GUI.backgroundColor;

            for (int y = 0; y < height; y++)
            {
                EditorGUILayout.BeginHorizontal();
                GUILayout.FlexibleSpace();
                for (int x = 0; x < width; x++)
                {
                    SerializedProperty cell = gridShape.GetArrayElementAtIndex(y * width + x);
                    bool selected = cell.boolValue;
                    GUI.backgroundColor = selected ? new Color(0.35f, 0.85f, 0.45f) : new Color(0.3f, 0.3f, 0.3f);

                    if (GUILayout.Button(selected ? "■" : string.Empty, GUILayout.Width(CellSize), GUILayout.Height(CellSize)))
                    {
                        // 마지막 한 칸을 끄면 아이템이 보이지 않고 클릭도 되지 않으므로 최소 한 칸은 항상 선택 상태로 둡니다.
                        if (!selected || CountSelectedCells() > 1) cell.boolValue = !selected;
                    }
                }
                GUILayout.FlexibleSpace();
                EditorGUILayout.EndHorizontal();
            }

            GUI.backgroundColor = previousBackground;
        }

        // 빠르게 기본 사각형으로 되돌리거나 복잡한 모양을 처음부터 다시 그릴 수 있는 보조 버튼입니다.
        private void DrawShapeButtons(int width, int height)
        {
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("전체 칸 채우기"))
            {
                for (int i = 0; i < width * height; i++)
                    gridShape.GetArrayElementAtIndex(i).boolValue = true;
            }

            if (GUILayout.Button("첫 칸만 남기기"))
            {
                for (int i = 0; i < width * height; i++)
                    gridShape.GetArrayElementAtIndex(i).boolValue = i == 0;
            }
            EditorGUILayout.EndHorizontal();
        }

        // 마지막 점유 셀을 끄려는 입력인지 판단할 수 있도록 현재 true인 마스크 셀의 개수를 계산합니다.
        private int CountSelectedCells()
        {
            int count = 0;
            for (int i = 0; i < gridShape.arraySize; i++)
                if (gridShape.GetArrayElementAtIndex(i).boolValue) count++;
            return count;
        }
    }
}
#endif
