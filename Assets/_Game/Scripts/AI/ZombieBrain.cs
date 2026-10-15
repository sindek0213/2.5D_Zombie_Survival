using UnityEngine;
using ZombieSurvival.CameraSystem;
using ZombieSurvival.Combat;
using ZombieSurvival.Core;
using ZombieSurvival.Player;

namespace ZombieSurvival.AI
{
    // 좀비가 현재 무엇을 해야 하는지 결정하는 간단한 유한 상태 머신입니다.
    // 실제 이동은 ZombieMovement, 피해 적용은 ZombieAttack에 맡기고 이 클래스는 시야·소리·기억을 이용한 상태 선택만 담당합니다.
    // 역할을 나누면 이동 방식이나 공격 방식을 바꿔도 상태 판단 코드를 함께 수정할 필요가 없습니다.
    [RequireComponent(typeof(ZombieMovement), typeof(ZombieAttack), typeof(Health))]
    public sealed class ZombieBrain : MonoBehaviour
    {
        public enum ZombieState
        {
            Idle,
            Investigate,
            Chase,
            Attack,
            Search,
            ReturnHome,
            Dead
        }

        [SerializeField, Min(0f)] private float detectionRange = 9f;
        [SerializeField, Min(0f)] private float attackRange = 1.35f;
        [SerializeField, Min(0f)] private float loseTargetRange = 13f;
        [SerializeField, Min(0f)] private float hearingRange = 18f;
        [SerializeField, Min(0.1f)] private float investigationDuration = 4f;
        [SerializeField] private ZombieBehaviorTrait behaviorTrait;

        [Header("시야와 추적 기억")]
        // detectionRange 안에 있어도 시야각 밖이거나 벽 뒤에 있으면 즉시 플레이어를 발견하지 못합니다.
        [SerializeField, Range(1f, 360f)] private float visionAngle = 130f;
        [SerializeField, Min(0f)] private float sightOriginHeight = 1.35f;
        [SerializeField] private LayerMask sightObstructionMask = ~0;
        // 시야에서 놓친 뒤 잠깐은 마지막 위치까지 추적하고, 그곳을 수색한 뒤 원래 대기 위치로 돌아갑니다.
        [SerializeField, Min(0f)] private float targetMemoryDuration = 2.5f;
        [SerializeField, Min(0.1f)] private float searchDuration = 4f;
        [SerializeField, Min(0.1f)] private float returnHomeStopDistance = 0.7f;

        [Header("다층 건물 판정")]
        // 학교 테스트 건물의 1층과 2층 바닥 간격입니다.
        // 이후 다른 맵의 층고가 달라지면 프리팹이나 종류별 설정에서 이 값만 바꿔 같은 층 판정 코드를 재사용할 수 있습니다.
        [SerializeField, Min(0.1f)] private float floorHeight = SchoolRaidMapLayout.ClassroomSecondFloorHeight;

        private ZombieMovement movement;
        private ZombieAttack attack;
        private Health health;
        private Transform target;
        private Vector3 lastHeardPosition;
        private float investigateUntil;
        private float nextScreamAt;
        private bool hasHeardNoise;
        private Vector3 homePosition;
        private Vector3 lastSeenPosition;
        private float lastSeenAt = float.NegativeInfinity;
        private float searchUntil;
        private bool hasSeenTarget;

        public ZombieState CurrentState { get; private set; } = ZombieState.Idle;
        // 디버깅이나 이후 층별 스폰 로직에서 사용할 수 있도록 좀비와 목표의 현재 층을 공개하되 외부에서 임의로 바꾸지는 못하게 합니다.
        public int CurrentFloor { get; private set; } = 1;
        public int TargetFloor { get; private set; } = 1;
        public bool IsTargetOnSameFloor => CurrentFloor == TargetFloor;

        // ZombieSpawner가 이미 알고 있는 플레이어 Transform을 새 좀비에 직접 전달할 때 사용합니다.
        // 스폰된 좀비마다 씬 전체를 다시 검색하지 않게 하며, null은 잘못된 연결을 만들지 않도록 무시합니다.
        public void SetTarget(Transform newTarget)
        {
            if (newTarget != null) target = newTarget;
        }

        public void ConfigureSenses(
            float newDetectionRange,
            float newHearingRange,
            float newLoseTargetRange,
            float newInvestigationDuration,
            ZombieBehaviorTrait trait)
        {
            detectionRange = Mathf.Max(0f, newDetectionRange);
            hearingRange = Mathf.Max(0f, newHearingRange);
            loseTargetRange = Mathf.Max(detectionRange, newLoseTargetRange);
            investigationDuration = Mathf.Max(0.1f, newInvestigationDuration);
            behaviorTrait = trait;
        }

