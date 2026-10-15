using UnityEngine;
using ZombieSurvival.Core;
using ZombieSurvival.Player;

namespace ZombieSurvival.Combat
{
    // PlayerCombat이 실제 탄약을 소비한 발사만 ShotResolved로 알려 줄 때 짧은 임시 총성을 재생합니다.
    // 입력을 직접 읽지 않으므로 탄약 부족이나 재장전 중 클릭에는 소리가 나지 않아 화면 판정과 소리가 항상 일치합니다.
    [RequireComponent(typeof(PlayerCombat))]
    public sealed class WeaponAudioFeedback : MonoBehaviour
    {
        [SerializeField] private PlayerCombat combat;
        [SerializeField, Range(0f, 1f)] private float volume = 0.42f;

        private AudioSource audioSource;
        private AudioClip gunshotClip;

        private void Awake()
        {
            if (combat == null) combat = GetComponent<PlayerCombat>();
            CreateRuntimeAudio();
        }

        private void OnEnable()
        {
            if (combat == null) combat = GetComponent<PlayerCombat>();
            if (combat != null) combat.ShotResolved += HandleShotResolved;
        }

        private void OnDisable()
        {
            if (combat != null) combat.ShotResolved -= HandleShotResolved;
        }

        private void OnDestroy()
        {
            // AudioClip.Create로 만든 런타임 클립은 에셋 데이터베이스가 관리하지 않으므로 플레이어 제거 시 직접 해제합니다.
            if (gunshotClip != null) Destroy(gunshotClip);
        }

        private void HandleShotResolved(Vector3 origin, Vector3 end, bool hitSomething)
        {
            if (audioSource == null || gunshotClip == null) CreateRuntimeAudio();
            audioSource.transform.position = origin;
            // 개별 총성 크기에 설정 화면의 효과음 배율을 곱합니다. 마스터 음량은 AudioListener에서 별도로 전체 출력에 적용됩니다.
            audioSource.PlayOneShot(gunshotClip, GameSettings.ScaleEffectVolume(volume));
        }

        private void CreateRuntimeAudio()
        {
            if (audioSource == null)
            {
                var sourceObject = new GameObject("RuntimeWeaponAudio");
                sourceObject.transform.SetParent(transform, false);
                audioSource = sourceObject.AddComponent<AudioSource>();
                audioSource.playOnAwake = false;
                audioSource.loop = false;
                audioSource.spatialBlend = 0.2f;
            }

            if (gunshotClip == null) gunshotClip = PrototypeSoundFactory.CreateGunshotClip();
        }
    }
}
