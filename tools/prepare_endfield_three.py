"""Resolve three gun towers and the named effects found in their skill payloads."""
import hashlib
import json
from pathlib import Path
import re
import shutil
import struct

ROOT = Path(__file__).resolve().parents[1]
LIB = ROOT / 'local/endfield/tower-library'
OUT = LIB / 'three-gun-towers'

def load(p):
    return json.loads(p.read_text(encoding='utf-8-sig'))

def save(p, value):
    p.parent.mkdir(parents=True, exist_ok=True)
    p.write_text(json.dumps(value, ensure_ascii=False, indent=2), encoding='utf-8')

def strings(data):
    # These compact SkillData strings use an Int32 UTF-8 byte count.
    # Preserve offsets; this is string framing, not a claim to decode the full skill.
    result=[]
    for at in range(len(data)-4):
        n=struct.unpack_from('<i', data, at)[0]
        if not (4<=n<=512 and at+4+n<=len(data)):
            continue
        raw=data[at+4:at+4+n]
        if all(32<=x<127 for x in raw):
            result.append(dict(offset=at,value=raw.decode('ascii')))
    return result

def main():
    manifest=load(ROOT/'local/endfield/manifest-index.json')
    vfs=load(ROOT/'local/endfield/vfs-index.json')
    bundles={x['index']:x for x in manifest['bundles']}
    assets={x['path']:x for x in manifest['assets']}
    vfiles={x['fileName'].lower():x for x in vfs['files']}
    family={}; all_ids=set()
    for variant in (1,2,3):
        key=f'battle_turret_{variant}'
        catalog=load(LIB/'families'/key/'catalog.json')
        seeds={p for p in catalog['seeds'] if '/sprites/' not in p and '/guide/' not in p}
        refs=[]
        for p in sorted((LIB/'combat-json').rglob(f'*turret_{variant}_*.json')):
            b=p.read_bytes()
            refs.append(dict(file=str(p.relative_to(LIB)),sha256=hashlib.sha256(b).hexdigest(),strings=strings(b)))
        effect_names={s['value'].lower() for r in refs for s in r['strings'] if s['value'].lower().startswith('p_')}
        # Projectile components provide named effect references independently of skill strings.
        for p in (LIB/'effects-v1/MonoBehaviour').glob(f'*turret_{variant}_*.json'):
            for effect in re.findall(r'"effectName":\s*"([^"]+)"',p.read_text()):
                effect_names.add(effect.lower())
        for p in assets:
            if p.rsplit('/',1)[-1].removesuffix('.prefab') in effect_names:
                seeds.add(p)
        ids=set(); todo=[assets[p]['bundle'] for p in seeds]
        while todo:
            bid=todo.pop()
            if bid in ids:continue
            ids.add(bid);todo.extend(bundles[bid]['dependencies'])
        all_ids.update(ids)
        family[key]=dict(nameCN=catalog['nameCN'],seeds=sorted(seeds),bundleIds=sorted(ids),
                         skillStringEvidence=refs,effectNames=sorted(effect_names),
                         combat=catalog['combat'],audioEvents=catalog['audioEvents'])
    receipts=[]
    for bid in sorted(all_ids):
        name=bundles[bid]['name']; f=vfiles[('Data/Bundles/Windows/'+name).lower()]
        assert not f['encrypted'], f['fileName']
        with open(f['chunkAbsolutePath'],'rb') as stream:
            stream.seek(f['offset']); b=stream.read(f['length'])
        assert len(b)==f['length']
        assert hashlib.md5(b).digest()==bytes.fromhex(f['fileDataMd5'])[::-1]
        p=OUT/'bundles'/name;p.parent.mkdir(parents=True,exist_ok=True)
        if p.exists() and p.read_bytes()!=b:raise ValueError('Archive content changed: '+str(p))
        p.write_bytes(b)
        receipts.append(dict(id=bid,file=str(p.relative_to(OUT)),sha256=hashlib.sha256(b).hexdigest(),
                             vfs=f,assets=[a for a in manifest['assets'] if a['bundle']==bid]))
    save(OUT/'selection.json',dict(families=family,bundles=receipts))
    print(json.dumps(dict(bundles=len(receipts),bytes=sum(x['vfs']['length'] for x in receipts),
                          effects={k:v['effectNames'] for k,v in family.items()})))

if __name__=='__main__':main()
