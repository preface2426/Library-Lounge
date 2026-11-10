using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

public static class LibraryLoungeDesktopCloseBuild
{
    public static void Build()
    {
        string label = "";
        foreach (string arg in Environment.GetCommandLineArgs())
            if (arg.StartsWith("-desktopCloseBuild=")) label = arg.Substring("-desktopCloseBuild=".Length);
        if (label != "Red" && label != "Green" && label != "Green2" && label != "Final") throw new Exception("Explicit acceptance label required");
        string output = Path.Combine(LibraryLoungeUiFixBuild.FindRoot(),
            "Builds/LibraryLounge-v0.1.3-Restroom-R8-" + label + "-20261108/LibraryLounge.exe");
        if (File.Exists(output)) throw new Exception("Preserve existing acceptance build");
        var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions {
            scenes = new[] { LibraryLoungeRestroomBuild.ScenePath },
            target = BuildTarget.StandaloneWindows64, locationPathName = output,
            options = BuildOptions.None, extraScriptingDefines = new[] { "NETCAFE_ACCEPTANCE" }
        });
        if (report.summary.result != BuildResult.Succeeded) throw new Exception("Desktop close build failed");
        Debug.Log("DESKTOP_CLOSE_BUILD_PASS " + output);
    }
}
