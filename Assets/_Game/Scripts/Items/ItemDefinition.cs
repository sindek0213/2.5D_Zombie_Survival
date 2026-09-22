using UnityEngine;
using ZombieSurvival.Combat;

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
        Container,
        // 시설 강화와 제작에 사용하는 자원입니다. 기존 저장의 enum 숫자를 유지하기 위해 마지막에 추가했습니다.
        Material
    }

    // 파밍 아이템의 가치와 발견 난이도를 네 단계로 구분합니다.
    // enum 순서는 저장된 숫자와 연결될 수 있으므로 새 등급이 필요하면 기존 항목 사이가 아니라 마지막에 추가해야 합니다.
    public enum ItemRarity
    {
        Common,
        Uncommon,
        Rare,
        Quest
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
    // 저장 데이터는 Unity 오브젝트 참조 대신 ItemId를 기록하고, 불러올 때 ItemCatalog에서 해당 정의를 다시 찾습니다.
    [CreateAssetMenu(menuName = "Zombie Survival/Item Definition")]
    public sealed class ItemDefinition : ScriptableObject
    {
        // 인스펙터의 아이템 모양 편집기와 런타임 배치 검사가 함께 사용하는 최대 한 변 크기입니다.
        // 한 곳에 상수로 두면 에디터는 7칸인데 런타임은 더 큰 값을 허용하는 식의 규칙 불일치를 막을 수 있습니다.
        public const int MaximumGridSize = 7;

        [SerializeField] private string itemId;
        [SerializeField] private string displayName;
        [SerializeField] private Sprite icon;
        [SerializeField] private ItemType itemType;
        [SerializeField] private ItemRarity rarity;
        // Ammo 아이템이 어떤 무기에 들어가는지 나타냅니다.
        // 무기의 WeaponDefinition.Caliber와 이 값이 같을 때만 재장전 과정에서 소비할 수 있습니다.
        [SerializeField] private AmmoType ammoType;
        [SerializeField, Min(1)] private int maxStack = 1;
        [SerializeField, Min(1)] private int gridWidth = 1;
        [SerializeField, Min(1)] private int gridHeight = 1;

        // gridShape은 왼쪽 위부터 오른쪽으로 읽는 1차원 배열이며 true인 셀만 실제 인벤토리 공간을 차지합니다.
        // HideInInspector를 사용한 이유는 기본 배열 UI 대신 ItemDefinitionEditor의 클릭 가능한 격자로 편집하기 위해서입니다.
        // 기존 에셋에는 이 배열이 없으므로 비어 있을 수 있으며, 그런 경우에는 이전 방식처럼 전체 사각형으로 처리합니다.
        [SerializeField, HideInInspector] private bool[] gridShape = new bool[0];
        [SerializeField] private EquipmentSlotType equipmentSlot;

        // Weapon 아이템이 장착됐을 때 적용할 피해량, 연사 속도와 탄창 수치는 별도 WeaponDefinition 에셋에서 읽습니다.
        // 인벤토리 모양과 전투 밸런스를 서로 다른 에셋에 나누면 한쪽을 수정할 때 다른 규칙을 실수로 건드리지 않습니다.
        [SerializeField] private WeaponDefinition weaponDefinition;
        // Armor 아이템이 막아 주는 피해 비율과 새 방탄복의 최대 내구도입니다.
        // 예를 들어 감소율 0.4는 들어온 피해의 40%를 내구도가 허용하는 범위에서 흡수한다는 뜻입니다.
        [SerializeField, Range(0f, 1f)] private float armorDamageReduction;
        [SerializeField, Min(0f)] private float armorMaxDurability;
        [SerializeField, Min(0)] private int containerWidth;
        [SerializeField, Min(0)] private int containerHeight;
        // 소비 효과를 코드에 고정하지 않고 아이템 에셋마다 조절할 수 있도록 실제 회복량을 정의에 저장합니다.
        // 의료품은 healthRestoreAmount, 음식은 satietyRestoreAmount만 사용하며 다른 종류에서는 0으로 읽힙니다.
        [SerializeField, Min(0f)] private float healthRestoreAmount;
        [SerializeField, Min(0f)] private float satietyRestoreAmount;
        [SerializeField] private bool usable;
        // 상점과 밸런스 표가 한 개당 기본 가치를 계산할 때 사용하는 정수 값입니다.
        // 실제 판매 시설이 추가되기 전에도 드롭·제작 비용의 상대 가치를 비교할 수 있습니다.
        [SerializeField, Min(0)] private int baseValue = 10;
        [SerializeField, TextArea] private string description;

        public string ItemId => itemId;
        public string DisplayName => displayName;
        public Sprite Icon => icon;
        public ItemType Type => itemType;
        public ItemRarity Rarity => rarity;
        public AmmoType Caliber => itemType == ItemType.Ammo ? ammoType : AmmoType.None;
        // 탄창, 방탄 내구도 또는 내부 격자를 가진 아이템은 각 InventoryEntry마다 서로 다른 실행 상태를 보존해야 합니다.
        // 이런 아이템을 한 묶음으로 합치면 어느 탄창·내구도·내용물을 남길지 결정할 수 없으므로 코드에서도 중첩을 금지합니다.
        public bool CanStack => itemType != ItemType.Weapon && itemType != ItemType.Armor && !IsContainer;
        public int MaxStack => CanStack ? Mathf.Max(1, maxStack) : 1;
        public int GridWidth => Mathf.Clamp(gridWidth, 1, MaximumGridSize);
        public int GridHeight => Mathf.Clamp(gridHeight, 1, MaximumGridSize);
        public EquipmentSlotType EquipmentSlot => equipmentSlot;
        public WeaponDefinition WeaponDefinition => itemType == ItemType.Weapon ? weaponDefinition : null;
        public float ArmorDamageReduction => itemType == ItemType.Armor ? Mathf.Clamp01(armorDamageReduction) : 0f;
        public float ArmorMaxDurability => itemType == ItemType.Armor ? Mathf.Max(0f, armorMaxDurability) : 0f;
        public int ContainerWidth => Mathf.Max(0, containerWidth);
        public int ContainerHeight => Mathf.Max(0, containerHeight);
        public float HealthRestoreAmount => itemType == ItemType.Healing ? Mathf.Max(0f, healthRestoreAmount) : 0f;
        public float SatietyRestoreAmount => itemType == ItemType.Food ? Mathf.Max(0f, satietyRestoreAmount) : 0f;
        public bool IsContainer => ContainerWidth > 0 && ContainerHeight > 0;
        public bool Usable => usable;
        public int BaseValue => Mathf.Max(0, baseValue);
        public string Description => description;

        // localX와 localY는 아이템 바운딩 박스 안의 셀 좌표입니다.
        // rotated가 true이면 화면에서 시계 방향으로 90도 회전한 좌표를 원본 마스크 좌표로 되돌린 뒤 저장된 값을 읽습니다.
        public bool IsGridCellOccupied(int localX, int localY, bool rotated)
        {
            int width = rotated ? GridHeight : GridWidth;
            int height = rotated ? GridWidth : GridHeight;
            if (localX < 0 || localY < 0 || localX >= width || localY >= height) return false;

            int sourceX = rotated ? localY : localX;
            int sourceY = rotated ? GridHeight - 1 - localX : localY;

            // 이전 버전에서 만든 아이템은 gridShape 데이터가 없으므로 전체 사각형을 점유한 것으로 읽습니다.
            // 배열 크기가 맞더라도 모든 셀이 false이면 보이지도 클릭되지도 않는 아이템이 생기므로 동일하게 사각형으로 복구합니다.
            if (!HasValidGridShape()) return true;
            return gridShape[sourceY * GridWidth + sourceX];
        }

        // 커스텀 모양은 현재 가로×세로와 배열 길이가 일치하고 최소 한 칸이 선택되어 있을 때만 유효합니다.
        // 최대 49칸만 검사하므로 배치 중 호출되어도 성능 부담이 거의 없습니다.
        private bool HasValidGridShape()
        {
            if (gridShape == null || gridShape.Length != GridWidth * GridHeight) return false;
            for (int i = 0; i < gridShape.Length; i++)
                if (gridShape[i]) return true;
            return false;
        }

        // 스크립트나 기본 인스펙터를 통해 7보다 큰 값이 들어와도 에셋에 허용 범위만 저장되도록 보정합니다.
        // gridShape 크기 변경과 셀 보존은 전용 에디터가 담당하며, 기존 에셋 호환을 위해 여기서 배열을 강제로 만들지는 않습니다.
        private void OnValidate()
        {
            gridWidth = Mathf.Clamp(gridWidth, 1, MaximumGridSize);
            gridHeight = Mathf.Clamp(gridHeight, 1, MaximumGridSize);
            // Inspector에 이전 값이 남아 있어도 개별 상태를 가진 장비가 중첩 가능한 에셋으로 저장되지 않도록 즉시 바로잡습니다.
            // 일반 탄약·의료품·식량은 입력한 maxStack을 그대로 사용하되 최소 한 개는 보장합니다.
            bool hasIndividualState = itemType == ItemType.Weapon ||
                                      itemType == ItemType.Armor ||
                                      (containerWidth > 0 && containerHeight > 0);
            maxStack = hasIndividualState ? 1 : Mathf.Max(1, maxStack);
            armorDamageReduction = Mathf.Clamp01(armorDamageReduction);
            armorMaxDurability = Mathf.Max(0f, armorMaxDurability);
            healthRestoreAmount = Mathf.Max(0f, healthRestoreAmount);
            satietyRestoreAmount = Mathf.Max(0f, satietyRestoreAmount);
            baseValue = Mathf.Max(0, baseValue);
        }
    }
}
