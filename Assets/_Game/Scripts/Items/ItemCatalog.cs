using System.Collections.Generic;
using UnityEngine;

namespace ZombieSurvival.Items
{
    // 저장 데이터의 ItemId를 실제 ItemDefinition 에셋으로 되돌릴 때 사용하는 프로젝트 공용 목록입니다.
    // JSON이나 RaidResult에는 Unity 오브젝트를 직접 저장하지 않고 문자열 ID만 기록하므로 씬이 바뀐 뒤 이 카탈로그가 참조를 복원합니다.
    [CreateAssetMenu(menuName = "Zombie Survival/Item Catalog")]
    public sealed class ItemCatalog : ScriptableObject
    {
        [SerializeField] private List<ItemDefinition> definitions = new List<ItemDefinition>();

        public IReadOnlyList<ItemDefinition> Definitions => definitions;

        // 같은 표시 이름을 가진 아이템이 있어도 저장용 고유 ItemId만 비교하여 정확한 정의를 반환합니다.
        // null 에셋이나 비어 있는 ID는 건너뛰며 찾지 못하면 null을 반환해 호출자가 아이템 유실 없이 실패 처리할 수 있게 합니다.
        public ItemDefinition Find(string itemId)
        {
            if (string.IsNullOrWhiteSpace(itemId)) return null;

            foreach (ItemDefinition definition in definitions)
            {
                if (definition == null) continue;
                if (definition.ItemId == itemId) return definition;
            }
            return null;
        }
    }
}
