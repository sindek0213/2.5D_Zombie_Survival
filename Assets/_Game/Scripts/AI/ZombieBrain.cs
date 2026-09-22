using UnityEngine;
using ZombieSurvival.Combat;
using ZombieSurvival.Player;

namespace ZombieSurvival.AI
{
    // 좀비가 현재 무엇을 해야 하는지 결정하는 간단한 유한 상태 머신입니다.
    // 실제 이동은 ZombieMovement, 피해 적용은 ZombieAttack에 맡기고 이 클래스는 거리 기반 상태 선택만 담당합니다.
    // 역할을 나누면 이동 방식이나 공격 방식을 바꿔도 상태 판단 코드를 함께 수정할 필요가 없습니다.
    [RequireComponent(typeof(ZombieMovement), typeof(ZombieAttack), typeof(Health))]
    public sealed class ZombieBrain : MonoBehaviour
    {
        public enum ZombieState
        {
            Idle,
            Chase,
            Attack,
            Dead
        }

        [SerializeField, Min(0f)] private float detectionRange = 9f;
        [SerializeField, Min(0f)] private float attackRange = 1.35f;
        [SerializeField, Min(0f)] private float loseTargetRange = 13f;

        private ZombieMovement movement;
        private ZombieAttack attack;
        private Health health;
        private Transform target;

        public ZombieState CurrentState { get; private set; } = ZombieState.Idle;

        // ZombieSpawner가 이미 알고 있는 플레이어 Transform을 새 좀비에 직접 전달할 때 사용합니다.
        // 스폰된 좀비마다 씬 전체를 다시 검색하지 않게 하며, null은 잘못된 연결을 만들지 않도록 무시합니다.
        public void SetTarget(Transform newTarget)
        {
            if (newTarget != null) target = newTarget;
        }

        private void Awake()
        {
            movement = GetComponent<ZombieMovement>();
            attack = GetComponent<ZombieAttack>();
            health = GetComponent<Health>();
            health.Died += HandleDeath;
        }

        private void Start()
        {
            // 정상 레이드에서는 ZombieSpawner가 같은 플레이어 참조를 미리 전달합니다.
            // 스포너가 없는 독립 테스트 씬에서만 한 번 자동 탐색해 ZombieBrain 단독 사용도 계속 지원합니다.
            if (target != null) return;
            PlayerMovement player = FindObjectOfType<PlayerMovement>();
            target = player != null ? player.transform : null;
        }

        private void OnDestroy()
        {
            if (health != null) health.Died -= HandleDeath;
        }

        private void Update()
        {
            if (CurrentState == ZombieState.Dead || target == null) return;

            float distance = PlanarDistance(transform.position, target.position);
            CurrentState = EvaluateState(CurrentState, distance, detectionRange, attackRange, loseTargetRange);

            switch (CurrentState)
            {
                case ZombieState.Chase:
                    movement.MoveTowards(target.position);
                    break;
                case ZombieState.Attack:
                    movement.ApplyGravity();
                    attack.TryAttack(target);
                    break;
                default:
                    movement.ApplyGravity();
                    break;
            }
        }

        // Health가 0이 되면 Died 이벤트로 호출됩니다.
        // Update와 이동/공격 컴포넌트를 즉시 끄고 CharacterController도 비활성화해 사망 뒤 추가 이동이나 공격을 막습니다.
        // 오브젝트 제거 시점은 Health가 담당하므로 ZombieBrain은 Destroy를 중복 호출하지 않습니다.
        private void HandleDeath()
        {
            CurrentState = ZombieState.Dead;
            enabled = false;
            movement.enabled = false;
            attack.enabled = false;

            CharacterController controller = GetComponent<CharacterController>();
            if (controller != null) controller.enabled = false;
        }

        // 공격 거리 안이면 이전 상태와 관계없이 Attack이 가장 우선됩니다.
        // Idle은 detectionRange 안에서만 Chase로 바뀌지만, 이미 추적 중이면 더 큰 loseTargetRange까지 추적을 유지합니다.
        // Dead는 어떤 거리에서도 다른 상태로 돌아가지 않으며, 순수 함수라 상태 경계값을 EditMode에서 테스트할 수 있습니다.
        public static ZombieState EvaluateState(
            ZombieState current,
            float distance,
            float detectionRange,
            float attackRange,
            float loseTargetRange)
        {
            if (current == ZombieState.Dead) return ZombieState.Dead;
            if (distance <= attackRange) return ZombieState.Attack;
            if (distance <= detectionRange ||
                (current != ZombieState.Idle && distance <= loseTargetRange))
                return ZombieState.Chase;
            return ZombieState.Idle;
        }

        // 높이 차이를 제외한 XZ 평면 거리를 반환합니다.
        public static float PlanarDistance(Vector3 from, Vector3 to)
        {
            from.y = 0f;
            to.y = 0f;
            return Vector3.Distance(from, to);
        }
    }
}
