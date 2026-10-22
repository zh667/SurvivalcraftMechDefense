"""3D models of the progression items (MDPartBlock, Mod/MDParts.cs): scrap, alloy plate, fire-control chip, energy core,
mech blueprint, decoy beacon, weapon kits Mk1-Mk3. Plan confirmed by the user on 2026-10-21: vanilla item style, low-poly
geometry with 32 px pixel textures in one 128 px atlas, face shading baked, each model <= 300 triangles, built by script in
Blender, scale checked against the vanilla iron ingot and battery. Project folder (blend, renders, checks):
E:/projects/md-item-models.

python tools/build_part_models.py [--review <dir>] [--blender <blender.exe>] [--selftest]

Steps: (1) paint the atlas with fixed palettes (Pillow, no generated images) -> Mod/Assets/Parts/parts.png, cell = item data
(4x4 cells of 32 px, row-major); (2) Blender builds every model from the primitives below (boxes, chamfered slabs, prisms),
UV-maps each face to a named tile of the item's cell, bakes the face shading into a colour attribute and writes
Mod/Assets/Parts/part<N>.bin; with --review it also saves <dir>/part_models.blend and renders each model and the vanilla
overlays; (3) the outer script reads the .bin files back and checks them (triangle budget, winding, readback against Blender,
floating parts, near-touching gaps 0.3 mm-3 cm, coplanar overlapping faces of different parts); --selftest first runs the
checks on deliberately broken copies to show they catch each fault.

part<N>.bin (Mod/MDParts.cs MDPartBlock.Mesh): int32 vertex count, int32 index count, per vertex float32 x y z u v shade,
int32 indices. Units: blocks (a full block = 1), SC frame (X right, Y up, Z back), centred on the bounding box; engine
winding (clockwise front, the reverse of Blender's). shade 0-1 is the baked brightness; 2 + shade marks an emissive vertex
(drawn without the world light: the energy core's band, the beacon's lamp, the Mk3 kit's connector).
"""
import json, math, os, struct, subprocess, sys
from pathlib import Path

CELL, GRID, ATLAS = 32, 4, 128
# Draw scale (step 4, calibrated against the vanilla iron ingot): the ingot's mesh is 0.4 x 0.2 x 0.8 after its node's 0.01
# scale and IngotBlock draws it x2, so in hand, on the ground and in a slot it is 1.6 blocks long; the battery (x1) is 1.0
# tall. The models keep the confirmed plan's proportions (alloy plate 0.6 long ...) and MDParts.cs draws them x8/3, so the
# alloy plate is exactly as long as an iron ingot and the energy core (1.28) a little taller than a battery.
DRAW_SCALE = 8 / 3
VANILLA = {'IronIngot': (0.8, 1.6, 0.4), 'Battery': (0.75, 0.75, 1.0)}   # drawn size in blocks: x, depth, height (Blender axes)
NAMES = ['scrap', 'alloy', 'chip', 'core', 'blueprint', 'beacon', 'kit1', 'kit2', 'kit3']

# ----------------------------------------------------------------------------------------------- palette (sprites' colours)
K = (26, 28, 33); M0, M1, M2, M3 = (62, 66, 74), (104, 110, 120), (156, 162, 172), (212, 216, 222)
Y0, Y1 = (176, 138, 24), (232, 196, 52); R0, R1 = (120, 70, 44), (168, 104, 60)
C0, C1, C2 = (150, 84, 44), (196, 118, 66), (236, 168, 112)
G0, G1, G2 = (18, 58, 34), (34, 104, 60), (60, 150, 86); AU, AU2 = (220, 176, 64), (246, 214, 120)
RED, RED2 = (210, 44, 36), (255, 120, 100); E0, E1, E2 = (20, 110, 160), (80, 210, 250), (220, 250, 255)
B0, B1, B2 = (22, 52, 110), (40, 86, 164), (186, 214, 255); W = (214, 232, 255); OR = (240, 140, 40)

# ----------------------------------------------------------------------------------------------- tiles: (x, y, w, h) in the cell
TILES = {
    0: dict(plateTop=(0, 0, 20, 14), plateEdge=(0, 14, 20, 3), plateUnder=(0, 17, 20, 5), stripe=(20, 0, 12, 10), rust=(20, 10, 12, 12),
            dark=(0, 22, 8, 8), bolt=(8, 22, 4, 4)),
    1: dict(top=(0, 0, 24, 16), bevel=(0, 16, 24, 4), side=(0, 20, 24, 3), bottom=(0, 23, 24, 9), rivet=(24, 0, 4, 4), rivetSide=(28, 0, 4, 4)),
    2: dict(board=(0, 0, 24, 24), boardSide=(0, 24, 24, 3), boardUnder=(0, 27, 24, 5), die=(24, 0, 8, 8), dieSide=(24, 8, 8, 3), pin=(24, 12, 4, 4)),
    3: dict(cap=(0, 0, 16, 16), capSide=(0, 16, 16, 4), glow=(16, 0, 16, 24), strut=(0, 20, 4, 12), copper=(4, 20, 4, 4), under=(8, 20, 8, 8)),
    4: dict(sheet=(0, 0, 32, 20), sheetBack=(0, 20, 24, 6), roll=(0, 26, 24, 6), rollEnd=(24, 20, 8, 8), paperEdge=(24, 28, 8, 4)),
    5: dict(box=(0, 0, 16, 12), boxTop=(16, 0, 12, 12), leg=(0, 12, 4, 12), antenna=(4, 12, 2, 12), lamp=(8, 12, 8, 8), foot=(16, 12, 4, 4), dark=(20, 12, 8, 8)),
}
for n in (6, 7, 8):
    TILES[n] = dict(bodySide=(0, 0, 20, 10), bodyTop=(0, 10, 20, 10), bodyEnd=(20, 0, 10, 10), barrel=(0, 20, 16, 6), muzzle=(16, 20, 8, 8),
                    mount=(24, 20, 8, 6), connector=(20, 10, 6, 6), under=(0, 26, 16, 6))
EMISSIVE = {(3, 'glow'), (5, 'lamp'), (8, 'connector')}


# =============================================================================================== geometry (pure Python)
# A part is a convex primitive: a list of faces, each (corners [(x, y, z)...] counter-clockwise seen from outside, in the SC
# frame, u axis, v axis, tile). UVs: each face's corners projected on its own (u, v) axes, stretched over the tile.
def _rot(p, r):
    x, y, z = p; ax, ay, az = (math.radians(a) for a in r)
    y, z = y * math.cos(ax) - z * math.sin(ax), y * math.sin(ax) + z * math.cos(ax)
    x, z = x * math.cos(ay) + z * math.sin(ay), -x * math.sin(ay) + z * math.cos(ay)
    x, y = x * math.cos(az) - y * math.sin(az), x * math.sin(az) + y * math.cos(az)
    return (x, y, z)


