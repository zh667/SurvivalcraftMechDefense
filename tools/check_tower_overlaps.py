"""Z-fight check on built tower glTFs: same-facing triangles closer than 4 mm that overlap (rest pose).

python tools/check_tower_overlaps.py [ids...]  -> counts per tower, split into same-part / cross-part pairs.
"""
import json, struct, sys
from collections import defaultdict
from pathlib import Path
import numpy as np
from shapely.geometry import Polygon
from shapely.strtree import STRtree
ROOT = Path(__file__).resolve().parents[1]; T = ROOT / 'Mod/Assets/Towers'

def load(tid):
    b = (T / tid / 'model.glb').read_bytes(); jl = struct.unpack_from('<I', b, 12)[0]; js = json.loads(b[20:20 + jl]); off = 20 + jl + 8
    def acc(i):
        a = js['accessors'][i]; v = js['bufferViews'][a['bufferView']]; n = {'VEC3': 3, 'VEC2': 2, 'SCALAR': 1}[a['type']]
        dt = {5126: '<f4', 5125: '<u4', 5123: '<u2'}[a['componentType']]
        return np.frombuffer(b, dt, a['count'] * n, off + v['byteOffset']).reshape(a['count'], n)
    nodes = js['nodes']; world = {}
    def walk(i, t):
        world[i] = t + np.array(nodes[i].get('translation', [0, 0, 0]))
        for c in nodes[i].get('children', []): walk(c, world[i])
    walk(0, np.zeros(3)); tris = []
    for i, n in enumerate(nodes):
        if 'mesh' not in n: continue
        pr = js['meshes'][n['mesh']]['primitives'][0]; P = acc(pr['attributes']['POSITION']).astype(float) + world[i]; I = acc(pr['indices']).reshape(-1, 3)
        for t in P[I]: tris.append((n['name'], t))
    return tris

def check(tid, eps=0.004):
    tris = load(tid); items = []
    for k, (part, t) in enumerate(tris):
        n = np.cross(t[1] - t[0], t[2] - t[0]); a = np.linalg.norm(n)
        if a < 1e-10: continue
        n /= a; ax = int(np.argmax(abs(n)))
        if abs(n[ax]) < .9999: continue
        items.append((k, ax, int(np.sign(n[ax])), float(t[0][ax]), part))
    groups = defaultdict(list)
    for it in items: groups[(it[1], it[2])].append(it)
    same = cross = 0; worst = []
    for (ax, sg), lst in groups.items():
        axes = [j for j in range(3) if j != ax]
        polys = [Polygon(tris[k][1][:, axes]) for k, *_ in lst]; tree = STRtree(polys); offs = np.array([x[3] for x in lst])
        for a, (ka, _, _, da, pa) in enumerate(lst):
            for b in tree.query(polys[a]):
                if b <= a or abs(offs[b] - da) >= eps: continue
                inter = polys[a].intersection(polys[b]).area
                if inter < 1e-6: continue                      # < 1 mm^2
                if pa == lst[b][4]: same += 1
                else: cross += 1
                worst.append((inter, abs(offs[b] - da), pa, lst[b][4]))
    worst.sort(reverse=True)
    return dict(triangles=len(tris), same_part_pairs=same, cross_part_pairs=cross, worst=[f'{p}/{q} gap {g * 1000:.2f} mm area {a * 1e4:.1f} cm2' for a, g, p, q in worst[:5]])

if __name__ == '__main__':
    ids = sys.argv[1:] or [t['id'] for t in json.loads((T / 'towers.json').read_text(encoding='utf-8'))['towers']]
    for tid in ids: print(tid, json.dumps(check(tid), ensure_ascii=False), flush=True)
