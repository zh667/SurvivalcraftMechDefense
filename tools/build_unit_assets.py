"""Build the enemy robot assets (Destroy Gunners) from the Blender files of E:/projects/dguns-enemy-models.

python tools/build_unit_assets.py [RD05 RD06 RD07 RD17] [--rd17-scale 3|4] [--blender <blender.exe>]

Units (asset project README: E:/projects/dguns-enemy-models/README.md):
  RD05 双管步行机兵 (normal), RD06 导弹步行机兵 and RD07 炮击步行机兵 (elites), RD17 恶魔机甲 (boss).
  The walkers are the original models x4 like Mercury (same metres per original unit); RD17 is x3 by default (x4 is 20.5 m
  across the wings and 12.8 m tall), `--rd17-scale 4` takes blend/RD17_x4.blend instead.
Output per unit: Mod/Assets/Imported/<UNIT>/ (ignored: extracted game data, rebuilt by this tool, packaged by package.py)
  model.glb      rigid parts, one node per joint at its rest pivot, the part's mesh in node space, the clear texture embedded as
                 lossless PNG (Real-ESRGAN x4plus + unsharp mask, the method accepted for Mercury); 16-bit indices
  model_b.glb, model_c.glb   walkers only: the same geometry with the original's B / C paint
  unit.json      Mercury's mech.json schema (joints, clips, step, muzzles, height) plus scale, box (suggested creature body box,
                 metres), feet, skins, rootMotion, events per clip (key event bits; 0x8000 = the shot), check, budget, sounds
  clips.bin      float32 per clip, per sample (every 2 ticks = 1/30 s), per joint: qx qy qz qw tx ty tz s (game frame)
  icon.bin / icon.png   inventory icon in MDTowerLibrary.IconMesh format (idle pose) and its 512 texture
  Audio/seNN.ogg the original sound effects by id (se.bin entry NN)
Game frame: Y up, the unit faces -Z, its right is +X. The .blend files are Z up, face -Y, left +X: (x, y, z)_game = (-x, z, y).
A joint's bone transform in the engine is T(p) L T(-p) (row vectors) with p its rest pivot and L the local deformation; this
tool re-poses the model from its own output with the engine's composition and compares with Blender's evaluation (p99 < 1 mm).
"""
import json, os, struct, subprocess, sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
ASSETS = Path(r'E:\projects\dguns-enemy-models')
BLENDER = Path(r'E:\projects\tools\Blender\blender-4.2.9-windows-x64\blender.exe')
SE_DIR = ROOT / 'local/dguns/assets/se.bin'      # the game's se.bin as ogg files 00NN.ogg (archive entry NN = sound id NN)
STEP = 2
WALKER_TEX = {'model': 'walker_A_1024_sharp.png', 'model_b': 'walker_B_1024_sharp.png', 'model_c': 'walker_C_1024_sharp.png'}
UNITS = {
    'RD05': dict(blend='RD05_x4.blend', skins=WALKER_TEX, sounds={8: 'shot_gun', 13: 'hit_gun', 24: 'bomb_explosion (destroyed)'}),
    'RD06': dict(blend='RD06_x4.blend', skins=WALKER_TEX, sounds={9: 'shot_missile', 14: 'hit_missile', 24: 'bomb_explosion (destroyed)'}),
    'RD07': dict(blend='RD07_x4.blend', skins=WALKER_TEX, sounds={11: 'shot_cannon', 13: 'hit_gun', 24: 'bomb_explosion (destroyed)'}),
    'RD17': dict(blend='RD17_x3.blend', skins={'model': 'devil_2048_sharp.png'},
                 sounds={31: 'boss_hover (the game names it for a boss hovering)', 10: 'shot_laser (inferred for the arm units)',
                         16: 'punch_swing (inferred for the sabres)', 17: 'hit_punch (inferred for sabre hits)', 24: 'bomb_explosion (destroyed)'}),
}
CHECK_FRAMES = {'walker': [('idle', 0), ('walk_forward', 20), ('fire', 8), ('hit', 12), ('death', 999)],
                'RD17': [('idle', 0), ('attack_saber', 40), ('dash_forward', 18), ('spin', 20), ('death', 999)]}


