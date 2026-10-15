using System;
using UnityEngine;
using UnityEngine.UI;
using ZombieSurvival.Core;
using ZombieSurvival.Hideout;
using ZombieSurvival.Inventory;
using ZombieSurvival.Items;

namespace ZombieSurvival.UI
{
    // 창고의 아이템 정리 기능과 실제 레이드 출발 기능을 분리한 거점 출격 화면입니다.
    // 이 화면은 지역 선택과 출발 명령만 담당하고, 출격품 저장과 씬 전환의 안전 검사는 HideoutController가 처리합니다.
    public sealed class HideoutDeploymentHud : MonoBehaviour
    {
        [SerializeField] private HideoutController controller;
        [SerializeField] private HideoutWorldController worldController;
        [SerializeField] private RaidRegionDefinition[] regions = Array.Empty<RaidRegionDefinition>();
        [SerializeField] private Text regionTitleText;
        [SerializeField] private Text descriptionText;
        [SerializeField] private Text difficultyText;
        [SerializeField] private Text loadoutText;
        [SerializeField] private Text statusText;
        [SerializeField] private Button previousButton;
        [SerializeField] private Button nextButton;
        [SerializeField] private Button launchButton;
        [SerializeField] private Button closeButton;

        private int selectedIndex;
        private bool launchConfirmationArmed;
        private float launchConfirmationUntil;

        public RaidRegionDefinition SelectedRegion
            => regions != null && regions.Length > 0
                ? regions[Mathf.Clamp(selectedIndex, 0, regions.Length - 1)]
                : null;

        // 에디터 생성기가 프리팹 자체에 지역 목록과 UI 자식 참조를 저장할 때 사용합니다.
        // 씬마다 달라지는 거점 컨트롤러는 null로 둘 수 있으며 실제 Hideout 씬을 만들 때 SetContext로 연결합니다.
        public void Configure(
            RaidRegionDefinition[] availableRegions,
            Text regionTitle,
            Text description,
            Text difficultyLabel,
            Text loadoutLabel,
            Text status,
            Button previous,
            Button next,
            Button launch,
            Button close)
        {
            regions = availableRegions ?? Array.Empty<RaidRegionDefinition>();
            regionTitleText = regionTitle;
            descriptionText = description;
            difficultyText = difficultyLabel;
            loadoutText = loadoutLabel;
            statusText = status;
            previousButton = previous;
            nextButton = next;
            launchButton = launch;
            closeButton = close;
            NormalizeSelection();
            ConfigureButtons();
            Refresh();
        }

        // 프리팹의 모양과 지역 에셋 참조는 유지하고 현재 씬의 데이터·화면 관리자만 주입합니다.
        // 이 분리 덕분에 출격 Canvas를 다른 거점 씬에서도 재사용할 때 프리팹을 다시 만들 필요가 없습니다.
        public void SetContext(HideoutController hideout, HideoutWorldController world)
        {
            Unsubscribe();
            controller = hideout;
            worldController = world;
            Subscribe();
            ConfigureButtons();
            Refresh();
        }

        private void Awake() => ConfigureButtons();

        private void OnEnable()
        {
            Subscribe();
            Refresh();
        }

        private void OnDisable() => Unsubscribe();

        private void ConfigureButtons()
        {
            Bind(previousButton, SelectPrevious);
            Bind(nextButton, SelectNext);
            Bind(launchButton, LaunchSelectedRegion);
            Bind(closeButton, Close);
        }

        private static void Bind(Button button, UnityEngine.Events.UnityAction action)
        {
            if (button == null) return;
            button.onClick.RemoveListener(action);
            button.onClick.AddListener(action);
        }

        private void SelectPrevious()
        {
            if (regions == null || regions.Length == 0) return;
            selectedIndex = (selectedIndex - 1 + regions.Length) % regions.Length;
            Refresh();
        }

        private void SelectNext()
        {
            if (regions == null || regions.Length == 0) return;
            selectedIndex = (selectedIndex + 1) % regions.Length;
            Refresh();
        }

