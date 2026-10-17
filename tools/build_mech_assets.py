"""Build the Mercury mech assets from the V8 asset of E:/projects/dguns-mech-model (Destroy Gunners Mercury, x4 scale).

python tools/build_mech_assets.py [--source <mercury_V8.blend>] [--blender <blender.exe>]

Output: Mod/Assets/Imported/Mercury/ (ignored: extracted game data, rebuilt by this tool, packaged by package.py)
  model.glb   rigid parts, one node per joint at its rest pivot, the part's mesh in node space; the texture is embedded
              (JPEG; the engine builds the mipmaps and samples it linearly). Drawn two-sided by ComponentMDMechModel.
  mech.json   joints (name, parent, pivot), clips (name, slot, loop, hold, samples, offset), muzzles, measured speeds
  clips.bin   float32 per clip, per sample (every 2 ticks = 1/30 s), per joint: qx qy qz qw tx ty tz s, the joint's
              deformation relative to its parent's (column vectors, game frame)
  icon.bin    inventory icon (idle pose, unit size, MDTowerLibrary.IconMesh format) with icon.png (512 texture)
  Audio/seNN.ogg  the original sound effects Mercury uses, by the game's own id (se.bin entry NN; names in SE_NAMES)
Game frame: Y up, the mech faces -Z, its right is +X. The V8 .blend is Z up, faces -Y, left +X: (x, y, z)_game = (-x, z, y).
A joint's bone transform in the engine is T(p) L T(-p) (row vectors) with p its rest pivot and L the local deformation;
this tool re-poses the model from its own output with the engine's composition and compares with Blender's evaluation.
"""
import json, os, struct, subprocess, sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT / 'Mod/Assets/Imported/Mercury'
SOURCE = Path(r'E:\projects\dguns-mech-model\v8_assets\mercury_V8.blend')
CONTRACT = 'mercury_V8_contract.json'           # next to the .blend: measured planted-foot speeds
BLENDER = Path(r'E:\projects\tools\Blender\blender-4.2.9-windows-x64\blender.exe')
STEP = 2                                        # ticks between samples
SE_DIR = ROOT / 'local/dguns/assets/se.bin'      # the game's se.bin as ogg files 00NN.ogg (archive entry NN = sound id NN)
SE_NAMES = {7: 'lock_on', 8: 'shot_gun', 9: 'shot_missile (also jump / boost take-off)', 10: 'shot_laser', 11: 'shot_cannon', 13: 'hit_gun',
            14: 'hit_missile', 16: 'punch_swing', 17: 'hit_punch', 19: 'player_damage', 20: 'player_destruction', 23: 'throw_bomb',
            24: 'bomb_explosion', 25: 'change_to_gun', 26: 'change_to_missile', 27: 'change_to_cannon', 29: 'change_to_bomb', 30: 'change_to_laser',
            31: 'boss_hover (the hover hum)'}


