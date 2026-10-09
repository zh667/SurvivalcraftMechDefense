"""Render recovered meshes, without modifying source assets. Run in Blender."""
import bpy, math
from pathlib import Path
from mathutils import Vector
root=Path(__file__).resolve().parents[1]
bpy.ops.object.select_all(action='SELECT');bpy.ops.object.delete(use_global=False)
for name,x in [('Mech',-5),('Enemy',0),('Turret',5)]:
    path=root/'Mod/Assets/Imported'/name
    if not (path/'model.obj').exists(): continue
    bpy.ops.wm.obj_import(filepath=str(path/'model.obj'))
    for obj in bpy.context.selected_objects:obj.location.x+=x
bpy.ops.mesh.primitive_plane_add(size=200,location=(0,0,-.02))
plane=bpy.context.object
mat=bpy.data.materials.new('ground');mat.diffuse_color=(.08,.1,.14,1);plane.data.materials.append(mat)
scene=bpy.context.scene;scene.world.color=(.35,.35,.35)
bpy.ops.object.light_add(type='AREA',location=(1,-9,10));bpy.context.object.data.energy=2500;bpy.context.object.data.shape='DISK';bpy.context.object.data.size=12
bpy.context.object.rotation_euler=(Vector((0,0,1))-bpy.context.object.location).to_track_quat('-Z','Y').to_euler()
bpy.ops.object.camera_add(location=(10,-19,9));camera=bpy.context.object
camera.rotation_euler=(Vector((0,0,2))-camera.location).to_track_quat('-Z','Y').to_euler();camera.data.type='ORTHO';camera.data.ortho_scale=17
scene.camera=camera;scene.render.engine='CYCLES';scene.cycles.samples=24
scene.render.resolution_x=1400;scene.render.resolution_y=800;scene.render.resolution_percentage=100
scene.render.filepath=str(root/'local/models-preview.png');bpy.ops.render.render(write_still=True)
