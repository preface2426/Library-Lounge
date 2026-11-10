using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

// Editor-only GPU-rendered evidence. Never changes saved scene/gameplay camera.
public static class LibraryLoungeFurnitureRenderAudit
{
    [MenuItem("NetCafe/Library Lounge/Capture refined furnished room previews")]
    public static void Capture()
    {
        EditorSceneManager.OpenScene(LibraryLoungeBuild.ScenePath,OpenSceneMode.Single);
        var output=Path.GetFullPath(Path.Combine(Application.dataPath,"../../docs/FURNITURE-REFINED-RENDERS-20261031"));
        Directory.CreateDirectory(output);
        CaptureView(output,"workstation.png",new Vector3(2.50f,1.95f,.25f),new Vector3(2.50f,.77f,3.05f),63f);
        CaptureView(output,"reading.png",new Vector3(-2.90f,2.05f,-.65f),new Vector3(-2.90f,.80f,1.61f),64f);
        CaptureView(output,"social.png",new Vector3(.45f,2.25f,.10f),new Vector3(-1.55f,.78f,-2.89f),69f);
        Debug.Log("FURNITURE_RENDER_VIEWS_PASS: " + output);
    }
    [MenuItem("NetCafe/Library Lounge/Capture laptop-fixed previews")]
    public static void CaptureLaptops()
    {
        EditorSceneManager.OpenScene(LibraryLoungeBuild.ScenePath,OpenSceneMode.Single);
        string output=Path.GetFullPath(Path.Combine(Application.dataPath,"../../docs/LAPTOP-FIXED-RENDERS-20261031"));
        Directory.CreateDirectory(output);
        CaptureView(output,"reading-laptops.png",new Vector3(-3.0f,1.57f,.38f),new Vector3(-2.9f,.70f,2.00f),56f);
        CaptureView(output,"social-laptops.png",new Vector3(-2.20f,1.85f,-4.55f),new Vector3(-2.4f,.65f,-2.20f),54f);
        Debug.Log("LAPTOP_FIXED_RENDER_PASS: "+output);
    }
    [MenuItem("NetCafe/Library Lounge/Capture unified desk monitor side views")]
    public static void CaptureUnifiedDesktopStands()
    {
        EditorSceneManager.OpenScene(LibraryLoungeBuild.ScenePath,OpenSceneMode.Single);
        string output=Path.GetFullPath(Path.Combine(Application.dataPath,"../../docs/DESKTOP-STAND-FIX-RENDERS-20261031"));
        Directory.CreateDirectory(output);
        CaptureView(output,"desk-monitor-left-side.png",new Vector3(.20f,1.55f,2.62f),new Vector3(1.22f,1.04f,3.42f),51f);
        CaptureView(output,"desk-monitor-right-side.png",new Vector3(4.82f,1.65f,2.49f),new Vector3(3.72f,1.03f,3.42f),51f);
        Debug.Log("DESKTOP_STAND_SIDE_VIEW_RENDER_PASS: "+output);
    }
    [MenuItem("NetCafe/Library Lounge/Capture approved cinema lounge preview")]
    public static void CaptureCinemaRedesign()
    {
        EditorSceneManager.OpenScene(LibraryLoungeBuild.ScenePath,OpenSceneMode.Single);
        string dir=Path.GetFullPath(Path.Combine(Application.dataPath,"../../docs/CINEMA-REDESIGN-PREVIEW-20261031"));
        Directory.CreateDirectory(dir);
        CaptureView(dir,"01-screen-and-seating.png",new Vector3(-.90f,2.25f,.62f),new Vector3(3.25f,1.38f,-2.23f),79f);
        CaptureView(dir,"02-wall-screen-front.png",new Vector3(1.25f,1.78f,-1.95f),new Vector3(5.72f,1.65f,-2.10f),75f);
        CaptureView(dir,"03-cream-sectional-and-olive-chair.png",new Vector3(1.00f,2.15f,-.24f),new Vector3(2.17f,.63f,-3.00f),70f);
        Debug.Log("CINEMA_PREVIEW_RENDER_PASS: "+dir);
    }
    static void CaptureView(string dir,string filename,Vector3 eye,Vector3 target,float fov)
    {
        var go=new GameObject("TEMP EDITOR QA CAMERA - NOT SAVED");
        var camera=go.AddComponent<Camera>();
        camera.enabled=false;
        camera.clearFlags=CameraClearFlags.Skybox;
        camera.fieldOfView=fov;
        camera.nearClipPlane=.04f;camera.farClipPlane=80f;
        camera.transform.position=eye;
        camera.transform.rotation=Quaternion.LookRotation(target-eye,Vector3.up);
        RenderTexture rt=RenderTexture.GetTemporary(1280,720,24);
        Texture2D tex=new Texture2D(1280,720,TextureFormat.RGB24,false);
        var old=RenderTexture.active;
        try
        {
            RenderPipeline.SubmitRenderRequest(camera,new UniversalRenderPipeline.SingleCameraRequest{destination=rt});
            RenderTexture.active=rt;
            tex.ReadPixels(new Rect(0,0,1280,720),0,0);tex.Apply();
            Color c=tex.GetPixel(0,0);int variation=0;
            for(int y=20;y<720;y+=60)for(int x=20;x<1280;x+=60)
                if(tex.GetPixel(x,y)!=c) variation++;
            if(variation<12)throw new InvalidOperationException("Blank GPU render for "+filename);
            string fp=Path.Combine(dir,filename);
            File.WriteAllBytes(fp,tex.EncodeToPNG());
            Debug.Log("FURNITURE_RENDER_SAVED: "+fp+" variation="+variation);
        }
        finally
        {
            RenderTexture.active=old;
            RenderTexture.ReleaseTemporary(rt);
            UnityEngine.Object.DestroyImmediate(tex);
            UnityEngine.Object.DestroyImmediate(go);
        }
    }
}
