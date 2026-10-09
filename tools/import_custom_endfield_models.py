"""Bind the approved local cube models to recovered Endfield VAT tracks.

The three gun towers' latest approved model version is v1, not batch2_v2
(which contains the other sixteen families). Never edits the authoring project.
Geometry conversion follows its Blender importer's Bedrock X mirror / ZYX
rotation and face UV convention, then scales model pixels by 1/16.
"""
from __future__ import annotations
import argparse
import hashlib
import importlib
import json
import math
from pathlib import Path
import shutil
import sys
from resolve_coplanar_faces import resolve as resolve_faces

import numpy as np
from scipy.spatial.transform import Rotation

ROOT=Path(__file__).resolve().parents[1]
ARCHIVE=ROOT/'local/endfield/tower-library/three-gun-towers'

def load(p):return json.loads(p.read_text(encoding='utf-8-sig'))
def sha(p):return hashlib.sha256(p.read_bytes()).hexdigest()
def save(p,d):
    p.parent.mkdir(parents=True,exist_ok=True)
    p.write_text(json.dumps(d,ensure_ascii=False,separators=(',',':')),encoding='utf8')
def bb(v):return np.array(v,dtype=float)*[-1,1,1]/16
def rotation(degrees):return Rotation.from_euler('xyz',np.array(degrees)*[-1,-1,1],degrees=True).as_matrix()

def decode_vat(variant):
    stem=f'T_battleturret+{variant}' if variant<3 else 'T_turret+3'
    meta_path=next((ARCHIVE/'converted/Texture2D').glob(stem+'*.raw.manifest.json'))
    meta=load(meta_path);p=meta_path.parent/meta['payload']['file']
    assert meta['format']=='RGBAHalf' and meta['width']==8 and meta['height']==44 and meta['mipCount']==1
    assert sha(p).upper()==meta['payload']['sha256']
    values=np.frombuffer(p.read_bytes(),dtype='<f2').reshape(44,8,4).astype(float)
    # Decompilation: texture V = 1 - (frame + .5)/height; X = (2*bone+.5)/width
    # then normalized quaternion and the next texel's XYZ translation / W scale.
    values=values[::-1]
    config_path=next((ARCHIVE/'json/MonoBehaviour').glob(f'battle_turret_{variant}_03_*.json'))
    config=load(config_path);tracks=[]
    for bone in (1,2,3):
        frames=[]
        for row in values:
            q=row[bone*2].copy();q/=np.linalg.norm(q);q[:2]*=-1
            t=row[bone*2+1].copy();t[2]*=-1
            frames.append([*q.tolist(),*t.tolist()])
        tracks.append(frames)
    return dict(fps=config['vatSetting']['frameRate'],tracks=tracks,
                clips=config['vatSetting']['clipDic']['_valueData'],
                sourceTexture=str(p.relative_to(ROOT)),sourceSha256=sha(p),
                sourceConfig=str(config_path.relative_to(ROOT)),sourceConfigSha256=sha(config_path),
                sampling='RGBAHalf; row=43-frame; col=2*bone quaternion, col+1 translationXYZ/scaleW',
                shaderEvidence='HGRP_Factory_LitFactoryBuilding: _FACTORY_VAT_SINGLEBONE, _343/_344/_354/_356/_381/_393/_405',
                sourceGame='Endfield'),config

