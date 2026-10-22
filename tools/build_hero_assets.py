"""Build the NEMESIS and REGULUS mech assets from the V9 assets of E:/projects/dguns-mech-model/v9_heroes (Destroy Gunners
player bodies 1 and 2, the original models x4 like Mercury). Mercury itself stays with tools/build_mech_assets.py (untouched).

python tools/build_hero_assets.py [nemesis regulus] [--v9 <v9_heroes dir>] [--blender <blender.exe>] [--no-poly]
python tools/build_hero_assets.py --poly-only [nemesis regulus]

Our own polygon models (0.9.0 round 14, E:/projects/dguns-mech-model/v10_poly/<hero>/): for the heroes in POLY_HEROES the build
then puts that folder's model.glb and game/icon.bin, game/icon.png over the V9 export (same node tree, checked; mech.json and
clips.bin stay from the V9 export, so clips, muzzles and the cockpit keep working). --poly-only does just that swap.

Output per hero: Mod/Assets/Imported/Nemesis/ and Mod/Assets/Imported/Regulus/ (ignored: extracted game data, rebuilt by
this tool, packaged by package.py), in Mercury's format (tools/build_mech_assets.py, read by MDMechLibrary.Load):
  model.glb   rigid parts, one node per joint at its rest pivot, the part's mesh in node space, 16-bit indices; the clean
              texture (Real-ESRGAN x4plus 2048 of the original 512 + unsharp mask, as Mercury) embedded as lossless PNG
  mech.json   Mercury's fields (joints, clips, step, muzzles, spanningTriangles, height, source, check) plus
              unit, roles (joint prefixes per job, for the C# that hard-codes Mercury's), box (idle torso + legs, measured, m),
              templateBox (Mercury's MDMech template 2.4 x 4.8 x 2.4: the same for NEMESIS, the same body; scaled by the idle
              height 5.21 / 4.40 for REGULUS), feet,
              budget, stats (the user's numbers, 2026-10-21) and original (the original table values with evidence)
  clips.bin   float32 per clip, per sample (every 2 ticks = 1/30 s), per joint: qx qy qz qw tx ty tz s (game frame)
  icon.bin / icon.png   inventory icon (idle pose) in MDTowerLibrary.IconMesh format and its 512 texture
Sounds: the original plays the same sound effects for every body (by weapon category), so the heroes use Mercury's
Imported/Mercury/Audio (no copy).
Game frame: Y up, the mech faces -Z, its right is +X. The .blend files are Z up, face -Y, left +X: (x, y, z)_game = (-x, z, y).
Roles come from the original's per-joint attribute words (PSCf +80, read by shdPartsAtbitToNo; v9_heroes/motion/roles_<hero>.json):
0x400 rifle, 0x800 missile, 0x1000 cannon, 0x2000 laser, 0x4000 grenade, 0x100000 / 0x200000 left / right jet. The original
fires from the posed pivot of that joint (shdPartsCalPos(joint, zeropos)); the muzzles here are the visible tips, and the
original point is kept as `pivot`.
Self-check: re-poses the model from its own output with the engine's bone composition and compares with Blender (p99 < 1 mm).
"""
import json, os, struct, subprocess, sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
V9 = Path(r'E:\projects\dguns-mech-model\v9_heroes')
BLENDER = Path(r'E:\projects\tools\Blender\blender-4.2.9-windows-x64\blender.exe')
STEP = 2
POLY = Path(r'E:\projects\dguns-mech-model\v10_poly')
POLY_HEROES = ('nemesis',)   # shipped polygon models (the user, 2026-10-22: put them in the package with the others)
# Joint prefixes per job. Mercury for comparison: turret 02_, arms 05_/10_, rifle 14_, cannon 15_-17_, missile 18_ (box on 02_),
# grenade 15_, jets 33_/35_, eye 04_, fist 09_, first person 13_-17_.
HEROES = {
    'nemesis': dict(folder='Nemesis', name='NEMESIS', body=1, kind='KN_RD04 (2)', model=208, templateBox=[2.4, 4.8, 2.4],
                    roles=dict(turret='02_', armL='05_', armR='11_', rifle='15_', laser='15_', missile='11_', cannon=['16_', '17_', '18_'], cannonMuzzle='18_',
                               grenade='16_', jetL='35_', jetR='37_', eye='04_', melee='10_', head=['03_', '04_'], back='32_',
                               legL=['19_', '20_', '21_', '22_', '23_', '24_'], legR=['25_', '26_', '27_', '28_', '29_', '30_'], footL='24_', footR='30_',
                               firstPerson=['14_', '15_', '16_', '17_', '18_']),
                    stats=dict(armour=6000, damageScale=1.25, energy=130000, rifleBurst=[256, 256, 832], rifleBurstSeconds=[0.133, 0.133, 0.433],
                               resistPercent=dict(impact=2, explosion=5, electric=0), missileLocks=3,
                               note='user 2026-10-21: armour 6000, damage x1.25, energy 130000, 3-round burst rifle (original), better against explosions'),
                    original=dict(hp=10000, attack=1500, energy=130000, resist=[2, 5, 0], slots=16, locks=3, flags=['pls_renrifle_f (3-round burst rifle)'],
                                  defaultParts=['Rifle NO-100 (2)', 'Missile NO-100 (12)', 'Cannon NO-200 (22)', 'Fighter Knuckle (1, always)'])),
    'regulus': dict(folder='Regulus', name='REGULUS', body=2, kind='KN_RD12 (16)', model=216, templateBox=[2.8, 5.6, 2.8],
                    roles=dict(turret='02_', armL='05_', armR='10_', rifle='15_', laser='15_', missile='22_', cannon=['16_', '17_', '18_', '19_', '20_'], cannonMuzzle='20_',
                               grenade='06_', jetL='24_', jetR='26_', eye='04_', melee='09_', head=['03_', '04_'], back=None,
                               legL=['27_', '28_', '29_', '30_', '31_'], legR=['32_', '33_', '34_', '35_', '36_'], footL='31_', footR='36_',
                               firstPerson=['13_', '14_', '16_', '17_', '18_', '19_', '20_']),
                    stats=dict(armour=8000, damageScale=1.5, energy=160000, rifleBurst=None, resistPercent=dict(impact=3, explosion=3, electric=3), missileLocks=2,
                               note='user 2026-10-21: armour 8000, damage x1.5, energy 160000, the original default weapons, the best mobility (our choice: the original moves every body alike)'),
                    original=dict(hp=9000, attack=1300, energy=160000, resist=[3, 3, 3], slots=20, locks=2, flags=[],
                                  defaultParts=['Rifle NO-100 (2)', 'Missile NO-100 (12)', 'Cannon NO-200 (22)', 'Fighter Knuckle (1, always)'])),
}