        private void Awake()
        {
            movement = GetComponent<ZombieMovement>();
            attack = GetComponent<ZombieAttack>();
            health = GetComponent<Health>();
            health.Died += HandleDeath;
            homePosition = transform.position;
            // 천장 투과 중 다른 층의 적까지 보이지 않도록 아이템·시체와 같은 층 표시 규칙을 사용합니다.
            if (GetComponent<FloorVisibilityTarget>() == null) gameObject.AddComponent<FloorVisibilityTarget>();
        }

        private void OnEnable()
        {
            ZombieNoiseSystem.NoiseEmitted -= HandleNoise;
            ZombieNoiseSystem.NoiseEmitted += HandleNoise;
        }

        private void OnDisable()
        {
            ZombieNoiseSystem.NoiseEmitted -= HandleNoise;
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

            CurrentFloor = FloorLevelUtility.ResolveFloorLevel(transform.position.y, floorHeight);
            TargetFloor = FloorLevelUtility.ResolveFloorLevel(target.position.y, floorHeight);

            // 평면 거리만 사용하면 1층 좀비가 바로 위 2층 플레이어를 0m 거리로 판단합니다.
            // 탐지와 추적에는 실제 3차원 거리를 사용하고, 서로 다른 층이면 공격 거리 안이어도 Chase 상태를 유지해 계단 경로를 찾게 합니다.
            float distance = Vector3.Distance(transform.position, target.position);
            ZombieState previousState = CurrentState;
            bool seesTarget = distance <= loseTargetRange && CanSeeTarget(target);

            if (seesTarget && (distance <= detectionRange || IsPursuitState(CurrentState)))
            {
                hasSeenTarget = true;
                lastSeenPosition = target.position;
                lastSeenAt = Time.time;
                CurrentState = distance <= attackRange && IsTargetOnSameFloor
                    ? ZombieState.Attack
                    : ZombieState.Chase;
            }
            else if (hasSeenTarget && IsDirectPursuitState(CurrentState) && Time.time <= lastSeenAt + targetMemoryDuration)
            {
                // 플레이어가 모퉁이를 돈 직후에는 마지막으로 본 위치까지 추적해 너무 쉽게 AI를 끊지 못하게 합니다.
                CurrentState = ZombieState.Chase;
            }
            else if (hasSeenTarget && IsDirectPursuitState(CurrentState))
            {
                CurrentState = ZombieState.Search;
                searchUntil = Time.time + searchDuration;
            }
            else if ((CurrentState == ZombieState.Idle || CurrentState == ZombieState.ReturnHome) &&
                     hasHeardNoise && Time.time <= investigateUntil)
            {
                CurrentState = ZombieState.Investigate;
            }
            else if (hasHeardNoise && Time.time > investigateUntil)
            {
                hasHeardNoise = false;
            }

            // 울부짖는 특수형이 플레이어를 처음 발견하면 넓은 소음을 발생시켜 주변 대기 좀비를 같은 방향으로 끌어옵니다.
            // 쿨다운을 두어 추적과 공격 상태가 바뀔 때마다 매 프레임 소리를 반복하지 않게 합니다.
            bool acquiredTarget = previousState != ZombieState.Chase && previousState != ZombieState.Attack &&
                                  (CurrentState == ZombieState.Chase || CurrentState == ZombieState.Attack);
            if (behaviorTrait == ZombieBehaviorTrait.Screamer && acquiredTarget && Time.time >= nextScreamAt)
            {
                nextScreamAt = Time.time + 8f;
                ZombieNoiseSystem.Emit(transform.position, 30f, ZombieNoiseSystem.NoiseKind.Scream);
            }

            switch (CurrentState)
            {
                case ZombieState.Investigate:
                    movement.MoveTowards(lastHeardPosition);
                    if (PlanarDistance(transform.position, lastHeardPosition) <= 0.8f)
                    {
                        hasHeardNoise = false;
                        CurrentState = ZombieState.Search;
                        searchUntil = Time.time + searchDuration;
                    }
                    break;
                case ZombieState.Chase:
                    movement.MoveTowards(seesTarget ? target.position : lastSeenPosition);
                    break;
                case ZombieState.Attack:
                    // 같은 층이어도 벽, 닫힌 문이나 2층 바닥이 사이를 막으면 제자리에서 벽 너머로 공격하면 안 됩니다.
                    // 이때 Chase로 되돌려 NavMesh가 출입구나 계단을 거치는 실제 경로를 계속 계산하게 합니다.
                    if (!attack.CanReachTarget(target))
                    {
                        CurrentState = ZombieState.Chase;
                        movement.MoveTowards(target.position);
                        break;
                    }

                    movement.ApplyGravity();
                    attack.TryAttack(target);
                    break;
                case ZombieState.Search:
                    movement.MoveTowards(lastSeenPosition);
                    if (Time.time >= searchUntil)
                    {
                        hasSeenTarget = false;
                        CurrentState = ZombieState.ReturnHome;
                    }
                    break;
                case ZombieState.ReturnHome:
                    movement.MoveTowards(homePosition);
                    if (PlanarDistance(transform.position, homePosition) <= returnHomeStopDistance)
                        CurrentState = ZombieState.Idle;
                    break;
                default:
                    movement.ApplyGravity();
                    break;
            }
        }

