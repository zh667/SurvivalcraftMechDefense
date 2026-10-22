"""Inventory icon pictures: every tower's FULL rest-pose model, seen the way an inventory slot sees a block.

python tools/render_tower_icons.py [ids...] [--towers DIR] [--sheet local/claude-work/r5/towers/icons_sheet.jpg] [--blender PATH]
  -> Mod/Assets/Towers/<id>/icon.png   256 x 256 RGBA, transparent background (default: every tower in towers.json)

Why: the old icon (icon.bin) kept only the 1,500 largest triangles, so small pieces (bevels, pads, thin parts) were
missing in the slot ("缺一块", e.g. 医疗塔). A picture of the whole model costs the slot one quad instead of 1,500
triangles transformed on the CPU every frame.

View = BlockIconWidget in DrawBlockMode.UI with the block's default IconViewOffset (1,1,1): orthographic, camera at
(1,1,1) looking at the origin, up +Y. In tower space (SC metres, X right, Y up, Z back, muzzle towards -Z) the
screen axes are right = (1,0,-1)/sqrt2 and up = (-1,2,-1)/sqrt6 (the sx/sy in MDTowerLibrary.IconMesh), so the
tower is seen from above, from its right-rear corner, exactly as the old mesh icon.
Framing = the old icon's: the picture is the icon widget's square (1.8 x 1.8 view units at size 1: CreateOrthographic
3.6 maps to twice the widget); the tower is centred on its projected bounding box and its longer projected side is
IconExtent 1.65 units = 91.7 % of the picture (234.7 of 256 px). Bounds come from every vertex of the model.
Shading = the old icon's: atlas texel x shade, shade 1.0 facing up (ny > .5), 0.55 facing down (ny < -.5), sides
0.72 + 0.14 nx - 0.08 nz; texels nearest (pixel look); two-sided like the placed towers (a back face is shaded with
its normal turned to the viewer). The engine multiplies texel and vertex colour as stored (no sRGB decoding), so the
render does the same: atlas read as non-colour data, emission = texel x shade, 'Raw' view transform, no dither.
Render: Blender (headless, Cycles, emission only, one centred sample per pixel) at 4x size, box-downsampled here with
alpha weighting; colours of fully transparent pixels are bled from their nearest opaque neighbour, so linear filtering
or premultiplying at load time does not draw dark fringes.

--sheet: also renders the old icon.bin (1,500 triangles, culled like the engine draws it, framed by its own vertices
like IconMesh's fit/center) and writes a contact sheet old | new per tower, plus summary JSON next to it with the
share of the new picture's pixels the old icon did not cover (same triangles subset rebuilt in the new frame).
"""
from __future__ import annotations
import json, math, os, struct, subprocess, sys, tempfile, time
from pathlib import Path

SX = (1 / math.sqrt(2), 0.0, -1 / math.sqrt(2))                     # screen right in tower space
SY = (-1 / math.sqrt(6), 2 / math.sqrt(6), -1 / math.sqrt(6))       # screen up
SZ = (1 / math.sqrt(3), 1 / math.sqrt(3), 1 / math.sqrt(3))         # towards the camera
ICON_EXTENT, WIDGET_UNITS = 1.65, 1.8                               # MDTowerLibrary.IconExtent; BlockIconWidget square
OUT_PX, SS = 256, 4
DEFAULT_BLENDER = 'E:/projects/tools/Blender/blender-4.2.9-windows-x64/blender.exe'


def shade_of(n):
    """The old icon's block-style shade for unit normal n (numpy array [..., 3])."""
    import numpy as np
    return np.where(n[..., 1] > .5, 1.0, np.where(n[..., 1] < -.5, .55, .72 + .14 * n[..., 0] - .08 * n[..., 2]))


