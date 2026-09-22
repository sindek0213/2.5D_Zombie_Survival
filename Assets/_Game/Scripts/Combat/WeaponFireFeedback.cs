using UnityEngine;
using ZombieSurvival.Player;

namespace ZombieSurvival.Combat
{
    // PlayerCombat의 확정된 발사 결과를 짧은 탄도선과 총구 Point Light로 표현합니다.
    // 별도 아트 에셋 없이도 동작하도록 필요한 LineRenderer와 Light를 런타임에 자식 오브젝트로 한 번 생성합니다.
    [RequireComponent(typeof(PlayerCombat))]
    public sealed class WeaponFireFeedback : MonoBehaviour
    {
        [SerializeField] private PlayerCombat combat;
        [SerializeField, Min(0.01f)] private float tracerDuration = 0.07f;
        [SerializeField, Min(0.01f)] private float muzzleLightDuration = 0.05f;
        [SerializeField, Min(0.001f)] private float tracerWidth = 0.035f;
        [SerializeField] private Color missColor = new Color(1f, 0.82f, 0.2f, 1f);
        [SerializeField] private Color hitColor = new Color(1f, 0.28f, 0.12f, 1f);

        private LineRenderer tracer;
        private Light muzzleLight;
        private Material runtimeTracerMaterial;
        private float tracerEndTime;
        private float muzzleLightEndTime;

        private void Awake()
        {
            if (combat == null) combat = GetComponent<PlayerCombat>();
            CreateRuntimeVisuals();
        }

        private void OnEnable()
        {
            if (combat == null) combat = GetComponent<PlayerCombat>();
            if (combat != null) combat.ShotResolved += HandleShotResolved;
        }

        private void Update()
        {
            // 발사 입력과 무관하게 표시 종료 시각만 확인하므로 탄약·명중 판정에는 영향을 주지 않습니다.
            if (tracer != null && tracer.enabled && Time.unscaledTime >= tracerEndTime)
                tracer.enabled = false;
            if (muzzleLight != null && muzzleLight.enabled && Time.unscaledTime >= muzzleLightEndTime)
                muzzleLight.enabled = false;
        }

        private void OnDisable()
        {
            if (combat != null) combat.ShotResolved -= HandleShotResolved;
            if (tracer != null) tracer.enabled = false;
            if (muzzleLight != null) muzzleLight.enabled = false;
        }

        private void OnDestroy()
        {
            // 런타임에 만든 Material은 씬 오브젝트와 별도로 메모리를 사용하므로 플레이어 제거 시 함께 해제합니다.
            if (runtimeTracerMaterial != null) Destroy(runtimeTracerMaterial);
        }

        private void HandleShotResolved(Vector3 origin, Vector3 end, bool hitSomething)
        {
            if (tracer == null || muzzleLight == null) CreateRuntimeVisuals();

            Color color = hitSomething ? hitColor : missColor;
            tracer.startColor = color;
            tracer.endColor = new Color(color.r, color.g, color.b, 0.15f);
            tracer.SetPosition(0, origin);
            tracer.SetPosition(1, end);
            tracer.enabled = true;
            tracerEndTime = Time.unscaledTime + tracerDuration;

            muzzleLight.transform.position = origin;
            muzzleLight.color = color;
            muzzleLight.enabled = true;
            muzzleLightEndTime = Time.unscaledTime + muzzleLightDuration;
        }

        private void CreateRuntimeVisuals()
        {
            if (tracer == null)
            {
                var tracerObject = new GameObject("RuntimeShotTracer");
                tracerObject.transform.SetParent(transform, false);
                tracer = tracerObject.AddComponent<LineRenderer>();
                tracer.useWorldSpace = true;
                tracer.positionCount = 2;
                tracer.startWidth = tracerWidth;
                tracer.endWidth = tracerWidth * 0.35f;
                tracer.numCapVertices = 2;
                tracer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                tracer.receiveShadows = false;

                // LineRenderer에 Material이 없으면 자홍색 오류 재질로 보일 수 있어 빌드에 포함되는 기본 Sprite Shader를 우선 사용합니다.
                Shader shader = Shader.Find("Sprites/Default") ?? Shader.Find("Universal Render Pipeline/Unlit");
                if (shader != null)
                {
                    runtimeTracerMaterial = new Material(shader);
                    tracer.sharedMaterial = runtimeTracerMaterial;
                }
                tracer.enabled = false;
            }

            if (muzzleLight == null)
            {
                var lightObject = new GameObject("RuntimeMuzzleFlash");
                lightObject.transform.SetParent(transform, false);
                muzzleLight = lightObject.AddComponent<Light>();
                muzzleLight.type = LightType.Point;
                muzzleLight.range = 3.2f;
                muzzleLight.intensity = 4.5f;
                muzzleLight.shadows = LightShadows.None;
                muzzleLight.enabled = false;
            }
        }
    }
}
