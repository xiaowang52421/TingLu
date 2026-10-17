using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace TingLu.Diagnostics.Editor
{
    public static class WindowsDemoBuild
    {
        public static void Build()
        {
            string output = Environment.GetEnvironmentVariable("TINGLU_BUILD_PATH");
            if (string.IsNullOrWhiteSpace(output))
                throw new InvalidOperationException("TINGLU_BUILD_PATH must name the output executable.");
            string[] scenes = EditorBuildSettings.scenes.Where(scene => scene.enabled).Select(scene => scene.path).ToArray();
            if (scenes.Length == 0 || scenes.Any(scene => !File.Exists(scene)))
                throw new InvalidOperationException("Build Settings must contain existing enabled scenes.");
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output)));
            BuildReport report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = output,
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.Development
            });
            if (report.summary.result != BuildResult.Succeeded)
                throw new InvalidOperationException("Windows build failed: " + report.summary.result + ", errors: " + report.summary.totalErrors);
            Debug.Log("TingLu Windows build succeeded: " + output);
        }
    }
}
