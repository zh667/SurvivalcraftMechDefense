"""Convert source gun-tower meshes and source particle emitters for Survivalcraft.

Meshes/sockets/textures are recovered. FX playback is a portable adapter:
source transforms, bursts, lifetime/size/color curves and textures are baked;
Unity shader distortion/soft-depth, GPU VAT and Wwise DSP are not emulated.
"""
import math
import random
import shutil
import numpy as np
from PIL import Image
from prepare_endfield_three import ROOT, OUT, load, save

DEST=ROOT/'Mod/Assets/Imported/Endfield'
objects={}; files={}
for folder in ['json','scene-json','builtin-json']:
    for p in (OUT/folder).rglob('*.json'):
        d=load(p);meta=d.get('$animestudio',{})
        if not meta.get('pathId'):continue
        key=(meta['type'],meta['pathId'])
        if key in objects and objects[key]['$animestudio']['sourceFile']!=meta['sourceFile']:
            raise ValueError('Ambiguous PathID across serialized files: '+str(key))
        objects[key]=d;files[key]=p

def vec(v):return np.array([v.get(k,v.get(k.upper(),0)) for k in 'xyz'],float)
def rgba(v):return np.array([v[k] for k in 'rgba'])
def resolve(kind,ptr):return objects[kind,ptr['m_PathID']]
def byname(kind,name):
    return next(v for (k,i),v in objects.items() if k==kind and v['$animestudio']['name']==name)
def imagefile(ptr):
    d=resolve('Texture2D',ptr);stem=files['Texture2D',ptr['m_PathID']].stem
    return OUT/'converted/Texture2D'/(stem+'.png')
def curve(c,t=0,r=.5):
    if not c:return 0
    state=c.get('minMaxState',0);mx=c.get('scalar',1);mn=c.get('minScalar',mx)
    def sample(which):
        keys=c.get(which,{}).get('m_Curve',[])
        if not keys:return 1
        if t<=keys[0]['time']:return keys[0]['value']
        for a,b in zip(keys,keys[1:]):
            if t<=b['time']:
                span=b['time']-a['time'];s=(t-a['time'])/max(span,1e-8)
                return (2*s**3-3*s*s+1)*a['value']+(s**3-2*s*s+s)*span*a['outSlope']+(-2*s**3+3*s*s)*b['value']+(s**3-s*s)*span*b['inSlope']
        return keys[-1]['value']
    if state==0:return mx
    if state==1:return mx*sample('maxCurve')
    if state==2:return mn*sample('minCurve')*(1-r)+mx*sample('maxCurve')*r
    return mn*(1-r)+mx*r
def color(c,t=0,r=.5):
    state=c.get('minMaxState',0)
    if state==0:return rgba(c['maxColor'])
    if state==3:return rgba(c['minColor'])*(1-r)+rgba(c['maxColor'])*r
    g=c['maxGradient'];values=np.ones(4)
    for ch in range(4):
        n=g['m_NumAlphaKeys'] if ch==3 else g['m_NumColorKeys'];prefix='atime' if ch==3 else 'ctime'
        if n:values[ch]=np.interp(t,[g[prefix+str(i)]/65535 for i in range(n)],[g['key'+str(i)]['rgba'[ch]] for i in range(n)])
    return values
def trs(t):
    from scipy.spatial.transform import Rotation
    q=t['m_LocalRotation'];q=[q.get(k,q.get(k.upper())) for k in 'xyzw']
    m=np.eye(4);m[:3,:3]=Rotation.from_quat(q).as_matrix()@np.diag(vec(t['m_LocalScale']));m[:3,3]=vec(t['m_LocalPosition']);return m
def mesh_data(d):
    count=d['m_VertexCount'];stride=len(d['m_UV0'])//count
    return [dict(p=d['m_Vertices'][i*3:i*3+3],n=d['m_Normals'][i*3:i*3+3],uv=d['m_UV0'][i*stride:i*stride+2],bone=0) for i in range(count)],d['m_Indices']

