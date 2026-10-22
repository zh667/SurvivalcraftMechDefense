"""Preview package for tower models built outside the cube pipeline (the polygon towers, 0.9.0).

python tools/stage_tower_preview.py <id> <model.glb> <atlas.png> [<id> <model.glb> <atlas.png> ...] [--base PKG] [--out PATH]
python tools/stage_tower_preview.py --poly <id>[,<id>...] [--base PKG] [--out PATH]
    (--poly takes E:/projects/endfield-turret-model/poly_v1/<id>/model.glb and atlas.png)
  -> local/claude-work/preview/<id>/ (staged Towers folder per tower) and dist/MechDefense-<version>-preview-<id or poly>.scmod

Never touches Mod/Assets. Per tower it stages towers.json (bounds and radius from the new model; cells unchanged), the model,
the atlas, icon.bin (the dropped item: every triangle, unit size, the old icon's face shade) and icon.png (the slot picture,
tools/render_tower_icons.py --towers), then copies the base package with those entries replaced and asset-manifest.json
updated. Each model must keep the tower's node names, parents and translations (checked against the base package).
"""
from pathlib import Path
import hashlib, json, shutil, struct, subprocess, sys, zipfile
import numpy as np

root = Path(__file__).resolve().parents[1]
POLY = Path('E:/projects/endfield-turret-model/poly_v1')
sys.path.insert(0, str(root / 'tools'))
from render_tower_icons import load_glb, shade_of   # noqa: E402  (plain numpy helpers)
from package_formats import webp                     # noqa: E402  (lossless WebP, as package.py writes tower atlases)


def nodes_of(glb_bytes):
    jl, = struct.unpack_from('<I', glb_bytes, 12); js = json.loads(glb_bytes[20:20 + jl])
    return [(n['name'], tuple(round(float(x), 5) for x in n.get('translation', [0, 0, 0])), tuple(n.get('children', []))) for n in js['nodes']]


def write_icon(path, P, N, U):
    """icon.bin like build_tower_assets.write_icon, but with every triangle (a polygon model has a few thousand)."""
    lo, hi = P.reshape(-1, 3).min(0), P.reshape(-1, 3).max(0); P = (P - (lo + hi) / 2) / max(hi - lo)
    n = N / np.linalg.norm(N, axis=1, keepdims=True); shade = shade_of(n)
    V = np.concatenate([P.reshape(-1, 3), U.reshape(-1, 2), np.repeat(shade, 3)[:, None]], 1).astype('<f4')
    V, I = np.unique(V, axis=0, return_inverse=True)                               # corners shared by triangles stored once
    I = I.reshape(-1, 3)[:, [0, 2, 1]].reshape(-1).astype('<i4')                   # glTF CCW -> engine front (CW)
    Path(path).write_bytes(struct.pack('<ii', len(V), len(I)) + V.tobytes() + I.tobytes())


