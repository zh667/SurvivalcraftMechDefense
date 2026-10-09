"""Run a test mod in an isolated copy of the Survivalcraft API runtime and wait for its result file.

python tools/run_isolated.py lab  [--mode all|gallery|fire|perf] [--package dist/MechDefense-x.scmod] [--limit 1500]
python tools/run_isolated.py perf --package dist/MechDefense-0.3.1.scmod --label old
Runtime: local/runtime-<name>; the original game installation and the user's worlds are never touched.
"""
import argparse, glob, json, os, shutil, subprocess, sys, time, zipfile
from pathlib import Path
ROOT = Path(__file__).resolve().parents[1]
# The user's own game build (binaries and Content.zip only; its Mods, worlds and settings are not copied).
SOURCE = Path(r'D:\下载\[Windows]SurvivalcraftAPI_1.9.3.1')
TESTS = {'lab': ('TowerLab', 'lab-result.json'), 'perf': ('PerfCompare', 'perf-result.json')}

def main():
    ap = argparse.ArgumentParser(); ap.add_argument('test', choices=TESTS); ap.add_argument('--mode', default='all')
    ap.add_argument('--package'); ap.add_argument('--label', default=''); ap.add_argument('--limit', type=int, default=1500)
    a = ap.parse_args(); project, result_name = TESTS[a.test]
    info = json.loads((ROOT / 'Mod/modinfo.json').read_text(encoding='utf-8'))
    package = Path(a.package) if a.package else ROOT / f"dist/MechDefense-{info['Version']}.scmod"
    runtime = ROOT / f"local/runtime-{a.test}{'-' + a.label if a.label else ''}"; mods = runtime / 'Mods'; mods.mkdir(parents=True, exist_ok=True)
    for f in SOURCE.iterdir():
        if f.is_file() and f.suffix.lower() in ('.dll', '.exe', '.json', '.zip', '.js', '.xml'): shutil.copy2(f, runtime / f.name)
    if (SOURCE / 'runtimes').exists(): shutil.copytree(SOURCE / 'runtimes', runtime / 'runtimes', dirs_exist_ok=True)
    p = subprocess.run(['dotnet', 'build', str(ROOT / f'Tests/{project}/{project}.csproj'), '-c', 'Release', '--nologo', '-v', 'q'], capture_output=True, text=True, encoding='utf-8', errors='replace')
    if p.returncode: print(p.stdout[-3000:]); sys.exit('test build failed')
    for f in mods.iterdir(): f.unlink()
    shutil.copy2(ROOT / package if not package.is_absolute() else package, mods / 'MechDefense.scmod')
    if a.test == 'lab':      # a copy of the user's Neorxna info mod, to exercise the optional compatibility patch
        for f in (SOURCE / 'Mods').glob('*Neorxna-v1.4*.scmod'): shutil.copy2(f, mods / 'Neorxna.scmod'); break
    deps = {info['PackageName']: info['Version']} if a.test == 'lab' else {}
    with zipfile.ZipFile(mods / f'{project}.scmod', 'w') as z:
        z.write(ROOT / f'Tests/{project}/bin/Release/net10.0/{project}.dll', f'{project}.dll')
        z.writestr('modinfo.json', json.dumps(dict(Name=project, Version='1.0.0', ApiVersion='1.9.3.1', ScVersion='2.4', PackageName='local.' + project.lower(),
                                                   Author='Tests', Description='Isolated tests only', LoadOrder=1300, Dependencies=deps)))
    (runtime / 'lab-mode.txt').write_text(a.mode, encoding='ascii')
    res = runtime / result_name
    if res.exists(): res.unlink()
    shots = runtime / 'ScreenCapture'
    if shots.exists(): shutil.rmtree(shots)
    si = subprocess.STARTUPINFO(); si.dwFlags |= subprocess.STARTF_USESHOWWINDOW; si.wShowWindow = 0   # hidden window
    proc = subprocess.Popen([str(runtime / 'Survivalcraft.exe')], cwd=runtime, startupinfo=si)
    t0 = time.time()
    while time.time() - t0 < a.limit and not res.exists() and proc.poll() is None: time.sleep(2)
    time.sleep(3)
    if proc.poll() is None: proc.kill()
    print('elapsed', round(time.time() - t0), 'exit', proc.poll())
    print(res.read_text(encoding='utf-8')[-8000:] if res.exists() else 'NO RESULT')
    print(len(list(shots.glob('*.png'))) if shots.exists() else 0, 'screenshots')
    for l in sorted(glob.glob(str(runtime / '*.log')) + glob.glob(str(runtime / 'Bugs/*')), key=os.path.getmtime)[-2:]:
        print('LOG', l); print(Path(l).read_text(encoding='utf-8', errors='replace')[-3000:])

if __name__ == '__main__':
    main()
