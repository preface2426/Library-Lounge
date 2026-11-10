#if UNITY_EDITOR || DEVELOPMENT_BUILD || NETCAFE_ACCEPTANCE
using System;
using System.Collections;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using UnityEngine;
using Debug = UnityEngine.Debug;

// Opt-in acceptance: closes only the fixture launched by this component.
public sealed class CafeDesktopCloseSmoke : MonoBehaviour
{
    [StructLayout(LayoutKind.Sequential)] struct RawDevice
    { public ushort page, usage; public uint flags; public IntPtr target; }
    delegate bool WindowCallback(IntPtr window, IntPtr value);
    delegate IntPtr MouseHook(int code, IntPtr message, IntPtr data);
    [StructLayout(LayoutKind.Sequential)] struct Point { public int x,y; }
    [StructLayout(LayoutKind.Sequential)] struct HookMouse
    { public Point position; public uint mouseData, flags, time; public UIntPtr tag; }
    const uint MotionTag=0x4E43574D;
    [DllImport("user32.dll")] static extern bool EnumWindows(WindowCallback callback, IntPtr value);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr window, out uint pid);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] static extern int GetClassName(IntPtr window, StringBuilder name, int count);
    [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr window);
    [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr window, int command);
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] static extern bool PostMessage(IntPtr window, uint message, IntPtr a, IntPtr b);
    [DllImport("user32.dll")] static extern uint GetRegisteredRawInputDevices([Out] RawDevice[] devices, ref uint count, uint size);
    [DllImport("user32.dll")] static extern bool RegisterRawInputDevices(RawDevice[] devices, uint count, uint size);
    [DllImport("user32.dll")] static extern bool GetCursorPos(out Point position);
    [DllImport("user32.dll")] static extern bool SetCursorPos(int x,int y);
    [DllImport("user32.dll")] static extern bool ClientToScreen(IntPtr window,ref Point position);
    [DllImport("user32.dll")] static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);
    [DllImport("user32.dll")] static extern void mouse_event(uint flags,int x,int y,uint data,UIntPtr tag);
    [DllImport("user32.dll")] static extern IntPtr SetWindowsHookExW(int id,MouseHook hook,IntPtr module,uint thread);
    [DllImport("user32.dll")] static extern IntPtr CallNextHookEx(IntPtr hook,int code,IntPtr message,IntPtr data);
    [DllImport("user32.dll")] static extern bool UnhookWindowsHookEx(IntPtr hook);
    [DllImport("NetCafeCapture")] static extern void NC_ToolbarPointer(out int x,out int y,out int held);
    static string Argument(string prefix)
    { foreach(string arg in Environment.GetCommandLineArgs()) if(arg.StartsWith(prefix, StringComparison.Ordinal))return arg.Substring(prefix.Length); return ""; }
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Install()
    {
        if (Array.IndexOf(Environment.GetCommandLineArgs(), "-desktopCloseSmoke") >= 0)
            new GameObject("DesktopCloseAcceptance").AddComponent<CafeDesktopCloseSmoke>();
    }
    static IntPtr OwnedWindow(uint owner, string expectedClass)
    {
        IntPtr found = IntPtr.Zero;
        EnumWindows((window, _) => {
            GetWindowThreadProcessId(window, out uint pid);
            var name = new StringBuilder(100); GetClassName(window, name, name.Capacity);
            if (pid != owner || name.ToString() != expectedClass) return true;
            found = window; return false;
        }, IntPtr.Zero);
        return found;
    }
    static RawDevice MouseRegistration()
    {
        uint count=0, size=(uint)Marshal.SizeOf<RawDevice>();
        GetRegisteredRawInputDevices(null, ref count, size);
        var devices=new RawDevice[count];
        if(GetRegisteredRawInputDevices(devices, ref count, size)==uint.MaxValue)throw new Exception("Raw query failed");
        foreach(var device in devices)if(device.page==1&&device.usage==2)return device;
        return default;
    }
    static string ClassName(IntPtr window)
    { var name=new StringBuilder(100); GetClassName(window,name,name.Capacity);return name.ToString(); }
    IEnumerator Wait(Func<bool> predicate, string stage)
    {
        float deadline=Time.realtimeSinceStartup+12;
        while(!predicate()&&Time.realtimeSinceStartup<deadline)yield return null;
        if(!predicate())throw new Exception("Timed out: "+stage);
    }
    CafeDesktopInput input;
    Process fixture;
    IntPtr fixtureWindow;
    IEnumerator Start()
    {
        var stack=new System.Collections.Generic.Stack<IEnumerator>(); stack.Push(Run());
        Exception failure=null;
        while(stack.Count>0)
        {
            var step=stack.Peek(); bool more=false;
            try { more=step.MoveNext(); } catch(Exception ex) { failure=ex; }
            if(failure!=null)break;
            if(!more){(stack.Pop() as IDisposable)?.Dispose();continue;}
            if(step.Current is IEnumerator nested)stack.Push(nested);else yield return step.Current;
        }
        while(stack.Count>0)(stack.Pop() as IDisposable)?.Dispose();
        input?.EndControl();
        if(fixtureWindow!=IntPtr.Zero&&fixture!=null&&!fixture.HasExited&&
            OwnedWindow((uint)fixture.Id,"NetCafeA2ExternalFixture")==fixtureWindow)
            PostMessage(fixtureWindow,0x10,IntPtr.Zero,IntPtr.Zero);
        fixture?.Dispose();
        if(failure!=null) Debug.LogError("DESKTOP_CLOSE_SMOKE_FAIL "+failure);
        else Debug.Log("DESKTOP_CLOSE_SMOKE_PASS: owned app close, foreground return, native raw mouse ownership, menu release");
        Application.Quit(failure==null?0:1);
    }
    IEnumerator Run()
    {
        while(!UnityEngine.Rendering.SplashScreen.isFinished)yield return null;
        yield return new WaitForSecondsRealtime(1);
        var cafe=FindFirstObjectByType<CafePrototype>(); input=cafe.GetComponent<CafeDesktopInput>();
        var game=OwnedWindow((uint)Process.GetCurrentProcess().Id,"UnityWndClass");
        if(game==IntPtr.Zero)throw new Exception("Owned game HWND missing");
        Screen.SetResolution(1280,800,FullScreenMode.Windowed);
        ShowWindow(game,9); SetForegroundWindow(game);
        yield return Wait(()=>Application.isFocused,"owned game focus");
        fixture=Process.Start(new ProcessStartInfo(Argument("-desktopCloseFixture="),"--fixture") { UseShellExecute=false });
        yield return Wait(()=>{fixtureWindow=OwnedWindow((uint)fixture.Id,"NetCafeA2ExternalFixture");return fixtureWindow!=IntPtr.Zero;},"owned fixture startup");
        SetForegroundWindow(game); yield return Wait(()=>Application.isFocused,"pre-seat focus");
        if(Argument("-desktopCloseMotion=")=="1")
        {
            var center=new Point { x=Screen.width/2,y=Screen.height/2 };
            ClientToScreen(game,ref center);
            if(!SetCursorPos(center.x,center.y))throw new Exception("Cannot center mouse on owned game");
        }
        cafe.Sit(0); yield return Wait(()=>input.Controlling,"native desktop input: "+input.Error);
        SetForegroundWindow(fixtureWindow);
        yield return Wait(()=>GetForegroundWindow()==fixtureWindow,"fixture foreground");
        Debug.Log("DESKTOP_CLOSE_BEFORE raw="+ClassName(MouseRegistration().target)+" "+input.Diagnostic());
        PostMessage(fixtureWindow,0x10,IntPtr.Zero,IntPtr.Zero);
        yield return Wait(()=>fixture.HasExited,"fixture closed");
        fixtureWindow=IntPtr.Zero;
        yield return new WaitForSecondsRealtime(.25f);
        Debug.Log("DESKTOP_CLOSE_NATURAL focused="+Application.isFocused+" raw="+ClassName(MouseRegistration().target)+" "+input.Diagnostic());
        // Exercise the focus return which can follow closing the last external app.
        SetForegroundWindow(game); yield return Wait(()=>Application.isFocused,"foreground return after close");
        yield return new WaitForSecondsRealtime(.25f);
        Debug.Log("DESKTOP_CLOSE_FOCUSED raw="+ClassName(MouseRegistration().target)+" "+input.Diagnostic());
        if(Argument("-desktopCloseMotion=")=="1")
        {
            yield return CheckWindowsMotion(game,"natural",false,12,7);
            yield return CheckWindowsMotion(game,"raw-taken-over",true,-10,-6);
            yield return CheckWindowsMotion(game,"continued",false,9,-5);
        }
        string takeover=Argument("-desktopCloseTakeover=");
        if(takeover=="receiver-close")
        {
            var receiver=MouseRegistration().target;
            GetWindowThreadProcessId(receiver,out uint owner);
            if(owner!=(uint)Process.GetCurrentProcess().Id||ClassName(receiver)!="NetCafeInputPump")
                throw new Exception("Refusing to close an unowned native receiver");
            PostMessage(receiver,0x10,IntPtr.Zero,IntPtr.Zero);
            yield return Wait(()=>!input.Controlling&&cafe.Mode==CafePrototype.InputMode.Menu,"receiver failure release");
            if(Cursor.lockState!=CursorLockMode.None||!Cursor.visible||input.Error.Length==0)
                throw new Exception("Receiver failure did not restore menu cursor and report error");
            Debug.Log("DESKTOP_CLOSE_RECEIVER_FAILURE_PASS: released control and visible menu cursor");
            yield break;
        }
        uint takeoverFlags=takeover=="exit"?0x100u:0u;
        if(takeover=="1"||takeover=="exit")
        {
            var unityMouse=new RawDevice {page=1,usage=2,flags=takeoverFlags,target=game};
            if(!RegisterRawInputDevices(new[]{unityMouse},1,(uint)Marshal.SizeOf<RawDevice>()))throw new Exception("Raw takeover setup failed");
            if(takeover!="exit")yield return new WaitForSecondsRealtime(.25f);
        }
        var actual=MouseRegistration();
        if(!input.Controlling||takeover!="exit"&&(ClassName(actual.target)!="NetCafeInputPump"||(actual.flags&0x100)==0))
            throw new Exception("NATIVE_MOUSE_OWNER_LOST target="+ClassName(actual.target)+" flags="+actual.flags.ToString("X")+" "+input.Diagnostic());
        cafe.OpenMenu(); yield return null;
        if(input.Controlling||Cursor.lockState!=CursorLockMode.None||!Cursor.visible)
            throw new Exception("Menu did not release desktop input");
        if(takeover=="1"||takeover=="exit")
        {
            var restored=MouseRegistration();
            // Unity may register again after End restores its HWND styles;
            // the native-only fixture checks exact flag preservation.
            if(restored.target!=game||takeover!="exit"&&restored.flags!=takeoverFlags)
                throw new Exception("Latest Unity mouse registration was not restored on exit");
            Debug.Log("DESKTOP_CLOSE_RESTORE_PASS: latest Unity mouse registration restored");
        }
    }

    IEnumerator CheckWindowsMotion(IntPtr game,string stage,bool takeOver,int x,int y)
    {
        Vector2 expected=Vector2.zero,actual=Vector2.zero;
        IntPtr receiverAtDelivery=IntPtr.Zero;
        bool seen=false;
        Exception callbackError=null;
        MouseHook observe=(code,message,data)=>{
            if(code>=0&&message.ToInt64()==0x200)
            {
                var move=Marshal.PtrToStructure<HookMouse>(data);
                // The acceptance harness owns the active game. Isolate the
                // one sampled packet from simultaneous real mouse movement
                // while allowing queued replay (injected) to drain.
                if((move.flags&1)==0)return new IntPtr(1);
                if(move.tag.ToUInt64()==MotionTag)
                {
                    int beforeX=0,beforeY=0;
                    try
                    {
                        var old=SetThreadDpiAwarenessContext(new IntPtr(-4));
                        try
                        {
                            if(GetCursorPos(out Point origin))
                                expected=new Vector2(move.position.x-origin.x,move.position.y-origin.y);
                        }
                        finally { if(old!=IntPtr.Zero)SetThreadDpiAwarenessContext(old); }
                        receiverAtDelivery=MouseRegistration().target;
                        NC_ToolbarPointer(out beforeX,out beforeY,out _);
                    }
                    catch(Exception error) { callbackError=error; }
                    var result=CallNextHookEx(IntPtr.Zero,code,message,data);
                    try
                    {
                        NC_ToolbarPointer(out int afterX,out int afterY,out _);
                        actual=new Vector2(afterX-beforeX,afterY-beforeY);
                    }
                    catch(Exception error) { callbackError=error; }
                    seen=true;
                    return result;
                }
            }
            return CallNextHookEx(IntPtr.Zero,code,message,data);
        };
        var hook=SetWindowsHookExW(14,observe,IntPtr.Zero,0);
        if(hook==IntPtr.Zero)throw new Exception("Cannot observe the test mouse packet");
        try
        {
            yield return new WaitForSecondsRealtime(.18f);
            if(takeOver)
            {
                var device=new RawDevice {page=1,usage=2,flags=0x100,target=game};
                if(!RegisterRawInputDevices(new[]{device},1,(uint)Marshal.SizeOf<RawDevice>())||MouseRegistration().target!=game)
                    throw new Exception("Cannot simulate interrupted raw input");
            }
            mouse_event(1,x,y,0,new UIntPtr(MotionTag));
            yield return Wait(()=>seen,"Windows processed movement "+stage);
            if(callbackError!=null)throw new Exception("Cannot measure processed movement",callbackError);
            if(!input.Controlling||expected==Vector2.zero||actual!=expected||takeOver&&receiverAtDelivery!=game)
                throw new Exception($"WINDOWS_MOTION_MISMATCH {stage} expected={expected} actual={actual} {input.Diagnostic()}");
            Debug.Log($"DESKTOP_CLOSE_WINDOWS_MOTION_PASS stage={stage} expected={expected} actual={actual}");
        }
        finally { UnhookWindowsHookEx(hook); GC.KeepAlive(observe); }
        yield return new WaitForSecondsRealtime(.16f);
    }
}
#endif
