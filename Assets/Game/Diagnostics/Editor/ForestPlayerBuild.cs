using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Gamex.Diagnostics.Editor
{
    /// <summary>
    /// Builds a macOS development player of the forest scene for the frame-time harness (Gamex.Diagnostics.ForestFrameHarness).
    /// The scene list is given here, so Build Settings (EditorBuildSettings.asset) are neither read nor changed. Batch mode:
    ///   Unity -batchmode -projectPath P -executeMethod Gamex.Diagnostics.Editor.ForestPlayerBuild.BuildMacDevelopment
    ///         [-forestBuildOut Builds/ForestDev/Forest.app] -quit
    /// Throws when the build does not succeed, so Unity exits with code 1.
    /// </summary>
    public static class ForestPlayerBuild
    {
        public const string ScenePath = "Assets/Game/Scenes/Forest.unity";
        public const string DefaultOutput = "Builds/ForestDev/Forest.app";

        public static void BuildMacDevelopment()
        {
            string output = Value("-forestBuildOut", DefaultOutput);
            if (!File.Exists(ScenePath))
                throw new InvalidOperationException($"{ScenePath} does not exist: build the forest scene first.");

            var options = new BuildPlayerOptions
            {
                scenes = new[] { ScenePath },
                locationPathName = output,
                target = BuildTarget.StandaloneOSX,
                targetGroup = BuildTargetGroup.Standalone,
                options = BuildOptions.Development,
            };
            Debug.Log($"Forest player build: {string.Join(", ", options.scenes)} -> {output}, {options.target}, {options.options}");
            BuildReport report = BuildPipeline.BuildPlayer(options);
            BuildSummary summary = report.summary;
            Debug.Log($"Forest player build: {summary.result}, {summary.totalErrors} errors, {summary.totalWarnings} warnings, " +
                      $"{summary.totalSize / 1048576.0:0.0} MiB, {summary.totalTime.TotalSeconds:0} s, output {summary.outputPath}, " +
                      $"platform {summary.platform}, options {summary.options}");
            foreach (BuildStep step in report.steps.Where(s => s.duration.TotalSeconds >= 5.0))
                Debug.Log($"Forest player build step: {step.name} {step.duration.TotalSeconds:0} s");
            if (summary.result != BuildResult.Succeeded)
                throw new InvalidOperationException($"The forest player build ended with {summary.result} ({summary.totalErrors} errors).");
        }

        static string Value(string name, string fallback)
        {
            string[] arguments = Environment.GetCommandLineArgs();
            int index = Array.IndexOf(arguments, name);
            return index >= 0 && index + 1 < arguments.Length ? arguments[index + 1] : fallback;
        }
    }
}
