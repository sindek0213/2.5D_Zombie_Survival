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
        private Health health;
        private bool dropped;

        public ZombieLootEntry[] LootEntries => lootEntries;

        // 프로토타입 생성기나 다른 스폰 시스템이 좀비별 목록을 명시적으로 넣을 때 사용합니다.
        public void Configure(params ZombieLootEntry[] entries)
        {
            lootEntries = entries ?? Array.Empty<ZombieLootEntry>();
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
