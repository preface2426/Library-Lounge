"""Measure owned browser profiles separately from the client; never include user profiles."""
import csv
import pathlib
import sys
import time
import psutil

root = pathlib.Path(__file__).resolve().parents[3]
client_id = int(sys.argv[1])
profiles = root / 'Builds/NetCafePrototype/Evidence/A2'
destination = root / 'Builds/a2-app-memory.csv'
with destination.open('w', newline='', encoding='utf-8') as output:
    writer = csv.DictWriter(output, fieldnames=['time', 'app', 'processes', 'privateMB', 'summedWorkingMB'])
    writer.writeheader()
    while psutil.pid_exists(client_id):
        groups = {'client': {client_id}, 'chrome': set(), 'msedge': set()}
        for process in psutil.process_iter(['pid', 'cmdline']):
            try:
                args = process.info['cmdline'] or []
                for name in ('chrome', 'msedge'):
                    expected = '--user-data-dir=' + str(profiles / (name + 'Profile'))
                    if expected.lower() in [arg.lower() for arg in args] and not any(arg.startswith('--type=') for arg in args):
                        groups[name].add(process.pid)
                        groups[name].update(child.pid for child in process.children(recursive=True))
            except (psutil.NoSuchProcess, psutil.AccessDenied):
                continue
        for name, ids in groups.items():
            private = working = count = 0
            for pid in ids:
                try:
                    info = psutil.Process(pid).memory_info()
                    private += info.private
                    working += info.rss
                    count += 1
                except (psutil.NoSuchProcess, psutil.AccessDenied):
                    continue
            if count:
                writer.writerow({'time': time.time(), 'app': name, 'processes': count, 'privateMB': round(private/2**20, 2), 'summedWorkingMB': round(working/2**20, 2)})
        output.flush()
        time.sleep(5)
