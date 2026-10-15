using UnityEngine;
using UnityEngine.UI;
using ZombieSurvival.Combat;
using ZombieSurvival.Player;
using ZombieSurvival.Core;

namespace ZombieSurvival.UI
{
    // 플레이어가 피해를 받으면 화면 전체의 Canvas Image를 짧게 붉게 표시합니다.
    // Health.Damaged 이벤트만 읽고 체력·사망 상태는 변경하지 않으며, unscaledDeltaTime으로 게임 시간 정지와 무관하게 사라집니다.
    public sealed class PlayerDamageHud : MonoBehaviour
    {
        [SerializeField] private Health playerHealth;
        [SerializeField] private Image damageOverlay;
        [SerializeField, Range(0.05f, 0.8f)] private float peakAlpha = 0.28f;
        [SerializeField, Min(0.05f)] private float fadeDuration = 0.45f;
        // 피해 오버레이도 공용 실패색을 기준으로 하되 실제 알파 변화는 기존 피격 연출 코드가 계속 담당합니다.
        [SerializeField] private Color damageColor = SurvivalUiTheme.Danger;

        private float currentAlpha;
        private bool subscribed;

        public float CurrentAlpha => currentAlpha;

        public void SetPlayerHealth(Health value)
        {
            Unsubscribe();
            playerHealth = value;
            if (isActiveAndEnabled) Subscribe();
        }

        public void ConfigureView(Image overlay)
        {
            damageOverlay = overlay;
            ApplyOverlay();
        }

        private void Awake()
        {
            if (playerHealth == null)
            {
                PlayerCombat combat = FindObjectOfType<PlayerCombat>();
                if (combat != null) playerHealth = combat.GetComponent<Health>();
            }
            ApplyOverlay();
        }

        private void OnEnable() => Subscribe();

        private void Update()
        {
            if (currentAlpha > 0f)
                currentAlpha = Mathf.MoveTowards(currentAlpha, 0f, peakAlpha * Time.unscaledDeltaTime / fadeDuration);
            ApplyOverlay();
        }

        private void ApplyOverlay()
        {
            if (damageOverlay == null) return;
            Color color = damageColor;
            color.a = currentAlpha;
            damageOverlay.color = color;
            damageOverlay.raycastTarget = false;
            damageOverlay.gameObject.SetActive(currentAlpha > 0.001f);
        }

        private void HandleDamaged(float appliedDamage, Vector3 hitPoint)
        {
            if (appliedDamage <= 0f) return;
            currentAlpha = peakAlpha * GameSettings.DamageEffectIntensity;
            ApplyOverlay();
        }

        private void Subscribe()
        {
            if (subscribed || playerHealth == null) return;
            playerHealth.Damaged += HandleDamaged;
            subscribed = true;
        }

        private void Unsubscribe()
        {
            if (!subscribed || playerHealth == null) return;
            playerHealth.Damaged -= HandleDamaged;
            subscribed = false;
        }

        private void OnDisable()
        {
            Unsubscribe();
            currentAlpha = 0f;
            ApplyOverlay();
        }
    }
}
