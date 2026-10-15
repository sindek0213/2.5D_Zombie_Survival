using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace ZombieSurvival.CameraSystem
{
    // 카메라와 플레이어 사이를 가린 건물 조각의 Renderer를 반투명하게 바꾸는 대상 컴포넌트입니다.
    // Collider는 전혀 끄지 않으므로 벽·2층 바닥·천장의 충돌과 NavMesh 판정은 유지되고 화면에 보이는 재질만 바뀝니다.
    [DisallowMultipleComponent]
    public sealed class OccluderFadeTarget : MonoBehaviour
    {
        [Header("가려질 때의 표시 설정")]
        // 0은 완전히 보이지 않고 1은 원래 모습입니다. 기본 0.18은 건물 위치를 희미하게 보여 주면서 내부 플레이어도 확인할 수 있는 값입니다.
        [SerializeField, Range(0.05f, 0.95f)] private float occludedAlpha = 0.18f;
        // 초당 바뀌는 알파 양입니다. 즉시 사라지지 않고 짧게 흐려져 카메라 이동 중 화면이 번쩍이는 현상을 줄입니다.
        [SerializeField, Min(0.1f)] private float fadeSpeed = 5f;
        // 비워 두면 이 오브젝트 아래의 모든 Renderer를 자동 수집합니다. 특정 메시만 흐리게 만들고 싶을 때만 Inspector에서 직접 지정합니다.
        [SerializeField] private Renderer[] targetRenderers;

        // 원래 Material은 다시 선명해졌을 때 그대로 복구해야 하므로 Renderer마다 배열 형태로 보관합니다.
        // 반투명 Material은 원본을 수정하지 않고 런타임 복사본으로 만들기 때문에 다른 벽이 함께 투명해지는 문제를 막습니다.
        private readonly List<Material[]> originalMaterials = new List<Material[]>();
        private readonly List<Material[]> transparentMaterials = new List<Material[]>();
        private bool materialsPrepared;
        private bool transparentMaterialsAssigned;
        private bool isOccluded;
        private float currentAlpha = 1f;

        public bool IsOccluded => isOccluded;
        public float CurrentAlpha => currentAlpha;

        private void Awake()
        {
            ResolveRenderers();
        }

        private void Update()
        {
            float targetAlpha = isOccluded ? occludedAlpha : 1f;
            currentAlpha = MoveFadeAlpha(currentAlpha, targetAlpha, fadeSpeed, Time.deltaTime);

            // 가려지기 시작하는 첫 프레임에만 복사 Material을 만들고 이후에는 같은 복사본의 색만 바꿔 불필요한 메모리 할당을 피합니다.
            if (isOccluded || currentAlpha < 0.999f)
            {
                EnsureTransparentMaterialsAssigned();
                ApplyAlpha(currentAlpha);
            }

            // 완전히 원래 밝기로 돌아오면 불투명 원본 Material을 다시 연결합니다.
            // 평상시에는 투명 렌더 큐를 쓰지 않으므로 벽이 겹치는 장소에서도 깊이 정렬이 안정적으로 유지됩니다.
            if (!isOccluded && currentAlpha >= 0.999f && transparentMaterialsAssigned)
                RestoreOriginalMaterials();
        }

        private void OnDisable()
        {
            // 씬 종료나 오브젝트 비활성화 중 반투명 상태가 남지 않게 즉시 원래 Material로 돌립니다.
            isOccluded = false;
            currentAlpha = 1f;
            RestoreOriginalMaterials();
        }

        private void OnDestroy()
        {
            RestoreOriginalMaterials();

            // 런타임에 만든 Material 복사본은 씬 에셋에 저장되지 않으므로 컴포넌트가 파괴될 때 직접 정리합니다.
            for (int rendererIndex = 0; rendererIndex < transparentMaterials.Count; rendererIndex++)
            {
                Material[] materials = transparentMaterials[rendererIndex];
                for (int materialIndex = 0; materialIndex < materials.Length; materialIndex++)
                {
                    Material material = materials[materialIndex];
                    if (material == null) continue;
                    if (Application.isPlaying) Destroy(material);
                    else DestroyImmediate(material);
                }
            }
        }

        // PlayerOcclusionController가 이번 프레임에 이 건물을 가림 대상으로 발견했는지 전달합니다.
        // 상태만 저장하고 실제 보간은 Update에서 처리해 여러 Collider가 같은 건물을 감지해도 속도가 중복 적용되지 않습니다.
        public void SetOccluded(bool value)
        {
            isOccluded = value;
            if (value) EnsureTransparentMaterialsAssigned();
        }

        private void ResolveRenderers()
        {
            if (targetRenderers != null && targetRenderers.Length > 0) return;
            targetRenderers = GetComponentsInChildren<Renderer>(true);
        }

        private void PrepareTransparentMaterials()
        {
            if (materialsPrepared) return;
            ResolveRenderers();

            originalMaterials.Clear();
            transparentMaterials.Clear();
            for (int rendererIndex = 0; rendererIndex < targetRenderers.Length; rendererIndex++)
            {
                Renderer targetRenderer = targetRenderers[rendererIndex];
                Material[] originals = targetRenderer != null ? targetRenderer.sharedMaterials : new Material[0];
                var transparentCopies = new Material[originals.Length];

                for (int materialIndex = 0; materialIndex < originals.Length; materialIndex++)
                {
                    Material original = originals[materialIndex];
                    if (original == null) continue;

                    var transparentCopy = new Material(original)
                    {
                        name = original.name + " (Occluder Fade Runtime)"
                    };
                    ConfigureTransparentRendering(transparentCopy);
                    transparentCopies[materialIndex] = transparentCopy;
                }

                originalMaterials.Add(originals);
                transparentMaterials.Add(transparentCopies);
            }

            materialsPrepared = true;
        }

        private void EnsureTransparentMaterialsAssigned()
        {
            if (transparentMaterialsAssigned) return;
            PrepareTransparentMaterials();

            for (int i = 0; i < targetRenderers.Length; i++)
            {
                if (targetRenderers[i] != null)
                    targetRenderers[i].sharedMaterials = transparentMaterials[i];
            }

            transparentMaterialsAssigned = true;
        }

        private void RestoreOriginalMaterials()
        {
            if (!transparentMaterialsAssigned || targetRenderers == null) return;

            for (int i = 0; i < targetRenderers.Length && i < originalMaterials.Count; i++)
            {
                if (targetRenderers[i] != null)
                    targetRenderers[i].sharedMaterials = originalMaterials[i];
            }

            transparentMaterialsAssigned = false;
        }

        private void ApplyAlpha(float alpha)
        {
            float safeAlpha = Mathf.Clamp01(alpha);
            for (int rendererIndex = 0; rendererIndex < transparentMaterials.Count; rendererIndex++)
            {
                Material[] materials = transparentMaterials[rendererIndex];
                Material[] originals = originalMaterials[rendererIndex];
                for (int materialIndex = 0; materialIndex < materials.Length; materialIndex++)
                {
                    Material material = materials[materialIndex];
                    Material original = originals[materialIndex];
                    if (material == null || original == null) continue;

                    // URP Lit은 _BaseColor를 사용하고 내장 Standard Shader는 _Color를 사용하므로 두 경우를 모두 지원합니다.
                    string colorProperty = material.HasProperty("_BaseColor") ? "_BaseColor" : "_Color";
                    if (!material.HasProperty(colorProperty) || !original.HasProperty(colorProperty)) continue;
                    Color color = original.GetColor(colorProperty);
                    color.a *= safeAlpha;
                    material.SetColor(colorProperty, color);
                }
            }
        }

        private static void ConfigureTransparentRendering(Material material)
        {
            // URP Lit Material을 Surface Type=Transparent, Alpha Blend, ZWrite Off 상태로 바꿉니다.
            // Standard Shader 속성도 함께 설정해 렌더 파이프라인이 바뀌어도 회색 상자 테스트가 계속 동작하게 합니다.
            if (material.HasProperty("_Surface")) material.SetFloat("_Surface", 1f);
            if (material.HasProperty("_Blend")) material.SetFloat("_Blend", 0f);
            if (material.HasProperty("_Mode")) material.SetFloat("_Mode", 3f);
            if (material.HasProperty("_SrcBlend")) material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            if (material.HasProperty("_DstBlend")) material.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            if (material.HasProperty("_ZWrite")) material.SetFloat("_ZWrite", 0f);
            material.DisableKeyword("_ALPHATEST_ON");
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.EnableKeyword("_ALPHABLEND_ON");
            material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            material.renderQueue = (int)RenderQueue.Transparent;
        }

        // MoveTowards를 순수 함수로 분리해 실제 카메라 없이도 페이드 속도와 경계값을 EditMode 테스트할 수 있습니다.
        public static float MoveFadeAlpha(float current, float target, float speed, float deltaTime)
        {
            float safeCurrent = Mathf.Clamp01(current);
            float safeTarget = Mathf.Clamp01(target);
            float maxChange = Mathf.Max(0f, speed) * Mathf.Max(0f, deltaTime);
            return Mathf.MoveTowards(safeCurrent, safeTarget, maxChange);
        }
    }
}
