"""Validate converted tower assets, original audio provenance and package closure."""
import hashlib
import json
import math
from pathlib import Path
import zipfile
import numpy as np
from prepare_endfield_three import ROOT, OUT, load, save

assets=ROOT/'Mod/Assets/Imported/Endfield'
checks=[]
def check(value,name):
    if not value:raise AssertionError(name)
    checks.append(name)

for n in (1,2,3):
    key=f'battle_turret_{n}';model=load(assets/key/'model.json')
    check(model['modelOrigin']=='local-authored-bedrock',key+' authored geometry')
    check(model['textureOrigin']=='local-authored-pixel-atlas',key+' authored atlas')
    check(len(model['joints'])==6 and model['muzzleBones']==[4,5],key+' aim and VAT bones plus barrel sockets')
    geo=Path(model['sourceModel']);texture=Path(model['sourceTexture'])
    check(hashlib.sha256(geo.read_bytes()).hexdigest()==model['geometrySha256'],key+' latest source geometry hash')
    check(hashlib.sha256(texture.read_bytes()).hexdigest()==model['textureSha256'],key+' source atlas hash')
    check((assets/key/'texture.png').read_bytes()==texture.read_bytes(),key+' original authored atlas unchanged')
    check(all(0<=i<len(model['vertices']) for i in model['indices']),key+' indices in range')
    check(all(math.isfinite(v) for vertex in model['vertices'] for v in vertex['p']),key+' finite mesh')
    check(all(p[2]<0 for p in model['muzzles']),key+' converted socket forward')
    own=load(geo)['minecraft:geometry'][0];head=next(b for b in own['bones'] if b['name']=='pitch')
    for i,p in enumerate(model['muzzles']):
        check(np.allclose(p,np.array(head['locators'][f'fire_{i+1}'])*[-1,1,1]/16),key+' authored muzzle reproduced')
    animation=model['animation'];vat=ROOT/animation['sourceTexture']
    check(hashlib.sha256(vat.read_bytes()).hexdigest()==animation['sourceSha256'],key+' animation source hash')
    texels=np.frombuffer(vat.read_bytes(),dtype='<f2').reshape(44,8,4).astype(float)
    for bone in range(1,4):
        for frame in range(44):
            sample=animation['tracks'][bone-1][frame];raw=texels[43-frame,bone*2:bone*2+2]
            q=raw[0]/np.linalg.norm(raw[0]);q[:2]*=-1;translation=raw[1].copy();translation[2]*=-1
            check(np.allclose(sample,[*q,*translation],atol=1e-6),f'{key} original VAT bone {bone} frame {frame}')
    check(animation['clips'][1]['frameDuration']==9 and animation['fps']==30,key+' source attack timing')
fx=load(assets/'Effects/library.json')
check(len(fx['effects'])==14,'fourteen source effect prefabs converted')
for key,clip in fx['effects'].items():
    check(bool(clip['particles'] or clip['meshes']),key+' nonempty visual')
    for p in clip['particles']:
        check(all(len(f)==14 and all(math.isfinite(x) for x in f) for f in p['frames']),key+' finite baked frames')
for m in fx['materials']:
    check((ROOT/'Mod/Assets'/(m['texture']+'.png')).is_file(),'source-derived effect texture '+m['texture'])
audio=load(OUT/'audio/catalog.json')
check(len(audio['media'])==92,'92 native stems')
check(all(e['graph']['traversalStatus']=='complete' for e in audio['events']),'all selected event graphs resolved')
for m in audio['media']:
    check(hashlib.sha256((OUT/m['file']).read_bytes()).hexdigest()==m['sha256'],'WEM hash '+str(m['id']))
    check(hashlib.sha256((OUT/m['wav']).read_bytes()).hexdigest()==m['wavSha256'],'WAV hash '+str(m['id']))
for e in audio['events']:
    for mix in e['mixes']:
        check(set(mix['mediaIds'])<=set(e['graph']['sourceMediaIds']),'mix only uses event-owned media '+e['name'])
        check((ROOT/'Mod/Assets'/(mix['path']+'.ogg')).exists(),'runtime audio exists '+mix['path'])
version=load(ROOT/'Mod/modinfo.json')['Version']
package=ROOT/f'dist/MechDefense-{version}.scmod'
with zipfile.ZipFile(ROOT/'dist/MechDefense-0.2.0.scmod') as previous:
    for p in assets.rglob('*'):
        if p.is_file() and ('Audio' in p.parts or 'Effects' in p.parts):
            key='Assets/'+p.relative_to(ROOT/'Mod/Assets').as_posix()
            check(p.read_bytes()==previous.read(key),'preserved Endfield audio/FX '+p.name)
with zipfile.ZipFile(package) as z:
    check(z.testzip() is None,'archive CRC')
    manifest=json.loads(z.read('asset-manifest.json'))
    for row in manifest:
        check(hashlib.sha256(z.read(row['path'])).hexdigest()==row['sha256'],'package hash '+row['path'])
    for p in assets.rglob('*'):
        if p.is_file() and p.suffix in ('.json','.png','.ogg'):
            check('Assets/'+p.relative_to(ROOT/'Mod/Assets').as_posix() in z.namelist(),'packaged '+p.name)
summary=dict(passed=True,checks=len(checks),packageSha256=hashlib.sha256(package.read_bytes()).hexdigest(),
             packageBytes=package.stat().st_size,sourceAudioStems=92,sourceEffects=14)
save(OUT/'verification.json',summary);print(json.dumps(summary))
import subprocess,sys
subprocess.run([sys.executable,str(ROOT/'tools/verify_tower_feedback.py')],check=True)
