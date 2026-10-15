using System;
using UnityEngine;

namespace ZombieSurvival.Core
{
    // 초보자 안내가 게임 흐름의 어느 단계까지 완료됐는지 나타냅니다.
    // 숫자 순서가 저장 파일에 기록되므로 새 단계는 기존 항목 사이가 아니라 Completed 앞에 추가하거나 저장 마이그레이션을 함께 작성해야 합니다.
    public enum TutorialStep
    {
        ExploreHideout = 0,
        OpenFacility = 1,
        PrepareLoadout = 2,
        AcceptMission = 3,
        StartRaid = 4,
        LearnCombat = 5,
        LootItem = 6,
        ReachExtraction = 7,
        Completed = 8
    }

    public static class TutorialProgressStore
    {
        public static TutorialStep CurrentStep { get; private set; } = TutorialStep.ExploreHideout;
        public static bool IsCompleted => CurrentStep == TutorialStep.Completed;
        public static event Action<TutorialStep> StepChanged;

        // 여러 조건이 같은 프레임에 충족돼도 진행도가 뒤로 돌아가지 않도록 현재보다 뒤 단계만 적용합니다.
        public static bool AdvanceTo(TutorialStep step)
        {
            TutorialStep safe = (TutorialStep)Mathf.Clamp((int)step, 0, (int)TutorialStep.Completed);
            if (safe <= CurrentStep) return false;
            CurrentStep = safe;
            StepChanged?.Invoke(CurrentStep);
            return true;
        }

        internal static void Restore(int savedStep)
        {
            CurrentStep = (TutorialStep)Mathf.Clamp(savedStep, 0, (int)TutorialStep.Completed);
            StepChanged?.Invoke(CurrentStep);
        }

        public static void ResetForTests()
        {
            CurrentStep = TutorialStep.ExploreHideout;
            StepChanged?.Invoke(CurrentStep);
        }

        // 설정이나 도움말의 "튜토리얼 다시 보기"가 저장을 지우지 않고 안내 단계만 처음으로 되돌릴 때 사용합니다.
        public static void RestartTutorial()
        {
            CurrentStep = TutorialStep.ExploreHideout;
            StepChanged?.Invoke(CurrentStep);
        }
    }
}