        private void LaunchSelectedRegion()
        {
            RaidRegionDefinition region = SelectedRegion;
            if (controller == null || region == null || !region.Available)
            {
                if (statusText != null) statusText.text = "현재 출격할 수 있는 지역이 없습니다.";
                return;
            }

            // 첫 클릭은 장비 점검 경고와 손실 규칙을 확인하고, 같은 버튼을 4초 안에 다시 눌러야 실제 출격합니다.
            if (!launchConfirmationArmed || Time.unscaledTime > launchConfirmationUntil)
            {
                launchConfirmationArmed = true;
                launchConfirmationUntil = Time.unscaledTime + 4f;
                if (statusText != null)
                    statusText.text = BuildLoadoutWarning(controller) + "\n사망·포기 시 가져간 아이템을 모두 잃습니다. 출격 버튼을 다시 누르면 시작합니다.";
                return;
            }
            launchConfirmationArmed = false;

            // StartRaid가 출격 인벤토리를 저장한 뒤에만 씬을 엽니다. 실패하면 현재 화면을 유지해 아이템 상태를 잃지 않습니다.
            if (!controller.StartRaid(region.SceneName) && statusText != null)
                statusText.text = string.IsNullOrWhiteSpace(controller.OperationStatus)
                    ? "출격에 실패했습니다. Build Settings와 저장 상태를 확인해 주세요."
                    : controller.OperationStatus;
        }

        private void Close() => worldController?.CloseFacility();

        private void Refresh()
        {
            NormalizeSelection();
            RaidRegionDefinition region = SelectedRegion;
            int loadoutQuantity = controller != null
                ? InventoryGridContent.CountAllQuantities(controller.LoadoutGrid)
                : 0;

            if (regionTitleText != null)
                regionTitleText.text = region != null ? region.DisplayName : "등록된 출격 지역 없음";
            if (descriptionText != null)
                descriptionText.text = region != null ? region.Description : "지역 데이터가 연결되지 않았습니다.";
            if (difficultyText != null)
                difficultyText.text = region != null
                    ? $"위험도  {new string('◆', region.Difficulty)}{new string('◇', 5 - region.Difficulty)}"
                    : "위험도 정보 없음";
            if (loadoutText != null)
                loadoutText.text = $"출격 인벤토리 준비 수량: {loadoutQuantity}개\n{BuildLoadoutWarning(controller)}";
            if (statusText != null)
                statusText.text = region == null
                    ? "출격 지역을 먼저 등록해 주세요."
                    : region.Available ? "지역을 확인한 뒤 출격하세요." : "아직 개방되지 않은 지역입니다.";

            bool hasMultipleRegions = regions != null && regions.Length > 1;
            if (previousButton != null) previousButton.interactable = hasMultipleRegions;
            if (nextButton != null) nextButton.interactable = hasMultipleRegions;
            if (launchButton != null) launchButton.interactable = controller != null && region != null && region.Available;
        }

        // 무기·호환 탄약·치료품·가방을 각각 검사해 수량만 보고 준비됐다고 착각하지 않게 합니다.
        // 경고가 있어도 플레이어가 의도적으로 맨몸 출격할 수 있도록 버튼을 잠그지는 않고 확인만 한 번 더 받습니다.
        public static string BuildLoadoutWarning(HideoutController hideout)
        {
            if (hideout?.PlayerInventory == null) return "플레이어 출격 인벤토리가 연결되지 않았습니다.";
            PlayerInventory inventory = hideout.PlayerInventory;
            bool hasWeapon = inventory.Equipment.ContainsKey(EquipmentSlotType.PrimaryWeapon) ||
                             inventory.Equipment.ContainsKey(EquipmentSlotType.SecondaryWeapon);
            bool hasBag = inventory.Equipment.ContainsKey(EquipmentSlotType.Backpack) ||
                          inventory.Equipment.ContainsKey(EquipmentSlotType.Rig);
            bool hasHealing = InventoryGridContent.CountByPredicate(
                inventory.Root,
                entry => entry?.Item.Type == ItemType.Healing,
                true) > 0;
            bool hasAmmo = InventoryGridContent.CountByPredicate(
                inventory.Root,
                entry => entry?.Item.Type == ItemType.Ammo,
                true) > 0;

            var warnings = new System.Collections.Generic.List<string>();
            if (!hasWeapon) warnings.Add("무기 없음");
            if (!hasAmmo) warnings.Add("예비 탄약 없음");
            if (!hasHealing) warnings.Add("치료품 없음");
            if (!hasBag) warnings.Add("가방·조끼 없음");
            return warnings.Count == 0
                ? "장비 점검 완료: 무기·탄약·치료품·수납 장비 준비"
                : "준비 경고: " + string.Join(" · ", warnings);
        }

        private void NormalizeSelection()
        {
            selectedIndex = regions == null || regions.Length == 0
                ? 0
                : Mathf.Clamp(selectedIndex, 0, regions.Length - 1);
        }

        private void Subscribe()
        {
            if (controller == null) return;
            controller.StateChanged -= Refresh;
            controller.StateChanged += Refresh;
        }

        private void Unsubscribe()
        {
            if (controller != null) controller.StateChanged -= Refresh;
        }
    }
}