def _place(p, at, r):
    q = _rot(p, r); return (q[0] + at[0], q[1] + at[1], q[2] + at[2])


def slab(size, at=(0, 0, 0), rot=(0, 0, 0), tiles=None, skip=(), chamfer=0.0):
    """Box of `size` (w along X, h along Y, d along Z) centred at `at`, rotated by `rot` degrees (X, then Y, then Z).
    tiles: per face name (top, bottom, front (+Z), back (-Z), right (+X), left (-X)) or 'all'; chamfer bevels the 4 top edges."""
    w, h, d = (s / 2 for s in size); t = tiles or {}
    def tile(f): return t.get(f, t.get('side', t.get('all')))
    c = chamfer
    P = lambda x, y, z: _place((x, y, z), at, rot)
    faces = []
    yt = h - c if c else h
    # sides (up to the chamfer), v up
    sides = [('front', [(-w, -h, d), (w, -h, d), (w, yt, d), (-w, yt, d)]), ('back', [(w, -h, -d), (-w, -h, -d), (-w, yt, -d), (w, yt, -d)]),
             ('right', [(w, -h, d), (w, -h, -d), (w, yt, -d), (w, yt, d)]), ('left', [(-w, -h, -d), (-w, -h, d), (-w, yt, d), (-w, yt, -d)])]
    for name, q in sides:
        if name not in skip: faces.append(([P(*v) for v in q], tile(name), name))
    if 'bottom' not in skip: faces.append(([P(-w, -h, -d), P(w, -h, -d), P(w, -h, d), P(-w, -h, d)], tile('bottom'), 'bottom'))
    if c:
        if 'top' not in skip: faces.append(([P(-w + c, h, d - c), P(w - c, h, d - c), P(w - c, h, -d + c), P(-w + c, h, -d + c)], tile('top'), 'top'))
        bev = tile('bevel') if 'bevel' in t else tile('top')
        faces += [([P(-w, yt, d), P(w, yt, d), P(w - c, h, d - c), P(-w + c, h, d - c)], bev, 'bevel'),
                  ([P(w, yt, -d), P(-w, yt, -d), P(-w + c, h, -d + c), P(w - c, h, -d + c)], bev, 'bevel'),
                  ([P(w, yt, d), P(w, yt, -d), P(w - c, h, -d + c), P(w - c, h, d - c)], bev, 'bevel'),
                  ([P(-w, yt, -d), P(-w, yt, d), P(-w + c, h, d - c), P(-w + c, h, -d + c)], bev, 'bevel')]
    elif 'top' not in skip:
        faces.append(([P(-w, h, d), P(w, h, d), P(w, h, -d), P(-w, h, -d)], tile('top'), 'top'))
    return faces


def prism(radius, height, at=(0, 0, 0), rot=(0, 0, 0), n=8, tiles=None, skip=(), r2=None):
    """n-sided prism along Y (radius at the bottom, r2 at the top), centred at `at`; faces 'side', 'top', 'bottom'."""
    t = tiles or {}; r2 = radius if r2 is None else r2; hh = height / 2
    ring = lambda r, y: [(r * math.cos(2 * math.pi * (i + .5) / n), y, -r * math.sin(2 * math.pi * (i + .5) / n)) for i in range(n)]
    lo, hi = ring(radius, -hh), ring(r2, hh)
    P = lambda v: _place(v, at, rot); faces = []
    if 'side' not in skip:
        for i in range(n):
            j = (i + 1) % n
            faces.append(([P(lo[i]), P(lo[j]), P(hi[j]), P(hi[i])], t.get('side', t.get('all')), 'side%d' % i))
    if 'top' not in skip: faces.append(([P(v) for v in hi], t.get('top', t.get('all')), 'top'))
    if 'bottom' not in skip: faces.append(([P(v) for v in reversed(lo)], t.get('bottom', t.get('all')), 'bottom'))
    return faces


