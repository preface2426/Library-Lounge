using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using UnityEngine;
using Debug = UnityEngine.Debug;

// Opt-in cross-process integration checks; no test windows or readback during normal play.
public sealed class CafeA2SmokeTest : MonoBehaviour
{
    const string Dll = "NetCafeCapture";
    [StructLayout(LayoutKind.Sequential)] struct Point { public int x,y; public Point(int x,int y) { this.x=x;this.y=y; } }
    [StructLayout(LayoutKind.Sequential)] struct Bounds { public int left,top,right,bottom; }
    [StructLayout(LayoutKind.Sequential)] struct RawDevice { public ushort page,usage; public uint flags; public IntPtr target; }
    [StructLayout(LayoutKind.Sequential)] struct CursorInfo { public int size,flags; public IntPtr cursor; public Point point; }
    delegate bool WindowCallback(IntPtr window, IntPtr data);
    [DllImport("user32.dll")] static extern void mouse_event(uint flags,uint x,uint y,uint data,UIntPtr extra);
    [DllImport("user32.dll")] static extern void keybd_event(byte key,byte scan,uint flags,UIntPtr extra);
    [DllImport("user32.dll")] static extern uint MapVirtualKey(uint key,uint type);
    static void UiKey(byte key,bool up=false) => keybd_event(key,(byte)MapVirtualKey(key,0),up?2u:0,UIntPtr.Zero);
    [DllImport("user32.dll")] static extern bool EnumWindows(WindowCallback callback,IntPtr data);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern int GetWindowText(IntPtr window,StringBuilder text,int count);
    [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr window);
    [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr window,int mode);
    [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr window,IntPtr after,int x,int y,int width,int height,uint flags);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32.dll")] static extern bool ClientToScreen(IntPtr window,ref Point point);
    [DllImport("user32.dll")] static extern bool GetClientRect(IntPtr window,out Bounds rect);
    [DllImport("user32.dll")] static extern IntPtr GetDlgItem(IntPtr window,int id);
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] static extern uint GetRegisteredRawInputDevices([Out] RawDevice[] devices,ref uint count,uint size);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern int GetClassName(IntPtr window,StringBuilder name,int count);
    [DllImport("user32.dll")] static extern bool GetCursorInfo(ref CursorInfo info);
    [DllImport("user32.dll")] static extern bool SetCursorPos(int x,int y);
    [DllImport("user32.dll")] static extern bool GetClipCursor(out Bounds rect);
    [DllImport("user32.dll")] static extern IntPtr WindowFromPoint(Point point);
    [DllImport("user32.dll")] static extern IntPtr GetAncestor(IntPtr window,uint flags);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr window,out uint pid);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern IntPtr SendMessageTimeout(IntPtr window,uint message,IntPtr a,IntPtr b,uint flags,uint timeout,out IntPtr result);
    [DllImport("user32.dll")] static extern bool PostMessage(IntPtr window,uint message,IntPtr a,IntPtr b);
    [DllImport("user32.dll")] static extern bool RegisterHotKey(IntPtr window,int id,uint modifiers,uint key);
    [DllImport("user32.dll")] static extern bool UnregisterHotKey(IntPtr window,int id);
    [DllImport(Dll)] static extern int NC_MonitorRect(int index,out Bounds rect);
    [DllImport(Dll)] static extern int NC_TestPointer(int x,int y,int flags,int data);
    [DllImport(Dll)] static extern int NC_GetMemory(out ulong privateBytes,out ulong workingBytes);
    CafePrototype cafe; CafeDesktopCapture capture; CafeDesktopInput input;
    Process fixtureProcess,browserProcess; IntPtr fixture,browser; Bounds monitor;
    string browserPrefix,browserProcessName;
    List<double> latency = new();
    int oldKey,oldMods; bool oldView;

    IEnumerator Start()
    {
        if(Array.IndexOf(Environment.GetCommandLineArgs(),"-memorySmoke")>=0) { yield return MemoryTest();yield break; }
        if(Array.IndexOf(Environment.GetCommandLineArgs(),"-exitSmoke")>=0) { yield return ExitTest();yield break; }
        bool focusSmoke=Array.IndexOf(Environment.GetCommandLineArgs(),"-focusSmoke")>=0;
        bool pointerSmoke=Array.IndexOf(Environment.GetCommandLineArgs(),"-pointerSmoke")>=0;
        bool fpsSmoke=Array.IndexOf(Environment.GetCommandLineArgs(),"-fpsSmoke")>=0;
        bool uiSmoke=Array.IndexOf(Environment.GetCommandLineArgs(),"-uiSmoke")>=0;
        bool local05Smoke=Array.IndexOf(Environment.GetCommandLineArgs(),"-local05GuiSmoke")>=0;
        if(!focusSmoke&&!pointerSmoke&&!fpsSmoke&&!uiSmoke&&!local05Smoke&&Array.IndexOf(Environment.GetCommandLineArgs(),"-a2Smoke")<0)yield break;
        cafe=GetComponent<CafePrototype>();capture=GetComponent<CafeDesktopCapture>();input=GetComponent<CafeDesktopInput>();
        oldKey=input.EscapeKey;oldMods=input.EscapeModifiers;oldView=input.RoomView;
        var stack=new Stack<IEnumerator>();stack.Push(local05Smoke?Local05GuiTest():uiSmoke?UiTest():fpsSmoke?FrameRateTest():pointerSmoke?PointerTest():focusSmoke?FocusTest():Run());Exception failure=null;
        while(stack.Count>0){var test=stack.Peek();bool more=false;try{more=test.MoveNext();}catch(Exception e){failure=e;}
            if(failure!=null)break;if(!more){(stack.Pop() as IDisposable)?.Dispose();continue;}
            if(test.Current is IEnumerator child){stack.Push(child);continue;}yield return test.Current;}
        while(stack.Count>0)(stack.Pop() as IDisposable)?.Dispose();
        input.EndControl();cafe.OpenMenu();input.TrySetHotkey(oldKey,oldMods,false);
        if(input.RoomView!=oldView)input.ToggleView();
        capture.StopCapture();
        if(fixture!=IntPtr.Zero)PostMessage(fixture,0x10,IntPtr.Zero,IntPtr.Zero);
        CloseBrowser();
        fixtureProcess?.Dispose();fixtureProcess=null;
        if(failure!=null)Debug.LogException(failure);
        else Debug.Log(local05Smoke?"LOCAL05_SMOKE_PASS: owned Windows clicks, two real screenshot buttons, F8 escape and real Continue click":uiSmoke?"UI_SMOKE_PASS: menu stack, focus, raycasts, text scaling, glyphs, layout and screenshot saving":fpsSmoke?"FPS_SMOKE_PASS: display-paced rendering and fresh desktop updates in desktop, full-screen, observation, menu and walking":pointerSmoke?"POINTER_SMOKE_PASS: desktop movement and resume; menu and observation cursor; toolbar stand and room camera; normal focus handling":focusSmoke?"FOCUS_SMOKE_PASS: normal focus handling; desktop handoff; external click; menu continue; escape cleanup":"A2_SMOKE_PASS: external native app and browser; captured pixel updates; 90%/full screen mapped clicks; toolbar isolation; observe/return; stand; menu; configurable escape collision");
        Application.Quit(failure==null?0:1);
    }
    IEnumerator UiTest()
    {
        while(!UnityEngine.Rendering.SplashScreen.isFinished)yield return null;
        yield return new WaitForSecondsRealtime(1);
        var hud=GetComponent<CafeHud>();float oldScale=hud.TextScale;int oldKey=input.EscapeKey,oldMods=input.EscapeModifiers;
        bool keysDispatched = false;
        string folder=Path.GetFullPath(Path.Combine(Application.dataPath,"..","Evidence","UI"));Directory.CreateDirectory(folder);
        cafe.OpenMenu();yield return null;
        Require(FindObjectsByType<UnityEngine.EventSystems.EventSystem>().Length==1,"Expected one EventSystem");
        Require(hud.canvas.GetComponent<UnityEngine.UI.GraphicRaycaster>()!=null&&hud.uiInput.enabled,"UI input or raycaster missing");
        try
        {
            foreach(var size in new[]{new Vector2Int(1280,720),new Vector2Int(1920,1080),new Vector2Int(1920,1200),new Vector2Int(1920,820),new Vector2Int(2560,1080),new Vector2Int(960,1200)})
            {
                Screen.SetResolution(size.x,size.y,FullScreenMode.Windowed);yield return new WaitForSecondsRealtime(.5f);
                foreach(float scale in new[]{1f,2f})
                {
                    hud.SetTextScale(scale);yield return null;yield return null;Canvas.ForceUpdateCanvases();
                    var corners=new Vector3[4];hud.menuPanel.GetWorldCorners(corners);
                    Require(corners[0].x>=0&&corners[0].y>=0&&corners[2].x<=Screen.width+1&&corners[2].y<=Screen.height+1,"Menu outside viewport");
                    var backRect=(RectTransform)hud.Button("BackButton").transform;
                    Require(((RectTransform)backRect.parent).rect.height<=130&&backRect.rect.width>=88&&backRect.rect.height>=44&&backRect.rect.height<=56,$"Header action has an invalid hit target; header={((RectTransform)backRect.parent).rect.size}; back={backRect.rect.size}");
                    Require(!hud.menuRail.gameObject.activeSelf &&
                         hud.menuContent.rect.width > 0 && hud.menuContent.rect.height > 0,
                         "UI V2 menu content collapsed or decorative rail still intercepts clicks");
                    hud.Button("SettingsButton").onClick.Invoke();yield return null;
                    Require(hud.Page==1&&hud.eventSystem.currentSelectedGameObject!=null,"Settings focus missing");
                    foreach(var control in hud.pages[1].GetComponentsInChildren<UnityEngine.UI.Button>())if(control.gameObject.activeInHierarchy){var nav=control.navigation;Require(nav.mode==UnityEngine.UI.Navigation.Mode.Explicit&&nav.selectOnUp!=null&&nav.selectOnDown!=null&&nav.selectOnLeft!=null&&nav.selectOnRight!=null,"Gamepad navigation incomplete: "+control.name);}
                    hud.Button("RebindButton").onClick.Invoke();Require(hud.Rebinding,"Rebind screen missing");hud.Back();Require(hud.Page==1,"Back skipped settings");
                    foreach(var name in new[]{"RebindButton","TextSizeButton","CaptureButton"})
                    {
                        Vector2 point=hud.ButtonPoint(name);
                        var hits=new List<UnityEngine.EventSystems.RaycastResult>();
                        hud.eventSystem.RaycastAll(new UnityEngine.EventSystems.PointerEventData(hud.eventSystem){position=point},hits);
                        Require(hits.Count>0&&hits[0].gameObject==hud.Button(name).gameObject,"Button raycast blocked: "+name);
                    }
                    hud.Back();hud.Button("HelpButton").onClick.Invoke();yield return null;
                    Require(hud.Page==2&&hud.scroll.content.rect.height>hud.scroll.viewport.rect.height,"Help does not scroll");
                    foreach(var text in hud.canvas.GetComponentsInChildren<TMPro.TMP_Text>())
                    {
                        text.ForceMeshUpdate();
                        for(int i=0;i<text.textInfo.characterCount;i++)if(!char.IsWhiteSpace(text.textInfo.characterInfo[i].character))Require(text.textInfo.characterInfo[i].textElement!=null,"Missing text glyph: "+text.name);
                    }
                    hud.Back();
                    Debug.Log($"UI_LAYOUT_PASS: {Screen.width}x{Screen.height}; text={scale*100:F0}%; menu bounds, scroll, raycasts, stack and glyphs");
                }
            }
            Screen.SetResolution(1920,1080,FullScreenMode.Windowed);yield return new WaitForSecondsRealtime(.6f);
            hud.SetTextScale(1);yield return null;yield return null;Canvas.ForceUpdateCanvases();
            var shots=GetComponent<CafeScreenshot>();Require(shots!=null,"Screenshot component missing");
            string shotFolder=Path.Combine(folder,"Screenshots",DateTime.Now.ToString("yyyyMMdd_HHmmss_fff"));
            shots.SetOutputDirectory(shotFolder);
            hud.ButtonPoint("SaveScreenshotButton");
            string selectedBefore=hud.eventSystem.currentSelectedGameObject?.name??"";
            hud.Button("SaveScreenshotButton").onClick.Invoke();
            yield return Wait(()=>!shots.Capturing,"First screenshot timeout");
            Require(shots.LastCaptureSucceeded,"First screenshot failed: "+shots.Status);
            string firstShot=shots.LastSavedPath;VerifySavedScreenshot(firstShot);
            hud.Button("SaveScreenshotButton").onClick.Invoke();
            yield return Wait(()=>!shots.Capturing,"Second screenshot timeout");
            Require(shots.LastCaptureSucceeded,"Second screenshot failed: "+shots.Status);
            string secondShot=shots.LastSavedPath;VerifySavedScreenshot(secondShot);
            Require(firstShot!=secondShot&&File.Exists(firstShot)&&File.Exists(secondShot),"Screenshots overwrote each other");
            string selectedAfter = hud.eventSystem.currentSelectedGameObject?.name ?? "";
            Debug.Log($"LOCAL05_SCREENSHOT_FOCUS: appFocused={Application.isFocused}; selectedBefore={selectedBefore}; selectedAfter={selectedAfter}; window={GetForegroundWindow()}");
            Require(Application.isFocused&&selectedAfter==selectedBefore,"Screenshot changed focus: app="+Application.isFocused+" selected="+selectedBefore+" -> "+selectedAfter);
            string blocked=Path.Combine(folder,"blocked-screenshot-path");File.WriteAllText(blocked,"not a directory");
            try
            {
                shots.SetOutputDirectory(blocked);
                hud.Button("SaveScreenshotButton").onClick.Invoke();
                yield return Wait(()=>!shots.Capturing,"Failed screenshot timeout");
                Require(!shots.LastCaptureSucceeded&&shots.Status.Contains("失败"),"Screenshot failure was not shown");
            }
            finally { File.Delete(blocked);shots.SetOutputDirectory(shotFolder); }
            Debug.Log("SCREENSHOT_SMOKE_PASS: 2 PNGs, dimensions, decode, unique names, focus retention, error feedback");
            ScreenCapture.CaptureScreenshot(Path.Combine(folder,"menu.png"));yield return new WaitForSecondsRealtime(.2f);
            hud.Button("SettingsButton").onClick.Invoke();yield return null;
            ScreenCapture.CaptureScreenshot(Path.Combine(folder,"settings.png"));yield return new WaitForSecondsRealtime(.2f);hud.Back();
            Screen.SetResolution(960,1200,FullScreenMode.Windowed);yield return new WaitForSecondsRealtime(.6f);Canvas.ForceUpdateCanvases();
            ScreenCapture.CaptureScreenshot(Path.Combine(folder,"menu-compact.png"));yield return new WaitForSecondsRealtime(.2f);
            Require(Application.isFocused,"Own menu lost keyboard focus");
            IntPtr ownedGameWindow = FindWindow("NetCafe Playtest", (uint)Process.GetCurrentProcess().Id);
            Require(ownedGameWindow != IntPtr.Zero && IsWindowVisible(ownedGameWindow), "Owned Unity game window missing; refusing OS key injection");
            if (GetForegroundWindow() != ownedGameWindow)
            {
                ShowWindow(ownedGameWindow, 9);
                SetForegroundWindow(ownedGameWindow);
                yield return new WaitForSecondsRealtime(.15f);
            }
            Require(GetForegroundWindow() == ownedGameWindow, "OS focus is not owned by the game; refusing injected Tab/Enter/shortcut keys");
            Debug.Log("UI_KEYBOARD_START: "+hud.eventSystem.currentSelectedGameObject?.name);
            keysDispatched = true;
            UiKey(0x09);yield return new WaitForSecondsRealtime(.08f);UiKey(0x09,true);yield return null;
            Require(hud.eventSystem.currentSelectedGameObject==hud.Button("SettingsButton").gameObject,"Tab did not move to settings: "+hud.eventSystem.currentSelectedGameObject?.name);
            UiKey(0x0d);yield return new WaitForSecondsRealtime(.08f);UiKey(0x0d,true);yield return null;
            Require(hud.Page==1,"Enter did not open settings");
            hud.Button("RebindButton").onClick.Invoke();yield return null;
            UiKey(0x11);UiKey(0x75);yield return new WaitForSecondsRealtime(.08f);
            UiKey(0x75,true);UiKey(0x11,true);yield return null;
            Require(input.EscapeKey==0x75&&input.EscapeModifiers==2&&hud.Page==1,"Ctrl+F6 rebind was not applied");
            UiKey(0x1b);yield return new WaitForSecondsRealtime(.08f);UiKey(0x1b,true);yield return null;
            Require(hud.Page==0&&cafe.Mode==CafePrototype.InputMode.Menu,"Escape did not return one menu level");
            Debug.Log("UI_KEYBOARD_PASS: real Tab, Enter, Ctrl+F6 recording and Escape back");
        }
        finally { if (keysDispatched) foreach(byte key in new byte[]{0x09,0x0d,0x75,0x11,0x1b}) UiKey(key,true); input.TrySetHotkey(oldKey,oldMods);hud.SetTextScale(oldScale); }
    }
    IEnumerator Local05GuiTest()
    {
        while (!UnityEngine.Rendering.SplashScreen.isFinished) yield return null;
        yield return new WaitForSecondsRealtime(1);
        var hud = GetComponent<CafeHud>();
        var shots = GetComponent<CafeScreenshot>();
        Require(shots != null, "Screenshot component missing");
        IntPtr game = FindWindow("NetCafe Playtest", (uint)Process.GetCurrentProcess().Id);
        Require(game != IntPtr.Zero && IsWindowVisible(game), "Owned Unity GUI window missing");
        Screen.SetResolution(1280, 800, FullScreenMode.Windowed);
        yield return new WaitForSecondsRealtime(.65f);
        ShowWindow(game, 9);
        SetWindowPos(game, (IntPtr)(-1), 0, 0, 0, 0, 0x0003);
        SetForegroundWindow(game);
        cafe.OpenMenu();
        yield return new WaitForSecondsRealtime(.25f);
        Require(!input.Controlling && cafe.Mode == CafePrototype.InputMode.Menu, "Menu did not safely release input");
        yield return ClickOwnedUi(game, hud, "SettingsButton");
        Require(hud.Page == 1, "Real mouse click did not open Settings page");
        Debug.Log("LOCAL05_REAL_CLICK_PASS: Settings");
        yield return ClickOwnedUi(game, hud, "BackButton");
        Require(hud.Page == 0, "Real mouse click did not return to main menu");
        yield return ClickOwnedUi(game, hud, "HelpButton");
        Require(hud.Page == 2, "Real mouse click did not open Help page");
        yield return ClickOwnedUi(game, hud, "BackButton");
        Require(hud.Page == 0, "Help Back mouse click failed");
        Debug.Log("LOCAL05_REAL_CLICK_PASS: Back/Help/Back");
        string folder = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Evidence", "LOCAL05", "RealGui"));
        Directory.CreateDirectory(folder);
        shots.SetOutputDirectory(Path.Combine(folder,"Screenshots"));
        yield return ClickOwnedUi(game, hud, "SaveScreenshotButton");
        yield return Wait(() => !shots.Capturing && shots.LastCaptureSucceeded,
            "Real UI screenshot button did not save");
        string first = shots.LastSavedPath;
        VerifySavedScreenshot(first);
        yield return ClickOwnedUi(game, hud, "SaveScreenshotButton");
        yield return Wait(() => !shots.Capturing && shots.LastCaptureSucceeded,
            "Second real UI screenshot button did not save");
        string second = shots.LastSavedPath;
        VerifySavedScreenshot(second);
        Require(first != second && File.Exists(first), "Real mouse screenshots overwrote a file");
        Debug.Log("LOCAL05_REAL_SCREENSHOT_PASS: " + first + " ; " + second);
        shots.SetOutputDirectory(null);
        yield return ClickOwnedUi(game, hud, "ContinueButton");
        Require(cafe.Mode == CafePrototype.InputMode.Walking && !input.Controlling,
            "Real Continue click failed from standing menu");
        Debug.Log("LOCAL05_REAL_CLICK_PASS: Continue walking");
        // Test the actual F8-to-menu-and-Continue cycle, on a controlled GUI with
        // no browser/fixture cursor ownership and no recording of real desktop pixels.
        cafe.Sit(0);
        yield return Wait(() => capture.Ready && input.Controlling,
            "Sitting did not auto-project desktop or obtain input");
        SendEscape();
        yield return Wait(() => cafe.Mode == CafePrototype.InputMode.Menu &&
                             !input.Controlling,"F8 did not release input and open menu");
        Require(hud.Page == 0, "F8 menu was not on main page");
        yield return ClickOwnedUi(game, hud, "ContinueButton");
        yield return Wait(() => cafe.Mode == CafePrototype.InputMode.Desktop && input.Controlling,
            "Real mouse Continue did not resume desktop input");
        Debug.Log("LOCAL05_F8_REAL_CONTINUE_PASS: OS mouse click after F8 resumed desktop control");
        SendEscape();
        yield return Wait(() => cafe.Mode == CafePrototype.InputMode.Menu && !input.Controlling,
            "Final F8 did not release input");
        Debug.Log("LOCAL05_REAL_UI_AND_SCREENSHOT_PASS");
    }

    IEnumerator ClickOwnedUi(IntPtr game, CafeHud hud, string name)
    {
        Require(cafe.Mode == CafePrototype.InputMode.Menu && !input.Controlling,
            "Refusing UI click while native desktop input is active");
        if (GetForegroundWindow() != game)
        {
            // Windows may refuse SetForegroundWindow for a remote terminal launcher.
            // Only promote our own visible game window, then use a normal click to focus it.
            ShowWindow(game, 9);
            SetWindowPos(game, (IntPtr)(-1), 0, 0, 0, 0, 0x0003);
            SetForegroundWindow(game);
        }
        yield return new WaitForSecondsRealtime(.12f);
        Debug.Log("LOCAL05_WINDOW_FOCUS: button=" + name + "; foreground=" + GetForegroundWindow() + "; own=" + game);
        Canvas.ForceUpdateCanvases();
        Vector2 point = hud.ButtonPoint(name);
        Require(GetClientRect(game, out Bounds client), "Unity window client rect unavailable");
        int width = client.right - client.left, height = client.bottom - client.top;
        Require(width > 300 && height > 300 && Screen.width > 0 && Screen.height > 0,
            "Invalid screen dimensions");
        var hits = new List<UnityEngine.EventSystems.RaycastResult>();
        hud.eventSystem.RaycastAll(new UnityEngine.EventSystems.PointerEventData(hud.eventSystem)
            { position = point }, hits);
        Require(hits.Count > 0 && (hits[0].gameObject == hud.Button(name).gameObject ||
            hits[0].gameObject.transform.IsChildOf(hud.Button(name).transform)),
            "UI raycast not owned by button " + name);
        var pos = new Point(
            Mathf.Clamp(Mathf.RoundToInt(point.x * width / Screen.width), 1, width - 2),
            Mathf.Clamp(Mathf.RoundToInt((Screen.height - point.y) * height / Screen.height), 1, height - 2));
        Require(ClientToScreen(game, ref pos), "Failed to map Unity button to Windows client");
        Require(GetAncestor(WindowFromPoint(pos), 2) == game, "Click target obscured by another window: " + name);
        SetCursorPos(pos.x, pos.y);
        yield return new WaitForSecondsRealtime(.14f);
        Require(GetAncestor(WindowFromPoint(pos), 2) == game,
            "Click target changed after pointer move: " + name);
        mouse_event(2, 0, 0, 0, UIntPtr.Zero);
        yield return new WaitForSecondsRealtime(.08f);
        mouse_event(4, 0, 0, 0, UIntPtr.Zero);
        yield return new WaitForSecondsRealtime(.22f);
        Debug.Log("LOCAL05_OS_CLICK_DISPATCHED: " + name +
            "; client=" + width + "x" + height + "; unity=" + Screen.width + "x" + Screen.height);
    }
    IEnumerator FrameRateTest()
    {
        while(!UnityEngine.Rendering.SplashScreen.isFinished)yield return null;
        yield return new WaitForSecondsRealtime(1);
        string helper=Path.GetFullPath(Path.Combine(Application.dataPath,"..","..","Native","NativeInputSmoke.exe"));
        fixtureProcess=Process.Start(new ProcessStartInfo(helper,"--fixture --fast"){UseShellExecute=false});
        yield return Wait(()=>{fixture=FindWindow("NetCafe A2 external input test",(uint)fixtureProcess.Id);return fixture!=IntPtr.Zero;},"FPS fixture startup");
        cafe.Sit(0);yield return Wait(()=>input.Controlling&&capture.Info.frames>3,"FPS desktop startup");
        RaiseOwned(fixture);
        for(int stage=0;stage<5;stage++)
        {
            if(stage==1)input.ToggleView();
            if(stage==2)cafe.Observe();
            if(stage==3)cafe.OpenMenu();
            if(stage==4)cafe.Stand();
            var expected=cafe.Mode;
            yield return new WaitForSecondsRealtime(1);
            Require(cafe.Mode==expected,"FPS sample changed input mode");
            double hz=Screen.currentResolution.refreshRateRatio.value;
            Require(hz>0&&QualitySettings.vSyncCount==1&&Application.targetFrameRate==-1,"Display pacing changed");
            var times=new List<double>();int startFrames=Time.frameCount,startTicks=Stat(7);ulong copied=capture.Info.frames;
            using var process=Process.GetCurrentProcess();var cpu=process.TotalProcessorTime;
            double start=Time.realtimeSinceStartupAsDouble;
            while(Time.realtimeSinceStartupAsDouble-start<3){yield return null;times.Add(Time.unscaledDeltaTime*1000);}
            double elapsed=Time.realtimeSinceStartupAsDouble-start,fps=(Time.frameCount-startFrames)/elapsed;
            double desktopFps=(capture.Info.frames-copied)/elapsed,sourceFps=(Stat(7)-startTicks)/elapsed;
            times.Sort();NC_GetMemory(out ulong privateBytes,out ulong workingBytes);
            process.Refresh();double cpuPercent=(process.TotalProcessorTime-cpu).TotalSeconds/elapsed*100/Environment.ProcessorCount;
            Debug.Log($"FPS_METRIC {cafe.Mode} full={!input.RoomView}: displayHz={hz:F2}; gameFPS={fps:F1}; desktopFPS={desktopFps:F1}; sourceTicksFPS={sourceFps:F1}; p95FrameMs={times[(int)((times.Count-1)*.95)]:F2}; cpuMachinePercent={cpuPercent:F2}; privateMB={privateBytes/1048576d:F1}; workingMB={workingBytes/1048576d:F1}; focused={Application.isFocused}");
            Require(fps>=hz*.85,"Rendering below 85% of display refresh: "+cafe.Mode);
            Require(desktopFps>=Math.Min(sourceFps,hz)*.5,"Fresh desktop updates throttled: "+cafe.Mode);
        }
    }
    IEnumerator PointerTest()
    {
        Require(!cafe.SelfTesting,"Pointer regression must use normal room input");
        while(!UnityEngine.Rendering.SplashScreen.isFinished)yield return null;
        yield return new WaitForSecondsRealtime(1);
        string helper=Path.GetFullPath(Path.Combine(Application.dataPath,"..","..","Native","NativeInputSmoke.exe"));
        fixtureProcess=Process.Start(new ProcessStartInfo(helper,"--fixture"){UseShellExecute=false});
        yield return Wait(()=>{fixture=FindWindow("NetCafe A2 external input test",(uint)fixtureProcess.Id);return fixture!=IntPtr.Zero;},"Pointer fixture startup");
        Require(NC_MonitorRect(0,out monitor)!=0,"Monitor geometry missing");
        SetWindowPos(fixture,IntPtr.Zero,monitor.right-1000,160,0,0,0x0011);
        cafe.Sit(0);yield return Wait(()=>input.Controlling,"Pointer desktop startup");
        RaiseOwned(fixture);yield return new WaitForSecondsRealtime(.5f);
        Debug.Log("POINTER_RAW_TARGET entry: "+RawMouseTarget());
        yield return DesktopMovement("entry");
        SendEscape();yield return Wait(()=>cafe.Mode==CafePrototype.InputMode.Menu&&!input.Controlling,"Pointer menu");
        yield return new WaitForSecondsRealtime(.15f);
        CheckVisibleCursor("menu");
        var ui = GetComponent<CafeHud>();
        Require(ui.uiInput.enabled, "Menu input module disabled");
        var point = ui.ButtonPoint("ContinueButton");
        var results = new List<UnityEngine.EventSystems.RaycastResult>();
        ui.eventSystem.RaycastAll(new UnityEngine.EventSystems.PointerEventData(ui.eventSystem)
            { position = point }, results);
        Require(results.Count > 0 && results[0].gameObject == ui.Button("ContinueButton").gameObject,
            "Esc menu Continue button blocked by another UI raycaster");
        cafe.Continue();yield return Wait(()=>input.Controlling,"Pointer menu continue");
        RaiseOwned(fixture);yield return new WaitForSecondsRealtime(.5f);
        Debug.Log("POINTER_RAW_TARGET resume: "+RawMouseTarget());
        yield return DesktopMovement("resume");
        input.ResetToolbar();yield return null;
        Rect r=input.ToolbarRect;
        NativePoint(r.x+r.width*.30f,r.center.y,2);yield return new WaitForSecondsRealtime(.04f);NativePoint(r.x+r.width*.30f,r.center.y,4);
        yield return Wait(()=>cafe.Mode==CafePrototype.InputMode.Observing&&!input.Controlling&&Application.isFocused,"Pointer observation");
        var hud=GetComponent<CafeHud>();
        Require(hud.Button("ToolbarMenu").GetComponentInChildren<TMPro.TMP_Text>().text=="菜单 Esc","Observation toolbar displays a desktop-only shortcut");
        cafe.OpenMenu();Require(hud.Button("ContinueButton").GetComponentInChildren<TMPro.TMP_Text>().text=="继续看房间","Observation continue label does not match its destination");
        cafe.Continue();Require(cafe.Mode==CafePrototype.InputMode.Observing,"Menu did not restore observation");
        yield return new WaitForSecondsRealtime(.15f);
        CheckVisibleCursor("observation");
        yield return CheckPointerPixel("observation");
        yield return LookMovement("observation");
        cafe.ResumeDesktop();yield return Wait(()=>input.Controlling,"Pointer observation return");
        RaiseOwned(fixture);yield return new WaitForSecondsRealtime(.3f);
        yield return DesktopMovement("observation-return");
        input.ResetToolbar();yield return null;r=input.ToolbarRect;
        NativePoint(r.x+r.width*.50f,r.center.y,2);yield return new WaitForSecondsRealtime(.04f);NativePoint(r.x+r.width*.50f,r.center.y,4);
        yield return Wait(()=>cafe.Mode==CafePrototype.InputMode.Walking&&!input.Controlling&&Application.isFocused,"Pointer toolbar stand");
        yield return new WaitForSecondsRealtime(.2f);
        Require(!input.OverToolbar,"Hidden seat toolbar still blocks the standing camera");
        Require(Cursor.lockState==CursorLockMode.Locked&&!Cursor.visible,"Walking cursor did not lock and hide");
        yield return LookMovement("walking-after-toolbar-stand");
        cafe.OpenMenu();yield return new WaitForSecondsRealtime(.15f);CheckVisibleCursor("standing-menu");
        cafe.Sit(0);yield return Wait(()=>input.Controlling,"Pointer re-seat");
        RaiseOwned(fixture);yield return new WaitForSecondsRealtime(.3f);
        yield return DesktopMovement("re-seat");
        // Run the system-shell check last so its animation cannot interfere with room input checks.
        keybd_event(0x5b,0x5b,1,UIntPtr.Zero);keybd_event(0x5b,0x5b,3,UIntPtr.Zero);
        yield return Wait(()=>cafe.Mode==CafePrototype.InputMode.Menu&&!input.Controlling,"Windows key release");
        var shellPoint=Source(180,90);SetCursorPos(shellPoint.x,shellPoint.y);yield return new WaitForSecondsRealtime(.5f);
        var systemCursor=new CursorInfo{size=Marshal.SizeOf<CursorInfo>()};
        Require(GetCursorInfo(ref systemCursor)&&(systemCursor.flags&1)!=0,"Windows key left the system cursor hidden");
        Debug.Log("POINTER_WINDOWS_KEY_PASS: native control ended; system cursor restored");
        GetWindowThreadProcessId(GetForegroundWindow(),out uint shellPid);
        if(shellPid!=0)
        {
            using var shell=Process.GetProcessById((int)shellPid);
            if(shell.ProcessName=="StartMenuExperienceHost"||shell.ProcessName=="SearchHost")
            {keybd_event(0x1b,0,0,UIntPtr.Zero);keybd_event(0x1b,0,2,UIntPtr.Zero);}
        }
    }
    static string RawMouseTarget()
    {
        uint count=0,size=(uint)Marshal.SizeOf<RawDevice>();GetRegisteredRawInputDevices(null,ref count,size);
        var devices=new RawDevice[count];Require(GetRegisteredRawInputDevices(devices,ref count,size)!=uint.MaxValue,"Raw input registration query failed");
        foreach(var device in devices)if(device.page==1&&device.usage==2){var name=new StringBuilder(100);GetClassName(device.target,name,name.Capacity);return $"{name}; flags={device.flags:X}; hwnd={device.target}";}
        return "none";
    }
    IEnumerator DesktopMovement(string stage)
    {
        Require(RawMouseTarget().StartsWith("NetCafeInputPump",StringComparison.Ordinal),"Native mouse receiver lost ownership: "+stage);
        Require(GetClipCursor(out var clip)&&clip.right-clip.left>=Screen.width&&clip.bottom-clip.top>=Screen.height,"Desktop mouse is clipped: "+stage);
        Vector2 before=input.Pointer;
        // Exercise Windows motion events, without NC_TestPointer setting the virtual position.
        for(int i=0;i<4;i++){mouse_event(1,8,4,0,UIntPtr.Zero);yield return new WaitForSecondsRealtime(.05f);}
        Require(input.Controlling&&Vector2.Distance(before,input.Pointer)>2,"Desktop pointer did not move: "+stage+"; "+input.Diagnostic());
        Debug.Log($"POINTER_MOVEMENT {stage}: before={before}; after={input.Pointer}");
    }
    IEnumerator LookMovement(string stage)
    {
        var client=FindWindow("NetCafe Playtest",(uint)Process.GetCurrentProcess().Id);
        Require(GetForegroundWindow()==client,"Room is not foreground; no movement sent");
        if(cafe.Mode==CafePrototype.InputMode.Observing){SetCursorPos(Screen.width/2,Screen.height/2);yield return new WaitForSecondsRealtime(.1f);}
        Quaternion before=cafe.eyes.transform.rotation;
        bool observe=cafe.Mode==CafePrototype.InputMode.Observing;
        if(observe){mouse_event(8,0,0,0,UIntPtr.Zero);yield return new WaitForSecondsRealtime(.1f);}
        for(int i=0;i<5;i++){mouse_event(1,12,3,0,UIntPtr.Zero);yield return new WaitForSecondsRealtime(.05f);}
        if(observe){mouse_event(16,0,0,0,UIntPtr.Zero);yield return new WaitForSecondsRealtime(.1f);}
        Require(Quaternion.Angle(before,cafe.eyes.transform.rotation)>1,"Room camera did not respond to OS mouse movement: "+stage+"; raw="+RawMouseTarget());
        Debug.Log("POINTER_LOOK_PASS: "+stage);
    }
    void CheckVisibleCursor(string stage)
    {
        var info=new CursorInfo{size=Marshal.SizeOf<CursorInfo>()};
        Require(GetCursorInfo(ref info),"Cursor state unavailable");
        bool menu = cafe.Mode == CafePrototype.InputMode.Menu;
        bool visible = input.DrawsPointer;
        bool correct = menu ?
            !visible && Cursor.visible && (info.flags & 1) != 0 :
            visible && !Cursor.visible && (info.flags & 1) == 0;
        Require(Cursor.lockState == CursorLockMode.None && correct,
            "Cursor policy wrong: " + stage + $"; software={visible}; unity={Cursor.visible}/{Cursor.lockState}; windows={info.flags}/{info.cursor}");
        Debug.Log("POINTER_CURSOR_PASS: "+stage);
    }
    IEnumerator CheckPointerPixel(string stage)
    {
        yield return new WaitForEndOfFrame();
        var frame=ScreenCapture.CaptureScreenshotAsTexture();
        try
        {
            Color pixel=frame.GetPixel(Mathf.RoundToInt(input.Pointer.x+4),Mathf.RoundToInt(Screen.height-input.Pointer.y-8));
            Require(pixel.r>.9f&&pixel.g>.9f&&pixel.b>.9f,"Software pointer not visible above UI: "+stage+"; "+pixel);
            Color edge=frame.GetPixel(Mathf.RoundToInt(input.Pointer.x+1),Mathf.RoundToInt(Screen.height-input.Pointer.y-3));
            Require(edge.r<.15f&&edge.g<.15f&&edge.b<.15f,"Pointer outline missing above bright UI: "+stage+"; "+edge);
            Debug.Log("POINTER_PIXEL_PASS: "+stage);
        }
        finally { Destroy(frame); }
    }
    IEnumerator FocusTest()
    {
        Require(!cafe.SelfTesting,"Focus regression must use normal application focus handling");
        while(!UnityEngine.Rendering.SplashScreen.isFinished)yield return null;
        yield return new WaitForSecondsRealtime(1);
        string helper=Path.GetFullPath(Path.Combine(Application.dataPath,"..","..","Native","NativeInputSmoke.exe"));
        fixtureProcess=Process.Start(new ProcessStartInfo(helper,"--fixture"){UseShellExecute=false});
        yield return Wait(()=>{fixture=FindWindow("NetCafe A2 external input test",(uint)fixtureProcess.Id);return fixture!=IntPtr.Zero;},"Focus fixture startup");
        Require(NC_MonitorRect(0,out monitor)!=0,"Monitor geometry missing");
        SetWindowPos(fixture,IntPtr.Zero,monitor.right-1000,160,0,0,0x0011);
        cafe.Sit(0);
        // Exercise the handoff callback before native Begin can report Controlling.
        cafe.SendMessage("OnApplicationFocus",false);
        Require(cafe.Mode==CafePrototype.InputMode.Desktop,"Focus handoff reopened the menu before control started");
        yield return Wait(()=>input.Controlling,"Normal focus desktop startup");
        RaiseOwned(fixture);yield return new WaitForSecondsRealtime(1);
        Require(cafe.Mode==CafePrototype.InputMode.Desktop&&input.Controlling,"Target focus reopened the menu");
        Point testPoint = Source(180,90);
        IntPtr targetRoot = GetAncestor(WindowFromPoint(testPoint),2);
        if (targetRoot != fixture)
        {
            Debug.LogWarning("FOCUS_FIXTURE_ZORDER_RETRY: actual=" + WindowTitle(targetRoot));
            RaiseOwned(fixture);
            // The focus-loss path can restore Unity's topmost order after RaiseOwned.
            // Put only our owned fixture on top immediately before the guarded click.
            SetWindowPos(fixture,(IntPtr)(-1),0,0,0,0,0x0013);
            SetForegroundWindow(fixture);
            yield return new WaitForSecondsRealtime(.15f);
            targetRoot = GetAncestor(WindowFromPoint(testPoint),2);
        }
        Require(targetRoot==fixture,"Owned fixture covered; no click sent; actual="+WindowTitle(targetRoot));
        int expected=Stat(0)+1;yield return Click(testPoint);yield return Wait(()=>Stat(0)==expected,"Normal-focus external click");
        yield return Wait(()=>GetForegroundWindow()==fixture,"External click keyboard focus");
        SendEscape();yield return Wait(()=>cafe.Mode==CafePrototype.InputMode.Menu&&!input.Controlling,"Focus escape menu");
        Vector2 point=GetComponent<CafeHud>().ButtonPoint("ContinueButton");
        Debug.Log($"FOCUS_SMOKE_CONTINUE_READY: x={Mathf.RoundToInt(point.x)} y={Mathf.RoundToInt(Screen.height-point.y)}");
        if (Array.IndexOf(Environment.GetCommandLineArgs(), "-focusAutoClick") >= 0)
        {
            // A process launched by a remote terminal can be on another Windows desktop.
            // Generate a real OS click only inside the GUI process, and only if its own
            // window owns the point; otherwise fail without injecting into another app.
            var clickPoint = new Point(Mathf.RoundToInt(point.x), Mathf.RoundToInt(Screen.height-point.y));
            IntPtr rootWindow = GetAncestor(WindowFromPoint(clickPoint), 2);
            GetWindowThreadProcessId(rootWindow, out uint ownerPid);
            Require(ownerPid == (uint)Process.GetCurrentProcess().Id, "Refusing menu click outside this game's window");
            SetCursorPos(clickPoint.x, clickPoint.y);
            yield return new WaitForSecondsRealtime(.12f);
            rootWindow = GetAncestor(WindowFromPoint(clickPoint), 2);
            GetWindowThreadProcessId(rootWindow, out ownerPid);
            Require(ownerPid == (uint)Process.GetCurrentProcess().Id, "Menu target changed before click, refusing injection");
            mouse_event(2, 0, 0, 0, UIntPtr.Zero);
            yield return new WaitForSecondsRealtime(.08f);
            mouse_event(4, 0, 0, 0, UIntPtr.Zero);
            Debug.Log("FOCUS_OWNED_OS_CLICK_DISPATCHED");
        }
        yield return Wait(()=>cafe.Mode==CafePrototype.InputMode.Desktop&&input.Controlling,"Real continue menu click",20);
        yield return new WaitForSecondsRealtime(1);
        Require(cafe.Mode==CafePrototype.InputMode.Desktop&&input.Controlling,"Continue immediately reopened the menu");
        SendEscape();yield return Wait(()=>cafe.Mode==CafePrototype.InputMode.Menu&&!input.Controlling,"Focus final escape");
    }
    IEnumerator MemoryTest()
    {
        while(!UnityEngine.Rendering.SplashScreen.isFinished)yield return null;
        cafe=GetComponent<CafePrototype>();capture=GetComponent<CafeDesktopCapture>();input=GetComponent<CafeDesktopInput>();
        yield return Measure("memory-empty-room",8);
        cafe.Sit(0);yield return new WaitForSecondsRealtime(2);
        yield return Measure("memory-desktop",8);
        input.EndControl();capture.StopCapture();cafe.OpenMenu();
        Debug.Log("MEMORY_SMOKE_PASS");Application.Quit(0);
    }
    IEnumerator ExitTest()
    {
        while(!UnityEngine.Rendering.SplashScreen.isFinished)yield return null;
        yield return new WaitForSecondsRealtime(1);
        cafe=GetComponent<CafePrototype>();capture=GetComponent<CafeDesktopCapture>();input=GetComponent<CafeDesktopInput>();
        cafe.Sit(0);yield return Wait(()=>input.Controlling,"Exit test desktop control");
        SendEscape();
        yield return Wait(()=>cafe.Mode==CafePrototype.InputMode.Menu&&!input.Controlling,"Exit test escape menu");
        yield return new WaitForSecondsRealtime(.2f);
        Vector2 point=GetComponent<CafeHud>().ButtonPoint("QuitButton");
        Debug.Log($"EXIT_SMOKE_MENU_READY: x={Mathf.RoundToInt(point.x)} y={Mathf.RoundToInt(Screen.height-point.y)}; {input.Diagnostic()}");
    }
    IEnumerator Run()
    {
        while(!UnityEngine.Rendering.SplashScreen.isFinished)yield return null;
        yield return new WaitForSecondsRealtime(1);
        string helper=Path.GetFullPath(Path.Combine(Application.dataPath,"..","..","Native","NativeInputSmoke.exe"));
        Require(File.Exists(helper),"Build native test helper first");
        fixtureProcess=Process.Start(new ProcessStartInfo(helper,"--fixture"){UseShellExecute=false});
        yield return Wait(()=>{fixture=FindWindow("NetCafe A2 external input test",(uint)fixtureProcess.Id);return fixture!=IntPtr.Zero;},"External fixture startup");
        ShowWindow(fixture,5);ShowWindow(fixture,5);SetForegroundWindow(fixture);
        Require(NC_MonitorRect(0,out monitor)!=0,"Monitor geometry missing");
        SetWindowPos(fixture,IntPtr.Zero,monitor.right-1000,160,0,0,0x0011);
        yield return Measure("empty-room",5);
        cafe.Sit(0);yield return Wait(()=>input.Controlling&&capture.Info.frames>3,"Desktop auto-start");
        PostMessage(fixture,0x800b,IntPtr.Zero,IntPtr.Zero);SetForegroundWindow(fixture);yield return new WaitForSecondsRealtime(.15f);
        yield return Measure("desktop-input",8);
        RaiseOwned(fixture);yield return new WaitForSecondsRealtime(.2f);
        Debug.Log($"A2_ENV: {SystemInfo.operatingSystem}; {SystemInfo.processorType}; {SystemInfo.graphicsDeviceName}; monitor={capture.Info.width}x{capture.Info.height}");
        Point button=Source(180,90),marker=Source(700,400);
        Color before=ReadPixel(marker);Require(before.b>before.r,$"External fixture was not visible in capture before clicking: color={before}; foreground={WindowTitle(GetForegroundWindow())}; fixture={fixture}; {input.Diagnostic()}");
        int expected=0;
        for(int round=0;round<3;round++)
        {
            if(round==1)input.ToggleView();
            if(round==2){cafe.Observe();yield return null;Require(!input.Controlling&&cafe.Mode==CafePrototype.InputMode.Observing,"Observation did not release input");cafe.ResumeDesktop();yield return Wait(()=>input.Controlling,"Return to desktop");}
            yield return null;yield return null;RaiseOwned(fixture);yield return null;
            for(int i=0;i<8;i++)
            {
                Require(GetAncestor(WindowFromPoint(button),2)==fixture,"Owned test fixture was covered; stopped to avoid clicking another app");
                yield return Click(button);
                expected++;yield return Wait(()=>Stat(0)==expected,"Mapped external click");
            }
            yield return Wait(()=>ReadPixel(marker).g>.5f&&ReadPixel(marker).r<.3f,"External click visible in captured GPU texture");
        }
        Debug.Log("A2_NATIVE_APP_PASS: 24 mapped clicks in both view modes and observe/return; captured pixels changed");
        Debug.Log($"A2_NATIVE_STATE: clicks={Stat(0)}; downs={Stat(5)}; ups={Stat(6)}; keyStateDown={Stat(3)}");
        Require(Stat(5)>=24&&Stat(5)==Stat(6),"Click down/up counts diverged");
        input.CheckToolbar();input.ResetToolbar();yield return null;
        var toolbar=input.ToolbarRect;NativePoint(toolbar.x+toolbar.width*.30f,toolbar.center.y,2);yield return new WaitForSecondsRealtime(.04f);NativePoint(toolbar.x+toolbar.width*.30f,toolbar.center.y,4);
        yield return Wait(()=>cafe.Mode==CafePrototype.InputMode.Observing&&!input.Controlling,"Native toolbar observation");
        Require(Stat(0)==24,"Toolbar click leaked to underlying application");
        cafe.ResumeDesktop();yield return Wait(()=>input.Controlling,"Toolbar return");
        cafe.OpenMenu();yield return null;Require(!input.Controlling&&capture.Active,"Menu did not release input while keeping capture");
        Require(RegisterHotKey(IntPtr.Zero,0x4e44,0x4000,0x79),"Could not reserve test hotkey F10");
        try{Require(!input.TrySetHotkey(0x79,0,false)&&input.EscapeKey==oldKey,"Hotkey collision did not preserve existing key");}
        finally{UnregisterHotKey(IntPtr.Zero,0x4e44);}
        Require(input.TrySetHotkey(0x78,2,false),"Ctrl+F9 configuration failed");
        Require(input.TrySetHotkey(oldKey,oldMods,false),"Original escape key restore failed");
        cafe.Continue();yield return Wait(()=>input.Controlling,"Menu continue");
        SendEscape();
        yield return Wait(()=>cafe.Mode==CafePrototype.InputMode.Menu&&!input.Controlling,"Actual escape hotkey");
        cafe.Continue();yield return Wait(()=>input.Controlling,"Actual escape resume");
        cafe.Stand();yield return null;Require(cafe.CurrentSeat<0&&cafe.body.enabled&&!input.Controlling&&capture.Active,"Stand did not release control");
        cafe.Sit(1);yield return Wait(()=>input.Controlling,"Second seat capture/input");
        cafe.OpenMenu();yield return BrowserTest();
        latency.Sort();Debug.Log($"A2_CLICK_LATENCY: samples={latency.Count}; p50Ms={latency[latency.Count/2]:F2}; p95Ms={latency[(int)((latency.Count-1)*.95)]:F2}; maxMs={latency[^1]:F2}; measured test-hook submission through OS and external event receipt, excludes physical hardware and display scanout");
        yield return Measure("menu",5);
        if(Array.IndexOf(Environment.GetCommandLineArgs(),"-a2Soak")>=0)yield return Soak();
    }
    IEnumerator BrowserTest()
    {
        string folder=Path.GetFullPath(Path.Combine(Application.dataPath,"..","Evidence","A2"));Directory.CreateDirectory(folder);
        string run=Guid.NewGuid().ToString("N");browserPrefix="NetCafeA2-"+run+"|";
        string page=Path.Combine(folder,"browser-input-"+run+".html");
        File.WriteAllText(page,"<!doctype html><meta charset='utf-8'><style>body{background:rgb(22,80,140);margin:0}button{position:fixed;inset:0;width:100%;height:100%;border:0;background:transparent;font-size:22px}#animation{pointer-events:none}</style><button id='b'>A2 browser clicks: 0</button><div id='animation' style='position:absolute;left:320px;top:40px;width:40px;height:40px'></div><script>let n=0,ticks=0;function animate(t){ticks++;animation.style.background=Math.floor(t/120)%2?'rgb(240,20,200)':'rgb(20,240,180)';requestAnimationFrame(animate)}requestAnimationFrame(animate);function report(){let r=b.getBoundingClientRect();document.title='NetCafeA2|'+Math.round((screenX+r.x+r.width/2)*devicePixelRatio)+'|'+Math.round((screenY+outerHeight-innerHeight+r.y+r.height/2)*devicePixelRatio)+'|'+n+'|'+ticks+'|'+Math.round((screenX+340)*devicePixelRatio)+'|'+Math.round((screenY+outerHeight-innerHeight+60)*devicePixelRatio)+'|'}b.onclick=()=>{b.textContent='A2 browser clicks: '+(++n);document.body.style.background='rgb(35,180,90)';report()};setInterval(report,100);report()</script>".Replace("NetCafeA2|",browserPrefix));
        string chrome=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),"Google","Chrome","Application","chrome.exe");
        string edge=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),"Microsoft","Edge","Application","msedge.exe");
        foreach(string executable in new[]{edge,chrome})
        {
        string name=Path.GetFileNameWithoutExtension(executable);Require(File.Exists(executable),name+" test browser missing");browserProcessName=name;
        string profile=Path.Combine(folder,name+"Profile-"+run);Directory.CreateDirectory(profile);File.WriteAllText(Path.Combine(profile,"First Run"),"");
        browserProcess=Process.Start(new ProcessStartInfo(executable,$"--user-data-dir=\"{profile}\" --no-first-run --no-default-browser-check --disable-background-mode --window-position=160,160 --window-size=820,600 --new-window \"{new Uri(page).AbsoluteUri}\""){UseShellExecute=false});
        yield return Wait(()=>{Require(browserProcess!=null,"Test browser launch failed");browser=FindWindow(browserPrefix,0,browserProcessName);return OwnsBrowser(browser);},"Isolated browser startup",20);
        Require(!OwnsBrowser(fixture),"Foreign fixture accepted as test browser");
        Require(OwnsBrowser(browser),"Test browser ownership lost");ShowWindow(browser,5);SetForegroundWindow(browser);
        cafe.ResumeDesktop();yield return Wait(()=>input.Controlling,"Browser desktop resume");
        Require(OwnsBrowser(browser),"Test browser ownership lost");SetForegroundWindow(browser);
        // Keep owned test controls clear of the Windows search popup; never click a covering window.
        SetWindowPos(browser,IntPtr.Zero,monitor.right-700,60,820,780,0x0010);
        RaiseOwned(browser);
        yield return new WaitForSecondsRealtime(.3f);
        double started=Time.realtimeSinceStartupAsDouble;
        int initialTicks=BrowserStats()[3];
        yield return new WaitForSecondsRealtime(2);
        int latestTicks=BrowserStats()[3];
        Require(latestTicks-initialTicks>=20,"Covered browser animation stalled");
        var geometry=BrowserStats();
        var animationPoint=new Point(geometry[4],geometry[5]);
        bool magenta=false,cyan=false;double until=Time.realtimeSinceStartupAsDouble+2;
        var samples=new[]{animationPoint,new Point(animationPoint.x+32,animationPoint.y),new Point(animationPoint.x-32,animationPoint.y),new Point(animationPoint.x,animationPoint.y+32),new Point(animationPoint.x,animationPoint.y-32)};
        while(Time.realtimeSinceStartupAsDouble<until)
        {
            foreach(var sample in samples){Color pixel=ReadPixel(sample);magenta|=pixel.r>.7f&&pixel.b>.5f;cyan|=pixel.g>.7f&&pixel.b>.4f;}
            if(magenta&&cyan)break;yield return new WaitForSecondsRealtime(.05f);
        }
        Require(magenta&&cyan,$"Browser animated pixels stopped updating in captured texture; point={animationPoint.x},{animationPoint.y}; magenta={magenta}; cyan={cyan}");
        Debug.Log($"A2_BROWSER_ANIMATION: app={name}; requestAnimationFrame advanced {latestTicks-initialTicks} frames in 2s while Unity covered the monitor");
        Debug.Log($"A2_BROWSER_CAPTURE_PASS: app={name}; seconds={Time.realtimeSinceStartupAsDouble-started:F1}; owned visible browser; captured animation updates; isolated profile");cafe.OpenMenu();
        Require(OwnsBrowser(browser),"Test browser ownership lost before cleanup");PostMessage(browser,0x10,IntPtr.Zero,IntPtr.Zero);
        yield return Wait(()=>!OwnsBrowser(browser),name+" test window cleanup");CloseBrowser();
        }
    }
    IEnumerator Measure(string stage,float seconds)
    {
        using var process=Process.GetCurrentProcess();var cpu=process.TotalProcessorTime;double start=Time.realtimeSinceStartupAsDouble;ulong frames=capture.Info.frames;int rendered=Time.frameCount;
        yield return new WaitForSecondsRealtime(seconds);process.Refresh();Require(NC_GetMemory(out ulong privateBytes,out ulong workingBytes)!=0,"Memory measurement failed");
        double elapsed=Time.realtimeSinceStartupAsDouble-start,cpuPercent=(process.TotalProcessorTime-cpu).TotalSeconds/elapsed*100;
        Debug.Log($"A2_ENGINE_MEMORY {stage}: trackedAllocatedMB={UnityEngine.Profiling.Profiler.GetTotalAllocatedMemoryLong()/1048576d:F1}; trackedReservedMB={UnityEngine.Profiling.Profiler.GetTotalReservedMemoryLong()/1048576d:F1}; managedLiveMB={GC.GetTotalMemory(false)/1048576d:F1}");
        Debug.Log($"A2_METRIC {stage}: seconds={elapsed:F2}; privateMB={privateBytes/1048576d:F1}; workingMB={workingBytes/1048576d:F1}; cpuOneCorePercent={cpuPercent:F1}; cpuMachinePercent={cpuPercent/Environment.ProcessorCount:F2}; unityFPS={(Time.frameCount-rendered)/elapsed:F1}; captureFPS={(capture.Info.frames>=frames?capture.Info.frames-frames:0)/elapsed:F1}; sourceToCopyMs={capture.Info.sourceAgeMs:F2}; {input.Diagnostic()}");
    }
    IEnumerator Soak()
    {
        CloseBrowser();
        double start=Time.realtimeSinceStartupAsDouble;int cycle=0;ulong previous=0;
        Debug.Log("A2_SOAK_START: 1800 seconds; animated external app, input/observe/menu cycles, no desktop screenshots recorded");
        while(Time.realtimeSinceStartupAsDouble-start<1800)
        {
            cycle++;cafe.ResumeDesktop();yield return Wait(()=>input.Controlling,"Soak desktop resume");
            RaiseOwned(fixture);yield return null;
            Require(GetAncestor(WindowFromPoint(Source(180,90)),2)==fixture,"Soak fixture covered; stopped to avoid clicking another app");
            int expected=Stat(0)+1;yield return Click(Source(180,90));yield return Wait(()=>Stat(0)==expected,"Soak real click");
            yield return Measure("soak-input-"+cycle,20);
            Require(capture.Info.frames>previous&&capture.Info.ageMs<2000,"Capture stalled during soak");previous=capture.Info.frames;
            cafe.Observe();yield return null;Require(!input.Controlling,"Soak observe did not release control");yield return Measure("soak-observe-"+cycle,20);
            cafe.OpenMenu();yield return Measure("soak-menu-"+cycle,20);
            input.ToggleView();
        }
        Debug.Log($"A2_SOAK_PASS: seconds={Time.realtimeSinceStartupAsDouble-start:F1}; cycles={cycle}; captureFrames={capture.Info.frames}; continued external animation and input release");
    }
    IEnumerator Click(Point source,IntPtr target=default)
    {
        if(target==IntPtr.Zero)target=fixture;
        if(target!=fixture){RaiseOwned(target);yield return new WaitForSecondsRealtime(.1f);}
        IntPtr hit=WindowFromPoint(source),root=GetAncestor(hit,2);if(target!=fixture){var hitClass=new StringBuilder(100);GetClassName(hit,hitClass,hitClass.Capacity);Debug.Log($"A2_BROWSER_CLICK: source={source.x},{source.y}; hit={hit}/{hitClass}; root={root}; foreground={GetForegroundWindow()}; expected={target}");}
        Require(OwnsTestWindow(target)&&InMonitor(source)&&root==target,$"Test click target is not the owned visible window; expected={target}; source={source.x},{source.y}; hit={hit}; root={root}/{WindowTitle(root)}; monitor={monitor.left},{monitor.top},{monitor.right},{monitor.bottom}; {input.Diagnostic()}");
        Rect r=capture.DesktopRect;
        float x=r.x+(source.x-monitor.left)*(r.width-1)/(monitor.right-monitor.left-1);
        float y=r.y+(source.y-monitor.top)*(r.height-1)/(monitor.bottom-monitor.top-1);
        int before=fixture!=IntPtr.Zero?Stat(0):0;
        NativePoint(x,y,0);yield return new WaitForSecondsRealtime(.08f);
        NativePoint(x,y,2);yield return new WaitForSecondsRealtime(.12f);
        double start=Time.realtimeSinceStartupAsDouble;NativePoint(x,y,4);if(target!=fixture){double adoptUntil=Time.realtimeSinceStartupAsDouble+1;while(Time.realtimeSinceStartupAsDouble<adoptUntil){IntPtr focused=GetForegroundWindow();if(OwnsBrowser(focused)){browser=focused;break;}yield return null;}}
        if(GetForegroundWindow()==fixture){yield return Wait(()=>Stat(0)>before,"Real click acknowledgement");latency.Add((Time.realtimeSinceStartupAsDouble-start)*1000);}
        else yield return new WaitForSecondsRealtime(.05f);
    }
    void NativePoint(float x,float y,int flags){if(!input.Controlling&&flags==4){mouse_event(4,0,0,0,UIntPtr.Zero);return;}Require(NC_TestPointer(Mathf.RoundToInt(x),Mathf.RoundToInt(y),flags,0)!=0,"Native pointer injection failed");}
    void SendEscape()
    {
        byte[] modifiers={0x11,0x12,0x10};int[] masks={2,1,4};
        for(int i=0;i<3;i++)if((input.EscapeModifiers&masks[i])!=0)keybd_event(modifiers[i],0,0,UIntPtr.Zero);
        byte key=(byte)input.EscapeKey;keybd_event(key,0,0,UIntPtr.Zero);keybd_event(key,0,2,UIntPtr.Zero);
        for(int i=2;i>=0;i--)if((input.EscapeModifiers&masks[i])!=0)keybd_event(modifiers[i],0,2,UIntPtr.Zero);
    }
    Point Source(int x,int y){var p=new Point(x,y);ClientToScreen(fixture,ref p);return p;}
    void RaiseOwned(IntPtr window)
    {
        Require(OwnsTestWindow(window),"Refusing to raise an unowned test window");
        // Pin the disposable source below Unity to prevent unrelated popup windows receiving test clicks.
        SetWindowPos(window,(IntPtr)(-1),0,0,0,0,0x0013);
        var client=FindWindow("NetCafe Playtest",(uint)Process.GetCurrentProcess().Id);
        Require(client!=IntPtr.Zero,"Owned Unity test window missing");
        SetWindowPos(client,(IntPtr)(-1),0,0,0,0,0x0013);
        SetForegroundWindow(window);
    }
    int Stat(int index){Require(SendMessageTimeout(fixture,0x800a,(IntPtr)index,IntPtr.Zero,2,500,out IntPtr value)!=IntPtr.Zero,"External fixture hung");return value.ToInt32();}
    static string WindowTitle(IntPtr w){var s=new StringBuilder(500);GetWindowText(w,s,s.Capacity);return s.ToString();}
    static IntPtr FindWindow(string title,uint owner=0,string processName=null){IntPtr found=IntPtr.Zero;EnumWindows((w,d)=>{if(!IsWindowVisible(w)||!WindowTitle(w).StartsWith(title,StringComparison.Ordinal))return true;GetWindowThreadProcessId(w,out uint pid);if(owner!=0&&pid!=owner)return true;if(!string.IsNullOrEmpty(processName)){try{if(!Process.GetProcessById((int)pid).ProcessName.Equals(processName,StringComparison.OrdinalIgnoreCase))return true;}catch{return true;}}found=w;return false;},IntPtr.Zero);return found;}
    static bool OwnedBy(IntPtr window,Process process) => window!=IntPtr.Zero&&process!=null&&!process.HasExited&&GetWindowThreadProcessId(window,out uint pid)!=0&&pid==(uint)process.Id;
    bool OwnsBrowser(IntPtr window){if(window==IntPtr.Zero||!IsWindowVisible(window)||string.IsNullOrEmpty(browserPrefix)||string.IsNullOrEmpty(browserProcessName)||!WindowTitle(window).StartsWith(browserPrefix,StringComparison.Ordinal))return false;GetWindowThreadProcessId(window,out uint pid);try{return Process.GetProcessById((int)pid).ProcessName.Equals(browserProcessName,StringComparison.OrdinalIgnoreCase);}catch{return false;}}
    bool OwnsTestWindow(IntPtr window) => window!=IntPtr.Zero&&(window==browser?OwnsBrowser(window):window==fixture&&OwnedBy(window,fixtureProcess));
    bool InMonitor(Point point) => point.x>=monitor.left&&point.x<monitor.right&&point.y>=monitor.top&&point.y<monitor.bottom;
    int[] BrowserStats()
    {
        Require(OwnsBrowser(browser),"Test browser ownership lost");
        string[] parts=WindowTitle(browser).Split('|');Require(parts.Length>=8,"Browser geometry missing");
        var values=new int[6];for(int i=0;i<values.Length;i++)Require(int.TryParse(parts[i+1],out values[i]),"Invalid browser geometry");
        Require(values[2]>=0&&values[3]>=0,"Invalid browser counters");
        Require(GetClientRect(browser,out Bounds bounds),"Browser client area missing");
        var origin=new Point(bounds.left,bounds.top);var end=new Point(bounds.right,bounds.bottom);
        Require(ClientToScreen(browser,ref origin)&&ClientToScreen(browser,ref end),"Browser client geometry unavailable");
        foreach(var point in new[]{new Point(values[0],values[1]),new Point(values[4],values[5])})
            Require(InMonitor(point)&&point.x>=origin.x&&point.x<end.x&&point.y>=origin.y&&point.y<end.y,"Browser geometry outside owned client area");
        return values;
    }
    void CloseBrowser()
    {
        if(OwnsBrowser(browser))PostMessage(browser,0x10,IntPtr.Zero,IntPtr.Zero);
        browser=IntPtr.Zero;browserProcess?.Dispose();browserProcess=null;
    }
    static IEnumerator Wait(Func<bool> condition,string reason,float seconds=8){double until=Time.realtimeSinceStartupAsDouble+seconds;while(!condition()){Require(Time.realtimeSinceStartupAsDouble<until,"Timed out: "+reason);yield return null;}}
    Color ReadPixel(Point p)
    {
        Require(InMonitor(p)&&p.x-monitor.left<capture.Info.width&&p.y-monitor.top<capture.Info.height,"Readback point outside capture");
        Require(capture.DesktopTexture!=null,"No capture texture");var previous=RenderTexture.active;var rt=RenderTexture.GetTemporary(capture.Info.width,capture.Info.height,0);var pixel=new Texture2D(1,1,TextureFormat.RGB24,false);
        try{Graphics.Blit(capture.DesktopTexture,rt);RenderTexture.active=rt;pixel.ReadPixels(new Rect(p.x-monitor.left,p.y-monitor.top,1,1),0,0);pixel.Apply();return pixel.GetPixel(0,0);}
        finally{RenderTexture.active=previous;RenderTexture.ReleaseTemporary(rt);Destroy(pixel);}
    }
    static void VerifySavedScreenshot(string path)
    {
        Require(!string.IsNullOrEmpty(path)&&File.Exists(path),"Screenshot file missing");
        byte[] bytes=File.ReadAllBytes(path);
        Require(bytes.Length>8&&bytes[0]==137&&bytes[1]==80&&bytes[2]==78&&bytes[3]==71,"Screenshot is not a PNG");
        var texture=new Texture2D(2,2,TextureFormat.RGBA32,false);
        try
        {
            Require(UnityEngine.ImageConversion.LoadImage(texture,bytes,false),"Screenshot PNG could not be decoded");
            Require(texture.width==Screen.width&&texture.height==Screen.height,$"Screenshot size {texture.width}x{texture.height} != {Screen.width}x{Screen.height}");
        }
        finally { UnityEngine.Object.Destroy(texture); }
    }
    void OnDestroy()
    {
        if(fixtureProcess!=null){if(fixture!=IntPtr.Zero)PostMessage(fixture,0x10,IntPtr.Zero,IntPtr.Zero);fixtureProcess.Dispose();fixtureProcess=null;}
        CloseBrowser();
    }
    static void Require(bool value,string message){if(!value)throw new Exception(message);}
}
