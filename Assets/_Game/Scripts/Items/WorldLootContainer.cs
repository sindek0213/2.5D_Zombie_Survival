using UnityEngine;
using UnityEngine.AI;
using ZombieSurvival.Inventory;

namespace ZombieSurvival.Items
{
    // 좀비 시체나 필드 아이템 상자처럼 여러 아이템을 내부 격자에 보관하는 월드 상호작용 대상입니다.
    // 플레이어가 E키를 누르면 아이템을 자동으로 모두 줍지 않고, InventoryHud가 이 격자를 열어 직접 필요한 물건만 옮기게 합니다.
    public sealed class WorldLootContainer : MonoBehaviour, IInteractable
    {
        [SerializeField] private string displayName = "전리품";
        [SerializeField, Min(1)] private int gridWidth = 8;
        [SerializeField, Min(1)] private int gridHeight = 6;
        [SerializeField] private bool blocksMovement;

        private PlayerInventory nearbyInventory;
        private FieldLootSource lootSource;

        public PlayerInventory.GridData LootGrid { get; private set; }
        public string DisplayName => string.IsNullOrWhiteSpace(displayName) ? "전리품" : displayName;
        public bool IsEmpty => LootGrid == null || LootGrid.Entries.Count == 0;
        public bool BlocksMovement => blocksMovement;
        public bool CanInteract => nearbyInventory != null && LootGrid != null;
        public string InteractionPrompt => HasPendingLoot
            ? $"[E]  {DisplayName} 수색"
            : IsEmpty
            ? $"[E]  {DisplayName} 확인  (비어 있음)"
            : $"[E]  {DisplayName} 수색";

        private bool HasPendingLoot => lootSource != null && lootSource.HasPendingLoot;

        private void Awake()
        {
            EnsureGrid();
            ApplyCollisionPolicy();
        }

        // 생성기와 런타임 시체 생성 코드가 표시 이름, 내부 크기와 이동 차단 여부를 한 번에 지정합니다.
        // 시체는 blocksPlayer를 false로 사용하고, 아이템 상자는 true로 사용해 같은 수색 UI를 서로 다른 충돌 규칙으로 재사용합니다.
        public void Configure(string containerName, int width, int height, bool blocksPlayer)
        {
            displayName = string.IsNullOrWhiteSpace(containerName) ? "전리품" : containerName;
            gridWidth = Mathf.Max(1, width);
            gridHeight = Mathf.Max(1, height);
            blocksMovement = blocksPlayer;
            LootGrid = new PlayerInventory.GridData(gridWidth, gridHeight);
            ApplyCollisionPolicy();
        }

        public void SetInteractor(PlayerInventory inventory) => nearbyInventory = inventory;

        // FieldLootSource가 자신을 등록해 첫 수색 전에는 빈 상자로 잘못 표시되지 않도록 합니다.
        // 시체에는 이 컴포넌트가 없으므로 기존 즉시 생성된 시체 전리품 흐름은 바뀌지 않습니다.
        public void SetLootSource(FieldLootSource source)
        {
            lootSource = source;
        }

        // 실제 화면 열기는 PlayerInteraction의 이벤트를 받은 InventoryHud가 담당합니다.
        // 이 메서드는 상호작용할 플레이어와 격자가 준비됐는지만 확인해 IInteractable 규칙을 유지합니다.
        public bool Interact()
        {
            if (!CanInteract) return false;
            lootSource?.PopulateIfNeeded();
            return true;
        }

        // 확률 계산을 통과한 전리품을 중첩 여유부터 채운 뒤 남은 묶음을 격자의 첫 빈자리에 배치합니다.
        // 전체 수량이 들어갈 수 없으면 아무것도 바꾸지 않아 시체 생성 중 일부 수량만 사라지는 일을 막습니다.
        public bool TryAdd(ItemDefinition item, int quantity)
        {
            EnsureGrid();
            // 플레이어 인벤토리와 같은 공통 추가 함수를 사용해 중첩 순서, 커스텀 모양과 공간 부족 처리 규칙을 완전히 일치시킵니다.
            // WorldLootContainer에는 별도 HUD 이벤트가 없으며, 열린 수색 화면은 실제 이동을 담당한 PlayerInventory 이벤트로 다시 그려집니다.
            return InventoryGridContent.TryAdd(LootGrid, item, quantity);
        }