def blender_export(work, hero):
    """Runs inside Blender: dump geometry, clips, muzzles and check poses to <work>/export.json + clips.bin."""
    import bpy
    import numpy as np
    from mathutils import Matrix, Vector
    cfg = HEROES[hero]; RO = cfg['roles']
    arm = bpy.data.objects[hero + '_rig']; mesh = bpy.data.objects[hero + '_body']; sc = bpy.context.scene
    for lc in bpy.context.view_layer.layer_collection.children: lc.hide_viewport = False
    eye4 = Matrix.Identity(4)
    assert all(abs(arm.matrix_world[i][j] - eye4[i][j]) < 1e-6 and abs(mesh.matrix_world[i][j] - eye4[i][j]) < 1e-6 for i in range(4) for j in range(4))
    C = Matrix(((-1, 0, 0, 0), (0, 0, 1, 0), (0, 1, 0, 0), (0, 0, 0, 1))); Ci = C.inverted(); C3 = C.to_3x3()
    bones = arm.data.bones
    order = lambda n: -1 if '(No Group)' in n else int(n.split('_')[0])
    names = sorted((b.name for b in bones), key=order); idx = {n: i for i, n in enumerate(names)}
    parent = [idx[bones[n].parent.name] if bones[n].parent else -1 for n in names]
    assert all(p < i for i, p in enumerate(parent)), 'parents first'
    pivot = [C @ bones[n].head_local for n in names]
    J = {n[:3]: idx[n] for n in names if '(No Group)' not in n}
    for k, v in RO.items():                                         # every role names an existing joint
        for p in (v if isinstance(v, list) else [v] if v else []): assert p in J, (k, p)
    # ------------------------------------------------------------- rest geometry per part (bone-local = rest - pivot)
    me = mesh.data; me.calc_loop_triangles(); uvl = me.uv_layers.active.data; cn = me.corner_normals
    gname = {g.index: g.name for g in mesh.vertex_groups}
    vj = [idx[gname[max(v.groups, key=lambda g: g.weight).group]] for v in me.vertices]
    parts = {}; spanning = 0
    for t in me.loop_triangles:
        js = [vj[v] for v in t.vertices]; j = js[0]; spanning += len(set(js)) > 1
        P = parts.setdefault(j, dict(key={}, pos=[], nrm=[], uv=[], src=[], idx=[]))
        tri = []
        for li, vi in zip(t.loops, t.vertices):
            p = C @ me.vertices[vi].co; n = (C3 @ cn[li].vector).normalized(); u = uvl[li].uv
            k = (vi, round(n.x, 4), round(n.y, 4), round(n.z, 4), round(u.x, 5), round(u.y, 5))
            if k not in P['key']:
                P['key'][k] = len(P['pos']); P['pos'].append(list(p - pivot[j])); P['nrm'].append(list(n)); P['uv'].append([u.x, 1 - u.y]); P['src'].append(vi)
            tri.append(P['key'][k])
        P['idx'].append(tri)
    # ------------------------------------------------------------- clips: local deformation per joint per sample
    def deform(fr):
        sc.frame_set(fr); D = {}
        for n in names:
            pb = arm.pose.bones[n]; D[n] = C @ (pb.matrix @ bones[n].matrix_local.inverted()) @ Ci
        return D
    chk = {}
    cp = os.path.join(os.path.dirname(bpy.data.filepath), 'review', 'check_%s.json' % hero)
    if os.path.exists(cp): chk = json.load(open(cp, encoding='utf-8'))['actions']
    acts = sorted((a for a in bpy.data.actions if 'slot' in a and not a.name.endswith('.001')), key=lambda a: a['slot'] if a['slot'] >= 0 else 999)
    clips = []; blob = []; offset = 0
    for a in acts:
        arm.animation_data.action = a; f0, f1 = int(a.frame_range[0]), int(a.frame_range[1])
        frames = list(range(f0, f1 + 1, STEP))
        if frames[-1] != f1: frames.append(f1)
        for fr in frames:
            D = deform(fr)
            for i, n in enumerate(names):
                L = (D[names[parent[i]]].inverted() @ D[n]) if parent[i] >= 0 else D[n]
                t, q, s = L.decompose(); su = (s.x + s.y + s.z) / 3
                if su < 1e-4: q = q.__class__()
                blob.append([q.x, q.y, q.z, q.w, t.x, t.y, t.z, su])
        fs = chk.get(a.name, {}).get('planted_foot_speed')
        clips.append(dict(name=a.name, slot=int(a['slot']), loop=bool(a.get('loops')), hold=bool(a.get('hold')), generated=bool(a.get('generated')),
                          samples=len(frames), ticks=f1 - f0, offset=offset, footSpeed=fs if fs and fs > 0.3 and a['slot'] in (10, 11, 12, 13, 15, 16, 30, 45, 46, 50) else None,
                          lastStep=frames[-1] - frames[-2] if len(frames) > 1 else STEP))
        offset += len(frames)
    np.array(blob, dtype='<f4').tofile(os.path.join(work, 'clips.bin'))
    # ------------------------------------------------------------- muzzles (rest model space of their joint, game frame)
    def evaluated(fr):
        sc.frame_set(fr); dg = bpy.context.evaluated_depsgraph_get(); m = mesh.evaluated_get(dg).to_mesh()
        V = np.array([tuple(v.co) for v in m.vertices]); mesh.evaluated_get(dg).to_mesh_clear(); return V
    def rest_point(Db, p): return [float(x) for x in C @ (Db.inverted() @ Vector(p))]
    def rest_dir(Db, d): return [float(x) for x in (C3 @ (Db.to_3x3().inverted() @ Vector(d))).normalized()]
    def axis(P, toward):
        c = P.mean(0); w, v = np.linalg.eigh(np.cov((P - c).T)); a_ = v[:, -1]
        return (a_ if a_ @ toward > 0 else -a_), c
    def deg(a_, b_): return float(np.degrees(np.arccos(np.clip(np.dot(a_, b_) / np.linalg.norm(a_) / np.linalg.norm(b_), -1, 1))))
    vjn = np.array(vj); fwd = np.array([0, -1, 0.]); down_back = np.array([0, .45, -1.]) / np.linalg.norm([0, .45, -1.])
    arm.animation_data.action = bpy.data.actions['idle']; V = evaluated(0); sc.frame_set(0)
    Db = {n: arm.pose.bones[n].matrix @ bones[n].matrix_local.inverted() for n in names}
    head = {n: np.array(arm.pose.bones[n].head) for n in names}           # posed pivots (idle frame 0, Blender frame)
    def jn(p): return names[J[p]]
    def orig(p): return [round(float(x), 5) for x in C @ bones[jn(p)].head_local]   # the original's fire point: the joint's pivot (rest)
    muzzles = {}
    # rifle (+ laser): the barrel tip; REGULUS has a dedicated muzzle locator joint (no mesh) at the tip
    rj = RO['rifle']; P = V[vjn == J[rj]]
    if len(P) > 8:
        a_, _ = axis(P, fwd); tip = P[np.argmax((P - P.mean(0)) @ a_)]; how = 'tip of the rifle part along its long axis (idle), as Mercury'
    else:
        pj = names[parent[J[rj]]]; tip = head[jn(rj)]; Q = V[vjn == idx[pj]]; Q = Q[np.linalg.norm(Q - tip, axis=1) < 1.2]
        a_, _ = axis(Q, fwd); how = 'pivot of the muzzle locator joint %s (the original fire point), direction = long axis of %s within 1.2 m of it' % (jn(rj), pj)
    muzzles['rifle'] = dict(joint=jn(rj), at=rest_point(Db[jn(rj)], tip), dir=rest_dir(Db[jn(rj)], a_), pivot=orig(rj), how=how, deg_from_forward_idle=round(deg(a_, fwd), 1))
    # missiles
    mj = RO['missile']; P = V[vjn == J[mj]]
    if len(P) > 8:   # NEMESIS: joint 11 (right shoulder) carries the missile bit; the 4-tube box itself is turret (02) geometry, as on
        # Mercury (its box is skinned to 02 next to the missile-pod joint): the 02 mesh island above and outside joint 11's pivot,
        # nearest to it; muzzle = centre of that island's front face (idle), forward
        tris02 = [tuple(t.vertices) for t in me.loop_triangles if vj[t.vertices[0]] == J['02_']]
        wk = {v: tuple(np.round(np.array(me.vertices[v].co) * 1000).astype(int)) for t in tris02 for v in t}   # welded by position (glTF splits flat faces)
        par_ = {}
        def find(x):
            while par_.setdefault(x, x) != x: par_[x] = par_[par_[x]]; x = par_[x]
            return x
        for a_, b_, c3 in tris02: par_[find(wk[a_])] = find(wk[b_]); par_[find(wk[b_])] = find(wk[c3])
        isl = {}
        for v_ in wk: isl.setdefault(find(wk[v_]), []).append(v_)
        pv = head[jn(mj)]; side = np.sign(pv[0]); best = None
        for vs in isl.values():
            Q = V[vs]; c3 = Q.mean(0)
            if len({wk[v] for v in vs}) >= 8 and c3[2] > pv[2] + 0.1 and (c3[0] - pv[0]) * side > -0.05:
                dd = np.linalg.norm(c3 - pv)
                if best is None or dd < best[0]: best = (dd, vs)
        if best:
            Q = V[best[1]]; front = Q[Q[:, 1] < Q[:, 1].min() + .04]; c_ = front.mean(0); d_ = fwd; sp = float(np.ptp(front[:, 0])); mj = '02_'
            how = 'front face of the missile box: the turret (02) mesh island above and outside the missile joint %s (%d vertices, %.2f m from its pivot), idle; forward' % (jn(RO['missile']), len(best[1]), best[0])
        else:
            front = P[P[:, 1] < P[:, 1].min() + .04]; c_ = front.mean(0); d_ = fwd; sp = float(np.ptp(front[:, 0])); how = 'centre of the front face of the missile joint part (idle), forward (no box island found)'
    else:            # REGULUS: missile launch locator on top of the right shoulder launcher; direction from the launcher pivot to it
        c_ = head[jn(mj)]; pj = names[parent[J[mj]]]; d_ = c_ - head[pj]; d_ = d_ / np.linalg.norm(d_); sp = 0.0
        how = 'pivot of the launch locator %s (the original fire point); direction from the launcher pivot %s to it (idle)' % (jn(mj), pj)
    muzzles['missile'] = dict(joint=jn(mj), at=rest_point(Db[jn(mj)], c_), dir=rest_dir(Db[jn(mj)], d_), spread=sp, pivot=orig(RO['missile']), how=how, deg_from_forward_idle=round(deg(d_, fwd), 1))
    # fist / pile driver, eye, grenade
    P = V[vjn == J[RO['melee']]]; muzzles['melee'] = dict(joint=jn(RO['melee']), at=rest_point(Db[jn(RO['melee'])], P.mean(0)), dir=rest_dir(Db[jn(RO['melee'])], fwd), how='centre of the part')
    P = V[vjn == J[RO['eye']]]; e = P[np.argmin(P[:, 1])] + np.array([0, 0, .12])
    muzzles['eye'] = dict(joint=jn(RO['eye']), at=rest_point(Db[jn(RO['eye'])], e), dir=rest_dir(Db[jn(RO['eye'])], fwd), how='front of the head sensor + 0.12 m up, as Mercury')
    g_ = RO['grenade']; muzzles['grenade'] = dict(joint=jn(g_), at=orig(g_), dir=rest_dir(Db[jn(g_)], fwd), pivot=orig(g_), how='the joint pivot (the original release point)')
    # thrusters: underside of the jet parts (NEMESIS) or the jet locators (REGULUS), blowing down and back as Mercury
    for key, rk in (('thrusterL', 'jetL'), ('thrusterR', 'jetR')):
        p_ = RO[rk]; P = V[vjn == J[p_]]
        if len(P) > 8: low = P[P[:, 2] < P[:, 2].min() + .06].mean(0); how = 'underside of the jet part, as Mercury'
        else: low = head[jn(p_)]; how = 'pivot of the jet locator (the original KN_JET0 attach point)'
        muzzles[key] = dict(joint=jn(p_), at=rest_point(Db[jn(p_)], low), dir=rest_dir(Db[jn(p_)], down_back), pivot=orig(p_), how=how)
    # cannon: deployed (last frame of cannon_deploy_fire), tip of the muzzle part along its long axis
    cf = bpy.data.actions['cannon_deploy_fire']; arm.animation_data.action = cf; fl = int(cf.frame_range[1]); V2 = evaluated(fl); sc.frame_set(fl)
    cm = RO['cannonMuzzle']; Dc = arm.pose.bones[jn(cm)].matrix @ bones[jn(cm)].matrix_local.inverted()
    P = V2[vjn == J[cm]]; a_, _ = axis(P, fwd); tip = P[np.argmax((P - P.mean(0)) @ a_)]
    muzzles['cannon'] = dict(joint=jn(cm), at=rest_point(Dc, tip), dir=rest_dir(Dc, a_), pivot=orig(cm), how='tip of the cannon muzzle part along its long axis, deployed (end of clip 54)',
                             deg_from_forward_deployed=round(deg(a_, fwd), 1))
    # ------------------------------------------------------------- idle numbers: height, feet, body box, cockpit
    arm.animation_data.action = bpy.data.actions['idle']; Vi = evaluated(0)
    core = [p for p in ['01_', '02_', '03_', '04_'] + RO['legL'] + RO['legR'] + ([RO['back']] if RO['back'] else [])]
    B = Vi[np.isin(vjn, [J[p] for p in core])]; wid = float(max(np.ptp(B[:, 0]), np.ptp(B[:, 1])))
    T02 = Vi[vjn == J['02_']]
    cockpit = dict(turretTop=round(float(T02[:, 2].max()), 3), turretCentre=[round(float(x), 3) for x in C @ Vector(tuple((T02.min(0) + T02.max(0)) / 2))],
                   firstPersonEye=[0.0, round(float(T02[:, 2].max()) - 0.73, 2), -0.5],
                   note='Mercury: turret top 4.33 m, first-person eye 3.6 m up and 0.5 m forward (MDMechPilot); the same offset from this turret top')
    idle = dict(height=round(float(Vi[:, 2].max()), 3), feet=round(float(Vi[:, 2].min()), 4), width_x=round(float(np.ptp(Vi[:, 0])), 3), depth=round(float(np.ptp(Vi[:, 1])), 3),
                body_width=round(wid, 3), box=[round(wid + .05, 1), round(float(Vi[:, 2].max()), 1), round(wid + .05, 1)], cockpit=cockpit)
    # ------------------------------------------------------------- check poses: Blender's own evaluation (game frame)
    checks = []
    for an, fr in (('idle', 0), ('idle', 192), ('walk_forward', 20), ('walk_left', 30), ('cannon_deploy_fire', fl), ('death', 60), ('melee', 18)):
        arm.animation_data.action = bpy.data.actions[an]; Vb = evaluated(fr)
        clip = next(c for c in clips if c['name'] == an); last = int(bpy.data.actions[an].frame_range[1])
        assert fr % STEP == 0 or fr == last
        s = clip['samples'] - 1 if fr == last else fr // STEP
        checks.append(dict(clip=an, frame=fr, sample=s, verts=[list(C @ Vector(v)) for v in Vb]))
    json.dump(dict(names=names, parent=parent, pivot=[list(p) for p in pivot], spanning=spanning,
                   parts={str(j): dict(pos=P['pos'], nrm=P['nrm'], uv=P['uv'], idx=P['idx'], src=P['src']) for j, P in parts.items()},
                   clips=clips, muzzles=muzzles, checks=checks, icon=[list(C @ Vector(v)) for v in Vi], source=bpy.data.filepath, idle=idle),
              open(os.path.join(work, 'export.json'), 'w', encoding='utf-8'))
    print('EXPORT_DONE', len(names), 'joints', len(clips), 'clips', offset, 'samples', spanning, 'spanning triangles')


