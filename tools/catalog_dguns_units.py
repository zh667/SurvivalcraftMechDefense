"""Bounded Sigma table/model extraction, separate from SC gameplay adaptation.

Layouts verified against retained AArch64 onm_datbin/load_pchr/set_1corp/
cal_ply_stat/echPchrDispSet and packet construction. Preserve unknown fields.
"""
from pathlib import Path
from collections import Counter
import hashlib, json, math, struct

ROOT=Path(__file__).resolve().parents[1]
OUT=ROOT/'local/dguns/unit-library'


def write(p,obj):
    p.parent.mkdir(parents=True,exist_ok=True)
    p.write_text(json.dumps(obj,ensure_ascii=False,separators=(',',':'),allow_nan=False),encoding='utf-8')


class Data:
    def __init__(self,b): self.b=b
    def read(self,fmt,off):
        if off<0 or off+struct.calcsize(fmt)>len(self.b): raise ValueError(f'Extent {off:x} {fmt}')
        return struct.unpack_from(fmt,self.b,off)
    def ptr(self,off): return self.read('<Q',off)[0]
    def text(self,off):
        if not 0<=off<len(self.b): raise ValueError('string pointer')
        end=self.b.find(b'\0',off)
        if end<0: raise ValueError('unterminated string')
        return self.b[off:end].decode('cp932')


def tables():
    d=Data((OUT/'variants/dat.bin/0001.1.bin').read_bytes())
    k=Data((OUT/'variants/dat.bin/0002.1.bin').read_bytes())
    strings=Data((OUT/'variants/dat.bin/0003.1.bin').read_bytes())
    names={k.read('<h',off)[0]:k.text(k.ptr(off+8)) for off in range(k.ptr(72),k.ptr(72)+(k.ptr(80)-1)*16,16)}
    programs=json.loads((OUT/'native-analysis/index.json').read_text())['programs']
    kinds=[]
    for i in range(k.ptr(48)-1):
        off=k.ptr(40)+64*i; ident=k.read('<h',off)[0]
        textures=list(k.read('<4h',off+20))
        kinds.append(dict(id=ident,name=names[ident],sourceOffset=off,programIndex=k.b[off+17],
            program=programs[k.b[off+17]]['name'],modelId=k.read('<h',off+18)[0],textures=textures,
            rawRecordHex=k.b[off:off+64].hex(),categoryBits=k.read('<H',off+2)[0]))
    bykind={x['id']:x for x in kinds}
    bodies=[]
    for i in range(d.ptr(176)-1):
        off=d.ptr(168)+56*i; kind=d.read('<h',off)[0]
        description=[]
        for language in range(2):
            description.append(strings.text(strings.ptr(strings.ptr(24)+8*((271+i)*2+language))))
        bodies.append(dict(index=i,kind=kind,kindName=names[kind],name=d.text(d.ptr(off+48)),
            modelId=bykind[kind]['modelId'],meshSection=d.b[off+2],slots=d.b[off+3],missileLockCount=d.b[off+4],
            resistancePercent=list(d.b[off+5:off+8]),baseAttack=d.read('<h',off+8)[0],baseHP=d.read('<i',off+12)[0],
            baseEnergy=d.read('<i',off+16)[0],textures=bykind[kind]['textures'],descriptionJP=description[0],descriptionEN=description[1],
            sourceOffset=off,rawRecordHex=d.b[off:off+56].hex()))
    enemies=[]
    for i in range(d.ptr(144)-1):
        off=d.ptr(136)+64*i; kind,variant=d.read('<hB',off)
        enemies.append(dict(index=i,kind=kind,kindName=names[kind],parameterVariant=variant,
            modelId=bykind[kind]['modelId'],meshSection=5 if kind==22 or kind in (19,20) and variant==1 else 0,
            resistancePercent=list(d.b[off+3:off+6]),baseAttack=d.read('<h',off+6)[0],baseHP=d.read('<i',off+8)[0],
            boss=bool(d.read('<I',off+60)[0]&1),rawFloatFields16to40=list(d.read('<7f',off+16)),
            sourceOffset=off,rawRecordHex=d.b[off:off+64].hex()))
    parts=[]
    for i in range(d.ptr(112)-1):
        off=d.ptr(104)+48*i
        parts.append(dict(index=i,id=d.read('<h',off)[0],category=d.b[off+2],
            value14=d.read('<h',off+14)[0],nameJP=d.text(d.ptr(off+32)),nameEN=d.text(d.ptr(off+40)),
            sourceOffset=off,rawRecordHex=d.b[off:off+48].hex()))
    attacks=[]
    for i in range(d.ptr(160)-1):
        off=d.ptr(152)+12*i
        attacks.append(dict(index=i,id=d.read('<h',off)[0],damageChannel=d.b[off+2],weaponCategory=d.b[off+3],
            powerPercent=d.read('<h',off+4)[0],sourceOffset=off,rawRecordHex=d.b[off:off+12].hex()))
    result=dict(kinds=kinds,playerBodies=bodies,enemies=enemies,parts=parts,attacks=attacks,
        fieldEvidence=dict(kind='onm_datbin + load_pchr',body='cal_ply_stat + set_player + make_body_name',
            enemy='get_ene_para + set_1corp (HP+8, attack+6, resist+3..5, boss bit+60)',
            section='PDISP+159 -> echPchrDispSet -> PSCf+120+8*section; NOT all sections are LODs',
            attacks='set_dmg: (actor.attack+bonus)*powerPercent/100 then per-channel resistance'))
    write(OUT/'tables.json',result)
    return result


