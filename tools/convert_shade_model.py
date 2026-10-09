"""Bounded PSCf rigid-mech conversion; derived from the arm64 native reader.

Evidence: local/dguns/native/0x418a8.{c,asm} (packet construction),
0x5ef3c/0x5f1fc (joint tracks), 0x77b3c (kind table), APK hash in manifest.
Exports original geometry/UVs and bind joints; runtime gait is an adaptation.
"""
import argparse
from collections import Counter
import hashlib
import json
from pathlib import Path
import shutil
import struct

p=argparse.ArgumentParser();p.add_argument('--id',type=int,required=True)
p.add_argument('--texture',type=int,required=True);p.add_argument('--name',required=True)
p.add_argument('--height',type=float,required=True);a=p.parse_args()
root=Path(__file__).resolve().parents[1]
src=root/'local/dguns/assets/grp.bin'/f'{a.id:04}.bin';b=src.read_bytes()
assert b[:4]==b'PSCf'
def read(fmt,off):
    if off<0 or off+struct.calcsize(fmt)>len(b):raise ValueError(f'Outside PSC: {off}')
    return struct.unpack_from(fmt,b,off)
def ptr(off):return read('<Q',off)[0]
count=b[4];lod=ptr(120);table=lod+read('<I',lod+20)[0]
geometry=ptr(104);vertex,weight,polygons,colors,materials,normals=read('<6I',lod+24)
vertex+=geometry;polygons+=geometry;materials+=geometry;normals+=geometry
names=ptr(96)
joints=[]
for n in range(count):
    off=table+24*n; row=read('<12H',off)
    nameptr=ptr(names+8*n)
    name=b[nameptr:].split(b'\0')[0].decode('cp932','replace')
    pivot=read('<3f',read('<I',off+12)[0])
    parent=b[off+10]
    joints.append(dict(name=name,parent=parent if parent!=n and parent<count else -1,pivot=list(pivot)))
polygon_count=read('<H',lod+2)[0]
vertices=[];triangles=[];flags=Counter()
for n in range(polygon_count):
    row=read('<16H',polygons+32*n);flags[row[4]]+=1
    if row[4]&0x20:continue
    if row[4]&0x800:raise ValueError('Weighted mesh is unsupported by rigid converter')
    if row[7]>=0x8000:raise ValueError('Untextured polygon requires color handling')
    mat=materials+40*row[7]
    uv=read('<8f',mat);mat_flags=read('<I',mat+32)[0]
    if mat_flags&7:raise ValueError('Sample needs additional texture slot')
    nverts=3 if row[2]==row[3] else 4
    start=len(vertices)
    for k in range(nverts):
        vo=vertex+16*row[k];bone=b[vo+1]
        if bone>=count:raise ValueError('Invalid joint')
        xyz=read('<3f',vo+4);pivot=joints[bone]['pivot']
        # Z-up source -> Y-up game, preserve handedness.
        world=[xyz[0]+pivot[0],xyz[2]+pivot[2],-(xyz[1]+pivot[1])]
        no=normals+12*row[11+k];normal=read('<3f',no)
        vertices.append(dict(p=world,n=[normal[0],normal[2],-normal[1]],uv=[uv[2*k],uv[2*k+1]],bone=bone))
    triangles.extend([start,start+1,start+2])
    if nverts==4:triangles.extend([start,start+2,start+3])
min_y=min(v['p'][1] for v in vertices);max_y=max(v['p'][1] for v in vertices)
scale=a.height/(max_y-min_y)
for v in vertices:
    v['p']=[v['p'][0]*scale,(v['p'][1]-min_y)*scale,v['p'][2]*scale]
for j in joints:
    x,y,z=j['pivot'];j['pivot']=[x*scale,(z-min_y)*scale,-y*scale]
out=root/'Mod/Assets/Imported'/a.name;out.mkdir(parents=True,exist_ok=True)
model=dict(name=a.name,sourceId=a.id,textureId=a.texture,sourceSha256=hashlib.sha256(b).hexdigest(),
           joints=joints,vertices=vertices,indices=triangles,adaptedMotion=True)
(out/'model.json').write_text(json.dumps(model,ensure_ascii=True,separators=(',',':')),encoding='utf-8')
shutil.copyfile(root/'local/dguns/assets/grp.bin'/f'{a.texture:04}.png',out/'texture.png')
obj=['mtllib model.mtl','usemtl skin']
obj += ['v '+' '.join(map(str,v['p'])) for v in vertices]
obj += ['vt '+str(v['uv'][0])+' '+str(1-v['uv'][1]) for v in vertices]
obj += ['f '+' '.join(f'{q+1}/{q+1}' for q in triangles[i:i+3]) for i in range(0,len(triangles),3)]
(out/'model.obj').write_text('\n'.join(obj),encoding='utf-8')
(out/'model.mtl').write_text('newmtl skin\nKd 1 1 1\nmap_Kd texture.png\n',encoding='utf-8')
print(json.dumps(dict(name=a.name,vertices=len(vertices),triangles=len(triangles)//3,joints=count,flags=dict(flags))))
