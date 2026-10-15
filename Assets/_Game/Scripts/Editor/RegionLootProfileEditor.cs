#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using ZombieSurvival.Items;

namespace ZombieSurvival.EditorTools
{
    // RegionLootProfile의 중첩 배열을 기본 Element 0 표시 대신 출처별 카드와 품목별 카드로 편집하게 해 주는 전용 Inspector입니다.
    // 이 코드는 Editor 폴더에 있어 실제 게임 빌드에는 포함되지 않고 밸런스 작업 화면만 개선합니다.
    [CustomEditor(typeof(RegionLootProfile))]
    public sealed class RegionLootProfileEditor : UnityEditor.Editor
    {
        private SerializedProperty region;
        private SerializedProperty activeMissionChanceBonus;
        private SerializedProperty sourceTables;

        private void OnEnable()
        {
            region = serializedObject.FindProperty("region");
            activeMissionChanceBonus = serializedObject.FindProperty("activeMissionChanceBonus");
            sourceTables = serializedObject.FindProperty("sourceTables");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            EditorGUILayout.PropertyField(region, new GUIContent("지역", "이 드랍 표가 적용될 RaidRegionDefinition입니다."));
            EditorGUILayout.Slider(
                activeMissionChanceBonus,
                0f,
                1f,
                new GUIContent("임무 품목 추가 확률", "아이템 회수 임무 진행 중 목표 품목에 더할 확률입니다. 0.05는 5%p입니다."));
            EditorGUILayout.HelpBox(
                "회수 임무가 활성화되면 이 지역에 등록된 목표 아이템만 기본 확률에 위 값을 더합니다. " +
                "에셋 원본은 수정하지 않으므로 임무 완료·포기 시 즉시 원래 수치로 돌아갑니다.",
                MessageType.Info);

            EditorGUILayout.Space(8f);
            DrawSourceTables();

            serializedObject.ApplyModifiedProperties();
        }

        private void DrawSourceTables()
        {
            EditorGUILayout.LabelField("출처별 드랍표", EditorStyles.boldLabel);
            int requestedCount = Mathf.Max(0, EditorGUILayout.IntField("표 개수", sourceTables.arraySize));
            if (requestedCount != sourceTables.arraySize) sourceTables.arraySize = requestedCount;

            for (int tableIndex = 0; tableIndex < sourceTables.arraySize; tableIndex++)
            {
                SerializedProperty table = sourceTables.GetArrayElementAtIndex(tableIndex);
                SerializedProperty sourceType = table.FindPropertyRelative("sourceType");
                SerializedProperty sourceId = table.FindPropertyRelative("sourceId");
                SerializedProperty minimumRolls = table.FindPropertyRelative("minimumRolls");
                SerializedProperty maximumRolls = table.FindPropertyRelative("maximumRolls");
                SerializedProperty entries = table.FindPropertyRelative("entries");

                string typeName = sourceType.enumDisplayNames[sourceType.enumValueIndex];
                string id = string.IsNullOrWhiteSpace(sourceId.stringValue) ? "default" : sourceId.stringValue;

                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                EditorGUILayout.BeginHorizontal();
                table.isExpanded = EditorGUILayout.Foldout(
                    table.isExpanded,
                    $"{typeName} / {id}",
                    true,
                    EditorStyles.foldoutHeader);
                if (GUILayout.Button("삭제", GUILayout.Width(46f)))
                {
                    sourceTables.DeleteArrayElementAtIndex(tableIndex);
                    EditorGUILayout.EndHorizontal();
                    EditorGUILayout.EndVertical();
                    tableIndex--;
                    continue;
                }
                EditorGUILayout.EndHorizontal();

                if (table.isExpanded)
                {
                    EditorGUI.indentLevel++;
                    EditorGUILayout.PropertyField(sourceType, new GUIContent("출처 종류"));
                    EditorGUILayout.PropertyField(sourceId, new GUIContent("출처 ID", "상자 에셋 이름, 바닥 지점 ID 또는 좀비 유형 에셋 이름과 맞춥니다. *는 기본 표입니다."));

                    RegionLootSourceType selectedType = (RegionLootSourceType)sourceType.enumValueIndex;
                    if (selectedType == RegionLootSourceType.Monster)
                    {
                        EditorGUILayout.HelpBox(
                            "Monster는 모든 아이템 줄의 확률을 각각 굴립니다. 추첨 횟수는 사용하지 않습니다.",
                            MessageType.None);
                    }
                    else
                    {
                        EditorGUILayout.PropertyField(minimumRolls, new GUIContent("최소 추첨 횟수"));
                        EditorGUILayout.PropertyField(maximumRolls, new GUIContent("최대 추첨 횟수"));
                        EditorGUILayout.HelpBox(
                            "Container와 Ground는 가중치로 후보를 고른 뒤 그 품목의 드랍 확률을 굴립니다.",
                            MessageType.None);
                    }

                    DrawEntries(entries);
                    EditorGUI.indentLevel--;
                }
                EditorGUILayout.EndVertical();
                EditorGUILayout.Space(3f);
            }

            if (GUILayout.Button("출처 표 추가")) AddSourceTable();
        }

