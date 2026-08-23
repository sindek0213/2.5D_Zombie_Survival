using UnityEngine;

namespace ZombieSurvival.Items
{
    // 아이템의 용도를 구분하는 1차 프로토타입 분류입니다.
    public enum ItemType
    {
        Ammo,
        Healing,
        Food,
        Quest,
        Weapon,
        Armor,
        Container
    }

    // 아이템이 장착될 수 있는 캐릭터 장비 위치입니다.
    public enum EquipmentSlotType
    {
        None,
        PrimaryWeapon,
        SecondaryWeapon,
        Armor,
        Rig,
        Backpack
    }

    // 아이템의 이름, 크기와 사용 규칙처럼 모든 인스턴스가 공유하는 정적 데이터를 담는 ScriptableObject입니다.
    // 실제 보유 수량과 격자 위치는 InventoryEntry에 저장하며 이 에셋에는 플레이 도중 변하는 값을 넣지 않습니다.
    // 저장 데이터는 Unity 오브젝트 참조 대신 ItemId를 기록하고 로드할 때 해당 정의를 다시 찾는 구조로 확장할 수 있습니다.
    [CreateAssetMenu(menuName = "Zombie Survival/Item Definition")]
    public sealed class ItemDefinition : ScriptableObject
    {
        [SerializeField] private string itemId;
        [SerializeField] private string displayName;
        [SerializeField] private Sprite icon;
        [SerializeField] private ItemType itemType;
        [SerializeField, Min(1)] private int maxStack = 1;
        [SerializeField, Min(1)] private int gridWidth = 1;
        [SerializeField, Min(1)] private int gridHeight = 1;
        [SerializeField] private EquipmentSlotType equipmentSlot;
        [SerializeField, Min(0)] private int containerWidth;
        [SerializeField, Min(0)] private int containerHeight;
        [SerializeField] private bool usable;
        [SerializeField, TextArea] private string description;

        public string ItemId => itemId;
        public string DisplayName => displayName;
        public Sprite Icon => icon;
        public ItemType Type => itemType;
        public int MaxStack => Mathf.Max(1, maxStack);
        public int GridWidth => Mathf.Max(1, gridWidth);
        public int GridHeight => Mathf.Max(1, gridHeight);
        public EquipmentSlotType EquipmentSlot => equipmentSlot;
        public int ContainerWidth => Mathf.Max(0, containerWidth);
        public int ContainerHeight => Mathf.Max(0, containerHeight);
        public bool IsContainer => ContainerWidth > 0 && ContainerHeight > 0;
        public bool Usable => usable;
        public string Description => description;
    }
}