# ---------------------------------------------------------------- geometry (plain Python + numpy; also used inside Blender)
def load_glb(path):
    import numpy as np
    data = Path(path).read_bytes()
    jl, = struct.unpack_from('<I', data, 12); js = json.loads(data[20:20 + jl]); binb = data[20 + jl + 8:]
    def acc(i):
        a = js['accessors'][i]; v = js['bufferViews'][a['bufferView']]
        dt = {5126: '<f4', 5123: '<u2', 5125: '<u4'}[a['componentType']]; n = {'SCALAR': 1, 'VEC2': 2, 'VEC3': 3}[a['type']]
        arr = np.frombuffer(binb, dt, a['count'] * n, v.get('byteOffset', 0) + a.get('byteOffset', 0))
        return arr.reshape(a['count'], n) if n > 1 else arr
    nodes = js['nodes']; world = {}
    def walk(i, t):
        t = t + np.array(nodes[i].get('translation', [0, 0, 0]), float); world[i] = t
        for c in nodes[i].get('children', []): walk(c, t)
    for r in js['scenes'][0]['nodes']: walk(r, np.zeros(3))
    P, N, U = [], [], []
    for i, nd in enumerate(nodes):                        # rest pose: nodes carry translations only
        if 'mesh' not in nd: continue
        for pr in js['meshes'][nd['mesh']]['primitives']:
            I = acc(pr['indices']).astype(np.int64).reshape(-1, 3)
            P.append(acc(pr['attributes']['POSITION']).astype(float)[I] + world[i])
            N.append(acc(pr['attributes']['NORMAL']).astype(float)[I][:, 0]); U.append(acc(pr['attributes']['TEXCOORD_0']).astype(float)[I])
    return np.concatenate(P), np.concatenate(N), np.concatenate(U)


def load_icon_bin(path):
    """icon.bin -> (P[n,3,3] in glTF CCW order, U[n,3,2], shade[n]) (file winding is the engine's clockwise front)."""
    import numpy as np
    b = Path(path).read_bytes(); nv, ni = struct.unpack_from('<ii', b, 0)
    V = np.frombuffer(b, '<f4', nv * 6, 8).reshape(nv, 6).astype(float); I = np.frombuffer(b, '<i4', ni, 8 + nv * 24).reshape(-1, 3)[:, [0, 2, 1]]
    return V[I, :3], V[I, 3:5], V[I, 5].mean(1)


def frame(P):
    """Projected bounds -> (centre x, centre y, picture side in view units)."""
    import numpy as np
    q = P.reshape(-1, 3); x = q @ np.array(SX); y = q @ np.array(SY)
    ext = max(x.max() - x.min(), y.max() - y.min())
    return (x.max() + x.min()) / 2, (y.max() + y.min()) / 2, ext * WIDGET_UNITS / ICON_EXTENT


def to_view(P, fr):
    """Tower space -> camera space of an orthographic camera at +Z looking down -Z (Blender's default orientation)."""
    import numpy as np
    cx, cy, _ = fr
    return np.stack([P @ np.array(SX) - cx, P @ np.array(SY) - cy, P @ np.array(SZ)], -1)


def full_model_job(glb, cull=False):
    """-> (view-space triangles, uv, per-triangle shade, frame) for a tower glb, two-sided shading towards the camera."""
    import numpy as np
    P, N, U = load_glb(glb)
    n = N / np.linalg.norm(N, axis=1, keepdims=True)
    back = n @ np.array(SZ) < 0
    sh = shade_of(np.where(back[:, None], -n, n))
    return P, U, sh, back