        private static void DrawEntries(SerializedProperty entries)
        {
            EditorGUILayout.Space(4f);
            EditorGUILayout.LabelField("아이템별 수치", EditorStyles.boldLabel);
            int requestedCount = Mathf.Max(0, EditorGUILayout.IntField("아이템 개수", entries.arraySize));
            if (requestedCount != entries.arraySize) entries.arraySize = requestedCount;

            for (int entryIndex = 0; entryIndex < entries.arraySize; entryIndex++)
            {
                SerializedProperty entry = entries.GetArrayElementAtIndex(entryIndex);
                SerializedProperty item = entry.FindPropertyRelative("item");
                SerializedProperty chance = entry.FindPropertyRelative("dropChance");
                SerializedProperty weight = entry.FindPropertyRelative("weight");
                SerializedProperty minimum = entry.FindPropertyRelative("minimumQuantity");
                SerializedProperty maximum = entry.FindPropertyRelative("maximumQuantity");

                string itemName = item.objectReferenceValue != null ? item.objectReferenceValue.name : "아이템 미지정";
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField(itemName, EditorStyles.boldLabel);
                if (GUILayout.Button("삭제", GUILayout.Width(46f)))
                {
                    entries.DeleteArrayElementAtIndex(entryIndex);
                    EditorGUILayout.EndHorizontal();
                    EditorGUILayout.EndVertical();
                    entryIndex--;
                    continue;
                }
                EditorGUILayout.EndHorizontal();

                EditorGUILayout.PropertyField(item, new GUIContent("아이템"));
                EditorGUILayout.Slider(chance, 0f, 1f, new GUIContent("드랍 확률", "0.05는 5%, 1은 100%입니다."));
                EditorGUILayout.PropertyField(weight, new GUIContent("선택 가중치", "Container와 Ground의 후보 선택 비율입니다. Monster에서는 사용하지 않습니다."));
                EditorGUILayout.PropertyField(minimum, new GUIContent("최소 수량"));
                EditorGUILayout.PropertyField(maximum, new GUIContent("최대 수량"));
                EditorGUILayout.EndVertical();
            }

            if (GUILayout.Button("아이템 항목 추가")) AddEntry(entries);
        }

        // Unity 배열은 마지막 위치에 새 칸을 넣을 때 이전 칸을 복제할 수 있으므로 새 표의 모든 필드를 명시적으로 초기화합니다.
        // 이렇게 해야 사용자가 표 추가 버튼을 누른 뒤 기존 지역 표를 실수로 같은 ID로 복제하지 않습니다.
        private void AddSourceTable()
        {
            int newIndex = sourceTables.arraySize;
            sourceTables.InsertArrayElementAtIndex(newIndex);
            SerializedProperty table = sourceTables.GetArrayElementAtIndex(newIndex);
            table.FindPropertyRelative("sourceType").enumValueIndex = (int)RegionLootSourceType.Container;
            table.FindPropertyRelative("sourceId").stringValue = "new_source";
            table.FindPropertyRelative("minimumRolls").intValue = 1;
            table.FindPropertyRelative("maximumRolls").intValue = 1;
            table.FindPropertyRelative("entries").arraySize = 0;
            table.isExpanded = true;
        }

        // 새 아이템 줄도 이전 값 복제를 지우고 100%·가중치 1·수량 1이라는 읽기 쉬운 기본값으로 시작합니다.
        private static void AddEntry(SerializedProperty entries)
        {
            int newIndex = entries.arraySize;
            entries.InsertArrayElementAtIndex(newIndex);
            SerializedProperty entry = entries.GetArrayElementAtIndex(newIndex);
            entry.FindPropertyRelative("item").objectReferenceValue = null;
            entry.FindPropertyRelative("dropChance").floatValue = 1f;
            entry.FindPropertyRelative("weight").floatValue = 1f;
            entry.FindPropertyRelative("minimumQuantity").intValue = 1;
            entry.FindPropertyRelative("maximumQuantity").intValue = 1;
        }
    }
}
#endif
