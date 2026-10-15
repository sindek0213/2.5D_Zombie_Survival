using System.Collections.Generic;
using UnityEngine;
using ZombieSurvival.Player;

namespace ZombieSurvival.Combat
{
    // PlayerCombat의 확정된 발사 결과를 짧은 탄도선과 총구 Point Light로 표현합니다.
    // 일반 총은 탄도선 하나, 산탄총은 WeaponDefinition의 Pellet Count만큼 LineRenderer를 동시에 사용합니다.
    // 별도 아트 에셋 없이도 동작하도록 필요한 LineRenderer와 Light를 런타임에 자식 오브젝트로 생성해 재사용합니다.
    [RequireComponent(typeof(PlayerCombat))]
    public sealed class WeaponFireFeedback : MonoBehaviour
    {
        [SerializeField] private PlayerCombat combat;
        [SerializeField, Min(0.01f)] private float tracerDuration = 0.07f;
        [SerializeField, Min(0.01f)] private float muzzleLightDuration = 0.05f;
        [SerializeField, Min(0.001f)] private float tracerWidth = 0.035f;
        [SerializeField] private Color missColor = new Color(1f, 0.82f, 0.2f, 1f);
        [SerializeField] private Color hitColor = new Color(1f, 0.28f, 0.12f, 1f);

        // 산탄은 같은 프레임에 여러 탄도선이 보여야 하므로 단일 LineRenderer 대신 작은 런타임 풀을 사용합니다.
        // 표시 시간이 끝난 탄도선을 다음 발사에서 재사용해 매번 GameObject와 Material을 새로 만드는 비용을 피합니다.
        private readonly List<LineRenderer> tracers = new List<LineRenderer>();
        private readonly List<float> tracerEndTimes = new List<float>();
        private Light muzzleLight;
        private Material runtimeTracerMaterial;
        private float muzzleLightEndTime;

        private void Awake()
        {
            if (combat == null) combat = GetComponent<PlayerCombat>();
            CreateRuntimeVisuals();
        }

        private void OnEnable()
        {
            if (combat == null) combat = GetComponent<PlayerCombat>();
            if (combat == null) return;

            // PelletResolved는 탄도선 전용이고 ShotResolved는 한 번만 재생해야 하는 총구 섬광 전용입니다.
            // 둘을 나누지 않으면 5펠릿 샷건에서 총구 빛과 발사음까지 다섯 번 중복 재생될 수 있습니다.
            combat.PelletResolved += HandlePelletResolved;
            combat.ShotResolved += HandleShotResolved;
        }

        private void Update()
        {
            // 발사 입력과 무관하게 각 탄도선의 표시 종료 시각만 확인하므로 탄약·명중 판정에는 영향을 주지 않습니다.
            for (int i = 0; i < tracers.Count; i++)
            {
                if (tracers[i] != null && tracers[i].enabled && Time.unscaledTime >= tracerEndTimes[i])
                    tracers[i].enabled = false;
            }

            if (muzzleLight != null && muzzleLight.enabled && Time.unscaledTime >= muzzleLightEndTime)
                muzzleLight.enabled = false;
        }

        private void OnDisable()
        {
            if (combat != null)
            {
                combat.PelletResolved -= HandlePelletResolved;
                combat.ShotResolved -= HandleShotResolved;
            }

            for (int i = 0; i < tracers.Count; i++)
            {
                if (tracers[i] != null) tracers[i].enabled = false;
            }
            if (muzzleLight != null) muzzleLight.enabled = false;
        }

        private void OnDestroy()
        {
            // 런타임에 만든 Material은 씬 오브젝트와 별도로 메모리를 사용하므로 플레이어 제거 시 함께 해제합니다.
            if (runtimeTracerMaterial != null) Destroy(runtimeTracerMaterial);
        }

        private void HandlePelletResolved(Vector3 origin, Vector3 end, bool hitSomething)
        {
            CreateRuntimeVisuals();
            int tracerIndex = AcquireTracerIndex();
            LineRenderer tracer = tracers[tracerIndex];

            Color color = hitSomething ? hitColor : missColor;
            tracer.startColor = color;
            tracer.endColor = new Color(color.r, color.g, color.b, 0.15f);
            tracer.SetPosition(0, origin);
            tracer.SetPosition(1, end);
            tracer.enabled = true;
            tracerEndTimes[tracerIndex] = Time.unscaledTime + tracerDuration;
        }

        private void HandleShotResolved(Vector3 origin, Vector3 end, bool hitSomething)
        {
            CreateRuntimeVisuals();

            // 총구 빛은 펠릿마다가 아니라 쉘 또는 탄환 한 발당 한 번만 켭니다.
            // 색상은 대표 탄도가 맞았는지에 따라 바꾸되 실제 각 펠릿 탄도선은 위 이벤트에서 개별 명중 색상을 사용합니다.
            Color color = hitSomething ? hitColor : missColor;
            muzzleLight.transform.position = origin;
            muzzleLight.color = color;
            muzzleLight.enabled = true;
            muzzleLightEndTime = Time.unscaledTime + muzzleLightDuration;
        }

        private void CreateRuntimeVisuals()
        {
            if (runtimeTracerMaterial == null)
            {
                // LineRenderer에 Material이 없으면 자홍색 오류 재질로 보일 수 있어 빌드에 포함되는 기본 Sprite Shader를 우선 사용합니다.
                Shader shader = Shader.Find("Sprites/Default") ?? Shader.Find("Universal Render Pipeline/Unlit");
                if (shader != null) runtimeTracerMaterial = new Material(shader);
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

        // 현재 꺼져 있거나 이미 표시 시간이 끝난 탄도선을 찾아 반환합니다.
        // 같은 프레임에 다섯 펠릿이 들어오면 앞의 네 탄도선은 아직 활성 상태이므로 다섯 번째까지 자동으로 새 슬롯이 만들어집니다.
        private int AcquireTracerIndex()
        {
            for (int i = 0; i < tracers.Count; i++)
            {
                if (tracers[i] == null || !tracers[i].enabled || Time.unscaledTime >= tracerEndTimes[i])
                {
                    if (tracers[i] != null) tracers[i].enabled = false;
                    return i;
                }
            }

            LineRenderer tracer = CreateTracer(tracers.Count);
            tracers.Add(tracer);
            tracerEndTimes.Add(0f);
            return tracers.Count - 1;
        }

        private LineRenderer CreateTracer(int index)
        {
            var tracerObject = new GameObject("RuntimeShotTracer_" + (index + 1).ToString("D2"));
            tracerObject.transform.SetParent(transform, false);
            LineRenderer tracer = tracerObject.AddComponent<LineRenderer>();
            tracer.useWorldSpace = true;
            tracer.positionCount = 2;
            tracer.startWidth = tracerWidth;
            tracer.endWidth = tracerWidth * 0.35f;
            tracer.numCapVertices = 2;
            tracer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            tracer.receiveShadows = false;
            if (runtimeTracerMaterial != null) tracer.sharedMaterial = runtimeTracerMaterial;
            tracer.enabled = false;
            return tracer;
        }
    }
}