def mesh(source,section):
    d=Data(source.read_bytes()); b=d.b
    assert b[:4]==b'PSCf'
    lod=d.ptr(120+8*section)
    if not lod:return None
    joint_table=lod+d.read('<I',lod+20)[0]; geometry=d.ptr(104)
    vo,weights,pol,colors,mats,normals=[geometry+x for x in d.read('<6I',lod+24)]
    joints=[]
    for n in range(b[4]):
        off=joint_table+24*n
        parent=b[off+10]
        joints.append(dict(index=n,name=d.text(d.ptr(d.ptr(96)+8*n)),parent=parent if parent!=n and parent<b[4] else -1,
            pivot=list(d.read('<3f',d.read('<I',off+12)[0])),rawRecordHex=b[off:off+24].hex()))
    vertices=[]; indices=[]; face_meta=[]; skipped=[]
    for n in range(d.read('<H',lod+2)[0]):
        row=d.read('<16H',pol+32*n)
        if row[4]&0x20:
            skipped.append(n);continue
        if row[4]&0x800:raise ValueError('Weighted polygon not covered by rigid decoder')
        nv=3 if row[2]==row[3] else 4
        if row[7]>=0x8000:
            uv=[0]*8;slot=-1;flags=0;ci=row[7]&0x7fff
        else:
            mat=mats+40*row[7];uv=d.read('<8f',mat);flags=d.read('<I',mat+32)[0];slot=flags&7;ci=row[5]
        base=len(vertices)
        for j in range(nv):
            at=vo+16*row[j]; bone=b[at+1]
            if bone>=len(joints):raise ValueError('joint index')
            p=d.read('<3f',at+4);pivot=joints[bone]['pivot']
            rgb=list(d.read('<3B',colors+3*(ci+(j if row[4]&8 else 0))))
            if slot>=0:rgb=[min(255,c*2) for c in rgb]
            vertices.append(dict(p=[p[k]+pivot[k] for k in range(3)],local=list(p),
                n=list(d.read('<3f',normals+12*row[11+j])),uv=list(uv[j*2:j*2+2]),bone=bone,color=rgb))
        indices.extend([base,base+1,base+2])
        if nv==4:indices.extend([base,base+2,base+3])
        face_meta.extend([dict(sourcePolygon=n,textureSlot=slot,flags=row[4],materialFlags=flags)]*(nv-2))
    if not vertices:return None
    assert all(math.isfinite(c) for v in vertices for c in v['p']+v['n']+v['uv'])
    return dict(sourceId=int(source.name.split('.')[0]),section=section,sourceSha256=hashlib.sha256(b).hexdigest(),
        coordinates='original source Z-up units; bind pose; no SC rescale',joints=joints,vertices=vertices,indices=indices,
        faces=face_meta,skippedNativeHiddenPolygons=skipped,
        bounds=[[min(v['p'][i] for v in vertices) for i in range(3)],[max(v['p'][i] for v in vertices) for i in range(3)]])


def embedded_motion(source):
    d=Data(source.read_bytes()); b=d.b
    frame_count=d.read('<H',10)[0];lod=d.ptr(120)
    if not lod or not frame_count:return None
    jt=lod+d.read('<I',lod+20)[0]; pos=d.ptr(40);rot=d.ptr(48)
    positions={};rotations={};tracks=[]
    for j in range(b[4]):
        off=d.read('<I',jt+24*j+20)[0]
        if not off:continue
        pairs=[]
        for frame in range(frame_count):
            pi,ri=d.read('<Hi',off+6*frame)
            if pi!=65535: positions[pi]=list(d.read('<4f',pos+16*pi))
            if ri>=0:rotations[ri]=list(d.read('<3f',rot+12*ri))
            pairs.append([pi,ri])
        tracks.append(dict(joint=j,indices=pairs))
    clip_count=d.read('<H',6)[0];directory=d.ptr(24)
    clips=[dict(slot=i,offset=d.read('<I',directory+8*i)[0],directoryBytes=b[directory+8*i:directory+8*i+8].hex()) for i in range(clip_count)] if directory else []
    return dict(stage='decoded source sample tables, NOT timed/retargeted animation playback',
        frameCount=frame_count,positionsAndScale=positions,eulerAngles=rotations,tracks=tracks,clipDirectory=clips,
        sentinels='position 65535=identity; x=32767=use parent/original matrix; Euler index<0=no rotation',
        evidence='shdCalPchrMat, sub_5EF3C, sub_6197C; original clip control bytecode retained in raw model')


