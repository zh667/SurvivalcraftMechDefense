"""Run a test mod in an isolated copy of the Survivalcraft API runtime and wait for its result file.

python tools/run_isolated.py lab  [--mode all|gallery|fire|perf|special|combat|mech] [--package dist/MechDefense-x.scmod] [--limit 1500]
python tools/run_isolated.py perf --package dist/MechDefense-0.3.1.scmod --label old
python tools/run_isolated.py smoke --mode old --package dist/MechDefense-0.5.0.scmod --label migration   (old-save migration:
python tools/run_isolated.py smoke --mode new --label migration                                           same runtime, both steps)
Runtime: local/runtime-<name>; the original game installation and the user's worlds are never touched.
--source 1.9.4 runs on the user's API 1.9.4 Windows build instead of 1.9.3.1 (players' phones run 1.9.4).
"""
import argparse, glob, json, os, shutil, subprocess, sys, time, zipfile
from pathlib import Path
ROOT = Path(__file__).resolve().parents[1]
# The user's own game build (binaries and Content.zip only; its Mods, worlds and settings are not copied).
SOURCE = Path(r'D:\下载\[Windows]SurvivalcraftAPI_1.9.3.1')
SOURCES = {'1.9.3.1': SOURCE, '1.9.4': Path(r'D:\下载\[Windows]SurvivalcraftAPI_1.9.4')}
# The user's Neorxna info mod (exercises the optional compatibility patch). Builds for API 1.9.4 do not run on this 1.9.3.1
# runtime (2026-10-11: v1.4.3 throws MissingMethodException at start and the lab never reaches the main menu).
NEORXNA = [SOURCE / 'Mods', Path(r'D:\下载\Mods')]
TESTS = {'lab': ('TowerLab', 'lab-result.json'), 'perf': ('PerfCompare', 'perf-result.json'), 'smoke': ('RuntimeSmoke', 'smoke-result.json')}
# lab --mode combat: the user's ghoul mod and Wonderful Era (pirates; with the two mods it depends on), copied into the
# isolated runtime only.
COMBAT_MODS = [SOURCE / 'Mods' / '[API1.9.2.1]Ghoul尸鬼-v2.0.scmod', Path(r'D:\下载\[API1.9.3]美好时代V1.9.3a.scmod'),
               SOURCE / 'Mods' / '[API1.9]服饰配件V1.2.1.scmod', SOURCE / 'Mods' / '[API1.9]构千形V1.2.1.scmod']

# lab --mode faction-mods (0.13.0): the user's gun mod with terrorists and the recipe mod it needs, API 1.9.4 builds
FACTION_MODS = [Path(r'D:\下载\[API1.9.4]SiyahAkrep黑蝎枪械v2.8.3-需求前置[Neo1.4.3][RecipaediaEX-2.0.0.2].scmod'), Path(r'D:\下载\RecipaediaEX-2.0.0.2.scmod')]

def main():
    ap = argparse.ArgumentParser(); ap.add_argument('test', choices=TESTS); ap.add_argument('--mode', default='all')
    ap.add_argument('--package'); ap.add_argument('--label', default=''); ap.add_argument('--limit', type=int, default=1500)
    ap.add_argument('--source', choices=SOURCES, default='1.9.3.1')
    # --no-build: use the test DLL already built (a batch builds once, then runs several isolated runtimes at the same time)
    ap.add_argument('--no-build', action='store_true')
    a = ap.parse_args(); project, result_name = TESTS[a.test]; source = SOURCES[a.source]
    info = json.loads((ROOT / 'Mod/modinfo.json').read_text(encoding='utf-8'))
    package = Path(a.package) if a.package else ROOT / f"dist/MechDefense-{info['Version']}.scmod"
    runtime = ROOT / f"local/runtime-{a.test}{'-' + a.label if a.label else ''}"; mods = runtime / 'Mods'; mods.mkdir(parents=True, exist_ok=True)
    for f in source.iterdir():
        if f.is_file() and f.suffix.lower() in ('.dll', '.exe', '.json', '.zip', '.js', '.xml'): shutil.copy2(f, runtime / f.name)
    if (source / 'runtimes').exists(): shutil.copytree(source / 'runtimes', runtime / 'runtimes', dirs_exist_ok=True)
    print('runtime source:', source)
    if not a.no_build:
        p = subprocess.run(['dotnet', 'build', str(ROOT / f'Tests/{project}/{project}.csproj'), '-c', 'Release', '--nologo', '-v', 'q'], capture_output=True, text=True, encoding='utf-8', errors='replace')
        if p.returncode: print(p.stdout[-3000:]); sys.exit('test build failed')
    for f in mods.iterdir(): f.unlink()
    shutil.copy2(ROOT / package if not package.is_absolute() else package, mods / 'MechDefense.scmod')
    if a.test == 'lab':
        found = [f for d in NEORXNA if d.exists() for f in sorted(d.glob('*Neorxna-v1.4*.scmod')) if ('[API1.9.4' in f.name) == (a.source == '1.9.4')]
        if found: shutil.copy2(found[0], mods / 'Neorxna.scmod')
        print('neorxna:', found[0] if found else 'none')
        if a.mode == 'faction-mods':
            for m in FACTION_MODS:
                if m.exists(): shutil.copy2(m, mods / m.name)
                print('faction mod:', m.name, 'copied' if m.exists() else 'MISSING')
        if a.mode == 'combat':
            for m in COMBAT_MODS:
                if m.exists(): shutil.copy2(m, mods / m.name)
                print('combat mod:', m.name, 'copied' if m.exists() else 'MISSING')
    with zipfile.ZipFile(mods / 'MechDefense.scmod') as z:   # the lab depends on the package under test (an older one too)
        tested = json.loads(z.read('modinfo.json').decode('utf-8-sig'))
    deps = {tested['PackageName']: tested['Version']} if a.test == 'lab' else {}
    with zipfile.ZipFile(mods / f'{project}.scmod', 'w') as z:
        z.write(ROOT / f'Tests/{project}/bin/Release/net10.0/{project}.dll', f'{project}.dll')
        z.writestr('modinfo.json', json.dumps(dict(Name=project, Version='1.0.0', ApiVersion='1.9.3.1', ScVersion='2.4', PackageName='local.' + project.lower(),
                                                   Author='Tests', Description='Isolated tests only', LoadOrder=1300, Dependencies=deps)))
    (runtime / ('smoke-mode.txt' if a.test == 'smoke' else 'lab-mode.txt')).write_text(a.mode, encoding='ascii')
    if a.test == 'smoke' and a.mode == 'old':   # a fresh old world (worlds live in <runtime>/doc/Worlds, inside local/)
        worlds = runtime / 'doc' / 'Worlds'; assert ROOT / 'local' in worlds.parents
        if worlds.exists(): shutil.rmtree(worlds)
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
    # a failed check is a failed run (the result's "passed"), not only a missing result
    try: passed = json.loads(res.read_text(encoding='utf-8')).get('passed') is True if res.exists() else False
    except ValueError: passed = False
    if not passed: sys.exit(f'{a.test} {a.mode}: FAILED (see {res.name})')

if __name__ == '__main__':
    main()