# ---------------------------------------------------------------- Blender side
def blender_main(jobfile):
    import bpy, numpy as np
    jobs = json.loads(Path(jobfile).read_text(encoding='utf-8'))
    sc = bpy.context.scene
    for o in list(bpy.data.objects): bpy.data.objects.remove(o, do_unlink=True)
    sc.render.engine = 'CYCLES'; sc.cycles.device = 'CPU'; sc.cycles.samples = 1; sc.cycles.use_adaptive_sampling = False
    sc.cycles.use_denoising = False; sc.cycles.pixel_filter_type = 'BOX'; sc.cycles.filter_width = 0.01
    sc.cycles.max_bounces = 0; sc.cycles.transparent_max_bounces = 0
    sc.render.film_transparent = True; sc.render.dither_intensity = 0.0
    try: sc.view_settings.view_transform = 'Raw'
    except TypeError as e: raise SystemExit(f'no Raw view transform: {e}')
    sc.view_settings.look = 'None'; sc.view_settings.exposure = 0.0; sc.view_settings.gamma = 1.0
    sc.render.image_settings.file_format = 'PNG'; sc.render.image_settings.color_mode = 'RGBA'; sc.render.image_settings.color_depth = '8'
    sc.render.resolution_percentage = 100
    if sc.world is None: sc.world = bpy.data.worlds.new('w')
    sc.world.use_nodes = False; sc.world.color = (0, 0, 0)
    cam = bpy.data.cameras.new('icon_cam'); cam.type = 'ORTHO'; cam.clip_start = 0.01; cam.clip_end = 400
    co = bpy.data.objects.new('icon_cam', cam); sc.collection.objects.link(co); sc.camera = co
    co.location = (0, 0, 100); co.rotation_euler = (0, 0, 0)
    for job in jobs:
        t0 = time.time()
        T = np.load(job['npz'])
        P, U, sh = T['P'], T['U'], T['shade']
        n = len(P)
        me = bpy.data.meshes.new('icon')
        me.vertices.add(n * 3); me.vertices.foreach_set('co', P.reshape(-1).astype(np.float32))
        me.loops.add(n * 3); me.loops.foreach_set('vertex_index', np.arange(n * 3, dtype=np.int32))
        me.polygons.add(n); me.polygons.foreach_set('loop_start', np.arange(0, n * 3, 3, dtype=np.int32))
        me.polygons.foreach_set('loop_total', np.full(n, 3, np.int32))
        me.update(calc_edges=True)
        uvl = me.uv_layers.new(name='UVMap'); uv = U.reshape(-1, 2).copy(); uv[:, 1] = 1.0 - uv[:, 1]      # glTF top-left -> Blender bottom-left
        uvl.data.foreach_set('uv', uv.reshape(-1).astype(np.float32))
        at = me.attributes.new('shade', 'FLOAT', 'FACE'); at.data.foreach_set('value', sh.astype(np.float32))
        img = bpy.data.images.load(job['atlas'], check_existing=False); img.colorspace_settings.name = 'Non-Color'; img.alpha_mode = 'CHANNEL_PACKED'
        mat = bpy.data.materials.new('icon'); mat.use_nodes = True; nt = mat.node_tree
        for nd in list(nt.nodes): nt.nodes.remove(nd)
        tex = nt.nodes.new('ShaderNodeTexImage'); tex.image = img; tex.interpolation = 'Closest'; tex.extension = 'EXTEND'
        att = nt.nodes.new('ShaderNodeAttribute'); att.attribute_name = 'shade'; att.attribute_type = 'GEOMETRY'
        em = nt.nodes.new('ShaderNodeEmission'); out = nt.nodes.new('ShaderNodeOutputMaterial')
        nt.links.new(tex.outputs['Color'], em.inputs['Color']); nt.links.new(att.outputs['Fac'], em.inputs['Strength'])
        nt.links.new(em.outputs['Emission'], out.inputs['Surface'])
        me.materials.append(mat)
        ob = bpy.data.objects.new('icon', me); sc.collection.objects.link(ob)
        cam.ortho_scale = float(job['side']); sc.render.resolution_x = sc.render.resolution_y = int(job['res'])
        sc.render.filepath = job['out']; bpy.ops.render.render(write_still=True)
        bpy.data.objects.remove(ob, do_unlink=True); bpy.data.meshes.remove(me); bpy.data.materials.remove(mat); bpy.data.images.remove(img)
        print('rendered', job['name'], n, 'tris', f'{time.time() - t0:.1f}s', flush=True)


# ---------------------------------------------------------------- outside Blender
def downsample(png_hi, ss):
    """4x render -> picture: alpha-weighted box filter, then bleed colours into fully transparent pixels."""
    import numpy as np
    from PIL import Image
    from scipy import ndimage
    a = np.asarray(Image.open(png_hi).convert('RGBA')).astype(float) / 255
    h, w = a.shape[0] // ss, a.shape[1] // ss
    blk = a.reshape(h, ss, w, ss, 4); al = blk[..., 3].mean((1, 3)); prem = (blk[..., :3] * blk[..., 3:4]).sum((1, 3)) / (ss * ss)
    rgb = np.where(al[..., None] > 0, prem / np.maximum(al[..., None], 1e-9), 0)
    empty = al <= 0
    if empty.any() and (~empty).any():
        _, (iy, ix) = ndimage.distance_transform_edt(empty, return_indices=True); rgb = rgb[iy, ix]
    out = np.dstack([rgb, al]); return (np.clip(out, 0, 1) * 255 + .5).astype(np.uint8)


