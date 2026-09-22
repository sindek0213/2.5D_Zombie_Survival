using UnityEngine;

namespace ZombieSurvival.Items
{
    // 필드 상자가 처음 수색될 때 LootTableDefinition을 굴려 실제 격자 아이템을 만드는 컴포넌트입니다.
    // 생성 여부를 기억하므로 상자를 닫았다 다시 열어도 전리품이 복제되지 않고 이미 가져간 아이템도 다시 생기지 않습니다.
    [RequireComponent(typeof(WorldLootContainer))]
    public sealed class FieldLootSource : MonoBehaviour
    {
        [SerializeField] private LootTableDefinition lootTable;
        [SerializeField] private bool populateOnStart;

        private WorldLootContainer container;

        public LootTableDefinition LootTable => lootTable;
        public bool HasPopulated { get; private set; }
        public bool HasPendingLoot => !HasPopulated && lootTable != null;

        private void Awake()
        {
            container = GetComponent<WorldLootContainer>();
            container.SetLootSource(this);
        }

        private void Start()
        {
            // 기본값은 첫 E키 수색 때 생성하는 지연 방식입니다.
            // 테스트용으로 미리 채워진 상자가 필요할 때만 populateOnStart를 켜서 시작 프레임에 생성할 수 있습니다.
            if (populateOnStart) PopulateIfNeeded();
        }

        // 에디터 생성기가 상자와 전리품 표를 연결할 때 사용합니다.
        // Awake보다 뒤에 호출되는 경우도 있으므로 컨테이너 참조와 수색 상태 연결을 여기서도 보장합니다.
        public void Configure(LootTableDefinition definition, bool generateOnStart = false)
        {
            lootTable = definition;
            populateOnStart = generateOnStart;
            if (container == null) container = GetComponent<WorldLootContainer>();
            container?.SetLootSource(this);
        }

        // 아직 생성하지 않은 상자만 한 번 채우고 실제 생성이 실행됐으면 true를 반환합니다.
        // HasPopulated를 추첨 전에 먼저 바꿔 같은 프레임의 중복 상호작용이나 재진입에서도 두 번 생성되지 않게 합니다.
        public bool PopulateIfNeeded()
        {
            if (HasPopulated || lootTable == null) return false;
            if (container == null) container = GetComponent<WorldLootContainer>();
            if (container == null) return false;

            HasPopulated = true;
            int rollCount = LootTableDefinition.CalculateRollCount(
                lootTable.MinimumRolls,
                lootTable.MaximumRolls,
                Random.value);

            for (int i = 0; i < rollCount; i++)
            {
                int entryIndex = LootTableDefinition.SelectWeightedEntryIndex(
                    lootTable.Entries,
                    Random.value);
                if (entryIndex < 0) continue;

                LootTableEntry entry = lootTable.Entries[entryIndex];
                int quantity = LootTableDefinition.CalculateQuantity(
                    entry.minimumQuantity,
                    entry.maximumQuantity,
                    Random.value);

                // 격자에 전체 수량이 들어갈 수 있을 때만 추가합니다.
                // 선택된 큰 아이템이 공간을 넘으면 일부만 억지로 넣지 않고 그 추첨 결과 하나를 건너뜁니다.
                container.TryAdd(entry.item, quantity);
            }

            return true;
        }
    }
}
