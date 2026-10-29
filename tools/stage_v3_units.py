"""Copy our own v3 enemy models (0.14.0) into Mod/Assets/Imported/<UNIT>/ (ignored; run before tools/package.py).

python tools/stage_v3_units.py [RD14 ...]      (default: every unit below)

Source: E:/projects/dguns-enemy-models/v3_new/<unit>/ (built and checked there, see its README.md / HANDOFF.md): the game LOD
model*.glb (one per paint scheme), game/unit.json, game/clips.bin, game/icon.bin, game/icon.png, checks/SUMMARY.json (must say
all_ok; copied as v3_checks.json, read by package.py). Prints the SHA-256 of every file copied.
"""
import hashlib, json, shutil, sys
from pathlib import Path
ROOT = Path(__file__).resolve().parents[1]
SRC = Path(r'E:/projects/dguns-enemy-models/v3_new')
UNITS = ['RD14']

def main():
    units = [a.upper() for a in sys.argv[1:]] or UNITS
    for unit in units:
        src = SRC / unit.lower(); dst = ROOT / 'Mod/Assets/Imported' / unit; dst.mkdir(parents=True, exist_ok=True)
        summary = json.loads((src / 'checks/SUMMARY.json').read_text(encoding='utf-8'))
        assert summary.get('all_ok') is True, f'{unit}: checks/SUMMARY.json is not all_ok'
        u = json.loads((src / 'game/unit.json').read_text(encoding='utf-8'))
        files = [(src / f'{s}.glb', f'{s}.glb') for s in (u.get('skins') or ['model'])]
        files += [(src / 'game' / f, f) for f in ('unit.json', 'clips.bin', 'icon.bin', 'icon.png')] + [(src / 'checks/SUMMARY.json', 'v3_checks.json')]
        for a, name in files:
            assert a.exists(), a
            shutil.copy2(a, dst / name); print(unit, name, a.stat().st_size, hashlib.sha256(a.read_bytes()).hexdigest()[:16])

if __name__ == '__main__':
    main()
