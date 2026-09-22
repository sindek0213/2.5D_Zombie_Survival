using UnityEngine;

namespace ZombieSurvival.Player
{
    // 화면 좌표인 마우스 위치를 3D 월드 좌표로 바꾸고 플레이어의 수평 조준 방향을 계산합니다.
    // 카메라는 비스듬히 내려다보므로 단순히 마우스 X/Y 값을 월드 X/Z로 사용할 수 없습니다.
    // 카메라에서 마우스를 통과하는 Ray를 만든 뒤 수평 Plane과 교차시켜 실제 월드 지점을 구합니다.
    public sealed class PlayerAim : MonoBehaviour
    {
        // aimMarker는 지면의 조준점, visual은 좌우 반전할 스프라이트 루트입니다.
        [SerializeField] private Camera aimCamera;
        [SerializeField] private Transform aimMarker;
        [SerializeField] private Transform visual;
        [SerializeField] private Transform aimOrigin;
        [SerializeField] private float groundHeight;
        [SerializeField, Min(0f)] private float weaponHeight = 1f;
        private PlayerSpriteAnimator spriteAnimator;

        // 다른 전투 시스템이 사용할 수 있는 현재 월드 조준 방향입니다.
        public Vector3 AimDirection { get; private set; } = Vector3.forward;

        // Start는 모든 오브젝트의 Awake가 끝난 뒤 첫 Update 전에 한 번 호출됩니다.
        // 생성기가 카메라를 직접 연결하지 않았을 경우 MainCamera 태그가 붙은 카메라를 자동으로 찾습니다.
        private void Start()
        {
            if (aimCamera == null) aimCamera = Camera.main;
            // 새 8방향 그림 애니메이터가 있으면 그 컴포넌트가 방향별 Sprite를 직접 고릅니다.
            // 참조를 한 번만 저장해 두고 아래 좌우 반전 방식과 동시에 적용되지 않게 구분합니다.
            spriteAnimator = GetComponent<PlayerSpriteAnimator>();
        }

        // 매 프레임 현재 마우스 위치를 다시 읽어 조준점과 AimDirection을 즉시 갱신합니다.
        // 카메라가 없거나 Ray가 수평 Plane과 만나지 않으면 기존 조준 방향을 유지하고 이번 프레임 처리를 끝냅니다.
        private void Update()
        {
            if (aimCamera == null) return;

            // 화면 좌표인 마우스 위치를 카메라에서 출발하는 월드 Ray로 변환합니다.
            Ray ray = aimCamera.ScreenPointToRay(Input.mousePosition);
            if (!TryProjectToGround(ray, groundHeight, out Vector3 groundPoint)) return;

            // 원근 카메라에서는 같은 화면 픽셀이라도 Y 높이에 따라 교차하는 XZ 위치가 달라집니다.
            // 바닥 교차점은 조준 마커 표시에만 쓰고, 총알 방향은 실제 총구 높이의 Plane 교차점으로 계산합니다.
            // 이 과정을 생략하면 총알은 수평으로 나가더라도 화면의 마우스 커서와 다른 방향으로 보입니다.
            // WeaponSocket이 연결되어 있으면 실제 총구 위치를 사용하고 없으면 기본 높이를 사용합니다.
            Vector3 weaponOrigin = aimOrigin != null
                ? aimOrigin.position
                : transform.position + Vector3.up * weaponHeight;
            float aimPlaneHeight = weaponOrigin.y;
            if (!TryProjectToGround(ray, aimPlaneHeight, out Vector3 weaponAimPoint)) return;

            // 높이 차이는 제거해 조준 방향이 XZ 평면에만 존재하도록 합니다.
            Vector3 direction = weaponAimPoint - weaponOrigin;
            direction.y = 0f;
            if (direction.sqrMagnitude > 0.001f)
                AimDirection = direction.normalized;

            // 조준점이 바닥과 겹쳐 깜빡이지 않도록 살짝 위에 표시합니다.
            if (aimMarker != null) aimMarker.position = groundPoint + Vector3.up * 0.03f;

            // 조준 방향의 X 부호에 따라 2D 스프라이트만 좌우 반전합니다.
            // 단일 임시 그림은 왼쪽을 볼 때 좌우 반전해야 하지만, 8방향 시트에는 왼쪽과 오른쪽 그림이 이미 따로 들어 있습니다.
            // 두 방식을 동시에 적용하면 왼쪽 행이 다시 오른쪽으로 뒤집히므로 새 애니메이터가 없는 이전 프리팹에서만 반전합니다.
            if (spriteAnimator == null && visual != null && Mathf.Abs(AimDirection.x) > 0.01f)
            {
                Vector3 scale = visual.localScale;
                scale.x = Mathf.Abs(scale.x) * (AimDirection.x < 0f ? -1f : 1f);
                visual.localScale = scale;
            }
        }

        // ray는 카메라에서 시작하는 선이고 height는 계산할 수평면의 월드 Y 높이입니다.
        // 교차하면 point에 월드 좌표를 기록하고 true를 반환합니다.
        // Ray가 평면과 평행하거나 반대 방향이면 교차할 수 없으므로 point를 기본값으로 두고 false를 반환합니다.
        public static bool TryProjectToGround(Ray ray, float height, out Vector3 point)
        {
            var plane = new Plane(Vector3.up, new Vector3(0f, height, 0f));
            if (plane.Raycast(ray, out float distance))
            {
                point = ray.GetPoint(distance);
                return true;
            }
            point = default;
            return false;
        }
    }
}
