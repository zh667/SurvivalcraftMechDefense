"""Copy our own v3 enemy models (0.14.0) into Mod/Assets/Imported/<UNIT>/ (ignored; run before tools/package.py).

python tools/stage_v3_units.py [RD14 ...]      (default: every unit below)

Source: E:/projects/dguns-enemy-models/v3_new/<unit>/ (built and checked there, see its README.md / HANDOFF.md): the game LOD
model*.glb (one per paint scheme), game/unit.json, game/clips.bin, game/icon.bin, game/icon.png, checks/SUMMARY.json (must say
all_ok, or the unit is in ACCEPTED below with the gates it misses; copied as v3_checks.json, read by package.py). Prints the
SHA-256 of every file copied.
"""
import hashlib, json, shutil, sys
from pathlib import Path
ROOT = Path(__file__).resolve().parents[1]
SRC = Path(r'E:/projects/dguns-enemy-models/v3_new')
UNITS = ['RD14', 'RD09', 'EN02', 'RD13', 'RD21', 'RD08', 'RD19']
# units shipped although their SUMMARY.json misses a gate: what it misses (from the unit's README.md / HANDOFF.md), written into
# v3_checks.json as "accepted"
ACCEPTED = {
    'RD09': 'outline side view 3.62 % of the height (gate 3 %): the level duct lip against the original lower rear rim',
    'EN02': 'outline side view 3.70 % (gate 3 %); see-through 558 px (gate 405) through the open ducted fans; '
            '2 coplanar overlaps of 1.5 cm2 (sight bracket end inside the barrel)',
    # 0.14.0 bosses (their README.md "没过的检查"; no gate relaxed)
    'RD13': 'LOD1 coplanar 27.8 cm2 at the rear mount trunnion pin; see-through 2601 px (original 1347); cannon-mount cheeks 2.4 cm '
            'from the centre block; LOD0 plate coplanars',
    'RD21': 'outline side view 3.08 % in LOD1 (4.11 % LOD0 / high) at the tail-spike root; death clip clash 14.4 vs 11.6 cm (right '
            'forearm into the body); coplanar 181 cm2 per side between upper-arm sleeve and body in idle; 1 open edge on a body shell; '
            'railgun white share -12.7 points (gate 10); LOD0 352 triangles under the budget',
    'RD08': 'see-through on LOD0 only (2430 px, limit 2321; LOD0 does not ship); every LOD1 check passes',
    'RD19': 'see-through 3285 px in 6 of 26 views (original 0): 1-2 cm slivers between the open front lids and the rail tops, seen '
            'level or from below; LOD0 (not shipped): 2 bolt/plate z-fights on the turret cheeks, a 1.6 cm gap at the sight lens',
}

def main():
    units = [a.upper() for a in sys.argv[1:]] or UNITS
    for unit in units:
        src = SRC / unit.lower(); dst = ROOT / 'Mod/Assets/Imported' / unit; dst.mkdir(parents=True, exist_ok=True)
        summary = json.loads((src / 'checks/SUMMARY.json').read_text(encoding='utf-8'))
        assert summary.get('all_ok') is True or unit in ACCEPTED, f'{unit}: checks/SUMMARY.json is not all_ok'
        u = json.loads((src / 'game/unit.json').read_text(encoding='utf-8'))
        files = [(src / f'{s}.glb', f'{s}.glb') for s in (u.get('skins') or ['model'])]
        files += [(src / 'game' / f, f) for f in ('unit.json', 'clips.bin', 'icon.bin', 'icon.png')]
        for a, name in files:
            assert a.exists(), a
            shutil.copy2(a, dst / name); print(unit, name, a.stat().st_size, hashlib.sha256(a.read_bytes()).hexdigest()[:16])
        if summary.get('all_ok') is not True: summary['accepted'] = ACCEPTED[unit]
        (dst / 'v3_checks.json').write_text(json.dumps(summary, ensure_ascii=False, indent=1), encoding='utf-8')
        print(unit, 'v3_checks.json', 'all_ok' if summary.get('all_ok') is True else 'accepted: ' + summary['accepted'])

if __name__ == '__main__':
    main()