# ---------------------------------------------------------------------------------------------------- outside Blender
def quat_matrix(q):
    x, y, z, w = q
    return [[1 - 2 * (y * y + z * z), 2 * (x * y - z * w), 2 * (x * z + y * w)],
            [2 * (x * y + z * w), 1 - 2 * (x * x + z * z), 2 * (y * z - x * w)],
            [2 * (x * z - y * w), 2 * (y * z + x * w), 1 - 2 * (x * x + y * y)]]


def engine_pose(np, ex, data, clip, sample):
    """Bone world matrices (column form) the way ComponentModel.ProcessBoneHierarchy composes them (see build_mech_assets.py)."""
    n = len(ex['names']); base = (clip['offset'] + sample) * n; W = []
    for i in range(n):
        q = data[base + i, :4]; t = data[base + i, 4:7]; s = data[base + i, 7]
        L = np.eye(4); L[:3, :3] = np.array(quat_matrix(q)) * s; L[:3, 3] = t
        p = np.array(ex['pivot'][i]); pp = np.array(ex['pivot'][ex['parent'][i]]) if ex['parent'][i] >= 0 else np.zeros(3)
        Tp = np.eye(4); Tp[:3, 3] = p; Tm = np.eye(4); Tm[:3, 3] = -p
        local = Tm @ L @ Tp; local[:3, 3] += p - pp
        W.append((W[ex['parent'][i]] if ex['parent'][i] >= 0 else np.eye(4)) @ local)
    return W