def blender_export(work):
    """Runs inside Blender: dump geometry, clips, muzzles and check poses to <work>/export.json + clips.bin."""
    import bpy
    import numpy as np
    from mathutils import Matrix, Vector
    arm = bpy.data.objects['mercury_rig']; mesh = bpy.data.objects['mercury_body']; sc = bpy.context.scene
    for lc in bpy.context.view_layer.layer_collection.children: lc.hide_viewport = False
    eye4 = Matrix.Identity(4)
    assert all(abs(arm.matrix_world[i][j] - eye4[i][j]) < 1e-6 and abs(mesh.matrix_world[i][j] - eye4[i][j]) < 1e-6 for i in range(4) for j in range(4))
    C = Matrix(((-1, 0, 0, 0), (0, 0, 1, 0), (0, 1, 0, 0), (0, 0, 0, 1))); Ci = C.inverted(); C3 = C.to_3x3()
    bones = arm.data.bones
    order = lambda n: -1 if n.startswith('36_') else int(n.split('_')[0])
    names = sorted((b.name for b in bones), key=order); idx = {n: i for i, n in enumerate(names)}
    parent = [idx[bones[n].parent.name] if bones[n].parent else -1 for n in names]
    assert all(p < i for i, p in enumerate(parent)), 'parents first'
    pivot = [C @ bones[n].head_local for n in names]
    J = {n[:3]: idx[n] for n in names}
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
    contract = json.load(open(os.path.join(os.path.dirname(bpy.data.filepath), CONTRACT), encoding='utf-8'))
    info = {c['action']: c for c in contract['clips']}
    def deform(fr):
        sc.frame_set(fr); D = {}
        for n in names:
            pb = arm.pose.bones[n]; D[n] = C @ (pb.matrix @ bones[n].matrix_local.inverted()) @ Ci
        return D
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
        c = info.get(a.name, {})
        clips.append(dict(name=a.name, slot=int(a['slot']), loop=bool(a.get('loops')), hold=bool(c.get('hold_last_pose')), generated=bool(c.get('generated')),
                          samples=len(frames), ticks=f1 - f0, offset=offset, footSpeed=c.get('planted_foot_speed_m_per_s'),
                          lastStep=frames[-1] - frames[-2] if len(frames) > 1 else STEP))
        offset += len(frames)
    np.array(blob, dtype='<f4').tofile(os.path.join(work, 'clips.bin'))
    # ------------------------------------------------------------- muzzles and camera points (rest model space, game frame)
    def evaluated(fr):
        sc.frame_set(fr); dg = bpy.context.evaluated_depsgraph_get(); m = mesh.evaluated_get(dg).to_mesh()
        V = np.array([tuple(v.co) for v in m.vertices]); mesh.evaluated_get(dg).to_mesh_clear(); return V
    def rest_point(Db, p): return list(C @ (Db.inverted() @ Vector(p)))
    def rest_dir(Db, d): return list((C3 @ (Db.to_3x3().inverted() @ Vector(d))).normalized())
    def axis(P, toward):
        c = P.mean(0); w, v = np.linalg.eigh(np.cov((P - c).T)); a = v[:, -1]
        return (a if a @ toward > 0 else -a), c
    vjn = np.array(vj); fwd = np.array([0, -1, 0.])
    arm.animation_data.action = bpy.data.actions['idle']; V = evaluated(0); sc.frame_set(0)
    Db = {n: arm.pose.bones[n].matrix @ bones[n].matrix_local.inverted() for n in names}
    muzzles = {}
    P = V[vjn == J['14_']]; a, _ = axis(P, fwd); tip = P[np.argmax((P - P.mean(0)) @ a)]
    muzzles['rifle'] = dict(joint=names[J['14_']], at=rest_point(Db[names[J['14_']]], tip), dir=rest_dir(Db[names[J['14_']]], a))
    pod = arm.pose.bones[names[J['18_']]].head; P = V[(vjn == J['02_']) & (np.linalg.norm(V - np.array(pod), axis=1) < .6)]
    assert len(P) > 8, 'missile box not found next to the missile-pod pivot'
    front = P[P[:, 1] < P[:, 1].min() + .04]; c = front.mean(0)
    muzzles['missile'] = dict(joint=names[J['02_']], at=rest_point(Db[names[J['02_']]], c), dir=rest_dir(Db[names[J['02_']]], fwd), spread=float(np.ptp(front[:, 0])))
    P = V[vjn == J['09_']]; muzzles['melee'] = dict(joint=names[J['09_']], at=rest_point(Db[names[J['09_']]], P.mean(0)), dir=rest_dir(Db[names[J['09_']]], fwd))
    P = V[vjn == J['04_']]; e = P[np.argmin(P[:, 1])] + np.array([0, 0, .12])
    muzzles['eye'] = dict(joint=names[J['04_']], at=rest_point(Db[names[J['04_']]], e), dir=rest_dir(Db[names[J['04_']]], fwd))
    # thrusters (hover and jump flames): the underside of the two lower back units (33 left, 35 right), blowing down and back
    # (inferred from the model's shape; the original's jet effect KN_JET0 is a separate effect model)
    for key, jn in (('thrusterL', '33_'), ('thrusterR', '35_')):
        P = V[vjn == J[jn]]; low = P[P[:, 2] < P[:, 2].min() + .06].mean(0)
        muzzles[key] = dict(joint=names[J[jn]], at=rest_point(Db[names[J[jn]]], low), dir=rest_dir(Db[names[J[jn]]], np.array([0, .45, -1.])))
    cf = bpy.data.actions['cannon_deploy_fire']; arm.animation_data.action = cf; fl = int(cf.frame_range[1]); V2 = evaluated(fl); sc.frame_set(fl)
    D17 = arm.pose.bones[names[J['17_']]].matrix @ bones[names[J['17_']]].matrix_local.inverted()
    P = V2[vjn == J['17_']]; a, _ = axis(P, fwd); tip = P[np.argmax((P - P.mean(0)) @ a)]
    muzzles['cannon'] = dict(joint=names[J['17_']], at=rest_point(D17, tip), dir=rest_dir(D17, a))
    # ------------------------------------------------------------- check poses: Blender's own evaluation (game frame)
    checks = []
    for an, fr in (('idle', 0), ('idle', 192), ('walk_forward', 20), ('walk_left', 30), ('cannon_deploy_fire', fl), ('death', 60), ('melee', 18)):
        arm.animation_data.action = bpy.data.actions[an]; Vb = evaluated(fr)
        clip = next(c for c in clips if c['name'] == an); last = int(bpy.data.actions[an].frame_range[1])
        assert fr % STEP == 0 or fr == last
        s = clip['samples'] - 1 if fr == last else fr // STEP
        checks.append(dict(clip=an, frame=fr, sample=s, verts=[list(C @ Vector(v)) for v in Vb]))
    # ------------------------------------------------------------- icon: idle pose, every triangle
    arm.animation_data.action = bpy.data.actions['idle']; Vi = evaluated(0)
    json.dump(dict(names=names, parent=parent, pivot=[list(p) for p in pivot], spanning=spanning,
                   parts={str(j): dict(pos=P['pos'], nrm=P['nrm'], uv=P['uv'], idx=P['idx'], src=P['src']) for j, P in parts.items()},
                   clips=clips, muzzles=muzzles, checks=checks, icon=[list(C @ Vector(v)) for v in Vi],
                   source=bpy.data.filepath, contract=dict(height=contract.get('height_idle_m'), box=[2.4, 4.8, 2.4])),
              open(os.path.join(work, 'export.json'), 'w', encoding='utf-8'))
    print('EXPORT_DONE', len(names), 'joints', len(clips), 'clips', offset, 'samples', spanning, 'spanning triangles')