def models():
    """item data -> list of parts (each a list of faces). Sizes in blocks, from the confirmed plan."""
    M = {}
    # 0 scrap: three bent armour pieces piled up (about 0.5 x 0.25 x 0.4)
    M[0] = [slab((.24, .045, .30), (-.12, -.090, .02), (0, 0, 18), dict(top='plateTop', bottom='plateUnder', side='plateEdge')),
            slab((.24, .045, .30), (.105, -.085, .02), (0, 0, -16), dict(top='plateTop', bottom='plateUnder', side='plateEdge')),
            slab((.30, .04, .20), (.03, -.010, -.035), (24, 30, 6), dict(top='stripe', bottom='plateUnder', side='plateEdge')),
            slab((.12, .10, .12), (-.13, -.012, .11), (0, 20, 0), dict(all='rust')),
            prism(.026, .05, (-.13, .05, .11), (0, 20, 0), n=6, tiles=dict(all='bolt'), skip=('bottom',))]
    # 1 alloy plate: chamfered thick plate with four rivets (0.6 x 0.08 x 0.4)
    M[1] = [slab((.60, .08, .40), tiles=dict(top='top', bevel='bevel', side='side', bottom='bottom'), chamfer=.02)]
    for x in (-.24, .24):
        for z in (-.14, .14):
            M[1].append(slab((.04, .02, .04), (x, .045, z), tiles=dict(top='rivet', side='rivetSide'), skip=('bottom',)))
    # 2 fire-control chip: circuit board, gold pins on its edges, the die with its red core (0.38 x 0.06 x 0.38)
    M[2] = [slab((.30, .022, .30), (0, -.019, 0), tiles=dict(top='board', side='boardSide', bottom='boardUnder')),
            slab((.15, .03, .15), (0, .007, 0), tiles=dict(top='die', side='dieSide'), skip=('bottom',))]
    for k in range(4):
        o = -.0975 + .065 * k
        for x, z, s in ((o, .17, (.022, .012, .045)), (o, -.17, (.022, .012, .045)), (.17, o, (.045, .012, .022)), (-.17, o, (.045, .012, .022))):
            M[2].append(slab(s, (x, -.019, z), tiles=dict(all='pin')))
    # 3 energy core: metal caps, a glowing cylinder, four struts, two copper terminals (diameter 0.24, 0.48 high)
    M[3] = [prism(.13, .07, (0, .205, 0), tiles=dict(top='cap', side='capSide', bottom='under')),
            prism(.13, .07, (0, -.205, 0), tiles=dict(top='under', side='capSide', bottom='cap')),
            prism(.09, .34, (0, 0, 0), tiles=dict(side='glow'), skip=('top', 'bottom'))]
    for a in (45, 135, 225, 315):
        x, z = .105 * math.cos(math.radians(a)), .105 * math.sin(math.radians(a))
        M[3].append(slab((.03, .34, .03), (x, 0, z), (0, -a, 0), tiles=dict(all='strut'), skip=('top', 'bottom')))
    for x in (-.10, .10):
        M[3].append(slab((.04, .08, .05), (x, 0, 0), tiles=dict(all='copper')))
    # 4 mech blueprint: a rolled-up tube with half the sheet unrolled (0.55 long)
    M[4] = [prism(.055, .55, (0, -.01, -.13), (0, 0, 90), n=8, tiles=dict(side='roll', top='rollEnd', bottom='rollEnd')),
            slab((.52, .012, .17), (0, -.059, -.02), (0, 0, 0), dict(top='sheet', bottom='sheetBack', side='paperEdge')),
            slab((.52, .012, .09), (0, -.054, .105), (-8, 0, 0), dict(top='sheet', bottom='sheetBack', side='paperEdge'))]
    # 5 decoy beacon: tripod, box with a yellow band, antenna, red lamp (0.5 high)
    M[5] = [slab((.20, .14, .20), (0, .02, 0), tiles=dict(side='box', top='boxTop', bottom='dark')),
            slab((.022, .12, .022), (0, .15, 0), tiles=dict(all='antenna'), skip=('bottom',)),
            slab((.08, .06, .08), (0, .24, 0), tiles=dict(all='lamp'))]
    for a in (90, 210, 330):
        x, z = .10 * math.cos(math.radians(a)), .10 * math.sin(math.radians(a))
        M[5].append(slab((.032, .22, .032), (x, -.135, z), (0, -a, 0), tiles=dict(all='leg'), skip=('top',)))
        M[5][-1] = [(_tilt(f, (x, -.135, z), a, 22), tl, nm) for f, tl, nm in M[5][-1]]
    # 6-8 weapon kits: a weapon module with a short barrel, a mount below and a connector on its side (0.55 long)
    for n in (6, 7, 8):
        M[n] = [slab((.30, .15, .18), (-.10, .0, 0), tiles=dict(side='bodySide', top='bodyTop', bottom='under', right='bodyEnd', left='bodyEnd')),
                prism(.038, .20, (.145, .025, 0), (0, 0, -90), n=8, tiles=dict(side='barrel', top='muzzle', bottom='muzzle')),
                prism(.052, .045, (.2575, .025, 0), (0, 0, -90), n=8, tiles=dict(side='mount', top='muzzle', bottom='muzzle')),
                slab((.09, .07, .10), (-.12, -.11, 0), tiles=dict(all='mount'), skip=('top',)),
                slab((.07, .07, .012), (-.13, .0, .096), tiles=dict(front='connector', side='mount'), skip=('back',))]
    return M


def _tilt(face, centre, yaw, deg):
    """lean a tripod leg outwards: rotate about the horizontal axis perpendicular to its direction, through its top end"""
    top = (centre[0], centre[1] + .11, centre[2])
    # horizontal axis perpendicular to the leg's outward direction o = (cos yaw, 0, sin yaw), chosen so ax x (-Y) = o
    ax = (-math.sin(math.radians(yaw)), 0, math.cos(math.radians(yaw)))
    c, s = math.cos(math.radians(deg)), math.sin(math.radians(deg)); out = []
    for p in face:
        v = (p[0] - top[0], p[1] - top[1], p[2] - top[2]); dot = sum(a * b for a, b in zip(ax, v))
        cr = (ax[1] * v[2] - ax[2] * v[1], ax[2] * v[0] - ax[0] * v[2], ax[0] * v[1] - ax[1] * v[0])
        r = tuple(v[i] * c + cr[i] * s + ax[i] * dot * (1 - c) for i in range(3))
        out.append((r[0] + top[0], r[1] + top[1], r[2] + top[2]))
    return out


def _sub(a, b): return (a[0] - b[0], a[1] - b[1], a[2] - b[2])
def _cross(a, b): return (a[1] * b[2] - a[2] * b[1], a[2] * b[0] - a[0] * b[2], a[0] * b[1] - a[1] * b[0])
def _dot(a, b): return a[0] * b[0] + a[1] * b[1] + a[2] * b[2]
def _norm(a):
    l = math.sqrt(_dot(a, a)) or 1; return (a[0] / l, a[1] / l, a[2] / l)


def face_normal(q): return _norm(_cross(_sub(q[1], q[0]), _sub(q[2], q[0])))


def shade_of(n):
    """the towers' block-style shading (tools/build_tower_assets.py), blended for slanted faces: top 1, bottom 0.55"""
    side = .72 + .14 * n[0] - .08 * n[2]
    return side + (1 - side) * n[1] if n[1] >= 0 else side + (.55 - side) * (-n[1])


