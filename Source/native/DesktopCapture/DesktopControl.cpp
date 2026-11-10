#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <windows.h>
#include <windowsx.h>
#include <algorithm>
#include <atomic>
#include <mutex>
#include <thread>
#include <future>
#include <vector>
#include <cstdio>
#include <array>
#include <magnification.h>
#include <condition_variable>
#include <deque>
#include <chrono>
#include "MouseEdgeMotion.h"
#define API extern "C" __declspec(dllexport)
extern "C" int __cdecl NC_MonitorRect(int index, RECT* rect);

// OS input only: no capture or room-state changes occur here.
static HWND client, receiver, lastTarget;
static LONG_PTR originalExStyle;
static HHOOK mouseHook, keyHook;
static std::thread worker, sender, safetyThread;
static std::atomic<DWORD> safetyId{0};
static std::atomic<ULONGLONG> unityHeartbeat{0};
static constexpr int EmergencyHotkeyId = 0x4E5A;
static std::atomic_int safetyKey{0};
static constexpr ULONGLONG ControlStallTimeoutMs = 15000;
struct MouseBatch { INPUT events[2]; UINT count; };
struct ReplayRequest { POINT point; DWORD flags,data; };
// The hook and its replay messages run on the same input worker.
static std::deque<ReplayRequest> clickRequests;
static std::mutex sendGate;
static std::condition_variable sendReady;
static std::deque<MouseBatch> sendQueue;
static bool senderStopped;
static DWORD workerId;
static std::atomic_bool active, held, yielding;
static std::atomic_int actions, lastError, injectedPending;
static std::mutex gate;
static RECT source{}, viewport{}, toolbar{}, bounds{};
static POINT pointer{};
static POINT requested{},lastSource{};
static bool collapsed, toolbarClick, rawRegistered, savedRawValid;
static std::atomic_bool movePending;
static std::atomic_bool testDriving;
static RAWINPUTDEVICE savedRaw{};
static UINT escapeKey = VK_F8, escapeModifiers;
static WORD pressed;
static HWND resumeTarget;
static bool focusReturnPending=false;
static ULONGLONG focusReturnStarted=0;
static std::array<DWORD,256> pressedKeys{};
static constexpr ULONG_PTR replayTag = 0x4E434152, testTag = 0x4E434154, windowsMotionTestTag = 0x4E43574D;
static std::atomic_bool cursorHidden{false};
static ULONGLONG lastMove;
static ULONGLONG rawMouseCheckAt;
static int rawMouseRecoveries;
static MouseEdgeMotion edgeX,edgeY;
static ULONGLONG absoluteMouseAt;

static bool ReadRawMouseRegistration(RAWINPUTDEVICE& registration,bool& found) {
    found=false;
    UINT count=0;
    if(GetRegisteredRawInputDevices(nullptr,&count,sizeof(RAWINPUTDEVICE))==UINT(-1)){
        DWORD error=GetLastError();
        if(error!=ERROR_INSUFFICIENT_BUFFER){lastError=error;return false;}
    }
    std::vector<RAWINPUTDEVICE> devices(count);
    if(count&&GetRegisteredRawInputDevices(devices.data(),&count,sizeof(RAWINPUTDEVICE))==UINT(-1)){
        lastError=GetLastError();return false;
    }
    for(auto const& device:devices)if(device.usUsagePage==1&&device.usUsage==2){
        registration=device;found=true;break;
    }
    return true;
}
static bool EnsureRawMouseReceiver() {
    if(!IsWindow(receiver)){lastError=ERROR_INVALID_WINDOW_HANDLE;return false;}
    RAWINPUTDEVICE current{};bool found=false;
    if(!ReadRawMouseRegistration(current,found))return false;
    if(found&&current.hwndTarget==receiver&&(current.dwFlags&RIDEV_INPUTSINK))return true;
    // Unity can register again when its HWND regains focus. Restore its
    // latest registration on exit, rather than the stale entry snapshot.
    if(found&&current.hwndTarget!=receiver){savedRaw=current;savedRawValid=true;}
    else if(!found)savedRawValid=false;
    RAWINPUTDEVICE mouse{1,2,RIDEV_INPUTSINK,receiver};
    if(!RegisterRawInputDevices(&mouse,1,sizeof(mouse))){
        lastError=GetLastError();return false;
    }
    ++rawMouseRecoveries;actions|=512;
    return true;
}

