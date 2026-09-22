using UnityEngine;

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

        private CharacterController controller;
        private PlayerSurvival survival;
        private float verticalVelocity;
        // 애니메이션 코드는 키 입력을 다시 읽지 않고 실제로 계산된 이동 결과를 사용해야 합니다.
        // 이렇게 공개된 수평 속도를 읽으면 포만감 페널티와 달리기 배율까지 반영된 움직임에 맞춰 걷기 프레임을 재생할 수 있습니다.
        public Vector3 CurrentPlanarVelocity { get; private set; }
        // 현재 배율에는 포만감 부족 페널티와 달리기 보너스가 모두 포함됩니다.
        // 예를 들어 포만감 배율 0.8과 달리기 배율 1.45가 동시에 적용되면 최종 배율은 1.16입니다.
        public float CurrentSpeedMultiplier => survival != null
            ? survival.MovementSpeedMultiplier * survival.SprintSpeedMultiplier
            : 1f;

        // Awake는 씬이 로드되어 이 컴포넌트가 준비될 때 한 번 호출됩니다.
        // 이후 매 프레임 GetComponent를 반복하지 않도록 같은 오브젝트의 컨트롤러를 미리 저장합니다.
        private void Awake()
        {
            controller = GetComponent<CharacterController>();
            // PlayerSurvival이 없는 이동 테스트나 이전 프리팹에서는 정상 속도 1을 사용하도록 선택 참조로 찾습니다.
            survival = GetComponent<PlayerSurvival>();
        }

        // Update는 게임이 실행되는 동안 화면 프레임마다 한 번 호출됩니다.
        // 키 입력은 Update에서 읽고, 이동 속도에 Time.deltaTime을 곱해 프레임 속도와 무관하게 움직입니다.
        private void Update()
        {
            // GetAxisRaw를 사용해 입력 가속 없이 즉시 반응하도록 합니다.
            var input = new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical"));

            // 왼쪽 Shift와 실제 이동 입력이 함께 있을 때만 달리기를 요청합니다.
            // PlayerSurvival이 고갈 잠금과 남은 스태미나를 최종 판단하므로 이동 코드는 속도 배율만 읽으면 됩니다.
            bool hasMovementInput = input.sqrMagnitude > 0.001f;
            bool shiftHeld = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
            survival?.SetSprintRequested(shiftHeld && hasMovementInput);

            // 포만감이 낮으면 기본 속도가 줄고, 달리기 중이면 같은 결과에 달리기 배율이 추가됩니다.
            CurrentPlanarVelocity = CalculatePlanarVelocity(input, moveSpeed * CurrentSpeedMultiplier);

            // CharacterController는 중력을 자동 적용하지 않습니다.
            // 지면에 닿았을 때 작은 음수 속도를 유지해야 경사면이나 계단에서 공중 판정이 반복되지 않습니다.
            if (controller.isGrounded && verticalVelocity < 0f)
                verticalVelocity = -2f;
            verticalVelocity += gravity * Time.deltaTime;

            controller.Move((CurrentPlanarVelocity + Vector3.up * verticalVelocity) * Time.deltaTime);
        }

        private void OnDisable()
        {
            // 인벤토리나 레이드 종료 화면이 PlayerMovement를 끄면 달리기 상태도 즉시 해제해야
            // 조작이 잠긴 동안 스태미나가 계속 줄지 않고 정상적으로 회복됩니다.
            survival?.SetSprintRequested(false);
            // 조작 잠금으로 컴포넌트가 꺼졌는데 마지막 이동 속도가 남아 있으면 캐릭터 그림만 계속 걷는 것처럼 보입니다.
            // 비활성화되는 순간 0으로 되돌려 인벤토리나 결과 화면에서는 정지 프레임이 표시되게 합니다.
            CurrentPlanarVelocity = Vector3.zero;
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
