using UnityEngine;

namespace ZombieSurvival.Items
{
    // 필드 상자가 처음 수색될 때 LootTableDefinition을 굴려 실제 격자 아이템을 만드는 컴포넌트입니다.
    // 생성 여부를 기억하므로 상자를 닫았다 다시 열어도 전리품이 복제되지 않고 이미 가져간 아이템도 다시 생기지 않습니다.
    [RequireComponent(typeof(WorldLootContainer))]
    public sealed class FieldLootSource : MonoBehaviour
    {
        [SerializeField] private LootTableDefinition lootTable;
        // 지역 프로필 안에서 이 상자가 사용할 표를 찾는 안정적인 ID입니다.
        // 비어 있으면 기존 LootTable 에셋 이름을 자동 사용하므로 예전 씬도 별도 수정 없이 동작합니다.
        [SerializeField] private string regionSourceId;
        [SerializeField] private bool populateOnStart;
        [SerializeField, Range(1, 5)] private int dangerRating = 1;

        private WorldLootContainer container;

        public LootTableDefinition LootTable => lootTable;
        public string RegionSourceId => string.IsNullOrWhiteSpace(regionSourceId)
            ? RegionLootSourceTable.NormalizeSourceId(lootTable != null ? lootTable.name : "default")
            : RegionLootSourceTable.NormalizeSourceId(regionSourceId);
        public bool HasPopulated { get; private set; }
        public bool HasPendingLoot => !HasPopulated &&
                                      (ResolveRegionTable(out _, out _) || lootTable != null);
        public int DangerRating => Mathf.Clamp(dangerRating, 1, 5);

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
        public void Configure(
            LootTableDefinition definition,
            bool generateOnStart = false,
            int areaDangerRating = 1,
            string sourceId = null)
        {
            lootTable = definition;
            regionSourceId = string.IsNullOrWhiteSpace(sourceId)
                ? RegionLootSourceTable.NormalizeSourceId(definition != null ? definition.name : "default")
                : RegionLootSourceTable.NormalizeSourceId(sourceId);
            populateOnStart = generateOnStart;
            dangerRating = Mathf.Clamp(areaDangerRating, 1, 5);
            if (container == null) container = GetComponent<WorldLootContainer>();
            container?.SetLootSource(this);
        }

        // 아직 생성하지 않은 상자만 한 번 채우고 실제 생성이 실행됐으면 true를 반환합니다.
        // HasPopulated를 추첨 전에 먼저 바꿔 같은 프레임의 중복 상호작용이나 재진입에서도 두 번 생성되지 않게 합니다.
        public bool PopulateIfNeeded()
        {
            if (HasPopulated) return false;
            if (container == null) container = GetComponent<WorldLootContainer>();
            if (container == null) return false;

            // 현재 레이드에 이 상자 ID와 일치하는 지역 표가 있으면 그 표가 우선권을 가집니다.
            // 일치하는 지역 표가 없는 예전 씬과 테스트에서는 바로 아래의 기존 LootTableDefinition 경로를 계속 사용합니다.
            if (ResolveRegionTable(out RegionLootProfile regionProfile, out RegionLootSourceTable regionTable))
            {
                HasPopulated = true;
                PopulateFromRegionTable(regionProfile, regionTable);
                return true;
            }

            if (lootTable == null) return false;

            HasPopulated = true;
            int rollCount = LootTableDefinition.CalculateRollCount(
                lootTable.MinimumRolls,
                lootTable.MaximumRolls,
                Random.value) + Mathf.Max(0, DangerRating - 1) / 2;

            for (int i = 0; i < rollCount; i++)
            {
                int entryIndex = LootTableDefinition.SelectWeightedEntryIndex(
                    lootTable.Entries,
                    Random.value,
                    DangerRating);
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

        // 지역 상자 표는 후보 선택 뒤 각 항목의 실제 드랍 확률을 한 번 더 굴립니다.
        // 이 실제 확률에만 진행 중 임무의 +5%p가 계산되며 ScriptableObject 원본 값은 전혀 변경하지 않습니다.
        private void PopulateFromRegionTable(RegionLootProfile profile, RegionLootSourceTable table)
        {
            int rollCount = RegionLootProfile.CalculateRollCount(table, Random.value) +
                            Mathf.Max(0, DangerRating - 1) / 2;

            for (int i = 0; i < rollCount; i++)
            {
                int entryIndex = profile.SelectWeightedEntryIndex(table, Random.value, DangerRating);
                if (entryIndex < 0) continue;

                RegionLootEntry entry = table.Entries[entryIndex];
                float chance = profile.GetEffectiveDropChance(entry);
                if (chance <= 0f || (chance < 1f && Random.value >= chance)) continue;

                int quantity = RegionLootProfile.CalculateQuantity(entry, Random.value);
                if (entry?.item == null || quantity <= 0) continue;

                // 큰 장비가 상자 격자에 들어가지 않으면 일부 수량을 잘라 넣지 않고 이번 추첨 결과를 통째로 건너뜁니다.
                container.TryAdd(entry.item, quantity);
            }
        }

        private bool ResolveRegionTable(out RegionLootProfile profile, out RegionLootSourceTable table)
        {
            profile = RegionLootController.ActiveProfile;
            table = profile?.FindTable(RegionLootSourceType.Container, RegionSourceId);
            return profile != null && table != null;
        }
    }
}
