using UnityEngine;
using UnityEngine.UI;
using ZombieSurvival.Core;

namespace ZombieSurvival.UI
{
    // RaidSession이 종료된 동안 성공·실패, 처치 수와 획득/손실 수량을 전체 화면 Canvas 패널에 표시합니다.
    // 결과 생성이나 씬 이동은 수행하지 않고 RaidSession에 이미 확정된 RaidResult만 읽습니다.
    public sealed class RaidResultHud : MonoBehaviour
    {
        [SerializeField] private RaidSession raidSession;
        [SerializeField] private GameObject resultPanel;
        [SerializeField] private Text titleText;
        [SerializeField] private Text detailText;
        [SerializeField] private Text timerText;
        [SerializeField] private Color successColor = new Color(0.3f, 1f, 0.65f, 1f);
        [SerializeField] private Color failureColor = new Color(1f, 0.35f, 0.3f, 1f);

        public void SetSession(RaidSession value)
        {
            raidSession = value;
            Refresh();
        }

        public void ConfigureViews(GameObject panel, Text title, Text detail, Text timer = null)
        {
            resultPanel = panel;
            titleText = title;
            detailText = detail;
            timerText = timer;
            Refresh();
        }

        private void Awake() => Refresh();
        private void Update() => Refresh();

        private void Refresh()
        {
            if (timerText != null)
            {
                bool showTimer = raidSession != null && !raidSession.HasEnded;
                timerText.gameObject.SetActive(showTimer);
                if (showTimer)
                    timerText.text = BuildRaidStatus(
                        raidSession.ElapsedTime,
                        raidSession.ZombieKills,
                        raidSession.RequiredZombieKills,
                        raidSession.MinimumExtractionTime);
            }

            bool visible = raidSession != null && raidSession.HasEnded && raidSession.Result != null;
            if (resultPanel != null) resultPanel.SetActive(visible);
            if (!visible) return;

            RaidResult result = raidSession.Result;
            int itemCount = result.succeeded ? CountSuccessfulItems(result) : result.lostItemCount;
            if (titleText != null)
            {
                titleText.text = result.succeeded ? "탈출 성공" : "레이드 실패";
                titleText.color = result.succeeded ? successColor : failureColor;
            }
            if (detailText != null)
            {
                string items = result.succeeded
                    ? $"획득 수량: {itemCount}"
                    : $"손실 수량: {itemCount} (소지품·장착 장비 전부 분실)";
                string reason = result.succeeded
                    ? "정상 탈출"
                    : "플레이어 사망";
                string transition = raidSession.ReturnToHideoutFailed
                    ? "거점 이동 실패 - Build Settings의 Hideout 씬을 확인해 주세요."
                    : "거점으로 이동 중...";
                detailText.text = $"종료 이유: {reason}\n진행 시간: {FormatTime(result.raidDurationSeconds)}\n좀비 처치: {result.zombieKills}\n{items}\n{transition}";
            }
        }

        // 제한 시간은 없으므로 전투 중에는 현재 레이드 경과 시간과 처치·최소 시간 조건을 한 줄에 함께 보여 줍니다.
        public static string BuildRaidStatus(
            float elapsedTime,
            int zombieKills,
            int requiredKills,
            float minimumExtractionTime)
        {
            float wait = Mathf.Max(0f, minimumExtractionTime - elapsedTime);
            return $"레이드 시간 {FormatElapsedTime(elapsedTime)}  |  처치 {zombieKills}/{requiredKills}  |  탈출 대기 {wait:0}초";
        }

        private static string FormatElapsedTime(float seconds)
        {
            // 경과 시간은 남은 시간과 달리 올림하지 않고 완료된 초까지만 표시해 시작 직후 00:01로 보이는 현상을 막습니다.
            int total = Mathf.Max(0, Mathf.FloorToInt(seconds));
            return $"{total / 60:00}:{total % 60:00}";
        }

        private static string FormatTime(float seconds)
        {
            int total = Mathf.Max(0, Mathf.CeilToInt(seconds));
            return $"{total / 60:00}:{total % 60:00}";
        }

        private static int CountSnapshot(RaidGridSnapshot grid)
        {
            if (grid == null) return 0;
            int total = 0;
            foreach (RaidItemSnapshot item in grid.items)
                total += RaidSnapshotUtility.CountAllQuantities(item);
            return total;
        }

        // 성공 보상에는 루트 인벤토리뿐 아니라 장착 장비와 컨테이너 내부 내용도 모두 포함합니다.
        private static int CountSuccessfulItems(RaidResult result)
        {
            int total = CountSnapshot(result.inventory);
            foreach (RaidEquipmentSnapshot equipped in result.equipment)
                if (equipped.item != null)
                    total += RaidSnapshotUtility.CountAllQuantities(equipped.item);
            return total;
        }
    }
}