def write_glb(np, path, ex, image, mime, name):
    bin_ = bytearray(); views = []; accessors = []; meshes = []; nodes = []
    def view(data, target=None):
        while len(bin_) % 4: bin_.append(0)
        off = len(bin_); bin_.extend(data); v = dict(buffer=0, byteOffset=off, byteLength=len(data))
        if target: v['target'] = target
        views.append(v); return len(views) - 1
    def acc(arr, ctype, typ, target, minmax=False):
        a = dict(bufferView=view(arr.tobytes(), target), componentType=ctype, count=int(arr.shape[0]), type=typ)
        if minmax: a['min'] = arr.min(0).tolist(); a['max'] = arr.max(0).tolist()
        accessors.append(a); return len(accessors) - 1
    nodes.append(dict(name=name, children=[i + 1 for i, p in enumerate(ex['parent']) if p < 0]))
    for i, nm in enumerate(ex['names']):
        p = np.array(ex['pivot'][i]); pp = np.array(ex['pivot'][ex['parent'][i]]) if ex['parent'][i] >= 0 else np.zeros(3)
        g = dict(name=nm, translation=[float(x) for x in p - pp])
        kids = [k + 1 for k, q in enumerate(ex['parent']) if q == i]
        if kids: g['children'] = kids
        part = ex['parts'].get(str(i))
        if part:
            assert len(part['pos']) < 65536, f'{nm}: {len(part["pos"])} vertices do not fit 16-bit indices'
            pos = np.array(part['pos'], '<f4'); nrm = np.array(part['nrm'], '<f4'); uv = np.array(part['uv'], '<f4'); idx = np.array(part['idx'], '<u2').reshape(-1)
            prim = dict(attributes=dict(POSITION=acc(pos, 5126, 'VEC3', 34962, True), NORMAL=acc(nrm, 5126, 'VEC3', 34962), TEXCOORD_0=acc(uv, 5126, 'VEC2', 34962)),
                        indices=acc(idx, 5123, 'SCALAR', 34963), material=0)
            meshes.append(dict(name=nm, primitives=[prim])); g['mesh'] = len(meshes) - 1
        nodes.append(g)
    img = view(image)
    gltf = dict(asset=dict(version='2.0', generator='MechDefense build_hero_assets'), scene=0, scenes=[dict(nodes=[0])], nodes=nodes, meshes=meshes,
                materials=[dict(name=name, pbrMetallicRoughness=dict(baseColorTexture=dict(index=0), metallicFactor=0, roughnessFactor=1), doubleSided=True)],
                textures=[dict(sampler=0, source=0)], images=[dict(bufferView=img, mimeType=mime, name=name)],
                samplers=[dict(magFilter=9729, minFilter=9987, wrapS=33071, wrapT=33071)],
                accessors=accessors, bufferViews=views, buffers=[dict(byteLength=len(bin_))])
    js = json.dumps(gltf, separators=(',', ':')).encode(); js += b' ' * (-len(js) % 4); bin_ += b'\0' * (-len(bin_) % 4)
    path.write_bytes(struct.pack('<III', 0x46546C67, 2, 12 + 8 + len(js) + 8 + len(bin_)) + struct.pack('<II', len(js), 0x4E4F534A) + js
                     + struct.pack('<II', len(bin_), 0x004E4942) + bytes(bin_))


