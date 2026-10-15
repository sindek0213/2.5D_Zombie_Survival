using System;
using UnityEngine;
using UnityEngine.Serialization;
using ZombieSurvival.AI;
using ZombieSurvival.CameraSystem;
using ZombieSurvival.Combat;

namespace ZombieSurvival.Player
{
    // 이 컴포넌트는 키보드의 WASD 입력을 3D 월드의 XZ 평면 이동으로 변환합니다.
    // Unity에서 X축은 좌우, Z축은 앞뒤, Y축은 높이를 뜻하므로 수평 이동에는 X와 Z만 사용합니다.
    // Rigidbody에 힘을 주는 방식이 아니라 CharacterController.Move를 사용하며,
    // 벽 충돌과 계단 이동은 CharacterController가 처리하고 중력 값은 이 코드가 직접 계산합니다.
    [RequireComponent(typeof(CharacterController))]
    public sealed class PlayerMovement : MonoBehaviour
    {
        // Inspector에서 이동 속도와 중력을 조정할 수 있습니다.
        [SerializeField, Min(0f)] private float moveSpeed = 5f;
        [SerializeField] private float gravity = -20f;

        [Header("이동 소음")]
        // 걷기는 가까운 좀비만 듣고, 달리기는 더 멀리 전달됩니다. 간격을 두어 매 프레임 이벤트를 보내지 않습니다.
        [SerializeField, Min(0f)] private float walkNoiseRadius = 4.5f;
        [SerializeField, Min(0f)] private float sprintNoiseRadius = 9f;
        [SerializeField, Min(0.05f)] private float walkNoiseInterval = 0.65f;
        [SerializeField, Min(0.05f)] private float sprintNoiseInterval = 0.38f;

        [Header("이동 방향 구르기")]
        // Space를 한 번 누르면 현재 WASD 이동 방향으로 짧게 구릅니다. 거리÷시간이 구르기 속도이며 현재 기본값은 3m÷0.24초입니다.
        // FormerlySerializedAs는 이전 프리팹에서 직접 조절했던 돌진 거리·시간이 이름 변경 때문에 사라지는 일을 막습니다.
        [FormerlySerializedAs("dashDistance"), SerializeField, Min(0.01f)] private float rollDistance = 3f;
        [FormerlySerializedAs("dashDuration"), SerializeField, Min(0.01f)] private float rollDuration = 0.24f;
        [FormerlySerializedAs("dashCooldown"), SerializeField, Min(0f)] private float rollCooldown = 0.55f;
        // 0.25는 현재 남은 기력이 아니라 최대 기력의 25%입니다. 최대치가 200으로 늘면 한 번에 50을 소모합니다.
        [SerializeField, Range(0.01f, 1f)] private float rollStaminaFraction = 0.25f;
        // 실제 충돌체는 회전하지 않고 Visual의 그림만 굴립니다. 나중에 전용 구르기 애니메이션으로 교체할 때 이 옵션을 끄면 됩니다.
        [SerializeField] private bool animateRoll = true;

        private CharacterController controller;
        private PlayerSurvival survival;
        private PlayerCondition condition;
        private PlayerStatusEffects statusEffects;
        private Health health;
        private float verticalVelocity;
        private SpriteBillboard rollVisual;
        // 키를 놓은 순간에도 마지막으로 걸었던 방향으로 구를 수 있도록 마우스 조준 방향과 별도로 기억합니다.
        private Vector3 lastMovementDirection;
        private Vector3 rollDirection;
        private float rollTimeRemaining;
        private float nextRollAllowedTime;
        private float nextFootstepNoiseTime;
        // 애니메이션 코드는 키 입력을 다시 읽지 않고 실제로 계산된 이동 결과를 사용해야 합니다.
        // 이렇게 공개된 수평 속도를 읽으면 포만감 페널티와 달리기 배율까지 반영된 움직임에 맞춰 걷기 프레임을 재생할 수 있습니다.
        public Vector3 CurrentPlanarVelocity { get; private set; }
        public bool IsRolling => rollTimeRemaining > 0f;
        public Vector3 RollDirection => rollDirection;
        public float RollProgress => IsRolling ? 1f - rollTimeRemaining / Mathf.Max(0.01f, rollDuration) : 0f;
        // 구르기가 실제로 시작된 순간 전투 시스템에 알려 진행 중인 재장전을 취소하게 합니다.
        // 단순 Space 입력이 아니라 기력·접지 검사를 모두 통과한 뒤에만 발생하므로 실패한 구르기는 재장전을 방해하지 않습니다.
        public event Action RollStarted;
        // 현재 배율에는 포만감 부족 페널티와 달리기 보너스가 모두 포함됩니다.
        // 예를 들어 포만감 배율 0.8과 달리기 배율 1.45가 동시에 적용되면 최종 배율은 1.16입니다.
        public float CurrentSpeedMultiplier
        {
            get
            {
                float survivalMultiplier = survival != null
                    ? survival.MovementSpeedMultiplier * survival.SprintSpeedMultiplier
                    : 1f;
                float conditionMultiplier = condition != null ? condition.MovementMultiplier : 1f;
                float statusMultiplier = statusEffects != null ? statusEffects.MovementSpeedMultiplier : 1f;
                return survivalMultiplier * conditionMultiplier * statusMultiplier;
            }
        }

