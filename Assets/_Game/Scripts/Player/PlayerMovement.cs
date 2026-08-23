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
        private float verticalVelocity;

        // Awake는 씬이 로드되어 이 컴포넌트가 준비될 때 한 번 호출됩니다.
        // 이후 매 프레임 GetComponent를 반복하지 않도록 같은 오브젝트의 컨트롤러를 미리 저장합니다.
        private void Awake() => controller = GetComponent<CharacterController>();

        // Update는 게임이 실행되는 동안 화면 프레임마다 한 번 호출됩니다.
        // 키 입력은 Update에서 읽고, 이동 속도에 Time.deltaTime을 곱해 프레임 속도와 무관하게 움직입니다.
        private void Update()
        {
            // GetAxisRaw를 사용해 입력 가속 없이 즉시 반응하도록 합니다.
            var input = new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical"));
            Vector3 planarVelocity = CalculatePlanarVelocity(input, moveSpeed);

            // CharacterController는 중력을 자동 적용하지 않습니다.
            // 지면에 닿았을 때 작은 음수 속도를 유지해야 경사면이나 계단에서 공중 판정이 반복되지 않습니다.
            if (controller.isGrounded && verticalVelocity < 0f)
                verticalVelocity = -2f;
            verticalVelocity += gravity * Time.deltaTime;

            controller.Move((planarVelocity + Vector3.up * verticalVelocity) * Time.deltaTime);
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
