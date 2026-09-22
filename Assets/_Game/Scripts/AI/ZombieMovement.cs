using UnityEngine;
using UnityEngine.AI;
using ZombieSurvival.Combat;

namespace ZombieSurvival.AI
{
    // NavMeshAgent가 계산한 경로 방향을 CharacterController의 실제 이동으로 바꾸는 좀비 이동 컴포넌트입니다.
    // Unity가 긴 벽 우회와 다른 좀비 회피를 담당하고, 이 클래스는 기존 중력과 총알 넉백을 함께 적용합니다.
    // NavMeshAgent가 Transform을 직접 움직이지 않게 해 물리 이동 주체가 둘이 되어 떨리는 문제를 막습니다.
    [RequireComponent(typeof(CharacterController))]
    public sealed class ZombieMovement : MonoBehaviour, IKnockbackReceiver
    {
        [Header("기본 이동")]
        [SerializeField, Min(0f)] private float moveSpeed = 2.35f;
        [SerializeField] private float gravity = -20f;
        [SerializeField, Min(0.1f)] private float agentAcceleration = 12f;
        [SerializeField, Min(0f)] private float agentStoppingDistance = 1.05f;

        [Header("NavMesh 위치와 목적지")]
        [SerializeField, Min(0.1f)] private float agentPlacementDistance = 2f;
        [SerializeField, Min(0.1f)] private float destinationSampleDistance = 2f;
        [SerializeField, Min(0.05f)] private float destinationRefreshInterval = 0.2f;
        [SerializeField, Min(0.05f)] private float targetMoveThreshold = 0.35f;

        [Header("총알 밀림")]
        [SerializeField, Min(0.1f)] private float knockbackDamping = 10f;

        private CharacterController controller;
        private NavMeshAgent agent;
        private float verticalVelocity;
        private Vector3 externalVelocity;
        private Vector3 lastDestination;
        private float nextDestinationRefreshAt;
        private float nextPlacementAttemptAt;
        private bool hasDestination;

        public bool IsUsingNavigation => agent != null && agent.enabled && agent.isOnNavMesh;

        private void Awake()
        {
            controller = GetComponent<CharacterController>();
            agent = GetComponent<NavMeshAgent>();
            if (agent == null) agent = gameObject.AddComponent<NavMeshAgent>();
            ConfigureAgent();

            // RaidNavigationSurface가 먼저 맵을 굽고 Start에서 안전한 NavMesh 위치를 찾은 다음 에이전트를 켭니다.
            // 프리팹이 맵 바깥에서 임시로 생성되는 순간 NavMeshAgent 오류가 발생하지 않도록 여기서는 비활성 상태로 둡니다.
            agent.enabled = false;
        }

        private void Start()
        {
            TryActivateAgent();
        }

        // 일반형·빠른형처럼 좀비 종류 데이터에서 받은 이동 속도를 물리 이동과 NavMesh 예상 속도 양쪽에 적용합니다.
        // 음수 값은 반대 방향 이동을 만들 수 있으므로 0으로 제한합니다.
        public void ConfigureSpeed(float speed)
        {
            moveSpeed = Mathf.Max(0f, speed);
            if (agent != null) agent.speed = moveSpeed;
        }

        // 플레이어 위치를 NavMesh 목적지로 갱신하고 Agent가 계산한 desiredVelocity를 실제 수평 속도로 사용합니다.
        // NavMesh가 아직 준비되지 않은 아주 짧은 순간에는 기존처럼 직선 방향으로 시도하되 CharacterController가 벽 통과를 막습니다.
        public void MoveTowards(Vector3 targetPosition)
        {
            if (!TryActivateAgent())
            {
                Vector3 fallbackDirection = FlattenAndNormalize(targetPosition - transform.position);
                MoveController(fallbackDirection * moveSpeed);
                return;
            }

            float targetMoveDistance = PlanarDistance(lastDestination, targetPosition);
            if (ShouldRefreshDestination(
                    hasDestination,
                    Time.time,
                    nextDestinationRefreshAt,
                    targetMoveDistance,
                    targetMoveThreshold))
            {
                RefreshDestination(targetPosition);
            }

            // desiredVelocity에는 NavMesh 경로의 다음 모서리 방향과 주변 Agent 회피 결과가 이미 반영되어 있습니다.
            // Y값은 CharacterController 중력이 따로 담당하므로 제거하고 최대 속도를 제한해 대각선에서도 설정 속도를 넘지 않게 합니다.
            Vector3 navigationVelocity = agent.desiredVelocity;
            navigationVelocity.y = 0f;
            navigationVelocity = Vector3.ClampMagnitude(navigationVelocity, moveSpeed);
            MoveController(navigationVelocity);
        }

