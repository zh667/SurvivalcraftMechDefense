"""Blender: source bind-pose GLBs plus labelled inspection renders.

Native geometry, per-vertex single-joint bindings, textures and vertex modulation are kept.
No animation is fabricated. No SC asset folder is written.
"""
import bpy
import json, math, sys
from pathlib import Path
from mathutils import Vector

ROOT=Path(__file__).resolve().parents[1]
OUT=ROOT/'local/dguns/unit-library'
if '--library' in sys.argv:
    OUT=Path(sys.argv[sys.argv.index('--library')+1]).resolve()
catalog=json.loads((OUT/'units.json').read_text(encoding='utf-8'))
tables=json.loads((OUT/'tables.json').read_text(encoding='utf-8'))
bpy.ops.object.select_all(action='SELECT');bpy.ops.object.delete(use_global=False)
scene=bpy.context.scene
scene.render.engine='CYCLES';scene.cycles.samples=12
scene.cycles.use_denoising=True
scene.render.resolution_x=420;scene.render.resolution_y=420;scene.render.resolution_percentage=100
scene.render.image_settings.file_format='PNG'
scene.world.use_nodes=True
scene.world.node_tree.nodes['Background'].inputs['Color'].default_value=(.22,.26,.32,1)
scene.world.node_tree.nodes['Background'].inputs['Strength'].default_value=.6
scene.view_settings.view_transform='Standard'
scene.render.film_transparent=False
bpy.ops.object.camera_add();camera=bpy.context.object;camera.data.type='ORTHO';scene.camera=camera
bpy.ops.object.light_add(type='AREA',location=(4,-6,10));key=bpy.context.object;key.data.energy=1800;key.data.size=7
bpy.ops.object.light_add(type='AREA',location=(-5,3,6));fill=bpy.context.object;fill.data.energy=1000;fill.data.size=6

cache={};imgcache={};records=[]
def build(view):
    mid,section=view['modelId'],view['section']
    if (mid,section) not in cache:
        cache[mid,section]=json.loads((OUT/f'models/{mid:04}/section-{section}/model.json').read_text(encoding='utf-8'))
    model=cache[mid,section];verts=model['vertices'];materials={}
    def material(slot):
        if slot in materials:return materials[slot]
        mat=bpy.data.materials.new('source_slot_'+str(slot));mat.use_nodes=True
        nodes=mat.node_tree.nodes;links=mat.node_tree.links;bsdf=next(n for n in nodes if n.type=='BSDF_PRINCIPLED')
        bsdf.inputs['Roughness'].default_value=.85
        bsdf.inputs['Specular IOR Level'].default_value=.15
        vc=nodes.new('ShaderNodeVertexColor');vc.layer_name='SourceColor'
        if slot>=0 and slot<len(view['textures']) and view['textures'][slot]>0:
            texture=view['textures'][slot];path=OUT/f'textures/{texture:04}.0.png'
            if texture not in imgcache:imgcache[texture]=bpy.data.images.load(str(path),check_existing=True)
            tex=nodes.new('ShaderNodeTexImage');tex.image=imgcache[texture];tex.interpolation='Closest'
            mul=nodes.new('ShaderNodeMixRGB');mul.blend_type='MULTIPLY';mul.inputs[0].default_value=1
            links.new(tex.outputs['Color'],mul.inputs[1]);links.new(vc.outputs['Color'],mul.inputs[2]);links.new(mul.outputs[0],bsdf.inputs['Base Color'])
            links.new(tex.outputs['Alpha'],bsdf.inputs['Alpha']);mat.surface_render_method='DITHERED'
        else:links.new(vc.outputs['Color'],bsdf.inputs['Base Color'])
        materials[slot]=mat;return mat
    root=bpy.data.objects.new(view['key'],None);scene.collection.objects.link(root)
    arm=bpy.data.armatures.new('source_joints');rig=bpy.data.objects.new('source_skeleton',arm);scene.collection.objects.link(rig);rig.parent=root
    bpy.context.view_layer.objects.active=rig;rig.select_set(True);bpy.ops.object.mode_set(mode='EDIT')
    bones=[]
    for j in model['joints']:
        bone=arm.edit_bones.new(f"{j['index']:02}_{j['name']}");bone.head=j['pivot'];bone.tail=Vector(j['pivot'])+Vector((0,0,.05));bones.append(bone)
    for j,bone in zip(model['joints'],bones):
        if j['parent']>=0:bone.parent=bones[j['parent']]
    bpy.ops.object.mode_set(mode='OBJECT')
    triangles=[tuple(model['indices'][i:i+3]) for i in range(0,len(model['indices']),3)]
    mesh=bpy.data.meshes.new('source_mesh');mesh.from_pydata([v['p'] for v in verts],[],triangles);mesh.update()
    obj=bpy.data.objects.new('source_mesh',mesh);scene.collection.objects.link(obj);obj.parent=rig
    modifier=obj.modifiers.new('source_skin','ARMATURE');modifier.object=rig
    for j in model['joints']:
        group=obj.vertex_groups.new(name=f"{j['index']:02}_{j['name']}")
        group.add([i for i,v in enumerate(verts) if v['bone']==j['index']],1,'REPLACE')
    mesh.uv_layers.new(name='UVMap');mesh.color_attributes.new(name='SourceColor',type='FLOAT_COLOR',domain='CORNER')
    # Adding a mesh attribute can invalidate previously held RNA layer handles.
    uv=mesh.uv_layers['UVMap'];col=mesh.color_attributes['SourceColor']
    mesh.color_attributes.active_color=col
    slots=[f['textureSlot'] for f in model['faces']];smap={s:i for i,s in enumerate(sorted(set(slots)))}
    for s in sorted(smap):mesh.materials.append(material(s))
    for poly in mesh.polygons:
        poly.material_index=smap[slots[poly.index]]
        for li in poly.loop_indices:
            v=verts[mesh.loops[li].vertex_index];uv.data[li].uv=(v['uv'][0],1-v['uv'][1]);col.data[li].color=tuple(c/255 for c in v['color'])+(1,)
    mesh.normals_split_custom_set_from_vertices([v['n'] for v in verts])
    objects=[root,rig,obj]
    return model,objects

