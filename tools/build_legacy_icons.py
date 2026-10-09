"""3D inventory icons for the legacy Destroy Gunners mech/enemy (Imported/<Mech|Enemy>/model.json, rest pose).

Same icon.bin format as build_tower_assets.write_icon; their source winding is mixed, so every kept
triangle is written in both windings. Output: Mod/Assets/Towers/_legacy/<name>_icon.bin.
"""
import json, struct
from pathlib import Path
import numpy as np
ROOT = Path(__file__).resolve().parents[1]

def main():
    out = ROOT / 'Mod/Assets/Towers/_legacy'; out.mkdir(parents=True, exist_ok=True)
    for name in ('Mech', 'Enemy'):
        m = json.loads((ROOT / f'Mod/Assets/Imported/{name}/model.json').read_text())
        V = m['vertices']; I = np.array(m['indices']).reshape(-1, 3)
        P = np.array([v['p'] for v in V], float)[I]; U = np.array([v['uv'] for v in V], float)[I]
        n = np.cross(P[:, 1] - P[:, 0], P[:, 2] - P[:, 0]); area = np.linalg.norm(n, axis=1) / 2
        keep = np.argsort(-area)[:1200]; P, U, n = P[keep], U[keep], n[keep] / np.maximum(1e-9, 2 * area[keep, None])
        lo, hi = P.reshape(-1, 3).min(0), P.reshape(-1, 3).max(0); P = (P - (lo + hi) / 2) / max(hi - lo)
        shade = np.where(np.abs(n[:, 1]) > .6, .95, .78)
        Vt = np.concatenate([P.reshape(-1, 3), U.reshape(-1, 2), np.repeat(shade, 3)[:, None]], 1).astype('<f4')
        tri = np.arange(len(Vt), dtype='<i4').reshape(-1, 3); Ix = np.concatenate([tri, tri[:, [0, 2, 1]]]).reshape(-1)
        (out / f'{name.lower()}_icon.bin').write_bytes(struct.pack('<ii', len(Vt), len(Ix)) + Vt.tobytes() + Ix.astype('<i4').tobytes())
        print(name, len(keep), 'triangles')

if __name__ == '__main__':
    main()
