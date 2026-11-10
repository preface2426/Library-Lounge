using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class LibraryLoungeUiFixBuild
{
    public static void Baseline()
    {
        EditorSceneManager.OpenScene(LibraryLoungeBuild.ScenePath);
        EditorApplication.isPlaying=true;
    }
    public static void Build()
    {
        var portraits=(TextureImporter)AssetImporter.GetAtPath("Assets/Resources/LoungePortraits/characters-5x2.png");
        portraits.alphaIsTransparency=true;portraits.mipmapEnabled=false;portraits.npotScale=TextureImporterNPOTScale.None;portraits.wrapMode=TextureWrapMode.Clamp;portraits.SaveAndReimport();
        LibraryLoungeBuild.Validate();
        PlayerSettings.bundleVersion="0.1.2";
        string output=Path.Combine(FindRoot(),"Builds/LibraryLounge-v0.1.2-UIFix-R2-20261107");
        Directory.CreateDirectory(output);
        var result=BuildPipeline.BuildPlayer(new BuildPlayerOptions { scenes=new[]{LibraryLoungeBuild.ScenePath},target=BuildTarget.StandaloneWindows64,
            locationPathName=Path.Combine(output,"LibraryLounge.exe"),options=BuildOptions.Development });
        if(result.summary.result!=BuildResult.Succeeded)throw new Exception("UIFIX_BUILD_FAILED "+result.summary.result);
        Debug.Log("UIFIX_BUILD_PASS "+output);
    }
    public static string FindRoot()
    {
        for(var p=new DirectoryInfo(Application.dataPath);p!=null;p=p.Parent)
            if(File.Exists(Path.Combine(p.FullName,"CURRENT-AUTHORITY.json")))return p.FullName;
        throw new DirectoryNotFoundException("Project authority root missing");
    }
    public static void AvatarReference()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
        RenderSettings.ambientMode=UnityEngine.Rendering.AmbientMode.Flat;
        RenderSettings.ambientLight=new Color(.78f,.77f,.72f);
        var light=new GameObject("Reference Light").AddComponent<Light>();light.type=LightType.Directional;light.intensity=1.5f;light.transform.rotation=Quaternion.Euler(35,-35,0);
        var camera=new GameObject("Reference Camera").AddComponent<Camera>();camera.orthographic=true;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.94f,.92f,.88f);
        var target=new RenderTexture(512,512,24);camera.targetTexture=target;
        var sheet=new Texture2D(2560,1024,TextureFormat.RGB24,false);
        var folder=Path.Combine(FindRoot(),"Workspaces/LibraryLounge-v0.1.2-UIFix-20261106/Docs/AvatarReference");Directory.CreateDirectory(folder);
        for(int i=0;i<CafeAvatarLibrary.Ids.Length;i++)
        {
            var id=CafeAvatarLibrary.Ids[i];var prefab=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prototype/Characters/Gobkit/"+id+"Avatar.prefab");
            var model=UnityEngine.Object.Instantiate(prefab);
            var renderers=model.GetComponentsInChildren<Renderer>();var bounds=renderers[0].bounds;foreach(var r in renderers)bounds.Encapsulate(r.bounds);
            camera.orthographicSize=bounds.size.y*.62f;
            camera.transform.position=bounds.center+new Vector3(.25f,.13f,1.8f)*bounds.size.y;camera.transform.LookAt(bounds.center+Vector3.up*bounds.size.y*.04f);
            camera.nearClipPlane=.01f;camera.farClipPlane=100;
            camera.Render();RenderTexture.active=target;
            var frame=new Texture2D(512,512,TextureFormat.RGB24,false);frame.ReadPixels(new Rect(0,0,512,512),0,0);frame.Apply();
            File.WriteAllBytes(Path.Combine(folder,id+".png"),frame.EncodeToPNG());sheet.SetPixels(i%5*512,(1-i/5)*512,512,512,frame.GetPixels());
            UnityEngine.Object.DestroyImmediate(frame);UnityEngine.Object.DestroyImmediate(model);
        }
        sheet.Apply();File.WriteAllBytes(Path.Combine(folder,"characters-5x2.png"),sheet.EncodeToPNG());RenderTexture.active=null;
        UnityEngine.Object.DestroyImmediate(sheet);UnityEngine.Object.DestroyImmediate(camera.gameObject);UnityEngine.Object.DestroyImmediate(light.gameObject);target.Release();UnityEngine.Object.DestroyImmediate(target);
        Debug.Log("UIFIX_AVATAR_REFERENCE_PASS "+folder);
    }
}
