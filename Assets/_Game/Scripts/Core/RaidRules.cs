using UnityEngine;

namespace ZombieSurvival.Core
{
    // 한 레이드의 추가 탈출 조건을 맵과 분리된 에셋으로 저장합니다.
    // 제한 시간은 없으며 다른 맵은 이 에셋만 바꿔 처치 수와 최소 생존 시간 조건을 다르게 구성할 수 있습니다.
    [CreateAssetMenu(menuName = "Zombie Survival/Raid Rules")]
    public sealed class RaidRules : ScriptableObject
    {
        // 첫 밸런스 기준은 좀비 세 마리를 처치하고 최소 25초가 지나야 탈출할 수 있는 입문 플레이입니다.
        // 오래 머문다고 자동 실패하지 않으므로 플레이어는 준비가 끝날 때까지 파밍하고 스스로 탈출 시점을 선택할 수 있습니다.
        [SerializeField, Min(0)] private int requiredZombieKills = 3;
        [SerializeField, Min(0f)] private float minimumExtractionTimeSeconds = 25f;

        public int RequiredZombieKills => Mathf.Max(0, requiredZombieKills);
        public float MinimumExtractionTimeSeconds => Mathf.Max(0f, minimumExtractionTimeSeconds);

        private void OnValidate()
        {
            requiredZombieKills = Mathf.Max(0, requiredZombieKills);
            minimumExtractionTimeSeconds = Mathf.Max(0f, minimumExtractionTimeSeconds);
        }
    }
}
