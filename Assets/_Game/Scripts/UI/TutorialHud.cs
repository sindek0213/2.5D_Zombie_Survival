using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using ZombieSurvival.Core;
using ZombieSurvival.Extraction;
using ZombieSurvival.Hideout;
using ZombieSurvival.Inventory;

namespace ZombieSurvival.UI
{
    // 거점과 레이드에서 공통으로 사용하는 초보자 안내와 현재 목표 표시입니다.
    // F1은 설명 패널만 숨기거나 다시 보여 주며, 작은 목표 문구는 계속 남겨 다음 행동을 잃지 않게 합니다.
    public sealed class TutorialHud : MonoBehaviour
    {
        [SerializeField] private GameObject tutorialPanel;
        [SerializeField] private Text tutorialTitleText;
        [SerializeField] private Text tutorialMessageText;
        [SerializeField] private Text currentGoalText;

        private HideoutController hideout;
        private HideoutWorldController hideoutWorld;
        private PlayerInventory inventory;
        private RaidSession raidSession;
        private ExtractionController extraction;
        private float nextRefreshAt;
        private bool panelVisible = true;

        public void Configure(GameObject panel, Text title, Text message, Text goal)
        {
            tutorialPanel = panel;
            tutorialTitleText = title;
            tutorialMessageText = message;
            currentGoalText = goal;
            RefreshReferences();
            Refresh();
        }

        private void Awake() => RefreshReferences();

        private void OnEnable()
        {
            TutorialProgressStore.StepChanged -= HandleStepChanged;
            TutorialProgressStore.StepChanged += HandleStepChanged;
            RefreshReferences();
            Refresh();
        }

