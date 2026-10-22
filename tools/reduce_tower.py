"""Triangle reduction for one built tower (prototype of the plan the user confirmed on 2026-10-21).

python tools/reduce_tower.py battle_rocket_1 [--out local/claude-work/r5/towers/battle_rocket_1]
       [--tol 0.002] [--fold-width 0] [--no-compare]

Input: the current build Mod/Assets/Towers/<id>/model.glb + atlas.png (never modified). Output, only into --out:
model.glb, atlas.png, numbers.json, compare_day.jpg, compare_flat.jpg, checks/ (gap and overlap check results).

Kept: the node list (names, order, parents, translations = pivots), one mesh per rigid part (each part's triangles stay
on that part), the material (the runtime draws towers two-sided), and the pixel look (nearest texels at the original
texel density; texel grids are copied, not resampled).

Per rigid part:
 0. Hidden triangles. A triangle that no ray can reach is dropped: exact GPU test (depth prepass, then an
    early-depth-tested pass that flags every triangle with a surviving fragment), the part on its own (so any yaw /
    pitch pose is covered), two-sided, 320 orthographic directions over the whole sphere at 4096 px plus 384 perspective
    cameras on two rings around the tower at 0.15-6 m height (player-like close views).
 1. Faces on one plane merged, texture rebaked (the plan's step 1). Visible triangles are grouped by plane (normal
    within 0.03 deg, offset chained within 0.2 mm). Inside a plane, triangles are merged into "islands" that can share
    one texture without resampling: all faces of one flat colour, or all faces whose texel grids coincide (same axes,
    scale and phase). Each island becomes one polygon: holes the original surface covered (or that no view can see)
    are filled, the outline's staircases (strips trimmed to a shape with steps <= --tol, V5's "steps only inward")
    are straightened outwards only, notches that no view can see are filled, collinear points dropped; nothing may
    grow into another island or onto a same-facing surface less than 6 mm in front or behind (z-fighting).
    Each island is triangulated (constrained Delaunay, no extra points) and its texels copied into a new atlas at the
    original grid (flat colours as one 4x4 block).
 2. Small slopes folded into the neighbouring big face (the plan's step 2), only with --fold-width > 0: a plane
    narrower than that (chamfer strips, step faces) is removed and its edge-neighbour planes are extended over its
    projection; its colour goes to the neighbour.
Compare (unless --no-compare): renders before/after from 8 views around (elevation 20 deg) + top + 3 close-ups,
'day' (texel x (0.45 + 0.55 max(0, n.sun)), two-sided) and 'flat' (texel only), 1024 px. A pixel differs when the
coverage differs or a channel differs by more than 16/255; share = differing / pixels covered in either image.
Then tools/check_tower_gaps.py and tools/check_tower_overlaps.py run on before and after.
"""
from __future__ import annotations
import argparse, importlib.util, json, math, struct, sys, time
from pathlib import Path
import numpy as np
from PIL import Image, ImageDraw, ImageFont
import shapely
from shapely.geometry import Polygon, MultiPolygon
from shapely.ops import unary_union
from shapely.strtree import STRtree

ROOT = Path(__file__).resolve().parents[1]
FIGHT = 0.006          # m: same-facing surfaces closer than this z-fight (build_tower_assets.FIGHT)
PLANE_TOL = 2e-4       # m: faces whose offsets chain within this are one plane
GRID = 1e-7            # m: polygon precision
LOG = []


def log(*a):
    s = ' '.join(str(x) for x in a); LOG.append(s); print(s, flush=True)


# ------------------------------------------------------------------ glb io
def read_glb(path):
    data = Path(path).read_bytes()
    jl, = struct.unpack_from('<I', data, 12); js = json.loads(data[20:20 + jl]); binb = data[20 + jl + 8:]
    def acc(i):
        a = js['accessors'][i]; v = js['bufferViews'][a['bufferView']]
        dt = {5126: '<f4', 5123: '<u2', 5125: '<u4'}[a['componentType']]; n = {'SCALAR': 1, 'VEC2': 2, 'VEC3': 3}[a['type']]
        arr = np.frombuffer(binb, dt, a['count'] * n, v.get('byteOffset', 0) + a.get('byteOffset', 0))
        return arr.reshape(a['count'], n) if n > 1 else arr
    parts, prims = {}, {}
    for nd in js['nodes']:
        if 'mesh' not in nd: continue
        Ps, Ns, Us = [], [], []; prims[nd['name']] = []
        for pr in js['meshes'][nd['mesh']]['primitives']:
            I = acc(pr['indices']).astype(np.int64).reshape(-1, 3); pos = acc(pr['attributes']['POSITION']).astype(float)
            Ps.append(pos[I]); Ns.append(acc(pr['attributes']['NORMAL']).astype(float)[I][:, 0]); Us.append(acc(pr['attributes']['TEXCOORD_0']).astype(float)[I])
            prims[nd['name']].append(dict(vertices=int(len(pos)), triangles=int(len(I)), index=('u16' if pr_ctype(js, pr) == 5123 else 'u32')))
        parts[nd['name']] = (np.concatenate(Ps), np.concatenate(Ns), np.concatenate(Us))
    return js, parts, prims


def pr_ctype(js, pr): return js['accessors'][pr['indices']]['componentType']


def world_offsets(js):
    nodes = js['nodes']; out = {}
    def walk(i, t):
        t = t + np.array(nodes[i].get('translation', [0, 0, 0]), float); out[nodes[i]['name']] = t
        for c in nodes[i].get('children', []): walk(c, t)
    for r in js['scenes'][0]['nodes']: walk(r, np.zeros(3))
    return out


def write_glb(path, js_src, meshes):
    """Same node list (names, order, translations, children) as js_src; meshes: name -> [(pos, nrm, uv, idx)] (< 65,536 vertices each)."""
    bin_ = bytearray(); views, accessors, gmeshes = [], [], []
    def add_view(data, target):
        while len(bin_) % 4: bin_.append(0)
        off = len(bin_); bin_.extend(data); views.append(dict(buffer=0, byteOffset=off, byteLength=len(data), target=target)); return len(views) - 1
    def add_acc(arr, ctype, typ, target, minmax=False):
        a = dict(bufferView=add_view(arr.tobytes(), target), componentType=ctype, count=int(arr.shape[0]), type=typ)
        if minmax: a['min'] = arr.min(0).tolist(); a['max'] = arr.max(0).tolist()
        accessors.append(a); return len(accessors) - 1
    nodes = []
    for nd in js_src['nodes']:
        g = {k: v for k, v in nd.items() if k != 'mesh'}
        if nd['name'] in meshes:
            prs = []
            for pos, nrm, uv, idx in meshes[nd['name']]:
                assert len(pos) < 65536
                prs.append(dict(attributes=dict(POSITION=add_acc(pos.astype('<f4'), 5126, 'VEC3', 34962, True), NORMAL=add_acc(nrm.astype('<f4'), 5126, 'VEC3', 34962),
                                                TEXCOORD_0=add_acc(uv.astype('<f4'), 5126, 'VEC2', 34962)),
                                indices=add_acc(idx.astype('<u2'), 5123, 'SCALAR', 34963), material=0))
            gmeshes.append(dict(name=nd['name'], primitives=prs)); g['mesh'] = len(gmeshes) - 1
        nodes.append(g)
    gltf = dict(asset=dict(version='2.0', generator='MechDefense reduce_tower'), scene=0, scenes=js_src['scenes'], nodes=nodes, meshes=gmeshes,
                materials=js_src['materials'], accessors=accessors, bufferViews=views, buffers=[dict(byteLength=len(bin_))])
    jsb = json.dumps(gltf, separators=(',', ':')).encode(); jsb += b' ' * (-len(jsb) % 4); bin_ += b'\0' * (-len(bin_) % 4)
    Path(path).write_bytes(struct.pack('<III', 0x46546C67, 2, 12 + 8 + len(jsb) + 8 + len(bin_)) + struct.pack('<II', len(jsb), 0x4E4F534A) + jsb
                           + struct.pack('<II', len(bin_), 0x004E4942) + bytes(bin_))