def blender_export(work, unit):
    """Runs inside Blender: dump geometry, clips, muzzles, check poses to <work>/export.json + clips.bin."""
    import bpy
    import numpy as np
    from mathutils import Matrix, Vector
    arm = next(o for o in bpy.data.objects if o.type == 'ARMATURE'); mesh = next(o for o in bpy.data.objects if o.type == 'MESH'); sc = bpy.context.scene
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
    me = mesh.data; me.calc_loop_triangles(); uvl = me.uv_layers.active.data; cn = me.corner_normals
    gname = {g.index: g.name for g in mesh.vertex_groups}
    vj = [idx[gname[max(v.groups, key=lambda g: g.weight).group]] for v in me.vertices]
    parts = {}; spanning = 0; tri_part = []; tri_uv = []
    for t in me.loop_triangles:
        js = [vj[v] for v in t.vertices]; j = js[0]; spanning += len(set(js)) > 1
        P = parts.setdefault(j, dict(key={}, pos=[], nrm=[], uv=[], src=[], idx=[])); tri = []
        for li, vi in zip(t.loops, t.vertices):
            p = C @ me.vertices[vi].co; n = (C3 @ cn[li].vector).normalized(); u = uvl[li].uv
            k = (vi, round(n.x, 4), round(n.y, 4), round(n.z, 4), round(u.x, 5), round(u.y, 5))
            if k not in P['key']:
                P['key'][k] = len(P['pos']); P['pos'].append(list(p - pivot[j])); P['nrm'].append(list(n)); P['uv'].append([u.x, 1 - u.y]); P['src'].append(vi)
            tri.append(P['key'][k])
        P['idx'].append(tri); tri_part.append(j); tri_uv.append([tuple(uvl[li].uv) for li in t.loops])
    # ---------------------------------------------------------- clips
    def deform(fr):
        sc.frame_set(fr); D = {}
        for n in names:
            pb = arm.pose.bones[n]; D[n] = C @ (pb.matrix @ bones[n].matrix_local.inverted()) @ Ci
        return D
    acts = sorted((a for a in bpy.data.actions if 'slot' in a), key=lambda a: a['slot'])
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
        clips.append(dict(name=a.name, slot=int(a['slot']), loop=bool(a.get('loops')), hold=bool(a.get('hold')), generated=False,
                          samples=len(frames), ticks=f1 - f0, offset=offset, footSpeed=None, lastStep=frames[-1] - frames[-2] if len(frames) > 1 else STEP))
        offset += len(frames)
    np.array(blob, dtype='<f4').tofile(os.path.join(work, 'clips.bin'))
    # ---------------------------------------------------------- rest geometry (bind = rest: identity rotations) in Blender frame
    V0 = np.array([tuple(v.co) for v in me.vertices]); vjn = np.array(vj)
    fwd = np.array([0, -1, 0.])                                     # the unit faces -Y in Blender
    def rest_pt(p): return [float(x) for x in (C @ Vector(p))]
    def rest_dir(d): return [float(x) for x in (C3 @ Vector(d)).normalized()]
    def axis(P, toward):
        c = P.mean(0); w, v = np.linalg.eigh(np.cov((P - c).T)); a_ = v[:, -1]; return (a_ if a_ @ toward > 0 else -a_)
    def jn(prefix): return names[J[prefix]]
    def tip(prefix, toward, band):
        P = V0[vjn == J[prefix]]; s = P @ toward; T = P[s > s.max() - band]
        return T, P
    muzzles = {}
    walker = unit != 'RD17'
    if walker:
        for side, pre in (('L', '05_'), ('R', '06_')):
            P = V0[vjn == J[pre]]
            if unit == 'RD06':
                # missile hatches: the red-textured faces of the pod (area-weighted centre, mean outward normal)
                # detected on the ORIGINAL 256 texture: the upscale softens the red of the hatches (r-g 0.23 -> 0.11)
                img = bpy.data.images.load(str(ASSETS / 'textures' / 'walker_A_orig_256.png'), check_existing=True)
                W_, H_ = img.size; px = np.array(img.pixels[:]).reshape(H_, W_, img.channels)
                cs = []; ns = []; ws = []
                for t, j, uvs in zip(me.loop_triangles, tri_part, tri_uv):
                    if j != J[pre]: continue
                    U = np.array(uvs); bw = np.array([(a_, b_, 6 - a_ - b_) for a_ in range(7) for b_ in range(7 - a_)], float) / 6   # 28 barycentric samples
                    S = (bw @ U) % 1.0; c = px[np.minimum(H_ - 1, (S[:, 1] * H_).astype(int)), np.minimum(W_ - 1, (S[:, 0] * W_).astype(int))][:, :3].mean(0)
                    if c[0] > .4 and c[0] - c[1] > .12:                       # area-averaged colour: red hatch (the dark launch holes pull the mean down)
                        A = np.array([tuple(me.vertices[k].co) for k in t.vertices]); n_ = np.cross(A[1] - A[0], A[2] - A[0]); area = np.linalg.norm(n_) / 2
                        if area > 0: cs.append(A.mean(0)); ns.append(n_ / (2 * area)); ws.append(area)
                ws = np.array(ws); c_ = (np.array(cs) * ws[:, None]).sum(0) / ws.sum(); nn = (np.array(ns) * ws[:, None]).sum(0)
                if nn @ (c_ - np.array(bones[jn(pre)].head_local)) < 0: nn = -nn
                muzzles['pod' + side] = dict(joint=jn(pre), at=rest_pt(c_), dir=rest_dir(nn / np.linalg.norm(nn)), spread=float(np.ptp(np.array(cs)[:, 2])),
                                             how='red missile-hatch faces of the pod: area-weighted centre and mean normal (%d faces)' % len(cs))
            else:
                # Box barrels have vertices only at their end caps, so the axis comes from the whole weapon part (long axis); it must
                # agree with the rest-pose forward within 15 degrees, otherwise rest forward is used (reported in 'how').
                T, P = tip(pre, fwd, .08); a_ = axis(P, fwd); ang = float(np.degrees(np.arccos(min(1, abs(a_ @ fwd)))))
                use = a_ if ang < 15 else fwd; key = ('gun' if unit == 'RD05' else 'cannon') + side
                muzzles[key] = dict(joint=jn(pre), at=rest_pt(T.mean(0)), dir=rest_dir(use), spread=float(np.ptp(T[:, 0])),
                                    how='barrel tip: vertices within 8 cm of the most forward point; direction: long axis of the weapon part (%.1f deg from rest forward)%s'
                                        % (ang, '' if ang < 15 else ' -> rest forward used'))
        if unit == 'RD07': muzzles['cannon'] = dict(muzzles['cannonL'], how='alias of cannonL (the left cannon); cannonR is the right one')
        T, _ = tip('04_', fwd, .05); muzzles['eye'] = dict(joint=jn('04_'), at=rest_pt(T.mean(0)), dir=rest_dir(fwd), how='front of the sensor (04_センサー)')
        P = V0[vjn == J['03_']]; muzzles['core'] = dict(joint=jn('03_'), at=rest_pt((P.min(0) + P.max(0)) / 2), dir=rest_dir(fwd), how='centre of the cockpit block (03_コックピット)')
    else:
        for side, sab, armw in (('L', '10_', '09_'), ('R', '16_', '15_')):
            P = V0[vjn == J[sab]]; a_ = axis(P, np.array([0, 0, -1.]))
            muzzles['sabre' + side] = dict(joint=jn(sab), at=rest_pt(P.mean(0)), dir=rest_dir(a_), length=float(np.ptp(P @ a_)), how='beam-sabre blade: centre and long axis (blade drawn in attack_saber_draw)')
            T, P = tip(armw, np.array([0, 0, -1.]), .06); a_ = axis(P, np.array([0, 0, -1.]))
            muzzles['arm' + side] = dict(joint=jn(armw), at=rest_pt(T.mean(0)), dir=rest_dir(a_), how='lower tip and long axis of the arm weapon unit (09/15 兵装懸架システム05); points forward when attack_fire raises the arms (inferred)')
        T, _ = tip('03_', fwd, .08); muzzles['eye'] = dict(joint=jn('03_'), at=rest_pt(T.mean(0)), dir=rest_dir(fwd), how='front of the head sensor unit (03_頭部センサーユニット01)')
        P = V0[vjn == J['02_']]; muzzles['core'] = dict(joint=jn('02_'), at=rest_pt((P.min(0) + P.max(0)) / 2), dir=rest_dir(fwd), how='centre of the abdomen turret / cockpit block (02_)')
        T, _ = tip('02_', fwd, .15); muzzles['weak'] = dict(joint=jn('02_'), at=rest_pt(T.mean(0)), dir=rest_dir(fwd),
                                                        how='candidate weak point: front of joint 02 腹部ターレットおよびコックピット (abdominal turret and cockpit), the only part named as the cockpit')
    # ---------------------------------------------------------- idle numbers, suggested body box
    def evaluated(action, fr):
        arm.animation_data.action = bpy.data.actions[action]; sc.frame_set(fr); dg = bpy.context.evaluated_depsgraph_get(); m = mesh.evaluated_get(dg).to_mesh()
        V = np.array([tuple(v.co) for v in m.vertices]); mesh.evaluated_get(dg).to_mesh_clear(); return V
    Vi = evaluated('idle', 0)
    # collision box from the body only: walkers without their weapon parts (05/06); RD17 waist, cockpit, head and legs (01-03, 19-28),
    # without arms, sabres and wings
    core = [p for p in J if p not in ('05_', '06_')] if walker else ['01_', '02_', '03_'] + ['%02d_' % k for k in range(19, 29)]
    keep = np.isin(vjn, [J[p] for p in core if p in J]); B = Vi[keep]
    wid = float(max(np.ptp(B[:, 0]), np.ptp(B[:, 1]))); box = [round(wid + .05, 1), round(float(Vi[:, 2].max()), 1), round(wid + .05, 1)]
    # ---------------------------------------------------------- check poses
    checks = []
    for an, fr in CHECK_FRAMES['walker' if walker else 'RD17']:
        ac = bpy.data.actions[an]; last = int(ac.frame_range[1]); fr = min(fr, last)
        if fr % STEP and fr != last: fr -= fr % STEP
        Vb = evaluated(an, fr); clip = next(c for c in clips if c['name'] == an)
        s = clip['samples'] - 1 if fr == last else fr // STEP
        checks.append(dict(clip=an, frame=fr, sample=s, verts=[list(C @ Vector(v)) for v in Vb]))
    json.dump(dict(names=names, parent=parent, pivot=[list(p) for p in pivot], spanning=spanning,
                   parts={str(j): dict(pos=P['pos'], nrm=P['nrm'], uv=P['uv'], idx=P['idx'], src=P['src']) for j, P in parts.items()},
                   clips=clips, muzzles=muzzles, checks=checks, icon=[list(C @ Vector(v)) for v in Vi], source=bpy.data.filepath,
                   scale=float(arm.get('scale', 0)), height=round(float(Vi[:, 2].max()), 3), feet=round(float(Vi[:, 2].min()), 4), box=box),
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
    gltf = dict(asset=dict(version='2.0', generator='MechDefense build_unit_assets'), scene=0, scenes=[dict(nodes=[0])], nodes=nodes, meshes=meshes,
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


def clip_meta(unit, scale, ex):
    """Per clip: names/evidence (motion/clip_names.json), key event bits with their time in the clip, decoded root motion, planted feet."""
    names = json.loads((ASSETS / 'motion/clip_names.json').read_text(encoding='utf-8'))[unit]
    src = json.loads((ASSETS / f'motion/clips_{unit}.json').read_text(encoding='utf-8'))
    feet = json.loads((ASSETS / 'motion/analysis2.json').read_text(encoding='utf-8'))['feet'].get(unit, {})
    root = {}; meta = {}
    for c in src['clips']:
        nm = names.get(str(c['slot']), {}).get('name', 'clip_%d' % c['slot']); t0 = c['timeline'][0][0]
        times = [t for t, _ in c['timeline']]; ev = []
        for i, k in enumerate(c['keys']):
            bits = [x & 0xFFFF for x in k['raw_record'][2:5]]
            if any(bits) and i < len(times): ev.append(dict(tick=round(times[i] - t0, 2), key=i, bits=bits))
        if c['loops'] and any(c['keys'][0]['raw_record'][2:5]) and len(times) > len(c['keys']):
            ev.append(dict(tick=round(times[-1] - t0, 2), key=0, bits=[x & 0xFFFF for x in c['keys'][0]['raw_record'][2:5]], note='key 0 again at the end of the loop'))
        mv = [k['raw_record'][5] / 16384 for k in c['keys'] if (k['raw_record'][1] & 0x7FFF) != 9999]
        u = sum(mv) / len(mv) if mv else 0
        f = feet.get(str(c['slot']))
        root[nm] = dict(decoded_units_per_tick=round(u, 5), m_per_s_model_scale=round(u * 60 * scale, 2), m_per_s_mercury_convention=round(u * 60 * 2.67 * scale / 4, 2),
                        planted_foot_m_per_s=round(f['speed_units_per_tick'] * 60 * scale, 2) if f and f['speed_units_per_tick'] > .01 else None)
        meta[nm] = dict(events=ev, meaning=names.get(str(c['slot']), {}).get('meaning'), evidence=names.get(str(c['slot']), {}).get('evidence'),
                        status=names.get(str(c['slot']), {}).get('status', 'unnamed'))
    return meta, root


def build(unit, a, np):
    import hashlib, io, shutil, tempfile
    cfg = UNITS[unit]; blend = ASSETS / 'blend' / (cfg['blend'] if unit != 'RD17' else f'RD17_x{a.rd17_scale}.blend')
    work = Path(tempfile.mkdtemp(prefix=f'unit_assets_{unit}_'))
    r = subprocess.run([a.blender, '-b', str(blend), '--python', __file__, '--', '--export', str(work), unit], capture_output=True, text=True, encoding='utf-8', errors='replace',
                       creationflags=getattr(subprocess, 'CREATE_NO_WINDOW', 0))
    if 'EXPORT_DONE' not in r.stdout: print(r.stdout[-3000:], r.stderr[-3000:]); sys.exit(f'{unit}: Blender export failed')
    print(unit, [l for l in r.stdout.splitlines() if l.startswith('EXPORT_DONE')][0])
    ex = json.loads((work / 'export.json').read_text(encoding='utf-8')); data = np.fromfile(work / 'clips.bin', '<f4').reshape(-1, 8)
    report = []
    for c in ex['checks']:
        clip = next(x for x in ex['clips'] if x['name'] == c['clip']); W = engine_pose(np, ex, data, clip, c['sample'])
        ref = np.array(c['verts']); err = []
        for j, part in ex['parts'].items():
            pos = np.c_[np.array(part['pos']), np.ones(len(part['pos']))]; got = (W[int(j)] @ pos.T).T[:, :3]
            err.append(np.linalg.norm(got - ref[part['src']], axis=1))
        e = np.concatenate(err); report.append(dict(clip=c['clip'], frame=c['frame'], max_error_m=float(e.max()), p99_m=float(np.percentile(e, 99))))
    out = ROOT / 'Mod/Assets/Imported' / unit; out.mkdir(parents=True, exist_ok=True)
    from PIL import Image
    for model, tex in cfg['skins'].items():
        im = Image.open(ASSETS / 'textures' / tex).convert('RGB'); buf = io.BytesIO(); im.save(buf, 'PNG', optimize=True)
        write_glb(np, out / f'{model}.glb', ex, buf.getvalue(), 'image/png', unit + ('' if model == 'model' else model[-2:]))
        if model == 'model': im.resize((512, 512), Image.LANCZOS).save(out / 'icon.png')
    shutil.copyfile(work / 'clips.bin', out / 'clips.bin')
    (out / 'Audio').mkdir(exist_ok=True)
    for i in cfg['sounds']: shutil.copyfile(SE_DIR / f'{i:04d}.ogg', out / 'Audio' / f'se{i:02d}.ogg')
    tris = write_icon(np, out / 'icon.bin', ex)
    meta, root = clip_meta(unit, ex['scale'], ex)
    for c in ex['clips']:
        c.update(meta.get(c['name'], {}))
        if root.get(c['name'], {}).get('planted_foot_m_per_s'): c['footSpeed'] = root[c['name']]['planted_foot_m_per_s']
    verts = {ex['names'][int(j)]: len(p['pos']) for j, p in ex['parts'].items()}
    manifest = dict(unit=unit, joints=[dict(name=n, parent=p, pivot=[round(x, 5) for x in v]) for n, p, v in zip(ex['names'], ex['parent'], ex['pivot'])],
                    clips=ex['clips'], step=STEP, muzzles=ex['muzzles'], height=ex['height'], scale=ex['scale'], box=ex['box'], feet=ex['feet'],
                    skins=list(cfg['skins']), rootMotion=root, spanningTriangles=ex['spanning'], source=ex['source'], check=report,
                    budget=dict(triangles=sum(len(p['idx']) for p in ex['parts'].values()), parts=len(ex['parts']), max_part_vertices=max(verts.values()), index_type='uint16',
                                textures={m: list(Image.open(ASSETS / 'textures' / t).size) for m, t in cfg['skins'].items()}),
                    sounds={f'se{i:02d}': n for i, n in cfg['sounds'].items()},
                    notes='Clip meanings are inferred unless status says decoded (see evidence). Root motion: the game moves the actor by move/16384 '
                          'units per tick; planted_foot_m_per_s is the speed at which the feet do not slide at this scale.')
    (out / 'unit.json').write_text(json.dumps(manifest, ensure_ascii=False, indent=1), encoding='utf-8')
    for f in sorted(p for p in out.rglob('*') if p.is_file()): print(f'  {str(f.relative_to(out)):16} {f.stat().st_size:>10,} B  sha256 {hashlib.sha256(f.read_bytes()).hexdigest()[:16]}')
    print(f'  icon triangles {tris} | parts {len(ex["parts"])} | max part vertices {max(verts.values())} | clips {len(ex["clips"])} | spanning triangles {ex["spanning"]} | box {ex["box"]} | height {ex["height"]}')
    for c in report: print(f"  check {c['clip']:18} frame {c['frame']:4}  max {c['max_error_m'] * 1000:7.3f} mm  p99 {c['p99_m'] * 1000:6.3f} mm")
    shutil.rmtree(work, ignore_errors=True)
    return max(c['p99_m'] for c in report)


def main():
    import argparse
    import numpy as np
    ap = argparse.ArgumentParser(); ap.add_argument('units', nargs='*', default=list(UNITS)); ap.add_argument('--rd17-scale', type=int, default=3, choices=(3, 4))
    ap.add_argument('--blender', default=str(BLENDER)); a = ap.parse_args()
    worst = {u: build(u, a, np) for u in a.units}
    bad = [u for u, w in worst.items() if w > 1e-3]
    if bad: sys.exit(f're-posed model does not match Blender (p99 > 1 mm): {bad}')
    print('UNIT_ASSETS_DONE', worst)


if __name__ == '__main__':
    if '--export' in sys.argv: i = sys.argv.index('--export'); blender_export(sys.argv[i + 1], sys.argv[i + 2])
    else: main()
