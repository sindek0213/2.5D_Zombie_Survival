using UnityEngine;
using System.Collections.Generic;

namespace ZombieSurvival.Hideout
{
    // 임무 보드가 보여 줄 임무 목록과 현재 선택 위치를 관리합니다.
    // Canvas는 이 공개 API만 호출하므로 임무 표시 모양을 바꿀 때 진행도와 저장 규칙은 영향을 받지 않습니다.
    public sealed class HideoutMissionBoard : MonoBehaviour
    {
        [SerializeField] private HideoutMissionDefinition[] missions;
        [SerializeField] private HideoutController hideoutController;
        [SerializeField] private HideoutFacilityDefinition facilityDefinition;
        private int selectedIndex;
        private readonly List<HideoutMissionDefinition> visibleMissions = new List<HideoutMissionDefinition>();

        public string LastOperationMessage { get; private set; }

        // 임무 보드 레벨의 효과 값만큼 앞쪽 임무를 공개하고, 배열 길이를 넘지 않게 제한합니다.
        // 시설 정의가 없는 테스트 환경에서는 이전 동작처럼 전체 임무를 공개합니다.
        public int MissionCount
        {
            get
            {
                RebuildVisibleMissions();
                return visibleMissions.Count;
            }
        }
        public HideoutMissionDefinition SelectedMission
        {
            get
            {
                RebuildVisibleMissions();
                return visibleMissions.Count == 0
                    ? null
                    : visibleMissions[Mathf.Clamp(selectedIndex, 0, visibleMissions.Count - 1)];
            }
        }
        public HideoutMissionDefinition ActiveMission
        {
            get
            {
                if (!HideoutMissionStateStore.HasActiveMission || missions == null) return null;
                foreach (HideoutMissionDefinition mission in missions)
                    if (mission != null && mission.MissionId == HideoutMissionStateStore.ActiveMissionId) return mission;
                return null;
            }
        }

        public void Configure(
            HideoutMissionDefinition[] availableMissions,
            HideoutController controller = null,
            HideoutFacilityDefinition boardDefinition = null)
        {
            missions = availableMissions ?? new HideoutMissionDefinition[0];
            if (controller != null) hideoutController = controller;
            if (boardDefinition != null) facilityDefinition = boardDefinition;
            selectedIndex = 0;
        }

        // 완료되지 않은 선행 임무가 있는 항목을 제외한 뒤 임무 보드 레벨의 공개 개수만 앞에서부터 보여 줍니다.
        // 임무 완료 직후 다시 호출하면 새 연속 임무가 자동으로 목록에 나타납니다.
        private void RebuildVisibleMissions()
        {
            visibleMissions.Clear();
            if (missions == null) return;
            int maximumVisible = missions.Length;
            if (facilityDefinition != null)
            {
                int level = HideoutFacilityStateStore.GetLevel(facilityDefinition);
                maximumVisible = Mathf.Clamp(facilityDefinition.GetEffectValue(level), 0, missions.Length);
            }

            foreach (HideoutMissionDefinition mission in missions)
            {
                if (mission == null || !mission.IsUnlocked()) continue;
                visibleMissions.Add(mission);
                if (visibleMissions.Count >= maximumVisible) break;
            }
            selectedIndex = visibleMissions.Count == 0 ? 0 : Mathf.Clamp(selectedIndex, 0, visibleMissions.Count - 1);
        }

        public void SelectPrevious()
        {
            if (MissionCount == 0 || HideoutMissionStateStore.HasActiveMission) return;
            selectedIndex = (selectedIndex - 1 + MissionCount) % MissionCount;
        }

        public void SelectNext()
        {
            if (MissionCount == 0 || HideoutMissionStateStore.HasActiveMission) return;
            selectedIndex = (selectedIndex + 1) % MissionCount;
        }

        public bool TryAcceptSelected()
        {
            bool accepted = HideoutMissionStateStore.TryAccept(SelectedMission);
            // 임무 수락 직후 저장해야 레이드로 곧바로 출격하거나 게임을 종료해도 받은 임무가 사라지지 않습니다.
            if (accepted)
            {
                // 메모리의 임무 수락은 이미 성공했으므로 저장 실패가 발생해도 버튼 동작 자체를 실패로 되돌리지는 않습니다.
                // 대신 사용자에게 디스크 기록 실패를 분명히 알려, 게임을 종료하기 전에 저장 경로와 디스크 상태를 확인할 수 있게 합니다.
                bool saved = HideoutSaveService.SaveDefault();
                LastOperationMessage = saved
                    ? "임무를 받았습니다."
                    : "임무는 받았지만 자동 저장에 실패했습니다. 게임을 종료하기 전에 저장 상태를 확인해 주세요.";
            }
            else
            {
                LastOperationMessage = SelectedMission != null &&
                                       !SelectedMission.Repeatable &&
                                       HideoutMissionStateStore.GetCompletionCount(SelectedMission.MissionId) > 0
                    ? "이미 완료한 일회성 임무입니다."
                    : "현재 임무를 받을 수 없습니다.";
            }
            return accepted;
        }

        public bool TryCompleteActive()
        {
            HideoutMissionDefinition mission = ActiveMission;
            bool completed;
            if (hideoutController != null)
            {
                // 실제 게임에서는 컨트롤러가 창고 공간 검사, 보상 지급, 완료 기록과 저장을 한 흐름으로 처리합니다.
                completed = hideoutController.TryCompleteMission(mission);
                LastOperationMessage = hideoutController.OperationStatus;
            }
            else
            {
                // 순수 상태 테스트나 이전 씬 데이터처럼 컨트롤러가 없는 경우에만 보상 없는 옛 완료 경로를 유지합니다.
                completed = HideoutMissionStateStore.TryComplete(mission);
                if (completed)
                {
                    bool saved = HideoutSaveService.SaveDefault();
                    LastOperationMessage = saved
                        ? "임무를 완료했습니다."
                        : "임무는 완료했지만 자동 저장에 실패했습니다. 게임을 종료하기 전에 저장 상태를 확인해 주세요.";
                }
                else LastOperationMessage = "아직 임무 목표를 달성하지 못했습니다.";
            }
            return completed;
        }

        public bool TryAbandonActive()
        {
            bool abandoned = HideoutMissionStateStore.Abandon();
            if (abandoned)
            {
                // 포기 결과도 현재 실행 중인 메모리에는 즉시 반영되므로 반환값은 성공으로 유지하고 저장 실패만 안내 문구로 구분합니다.
                bool saved = HideoutSaveService.SaveDefault();
                LastOperationMessage = saved
                    ? "진행 중인 임무를 포기했습니다."
                    : "임무는 포기했지만 자동 저장에 실패했습니다. 게임을 종료하기 전에 저장 상태를 확인해 주세요.";
            }
            else LastOperationMessage = "포기할 임무가 없습니다.";
            return abandoned;
        }
    }
}