def write_icon(np, path, ex):
    """MDTowerLibrary.IconMesh format: int32 nv, int32 ni, nv x (x y z u v shade), ni int32; engine winding (clockwise)."""
    V = np.array(ex['icon']); P = []; U = []
    for j, part in ex['parts'].items():
        for tri in part['idx']:
            P.append([V[part['src'][k]] for k in tri]); U.append([part['uv'][k] for k in tri])
    P = np.array(P); U = np.array(U)
    lo, hi = P.reshape(-1, 3).min(0), P.reshape(-1, 3).max(0); P = (P - (lo + hi) / 2) / max(hi - lo)
    N = np.cross(P[:, 1] - P[:, 0], P[:, 2] - P[:, 0]); N /= np.maximum(np.linalg.norm(N, axis=1), 1e-9)[:, None]
    shade = .62 + .38 * np.abs(N[:, 1])
    Vv = np.concatenate([P.reshape(-1, 3), U.reshape(-1, 2), np.repeat(shade, 3)[:, None]], 1).astype('<f4')
    I = np.arange(len(Vv), dtype='<i4').reshape(-1, 3)[:, [0, 2, 1]].reshape(-1)
    path.write_bytes(struct.pack('<ii', len(Vv), len(I)) + Vv.tobytes() + I.tobytes()); return len(P)