views=[]
# Every kind/shape has a reference GLB; player names also get all 4 validated paint slots.
for v in catalog['views']:views.append((v,False,'kind'))
for v in catalog['playerViews']:views.append((v,v['skin']==0,'player'))
# Render each RD type once, plus alternate shapes actually referenced by enemy spawn.
enemy_pairs={(e['kind'],e['meshSection']) for e in tables['enemies']}
for i,(v,render,group) in enumerate(views):
    if v.get('previewGroup'):
        views[i]=(v,True,v['previewGroup'])
        continue
    if group=='kind' and (v['kind'],v['section']) in enemy_pairs:views[i]=(v,True,'enemy')
    elif group=='kind' and v['kind'] is None:views[i]=(v,True,'unbound')
only=sys.argv[sys.argv.index('--only')+1] if '--only' in sys.argv else None
if only:views=[x for x in views if x[0]['key']==only]
for index,(view,render,group) in enumerate(views):
    existing_glb=OUT/'views'/view['key']/'model.glb'
    existing_preview=OUT/'preview'/f"{view['key']}.png"
    if '--missing' in sys.argv and existing_glb.exists() and (not render or existing_preview.exists()):
        m=json.loads((OUT/f"models/{view['modelId']:04}/section-{view['section']}/model.json").read_text(encoding='utf-8'))
        row=dict(key=view['key'],group=group,glb=existing_glb.relative_to(OUT).as_posix(),vertices=len(m['vertices']),triangles=len(m['indices'])//3)
        if render:row['preview']=existing_preview.relative_to(OUT).as_posix()
        records.append(row);continue
    model,objects=build(view)
    bpy.ops.object.select_all(action='DESELECT')
    for obj in objects:obj.select_set(True)
    bpy.context.view_layer.objects.active=objects[0]
    target=OUT/'views'/view['key']/'model.glb'
    bpy.ops.export_scene.gltf(filepath=str(target),export_format='GLB',use_selection=True,export_animations=False,export_extras=True)
    row=dict(key=view['key'],group=group,glb=target.relative_to(OUT).as_posix(),vertices=len(model['vertices']),triangles=len(model['indices'])//3)
    if render:
        lo=Vector(model['bounds'][0]);hi=Vector(model['bounds'][1]);center=(lo+hi)*.5
        size=max(hi-lo);direction=Vector((1,-1.6,.85)).normalized()
        camera.location=center+direction*size*3;camera.rotation_euler=(center-camera.location).to_track_quat('-Z','Y').to_euler()
        camera.data.ortho_scale=size*1.5;camera.data.clip_end=max(1000,size*20)
        for light,delta in [(key,Vector((1,-1.5,2))),(fill,Vector((-1,1,1)))]:
            light.location=center+delta*size;light.rotation_euler=(center-light.location).to_track_quat('-Z','Y').to_euler()
            light.data.size=size*1.5;light.data.energy=(150 if light==key else 80)*size*size
        shot=OUT/'preview'/f"{view['key']}.png";shot.parent.mkdir(exist_ok=True)
        scene.render.filepath=str(shot);bpy.ops.render.render(write_still=True);row['preview']=shot.relative_to(OUT).as_posix()
    records.append(row)
    for obj in objects:bpy.data.objects.remove(obj,do_unlink=True)
    for mesh in list(bpy.data.meshes):
        if mesh.users==0:bpy.data.meshes.remove(mesh)
    for mat in list(bpy.data.materials):
        if mat.users==0:bpy.data.materials.remove(mat)
    print('DGUNS',index+1,len(views),view['key'],flush=True)
(OUT/('render-export.json' if not only else 'render-probe.json')).write_text(json.dumps(dict(stage='source single-weight bind-pose exports; not original animation playback',outputs=records),indent=2),encoding='utf-8')
