using UnityEngine;
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
            : $"E - {definition.DisplayName} x{quantity} 줍기";

        // 탐색 중인 플레이어 인벤토리를 연결합니다.
        public void SetInteractor(PlayerInventory inventory) => nearbyInventory = inventory;

        // 버리기 기능이 런타임에 생성한 월드 아이템 데이터를 설정합니다.
        public void Initialize(ItemDefinition item, int amount)
        {
            definition = item;
            quantity = Mathf.Max(1, amount);
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