        // Idle이나 Attack 상태에서는 이전 추적 경로를 멈추고 수평 이동 없이 중력과 남아 있는 넉백만 적용합니다.
        // 공격이 끝나 Chase로 돌아가면 hasDestination이 false라 즉시 새로운 플레이어 위치로 경로를 요청합니다.
        public void ApplyGravity()
        {
            StopNavigation();
            MoveController(Vector3.zero);
        }

        // 총알이 전달한 수평 밀림 속도를 현재 값에 더합니다.
        // 여러 산탄 펠릿이 동시에 맞아도 최대 8m/s로 제한하고 시간이 지나면 knockbackDamping 속도로 자연스럽게 줄어듭니다.
        public void ApplyKnockback(Vector3 velocity)
        {
            velocity.y = 0f;
            externalVelocity = Vector3.ClampMagnitude(externalVelocity + velocity, 8f);
        }

        private void RefreshDestination(Vector3 requestedDestination)
        {
            nextDestinationRefreshAt = Time.time + destinationRefreshInterval;

            // 플레이어가 벽 위나 NavMesh 가장자리 가까이에 있어도 주변의 가장 가까운 이동 가능한 지점을 목적지로 사용합니다.
            // SamplePosition이 실패하면 현재 경로를 유지하고 다음 갱신 시 다시 시도합니다.
            if (!NavMesh.SamplePosition(
                    requestedDestination,
                    out NavMeshHit sampledDestination,
                    destinationSampleDistance,
                    agent.areaMask))
                return;

            agent.isStopped = false;
            if (!agent.SetDestination(sampledDestination.position)) return;
            lastDestination = requestedDestination;
            hasDestination = true;
        }

        // 스폰 위치와 가장 가까운 NavMesh 지점을 찾아 좀비를 그 위에 올리고 Agent를 활성화합니다.
        // 스폰 지점이 잘못 배치되어 찾지 못한 경우 0.5초마다 다시 시도해 매 프레임 비싼 NavMesh 검사를 반복하지 않습니다.
        private bool TryActivateAgent()
        {
            if (agent != null && agent.enabled && agent.isOnNavMesh) return true;
            if (agent == null || Time.time < nextPlacementAttemptAt) return false;
            nextPlacementAttemptAt = Time.time + 0.5f;

            if (!NavMesh.SamplePosition(
                    transform.position,
                    out NavMeshHit sampledPosition,
                    agentPlacementDistance,
                    agent.areaMask))
                return false;

            bool controllerWasEnabled = controller.enabled;
            if (controllerWasEnabled) controller.enabled = false;
            transform.position = sampledPosition.position;
            if (controllerWasEnabled) controller.enabled = true;

            agent.enabled = true;
            if (!agent.isOnNavMesh || !agent.Warp(sampledPosition.position))
            {
                agent.enabled = false;
                return false;
            }

            agent.isStopped = true;
            agent.nextPosition = transform.position;
            hasDestination = false;
            return true;
        }

        // 수평 경로 속도, 총알 넉백과 중력을 합쳐 CharacterController를 정확히 한 번만 이동시킵니다.
        // 이동 뒤 Agent의 내부 위치를 실제 Transform과 맞춰 다음 프레임의 경로와 회피 계산이 엉뚱한 위치에서 시작되지 않게 합니다.
        private void MoveController(Vector3 planarVelocity)
        {
            if (controller.isGrounded && verticalVelocity < 0f) verticalVelocity = -2f;
            verticalVelocity += gravity * Time.deltaTime;
            Vector3 finalVelocity = planarVelocity + externalVelocity + Vector3.up * verticalVelocity;
            controller.Move(finalVelocity * Time.deltaTime);

            externalVelocity = Vector3.MoveTowards(
                externalVelocity,
                Vector3.zero,
                knockbackDamping * Time.deltaTime);

            if (agent != null && agent.enabled && agent.isOnNavMesh)
                agent.nextPosition = transform.position;
        }

