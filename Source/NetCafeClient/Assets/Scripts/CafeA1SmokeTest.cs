using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using UnityEngine;
using Debug = UnityEngine.Debug;

// Opt-in hardware integration test. Normal play never runs the pattern or readback.
public sealed class CafeA1SmokeTest : MonoBehaviour
{
    [DllImport("NetCafeCapture", CallingConvention = CallingConvention.Cdecl)] static extern int NC_TestBegin();
    [DllImport("NetCafeCapture", CallingConvention = CallingConvention.Cdecl)] static extern int NC_TestCovered();
    [DllImport("NetCafeCapture", CallingConvention = CallingConvention.Cdecl)] static extern void NC_TestEnd();
    [DllImport("NetCafeCapture", CallingConvention = CallingConvention.Cdecl, CharSet=CharSet.Unicode)] static extern void NC_TestDiagnostic(StringBuilder text,int capacity);
    [DllImport("NetCafeCapture", CallingConvention = CallingConvention.Cdecl)] static extern int NC_GetMemory(out ulong privateBytes,out ulong workingBytes);
    CafeDesktopCapture desktop;
    bool testing;
    string folder;

    IEnumerator Start()
    {
        if (Array.IndexOf(Environment.GetCommandLineArgs(), "-a1Smoke") < 0) yield break;
        desktop = GetComponent<CafeDesktopCapture>();
        folder = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Evidence", "A1"));
        Directory.CreateDirectory(folder);
        var stack = new Stack<IEnumerator>(); stack.Push(Run());
        Exception failure = null;
        while (stack.Count > 0)
        {
            var test = stack.Peek();
            bool more = false;
            try { more = test.MoveNext(); }
            catch (Exception e) { failure = e; }
            if (failure != null) break;
            if (!more) { (stack.Pop() as IDisposable)?.Dispose(); continue; }
            if (test.Current is IEnumerator child) { stack.Push(child); continue; }
            yield return test.Current;
        }
        while (stack.Count > 0) (stack.Pop() as IDisposable)?.Dispose();
        desktop.StopCapture();
        yield return null;
        if (testing) { NC_TestEnd(); testing = false; }
        if (failure != null) Debug.LogException(failure);
        else Debug.Log("A1_SMOKE_PASS: same-monitor full-window exclusion, animated GPU texture, stop/restart, invalid-monitor failure, rendered screen");
        Application.Quit(failure == null ? 0 : 1);
    }

    IEnumerator Run()
    {
        while (!UnityEngine.Rendering.SplashScreen.isFinished) yield return null;
        yield return new WaitForSecondsRealtime(1);
        testing = true; Require(NC_TestBegin() != 0, "Controlled background test window could not start");
        yield return new WaitForSecondsRealtime(1);
        Require(NC_TestCovered() != 0, "Client window does not cover its selected monitor");
        Debug.Log($"A1_ENV: {SystemInfo.operatingSystem}; {SystemInfo.processorType}; {SystemInfo.graphicsDeviceName}; RAM={SystemInfo.systemMemorySize}MB; monitors={CafeDesktopCapture.NC_MonitorCount()}; GPU={SystemInfo.graphicsDeviceType}");
        GetComponent<CafePrototype>().Sit(0);
        using var process = Process.GetCurrentProcess();
        yield return Measure(process, "baseline", 4);
        Require(desktop.BeginCapture(0), desktop.Error);
        yield return WaitForFrames(3);
        Require(desktop.Info.excluded == 1, "Client exclusion was not enabled");
        Debug.Log($"A1_CAPTURE_SIZE: {desktop.Info.width}x{desktop.Info.height}");
        var diagnostic = new StringBuilder(1024); NC_TestDiagnostic(diagnostic,diagnostic.Capacity); Debug.Log("A1_WINDOWS: "+diagnostic);
        int cyan = 0, magenta = 0;
        for (int i = 0; i < 18; i++)
        {
            yield return new WaitForSecondsRealtime(.13f);
            var image = ReadDesktop();
            try
            {
                // Raw capture texture has top-down rows. Four controlled colors
                // must remain visible under the excluded, monitor-sized client.
                if (i == 0)
                {
                    // Compare direct BGRA GPU readback with the RenderTexture
                    // Blit path. This determines whether the dimming precedes
                    // or occurs during Unity's color-space conversion.
                    var raw = UnityEngine.Rendering.AsyncGPUReadback.Request(
                        desktop.DesktopTexture, 0, TextureFormat.RGBA32);
                    while (!raw.done) yield return null;
                    if (!raw.hasError)
                    {
                        var pixels = raw.GetData<Color32>();
                        int sample = (int)(desktop.Info.height * .20f) * desktop.Info.width +
                            (int)(desktop.Info.width * .12f);
                        Debug.Log("A1_COLOR_PATH rawGpu=" + pixels[sample] +
                            " blitReadback=" + (Color32)image.GetPixel((int)(image.width*.12f),(int)(image.height*.20f)) +
                            " activeColorSpace=" + QualitySettings.activeColorSpace);
                    }
                    else Debug.LogWarning("A1_COLOR_PATH direct_gpu_readback_unavailable");
                }
                Check(image, .12f,.20f,new Color32(220,40,40,255));
                Check(image, .88f,.20f,new Color32(40,200,70,255));
                Check(image, .12f,.70f,new Color32(40,80,220,255));
                Check(image, .88f,.70f,new Color32(230,200,40,255));
                Color center = image.GetPixel(image.width/2,image.height/2);
                if (center.g > .5f && center.r < .3f) cyan++;
                if (center.r > .5f && center.g < .3f) magenta++;
                if (i == 0) SaveTopDown(image, "controlled-desktop.png");
            }
            finally { Destroy(image); }
        }
        Require(cyan > 0 && magenta > 0, "Covered desktop animation stopped or colors were corrupted");
        Debug.Log($"A1_OCCLUSION_PASS: four quadrants preserved, cyan={cyan}, magenta={magenta}");
        yield return Measure(process, "capture", 6);
        var cafe = GetComponent<CafePrototype>();
        // Render just the world camera, demonstrating that the live desktop is
        // mapped onto the existing 3D monitor rather than an OnGUI replacement.
        var old = RenderTexture.active;
        var rt = RenderTexture.GetTemporary(1280,720,24);
        var shot = new Texture2D(1280,720,TextureFormat.RGB24,false);
        try
        {
            UnityEngine.Rendering.RenderPipeline.SubmitRenderRequest(cafe.eyes,
                new UnityEngine.Rendering.Universal.UniversalRenderPipeline.SingleCameraRequest{destination=rt});
            RenderTexture.active=rt; shot.ReadPixels(new Rect(0,0,1280,720),0,0); shot.Apply();
            File.WriteAllBytes(Path.Combine(folder,"seated-desktop.png"),shot.EncodeToPNG());
        }
        finally { RenderTexture.active=old; RenderTexture.ReleaseTemporary(rt); Destroy(shot); }
        for (int i=0;i<3;i++)
        {
            desktop.StopCapture(); yield return null; yield return null;
            Require(!desktop.Active && desktop.DesktopTexture == null, "Stopping left a visible desktop texture");
            Require(desktop.BeginCapture(0),desktop.Error); yield return WaitForFrames(3);
        }
        Debug.Log("A1_RESTART_PASS: three stop/start cycles");
        desktop.StopCapture(); yield return null;
        Require(!desktop.BeginCapture(int.MaxValue),"Invalid monitor was accepted");
        Require(desktop.Info.state == 3 && desktop.Error.Length > 0,"Capture failure was not reported");
        Debug.Log("A1_FAILURE_PASS: invalid monitor refused, user-facing error set");
        desktop.StopCapture(); yield return null; yield return null;
        yield return Measure(process,"stopped",4);
    }

