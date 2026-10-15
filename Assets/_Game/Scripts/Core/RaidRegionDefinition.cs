using UnityEngine;

namespace ZombieSurvival.Core
{
    // 거점의 출격 화면에 표시할 지역 이름, 설명과 실제 씬 이름을 한 묶음으로 보관합니다.
    // 출격 UI가 씬 이름을 직접 여러 곳에 적지 않으므로 이후 공장·숲 지역을 추가할 때 에셋만 늘려 같은 화면을 재사용할 수 있습니다.
    [CreateAssetMenu(menuName = "Zombie Survival/Raid Region")]
    public sealed class RaidRegionDefinition : ScriptableObject
    {
        [SerializeField] private string regionId = "region";
        [SerializeField] private string displayName = "레이드 지역";
        // 새 지역 에셋을 만든 직후에도 빌드에서 제외된 구형 테스트 씬이 아니라 현재 기본 학교 레이드로 연결합니다.
        [SerializeField] private string sceneName = GameSceneNavigator.RaidSceneName;
        [SerializeField, TextArea] private string description;
        [SerializeField, Range(1, 5)] private int difficulty = 1;
        [SerializeField] private bool available = true;

        public string RegionId => regionId;
        public string DisplayName => string.IsNullOrWhiteSpace(displayName) ? regionId : displayName;
        public string SceneName => sceneName;
        public string Description => description;
        public int Difficulty => Mathf.Clamp(difficulty, 1, 5);
        public bool Available => available && !string.IsNullOrWhiteSpace(sceneName);

        // 프로토타입 생성기가 같은 지역 에셋을 반복해서 갱신할 때 사용하는 명시적인 설정 진입점입니다.
        // available을 false로 두면 목록에는 지역을 보여 주되 출격 버튼만 잠그는 미구현 지역 표시에도 그대로 사용할 수 있습니다.
        public void Configure(
            string id,
            string name,
            string targetSceneName,
            string regionDescription,
            int difficultyRating,
            bool isAvailable)
        {
            regionId = string.IsNullOrWhiteSpace(id) ? "region" : id.Trim();
            displayName = string.IsNullOrWhiteSpace(name) ? regionId : name.Trim();
            sceneName = targetSceneName?.Trim() ?? string.Empty;
            description = regionDescription ?? string.Empty;
            difficulty = Mathf.Clamp(difficultyRating, 1, 5);
            available = isAvailable;
        }

        private void OnValidate()
        {
            difficulty = Mathf.Clamp(difficulty, 1, 5);
            regionId = string.IsNullOrWhiteSpace(regionId) ? "region" : regionId.Trim();
            sceneName = sceneName?.Trim() ?? string.Empty;
        }
    }
}
