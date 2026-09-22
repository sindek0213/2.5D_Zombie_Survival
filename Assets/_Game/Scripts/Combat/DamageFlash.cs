using UnityEngine;

namespace ZombieSurvival.Combat
{
    // Health가 피해를 받는 순간 캐릭터 SpriteRenderer의 색을 잠깐 바꿔 명중 여부를 화면에서 바로 알 수 있게 합니다.
    // 피해 계산에는 관여하지 않고 Damaged 이벤트만 구독하므로 플레이어와 좀비가 같은 표현 코드를 재사용합니다.
    [RequireComponent(typeof(Health))]
    public sealed class DamageFlash : MonoBehaviour
    {
        [SerializeField] private Health health;
        [SerializeField] private SpriteRenderer targetRenderer;
        [SerializeField] private Color flashColor = new Color(1f, 0.18f, 0.12f, 1f);
        [SerializeField, Min(0.01f)] private float flashDuration = 0.12f;

        private Color originalColor = Color.white;
        private float flashEndTime;
        private bool isFlashing;

        // 에디터 생성기가 정확한 체력과 Visual 렌더러를 연결할 때 사용하는 설정 진입점입니다.
        public void Configure(Health source, SpriteRenderer renderer)
        {
            health = source;
            targetRenderer = renderer;
        }

        // 스폰 시스템이 종류별 표시색을 바꿀 때 피격 점멸이 끝난 뒤 돌아갈 원래 색도 함께 갱신합니다.
        // 현재 점멸 중이 아니라면 SpriteRenderer에도 즉시 적용해 일반·빠른·튼튼한 좀비를 눈으로 구분하게 합니다.
        public void ConfigureBaseColor(Color baseColor)
        {
            if (targetRenderer == null) targetRenderer = GetComponentInChildren<SpriteRenderer>();
            originalColor = baseColor;
            if (targetRenderer != null && !isFlashing) targetRenderer.color = baseColor;
        }

        private void Awake()
        {
            if (health == null) health = GetComponent<Health>();
            if (targetRenderer == null) targetRenderer = GetComponentInChildren<SpriteRenderer>();
            if (targetRenderer != null) originalColor = targetRenderer.color;
        }

        private void OnEnable()
        {
            if (health == null) health = GetComponent<Health>();
            if (health != null) health.Damaged += HandleDamaged;
        }

        private void Update()
        {
            // unscaledTime을 사용해 결과 화면이나 일시정지로 게임 시간이 멈춰도 피격색이 원래 색으로 돌아오게 합니다.
            if (!isFlashing || Time.unscaledTime < flashEndTime) return;
            RestoreOriginalColor();
        }

        private void OnDisable()
        {
            if (health != null) health.Damaged -= HandleDamaged;
            RestoreOriginalColor();
        }

        private void HandleDamaged(float appliedDamage, Vector3 hitPoint)
        {
            if (targetRenderer == null || appliedDamage <= 0f) return;

            // 연속으로 맞으면 종료 시각을 뒤로 갱신하여 마지막 피격부터 flashDuration 동안 색을 유지합니다.
            targetRenderer.color = flashColor;
            flashEndTime = Time.unscaledTime + flashDuration;
            isFlashing = true;
        }

        private void RestoreOriginalColor()
        {
            if (targetRenderer != null) targetRenderer.color = originalColor;
            isFlashing = false;
        }
    }
}
