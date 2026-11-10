using System;
using System.Collections;
using System.Runtime.InteropServices;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering;

[RequireComponent(typeof(CafeDesktopInput))]
public sealed class CafeDesktopCapture : MonoBehaviour
{
    public Material desktopMaterial;
    public event Action Changed;
    // Capture reports failure; it does not own the room's input mode.
    public event Action CaptureFailed;
    public string MonitorLabel => monitorNames.Length == 0 ? "未找到显示器" :
        monitorIndex >= 0 && monitorIndex < monitorNames.Length ? monitorNames[monitorIndex] : "显示器编号无效，请刷新列表";
    public void SelectNextMonitor()
    {
        if (Active || monitorNames.Length == 0) return;
        monitorIndex = monitorIndex < 0 || monitorIndex >= monitorNames.Length - 1 ? 0 : monitorIndex + 1;
        Changed?.Invoke();
    }
    [StructLayout(LayoutKind.Sequential)]
    public struct CaptureInfo
    {
        public int state, width, height, generation, error, excluded;
        public ulong frames;
        public double ageMs, submitMs, sourceAgeMs;
    }
    const string Dll = "NetCafeCapture";
    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern int NC_MonitorCount();
    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
    static extern int NC_MonitorInfo(int index, StringBuilder name, int capacity, out int width, out int height);
    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] static extern int NC_Start(int index);
    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] static extern void NC_Stop();
    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] static extern void NC_GetInfo(out CaptureInfo info);
    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)] static extern void NC_GetError(StringBuilder text, int capacity);
    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] static extern int NC_SetTexture(IntPtr texture);
    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] static extern IntPtr NC_RenderEvent();
    public CaptureInfo Info { get; private set; }
    public Texture2D DesktopTexture { get; private set; }
    public Transform DisplaySurface => surface != null && surface.activeSelf ? surface.transform : null;
    public bool Active { get; private set; }
    // A native frame count alone does not mean the GPU texture/scene surface is usable.
    internal bool FrameReady => Active && Info.frames > 0 && generation == Info.generation &&
        DesktopTexture != null && surface != null;
    public bool Ready => FrameReady && surface.activeSelf;
    CafeDesktopInput input;
    public bool ShowBorder => input == null || input.RoomView;
    public int MonitorIndex => monitorIndex;
    public float MonitorAspect => Info.width>0&&Info.height>0 ? (float)Info.width/Info.height :
        monitorIndex>=0&&monitorIndex<monitorSizes.Length&&monitorSizes[monitorIndex].y>0 ?
        (float)monitorSizes[monitorIndex].x/monitorSizes[monitorIndex].y : (float)Screen.currentResolution.width/Mathf.Max(1,Screen.currentResolution.height);
    public Rect DesktopRect
    {
        get
        {
            if (ShowBorder && cafe.DesktopFocused)
            {
                Transform screen = DisplaySurface != null ? DisplaySurface : cafe.screens[cafe.CurrentSeat].transform;
                float left=float.PositiveInfinity, right=float.NegativeInfinity;
                float bottom=float.PositiveInfinity, top=float.NegativeInfinity;
                // Compute all four corners: inclined screens do not project to two diagonal corners.
                for(int x=-1;x<=1;x+=2) for(int y=-1;y<=1;y+=2)
                {
                    Vector3 p=cafe.eyes.WorldToScreenPoint(screen.TransformPoint(new Vector3(x*.5f,y*.5f,0)));
                    left=Mathf.Min(left,p.x);right=Mathf.Max(right,p.x);
                    bottom=Mathf.Min(bottom,p.y);top=Mathf.Max(top,p.y);
                }
                return Rect.MinMaxRect(left,Screen.height-top,right,Screen.height-bottom);
            }
            float aspect = Info.height > 0 ? (float)Info.width / Info.height : (float)Screen.width / Screen.height;
            float width = Mathf.Min(Screen.width, Screen.height * aspect), height = width / aspect;
            return new Rect((Screen.width - width) / 2, (Screen.height - height) / 2, width, height);
        }
    }
    public string Error { get; private set; } = "";
    public string Status => !string.IsNullOrEmpty(Error) ? Error : !available ? "当前设备不支持本地桌面捕获。" :
        !Active ? "未开启 · 仅本地预览" :
        !FrameReady ? "正在等待桌面画面及贴图…" : Info.ageMs > 2000 ? "桌面暂未更新，请检查锁屏、显示器连接或重新开始" :
        $"游戏 {renderFps:F0} / 桌面更新 {captureFps:F0} FPS · 仅本地\n{Info.width} × {Info.height} · 跟随显示器 {Screen.currentResolution.refreshRateRatio.value:F0} Hz";
    CafePrototype cafe;
    bool available;
    IntPtr renderEvent;
    string[] monitorNames = Array.Empty<string>();
    Vector2Int[] monitorSizes=Array.Empty<Vector2Int>();
    int monitorIndex, generation = -1, ownerSeat;
    Material material;
    Mesh mesh;
    GameObject surface;
    Transform fittedScreen, fittedFrame;
    Vector3 originalScreenSize, originalFrameSize;
    double fpsAt;
    ulong fpsFrames;
    float captureFps, renderFps;
    int fpsRenderFrames;
    bool waitingForFirstFrame;
    float firstFrameDeadline;
    const float FirstFrameTimeoutSeconds = 8f;
    void Awake()
    {
        cafe = GetComponent<CafePrototype>();
        input = GetComponent<CafeDesktopInput>();
        available = Application.platform == RuntimePlatform.WindowsPlayer && SystemInfo.graphicsDeviceType == GraphicsDeviceType.Direct3D11;
        if (!available) { Error = "真实桌面请使用 Windows 独立运行包（Direct3D 11）。"; return; }
        try { renderEvent = NC_RenderEvent(); RefreshMonitors(); }
        catch (Exception e) { available = false; Error = "桌面模块无法加载：" + e.Message; }
    }

    public void RefreshMonitors()
    {
        if (!available) return;
        int count = NC_MonitorCount();
        monitorNames = new string[count];
        monitorSizes=new Vector2Int[count];
        for (int i = 0; i < count; i++)
        {
            var name = new StringBuilder(80);
            monitorNames[i] = NC_MonitorInfo(i, name, name.Capacity, out int w, out int h) != 0 ?
                $"显示器 {i + 1} / {w} × {h}" : $"显示器 {i + 1} 已断开";
            monitorSizes[i]=new Vector2Int(w,h);
        }
        monitorIndex = Mathf.Clamp(monitorIndex, 0, Mathf.Max(0, count - 1)); Changed?.Invoke();
    }

    public bool BeginCapture(int index)
    {
        if (!available) return false;
        input?.EndControl();
        // An invalid requested monitor must not corrupt the UI's selected monitor.
        if (index >= 0 && index < monitorNames.Length) monitorIndex = index;
        ReleaseTexture();
        generation = -1; Error = "";
        Active = NC_Start(index) != 0;
        NC_GetInfo(out var info); Info = info;
        if (!Active) { ReadError(); GL.IssuePluginEvent(renderEvent, 1); } // Drain queued native failure cleanup.
        waitingForFirstFrame = Active;
        firstFrameDeadline = Time.unscaledTime + FirstFrameTimeoutSeconds;
        fpsAt = Time.realtimeSinceStartupAsDouble; fpsFrames = 0; captureFps = renderFps = 0; fpsRenderFrames = Time.frameCount;
        Changed?.Invoke(); return Active;
    }

    public void StopCapture()
    {
        input?.EndControl();
        if (available) { NC_Stop(); GL.IssuePluginEvent(renderEvent, 1); }
        Active = false; waitingForFirstFrame = false; ReleaseTexture();
        Info = default; captureFps = 0; Error = ""; Changed?.Invoke();
    }

    void ReleaseTexture()
    {
        if (available) NC_SetTexture(IntPtr.Zero);
        if (surface != null) surface.SetActive(false);
        if (material != null) material.mainTexture = null;
        if (DesktopTexture != null) Destroy(DesktopTexture);
        DesktopTexture = null;
        RestoreMonitorSize();
    }

    void ReadError()
    {
        var text = new StringBuilder(1024); NC_GetError(text, text.Capacity);
        // Explicit Windows denial is different from a blank GPU material.
        // Never silently switch to another capture API to evade OS policy.
        Error = unchecked((uint)Info.error) == 0x80070005u
            ? "Windows 拒绝访问所选显示器（0x80070005）。请检查「设置 → 隐私和安全性 → 屏幕截图和屏幕录制」；若已允许，请检查系统/显卡驱动和图形捕获兼容性。详细信息：" + text
            : $"桌面捕获失败（0x{Info.error:X8}）：{text}";
        Debug.LogWarning("[NetCafe/Capture] " + Error);
        Changed?.Invoke();
    }

    void Update()
    {
        if (!available) return;
        NC_GetInfo(out var info); Info = info;
        if (Info.state == 3) {
            if (Active) {
                input?.EndControl(); Active = false; waitingForFirstFrame = false;
                ReleaseTexture(); ReadError(); CaptureFailed?.Invoke();
            }
            return;
        }
        if (!Active) return;
        if (Info.width > 0 && Info.height > 0 && generation != Info.generation)
        {
            ReleaseTexture();
            DesktopTexture = new Texture2D(Info.width, Info.height, TextureFormat.BGRA32, false, false) { name = "Local desktop GPU texture", filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            DesktopTexture.Apply(false, true);
            if (NC_SetTexture(DesktopTexture.GetNativeTexturePtr()) == 0) return;
            generation = Info.generation;
            if (material == null) material = new Material(desktopMaterial);
            material.mainTexture = DesktopTexture;
            MakeSurface(); Changed?.Invoke();
        }
        if (FrameReady)
        {
            if (cafe.IsComputerSeat) ownerSeat = cafe.CurrentSeat;
            bool showPreview = cafe.IsComputerSeat || cafe.SeatAvailable(ownerSeat);
            if (showPreview)
            {
                FitMonitor(cafe.screens[ownerSeat].transform, (float)Info.width / Info.height);
                if (surface.transform.parent != cafe.screens[ownerSeat].transform)
                {
                    surface.transform.SetParent(cafe.screens[ownerSeat].transform, false);
                    surface.transform.localPosition = new Vector3(0, 0, -.53f);
                    surface.transform.localScale = Vector3.one;
                }
            }
            if (surface.activeSelf != showPreview) surface.SetActive(showPreview);
        }
        if (waitingForFirstFrame)
        {
            // Wait for the *visible* surface, not only the native frame counter.
            // A failed NC_SetTexture could otherwise leave a blank panel forever.
            if (FrameReady) waitingForFirstFrame = false;
            else if (Time.unscaledTime >= firstFrameDeadline)
            {
                StopCapture();
                Error = "电脑画面或贴图等待超时（8 秒）。请到设置刷新显示器并重新投射。";
                Debug.LogWarning("[NetCafe/Capture] Visible desktop surface timed out; check native error/texture registration.");
                Changed?.Invoke();
                CaptureFailed?.Invoke();
                return;
            }
        }
        double now = Time.realtimeSinceStartupAsDouble;
        if (now - fpsAt >= 1)
        {
            captureFps = (float)((Info.frames >= fpsFrames ? Info.frames - fpsFrames : Info.frames) / (now - fpsAt));
            renderFps = (float)((Time.frameCount - fpsRenderFrames) / (now - fpsAt));
            fpsRenderFrames = Time.frameCount;
            fpsFrames = Info.frames; fpsAt = now; Changed?.Invoke();
        }
    }

    void FitMonitor(Transform screen, float aspect)
    {
        var standard=screen.parent.GetComponent<CafeMonitorFrame>();
        if(standard!=null){standard.Fit(aspect);return;}
        if (fittedScreen != screen)
        {
            RestoreMonitorSize();
            fittedScreen = screen; originalScreenSize = screen.localScale;
            fittedFrame = screen.parent.Find("Monitor frame");
            if (fittedFrame != null) originalFrameSize = fittedFrame.localScale;
        }
        // Match the physical bezel to the desktop so focused viewing has no inner letterbox.
        float width = originalScreenSize.y * aspect;
        Vector3 size = new Vector3(width, originalScreenSize.y, originalScreenSize.z);
        if(fittedScreen.localScale != size) fittedScreen.localScale = size;
        if (fittedFrame != null) {
            Vector3 frameSize = new Vector3(width + .012f, originalScreenSize.y + .012f, originalFrameSize.z);
            if(fittedFrame.localScale != frameSize) fittedFrame.localScale = frameSize;
        }
    }

    void RestoreMonitorSize()
    {
        if (fittedScreen != null) fittedScreen.localScale = originalScreenSize;
        if (fittedFrame != null) fittedFrame.localScale = originalFrameSize;
        fittedScreen = fittedFrame = null;
    }

    void MakeSurface()
    {
        if (surface != null) return;
        mesh = new Mesh { name = "Desktop display quad" };
        mesh.vertices = new[] { new Vector3(-.5f,-.5f,0), new Vector3(.5f,-.5f,0), new Vector3(-.5f,.5f,0), new Vector3(.5f,.5f,0) };
        mesh.uv = new[] { Vector2.zero, Vector2.right, Vector2.up, Vector2.one };
        mesh.triangles = new[] { 0,2,1,2,3,1 }; mesh.RecalculateBounds();
        surface = new GameObject("Local desktop display");
        surface.AddComponent<MeshFilter>().sharedMesh = mesh;
        surface.AddComponent<MeshRenderer>().sharedMaterial = material;
        surface.SetActive(false);
    }

    void LateUpdate()
    {
        // Unity executes the copy in its render command stream, never on Update's thread.
        if (available && Active) GL.IssuePluginEvent(renderEvent, 1);
    }

    public void AutoStart()
    {
        input?.ResetToolbar();
        // Auto capture starts on sitting, not when joining a network room.
        if (Active) return;
        if (!BeginCapture(monitorIndex))
        {
            if (string.IsNullOrEmpty(Error))
                Error = "无法自动投射桌面，请在设置中检查显示器、捕获权限与 Direct3D 11。";
            Debug.LogWarning("[NetCafe/Capture] AutoStart failed: " + Error);
            Changed?.Invoke();
            CaptureFailed?.Invoke();
        }
    }
    public void FitSeat(int index)
    {
        float aspect = MonitorAspect;
        FitMonitor(cafe.screens[index].transform, aspect);
    }

    void OnDisable() { if (available) StopCapture(); }
    void OnDestroy()
    {
        if (surface != null) Destroy(surface);
        if (material != null) Destroy(material);
        if (mesh != null) Destroy(mesh);
    }
}
