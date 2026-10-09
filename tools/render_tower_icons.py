"""Inventory icons: Blender renders each tower glTF with its atlas (nearest) -> Towers/<id>/icon.png.

blender -b --factory-startup --python tools/render_tower_icons.py -- [ids...]
Also renders the two legacy Destroy Gunners units from Imported/<Mech|Enemy>/model.obj when present.
"""
import bpy, sys, json, math
from pathlib import Path
from mathutils import Vector

ROOT = Path(__file__).resolve().parents[1]
TOWERS = ROOT / 'Mod/Assets/Towers'
ids = sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else []

def reset():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    s = bpy.context.scene; s.render.engine = 'BLENDER_EEVEE_NEXT'
    s.render.resolution_x = s.render.resolution_y = 160; s.render.film_transparent = True
    s.view_settings.view_transform = 'Standard'; s.eevee.taa_render_samples = 16
    w = bpy.data.worlds.new('w'); s.world = w; w.use_nodes = True
    bg = w.node_tree.nodes['Background']; bg.inputs[0].default_value = (1, 1, 1, 1); bg.inputs[1].default_value = .55
    sun = bpy.data.objects.new('sun', bpy.data.lights.new('sun', 'SUN')); s.collection.objects.link(sun)
    sun.data.energy = 2.6; sun.rotation_euler = (math.radians(50), 0, math.radians(-35))
    fill = bpy.data.objects.new('fill', bpy.data.lights.new('fill', 'SUN')); s.collection.objects.link(fill)
    fill.data.energy = 1.0; fill.rotation_euler = (math.radians(60), 0, math.radians(150))

def atlas_material(png):
    m = bpy.data.materials.new('atlas'); m.use_nodes = True; nt = m.node_tree
    tex = nt.nodes.new('ShaderNodeTexImage'); tex.image = bpy.data.images.load(str(png)); tex.interpolation = 'Closest'
    bsdf = nt.nodes['Principled BSDF']; bsdf.inputs['Roughness'].default_value = 1; bsdf.inputs['Specular IOR Level'].default_value = 0
    nt.links.new(tex.outputs['Color'], bsdf.inputs['Base Color']); return m

def frame_and_render(objs, out):
    s = bpy.context.scene; pts = [o.matrix_world @ Vector(c) for o in objs for c in o.bound_box]
    lo = Vector((min(p.x for p in pts), min(p.y for p in pts), min(p.z for p in pts))); hi = Vector((max(p.x for p in pts), max(p.y for p in pts), max(p.z for p in pts)))
    centre = (lo + hi) / 2; size = (hi - lo).length
    cam = bpy.data.objects.new('cam', bpy.data.cameras.new('cam')); s.collection.objects.link(cam); s.camera = cam
    cam.data.type = 'ORTHO'
    d = Vector((0.85, 1.0, 0.62)).normalized()      # front-right, from above (glTF -Z forward = Blender +Y)
    cam.location = centre + d * size * 2
    cam.rotation_euler = (centre - cam.location).to_track_quat('-Z', 'Y').to_euler()
    # fit: project points onto the camera plane
    R = cam.rotation_euler.to_matrix(); right = R @ Vector((1, 0, 0)); up = R @ Vector((0, 1, 0))
    ext = max(max(abs((p - centre).dot(right)) for p in pts), max(abs((p - centre).dot(up)) for p in pts))
    cam.data.ortho_scale = ext * 2 * 1.06
    s.render.filepath = str(out); bpy.ops.render.render(write_still=True)

def tower(tid):
    reset(); d = TOWERS / tid
    bpy.ops.import_scene.gltf(filepath=str(d / 'model.glb'))
    m = atlas_material(d / 'atlas.png'); meshes = [o for o in bpy.context.scene.objects if o.type == 'MESH']
    for o in meshes: o.data.materials.clear(); o.data.materials.append(m)
    frame_and_render(meshes, d / 'icon.png'); print('icon', tid, flush=True)

def legacy(name, out):
    obj = ROOT / f'Mod/Assets/Imported/{name}/model.obj'
    if not obj.exists(): print('no obj for', name); return
    reset(); bpy.ops.wm.obj_import(filepath=str(obj))
    m = atlas_material(ROOT / f'Mod/Assets/Imported/{name}/texture.png'); meshes = [o for o in bpy.context.scene.objects if o.type == 'MESH']
    for o in meshes: o.data.materials.clear(); o.data.materials.append(m)
    frame_and_render(meshes, out); print('icon', name, flush=True)

towers = json.loads((TOWERS / 'towers.json').read_text(encoding='utf-8'))['towers']
for t in towers:
    if not ids or t['id'] in ids: tower(t['id'])
if not ids or '_legacy' in ids:
    (TOWERS / '_legacy').mkdir(exist_ok=True)
    legacy('Mech', TOWERS / '_legacy/mech_icon.png'); legacy('Enemy', TOWERS / '_legacy/enemy_icon.png')
print('ICONS DONE', flush=True)
