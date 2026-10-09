"""Independent converter cross-check against the approved authored .blend scenes.
Run with Blender -b --python tools/verify_custom_models_blender.py.
"""
import bpy
import json
from pathlib import Path
import numpy as np
from collections import Counter

ROOT=Path(__file__).resolve().parents[1]
report=[]
for variant in (1,2,3):
    key=f'battle_turret_{variant}'
    runtime=json.loads((ROOT/f'Mod/Assets/Imported/Endfield/{key}/model.json').read_text(encoding='utf8'))
    source=Path(runtime['sourceModel']).with_suffix('').with_suffix('.blend')
    # _V1.geo.json -> _V1.blend
    bpy.ops.wm.open_mainfile(filepath=str(source))
    expected=[]
    for group in ['base','yaw','pitch']:
        ob=bpy.data.objects[group];m=np.array(ob.matrix_world)
        mesh=ob.data
        for polygon in mesh.polygons:
            for li in polygon.loop_indices:
                point=np.array([*mesh.vertices[mesh.loops[li].vertex_index].co,1.])@m.T
                uv=mesh.uv_layers.active.data[li].uv
                # Blender Z-up -> engine Y-up; V convention is top-origin in Engine.
                expected.append((point[0],point[2],-point[1],uv[0],1-uv[1]))
    actual=[(*v['p'],*v['uv']) for v in runtime['vertices']]
    def quantized(rows):return Counter(tuple(round(float(x),4) for x in row) for row in rows)
    # Floating point transforms can straddle a quantization boundary. Compare sorted
    # corner+UV vectors at 2e-5 tolerance after sorting with rounded matching keys.
    check=len(actual)==len(expected)
    exp=quantized(expected);act=quantized(actual)
    # Every face was separately authored; test the full count, not just a bbox.
    if exp!=act:
        a=np.array(actual);e=np.array(expected)
        from mathutils.kdtree import KDTree
        tree=KDTree(len(a))
        for i,p in enumerate(a):tree.insert(p[:3],i)
        tree.balance();mismatch=0
        for row in e:
            matches=tree.find_range(row[:3],.00003)
            if not any(np.max(np.abs(a[i,3:]-row[3:]))<.00003 for _,i,_ in matches):mismatch+=1
        check=check and mismatch==0
    else:mismatch=0
    if not check:raise AssertionError((key,len(actual),len(expected),mismatch))
    report.append(dict(id=key,passed=True,faceCorners=len(expected),unmatchedCorners=mismatch,source=str(source)))
path=ROOT/'local/custom-model-integration/blender-cross-check.json'
path.parent.mkdir(parents=True,exist_ok=True);path.write_text(json.dumps(report,indent=2),encoding='utf8')
print('BLENDER_CROSS_CHECK',json.dumps(report))
