"""Short regression with normal Unity focus handling and a real Continue click."""
import ctypes as c
from ctypes import wintypes as w
from pathlib import Path
import re
import subprocess
import sys
import time

root = Path(__file__).resolve().parents[3]
label = sys.argv[1] if len(sys.argv) > 1 else 'latest'
if not re.fullmatch(r'[a-z0-9-]+', label):
    raise ValueError('Invalid log label')
pointer = '--pointer' in sys.argv[2:]
fps = '--fps' in sys.argv[2:]
kind = 'ui' if '--ui' in sys.argv[2:] else 'fps' if fps else 'pointer' if pointer else 'focus'
log = root / 'Builds' / f'a2-{kind}-{label}.log'
log.unlink(missing_ok=True)
u = c.WinDLL('user32', use_last_error=True)
u.SetProcessDpiAwarenessContext(c.c_void_p(-4))
u.GetForegroundWindow.restype = w.HWND
u.GetWindowThreadProcessId.argtypes = [w.HWND, c.POINTER(w.DWORD)]
u.GetAncestor.argtypes = [w.HWND, w.UINT]
u.GetAncestor.restype = w.HWND
u.WindowFromPoint.argtypes = [w.POINT]
u.WindowFromPoint.restype = w.HWND
u.SetForegroundWindow.argtypes = [w.HWND]
u.PostMessageW.argtypes = [w.HWND, w.UINT, w.WPARAM, w.LPARAM]
u.mouse_event.argtypes = [w.DWORD, w.DWORD, w.DWORD, w.DWORD, c.c_size_t]
process = subprocess.Popen([str(root / 'Builds/NetCafePrototype/NetCafePrototype.exe'), f'-{kind}Smoke', '-logFile', str(log)])
u.AllowSetForegroundWindow(process.pid)
clicked = False
foreground_changes = []
started = time.monotonic()
try:
    while process.poll() is None and time.monotonic() - started < 45:
        if pointer:
            foreground = u.GetForegroundWindow()
            if not foreground_changes or foreground_changes[-1][1] != foreground:
                pid = w.DWORD()
                u.GetWindowThreadProcessId(foreground, c.byref(pid))
                name = c.create_unicode_buffer(100)
                u.GetClassNameW(w.HWND(foreground), name, 100)
                foreground_changes.append((round(time.monotonic()-started, 2), foreground, pid.value, name.value))
        text = log.read_text(encoding='utf-8', errors='replace') if log.exists() else ''
        match = re.search(r'FOCUS_SMOKE_CONTINUE_READY: x=(\d+) y=(\d+)', text)
        if match and not clicked:
            window = u.GetForegroundWindow()
            pid = w.DWORD()
            u.GetWindowThreadProcessId(window, c.byref(pid))
            if pid.value != process.pid:
                raise RuntimeError('Own menu is not foreground; no click sent')
            x, y = map(int, match.groups())
            if u.GetAncestor(u.WindowFromPoint(w.POINT(x, y)), 2) != window:
                raise RuntimeError('Own Continue button is covered; no click sent')
            u.SetCursorPos(x, y)
            u.mouse_event(2, 0, 0, 0, 0)
            time.sleep(.1)
            u.mouse_event(4, 0, 0, 0, 0)
            clicked = True
        time.sleep(.1)
    if process.poll() is None:
        raise RuntimeError('Focus regression exceeded 45 seconds')
    text = log.read_text(encoding='utf-8', errors='replace')
    if process.returncode != 0 or f'{kind.upper()}_SMOKE_PASS:' not in text or (kind == 'focus' and not clicked):
        if pointer:
            print('Foreground transitions (seconds, hwnd, pid, class):', foreground_changes[-15:], flush=True)
        raise RuntimeError(f'Focus regression failed: exit={process.returncode}; {text[-2300:]}')
    if not u.RegisterHotKey(None, 0x4e45, 0x4000, 0x77):
        raise RuntimeError('F8 still reserved after cleanup')
    u.UnregisterHotKey(None, 0x4e45)
    print(f'{kind.upper()}_SMOKE_PASS: normal focus handling, input transitions, process exit=0, F8 released')
finally:
    if process.poll() is None:
        callback = c.WINFUNCTYPE(w.BOOL, w.HWND, w.LPARAM)
        @callback
        def close_owned(window, _):
            pid = w.DWORD()
            u.GetWindowThreadProcessId(window, c.byref(pid))
            if pid.value == process.pid:
                u.PostMessageW(window, 0x10, 0, 0)
            return True
        u.EnumWindows(close_owned, 0)
        process.wait(timeout=10)
