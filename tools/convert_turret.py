"""Assemble all three Endfield factory turret subAssets in their source transforms."""
import json,shutil,hashlib
from pathlib import Path
root=Path(__file__).resolve().parents[1];src=root/'local/endfield/turret-export'
tex=next((src/'Texture2D').glob('*001_01_D_*png'))
verts=[];indices=[];sources=[];muzzles=[]
for part in range(1,4):
    obj=next((src/'Mesh').glob(f'*001_0{part}_dynamic_lod0*obj'))
    meta=json.loads(next((root/'local/endfield/turret-json/MonoBehaviour').glob(f'battle_turret_1_0{part}*')).read_text())
    offset=list(meta['localTransform']['position'].values());p=[];uv=[];norm=[]
    # Endfield stores the two barrel sockets on the pitch assembly.
    if part==3:
        for config in meta.get('extendConfigs',[]):
            raw=config.get('nameRaw',{}).get('bytes',{}).get('offset0000',{})
            name=''.join(chr(raw.get(f'byte{i:04}',0)) for i in range(16)).split('\0',1)[0]
            if name.startswith('firePos_'):
                pos=config['pos'];muzzles.append([pos['x']+offset[0],pos['y']+offset[1],pos['z']+offset[2]])
    sources.append(dict(file=obj.name,sha256=hashlib.sha256(obj.read_bytes()).hexdigest(),offset=offset))
    for line in obj.read_text().splitlines():
        a=line.split()
        if not a:continue
        if a[0]=='v':p.append(list(map(float,a[1:4])))
        elif a[0]=='vt':uv.append(list(map(float,a[1:3])))
        elif a[0]=='vn':norm.append(list(map(float,a[1:4])))
        elif a[0]=='f':
            start=len(verts)
            for v in a[1:]:
                ix=[int(i)-1 for i in v.split('/')]
                verts.append(dict(p=[p[ix[0]][i]+offset[i] for i in range(3)],n=norm[ix[2]],uv=[uv[ix[1]][0],1-uv[ix[1]][1]],bone=part-1))
            for i in range(1,len(a)-2):indices.extend([start,start+i,start+i+1])
floor=min(v['p'][1] for v in verts)
for v in verts:v['p'][1]-=floor
for p in muzzles:p[1]-=floor
out=root/'Mod/Assets/Imported/Turret';out.mkdir(parents=True,exist_ok=True)
model=dict(name='Turret',sources=sources,muzzles=muzzles,joints=[dict(name='Base',parent=-1,pivot=[0,-floor,0]),dict(name='Yaw',parent=0,pivot=[0,-floor,0]),dict(name='Pitch',parent=1,pivot=[0,3.095-floor,0])],vertices=verts,indices=indices,adaptedMotion=True)
(out/'model.json').write_text(json.dumps(model,separators=(',',':')))
shutil.copyfile(tex,out/'texture.png')
lines=['mtllib model.mtl','usemtl skin']
lines+=['v '+' '.join(map(str,v['p'])) for v in verts]
lines+=['vt '+str(v['uv'][0])+' '+str(1-v['uv'][1]) for v in verts]
lines+=['f '+' '.join(f'{q+1}/{q+1}' for q in indices[i:i+3]) for i in range(0,len(indices),3)]
(out/'model.obj').write_text('\n'.join(lines));(out/'model.mtl').write_text('newmtl skin\nKd 1 1 1\nmap_Kd texture.png\n')
print(len(verts),len(indices)//3,'floor',floor)
