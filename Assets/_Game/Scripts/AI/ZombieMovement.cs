using UnityEngine;

namespace ZombieSurvival.AI
{
    // ZombieBrain이 전달한 목표 위치를 향해 CharacterController를 이동시키는 전용 컴포넌트입니다.
    // 목표와의 높이 차이는 제거해 계단이나 지형 높이 때문에 좀비가 위아래로 직접 날아가지 않게 합니다.
    // 현재는 직선 추적이므로 벽을 돌아가는 길찾기는 하지 않으며, 추후 NavMesh 방식으로 교체할 수 있습니다.
    [RequireComponent(typeof(CharacterController))]
    public sealed class ZombieMovement : MonoBehaviour
    {
        [SerializeField, Min(0f)] private float moveSpeed = 2.2f;
        [SerializeField] private float gravity = -20f;

        private CharacterController controller;
        private float verticalVelocity;

        private void Awake() => controller = GetComponent<CharacterController>();

        // 목표 위치를 향해 XZ 평면에서 이동하고 중력을 함께 적용합니다.
        public void MoveTowards(Vector3 targetPosition)
        {
            Vector3 direction = targetPosition - transform.position;
            direction.y = 0f;
            direction = direction.sqrMagnitude > 0.001f ? direction.normalized : Vector3.zero;

            if (controller.isGrounded && verticalVelocity < 0f) verticalVelocity = -2f;
            verticalVelocity += gravity * Time.deltaTime;
            controller.Move((direction * moveSpeed + Vector3.up * verticalVelocity) * Time.deltaTime);
        }

        // 좀비가 Idle 또는 Attack 상태여도 중력 계산을 멈추면 경사면에서 떠 있을 수 있습니다.
        // 수평 방향은 이동하지 않고 기존 verticalVelocity와 중력만 CharacterController에 적용합니다.
        public void ApplyGravity()
        {
            if (controller.isGrounded && verticalVelocity < 0f) verticalVelocity = -2f;
            verticalVelocity += gravity * Time.deltaTime;
            controller.Move(Vector3.up * verticalVelocity * Time.deltaTime);
        }
    }
}
