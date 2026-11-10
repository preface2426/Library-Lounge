using System;
using System.Runtime.InteropServices;
using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;

// Input and toolbar only. Capture and room transitions are coordinated by CafePrototype.
public sealed class CafeDesktopInput : MonoBehaviour
{
    const string Dll = "NetCafeCapture";
    [DllImport(Dll)] static extern int NC_SafetyStart();
    [DllImport(Dll)] static extern int NC_SafetyKey();
    [DllImport(Dll)] static extern void NC_ControlShutdown();
    [DllImport(Dll)] static extern int NC_ControlBegin(int monitor, int unused);
    [DllImport(Dll)] static extern void NC_ControlEnd();
    [DllImport(Dll)] static extern int NC_ControlPoll();
    [DllImport(Dll)] static extern void NC_ControlView(int x, int y, int width, int height);
    [DllImport(Dll)] static extern void NC_ControlToolbar(int x, int y, int width, int height, int collapsed);
    [DllImport(Dll)] static extern void NC_ToolbarPointer(out int x, out int y, out int held);
    [DllImport(Dll)] static extern int NC_ControlHotkey(int key, int modifiers);
    [DllImport(Dll)] static extern int NC_ControlError();
    [DllImport(Dll, CharSet = CharSet.Unicode)] static extern void NC_ControlDiagnostic(StringBuilder text, int capacity);
    [DllImport(Dll)] public static extern int NC_TestPointer(int x, int y, int flags, int data);
    public bool Controlling { get; private set; }
    public bool RoomView { get; private set; }
    public string Error { get; private set; } = "";
    public int EscapeKey { get; private set; } = 0x77;
    public int EscapeModifiers { get; private set; }
    public string EscapeLabel => ((EscapeModifiers & 2) != 0 ? "Ctrl+" : "") + ((EscapeModifiers & 1) != 0 ? "Alt+" : "") + ((EscapeModifiers & 4) != 0 ? "Shift+" : "") + "F" + (EscapeKey - 0x6f);
    public string EmergencyLabel { get; private set; } = "Ctrl+Shift+F12";
    public Vector2 Pointer { get; private set; }
    public bool OverToolbar => cafe.IsComputerSeat && cafe.Mode != CafePrototype.InputMode.Menu && VisibleToolbar.Contains(Pointer);
    // Menus use the real OS cursor so the visible pointer matches Unity UI raycasts.
    // While native control is active the real Windows app owns focus, but
    // the software pointer must remain visible inside the capture. In
    // observation mode after control has ended, use an OS cursor until Unity
    // has actually regained focus; otherwise both pointers are drawn.
    public bool DrawsPointer => cursor != null && !cafe.LookingAround &&
        cafe.Mode != CafePrototype.InputMode.Menu && cafe.CurrentSeat >= 0 &&
        (Controlling || Application.isFocused);
    public Rect ToolbarRect => VisibleToolbar;
    bool available;
    CafePrototype cafe;
    Rect viewRect, toolbar;
    bool toolbarCollapsed, toolbarDragging;
    float readyToAcquireAt;
    Vector2 toolbarDragOffset;
    double toolbarTouched;
    int dockEdge;
    Texture2D cursor;
    public enum RequestedAction { Menu, Observe, Stand }
    // Input reports an intent; the room coordinator alone performs mode transitions.
    public event Action<RequestedAction> ActionRequested;
    public event Action Changed;
    public event Action<Vector2> PointerChanged;
    public Texture2D CursorTexture => cursor;
    public bool ToolbarCollapsed => toolbarCollapsed;
    Vector2Int toolbarScreen;
    float toolbarScale = 1;
    bool toolbarPositionLoaded;
    const string ToolbarPosX = "DesktopToolbarPosX";
    const string ToolbarPosY = "DesktopToolbarPosY";
    public void SetToolbarScale(float value) { toolbarScale = value; toolbarScreen = Vector2Int.zero; }

    Rect VisibleToolbar
    {
        get
        {
            if (!toolbarCollapsed) return toolbar;
            // One geometry owns rendering, hover and native hit-testing on all four edges.
            float h=Mathf.Min(Screen.height,Mathf.Max(36,toolbar.height*.9f));
            float w=Mathf.Min(Screen.width,Mathf.Max(112,toolbar.height*3.2f));
            float x=dockEdge==0?0:dockEdge==1?Screen.width-w:Mathf.Clamp(toolbar.center.x-w*.5f,0,Screen.width-w);
            float y=dockEdge==2?0:dockEdge==3?Screen.height-h:Mathf.Clamp(toolbar.center.y-h*.5f,0,Screen.height-h);
            return new Rect(x,y,w,h);
        }
    }