def main():
    import numpy as np
    from PIL import Image, ImageDraw, ImageFont
    args = sys.argv[1:]; sheet = None; blender = DEFAULT_BLENDER; ids = []; towers_dir = None
    i = 0
    while i < len(args):
        if args[i] == '--sheet': sheet = args[i + 1]; i += 2
        elif args[i] == '--blender': blender = args[i + 1]; i += 2
        elif args[i] == '--towers': towers_dir = Path(args[i + 1]); i += 2   # another Towers folder (tools/stage_tower_preview.py)
        else: ids.append(args[i]); i += 1
    root = Path(__file__).resolve().parents[1]; towers_dir = towers_dir or root / 'Mod/Assets/Towers'
    towers = json.loads((towers_dir / 'towers.json').read_text(encoding='utf-8'))['towers']
    if ids: towers = [t for t in towers if t['id'] in ids]
    tmp = Path(tempfile.mkdtemp(prefix='md_icons_')); jobs = []; meta = {}
    for t in towers:
        tid = t['id']; d = towers_dir / tid
        P, U, sh, back = full_model_job(d / 'model.glb'); fr = frame(P)
        np.savez(tmp / f'{tid}_new.npz', P=to_view(P, fr), U=U, shade=sh)
        jobs.append(dict(name=tid + ' new', npz=str(tmp / f'{tid}_new.npz'), atlas=str(d / 'atlas.png'), side=fr[2], res=OUT_PX * SS, out=str(tmp / f'{tid}_new.png')))
        meta[tid] = dict(name=t.get('name', tid), triangles=int(len(P)), frame=[round(float(x), 5) for x in fr])
        if sheet:
            # what players saw: icon.bin, culled (engine draws its icon single-sided), framed by its own vertices (IconMesh fit/center)
            Pi, Ui, shi = load_icon_bin(d / 'icon.bin')
            ni = np.cross(Pi[:, 1] - Pi[:, 0], Pi[:, 2] - Pi[:, 0]); front = ni @ np.array(SZ) > 0
            fri = frame(Pi)
            np.savez(tmp / f'{tid}_old.npz', P=to_view(Pi[front], fri), U=Ui[front], shade=shi[front])
            jobs.append(dict(name=tid + ' old', npz=str(tmp / f'{tid}_old.npz'), atlas=str(d / 'atlas.png'), side=fri[2], res=OUT_PX * SS, out=str(tmp / f'{tid}_old.png')))
            # the same 1,500-triangle choice (largest areas, as write_icon) in the NEW frame: what exactly is missing
            area = np.linalg.norm(np.cross(P[:, 1] - P[:, 0], P[:, 2] - P[:, 0]), axis=1) / 2; keep = np.argsort(-area)[:1500]
            keep = keep[~back[keep]]
            np.savez(tmp / f'{tid}_sub.npz', P=to_view(P[keep], fr), U=U[keep], shade=sh[keep])
            jobs.append(dict(name=tid + ' sub', npz=str(tmp / f'{tid}_sub.npz'), atlas=str(d / 'atlas.png'), side=fr[2], res=OUT_PX * SS, out=str(tmp / f'{tid}_sub.png')))
    (tmp / 'jobs.json').write_text(json.dumps(jobs), encoding='utf-8')
    r = subprocess.run([blender, '-b', '--factory-startup', '--python', str(Path(__file__).resolve()), '--', str(tmp / 'jobs.json')],
                       capture_output=True, text=True, creationflags=0x08000000 if os.name == 'nt' else 0)
    if r.returncode != 0 or not all(Path(j['out']).exists() for j in jobs):
        print(r.stdout[-3000:], r.stderr[-3000:]); raise SystemExit('blender failed')
    for line in r.stdout.splitlines():
        if line.startswith('rendered'): print(line)
    for t in towers:
        tid = t['id']; pic = downsample(tmp / f'{tid}_new.png', SS)
        Image.fromarray(pic, 'RGBA').save(towers_dir / tid / 'icon.png', optimize=True)
        ys, xs = np.nonzero(pic[..., 3] > 0)
        meta[tid]['icon_png'] = str(towers_dir / tid / 'icon.png'); meta[tid]['coverage_px'] = int((pic[..., 3] > 127).sum())
        meta[tid]['alpha_bbox'] = [int(xs.min()), int(ys.min()), int(xs.max()), int(ys.max())]     # tower must stay inside the picture
    if sheet:
        make_sheet(towers, tmp, Path(sheet) if Path(sheet).is_absolute() else root / sheet, meta)
    import shutil; shutil.rmtree(tmp, ignore_errors=True)
    print(json.dumps(meta, ensure_ascii=False, indent=1))


