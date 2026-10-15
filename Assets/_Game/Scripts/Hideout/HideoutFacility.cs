using UnityEngine;
using ZombieSurvival.Items;

namespace ZombieSurvival.Hideout
{
    // 월드에 놓인 창고 상자나 임무 보드를 PlayerInteraction이 같은 방식으로 찾게 하는 공용 시설 컴포넌트입니다.
    // 시설별 실제 행동은 HideoutWorldController가 종류를 보고 선택하므로 이 클래스에는 UI 세부 로직을 넣지 않습니다.
    [RequireComponent(typeof(Collider))]
    public sealed class HideoutFacility : MonoBehaviour, IInteractable
    {
        [SerializeField] private HideoutFacilityDefinition definition;
        [SerializeField] private HideoutWorldController worldController;
        // 시설 모델의 가장 높은 지점보다 안내 문구를 조금 더 띄우는 거리입니다.
        // 모델 크기가 서로 달라도 Renderer 또는 Collider 경계를 사용하므로 각 시설 위에 자연스럽게 표시됩니다.
        [SerializeField, Min(0f)] private float promptHeightOffset = 0.45f;

        private Renderer facilityRenderer;
        private Color baseColor = Color.white;
        private Vector3 baseScale;

        public HideoutFacilityDefinition Definition => definition;
        public int CurrentLevel => HideoutFacilityStateStore.GetLevel(definition);
        public bool CanUpgrade => definition != null && CurrentLevel < definition.MaximumLevel;
        public bool CanInteract => definition != null && worldController != null;
        public string InteractionPrompt => definition == null
            ? "설정되지 않은 거점 시설"
            : $"[E] {definition.DisplayName} 사용  (Lv.{CurrentLevel})";
        public Vector3 PromptWorldPosition
        {
            get
            {
                // 보이는 모델이 있으면 그 모델의 상단 중앙을 우선 사용합니다.
                // Renderer가 없는 트리거형 시설도 Collider 상단을 사용해 안내가 바닥에 붙지 않게 합니다.
                if (facilityRenderer == null) facilityRenderer = GetComponent<Renderer>();
                if (facilityRenderer != null)
                {
                    Bounds bounds = facilityRenderer.bounds;
                    return new Vector3(bounds.center.x, bounds.max.y + promptHeightOffset, bounds.center.z);
                }

                Collider facilityCollider = GetComponent<Collider>();
                if (facilityCollider != null)
                {
                    Bounds bounds = facilityCollider.bounds;
                    return new Vector3(bounds.center.x, bounds.max.y + promptHeightOffset, bounds.center.z);
                }

                return transform.position + Vector3.up * promptHeightOffset;
            }
        }

        public void Configure(HideoutFacilityDefinition facilityDefinition, HideoutWorldController controller)
        {
            definition = facilityDefinition;
            worldController = controller;
        }

        private void Awake()
        {
            facilityRenderer = GetComponent<Renderer>();
            baseScale = transform.localScale;
            if (facilityRenderer != null && facilityRenderer.sharedMaterial != null)
                baseColor = facilityRenderer.sharedMaterial.color;
            RefreshAppearance();
        }

        // 강화 레벨이 오르면 시설 색을 조금 밝게 하고 높이를 단계적으로 키워 월드에서도 변화가 보이게 합니다.
        // 원본 크기와 색을 기준으로 다시 계산하므로 이 메서드를 여러 번 불러도 크기가 계속 곱해져 커지지 않습니다.
        public void RefreshAppearance()
        {
            if (baseScale == Vector3.zero) baseScale = transform.localScale;
            int level = CurrentLevel;
            float normalized = definition == null || definition.MaximumLevel <= 1
                ? 0f
                : (level - 1f) / (definition.MaximumLevel - 1f);
            transform.localScale = new Vector3(baseScale.x, baseScale.y * (1f + normalized * 0.18f), baseScale.z);

            if (facilityRenderer == null) facilityRenderer = GetComponent<Renderer>();
            if (facilityRenderer != null)
            {
                // renderer.material은 이 오브젝트 전용 인스턴스를 만들어 다른 시설의 색이 함께 바뀌지 않게 합니다.
                facilityRenderer.material.color = Color.Lerp(baseColor, Color.white, normalized * 0.35f);
            }
        }

        public bool Interact()
        {
            // 현재 시설 자신을 넘겨야 화면 관리자가 창고와 임무 보드를 구분하고 한 번에 하나의 화면만 열 수 있습니다.
            return CanInteract && worldController.OpenFacility(this);
        }
    }
}
