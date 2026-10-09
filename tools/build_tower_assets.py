"""Authored Endfield cube towers -> Survivalcraft runtime assets.

python tools/build_tower_assets.py [ids...] [--runtime-only]   (default: every tower in tools/towers.json)

For each tower: one glTF binary with a node per rigid part (base / yaw / pitch / animated
sub-parts), the authored pixel atlas, and Mod/Assets/Towers/towers.json metadata (pivots,
muzzles, clip-free pitch range, footprint). Outputs go to Mod/Assets/Towers (generated, ignored).

Hidden-surface clean-up, per rigid part: a face region is dropped when solid cubes of the same
part fill the space just in front of it. The authored cube models stack faces 0.006-0.5 mm
apart (bevel slabs, plates, rings). Blender close-ups hide that; in game it z-fights ("colour
changes" while the camera moves). Exact ties keep the smaller face (the decal). Faces of two
parts that still coincide at rest are pushed 2.5 mm apart, the moving part in front.
Units: SC metres, X right, Y up, -Z forward (authoring frame x forward, y left, z up, 16 px = 1 m).
"""
from __future__ import annotations
import hashlib, importlib, json, math, re, struct, sys, time
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
SPEC = json.loads((ROOT / 'tools/towers.json').read_text(encoding='utf-8'))
EPS_COVER = 1e-5      # m: probe plane in front of a face (smaller than the 6e-6..5e-4 m stacking gaps? see tie)
EPS_TIE = 1e-5        # m: faces closer than this are one plane; priority decides
FIGHT = 0.006         # m: same-facing faces closer than this z-fight at range (24-bit depth, near 0.1)
NUDGE = 0.007         # m: cross-part coincident faces are pushed this far apart (24-bit depth resolves 7 mm to ~100 m)
CROSS_GAP = 0.006     # m: cross-part faces closer than this get nudged
PART_ORDER = {'base': 0, 'yaw': 1, 'pitch': 2}

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

def cube_geometry(cube):
    """-> (to_sc(point_raw), 8 corners SC, faces {key: (TL, TR, BL) SC})."""
    o, s = cube['origin'], cube['size']
    x0, y0, z0 = -(o[0] + s[0]), o[1], o[2]; x1, y1, z1 = -o[0], o[1] + s[1], o[2] + s[2]
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
        if m.startswith(('fam_', 'famB', 'A_')) or m in ('blocky', 'build_family', 'turret_parts', 'check_floating') or m.endswith(('_C', '_D')):
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

def faces_of(cubes, owner, tex):
    faces, boxes = [], []
    for k, cube in enumerate(cubes):
        corners, fq = cube_geometry(cube); boxes.append(Box(corners))
        for key, f in cube['uv'].items():
            (u, v), (du, dv) = f['uv'], f['uv_size']
            rect = (min(u, u + du), min(v, v + dv), abs(du), abs(dv))
            TL, TR, BL = fq[key]
            # blocky painted texel (i, j) of every face at TL + i (TR - TL) + j (BL - TL), from the rect's top-left
            faces.append(Face(owner[k], k, key, TL, TR, BL, rect, tex, boxes[-1].c))
    return faces, boxes

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
                if gd.max() <= EPS_TIE and gd.min() >= -EPS_TIE or gd.min() < -EPS_TIE < gd.max():
                    if (g.area, g.cube) >= (f.area, f.cube): continue     # coplanar or crossing (tilted slab): smaller face wins
                elif not (EPS_TIE <= gd.min() and gd.max() <= FIGHT): continue    # g entirely 0..FIGHT in front of f
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