def make_sheet(towers, tmp, out, meta):
    import numpy as np
    from PIL import Image, ImageDraw, ImageFont
    cell = 200; pad = 10; head = 26; cols = 4
    try: font = ImageFont.truetype('C:/Windows/Fonts/msyh.ttc', 16); big = ImageFont.truetype('C:/Windows/Fonts/msyh.ttc', 22)
    except OSError: font = big = ImageFont.load_default()
    rows = math.ceil(len(towers) / cols); pw = 2 * cell + 3 * pad; ph = cell + head + 2 * pad
    zoom = [t for t in towers if t['id'] == 'battle_medic_1']; Z = 320
    zh = (Z + head + 2 * pad) if zoom else 0
    W = cols * pw + pad; H = 50 + rows * ph + zh + pad
    sheet = Image.new('RGB', (W, H), (34, 34, 38)); dr = ImageDraw.Draw(sheet)
    dr.text((pad, 12), '物品栏图标：左 = 旧图标（只留 1,500 个最大三角形，游戏里的样子）　右 = 新图片（整座模型预先渲染）　红色 = 旧图标缺掉的部分', font=big, fill=(235, 235, 235))
    slot = (78, 78, 82)
    def put(img, x, y, size):
        bg = Image.new('RGBA', (size, size), slot + (255,)); im = Image.open(img).convert('RGBA').resize((size, size), Image.LANCZOS)
        bg.alpha_composite(im); sheet.paste(bg.convert('RGB'), (x, y))
    for k, t in enumerate(towers):
        tid = t['id']; x0 = pad + (k % cols) * pw; y0 = 50 + (k // cols) * ph
        sub = downsample(tmp / f'{tid}_sub.png', SS); new = downsample(tmp / f'{tid}_new.png', SS)
        miss = (new[..., 3] > 127) & (sub[..., 3] <= 127); meta[tid]['old_missing_share'] = round(float(miss.sum() / max(1, (new[..., 3] > 127).sum())), 4)
        dr.text((x0, y0), f"{t.get('name', tid)}  缺 {100 * meta[tid]['old_missing_share']:.1f}%", font=font, fill=(230, 230, 230))
        old = downsample(tmp / f'{tid}_old.png', SS); Image.fromarray(old, 'RGBA').save(tmp / f'{tid}_old_s.png')
        put(tmp / f'{tid}_old_s.png', x0, y0 + head, cell)
        # new picture with the old icon's gaps tinted red
        tint = new.copy(); tint[miss, :3] = (0.45 * tint[miss, :3] + 0.55 * np.array([255, 30, 30])).astype(np.uint8)
        Image.fromarray(tint, 'RGBA').save(tmp / f'{tid}_tint.png'); put(tmp / f'{tid}_tint.png', x0 + cell + pad, y0 + head, cell)
    if zoom:
        y0 = 50 + rows * ph; tid = zoom[0]['id']
        dr.text((pad, y0), f"{zoom[0]['name']} 放大：左 旧图标 / 中 新图片 / 右 新图片（红 = 旧图标缺的）", font=font, fill=(230, 230, 230))
        Image.fromarray(downsample(tmp / f'{tid}_new.png', SS), 'RGBA').save(tmp / f'{tid}_new_s.png')
        for j, f in enumerate((f'{tid}_old_s.png', f'{tid}_new_s.png', f'{tid}_tint.png')):
            put(tmp / f, pad + j * (Z + pad), y0 + head, Z)
    out.parent.mkdir(parents=True, exist_ok=True); sheet.save(out, quality=90)
    (out.with_suffix('.json')).write_text(json.dumps(meta, ensure_ascii=False, indent=1), encoding='utf-8')


if __name__ == '__main__':
    try:
        import bpy  # noqa: F401  (inside Blender)
        blender_main(sys.argv[sys.argv.index('--') + 1])
    except ImportError:
        main()