def stage(zi, runtime, tid, glb, atlas):
    """Stage one tower; returns its new runtime entry and {package path: bytes}."""
    st = root / 'local/claude-work/preview' / tid; towers = st / 'Towers'
    if st.exists(): shutil.rmtree(st)
    (towers / tid).mkdir(parents=True)
    entry = dict(next(t for t in runtime['towers'] if t['id'] == tid))
    new_glb = Path(glb).read_bytes(); old_nodes = nodes_of(zi.read(f'Assets/Towers/{tid}/model.glb'))
    assert nodes_of(new_glb) == old_nodes, f'{tid}: node names / translations / children differ: {nodes_of(new_glb)} vs {old_nodes}'
    shutil.copy2(glb, towers / tid / 'model.glb'); shutil.copy2(atlas, towers / tid / 'atlas.png')
    P, N, U = load_glb(towers / tid / 'model.glb')
    q = P.reshape(-1, 3); lo, hi = q.min(0), q.max(0)
    old = entry['bounds']; entry['bounds'] = [[round(float(v), 4) for v in lo], [round(float(v), 4) for v in hi]]
    entry['radius'] = round(float(np.linalg.norm(q, axis=1).max()) + .1, 3)
    grow = max(max(o - n for o, n in zip(old[0], entry['bounds'][0])), max(n - o for o, n in zip(old[1], entry['bounds'][1])))
    print(f'{tid}: {len(P)} triangles, bounds {old} -> {entry["bounds"]} (grows {grow:.3f} m), radius {entry["radius"]}')
    if grow > .05: print(f'WARNING {tid}: the model is more than 5 cm outside the old bounds; cells', entry['cells'], 'were kept')
    (towers / 'towers.json').write_text(json.dumps({**runtime, 'towers': [entry]}, ensure_ascii=False, indent=1), encoding='utf-8')
    write_icon(towers / tid / 'icon.bin', P, N, U)
    r = subprocess.run([sys.executable, str(root / 'tools/render_tower_icons.py'), '--towers', str(towers), tid],
                       capture_output=True, text=True, encoding='utf-8', errors='replace', creationflags=0x08000000)
    assert r.returncode == 0 and (towers / tid / 'icon.png').exists(), r.stderr[-1500:]
    files = {f'Assets/Towers/{tid}/{f}': (towers / tid / f).read_bytes() for f in ('model.glb', 'icon.bin', 'icon.png')}
    if f'Assets/Towers/{tid}/atlas.webp' in zi.namelist(): files[f'Assets/Towers/{tid}/atlas.webp'] = webp(towers / tid / 'atlas.png')
    else: files[f'Assets/Towers/{tid}/atlas.png'] = (towers / tid / 'atlas.png').read_bytes()
    return entry, files


def main():
    args = sys.argv[1:]; opts = {}
    for k in ('--base', '--out', '--poly'):
        if k in args: i = args.index(k); opts[k] = args[i + 1]; del args[i:i + 2]
    jobs = [(tid, POLY / tid / 'model.glb', POLY / tid / 'atlas.png') for tid in opts['--poly'].split(',')] if '--poly' in opts else \
        [(args[i], Path(args[i + 1]), Path(args[i + 2])) for i in range(0, len(args), 3)]
    assert jobs and all(len(j) == 3 for j in jobs), __doc__
    version = json.loads((root / 'Mod/modinfo.json').read_text(encoding='utf-8'))['Version']
    base = root / opts.get('--base', f'dist/MechDefense-{version}.scmod')
    name = jobs[0][0] if len(jobs) == 1 else 'poly'
    out = root / opts.get('--out', f'dist/MechDefense-{version}-preview-{name}.scmod')
    zi = zipfile.ZipFile(base); runtime = json.loads(zi.read('Assets/Towers/towers.json'))
    new = {}; entries = {}
    for tid, glb, atlas in jobs:
        entries[tid], files = stage(zi, runtime, tid, glb, atlas); new.update(files)
    full = dict(runtime); full['towers'] = [entries.get(t['id'], t) for t in runtime['towers']]
    new['Assets/Towers/towers.json'] = json.dumps(full, ensure_ascii=False, indent=1).encode('utf-8')
    man = json.loads(zi.read('asset-manifest.json'))
    for m in man:
        if m['path'] in new: m['bytes'] = len(new[m['path']]); m['sha256'] = hashlib.sha256(new[m['path']]).hexdigest()
    missing = set(new) - {m['path'] for m in man}; assert not missing, missing
    with zipfile.ZipFile(out, 'w', zipfile.ZIP_DEFLATED) as zo:
        for info in zi.infolist():
            if info.filename in new: zo.writestr(info.filename, new[info.filename])
            elif info.filename == 'asset-manifest.json': zo.writestr(info.filename, json.dumps(man, indent=2))
            else: zo.writestr(info, zi.read(info.filename))
    print('preview', out, out.stat().st_size, hashlib.sha256(out.read_bytes()).hexdigest(), 'towers', [j[0] for j in jobs])


if __name__ == '__main__':
    main()
