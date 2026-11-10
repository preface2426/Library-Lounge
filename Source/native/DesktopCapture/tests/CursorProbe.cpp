#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <cstdio>
#include <thread>
static bool block;
static int seen;
static LRESULT CALLBACK Hook(int n, WPARAM m, LPARAM p) {
    if (n >= 0 && m == WM_MOUSEMOVE) { seen++; if (block) return 1; }
    return CallNextHookEx(nullptr,n,m,p);
}
int main() {
    SetProcessDpiAwarenessContext(DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2);
    POINT saved{}; GetCursorPos(&saved);
    WNDCLASSW type{};type.lpfnWndProc=DefWindowProcW;type.hInstance=GetModuleHandleW(nullptr);type.lpszClassName=L"CursorProbe";type.hCursor=LoadCursor(nullptr,IDC_ARROW);RegisterClassW(&type);
    HWND w=CreateWindowW(type.lpszClassName,L"NetCafe cursor probe",WS_OVERLAPPEDWINDOW,150,150,640,400,nullptr,nullptr,type.hInstance,nullptr);
    SetWindowPos(w,HWND_TOPMOST,0,0,0,0,SWP_NOMOVE|SWP_NOSIZE|SWP_SHOWWINDOW);SetForegroundWindow(w);SetCursorPos(250,250);SetCursor(type.hCursor);
    DWORD mainThread=GetCurrentThreadId();
    std::thread worker([=]{
        Sleep(100); MSG m{};PeekMessageW(&m,nullptr,0,0,PM_NOREMOVE);
        CURSORINFO info{sizeof(info)};GetCursorInfo(&info);POINT pos{};GetCursorPos(&pos);printf("before flags=%lu pos=%ld,%ld own=%d fg=%d\n",info.flags,pos.x,pos.y,WindowFromPoint(pos)==w,GetForegroundWindow()==w);
        BOOL attached=AttachThreadInput(GetCurrentThreadId(),mainThread,TRUE);printf("attached=%d\n",attached);
        int count=ShowCursor(FALSE);SetCursor(nullptr); GetCursorInfo(&info);printf("hidden counter=%d flags=%lu\n",count,info.flags);
        AttachThreadInput(GetCurrentThreadId(),mainThread,FALSE);GetCursorInfo(&info);printf("detached flags=%lu\n",info.flags);
        Sleep(100);GetCursorInfo(&info);printf("later flags=%lu\n",info.flags);
        AttachThreadInput(GetCurrentThreadId(),mainThread,TRUE);ShowCursor(TRUE);AttachThreadInput(GetCurrentThreadId(),mainThread,FALSE);
        PostThreadMessageW(mainThread,WM_QUIT,0,0);
    });
    MSG m{};while(GetMessageW(&m,nullptr,0,0)>0){TranslateMessage(&m);DispatchMessageW(&m);}worker.join();DestroyWindow(w);SetCursorPos(saved.x,saved.y);
    return 0;
}
