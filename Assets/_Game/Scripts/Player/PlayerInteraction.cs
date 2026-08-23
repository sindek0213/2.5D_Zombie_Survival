using UnityEngine;
using ZombieSurvival.Inventory;
using ZombieSurvival.Items;

namespace ZombieSurvival.Player
{
    // 플레이어 주변의 Collider를 검사해 가장 가까운 IInteractable을 선택하고 E키 입력을 전달합니다.
    // 고정 크기 배열과 OverlapSphereNonAlloc을 사용해 매 프레임 불필요한 메모리 할당을 줄입니다.
    [RequireComponent(typeof(PlayerInventory))]
    public sealed class PlayerInteraction : MonoBehaviour
    {
        [SerializeField, Min(0.1f)] private float interactionRadius = 1.8f;
        [SerializeField] private LayerMask interactionMask = ~0;

        private readonly Collider[] overlaps = new Collider[24];
        private PlayerInventory inventory;

        public IInteractable Current { get; private set; }
        public string StatusMessage { get; private set; }

        private void Awake()
        {
            inventory = GetComponent<PlayerInventory>();
            inventory.DropRequested += SpawnDroppedItem;
        }

        private void OnDestroy()
        {
            if (inventory != null) inventory.DropRequested -= SpawnDroppedItem;
        }

        private void Update()
        {
            Current = FindClosestInteractable();
            if (Current is WorldItem worldItem) worldItem.SetInteractor(inventory);

            if (Input.GetKeyDown(KeyCode.E) && Current != null)
            {
                StatusMessage = Current.Interact() ? "아이템 획득" : "인벤토리 공간 부족";
            }
        }

        // 반경 안의 Collider를 최대 24개까지 가져온 뒤 자기 자신의 Collider는 제외합니다.
        // ClosestPoint까지의 제곱 거리를 비교하며, 제곱근 계산이 필요 없는 sqrMagnitude를 사용합니다.
        // 상호작용 대상이 없으면 null을 반환하고 HUD의 상호작용 안내도 표시되지 않습니다.
        private IInteractable FindClosestInteractable()
        {
            int count = Physics.OverlapSphereNonAlloc(
                transform.position,
                interactionRadius,
                overlaps,
                interactionMask,
                QueryTriggerInteraction.Collide);

            IInteractable closest = null;
            float closestDistance = float.PositiveInfinity;
            for (int i = 0; i < count; i++)
            {
                Collider candidate = overlaps[i];
                if (candidate.transform == transform || candidate.transform.IsChildOf(transform)) continue;

                IInteractable interactable = candidate.GetComponentInParent<IInteractable>();
                if (interactable == null) continue;

                float distance = (candidate.ClosestPoint(transform.position) - transform.position).sqrMagnitude;
                if (distance >= closestDistance) continue;
                closestDistance = distance;
                closest = interactable;
            }
            return closest;
        }

        // PlayerInventory가 버리기를 확정하면 DropRequested 이벤트를 통해 이 메서드가 호출됩니다.
        // 원래 정의와 수량을 가진 WorldItem을 플레이어 앞에 생성하므로 다시 E키로 주울 수 있습니다.
        // 현재 단계에서는 완성된 아이템 프리팹 대신 크기가 일정한 Cube를 임시 외형으로 사용합니다.
        private void SpawnDroppedItem(ItemDefinition definition, int quantity)
        {
            GameObject dropped = GameObject.CreatePrimitive(PrimitiveType.Cube);
            dropped.name = "Dropped_" + definition.ItemId;
            dropped.transform.position = transform.position + Vector3.forward * 1.2f + Vector3.up * 0.3f;
            dropped.transform.localScale = Vector3.one * 0.45f;
            dropped.AddComponent<WorldItem>().Initialize(definition, quantity);
        }
    }
}
