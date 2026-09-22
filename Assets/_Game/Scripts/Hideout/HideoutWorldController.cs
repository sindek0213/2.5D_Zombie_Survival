using UnityEngine;
using ZombieSurvival.Player;
using ZombieSurvival.UI;

namespace ZombieSurvival.Hideout
{
    // 거점 월드의 플레이어, 카메라와 시설별 Canvas를 연결하고 한 번에 하나의 시설 화면만 열리게 합니다.
    // 시설이 늘어나도 플레이어 입력 코드는 그대로 두고 OpenFacility의 종류 분기에 새 화면만 연결하면 됩니다.
    public sealed class HideoutWorldController : MonoBehaviour
    {
        [SerializeField] private HideoutResultHud storageHud;
        [SerializeField] private HideoutMissionBoardHud missionBoardHud;
        [SerializeField] private HideoutFacilityUpgradeHud facilityUpgradeHud;
        [SerializeField] private HideoutWorldHud worldHud;
        [SerializeField] private HideoutController hideoutController;
        [SerializeField] private PlayerInteraction playerInteraction;
        [SerializeField] private PlayerControlGate controlGate;
        [SerializeField] private Transform player;
        [SerializeField] private Transform cameraRig;
        [SerializeField] private Vector3 cameraOffset = new Vector3(0f, 11f, -8f);
        [SerializeField, Min(0f)] private float cameraSmoothTime = 0.12f;

        private GameObject activeScreen;
        private HideoutFacility activeFacility;
        private Vector3 cameraVelocity;

        public bool IsFacilityOpen => activeFacility != null;
        public PlayerInteraction PlayerInteraction => playerInteraction;
        public string ActiveMissionSummary => hideoutController != null
            ? hideoutController.ActiveMissionSummary
            : "진행 중인 임무 없음";

        public void Configure(
            HideoutResultHud storage,
            HideoutMissionBoardHud missionBoard,
            HideoutFacilityUpgradeHud upgradePanel,
            HideoutWorldHud overlay,
            HideoutController hideout,
            PlayerInteraction interaction,
            PlayerControlGate gate,
            Transform playerTransform,
            Transform cameraTransform)
        {
            storageHud = storage;
            missionBoardHud = missionBoard;
            facilityUpgradeHud = upgradePanel;
            worldHud = overlay;
            hideoutController = hideout;
            playerInteraction = interaction;
            controlGate = gate;
            player = playerTransform;
            cameraRig = cameraTransform;
        }

        private void Start()
        {
            // 첫 프레임에는 월드를 보여 주고, 창고와 임무 보드는 각 시설에서 E키를 눌렀을 때만 활성화합니다.
            if (storageHud != null) storageHud.gameObject.SetActive(false);
            if (missionBoardHud != null) missionBoardHud.gameObject.SetActive(false);
            if (facilityUpgradeHud != null) facilityUpgradeHud.gameObject.SetActive(false);
            activeScreen = null;
            activeFacility = null;

            // 거점에서는 총을 쏘거나 포만감이 감소하지 않지만 이동과 시설 상호작용은 그대로 사용합니다.
            if (player != null)
            {
                PlayerAim aim = player.GetComponent<PlayerAim>();
                PlayerCombat combat = player.GetComponent<PlayerCombat>();
                PlayerSurvival survival = player.GetComponent<PlayerSurvival>();
                if (aim != null) aim.enabled = false;
                if (combat != null) combat.enabled = false;
                if (survival != null) survival.enabled = false;
            }
        }

        private void Update()
        {
            if (IsFacilityOpen && (Input.GetKeyDown(KeyCode.Escape) || Input.GetKeyDown(KeyCode.Tab)))
                CloseFacility();
        }

        private void LateUpdate()
        {
            // CameraFollow를 별도로 붙이지 않은 거점에서도 같은 부드러운 추적 감각을 유지합니다.
            if (player == null || cameraRig == null) return;
            cameraRig.position = Vector3.SmoothDamp(
                cameraRig.position,
                player.position + cameraOffset,
                ref cameraVelocity,
                cameraSmoothTime);
        }

        public bool OpenFacility(HideoutFacility facility)
        {
            if (facility?.Definition == null || IsFacilityOpen) return false;

            switch (facility.Definition.FacilityType)
            {
                case HideoutFacilityType.Storage:
                    activeScreen = storageHud != null ? storageHud.gameObject : null;
                    break;
                case HideoutFacilityType.MissionBoard:
                    activeScreen = missionBoardHud != null ? missionBoardHud.gameObject : null;
                    break;
                default:
                    // 제작대·치료소·발전기처럼 전용 화면이 아직 필요 없는 시설은 공용 강화 패널 자체를 주 화면으로 사용합니다.
                    activeScreen = facilityUpgradeHud != null ? facilityUpgradeHud.gameObject : null;
                    break;
            }

            if (activeScreen == null) return false;
            activeFacility = facility;
            activeScreen.SetActive(true);
            facilityUpgradeHud?.ShowFacility(facility);
            controlGate?.AcquireLock(this);
            worldHud?.RefreshImmediately();
            return true;
        }

        public void CloseFacility()
        {
            if (activeScreen != null) activeScreen.SetActive(false);
            // 창고나 임무 화면과 함께 떠 있던 강화 패널도 항상 닫아 다음 시설 정보가 남지 않게 합니다.
            if (facilityUpgradeHud != null) facilityUpgradeHud.HideFacility();
            activeScreen = null;
            activeFacility = null;
            controlGate?.ReleaseLock(this);
            worldHud?.RefreshImmediately();
        }

        private void OnDisable()
        {
            // 씬 전환이나 Play 종료 중에도 이 관리자가 남긴 조작 잠금이 플레이어에 남지 않도록 자신의 잠금만 해제합니다.
            if (controlGate != null) controlGate.ReleaseLock(this);
            activeScreen = null;
            activeFacility = null;
        }
    }
}
