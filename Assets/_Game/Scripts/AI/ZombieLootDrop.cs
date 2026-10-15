using System;
using UnityEngine;
using ZombieSurvival.Combat;
using ZombieSurvival.Items;

namespace ZombieSurvival.AI
{
    // 좀비 한 마리가 어떤 아이템을 몇 퍼센트 확률과 어떤 수량 범위로 떨어뜨리는지 정의하는 한 줄입니다.
    // 배열로 여러 줄을 넣을 수 있으므로 탄약과 치료품을 각각 독립적으로 굴리는 드롭 목록을 만들 수 있습니다.
    [Serializable]
    public sealed class ZombieLootEntry
    {
        public ItemDefinition item;
        [Range(0f, 1f)] public float dropChance = 1f;
        [Min(1)] public int minimumQuantity = 1;
        [Min(1)] public int maximumQuantity = 1;
    }

    // Health의 사망 이벤트를 한 번 받아 이 좀비에 설정된 전리품 목록을 확률별로 굴리고 시체 내부 격자에 넣습니다.
    // 아이템을 바닥에 즉시 흩뿌리지 않으므로 플레이어는 시체에 E키로 상호작용한 뒤 필요한 전리품을 직접 옮겨야 합니다.
    [RequireComponent(typeof(Health))]
    public sealed class ZombieLootDrop : MonoBehaviour
    {
        [SerializeField] private ZombieLootEntry[] lootEntries = Array.Empty<ZombieLootEntry>();
        // 현재 지역 프로필에서 이 좀비 종류의 표를 찾을 때 사용하는 ID입니다.
        // 지역 표가 없는 씬에서는 lootEntries가 그대로 대체 데이터로 사용됩니다.
        [SerializeField] private string regionSourceId;
        private Health health;
        private bool dropped;

        public ZombieLootEntry[] LootEntries => lootEntries;
        public string RegionSourceId => RegionLootSourceTable.NormalizeSourceId(regionSourceId);

        // 프로토타입 생성기나 다른 스폰 시스템이 좀비별 목록을 명시적으로 넣을 때 사용합니다.
        public void Configure(params ZombieLootEntry[] entries)
        {
            regionSourceId = string.Empty;
            lootEntries = entries ?? Array.Empty<ZombieLootEntry>();
        }

        // ZombieArchetype은 에셋 이름 같은 안정적인 유형 ID와 기존 대체 목록을 함께 전달합니다.
        // 학교처럼 지역 표가 준비된 씬에서는 ID에 맞는 지역 수치가 우선이고, 다른 씬에서는 대체 목록이 사용됩니다.
        public void Configure(string sourceId, params ZombieLootEntry[] fallbackEntries)
        {
            regionSourceId = RegionLootSourceTable.NormalizeSourceId(sourceId);
            lootEntries = fallbackEntries ?? Array.Empty<ZombieLootEntry>();
        }

        private void Awake()
        {
            health = GetComponent<Health>();
            health.Died += HandleDeath;
        }

        private void OnDestroy()
        {
            if (health != null) health.Died -= HandleDeath;
        }

        private void HandleDeath()
        {
            // 여러 시스템이 사망 이벤트를 다시 전달해도 같은 시체에서 전리품이 중복 생성되지 않게 합니다.
            if (dropped) return;
            dropped = true;

            // 원래 좀비 오브젝트는 Health가 프레임 끝에 제거하므로 전리품을 보관할 별도 시체 오브젝트를 먼저 만듭니다.
            // 시체 Collider는 Trigger라 플레이어 이동을 막지 않지만 PlayerInteraction의 수색 대상에는 계속 포함됩니다.
            WorldLootContainer corpse = WorldLootContainer.SpawnCorpse(gameObject.name, transform.position, transform.rotation);

            RegionLootProfile regionProfile = RegionLootController.ActiveProfile;
            RegionLootSourceTable regionTable = regionProfile?.FindTable(
                RegionLootSourceType.Monster,
                RegionSourceId);
            if (regionTable != null)
            {
                DropRegionEntries(corpse, regionProfile, regionTable);
                return;
            }

            for (int i = 0; i < lootEntries.Length; i++)
            {
                ZombieLootEntry entry = lootEntries[i];
                if (entry?.item == null) continue;

                int quantity = CalculateDropQuantity(
                    entry.dropChance,
                    entry.minimumQuantity,
                    entry.maximumQuantity,
                    UnityEngine.Random.value,
                    UnityEngine.Random.value);
                if (quantity <= 0) continue;

                // 시체 격자에 공간이 있는 경우에만 전체 수량을 한 번에 넣습니다.
                // 현재 기본 시체는 8×6으로 큰 무기와 여러 종류의 프로토타입 전리품을 함께 담을 수 있습니다.
                corpse.TryAdd(entry.item, quantity);
            }
        }

        // 몬스터 표는 상자처럼 후보 하나를 고르는 방식이 아니라 모든 항목의 확률을 각각 독립적으로 굴립니다.
        // 따라서 좀비 한 마리가 탄약과 재료를 동시에 남길 수 있고 각 품목을 Inspector에서 따로 조절할 수 있습니다.
        private static void DropRegionEntries(
            WorldLootContainer corpse,
            RegionLootProfile profile,
            RegionLootSourceTable table)
        {
            RegionLootEntry[] entries = table.Entries;
            for (int i = 0; i < entries.Length; i++)
            {
                RegionLootEntry entry = entries[i];
                if (entry?.item == null) continue;

                int quantity = CalculateDropQuantity(
                    profile.GetEffectiveDropChance(entry),
                    entry.minimumQuantity,
                    entry.maximumQuantity,
                    UnityEngine.Random.value,
                    UnityEngine.Random.value);
                if (quantity <= 0) continue;
                corpse.TryAdd(entry.item, quantity);
            }
        }

        // chanceRoll이 확률보다 작을 때만 드롭하며, quantityRoll을 최소~최대 범위의 정수 하나로 변환합니다.
        // 난수 자체를 인자로 받아 EditMode 테스트에서 0%, 100%와 수량 경계값을 정확히 재현할 수 있습니다.
        public static int CalculateDropQuantity(
            float dropChance,
            int minimumQuantity,
            int maximumQuantity,
            float chanceRoll,
            float quantityRoll)
        {
            float chance = Mathf.Clamp01(dropChance);
            if (chance <= 0f || (chance < 1f && Mathf.Clamp01(chanceRoll) >= chance)) return 0;

            int minimum = Mathf.Max(1, minimumQuantity);
            int maximum = Mathf.Max(minimum, maximumQuantity);
            int range = maximum - minimum + 1;
            int offset = Mathf.Min(range - 1, Mathf.FloorToInt(Mathf.Clamp01(quantityRoll) * range));
            return minimum + offset;
        }
    }
}
