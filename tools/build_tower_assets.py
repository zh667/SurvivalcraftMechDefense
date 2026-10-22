"""Authored Endfield cube towers -> Survivalcraft runtime assets.

python tools/build_tower_assets.py [ids...] [--runtime-only]   (default: every tower in tools/towers.json)
A tower with "poly" in tools/towers.json (a folder under E:/projects/endfield-turret-model, e.g. poly_v1/battle_rocket_1)
ships that polygon model and atlas instead of the cube build; the cube build still runs and must produce the same node
tree (names, parents, translations), because pivots, muzzles, links and the pitch clearance come from it.
A tower with "polyOnly" (a folder with model.glb, atlas.png and parts.json; 0.9.0 round 6, the towers of
E:/projects/sog-tower-model) has no cube build at all: node tree and pivots come from its model, muzzles and the clash-free
pitch range from parts.json, the footprint from its base part. It is rebuilt on every run (well under a second).
Each built tower also leaves local/tower-meta/<id>.json, so towers can be built in subsets or parallel jobs and a
final --runtime-only run assembles towers.json from them (used when they still match the source geo and atlas).

For each tower: one glTF binary with a node per rigid part (base / yaw / pitch / animated
sub-parts), the authored pixel atlas, and Mod/Assets/Towers/towers.json metadata (pivots,
muzzles, clip-free pitch range, footprint). Outputs go to Mod/Assets/Towers (generated, ignored).

Every cube is first made a closed box: the authored geo leaves out faces it thought hidden (tube
slabs keep only their outer side, plates only their front), and Blender's two-sided view hides the
holes that leaves. Missing faces get a flat colour from the cube's own texture.

Hidden-surface clean-up, per rigid part: a face region is dropped when solid cubes of the same
part fill the space just in front of it. The authored cube models stack faces 0.006-0.5 mm
apart (bevel slabs, plates, rings). Blender close-ups hide that; in game it z-fights ("colour
changes" while the camera moves). Pieces floating up to SNAP_GAP in front of a parallel surface are
grown back onto it (cutting the surface under them opened slits); exact ties keep the smaller face
(the decal). Cubes of two parts whose faces still coincide at rest are grown NUDGE apart, the moving
part in front (moving only the face left a slit around it).
Units: SC metres, X right, Y up, -Z forward (authoring frame x forward, y left, z up, 16 px = 1 m).
"""
from __future__ import annotations
import hashlib, importlib, json, math, re, shutil, struct, sys, time
from pathlib import Path
import numpy as np
from shapely.geometry import MultiPoint, Polygon
from shapely.ops import unary_union
from shapely import constrained_delaunay_triangles
import shapely
from PIL import Image

ROOT = Path(__file__).resolve().parents[1]
AUTHOR = Path('E:/projects/endfield-turret-model')
OUT = ROOT / 'Mod/Assets/Towers'
META = ROOT / 'local/tower-meta'                 # per-tower build metadata (ignored)
SPEC = json.loads((ROOT / 'tools/towers.json').read_text(encoding='utf-8'))
EPS_COVER = 1e-5      # m: probe plane in front of a face (smaller than the 6e-6..5e-4 m stacking gaps? see tie)
EPS_TIE = 1e-5        # m: faces closer than this are one plane; priority decides
FIGHT = 0.006         # m: same-facing faces closer than this z-fight at range (24-bit depth, near 0.1)
NUDGE = 0.003         # m: cross-part coincident faces are pushed this far apart (24-bit depth resolves 3 mm to ~70 m; larger steps read as seams)
CROSS_GAP = 0.006     # m: cross-part faces closer than this get nudged
SNAP_GAP = 0.0125     # m: same-part pieces this close (0.2 model px; designed gaps are whole or half pixels) are grown shut
PART_ORDER = {'base': 0, 'yaw': 1, 'pitch': 2}
TRACE = bool(__import__('os').environ.get('MD_TRACE'))     # also write local/claude-work/trace/<id>.json (triangle -> source cube)

def sha(p): return hashlib.sha256(Path(p).read_bytes()).hexdigest()

# ------------------------------------------------------------------ authoring conventions
MBB = np.array([[0, -1, 0], [0, 0, 1], [-1, 0, 0]], float)  # model frame -> Bedrock (= blocky.MBB)

def rx(a): c, s = math.cos(a), math.sin(a); return np.array([[1, 0, 0], [0, c, -s], [0, s, c]])
def ry(a): c, s = math.cos(a), math.sin(a); return np.array([[c, 0, s], [0, 1, 0], [-s, 0, c]])
def rz(a): c, s = math.cos(a), math.sin(a); return np.array([[c, -s, 0], [s, c, 0], [0, 0, 1]])
def bb_rot(r): return rz(math.radians(r[2])) @ ry(math.radians(-r[1])) @ rx(math.radians(-r[0]))

def model_to_sc(p):
    """authoring model frame (px, x fwd, y left, z up) -> SC metres (X right, Y up, Z back)."""
    p = np.asarray(p, float)
    return np.stack([-p[..., 1], p[..., 2], -p[..., 0]], -1) / 16.0

def bb_point_to_model(b):
    """Bedrock raw (file) coordinates -> model frame, as blocky.texel_points does."""
    return MBB.T @ np.asarray(b, float)

FACE_Q = {  # TL, TR, BL corners per Bedrock face key (blocky.texel_points)
    'east': lambda x0, y0, z0, x1, y1, z1: [(x1, y1, z1), (x1, y1, z0), (x1, y0, z1)],
    'west': lambda x0, y0, z0, x1, y1, z1: [(x0, y1, z0), (x0, y1, z1), (x0, y0, z0)],
    'up': lambda x0, y0, z0, x1, y1, z1: [(x0, y1, z0), (x1, y1, z0), (x0, y1, z1)],
    'down': lambda x0, y0, z0, x1, y1, z1: [(x0, y0, z1), (x1, y0, z1), (x0, y0, z0)],
    'south': lambda x0, y0, z0, x1, y1, z1: [(x0, y1, z1), (x1, y1, z1), (x0, y0, z1)],
    'north': lambda x0, y0, z0, x1, y1, z1: [(x1, y1, z0), (x0, y1, z0), (x1, y0, z0)],
}

OPPOSITE = {'east': 'west', 'west': 'east', 'up': 'down', 'down': 'up', 'north': 'south', 'south': 'north'}
BOUND = {'east': (0, 1), 'west': (0, 0), 'up': (1, 1), 'down': (1, 0), 'south': (2, 1), 'north': (2, 0)}   # FACE_Q plane per key

