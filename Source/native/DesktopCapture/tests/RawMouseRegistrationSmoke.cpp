#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <cstdio>
#include <stdexcept>
#include <vector>

static void Require(bool value,char const* message) { if(!value)throw std::runtime_error(message); }
static RAWINPUTDEVICE MouseRegistration() {
    UINT count=0;
    auto result=GetRegisteredRawInputDevices(nullptr,&count,sizeof(RAWINPUTDEVICE));
    Require(result!=UINT(-1)||GetLastError()==ERROR_INSUFFICIENT_BUFFER,"Raw capacity query failed");
    std::vector<RAWINPUTDEVICE> devices(count);
    if(count)Require(GetRegisteredRawInputDevices(devices.data(),&count,sizeof(RAWINPUTDEVICE))!=UINT(-1),"Raw query failed");
    for(auto const& device:devices)if(device.usUsagePage==1&&device.usUsage==2)return device;
    return {};
}
static void Pump() {
    MSG message{};
    while(PeekMessageW(&message,nullptr,0,0,PM_REMOVE)){TranslateMessage(&message);DispatchMessageW(&message);}
}
int wmain(int argc,wchar_t** argv) {
    if(argc!=3)return 2;
    SetProcessDpiAwarenessContext(DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2);
    HMODULE dll=LoadLibraryW(argv[1]);if(!dll)return 3;
    using Start=int(__cdecl*)();using Begin=int(__cdecl*)(int,int);using End=void(__cdecl*)();using Poll=int(__cdecl*)();
    auto safety=reinterpret_cast<Start>(GetProcAddress(dll,"NC_SafetyStart"));
    auto begin=reinterpret_cast<Begin>(GetProcAddress(dll,"NC_ControlBegin"));
    auto end=reinterpret_cast<End>(GetProcAddress(dll,"NC_ControlEnd"));
    auto shutdown=reinterpret_cast<End>(GetProcAddress(dll,"NC_ControlShutdown"));
    auto poll=reinterpret_cast<Poll>(GetProcAddress(dll,"NC_ControlPoll"));
    using MonitorRect=int(__cdecl*)(int,RECT*);
    auto monitorRect=reinterpret_cast<MonitorRect>(GetProcAddress(dll,"NC_MonitorRect"));
    int status=1;HWND game=nullptr,background=nullptr;
    try {
        Require(safety&&begin&&end&&shutdown&&poll&&monitorRect,"Missing native export");
        RECT monitor{};Require(monitorRect(0,&monitor)!=0,"Monitor missing");
        WNDCLASSW cls{};cls.hInstance=GetModuleHandleW(nullptr);cls.lpfnWndProc=DefWindowProcW;cls.lpszClassName=L"UnityWndClass";
        Require(RegisterClassW(&cls)!=0,"Fixture class failed");
        cls.lpszClassName=L"NetCafeOwnedRawBackground";Require(RegisterClassW(&cls)!=0,"Background class failed");
        background=CreateWindowExW(WS_EX_TOPMOST,cls.lpszClassName,L"Owned desktop substitute",WS_POPUP,
            monitor.left,monitor.top,monitor.right-monitor.left,monitor.bottom-monitor.top,nullptr,nullptr,cls.hInstance,nullptr);
        Require(background!=nullptr,"Background fixture failed");ShowWindow(background,SW_SHOWNOACTIVATE);
        game=CreateWindowW(L"UnityWndClass",L"LibraryLounge owned raw-input fixture",WS_OVERLAPPEDWINDOW,monitor.left+200,monitor.top+200,800,500,nullptr,nullptr,cls.hInstance,nullptr);
        Require(game!=nullptr,"Fixture window failed");ShowWindow(game,SW_SHOW);
        SetWindowPos(game,HWND_TOPMOST,0,0,0,0,SWP_NOMOVE|SWP_NOSIZE|SWP_SHOWWINDOW);
        const auto focusDeadline=GetTickCount64()+3000;
        do { SetForegroundWindow(game);Pump();Sleep(20); }
        while(GetForegroundWindow()!=game&&GetTickCount64()<focusDeadline);
        Require(GetForegroundWindow()==game,"Owned fixture did not gain focus; refusing native control");
        // With the default zero viewport, Begin's fallback maps to a monitor
        // corner. All possible corners must be covered by this process.
        for(POINT p: {POINT{monitor.left,monitor.top},POINT{monitor.right-1,monitor.top},
                      POINT{monitor.left,monitor.bottom-1},POINT{monitor.right-1,monitor.bottom-1}}){
            DWORD owner=0;GetWindowThreadProcessId(WindowFromPoint(p),&owner);
            Require(owner==GetCurrentProcessId(),"Desktop substitute covered; refusing native control");
        }
        RAWINPUTDEVICE original{1,2,0,game};Require(RegisterRawInputDevices(&original,1,sizeof(original))!=0,"Original registration failed");
        Require(safety()!=0,"Safety hotkey unavailable");
        Require(begin(0,0)!=0,"Native begin failed");
        HWND receiver=MouseRegistration().hwndTarget;
        Require(receiver&&receiver!=game,"Native receiver missing");
        RAWINPUTDEVICE latest{1,2,RIDEV_INPUTSINK,game};
        if(wcscmp(argv[2],L"route")==0) {
            Require(RegisterRawInputDevices(&latest,1,sizeof(latest))!=0,"Takeover setup failed");
            const auto deadline=GetTickCount64()+600;
            while(GetTickCount64()<deadline&&MouseRegistration().hwndTarget!=receiver){poll();Pump();Sleep(5);}
            auto recovered=MouseRegistration();
            Require(recovered.hwndTarget==receiver&&(recovered.dwFlags&RIDEV_INPUTSINK),"Native mouse receiver was not recovered");
            end();
        } else if(wcscmp(argv[2],L"exit")==0) {
            Require(RegisterRawInputDevices(&latest,1,sizeof(latest))!=0,"Immediate takeover setup failed");
            end();
        } else throw std::runtime_error("Unknown test mode");
        // No Unity engine is present to re-register after the native End call.
        auto restored=MouseRegistration();
        std::printf("RAW_MOUSE_EXIT_STATE flags=%lu targetMatches=%d\n",restored.dwFlags,restored.hwndTarget==game);
        Require(restored.hwndTarget==game&&restored.dwFlags==RIDEV_INPUTSINK,"Native exit overwrote latest engine registration");
        Require(RegisterHotKey(nullptr,0x4341,MOD_NOREPEAT,VK_F8)!=0,"Native exit left F8 held");
        UnregisterHotKey(nullptr,0x4341);
        std::printf("RAW_MOUSE_REGISTRATION_PASS mode=%ls\n",argv[2]);status=0;
    } catch(std::exception const& error) { std::printf("RAW_MOUSE_REGISTRATION_FAIL %s\n",error.what()); }
    if(shutdown)shutdown();if(game)DestroyWindow(game);if(background)DestroyWindow(background);
    RAWINPUTDEVICE remove{1,2,RIDEV_REMOVE,nullptr};RegisterRawInputDevices(&remove,1,sizeof(remove));
    FreeLibrary(dll);return status;
}
