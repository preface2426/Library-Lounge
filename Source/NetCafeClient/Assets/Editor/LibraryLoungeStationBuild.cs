using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

public static class LibraryLoungeStationBuild
{
    public static void Build() => Build("R9", "station");
    public static void BuildWall() => Build("R10", "wall");
    public static void BuildMouse() => Build("R11", "mouse");

    static void Build(string revision, string probe)
    {
        string label = "";
        foreach (var arg in Environment.GetCommandLineArgs())
            if (arg.StartsWith("-" + probe + "Build=", StringComparison.Ordinal)) label = arg.Substring(probe.Length + 7);
        if (label != "Red" && label != "Green" && label != "Green2" && label != "Final" &&
            !(revision == "R11" && (label == "Release" || label == "Verified" ||
              label == "ShareRed" || label == "ShareQA" || label == "ShareQA2" || label == "ShareQA3" ||
              label == "Share" || label == "Share2" || label == "Share3" ||
              label == "StandRed" || label == "StandQA" || label == "StandQA2" || label == "StandQA3" || label == "Stand" || label == "Stand2" ||
              label == "FontReset" || label == "NativeDisplayRed" || label == "NativeDisplay" ||
              label == "MediaQA" || label == "MediaQA2" || label == "MediaQA3" || label == "Media" ||
              label == "WallDefaultQA" || label == "WallDefault" ||
              label == "WallClarityQA" || label == "WallClarity" ||
              label == "PlayerRed" || label == "PlayerQA" || label == "PlayerQA2" || label == "PlayerRecovery" || label == "PlayerRecovery2")))
            throw new InvalidOperationException("Use an explicit station build label");
        string date = label.StartsWith("WallDefault", StringComparison.Ordinal) ||
            label.StartsWith("WallClarity", StringComparison.Ordinal) ||
            label.StartsWith("Player", StringComparison.Ordinal) ? "20261110" : "20261109";
        var output = Path.Combine(LibraryLoungeUiFixBuild.FindRoot(),
            "Builds/LibraryLounge-v0.1.3-Restroom-" + revision + "-" + label + "-" + date + "/LibraryLounge.exe");
        if (File.Exists(output)) throw new InvalidOperationException("Preserve existing station build");
        var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions {
            scenes = new[] { LibraryLoungeRestroomBuild.ScenePath },
            target = BuildTarget.StandaloneWindows64, locationPathName = output,
            options = label == "Final" || label == "Release" || label == "Verified" ||
                label == "Share" || label == "Share2" || label == "Share3" || label == "Stand" || label == "Stand2" || label == "FontReset" ||
                label == "NativeDisplayRed" || label == "NativeDisplay" || label == "Media" ||
                label == "WallDefault" || label == "WallClarity" || label == "PlayerRecovery" || label == "PlayerRecovery2"
                ? BuildOptions.None : BuildOptions.Development,
            extraScriptingDefines = new[] { "NETCAFE_ACCEPTANCE" }
        });
        if (report.summary.result != BuildResult.Succeeded) throw new InvalidOperationException("Station build failed");
        Debug.Log("STATION_BUILD_PASS " + output);
    }
}