        private void HandleNoise(ZombieNoiseSystem.NoiseEvent noise)
        {
            if (CurrentState == ZombieState.Dead || hearingRange <= 0f) return;
            float distance = PlanarDistance(transform.position, noise.Position);
            if (distance > Mathf.Min(hearingRange, noise.Radius)) return;

            lastHeardPosition = noise.Position;
            investigateUntil = Time.time + investigationDuration;
            hasHeardNoise = true;
            // 총성과 비명은 짧은 발소리보다 오래 조사해 강한 소음의 차이가 반경뿐 아니라 행동에도 드러나게 합니다.
            if (noise.Kind == ZombieNoiseSystem.NoiseKind.Gunshot || noise.Kind == ZombieNoiseSystem.NoiseKind.Scream)
                investigateUntil += investigationDuration * 0.5f;
        }

        // 시야각과 벽 가림을 함께 검사합니다. Raycast가 자기 Collider나 플레이어의 자식 Collider를 만나면 건너뜁니다.
        // 이 검사를 한곳에 모아 이후 손전등·연막 같은 시야 규칙을 추가해도 상태 머신 전체를 고칠 필요가 없게 합니다.
        private bool CanSeeTarget(Transform candidate)
        {
            if (candidate == null) return false;
            Vector3 origin = transform.position + Vector3.up * sightOriginHeight;
            Vector3 destination = candidate.position + Vector3.up;
            Vector3 direction = destination - origin;
            float distance = direction.magnitude;
            if (distance <= 0.001f) return true;

            Vector3 planarForward = transform.forward;
            planarForward.y = 0f;
            Vector3 planarDirection = direction;
            planarDirection.y = 0f;
            if (!IsInsideVisionCone(planarForward, planarDirection, visionAngle)) return false;

            RaycastHit[] hits = Physics.RaycastAll(
                origin,
                direction / distance,
                distance,
                sightObstructionMask,
                QueryTriggerInteraction.Ignore);
            System.Array.Sort(hits, (left, right) => left.distance.CompareTo(right.distance));
            foreach (RaycastHit hit in hits)
            {
                if (hit.collider == null) continue;
                Transform hitTransform = hit.collider.transform;
                if (hitTransform == transform || hitTransform.IsChildOf(transform)) continue;
                if (hitTransform == candidate || hitTransform.IsChildOf(candidate)) return true;
                return false;
            }
            return true;
        }

        // 방향 벡터와 전체 시야각만 사용하는 순수 함수라 EditMode 테스트에서 정확한 경계값을 검증할 수 있습니다.
        public static bool IsInsideVisionCone(Vector3 forward, Vector3 toTarget, float fullVisionAngle)
        {
            forward.y = 0f;
            toTarget.y = 0f;
            if (toTarget.sqrMagnitude <= 0.0001f) return true;
            if (forward.sqrMagnitude <= 0.0001f) forward = Vector3.forward;
            return Vector3.Angle(forward, toTarget) <= Mathf.Clamp(fullVisionAngle, 1f, 360f) * 0.5f;
        }

        private static bool IsPursuitState(ZombieState state)
        {
            return state == ZombieState.Chase || state == ZombieState.Attack || state == ZombieState.Search;
        }

        private static bool IsDirectPursuitState(ZombieState state)
        {
            return state == ZombieState.Chase || state == ZombieState.Attack;
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
                ((current == ZombieState.Chase || current == ZombieState.Attack) && distance <= loseTargetRange))
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
