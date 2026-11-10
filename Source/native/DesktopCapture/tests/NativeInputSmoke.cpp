#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <windows.h>
#include <windowsx.h>
#include <cstdio>
#include <stdexcept>
#include <string>
static HWND fixture,edit,button,panel;
static int clicks,doubles,wheels,keyStateDown,dragged,downs,ups,ticks,rightDown;
static bool fastFixture;
static WNDPROC buttonProc;
static LRESULT CALLBACK ButtonProc(HWND w,UINT m,WPARAM a,LPARAM b){if(m==WM_LBUTTONDOWN||m==WM_LBUTTONDBLCLK){downs++;if(GetKeyState(VK_LBUTTON)<0)keyStateDown++;}if(m==WM_LBUTTONUP)ups++;if(m==WM_RBUTTONDOWN)rightDown++;return CallWindowProcW(buttonProc,w,m,a,b);}
static LRESULT CALLBACK FixtureProc(HWND w,UINT m,WPARAM a,LPARAM b){
    if(m==WM_COMMAND&&HIWORD(a)==BN_CLICKED){clicks++;InvalidateRect(w,nullptr,FALSE);}
    if(m==WM_PAINT){PAINTSTRUCT paint{};HDC dc=BeginPaint(w,&paint);RECT r{};GetClientRect(w,&r);auto brush=CreateSolidBrush(clicks?RGB(35,180,90):RGB(22,80,140));FillRect(dc,&r,brush);DeleteObject(brush);RECT blink{700,30,735,65};auto status=CreateSolidBrush(fastFixture?RGB(ticks%256,(ticks/256)%256,180):ticks%2?RGB(240,40,160):RGB(20,240,180));FillRect(dc,&blink,status);DeleteObject(status);EndPaint(w,&paint);return 0;}
    if(m==WM_TIMER){ticks++;RECT r{700,30,735,65};InvalidateRect(w,&r,FALSE);return 0;}
    if(m==WM_MOUSEWHEEL)wheels++;
    if(m==WM_RBUTTONDOWN)rightDown++;
    if(m==WM_LBUTTONDBLCLK)doubles++;
    if(m==WM_LBUTTONDOWN){SetCapture(w);return 0;}
    if(m==WM_MOUSEMOVE&&GetCapture()==w){dragged++;return 0;}
    if(m==WM_LBUTTONUP&&GetCapture()==w){ReleaseCapture();return 0;}
    if(m==WM_APP+11){SetWindowPos(w,HWND_TOP,0,0,0,0,SWP_NOMOVE|SWP_NOSIZE|SWP_SHOWWINDOW|SWP_NOACTIVATE);return 0;}
    if(m==WM_APP+10){switch(a){case 0:return clicks;case 1:return doubles;case 2:return wheels;case 3:return keyStateDown;case 4:return dragged;case 5:return downs;case 6:return ups;case 7:return ticks;case 8:return rightDown;}}
    if(m==WM_CLOSE){DestroyWindow(w);return 0;}
    if(m==WM_DESTROY){PostQuitMessage(0);return 0;}
    return DefWindowProcW(w,m,a,b);
}
static LRESULT CALLBACK OverlayProc(HWND w,UINT m,WPARAM a,LPARAM b){if(m==WM_SETCURSOR){SetCursor(nullptr);return TRUE;}return DefWindowProcW(w,m,a,b);}
static void Pump(int ms){ULONGLONG end=GetTickCount64()+ms;do{MSG m{};while(PeekMessageW(&m,nullptr,0,0,PM_REMOVE)){TranslateMessage(&m);DispatchMessageW(&m);}if(ms>0)MsgWaitForMultipleObjects(0,nullptr,FALSE,1,QS_ALLINPUT);}while(GetTickCount64()<end);}
static void Require(bool value,char const* message){if(!value)throw std::runtime_error(message);}
int wmain(int argc,wchar_t** argv){
    SetProcessDpiAwarenessContext(DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2);
    WNDCLASSW type{};type.lpfnWndProc=FixtureProc;type.hInstance=GetModuleHandleW(nullptr);type.lpszClassName=L"NetCafeA2ExternalFixture";type.style=CS_DBLCLKS;type.hCursor=LoadCursor(nullptr,IDC_ARROW);type.hbrBackground=reinterpret_cast<HBRUSH>(COLOR_WINDOW+1);RegisterClassW(&type);
    if(argc>1&&wcscmp(argv[1],L"--fixture")==0){
        fixture=CreateWindowW(type.lpszClassName,L"NetCafe A2 external input test",WS_OVERLAPPEDWINDOW,160,160,820,600,nullptr,nullptr,type.hInstance,nullptr);
        button=CreateWindowW(L"BUTTON",L"Count clicks",WS_CHILD|WS_VISIBLE|BS_PUSHBUTTON,60,60,240,65,fixture,reinterpret_cast<HMENU>(1),type.hInstance,nullptr);
        buttonProc=reinterpret_cast<WNDPROC>(SetWindowLongPtrW(button,GWLP_WNDPROC,reinterpret_cast<LONG_PTR>(ButtonProc)));
        edit=CreateWindowExW(WS_EX_CLIENTEDGE,L"EDIT",L"",WS_CHILD|WS_VISIBLE|ES_MULTILINE|ES_AUTOVSCROLL,60,160,600,180,fixture,reinterpret_cast<HMENU>(2),type.hInstance,nullptr);
        SetWindowPos(fixture,HWND_TOP,0,0,0,0,SWP_NOMOVE|SWP_NOSIZE|SWP_SHOWWINDOW);SetForegroundWindow(fixture);
        if(argc>2&&wcscmp(argv[2],L"--fast")==0){
            fastFixture=true;
            HANDLE timer=CreateWaitableTimerExW(nullptr,nullptr,CREATE_WAITABLE_TIMER_HIGH_RESOLUTION,TIMER_ALL_ACCESS);
            if(!timer)return 2;
            LARGE_INTEGER due{};due.QuadPart=-10000;
            DEVMODEW display{};display.dmSize=sizeof(display);
            int interval=EnumDisplaySettingsW(nullptr,ENUM_CURRENT_SETTINGS,&display)&&display.dmDisplayFrequency>1?1000/(display.dmDisplayFrequency+1):16;
            if(interval<1)interval=1;
            if(!SetWaitableTimer(timer,&due,interval,nullptr,nullptr,FALSE)){CloseHandle(timer);return 2;}
            while(IsWindow(fixture)){
                Pump(0);if(!IsWindow(fixture))break;
                SendMessageW(fixture,WM_TIMER,1,0);UpdateWindow(fixture);
                WaitForSingleObject(timer,100);
            }
            CancelWaitableTimer(timer);CloseHandle(timer);
            return 0;
        }
        SetTimer(fixture,1,50,nullptr);
        MSG m{};while(GetMessageW(&m,nullptr,0,0)>0){TranslateMessage(&m);DispatchMessageW(&m);}return 0;
    }
    POINT saved{};GetCursorPos(&saved);PROCESS_INFORMATION child{};HWND overlay=nullptr;HMODULE dll=nullptr;
    using Poll=int(__cdecl*)();using Begin=int(__cdecl*)(int,int);using End=void(__cdecl*)();using RectFn=void(__cdecl*)(int,int,int,int);using Toolbar=void(__cdecl*)(int,int,int,int,int);using Pointer=int(__cdecl*)(int,int,int,int);using Diagnostic=void(__cdecl*)(wchar_t*,int);
    End end=nullptr;Diagnostic diagnostic=nullptr;int result=1;
    try{
        wchar_t exe[MAX_PATH]{};GetModuleFileNameW(nullptr,exe,MAX_PATH);std::wstring command=L"\""+std::wstring(exe)+L"\" --fixture";STARTUPINFOW startup{sizeof(startup)};startup.dwFlags=STARTF_USESHOWWINDOW;startup.wShowWindow=SW_HIDE;
        Require(CreateProcessW(nullptr,command.data(),nullptr,nullptr,FALSE,0,nullptr,nullptr,&startup,&child)!=0,"fixture process failed");
        for(int i=0;i<100&&!fixture;i++){Pump(20);EnumWindows([](HWND w,LPARAM id)->BOOL{DWORD pid;GetWindowThreadProcessId(w,&pid);wchar_t name[80]{};GetClassNameW(w,name,80);if(pid==static_cast<DWORD>(id)&&wcscmp(name,L"NetCafeA2ExternalFixture")==0){fixture=w;return FALSE;}return TRUE;},child.dwProcessId);}Require(fixture!=nullptr,"fixture window missing");
        ShowWindow(fixture,SW_SHOW);ShowWindow(fixture,SW_SHOW);SetWindowPos(fixture,HWND_NOTOPMOST,0,0,0,0,SWP_NOMOVE|SWP_NOSIZE|SWP_SHOWWINDOW);SetForegroundWindow(fixture);Pump(100);
        RECT fr{};GetWindowRect(fixture,&fr);printf("fixture visible=%d iconic=%d rect=%ld,%ld,%ld,%ld fg=%d\n",IsWindowVisible(fixture),IsIconic(fixture),fr.left,fr.top,fr.right,fr.bottom,GetForegroundWindow()==fixture);
        MONITORINFO monitor{sizeof(monitor)};GetMonitorInfoW(MonitorFromPoint({0,0},MONITOR_DEFAULTTOPRIMARY),&monitor);auto r=monitor.rcMonitor;
        SetWindowPos(fixture,HWND_TOP,r.right-1000,160,0,0,SWP_NOSIZE|SWP_NOACTIVATE);Pump(50);
        type.lpfnWndProc=OverlayProc;type.lpszClassName=L"UnityWndClass";type.hCursor=nullptr;RegisterClassW(&type);
        overlay=CreateWindowExW(WS_EX_TOPMOST,type.lpszClassName,L"NetCafe input overlay test",WS_POPUP,r.left,r.top,r.right-r.left,r.bottom-r.top,nullptr,nullptr,type.hInstance,nullptr);
        SetWindowPos(overlay,HWND_TOPMOST,0,0,0,0,SWP_NOMOVE|SWP_NOSIZE|SWP_SHOWWINDOW);SetForegroundWindow(overlay);Pump(100);
        dll=LoadLibraryW(L"NetCafeClient\\Assets\\Plugins\\x86_64\\NetCafeCapture.dll");Require(dll!=nullptr,"DLL missing");
        auto poll=reinterpret_cast<Poll>(GetProcAddress(dll,"NC_ControlPoll"));auto begin=reinterpret_cast<Begin>(GetProcAddress(dll,"NC_ControlBegin"));end=reinterpret_cast<End>(GetProcAddress(dll,"NC_ControlEnd"));auto view=reinterpret_cast<RectFn>(GetProcAddress(dll,"NC_ControlView"));auto toolbar=reinterpret_cast<Toolbar>(GetProcAddress(dll,"NC_ControlToolbar"));auto pointer=reinterpret_cast<Pointer>(GetProcAddress(dll,"NC_TestPointer"));diagnostic=reinterpret_cast<Diagnostic>(GetProcAddress(dll,"NC_ControlDiagnostic"));
        Require(begin&&end&&view&&toolbar&&pointer,"exports missing");
        RECT v{(r.right-r.left)/20,(r.bottom-r.top)/20,(r.right-r.left)*19/20,(r.bottom-r.top)*19/20};view(v.left,v.top,v.right-v.left,v.bottom-v.top);toolbar(1100,10,600,50,0);
        Require(begin(0,1)!=0,"native control begin failed");view(v.left,v.top,v.right-v.left,v.bottom-v.top);
        // Pin only the owned fixture under the owned overlay so unrelated windows cannot take test clicks.
        SetWindowPos(fixture,HWND_TOPMOST,0,0,0,0,SWP_NOMOVE|SWP_NOSIZE|SWP_NOACTIVATE);
        SetWindowPos(overlay,HWND_TOPMOST,0,0,0,0,SWP_NOMOVE|SWP_NOSIZE|SWP_NOACTIVATE);Pump(100);
        auto click=[&](POINT source,DWORD flags,DWORD data=0){POINT p{v.left+MulDiv(source.x-r.left,v.right-v.left-1,r.right-r.left-1),v.top+MulDiv(source.y-r.top,v.bottom-v.top-1,r.bottom-r.top-1)};Require(pointer(p.x,p.y,flags,data)!=0,"SendInput driver failed");Pump(200);};
        auto stat=[&](int index){DWORD_PTR value=0;Require(SendMessageTimeoutW(fixture,WM_APP+10,index,0,SMTO_ABORTIFHUNG,500,&value)!=0,"fixture hung");return static_cast<int>(value);};
        POINT p{180,90};ClientToScreen(fixture,&p);printf("fixture source point=%ld,%ld button=%p\n",p.x,p.y,GetDlgItem(fixture,1));fflush(stdout);
        for(int i=0;i<12;i++){click(p,MOUSEEVENTF_LEFTDOWN);click(p,MOUSEEVENTF_LEFTUP);Require(stat(0)==i+1,"external button click not delivered");}
        Require(stat(3)==12,"buttons did not update OS key state");Require(GetForegroundWindow()==fixture,"external application not focused");
        POINT e{180,220};ClientToScreen(fixture,&e);click(e,MOUSEEVENTF_LEFTDOWN);click(e,MOUSEEVENTF_LEFTUP);
        const wchar_t* text=L"A2 cross-process 中文";for(auto t=text;*t;t++){INPUT k[2]{};k[0].type=k[1].type=INPUT_KEYBOARD;k[0].ki.wScan=k[1].ki.wScan=*t;k[0].ki.dwFlags=KEYEVENTF_UNICODE;k[1].ki.dwFlags=KEYEVENTF_UNICODE|KEYEVENTF_KEYUP;SendInput(2,k,sizeof(INPUT));}Pump(100);
        edit=GetDlgItem(fixture,2);wchar_t buffer[100]{};SendMessageTimeoutW(edit,WM_GETTEXT,100,reinterpret_cast<LPARAM>(buffer),SMTO_ABORTIFHUNG,500,nullptr);Require(wcscmp(buffer,text)==0,"external typing failed");
        POINT area{700,390};ClientToScreen(fixture,&area);click(area,MOUSEEVENTF_LEFTDOWN);click({area.x+30,area.y+15},0);click({area.x+60,area.y+30},0);click({area.x+60,area.y+30},MOUSEEVENTF_LEFTUP);Require(stat(4)>0,"captured drag failed");
        click(area,MOUSEEVENTF_LEFTDOWN);click(area,MOUSEEVENTF_LEFTUP);click(area,MOUSEEVENTF_LEFTDOWN);click(area,MOUSEEVENTF_LEFTUP);Require(stat(1)>0,"OS double click failed");
        click(area,MOUSEEVENTF_WHEEL,WHEEL_DELTA);Require(stat(2)>0,"OS wheel failed");
        // One physical right-click must replay to the real Windows fixture
        // exactly once, including its release. It is NOT two click streams.
        int priorRight=stat(8);
        click(area,MOUSEEVENTF_RIGHTDOWN);click(area,MOUSEEVENTF_RIGHTUP);
        Require(stat(8)==priorRight+1,"RIGHT_CLICK_DUPLICATED: one input yielded multiple OS downs");
        printf("NATIVE_RIGHT_CLICK_ONCE_PASS: rightDown=%d\n",stat(8));
        POINT mapped{v.left+MulDiv(p.x-r.left,v.right-v.left-1,r.right-r.left-1),v.top+MulDiv(p.y-r.top,v.bottom-v.top-1,r.bottom-r.top-1)};
        toolbar(mapped.x-60,mapped.y-25,300,50,0);int oldWheel=stat(2);
        click(p,MOUSEEVENTF_RIGHTDOWN);click(p,MOUSEEVENTF_RIGHTUP);click(p,MOUSEEVENTF_WHEEL,WHEEL_DELTA);
        Require(stat(8)==0&&stat(2)==oldWheel,"Toolbar leaked right click or wheel");toolbar(1100,10,600,50,0);
        SetForegroundWindow(overlay);Pump(50);
        INPUT switchKeys[4]{};for(auto& key:switchKeys)key.type=INPUT_KEYBOARD;
        switchKeys[0].ki.wVk=VK_MENU;switchKeys[1].ki.wVk=VK_TAB;switchKeys[2].ki.wVk=VK_TAB;switchKeys[2].ki.dwFlags=KEYEVENTF_KEYUP;switchKeys[3].ki.wVk=VK_MENU;switchKeys[3].ki.dwFlags=KEYEVENTF_KEYUP;
        Require(SendInput(4,switchKeys,sizeof(INPUT))==4,"System window switch setup failed");Pump(100);
        Require((poll()&256)!=0,"Alt+Tab did not yield input");Pump(100);Require(GetForegroundWindow()!=overlay,"System switch stole focus back to overlay");
        Require(begin(0,1)!=0,"Input did not resume after system switch");view(v.left,v.top,v.right-v.left,v.bottom-v.top);Pump(50);
        click(p,MOUSEEVENTF_LEFTDOWN);click(p,MOUSEEVENTF_XDOWN,XBUTTON1);click(p,MOUSEEVENTF_XDOWN,XBUTTON2);
        INPUT key{};key.type=INPUT_KEYBOARD;key.ki.wVk=VK_SHIFT;Require(SendInput(1,&key,sizeof(key))==1,"Held key setup failed");Pump(50);
        Require((GetAsyncKeyState(VK_LBUTTON)&0x8000)&&(GetAsyncKeyState(VK_XBUTTON1)&0x8000)&&(GetAsyncKeyState(VK_XBUTTON2)&0x8000)&&(GetAsyncKeyState(VK_SHIFT)&0x8000),"Held input did not reach OS state");
        end();Pump(100);Require(!(GetAsyncKeyState(VK_LBUTTON)&0x8000)&&!(GetAsyncKeyState(VK_XBUTTON1)&0x8000)&&!(GetAsyncKeyState(VK_XBUTTON2)&0x8000)&&!(GetAsyncKeyState(VK_SHIFT)&0x8000),"Shutdown left held input stuck");
        Require(RegisterHotKey(nullptr,3,MOD_NOREPEAT,VK_F8)!=0,"Shutdown left F8 registered");UnregisterHotKey(nullptr,3);
        wchar_t info[600]{};diagnostic(info,600);wprintf(L"NATIVE_INPUT_PASS: 12 external clicks, OS key state, foreground focus, Unicode text, captured drag, double click, wheel, toolbar isolation, system window switch, held mouse/side buttons/keyboard release, F8 release; %ls\n",info);result=0;
    }catch(std::exception const& error){printf("NATIVE_INPUT_FAIL: %s fixture=%p button=%p stats:",error.what(),fixture,GetDlgItem(fixture,1));for(int i=0;i<7;i++){DWORD_PTR value=0;SendMessageTimeoutW(fixture,WM_APP+10,i,0,SMTO_ABORTIFHUNG,100,&value);printf(" %llu",static_cast<unsigned long long>(value));}printf("\n");if(diagnostic){wchar_t info[600]{};diagnostic(info,600);wprintf(L"%ls\n",info);}}
    if(end)end();if(overlay)DestroyWindow(overlay);if(fixture)PostMessageW(fixture,WM_CLOSE,0,0);if(child.hProcess){WaitForSingleObject(child.hProcess,1500);CloseHandle(child.hThread);CloseHandle(child.hProcess);}if(dll)FreeLibrary(dll);SetCursorPos(saved.x,saved.y);return result;
}
