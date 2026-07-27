#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

public static class BuildWindows
{
    [MenuItem("MenuBar Tetra/Build Windows Player")]
    public static void Build()
    {
        const string output = "Builds/Windows/MenuBarTetra.exe";
        var report = BuildPipeline.BuildPlayer(new[] { "Assets/Scenes/Main.unity" }, output, BuildTarget.StandaloneWindows64, BuildOptions.None);
        if (report.summary.result != BuildResult.Succeeded)
            Debug.LogError("Windows build failed: " + report.summary.result);
        else
            Debug.Log("Built " + output + ". Copy MenuBarTetraTray.exe beside it to use the tray launcher.");
    }
}
#endif
