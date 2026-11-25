using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace SpaceTrade.EditorTools
{
    /// <summary>
    /// Batch-mode entry points, e.g.
    /// Unity.exe -batchmode -quit -projectPath . -executeMethod SpaceTrade.EditorTools.BuildScript.BuildWindowsRelease -logFile Builds/build.log
    /// Optional: -buildOutput "C:\path\SpaceTrade.exe" overrides the default location under Builds/.
    /// </summary>
    public static class BuildScript
    {
        public const string ExeName = "SpaceTrade.exe";
        public const string ReleaseDir = "Builds/Windows-x64";
        public const string DevelopmentDir = "Builds/Windows-x64-Dev";

        [MenuItem("Space Trade/Build Windows x64 (Release)")]
        public static void BuildWindowsRelease() { Build(false); }

        [MenuItem("Space Trade/Build Windows x64 (Development)")]
        public static void BuildWindowsDevelopment() { Build(true); }

        private static void Build(bool development)
        {
            string output = ArgValue("-buildOutput") ?? Path.Combine(development ? DevelopmentDir : ReleaseDir, ExeName);
            string[] scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();
            if (scenes.Length == 0)
            {
                Fail("No enabled scenes in Build Settings (expected Assets/Scenes/Main.unity).");
                return;
            }

            string dir = Path.GetDirectoryName(Path.GetFullPath(output));
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
            Directory.CreateDirectory(dir);

            var options = new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = output,
                target = BuildTarget.StandaloneWindows64,
                targetGroup = BuildTargetGroup.Standalone,
                options = development ? BuildOptions.Development : BuildOptions.None,
            };

            Debug.Log("[BuildScript] Building " + (development ? "Development" : "Release") + " player to " + output);
            BuildReport report = BuildPipeline.BuildPlayer(options);
            BuildSummary summary = report.summary;
            Debug.Log(string.Format("[BuildScript] Result {0}: {1} errors, {2} warnings, {3:N0} bytes, {4}",
                summary.result, summary.totalErrors, summary.totalWarnings, summary.totalSize, summary.totalTime));

            if (summary.result != BuildResult.Succeeded) Fail("Build " + summary.result);
            else if (Application.isBatchMode) EditorApplication.Exit(0);
        }

        private static void Fail(string message)
        {
            Debug.LogError("[BuildScript] " + message);
            if (Application.isBatchMode) EditorApplication.Exit(1);
        }

        private static string ArgValue(string name)
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
                if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase)) return args[i + 1];
            return null;
        }
    }
}
