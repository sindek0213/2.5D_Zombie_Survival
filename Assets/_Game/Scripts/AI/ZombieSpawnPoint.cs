using UnityEngine;

namespace ZombieSurvival.AI
{
    // 좀비가 나타날 수 있는 월드 위치를 표시하는 가벼운 마커입니다.
    // 빈 GameObject에 이 컴포넌트를 붙여 두면 ZombieSpawner가 그 Transform의 위치와 방향을 사용합니다.
    public sealed class ZombieSpawnPoint : MonoBehaviour
    {
        [SerializeField, Range(1, 5)] private int dangerRating = 1;
        [SerializeField, Min(0f)] private float minimumRaidTime;
        [SerializeField] private bool allowRunner;
        [SerializeField] private bool allowBrute;
        [SerializeField] private bool allowScreamer;

        public Vector3 Position => transform.position;
        public Quaternion Rotation => transform.rotation;
        public int DangerRating => Mathf.Clamp(dangerRating, 1, 5);
        public float MinimumRaidTime => Mathf.Max(0f, minimumRaidTime);

        // 학교 생성기가 구역 위험도와 활성 시간을 연결할 때 사용하는 명시적 설정 함수입니다.
        // 일반형은 모든 지점에서 허용하고, 특수형 세 종류만 위험도에 따라 단계적으로 열어 초반 정문 난이도를 낮춥니다.
        public void Configure(
            int danger,
            float unlockTime,
            bool runners,
            bool brutes,
            bool screamers)
        {
            dangerRating = Mathf.Clamp(danger, 1, 5);
            minimumRaidTime = Mathf.Max(0f, unlockTime);
            allowRunner = runners;
            allowBrute = brutes;
            allowScreamer = screamers;
        }

        public bool IsAvailable(float elapsedTime)
            => Mathf.Max(0f, elapsedTime) >= MinimumRaidTime;

        // 스폰 지점의 역할과 좀비 특성을 함께 확인해 정문에 튼튼한 좀비가 나오거나 좁은 초반 구역에 비명형이 몰리는 상황을 막습니다.
        public bool Allows(ZombieArchetype archetype)
        {
            if (archetype == null) return false;
            switch (archetype.BehaviorTrait)
            {
                case ZombieBehaviorTrait.Runner: return allowRunner;
                case ZombieBehaviorTrait.Brute: return allowBrute;
                case ZombieBehaviorTrait.Screamer: return allowScreamer;
                default: return true;
            }
        }

        // 후반으로 갈수록 높은 위험도 지점이 선택될 확률을 조금 올리되 낮은 위험 지점도 완전히 제외하지 않습니다.
        public float GetSelectionWeight(float difficultyProgress)
        {
            float dangerBonus = (DangerRating - 1) * Mathf.Clamp01(difficultyProgress) * 0.55f;
            return 1f + dangerBonus;
        }

        // Scene 창에서 선택하지 않아도 스폰 위치를 확인할 수 있도록 작은 빨간 구와 방향선을 그립니다.
        // Gizmos는 에디터에서만 보이며 실제 게임 화면이나 물리 충돌에는 아무 영향이 없습니다.
        private void OnDrawGizmos()
        {
            // 위험도가 높을수록 노랑에서 진한 빨강으로 바뀌어 Scene 창에서 난이도 배치를 바로 확인할 수 있습니다.
            Gizmos.color = Color.Lerp(
                new Color(0.95f, 0.72f, 0.16f, 0.9f),
                new Color(0.9f, 0.08f, 0.04f, 0.95f),
                (DangerRating - 1) / 4f);
            Gizmos.DrawWireSphere(transform.position + Vector3.up * 0.2f, 0.45f);
            Gizmos.DrawLine(
                transform.position + Vector3.up * 0.2f,
                transform.position + Vector3.up * 0.2f + transform.forward * 1.2f);
        }
    }
}
