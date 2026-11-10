// Only used with the explicit -a1Smoke argument. No desktop pixels are saved by
// normal capture. The test paints a controlled background beneath Unity.
#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <windows.h>
#include <atomic>
#include <thread>
#include <string>
#include <vector>
#include <chrono>
#define API extern "C" __declspec(dllexport)
static std::atomic<HWND> pattern{nullptr};
static HWND overlay;
static std::thread worker;
static std::atomic<bool> ready{false};
static std::atomic<bool> stopRequested{false};
static RECT savedRect, monitorRect;
static LONG_PTR savedStyle, savedExStyle;
static POINT savedCursor;
static int tick;
static DWORD positionError;

static LRESULT CALLBACK Paint(HWND hwnd, UINT message, WPARAM w, LPARAM l) {
    if (message == WM_TIMER) { ++tick; InvalidateRect(hwnd, nullptr, FALSE); return 0; }
    if (message == WM_PAINT) {
        PAINTSTRUCT ps; HDC screen = BeginPaint(hwnd, &ps);
        RECT r; GetClientRect(hwnd, &r);
        HDC dc = CreateCompatibleDC(screen); HBITMAP bitmap = CreateCompatibleBitmap(screen, r.right, r.bottom);
        auto oldBitmap = SelectObject(dc, bitmap);
        const COLORREF colors[] = {RGB(220,40,40),RGB(40,200,70),RGB(40,80,220),RGB(230,200,40)};
        for (int i = 0; i < 4; i++) {
            RECT area{i%2*r.right/2, i/2*r.bottom/2, (i%2+1)*r.right/2, (i/2+1)*r.bottom/2};
            auto brush = CreateSolidBrush(colors[i]); FillRect(dc, &area, brush); DeleteObject(brush);
        }
        RECT animation{r.right/3, r.bottom/3, r.right*2/3, r.bottom*2/3};
        auto brush = CreateSolidBrush(tick/15%2 ? RGB(220,40,200) : RGB(30,200,210));
        FillRect(dc, &animation, brush); DeleteObject(brush);
        SetBkMode(dc, TRANSPARENT); SetTextColor(dc, RGB(255,255,255));
        auto font = CreateFontW(36,0,0,0,FW_BOLD,0,0,0,DEFAULT_CHARSET,0,0,0,0,L"Microsoft YaHei");
        auto oldFont = SelectObject(dc, font);
        std::wstring text = L"NETCAFE A1 / animated desktop / frame " + std::to_wstring(tick);
        TextOutW(dc, r.right/8, r.bottom/8, text.c_str(), static_cast<int>(text.size()));
        std::wstring scroll = L"Scrolling text 0123456789   持续更新测试";
        TextOutW(dc, r.right/8 + tick*7%(r.right/3), r.bottom*3/4, scroll.c_str(), static_cast<int>(scroll.size()));
        if (tick/30%2) {
            RECT popup{r.right/8,r.bottom*4/5,r.right/2,r.bottom*9/10};
            FillRect(dc, &popup, static_cast<HBRUSH>(GetStockObject(WHITE_BRUSH)));
            SetTextColor(dc, RGB(0,0,0)); TextOutW(dc,popup.left+8,popup.top+8,L"Test popup",10);
        }
        SelectObject(dc,oldFont); DeleteObject(font);
        BitBlt(screen,0,0,r.right,r.bottom,dc,0,0,SRCCOPY);
        SelectObject(dc,oldBitmap); DeleteObject(bitmap); DeleteDC(dc); EndPaint(hwnd,&ps); return 0;
    }
    if (message == WM_CLOSE) { DestroyWindow(hwnd); return 0; }
    if (message == WM_DESTROY) { PostQuitMessage(0); return 0; }
    return DefWindowProcW(hwnd,message,w,l);
}
API int __cdecl NC_TestBegin() {
    if (worker.joinable()) return 0;
    overlay = nullptr;
    EnumWindows([](HWND hwnd, LPARAM) -> BOOL {
        DWORD pid; GetWindowThreadProcessId(hwnd,&pid); wchar_t name[80]{}; GetClassNameW(hwnd,name,80);
        if (pid == GetCurrentProcessId() && wcscmp(name,L"UnityWndClass") == 0) { overlay=hwnd; return FALSE; }
        return TRUE;
    },0);
    if (!overlay) return 0;
    MONITORINFO monitor{sizeof(monitor)}; GetMonitorInfoW(MonitorFromPoint(POINT{0,0},MONITOR_DEFAULTTOPRIMARY),&monitor);
    monitorRect=monitor.rcMonitor;
    GetWindowRect(overlay,&savedRect); savedStyle=GetWindowLongPtrW(overlay,GWL_STYLE); savedExStyle=GetWindowLongPtrW(overlay,GWL_EXSTYLE);
    GetCursorPos(&savedCursor);
    SetWindowLongPtrW(overlay,GWL_STYLE,WS_POPUP|WS_VISIBLE);
    SetWindowPos(overlay,HWND_TOPMOST,monitorRect.left,monitorRect.top,monitorRect.right-monitorRect.left,monitorRect.bottom-monitorRect.top,SWP_FRAMECHANGED|SWP_SHOWWINDOW|SWP_NOACTIVATE);
    SetCursorPos(monitorRect.right-25,monitorRect.bottom-25);
    tick=0; ready=false; stopRequested=false;
    worker=std::thread([] {
        WNDCLASSW cls{}; cls.lpfnWndProc=Paint; cls.hInstance=GetModuleHandleW(nullptr); cls.lpszClassName=L"NetCafeA1Pattern";
        RegisterClassW(&cls);
        pattern=CreateWindowExW(WS_EX_TOPMOST,cls.lpszClassName,L"A1 controlled desktop",WS_POPUP,
            monitorRect.left,monitorRect.top,monitorRect.right-monitorRect.left,monitorRect.bottom-monitorRect.top,nullptr,nullptr,cls.hInstance,nullptr);
        if (!pattern) { ready=true; return; }
        if (stopRequested) { DestroyWindow(pattern); pattern=nullptr; ready=true; return; }
        SetLastError(0);
        SetWindowPos(pattern,HWND_TOPMOST,0,0,0,0,SWP_NOMOVE|SWP_NOSIZE|SWP_NOACTIVATE|SWP_SHOWWINDOW);
        positionError=GetLastError();
        SetTimer(pattern,1,33,nullptr); UpdateWindow(pattern); ready=true;
        MSG msg; while (!stopRequested && GetMessageW(&msg,nullptr,0,0)>0) { TranslateMessage(&msg); DispatchMessageW(&msg); }
        if (IsWindow(pattern)) DestroyWindow(pattern);
        pattern=nullptr;
    });
    for (int i=0;i<200 && !ready;i++) Sleep(10);
    // Only its own thread may synchronously reposition Unity while this call is
    // running; doing this on the worker deadlocks against the readiness wait.
    if (ready && pattern) {
        SetWindowPos(pattern,HWND_TOPMOST,0,0,0,0,SWP_NOMOVE|SWP_NOSIZE|SWP_NOACTIVATE);
        SetWindowPos(overlay,HWND_TOPMOST,0,0,0,0,SWP_NOMOVE|SWP_NOSIZE|SWP_NOACTIVATE);
    }
    return ready && pattern ? 1 : 0;
}
API int __cdecl NC_TestCovered() {
    RECT r{}; GetWindowRect(overlay,&r);
    bool above=false;
    for (HWND window=GetTopWindow(nullptr);window;window=GetWindow(window,GW_HWNDNEXT)) {
        if (window==overlay) above=true;
        if (window==pattern.load()) break;
    }
    return above && IsWindowVisible(overlay) && r.left<=monitorRect.left && r.top<=monitorRect.top && r.right>=monitorRect.right && r.bottom>=monitorRect.bottom;
}
API void __cdecl NC_TestDiagnostic(wchar_t* text,int capacity) {
    DWORD a=0,b=0; GetWindowDisplayAffinity(overlay,&a); GetWindowDisplayAffinity(pattern,&b);
    RECT r{}; GetWindowRect(pattern,&r);
    swprintf_s(text,capacity,L"overlay=%p affinity=%lu style=%llx ex=%llx; pattern=%p affinity=%lu rect=%ld,%ld,%ld,%ld visible=%d ex=%llx positionError=%lu above=%p",
        overlay,a,static_cast<unsigned long long>(GetWindowLongPtrW(overlay,GWL_STYLE)),static_cast<unsigned long long>(GetWindowLongPtrW(overlay,GWL_EXSTYLE)),
        pattern.load(),b,r.left,r.top,r.right,r.bottom,IsWindowVisible(pattern),static_cast<unsigned long long>(GetWindowLongPtrW(pattern,GWL_EXSTYLE)),positionError,GetWindow(pattern,GW_HWNDPREV));
}
API void __cdecl NC_TestEnd() {
    stopRequested=true;
    if (auto window=pattern.load()) PostMessageW(window,WM_CLOSE,0,0);
    if (worker.joinable()) worker.join();
    if (IsWindow(overlay)) {
        SetWindowLongPtrW(overlay,GWL_STYLE,savedStyle); SetWindowLongPtrW(overlay,GWL_EXSTYLE,savedExStyle);
        SetWindowPos(overlay,(savedExStyle&WS_EX_TOPMOST)?HWND_TOPMOST:HWND_NOTOPMOST,savedRect.left,savedRect.top,savedRect.right-savedRect.left,savedRect.bottom-savedRect.top,SWP_FRAMECHANGED|SWP_NOACTIVATE);
        SetCursorPos(savedCursor.x,savedCursor.y);
    }
    overlay=nullptr;
}