def cube_geometry(cube, grow=None):
    """-> (8 corners SC, faces {key: (TL, TR, BL) SC}). grow: {face key: metres} moves that face outward."""
    o, s = cube['origin'], cube['size']
    b = [[-(o[0] + s[0]), -o[0]], [o[1], o[1] + s[1]], [o[2], o[2] + s[2]]]
    for key, m in (grow or {}).items():
        ax, side = BOUND[key]; b[ax][side] += 16 * m if side else -16 * m
    (x0, x1), (y0, y1), (z0, z1) = b
    M = np.eye(3); cp = np.zeros(3)
    if 'rotation' in cube:
        M = bb_rot(cube['rotation']); cp = np.array([-cube['pivot'][0], cube['pivot'][1], cube['pivot'][2]], float)
    def to_sc(p): return model_to_sc(MBB.T @ (M @ (np.asarray(p, float) - cp) + cp))
    corners = np.array([to_sc((x, y, z)) for x in (x0, x1) for y in (y0, y1) for z in (z0, z1)])
    faces = {k: tuple(to_sc(p) for p in q(x0, y0, z0, x1, y1, z1)) for k, q in FACE_Q.items()}
    return corners, faces

class Box:
    """Oriented box from its 8 corners (x-major order as cube_geometry emits)."""
    def __init__(self, corners):
        c = corners; self.corners = c; self.c = c.mean(0)
        ex, ey, ez = c[4] - c[0], c[2] - c[0], c[1] - c[0]
        self.h = np.array([np.linalg.norm(ex), np.linalg.norm(ey), np.linalg.norm(ez)]) / 2
        self.a = np.stack([ex / max(1e-12, 2 * self.h[0]), ey / max(1e-12, 2 * self.h[1]), ez / max(1e-12, 2 * self.h[2])])
        self.lo = c.min(0); self.hi = c.max(0)
    def extent(self, n): return float(np.sum(np.abs(self.a @ n) * self.h))
    def section(self, n, d, e1, e2):
        """Polygon (2D in e1/e2) of this box cut by the plane n.x = d, or None."""
        s = self.corners @ n - d
        if s.max() <= 0 or s.min() >= 0: return None
        pts = []
        for i in range(8):
            for bit in (4, 2, 1):
                j = i | bit
                if j == i: continue
                if s[i] * s[j] < 0:
                    t = s[i] / (s[i] - s[j]); pts.append(self.corners[i] + t * (self.corners[j] - self.corners[i]))
        if len(pts) < 3: return None
        P = np.array(pts); hull = MultiPoint([(float(p @ e1), float(p @ e2)) for p in P]).convex_hull
        return hull if hull.geom_type == 'Polygon' and hull.area > 1e-12 else None

def sat_depth(A, B):
    """Smallest overlap over all separating axes = penetration depth estimate (<= 0: apart)."""
    d = B.c - A.c; axes = list(A.a) + list(B.a); best = 1e9
    for i in range(3):
        for j in range(3):
            v = np.cross(A.a[i], B.a[j]); n = np.linalg.norm(v)
            if n > 1e-9: axes.append(v / n)
    for L in axes:
        best = min(best, A.extent(L) + B.extent(L) - abs(d @ L))
        if best <= 0: return best
    return best

def sat_overlap(A, B, tol=0.0):
    """True when two boxes interpenetrate by more than tol along every separating axis."""
    d = B.c - A.c; axes = list(A.a) + list(B.a)
    for i in range(3):
        for j in range(3):
            v = np.cross(A.a[i], B.a[j]); n = np.linalg.norm(v)
            if n > 1e-9: axes.append(v / n)
    for L in axes:
        if abs(d @ L) - A.extent(L) - B.extent(L) > -tol: return False
    return True

# ------------------------------------------------------------------ source loading
def purge_modules():
    for m in list(sys.modules):
        if m.startswith(('fam_', 'famB', 'A_')) or m in ('blocky', 'bevel', 'build_family', 'turret_parts', 'check_floating') or m.endswith(('_C', '_D')):
            del sys.modules[m]

def authored_labels(spec, geo_bones):
    """Semantic labels per Bedrock cube, rebuilt from the authoring module and proven identical."""
    purge_modules(); tools = str(AUTHOR / spec['tools']); sys.path.insert(0, tools)
    try:
        K = importlib.import_module('blocky')
        if spec['tools'].startswith('v1'):
            T = importlib.import_module('turret_parts'); bones, _, _ = T.build(spec['id'])
        else:
            import os; os.environ['TURRET_VERSION'] = spec['version']
            BF = importlib.import_module('build_family'); bones, _, _, _ = BF.load_family(spec['id'])
        order = [b for b in ('base', 'yaw', 'pitch') if bones.get(b)]
        allc = [c for b in order for c in bones[b]]
        rebuilt, _ = K.to_bedrock(allc, 2.5)
    finally:
        sys.path.remove(tools)
    existing = [c for b in order for c in geo_bones[b]['cubes']]
    assert len(rebuilt) == len(existing), (spec['id'], len(rebuilt), len(existing))
    for a, b in zip(rebuilt, existing):
        for k in ('origin', 'size', 'pivot', 'rotation'):
            assert np.allclose(a.get(k, [0, 0, 0]), b.get(k, [0, 0, 0]), atol=1.1e-4), (spec['id'], k)
    return [c.get('label') or '' for c in allc]

def assign_parts(spec, geo_bones, labels):
    """Every cube -> part name. Sub-parts are carved from a geo bone by label regex (+ optional side)."""
    owner = []; i = 0
    for b in ('base', 'yaw', 'pitch'):
        for _ in geo_bones.get(b, {}).get('cubes', []):
            owner.append(b); i += 1
    cubes = [c for b in ('base', 'yaw', 'pitch') for c in geo_bones.get(b, {}).get('cubes', [])]
    for tr in spec.get('trim', []):              # {"labels": regex, "minY"/"maxY": metres}: shave hidden overlap off unrotated cubes
        rx_ = re.compile(tr['labels'])
        for k, lab in enumerate(labels):
            if not rx_.search(lab): continue
            c = cubes[k]; assert 'rotation' not in c, (spec['id'], lab)
            lo, hi = c['origin'][1], c['origin'][1] + c['size'][1]
            lo = max(lo, tr.get('minY', -1e9) * 16); hi = min(hi, tr.get('maxY', 1e9) * 16)
            assert hi > lo, (spec['id'], lab); c['origin'][1] = lo; c['size'][1] = hi - lo
    for mv in spec.get('reassign', []):          # {"labels": regex, "from": bone, "to": bone}
        rx_ = re.compile(mv['labels'])
        for k, lab in enumerate(labels):
            if owner[k] == mv['from'] and rx_.search(lab): owner[k] = mv['to']
    for sub in spec.get('subparts', []):
        rx_ = re.compile(sub['labels'])
        for k, (lab, own) in enumerate(zip(labels, owner)):
            if own != sub['from'] or not rx_.search(lab): continue
            if 'side' in sub:   # +X = right in SC; Bedrock raw x is mirrored, so raw origin x > 0 means SC right
                corners, _ = cube_geometry(cubes[k]); x = corners[:, 0].mean()
                if (x > 0) != (sub['side'] == 'right'): continue
            owner[k] = sub['name']
    return cubes, owner