def models():
    summaries=[]
    for variant in (1,2,3):
        key=f'battle_turret_{variant}';target=DEST/key;target.mkdir(parents=True,exist_ok=True)
        vertices=[];indices=[];joints=[];muzzles=[];sources=[];clips=[]
        for part in (1,2,3):
            config=byname('MonoBehaviour',f'{key}_0{part}')
            offset=vec(config['localTransform']['position']);assert config['localTransform']['scale']==1
            rd=config['lodSettings']['_valueData'][0]['m_rendererDataArr'][0]
            mesh=resolve('Mesh',rd['m_mesh']);verts,ix=mesh_data(mesh)
            start=len(vertices)
            for v in verts:
                p=np.array(v['p'])+offset;p[2]*=-1;v['p']=p.tolist();v['n'][2]*=-1;v['uv'][1]=1-v['uv'][1];v['bone']=part-1
            vertices+=verts;indices += [start+i for i in ix]
            pivot=offset.copy();pivot[2]*=-1
            joints.append(dict(name=f'Part{part}',parent=part-2,pivot=pivot.tolist()))
            for socket in config['extendConfigs']:
                b=socket['nameRaw']['bytes']['offset0000'];name=''.join(chr(b[f'byte{i:04}']) for i in range(16)).split('\0')[0]
                if name.startswith('firePos_'):
                    p=vec(socket['pos'])+offset;p[2]*=-1;muzzles.append(p.tolist())
            material=resolve('Material',rd['m_materials'][0]);tex=material['m_SavedProperties']['m_TexEnvs']
            sources.append(dict(mesh=mesh['$animestudio'],assembly=config['$animestudio'],material=material['$animestudio']))
            clips.append(config['vatSetting'])
            # All three parts of each selected tower share the same diffuse atlas.
            diffuse=next(v['m_Texture'] for k,v in tex.items() if k in ('_MainTex','_BaseMap','_DiffuseTex','_BaseColorMap') and v['m_Texture']['m_PathID'])
            source=imagefile(diffuse)
            if part==1:shutil.copyfile(source,target/'texture.png')
            else:assert source.read_bytes()==(target/'texture.png').read_bytes()
        floor=min(v['p'][1] for v in vertices)
        for v in vertices:v['p'][1]-=floor
        for j in joints:j['pivot'][1]-=floor
        for p in muzzles:p[1]-=floor
        model=dict(name=key,vertices=vertices,indices=indices,joints=joints,muzzles=muzzles,
                   coordinatePolicy='Unity +Z forward to Survivalcraft -Z; reflect Z once',sourceGame='Endfield',sources=sources,sourceVatClips=clips,adaptedMotion=True)
        save(target/'model.json',model)
        # Editable, assembled preview with the same converted coordinates.
        lines=['mtllib model.mtl','usemtl skin']
        lines+=['v '+' '.join(map(str,v['p'])) for v in vertices]
        lines+=['vt '+str(v['uv'][0])+' '+str(1-v['uv'][1]) for v in vertices]
        lines+=['f '+' '.join(f'{i+1}/{i+1}' for i in indices[a:a+3]) for a in range(0,len(indices),3)]
        (target/'model.obj').write_text('\n'.join(lines),encoding='ascii')
        (target/'model.mtl').write_text('newmtl skin\nKd 1 1 1\nmap_Kd texture.png\n',encoding='ascii')
        summaries.append(dict(id=key,vertices=len(vertices),triangles=len(indices)//3,sockets=len(muzzles)))
    return summaries

materials=[];matids={};fxmeshes=[];fxmeshids={}
def fxmesh(ptr):
    mid=ptr['m_PathID']
    if not mid:return -1
    if mid in fxmeshids:return fxmeshids[mid]
    d=resolve('Mesh',ptr);vs,ix=mesh_data(d)
    for v in vs:v['p'][2]*=-1;v['uv'][1]=1-v['uv'][1]
    index=len(fxmeshes);fxmeshes.append(dict(vertices=vs,indices=ix,source=d['$animestudio']));fxmeshids[mid]=index
    return index
def material(ptr):
    mid=ptr['m_PathID']
    if mid in matids:return matids[mid]
    d=resolve('Material',ptr);props=d['m_SavedProperties'];tex=props['m_TexEnvs'];f=props['m_Floats'];cols=props['m_Colors']
    # Bake the static main/mask combination; retain source material for shader work.
    base=tex.get('_MainTex');mask=tex.get('_MaskTex')
    a=np.ones((256,256,4))
    def sample(env):
        im=np.array(Image.open(imagefile(env['m_Texture'])).convert('RGBA').resize((256,256)),dtype=float)/255
        s=env['m_Scale'];o=env['m_Offset'];u=(np.arange(256)+.5)/256;v=1-u
        x=((u*s.get('X',1)+o.get('X',0))%1*256).astype(int)
        y=((1-(v*s.get('Y',1)+o.get('Y',0)))%1*256).astype(int)
        return im[y[:,None],x[None,:]]
    if base and base['m_Texture']['m_PathID']:a=sample(base)
    if f.get('_UseMainTexAsAlpha',0):a[:,:,3]*=a[:,:,:3].mean(axis=2)
    if mask and mask['m_Texture']['m_PathID'] and f.get('_UseMask',0):
        b=sample(mask);a[:,:,3]*=b[:,:,:3].mean(axis=2) if f.get('_UseMaskTexAsAlpha',0) else b[:,:,3]
    name=d['$animestudio']['name']+f'_{mid & ((1<<64)-1):016x}'
    p=DEST/'Effects'/('textures/'+name+'.png');p.parent.mkdir(parents=True,exist_ok=True)
    Image.fromarray(np.uint8(np.clip(a,0,1)*255)).save(p)
    tint=rgba(cols.get('_TintColor',dict(r=1,g=1,b=1,a=1)));tint[:3]*=f.get('_TintColorIntensity',1);tint[3]*=f.get('_TintColorAlpha',1)
    row=dict(texture='Imported/Endfield/Effects/textures/'+name,color=tint.tolist(),additive=f.get('_DstBlend',1)==1,
             source=d['$animestudio'],unported=[k for k in ('_UseDisturb','_UseDisturb2','_UseDissolve','_UsePolarUV','_UseSoftBlend','_UseFresnel') if f.get(k,0)])
    index=len(materials);materials.append(row);matids[mid]=index;return index

def effects():
    effects={};sel=load(OUT/'selection.json');wanted={n.lower() for f in sel['families'].values() for n in f['effectNames']}
    # Also preserve the previous source-named attack/trail family for editing.
    gos={i:d for (k,i),d in objects.items() if k=='GameObject'}
    transforms={i:d for (k,i),d in objects.items() if k=='Transform'}
    systems={d['m_GameObject']['m_PathID']:d for (k,i),d in objects.items() if k=='ParticleSystem'}
    renderers={d['m_GameObject']['m_PathID']:d for (k,i),d in objects.items() if k=='ParticleSystemRenderer'}
    meshfilters={d['m_GameObject']['m_PathID']:d for (k,i),d in objects.items() if k=='MeshFilter'}
    meshrenderers={d['m_GameObject']['m_PathID']:d for (k,i),d in objects.items() if k=='MeshRenderer'}
    for goid,go in gos.items():
        name=go['m_Name']
        if name.lower() not in wanted:continue
        rng=random.Random(goid);particles=[];proof=[];static=[]
        def walk(t,m):
            m=m@trs(t);gid=t['m_GameObject']['m_PathID'];ps=systems.get(gid);renderer=renderers.get(gid)
            if gid in meshfilters and gid in meshrenderers:
                mr=meshrenderers[gid];mf=meshfilters[gid]
                if mr.get('m_Enabled',True) and mf['m_Mesh']['m_PathID']:
                    reflection=np.diag([1,1,-1,1]);matrix=reflection@m@reflection
                    static.append(dict(mesh=fxmesh(mf['m_Mesh']),material=material(mr['m_Materials'][0]),matrix=matrix.T.flatten().tolist()))
            if ps and renderer and renderer['m_Enabled'] and renderer['m_RenderMode']!=5:
                initial=ps['InitialModule'];em=ps['EmissionModule'];shape=ps['ShapeModule']
                if em['enabled']:
                    mat=material(renderer['m_Materials'][0]);births=[];delay=curve(ps['startDelay'])
                    for burst in em['m_Bursts']:
                        for repeat in range(max(1,burst['cycleCount'])):
                            births += [delay+burst['time']+repeat*burst['repeatInterval']]*int(curve(burst['countCurve']))
                    rate=curve(em['rateOverTime'])
                    if rate>0:births += [delay+i/rate for i in range(min(64,int(rate*min(.4,ps['lengthInSec']))))]
                    for born in births[:96]:
                        rand=rng.random();life=max(.01,curve(initial['startLifetime'],r=rand));speed=curve(initial['startSpeed'],r=rand)
                        pos=np.zeros(3);direction=np.array([0.,0.,1.])
                        if shape['enabled']:
                            angle=rng.random()*math.tau;radius=shape.get('radius',{}).get('value',0)*math.sqrt(rng.random())
                            if shape['type'] in (0,4):
                                pos=np.array([math.cos(angle)*radius,math.sin(angle)*radius,0.])
                                theta=math.radians(shape.get('angle',25))*rng.random()
                                direction=np.array([math.sin(theta)*math.cos(angle),math.sin(theta)*math.sin(angle),math.cos(theta)])
                            elif shape['type']==8:pos=np.array([rng.uniform(-.5,.5) for _ in range(3)])*vec(shape['m_Scale'])
                            pos+=vec(shape['m_Position'])
                        frames=[];velocity_offset=np.zeros(3);rotation_offset=0.
                        for frame in range(min(90,int(life*30)+1)):
                            age=frame/30;fraction=min(1,age/life);p=pos+direction*speed*age
                            if ps['VelocityModule']['enabled'] and frame:
                                velocity_offset+=np.array([curve(ps['VelocityModule'][k],fraction,rand) for k in 'xyz'])/30
                            p+=velocity_offset
                            p[1]-=4.905*curve(initial['gravityModifier'])*age*age
                            world=(m@np.append(p,1))[:3];world[2]*=-1
                            size=curve(initial['startSize'],r=rand);sy=curve(initial['startSizeY'],r=rand) if initial['size3D'] else size
                            if ps['SizeModule']['enabled']:
                                sm=ps['SizeModule'];mul=curve(sm['curve'],fraction,rand);size*=mul;sy*=curve(sm['y'],fraction,rand) if sm['separateAxes'] else mul
                            scale=np.linalg.norm(m[:3,:3],axis=0);size*=scale[0];sy*=scale[1]
                            c=color(initial['startColor'],r=rand)
                            if ps['ColorModule']['enabled']:c*=color(ps['ColorModule']['gradient'],fraction,rand)
                            c*=np.array(materials[mat]['color'])
                            if ps['RotationModule']['enabled'] and frame:rotation_offset+=curve(ps['RotationModule']['curve'],fraction,rand)/30
                            rotation=curve(initial['startRotation'],r=rand)+rotation_offset
                            uv=[0,0,1,1];um=ps['UVModule']
                            if um['enabled']:
                                nx,ny=um['tilesX'],um['tilesY'];n=nx*ny
                                tile=min(n-1,int((curve(um['frameOverTime'],fraction,rand)*um['cycles']+curve(um['startFrame'],r=rand))%1*n))
                                uv=[(tile%nx)/nx,(tile//nx)/ny,(tile%nx+1)/nx,(tile//nx+1)/ny]
                            frames.append([*world.tolist(),size,sy,rotation,*c.tolist(),*uv])
                        basis=np.diag([1,1,-1])@m[:3,:3]@np.diag([1,1,-1]);basis/=np.maximum(1e-6,np.linalg.norm(basis,axis=0))
                        particles.append(dict(birth=born,life=life,material=mat,frames=frames,
                                              right=basis[:,0].tolist(),up=basis[:,1].tolist(),forward=basis[:,2].tolist(),
                                              mesh=fxmesh(renderer['m_Mesh']) if renderer['m_RenderMode']==4 else -1,
                                              renderMode=renderer['m_RenderMode'],lengthScale=renderer['m_LengthScale'],velocityScale=renderer['m_VelocityScale'],source=ps['$animestudio']['pathId']))
                    proof.append(dict(particle=ps['$animestudio'],renderer=renderer['$animestudio'],
                                      unsupportedModules=[k for k in ['NoiseModule','TrailModule','SubModule','CustomDataModule'] if ps[k].get('enabled')],
                                      sourceMesh=renderer['m_Mesh'] if renderer['m_RenderMode']==4 else None))
            for child in t['m_Children']:walk(transforms[child['m_PathID']],m)
        walk(go['m_Transform'],np.eye(4))
        effects[name.lower()]=dict(name=name,fps=30,particles=particles,meshes=static,source=go['$animestudio'],emitters=proof,
                                  duration=max((p['birth']+p['life'] for p in particles),default=.3))
    missing=wanted-effects.keys()
    if missing:raise ValueError('Missing source FX: '+str(missing))
    save(DEST/'Effects/library.json',dict(materials=materials,effects=effects,meshes=fxmeshes,
                                       conversion='source parameter bake with shader/mesh-particle adaptations; see emitters'))
    return dict(effects=len(effects),emitters=sum(len(e['emitters']) for e in effects.values()),materials=len(materials))

if __name__=='__main__':
    import argparse
    parser=argparse.ArgumentParser()
    parser.add_argument('--source-models',action='store_true',help='Explicitly replace runtime authored models with recovered game meshes for reference work.')
    args=parser.parse_args()
    result=dict(models=models() if args.source_models else 'unchanged; import_custom_endfield_models.py owns authored geometry',effects=effects());save(OUT/'conversion-report.json',result);print(result)
