"""See-through gap check: render every tower on the GPU and find background pixels enclosed by the model.

Each tower is posed (rest, yaw 45 degrees, both pitch limits) and drawn orthographically from 86 directions at
1024 px, with back faces culled (how 0.4.0 drew it) and two-sided (0.4.1). A gap is a 4-connected patch of
background that does not reach the image border and is at most ~4 px wide (about 1.5 cm at tower scale):
a slit or pinhole you can see the sky through. Wider enclosed openings (frames, between legs) are by design.

    python tools/check_tower_gaps.py [--package dist/X.scmod] [--out local/claude-work/gaps] [--ids a,b]
Without --package it reads Mod/Assets/Towers. Writes summary.json and, per tower, the worst view with gaps
marked red (and a 4x crop around the largest one).
"""
import argparse, io, json, math, struct, zipfile
from pathlib import Path
import numpy as np, moderngl
from PIL import Image
from scipy import ndimage

ROOT = Path(__file__).resolve().parents[1]
RES = 1024; MAX_HALF_WIDTH = 2.0

class Source:
    def __init__(self, package):
        self.z = zipfile.ZipFile(package) if package else None
    def read(self, rel):
        if self.z: return self.z.read('Assets/Towers/' + rel)
        return (ROOT / 'Mod/Assets/Towers' / rel).read_bytes()

def load_glb(data):
    jl, = struct.unpack_from('<I', data, 12); js = json.loads(data[20:20 + jl]); b0 = 20 + jl + 8; binb = data[b0:]
    def acc(i):
        a = js['accessors'][i]; v = js['bufferViews'][a['bufferView']]
        dt = {5126: '<f4', 5123: '<u2', 5125: '<u4'}[a['componentType']]; n = {'SCALAR': 1, 'VEC2': 2, 'VEC3': 3}[a['type']]
        return np.frombuffer(binb, dt, a['count'] * n, v.get('byteOffset', 0) + a.get('byteOffset', 0)).reshape(a['count'], n) if n > 1 else \
            np.frombuffer(binb, dt, a['count'], v.get('byteOffset', 0) + a.get('byteOffset', 0))
    meshes = {}
    for nd in js['nodes']:
        if 'mesh' in nd:
            pr = js['meshes'][nd['mesh']]['primitives'][0]
            meshes[nd['name']] = (acc(pr['attributes']['POSITION']).astype('f4'), acc(pr['attributes']['NORMAL']).astype('f4'),
                                  acc(pr['attributes']['TEXCOORD_0']).astype('f4'), acc(pr['indices']).astype('u4'))
    return meshes

def rot(axis, a):
    c, s = math.cos(a), math.sin(a); R = np.eye(4)
    if axis == 'x': R[1:3, 1:3] = [[c, -s], [s, c]]
    else: R[0, 0], R[0, 2], R[2, 0], R[2, 2] = c, s, -s, c
    return R

def posed(defn, meshes, yaw, pitch):
    """Triangle soup in tower space, same composition as MDTowerLibrary.Pose (local = R then T, parent last)."""
    W = []
    for i, nd in enumerate(defn['nodes']):
        T = np.eye(4); T[:3, 3] = nd['translation']
        R = rot('y', yaw) if nd['name'] == 'yaw' else rot('x', pitch) if nd['name'] == 'pitch' else np.eye(4)
        par = [j for j, m in enumerate(defn['nodes']) if m['name'] == nd['parent']]
        W.append((W[par[0]] if par else np.eye(4)) @ T @ R)
    P, N, U = [], [], []
    for i, nd in enumerate(defn['nodes']):
        if nd['name'] not in meshes: continue
        p, n, u, idx = meshes[nd['name']]
        P.append((np.c_[p, np.ones(len(p))] @ W[i].T)[:, :3][idx]); N.append((n @ W[i][:3, :3].T)[idx]); U.append(u[idx])
    return np.concatenate(P).astype('f4'), np.concatenate(N).astype('f4'), np.concatenate(U).astype('f4')

def views():
    out = [(0.0, 90.0), (0.0, -90.0)]
    for el in (-75, -45, -20, 0, 20, 45, 75):
        for az in range(0, 360, 30): out.append((float(az), float(el)))
    return out

def view_matrix(az, el, center, half):
    a, e = math.radians(az), math.radians(el)
    d = np.array([math.cos(e) * math.sin(a), math.sin(e), math.cos(e) * math.cos(a)])     # from centre towards the eye
    up = np.array([0, 1.0, 0]) if abs(d[1]) < .99 else np.array([0, 0, 1.0])
    z = d; x = np.cross(up, z); x /= np.linalg.norm(x); y = np.cross(z, x)
    V = np.eye(4); V[0, :3], V[1, :3], V[2, :3] = x, y, z; V[:3, 3] = -V[:3, :3] @ center
    O = np.diag([1 / half, 1 / half, -1 / (2 * half), 1.0]); O[2, 3] = 0       # orthographic, depth range [-2h, 2h] around centre
    return O @ V

VS = '''#version 330
uniform mat4 mvp; in vec3 pos; in vec3 nrm; in vec2 uv; out vec2 v_uv; out vec3 v_n;
void main(){ gl_Position = mvp * vec4(pos, 1.0); v_uv = uv; v_n = nrm; }'''
FS = '''#version 330
uniform sampler2D tex; in vec2 v_uv; in vec3 v_n; out vec4 color;
void main(){ vec4 t = texture(tex, v_uv); float l = 0.55 + 0.45 * abs(dot(normalize(v_n), normalize(vec3(0.4, 0.8, 0.45))));
  color = vec4(t.rgb * l, 1.0); }'''

