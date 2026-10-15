using UnityEngine;
using ZombieSurvival.CameraSystem;
using ZombieSurvival.Inventory;

namespace ZombieSurvival.Items
{
    // 3D 월드에 놓인 아이템 정의와 수량을 보관하고 E키 상호작용 시 인벤토리로 이전합니다.
    // Collider는 PlayerInteraction의 범위 탐색에 사용되며, 실제 추가 가능 여부는 PlayerInventory가 최종 판단합니다.
    [RequireComponent(typeof(Collider))]
    public sealed class WorldItem : MonoBehaviour, IInteractable
    {
        [SerializeField] private ItemDefinition definition;
        [SerializeField, Min(1)] private int quantity = 1;

        private PlayerInventory nearbyInventory;

        public ItemDefinition Definition => definition;
        public int Quantity => quantity;
        public bool CanInteract => definition != null && nearbyInventory != null;
        public string InteractionPrompt => definition == null
            ? string.Empty
            : $"[E]  {definition.DisplayName}  ×{quantity}  줍기";

        private void Awake()
        {
            // 바닥에 놓인 작은 아이템은 물리 장애물이 아니라 줍기 감지 대상이므로 모든 Collider를 Trigger로 사용합니다.
            // 이렇게 하면 CharacterController가 아이템에 막히거나 작은 Cube 위로 계단처럼 올라타는 현상이 사라집니다.
            ConfigurePickupColliders();
            // 위층 바닥이 투과될 때 다른 층의 드롭 아이템까지 보이지 않도록 공용 층 표시 대상을 자동으로 붙입니다.
            if (GetComponent<FloorVisibilityTarget>() == null) gameObject.AddComponent<FloorVisibilityTarget>();
        }

        // 탐색 중인 플레이어 인벤토리를 연결합니다.
        public void SetInteractor(PlayerInventory inventory) => nearbyInventory = inventory;

        // 버리기 기능이 런타임에 생성한 월드 아이템 데이터를 설정합니다.
        public void Initialize(ItemDefinition item, int amount)
        {
            definition = item;
            quantity = Mathf.Max(1, amount);
            ConfigurePickupColliders();
        }

        // 좀비 전리품처럼 플레이 도중 만들어지는 아이템을 기존 배치 아이템과 같은 구조로 생성합니다.
        // 임시 Cube 외형은 최종 드롭 프리팹이 준비되기 전에도 Collider, 색상과 상호작용을 모두 시험할 수 있게 해 줍니다.
        public static WorldItem Spawn(ItemDefinition item, int amount, Vector3 position)
        {
            if (item == null || amount <= 0) return null;

            GameObject drop = GameObject.CreatePrimitive(PrimitiveType.Cube);
            drop.name = "Dropped_" + item.ItemId;
            drop.transform.position = position + Vector3.up * 0.28f;
            drop.transform.localScale = new Vector3(0.42f, 0.42f, 0.42f);

            Renderer renderer = drop.GetComponent<Renderer>();
            if (renderer != null)
            {
                Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
                if (shader != null)
                    renderer.material = new Material(shader) { color = GetDropColor(item.Type) };
            }

            WorldItem worldItem = drop.AddComponent<WorldItem>();
            worldItem.Initialize(item, amount);
            return worldItem;
        }

        // 아이템 종류마다 임시 드롭 색상을 통일해 멀리서도 탄약·치료품·장비를 대략 구분할 수 있게 합니다.
        private static Color GetDropColor(ItemType type)
        {
            switch (type)
            {
                case ItemType.Ammo: return new Color(0.82f, 0.68f, 0.22f);
                case ItemType.Healing: return new Color(0.85f, 0.2f, 0.2f);
                case ItemType.Weapon: return new Color(0.28f, 0.3f, 0.32f);
                case ItemType.Armor: return new Color(0.25f, 0.45f, 0.55f);
                case ItemType.Container: return new Color(0.32f, 0.48f, 0.3f);
                // 강화 재료는 장비나 소비품과 구분되는 주황빛으로 표시해 레이드 중 경제 자원을 빠르게 알아보게 합니다.
                case ItemType.Material: return new Color(0.78f, 0.42f, 0.16f);
                default: return new Color(0.55f, 0.62f, 0.68f);
            }
        }

        private void ConfigurePickupColliders()
        {
            Collider[] colliders = GetComponentsInChildren<Collider>(true);
            foreach (Collider collider in colliders)
                if (collider != null) collider.isTrigger = true;
        }

        // TryAdd는 전체 수량을 받을 공간이 있을 때만 true를 반환합니다.
        // 성공한 뒤에만 Destroy를 호출하므로 공간 부족 시 월드 아이템과 수량이 그대로 유지됩니다.
        // Destroy는 현재 프레임이 끝날 때 실행되어 상호작용 처리 도중 참조가 갑자기 사라지는 것을 방지합니다.
        public bool Interact()
        {
            if (!CanInteract || !nearbyInventory.TryAdd(definition, quantity)) return false;
            Destroy(gameObject);
            return true;
        }
    }
}
