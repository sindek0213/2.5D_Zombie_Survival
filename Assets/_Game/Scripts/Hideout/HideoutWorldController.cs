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
        [SerializeField] private HideoutTraderHud traderHud;
        [SerializeField] private HideoutDeploymentHud deploymentHud;
        [SerializeField] private HideoutFacilityUpgradeHud facilityUpgradeHud;
        [SerializeField] private InventoryHud playerInventoryHud;
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

        // Tab으로 연 거점 인벤토리는 특정 시설 참조가 없어도 화면이 열려 있으므로 activeScreen을 기준으로 판정합니다.
        // 이 값은 시설 중복 열기, 튜토리얼 진행과 플레이어 조작 잠금 상태가 모두 같은 기준을 사용하게 합니다.
        public bool IsFacilityOpen => activeScreen != null;
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

        // 상인 화면은 기존 Configure 시그니처를 바꾸지 않고 별도로 연결합니다.
        // 이렇게 하면 이미 저장된 Hideout 씬과 기존 테스트 코드가 새 매개변수 때문에 컴파일되지 않는 문제를 피할 수 있습니다.
        public void ConfigureTrader(HideoutTraderHud trader)
        {
            traderHud = trader;
            traderHud?.SetContext(hideoutController, this);
        }

        // 출격 화면도 기존 Configure 매개변수를 변경하지 않고 별도로 연결해 저장된 씬과 기존 테스트의 호환성을 유지합니다.
        // 화면이 지역 선택을 마치면 같은 HideoutController를 사용해 출격 인벤토리를 저장하고 해당 레이드 씬을 엽니다.
        public void ConfigureDeployment(HideoutDeploymentHud deployment)
        {
            deploymentHud = deployment;
            deploymentHud?.SetContext(hideoutController, this);
        }

        // 거점의 Tab 인벤토리는 창고 Canvas가 아니라 레이드와 같은 플레이어 인벤토리 Canvas를 사용합니다.
        // Tab 입력 자체는 이 월드 관리자가 처리하므로 HUD 내부의 자동 토글은 꺼 화면 두 개가 동시에 열리는 일을 막습니다.
        public void ConfigurePlayerInventoryHud(InventoryHud inventoryHud)
        {
            playerInventoryHud = inventoryHud;
            if (playerInventoryHud == null) return;
            playerInventoryHud.SetKeyboardToggleEnabled(false);
            playerInventoryHud.SetOpen(false);
        }

        private void Start()
        {
            // 첫 프레임에는 월드를 보여 주고, 창고와 임무 보드는 각 시설에서 E키를 눌렀을 때만 활성화합니다.
            if (storageHud != null) storageHud.gameObject.SetActive(false);
            if (missionBoardHud != null) missionBoardHud.gameObject.SetActive(false);
            if (traderHud != null) traderHud.gameObject.SetActive(false);
            if (deploymentHud != null) deploymentHud.gameObject.SetActive(false);
            if (facilityUpgradeHud != null) facilityUpgradeHud.gameObject.SetActive(false);
            if (playerInventoryHud != null)
            {
                playerInventoryHud.SetKeyboardToggleEnabled(false);
                playerInventoryHud.SetOpen(false);
            }
            activeScreen = null;
            activeFacility = null;
            worldHud?.SetMissionJournalOpen(false);

            // 거점에서는 총을 쏘거나 포만감이 감소하지 않지만 이동, 달리기와 Space 구르기는 그대로 사용합니다.
            if (player != null)
            {
                PlayerAim aim = player.GetComponent<PlayerAim>();
                PlayerCombat combat = player.GetComponent<PlayerCombat>();
                PlayerSurvival survival = player.GetComponent<PlayerSurvival>();
                if (aim != null) aim.enabled = false;
                if (combat != null) combat.enabled = false;
                // PlayerSurvival 전체를 끄면 구르기 비용과 회복도 멈추므로 포만감 감소만 선택적으로 정지합니다.
                if (survival != null) survival.SetSatietyDrainEnabled(false);
            }
        }

        private void Update()
        {
            // 임무수첩은 J를 다시 눌러도 닫힙니다. Escape와 Tab은 다른 시설 화면과 동일하게 공통 닫기 키로 유지합니다.
            if (IsFacilityOpen &&
                (Input.GetKeyDown(KeyCode.Escape) ||
                 Input.GetKeyDown(KeyCode.Tab) ||
                 (worldHud != null && worldHud.OwnsMissionJournalScreen(activeScreen) && Input.GetKeyDown(KeyCode.J))))
            {
                CloseFacility();
                return;
            }

            // 시설과 인벤토리가 닫힌 상태에서 J를 누르면 현재 수주한 임무만 모아 보는 수첩을 엽니다.
            // 화면이 열린 동안에는 PlayerControlGate가 이동과 상호작용을 잠가 UI 입력이 월드 조작으로 전달되지 않습니다.
            if (!IsFacilityOpen && Input.GetKeyDown(KeyCode.J))
            {
                OpenMissionJournal();
                return;
            }

            // 거점에서는 시설까지 가지 않아도 Tab으로 현재 창고·출격 인벤토리를 확인할 수 있습니다.
            // 별도 PlayerInventory 사본을 만들지 않고 실제 HideoutController 격자를 열어 화면에서 옮긴 결과가 다음 출격 데이터와 즉시 일치합니다.
            if (!IsFacilityOpen && Input.GetKeyDown(KeyCode.Tab))
                OpenInventoryFromHotkey();
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
                case HideoutFacilityType.Trader:
                    activeScreen = traderHud != null ? traderHud.gameObject : null;
                    break;
                case HideoutFacilityType.Deployment:
                    activeScreen = deploymentHud != null ? deploymentHud.gameObject : null;
                    break;
                case HideoutFacilityType.UpgradeStation:
                    activeScreen = facilityUpgradeHud != null ? facilityUpgradeHud.gameObject : null;
                    break;
                default:
                    // 제작대·치료소·발전기처럼 전용 화면이 아직 필요 없는 시설은 공용 강화 패널 자체를 주 화면으로 사용합니다.
                    activeScreen = facilityUpgradeHud != null ? facilityUpgradeHud.gameObject : null;
                    break;
            }

            if (activeScreen == null) return false;
            activeFacility = facility;
            activeScreen.SetActive(true);
            // 강화 버튼은 강화소에서만 표시합니다. 제작대·치료소는 기존 제작법만 보여 주고,
            // 창고·임무·상인·출격처럼 전용 Canvas가 있는 시설에는 공용 패널을 겹쳐 띄우지 않습니다.
            if (facility.Definition.FacilityType == HideoutFacilityType.UpgradeStation)
                facilityUpgradeHud?.ShowUpgradeStation();
            else if (facility.Definition.FacilityType == HideoutFacilityType.CraftingBench ||
                     facility.Definition.FacilityType == HideoutFacilityType.Infirmary ||
                     facility.Definition.FacilityType == HideoutFacilityType.Generator)
                facilityUpgradeHud?.ShowFacility(facility);
            else
                facilityUpgradeHud?.HideFacility();
            controlGate?.AcquireLock(this);
            worldHud?.RefreshImmediately();
            return true;
        }

        private bool OpenInventoryFromHotkey()
        {
            if (playerInventoryHud == null || IsFacilityOpen) return false;

            // activeFacility은 null로 유지해 이 화면이 시설 상호작용이 아니라 단축키로 열렸음을 나타냅니다.
            // 닫기 로직은 activeScreen을 기준으로 동작하므로 Escape와 Tab 어느 쪽을 눌러도 조작 잠금까지 정상 해제됩니다.
            activeScreen = playerInventoryHud.gameObject;
            activeFacility = null;
            playerInventoryHud.SetOpen(true);
            facilityUpgradeHud?.HideFacility();
            controlGate?.AcquireLock(this);
            worldHud?.RefreshImmediately();
            return true;
        }

        private bool OpenMissionJournal()
        {
            if (worldHud == null || IsFacilityOpen) return false;

            GameObject journalScreen = worldHud.MissionJournalScreen;
            if (journalScreen == null) return false;

            // 임무수첩은 특정 시설에서 연 화면이 아니므로 activeFacility은 null로 둡니다.
            // activeScreen에는 수첩 루트를 기록해 기존 화면 중복 방지와 공통 닫기 로직을 그대로 활용합니다.
            activeScreen = journalScreen;
            activeFacility = null;
            worldHud.SetMissionJournalOpen(true);
            facilityUpgradeHud?.HideFacility();
            controlGate?.AcquireLock(this);
            worldHud.RefreshImmediately();
            return true;
        }

        public void CloseFacility()
        {
            if (playerInventoryHud != null && activeScreen == playerInventoryHud.gameObject)
                playerInventoryHud.SetOpen(false);
            else if (worldHud != null && worldHud.OwnsMissionJournalScreen(activeScreen))
                worldHud.SetMissionJournalOpen(false);
            else if (activeScreen != null)
                activeScreen.SetActive(false);
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

            // Unity 오브젝트는 파괴된 뒤 C# 참조가 남아 있어도 `== null`이 true가 되는 특별한 수명 주기를 사용합니다.
            // 일반 C#의 `?.` 연산자는 이 Unity식 null 판정을 거치지 않으므로, 씬 종료 중 먼저 파괴된 HUD 메서드를 호출해
            // MissingReferenceException이 발생할 수 있습니다. 명시적으로 비교한 뒤 살아 있는 HUD만 닫아 안전하게 씬을 떠납니다.
            if (playerInventoryHud != null) playerInventoryHud.SetOpen(false);
            if (worldHud != null) worldHud.SetMissionJournalOpen(false);
            activeScreen = null;
            activeFacility = null;
        }
    }
}