def build(hero, a, np):
    import hashlib, io, shutil, tempfile
    from PIL import Image
    cfg = HEROES[hero]; v9 = Path(a.v9); blend = v9 / f'{hero}_V9.blend'; texture = v9 / 'textures' / f'{hero}_clean_2048_sharp.png'
    # the roles must agree with the original's attribute words (decoded by v9_heroes/tools/hero_decode.py)
    rf = v9 / 'motion' / f'roles_{hero}.json'
    if rf.exists():
        r = json.loads(rf.read_text(encoding='utf-8'))['roles']; ro = cfg['roles']
        for key, bit in (('rifle', 'rifle'), ('laser', 'laser'), ('missile', 'missile'), ('cannonMuzzle', 'cannon'), ('grenade', 'grenade'), ('jetL', 'jetL'), ('jetR', 'jetR')):
            assert [n[:3] for n in r[bit]] == [ro[key]], (hero, key, r[bit], ro[key])
    work = Path(tempfile.mkdtemp(prefix=f'hero_assets_{hero}_'))
    r = subprocess.run([a.blender, '-b', str(blend), '--python', __file__, '--', '--export', str(work), hero], capture_output=True, text=True, encoding='utf-8', errors='replace',
                       creationflags=getattr(subprocess, 'CREATE_NO_WINDOW', 0))
    if 'EXPORT_DONE' not in r.stdout: print(r.stdout[-3000:], r.stderr[-3000:]); sys.exit(f'{hero}: Blender export failed')
    print(hero, [l for l in r.stdout.splitlines() if l.startswith('EXPORT_DONE')][0])
    ex = json.loads((work / 'export.json').read_text(encoding='utf-8')); data = np.fromfile(work / 'clips.bin', '<f4').reshape(-1, 8)
    report = []
    for c in ex['checks']:
        clip = next(x for x in ex['clips'] if x['name'] == c['clip']); W = engine_pose(np, ex, data, clip, c['sample'])
        ref = np.array(c['verts']); err = []
        for j, part in ex['parts'].items():
            pos = np.c_[np.array(part['pos']), np.ones(len(part['pos']))]; got = (W[int(j)] @ pos.T).T[:, :3]
            err.append(np.linalg.norm(got - ref[part['src']], axis=1))
        e = np.concatenate(err); report.append(dict(clip=c['clip'], frame=c['frame'], max_error_m=float(e.max()), p99_m=float(np.percentile(e, 99))))
    out = ROOT / 'Mod/Assets/Imported' / cfg['folder']; out.mkdir(parents=True, exist_ok=True)
    im = Image.open(texture).convert('RGB'); buf = io.BytesIO(); im.save(buf, 'PNG', optimize=True)
    write_glb(np, out / 'model.glb', ex, buf.getvalue(), 'image/png', hero)
    im.resize((512, 512), Image.LANCZOS).save(out / 'icon.png')
    shutil.copyfile(work / 'clips.bin', out / 'clips.bin')
    tris = write_icon(np, out / 'icon.bin', ex)
    verts = {ex['names'][int(j)]: len(p['pos']) for j, p in ex['parts'].items()}
    idle = ex['idle']
    manifest = dict(unit=cfg['name'], joints=[dict(name=n, parent=p, pivot=[round(x, 5) for x in v]) for n, p, v in zip(ex['names'], ex['parent'], ex['pivot'])],
                    clips=ex['clips'], step=STEP, muzzles=ex['muzzles'], spanningTriangles=ex['spanning'], height=idle['height'], source=ex['source'], check=report,
                    scale=4.0, feet=idle['feet'], box=idle['box'], roles=cfg['roles'], cockpit=idle['cockpit'],
                    size=dict(height=idle['height'], width_x=idle['width_x'], depth=idle['depth'], body_width=idle['body_width']),
                    budget=dict(triangles=sum(len(p['idx']) for p in ex['parts'].values()), parts=len(ex['parts']), max_part_vertices=max(verts.values()),
                                vertices=sum(verts.values()), index_type='uint16', texture=list(im.size), texture_bytes_rgba8_with_mips=int(im.size[0] * im.size[1] * 4 * 4 / 3)),
                    templateBox=cfg['templateBox'], stats=cfg['stats'], original=dict(cfg['original'], body=cfg['body'], kind=cfg['kind'], model=cfg['model'], evidence='docs/HEROES_ORIGINAL.md'),
                    soundsFrom='Imported/Mercury/Audio (the original plays the same sound ids for every body)')
    (out / 'mech.json').write_text(json.dumps(manifest, ensure_ascii=False, indent=1), encoding='utf-8')
    for f in sorted(p for p in out.iterdir() if p.is_file()): print(f'  {f.name:10} {f.stat().st_size:>10,} B  sha256 {hashlib.sha256(f.read_bytes()).hexdigest()[:16]}')
    print(f'  triangles {manifest["budget"]["triangles"]} | parts {len(ex["parts"])} | max part vertices {max(verts.values())} | clips {len(ex["clips"])} | spanning {ex["spanning"]} '
          f'| height {idle["height"]} | box {idle["box"]} | icon triangles {tris}')
    for c in report: print(f"  check {c['clip']:20} frame {c['frame']:4}  max {c['max_error_m'] * 1000:7.3f} mm  p99 {c['p99_m'] * 1000:6.3f} mm")
    shutil.rmtree(work, ignore_errors=True)
    return max(c['p99_m'] for c in report)