# ---------------------------------------------------------------------------------------------------- outside Blender
def quat_matrix(q):
    x, y, z, w = q
    return [[1 - 2 * (y * y + z * z), 2 * (x * y - z * w), 2 * (x * z + y * w)],
            [2 * (x * y + z * w), 1 - 2 * (x * x + z * z), 2 * (y * z - x * w)],
            [2 * (x * z - y * w), 2 * (y * z + x * w), 1 - 2 * (x * x + y * y)]]


def engine_pose(np, ex, data, clip, sample):
    """Bone world matrices (column form) the way ComponentModel.ProcessBoneHierarchy composes them: the transform we set
    is T(-p) L T(p) in column form (CreateTranslation(p)*L*CreateTranslation(-p) in the engine's row form); the engine adds
    the node's bind translation (pivot - parent pivot) and multiplies by the parent's world."""
    n = len(ex['names']); base = (clip['offset'] + sample) * n; W = []
    for i in range(n):
        q = data[base + i, :4]; t = data[base + i, 4:7]; s = data[base + i, 7]
        L = np.eye(4); L[:3, :3] = np.array(quat_matrix(q)) * s; L[:3, 3] = t
        p = np.array(ex['pivot'][i]); pp = np.array(ex['pivot'][ex['parent'][i]]) if ex['parent'][i] >= 0 else np.zeros(3)
        Tp = np.eye(4); Tp[:3, 3] = p; Tm = np.eye(4); Tm[:3, 3] = -p
        local = Tm @ L @ Tp; local[:3, 3] += p - pp
        W.append((W[ex['parent'][i]] if ex['parent'][i] >= 0 else np.eye(4)) @ local)
    return W


