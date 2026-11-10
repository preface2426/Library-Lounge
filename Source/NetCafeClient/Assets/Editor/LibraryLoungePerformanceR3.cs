using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using TMPro;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.Build;
using UnityEngine;

public static class LibraryLoungePerformanceR3
{
    public static void BuildRed() => Build("R3-Red", false);
    public static void BuildQa() => Build("R3", true);
    public static void BuildRoomServiceFix() => Build("R3-Wake", true);
    public static void BuildMemoryRelease() => Build("R4-Release", true);
    public static void BuildPortableAcceptance() => Build("R5-Release", true);
    public static void BuildAdmissionFix() => Build("R7-Release", true);
    static void Build(string revision, bool optimizeFonts)
    {
        if (optimizeFonts)
        {
            foreach (string name in new[] { "NotoSansSC-Dynamic", "NotoSerifSC-Dynamic" })
            {
                var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/Resources/LoungeFonts/" + name + ".asset");
                if (font == null || font.atlasPopulationMode != AtlasPopulationMode.Dynamic || font.sourceFontFile == null)
                    throw new Exception("R3 dynamic source font missing: " + name);
                var serialized = new SerializedObject(font);
                serialized.FindProperty("m_ClearDynamicDataOnBuild").boolValue = true;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(font);
            }
            AssetDatabase.SaveAssets();
        }
        string date = revision == "R3-Wake" || revision == "R4-Release" || revision == "R5-Release" || revision == "R7-Release" ? "20261108" : "20261107";
        string output = Path.Combine(LibraryLoungeUiFixBuild.FindRoot(), "Builds/LibraryLounge-v0.1.3-Restroom-" + revision + "-" + date);
        Directory.CreateDirectory(output);
        PlayerSettings.bundleVersion = "0.1.3";
        var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions {
            scenes = new[] { LibraryLoungeRestroomBuild.ScenePath }, target = BuildTarget.StandaloneWindows64,
            locationPathName = Path.Combine(output, "LibraryLounge.exe"),
            options = revision == "R4-Release" || revision == "R5-Release" || revision == "R7-Release" ? BuildOptions.None : BuildOptions.Development,
            extraScriptingDefines = revision == "R5-Release" || revision == "R7-Release" ? new[] { "NETCAFE_ACCEPTANCE" } : Array.Empty<string>()
        });
        if (report.summary.result != BuildResult.Succeeded) throw new Exception("R3 build failed: " + report.summary.result);
        Debug.Log("PERFORMANCE_BUILD_PASS " + output);
    }
}

// TMP's order-0 callback removes accumulated dynamic glyphs. Warm only UI literals
// afterwards in the Editor, so first launch doesn't rasterize both Chinese faces.
public sealed class LibraryLoungeUiFontWarmup : IPreprocessBuildWithReport
{
    public int callbackOrder => 10;
    public void OnPreprocessBuild(BuildReport report)
    {
        // Other legacy builders keep their existing font policy.
        if (!report.summary.outputPath.Contains("Restroom-R3-20261107") &&
            !report.summary.outputPath.Contains("Restroom-R3-Wake-20261108") &&
            !report.summary.outputPath.Contains("Restroom-R4-Release-20261108") &&
            !report.summary.outputPath.Contains("Restroom-R5-Release-20261108") &&
            !report.summary.outputPath.Contains("Restroom-R7-Release-20261108")) return;
        var characters = new SortedSet<char>(Enumerable.Range(32,95).Select(c => (char)c));
        var scripts = Path.Combine(Application.dataPath,"Scripts");
        foreach (string file in Directory.GetFiles(scripts,"*.cs"))
        {
            string name=Path.GetFileName(file);
            if (!(name.StartsWith("CafeEntryUi")||name.StartsWith("CafeHud")||name.StartsWith("CafeLounge")||
                name=="CafeSpatialVoice.cs"||name=="CafeDesktopCapture.cs"||name=="CafeDesktopSharing.cs"||name=="CafeRoomTransition.cs")) continue;
            foreach(Match literal in Regex.Matches(File.ReadAllText(file), "\"(?:[^\"\\\\]|\\\\.)*\""))
                foreach(char c in literal.Value) if(c>=32&&!char.IsSurrogate(c)) characters.Add(c);
        }
        string text=new string(characters.ToArray());
        foreach(string name in new[]{"NotoSansSC-Dynamic","NotoSerifSC-Dynamic"})
        {
            var font=AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/Resources/LoungeFonts/"+name+".asset");
            font.TryAddCharacters(text,out string missing);
            // Some decorative arrows are deliberately supplied by the existing fallback.
            if(missing.Any(c=>c>='\u3400'&&c<='\u9fff')) throw new Exception("Missing UI Chinese glyph in "+name);
            EditorUtility.SetDirty(font);
            Debug.Log("R3_FONT_WARMUP "+name+" UI="+text.Length+" cached="+font.characterTable.Count+" atlases="+font.atlasTextureCount);
        }
        AssetDatabase.SaveAssets();
    }
}