        private void EnsureGrid()
        {
            if (LootGrid == null) LootGrid = new PlayerInventory.GridData(gridWidth, gridHeight);
        }

        // 시체와 바닥 드롭은 모든 Collider를 Trigger로 바꿔 플레이어가 통과할 수 있게 합니다.
        // 아이템 상자는 Collider를 단단한 장애물로 유지하므로 통과할 수 없고, 기본 플레이어 Step Offset보다 높아 위로 걸어 올라가지 못합니다.
        private void ApplyCollisionPolicy()
        {
            Collider[] colliders = GetComponentsInChildren<Collider>(true);
            foreach (Collider collider in colliders)
            {
                if (collider == null || collider is CharacterController) continue;
                collider.isTrigger = !blocksMovement;
            }

            ConfigureNavigationObstacle();
        }

        // 단단한 아이템 상자는 런타임 NavMesh 위에 구멍을 만드는 Carving 장애물로 등록합니다.
        // 시체와 바닥 드롭은 통과 대상이므로 NavMeshObstacle을 만들지 않아 좀비 경로도 불필요하게 막지 않습니다.
        private void ConfigureNavigationObstacle()
        {
            NavMeshObstacle obstacle = GetComponent<NavMeshObstacle>();
            if (!blocksMovement)
            {
                if (obstacle != null) obstacle.enabled = false;
                return;
            }

            if (obstacle == null) obstacle = gameObject.AddComponent<NavMeshObstacle>();
            obstacle.enabled = true;
            obstacle.shape = NavMeshObstacleShape.Box;
            obstacle.carving = true;
            obstacle.carveOnlyStationary = true;

            // SpawnLootBox가 만드는 루트 BoxCollider와 같은 중심·크기를 사용해 보이는 상자와 길찾기 구멍이 정확히 겹치게 합니다.
            BoxCollider boxCollider = GetComponent<BoxCollider>();
            if (boxCollider != null)
            {
                obstacle.center = boxCollider.center;
                obstacle.size = boxCollider.size;
            }
        }

        // 좀비가 죽은 자리에 낮게 누운 임시 시체 외형과 통과 가능한 수색 Trigger를 만듭니다.
        // 최종 좀비 시체 모델이 준비되면 이 생성 부분만 프리팹 방식으로 바꾸고 LootGrid와 UI 코드는 그대로 사용할 수 있습니다.
        public static WorldLootContainer SpawnCorpse(string corpseName, Vector3 position, Quaternion rotation)
        {
            GameObject corpse = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            corpse.name = "Corpse_" + corpseName;
            corpse.transform.position = position + Vector3.up * 0.16f;
            corpse.transform.rotation = rotation * Quaternion.Euler(0f, 0f, 90f);
            corpse.transform.localScale = new Vector3(0.38f, 0.65f, 0.38f);

            Renderer renderer = corpse.GetComponent<Renderer>();
            if (renderer != null)
            {
                Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
                if (shader != null) renderer.material = new Material(shader) { color = new Color(0.18f, 0.2f, 0.18f) };
            }

            WorldLootContainer container = corpse.AddComponent<WorldLootContainer>();
            container.Configure("좀비 시체", 8, 6, false);
            return container;
        }

        // 앞으로 필드 파밍 상자를 만들 때 사용할 공통 생성 함수입니다.
        // 시체와 달리 BoxCollider를 Trigger로 바꾸지 않으며 높이를 충분히 주어 플레이어가 통과하거나 Step Offset으로 올라타지 못하게 합니다.
        public static WorldLootContainer SpawnLootBox(string boxName, Vector3 position, int width = 8, int height = 5)
        {
            GameObject box = GameObject.CreatePrimitive(PrimitiveType.Cube);
            box.name = "LootBox_" + boxName;
            box.transform.position = position + Vector3.up * 0.55f;
            box.transform.localScale = new Vector3(1.2f, 1.1f, 0.9f);

            WorldLootContainer container = box.AddComponent<WorldLootContainer>();
            container.Configure(boxName, width, height, true);
            return container;
        }
    }
}