def write_glb(np, path, ex, jpeg):
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
    names = ex['names']
    nodes.append(dict(name='mercury', children=[i + 1 for i, p in enumerate(ex['parent']) if p < 0]))
    for i, nm in enumerate(names):
        p = np.array(ex['pivot'][i]); pp = np.array(ex['pivot'][ex['parent'][i]]) if ex['parent'][i] >= 0 else np.zeros(3)
        g = dict(name=nm, translation=[float(x) for x in p - pp])
        kids = [k + 1 for k, q in enumerate(ex['parent']) if q == i]
        if kids: g['children'] = kids
        part = ex['parts'].get(str(i))
        if part:
            pos = np.array(part['pos'], '<f4'); nrm = np.array(part['nrm'], '<f4'); uv = np.array(part['uv'], '<f4'); idx = np.array(part['idx'], '<u2').reshape(-1)
            prim = dict(attributes=dict(POSITION=acc(pos, 5126, 'VEC3', 34962, True), NORMAL=acc(nrm, 5126, 'VEC3', 34962), TEXCOORD_0=acc(uv, 5126, 'VEC2', 34962)),
                        indices=acc(idx, 5123, 'SCALAR', 34963), material=0)
            meshes.append(dict(name=nm, primitives=[prim])); g['mesh'] = len(meshes) - 1
        nodes.append(g)
    img = view(jpeg)
    gltf = dict(asset=dict(version='2.0', generator='MechDefense build_mech_assets'), scene=0, scenes=[dict(nodes=[0])], nodes=nodes, meshes=meshes,
                materials=[dict(name='mercury', pbrMetallicRoughness=dict(baseColorTexture=dict(index=0), metallicFactor=0, roughnessFactor=1), doubleSided=True)],
                textures=[dict(sampler=0, source=0)], images=[dict(bufferView=img, mimeType='image/jpeg', name='mercury')],
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


def main():
    import argparse, hashlib, io, shutil, tempfile
    import numpy as np
    ap = argparse.ArgumentParser(); ap.add_argument('--source', default=str(SOURCE)); ap.add_argument('--blender', default=str(BLENDER)); a = ap.parse_args()
    work = Path(tempfile.mkdtemp(prefix='mech_assets_'))
    r = subprocess.run([a.blender, '-b', a.source, '--python', __file__, '--', '--export', str(work)], capture_output=True, text=True, encoding='utf-8', errors='replace')
    if 'EXPORT_DONE' not in r.stdout: print(r.stdout[-3000:], r.stderr[-3000:]); sys.exit('Blender export failed')
    print([l for l in r.stdout.splitlines() if l.startswith('EXPORT_DONE')][0])
    ex = json.loads((work / 'export.json').read_text(encoding='utf-8')); data = np.fromfile(work / 'clips.bin', '<f4').reshape(-1, 8)
    # ---- self-check: re-pose from the written data with the engine's composition, compare with Blender
    report = []
    for c in ex['checks']:
        clip = next(x for x in ex['clips'] if x['name'] == c['clip']); W = engine_pose(np, ex, data, clip, c['sample'])
        ref = np.array(c['verts']); err = []
        for j, part in ex['parts'].items():
            pos = np.c_[np.array(part['pos']), np.ones(len(part['pos']))]; got = (W[int(j)] @ pos.T).T[:, :3]
            err.append(np.linalg.norm(got - ref[part['src']], axis=1))
        e = np.concatenate(err); report.append(dict(clip=c['clip'], frame=c['frame'], max_error_m=float(e.max()), p99_m=float(np.percentile(e, 99))))
    OUT.mkdir(parents=True, exist_ok=True)
    from PIL import Image   # the V8 texture as baked (sRGB PNG next to the .blend), embedded as JPEG
    im = Image.open(Path(a.source).parent / 'textures/mercury_V8_2048.png').convert('RGB'); buf = io.BytesIO(); im.save(buf, 'JPEG', quality=92, subsampling=0)
    write_glb(np, OUT / 'model.glb', ex, buf.getvalue())
    im.resize((512, 512), Image.LANCZOS).save(OUT / 'icon.png')
    shutil.copyfile(work / 'clips.bin', OUT / 'clips.bin')
    (OUT / 'Audio').mkdir(exist_ok=True)
    for i in SE_NAMES: shutil.copyfile(SE_DIR / f'{i:04d}.ogg', OUT / 'Audio' / f'se{i:02d}.ogg')
    tris = write_icon(np, OUT / 'icon.bin', ex)
    manifest = dict(joints=[dict(name=n, parent=p, pivot=[round(x, 5) for x in v]) for n, p, v in zip(ex['names'], ex['parent'], ex['pivot'])],
                    clips=ex['clips'], step=STEP, muzzles=ex['muzzles'], spanningTriangles=ex['spanning'], height=ex['contract']['height'],
                    source=ex['source'], check=report, sounds={f'se{i:02d}': n for i, n in SE_NAMES.items()})
    (OUT / 'mech.json').write_text(json.dumps(manifest, ensure_ascii=False, indent=1), encoding='utf-8')
    for f in sorted(p for p in OUT.iterdir() if p.is_file()): print(f'{f.name:10} {f.stat().st_size:>9,} B  sha256 {hashlib.sha256(f.read_bytes()).hexdigest()[:16]}')
    print('icon triangles', tris, '| parts', len(ex['parts']), '| clips', len(ex['clips']), '| spanning triangles', ex['spanning'])
    for c in report: print(f"check {c['clip']:20} frame {c['frame']:4}  max {c['max_error_m'] * 1000:7.3f} mm  p99 {c['p99_m'] * 1000:6.3f} mm")
    worst = max(c['p99_m'] for c in report); shutil.rmtree(work, ignore_errors=True)
    if worst > 1e-3: sys.exit('re-posed model does not match Blender (p99 > 1 mm)')


if __name__ == '__main__':
    if '--export' in sys.argv: blender_export(sys.argv[sys.argv.index('--export') + 1])
    else: main()
