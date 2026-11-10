"""Stop background browser processes belonging only to disposable A2 test profiles."""
from pathlib import Path
import psutil

root = Path(__file__).resolve().parents[3]
folder = root / 'Builds/NetCafePrototype/Evidence/A2'
profiles = {str((folder / name).resolve()).lower() for name in ('chromeProfile', 'msedgeProfile', 'BrowserProfile')}
owned = {}
for process in psutil.process_iter(['pid', 'name', 'cmdline']):
    try:
        args = process.info['cmdline'] or []
        profile = next((arg.split('=', 1)[1] for arg in args if arg.startswith('--user-data-dir=')), '')
        if profile.lower() not in profiles or any(arg.startswith('--type=') for arg in args):
            continue
        name = process.info['name'].lower()
        if name not in ('chrome.exe', 'msedge.exe'):
            continue
        owned[process.pid] = process
        for child in process.children(recursive=True):
            if child.name().lower() == name:
                owned[child.pid] = child
    except (psutil.NoSuchProcess, psutil.AccessDenied):
        continue
for process in owned.values():
    try:
        process.terminate()
    except psutil.NoSuchProcess:
        pass
_, remaining = psutil.wait_procs(list(owned.values()), timeout=5)
if remaining:
    raise RuntimeError(f'Owned test browser processes still running: {[p.pid for p in remaining]}')
print(f'TEST_BROWSER_CLEANUP_PASS: {len(owned)} owned profile processes stopped; user profiles excluded')
