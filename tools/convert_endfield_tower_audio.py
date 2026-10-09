"""Decode original WEMs and bake deterministic preview mixes of Wwise events.

One child per random container, all layer children. These are source-audio
adaptations, not a Wwise DSP/RTPC emulator. Original stems are also retained.
"""
import hashlib
import subprocess
import numpy as np
import soundfile as sf
from extract_endfield_tower_audio import OUT, ROOT, load, save, parse_hirc_objects

def main():
    catalog=load(OUT/'audio/catalog.json'); exe=ROOT/'local/tools/EndfieldResearch/tools/vgmstream/vgmstream-cli.exe'
    wav=OUT/'audio/wav';wav.mkdir(exist_ok=True)
    for row in catalog['media']:
        p=wav/f"{row['id']}.wav"
        subprocess.run([str(exe),'-i','-o',str(p),str(OUT/row['file'])],check=True,capture_output=True)
        row['wav']=str(p.relative_to(OUT));row['wavSha256']=hashlib.sha256(p.read_bytes()).hexdigest()
        info=sf.info(p);row['seconds']=info.duration;row['sampleRate']=info.samplerate
    output=ROOT/'Mod/Assets/Imported/Endfield/Audio';output.mkdir(parents=True,exist_ok=True)
    for event in catalog['events']:
        graph=event.get('graph');event['mixes']=[]
        if not graph or not graph['sourceMediaIds']:continue
        objects=parse_hirc_objects((OUT/event['bank']).read_bytes())
        containers={r['objectId']:r for r in graph['containerEvidence']}
        sounds={s:row['mediaId'] for row in graph['mediaEvidence'] for s in row.get('soundObjectIds',[])}
        def choose(oid,take):
            if oid in sounds:return [sounds[oid]]
            c=containers.get(oid)
            if not c:raise ValueError(f'Unhandled audio node {oid}')
            if c['objectType']==5:
                choices=c['playlistChildOrder'];return choose(choices[take%len(choices)],take)
            if c['objectType'] not in (7,9):raise ValueError(f'Unhandled audio selector {c}')
            import struct
            n=c['childCount'];children=struct.unpack_from('<'+'I'*n,objects[oid]['data'],c['childrenOffset']+4)
            return [mid for child in children for mid in choose(child,take)]
        for take in range(3):
            mids=[]
            for action in graph['actionEvidence']:
                if action['isRootEventAction'] and action['operation']=='play':mids.extend(choose(action['targetId'],take))
            if not mids:continue
            tracks=[]
            for mid in mids:
                data,rate=sf.read(wav/f'{mid}.wav',always_2d=True);assert rate==48000
                tracks.append(data.mean(axis=1))
            mix=np.zeros(max(len(t) for t in tracks))
            for t in tracks:mix[:len(t)]+=t
            gain=.85/max(.85,float(np.max(np.abs(mix))));mix*=gain
            name=f"{event['name']}_{take}"
            sf.write(output/(name+'.ogg'),mix,48000,format='OGG',subtype='VORBIS')
            preview=OUT/'audio/event-previews'/(name+'.wav');preview.parent.mkdir(exist_ok=True)
            sf.write(preview,mix,48000,subtype='PCM_16')
            event['mixes'].append(dict(path='Imported/Endfield/Audio/'+name,mediaIds=mids,gain=gain,
                                       preview=str(preview.relative_to(OUT)),processing='mono-sum-peak-limit; no Wwise DSP or inherited RTPC'))
    save(OUT/'audio/catalog.json',catalog)
    print('Decoded',len(catalog['media']),'stems; baked',sum(len(e['mixes']) for e in catalog['events']),'event mixes')

if __name__=='__main__':main()
