#if UNITY_EDITOR
using UnityEngine;
using ZombieSurvival.Core;

namespace ZombieSurvival.EditorTools
{
    // 학교에 직접 놓을 아이템 한 묶음의 ID, 위치, 수량과 임시 표시색입니다.
    // PrototypeContentBuilder 안의 긴 함수에서 배열 번호를 사용하지 않고 ItemCatalog의 안정적인 ItemId로 찾게 합니다.
    internal readonly struct SchoolWorldItemPlacement
    {
        public readonly string ObjectName;
        public readonly string ItemId;
        public readonly Vector3 Position;
        public readonly int Quantity;
        public readonly Color Color;

        public SchoolWorldItemPlacement(string objectName, string itemId, Vector3 position, int quantity, Color color)
        {
            ObjectName = objectName;
            ItemId = itemId;
            Position = position;
            Quantity = quantity;
            Color = color;
        }
    }

    // 학교의 단단한 수색 상자 한 개를 설명합니다. LootTableIndex는 0 보급, 1 무기, 2 은닉물 순서입니다.
    internal readonly struct SchoolLootBoxPlacement
    {
        public readonly string ObjectName;
        public readonly Vector3 Position;
        public readonly int GridWidth;
        public readonly int GridHeight;
        public readonly int LootTableIndex;
        public readonly Color Color;

        public SchoolLootBoxPlacement(
            string objectName,
            Vector3 position,
            int gridWidth,
            int gridHeight,
            int lootTableIndex,
            Color color)
        {
            ObjectName = objectName;
            Position = position;
            GridWidth = gridWidth;
            GridHeight = gridHeight;
            LootTableIndex = lootTableIndex;
            Color = color;
        }
    }

    // 학교의 파밍 위치를 지형 생성 코드와 분리한 읽기 전용 배치표입니다.
    // 위치나 수량을 조절할 때 벽·카메라·레이드 종료 코드를 훑지 않고 이 파일의 표만 확인하면 됩니다.
    internal static class SchoolRaidContentLayout
    {
        internal static readonly SchoolWorldItemPlacement[] WorldItems =
        {
            new SchoolWorldItemPlacement("School_Entrance_Ammo", "ammo_9mm", new Vector3(-3f, 0.3f, -34f), 12, new Color(0.85f, 0.72f, 0.2f)),
            new SchoolWorldItemPlacement("School_Nurse_Medkit", "medkit_small", new Vector3(-28f, 0.3f, -21f), 1, new Color(0.85f, 0.2f, 0.2f)),
            new SchoolWorldItemPlacement("School_Cafeteria_Food", "food_canned", new Vector3(22f, 0.3f, -21f), 2, new Color(0.85f, 0.48f, 0.18f)),
            new SchoolWorldItemPlacement("School_Broadcast_Radio", "quest_radio", new Vector3(-25f, 0.3f, 22f), 1, new Color(0.25f, 0.65f, 1f)),
            new SchoolWorldItemPlacement("School_Gym_Rifle", "weapon_rifle", new Vector3(25f, 0.3f, 9f), 1, new Color(0.3f, 0.3f, 0.3f)),
            new SchoolWorldItemPlacement("School_Office_Pistol", "weapon_pistol", new Vector3(3f, 0.3f, -23f), 1, new Color(0.25f, 0.25f, 0.25f)),
            new SchoolWorldItemPlacement("School_Classroom_Armor", "armor_light", new Vector3(-28f, 0.3f, 9f), 1, new Color(0.25f, 0.45f, 0.55f)),
            new SchoolWorldItemPlacement("School_Kitchen_Rig", "rig_small", new Vector3(31f, 0.3f, -20f), 1, new Color(0.35f, 0.5f, 0.3f)),
            new SchoolWorldItemPlacement("School_Courtyard_Backpack", "backpack_small", new Vector3(-7f, 0.3f, 7f), 1, new Color(0.3f, 0.4f, 0.3f)),
            new SchoolWorldItemPlacement("School_Science_556", "ammo_556", new Vector3(-9f, 0.3f, 22f), 30, new Color(0.72f, 0.58f, 0.18f)),
            new SchoolWorldItemPlacement("School_Gym_12G", "ammo_12g", new Vector3(31f, 0.3f, 10f), 12, new Color(0.72f, 0.38f, 0.16f)),
            new SchoolWorldItemPlacement("School_Classroom_Smg", "weapon_smg", new Vector3(-29f, 0.3f, -8f), 1, new Color(0.18f, 0.24f, 0.28f)),
            new SchoolWorldItemPlacement("School_Gym_Shotgun", "weapon_shotgun", new Vector3(23f, 0.3f, -6f), 1, new Color(0.35f, 0.2f, 0.16f))
        };

        internal static readonly SchoolLootBoxPlacement[] LootBoxes =
        {
            new SchoolLootBoxPlacement("School_NurseOffice_Supply", new Vector3(-35f, 0f, -18f), 6, 4, 0, new Color(0.35f, 0.5f, 0.28f)),
            new SchoolLootBoxPlacement("School_Cafeteria_Supply", new Vector3(27f, 0f, -24f), 7, 4, 0, new Color(0.35f, 0.5f, 0.28f)),
            new SchoolLootBoxPlacement("School_Library_Hidden", new Vector3(-34f, 0f, 26f), 6, 4, 2, new Color(0.42f, 0.34f, 0.2f)),
            new SchoolLootBoxPlacement("School_ScienceLab_Hidden", new Vector3(-14f, 0f, 26f), 6, 4, 2, new Color(0.42f, 0.34f, 0.2f)),
            new SchoolLootBoxPlacement("School_Gym_Weapon", new Vector3(22f, 0f, 10f), 8, 5, 1, new Color(0.28f, 0.32f, 0.38f)),
            new SchoolLootBoxPlacement("School_Classroom2F_Weapon", new Vector3(-34f, SchoolRaidMapLayout.ClassroomSecondFloorHeight, 2f), 8, 5, 1, new Color(0.28f, 0.32f, 0.38f)),
            new SchoolLootBoxPlacement("School_Playground_Emergency", new Vector3(24f, 0f, 34f), 6, 4, 0, new Color(0.35f, 0.5f, 0.28f))
        };
    }
}
#endif
