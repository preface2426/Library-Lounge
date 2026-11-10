"""Compare one owned client process; browser processes and GPU memory are separate."""
import csv
import json
import pathlib
import subprocess
import time
import psutil

root = pathlib.Path(__file__).resolve().parents[3]
player = root / 'Builds/NetCafePrototype/NetCafePrototype.exe'
boot = player.parent / 'NetCafePrototype_Data/boot.config'
original = boot.read_bytes()
results = []
try:
    for name, graphics_jobs, workers in [('baseline', True, None), ('no-graphics-jobs', False, None), ('two-workers', False, 2)]:
        config = original.decode('utf-8')
        if not graphics_jobs:
            config = config.replace('gfx-enable-gfx-jobs=1', 'gfx-enable-gfx-jobs=0').replace('gfx-threading-mode=6', 'gfx-threading-mode=1')
        boot.write_text(config, encoding='utf-8')
        log = root / f'Builds/memory-{name}.log'
        args = [str(player), '-memorySmoke', '-logFile', str(log)]
        if workers is not None:
            args += ['-job-worker-count', str(workers)]
        child = subprocess.Popen(args)
        process = psutil.Process(child.pid)
        samples = []
        started = time.monotonic()
        while child.poll() is None and time.monotonic() - started < 50:
            try:
                info = process.memory_info()
                samples.append({'seconds': round(time.monotonic()-started, 2), 'privateMB': round(info.private/2**20, 2), 'workingMB': round(info.rss/2**20, 2), 'threads': process.num_threads()})
            except psutil.NoSuchProcess:
                break
            time.sleep(.5)
        if child.poll() is None:
            raise RuntimeError(f'{name} exceeded 50 seconds; owned test process {child.pid} left for inspection')
        text = log.read_text(encoding='utf-8', errors='replace')
        if child.returncode != 0 or 'MEMORY_SMOKE_PASS' not in text:
            raise RuntimeError(f'{name} failed: exit={child.returncode}; {text[-2500:]}')
        result = {'name': name, 'metrics': [line for line in text.splitlines() if 'A2_METRIC' in line], 'samples': samples}
        results.append(result)
        print(json.dumps({k: v for k, v in result.items() if k != 'samples'}, ensure_ascii=False), flush=True)
finally:
    boot.write_bytes(original)
    (root / 'Builds/memory-comparison.json').write_text(json.dumps(results, indent=2, ensure_ascii=False), encoding='utf-8')