static constexpr WORD ButtonState(WORD state,DWORD flags,DWORD data) {
    WORD button=0;
    if(flags&(MOUSEEVENTF_LEFTDOWN|MOUSEEVENTF_LEFTUP))button=MK_LBUTTON;
    else if(flags&(MOUSEEVENTF_RIGHTDOWN|MOUSEEVENTF_RIGHTUP))button=MK_RBUTTON;
    else if(flags&(MOUSEEVENTF_MIDDLEDOWN|MOUSEEVENTF_MIDDLEUP))button=MK_MBUTTON;
    else if(flags&(MOUSEEVENTF_XDOWN|MOUSEEVENTF_XUP))button=data==XBUTTON1?MK_XBUTTON1:data==XBUTTON2?MK_XBUTTON2:0;
    if(flags&(MOUSEEVENTF_LEFTUP|MOUSEEVENTF_RIGHTUP|MOUSEEVENTF_MIDDLEUP|MOUSEEVENTF_XUP))return state&~button;
    return state|button;
}
static_assert(ButtonState(0,MOUSEEVENTF_LEFTDOWN,0)==MK_LBUTTON);
static_assert(ButtonState(MK_LBUTTON|MK_XBUTTON1,MOUSEEVENTF_LEFTUP,0)==MK_XBUTTON1);
static_assert(ButtonState(MK_XBUTTON1|MK_XBUTTON2,MOUSEEVENTF_XUP,XBUTTON2)==MK_XBUTTON1);