def obj_export(m,target,textures,allow_unbound=False):
    target.mkdir(parents=True,exist_ok=True)
    lines=['mtllib model.mtl']
    lines+=['v '+' '.join(str(x) for x in v['p']) for v in m['vertices']]
    lines+=['vt '+str(v['uv'][0])+' '+str(1-v['uv'][1]) for v in m['vertices']]
    slot=None
    for f in range(len(m['indices'])//3):
        s=m['faces'][f]['textureSlot']
        if s!=slot:lines.append('usemtl slot_'+str(s));slot=s
        lines.append('f '+' '.join(f'{i+1}/{i+1}' for i in m['indices'][3*f:3*f+3]))
    (target/'model.obj').write_text('\n'.join(lines),encoding='utf-8')
    materials=[]
    for s in sorted({f['textureSlot'] for f in m['faces']}):
        materials.extend(['newmtl slot_'+str(s),'Kd 1 1 1','d 1'])
        if s>=0 and s<len(textures) and textures[s]>0:
            p=OUT/'textures'/f'{textures[s]:04}.0.png'
            if not p.exists():raise ValueError('Missing texture '+str(p))
            import os
            materials.append('map_Kd '+Path(os.path.relpath(p,target)).as_posix())
        elif s>=0 and not allow_unbound: raise ValueError('Unbound texture slot')
    (target/'model.mtl').write_text('\n'.join(materials),encoding='utf-8')


def main():
    t=tables();models=[];errors=[];cache={};motions=[]
    for source in sorted((OUT/'variants/grp.bin').glob('*.0.bin')):
        if source.read_bytes()[:4]!=b'PSCf':continue
        mid=int(source.name.split('.')[0]);b=source.read_bytes();sections=[]
        for section in range(b[5]):
            try:
                m=mesh(source,section)
                if m:
                    target=OUT/'models'/f'{mid:04}'/f'section-{section}'
                    write(target/'model.json',m);cache[mid,section]=m
                    sections.append(dict(section=section,vertices=len(m['vertices']),triangles=len(m['indices'])//3,
                        bounds=m['bounds'],file=(target/'model.json').relative_to(OUT).as_posix()))
            except Exception as exc:errors.append(dict(model=mid,section=section,error=str(exc)))
        try:
            motion=embedded_motion(source)
            if motion:
                write(OUT/'models'/f'{mid:04}'/'embedded-motion.json',motion)
                motions.append(dict(model=mid,frames=motion['frameCount'],tracks=len(motion['tracks'])))
        except Exception as exc:errors.append(dict(model=mid,stage='embedded-motion',error=str(exc)))
        model_name=Data(b).text(Data(b).ptr(112)) if Data(b).ptr(112) else None
        models.append(dict(id=mid,sourceModelName=model_name,sectionSlots=b[5],sections=sections))
    views=[]
    for k in t['kinds']:
        for model_id,section in cache:
            if model_id!=k['modelId']:continue
            target=OUT/'views'/(k['name']+f'_s{section}')
            try:
                obj_export(cache[model_id,section],target,k['textures'])
                views.append(dict(key=target.name,kind=k['id'],modelId=model_id,section=section,textures=k['textures'],obj=(target/'model.obj').relative_to(OUT).as_posix()))
            except Exception as exc:errors.append(dict(kind=k['id'],section=section,error=str(exc)))
    player_views=[]
    bound_models={k['modelId'] for k in t['kinds']}
    for (model_id,section),model in cache.items():
        if model_id in bound_models:continue
        key=f'unbound-{model_id:04}_s{section}'
        target=OUT/'views'/key;obj_export(model,target,[],allow_unbound=True)
        views.append(dict(key=key,kind=None,modelId=model_id,section=section,textures=[],
            textureBindingStatus='not referenced by KINDDT; untextured ORIGINAL geometry, not a placeholder',
            obj=(target/'model.obj').relative_to(OUT).as_posix()))
    for body in t['playerBodies']:
        for skin in range(4):
            textures=list(body['textures']);textures[0]+=skin
            key=f"player-{body['index']+1:02}-{body['name']}-skin{skin}"
            target=OUT/'views'/key
            obj_export(cache[body['modelId'],body['meshSection']],target,textures)
            player_views.append(dict(key=key,bodyIndex=body['index'],skin=skin,modelId=body['modelId'],section=body['meshSection'],
                textures=textures,obj=(target/'model.obj').relative_to(OUT).as_posix(),evidence='MAPCLS_MAP0006 constructor loads base texture + 0..3; set_ply_model selects section'))
    result=dict(stage='source bind meshes / source tables; animation timing and runtime integration separate',
        modelContainers=models,embeddedMotions=motions,views=views,playerViews=player_views,errors=errors,
        counts=dict(models=len(models),sections=len(cache),kinds=len(t['kinds']),players=len(t['playerBodies']),
            enemyRows=len(t['enemies']),bossRows=sum(e['boss'] for e in t['enemies']),parts=len(t['parts']),attacks=len(t['attacks'])))
    write(OUT/'units.json',result)
    print(json.dumps(result['counts']));print('errors',json.dumps(errors))


if __name__=='__main__':main()
