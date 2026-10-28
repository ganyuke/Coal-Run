using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;

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
        public static bool Build(string version)
        {
            PlayerSettings.bundleVersion = version;
            AssetDatabase.SaveAssets();
            var scenes = new List<string>();
            foreach (var s in EditorBuildSettings.scenes) if (s.enabled) scenes.Add(s.path);

            var dir = Path.Combine("dist", version);
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
            Directory.CreateDirectory(dir);
            // Building for another platform switches the editor to it; put it back after.
            var was = EditorUserBuildSettings.activeBuildTarget;
            var wasGroup = BuildPipeline.GetBuildTargetGroup(was);
            var lines = new List<string>();
            var ok = true;
            foreach (var (target, group, name, file) in Targets)
            {
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
                    targetGroup = group
                });
                var summary = report.summary;
                ok &= summary.result == BuildResult.Succeeded;
                lines.Add($"{name} {summary.result} {summary.totalSize / 1048576f:0.0} MB in {summary.totalTime.TotalSeconds:0} s");
            }
            if (EditorUserBuildSettings.activeBuildTarget != was) EditorUserBuildSettings.SwitchActiveBuildTarget(wasGroup, was);
            File.WriteAllLines(Path.Combine(dir, "build.txt"), lines);
            return ok;
        }

        // Batch mode: Unity -batchmode -quit -projectPath . -executeMethod CoalRun.Editor.Builder.BuildFromCommandLine -version 0.2
        public static void BuildFromCommandLine()
        {
            var args = System.Environment.GetCommandLineArgs();
            var i = System.Array.IndexOf(args, "-version");
            var version = i >= 0 && i + 1 < args.Length ? args[i + 1] : PlayerSettings.bundleVersion;
            EditorApplication.Exit(Build(version) ? 0 : 1);
        }
    }
}