        // Awake는 씬이 로드되어 이 컴포넌트가 준비될 때 한 번 호출됩니다.
        // 이후 매 프레임 GetComponent를 반복하지 않도록 같은 오브젝트의 컨트롤러를 미리 저장합니다.
        private void Awake()
        {
            controller = GetComponent<CharacterController>();
            // PlayerSurvival이 없는 이동 테스트나 이전 프리팹에서는 정상 속도 1을 사용하도록 선택 참조로 찾습니다.
            survival = GetComponent<PlayerSurvival>();
            condition = GetComponent<PlayerCondition>();
            statusEffects = GetComponent<PlayerStatusEffects>();
            health = GetComponent<Health>();
            rollVisual = GetComponentInChildren<SpriteBillboard>();
            lastMovementDirection = transform.forward;
        }

        // Update는 게임이 실행되는 동안 화면 프레임마다 한 번 호출됩니다.
        // 키 입력은 Update에서 읽고, 이동 속도에 Time.deltaTime을 곱해 프레임 속도와 무관하게 움직입니다.
        private void Update()
        {
            // GetAxisRaw를 사용해 입력 가속 없이 즉시 반응하도록 합니다.
            var input = new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical"));

            // 구르기는 마우스 조준 방향이 아니라 현재 누른 WASD 방향을 사용합니다.
            // 이동키를 누르지 않았다면 마지막 이동 방향을 유지해 제자리에서 Space를 눌러도 예측 가능한 방향으로 구릅니다.
            if (!IsRolling)
            {
                Vector3 movementDirection = CalculatePlanarVelocity(input, 1f);
                if (movementDirection.sqrMagnitude > 0.001f)
                    lastMovementDirection = movementDirection.normalized;
                if (Input.GetKeyDown(KeyCode.Space)) TryBeginRoll(input);
            }

            // 누른 바로 그 프레임부터 구르기를 적용합니다. 일반 이동을 함께 적용하면 시작 순간 이동량과 기력 소모가 달라집니다.
            if (IsRolling)
            {
                AdvanceRoll();
                return;
            }

            // 왼쪽 Shift와 실제 이동 입력이 함께 있을 때만 달리기를 요청합니다.
            // PlayerSurvival이 고갈 잠금과 남은 스태미나를 최종 판단하므로 이동 코드는 속도 배율만 읽으면 됩니다.
            bool hasMovementInput = input.sqrMagnitude > 0.001f;
            bool shiftHeld = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
            survival?.SetSprintRequested(shiftHeld && hasMovementInput);

            // 포만감이 낮으면 기본 속도가 줄고, 달리기 중이면 같은 결과에 달리기 배율이 추가됩니다.
            CurrentPlanarVelocity = CalculatePlanarVelocity(input, moveSpeed * CurrentSpeedMultiplier);

            // CharacterController는 중력을 자동 적용하지 않습니다.
            // 지면에 닿았을 때 작은 음수 속도를 유지해야 경사면이나 계단에서 공중 판정이 반복되지 않습니다.
            ApplyGravityAndMove(Time.deltaTime);
            EmitFootstepNoise(hasMovementInput, shiftHeld);
        }

        // 실제 이동 입력과 접지 상태가 모두 있을 때만 발소리를 냅니다.
        // 달리기는 반경이 넓고 간격이 짧아 빠르게 이동하는 대신 더 많은 좀비를 끌어들이는 선택이 됩니다.
        private void EmitFootstepNoise(bool hasMovementInput, bool sprintRequested)
        {
            if (!hasMovementInput || !controller.isGrounded || Time.time < nextFootstepNoiseTime) return;

            bool sprinting = sprintRequested && survival != null && survival.IsSprinting;
            float radius = sprinting ? sprintNoiseRadius : walkNoiseRadius;
            float interval = sprinting ? sprintNoiseInterval : walkNoiseInterval;
            nextFootstepNoiseTime = Time.time + Mathf.Max(0.05f, interval);
            ZombieNoiseSystem.Emit(transform.position, radius, ZombieNoiseSystem.NoiseKind.Footstep);
        }

        // 입력 처리와 실제 시작 판정을 분리해 테스트와 향후 다른 입력 장치도 같은 기력·접지·잠금 규칙을 사용하게 합니다.
        public bool TryBeginRoll() => TryBeginRoll(Vector2.zero);

