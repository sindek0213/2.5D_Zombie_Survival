using System;
using UnityEngine;
using ZombieSurvival.Hideout;
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
        private float statusMessageEndTime;

        public IInteractable Current { get; private set; }
        public string StatusMessage { get; private set; }
        public bool LastInteractionSucceeded { get; private set; }
        public bool HasVisibleStatus => IsStatusVisible(StatusMessage, Time.unscaledTime, statusMessageEndTime);
        // InventoryHud가 시체나 아이템 상자의 외부 격자를 열 수 있도록 성공한 수색 대상을 전달합니다.
        public event Action<WorldLootContainer> LootContainerOpened;

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
            // 성공·실패 결과는 잠시만 보여 주고 시간이 지나면 지워 다음 상호작용 안내와 섞이지 않게 합니다.
            if (!string.IsNullOrWhiteSpace(StatusMessage) && Time.unscaledTime >= statusMessageEndTime)
                StatusMessage = string.Empty;

            Current = FindClosestInteractable();
            if (Current is WorldItem worldItem) worldItem.SetInteractor(inventory);
            if (Current is WorldLootContainer lootContainer) lootContainer.SetInteractor(inventory);

            if (Input.GetKeyDown(KeyCode.E) && Current != null)
            {
                LastInteractionSucceeded = Current.Interact();
                if (Current is WorldLootContainer openedContainer)
                {
                    // 수색은 즉시 아이템을 지급하지 않고 HUD에 외부 격자를 열어 플레이어가 직접 옮기게 합니다.
                    if (LastInteractionSucceeded) LootContainerOpened?.Invoke(openedContainer);
                    StatusMessage = LastInteractionSucceeded
                        ? openedContainer.IsEmpty ? openedContainer.DisplayName + ": 비어 있습니다." : "전리품을 확인합니다."
                        : "지금은 수색할 수 없습니다.";
                }
                else if (Current is HideoutFacility facility)
                {
                    // 거점 시설을 열었을 때 아이템 획득 문구가 나오지 않도록 시설에 맞는 결과를 따로 표시합니다.
                    StatusMessage = LastInteractionSucceeded
                        ? facility.Definition.DisplayName + " 화면을 열었습니다."
                        : "지금은 이 시설을 사용할 수 없습니다.";
                }
                else
                {
                    StatusMessage = LastInteractionSucceeded ? "아이템을 획득했습니다." : "인벤토리에 공간이 부족합니다.";
                }
                statusMessageEndTime = Time.unscaledTime + 1.6f;
            }
        }

        // 문자열이 있고 종료 시각 전일 때만 결과 메시지를 표시합니다.
        // 시간 비교를 순수 함수로 분리해 실제 대기 없이 EditMode 테스트에서 경계값을 검증할 수 있습니다.
        public static bool IsStatusVisible(string message, float currentTime, float endTime)
        {
            return !string.IsNullOrWhiteSpace(message) && currentTime < endTime;
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
        // WorldItem.Spawn을 사용하므로 버린 아이템도 충돌을 막지 않는 Trigger 규칙을 자동으로 적용받습니다.
        private void SpawnDroppedItem(ItemDefinition definition, int quantity)
        {
            WorldItem.Spawn(definition, quantity, transform.position + Vector3.forward * 1.2f);
        }
    }
}