# ------------------------------------------------------------------ GPU visibility
VS = '''#version 430
uniform mat4 mvp; in vec3 pos; in float tid; flat out int v_id;
void main(){ gl_Position = mvp * vec4(pos, 1.0); v_id = int(tid); }'''
VS_D = '''#version 430
uniform mat4 mvp; in vec3 pos; void main(){ gl_Position = mvp * vec4(pos, 1.0); }'''
FS_D = '''#version 430
out vec4 color; void main(){ color = vec4(1.0); }'''
FS_FLAG = '''#version 430
layout(early_fragment_tests) in;
flat in int v_id; layout(std430, binding=0) buffer Vis { uint vis[]; }; out vec4 color;
void main(){ atomicAdd(vis[v_id], 1u); color = vec4(1.0); }'''


def fib(n):
    i = np.arange(n) + .5; phi = np.arccos(1 - 2 * i / n); th = math.pi * (1 + 5 ** .5) * i
    return np.c_[np.cos(th) * np.sin(phi), np.cos(phi), np.sin(th) * np.sin(phi)]


def look(eye, target, up=None):
    z = eye - target; z = z / np.linalg.norm(z)
    if up is None: up = np.array([0, 1.0, 0]) if abs(z[1]) < .99 else np.array([0, 0, 1.0])
    x = np.cross(up, z); x /= np.linalg.norm(x); y = np.cross(z, x)
    V = np.eye(4); V[0, :3], V[1, :3], V[2, :3] = x, y, z; V[:3, 3] = -V[:3, :3] @ eye; return V


def ortho(d, center, half):
    V = look(center + d, center); V[:3, 3] = -V[:3, :3] @ center
    O = np.diag([1 / half, 1 / half, -1 / (2 * half), 1.0]); return O @ V


def persp(eye, target, fov=100.0, near=0.05, far=60.0, aspect=1.0):
    f = 1 / math.tan(math.radians(fov) / 2); P = np.zeros((4, 4))
    P[0, 0] = f / aspect; P[1, 1] = f; P[2, 2] = (far + near) / (near - far); P[2, 3] = 2 * far * near / (near - far); P[3, 2] = -1
    return P @ look(eye, target)


def ring_cameras(lo, hi, n_az=16):
    c = (lo + hi) / 2; r0 = max(hi[0] - lo[0], hi[2] - lo[2]) / 2; cams = []
    for r in (r0 + 0.8, r0 + 2.5):
        for h in (0.15, 0.9, 1.7, 2.6, hi[1] + 0.5, hi[1] + 2.0):
            for k in range(n_az):
                a = 2 * math.pi * (k + .5 * (r > r0 + 1)) / n_az; eye = np.array([c[0] + r * math.sin(a), h, c[2] + r * math.cos(a)])
                for ty in (0.25 * hi[1], 0.6 * hi[1]): cams.append(persp(eye, np.array([c[0], ty, c[2]])))
    return cams


class GPU:
    def __init__(self):
        import moderngl
        self.mgl = moderngl; self.ctx = moderngl.create_standalone_context(require=430)
        self.p_d = self.ctx.program(vertex_shader=VS_D, fragment_shader=FS_D); self.p_f = self.ctx.program(vertex_shader=VS, fragment_shader=FS_FLAG)
        self.fbos = {}

    def fbo(self, res):
        if res not in self.fbos:
            self.fbos[res] = self.ctx.framebuffer(color_attachments=[self.ctx.renderbuffer((res, res), 4)], depth_attachment=self.ctx.depth_renderbuffer((res, res)))
        return self.fbos[res]

    def flags(self, occluders, flagged, ids, n_ids, views, offset=None):
        """occluders: (m,3,3) drawn into depth; flagged: (k,3,3) with ids (k,): which ids get a fragment that passes.
        views: [(res, mvp)]. offset: polygon offset (factor, units) for the flagged pass (pushes them back), depth test '<'
        when given, '<=' otherwise (the flagged triangles are the occluders themselves)."""
        mgl = self.mgl; ctx = self.ctx
        vb_o = ctx.buffer(occluders.reshape(-1, 3).astype('f4').tobytes()); va_o = ctx.vertex_array(self.p_d, [(vb_o, '3f', 'pos')])
        data = np.c_[flagged.reshape(-1, 3).astype('f4'), np.repeat(ids.astype('f4'), 3)].astype('f4')
        vb_f = ctx.buffer(data.tobytes()); va_f = ctx.vertex_array(self.p_f, [(vb_f, '3f 1f', 'pos', 'tid')])
        ssbo = ctx.buffer(np.zeros(max(1, n_ids), 'u4').tobytes()); ssbo.bind_to_storage_buffer(0)
        ctx.disable(mgl.CULL_FACE); ctx.enable(mgl.DEPTH_TEST)
        for res, M in views:
            fb = self.fbo(res); fb.use(); fb.clear(0, 0, 0, 0, depth=1.0); fb.depth_mask = True; ctx.depth_func = '<'
            m = M.T.astype('f4').tobytes(); self.p_d['mvp'].write(m); va_o.render()
            fb.depth_mask = False; self.p_f['mvp'].write(m)
            if offset: ctx.depth_func = '<'; ctx.polygon_offset = offset
            else: ctx.depth_func = '<='
            va_f.render(); ctx.polygon_offset = (0, 0); fb.depth_mask = True
        ctx.finish(); self.counts = np.frombuffer(ssbo.read(), 'u4')[:n_ids].copy(); out = self.counts > 0
        for o in (va_o, vb_o, va_f, vb_f, ssbo): o.release()
        return out


def view_set(P_world, lo, hi, n_dirs=320, res=4096, ring_res=2048):
    c = (P_world.reshape(-1, 3).min(0) + P_world.reshape(-1, 3).max(0)) / 2
    half = float(np.linalg.norm(P_world.reshape(-1, 3) - c, axis=1).max()) * 1.02
    return [(res, ortho(d, c, half)) for d in fib(n_dirs)] + [(ring_res, M) for M in ring_cameras(lo, hi)]


# ------------------------------------------------------------------ planes and islands
def frame_of(n):
    e1 = np.cross(n, [0, 0, 1.0] if abs(n[2]) < .9 else [1.0, 0, 0]); e1 /= np.linalg.norm(e1); return e1, np.cross(n, e1)


def cluster_planes(P, N):
    """-> plane id per triangle, list of (n, d) per plane (offsets chained within PLANE_TOL)."""
    n = N / np.linalg.norm(N, axis=1, keepdims=True); d = np.einsum('ij,ij->i', n, P.mean(1))
    nk = np.round(n * 2000).astype(np.int64); _, ng = np.unique(nk, axis=0, return_inverse=True); ng = ng.reshape(-1)
    pid = np.full(len(P), -1); planes = []
    area = np.linalg.norm(np.cross(P[:, 1] - P[:, 0], P[:, 2] - P[:, 0]), axis=1) / 2
    for g in range(ng.max() + 1):
        idx = np.flatnonzero(ng == g); idx = idx[np.argsort(d[idx])]
        cuts = np.flatnonzero(np.diff(d[idx]) > PLANE_TOL) + 1
        for chunk in np.split(idx, cuts):
            w = area[chunk] + 1e-12; nn = (n[chunk] * w[:, None]).sum(0); nn /= np.linalg.norm(nn)
            pid[chunk] = len(planes); planes.append((nn, float((d[chunk] * w).sum() / w.sum())))
    return pid, planes


