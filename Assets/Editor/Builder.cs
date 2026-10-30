using System.Collections.Generic;
using System.IO;
using System;
using UnityEditor.Build.Reporting;
using UnityEditor;
using UnityEngine;

namespace CoalRun.Editor
{
    // Builds the game for a release: the scenes ticked in Build Settings, for each target this editor has the
    // module for, into dist/<version>/<target>/. Tools/release.py drives it (through the open editor, or batch
    // mode when it's closed), then zips the builds and can put them on a GitHub release. It writes
    // dist/<version>/build.txt last, a line a target, so the script can tell it's finished and how it went.
    public static class Builder
    {
        private static readonly (BuildTarget target, BuildTargetGroup group, string name, string file)[] Targets =
        {
            (BuildTarget.StandaloneWindows64, BuildTargetGroup.Standalone, "Windows", "Coal Run.exe"),
            (BuildTarget.StandaloneLinux64, BuildTargetGroup.Standalone, "Linux", "Coal Run.x86_64"),
            // A folder of files (index.html and the rest) to serve as it is.
            (BuildTarget.WebGL, BuildTargetGroup.WebGL, "WebGL", "")
        };

        // Sets the game's version and builds every installed target. True if none failed.
        public static bool Build(string version, string output = null, string[] targets = null)
        {
            PlayerSettings.bundleVersion = version;
            AssetDatabase.SaveAssets();
            var scenes = new List<string>();
            foreach (var s in EditorBuildSettings.scenes)
                if (s.enabled)
                    scenes.Add(s.path);

            if (scenes.Count == 0) throw new InvalidOperationException("No enabled build scenes.");
            var dir = output ?? Path.Combine("dist", version);
            Directory.CreateDirectory(dir);
            // Building for another platform switches the editor to it; put it back after.
            var was = EditorUserBuildSettings.activeBuildTarget;
            var wasGroup = BuildPipeline.GetBuildTargetGroup(was);
            var lines = new List<string>();
            var ok = true;
            foreach (var (target, group, name, file) in Targets)
            {
                if (targets != null && Array.IndexOf(targets, name) < 0) continue;
                if (Directory.Exists(Path.Combine(dir, name)))
                    throw new InvalidOperationException("Target output already exists: " + Path.Combine(dir, name));
                if (!BuildPipeline.IsBuildTargetSupported(group, target))
                {
                    lines.Add($"{name} skipped: its build module isn't installed");
                    continue;
                }

                var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
                {
                    scenes = scenes.ToArray(),
                    locationPathName = Path.Combine(dir, name, file),
                    target = target,
                    targetGroup = group,
                    options = BuildOptions.StrictMode
                });
                var summary = report.summary;
                var passed = summary.result == BuildResult.Succeeded && summary.totalErrors == 0;
                ok &= passed;
                var clips = 0;
                foreach (var asset in report.packedAssets)
                foreach (var entry in asset.contents)
                    if (entry.type == typeof(AudioClip))
                        clips++;
                lines.Add(
                    $"{name} {(passed ? "Succeeded" : "Failed")} {summary.totalSize / 1048576f:0.0} MB in {summary.totalTime.TotalSeconds:0} s; {summary.totalErrors} errors; {clips} audio clips");
            }

            // A batch Editor is about to exit; restoring its target only triggers another import.
            if (!Application.isBatchMode && EditorUserBuildSettings.activeBuildTarget != was)
                EditorUserBuildSettings.SwitchActiveBuildTarget(wasGroup, was);
            File.WriteAllLines(Path.Combine(dir, "build.txt"), lines);
            return ok;
        }

        // -version is Unity's own flag (prints its version and exits); use a separate release flag.
        // Unity -batchmode -quit -projectPath . -executeMethod CoalRun.Editor.Builder.BuildFromCommandLine -releaseVersion 0.2
        public static void BuildFromCommandLine()
        {
            var args = System.Environment.GetCommandLineArgs();
            var i = Array.IndexOf(args, "-releaseVersion");
            var version = i >= 0 && i + 1 < args.Length ? args[i + 1] : PlayerSettings.bundleVersion;
            i = Array.IndexOf(args, "-buildOutput");
            var output = i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
            i = Array.IndexOf(args, "-releaseTargets");
            var targets = i >= 0 && i + 1 < args.Length ? args[i + 1].Split(',') : null;
            try
            {
                EditorApplication.Exit(Build(version, output, targets) ? 0 : 1);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                EditorApplication.Exit(1);
            }
        }
    }
}