static POINT MapPoint(POINT p) {
    LONG width=std::max(2L,viewport.right-viewport.left),height=std::max(2L,viewport.bottom-viewport.top);
    return {std::clamp(source.left+MulDiv(p.x-viewport.left,source.right-source.left-1,width-1),source.left,source.right-1),
        std::clamp(source.top+MulDiv(p.y-viewport.top,source.bottom-source.top-1,height-1),source.top,source.bottom-1)};
}
static INPUT Mouse(DWORD flags,POINT p,DWORD data=0) {
    INPUT value{};value.type=INPUT_MOUSE;value.mi.dwFlags=flags;value.mi.mouseData=data;value.mi.dwExtraInfo=replayTag;
    if(flags&MOUSEEVENTF_MOVE){
        value.mi.dwFlags|=MOUSEEVENTF_ABSOLUTE|MOUSEEVENTF_VIRTUALDESK;
        value.mi.dx=MulDiv(p.x-GetSystemMetrics(SM_XVIRTUALSCREEN),65535,std::max(1,GetSystemMetrics(SM_CXVIRTUALSCREEN)-1));
        value.mi.dy=MulDiv(p.y-GetSystemMetrics(SM_YVIRTUALSCREEN),65535,std::max(1,GetSystemMetrics(SM_CYVIRTUALSCREEN)-1));
    }
    return value;
}
static bool QueueReplay(POINT p,DWORD flags=0,DWORD data=0) {
    if(yielding)return false;
    {std::lock_guard lock(gate);lastSource=p;}
    lastTarget=WindowFromPoint(p);
    MouseBatch batch{{Mouse(MOUSEEVENTF_MOVE|MOUSEEVENTF_MOVE_NOCOALESCE,p),Mouse(flags,p,data)},flags?2u:1u};
    {
        std::lock_guard lock(sendGate);
        if(senderStopped)return false;
        // Replace stale hover positions, but never reorder across a button or wheel event.
        if(!flags&&!sendQueue.empty()&&sendQueue.back().count==1)sendQueue.back()=batch;
        else sendQueue.push_back(batch);
    }
    sendReady.notify_one();return true;
}
static bool ReplayMovement() {
    POINT p;{std::lock_guard lock(gate);p=MapPoint(pointer);}
    return QueueReplay(p);
}
static int ToolbarAction(POINT p) {
    if(!PtInRect(&toolbar,p))return 0;
    if(collapsed)return 32;
    int cell=static_cast<int>((p.x-toolbar.left)*5/std::max(1L,toolbar.right-toolbar.left));
    const int mapping[]{16,64,128,2,1};return mapping[std::clamp(cell,0,4)];
}
static void PointerMoved(LONG dx,LONG dy) {
    if(!dx&&!dy)return;
    std::lock_guard lock(gate);
    pointer.x=std::clamp(pointer.x+dx,bounds.left,bounds.right-1);
    pointer.y=std::clamp(pointer.y+dy,bounds.top,bounds.bottom-1);
    if(!movePending.exchange(true))PostMessageW(receiver,WM_NULL,0,0);
}
static bool ApplyWindowsMovement(POINT proposed,bool taggedTest) {
    // The hook coordinates are per-monitor aware, but USER32 can invoke the
    // callback in the DPI context of the foreground window. Sample its origin
    // in the same units. Windows has not committed this event yet.
    auto previous=SetThreadDpiAwarenessContext(DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2);
    POINT origin{};
    const bool ok=GetCursorPos(&origin)!=0;
    if(previous)SetThreadDpiAwarenessContext(previous);
    if(ok){
        const LONG dx=proposed.x-origin.x,dy=proposed.y-origin.y;
        if(dx)edgeX.Reset();
        if(dy)edgeY.Reset();
        // True absolute HID devices still own their normalized coordinates.
        // If those reports stop (e.g. a registration change), the hook resumes.
        if(taggedTest||!absoluteMouseAt||GetTickCount64()-absoluteMouseAt>32)
            PointerMoved(dx,dy);
    }
    return ok;
}
static void YieldInput(DWORD error) {
    lastError=error;
    yielding=true;
    actions|=8;
    if(receiver)PostMessageW(receiver,WM_APP+4,0,0);
}
static LRESULT CALLBACK MouseProc(int code,WPARAM message,LPARAM data) {
    if(code<0||!active||yielding)return CallNextHookEx(nullptr,code,message,data);
    auto& event=*reinterpret_cast<MSLLHOOKSTRUCT*>(data);
    if(event.dwExtraInfo==replayTag){
        injectedPending.fetch_sub(1);
        if(message==WM_MOUSEMOVE){edgeX.AnchorChanged(event.pt.x);edgeY.AnchorChanged(event.pt.y);}
        return CallNextHookEx(nullptr,code,message,data);
    }
    if(message==WM_MOUSEMOVE){
        if(!testDriving){
            if(!(event.flags&LLMHF_INJECTED)||event.dwExtraInfo==windowsMotionTestTag){
                // Use the Windows-calculated delta (speed and acceleration).
                // Unlike WM_INPUT it still arrives during a raw registration change.
                if(!ApplyWindowsMovement(event.pt,event.dwExtraInfo==windowsMotionTestTag)){
                    YieldInput(ERROR_NOT_READY);
                    return CallNextHookEx(nullptr,code,message,data);
                }
            }else{
                // Keep the existing absolute positioning path for external injection.
                std::lock_guard lock(gate);pointer=event.pt;
                if(!movePending.exchange(true))PostMessageW(receiver,WM_NULL,0,0);
            }
        }
        return 1;
    }
    if(message==WM_LBUTTONUP&&toolbarClick){toolbarClick=false;return 1;}
    if(message==WM_LBUTTONUP&&held){held=false;return 1;}
    POINT p,mapped;int action=0;bool inView,inToolbar;
    {std::lock_guard lock(gate);p=pointer;mapped=MapPoint(p);inView=PtInRect(&viewport,p)!=0;inToolbar=PtInRect(&toolbar,p)!=0;if(message==WM_LBUTTONDOWN)action=ToolbarAction(p);}
    if(action){if(action==16)held=true;else{toolbarClick=true;actions|=action;}return 1;}
    if(held||(inToolbar&&!pressed))return 1;
    if(!inView&&!pressed){if(message==WM_LBUTTONDOWN)actions|=64;return 1;}
    DWORD flags=0,value=0;WORD button=0;
    switch(message){
        case WM_LBUTTONDOWN:flags=MOUSEEVENTF_LEFTDOWN;button=MK_LBUTTON;break;
        case WM_LBUTTONUP:flags=MOUSEEVENTF_LEFTUP;button=MK_LBUTTON;break;
        case WM_RBUTTONDOWN:flags=MOUSEEVENTF_RIGHTDOWN;button=MK_RBUTTON;break;
        case WM_RBUTTONUP:flags=MOUSEEVENTF_RIGHTUP;button=MK_RBUTTON;break;
        case WM_MBUTTONDOWN:flags=MOUSEEVENTF_MIDDLEDOWN;button=MK_MBUTTON;break;
        case WM_MBUTTONUP:flags=MOUSEEVENTF_MIDDLEUP;button=MK_MBUTTON;break;
        case WM_MOUSEWHEEL:flags=MOUSEEVENTF_WHEEL;value=static_cast<DWORD>(static_cast<short>(HIWORD(event.mouseData)));break;
        case WM_MOUSEHWHEEL:flags=MOUSEEVENTF_HWHEEL;value=static_cast<DWORD>(static_cast<short>(HIWORD(event.mouseData)));break;
        case WM_XBUTTONDOWN:case WM_XBUTTONUP:flags=message==WM_XBUTTONDOWN?MOUSEEVENTF_XDOWN:MOUSEEVENTF_XUP;value=HIWORD(event.mouseData);button=value==XBUTTON1?MK_XBUTTON1:MK_XBUTTON2;break;
    }
    if(button){if(message==WM_LBUTTONUP||message==WM_RBUTTONUP||message==WM_MBUTTONUP||message==WM_XBUTTONUP)pressed&=~button;else pressed|=button;}
    // Return from the low-level hook before any replay or interaction with other windows.
    if(flags){
        clickRequests.push_back({mapped,flags,value});
        if(!PostMessageW(receiver,WM_APP+2,0,0))YieldInput(GetLastError());
    }
    return 1;
}
static LRESULT CALLBACK KeyProc(int code,WPARAM message,LPARAM data) {
    if(code<0||!active||yielding)return CallNextHookEx(nullptr,code,message,data);
    auto& key=*reinterpret_cast<KBDLLHOOKSTRUCT*>(data);
    bool down=message==WM_KEYDOWN||message==WM_SYSKEYDOWN;
    if(key.vkCode<256)pressedKeys[key.vkCode]=down?1|((key.flags&LLKHF_EXTENDED)?2:0):0;
    if(down&&(key.vkCode==VK_LWIN||key.vkCode==VK_RWIN||
        ((key.flags&LLKHF_ALTDOWN)&&(key.vkCode==VK_TAB||key.vkCode==VK_ESCAPE))||
        ((GetAsyncKeyState(VK_CONTROL)&0x8000)&&key.vkCode==VK_ESCAPE))){
        yielding=true;actions|=256;
        // Never call Magnification APIs from a low-level keyboard hook:
        // they can synchronously enter the OS while the hook callback blocks
        // normal input. Wake our dedicated input pump instead (no Unity frame
        // is needed to release the mouse).
        if(receiver)PostMessageW(receiver,WM_APP+4,0,0);
    }
    return CallNextHookEx(nullptr,code,message,data);
}
// When we mirror a real Windows desktop behind a click-through Unity HWND,
// real native popups can be created ABOVE Unity's topmost window. They are
// already visible inside the GPU texture, so a second physical copy appears
// on top of the game. Modern menus are not restricted to class #32768:
// WinUI/Chromium and IME menus may use independent top-level HWND classes.
// Keep Unity at the front of the TOPMOST band, WITHOUT activating it or
// modifying/clicking the source app, only while desktop control owns input.
// Once control yields (Win key/Alt-Tab/F8/exit) End() restores normal z-order.
static void KeepMirroredDesktopBehindGame()
{
    // ONLY the Unity UI thread may reorder its own HWND. The old native
    // WM_TIMER worker used SetWindowPos on Unity's HWND every 16 ms; when
    // Unity called End() and joined that worker, Windows could synchronously
    // wait on the Unity thread for a window message -> deadlock.
    static ULONGLONG lastCheck=0;
    if (!active || yielding || !IsWindow(client) || !IsWindowVisible(client)) return;
    ULONGLONG now=GetTickCount64();
    if (now-lastCheck<80) return;
    lastCheck=now;
    if (GetWindow(client, GW_HWNDPREV) != nullptr)
        SetWindowPos(client, HWND_TOPMOST, 0, 0, 0, 0,
                     SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_SHOWWINDOW);
}
static LRESULT CALLBACK InputProc(HWND w,UINT message,WPARAM a,LPARAM b) {
    if(message==WM_SETCURSOR){SetCursor(nullptr);return TRUE;}
    if(message==WM_MOUSEACTIVATE)return MA_NOACTIVATE;
    if(message==WM_APP+2){
        if(!clickRequests.empty()){
            const auto click=clickRequests.front();clickRequests.pop_front();
            if(active)QueueReplay(click.point,click.flags,click.data);
        }
        return 0;
    }
    if(message==WM_APP+4){if(cursorHidden.exchange(false))MagShowSystemCursor(TRUE);return 0;}
    if(message==WM_DESTROY&&active){
        lastError=ERROR_INVALID_WINDOW_HANDLE;yielding=true;actions|=8;
        if(cursorHidden.exchange(false))MagShowSystemCursor(TRUE);
        return 0;
    }
    if(message==WM_INPUT&&active&&!yielding&&!testDriving){
        RAWINPUT packet{};UINT size=sizeof(packet);
        // The Windows pointer stops at the monitor edge. Continue only along
        // blocked axes so the in-game pointer can reach outside its viewport
        // (e.g. the toolbar), without applying any ordinary movement twice.
        if(GetRawInputData(reinterpret_cast<HRAWINPUT>(b),RID_INPUT,&packet,&size,sizeof(RAWINPUTHEADER))!=static_cast<UINT>(-1)&&packet.header.dwType==RIM_TYPEMOUSE&&
            packet.data.mouse.ulExtraInformation!=replayTag&&packet.data.mouse.ulExtraInformation!=testTag&&
            packet.data.mouse.ulExtraInformation!=windowsMotionTestTag){
            auto& m=packet.data.mouse;
            if(m.usFlags&MOUSE_MOVE_ABSOLUTE){
                absoluteMouseAt=GetTickCount64();
                edgeX.Reset();edgeY.Reset();
                std::lock_guard lock(gate);
                pointer={bounds.left+MulDiv(std::clamp(m.lLastX,0L,65535L),bounds.right-bounds.left-1,65535),
                         bounds.top+MulDiv(std::clamp(m.lLastY,0L,65535L),bounds.bottom-bounds.top-1,65535)};
                movePending=true;
            }else if(m.lLastX||m.lLastY){
                absoluteMouseAt=0;
                const DWORD time=static_cast<DWORD>(GetMessageTime());
                POINT origin{};
                if(GetCursorPos(&origin)){
                    RECT clip{};
                    const bool hasClip=GetClipCursor(&clip)!=0;
                    auto blocked=[&](POINT next){
                        return (hasClip&&!PtInRect(&clip,next))||!MonitorFromPoint(next,MONITOR_DEFAULTTONULL);
                    };
                    const POINT nextX{origin.x+(m.lLastX>0?1:-1),origin.y};
                    const POINT nextY{origin.x,origin.y+(m.lLastY>0?1:-1)};
                    PointerMoved(
                        edgeX.Outward(origin.x,m.lLastX,m.lLastX&&blocked(nextX),time),
                        edgeY.Outward(origin.y,m.lLastY,m.lLastY&&blocked(nextY),time));
                }
            }
        }
    }
    return DefWindowProcW(w,message,a,b);
}
static void End(bool activate=true) {
    HWND foreground=GetForegroundWindow();DWORD pid=0;GetWindowThreadProcessId(foreground,&pid);
    if(pid&&pid!=GetCurrentProcessId())resumeTarget=foreground;
    bool restore=active.exchange(false);
    if(worker.joinable()){PostThreadMessageW(workerId,WM_QUIT,0,0);worker.join();}
    if(!restore)return;
    if(IsWindow(client)){
        SetWindowLongPtrW(client,GWL_EXSTYLE,originalExStyle);
        // On Win/Alt+Tab yield, do not bury Unity at HWND_BOTTOM: it can
        // become unreachable behind every genuine Windows window, appearing
        // frozen even after the OS mouse is released. Restore the pre-control
        // topmost state without stealing foreground focus.
        SetWindowPos(client,(originalExStyle&WS_EX_TOPMOST)?HWND_TOPMOST:HWND_NOTOPMOST,0,0,0,0,
            SWP_NOMOVE|SWP_NOSIZE|SWP_SHOWWINDOW|SWP_FRAMECHANGED|(activate?0:SWP_NOACTIVATE));
        POINT p;{std::lock_guard lock(gate);p=pointer;}
        if(activate)
        {
            SetCursorPos(p.x,p.y);
            BringWindowToTop(client);
            SetForegroundWindow(client);
            // Windows may refuse a foreground handoff while the genuine app
            // still owns the last input. Retry briefly on Unity's main loop,
            // never on the native worker that main may join.
            focusReturnPending=true;focusReturnStarted=GetTickCount64();
        }
        else focusReturnPending=false;
    }
}
// Independent, low-overhead safety hotkey/watchdog. When Unity is stalled,
 // its Update/OnDestroy and the F8 poll cannot be relied on to restore input.
 // Ctrl+Shift+F12 intentionally performs an EMERGENCY process exit without
 // saving the current in-memory room state. It is registered only while the
 // application is running, and unregistered on graceful shutdown.
