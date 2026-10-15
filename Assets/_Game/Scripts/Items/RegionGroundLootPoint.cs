using UnityEngine;

namespace ZombieSurvival.Items
{
    // 씬에 놓인 빈 지점 하나가 현재 지역의 Ground 표를 굴려 바닥 아이템을 최대 한 개 생성합니다.
    // 특정 아이템을 씬에 고정 배치하지 않으므로 지역 에셋의 수치만 수정해도 다음 레이드부터 바닥 전리품 구성이 달라집니다.
    public sealed class RegionGroundLootPoint : MonoBehaviour
    {
        [SerializeField] private string sourceId = "default";
        [SerializeField, Range(1, 5)] private int dangerRating = 1;
        [SerializeField] private bool spawnOnStart = true;

        public string SourceId => RegionLootSourceTable.NormalizeSourceId(sourceId);
        public int DangerRating => Mathf.Clamp(dangerRating, 1, 5);
        public bool HasRolled { get; private set; }
        public WorldItem SpawnedItem { get; private set; }

        private void Start()
        {
            if (spawnOnStart) SpawnIfNeeded();
        }

        // 에디터 생성기가 지점 종류와 위험도를 연결할 때 사용합니다.
        public void Configure(string newSourceId, int areaDangerRating = 1, bool generateOnStart = true)
        {
            sourceId = RegionLootSourceTable.NormalizeSourceId(newSourceId);
            dangerRating = Mathf.Clamp(areaDangerRating, 1, 5);
            spawnOnStart = generateOnStart;
        }

        // 실제 플레이용 난수를 사용해 이 지점을 한 번만 추첨합니다.
        public bool SpawnIfNeeded()
            => SpawnIfNeeded(Random.value, Random.value, Random.value);

        // 난수 세 개를 외부에서 받는 오버로드는 선택·확률·수량을 EditMode 테스트에서 정확히 재현할 수 있게 합니다.
        public bool SpawnIfNeeded(float selectionRoll, float chanceRoll, float quantityRoll)
        {
            if (HasRolled) return false;
            HasRolled = true;

            RegionLootProfile profile = RegionLootController.ActiveProfile;
            RegionLootSourceTable table = profile?.FindTable(RegionLootSourceType.Ground, SourceId);
            if (table == null) return false;

            int entryIndex = profile.SelectWeightedEntryIndex(table, selectionRoll, DangerRating);
            if (entryIndex < 0) return false;
            RegionLootEntry entry = table.Entries[entryIndex];

            float chance = profile.GetEffectiveDropChance(entry);
            float safeChanceRoll = Mathf.Clamp01(chanceRoll);
            if (chance <= 0f || (chance < 1f && safeChanceRoll >= chance)) return false;

            int quantity = RegionLootProfile.CalculateQuantity(entry, quantityRoll);
            if (entry?.item == null || quantity <= 0) return false;

            SpawnedItem = WorldItem.Spawn(entry.item, quantity, transform.position);
            if (SpawnedItem != null)
                SpawnedItem.gameObject.name = $"GroundLoot_{SourceId}_{entry.item.ItemId}";
            return SpawnedItem != null;
        }
    }
}
