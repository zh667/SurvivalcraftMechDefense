"""Resolve gun-tower Wwise events to original media with typed v150 HIRC edges."""
import hashlib
import json
from pathlib import Path
import struct
import sys
import numpy as np
from prepare_endfield_three import ROOT, LIB, OUT, load, save

sys.path.insert(0, str(ROOT/'local/tools/EndfieldResearch'))
from scripts.game_data.wwise_package import read_header, read_entry_bytes
from scripts.webui.audio.semantics.hirc_v150 import parse_hirc_objects, traverse_hirc_event, iter_bnk_sections

def decrypt(data, seed):
    padded = data + b'\0' * (-len(data) % 4)
    a = np.frombuffer(padded, dtype='<u4').copy()
    seeds = np.arange(len(a), dtype=np.uint32) + np.uint32(seed & 0xffffffff)
    key = ((seeds & 255) ^ np.uint32(0x9c5a0b29)) * np.uint32(81861667)
    for shift in (8, 16, 24):
        key = (key ^ ((seeds >> shift) & 255)) * np.uint32(81861667)
    return (a ^ key).tobytes()[:len(data)]

def wwise_hash(value):
    n = 2166136261
    for b in value.lower().encode('utf8'):
        n = ((n * 16777619) ^ b) & 0xffffffff
    return n

def main():
    selection = load(OUT/'selection.json')
    events = {}
    for family, row in selection['families'].items():
        for proof in row['skillStringEvidence']:
            for s in proof['strings']:
                if s['value'].startswith('au_'):
                    key=wwise_hash(s['value'])
                    e=events.setdefault(key, dict(id=key, name=s['value'], families=[], evidence=[]))
                    if family not in e['families']:e['families'].append(family)
                    e['evidence'].append(dict(file=proof['file'],offset=s['offset']))
        for role, signed in row['audioEvents'].items():
            if signed:
                key=signed & 0xffffffff
                e=events.setdefault(key, dict(id=key,name=f'cue_{key}',families=[],evidence=[]))
                if family not in e['families']:e['families'].append(family)
                e['evidence'].append(dict(table='AudioBattleBuildings',family=family,role=role))
    # Include projectile cues even where the normal hitscan skill uses different audio.
    for p in (OUT/'json/MonoBehaviour').glob('data_projectile_int_fac_turret_*.json'):
        d=load(p)
        def visit(x):
            if isinstance(x,dict):
                for k,v in x.items():
                    if k.endswith('Sound') and isinstance(v,dict) and v.get('_id'):
                        key=v['_id'] & 0xffffffff
                        e=events.setdefault(key,dict(id=key,name=f'cue_{key}',families=[],evidence=[]))
                        family='battle_turret_'+p.name.split('turret_')[1][0]
                        if family not in e['families']:e['families'].append(family)
                        e['evidence'].append(dict(file=str(p.relative_to(OUT)),role=k))
                    visit(v)
            elif isinstance(x,list):
                for v in x:visit(v)
        visit(d)
    packages=[]; banks=[]; available=set()
    vfs=load(ROOT/'local/endfield/vfs-index.json')
    for f in vfs['files']:
        if f['blockName']!='Audio' or '/Main/' not in f['fileName']:continue
        assert not f['encrypted']
        with open(f['chunkAbsolutePath'],'rb') as stream:
            stream.seek(f['offset']);header=read_header(stream,f['length'])
        packages.append((f,header))
        for entry in header['entries']:
            if entry.sector=='sounds':available.add(entry.key)
            if entry.sector!='banks':continue
            with open(f['chunkAbsolutePath'],'rb') as stream:
                raw=read_entry_bytes(stream,f['offset'],f['length'],entry)
            payload=decrypt(raw,entry.key)
            assert payload[:4]==b'BKHD'
            objects=parse_hirc_objects(payload)
            if not any(key in objects and objects[key]['type']==4 for key in events):continue
            path=OUT/'audio/banks'/f'{entry.key}.bnk';path.parent.mkdir(parents=True,exist_ok=True);path.write_bytes(payload)
            banks.append((f,entry,payload,objects,path))
    media={}; embedded={}
    for f,entry,payload,objects,path in banks:
        sections=dict(iter_bnk_sections(payload));didx=sections.get(b'DIDX',b'');data=sections.get(b'DATA',b'')
        assert len(didx)%12==0
        for at in range(0,len(didx),12):
            mid,off,size=struct.unpack_from('<3I',didx,at);assert off+size<=len(data)
            embedded[mid]=(data[off:off+size],str(path.relative_to(OUT)))
        version=struct.unpack_from('<I',sections[b'BKHD'])[0]
        for key,e in events.items():
            if key not in objects or objects[key]['type']!=4:continue
            proof=traverse_hirc_event(key,objects,available|set(embedded),bank_version=version)
            e['graph']=proof;e['bank']=str(path.relative_to(OUT));e['bankSha256']=hashlib.sha256(payload).hexdigest()
            for mid in proof['sourceMediaIds']:media.setdefault(mid,dict(id=mid,events=[]))['events'].append(key)
    for mid,row in media.items():
        candidates=[]
        if mid in embedded:
            raw,source=embedded[mid];candidates.append((raw,dict(bank=source)))
        for f,header in packages:
            for entry in header['entries']:
                if entry.sector=='sounds' and entry.key==mid:
                    with open(f['chunkAbsolutePath'],'rb') as stream:
                        raw=read_entry_bytes(stream,f['offset'],f['length'],entry)
                    candidates.append((raw,dict(package=f['fileName'],chunk=f['chunkAbsolutePath'],offset=f['offset']+entry.byte_offset,bytes=entry.byte_length)))
        if not candidates:
            row['status']='missing';continue
        # A stream is full-length; an in-bank copy may only be its prefetch prefix.
        raw,source=max(candidates,key=lambda x:len(x[0]))
        payload=raw if raw[:4] in (b'RIFF',b'RIFX') else decrypt(raw,mid)
        assert payload[:4] in (b'RIFF',b'RIFX'), (mid,payload[:8])
        p=OUT/'audio/wem'/f'{mid}.wem';p.parent.mkdir(parents=True,exist_ok=True);p.write_bytes(payload)
        row.update(status='original-wem-extracted',file=str(p.relative_to(OUT)),source=source,sha256=hashlib.sha256(payload).hexdigest())
    save(OUT/'audio/catalog.json',dict(events=list(events.values()),media=list(media.values()),
                                      boundary='Original WEM source media; source event DSP and random selection are not rendered.'))
    print(json.dumps(dict(events=len(events),resolved=sum('graph' in x for x in events.values()),media=len(media),
                          missing=[x['id'] for x in media.values() if x['status']=='missing'])))

if __name__=='__main__':main()