# ------------------------------------------------------------------ faces
class Face:
    __slots__ = ('part', 'cube', 'key', 'n', 'd', 'e1', 'e2', 'poly', 'area', 'uvmap', 'tl')
    def __init__(self, part, cube, key, TL, TR, BL, uvrect, tex, centre):
        self.part, self.cube, self.key = part, cube, key
        BR = TR + BL - TL
        n = np.cross(TR - TL, BL - TL); ln = np.linalg.norm(n); n = n / ln
        if n @ ((TR + BL) / 2 - centre) < 0: n = -n          # outward, as blocky.texel_points
        e1 = (TR - TL) / np.linalg.norm(TR - TL); e2 = np.cross(n, e1)
        self.n, self.e1, self.e2, self.tl = n, e1, e2, TL
        self.d = float(n @ TL)
        pts = [TL, TR, BR, BL]
        self.poly = Polygon([(float(p @ e1), float(p @ e2)) for p in pts]); self.area = self.poly.area
        u0, v0, w, h = uvrect; W, H = tex
        uv = np.array([[u0, v0], [u0 + w, v0], [u0 + w, v0 + h], [u0, v0 + h]], float) / [W, H]
        A = np.array([[p @ e1, p @ e2, 1.0] for p in pts]); self.uvmap = np.linalg.lstsq(A, uv, rcond=None)[0]
    def lift(self, xy):
        """2D plane coords -> 3D: point = x e1 + y e2 + d n (e1, e2, n orthonormal)."""
        xy = np.asarray(xy, float); return xy[:, :1] * self.e1 + xy[:, 1:2] * self.e2 + self.d * self.n

def uv_rect(f):
    (u, v), (du, dv) = f['uv'], f['uv_size']; return (min(u, u + du), min(v, v + dv), abs(du), abs(dv))