    IEnumerator WaitForFrames(ulong minimum)
    {
        double timeout=Time.realtimeSinceStartupAsDouble+12;
        while (desktop.Info.frames<minimum)
        {
            Require(desktop.Active,desktop.Error);
            Require(Time.realtimeSinceStartupAsDouble<timeout,"Timed out waiting for GPU desktop frames: "+desktop.Status);
            yield return null;
        }
    }

    IEnumerator Measure(Process process,string stage,float seconds)
    {
        process.Refresh(); var cpu=process.TotalProcessorTime; double time=Time.realtimeSinceStartupAsDouble;
        ulong captured=desktop.Info.frames; int frames=Time.frameCount;
        yield return new WaitForSecondsRealtime(seconds);
        process.Refresh(); double elapsed=Time.realtimeSinceStartupAsDouble-time;
        Require(NC_GetMemory(out ulong privateBytes,out ulong workingBytes)!=0,"Process memory measurement failed");
        Debug.Log($"A1_METRIC {stage}: utc={DateTime.UtcNow:O}; seconds={elapsed:F2}; privateMB={privateBytes/1048576.0:F1}; workingMB={workingBytes/1048576.0:F1}; cpuOneCorePercent={(process.TotalProcessorTime-cpu).TotalSeconds/elapsed*100:F1}; unityFPS={(Time.frameCount-frames)/elapsed:F1}; captureFPS={(desktop.Info.frames>=captured?desktop.Info.frames-captured:0)/elapsed:F1}; copySubmitMs={desktop.Info.submitMs:F4}; frameAgeMs={desktop.Info.ageMs:F1}; sourceToCopyMs={desktop.Info.sourceAgeMs:F1}");
    }

    Texture2D ReadDesktop()
    {
        Require(desktop.DesktopTexture!=null,"No desktop GPU texture");
        int w=desktop.Info.width,h=desktop.Info.height;
        var old=RenderTexture.active; var rt=RenderTexture.GetTemporary(w,h,0,RenderTextureFormat.ARGB32,RenderTextureReadWrite.sRGB);
        var result=new Texture2D(w,h,TextureFormat.RGBA32,false);
        try { Graphics.Blit(desktop.DesktopTexture,rt); RenderTexture.active=rt; result.ReadPixels(new Rect(0,0,w,h),0,0); result.Apply(); return result; }
        catch { Destroy(result); throw; }
        finally { RenderTexture.active=old; RenderTexture.ReleaseTemporary(rt); }
    }
    static void Check(Texture2D image,float x,float y,Color32 expected)
    {
        Color32 actual=image.GetPixel((int)(image.width*x),(int)(image.height*y));
        Require(Math.Abs(actual.r-expected.r)<20 && Math.Abs(actual.g-expected.g)<20 && Math.Abs(actual.b-expected.b)<20,
            $"Desktop marker mismatch at {x},{y}: expected {expected}, got {actual}");
    }
    void SaveTopDown(Texture2D image,string name)
    {
        var source=image.GetPixels32(); var result=new Color32[source.Length];
        for(int y=0;y<image.height;y++) Array.Copy(source,y*image.width,result,(image.height-1-y)*image.width,image.width);
        image.SetPixels32(result); image.Apply(); File.WriteAllBytes(Path.Combine(folder,name),image.EncodeToPNG());
    }
    static void Require(bool condition,string message) { if (!condition) throw new Exception(message); }
    void OnDestroy() { if (testing) { NC_TestEnd(); testing=false; } }
}