def glb_nodes(b):
    jl, = struct.unpack_from('<I', b, 12); js = json.loads(b[20:20 + jl])
    r = lambda v: None if v is None else tuple(round(float(x), 5) for x in v)
    return [(n['name'], tuple(n.get('children', [])), r(n.get('translation')), r(n.get('rotation')), r(n.get('scale'))) for n in js['nodes']]


def use_poly(hero):
    """Ship our polygon model of a hero over the V9 export (model.glb, icon.bin, icon.png)."""
    import hashlib, shutil
    src = POLY / hero; out = ROOT / 'Mod/Assets/Imported' / HEROES[hero]['folder']
    new = (src / 'model.glb').read_bytes(); a, b = glb_nodes((out / 'model.glb').read_bytes()), glb_nodes(new)
    if a != b: sys.exit(f'{hero}: the polygon model\'s node tree differs from the V9 export: {[(x, y) for x, y in zip(a, b) if x != y][:3]} ({len(a)} vs {len(b)} nodes)')
    for s, d in [('model.glb', 'model.glb'), ('game/icon.bin', 'icon.bin'), ('game/icon.png', 'icon.png')]:
        shutil.copyfile(src / s, out / d); print(f'  poly {hero} {d:10} {(out / d).stat().st_size:>10,} B  sha256 {hashlib.sha256((out / d).read_bytes()).hexdigest()[:16]}')


def main():
    import argparse
    import numpy as np
    ap = argparse.ArgumentParser(); ap.add_argument('heroes', nargs='*', default=list(HEROES)); ap.add_argument('--v9', default=str(V9)); ap.add_argument('--blender', default=str(BLENDER))
    ap.add_argument('--no-poly', action='store_true'); ap.add_argument('--poly-only', action='store_true')
    a = ap.parse_args()
    if a.poly_only:
        for h in a.heroes:
            if h in POLY_HEROES: use_poly(h)
        return
    worst = {h: build(h, a, np) for h in a.heroes}
    bad = [h for h, w in worst.items() if w > 1e-3]
    if bad: sys.exit(f're-posed model does not match Blender (p99 > 1 mm): {bad}')
    if not a.no_poly:
        for h in a.heroes:
            if h in POLY_HEROES: use_poly(h)
    print('HERO_ASSETS_DONE', worst)


if __name__ == '__main__':
    if '--export' in sys.argv: i = sys.argv.index('--export'); blender_export(sys.argv[i + 1], sys.argv[i + 2])
    else: main()