# ------------------------------------------------------------------ build one tower
def build(spec):
    t0 = time.time(); tid = spec['id']
    src = AUTHOR / spec['source']; geo_path = src / f"{tid}_{spec['version']}.geo.json"; png_path = src / f"{tid}_{spec['version']}.png"
    geo = json.loads(geo_path.read_text(encoding='utf-8'))['minecraft:geometry'][0]
    gb = {b['name']: b for b in geo['bones']}
    tex = (geo['description']['texture_width'], geo['description']['texture_height'])
    labels = authored_labels(spec, gb)
    cubes, owner = assign_parts(spec, gb, labels)
    faces, boxes = faces_of(cubes, owner, tex)
    parts = sorted(set(owner), key=lambda p: (PART_ORDER.get(p, 3), p))
    # geo bone pivots (Bedrock raw) -> SC
    piv = {b: model_to_sc(bb_point_to_model([-gb[b]['pivot'][0], gb[b]['pivot'][1], gb[b]['pivot'][2]])) for b in ('base', 'yaw', 'pitch') if b in gb}
    parent_of = {'base': 'Root', 'yaw': 'Root', 'pitch': 'yaw' if 'yaw' in gb else 'Root'}
    for sub in spec.get('subparts', []):
        mem = [i for i, o in enumerate(owner) if o == sub['name']]
        assert mem, (tid, sub['name'], 'matched no cubes')
        P = np.concatenate([boxes[i].corners for i in mem]); piv[sub['name']] = (P.min(0) + P.max(0)) / 2; parent_of[sub['name']] = sub['from']
    # 1) per-part hidden-surface clean-up
    vis = {}; stats = dict(faces_in=len(faces))
    for p in parts:
        members = [i for i, o in enumerate(owner) if o == p]
        vis.update(cull_part([f for f in faces if f.part == p], boxes, members))
    kept = [(f, vis[id(f)]) for f in faces if vis[id(f)] is not None]
    stats['faces_hidden'] = len(faces) - len(kept); stats['faces_clipped'] = sum(1 for f, v in kept if abs(v.area - f.area) > 1e-12)
    # 2) cross-part coincident faces at rest: push the moving part forward
    rank = lambda p: PART_ORDER.get(p, 3)
    nudge = {}; pairs = 0; buckets = {}
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
            pairs += 1
            mover, other = (f, g) if rank(f.part) > rank(g.part) else (g, f)
            nudge[id(mover)] = max(nudge.get(id(mover), 0.0), other.d - mover.d + NUDGE)
    stats['cross_part_pairs_nudged'] = pairs
    # 3) triangles per part, positions relative to the part's world pivot
    geo_parts = {}
    for p in parts:
        P, N, U = [], [], []
        for f, v in kept:
            if f.part != p: continue
            off = f.n * nudge.get(id(f), 0.0)
            for tri, uv in triangulate(f, v):
                tri = tri + off
                # glTF front faces are CCW seen from outside (normal side)
                if np.cross(tri[1] - tri[0], tri[2] - tri[0]) @ f.n < 0: tri = tri[[0, 2, 1]]; uv = uv[[0, 2, 1]]
                P.append(tri - piv.get(p, 0)); N.append(np.repeat(f.n[None], 3, 0)); U.append(uv)
        P = np.concatenate(P).reshape(-1, 3); N = np.concatenate(N).reshape(-1, 3); U = np.concatenate(U).reshape(-1, 2)
        key = np.round(np.c_[P * 1e5, N * 1e4, U * 1e6]).astype(np.int64)
        _, first, inv = np.unique(key, axis=0, return_index=True, return_inverse=True)
        geo_parts[p] = (P[first], N[first], U[first], inv.reshape(-1).astype(np.uint32))
    stats['triangles'] = {p: int(len(g[3]) // 3) for p, g in geo_parts.items()}; stats['vertices'] = {p: int(len(g[0])) for p, g in geo_parts.items()}
    # 4) node tree
    nodes = [dict(name='Root', parent=None, translation=(0, 0, 0))]
    for p in parts:
        parent = parent_of[p]; base_t = piv[parent] if parent in piv else np.zeros(3)
        nodes.append(dict(name=p, parent=parent, translation=piv.get(p, np.zeros(3)) - base_t))
    d = OUT / tid; d.mkdir(parents=True, exist_ok=True)
    write_glb(d / 'model.glb', nodes, geo_parts)
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
    meta['clearance'] = clearance(spec, boxes, owner, piv, labels)
    base = np.concatenate([b.corners for b, o in zip(boxes, owner) if o == 'base'])
    half = np.abs(base[:, [0, 2]]).max(0)
    meta['cells'] = [int(max(spec.get('minCells', [1, 1])[0], math.ceil(2 * half[0] - .05))), int(math.ceil(allc[:, 1].max() - .05)),
                     int(max(spec.get('minCells', [1, 1])[1], math.ceil(2 * half[1] - .05)))]
    print(tid, json.dumps(dict(stats=stats, clearance=meta['clearance']), ensure_ascii=False), f'{time.time() - t0:.1f}s', flush=True)
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
        moving = [i for i, o in enumerate(owner) if o == 'pitch' or spec_parent(spec, o) == 'pitch']
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

def spec_parent(spec, name):
    for sub in spec.get('subparts', []):
        if sub['name'] == name: return sub['from']
    return None

def runtime_entry(spec, meta):
    """Design fields from tools/towers.json + generated geometry -> one Assets/Towers/towers.json record."""
    d = {k: v for k, v in spec.items() if k not in ('source', 'version', 'tools', 'subparts', 'minCells', 'reassign', 'trim')}
    d.update(nodes=meta['nodes'], muzzles=[{k: m[k] for k in ('name', 'part', 'local')} for m in meta['muzzles']],
             bounds=meta['bounds'], radius=meta['radius'], cells=meta['cells'])
    if 'pitch' in meta['clearance']:
        lo, hi = meta['clearance']['pitch']; d['pitch'] = [max(lo + 2, -35), min(hi - 2, 70)] if hi > lo else [0, 0]
    subs = [x for x in spec.get('subparts', []) if 'recoil' in x]
    d['recoilParts'] = [x['name'] for x in subs]; d['recoil'] = [x['recoil'] for x in subs]
    return d

def main(ids):
    towers = [t for t in SPEC['towers'] if not ids or t['id'] in ids]
    metas = {}
    old = OUT / 'towers.generated.json'
    if old.exists(): metas = {m['id']: m for m in json.loads(old.read_text(encoding='utf-8'))['towers']}
    if '--runtime-only' not in sys.argv:          # --runtime-only: regenerate towers.json from tools/towers.json + cached geometry
        for spec in towers: metas[spec['id']] = build(spec)
    OUT.mkdir(parents=True, exist_ok=True)
    order = [t['id'] for t in SPEC['towers'] if t['id'] in metas]
    old.write_text(json.dumps(dict(towers=[metas[i] for i in order]), ensure_ascii=False, indent=1), encoding='utf-8')
    runtime = [runtime_entry(t, metas[t['id']]) for t in SPEC['towers'] if t['id'] in metas]
    (OUT / 'towers.json').write_text(json.dumps(dict(towers=runtime), ensure_ascii=False, indent=1), encoding='utf-8')
    prov = [dict(id=m['id'], source=m['source'], triangles=sum(m['stats']['triangles'].values()), facesHidden=m['stats']['faces_hidden'],
                 facesClipped=m['stats']['faces_clipped'], crossPartNudged=m['stats']['cross_part_pairs_nudged'], clearance=m['clearance']) for m in (metas[i] for i in order)]
    (ROOT / 'docs/tower-provenance.json').write_text(json.dumps(dict(boundary='Authored cube models and atlases from E:/projects/endfield-turret-model; no game model or texture is shipped.', towers=prov), ensure_ascii=False, indent=1), encoding='utf-8')
    print('runtime towers.json:', len(runtime), {r['id']: r['cells'] for r in runtime})

if __name__ == '__main__':
    main([a for a in sys.argv[1:] if not a.startswith('--')])
