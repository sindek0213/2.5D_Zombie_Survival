using UnityEngine;

namespace ZombieSurvival.Items
{
    // 현재 열린 레이드 씬이 사용할 지역 드랍 프로필을 런타임 시스템에 알려 주는 연결 컴포넌트입니다.
    // 상자, 바닥 생성 지점과 좀비 시체는 서로 직접 참조하지 않고 이 단일 활성 프로필을 조회합니다.
    [DefaultExecutionOrder(-500)]
    public sealed class RegionLootController : MonoBehaviour
    {
        [SerializeField] private RegionLootProfile profile;

        public static RegionLootProfile ActiveProfile { get; private set; }
        public RegionLootProfile Profile => profile;

        private void Awake()
        {
            // 일반 드랍 컴포넌트의 Start보다 먼저 실행되도록 실행 순서를 앞당겨 바닥 아이템 생성 시점에도 프로필이 준비되게 합니다.
            ActiveProfile = profile;
        }

        private void OnDestroy()
        {
            // 다른 씬이 더 나중에 등록한 프로필까지 오래된 컨트롤러가 지우지 않도록 자기 참조일 때만 비웁니다.
            if (ActiveProfile == profile) ActiveProfile = null;
        }

        // 에디터 생성기가 씬 오브젝트에 지역 에셋을 연결할 때 사용합니다.
        // 플레이 중 프로필을 바꾸는 도구에서도 즉시 활성값을 갱신하도록 현재 활성 오브젝트인지 함께 확인합니다.
        public void Configure(RegionLootProfile regionProfile)
        {
            profile = regionProfile;
            // 씬을 생성하는 Editor 작업 중에는 정적 런타임 상태를 바꾸지 않고 실제 Play 중인 활성 오브젝트만 즉시 반영합니다.
            if (Application.isPlaying && isActiveAndEnabled) ActiveProfile = profile;
        }

        // 정적 상태가 남을 수 있는 Enter Play Mode Options 환경에서도 새 실행마다 깨끗하게 시작합니다.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetRuntimeState()
        {
            ActiveProfile = null;
        }
    }
}
