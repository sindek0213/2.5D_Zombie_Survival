using UnityEngine;

namespace ZombieSurvival.CameraSystem
{
    // target 위치에 offset을 더한 목표 지점으로 카메라 리그를 부드럽게 이동시킵니다.
    // 카메라 회전은 바꾸지 않으므로 씬에서 설정한 비스듬한 탑다운 각도가 계속 유지됩니다.
    public sealed class CameraFollow : MonoBehaviour
    {
        [SerializeField] private Transform target;
        [SerializeField] private Vector3 offset = new Vector3(0f, 11f, -8f);
        [SerializeField, Min(0f)] private float smoothTime = 0.12f;
        // SmoothDamp가 프레임 사이의 이동 속도를 기억할 때 사용하는 내부 값입니다.
        private Vector3 velocity;

        // 씬 생성 코드나 다른 시스템이 추적 대상을 지정할 수 있게 합니다.
        public void SetTarget(Transform value) => target = value;

        // LateUpdate는 일반 Update가 모두 끝난 뒤 호출됩니다.
        // 플레이어가 이번 프레임 이동을 마친 위치를 기준으로 추적하므로 카메라가 한 프레임 뒤처져 떨리는 현상을 줄입니다.
        // SmoothDamp는 즉시 순간이동하지 않고 smoothTime 동안 속도를 보간하며 velocity에 계산 상태를 보관합니다.
        private void LateUpdate()
        {
            if (target == null) return;
            transform.position = Vector3.SmoothDamp(transform.position, target.position + offset, ref velocity, smoothTime);
        }
    }
}
