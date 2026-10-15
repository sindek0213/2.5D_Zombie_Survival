#if UNITY_EDITOR
using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace ZombieSurvival.EditorTools
{
    // 자동 검증과 수동 메뉴가 같은 Windows 테스트 빌드 규칙을 사용하도록 만든 Editor 전용 진입점입니다.
    // 런타임 어셈블리에는 포함되지 않으며 Build Settings에서 활성화된 Title·Loading·Hideout·RaidSchool 씬만 패키징합니다.
    public static class ProjectBuildUtility
    {
        private const string MenuPath = "Tools/2.5D Zombie Survival/Build Windows Test Player";
        // 출력 파일 이름도 플레이어 설정의 가제와 맞춰 테스트 빌드를 전달했을 때 프로젝트 내부 이름이 노출되지 않게 합니다.
        // 최종 제목을 바꾸면 TitleMenuHud.WorkingGameTitle, Player Settings의 Product Name과 이 파일명도 함께 변경합니다.
        private const string RelativeOutputPath = "Builds/WindowsTest/LastSalvage.exe";

        [MenuItem(MenuPath)]
        public static void BuildWindowsTestPlayer()
        {
            string[] scenes = GetEnabledScenePaths();
            if (scenes.Length == 0)
                throw new InvalidOperationException("Build Settings에 활성화된 씬이 없습니다.");

            string outputPath = Path.GetFullPath(Path.Combine(Application.dataPath, "..", RelativeOutputPath));
            string outputDirectory = Path.GetDirectoryName(outputPath);
            if (string.IsNullOrWhiteSpace(outputDirectory))
                throw new InvalidOperationException("테스트 빌드 출력 경로를 계산하지 못했습니다.");
            Directory.CreateDirectory(outputDirectory);

            // Development 옵션은 최종 배포 최적화보다 오류 추적 정보를 우선해 프로토타입 플레이 테스트에 적합합니다.
            var options = new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = outputPath,
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.Development
            };
            BuildReport report = BuildPipeline.BuildPlayer(options);
            BuildSummary summary = report.summary;
            if (summary.result != BuildResult.Succeeded)
                throw new InvalidOperationException(
                    $"Windows 테스트 빌드 실패: {summary.result}, 오류 {summary.totalErrors}, 경고 {summary.totalWarnings}");

            Debug.Log($"Windows 테스트 빌드 완료: {outputPath} ({summary.totalSize:N0} bytes)");
        }

        private static string[] GetEnabledScenePaths()
        {
            var paths = new System.Collections.Generic.List<string>();
            foreach (EditorBuildSettingsScene scene in EditorBuildSettings.scenes)
                if (scene != null && scene.enabled && !string.IsNullOrWhiteSpace(scene.path)) paths.Add(scene.path);
            return paths.ToArray();
        }
    }
}
#endif