static void EmergencyTerminate(DWORD code)
{
    if(active.load() && cursorHidden)
        MagShowSystemCursor(TRUE);
    // TerminateProcess avoids waiting for a frozen Unity main thread or an
    // input hook join; Windows releases hooks and HWNDs on process exit.
    TerminateProcess(GetCurrentProcess(), code);
}
API int __cdecl NC_SafetyStart()
{
    if(safetyThread.joinable()) return 1;
    std::promise<bool> ready;auto future=ready.get_future();
    unityHeartbeat.store(GetTickCount64());
    safetyThread=std::thread([&ready]{
        safetyId.store(GetCurrentThreadId());
        MSG m{};PeekMessageW(&m,nullptr,WM_USER,WM_USER,PM_NOREMOVE);
        // Multiple independent LibraryLounge clients can run on one Windows
        // machine during multiplayer QA. A global hotkey belongs to only one
        // process, so select the first available Fn variant per client.
        int selected=0;
        for(int key : {VK_F12,VK_F11,VK_F10,VK_F9})
            if(RegisterHotKey(nullptr,EmergencyHotkeyId,
                MOD_CONTROL|MOD_SHIFT|MOD_NOREPEAT,key)){selected=key;break;}
        safetyKey.store(selected);
        bool registered=selected!=0;
        ready.set_value(registered);
        bool running=true;
        while(running)
        {
            MsgWaitForMultipleObjectsEx(0,nullptr,250,QS_ALLINPUT,MWMO_INPUTAVAILABLE);
            while(PeekMessageW(&m,nullptr,0,0,PM_REMOVE))
            {
                if(m.message==WM_QUIT){running=false;break;}
                if(m.message==WM_HOTKEY && m.wParam==EmergencyHotkeyId)
                    EmergencyTerminate(0xE012);
            }
            // No automatic emergency quit outside native desktop control.
            // A paused debugger may intentionally halt Unity for longer.
            if(active.load() && !IsDebuggerPresent() &&
                GetTickCount64()-unityHeartbeat.load()>ControlStallTimeoutMs)
                EmergencyTerminate(0xE015);
        }
        if(registered)UnregisterHotKey(nullptr,EmergencyHotkeyId);
        safetyId.store(0);
        safetyKey.store(0);
    });
    bool ok=future.get();
    if(!ok)
    {
        PostThreadMessageW(safetyId.load(),WM_QUIT,0,0);
        safetyThread.join();
        lastError=ERROR_HOTKEY_ALREADY_REGISTERED;
        return 0;
    }
    return 1;
}
API int __cdecl NC_SafetyKey(){return safetyKey.load();}
API void __cdecl NC_SafetyStop()
{
    DWORD id=safetyId.load();
    if(id)PostThreadMessageW(id,WM_QUIT,0,0);
    if(safetyThread.joinable())safetyThread.join();
}
API int __cdecl NC_ControlHotkey(int key,int modifiers) {
    if(active||key<VK_F1||key>VK_F12||modifiers<0||(modifiers&~(MOD_CONTROL|MOD_ALT|MOD_SHIFT))){lastError=ERROR_INVALID_PARAMETER;return 0;}
    if(!RegisterHotKey(nullptr,0x4e43,modifiers|MOD_NOREPEAT,key)){lastError=GetLastError();return 0;}
    UnregisterHotKey(nullptr,0x4e43);escapeKey=key;escapeModifiers=modifiers;lastError=0;return 1;
}
API int __cdecl NC_ControlError(){return lastError;}
API void __cdecl NC_ControlView(int x,int y,int width,int height) {
    std::lock_guard lock(gate);POINT origin{};if(client)ClientToScreen(client,&origin);
    viewport={origin.x+x,origin.y+y,origin.x+x+std::max(2,width),origin.y+y+std::max(2,height)};
}
API void __cdecl NC_ControlToolbar(int x,int y,int width,int height,int isCollapsed) {
    std::lock_guard lock(gate);POINT origin{};if(client)ClientToScreen(client,&origin);
    toolbar={origin.x+x,origin.y+y,origin.x+x+width,origin.y+y+height};collapsed=isCollapsed!=0;
}
API void __cdecl NC_ToolbarPointer(int* x,int* y,int* isHeld) {
    POINT p;if(active){std::lock_guard lock(gate);p=pointer;}else GetCursorPos(&p);
    if(client)ScreenToClient(client,&p);*x=p.x;*y=p.y;*isHeld=held?1:0;
}
API int __cdecl NC_ControlBegin(int monitorIndex,int) {
    if(active)return 1;
    lastError=0;
    HWND previous=GetForegroundWindow();DWORD previousOwner=0;GetWindowThreadProcessId(previous,&previousOwner);
    if(previousOwner&&previousOwner!=GetCurrentProcessId())resumeTarget=previous;
    if(!NC_MonitorRect(monitorIndex,&source))return 0;
    client=nullptr;
    EnumWindows([](HWND w,LPARAM)->BOOL{DWORD pid;GetWindowThreadProcessId(w,&pid);wchar_t name[80]{};GetClassNameW(w,name,80);
        if(pid==GetCurrentProcessId()&&wcscmp(name,L"UnityWndClass")==0){client=w;return FALSE;}return TRUE;},0);
    if(!client)return 0;
    ClipCursor(nullptr);
    originalExStyle=GetWindowLongPtrW(client,GWL_EXSTYLE);
    // The virtual software pointer must start inside the Unity client area
    // even if the physical Windows cursor was on another monitor. Border and
    // resize shadows are not part of the client area.
    RECT clientRect{};GetClientRect(client,&clientRect);
    POINT clientOrigin{};ClientToScreen(client,&clientOrigin);
    bounds={clientOrigin.x,clientOrigin.y,clientOrigin.x+std::max(2L,clientRect.right),
        clientOrigin.y+std::max(2L,clientRect.bottom)};
    GetCursorPos(&pointer);
    pointer.x=std::clamp(pointer.x,bounds.left,bounds.right-1);
    pointer.y=std::clamp(pointer.y,bounds.top,bounds.bottom-1);
    actions=0;held=false;yielding=false;toolbarClick=false;pressed=0;pressedKeys.fill(0);injectedPending=0;testDriving=false;
    rawMouseCheckAt=0;rawMouseRecoveries=0;absoluteMouseAt=0;edgeX.Reset();edgeY.Reset();clickRequests.clear();
    SetWindowLongPtrW(client,GWL_EXSTYLE,originalExStyle|WS_EX_NOACTIVATE|WS_EX_LAYERED|WS_EX_TRANSPARENT);
    SetLayeredWindowAttributes(client,0,255,LWA_ALPHA);
    SetWindowPos(client,HWND_TOPMOST,0,0,0,0,SWP_NOMOVE|SWP_NOSIZE|SWP_NOACTIVATE|SWP_SHOWWINDOW);
    std::promise<bool> ready;auto future=ready.get_future();active=true;
    worker=std::thread([&ready]{
        workerId=GetCurrentThreadId();SetThreadDpiAwarenessContext(DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2);
        WNDCLASSW type{};type.hInstance=GetModuleHandleW(nullptr);type.lpfnWndProc=InputProc;type.lpszClassName=L"NetCafeInputPump";
        RegisterClassW(&type);receiver=CreateWindowW(type.lpszClassName,L"",0,0,0,0,0,HWND_MESSAGE,nullptr,type.hInstance,nullptr);
        bool magnification=MagInitialize()!=0;
        cursorHidden=magnification&&MagShowSystemCursor(FALSE);
        UINT count=0;GetRegisteredRawInputDevices(nullptr,&count,sizeof(RAWINPUTDEVICE));std::vector<RAWINPUTDEVICE> devices(count);
        if(count)GetRegisteredRawInputDevices(devices.data(),&count,sizeof(RAWINPUTDEVICE));savedRawValid=false;
        for(auto& device:devices)if(device.usUsagePage==1&&device.usUsage==2){savedRaw=device;savedRawValid=true;break;}
        RAWINPUTDEVICE mouse{1,2,RIDEV_INPUTSINK,receiver};rawRegistered=RegisterRawInputDevices(&mouse,1,sizeof(mouse))!=0;
        HMODULE module=nullptr;GetModuleHandleExW(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS|GET_MODULE_HANDLE_EX_FLAG_UNCHANGED_REFCOUNT,reinterpret_cast<LPCWSTR>(MouseProc),&module);
        mouseHook=SetWindowsHookExW(WH_MOUSE_LL,MouseProc,module,0);keyHook=SetWindowsHookExW(WH_KEYBOARD_LL,KeyProc,module,0);
        bool hotkey=RegisterHotKey(nullptr,1,escapeModifiers|MOD_NOREPEAT,escapeKey)!=0;
        senderStopped=false;
        WORD replayPressed=0;
        sender=std::thread([&replayPressed]{
            for(;;){
                MouseBatch batch;
                {std::unique_lock lock(sendGate);sendReady.wait(lock,[]{return senderStopped||!sendQueue.empty();});if(senderStopped)break;batch=sendQueue.front();sendQueue.pop_front();}
                if(!active||yielding)continue;
                injectedPending+=batch.count;UINT sent=SendInput(batch.count,batch.events,sizeof(INPUT));
                for(UINT i=0;i<batch.count;i++){
                    auto& mouse=batch.events[i].mi;
                    DWORD flags=sent==batch.count?mouse.dwFlags:mouse.dwFlags&(MOUSEEVENTF_LEFTDOWN|MOUSEEVENTF_RIGHTDOWN|MOUSEEVENTF_MIDDLEDOWN|MOUSEEVENTF_XDOWN);
                    replayPressed=ButtonState(replayPressed,flags,mouse.mouseData);
                }
                if(sent!=batch.count){injectedPending-=batch.count-sent;lastError=GetLastError()?GetLastError():ERROR_ACCESS_DENIED;actions|=8;}
            }
        });
        bool ok=receiver&&cursorHidden&&rawRegistered&&mouseHook&&keyHook&&hotkey;if(!ok)lastError=GetLastError();ready.set_value(ok);
        bool running=ok;MSG message{};
        while(running){
            MsgWaitForMultipleObjectsEx(0,nullptr,movePending?8:INFINITE,QS_ALLINPUT,MWMO_INPUTAVAILABLE);
            for(int i=0;i<128&&PeekMessageW(&message,nullptr,0,0,PM_REMOVE);i++){
                if(message.message==WM_QUIT){running=false;break;}
                if(message.message==WM_HOTKEY)actions|=1;else{TranslateMessage(&message);DispatchMessageW(&message);}
            }
            bool flush=false;{std::lock_guard lock(gate);if(movePending&&GetTickCount64()-lastMove>=8){movePending=false;lastMove=GetTickCount64();flush=(PtInRect(&viewport,pointer)||pressed)&&!PtInRect(&toolbar,pointer)&&!held;}}
            if(flush&&active)ReplayMovement();
        }
        if(hotkey)UnregisterHotKey(nullptr,1);
        if(mouseHook)UnhookWindowsHookEx(mouseHook);if(keyHook)UnhookWindowsHookEx(keyHook);mouseHook=keyHook=nullptr;
        {std::lock_guard lock(sendGate);senderStopped=true;sendQueue.clear();}sendReady.notify_one();if(sender.joinable())sender.join();
        // A physical UP may have been intercepted but cancelled before its queued replay.
        pressed|=replayPressed;
        if(rawRegistered){
            RAWINPUTDEVICE current{};bool found=false;
            // Unity may reclaim the mouse between the last poll and F8.
            // Only replace our own registration during teardown.
            if(ReadRawMouseRegistration(current,found)&&found&&current.hwndTarget==receiver){
                RAWINPUTDEVICE restore=savedRawValid?savedRaw:RAWINPUTDEVICE{1,2,RIDEV_REMOVE,nullptr};
                RegisterRawInputDevices(&restore,1,sizeof(restore));
            }
            rawRegistered=false;
        }
        if(pressed){INPUT releases[5]{};int n=0;if(pressed&MK_LBUTTON)releases[n++]=Mouse(MOUSEEVENTF_LEFTUP,{});if(pressed&MK_RBUTTON)releases[n++]=Mouse(MOUSEEVENTF_RIGHTUP,{});if(pressed&MK_MBUTTON)releases[n++]=Mouse(MOUSEEVENTF_MIDDLEUP,{});if(pressed&MK_XBUTTON1)releases[n++]=Mouse(MOUSEEVENTF_XUP,{},XBUTTON1);if(pressed&MK_XBUTTON2)releases[n++]=Mouse(MOUSEEVENTF_XUP,{},XBUTTON2);SendInput(n,releases,sizeof(INPUT));}
        pressed=0;held=false;injectedPending=0;movePending=false;if(cursorHidden)MagShowSystemCursor(TRUE);cursorHidden=false;if(magnification)MagUninitialize();if(receiver)DestroyWindow(receiver);receiver=nullptr;
        std::vector<INPUT> releases;
        for(size_t key=0;key<pressedKeys.size();key++)if(pressedKeys[key]&&!yielding){INPUT release{};release.type=INPUT_KEYBOARD;release.ki.wVk=static_cast<WORD>(key);release.ki.dwFlags=KEYEVENTF_KEYUP|((pressedKeys[key]&2)?KEYEVENTF_EXTENDEDKEY:0);releases.push_back(release);}
        if(!releases.empty())SendInput(static_cast<UINT>(releases.size()),releases.data(),sizeof(INPUT));pressedKeys.fill(0);
    });
    if(!future.get()){End();return 0;}
    if(!IsWindow(resumeTarget)||!IsWindowVisible(resumeTarget)) { POINT p;{std::lock_guard lock(gate);p=MapPoint(pointer);}resumeTarget=GetAncestor(WindowFromPoint(p),GA_ROOT); }
    DWORD owner=0;GetWindowThreadProcessId(resumeTarget,&owner);
    if(owner!=GetCurrentProcessId()&&IsWindow(resumeTarget)&&IsWindowVisible(resumeTarget))SetForegroundWindow(resumeTarget);
    return 1;
}
API void __cdecl NC_ControlEnd(){End();}
API int __cdecl NC_ControlPoll(){
    unityHeartbeat.store(GetTickCount64());
    if(active&&!yielding&&GetTickCount64()-rawMouseCheckAt>=100){
        rawMouseCheckAt=GetTickCount64();
        if(!EnsureRawMouseReceiver())actions|=8;
    }
    int value=actions.exchange(0);
    if(value&(1|8|64|128|256))End((value&256)==0);
    else if(active && !yielding)KeepMirroredDesktopBehindGame();
    if(!active && focusReturnPending)
    {
        if(!IsWindow(client) || GetForegroundWindow()==client ||
            GetTickCount64()-focusReturnStarted>1500)
            focusReturnPending=false;
        else SetForegroundWindow(client);
    }
    return value;
}
API void __cdecl NC_ControlShutdown(){End();NC_SafetyStop();}
API void __cdecl NC_ControlDiagnostic(wchar_t* text,int capacity){
    CURSORINFO cursor{sizeof(cursor)};GetCursorInfo(&cursor);
    wchar_t name[80]{};GetClassNameW(lastTarget,name,80);
    _snwprintf_s(text,capacity,_TRUNCATE,L"active=%d visible=%d error=%d cursorFlags=%lu hidden=%d raw=%d rawRecoveries=%d pending=%d view=%ld,%ld,%ld,%ld pointer=%ld,%ld requested=%ld,%ld source=%ld,%ld target=%p/%ls foreground=%p",
        active.load(),IsWindowVisible(client),lastError.load(),cursor.flags,cursorHidden.load(),rawRegistered,rawMouseRecoveries,injectedPending.load(),viewport.left,viewport.top,viewport.right,viewport.bottom,pointer.x,pointer.y,requested.x,requested.y,lastSource.x,lastSource.y,lastTarget,name,GetForegroundWindow());
}
// Opt-in test events use the same OS path as physical input, without desktop screenshots.
API int __cdecl NC_TestPointer(int x,int y,int flags,int data){
    if(!active)return 0;testDriving=true;requested={x,y};POINT p{x,y};if(client)ClientToScreen(client,&p);
    {std::lock_guard lock(gate);pointer=p;movePending=true;}
    PostMessageW(receiver,WM_NULL,0,0);
    if(!flags)return 1;
    INPUT value=Mouse(flags,{},data);value.mi.dwExtraInfo=testTag;return SendInput(1,&value,sizeof(INPUT))==1;
}