def main(source):
    sys.dont_write_bytecode=True
    sys.path.insert(0,str(source/'v1/tools'))
    authored=importlib.import_module('turret_parts');bedrock=importlib.import_module('blocky')
    report=[]
    for variant in (1,2,3):
        key=f'battle_turret_{variant}';folder=source/'v1'/key
        geo_path=folder/f'{key}_V1.geo.json';texture_path=folder/f'{key}_V1.png'
        geo=load(geo_path)['minecraft:geometry'][0];bones={b['name']:b for b in geo['bones']}
        assert set(bones)=={'turret','base','yaw','pitch'}
        assert all(not b.get('rotation') for b in bones.values()),'Unexpected bone bind rotation'
        labels,muzzles,variant_config=authored.build(key)
        # The authoring script carries semantic cube labels, absent from Bedrock.
        # Prove the ordered geometry matches before using these for VAT assignment.
        original=[c for b in ['base','yaw','pitch'] for c in labels[b]]
        rebuilt,_=bedrock.to_bedrock(original,2.5)
        existing=[c for b in ['base','yaw','pitch'] for c in bones[b]['cubes']]
        assert len(rebuilt)==len(existing)
        for a,b in zip(rebuilt,existing):
            for k in ['origin','size','pivot','rotation']:
                assert np.allclose(a.get(k,[0,0,0]),b.get(k,[0,0,0]),atol=.00011), (key,k)
        target=ROOT/'Mod/Assets/Imported/Endfield'/key;target.mkdir(parents=True,exist_ok=True)
        snapshot=ROOT/'Mod/Assets/Models/Endfield'/key;snapshot.mkdir(parents=True,exist_ok=True)
        for p in [geo_path,texture_path,folder/'build_stats.json']:shutil.copyfile(p,snapshot/p.name)
        shutil.copyfile(texture_path,target/'texture.png')
        animation,config=decode_vat(variant)
        own_sockets=[bb(bones['pitch']['locators'][f'fire_{i}']).tolist() for i in (1,2)]
        source_sockets=[np.array([s['pos'][k] for k in 'xyz'])*[1,1,-1] for s in config['extendConfigs']]
        # firePos numbering changes sides between source variants. VAT track 2
        # belongs to negative X, track 3 positive X, as source UV3 groups show.
        source_sockets.sort(key=lambda p:p[0])
        assert own_sockets[0][0]<0<own_sockets[1][0]
        pitch=bb(bones['pitch']['pivot'])
        # Independent source VAT matrices include damage transforms already: the
        # barrel animation bones are siblings of the head body under aim_pitch.
        joints=[dict(name='base',parent=-1,pivot=bb(bones['base']['pivot']).tolist()),
                dict(name='yaw',parent=-1,pivot=bb(bones['yaw']['pivot']).tolist()),
                dict(name='aim_pitch',parent=1,pivot=pitch.tolist())]
        anchors=[pitch, np.array(own_sockets[0])-source_sockets[0],np.array(own_sockets[1])-source_sockets[1]]
        for i,anchor in enumerate(anchors):joints.append(dict(name=['head','barrel_left','barrel_right'][i],parent=2,pivot=anchor.tolist(),vatTrack=i))
        animation['retargetAnchors']=[a.tolist() for a in anchors]
        animation['retargetPolicy']='Source part-local matrices conjugated by authored pitch or source-to-authored muzzle offsets; each cube stays rigid.'
        vertices=[];indices=[];cube_bones=[]
        tw=geo['description']['texture_width'];th=geo['description']['texture_height']
        for group in ['base','yaw','pitch']:
            for cube,label in zip(bones[group]['cubes'],labels[group]):
                bone=0 if group=='base' else 1 if group=='yaw' else 3
                text=label['label']
                is_barrel=text.startswith(('barrel','muzzle')) or (variant==2 and text.startswith('shroud')) or (variant==3 and text=='brass ring')
                if group=='pitch' and is_barrel:
                    # Model coordinates use +Y to the left; BB mirroring maps it to -X.
                    bone=4 if label['center'][1]>0 else 5
                cube_bones.append(dict(group=group,label=text,bone=bone))
                origin=np.array(cube['origin']);size=np.array(cube['size']);inflate=cube.get('inflate',0)
                x0=-(origin[0]+size[0])-inflate;x1=-origin[0]+inflate
                y0=origin[1]-inflate;y1=origin[1]+size[1]+inflate
                z0=origin[2]-inflate;z1=origin[2]+size[2]+inflate
                pivot=np.array(cube.get('pivot',[0,0,0]))*[-1,1,1];rot=rotation(cube.get('rotation',[0,0,0]))
                faces={
                    'east':[(x1,y1,z1),(x1,y1,z0),(x1,y0,z1),(x1,y0,z0)],
                    'west':[(x0,y1,z0),(x0,y1,z1),(x0,y0,z0),(x0,y0,z1)],
                    'up':[(x0,y1,z0),(x1,y1,z0),(x0,y1,z1),(x1,y1,z1)],
                    'down':[(x0,y0,z1),(x1,y0,z1),(x0,y0,z0),(x1,y0,z0)],
                    'south':[(x0,y1,z1),(x1,y1,z1),(x0,y0,z1),(x1,y0,z1)],
                    'north':[(x1,y1,z0),(x0,y1,z0),(x1,y0,z0),(x0,y0,z0)]}
                for face,quad in faces.items():
                    if face not in cube['uv']:continue
                    f=cube['uv'][face];u,v=f['uv'];du,dv=f['uv_size'];rect=[u,v,u+du,v+dv]
                    if face in ('up','down'):rect=rect[2:]+rect[:2]
                    u0,v0,u1,v1=rect
                    uvs=[[u0/tw,v0/th],[u0/tw,v1/th],[u1/tw,v1/th],[u1/tw,v0/th]]
                    points=[(rot@(np.array(quad[i])-pivot)+pivot)/16 for i in [0,2,3,1]]
                    normal=np.cross(points[1]-points[0],points[2]-points[0]);normal/=np.linalg.norm(normal)
                    start=len(vertices)
                    for point,uv in zip(points,uvs):vertices.append(dict(p=point.tolist(),n=normal.tolist(),uv=uv,bone=bone))
                    indices.extend([start,start+1,start+2,start,start+2,start+3])
        source_corner_count=len(vertices)
        vertices,indices,cleanup=resolve_faces(vertices)
        model=dict(name=key,vertices=vertices,indices=indices,joints=joints,muzzles=own_sockets,muzzleBones=[4,5],coplanarCleanup=cleanup,
                   modelOrigin='local-authored-bedrock',textureOrigin='local-authored-pixel-atlas',pointSampling=True,
                   geometrySha256=sha(geo_path),textureSha256=sha(texture_path),sourceModel=str(geo_path),
                   sourceTexture=str(texture_path),sourceVersion='V1',animation=animation,
                   coordinatePolicy='Bedrock X mirror, Euler ZYX with X/Y rotation signs negated; 16px=1m; -Z forward',
                   provenanceBoundary='Authored model/atlas; original Endfield animation, VFX, projectiles and audio retained separately.')
        save(target/'model.json',model)
        # Keep editable OBJ preview consistent with what the engine draws.
        obj=['mtllib model.mtl','usemtl skin']
        obj+=['v '+' '.join(map(str,v['p'])) for v in vertices]
        obj+=['vt '+str(v['uv'][0])+' '+str(1-v['uv'][1]) for v in vertices]
        obj+=['f '+' '.join(f'{q+1}/{q+1}' for q in indices[i:i+3]) for i in range(0,len(indices),3)]
        (target/'model.obj').write_text('\n'.join(obj),encoding='ascii')
        (target/'model.mtl').write_text('newmtl skin\nKd 1 1 1\nmap_Kd texture.png\n',encoding='ascii')
        entry=dict(id=key,geometrySha256=sha(geo_path),textureSha256=sha(texture_path),sourceModel=str(geo_path),
                   cubes=len(existing),authoredCorners=source_corner_count,coplanarCleanup=cleanup,vertices=len(vertices),triangles=len(indices)//3,
                   muzzleBones=[4,5],vatHash=animation['sourceSha256'],vatFrames=44,sourceClips=animation['clips'],
                   boneCubeCounts={str(i):sum(c['bone']==i for c in cube_bones) for i in (0,1,3,4,5)})
        save(ROOT/'local/custom-model-integration'/key/'cube-bone-map.json',cube_bones)
        report.append(entry)
    save(ROOT/'docs/custom-model-provenance.json',dict(models=report,sourceRoot=str(source),
        versionEvidence='endfield-turret-model/反馈记录表.md: first batch V1; batch2_v2 excludes the three gun towers'))
    print(json.dumps(report,ensure_ascii=False,indent=2))

if __name__=='__main__':
    p=argparse.ArgumentParser();p.add_argument('--source',type=Path,default=Path('E:/projects/endfield-turret-model'))
    main(p.parse_args().source)