        private void OnDisable() => TutorialProgressStore.StepChanged -= HandleStepChanged;

        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.F1))
            {
                panelVisible = !panelVisible;
                Refresh();
            }

            if (Time.unscaledTime < nextRefreshAt) return;
            nextRefreshAt = Time.unscaledTime + 0.25f;
            EvaluateProgress();
            Refresh();
        }

        private void RefreshReferences()
        {
            hideout = FindObjectOfType<HideoutController>();
            hideoutWorld = FindObjectOfType<HideoutWorldController>();
            inventory = FindObjectOfType<PlayerInventory>();
            raidSession = FindObjectOfType<RaidSession>();
            extraction = FindObjectOfType<ExtractionController>();
        }

        private void EvaluateProgress()
        {
            TutorialStep previous = TutorialProgressStore.CurrentStep;
            bool isHideout = SceneManager.GetActiveScene().name == GameSceneNavigator.HideoutSceneName;
            if (isHideout)
            {
                if (hideoutWorld != null && hideoutWorld.IsFacilityOpen)
                    TutorialProgressStore.AdvanceTo(TutorialStep.OpenFacility);
                if (hideout?.LoadoutGrid != null && hideout.LoadoutGrid.Entries.Count > 0)
                    TutorialProgressStore.AdvanceTo(TutorialStep.PrepareLoadout);
                if (HideoutMissionStateStore.HasActiveMission)
                    TutorialProgressStore.AdvanceTo(TutorialStep.AcceptMission);
                if (hideout?.ProcessedResult != null && hideout.ProcessedResult.succeeded &&
                    TutorialProgressStore.CurrentStep >= TutorialStep.ReachExtraction)
                    TutorialProgressStore.AdvanceTo(TutorialStep.Completed);
            }
            else if (GameSceneNavigator.IsRaidScene(SceneManager.GetActiveScene().name))
            {
                TutorialProgressStore.AdvanceTo(TutorialStep.StartRaid);
                if (raidSession != null && raidSession.ElapsedTime >= 5f)
                    TutorialProgressStore.AdvanceTo(TutorialStep.LearnCombat);
                if (HasCollectedRaidItem())
                    TutorialProgressStore.AdvanceTo(TutorialStep.LootItem);
                if (extraction != null && extraction.ItemRequirementMet && extraction.RaidRequirementMet)
                    TutorialProgressStore.AdvanceTo(TutorialStep.ReachExtraction);
            }

            // 거점에서 단계가 변하면 즉시 저장해 게임을 종료해도 튜토리얼이 처음부터 반복되지 않게 합니다.
            if (isHideout && previous != TutorialProgressStore.CurrentStep)
                HideoutSaveService.SaveDefault();
        }

        private bool HasCollectedRaidItem()
        {
            if (inventory == null) return false;
            foreach (InventoryEntry entry in inventory.Root.Entries)
                if (entry != null && !entry.IsRaidProvided) return true;
            return false;
        }

        private void HandleStepChanged(TutorialStep _) => Refresh();

        private void Refresh()
        {
            if (tutorialPanel != null)
                tutorialPanel.SetActive(panelVisible && !TutorialProgressStore.IsCompleted);
            if (tutorialTitleText != null)
                tutorialTitleText.text = "초보자 안내  ·  F1 숨기기/표시";
            if (tutorialMessageText != null)
                tutorialMessageText.text = BuildTutorialMessage(TutorialProgressStore.CurrentStep);
            if (currentGoalText != null)
                currentGoalText.text = "현재 목표: " + BuildCurrentGoal();
        }

        private string BuildCurrentGoal()
        {
            if (hideout != null)
            {
                if (!HideoutMissionStateStore.HasActiveMission) return "임무 보드에서 임무를 받으세요.";
                if (hideout.LoadoutGrid == null || hideout.LoadoutGrid.Entries.Count == 0)
                    return "창고에서 출격 준비 격자로 장비와 소모품을 옮기세요.";
                return hideout.ActiveMissionSummary + " · 준비가 끝났다면 창고 화면에서 출격하세요.";
            }

            if (raidSession != null)
            {
                if (extraction != null && !extraction.ItemRequirementMet)
                    return "상자와 시체를 수색해 탈출용 무전기를 찾으세요.";
                if (raidSession.ZombieKills < raidSession.RequiredZombieKills)
                    return $"좀비 처치 {raidSession.ZombieKills}/{raidSession.RequiredZombieKills}";
                if (raidSession.ElapsedTime < raidSession.MinimumExtractionTime)
                    return $"최소 생존 시간까지 {Mathf.CeilToInt(raidSession.MinimumExtractionTime - raidSession.ElapsedTime)}초";
                return "북쪽 탈출 지점으로 이동해 3초 동안 머무르세요.";
            }
            return "게임 시스템을 준비하는 중입니다.";
        }

        public static string BuildTutorialMessage(TutorialStep step)
        {
            switch (step)
            {
                case TutorialStep.ExploreHideout:
                    return "WASD로 거점을 이동하고 시설 가까이에서 E를 눌러 사용하세요.";
                case TutorialStep.OpenFacility:
                    return "창고에서 아이템을 드래그하거나 Ctrl+좌클릭으로 출격 준비에 옮길 수 있습니다.";
                case TutorialStep.PrepareLoadout:
                    return "임무 보드에서 목표를 하나 받은 뒤 출격하면 보상과 크레딧을 함께 얻을 수 있습니다.";
                case TutorialStep.AcceptMission:
                    return "창고 화면의 출격 버튼으로 레이드에 들어가세요. 사망하면 가져간 물건을 모두 잃습니다.";
                case TutorialStep.StartRaid:
                    return "WASD 이동, Shift 달리기, Space 전방 구르기(기력 25%), 좌클릭 발사, 우클릭 조준, R 재장전, Tab 인벤토리입니다.";
                case TutorialStep.LearnCombat:
                    return "작은 바닥 아이템은 E로 줍고, 시체와 상자는 E로 연 뒤 격자에서 직접 옮깁니다.";
                case TutorialStep.LootItem:
                    return "탈출에는 무전기·처치 수·최소 생존 시간이 필요합니다. HUD의 현재 목표를 확인하세요.";
                case TutorialStep.ReachExtraction:
                    return "조건을 모두 채운 뒤 북쪽 탈출 구역에 3초 동안 머무르면 아이템을 거점으로 가져갑니다.";
                default:
                    return "기본 튜토리얼을 완료했습니다. F1로 안내 패널을 다시 확인할 수 있습니다.";
            }
        }
    }
}
