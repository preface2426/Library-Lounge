"""Click the real Unity quit menu; never force-kill a passing test."""
import ctypes as c
from ctypes import wintypes as w
from pathlib import Path
import re
import subprocess
import time

root=Path(__file__).resolve().parents[3]
log=root/'Builds'/'a2-exit.log'
log.unlink(missing_ok=True)
u=c.WinDLL('user32',use_last_error=True)
u.SetProcessDpiAwarenessContext(c.c_void_p(-4))
u.GetForegroundWindow.restype=w.HWND
u.SetForegroundWindow.argtypes=[w.HWND]
u.GetWindowThreadProcessId.argtypes=[w.HWND,c.POINTER(w.DWORD)]
u.GetClassNameW.argtypes=[w.HWND,w.LPWSTR,c.c_int]
callback=c.WINFUNCTYPE(w.BOOL,w.HWND,w.LPARAM)
u.EnumWindows.argtypes=[callback,w.LPARAM]
u.mouse_event.argtypes=[w.DWORD,w.DWORD,w.DWORD,w.DWORD,c.c_size_t]
process=subprocess.Popen([str(root/'Builds'/'NetCafePrototype'/'NetCafePrototype.exe'),'-exitSmoke','-logFile',str(log)])
window=None
until=time.monotonic()+45
while time.monotonic()<until:
    if process.poll() is not None:raise RuntimeError(f'Client exited before quit test: {process.returncode}')
    text=log.read_text(encoding='utf-8',errors='replace') if log.exists() else ''
    match=re.search(r'EXIT_SMOKE_MENU_READY: x=(\d+) y=(\d+)',text)
    if match:break
    time.sleep(.1)
else:raise RuntimeError('Quit menu did not become ready; owned test left running for diagnosis')
@callback
def find(hwnd,_):
    global window
    pid=w.DWORD();u.GetWindowThreadProcessId(hwnd,c.byref(pid))
    name=c.create_unicode_buffer(80);u.GetClassNameW(hwnd,name,80)
    if pid.value==process.pid and name.value=='UnityWndClass':window=hwnd;return False
    return True
u.EnumWindows(find,0)
if not window:raise RuntimeError('Owned Unity window missing')
u.SetForegroundWindow(window);time.sleep(.2)
if u.GetForegroundWindow()!=window:raise RuntimeError('Owned quit menu is not foreground; no mouse input sent')
x,y=map(int,match.groups())
width,height=u.GetSystemMetrics(0),u.GetSystemMetrics(1)
u.mouse_event(0x8001,round(x*65535/(width-1)),round(y*65535/(height-1)),0,0)
time.sleep(.05);u.mouse_event(2,0,0,0,0);time.sleep(.1);u.mouse_event(4,0,0,0,0)
code=process.wait(timeout=15)
text=log.read_text(encoding='utf-8',errors='replace')
if code!=0 or 'QUIT_REQUESTED: input released' not in text:raise RuntimeError(f'Actual quit failed: {code}')
if not u.RegisterHotKey(None,0x4e45,0x4000,0x77):raise RuntimeError('F8 still registered after quit')
u.UnregisterHotKey(None,0x4e45)
print(f'EXIT_SMOKE_PASS: real menu click, process exit={code}, input released, F8 available')