def tex_key(tri_xy, tri_uv, atlas):
    """Texture class of one triangle: ('u', rgba) flat colour, or ('m', axes+scale, phase) its texel grid. Also the
    affine map (2x3) plane xy -> texel coords."""
    H, W = atlas.shape[:2]; uvp = tri_uv * [W, H]
    u0, v0 = np.floor(uvp.min(0) + 1e-6).astype(int); u1, v1 = np.ceil(uvp.max(0) - 1e-6).astype(int)
    u1 = max(u1, u0 + 1); v1 = max(v1, v0 + 1)
    blk = atlas[max(v0, 0):min(v1, H), max(u0, 0):min(u1, W)].reshape(-1, 4)
    A = np.c_[tri_xy, np.ones(3)]
    try: J = np.linalg.solve(A, uvp).T
    except np.linalg.LinAlgError: J = None
    if len(blk) and (blk == blk[0]).all(): return ('u', tuple(int(x) for x in blk[0])), J
    if J is None: return ('u', tuple(int(x) for x in blk[len(blk) // 2])), None
    L = np.round(J[:, :2], 3); o = np.round(np.mod(J[:, 2], 1.0), 3) % 1.0
    return ('m', tuple(L.reshape(-1)), tuple(o)), J


def polys_of(g):
    if g is None or g.is_empty: return []
    if g.geom_type == 'Polygon': return [g] if g.area > 0 else []
    return [x for x in getattr(g, 'geoms', []) for x in polys_of(x)]


def snap(g): return shapely.set_precision(g, GRID)


def cross2(a, b): return a[0] * b[1] - a[1] * b[0]


class Island:
    __slots__ = ('plane', 'key', 'poly', 'tris', 'J', 'grid')
    def __init__(self, plane, key, poly, tris, J):
        self.plane, self.key, self.poly, self.tris, self.J = plane, key, poly, tris, J; self.grid = None


def ring_coords(r): return [tuple(c) for c in list(r.coords)[:-1]]


def remove_collinear(poly, eps=1e-5):
    def clean(cs):
        cs = list(cs); changed = True
        while changed and len(cs) > 3:
            changed = False
            for i in range(len(cs)):
                a, b, c = np.array(cs[i - 1]), np.array(cs[i]), np.array(cs[(i + 1) % len(cs)])
                ab = c - a; L = np.linalg.norm(ab)
                if L < 1e-12 or abs(cross2(ab, b - a)) / L < eps and np.dot(b - a, ab) > 0 and np.dot(c - b, ab) > 0:
                    del cs[i]; changed = True; break
        return cs
    try:
        p = Polygon(clean(ring_coords(poly.exterior)), [clean(ring_coords(h)) for h in poly.interiors])
        return p if p.is_valid and abs(p.area - poly.area) < 1e-9 + 1e-4 * poly.area else poly
    except Exception:
        return poly


# ------------------------------------------------------------------ reduce one part
def reduce_part(name, P, N, U, atlas, offset_w, lo, hi, gpu, tol, fold_width, stats, merge_mode='plane', bake_scale=2, max_passes=5, flatten_h=0.0, support=None, min_plane_frag=0):
    t0 = time.time(); nT = len(P)
    Pw = P + offset_w
    # 0) exact visibility, part on its own
    views = view_set(Pw, lo, hi)
    occ = Pw if support is None else np.concatenate([Pw, support])
    vis = gpu.flags(occ, Pw, np.arange(nT), nT, views); frag = gpu.counts.copy()
    stats['visible'] = int(vis.sum()); log(name, 'triangles', nT, 'visible', int(vis.sum()), f'{time.time() - t0:.0f}s')
    # 1) planes (all triangles: hidden ones tell where the original surface was)
    pid, planes = cluster_planes(P, N)
    area = np.linalg.norm(np.cross(P[:, 1] - P[:, 0], P[:, 2] - P[:, 0]), axis=1) / 2
    frames = [frame_of(n) for n, _ in planes]
    pf = np.bincount(pid, weights=frag, minlength=len(planes)); pv = np.bincount(pid, weights=vis, minlength=len(planes))
    stats['planes_by_fragments'] = {f'<{k}': dict(planes=int(((pf < k) & (pv > 0)).sum()), visible_tris=int(vis[(pf[pid] < k)].sum())) for k in (4, 16, 64, 256, 1024)}
    if min_plane_frag > 0:                                  # planes seen only through hairline cracks or as sub-pixel slivers
        drop = (pf[pid] < min_plane_frag) & vis; vis = vis & ~drop; stats['dropped_low_fragment_tris'] = int(drop.sum())
    moved_mask = np.zeros(nT, bool)
    if flatten_h > 0:
        P = P.copy(); vis = vis.copy()
        moved_mask = flatten_proud(P, pid, planes, frames, vis, area, flatten_h, stats)
        area = np.linalg.norm(np.cross(P[:, 1] - P[:, 0], P[:, 2] - P[:, 0]), axis=1) / 2
    xy = np.zeros((nT, 3, 2))
    for k, (n, d) in enumerate(planes):
        m = pid == k; e1, e2 = frames[k]; xy[m] = np.stack([P[m] @ e1, P[m] @ e2], -1)
    keys = [None] * nT; Js = [None] * nT
    for t in np.flatnonzero(vis): keys[t], Js[t] = tex_key(xy[t], U[t], atlas)
    # same-facing surfaces within FIGHT in front of / behind each plane: islands must not grow over them
    by_plane = {}
    for t in range(nT): by_plane.setdefault(pid[t], []).append(t)
    vis_planes = sorted({pid[t] for t in np.flatnonzero(vis)})
    nk = np.round(np.array([n for n, _ in planes]) * 2000).astype(np.int64)
    pl_d = np.array([d for _, d in planes])
    islands = []; forbid_fight = {}; all_cover = {}
    Nn = N / np.linalg.norm(N, axis=1, keepdims=True); tlo = P.min(1); thi = P.max(1)
    all_cover_pre = {}; reg_box = {}
    for p in vis_planes:
        mem = by_plane[p]; reg_box[p] = (P[mem].reshape(-1, 3).min(0), P[mem].reshape(-1, 3).max(0)); all_cover_pre[p] = None
    for p in vis_planes:
        n, d = planes[p]; e1, e2 = frames[p]
        # z-fight zone (build_tower_assets rule): triangles of other planes facing the same way or the opposite way
        # within ~1.8 deg whose corners all lie within FIGHT of this plane
        rlo, rhi = reg_box[p]
        cand = np.flatnonzero((np.abs(Nn @ n) > 0.9995) & (pid != p) & vis & np.all(tlo <= rhi + FIGHT, 1) & np.all(thi >= rlo - FIGHT, 1))
        if len(cand):
            off_ = P[cand] @ n - d; cand = cand[np.all(np.abs(off_) <= FIGHT, 1)]
        fz = [Polygon(np.c_[P[t] @ e1, P[t] @ e2]) for t in cand if area[t] > 1e-12]
        forbid_fight[p] = snap(unary_union(fz)) if fz else None
        all_cover[p] = snap(unary_union([Polygon(xy[t]) for t in by_plane[p] if area[t] > 1e-12]))
        groups = {}
        for t in by_plane[p]:
            if vis[t] and area[t] > 1e-12: groups.setdefault(keys[t] if merge_mode == 'exact' else ('p',), []).append(t)
        taken = None
        for key in sorted(groups, key=lambda k: -sum(area[t] for t in groups[k])):
            g = snap(unary_union([Polygon(xy[t]) for t in groups[key]]))
            if taken is not None: g = snap(g.difference(taken))
            taken = g if taken is None else snap(unary_union([taken, g]))
            J = next((Js[t] for t in groups[key] if Js[t] is not None), None)
            for poly in polys_of(g):
                if poly.area < 1e-10: continue
                if key == ('p',):                               # whole plane: flat if every visible triangle in it has one colour
                    inside = [t for t in groups[key] if poly.buffer(1e-6).contains(Polygon(xy[t]).representative_point())]
                    ks = {keys[t] for t in inside}
                    if len(ks) == 1 and next(iter(ks))[0] == 'u': islands.append(Island(p, next(iter(ks)), remove_collinear(poly), inside, None)); continue
                    I = Island(p, key, remove_collinear(poly), inside, None)
                    cls = {}
                    for t in inside:
                        if keys[t][0] == 'm': cls.setdefault(keys[t], [0.0, Js[t]])[0] += area[t]
                    if cls:                                     # dominant texel grid, k times finer (its texel edges stay texel edges)
                        Jd = max(cls.values(), key=lambda v: v[0])[1]; I.grid = (bake_scale * Jd[:, :2], bake_scale * Jd[:, 2])
                    else:                                       # several flat colours: grid along the outline's main direction, 64 k texels/m
                        cs = np.array(poly.exterior.coords); ed = np.diff(cs, axis=0); ln = np.linalg.norm(ed, axis=1)
                        ang = np.mod(np.arctan2(ed[:, 1], ed[:, 0]), math.pi / 2); hb = np.histogram(ang, bins=90, range=(0, math.pi / 2), weights=ln)[0]
                        th = (np.argmax(hb) + .5) * (math.pi / 2) / 90; c_, s_ = math.cos(th), math.sin(th)
                        I.grid = (64.0 * bake_scale * np.array([[c_, s_], [-s_, c_]]), np.zeros(2))
                    I.tris = sorted(by_plane[p], key=lambda t: (vis[t], moved_mask[t], -area[t]))   # painted in order, last wins: visible over hidden, flattened plates over the surface under them, small over large
                    islands.append(I); continue
                I = Island(p, key, remove_collinear(poly), groups[key], J)
                if key[0] == 'm': I.grid = (J[:, :2], J[:, 2])
                islands.append(I)
    stats['planes'] = len(vis_planes); stats['islands'] = len(islands)
    tri_count = lambda: sum(len(I.poly.exterior.coords) - 3 + sum(len(h.coords) - 1 for h in I.poly.interiors) + 2 * len(I.poly.interiors) - 0 for I in islands)
    stats['tris_after_merge'] = tri_count(); log(name, 'planes', len(vis_planes), 'islands', len(islands), 'tris after merge', stats['tris_after_merge'])
    # 2) fold small slopes (optional)
    if fold_width > 0:
        fold_small(islands, planes, frames, fold_width, stats, forbid_fight)
        stats['tris_after_fold'] = tri_count(); log(name, 'fold', json.dumps(stats['fold']), 'tris now', stats['tris_after_fold'])
    # 3) fills: holes + outward notches, per pass; GPU-tested when larger than tol
    occl = Pw if support is None else np.concatenate([Pw, support])   # original part (+ support): the depth fills are tested against
    fill_views = view_set(Pw, lo, hi, n_dirs=160, res=4096, ring_res=2048)
    by_plane_isl = {}
    for i, I in enumerate(islands): by_plane_isl.setdefault(I.plane, []).append(i)
    rejected = set()
    for it in range(max_passes):
        cands = []                                          # (island index, kind, payload, 2D triangle list, needs_gpu)
        for i, I in enumerate(islands):
            p = I.plane; others = [islands[j].poly for j in by_plane_isl[p] if j != i]
            forb = [x for x in others] + ([forbid_fight[p]] if forbid_fight[p] is not None else [])
            I_forb = snap(unary_union(forb)) if forb else None
            if I_forb is not None and I_forb.is_empty: I_forb = None
            if I_forb is not None: shapely.prepare(I_forb)
            # holes
            for hi_, h in enumerate(I.poly.interiors):
                hp = Polygon(h)
                if I_forb is not None and I_forb.intersects(hp) and hp.intersection(I_forb).area > 1e-9: continue
                covered = hp.difference(all_cover[I.plane]).area < 1e-9
                if not covered and (i, 'hole', round(hp.area, 12), tuple(np.round(hp.centroid.coords[0], 7))) in rejected: continue
                cands.append((i, 'hole', hi_, [hp], not covered))
            # reflex vertices of the exterior ring (counter-clockwise) and convex vertices of holes (clockwise rings seen from the hole)
            for ri, ring in enumerate([I.poly.exterior] + list(I.poly.interiors)):
                cs = ring_coords(ring)
                if len(cs) <= 3: continue
                ccw = Polygon(cs).exterior.is_ccw if ri == 0 else not Polygon(cs).exterior.is_ccw
                used = set()
                for k in range(len(cs)):
                    if k in used or (k - 1) % len(cs) in used or (k + 1) % len(cs) in used: continue
                    a, b, c = np.array(cs[k - 1]), np.array(cs[k]), np.array(cs[(k + 1) % len(cs)])
                    cr = cross2(b - a, c - b)
                    if (cr < 0) != ccw: continue                # not reflex (for holes: ring orientation flipped)
                    if ri == 0 and cr == 0: continue
                    L = np.linalg.norm(c - a)
                    if L < 1e-9: continue
                    h_ = abs(cross2(c - a, b - a)) / L
                    tri = Polygon([tuple(a), tuple(b), tuple(c)])
                    if tri.area < 1e-14: continue
                    if I_forb is not None and I_forb.intersects(tri) and tri.intersection(I_forb).area > 1e-10: continue
                    gpu_needed = h_ > tol
                    if gpu_needed and (i, tuple(np.round(np.r_[a, b, c], 7))) in rejected: continue
                    cands.append((i, 'v', (ri, k), [tri], gpu_needed)); used.add(k)
        if not cands: break
        # GPU test for the ones that would change what is visible
        need = [c for c in cands if c[4]]
        ok_gpu = set()
        if need:
            T3, ids = [], []
            for ci, c in enumerate(need):
                I = islands[c[0]]; n, d = planes[I.plane]; e1, e2 = frames[I.plane]
                for poly in c[3]:
                    for tri in shapely.constrained_delaunay_triangles(poly).geoms:
                        q = np.array(tri.exterior.coords)[:3]; T3.append(q[:, :1] * e1 + q[:, 1:2] * e2 + d * n + offset_w); ids.append(ci)
            if T3:
                seen = gpu.flags(occl, np.array(T3), np.array(ids), len(need), fill_views, offset=(1.0, 8.0))
                ok_gpu = {id(need[ci]) for ci in range(len(need)) if not seen[ci]}
                for ci in range(len(need)):
                    if seen[ci]:
                        c = need[ci]; g = c[3][0]
                        if c[1] == 'hole': rejected.add((c[0], 'hole', round(g.area, 12), tuple(np.round(g.centroid.coords[0], 7))))
                        else: rejected.add((c[0], tuple(np.round(np.array(g.exterior.coords)[:3].reshape(-1), 7))))
        # apply, island by island, re-checking against fills accepted this pass in the same plane
        accepted = {}; napplied = 0
        per_island = {}
        for c in cands:
            if c[4] and id(c) not in ok_gpu: continue
            per_island.setdefault(c[0], []).append(c)
        for i, cs in per_island.items():
            I = islands[i]; poly = I.poly; plane_acc = accepted.setdefault(I.plane, [])
            holes = list(I.poly.interiors); drop_holes = set(); ext_rm = set(); hole_rm = {}
            for c in cs:
                g = c[3][0]
                if any(g.intersection(x).area > 1e-10 for x in plane_acc): continue
                if c[1] == 'hole': drop_holes.add(c[2])
                else:
                    ri, k = c[2]
                    (ext_rm if ri == 0 else hole_rm.setdefault(ri - 1, set())).add(k)
                plane_acc.append(g)
            ext = [p for k, p in enumerate(ring_coords(poly.exterior)) if k not in ext_rm]
            hs = []
            for hi_, h in enumerate(holes):
                if hi_ in drop_holes: continue
                hc = [p for k, p in enumerate(ring_coords(h)) if k not in hole_rm.get(hi_, set())]
                if len(hc) >= 3: hs.append(hc)
            try:
                newp = Polygon(ext, hs) if len(ext) >= 3 else None
            except Exception:
                newp = None
            if newp is None or not newp.is_valid or newp.area < poly.area - 1e-10 or not newp.buffer(1e-7).contains(poly):
                # fall back: apply candidates one by one
                newp = poly
                for c in cs:
                    trial = snap(unary_union([newp] + c[3]))
                    pp = polys_of(trial)
                    if len(pp) == 1 and pp[0].is_valid: newp = pp[0]
            if newp is not poly:
                newp = remove_collinear(newp)
                if len(newp.exterior.coords) - 1 + sum(len(h.coords) - 1 for h in newp.interiors) < len(poly.exterior.coords) - 1 + sum(len(h.coords) - 1 for h in poly.interiors):
                    I.poly = newp; napplied += 1
        log(name, 'fill pass', it, 'candidates', len(cands), 'gpu tested', len(need), 'islands changed', napplied, 'tris now', tri_count(), f'{time.time() - t0:.0f}s')
        if napplied == 0: break
    stats['tris_after_fill'] = tri_count()
    return islands, planes, frames


def edge_dirs(g, min_len=0.002):
    """Outline edge directions (degrees mod 90) and lengths of a polygon or multipolygon."""
    angs, lens = [], []
    for poly in polys_of(g):
        for ring in [poly.exterior] + list(poly.interiors):
            cs = np.array(ring.coords); ed = np.diff(cs, axis=0); ln = np.linalg.norm(ed, axis=1); k = ln > min_len
            angs.append(np.degrees(np.arctan2(ed[k, 1], ed[k, 0])) % 90); lens.append(ln[k])
    return (np.concatenate(angs), np.concatenate(lens)) if angs else (np.zeros(0), np.zeros(0))


def main_dir(g):
    a, l = edge_dirs(g)
    if not len(a): return None
    h = np.histogram(a, bins=180, range=(0, 90), weights=l)[0]; return (np.argmax(h) + .5) * 0.5


def along(a, ref, tol=2.0): return np.abs(((a - ref + 45) % 90) - 45) <= tol


def flatten_proud(P, pid, planes, frames, vis, area, max_h, stats):
    """Plates and bands standing at most max_h proud of a same-facing surface they sit on (V5's 'proud' paint bands,
    thin plates) are moved onto that surface, their colour baked into it, and their side steps (no taller than max_h)
    dropped. Changes P, pid, vis in place."""
    nk = np.round(np.array([n for n, _ in planes]) * 2000).astype(np.int64); pd = np.array([d for _, d in planes])
    members = {}
    for t in range(len(P)): members.setdefault(pid[t], []).append(t)
    has_vis = {p for p in members if vis[members[p]].any()}
    def region(p, frame):
        e1, e2 = frame; return snap(unary_union([Polygon(np.c_[P[t] @ e1, P[t] @ e2]) for t in members[p] if area[t] > 1e-12]))
    target = {}; skipped_diag = 0
    for p in sorted(has_vis, key=lambda q: -pd[q]):
        cand = [q for q in np.flatnonzero((nk == nk[p]).all(1)) if q in has_vis and PLANE_TOL < pd[p] - pd[q] <= max_h]
        if not cand: continue
        Dr = region(p, frames[p])
        if Dr.is_empty or Dr.boundary.length <= 0: continue
        for q in sorted(cand, key=lambda q: pd[p] - pd[q]):
            Sr = region(q, frames[p])
            if Sr.is_empty: continue
            near = Dr.boundary.intersection(Sr.buffer(0.003)).length / Dr.boundary.length
            if near < 0.4: continue
            # only plates whose outline runs along the surface's own edge directions: their colour then becomes texel
            # rows and columns of the rebaked grid; diagonal or turned decals (warning triangles) would alias away
            a, l = edge_dirs(Dr); ref = main_dir(Sr)
            if ref is None or not along(a, ref).all(): skipped_diag += 1; break
            target[p] = q; break
    moved = dropped = 0; moved_mask = np.zeros(len(P), bool)
    Nt = np.array([planes[pid[t]][0] for t in range(len(P))]); tlo = P.min(1); thi = P.max(1)
    for p, q in target.items():
        while q in target and pd[p] - pd[target[q]] <= max_h: q = target[q]
        n, d = planes[q]; dtop = pd[p]
        if not members[p]: continue
        Dr = region(p, frames[q]); ring = Dr.boundary.buffer(0.001); shapely.prepare(ring)
        plo = np.min([P[t].min(0) for t in members[p]], 0) - 0.002; phi = np.max([P[t].max(0) for t in members[p]], 0) + 0.002
        for t in members[p]:
            P[t] = P[t] - (P[t] @ n - d)[:, None] * n; pid[t] = q; moved += 1; moved_mask[t] = True
        members.setdefault(q, []).extend(members[p]); members[p] = []
        # side steps: triangles across this plane, inside the slab between the surface and the old top, on the plate's outline
        e1, e2 = frames[q]
        cand = np.flatnonzero((np.abs(Nt @ n) < 0.1) & np.all(tlo <= phi, 1) & np.all(thi >= plo, 1) & vis)
        h = P[cand] @ n; cand = cand[(h.min(1) >= d - 3e-4) & (h.max(1) <= dtop + 3e-4)]
        if len(cand):
            c2 = P[cand].mean(1); hit = ring.contains(shapely.points(np.c_[c2 @ e1, c2 @ e2]))
            vis[cand[hit]] = False; dropped += int(hit.sum())
    stats['flatten'] = dict(plates=len(target), triangles_moved=moved, side_triangles_dropped=dropped, kept_not_along_grid=skipped_diag)
    return moved_mask


def island_width(poly):
    mrr = poly.minimum_rotated_rectangle
    if mrr.geom_type != 'Polygon': return 0.0, 0.0
    c = np.array(mrr.exterior.coords)[:4]; a = np.linalg.norm(c[1] - c[0]); b = np.linalg.norm(c[2] - c[1]); return min(a, b), max(a, b)


def lift2(plane, frame, xy):
    n, d = plane; e1, e2 = frame; xy = np.asarray(xy, float); return xy[:, :1] * e1 + xy[:, 1:2] * e2 + d * n


def fold_small(islands, planes, frames, width, stats, forbid_fight):
    """Plan step 2: an island narrower than `width` that runs between exactly two faces of other planes (a chamfer or
    slope strip) is removed; both flank faces are extended to their common line (oblique projection of the strip onto
    each flank along the other flank's direction across that line), and end caps get the corner triangle back."""
    alive = [True] * len(islands); W = [island_width(I.poly)[0] for I in islands]
    def bb(I):
        P3 = lift2(planes[I.plane], frames[I.plane], np.array(I.poly.exterior.coords)); return P3.min(0), P3.max(0)
    boxes = [bb(I) for I in islands]; lo = np.array([b[0] for b in boxes]); hi = np.array([b[1] for b in boxes])
    by_plane = {}
    for k, I in enumerate(islands): by_plane.setdefault(I.plane, []).append(k)
    order = sorted([i for i in range(len(islands)) if W[i] < width], key=lambda i: W[i])
    done = skipped = 0; reasons = {}
    def grow(j, add):
        N = islands[j]; forb = [islands[k].poly for k in by_plane[N.plane] if alive[k] and k != j]
        if forbid_fight.get(N.plane) is not None: forb.append(forbid_fight[N.plane])
        if forb: add = add.difference(snap(unary_union(forb)))
        if add.area < 1e-12: return False
        pp = polys_of(snap(unary_union([N.poly, add])))
        if not pp: return False
        N.poly = max(pp, key=lambda g: g.intersection(N.poly).area); return True
    for i in order:
        if not alive[i]: continue
        S = islands[i]; cs = np.array(S.poly.exterior.coords)
        seg = []
        for a, b in zip(cs[:-1], cs[1:]):
            L = np.linalg.norm(b - a); k = max(1, int(L / 0.001)); t = (np.arange(k) + .5) / k; seg.append(a + (b - a) * t[:, None])
        samp3 = lift2(planes[S.plane], frames[S.plane], np.concatenate(seg)); perim = len(samp3)
        slo, shi = samp3.min(0) - 0.003, samp3.max(0) + 0.003
        touch = {}
        for j in np.flatnonzero(np.all((lo <= shi) & (hi >= slo), 1)):
            if j == i or not alive[j] or islands[j].plane == S.plane: continue
            N = islands[j]; n, d = planes[N.plane]; e1, e2 = frames[N.plane]
            near = np.abs(samp3 @ n - d) < 3e-4
            if not near.any(): continue
            dist = shapely.distance(N.poly, shapely.points(np.c_[samp3[near] @ e1, samp3[near] @ e2]))
            c = int((dist < 2.5e-3).sum())
            if c: touch[j] = c
        per_plane = {}
        for j, c in touch.items(): per_plane[islands[j].plane] = per_plane.get(islands[j].plane, 0) + c
        flank_planes = [q for q, c in per_plane.items() if c / perim > 0.15]
        if len(flank_planes) != 2:
            skipped += 1; reasons[len(flank_planes)] = reasons.get(len(flank_planes), 0) + 1; continue
        (n1, d1), (n2, d2) = planes[flank_planes[0]], planes[flank_planes[1]]
        k = np.cross(n1, n2)
        if np.linalg.norm(k) < 0.05: skipped += 1; reasons['parallel'] = reasons.get('parallel', 0) + 1; continue
        k /= np.linalg.norm(k); u1 = np.cross(n1, k); u2 = np.cross(n2, k)        # in-plane directions across the corner line
        S3 = lift2(planes[S.plane], frames[S.plane], cs[:-1])
        ok = True; adds = []
        for q, (n, d), u in ((flank_planes[0], (n1, d1), u2), (flank_planes[1], (n2, d2), u1)):
            t = (d - S3 @ n) / (n @ u); Q3 = S3 + t[:, None] * u; e1, e2 = frames[q]
            g = Polygon(np.c_[Q3 @ e1, Q3 @ e2]).buffer(0)
            if g.is_empty or g.area < 0.2 * S.poly.area * abs(n @ planes[S.plane][0]) or np.abs(t).max() > 3 * width: ok = False; break
            js = [j for j in touch if islands[j].plane == q]; adds.append((max(js, key=lambda j: touch[j]), g))
        if not ok: skipped += 1; reasons['projection'] = reasons.get('projection', 0) + 1; continue
        for j, g in adds: grow(j, g)
        # end caps: islands of other planes the strip touches at its ends get the corner triangle back
        for j in touch:
            q = islands[j].plane
            if q in flank_planes: continue
            n3, d3 = planes[q]
            try: X = np.linalg.solve(np.array([n1, n2, n3]), np.array([d1, d2, d3]))
            except np.linalg.LinAlgError: continue
            pts = samp3[np.abs(samp3 @ n3 - d3) < 3e-4]
            if len(pts) < 2 or np.linalg.norm(X - pts.mean(0)) > 3 * width: continue
            e1, e2 = frames[q]; allp = np.vstack([pts, X])
            grow(j, shapely.MultiPoint(np.c_[allp @ e1, allp @ e2]).convex_hull.buffer(0))
        alive[i] = False; done += 1
    islands[:] = [I for k, I in enumerate(islands) if alive[k]]
    stats['fold'] = dict(narrow=len(order), folded=done, skipped=skipped, skipped_by_reason={str(k): v for k, v in reasons.items()})


# ------------------------------------------------------------------ texture rebake + output
class Packer:
    def __init__(self, width): self.W = width; self.x = 0; self.y = 0; self.row = 0; self.items = []
    def add(self, w, h):
        if self.x + w > self.W: self.y += self.row; self.x = 0; self.row = 0
        pos = (self.x, self.y); self.x += w; self.row = max(self.row, h); return pos
    @property
    def height(self): return self.y + self.row


def island_rect(I):
    L, o = I.grid; cs = np.array(I.poly.exterior.coords); g = cs @ L.T + o
    u0, v0 = np.floor(g.min(0)).astype(int) - 1; u1, v1 = np.ceil(g.max(0)).astype(int) + 1
    return u0, v0, u1 - u0, v1 - v0


def raster_island(I, P, U, frame, atlas, rect):
    """Texels of one island on its grid: each source triangle (later ones win) paints the texel centres it covers with
    the original atlas texel under that point (nearest). Texels no triangle covers (filled areas, padding) copy the
    nearest painted one."""
    H0, W0 = atlas.shape[:2]; L, o = I.grid; Li = np.linalg.inv(L); u0, v0, w, h = rect; e1, e2 = frame
    blk = np.zeros((h, w, 4), np.uint8); got = np.zeros((h, w), bool)
    for t in I.tris:
        q = np.stack([P[t] @ e1, P[t] @ e2], -1)
        try: Jt = np.linalg.solve(np.c_[q, np.ones(3)], U[t] * [W0, H0]).T
        except np.linalg.LinAlgError: continue
        g = q @ L.T + o; a0 = np.floor(g.min(0)).astype(int); a1 = np.ceil(g.max(0)).astype(int)
        a0 = np.maximum(a0, [u0, v0]); a1 = np.minimum(a1, [u0 + w, v0 + h])
        if (a1 <= a0).any(): continue
        gu, gv = np.meshgrid(np.arange(a0[0], a1[0]) + .5, np.arange(a0[1], a1[1]) + .5); pts = np.c_[gu.ravel(), gv.ravel()]
        Tm = np.array([g[1] - g[0], g[2] - g[0]]).T
        if abs(np.linalg.det(Tm)) < 1e-12: continue
        bc = np.linalg.solve(Tm, (pts - g[0]).T).T
        inside = (bc[:, 0] >= -1e-6) & (bc[:, 1] >= -1e-6) & (bc.sum(1) <= 1 + 1e-6); pts = pts[inside]
        if not len(pts): continue
        x = (pts - o) @ Li.T; src = x @ Jt[:, :2].T + Jt[:, 2]
        si = np.floor(src + 1e-7).astype(int); si[:, 0] = np.clip(si[:, 0], 0, W0 - 1); si[:, 1] = np.clip(si[:, 1], 0, H0 - 1)
        dst = np.floor(pts).astype(int) - [u0, v0]
        blk[dst[:, 1], dst[:, 0]] = atlas[si[:, 1], si[:, 0]]; got[dst[:, 1], dst[:, 0]] = True
    if got.any() and not got.all():
        from scipy import ndimage
        _, (iy, ix) = ndimage.distance_transform_edt(~got, return_indices=True); blk = blk[iy, ix]
    elif not got.any():
        t = I.tris[-1]; c = (U[t].mean(0) * [W0, H0]).astype(int); blk[:] = atlas[min(c[1], H0 - 1), min(c[0], W0 - 1)]
    return blk


class Packer:
    def __init__(self, width): self.W = width; self.x = 0; self.y = 0; self.row = 0
    def add(self, w, h):
        if self.x + w > self.W: self.y += self.row; self.x = 0; self.row = 0
        pos = (self.x, self.y); self.x += w; self.row = max(self.row, h); return pos
    @property
    def height(self): return self.y + self.row


def build_outputs(parts_islands, P_all, U_all, atlas, width=1024):
    """Triangulate islands, bake texels into a new atlas -> meshes {part: [(pos, nrm, uv, idx)]}, atlas image, counts."""
    flat_cols = {}; rects = []
    for part, (islands, planes, frames) in parts_islands.items():
        for I in islands:
            if I.key[0] == 'u': flat_cols.setdefault(I.key[1], None)
            else: rects.append((part, I, island_rect(I)))
    rects.sort(key=lambda r: -r[2][3])
    for W in (width, 2 * width, 4 * width):
        if any(r[2][2] > W for r in rects): continue
        pk = Packer(W); fc = {c: pk.add(4, 4) for c in flat_cols}; place = {id(r[1]): pk.add(r[2][2], r[2][3]) for r in rects}
        if pk.height <= W: break
    flat_cols = fc; Wn = W; Hn = 1 << max(4, math.ceil(math.log2(max(1, pk.height))))
    new = np.zeros((Hn, Wn, 4), np.uint8)
    for col, (x, y) in flat_cols.items(): new[y:y + 4, x:x + 4] = col
    for part, I, rect in rects:
        x, y = place[id(I)]; planes, frames = parts_islands[part][1], parts_islands[part][2]
        new[y:y + rect[3], x:x + rect[2]] = raster_island(I, P_all[part], U_all[part], frames[I.plane], atlas, rect)
    meshes, counts = {}, {}
    for part, (islands, planes, frames) in parts_islands.items():
        Ps, Ns, UVs = [], [], []
        for I in islands:
            n, d = planes[I.plane]; e1, e2 = frames[I.plane]
            tris = [np.array(t.exterior.coords)[:3] for t in shapely.constrained_delaunay_triangles(I.poly).geoms if t.area > 1e-14]
            if not tris: continue
            Q = np.array(tris); P3 = Q[..., :1] * e1 + Q[..., 1:2] * e2 + d * n
            flip = np.einsum('ij,j->i', np.cross(P3[:, 1] - P3[:, 0], P3[:, 2] - P3[:, 0]), n) < 0
            P3[flip] = P3[flip][:, [0, 2, 1]]; Q[flip] = Q[flip][:, [0, 2, 1]]
            if I.key[0] == 'u':
                x, y = flat_cols[I.key[1]]; uv = np.broadcast_to(np.array([(x + 2.0) / Wn, (y + 2.0) / Hn]), Q.shape).copy()
            else:
                L, o = I.grid; x, y = place[id(I)]; u0, v0, _, _ = island_rect(I)
                uv = (Q @ L.T + o - [u0, v0] + [x, y]) / [Wn, Hn]
            Ps.append(P3); Ns.append(np.broadcast_to(n, P3.shape).copy()); UVs.append(uv)
        P3 = np.concatenate(Ps).reshape(-1, 3); N3 = np.concatenate(Ns).reshape(-1, 3); UV = np.concatenate(UVs).reshape(-1, 2)
        key = np.round(np.c_[P3 * 1e5, N3 * 1e4, UV * 1e6]).astype(np.int64)
        _, first, inv = np.unique(key, axis=0, return_index=True, return_inverse=True); inv = inv.reshape(-1)
        pos, nrm, uv, idx = P3[first], N3[first], UV[first], inv.reshape(-1, 3)
        prims = []; start = 0
        while start < len(idx):                                 # primitives below 65,536 vertices (16-bit indices)
            used = {}; sel = []
            for t in range(start, len(idx)):
                new_v = [v for v in idx[t] if v not in used]
                if len(used) + len(new_v) > 65535: break
                for v in new_v: used[v] = len(used)
                sel.append(t)
            vs = np.array(sorted(used, key=used.get)); remap = np.full(len(pos), -1); remap[vs] = np.arange(len(vs))
            prims.append((pos[vs], nrm[vs], uv[vs], remap[idx[sel]].reshape(-1).astype(np.uint32))); start += len(sel)
        meshes[part] = prims; counts[part] = dict(triangles=int(len(idx)), primitives=[dict(vertices=int(len(p[0])), triangles=int(len(p[3]) // 3)) for p in prims])
    return meshes, new, counts


# ------------------------------------------------------------------ comparison renders
RVS = '''#version 330
uniform mat4 mvp; in vec3 pos; in vec3 nrm; in vec2 uv; out vec2 v_uv; out vec3 v_n; out vec3 v_p;
void main(){ gl_Position = mvp * vec4(pos, 1.0); v_uv = uv; v_n = nrm; v_p = pos; }'''
RFS = '''#version 330
uniform sampler2D tex; uniform int mode; uniform vec3 eye; uniform int is_ortho; uniform vec3 vdir;
in vec2 v_uv; in vec3 v_n; in vec3 v_p; out vec4 color;
void main(){ vec4 t = texture(tex, v_uv); if (t.a < 0.5) discard;
  vec3 n = normalize(v_n); vec3 toward = is_ortho == 1 ? vdir : normalize(eye - v_p); if (dot(n, toward) < 0.0) n = -n;
  float l = mode == 0 ? 0.45 + 0.55 * max(0.0, dot(n, normalize(vec3(0.4, 0.8, 0.45)))) : 1.0;
  color = vec4(t.rgb * l, 1.0); }'''


def compare_views(lo, hi, muzzle, joint):
    c = (lo + hi) / 2; R = float(np.linalg.norm(hi - lo)) / 2; out = []
    dist = R / math.sin(math.radians(15)) * 1.02
    for az in range(0, 360, 45):
        a, e = math.radians(az), math.radians(20); d = np.array([math.cos(e) * math.sin(a), math.sin(e), math.cos(e) * math.cos(a)])
        out.append((f'around_{az:03d}', c + d * dist, c, 30.0))
    out.append(('top', c + np.array([0, dist, 0.001]), c, 30.0))
    base_corner = np.array([hi[0] * .75, 0.35, hi[2] * .75])
    out.append(('close_muzzle', muzzle + np.array([1.0, 0.6, -1.0]), muzzle, 40.0))
    out.append(('close_base', base_corner + np.array([1.2, 0.5, 1.2]), base_corner, 40.0))
    out.append(('close_joint', joint + np.array([-1.4, 0.5, -0.9]), joint, 40.0))
    return out


class Renderer:
    def __init__(self, res=1024):
        import moderngl
        self.mgl = moderngl; self.ctx = moderngl.create_standalone_context(); self.res = res
        self.prog = self.ctx.program(vertex_shader=RVS, fragment_shader=RFS)
        self.fbo = self.ctx.framebuffer(color_attachments=[self.ctx.texture((res, res), 4)], depth_attachment=self.ctx.depth_texture((res, res)))
    def load(self, P, N, U, atlas):
        data = np.concatenate([P.reshape(-1, 3), np.repeat(N, 3, 0), U.reshape(-1, 2)], 1).astype('f4')
        vbo = self.ctx.buffer(data.tobytes()); vao = self.ctx.vertex_array(self.prog, [(vbo, '3f 3f 2f', 'pos', 'nrm', 'uv')])
        tex = self.ctx.texture((atlas.shape[1], atlas.shape[0]), 4, np.ascontiguousarray(atlas).tobytes()); tex.filter = (self.mgl.NEAREST, self.mgl.NEAREST)
        tex.repeat_x = tex.repeat_y = False
        return vbo, vao, tex
    def render(self, model, eye, target, fov, mode):
        vbo, vao, tex = model
        M = persp(eye, target, fov=fov, near=0.05, far=80.0)
        self.fbo.use(); self.fbo.clear(0, 0, 0, 0, depth=1.0); self.ctx.enable(self.mgl.DEPTH_TEST); self.ctx.disable(self.mgl.CULL_FACE)
        tex.use(0); self.prog['tex'] = 0; self.prog['mode'] = mode; self.prog['mvp'].write(M.T.astype('f4').tobytes())
        self.prog['eye'] = tuple(float(x) for x in eye); self.prog['is_ortho'] = 0; self.prog['vdir'] = (0.0, 0.0, 1.0)
        vao.render()
        return np.frombuffer(self.fbo.read(components=4), np.uint8).reshape(self.res, self.res, 4)[::-1].copy()


def soup_world(js, parts):
    off = world_offsets(js); P, N, U = [], [], []
    for k, (p, n, u) in parts.items(): P.append(p + off[k]); N.append(n); U.append(u)
    return np.concatenate(P), np.concatenate(N), np.concatenate(U)


def diff_metric(a, b, thr=16):
    ca, cb = a[..., 3] > 0, b[..., 3] > 0; cov = ca | cb
    dc = np.abs(a[..., :3].astype(int) - b[..., :3].astype(int)).max(-1)
    differ = cov & ((ca != cb) | (dc > thr))
    return differ, int(cov.sum())


def compare(js0, parts0, atlas0, js1, parts1, atlas1, muzzle, joint, outdir):
    P0, N0, U0 = soup_world(js0, parts0); P1, N1, U1 = soup_world(js1, parts1)
    lo, hi = P0.reshape(-1, 3).min(0), P0.reshape(-1, 3).max(0)
    R = Renderer(1024); m0 = R.load(P0, N0, U0, atlas0); m1 = R.load(P1, N1, U1, atlas1)
    res = {}; imgs = {}
    for mode, mname in ((0, 'day'), (1, 'flat')):
        tot_d = tot_c = 0; per = {}
        for vname, eye, tgt, fov in compare_views(lo, hi, muzzle, joint):
            a = R.render(m0, eye, tgt, fov, mode); b = R.render(m1, eye, tgt, fov, mode)
            differ, cov = diff_metric(a, b)
            per[vname] = dict(share=round(float(differ.sum() / max(1, cov)), 5), px=int(differ.sum()), covered=cov,
                              share_thr8=round(float(diff_metric(a, b, 8)[0].sum() / max(1, cov)), 5), share_thr32=round(float(diff_metric(a, b, 32)[0].sum() / max(1, cov)), 5),
                              coverage_changed=int(((a[..., 3] > 0) != (b[..., 3] > 0)).sum()))
            tot_d += int(differ.sum()); tot_c += cov; imgs[(mname, vname)] = (a, b, differ)
        res[mname] = dict(overall_share=round(tot_d / max(1, tot_c), 5), max_view_share=max(v['share'] for v in per.values()), views=per)
    for mname in ('day', 'flat'):
        sheet(imgs, mname, res[mname], outdir / f'compare_{mname}.jpg')
    return res


def sheet(imgs, mname, res, path):
    names = [k[1] for k in imgs if k[0] == mname]; cell = 300; pad = 6; head = 22
    cols = 2; rows = math.ceil(len(names) / cols)
    W = cols * (3 * cell + 2 * pad) + (cols + 1) * pad * 3; H = 40 + rows * (cell + head + pad)
    im = Image.new('RGB', (W, H), (30, 30, 34)); dr = ImageDraw.Draw(im)
    try: font = ImageFont.truetype('C:/Windows/Fonts/msyh.ttc', 15); big = ImageFont.truetype('C:/Windows/Fonts/msyh.ttc', 18)
    except OSError: font = big = ImageFont.load_default()
    dr.text((pad, 8), f"{'白天光照' if mname == 'day' else '纯贴图颜色'}：每组 左 = 现在的模型　中 = 减面后　右 = 差异（红 = 颜色差 > 16/255 或有无覆盖不同）　总差异 {100 * res['overall_share']:.2f}%　单视角最大 {100 * res['max_view_share']:.2f}%", font=big, fill=(235, 235, 235))
    bg = np.array([70, 74, 80], np.uint8)
    def comp(x):
        out = np.where(x[..., 3:4] > 0, x[..., :3], bg); return Image.fromarray(out.astype(np.uint8)).resize((cell, cell), Image.LANCZOS)
    for k, v in enumerate(names):
        a, b, differ = imgs[(mname, v)]
        x0 = pad * 3 + (k % cols) * (3 * cell + 2 * pad + pad * 3); y0 = 40 + (k // cols) * (cell + head + pad)
        dr.text((x0, y0), f"{v}  差异 {100 * res['views'][v]['share']:.2f}%", font=font, fill=(220, 220, 220))
        gray = (a[..., :3].mean(-1) * 0.6).astype(np.uint8); hm = np.dstack([gray] * 3); hm[a[..., 3] == 0] = bg
        hm[differ] = (255, 40, 40)
        im.paste(comp(a), (x0, y0 + head)); im.paste(comp(b), (x0 + cell + pad, y0 + head))
        # heat map: keep thin differences visible when shrunk
        from scipy import ndimage
        dm = ndimage.binary_dilation(differ, iterations=1); hm[dm] = (255, 40, 40)
        im.paste(Image.fromarray(hm).resize((cell, cell), Image.BILINEAR), (x0 + 2 * (cell + pad), y0 + head))
    im.save(path, quality=88)


# ------------------------------------------------------------------ checks
def run_checks(tid, out, towers_entry):
    """Gap and overlap checks (the repository's own tools) on the current build and on the result."""
    import shutil
    layout = out / 'checks' / 'after_root'; d = layout / 'Mod/Assets/Towers' / tid; d.mkdir(parents=True, exist_ok=True)
    shutil.copy(out / 'model.glb', d / 'model.glb'); shutil.copy(out / 'atlas.png', d / 'atlas.png')
    (layout / 'Mod/Assets/Towers/towers.json').write_text(json.dumps(dict(towers=[towers_entry]), ensure_ascii=False), encoding='utf-8')
    def load_mod(name):
        spec = importlib.util.spec_from_file_location(name + '_r5', ROOT / 'tools' / f'{name}.py'); m = importlib.util.module_from_spec(spec); spec.loader.exec_module(m); return m
    res = {}
    for label, root in (('before', ROOT), ('after', layout)):
        g = load_mod('check_tower_gaps'); g.ROOT = root
        argv = sys.argv; sys.argv = ['check_tower_gaps', '--out', str(out / 'checks' / f'gaps_{label}'), '--ids', tid]
        try: g.main()
        finally: sys.argv = argv
        res[f'gaps_{label}'] = json.loads((out / 'checks' / f'gaps_{label}' / 'summary.json').read_text())[tid]
        o = load_mod('check_tower_overlaps'); o.T = root / 'Mod/Assets/Towers'
        res[f'overlaps_{label}'] = o.check(tid)
    shutil.rmtree(layout, ignore_errors=True)
    return res


# ------------------------------------------------------------------ main
def main():
    ap = argparse.ArgumentParser(); ap.add_argument('id'); ap.add_argument('--out'); ap.add_argument('--tol', type=float, default=0.002)
    ap.add_argument('--fold-width', type=float, default=0.0); ap.add_argument('--no-compare', action='store_true'); ap.add_argument('--atlas-width', type=int, default=1024)
    ap.add_argument('--merge', choices=('plane', 'exact'), default='plane'); ap.add_argument('--bake-scale', type=int, default=2)
    ap.add_argument('--compare-only', action='store_true'); ap.add_argument('--no-checks', action='store_true')
    ap.add_argument('--flatten', type=float, default=0.0, help='m: move plates at most this proud onto the surface under them (0 = off)')
    ap.add_argument('--min-plane-fragments', type=int, default=0, help='drop planes with fewer visible fragments (4096 px views, summed)')
    ap.add_argument('--support', action='store_true', help='treat the full blocks under the footprint as an occluder (towers need them to be placed)')
    a = ap.parse_args(); tid = a.id; T0 = time.time()
    out = Path(a.out) if a.out else ROOT / 'local/claude-work/r5/towers' / tid
    if not out.is_absolute(): out = ROOT / out
    src = ROOT / 'Mod/Assets/Towers' / tid
    assert out.resolve() != src.resolve(), 'never overwrite the current build'
    out.mkdir(parents=True, exist_ok=True)
    js, parts, prims0 = read_glb(src / 'model.glb'); atlas = np.asarray(Image.open(src / 'atlas.png').convert('RGBA')).copy()
    towers = json.loads((ROOT / 'Mod/Assets/Towers/towers.json').read_text(encoding='utf-8'))['towers']; entry = next(t for t in towers if t['id'] == tid)
    off = world_offsets(js)
    Pw = np.concatenate([p + off[k] for k, (p, _, _) in parts.items()]); lo, hi = Pw.reshape(-1, 3).min(0), Pw.reshape(-1, 3).max(0)
    if a.compare_only:
        nj = out / 'numbers.json'
        numbers = json.loads(nj.read_text(encoding='utf-8')) if nj.exists() else dict(id=tid, note='compare only')
        js1, parts1, prims1 = read_glb(out / 'model.glb'); new_atlas = np.asarray(Image.open(out / 'atlas.png').convert('RGBA')).copy()
    else:
        gpu = GPU(); stats = {}; result = {}
        sup = None
        if a.support:                                   # cells x cells blocks right under the tower (footprint centred on the origin)
            cx, _, cz = entry['cells']; x0, x1, z0, z1, y0, y1 = -cx / 2, cx / 2, -cz / 2, cz / 2, -1.0, -0.0005
            C = np.array([[x, y, z] for x in (x0, x1) for y in (y0, y1) for z in (z0, z1)])
            F = [(0, 1, 3, 2), (4, 6, 7, 5), (0, 4, 5, 1), (2, 3, 7, 6), (0, 2, 6, 4), (1, 5, 7, 3)]
            sup = np.array([[C[f[0]], C[f[1]], C[f[2]]] for f in F] + [[C[f[0]], C[f[2]], C[f[3]]] for f in F])
        for name, (P, N, U) in parts.items():
            stats[name] = dict(triangles_in=int(len(P)))
            result[name] = reduce_part(name, P, N, U, atlas, off[name], lo, hi, gpu, a.tol, a.fold_width, stats[name], a.merge, a.bake_scale,
                                       flatten_h=a.flatten, support=sup, min_plane_frag=a.min_plane_fragments)
        meshes, new_atlas, counts = build_outputs(result, {k: v[0] for k, v in parts.items()}, {k: v[2] for k, v in parts.items()}, atlas, a.atlas_width)
        write_glb(out / 'model.glb', js, meshes); Image.fromarray(new_atlas, 'RGBA').save(out / 'atlas.png', optimize=True)
        for k in counts: stats[k]['triangles_out'] = counts[k]['triangles']; stats[k]['primitives_out'] = counts[k]['primitives']
        js1, parts1, prims1 = read_glb(out / 'model.glb')
    assert [n['name'] for n in js1['nodes']] == [n['name'] for n in js['nodes']]
    assert all(np.allclose(n1.get('translation', [0, 0, 0]), n0.get('translation', [0, 0, 0])) for n0, n1 in zip(js['nodes'], js1['nodes']))
    if not a.compare_only: numbers = dict(id=tid, source=dict(glb=str(src / 'model.glb'), atlas=str(src / 'atlas.png')), settings=dict(tol_m=a.tol, fold_width_m=a.fold_width, merge=a.merge, bake_scale=a.bake_scale, flatten_m=a.flatten, support=a.support, min_plane_fragments=a.min_plane_fragments),
                   before=dict(triangles=int(sum(len(p[0]) for p in parts.values())), primitives=prims0, atlas=list(atlas.shape[1::-1])),
                   after=dict(triangles=int(sum(c['triangles'] for c in counts.values())), primitives={k: v['primitives'] for k, v in counts.items()},
                              max_vertices_per_primitive=int(max(p['vertices'] for v in counts.values() for p in v['primitives'])), index='u16',
                              atlas=list(new_atlas.shape[1::-1])),
                   steps=stats, nodes=[dict(name=n['name'], translation=n.get('translation'), children=n.get('children')) for n in js1['nodes']])
    if not a.no_compare:
        mz = entry['muzzles'][0]; muzzle = off[mz['part']] + np.array(mz['local'])
        joint = off.get('pitch', off.get('yaw', np.zeros(3)))          # the cradle / turret pivot
        numbers['difference'] = compare(js, parts, atlas, js1, parts1, new_atlas, muzzle, joint, out)
        if not a.no_checks: numbers['checks'] = run_checks(tid, out, entry)
    numbers['seconds'] = round(time.time() - T0, 1); numbers['log'] = LOG
    (out / 'numbers.json').write_text(json.dumps(numbers, ensure_ascii=False, indent=1), encoding='utf-8')
    log('done', json.dumps(dict(before=numbers['before']['triangles'], after=numbers['after']['triangles'], max_v=numbers['after']['max_vertices_per_primitive'],
                                diff={k: (v['overall_share'], v['max_view_share']) for k, v in numbers.get('difference', {}).items()}), ensure_ascii=False))


if __name__ == '__main__':
    main()
