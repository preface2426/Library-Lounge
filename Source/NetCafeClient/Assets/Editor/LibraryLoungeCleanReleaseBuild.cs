using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class LibraryLoungeCleanReleaseBuild
{
    public static void RunCleanRelease20261102()
    {
#if !NETCAFE_B1
        throw new InvalidOperationException("NETCAFE_B1 must be enabled for the integrated multiplayer build");
#else
        LibraryLoungeBuild.Validate();
        string root = Path.GetFullPath(Path.Combine(Application.dataPath, "../../../../Builds"));
        string target = Path.Combine(root, "LibraryLounge-Cinema-PerfSecurity-Release-20261102");
        string stage = target + ".staging";
        string backup = target + "-PreScreenshotFix-20261102";
        if (Directory.Exists(stage))
            throw new IOException("Refusing to overwrite an existing staging directory: " + stage);
        if (Directory.Exists(backup))
            throw new IOException("Refusing to overwrite preserved failed-build backup: " + backup);

        Directory.CreateDirectory(stage);
        string stageExe = Path.Combine(stage, "LibraryLounge.exe");
        BuildReport report = BuildPipeline.BuildPlayer(new BuildPlayerOptions {
            scenes = new[] { LibraryLoungeBuild.ScenePath },
            locationPathName = stageExe,
            target = BuildTarget.StandaloneWindows64,
            options = BuildOptions.Development
        });
        if (report.summary.result != BuildResult.Succeeded || !File.Exists(stageExe))
            throw new InvalidOperationException("Clean QA player build failed: " + report.summary.result);

        bool movedOld = false;
        try
        {
            if (Directory.Exists(target))
            {
                Directory.Move(target, backup);
                movedOld = true;
            }
            Directory.Move(stage, target);
        }
        catch
        {
            if (movedOld && !Directory.Exists(target) && Directory.Exists(backup))
                Directory.Move(backup, target);
            throw;
        }

        string exe = Path.Combine(target, "LibraryLounge.exe");
        if (!File.Exists(exe) || new FileInfo(exe).Length == 0)
            throw new InvalidOperationException("Installed clean build is missing executable");
        Debug.Log("LIBRARY_LOUNGE_CLEAN_RELEASE_BUILD_PASS: " + exe +
            "; development=true; B1=enabled; previous_failed_build_preserved=" + movedOld +
            "; backup=" + (movedOld ? backup : "none") +
            "; launcher_pointer_unchanged=true");
#endif
    }
}