def face_uvs(item, q, tile):
    """corners -> atlas UVs: the face's own axes (v towards +Y where it can, else towards -Z), stretched over the tile"""
    n = face_normal(q); up = (0, 1, 0) if abs(n[1]) < .9 else (0, 0, -1)
    vax = _norm(_sub(up, tuple(n[i] * _dot(up, n) for i in range(3)))); uax = _cross(vax, n)
    us = [_dot(p, uax) for p in q]; vs = [_dot(p, vax) for p in q]
    u0, u1, v0, v1 = min(us), max(us), min(vs), max(vs)
    x, y, w, h = TILES[item][tile]; cx, cy = (item % GRID) * CELL, (item // GRID) * CELL; e = .04   # inset: point sampling stays inside
    return [((cx + x + e + (w - 2 * e) * (u - u0) / max(u1 - u0, 1e-9)) / ATLAS, (cy + y + e + (h - 2 * e) * (1 - (v - v0) / max(v1 - v0, 1e-9))) / ATLAS) for u, v in zip(us, vs)]


def triangles(item, parts):
    """-> list of (part, [p0, p1, p2] CCW-outward, [uv0, uv1, uv2], shade, emissive), centred on the bounding box"""
    out = []
    for pi, part in enumerate(parts):
        for q, tile, _ in part:
            uv = face_uvs(item, q, tile); n = face_normal(q); em = (item, tile) in EMISSIVE
            for k in range(1, len(q) - 1):
                out.append((pi, [q[0], q[k], q[k + 1]], [uv[0], uv[k], uv[k + 1]], 1.0 if em else shade_of(n), em))
    lo = [min(p[i] for t in out for p in t[1]) for i in range(3)]; hi = [max(p[i] for t in out for p in t[1]) for i in range(3)]
    c = [(lo[i] + hi[i]) / 2 for i in range(3)]
    return [(pi, [tuple(p[i] - c[i] for i in range(3)) for p in ps], uv, s, em) for pi, ps, uv, s, em in out], [hi[i] - lo[i] for i in range(3)]


def write_bin(path, tris):
    V, I = [], []
    for _, ps, uv, s, em in tris:
        b = len(V)
        for p, t in zip(ps, uv): V.append((p[0], p[1], p[2], t[0], t[1], s + (2 if em else 0)))
        I += [b, b + 2, b + 1]   # Blender / CCW -> engine front (clockwise)
    Path(path).write_bytes(struct.pack('<ii', len(V), len(I)) + b''.join(struct.pack('<6f', *v) for v in V) + struct.pack('<%di' % len(I), *I))


def read_bin(path):
    b = Path(path).read_bytes(); nv, ni = struct.unpack_from('<ii', b); V = [struct.unpack_from('<6f', b, 8 + 24 * i) for i in range(nv)]
    I = list(struct.unpack_from('<%di' % ni, b, 8 + 24 * nv)); return V, I


# =============================================================================================== checks (outer, pure Python)
def _point_tri(p, a, b, c):
    """distance from p to triangle abc"""
    ab, ac, ap = _sub(b, a), _sub(c, a), _sub(p, a); d1, d2 = _dot(ab, ap), _dot(ac, ap)
    if d1 <= 0 and d2 <= 0: return math.dist(p, a)
    bp = _sub(p, b); d3, d4 = _dot(ab, bp), _dot(ac, bp)
    if d3 >= 0 and d4 <= d3: return math.dist(p, b)
    vc = d1 * d4 - d3 * d2
    if vc <= 0 and d1 >= 0 and d3 <= 0: v = d1 / (d1 - d3); return math.dist(p, tuple(a[i] + v * ab[i] for i in range(3)))
    cp = _sub(p, c); d5, d6 = _dot(ab, cp), _dot(ac, cp)
    if d6 >= 0 and d5 <= d6: return math.dist(p, c)
    vb = d5 * d2 - d1 * d6
    if vb <= 0 and d2 >= 0 and d6 <= 0: w = d2 / (d2 - d6); return math.dist(p, tuple(a[i] + w * ac[i] for i in range(3)))
    va = d3 * d6 - d5 * d4
    if va <= 0 and (d4 - d3) >= 0 and (d5 - d6) >= 0:
        w = (d4 - d3) / ((d4 - d3) + (d5 - d6)); return math.dist(p, tuple(b[i] + w * (c[i] - b[i]) for i in range(3)))
    den = 1 / (va + vb + vc); v, w = vb * den, vc * den
    return math.dist(p, tuple(a[i] + ab[i] * v + ac[i] * w for i in range(3)))


def _inside(p, tris, tol=1e-4):
    """p inside the convex part made of these (CCW-outward) triangles"""
    return all(_dot(_sub(p, t[0]), face_normal(t)) <= tol for t in tris)


def check(item, tris, readback=None):
    """budget, winding, readback, floating / near gaps, coplanar overlapping faces of different parts"""
    r = dict(item=NAMES[item], triangles=len(tris))
    r['budgetOk'] = len(tris) <= 300 and 3 * len(tris) < 65536
    if readback is not None:
        V, I = readback; err = 0; flips = 0; uverr = 0; serr = 0
        for k, (_, ps, uv, s, em) in enumerate(tris):
            a, b, c = (V[I[3 * k + j]] for j in range(3))
            for got, want, wuv in ((a, ps[0], uv[0]), (b, ps[2], uv[2]), (c, ps[1], uv[1])):
                err = max(err, math.dist(got[:3], want)); uverr = max(uverr, abs(got[3] - wuv[0]) * ATLAS, abs(got[4] - wuv[1]) * ATLAS)
                serr = max(serr, abs(got[5] - (s + (2 if em else 0))))
            n_engine = _norm(_cross(_sub(c[:3], a[:3]), _sub(b[:3], a[:3])))   # clockwise front: outward = (c-a) x (b-a)
            if _dot(n_engine, face_normal(ps)) < .99: flips += 1
        r['readbackMaxErrorBlocks'] = err; r['readbackMaxUvErrorPx'] = uverr; r['readbackMaxShadeError'] = serr; r['windingWrong'] = flips
    parts = {}
    for pi, ps, *_ in tris: parts.setdefault(pi, []).append(ps)
    gaps = []
    for pi, A in parts.items():
        if len(parts) == 1: break
        best = 1e9; touching = False
        for pj, B in parts.items():
            if pj == pi: continue
            pa = {p for t in A for p in t} | {tuple(sum(t[k][i] for k in range(3)) / 3 for i in range(3)) for t in A}
            pb = {p for t in B for p in t}
            if any(_inside(p, B) for p in pa) or any(_inside(p, A) for p in pb): touching = True; break
            for p in pa:
                for t in B: best = min(best, _point_tri(p, *t))
            for p in pb:
                for t in A: best = min(best, _point_tri(p, *t))
            if best < .0003: touching = True; break
        if not touching: gaps.append((pi, round(best, 5)))
    r['floatingOrGapParts'] = gaps   # (part, nearest distance in blocks); 1 block = 1 m, so 0.0003-0.03 is 0.3 mm-3 cm
    fights = 0
    for pi, A in parts.items():
        for pj, B in parts.items():
            if pj <= pi: continue
            for ta in A:
                na = face_normal(ta); ca = tuple(sum(p[i] for p in ta) / 3 for i in range(3))
                for tb in B:
                    nb = face_normal(tb)
                    if _dot(na, nb) > .999 and abs(_dot(_sub(ca, tb[0]), nb)) < .0005 and _in_tri(ca, tb, nb): fights += 1
    r['coplanarOverlaps'] = fights
    r['ok'] = r['budgetOk'] and not gaps and fights == 0 and (readback is None or (r['windingWrong'] == 0 and r['readbackMaxErrorBlocks'] < 1e-5
                                                                         and r['readbackMaxUvErrorPx'] < .01 and r['readbackMaxShadeError'] < 1e-4))
    return r


def _in_tri(p, t, n):
    for k in range(3):
        if _dot(_cross(_sub(t[(k + 1) % 3], t[k]), _sub(p, t[k])), n) < -1e-7: return False
    return True


def selftest():
    """the checks must catch a flipped triangle, a floating part, a 1 cm gap and a coplanar face"""
    tris, _ = triangles(1, models()[1]); out = {}
    import tempfile
    f = Path(tempfile.gettempdir()) / 'md_part_selftest.bin'; write_bin(f, tris); V, I = read_bin(f)
    I2 = list(I); I2[1], I2[2] = I2[2], I2[1]; out['flippedTriangle'] = check(1, tris, (V, I2))['windingWrong'] == 1
    moved = [(pi, [(p[0], p[1] + (.2 if pi == 1 else 0), p[2]) for p in ps], uv, s, em) for pi, ps, uv, s, em in tris]
    out['floatingRivet'] = any(g[0] == 1 for g in check(1, moved)['floatingOrGapParts'])
    gap = [(pi, [(p[0], p[1] + (.02 if pi == 1 else 0), p[2]) for p in ps], uv, s, em) for pi, ps, uv, s, em in tris]   # sunk 5 mm, raised 20: a 15 mm gap
    out['rivet15mmAbove'] = any(g[0] == 1 and .01 < g[1] < .02 for g in check(1, gap)['floatingOrGapParts'])
    top = [t for t in tris if t[0] == 0 and face_normal(t[1])[1] > .99][0]
    dup = tris + [(9, [(p[0], p[1], p[2]) for p in top[1]], top[2], top[3], top[4])]
    out['coplanarDuplicate'] = check(1, dup)['coplanarOverlaps'] > 0
    V2 = [v[:3] + (v[5], 1 - v[5], v[5]) if i < 3 else v for i, v in enumerate(V)]; out['scrambledUv'] = check(1, tris, (V2, I))['readbackMaxUvErrorPx'] > 1
    out['cleanModelPasses'] = check(1, tris, (V, I))['ok']
    return out


# =============================================================================================== atlas (outer, Pillow)
def paint_atlas(path):
    from PIL import Image, ImageDraw
    im = Image.new('RGBA', (ATLAS, ATLAS), (0, 0, 0, 0)); d = ImageDraw.Draw(im)
    def T(item, name):
        x, y, w, h = TILES[item][name]; return (item % GRID) * CELL + x, (item // GRID) * CELL + y, w, h
    def R(x, y, w, h, c): d.rectangle([x, y, x + w - 1, y + h - 1], fill=c)
    def P(x, y, c): d.point((x, y), fill=c)
    def frame(x, y, w, h, light, dark):
        R(x, y, w, 1, light); R(x, y, 1, h, light); R(x, y + h - 1, w, 1, dark); R(x + w - 1, y, 1, h, dark)
    # 0 scrap
    x, y, w, h = T(0, 'plateTop'); R(x, y, w, h, M1); frame(x, y, w, h, M2, M0)
    for k in range(3, w - 3, 5): d.line([(x + k, y + 3), (x + k + 3, y + h - 4)], fill=M0)
    P(x + 3, y + 3, K); P(x + w - 4, y + h - 4, K); P(x + 4, y + h - 3, M3)
    x, y, w, h = T(0, 'plateEdge'); R(x, y, w, h, M0); R(x, y, w, 1, M2)
    x, y, w, h = T(0, 'plateUnder'); R(x, y, w, h, M0)
    x, y, w, h = T(0, 'stripe'); R(x, y, w, h, Y1)
    for k in range(-h, w, 4): d.line([(x + k, y + h - 1), (x + k + h - 1, y)], fill=K, width=2)
    frame(x, y, w, h, M3, M0)
    x, y, w, h = T(0, 'rust'); R(x, y, w, h, R1); frame(x, y, w, h, M1, M0)
    for px, py in ((3, 3), (7, 5), (5, 8), (9, 9), (2, 9)): P(x + px, y + py, R0)
    R(x + 4, y + 4, 4, 4, R0); R(x + 5, y + 5, 2, 2, M2)
    x, y, w, h = T(0, 'dark'); R(x, y, w, h, M0); P(x + 3, y + 3, K)
    x, y, w, h = T(0, 'bolt'); R(x, y, w, h, M3); R(x + 1, y + 1, 2, 2, M2)
    # 1 alloy plate
    x, y, w, h = T(1, 'top'); R(x, y, w, h, M1)
    for k in (3, 6, 9, 12): R(x + 2, y + k, w - 4, 1, (118, 124, 134))
    R(x + 2, y + 2, w - 4, 1, M2); R(x + 8, y + 6, 8, 4, Y1); R(x + 8, y + 9, 8, 1, Y0); R(x + 9, y + 7, 2, 1, K); R(x + 12, y + 7, 3, 1, K)
    x, y, w, h = T(1, 'bevel'); R(x, y, w, h, M3); R(x, y + h - 1, w, 1, M2)
    x, y, w, h = T(1, 'side'); R(x, y, w, h, M0); R(x, y, w, 1, M2)
    x, y, w, h = T(1, 'bottom'); R(x, y, w, h, M0); R(x + 2, y + 2, w - 4, 1, (70, 74, 82))
    x, y, w, h = T(1, 'rivet'); R(x, y, w, h, M3); P(x + 2, y + 2, M1); P(x + 3, y + 3, M1)
    x, y, w, h = T(1, 'rivetSide'); R(x, y, w, h, M2); R(x, y + h - 1, w, 1, M0)
    # 2 chip
    x, y, w, h = T(2, 'board'); R(x, y, w, h, G1); frame(x, y, w, h, G2, G0)
    for a, b in (((2, 4), (8, 4)), ((8, 4), (8, 8)), ((21, 19), (15, 19)), ((15, 19), (15, 15)), ((2, 18), (6, 18)), ((17, 3), (21, 3)), ((3, 12), (6, 12)), ((18, 12), (21, 12))):
        d.line([(x + a[0], y + a[1]), (x + b[0], y + b[1])], fill=AU)
    for px, py in ((8, 8), (15, 15), (6, 18), (21, 3)): P(x + px, y + py, AU2)
    x, y, w, h = T(2, 'boardSide'); R(x, y, w, h, G0); R(x, y, w, 1, G2)
    x, y, w, h = T(2, 'boardUnder'); R(x, y, w, h, G0)
    x, y, w, h = T(2, 'die'); R(x, y, w, h, M0); frame(x, y, w, h, M1, K); R(x + 2, y + 2, 4, 4, RED); R(x + 2, y + 2, 2, 2, RED2)
    x, y, w, h = T(2, 'dieSide'); R(x, y, w, h, K)
    x, y, w, h = T(2, 'pin'); R(x, y, w, h, AU); P(x + 1, y + 1, AU2)
    # 3 core
    x, y, w, h = T(3, 'cap'); R(x, y, w, h, M1); d.ellipse([x + 2, y + 2, x + 13, y + 13], outline=M2); d.ellipse([x + 5, y + 5, x + 10, y + 10], fill=M3, outline=M0)
    x, y, w, h = T(3, 'capSide'); R(x, y, w, h, M1); R(x, y, w, 1, M3); R(x, y + h - 1, w, 1, M0)
    x, y, w, h = T(3, 'glow')
    for k in range(w):
        f = abs(k - (w - 1) / 2) / ((w - 1) / 2); R(x + k, y, 1, h, E2 if f < .3 else E1 if f < .75 else E0)
    for k in (4, 11, 18): R(x, y + k, w, 1, E0)
    x, y, w, h = T(3, 'strut'); R(x, y, w, h, M0); R(x, y, 1, h, M2)
    x, y, w, h = T(3, 'copper'); R(x, y, w, h, C1); P(x, y, C2); R(x, y + h - 1, w, 1, C0)
    x, y, w, h = T(3, 'under'); R(x, y, w, h, M0)
    # 4 blueprint
    x, y, w, h = T(4, 'sheet'); R(x, y, w, h, B1)
    for k in range(3, w, 4): R(x + k, y, 1, h, B0)
    for k in range(3, h, 4): R(x, y + k, w, 1, B0)
    d.rectangle([x + 13, y + 2, x + 18, y + 6], outline=W); d.rectangle([x + 12, y + 7, x + 19, y + 12], outline=W)
    d.line([(x + 12, y + 8), (x + 9, y + 11)], fill=W); d.line([(x + 19, y + 8), (x + 23, y + 6)], fill=W); d.line([(x + 23, y + 6), (x + 26, y + 6)], fill=W)
    d.line([(x + 14, y + 13), (x + 12, y + 18)], fill=W); d.line([(x + 17, y + 13), (x + 19, y + 18)], fill=W)
    R(x + 25, y + 14, 4, 3, RED); R(x, y, w, 1, B2)
    x, y, w, h = T(4, 'sheetBack'); R(x, y, w, h, B0)
    x, y, w, h = T(4, 'roll'); R(x, y, w, h, B1); R(x, y + 1, w, 1, B2); R(x, y + h - 1, w, 1, B0); R(x + 6, y, 1, h, B0); R(x + 17, y, 1, h, B0)
    x, y, w, h = T(4, 'rollEnd'); R(x, y, w, h, B0); d.ellipse([x + 1, y + 1, x + 6, y + 6], outline=B2); P(x + 3, y + 3, B2); P(x + 4, y + 4, W)
    x, y, w, h = T(4, 'paperEdge'); R(x, y, w, h, B2)
    # 5 beacon
    x, y, w, h = T(5, 'box'); R(x, y, w, h, M1); frame(x, y, w, h, M2, M0); R(x + 1, y + 5, w - 2, 3, Y1); R(x + 1, y + 7, w - 2, 1, Y0)
    for k in range(2, w - 2, 4): R(x + k, y + 5, 2, 2, K)
    x, y, w, h = T(5, 'boxTop'); R(x, y, w, h, M2); frame(x, y, w, h, M3, M0)
    for k in range(3, h - 2, 2): R(x + 3, y + k, w - 6, 1, M0)
    x, y, w, h = T(5, 'leg'); R(x, y, w, h, M0); R(x, y, 1, h, M1)
    x, y, w, h = T(5, 'antenna'); R(x, y, w, h, M2); R(x + 1, y, 1, h, M1)
    x, y, w, h = T(5, 'lamp'); R(x, y, w, h, RED); R(x + 1, y + 1, 3, 3, RED2); P(x + 2, y + 2, (255, 220, 210))
    x, y, w, h = T(5, 'foot'); R(x, y, w, h, K)
    x, y, w, h = T(5, 'dark'); R(x, y, w, h, M0)
    # 6-8 kits
    for n, (col, bars) in ((6, (Y1, 1)), (7, (OR, 2)), (8, (RED, 3))):
        x, y, w, h = T(n, 'bodySide'); R(x, y, w, h, M1); frame(x, y, w, h, M2, M0); R(x + 6, y + 2, 1, h - 4, M0); R(x + 13, y + 2, 1, h - 4, M0)
        x, y, w, h = T(n, 'bodyTop'); R(x, y, w, h, M2); frame(x, y, w, h, M3, M1)
        for b in range(bars): R(x + 3 + b * 5, y + 3, 3, h - 6, col); R(x + 3 + b * 5, y + h - 4, 3, 1, M0)
        x, y, w, h = T(n, 'bodyEnd'); R(x, y, w, h, M1); frame(x, y, w, h, M2, M0)
        for k in range(2, h - 2, 2): R(x + 2, y + k, w - 4, 1, M0)
        x, y, w, h = T(n, 'barrel'); R(x, y, w, h, M2); R(x, y, w, 1, M3); R(x, y + h - 1, w, 1, M0); R(x + 4, y, 1, h, M1); R(x + 10, y, 1, h, M1)
        x, y, w, h = T(n, 'muzzle'); R(x, y, w, h, M0); d.ellipse([x + 1, y + 1, x + 6, y + 6], fill=K, outline=M1)
        x, y, w, h = T(n, 'mount'); R(x, y, w, h, M0); R(x, y, w, 1, M1)
        x, y, w, h = T(n, 'connector'); R(x, y, w, h, K); R(x + 1, y + 1, w - 2, h - 2, E1 if n == 8 else C1); P(x + 2, y + 2, E2 if n == 8 else C2)
        x, y, w, h = T(n, 'under'); R(x, y, w, h, M0)
    im.save(path)


# =============================================================================================== Blender side
def blender_main(args):
    import bpy, bmesh
    out = Path(args['out']); review = Path(args['review']) if args.get('review') else None
    for o in list(bpy.data.objects): bpy.data.objects.remove(o, do_unlink=True)
    img = bpy.data.images.load(str(out / 'parts.png')); img.colorspace_settings.name = 'sRGB'
    mat = bpy.data.materials.new('md_parts'); mat.use_nodes = True; nt = mat.node_tree
    for nd in list(nt.nodes): nt.nodes.remove(nd)
    # unlit like the game: texture x baked shade (a colour attribute); emission so render light does not change it
    o_ = nt.nodes.new('ShaderNodeOutputMaterial'); em = nt.nodes.new('ShaderNodeEmission'); tx = nt.nodes.new('ShaderNodeTexImage'); tx.image = img; tx.interpolation = 'Closest'
    at = nt.nodes.new('ShaderNodeVertexColor'); at.layer_name = 'shade'; mul = nt.nodes.new('ShaderNodeMix'); mul.data_type = 'RGBA'; mul.blend_type = 'MULTIPLY'
    mul.inputs[0].default_value = 1.0
    nt.links.new(tx.outputs['Color'], mul.inputs[6]); nt.links.new(at.outputs['Color'], mul.inputs[7]); nt.links.new(mul.outputs[2], em.inputs['Color'])
    mix = nt.nodes.new('ShaderNodeMixShader'); tr = nt.nodes.new('ShaderNodeBsdfTransparent')
    nt.links.new(tx.outputs['Alpha'], mix.inputs[0]); nt.links.new(tr.outputs[0], mix.inputs[1]); nt.links.new(em.outputs[0], mix.inputs[2]); nt.links.new(mix.outputs[0], o_.inputs['Surface'])
    if hasattr(mat, 'blend_method'): mat.blend_method = 'CLIP'
    S2B = lambda p: (p[0], -p[2], p[1])        # SC (Y up, Z back) -> Blender (Z up, -Y back)
    B2S = lambda p: (p[0], p[2], -p[1])
    report = {}; objs = []
    M = models()
    for item, parts in M.items():
        tris, size = triangles(item, parts)
        me = bpy.data.meshes.new(NAMES[item]); verts = []; faces = []; uvs = []; shades = []; part_ids = []; ems = []
        for pi, ps, uv, s, e in tris:
            b = len(verts); verts += [S2B(p) for p in ps]; faces.append((b, b + 1, b + 2)); uvs += uv; shades += [s] * 3; part_ids.append(pi); ems.append(e)
        me.from_pydata(verts, [], faces); me.update()
        # create every layer first, then fetch the handles: adding an attribute reallocates the mesh's layers and an older
        # Python handle (the UV layer's) then writes into the wrong memory (first build: the UVs held the shade values)
        me.uv_layers.new(name='UVMap'); me.color_attributes.new('shade', 'FLOAT_COLOR', 'CORNER')
        me.attributes.new('part', 'INT', 'FACE'); me.attributes.new('emissive', 'BOOLEAN', 'FACE')
        ul = me.uv_layers['UVMap']; ca = me.color_attributes['shade']; pa = me.attributes['part']; ea = me.attributes['emissive']
        for li, l in enumerate(me.loops):
            u, v = uvs[l.vertex_index]; ul.data[li].uv = (u, 1 - v); s = shades[l.vertex_index]; ca.data[li].color = (s, s, s, 1)
        for fi in range(len(faces)): pa.data[fi].value = part_ids[fi]; ea.data[fi].value = ems[fi]
        me.materials.append(mat)
        ob = bpy.data.objects.new('item%d_%s' % (item, NAMES[item]), me); bpy.context.scene.collection.objects.link(ob); ob.location = ((item % 3) * 1.0, -(item // 3) * 1.0, 0); objs.append(ob)
        # export from the Blender mesh (what the .blend holds), back to the SC frame, engine winding
        me.calc_loop_triangles(); soup = []
        for lt in me.loop_triangles:
            ps = [B2S(tuple(me.vertices[me.loops[li].vertex_index].co)) for li in lt.loops]
            uv = [(ul.data[li].uv[0], 1 - ul.data[li].uv[1]) for li in lt.loops]
            soup.append((pa.data[lt.polygon_index].value, ps, uv, ca.data[lt.loops[0]].color[0], bool(ea.data[lt.polygon_index].value)))
        write_bin(out / ('part%d.bin' % item), soup)
        report[item] = dict(name=NAMES[item], triangles=len(soup), size=[round(s, 4) for s in size], parts=len(parts))
    if review:
        review.mkdir(parents=True, exist_ok=True)
        _review(bpy, objs, review, report)
        bpy.ops.wm.save_as_mainfile(filepath=str(review.parent / 'part_models.blend'))
    (out / '_report.json').write_text(json.dumps(report, ensure_ascii=False))


def _review(bpy, objs, review, report):
    """renders: each model from the inventory angle and from the front; vanilla iron ingot / battery next to the plate,
    chip, core and beacon at the same scale (drawn sizes: ingot mesh x2 - 0.1 y, battery x1, as IngotBlock / BatteryBlock)"""
    import mathutils
    sc = bpy.context.scene
    try: sc.render.engine = 'BLENDER_EEVEE_NEXT'
    except TypeError: sc.render.engine = 'BLENDER_EEVEE'
    sc.render.resolution_x = sc.render.resolution_y = 320; sc.render.film_transparent = False
    w = sc.world or bpy.data.worlds.new('w'); sc.world = w; w.use_nodes = True
    bg = next(n for n in w.node_tree.nodes if n.type == 'BACKGROUND'); bg.inputs[0].default_value = (0.36, 0.40, 0.44, 1); bg.inputs[1].default_value = 1
    sc.view_settings.view_transform = 'Standard'
    cam_data = bpy.data.cameras.new('cam'); cam_data.type = 'ORTHO'; cam = bpy.data.objects.new('cam', cam_data); sc.collection.objects.link(cam); sc.camera = cam
    def shoot(target, direction, scale, path):
        for o in objs + refs: o.hide_render = o is not target and o not in extra
        d = mathutils.Vector(direction).normalized(); cam.location = target.location + d * 4
        cam.rotation_euler = (-d).to_track_quat('-Z', 'Y').to_euler(); cam_data.ortho_scale = scale; sc.render.filepath = str(path)
        render()
    def render():
        try: bpy.ops.render.render(write_still=True)
        except Exception as e:   # no GPU context for EEVEE in background: Cycles renders the same unlit emission
            report['_engine'] = 'CYCLES (%s)' % e; sc.render.engine = 'CYCLES'; sc.cycles.samples = 8; sc.cycles.device = 'CPU'; bpy.ops.render.render(write_still=True)
    refs = []; extra = []
    grey = bpy.data.materials.new('vanilla_grey'); grey.use_nodes = True
    gb = next(n for n in grey.node_tree.nodes if n.type == 'BSDF_PRINCIPLED'); gb.inputs['Base Color'].default_value = (.85, .55, .45, 1)
    # vanilla references (local Content.zip models, never packaged), drawn in a flat terracotta colour
    ref = Path(report.get('_ref', 'E:/projects/md-item-models/V1/ref'))
    for f, mesh, mul, dy in (('Ingots.dae', 'IronIngot', 2, -.1), ('Battery.dae', 'Battery', 1, -.5)):
        before = set(bpy.data.objects)
        try: bpy.ops.wm.collada_import(filepath=str(ref / f))
        except Exception as e: report['_refError'] = str(e); continue
        new = [o for o in bpy.data.objects if o not in before]; keep = None
        for o in new:
            if o.type == 'MESH' and mesh.lower() in o.name.lower(): keep = o
        for o in new:
            if o is not keep and o.type == 'MESH': o.hide_render = o.hide_viewport = True
        if keep is None: report['_refError%s' % mesh] = [o.name for o in new]; continue
        mw = keep.matrix_world.copy(); keep.parent = None; keep.matrix_world = mw; keep.modifiers.clear()   # no armature deformation
        keep.data.transform(keep.matrix_world); keep.matrix_world = mathutils.Matrix.Identity(4)
        for o in new:
            if o is not keep: bpy.data.objects.remove(o, do_unlink=True)
        # the importer's unit handling differs from the engine's: rescale to the engine's drawn size (node 0.01 x draw scale)
        def ext(): vs = [v.co for v in keep.data.vertices]; return [(min(v[i] for v in vs), max(v[i] for v in vs)) for i in range(3)]
        e = ext(); dims = [hi - lo for lo, hi in e]
        f = VANILLA[mesh][2] / dims[2]; keep.data.transform(mathutils.Matrix.Scale(f, 4)); e = ext()
        keep.data.transform(mathutils.Matrix.Translation(tuple(-(lo + hi) / 2 for lo, hi in e)))
        if mesh == 'IronIngot': keep.data.transform(mathutils.Matrix.Rotation(math.radians(90), 4, 'Z'))   # length along X, like the plate
        keep.data.materials.clear(); keep.data.materials.append(grey)
        report['vanilla_' + mesh] = dict(importedRaw=[round(d, 3) for d in dims], drawn=[round(hi - lo, 4) for lo, hi in ext()], engine=VANILLA[mesh])
        keep.name = 'vanilla_' + mesh; refs.append(keep)
    for ob in objs:
        n = int(ob.name.split('_')[0][4:])
        shoot(ob, (1, -1, 1), 0.85, review / ('%d_%s_iso.png' % (n, NAMES[n])))
        shoot(ob, (0, -1, .35), 0.85, review / ('%d_%s_front.png' % (n, NAMES[n])))
    # overlays: our model and the vanilla one side by side, same scale (1 block = the frame's 1.6 width / 1.6)
    pairs = [(1, 'vanilla_IronIngot'), (2, 'vanilla_IronIngot'), (3, 'vanilla_Battery'), (5, 'vanilla_Battery'), (0, 'vanilla_IronIngot'), (6, 'vanilla_IronIngot')]
    for n, rname in pairs:
        r = next((o for o in refs if o.name == rname), None); ob = next(o for o in objs if o.name.startswith('item%d_' % n))
        if r is None: continue
        r.location = ob.location + mathutils.Vector((-.6, 0, 0)); extra[:] = [r]
        tmp = ob.location.copy(); ob.location = tmp + mathutils.Vector((.85, 0, 0)); sc0 = ob.scale.copy(); ob.scale = (DRAW_SCALE,) * 3
        r.location = tmp + mathutils.Vector((-1.0, 0, 0))
        mid = bpy.data.objects.new('mid', None); sc.collection.objects.link(mid); mid.location = tmp + mathutils.Vector((-.1, 0, 0))
        for o in objs + refs: o.hide_render = o not in (ob, r)
        d = mathutils.Vector((0, -1, .25)).normalized(); cam.location = mid.location + d * 4
        cam.rotation_euler = (-d).to_track_quat('-Z', 'Y').to_euler(); cam_data.ortho_scale = 4.0; sc.render.filepath = str(review / ('overlay_%d_%s_vs_%s.png' % (n, NAMES[n], rname)))
        render()
        dims = [round(DRAW_SCALE * s, 3) for s in report[n]['size']]; report.setdefault('overlay', {})['%s vs %s' % (NAMES[n], rname)] = dict(oursDrawn=dims, vanilla=VANILLA[rname[8:]])
        ob.location = tmp; ob.scale = sc0; bpy.data.objects.remove(mid)
    extra[:] = []
    for o in objs + refs: o.hide_render = False


# =============================================================================================== outer driver
def main():
    root = Path(__file__).resolve().parents[1]; out = root / 'Mod/Assets/Parts'; out.mkdir(parents=True, exist_ok=True)
    a = sys.argv[1:]; review = a[a.index('--review') + 1] if '--review' in a else None
    blender = a[a.index('--blender') + 1] if '--blender' in a else r'E:\projects\tools\Blender\blender-4.2.9-windows-x64\blender.exe'
    result = {}
    if '--selftest' in a: result['selftest'] = selftest(); print('selftest', result['selftest'])
    paint_atlas(out / 'parts.png')
    job = dict(out=str(out), review=review)
    p = subprocess.run([blender, '-b', '--factory-startup', '--python', str(Path(__file__).resolve()), '--', json.dumps(job)],
                       capture_output=True, text=True, encoding='utf-8', errors='replace', creationflags=getattr(subprocess, 'CREATE_NO_WINDOW', 0))
    if p.returncode or not (out / '_report.json').exists():
        print(p.stdout[-4000:], p.stderr[-4000:]); sys.exit('blender step failed')
    rep = json.loads((out / '_report.json').read_text()); checks = {}
    M = models()
    for item in range(len(NAMES)):
        # the .bin exported from Blender against what the script meant (pure Python): positions, UVs, shade, winding
        expected, _ = triangles(item, M[item])
        checks[NAMES[item]] = check(item, expected, read_bin(out / ('part%d.bin' % item)))
    (out / '_report.json').unlink()
    result.update(models=rep, checks=checks, ok=all(c['ok'] for c in checks.values()))
    if review:
        Path(review, 'checks.json').write_text(json.dumps(result, ensure_ascii=False, indent=1))
        from PIL import Image, ImageDraw
        shots = sorted(Path(review).glob('[0-9]_*.png')) + sorted(Path(review).glob('overlay_*.png'))
        if shots:
            cols = 6; s = 240; sheet = Image.new('RGB', (cols * s, ((len(shots) + cols - 1) // cols) * s), (30, 30, 30)); dr = ImageDraw.Draw(sheet)
            for i, f in enumerate(shots):
                im = Image.open(f).convert('RGB').resize((s, s), Image.NEAREST); sheet.paste(im, ((i % cols) * s, (i // cols) * s)); dr.text(((i % cols) * s + 4, (i // cols) * s + 4), f.stem[:34], fill=(255, 255, 0))
            sheet.save(Path(review, 'sheet.jpg'), quality=85)
    print(json.dumps(result, ensure_ascii=False, indent=1))
    if not result['ok']: sys.exit('checks failed')


if __name__ == '__main__':
    try:
        import bpy  # noqa: F401  (inside Blender)
        blender_main(json.loads(sys.argv[sys.argv.index('--') + 1]))
    except ImportError:
        main()