    void Awake()
    {
        cafe = GetComponent<CafePrototype>();
        RoomView = PlayerPrefs.GetInt("DesktopRoomView", 1) != 0;
        available = Application.platform == RuntimePlatform.WindowsPlayer;
        if (!available) return;
        int key = PlayerPrefs.GetInt("DesktopEscapeKey", 0x77), mods = PlayerPrefs.GetInt("DesktopEscapeModifiers", 0);
        try
        {
            if (NC_SafetyStart() == 0)
            {
                available = false;
                Error = "紧急退出快捷键 Ctrl+Shift+F12/F11/F10/F9 均被占用。为避免鼠标被锁定，已禁用桌面接管（" + NC_ControlError() + "）。";
                return;
            }
            int emergencyVk = NC_SafetyKey();
            EmergencyLabel = "Ctrl+Shift+F" + (emergencyVk - 0x6f);
            if (!TrySetHotkey(key, mods, false)) TrySetHotkey(0x77, 0, false);
        }
        catch(Exception e)
        {
            // If hotkey setup fails after the safety service was armed, do not
            // leave a process-wide Ctrl+Shift+F12 registration behind.
            try { NC_ControlShutdown(); } catch { }
            available = false;
            Error = "桌面输入保护模块无法加载：" + e.Message;
            return;
        }
        const string shape = "1000000000000000/1100000000000000/1210000000000000/1221000000000000/1222100000000000/1222210000000000/1222221000000000/1222222100000000/1222222210000000/1222222221000000/1222222222100000/1222222111110000/1221222100000000/1210122100000000/1100012210000000/1000012210000000/0000001221000000/0000001221000000/0000000110000000";
        var rows = shape.Split('/'); cursor = new Texture2D(16, rows.Length, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point };
        for (int y = 0; y < rows.Length; y++) for (int x = 0; x < 16; x++) cursor.SetPixel(x, rows.Length - 1 - y, rows[y][x] == '0' ? Color.clear : rows[y][x] == '1' ? Color.black : Color.white);
        cursor.Apply(false, true);
    }
    public bool TrySetHotkey(int key, int mods, bool save = true)
    {
        if (!available) return false;
        if (NC_ControlHotkey(key, mods) == 0) { Error = $"脱离键不可用或已被占用（{NC_ControlError()}），保留原设置。"; Changed?.Invoke(); return false; }
        EscapeKey = key; EscapeModifiers = mods; Error = ""; toolbarScreen = Vector2Int.zero;
        if (save) { PlayerPrefs.SetInt("DesktopEscapeKey", key); PlayerPrefs.SetInt("DesktopEscapeModifiers", mods); PlayerPrefs.Save(); }
        Changed?.Invoke(); return true;
    }
    public string Diagnostic() { var text = new StringBuilder(800); if (available) NC_ControlDiagnostic(text, text.Capacity); return text.ToString(); }
    void Update()
    {
        if (available)
        {
            int events = NC_ControlPoll();
            if ((events & 512) != 0) Debug.Log("[NetCafe/Input] Restored native raw mouse receiver. " + Diagnostic());
            if ((events & (1 | 8 | 64 | 128 | 256)) != 0) Controlling = false;
            if ((events & 8) != 0) { Error = $"系统输入不可用（{NC_ControlError()}），已释放控制。"; Debug.LogWarning("[NetCafe/Input] " + Error + " " + Diagnostic()); ActionRequested?.Invoke(RequestedAction.Menu); }
            else if ((events & 128) != 0) ActionRequested?.Invoke(RequestedAction.Stand);
            else if ((events & 64) != 0) ActionRequested?.Invoke(RequestedAction.Observe);
            else if ((events & (1 | 256)) != 0) ActionRequested?.Invoke(RequestedAction.Menu);
            if ((events & 2) != 0) ToggleView();
            if ((events & 32) != 0) ResetToolbar();
        }
        if (toolbarScreen.x != Screen.width || toolbarScreen.y != Screen.height)
        {
            float scale = Mathf.Clamp(Screen.height / 900f, .7f, 1.6f) * toolbarScale;
            float oldMaxX=Mathf.Max(1,toolbarScreen.x-toolbar.width);
            float oldMaxY=Mathf.Max(1,toolbarScreen.y-toolbar.height);
            float oldX=toolbar.x/oldMaxX,oldY=toolbar.y/oldMaxY;
            float cellWidth = Mathf.Max(112, 40 + EscapeLabel.Length * 9);
            toolbar.size = new Vector2(Mathf.Min(5 * cellWidth * scale, Screen.width - 24), 44 * scale);
            if (!toolbarPositionLoaded)
            {
                // First entry: centered at the TOP. A user drag overrides that default.
                bool stored=PlayerPrefs.HasKey(ToolbarPosX) && PlayerPrefs.HasKey(ToolbarPosY);
                oldX=stored?PlayerPrefs.GetFloat(ToolbarPosX):.5f;
                oldY=stored?PlayerPrefs.GetFloat(ToolbarPosY):0f;
                toolbarPositionLoaded=true;
            }
            toolbar.position=new Vector2(
                Mathf.Clamp01(oldX)*Mathf.Max(0,Screen.width-toolbar.width),
                Mathf.Clamp(toolbarScreen.x==0 && !PlayerPrefs.HasKey(ToolbarPosY) ? 12f :
                    Mathf.Clamp01(oldY)*Mathf.Max(0,Screen.height-toolbar.height),
                    0,Mathf.Max(0,Screen.height-toolbar.height)));
            toolbarScreen = new Vector2Int(Screen.width, Screen.height);
            toolbarTouched = Time.realtimeSinceStartupAsDouble;
            Changed?.Invoke();
        }
        Vector2 beforePointer = Pointer;
        int held = 0;
        if (available) { NC_ToolbarPointer(out int x, out int y, out held); Pointer = new Vector2(x, y); }
        else if (Mouse.current != null) { Vector2 p = Mouse.current.position.ReadValue(); Pointer = new Vector2(p.x, Screen.height - p.y); }
        if (Pointer != beforePointer) PointerChanged?.Invoke(Pointer);
        if (!cafe.IsComputerSeat || cafe.Mode == CafePrototype.InputMode.Menu) return;
        if (!Controlling && Mouse.current != null) held = Mouse.current.leftButton.isPressed && (toolbarDragging || (!toolbarCollapsed && new Rect(toolbar.x, toolbar.y, toolbar.width / 5, toolbar.height).Contains(Pointer))) ? 1 : 0;
        Rect before = VisibleToolbar; bool wasCollapsed = toolbarCollapsed;
        TickToolbar(Pointer, held != 0, Time.realtimeSinceStartupAsDouble);
        if (before != VisibleToolbar || wasCollapsed != toolbarCollapsed) Changed?.Invoke();
        ConfigureControlView();
    }
    public void UpdateControl(bool wanted, int monitorIndex, Rect rect)
    {
        viewRect = rect;
        if (!wanted) { EndControl(); return; }
        if (!available || Controlling) return;
        // Never turn an unfocused Unity HWND into a transparent topmost overlay.
        // Wait until the menu's click has been released and the UI transition
        // has finished; otherwise the first right-click may reach two windows.
        if (!Application.isFocused || Time.unscaledTime < readyToAcquireAt ||
            Mouse.current != null && (Mouse.current.leftButton.isPressed || Mouse.current.rightButton.isPressed))
            return;
        if (Error.Length > 0) { ActionRequested?.Invoke(RequestedAction.Menu); return; }
        // Unity must unlock before native input takes ownership of mouse registration.
        Cursor.lockState = CursorLockMode.None; Cursor.visible = false;
        ConfigureControlView();
        if (NC_ControlBegin(monitorIndex, 0) == 0)
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            Error = $"无法进入桌面控制，检查脱离键或系统权限（{NC_ControlError()}）。";
            ActionRequested?.Invoke(RequestedAction.Menu);
            return;
        }
        Controlling = true; ConfigureControlView(); Changed?.Invoke();
    }
    public void EndControl()
    {
        if (!Controlling) return;
        NC_ControlEnd(); Controlling = false; Changed?.Invoke();
    }
    public void DeferControlAcquisition()
    {
        readyToAcquireAt = Time.unscaledTime + .08f;
        if (Controlling) EndControl();
    }
    public void Retry() { if(available) Error = ""; }
    public void PrepareComputerSeat()
    {
        // A new seat always starts in spatial view. Fullscreen remains an
        // explicit choice while using this computer, not a seating default.
        RoomView = true;
        Retry();
        DeferControlAcquisition();
        Changed?.Invoke();
    }
    // Immediately reserve an unobstructed 90% monitor when seating/switching
    // to spatial view. The narrow top-edge tab remains available on hover;
    // explicit ResetToolbar expands the command bar when requested.
    public void DockRoomViewToolbar()
    {
        if (!RoomView) return;
        toolbarDragging = false;
        toolbarCollapsed = true;
        dockEdge = 2;
        toolbarTouched = Time.realtimeSinceStartupAsDouble;
        Changed?.Invoke();
    }
    public void ResetToolbar() { toolbarCollapsed = false; toolbarTouched = Time.realtimeSinceStartupAsDouble; Changed?.Invoke(); }
    public void ToggleView()
    {
        RoomView = !RoomView;
        PlayerPrefs.SetInt("DesktopRoomView", RoomView ? 1 : 0); PlayerPrefs.Save();
        // Switching modes must not snap a dragged toolbar back to the bottom.
        toolbarDragging = false;
        ResetToolbar();
        if (RoomView) DockRoomViewToolbar();
    }
    void ConfigureControlView()
    {
        if (!available) return;
        NC_ControlView(Mathf.RoundToInt(viewRect.x), Mathf.RoundToInt(viewRect.y), Mathf.RoundToInt(viewRect.width), Mathf.RoundToInt(viewRect.height));
        Rect r = VisibleToolbar; NC_ControlToolbar(Mathf.RoundToInt(r.x), Mathf.RoundToInt(r.y), Mathf.RoundToInt(r.width), Mathf.RoundToInt(r.height), toolbarCollapsed ? 1 : 0);
    }
    void TickToolbar(Vector2 pointer, bool held, double now)
    {
        // Dragging starts only on the first (handle) cell, never on desktop clicks.
        bool onHandle = !toolbarCollapsed && new Rect(toolbar.x,toolbar.y,toolbar.width / 5f,toolbar.height).Contains(pointer);
        if (held && !toolbarDragging && onHandle)
        { toolbarCollapsed = false; toolbarDragging = true; toolbarDragOffset = pointer - toolbar.position; }
        if (toolbarDragging)
        {
            toolbar.position = pointer - toolbarDragOffset;
            toolbar.x = Mathf.Clamp(toolbar.x, 0, Mathf.Max(0, Screen.width - toolbar.width));
            toolbar.y = Mathf.Clamp(toolbar.y, 0, Mathf.Max(0, Screen.height - toolbar.height));
            toolbarTouched = now;
            if (!held)
            {
                toolbarDragging = false;
                // Store normalized coordinates so the toolbar stays where dragged
                // when switching 90% / fullscreen and on the next launch.
                PlayerPrefs.SetFloat(ToolbarPosX,toolbar.x/Mathf.Max(1,Screen.width-toolbar.width));
                PlayerPrefs.SetFloat(ToolbarPosY,toolbar.y/Mathf.Max(1,Screen.height-toolbar.height));
                PlayerPrefs.Save();
                Debug.Log("TOP_TOOLBAR_DRAG_SAVED position="+toolbar.position);
            }
        }
        if (VisibleToolbar.Contains(pointer)) { toolbarCollapsed = false; toolbarTouched = now; }
        // A 90% spatial monitor leaves only 5% at the viewport edges. The
        // expanded toolbar can cover Windows' taskbar or the monitor bezel.
        // Auto-dock to a slim edge tab after idle in BOTH display modes;
        // hovering the tab expands it and preserves the user's drag position.
        if (!toolbarDragging && !toolbarCollapsed && now - toolbarTouched > 2)
        {
            float nearest = toolbar.center.x; dockEdge = 0;
            float right = Screen.width - toolbar.center.x, bottom = Screen.height - toolbar.center.y;
            if(right < nearest) { nearest = right; dockEdge = 1; }
            if(toolbar.center.y < nearest) { nearest = toolbar.center.y; dockEdge = 2; }
            if(bottom < nearest) dockEdge = 3;
            if (dockEdge == 0) toolbar.x = 0; else if (dockEdge == 1) toolbar.x = Screen.width - toolbar.width;
            else if (dockEdge == 2) toolbar.y = 0; else toolbar.y = Screen.height - toolbar.height;
            toolbarCollapsed = true;
        }
    }

    public void CheckRoomToolbarDrag()
    {
        Rect initial=toolbar;
        bool initialCollapsed=toolbarCollapsed;
        bool initialDragging=toolbarDragging;
        bool hadX=PlayerPrefs.HasKey(ToolbarPosX),hadY=PlayerPrefs.HasKey(ToolbarPosY);
        float storedX=PlayerPrefs.GetFloat(ToolbarPosX),storedY=PlayerPrefs.GetFloat(ToolbarPosY);
        try
        {
            if(!RoomView)throw new Exception("Drag test requires room view");
            if(toolbar.y>Mathf.Max(25,Screen.height*.08f) && !hadY)
                throw new Exception("Default draggable toolbar not at top");
            toolbarCollapsed=false;
            Vector2 handle=toolbar.position+new Vector2(10,toolbar.height*.5f);
            TickToolbar(handle,true,1);
            if(!toolbarDragging)throw new Exception("Toolbar handle did not start drag");
            Vector2 target=handle+new Vector2(-Mathf.Min(Screen.width*.12f,150),Mathf.Min(Screen.height*.20f,250));
            TickToolbar(target,true,1.1);
            TickToolbar(target,false,1.2);
            if(toolbarDragging || Vector2.Distance(initial.position,toolbar.position)<15f)
                throw new Exception("Toolbar handle drag was snapped/locked");
            Vector2 dragged=toolbar.position;
            ToggleView();ToggleView();
            if(Vector2.Distance(dragged,toolbar.position)>.5f)
                throw new Exception("Toolbar position changed after fullscreen toggle");
            Debug.Log("ROOMVIEW_TOOLBAR_DRAG_PASS initial="+initial.position+" dragged="+dragged+
                " persisted=true togglePreserved=true");
        }
        finally
        {
            if(hadX)PlayerPrefs.SetFloat(ToolbarPosX,storedX);else PlayerPrefs.DeleteKey(ToolbarPosX);
            if(hadY)PlayerPrefs.SetFloat(ToolbarPosY,storedY);else PlayerPrefs.DeleteKey(ToolbarPosY);
            PlayerPrefs.Save();
            toolbar=initial;toolbarCollapsed=initialCollapsed;toolbarDragging=initialDragging;
            toolbarTouched=Time.realtimeSinceStartupAsDouble;
            ConfigureControlView();Changed?.Invoke();
        }
    }

    public void CheckToolbar()
    {
        Rect saved = toolbar; bool collapsed = toolbarCollapsed; int edge = dockEdge;
        bool hadX=PlayerPrefs.HasKey(ToolbarPosX), hadY=PlayerPrefs.HasKey(ToolbarPosY);
        float savedX=PlayerPrefs.GetFloat(ToolbarPosX), savedY=PlayerPrefs.GetFloat(ToolbarPosY);
        try
        {
            toolbarCollapsed = false;
            TickToolbar(toolbar.position + new Vector2(10, 10), true, 10);
            TickToolbar(new Vector2(-100, -100), true, 10.1);
            TickToolbar(new Vector2(-100, -100), false, 10.2);
            if (toolbar.x != 0 || toolbar.y != 0) throw new Exception("Toolbar drag did not clamp to screen");
            TickToolbar(new Vector2(Screen.width / 2, Screen.height / 2), false, 13);
            if (!toolbarCollapsed) throw new Exception("Toolbar did not collapse after idle");
            Rect tab = VisibleToolbar;
            if (tab.xMin > 0 && tab.yMin > 0 && tab.xMax < Screen.width && tab.yMax < Screen.height) throw new Exception("Toolbar tab did not dock to an edge");
            TickToolbar(tab.center, false, 13.1);
            if (toolbarCollapsed) throw new Exception("Hover did not expand toolbar");
        }
        finally
        {
            if(hadX)PlayerPrefs.SetFloat(ToolbarPosX,savedX);else PlayerPrefs.DeleteKey(ToolbarPosX);
            if(hadY)PlayerPrefs.SetFloat(ToolbarPosY,savedY);else PlayerPrefs.DeleteKey(ToolbarPosY);
            PlayerPrefs.Save();
            toolbar=saved;toolbarCollapsed=collapsed;dockEdge=edge;
            toolbarDragging=false;toolbarTouched=Time.realtimeSinceStartupAsDouble;ConfigureControlView();
        }
    }

    void OnDisable() { EndControl(); Cursor.lockState = CursorLockMode.None; Cursor.visible = true; }
    void OnApplicationQuit()
    {
        // Release native input and the independent emergency hotkey even when
        // the game quits from a menu/network callback instead of leaving a seat.
        if (available) NC_ControlShutdown();
        Controlling = false;
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }
    void OnDestroy()
    {
        if (available) NC_ControlShutdown();
        Controlling = false;
        if (cursor != null) Destroy(cursor);
    }
}