        // Update에서 읽은 이동 입력을 직접 받아 같은 프레임의 방향으로 구릅니다.
        // 매개변수가 없는 호출은 테스트나 다른 시스템용이며 입력이 없을 때 마지막 이동 방향을 그대로 사용합니다.
        public bool TryBeginRoll(Vector2 movementInput)
        {
            // 공중·일시정지·사망·UI 조작 잠금·연속 입력은 비용을 지불하기 전에 거부합니다.
            if (!isActiveAndEnabled || IsRolling || Time.timeScale <= 0f ||
                survival == null || !survival.isActiveAndEnabled || health == null || health.IsDead ||
                controller == null || !controller.enabled || !controller.isGrounded ||
                Time.time < nextRollAllowedTime) return false;

            float duration = Mathf.Max(0.01f, rollDuration);
            // 비용은 시작할 때 한 번만 내고 구르는 동안 회복을 잠시 미룹니다. 기력이 25% 미만이면 상태를 바꾸지 않습니다.
            if (!survival.TrySpendStaminaFraction(rollStaminaFraction, duration)) return false;

            Vector3 requestedDirection = CalculatePlanarVelocity(movementInput, 1f);
            requestedDirection.y = 0f;
            if (requestedDirection.sqrMagnitude > 0.001f)
                lastMovementDirection = requestedDirection.normalized;
            else
                requestedDirection = lastMovementDirection;
            if (requestedDirection.sqrMagnitude <= 0.001f) requestedDirection = transform.forward;
            requestedDirection.y = 0f;
            if (requestedDirection.sqrMagnitude <= 0.001f) requestedDirection = Vector3.forward;

            rollDirection = requestedDirection.normalized;
            rollTimeRemaining = duration;
            nextRollAllowedTime = Time.time + duration + Mathf.Max(0f, rollCooldown);
            health.SetEvasionInvulnerability(true);
            RollStarted?.Invoke();
            return true;
        }

        private void AdvanceRoll()
        {
            survival.SetSprintRequested(false);
            // 마지막 프레임은 남은 시간만 사용해 낮은 프레임에서도 정해진 거리보다 멀리 튀어나가지 않게 합니다.
            float stepTime = Mathf.Min(Time.deltaTime, rollTimeRemaining);
            CurrentPlanarVelocity = rollDirection * (Mathf.Max(0.01f, rollDistance) / Mathf.Max(0.01f, rollDuration));
            ApplyGravityAndMove(stepTime);
            rollTimeRemaining = Mathf.Max(0f, rollTimeRemaining - stepTime);

            // Billboard가 카메라를 바라보는 회전에 이 각도를 더합니다. 그림만 회전하므로 총구와 플레이어 충돌체는 기울지 않습니다.
            if (rollVisual != null && animateRoll)
                rollVisual.SetScreenRollAngle((rollDirection.x < 0f ? 1f : -1f) * 360f * RollProgress);
            if (!IsRolling) EndRoll();
        }

        private void EndRoll()
        {
            rollTimeRemaining = 0f;
            health?.SetEvasionInvulnerability(false);
            rollVisual?.SetScreenRollAngle(0f);
            CurrentPlanarVelocity = Vector3.zero;
        }

        private void ApplyGravityAndMove(float deltaTime)
        {
            if (controller.isGrounded && verticalVelocity < 0f)
                verticalVelocity = -2f;
            verticalVelocity += gravity * deltaTime;
            controller.Move((CurrentPlanarVelocity + Vector3.up * verticalVelocity) * deltaTime);
        }

        private void OnDisable()
        {
            // 인벤토리나 레이드 종료 화면이 PlayerMovement를 끄면 달리기 상태도 즉시 해제해야
            // 조작이 잠긴 동안 스태미나가 계속 줄지 않고 정상적으로 회복됩니다.
            survival?.SetSprintRequested(false);
            // 조작 잠금으로 컴포넌트가 꺼졌는데 마지막 이동 속도가 남아 있으면 캐릭터 그림만 계속 걷는 것처럼 보입니다.
            // 비활성화되는 순간 0으로 되돌려 인벤토리나 결과 화면에서는 정지 프레임이 표시되게 합니다.
            // 구르는 도중 인벤토리를 열어도 정지 상태에 무적과 회전이 남지 않도록 함께 해제합니다.
            EndRoll();
        }

        // 입력 벡터의 길이를 최대 1로 제한합니다.
        // 예를 들어 W+D는 (1,1)이 되어 길이가 약 1.414이므로 그대로 쓰면 대각선이 41% 빨라집니다.
        // ClampMagnitude로 이를 (0.707,0.707) 정도로 줄여 모든 방향의 최종 속도를 동일하게 만듭니다.
        // 이 메서드는 오브젝트 상태를 사용하지 않는 정적 계산 함수라 EditMode 테스트에서도 호출할 수 있습니다.
        public static Vector3 CalculatePlanarVelocity(Vector2 input, float speed)
        {
            Vector2 normalized = Vector2.ClampMagnitude(input, 1f);
            return new Vector3(normalized.x, 0f, normalized.y) * Mathf.Max(0f, speed);
        }
    }
}
