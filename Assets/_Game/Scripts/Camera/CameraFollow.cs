using UnityEngine;
using ZombieSurvival.Player;

namespace ZombieSurvival.CameraSystem
{
    // target 위치에 offset을 더한 목표 지점으로 카메라 리그를 부드럽게 이동시킵니다.
    // 카메라 회전은 바꾸지 않으므로 씬에서 설정한 비스듬한 탑다운 각도가 계속 유지됩니다.
    public sealed class CameraFollow : MonoBehaviour
    {
        [SerializeField] private Transform target;
        [SerializeField] private Vector3 offset = new Vector3(0f, 11f, -8f);
        [SerializeField, Min(0f)] private float smoothTime = 0.12f;

        [Header("우클릭 정밀 조준 확대")]
        // 1이면 평소 화면 크기와 같고 값이 작을수록 조준 중 더 가까이 확대됩니다.
        // 기본 0.8은 시야를 알아볼 수 있을 만큼 당기면서 주변 위험도 어느 정도 볼 수 있는 값입니다.
        [SerializeField, Range(0.5f, 1f)] private float aimingZoomRatio = 0.8f;
        // 확대 시작과 해제 때 카메라가 목표 시야각으로 가까워지는 속도입니다.
        // 값이 너무 크면 화면이 순간적으로 튀고 너무 작으면 조준 반응이 답답하므로 기본값은 8로 둡니다.
        [SerializeField, Min(0.1f)] private float aimingZoomSpeed = 8f;

        // SmoothDamp가 프레임 사이의 이동 속도를 기억할 때 사용하는 내부 값입니다.
        private Vector3 velocity;
        private Camera controlledCamera;
        private PlayerCombat targetCombat;
        private float normalFieldOfView;
        private float normalOrthographicSize;
        private bool lensInitialized;

        // 씬 생성 코드나 다른 시스템이 추적 대상을 지정할 수 있게 합니다.
        public void SetTarget(Transform value)
        {
            target = value;
            ResolveTargetCombat();
        }

        private void Awake()
        {
            ResolveCamera();
            ResolveTargetCombat();
        }

        private void OnDisable()
        {
            // 씬을 다시 열거나 컴포넌트를 껐다 켰을 때 이전 조준 확대가 기본 시야처럼 저장되지 않도록 원래 렌즈 값으로 복구합니다.
            if (!lensInitialized || controlledCamera == null) return;
            controlledCamera.fieldOfView = normalFieldOfView;
            controlledCamera.orthographicSize = normalOrthographicSize;
        }

        // LateUpdate는 일반 Update가 모두 끝난 뒤 호출됩니다.
        // 플레이어가 이번 프레임 이동을 마친 위치를 기준으로 추적하므로 카메라가 한 프레임 뒤처져 떨리는 현상을 줄입니다.
        // SmoothDamp는 즉시 순간이동하지 않고 smoothTime 동안 속도를 보간하며 velocity에 계산 상태를 보관합니다.
        private void LateUpdate()
        {
            if (target != null)
                transform.position = Vector3.SmoothDamp(transform.position, target.position + offset, ref velocity, smoothTime);

            UpdateAimZoom();
        }

        private void UpdateAimZoom()
        {
            ResolveCamera();
            ResolveTargetCombat();
            if (controlledCamera == null || !lensInitialized) return;

            // PlayerCombat은 총기가 없거나 전투 조작이 잠긴 경우 IsAiming을 자동으로 false로 만듭니다.
            // CameraFollow는 입력을 다시 읽지 않고 그 최종 상태만 사용하므로 인벤토리 우클릭과 카메라 확대가 충돌하지 않습니다.
            bool isAiming = targetCombat != null && targetCombat.IsAiming;
            float zoomRatio = isAiming ? aimingZoomRatio : 1f;
            // Inspector의 Min 표시는 보통 잘못된 값을 막지만, 오래된 씬이나 텍스트로 수정된 데이터도 안전하게 읽도록 런타임에서 한 번 더 보정합니다.
            float safeZoomSpeed = Mathf.Max(0.1f, aimingZoomSpeed);
            float smoothFactor = 1f - Mathf.Exp(-safeZoomSpeed * Time.unscaledDeltaTime);

            if (controlledCamera.orthographic)
            {
                float targetSize = CalculateZoomedLensValue(normalOrthographicSize, zoomRatio);
                controlledCamera.orthographicSize = Mathf.Lerp(
                    controlledCamera.orthographicSize,
                    targetSize,
                    smoothFactor);
                return;
            }

            float targetFieldOfView = CalculateZoomedLensValue(normalFieldOfView, zoomRatio);
            controlledCamera.fieldOfView = Mathf.Lerp(
                controlledCamera.fieldOfView,
                targetFieldOfView,
                smoothFactor);
        }

        private void ResolveCamera()
        {
            if (controlledCamera == null) controlledCamera = GetComponentInChildren<Camera>(true);
            if (controlledCamera == null || lensInitialized) return;

            // 씬에서 설정한 값을 평상시 기준으로 기억하므로 나중에 Inspector에서 FOV를 바꿔도 코드의 숫자와 충돌하지 않습니다.
            normalFieldOfView = controlledCamera.fieldOfView;
            normalOrthographicSize = controlledCamera.orthographicSize;
            lensInitialized = true;
        }

        private void ResolveTargetCombat()
        {
            if (target == null)
            {
                targetCombat = null;
                return;
            }

            if (targetCombat == null || targetCombat.transform != target)
                targetCombat = target.GetComponent<PlayerCombat>();
        }

        // 원래 FOV 또는 Orthographic Size에 조준 비율을 곱해 확대 목표값을 계산합니다.
        // 두 카메라 방식 모두 렌즈 값이 작아질수록 화면이 확대되므로 같은 공식을 사용할 수 있습니다.
        // 계산을 순수 함수로 분리해 실제 카메라와 마우스가 없어도 EditMode 테스트로 경계값을 확인합니다.
        public static float CalculateZoomedLensValue(float normalValue, float zoomRatio)
        {
            return Mathf.Max(0.01f, normalValue) * Mathf.Clamp(zoomRatio, 0.5f, 1f);
        }
    }
}