        private void StopNavigation()
        {
            if (agent != null && agent.enabled && agent.isOnNavMesh)
            {
                agent.isStopped = true;
                if (hasDestination) agent.ResetPath();
                agent.nextPosition = transform.position;
            }
            hasDestination = false;
            nextDestinationRefreshAt = 0f;
        }

        // NavMeshAgent는 경로 계산만 맡고 CharacterController가 실제 Transform을 움직이도록 설정합니다.
        // 회피 우선순위를 좀비마다 조금 다르게 주면 서로 마주쳤을 때 양쪽이 같은 판단을 반복하며 떨 가능성이 줄어듭니다.
        private void ConfigureAgent()
        {
            agent.updatePosition = false;
            agent.updateRotation = false;
            agent.updateUpAxis = false;
            agent.radius = controller.radius;
            agent.height = controller.height;
            agent.baseOffset = 0f;
            agent.speed = moveSpeed;
            agent.acceleration = agentAcceleration;
            agent.angularSpeed = 720f;
            agent.stoppingDistance = agentStoppingDistance;
            agent.autoBraking = true;
            agent.autoRepath = true;
            agent.obstacleAvoidanceType = ObstacleAvoidanceType.HighQualityObstacleAvoidance;
            agent.avoidancePriority = CalculateAvoidancePriority(GetInstanceID());
        }

        private void OnDisable()
        {
            StopNavigation();
            if (agent != null) agent.enabled = false;
        }

        // 최소 갱신 시간이 지난 뒤, 목적지가 없거나 플레이어가 설정 거리 이상 움직인 경우에만 새 경로를 요청합니다.
        // 첫 추적과 공격 후 재추적 때는 nextAllowedTime을 0으로 초기화하므로 즉시 요청하고, 실패한 요청만 짧게 기다렸다 재시도합니다.
        public static bool ShouldRefreshDestination(
            bool hasCurrentDestination,
            float currentTime,
            float nextAllowedTime,
            float targetMoveDistance,
            float moveThreshold)
        {
            if (currentTime < nextAllowedTime) return false;
            if (!hasCurrentDestination) return true;
            return Mathf.Max(0f, targetMoveDistance) >= Mathf.Max(0.05f, moveThreshold);
        }

        // Unity의 회피 우선순위는 0~99이고 작은 숫자가 더 높은 우선순위입니다.
        // 모든 좀비를 25~74 사이에 고르게 나눠 어느 한 마리도 항상 최우선이나 최하위가 되지 않게 합니다.
        public static int CalculateAvoidancePriority(int instanceId)
        {
            int safeId = instanceId == int.MinValue ? int.MaxValue : Mathf.Abs(instanceId);
            return 25 + safeId % 50;
        }

        private static Vector3 FlattenAndNormalize(Vector3 value)
        {
            value.y = 0f;
            return value.sqrMagnitude > 0.001f ? value.normalized : Vector3.zero;
        }

        private static float PlanarDistance(Vector3 from, Vector3 to)
        {
            float deltaX = from.x - to.x;
            float deltaZ = from.z - to.z;
            return Mathf.Sqrt(deltaX * deltaX + deltaZ * deltaZ);
        }

        private void OnValidate()
        {
            moveSpeed = Mathf.Max(0f, moveSpeed);
            agentAcceleration = Mathf.Max(0.1f, agentAcceleration);
            agentStoppingDistance = Mathf.Max(0f, agentStoppingDistance);
            agentPlacementDistance = Mathf.Max(0.1f, agentPlacementDistance);
            destinationSampleDistance = Mathf.Max(0.1f, destinationSampleDistance);
            destinationRefreshInterval = Mathf.Max(0.05f, destinationRefreshInterval);
            targetMoveThreshold = Mathf.Max(0.05f, targetMoveThreshold);
            knockbackDamping = Mathf.Max(0.1f, knockbackDamping);

            // Play 중 Inspector에서 값을 바꾸면 현재 Agent에도 즉시 반영해 테스트를 다시 시작하지 않아도 조정 결과를 볼 수 있습니다.
            if (Application.isPlaying && agent != null) ConfigureAgent();
        }
    }
}
