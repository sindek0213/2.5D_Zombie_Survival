using UnityEngine;

namespace ZombieSurvival.Extraction
{
    // 탈출 지점을 활성화하기 위해 필요한 아이템 ID와 수량을 에셋으로 정의합니다.
    // 월드 오브젝트가 ItemDefinition을 직접 참조하지 않고 문자열 ID만 사용하므로,
    // 이후 저장 데이터나 다른 레이드 맵에서도 같은 조건을 쉽게 재사용할 수 있습니다.
    [CreateAssetMenu(menuName = "Zombie Survival/Extraction Requirement")]
    public sealed class ExtractionRequirement : ScriptableObject
    {
        [SerializeField] private string requiredItemId = "quest_radio";
        [SerializeField] private string displayName = "고장 난 무전기";
        [SerializeField, Min(1)] private int requiredQuantity = 1;

        public string RequiredItemId => requiredItemId;
        public string DisplayName => displayName;
        public int RequiredQuantity => Mathf.Max(1, requiredQuantity);
    }
}
