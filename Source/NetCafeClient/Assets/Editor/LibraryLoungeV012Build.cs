using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;
using TMPro;
using UnityEngine.TextCore.LowLevel;
using System.Linq;

public static class LibraryLoungeV012Build
{
    public static void Build()
    {
#if !NETCAFE_B1
        throw new InvalidOperationException("NETCAFE_B1 must be enabled");
#else
        LibraryLoungeBuild.Validate();
        PrepareFonts();
        VerifyContact();
        PlayerSettings.bundleVersion = "0.1.2";
        string root = FindRoot();
        string output = Path.Combine(root,"Builds","LibraryLounge-v0.1.2-UI-20261106");
        // Rebuild only our candidate; old builds and frozen snapshots are never targets.
        Directory.CreateDirectory(output);
        var result = BuildPipeline.BuildPlayer(new BuildPlayerOptions {
            scenes = new[]{LibraryLoungeBuild.ScenePath}, target = BuildTarget.StandaloneWindows64,
            locationPathName = Path.Combine(output,"LibraryLounge.exe"), options = BuildOptions.Development
        });
        if(result.summary.result != BuildResult.Succeeded)throw new Exception("v0.1.2 build: "+result.summary.result);
        Debug.Log("V012_BUILD_PASS "+output+" size="+result.summary.totalSize);
#endif
    }
    static string FindRoot()
    {
        var parent=new DirectoryInfo(Application.dataPath);
        while(parent!=null)
        {
            if(File.Exists(Path.Combine(parent.FullName,"CURRENT-AUTHORITY.json")))return parent.FullName;
            parent=parent.Parent;
        }
        throw new DirectoryNotFoundException("Project authority root not found");
    }
    static void VerifyContact()
    {
        // Dense independent surface samples: moving teeth may never enter a strip.
        Vector2[] edge = {
            new Vector2(-260,-24),new Vector2(-238,0),new Vector2(-220,0),new Vector2(-210,18),new Vector2(-202,18),new Vector2(-190,-20),new Vector2(-181,-20),
            new Vector2(-170,8),new Vector2(-159,8),new Vector2(-147,-26),new Vector2(-136,-26),new Vector2(-125,36),new Vector2(-113,36),new Vector2(-101,0),
            new Vector2(-87,0),new Vector2(-76,-18),new Vector2(-64,-18),new Vector2(-51,22),new Vector2(-39,22),new Vector2(-27,-12),new Vector2(-15,-12),new Vector2(-4,0),new Vector2(95,0) };
        float Y(float x) {
            for(int i=1;i<edge.Length;i++)if(x<=edge[i].x)return edge[i-1].y+(edge[i].y-edge[i-1].y)*(x-edge[i-1].x)/(edge[i].x-edge[i-1].x);
            return 0;
        }
        int checks=0,bidirectional=0;
        for(int strip=0;strip<8;strip++) {
            bool up=false,down=false;float previous=CafeKeyGraphic.Contact(strip,380);
            for(int step=0;step<=760;step++) {
                float dx=380-step*.5f,h=CafeKeyGraphic.Contact(strip,dx);
                up|=h<previous-.01f;down|=h>previous+.01f;previous=h;
                for(int x=0;x<=100;x++) {
                    float position=-245+strip*42.5f+x*42.5f/100;
                    if(position<-260+dx)continue;
                    if(Y(position-dx)>Y(position)+h+.005f)throw new Exception("Key penetrates strip "+strip+" at "+dx);
                    checks++;
                }
            }
            if(up&&down)bidirectional++;
            if(Mathf.Abs(CafeKeyGraphic.Contact(strip,0))>.001f)throw new Exception("Key strips fail final alignment");
        }
        if(bidirectional<4)throw new Exception("Key strips must respond in both vertical directions");
        Debug.Log("V012_KEY_CONTACT_PASS checks="+checks+" bidirectional_strips="+bidirectional);
    }
    static void PrepareFonts()
    {
        const string folder="Assets/Resources/LoungeFonts/";
        string characters=File.ReadAllText(folder+"UiFontCharacterSet.txt");
        foreach(string script in Directory.GetFiles("Assets/Scripts","*.cs"))characters+=File.ReadAllText(script);
        characters=new string(characters.Where(c=>!char.IsControl(c)).Distinct().ToArray());
        foreach(string prefix in new[]{"NotoSansSC","NotoSerifSC"})
        {
            string path=folder+prefix+"-Dynamic.asset";
            var asset=AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(path);
            if(asset!=null)continue;
            var source=AssetDatabase.LoadAssetAtPath<Font>(folder+prefix+"-Regular.ttf");
            if(source==null)throw new FileNotFoundException("Regular font source is missing: "+prefix);
            asset=TMP_FontAsset.CreateFontAsset(source,64,6,GlyphRenderMode.SDFAA,2048,2048,AtlasPopulationMode.Dynamic,true);
            if(asset==null)throw new Exception("TMP font creation failed");
            AssetDatabase.CreateAsset(asset,path);
            asset.TryAddCharacters(characters,out string missing);
            if(!AssetDatabase.Contains(asset.material))AssetDatabase.AddObjectToAsset(asset.material,asset);
            foreach(var texture in asset.atlasTextures)if(texture!=null&&!AssetDatabase.Contains(texture))AssetDatabase.AddObjectToAsset(texture,asset);
            var serialized = new SerializedObject(asset);
            var clear = serialized.FindProperty("m_ClearDynamicDataOnBuild"); if(clear!=null)clear.boolValue=false;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(asset);AssetDatabase.SaveAssets();
            Debug.Log("V012_FONT_READY "+prefix+" glyphs="+asset.characterTable.Count+" missing="+(missing?.Length??0));
        }
    }
}
