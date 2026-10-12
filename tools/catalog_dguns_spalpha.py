"""Exact-build SP-alpha tables and source meshes; no Sigma table offsets reused."""
from pathlib import Path
import json, struct, hashlib, shutil
from collections import Counter
import catalog_dguns_units as common
from archive_dguns_spalpha import ROOT, OUT, write


def resource(ident):
    if 0 < ident < 1000: return 'grp.bin', ident
    if 4000 < ident < 5000: return 'grp2.bin', ident-4000
    raise ValueError(f'Not a graphic resource: {ident}')


def main():
    d=common.Data((OUT/'variants/dat.bin/0001.1.bin').read_bytes())
    k=common.Data((OUT/'variants/dat.bin/0002.1.bin').read_bytes())
    s=common.Data((OUT/'variants/dat.bin/0003.1.bin').read_bytes())
    strings=[s.text(s.ptr(s.ptr(24)+8*i)) for i in range(s.ptr(32))]
    write(OUT/'strings.json', [dict(id=i,jp=strings[2*i],en=strings[2*i+1]) for i in range(len(strings)//2)])
    names={k.read('<h',o)[0]:k.text(k.ptr(o+8)) for o in range(k.ptr(72),k.ptr(72)+(k.ptr(80)-1)*16,16)}
    kinds=[]
    for i in range(k.ptr(48)-1):
        o=k.ptr(40)+64*i; ident=k.read('<h',o)[0]; mid=k.read('<h',o+20)[0]
        bank,entry=resource(mid)
        kinds.append(dict(id=ident,name=names[ident],sourceOffset=o,modelId=mid,sourceBank=bank,sourceEntry=entry,
                          textures=list(k.read('<2h',o+22)),defaultSection=k.b[o+18],programIndex=k.b[o+17],rawRecordHex=k.b[o:o+64].hex()))
    bykind={x['id']:x for x in kinds}
    cn=['通用型','试作型','新型','通用型二代','试作型二代','新型二代']
    bodies=[]
    for table,header,menu in [('plbody1',216,167),('plbody3',232,173)]:
        assert d.ptr(header+8)==7
        for i in range(6):
            o=d.ptr(header)+28*i; kind=d.read('<h',o)[0]; kd=bykind[kind]
            label=strings[(menu+i)*2+1]
            bodies.append(dict(index=len(bodies),table=table,tableIndex=i,kind=kind,kindName=kd['name'],name=label,
                               nameCN=label.replace('Type ','').replace(' 2nd',' 二代')+'（'+cn[i]+'）',
                               nameStatus='source menu label + descriptive Chinese translation; not an official Chinese name',
                               standardModeName=strings[(161+i)*2+1] if table=='plbody1' else None,
                               modelId=kd['modelId'],meshSection=kd['defaultSection'],textures=kd['textures'],
                               slots=d.b[o+4],missileLockCount=d.b[o+5],resistancePercent=list(d.b[o+6:o+9]),
                               baseAttack=d.read('<h',o+10)[0],baseHP=d.read('<i',o+12)[0],baseEnergy=d.read('<i',o+16)[0],
                               sourceOffset=o,rawRecordHex=d.b[o:o+28].hex()))
    enemies=[]
    for table,header in [('enepara',152),('enepara2',168),('enepara3',184)]:
        for i in range(d.ptr(header+8)-1):
            o=d.ptr(header)+60*i; kind=d.read('<h',o)[0]; kd=bykind[kind]
            enemies.append(dict(index=len(enemies),table=table,tableIndex=i,kind=kind,kindName=kd['name'],
                                parameterVariant=d.b[o+2],modelId=kd['modelId'],meshSection=kd['defaultSection'],
                                resistancePercent=list(d.b[o+3:o+6]),baseAttack=d.read('<h',o+6)[0],baseHP=d.read('<i',o+8)[0],
                                bossFamily=kd['name'].startswith(('KN_BOS','KN_BSM')),sourceOffset=o,rawRecordHex=d.b[o:o+60].hex()))
    parts=[]
    for i in range(d.ptr(96)-1):
        o=d.ptr(88)+32*i
        parts.append(dict(index=i,id=d.read('<h',o)[0],category=d.read('<h',o+2)[0],value=d.read('<h',o+4)[0],
                          nameJP=d.text(d.ptr(o+16)),nameEN=d.text(d.ptr(o+24)),sourceOffset=o,rawRecordHex=d.b[o:o+32].hex()))
    attacks=[]
    for i in range(d.ptr(208)-1):
        o=d.ptr(200)+12*i
        attacks.append(dict(index=i,id=d.read('<h',o)[0],rawRecordHex=d.b[o:o+12].hex(),sourceOffset=o))
    tables=dict(kinds=kinds,playerBodies=bodies,enemies=enemies,parts=parts,attacks=attacks,
                evidence=dict(loader='0x6eb58 bank lookup, 0x6febc KINDDT+20/+22',tables='0x7120c',
                              player='0x77f80 stride28; attack+10, HP+12, energy+16',enemy='0x84508 stride60; 0xb3a30 HP+8, attack+6',
                              names='0x93e18/0x97898/0x9b514 menu indices161/167/173',
                              limits='Enemy meshSection is KINDDT default, not every mission/behavior override. All geometry sections exported.'))
    write(OUT/'tables.json',tables)
    # Convenience global-ID texture directory. Originals retain the bank namespace.
    for bank,base in [('grp.bin',0),('grp2.bin',4000)]:
        for tex in (OUT/'textures'/bank).glob('*.png'):
            a,b=tex.stem.split('.')
            shutil.copyfile(tex,OUT/'textures'/f'{int(a)+base:04}.{b}.png')
    models=[]; cache={}; errors=[]; motions=[]
    for bank,base in [('grp.bin',0),('grp2.bin',4000)]:
        for source in sorted((OUT/'variants'/bank).glob('*.0.bin')):
            b=source.read_bytes()
            if b[:4]!=b'PSCf':continue
            mid=base+int(source.name.split('.')[0]);sections=[]
            for section in range(b[5]):
                try:
                    model=common.mesh(source,section)
                    if model:
                        model.update(sourceBank=bank,sourceEntry=int(source.name.split('.')[0]),globalResourceId=mid)
                        dest=OUT/'models'/f'{mid:04}'/f'section-{section}'/'model.json'
                        common.write(dest,model);cache[mid,section]=model
                        sections.append(dict(section=section,vertices=len(model['vertices']),triangles=len(model['indices'])//3,file=dest.relative_to(OUT).as_posix()))
                except Exception as exc:errors.append(dict(model=mid,section=section,error=str(exc)))
            try:
                motion=common.embedded_motion(source)
                if motion:
                    motion['evidence']='Shared PSCf sample-table parser, bounds checked; SP-alpha clip timing not reconstructed'
                    write(OUT/'models'/f'{mid:04}'/'embedded-motion.json',motion)
                    motions.append(dict(model=mid,frames=motion['frameCount'],tracks=len(motion['tracks'])))
            except Exception as exc:errors.append(dict(model=mid,stage='embedded-motion',error=str(exc)))
            md=common.Data(b)
            models.append(dict(id=mid,sourceBank=bank,sourceEntry=mid-base,sourceModelName=md.text(md.ptr(112)),sections=sections))
    common.OUT=OUT
    views=[]; previewed=set()
    for kd in kinds:
        for mid,section in cache:
            if mid!=kd['modelId']:continue
            key=kd['name']+f'_s{section}'
            missing=sorted({f['textureSlot'] for f in cache[mid,section]['faces'] if f['textureSlot']>=0 and (f['textureSlot']>=len(kd['textures']) or kd['textures'][f['textureSlot']]<=0)})
            common.obj_export(cache[mid,section],OUT/'views'/key,kd['textures'],allow_unbound=bool(missing))
            v=dict(key=key,kind=kd['id'],modelId=mid,section=section,textures=kd['textures'],obj=f'views/{key}/model.obj',unboundTextureSlots=missing)
            if mid not in previewed and section==kd['defaultSection']:
                v['previewGroup']='boss' if kd['name'].startswith(('KN_BOS','KN_BSM')) else 'resource'
                previewed.add(mid)
            views.append(v)
    bound={kd['modelId'] for kd in kinds}
    for (mid,section),model in cache.items():
        if mid in bound:continue
        key=f'unbound-{mid:04}_s{section}'
        common.obj_export(model,OUT/'views'/key,[],allow_unbound=True)
        views.append(dict(key=key,kind=None,modelId=mid,section=section,textures=[],obj=f'views/{key}/model.obj',previewGroup='unbound'))
    players=[]
    for body in bodies:
        key=f"player-{body['table']}-{body['tableIndex']+1}"
        common.obj_export(cache[body['modelId'],body['meshSection']],OUT/'views'/key,body['textures'])
        players.append(dict(key=key,bodyIndex=body['index'],skin=0,modelId=body['modelId'],section=body['meshSection'],
                            textures=body['textures'],obj=f'views/{key}/model.obj'))
    units=dict(modelContainers=models,embeddedMotions=motions,views=views,playerViews=players,errors=errors,
               counts=dict(models=len(models),sections=len(cache),kinds=len(kinds),playerRows=len(bodies),
                           distinctPlayerModels=len({p['modelId'] for p in bodies}),enemyRows=len(enemies),
                           enemyKinds=len({e['kind'] for e in enemies}),bossFamilyKinds=len({e['kind'] for e in enemies if e['bossFamily']}),
                           parts=len(parts),attacks=len(attacks)))
    write(OUT/'units.json',units)
    print(json.dumps(units['counts']));print('errors',errors)
    assert not errors


if __name__=='__main__':main()
