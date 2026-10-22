"""Preview package for a remodelled hero mech (0.9.0 round 13: the polygon 水星).

python tools/stage_mech_preview.py [--mech Mercury] --dir E:/projects/dguns-mech-model/v10_poly/mercury [--base PKG] [--out PATH]
  --dir holds model.glb and game/icon.bin, game/icon.png (the inventory icon of the new model, tools/build_mech_assets.py format)
  -> dist/MechDefense-<version>-preview-<mech>.scmod

Never touches Mod/Assets. Copies the base package with Assets/Imported/<mech>/model.glb (texture re-encoded as WebP quality 90,
as package.py does), icon.bin and icon.webp replaced and asset-manifest.json updated. mech.json, clips.bin and the sounds stay:
the new model must have the same node tree (names, children, translations, rotations, scales) as the one in the base package,
so the clips, muzzles, cockpit and feet keep working.
"""
from pathlib import Path
import hashlib, json, struct, sys, zipfile

root = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(root / 'tools'))
from webp_glb import webp_glb            # noqa: E402
from package_formats import webp          # noqa: E402


def nodes_of(glb):
    jl, = struct.unpack_from('<I', glb, 12); js = json.loads(glb[20:20 + jl])
    r = lambda v: tuple(round(float(x), 5) for x in v) if v is not None else None
    return [(n['name'], tuple(n.get('children', [])), r(n.get('translation')), r(n.get('rotation')), r(n.get('scale'))) for n in js['nodes']]


def main():
    args = sys.argv[1:]; opts = {'--mech': 'Mercury'}
    for k in ('--mech', '--dir', '--base', '--out'):
        if k in args: i = args.index(k); opts[k] = args[i + 1]; del args[i:i + 2]
    assert '--dir' in opts and not args, __doc__
    src = Path(opts['--dir']); mech = opts['--mech']
    version = json.loads((root / 'Mod/modinfo.json').read_text(encoding='utf-8'))['Version']
    base = root / opts.get('--base', f'dist/MechDefense-{version}.scmod')
    out = root / opts.get('--out', f'dist/MechDefense-{version}-preview-{mech.lower()}.scmod')
    zi = zipfile.ZipFile(base); pre = f'Assets/Imported/{mech}/'
    glb = (src / 'model.glb').read_bytes()
    old, new_nodes = nodes_of(zi.read(pre + 'model.glb')), nodes_of(glb)
    assert old == new_nodes, f'node tree differs: {[(a, b) for a, b in zip(old, new_nodes) if a != b][:5]} ({len(old)} vs {len(new_nodes)} nodes)'
    new = {pre + 'model.glb': webp_glb(glb, 90), pre + 'icon.bin': (src / 'game/icon.bin').read_bytes(),
           pre + 'icon.webp': webp(src / 'game/icon.png', lossless=False)}
    man = json.loads(zi.read('asset-manifest.json'))
    for m in man:
        if m['path'] in new: m['bytes'] = len(new[m['path']]); m['sha256'] = hashlib.sha256(new[m['path']]).hexdigest()
    missing = set(new) - {m['path'] for m in man}; assert not missing, missing
    with zipfile.ZipFile(out, 'w', zipfile.ZIP_DEFLATED) as zo:
        for info in zi.infolist():
            if info.filename in new: zo.writestr(info.filename, new[info.filename])
            elif info.filename == 'asset-manifest.json': zo.writestr(info.filename, json.dumps(man, indent=2))
            else: zo.writestr(info, zi.read(info.filename))
    print('nodes', len(new_nodes), 'identical; model', len(zi.read(pre + 'model.glb')), '->', len(new[pre + 'model.glb']), 'bytes in the package')
    print('preview', out, out.stat().st_size, hashlib.sha256(out.read_bytes()).hexdigest())


if __name__ == '__main__':
    main()