def flat_texel(atlas, rect):
    """Zero-size uv rect on the most common opaque colour of an authored face (fill for restored faces)."""
    u0, v0, w, h = rect; H, W = atlas.shape[:2]
    a = atlas[int(v0):max(int(v0) + 1, int(math.ceil(v0 + h))), int(u0):max(int(u0) + 1, int(math.ceil(u0 + w)))].reshape(-1, 4)
    ok = a[:, 3] > 127; cand = a[ok] if ok.any() else a
    vals, inv, cnt = np.unique(cand[:, :3], axis=0, return_inverse=True, return_counts=True)
    i = int(np.flatnonzero(ok)[np.flatnonzero(inv.reshape(-1) == cnt.argmax())[0]]) if ok.any() else 0
    cols = max(int(math.ceil(u0 + w)) - int(u0), 1)
    return (int(u0) + i % cols + .5, int(v0) + i // cols + .5, 0.0, 0.0)

def faces_of(cubes, owner, tex, atlas, grow={}):
    """All six faces of every cube: authored ones with their uv, missing ones flat-coloured (restored)."""
    faces, boxes = [], []
    for k, cube in enumerate(cubes):
        corners, fq = cube_geometry(cube, grow.get(k)); boxes.append(Box(corners)); uv = cube['uv']
        for key in FACE_Q:
            TL, TR, BL = fq[key]
            if np.linalg.norm(np.cross(TR - TL, BL - TL)) < 1e-10: continue      # zero-thickness side
            src = uv.get(key) or uv.get(OPPOSITE[key]) or next(iter(uv.values()))
            rect = uv_rect(src) if key in uv else flat_texel(atlas, uv_rect(src))
            # blocky painted texel (i, j) of every face at TL + i (TR - TL) + j (BL - TL), from the rect's top-left
            faces.append(Face(owner[k], k, key, TL, TR, BL, rect, tex, boxes[-1].c))
    return faces, boxes

def snap_plates(faces, boxes, owner):
    """{cube: {key: metres}}: a cube whose back floats (EPS_TIE, SNAP_GAP] in front of a parallel surface of the
    same part grows back onto it (0.1 mm into it): no hairline gap, and the surface under it is hidden by a
    solid instead of being cut open (the cut left a slit you could see through)."""
    grow = {}; by_cube = {}
    for f in faces: by_cube.setdefault(f.cube, {})[f.key] = f
    los = np.array([b.lo for b in boxes]); his = np.array([b.hi for b in boxes]); own = np.array(owner)
    for f in faces:
        pts = f.lift(np.array(f.poly.exterior.coords)[:4])
        lo, hi = pts.min(0) - FIGHT, pts.max(0) + FIGHT
        for j in np.flatnonzero(np.all((los <= hi) & (his >= lo), 1) & (own == f.part)):
            if j == f.cube: continue
            for key, b in by_cube.get(j, {}).items():          # b: plate's back face, facing f
                if b.n @ f.n > -0.9995 or (b.area, j) > (f.area, f.cube): continue    # only the smaller side moves
                bd = b.lift(np.array(b.poly.exterior.coords)[:4]) @ f.n - f.d
                if bd.min() <= EPS_TIE or bd.max() > SNAP_GAP: continue
                bp = Polygon([(float(p @ f.e1), float(p @ f.e2)) for p in b.lift(np.array(b.poly.exterior.coords)[:4])])
                if bp.intersection(f.poly).area < 1e-7: continue
                m = float(bd.max()) + 1e-4
                cur = grow.setdefault(j, {}).get(key)
                grow[j][key] = m if cur is None else min(cur, m)       # nearest surface under the plate
    return grow

def snap(g): return shapely.set_precision(g, 1e-7)

def cull_part(faces, boxes, members):
    """Visible polygon per face of one rigid part (None = fully hidden).

    A face is dropped when the solids of its part fill the space just in front of it. Only regions
    that z-fight are cut out: under a same-facing face of the part lying 0..FIGHT m in front, or under
    the winner of an exact tie. Regions merely buried behind a thicker solid stay (the depth test hides them).
    """
    idx = np.array(members); los = np.array([boxes[i].lo for i in idx]); his = np.array([boxes[i].hi for i in idx])
    by_cube = {}
    for f in faces: by_cube.setdefault(f.cube, []).append(f)
    out = {}
    for f in faces:
        pts = f.lift(np.array(f.poly.exterior.coords)[:4]); flo = pts.min(0) - FIGHT - 1e-4; fhi = pts.max(0) + FIGHT + 1e-4   # plates may float a few mm in front
        near = idx[np.all((los <= fhi) & (his >= flo), 1)]
        solids, fights = [], []
        for j in near:
            if j == f.cube: continue
            sec = boxes[j].section(f.n, f.d + EPS_COVER, f.e1, f.e2)
            if sec is not None and sec.intersects(f.poly): solids.append(sec)
            for g in by_cube.get(j, ()):
                if g.n @ f.n < 0.9995: continue                   # within ~1.8 degrees: slightly tilted slabs fight too
                gd = g.lift(np.array(g.poly.exterior.coords)[:4]) @ f.n - f.d    # g's corners relative to f's plane
                lo, hi = gd.min(), gd.max()
                if hi <= EPS_TIE and lo >= -EPS_TIE or lo < -EPS_TIE < hi:
                    if (g.area, g.cube) >= (f.area, f.cube): continue     # coplanar or crossing (tilted slab): smaller face wins
                elif not (lo >= -EPS_TIE and hi <= FIGHT): continue    # g 0..FIGHT in front of f (may touch f along an edge)
                gp = Polygon([(float(p @ f.e1), float(p @ f.e2)) for p in g.lift(np.array(g.poly.exterior.coords)[:4])])
                if gp.intersects(f.poly): fights.append(gp)
        # GEOS overlay on nearly coincident edges is not robust (a buffered union once dropped a whole
        # polygon); snapping everything to a 0.1 um grid first gives exact, repeatable results.
        if solids:
            rest = snap(f.poly).difference(unary_union([snap(x) for x in solids]))
            if rest.is_empty or rest.area < max(1e-8, 1e-3 * f.area): out[id(f)] = None; continue
        if not fights: out[id(f)] = f.poly; continue
        vis = snap(f.poly).difference(unary_union([snap(x) for x in fights]))
        if vis.is_empty or vis.area < 1e-8: out[id(f)] = None
        elif f.area - vis.area < 1e-7: out[id(f)] = f.poly                 # < 0.1 mm^2 sliver: keep the quad
        else: out[id(f)] = vis
    return out

def triangulate(f, vis):
    """-> list of (3x3 positions, 3x2 uv). Full quads stay two triangles."""
    tris = []
    if vis.equals_exact(f.poly, 1e-12) or abs(vis.area - f.area) < 1e-12:
        q = np.array(f.poly.exterior.coords)[:4]; P = f.lift(q); UV = np.c_[q, np.ones(4)] @ f.uvmap
        return [(P[[0, 1, 2]], UV[[0, 1, 2]]), (P[[0, 2, 3]], UV[[0, 2, 3]])]
    geoms = [vis] if vis.geom_type == 'Polygon' else [g for g in getattr(vis, 'geoms', []) if g.geom_type == 'Polygon']
    for g in geoms:
        if g.area < 1e-10: continue
        for t in constrained_delaunay_triangles(g).geoms:
            if t.area < 1e-12: continue
            q = np.array(t.exterior.coords)[:3]; P = f.lift(q); UV = np.c_[q, np.ones(3)] @ f.uvmap
            tris.append((P, UV))
    return tris

# ------------------------------------------------------------------ glTF writer
def write_glb(path, nodes, parts_geo):
    """nodes: list of dict(name, parent, translation); parts_geo: name -> (pos[N,3], nrm[N,3], uv[N,2], idx[M])."""
    bin_ = bytearray(); views = []; accessors = []; meshes = []
    def add_view(data, target):
        while len(bin_) % 4: bin_.append(0)
        off = len(bin_); bin_.extend(data); views.append(dict(buffer=0, byteOffset=off, byteLength=len(data), target=target)); return len(views) - 1
    def add_acc(arr, ctype, typ, target, minmax=False):
        v = add_view(arr.tobytes(), target); a = dict(bufferView=v, componentType=ctype, count=int(arr.shape[0]), type=typ)
        if minmax: a['min'] = arr.min(0).tolist(); a['max'] = arr.max(0).tolist()
        accessors.append(a); return len(accessors) - 1
    gnodes = []
    for nd in nodes:
        g = dict(name=nd['name'], translation=[float(x) for x in nd['translation']])
        if nd['name'] in parts_geo:
            pos, nrm, uv, idx = parts_geo[nd['name']]
            ip = add_acc(pos.astype('<f4'), 5126, 'VEC3', 34962, True); inn = add_acc(nrm.astype('<f4'), 5126, 'VEC3', 34962)
            iu = add_acc(uv.astype('<f4'), 5126, 'VEC2', 34962)
            big = pos.shape[0] > 65535
            ii = add_acc(idx.astype('<u4' if big else '<u2'), 5125 if big else 5123, 'SCALAR', 34963)
            meshes.append(dict(name=nd['name'], primitives=[dict(attributes=dict(POSITION=ip, NORMAL=inn, TEXCOORD_0=iu), indices=ii, material=0)]))
            g['mesh'] = len(meshes) - 1
        gnodes.append(g)
    for i, nd in enumerate(nodes):
        kids = [j for j, m in enumerate(nodes) if m['parent'] == nd['name']]
        if kids: gnodes[i]['children'] = kids
    gltf = dict(asset=dict(version='2.0', generator='MechDefense build_tower_assets'), scene=0,
                scenes=[dict(nodes=[i for i, nd in enumerate(nodes) if nd['parent'] is None])], nodes=gnodes, meshes=meshes,
                materials=[dict(name='atlas', pbrMetallicRoughness=dict(baseColorFactor=[1, 1, 1, 1], metallicFactor=0, roughnessFactor=1), doubleSided=False)],
                accessors=accessors, bufferViews=views, buffers=[dict(byteLength=len(bin_))])
    js = json.dumps(gltf, separators=(',', ':')).encode()
    js += b' ' * (-len(js) % 4); bin_ += b'\0' * (-len(bin_) % 4)
    data = struct.pack('<III', 0x46546C67, 2, 12 + 8 + len(js) + 8 + len(bin_)) + struct.pack('<II', len(js), 0x4E4F534A) + js + struct.pack('<II', len(bin_), 0x004E4942) + bytes(bin_)
    Path(path).write_bytes(data)

# ------------------------------------------------------------------ inventory icon mesh
ICON_TRIS = 1500

def write_icon(path, P, U, N, limit=ICON_TRIS):
    """3D inventory icon: the largest triangles (rest pose) up to `limit` (None: all), centred and scaled into a unit cube.
    icon.bin = int32 nv, int32 ni, nv x (x y z u v shade) float32, ni int32 (engine winding: clockwise front)."""
    P = P.reshape(-1, 3, 3); U = U.reshape(-1, 3, 2); N = N.reshape(-1, 3, 3)[:, 0]
    area = np.linalg.norm(np.cross(P[:, 1] - P[:, 0], P[:, 2] - P[:, 0]), axis=1) / 2
    keep = np.argsort(-area)[:limit]; P, U, N = P[keep], U[keep], N[keep]
    lo, hi = P.reshape(-1, 3).min(0), P.reshape(-1, 3).max(0); P = (P - (lo + hi) / 2) / max(hi - lo)
    # block-style face shading: top light, sides by facing, underside dark
    shade = np.where(N[:, 1] > .5, 1.0, np.where(N[:, 1] < -.5, .55, .72 + .14 * N[:, 0] - .08 * N[:, 2]))
    V = np.concatenate([P.reshape(-1, 3), U.reshape(-1, 2), np.repeat(shade, 3)[:, None]], 1).astype('<f4')
    V, I = np.unique(V, axis=0, return_inverse=True)                               # corners shared by triangles stored once
    I = I.reshape(-1, 3)[:, [0, 2, 1]].reshape(-1).astype('<i4')                   # glTF CCW -> engine front (CW)
    Path(path).write_bytes(struct.pack('<ii', len(V), len(I)) + V.tobytes() + I.tobytes())
    return len(keep)

# ------------------------------------------------------------------ polygon models (replace the cube build's output)
def read_glb(path):
    """-> [(name, parent, translation)], {name: (pos, nrm, uv, idx)} as stored (part-local positions)."""
    b = Path(path).read_bytes(); jl = struct.unpack_from('<I', b, 12)[0]; js = json.loads(b[20:20 + jl]); off = 20 + jl + 8
    def acc(i):
        a = js['accessors'][i]; v = js['bufferViews'][a['bufferView']]; n = {'VEC3': 3, 'VEC2': 2, 'SCALAR': 1}[a['type']]
        dt = {5126: '<f4', 5125: '<u4', 5123: '<u2'}[a['componentType']]
        arr = np.frombuffer(b, dt, a['count'] * n, off + v.get('byteOffset', 0) + a.get('byteOffset', 0))
        return arr.reshape(a['count'], n).astype(float) if n > 1 else arr.astype(np.int64)
    names = [n['name'] for n in js['nodes']]
    parent = {names[c]: n['name'] for n in js['nodes'] for c in n.get('children', [])}
    nodes = [(n['name'], parent.get(n['name']), np.asarray(n.get('translation', [0, 0, 0]), float)) for n in js['nodes']]
    parts = {}
    for n in js['nodes']:
        if 'mesh' in n:
            pr = js['meshes'][n['mesh']]['primitives'][0]; at = pr['attributes']
            parts[n['name']] = (acc(at['POSITION']), acc(at['NORMAL']), acc(at['TEXCOORD_0']), acc(pr['indices']))
    return nodes, parts

def use_poly(spec, nodes, d, stats, src=None):
    """Ship the polygon model of spec['poly'] (or the folder src) in place of the cube build (model.glb, atlas.png, icon.bin; bounds, radius)."""
    src = Path(src) if src else AUTHOR / spec['poly']; glb = src / 'model.glb'; png = src / 'atlas.png'
    pnodes, parts = read_glb(glb)
    want = [(n['name'], n['parent'], np.asarray(n['translation'], float)) for n in nodes]
    assert [(a, b) for a, b, _ in pnodes] == [(a, b) for a, b, _ in want], (spec['id'], 'poly node tree differs', pnodes, want)
    for (a, _, t1), (_, _, t2) in zip(pnodes, want): assert np.allclose(t1, t2, atol=1e-5), (spec['id'], a, t1, t2)
    shutil.copyfile(glb, d / 'model.glb'); Image.open(png).convert('RGBA').save(d / 'atlas.png')
    world = {}
    for name, par, t in pnodes: world[name] = t + (world[par] if par else 0)      # the nodes only translate
    P, U, N = [], [], []
    for name, (pos, nrm, uv, idx) in parts.items():
        tri = idx.reshape(-1, 3); q = pos[tri] + world[name]; P.append(q); U.append(uv[tri])
        n = np.cross(q[:, 1] - q[:, 0], q[:, 2] - q[:, 0]); N.append(np.repeat((n / np.maximum(np.linalg.norm(n, axis=1, keepdims=True), 1e-12))[:, None], 3, 1))
    P, U, N = np.concatenate(P), np.concatenate(U), np.concatenate(N)
    stats['cube_triangles'] = stats.pop('triangles'); stats['triangles'] = {k: int(len(v[3]) // 3) for k, v in parts.items()}
    stats['vertices'] = {k: int(len(v[0])) for k, v in parts.items()}
    stats['icon_triangles'] = write_icon(d / 'icon.bin', P, U, N, limit=None)    # the dropped item: a few thousand triangles, keep all
    allc = P.reshape(-1, 3)
    return dict(bounds=[allc.min(0).round(4).tolist(), allc.max(0).round(4).tolist()], radius=round(float(np.linalg.norm(allc, axis=1).max()) + .1, 3),
                poly=dict(glb=str(glb), glbSha256=sha(glb), atlas=str(png), atlasSha256=sha(png)))

def cross_part_pairs(kept, rank):
    """{id(mover face): (face, metres)} for visible same-facing faces of two parts closer than CROSS_GAP that
    overlap: the higher-ranked (moving) part's face must come NUDGE in front of the other."""
    out = {}; buckets = {}
    for f, v in kept:
        buckets.setdefault((tuple(np.round(f.n * 200).astype(int)), int(math.floor(f.d / CROSS_GAP))), []).append((f, v))
    def neighbours(f):
        kn = tuple(np.round(f.n * 200).astype(int)); kd = int(math.floor(f.d / CROSS_GAP))
        for dd in (kd - 1, kd, kd + 1): yield from buckets.get((kn, dd), ())
    for f, v in kept:
        for g, w in neighbours(f):
            if id(g) <= id(f) or f.part == g.part or f.n @ g.n < 0.9999 or abs(f.d - g.d) > CROSS_GAP: continue
            gp = Polygon([(float(p @ f.e1), float(p @ f.e2)) for p in g.lift(np.array(g.poly.exterior.coords)[:4])])
            if not gp.intersects(f.poly): continue
            gw = Polygon([(float(p @ f.e1), float(p @ f.e2)) for p in g.lift(np.array(w.exterior.coords)[:-1])]) if w.geom_type == 'Polygon' else gp
            if v.intersection(gw.buffer(0)).area < 1e-6: continue
            mover, other = (f, g) if rank(f.part) > rank(g.part) else (g, f)
            m = other.d - mover.d + NUDGE
            if m > 1e-6: out[id(mover)] = (mover, max(out.get(id(mover), (None, 0.0))[1], m))
    return out

def build_poly_only(spec):
    """A tower that exists only as a polygon model (spec['polyOnly']): see the module docstring."""
    t0 = time.time(); tid = spec['id']; src = Path(spec['polyOnly']); d = OUT / tid; d.mkdir(parents=True, exist_ok=True)
    pnodes, parts = read_glb(src / 'model.glb'); pj = json.loads((src / 'parts.json').read_text(encoding='utf-8'))
    names = [n for n, _, _ in pnodes]
    for need in ['Root', 'base', 'yaw', 'pitch'] + list(spec.get('spinParts', [])) + list(spec.get('recoilParts', [])):
        assert need in names, (tid, 'node missing from the polygon model', need, names)
    nodes = [dict(name=n, parent=p, translation=[round(float(x), 6) for x in t]) for n, p, t in pnodes]
    stats = dict(triangles={}); over = use_poly(spec, nodes, d, stats, src); stats.pop('cube_triangles', None)
    stats.update(faces_hidden=0, faces_clipped=0, cross_part_pairs_nudged=0)
    world = {}
    for n, p, t in pnodes: world[n] = t + (world[p] if p else 0)
    muzzles = [dict(name=m['name'], part=m['part'], local=[float(x) for x in m['local']], rest=[round(float(x), 5) for x in np.asarray(m['local'], float) + world[m['part']]])
               for m in pj['muzzles']]
    assert muzzles and all(m['part'] in names for m in muzzles), (tid, 'muzzles', pj.get('muzzles'))
    base = parts['base'][0] + world['base']; half = np.abs(base[:, [0, 2]]).max(0); top = over['bounds'][1][1]
    cells = [int(max(spec.get('minCells', [1, 1])[0], math.ceil(2 * half[0] - .05))), int(math.ceil(top - .05)), int(max(spec.get('minCells', [1, 1])[1], math.ceil(2 * half[1] - .05)))]
    lo, hi = pj['pitch']                          # the model's own clash sweep (every 15 deg of yaw, pitch limits, spin)
    meta = dict(id=tid, nodes=nodes, pivots={n['name']: [round(float(x), 6) for x in world[n['name']]] for n in nodes}, muzzles=muzzles,
                bounds=over['bounds'], radius=over['radius'], source=dict(poly=over['poly'], parts=str(src / 'parts.json'), partsSha256=sha(src / 'parts.json')),
                stats=stats, clearance=dict(pitch=[lo, hi] if hi > lo else [0, 0], from_model=True), cells=cells)
    print(tid, 'polygon-only', json.dumps(dict(triangles=stats['triangles'], cells=cells, pitch=[lo, hi])), f'{time.time() - t0:.1f}s', flush=True)
    META.mkdir(parents=True, exist_ok=True); (META / f'{tid}.json').write_text(json.dumps(meta, ensure_ascii=False, indent=1), encoding='utf-8')
    return meta

# ------------------------------------------------------------------ build one tower
def build(spec):
    t0 = time.time(); tid = spec['id']
    if spec.get('polyOnly'): return build_poly_only(spec)
    src = AUTHOR / spec['source']; geo_path = src / f"{tid}_{spec['version']}.geo.json"; png_path = src / f"{tid}_{spec['version']}.png"
    if spec.get('poly'):         # polygon model over an unchanged cube build: reuse the cube build's cached metadata
        cached = META / f'{tid}.json'
        if cached.exists():
            m = json.loads(cached.read_text(encoding='utf-8'))
            if m['source']['geoSha256'] == sha(geo_path) and m['source']['atlasSha256'] == sha(png_path):
                d = OUT / tid; d.mkdir(parents=True, exist_ok=True); stats = dict(m['stats'])
                if 'cube_triangles' in stats: stats['triangles'] = stats.pop('cube_triangles')
                over = use_poly(spec, m['nodes'], d, stats); m.update(bounds=over['bounds'], radius=over['radius'], stats=stats); m['source']['poly'] = over['poly']
                print(tid, 'polygon model over the cached cube build', json.dumps(stats['triangles']), f'{time.time() - t0:.1f}s', flush=True)
                cached.write_text(json.dumps(m, ensure_ascii=False, indent=1), encoding='utf-8'); return m
    geo = json.loads(geo_path.read_text(encoding='utf-8'))['minecraft:geometry'][0]
    gb = {b['name']: b for b in geo['bones']}
    tex = (geo['description']['texture_width'], geo['description']['texture_height'])
    labels = authored_labels(spec, gb)
    cubes, owner = assign_parts(spec, gb, labels)
    atlas = np.asarray(Image.open(png_path).convert('RGBA'))
    faces, boxes = faces_of(cubes, owner, tex, atlas)
    grow = snap_plates(faces, boxes, owner)
    faces, boxes = faces_of(cubes, owner, tex, atlas, grow)
    parts = sorted(set(owner), key=lambda p: (PART_ORDER.get(p, 3), p))
    # geo bone pivots (Bedrock raw) -> SC
    piv = {b: model_to_sc(bb_point_to_model([-gb[b]['pivot'][0], gb[b]['pivot'][1], gb[b]['pivot'][2]])) for b in ('base', 'yaw', 'pitch') if b in gb}
    parent_of = {'base': 'Root', 'yaw': 'Root', 'pitch': 'yaw' if 'yaw' in gb else 'Root'}
    for sub in spec.get('subparts', []):
        mem = [i for i, o in enumerate(owner) if o == sub['name']]
        assert mem, (tid, sub['name'], 'matched no cubes')
        P = np.concatenate([boxes[i].corners for i in mem]); piv[sub['name']] = sub_pivot(sub, P); parent_of[sub['name']] = sub['from']
    # 1) per-part hidden-surface clean-up
    vis = {}; stats = dict(faces_in=len(faces), plates_snapped=sum(len(g) for g in grow.values()))
    def cull(which):
        for p in which:
            members = [i for i, o in enumerate(owner) if o == p]
            vis.update(cull_part([f for f in faces if f.part == p], boxes, members))
        return [(f, vis[id(f)]) for f in faces if vis[id(f)] is not None]
    kept = cull(parts)
    # 2) cross-part coincident faces at rest: grow the moving part's cube NUDGE in front, then redo that part
    rank = lambda p: PART_ORDER.get(p, 3)
    nudge = cross_part_pairs(kept, rank); stats['cross_part_pairs_nudged'] = len(nudge); moved_all = set()
    for _ in range(3):                       # growing a face past one neighbour can bring it near another: repeat
        if not nudge: break
        for f, m in nudge.values(): grow.setdefault(f.cube, {})[f.key] = grow.get(f.cube, {}).get(f.key, 0.0) + m
        moved = sorted({f.part for f, _ in nudge.values()}); moved_all |= set(moved)
        new, boxes = faces_of(cubes, owner, tex, atlas, grow)
        faces = [f for f in faces if f.part not in moved] + [f for f in new if f.part in moved]
        kept = cull(moved)
        nudge = cross_part_pairs(kept, rank)
    if moved_all: stats['cross_part_pairs_left'] = len(nudge); stats['parts_grown'] = sorted(moved_all)
    stats['faces_hidden'] = len(faces) - len(kept); stats['faces_clipped'] = sum(1 for f, v in kept if abs(v.area - f.area) > 1e-12)
    for sub in spec.get('subparts', []):         # sub-part pivots from the final boxes
        P = np.concatenate([boxes[i].corners for i, o in enumerate(owner) if o == sub['name']]); piv[sub['name']] = sub_pivot(sub, P)
    # 3) triangles per part, positions relative to the part's world pivot
    geo_parts = {}; trace = []
    for p in parts:
        P, N, U = [], [], []
        for f, v in kept:
            if f.part != p: continue
            for tri, uv in triangulate(f, v):
                # glTF front faces are CCW seen from outside (normal side)
                if np.cross(tri[1] - tri[0], tri[2] - tri[0]) @ f.n < 0: tri = tri[[0, 2, 1]]; uv = uv[[0, 2, 1]]
                P.append(tri - piv.get(p, 0)); N.append(np.repeat(f.n[None], 3, 0)); U.append(uv)
                if TRACE: trace.append(dict(part=p, tri=tri.round(6).tolist(), label=labels[f.cube], cube=f.cube, key=f.key,
                                            restored=f.key not in cubes[f.cube]['uv'], grow=grow.get(f.cube)))
        P = np.concatenate(P).reshape(-1, 3); N = np.concatenate(N).reshape(-1, 3); U = np.concatenate(U).reshape(-1, 2)
        key = np.round(np.c_[P * 1e5, N * 1e4, U * 1e6]).astype(np.int64)
        _, first, inv = np.unique(key, axis=0, return_index=True, return_inverse=True)
        geo_parts[p] = (P[first], N[first], U[first], inv.reshape(-1).astype(np.uint32))
    stats['triangles'] = {p: int(len(g[3]) // 3) for p, g in geo_parts.items()}; stats['vertices'] = {p: int(len(g[0])) for p, g in geo_parts.items()}
    # rest-pose triangle soup for the icon (positions back in tower space)
    soup = [(g[0][g[3]] + piv.get(p, 0), g[2][g[3]], g[1][g[3]]) for p, g in geo_parts.items()]
    icon_soup = tuple(np.concatenate([x[k] for x in soup]) for k in range(3))
    # 4) node tree
    nodes = [dict(name='Root', parent=None, translation=(0, 0, 0))]
    for p in parts:
        parent = parent_of[p]; base_t = piv[parent] if parent in piv else np.zeros(3)
        nodes.append(dict(name=p, parent=parent, translation=piv.get(p, np.zeros(3)) - base_t))
    d = OUT / tid; d.mkdir(parents=True, exist_ok=True)
    write_glb(d / 'model.glb', nodes, geo_parts)
    if TRACE:
        (ROOT / 'local/claude-work/trace').mkdir(parents=True, exist_ok=True)
        (ROOT / f'local/claude-work/trace/{tid}.json').write_text(json.dumps(trace), encoding='utf-8')
    stats['icon_triangles'] = write_icon(d / 'icon.bin', *icon_soup)
    Image.open(png_path).convert('RGBA').save(d / 'atlas.png')
    # 5) muzzles (locators on the bone that carries them)
    muzzles = []
    for b in ('pitch', 'yaw', 'base'):
        if b in gb and gb[b].get('locators'):
            for name, v in sorted(gb[b]['locators'].items()):
                w = model_to_sc(bb_point_to_model([-v[0], v[1], v[2]]))
                owner_part = b
                for sub in spec.get('subparts', []):     # a locator in front of a sub-part's barrel belongs to it
                    if sub['from'] == b and sub.get('muzzle') == name: owner_part = sub['name']
                muzzles.append(dict(name=name, part=owner_part, local=(w - piv.get(owner_part, 0)).tolist(), rest=w.tolist()))
            break
    allc = np.concatenate([b.corners for b in boxes])
    meta = dict(id=tid, nodes=[dict(name=n['name'], parent=n['parent'], translation=[round(float(x), 6) for x in n['translation']]) for n in nodes],
                pivots={k: [round(float(x), 6) for x in v] for k, v in piv.items()}, muzzles=muzzles,
                bounds=[allc.min(0).round(4).tolist(), allc.max(0).round(4).tolist()],
                radius=round(float(np.linalg.norm(allc, axis=1).max()) + .1, 3),
                source=dict(geo=str(geo_path), geoSha256=sha(geo_path), atlas=str(png_path), atlasSha256=sha(png_path)), stats=stats)
    if spec.get('poly'):
        cube_bounds = meta['bounds']; over = use_poly(spec, nodes, d, stats); meta['bounds'], meta['radius'] = over['bounds'], over['radius']
        meta['source']['poly'] = over['poly']
        grow = max(max(a - b for a, b in zip(cube_bounds[0], meta['bounds'][0])), max(b - a for a, b in zip(cube_bounds[1], meta['bounds'][1])))
        if grow > .05: print(f"{tid}: WARNING the polygon model reaches {grow:.3f} m beyond the cube build (cells come from the cube build)")
    meta['clearance'] = clearance(spec, boxes, owner, piv, labels)
    base = np.concatenate([b.corners for b, o in zip(boxes, owner) if o == 'base'])
    half = np.abs(base[:, [0, 2]]).max(0)
    meta['cells'] = [int(max(spec.get('minCells', [1, 1])[0], math.ceil(2 * half[0] - .05))), int(math.ceil(allc[:, 1].max() - .05)),
                     int(max(spec.get('minCells', [1, 1])[1], math.ceil(2 * half[1] - .05)))]
    print(tid, json.dumps(dict(stats=stats, clearance=meta['clearance']), ensure_ascii=False), f'{time.time() - t0:.1f}s', flush=True)
    META.mkdir(parents=True, exist_ok=True)                  # per tower, so a subset build (or a parallel one) is reusable
    (META / f'{tid}.json').write_text(json.dumps(meta, ensure_ascii=False, indent=1), encoding='utf-8')
    return meta

# ------------------------------------------------------------------ motion clearance
def moved(box, R, pivot, t=np.zeros(3)):
    c = (box.corners - pivot) @ R.T + pivot + t; return Box(c)

DEPTH = 0.012   # m: interpenetration deeper than this (vs. rest) counts as visible clipping
AXLE = re.compile(r'trunnion|axle|hub|bearing|pin\b|pivot|boss')   # turning in a bore: polygon corners graze it

def new_contacts(boxes, moving, fixed, R, pivot, t, rest, Ry=None, ypiv=None):
    """Moving/fixed pairs interpenetrating deeper than DEPTH after the motion, excluding rest contacts -> [(i, j, depth)]."""
    C = np.array([boxes[i].corners for i in moving]); C = (C - pivot) @ R.T + pivot + t
    F = np.array([boxes[j].corners for j in fixed])
    if Ry is not None: C = (C - ypiv) @ Ry.T + ypiv
    lo_m, hi_m = C.min(1), C.max(1); lo_f, hi_f = F.min(1), F.max(1)
    cand = np.all((lo_m[:, None] <= hi_f[None] - DEPTH) & (hi_m[:, None] >= lo_f[None] + DEPTH), -1)
    out = []
    for a, b in zip(*np.nonzero(cand)):
        if (moving[a], fixed[b]) in rest or moving[a] in AXLE_IDS or fixed[b] in AXLE_IDS: continue
        dep = sat_depth(Box(C[a]), Box(F[b]))
        if dep > DEPTH: out.append((moving[a], fixed[b], dep))
    return out

AXLE_IDS = set()

def rest_contacts(boxes, moving, fixed):
    C = np.array([boxes[i].corners for i in moving]); F = np.array([boxes[j].corners for j in fixed])
    cand = np.all((C.min(1)[:, None] <= F.max(1)[None] + 1e-3) & (C.max(1)[:, None] >= F.min(1)[None] - 1e-3), -1)
    return {(moving[a], fixed[b]) for a, b in zip(*np.nonzero(cand)) if sat_overlap(boxes[moving[a]], boxes[fixed[b]], -0.001)}

def clearance(spec, boxes, owner, piv, labels=None):
    """Clip-free pitch range (deg), yaw sweep and recoil checks: no new interpenetration deeper than DEPTH vs. the rest pose."""
    global AXLE_IDS
    AXLE_IDS = {i for i, l in enumerate(labels or []) if AXLE.search(l)}
    res = {}; parts = set(owner); lab = (lambda i: labels[i] if labels else str(i))
    def worst(pairs, k=6):
        agg = {}
        for i, j, dep in pairs:
            key = (lab(i), lab(j)); agg[key] = max(agg.get(key, 0), dep)
        return [f"{a} x {b}: {d * 100:.1f} cm" for (a, b), d in sorted(agg.items(), key=lambda x: -x[1])[:k]]
    if 'pitch' in parts and not spec.get('pitchFixed'):
        links = {s['name'] for s in spec.get('subparts', []) if 'aim' in s}   # pistons/struts follow the cradle at runtime
        moving = [i for i, o in enumerate(owner) if (o == 'pitch' or spec_parent(spec, o) == 'pitch') and o not in links]
        pv = piv['pitch']; reach = max(np.linalg.norm(boxes[i].corners - pv, axis=1).max() for i in moving) + 0.05
        fixed = [j for j, o in enumerate(owner) if o in ('yaw', 'base') and np.linalg.norm(np.clip(pv, boxes[j].lo, boxes[j].hi) - pv) <= reach]
        rest = rest_contacts(boxes, moving, fixed)
        base_fixed = [j for j in fixed if owner[j] == 'base']; ypiv = piv.get('yaw', np.zeros(3))
        def hits(a):
            R = rx(math.radians(a)); out = new_contacts(boxes, moving, fixed, R, pv, 0, rest)
            if base_fixed and 'yaw' in parts:
                for y in (90, 180, 270): out += new_contacts(boxes, moving, base_fixed, R, pv, 0, rest, ry(math.radians(y)), ypiv)
            return out
        lims = {}
        for sign, rng in ((1, range(2, 82, 2)), (-1, range(2, 62, 2))):
            last = 0; why = []
            for a in rng:
                h = hits(sign * a)
                if h: why = worst(h); break
                last = sign * a
            lims[sign] = (last, why)
        res['pitch'] = [lims[-1][0], lims[1][0]]; res['pitch_up_blocked_by'] = lims[1][1]; res['pitch_down_blocked_by'] = lims[-1][1]
    if 'yaw' in parts:
        moving = [i for i, o in enumerate(owner) if o != 'base']; fixed = [i for i, o in enumerate(owner) if o == 'base']
        rest = rest_contacts(boxes, moving, fixed); bad = []
        for y in range(10, 360, 10):
            bad += new_contacts(boxes, moving, fixed, ry(math.radians(y)), piv['yaw'], 0, rest)
        res['yaw_collisions'] = len(bad); res['yaw_worst'] = worst(bad)
    for sub in spec.get('subparts', []):
        if 'recoil' not in sub: continue
        mem = [i for i, o in enumerate(owner) if o == sub['name']]
        others = [i for i, o in enumerate(owner) if o in ('yaw', 'base')]
        rest = rest_contacts(boxes, mem, others)
        res[f"recoil_{sub['name']}"] = len(new_contacts(boxes, mem, others, np.eye(3), np.zeros(3), np.array([0, 0, sub['recoil']]), rest))
    return res

def sub_pivot(sub, P):
    """Sub-part pivot: an explicit hinge [y, z] (SC metres; links turn about X there), else the centre of its boxes."""
    c = (P.min(0) + P.max(0)) / 2
    return np.array([c[0], sub['hinge'][0], sub['hinge'][1]]) if 'hinge' in sub else c

def spec_parent(spec, name):
    for sub in spec.get('subparts', []):
        if sub['name'] == name: return sub['from']
    return None

def runtime_entry(spec, meta):
    """Design fields from tools/towers.json + generated geometry -> one Assets/Towers/towers.json record."""
    d = {k: v for k, v in spec.items() if k not in ('source', 'version', 'tools', 'subparts', 'minCells', 'reassign', 'trim', 'poly', 'polyOnly')}
    d.update(nodes=meta['nodes'], muzzles=[{k: m[k] for k in ('name', 'part', 'local')} for m in meta['muzzles']],
             bounds=meta['bounds'], radius=meta['radius'], cells=meta['cells'])
    if 'pitch' in meta['clearance']:
        lo, hi = meta['clearance']['pitch']; d['pitch'] = [max(lo + 2, -35), min(hi - 2, 70)] if hi > lo else [0, 0]
    subs = [x for x in spec.get('subparts', []) if 'recoil' in x]
    if not spec.get('polyOnly'): d['recoilParts'] = [x['name'] for x in subs]; d['recoil'] = [x['recoil'] for x in subs]   # polygon-only towers name them in tools/towers.json
    # articulated links: turn about X at their hinge to keep pointing at a partner point (on the turret or the cradle)
    d['links'] = [dict(node=x['name'], parent=x['from'], hinge=x['hinge'], at=x['aim']['at'], on=x['aim']['on'], stretch=bool(x.get('stretch')))
                  for x in spec.get('subparts', []) if 'aim' in x]
    return d

def main(ids):
    towers = [t for t in SPEC['towers'] if not ids or t['id'] in ids]
    metas = {}
    old = OUT / 'towers.generated.json'
    if old.exists(): metas = {m['id']: m for m in json.loads(old.read_text(encoding='utf-8'))['towers']}
    for spec in SPEC['towers']:                    # per-tower metadata from earlier subset builds, if it matches the source now
        p = META / f"{spec['id']}.json"
        if not p.exists() or spec.get('polyOnly'): continue
        m = json.loads(p.read_text(encoding='utf-8')); src = AUTHOR / spec['source']
        geo = src / f"{spec['id']}_{spec['version']}.geo.json"; png = src / f"{spec['id']}_{spec['version']}.png"
        poly_ok = not spec.get('poly') and 'poly' not in m['source'] or spec.get('poly') and m['source'].get('poly', {}).get('glbSha256') == sha(AUTHOR / spec['poly'] / 'model.glb') \
            and m['source']['poly']['atlasSha256'] == sha(AUTHOR / spec['poly'] / 'atlas.png')
        if geo.exists() and m['source']['geoSha256'] == sha(geo) and m['source']['atlasSha256'] == sha(png) and poly_ok: metas[spec['id']] = m
    built = set()
    if '--runtime-only' not in sys.argv:          # --runtime-only: regenerate towers.json from tools/towers.json + cached geometry
        for spec in towers: metas[spec['id']] = build(spec); built.add(spec['id'])
    for spec in SPEC['towers']:                    # polygon-only towers are rebuilt every time (no cube build to cache)
        if spec.get('polyOnly') and spec['id'] not in built: metas[spec['id']] = build_poly_only(spec)
    OUT.mkdir(parents=True, exist_ok=True)
    order = [t['id'] for t in SPEC['towers'] if t['id'] in metas]
    old.write_text(json.dumps(dict(towers=[metas[i] for i in order]), ensure_ascii=False, indent=1), encoding='utf-8')
    runtime = [runtime_entry(t, metas[t['id']]) for t in SPEC['towers'] if t['id'] in metas]
    (OUT / 'towers.json').write_text(json.dumps(dict(towers=runtime), ensure_ascii=False, indent=1), encoding='utf-8')
    prov = [dict(id=m['id'], source=m['source'], model='polygon' if 'poly' in m['source'] else 'cube', triangles=sum(m['stats']['triangles'].values()), facesHidden=m['stats']['faces_hidden'],
                 facesClipped=m['stats']['faces_clipped'], crossPartNudged=m['stats']['cross_part_pairs_nudged'], clearance=m['clearance']) for m in (metas[i] for i in order)]
    (ROOT / 'docs/tower-provenance.json').write_text(json.dumps(dict(boundary='Authored cube models (v5) and polygon models (poly_v1) with their atlases from E:/projects/endfield-turret-model and E:/projects/sog-tower-model; no game model or texture is shipped.', towers=prov), ensure_ascii=False, indent=1), encoding='utf-8')
    print('runtime towers.json:', len(runtime), {r['id']: r['cells'] for r in runtime})

if __name__ == '__main__':
    main([a for a in sys.argv[1:] if not a.startswith('--')])