def gaps(alpha):
    bg = alpha == 0
    lab, n = ndimage.label(bg)                     # 4-connected
    border = np.unique(np.r_[lab[0], lab[-1], lab[:, 0], lab[:, -1]])
    inner = np.setdiff1d(np.arange(1, n + 1), border)
    if not len(inner): return [], np.zeros_like(bg)
    edt = ndimage.distance_transform_edt(bg)
    width = ndimage.maximum(edt, lab, inner); area = ndimage.sum(bg, lab, inner)
    keep = inner[np.array(width) <= MAX_HALF_WIDTH]
    mask = np.isin(lab, keep)
    sl = ndimage.find_objects(lab)
    found = [dict(px=int(area[k]), w=float(width[k]), box=[sl[c - 1][1].start, sl[c - 1][0].start, sl[c - 1][1].stop, sl[c - 1][0].stop])
             for k, c in enumerate(inner) if width[k] <= MAX_HALF_WIDTH]
    return found, mask

def main():
    ap = argparse.ArgumentParser(); ap.add_argument('--package'); ap.add_argument('--out', default='local/claude-work/gaps'); ap.add_argument('--ids')
    a = ap.parse_args(); src = Source(a.package); out = ROOT / a.out; out.mkdir(parents=True, exist_ok=True)
    towers = json.loads(src.read('towers.json').decode('utf-8'))['towers']
    if a.ids: towers = [t for t in towers if t['id'] in a.ids.split(',')]
    ctx = moderngl.create_standalone_context(); prog = ctx.program(vertex_shader=VS, fragment_shader=FS)
    fbo = ctx.simple_framebuffer((RES, RES)); fbo.use()
    summary = {}
    for t in towers:
        meshes = load_glb(src.read(t['id'] + '/model.glb'))
        atlas = Image.open(io.BytesIO(src.read(t['id'] + '/atlas.png'))).convert('RGBA')
        tex = ctx.texture(atlas.size, 4, atlas.tobytes()); tex.filter = (moderngl.NEAREST, moderngl.NEAREST); tex.use(0)
        lo, hi = t.get('pitch', [0, 0]); poses = [('rest', 0, 0), ('yaw45', 45, 0)]
        if hi > lo: poses += [('pitch%+d' % lo, 0, lo), ('pitch%+d' % hi, 0, hi)]
        res = {}; worst = None
        for pname, yaw, pitch in poses:
            P, N, U = posed(t, meshes, math.radians(yaw), math.radians(pitch))
            vbo = ctx.buffer(np.c_[P, N, U].astype('f4').tobytes()); vao = ctx.vertex_array(prog, [(vbo, '3f 3f 2f', 'pos', 'nrm', 'uv')])
            center = (P.min(0) + P.max(0)) / 2; half = float(np.linalg.norm(P - center, axis=1).max()) * 1.03
            for cull in (True, False):
                tot = dict(gaps=0, px=0, views=0)
                for az, el in views():
                    prog['mvp'].write(view_matrix(az, el, center, half).T.astype('f4').tobytes())
                    fbo.clear(0, 0, 0, 0); ctx.enable(moderngl.DEPTH_TEST)
                    if cull: ctx.enable(moderngl.CULL_FACE); ctx.front_face = 'ccw'
                    else: ctx.disable(moderngl.CULL_FACE)
                    vao.render()
                    img = np.frombuffer(fbo.read(components=4), np.uint8).reshape(RES, RES, 4)[::-1]
                    found, mask = gaps(img[..., 3])
                    if found:
                        px = sum(g['px'] for g in found); tot['gaps'] += len(found); tot['px'] += px; tot['views'] += 1
                        if not cull and (worst is None or px > worst[0]): worst = (px, pname, az, el, img.copy(), mask, found)
                res[f'{pname}/{"culled" if cull else "two-sided"}'] = tot
            vao.release(); vbo.release()
        tex.release(); summary[t['id']] = res
        if worst:
            px, pname, az, el, img, mask, found = worst
            rgb = img[..., :3].copy(); rgb[img[..., 3] == 0] = (200, 220, 240)
            ring = ndimage.binary_dilation(mask, iterations=3); rgb[ring & ~mask] = (255, 0, 0); rgb[mask] = (255, 0, 255)
            Image.fromarray(rgb).save(out / f'{t["id"]}_worst.png')
            g = max(found, key=lambda g: g['px']); cx, cy = (g['box'][0] + g['box'][2]) // 2, (g['box'][1] + g['box'][3]) // 2
            x0, y0 = min(max(cx - 64, 0), RES - 128), min(max(cy - 64, 0), RES - 128)
            Image.fromarray(rgb[y0:y0 + 128, x0:x0 + 128]).resize((512, 512), Image.NEAREST).save(out / f'{t["id"]}_crop.png')
            res['worst'] = dict(pose=pname, az=az, el=el, px=px)
        print(t['id'], json.dumps(res), flush=True)
    (out / 'summary.json').write_text(json.dumps(summary, indent=1))

if __name__ == '__main__':
    main()
