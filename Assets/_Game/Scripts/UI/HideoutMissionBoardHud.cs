using UnityEngine;
using UnityEngine.UI;
using ZombieSurvival.Hideout;

namespace ZombieSurvival.UI
{
    // 임무 보드 데이터를 실제 Canvas 글자와 버튼으로 표시합니다.
    // 임무 진행 상태는 HideoutMissionStateStore가 보관하므로 이 클래스는 선택·수락·포기 명령과 화면 갱신만 담당합니다.
    public sealed class HideoutMissionBoardHud : MonoBehaviour
    {
        [SerializeField] private HideoutMissionBoard board;
        [SerializeField] private HideoutWorldController worldController;
        [SerializeField] private Text titleText;
        [SerializeField] private Text descriptionText;
        [SerializeField] private Text progressText;
        [SerializeField] private Text statusText;
        [SerializeField] private Button previousButton;
        [SerializeField] private Button nextButton;
        [SerializeField] private Button acceptButton;
        [SerializeField] private Text acceptButtonText;
        [SerializeField] private Button abandonButton;
        [SerializeField] private Button closeButton;

        public void Configure(
            HideoutMissionBoard missionBoard,
            HideoutWorldController world,
            Text title,
            Text description,
            Text progress,
            Text status,
            Button previous,
            Button next,
            Button accept,
            Text acceptLabel,
            Button abandon,
            Button close)
        {
            board = missionBoard;
            worldController = world;
            titleText = title;
            descriptionText = description;
            progressText = progress;
            statusText = status;
            previousButton = previous;
            nextButton = next;
            acceptButton = accept;
            acceptButtonText = acceptLabel;
            abandonButton = abandon;
            closeButton = close;
            ConfigureButtons();
            Refresh();
        }

        // Canvas 프리팹 안에는 글자와 버튼 참조가 이미 저장되어 있으므로 씬을 만들 때는 월드의 보드와 관리자만 다시 연결하면 됩니다.
        // 화면 참조까지 null로 덮어쓰지 않는 별도 메서드를 둬서 디자이너가 프리팹에서 바꾼 UI 배치가 씬 생성 중 사라지지 않게 합니다.
        public void SetContext(HideoutMissionBoard missionBoard, HideoutWorldController world)
        {
            board = missionBoard;
            worldController = world;
            ConfigureButtons();
            Refresh();
        }

        private void Awake() => ConfigureButtons();
        private void OnEnable() => Refresh();

        private void ConfigureButtons()
        {
            if (previousButton != null)
            {
                previousButton.onClick.RemoveListener(SelectPrevious);
                previousButton.onClick.AddListener(SelectPrevious);
            }
            if (nextButton != null)
            {
                nextButton.onClick.RemoveListener(SelectNext);
                nextButton.onClick.AddListener(SelectNext);
            }
            if (acceptButton != null)
            {
                acceptButton.onClick.RemoveListener(AcceptSelected);
                acceptButton.onClick.AddListener(AcceptSelected);
            }
            if (abandonButton != null)
            {
                abandonButton.onClick.RemoveListener(AbandonActive);
                abandonButton.onClick.AddListener(AbandonActive);
            }
            if (closeButton != null)
            {
                closeButton.onClick.RemoveListener(Close);
                closeButton.onClick.AddListener(Close);
            }
        }

        private void SelectPrevious()
        {
            board?.SelectPrevious();
            Refresh();
        }

        private void SelectNext()
        {
            board?.SelectNext();
            Refresh();
        }

        private void AcceptSelected()
        {
            // 임무가 없을 때는 수락 버튼, 목표를 채운 뒤에는 같은 자리를 완료 버튼으로 사용해 화면을 단순하게 유지합니다.
            bool wasActive = HideoutMissionStateStore.HasActiveMission;
            bool succeeded = board != null && (wasActive ? board.TryCompleteActive() : board.TryAcceptSelected());
            if (statusText != null)
                statusText.text = board != null && !string.IsNullOrWhiteSpace(board.LastOperationMessage)
                    ? board.LastOperationMessage
                    : succeeded ? "임무 처리가 완료됐습니다." : "임무를 처리할 수 없습니다.";
            Refresh(false);
        }

        private void AbandonActive()
        {
            bool abandoned = board != null && board.TryAbandonActive();
            if (statusText != null)
                statusText.text = board != null && !string.IsNullOrWhiteSpace(board.LastOperationMessage)
                    ? board.LastOperationMessage
                    : abandoned ? "진행 중인 임무를 포기했습니다." : "포기할 임무가 없습니다.";
            Refresh(false);
        }

        private void Close() => worldController?.CloseFacility();

        private void Refresh(bool clearStatus = true)
        {
            bool active = HideoutMissionStateStore.HasActiveMission;
            // 저장 파일의 임무 ID가 현재 데이터 목록에서 사라졌다면 다른 임무를 진행 중인 것처럼 잘못 표시하지 않습니다.
            HideoutMissionDefinition mission = active ? board?.ActiveMission : board?.SelectedMission;
            bool completable = HideoutMissionStateStore.CanComplete(mission);

            if (titleText != null) titleText.text = mission != null ? mission.DisplayName : "등록된 임무 없음";
            if (descriptionText != null)
                descriptionText.text = mission != null
                    ? mission.Description
                    : active
                        ? "저장된 임무의 데이터가 현재 프로젝트에 없습니다. 임무를 포기한 뒤 새 임무를 선택해 주세요."
                        : "임무 데이터가 연결되지 않았습니다.";
            if (progressText != null)
            {
                if (mission == null)
                    progressText.text = active
                        ? $"저장된 임무 진행도: {HideoutMissionStateStore.ObjectiveProgress}"
                        : "선택 가능한 임무가 없습니다.";
                else
                {
                    int progress = active ? HideoutMissionStateStore.ObjectiveProgress : 0;
                    int completionCount = HideoutMissionStateStore.GetCompletionCount(mission.MissionId);
                    progressText.text = active
                        ? mission.BuildProgressDescription(progress) +
                          (progress >= mission.RequiredAmount ? "  · 목표 달성" : string.Empty) +
                          $"\n보상: {mission.RewardDescription}"
                        : $"목표: {mission.BuildProgressDescription(0)}\n" +
                          $"보상: {mission.RewardDescription}\n" +
                          $"위험도: {new string('◆', mission.RiskRating)}{new string('◇', 5 - mission.RiskRating)}\n" +
                          (mission.Repeatable ? $"반복 가능 · 완료 {completionCount}회" : completionCount > 0 ? "완료한 일회성 임무" : "일회성 임무");
                }
            }

            if (clearStatus && statusText != null)
                statusText.text = active ? "진행 중인 임무" : "받을 임무를 선택하세요.";
            bool canAccept = !active && HideoutMissionStateStore.CanAccept(mission);
            if (acceptButton != null) acceptButton.interactable = mission != null && (active ? completable : canAccept);
            if (acceptButtonText != null)
                acceptButtonText.text = active
                    ? completable ? "보상 받기" : "진행 중"
                    : mission != null && !mission.Repeatable && HideoutMissionStateStore.GetCompletionCount(mission.MissionId) > 0
                        ? "완료됨"
                        : "임무 받기";
            if (abandonButton != null) abandonButton.interactable = active;
            if (previousButton != null) previousButton.interactable = !active && (board?.MissionCount ?? 0) > 1;
            if (nextButton != null) nextButton.interactable = !active && (board?.MissionCount ?? 0) > 1;
        }
    }
}
