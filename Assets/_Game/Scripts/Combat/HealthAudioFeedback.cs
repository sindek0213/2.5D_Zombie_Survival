using UnityEngine;

namespace ZombieSurvival.Combat
{
    public enum HealthAudioRole
    {
        Player,
        Zombie
    }

    // Health의 피격과 사망 이벤트를 받아 플레이어와 좀비에 맞는 임시 효과음을 재생합니다.
    // 치명타에서는 일반 피격음을 건너뛰고 사망음만 남겨 같은 프레임에 두 소리가 겹쳐 커지는 현상을 막습니다.
    [RequireComponent(typeof(Health))]
    public sealed class HealthAudioFeedback : MonoBehaviour
    {
        [SerializeField] private Health health;
        [SerializeField] private HealthAudioRole role;
        [SerializeField, Range(0f, 1f)] private float impactVolume = 0.38f;
        [SerializeField, Range(0f, 1f)] private float deathVolume = 0.5f;

        private AudioSource audioSource;
        private AudioClip impactClip;
        private AudioClip deathClip;

        // 에디터 생성기가 플레이어와 좀비 역할을 명시적으로 정해 서로 다른 음색과 공간감을 사용하도록 합니다.
        public void Configure(Health source, HealthAudioRole value)
        {
            health = source;
            role = value;
        }

        private void Awake()
        {
            if (health == null) health = GetComponent<Health>();
            CreateRuntimeAudio();
        }

        private void OnEnable()
        {
            if (health == null) health = GetComponent<Health>();
            if (health == null) return;
            health.Damaged += HandleDamaged;
            health.Died += HandleDied;
        }

        private void OnDisable()
        {
            if (health == null) return;
            health.Damaged -= HandleDamaged;
            health.Died -= HandleDied;
        }

        private void OnDestroy()
        {
            if (impactClip != null) Destroy(impactClip);
            // 사망음은 오브젝트가 사라진 뒤에도 재생되도록 별도 임시 AudioSource에 넘기므로 null이 아닐 때만 여기서 해제합니다.
            if (deathClip != null) Destroy(deathClip);
        }

        private void HandleDamaged(float appliedDamage, Vector3 hitPoint)
        {
            if (appliedDamage <= 0f || health.CurrentHealth <= 0f) return;
            if (audioSource == null || impactClip == null) CreateRuntimeAudio();
            audioSource.transform.position = hitPoint;
            audioSource.PlayOneShot(impactClip, impactVolume);
        }

        private void HandleDied()
        {
            if (deathClip == null) CreateRuntimeAudio();

            // Health는 이벤트 직후 캐릭터 루트를 제거하므로 PlayClipAtPoint를 사용해 독립된 임시 소스에서 끝까지 재생합니다.
            AudioSource.PlayClipAtPoint(deathClip, transform.position, deathVolume);
            Destroy(deathClip, deathClip.length + 0.1f);
            deathClip = null;
        }

        private void CreateRuntimeAudio()
        {
            bool isPlayer = role == HealthAudioRole.Player;
            if (audioSource == null)
            {
                var sourceObject = new GameObject("RuntimeHealthAudio");
                sourceObject.transform.SetParent(transform, false);
                audioSource = sourceObject.AddComponent<AudioSource>();
                audioSource.playOnAwake = false;
                audioSource.loop = false;
                audioSource.spatialBlend = isPlayer ? 0.1f : 1f;
                audioSource.minDistance = 1f;
                audioSource.maxDistance = 18f;
            }

            if (impactClip == null) impactClip = PrototypeSoundFactory.CreateImpactClip(isPlayer);
            if (deathClip == null) deathClip = PrototypeSoundFactory.CreateDeathClip(isPlayer);
        }
    }
}
