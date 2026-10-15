using UnityEngine;

namespace ZombieSurvival.CameraSystem
{
    // 3D 공간의 SpriteRenderer가 비스듬한 카메라에서 납작하게 보이지 않도록 카메라 회전에 맞춥니다.
    // 물리 루트가 아니라 Visual 자식에만 붙이므로 화면 표현의 회전이 이동 방향이나 Collider에 영향을 주지 않습니다.
    public sealed class SpriteBillboard : MonoBehaviour
    {
        private Camera targetCamera;
        private float screenRollAngle;

        // 카메라를 향하는 기본 회전 위에 화면 안쪽 회전만 더합니다. 플레이어의 구르기 표현이 물리 루트를 회전시키지 않게 분리합니다.
        public void SetScreenRollAngle(float angle) => screenRollAngle = angle;

        // 카메라 이동이 끝난 뒤 회전을 맞춰 한 프레임 늦게 보이는 현상을 방지합니다.
        private void LateUpdate()
        {
            // 반복 검색을 피하기 위해 MainCamera 참조를 처음 한 번만 저장합니다.
            if (targetCamera == null) targetCamera = Camera.main;
            if (targetCamera == null) return;
            transform.rotation = Quaternion.LookRotation(targetCamera.transform.forward, targetCamera.transform.up)
                                 * Quaternion.AngleAxis(screenRollAngle, Vector3.forward);
        }
    }
}
