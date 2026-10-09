"""Original Endfield sounds for every tower family -> Mod/Assets/Imported/Endfield/Audio/<event>_<take>.ogg.

Event names: 'au_' strings in each family's skill data (combat-json), plus conventional names
(au_int_fac_battle_<family>_skill_hit ...) kept only when their Wwise id exists as an event in a bank.
Banks are read from the installed game through the VFS index (read-only); decoded stems and the
catalogue go to local/endfield/tower-library/tower-audio. Mixes: one child per random container,
all layer children, peak-limited mono (no Wwise DSP/RTPC), as tools/convert_endfield_tower_audio.py.
"""
import hashlib, json, struct, subprocess, sys
from pathlib import Path
import numpy as np
import soundfile as sf
sys.dont_write_bytecode = True
from prepare_endfield_three import ROOT, LIB, load, save, strings
from extract_endfield_tower_audio import decrypt, wwise_hash, read_header, read_entry_bytes, parse_hirc_objects, traverse_hirc_event, iter_bnk_sections

OUT = LIB / 'tower-audio'
RUNTIME = ROOT / 'Mod/Assets/Imported/Endfield/Audio'
FAMILIES = ['battle_cannon_1', 'battle_cannon_1_dg002', 'battle_cannon_2', 'battle_debuff_1', 'battle_fog_1', 'battle_frost_1', 'battle_jet_1', 'battle_laser_1',
            'battle_laser_2', 'battle_lightning_1', 'battle_lightning_2', 'battle_medic_1', 'battle_rocket_1', 'battle_shockwave_1', 'battle_sniper_1', 'battle_trap_1']
SUFFIX = ['', '_skill', '_skill_hit', '_skill_hit_bomb', '_skill_bomb', '_skill_fire', '_skill_shoot', '_skill_launch', '_skill_explode', '_skill_explosion', '_skill_charge',
          '_skill_start', '_skill_end', '_skill_loop', '_skill_plus', '_skill_passby', '_hit', '_fire', '_shoot', '_launch', '_attack', '_explode', '_bomb', '_charge',
          '_blast_start', '_blast_end', '_hit_start', '_hit_end', '_skill_hit_start', '_skill_hit_end', '_skill_projhit', '_projectile', '_skill_cast', '_skill_release']

def candidates(fid):
    short = fid.replace('battle_', ''); flat = short.replace('_', '')
    stems = {f'au_int_fac_battle_{short}', f'au_int_fac_battle_{flat}', f'au_int_fac_{short}', f'au_int_fac_battle_{short.split("_")[0]}'}
    return {s + x for s in stems for x in SUFFIX}

def main():
    OUT.mkdir(parents=True, exist_ok=True); events = {}
    def add(name, fid, why):
        key = wwise_hash(name); e = events.setdefault(key, dict(id=key, name=name, families=[], why=[]))
        if fid not in e['families']: e['families'].append(fid)
        if why not in e['why']: e['why'].append(why)
    combat = sorted((LIB / 'combat-json').rglob('*.json'))
    for fid in FAMILIES:
        short = fid.replace('battle_', '')
        for p in combat:
            if short in p.name and (fid != 'battle_cannon_1' or 'dg002' not in p.name):
                for s in strings(p.read_bytes()):
                    if s['value'].startswith('au_'): add(s['value'], fid, 'skill-data')
        for n in candidates(fid): add(n, fid, 'candidate')
    vfs = load(ROOT / 'local/endfield/vfs-index.json'); packages = []; available = set(); banks = []
    for f in vfs['files']:
        if f['blockName'] != 'Audio' or '/Main/' not in f['fileName']: continue
        with open(f['chunkAbsolutePath'], 'rb') as stream:
            stream.seek(f['offset']); header = read_header(stream, f['length'])
        packages.append((f, header))
        for entry in header['entries']:
            if entry.sector == 'sounds': available.add(entry.key)
            if entry.sector != 'banks': continue
            with open(f['chunkAbsolutePath'], 'rb') as stream:
                raw = read_entry_bytes(stream, f['offset'], f['length'], entry)
            payload = decrypt(raw, entry.key)
            if payload[:4] != b'BKHD': continue
            objects = parse_hirc_objects(payload)
            hit = [k for k in events if k in objects and objects[k]['type'] == 4]
            if not hit: continue
            path = OUT / 'banks' / f'{entry.key}.bnk'; path.parent.mkdir(parents=True, exist_ok=True); path.write_bytes(payload)
            banks.append((entry, payload, objects, path))
    media, embedded = {}, {}
    for entry, payload, objects, path in banks:
        sections = dict(iter_bnk_sections(payload)); didx = sections.get(b'DIDX', b''); data = sections.get(b'DATA', b'')
        for at in range(0, len(didx), 12):
            mid, off, size = struct.unpack_from('<3I', didx, at); embedded[mid] = data[off:off + size]
        version = struct.unpack_from('<I', sections[b'BKHD'])[0]
        for key, e in events.items():
            if 'graph' in e or key not in objects or objects[key]['type'] != 4: continue
            proof = traverse_hirc_event(key, objects, available | set(embedded), bank_version=version)
            e['graph'] = proof; e['bank'] = str(path.relative_to(OUT))
            for mid in proof['sourceMediaIds']: media.setdefault(mid, dict(id=mid, events=[]))['events'].append(key)
    found = {k: e for k, e in events.items() if 'graph' in e and e['graph']['sourceMediaIds']}
    exe = ROOT / 'local/tools/EndfieldResearch/tools/vgmstream/vgmstream-cli.exe'
    (OUT / 'wem').mkdir(exist_ok=True); (OUT / 'wav').mkdir(exist_ok=True)
    for mid, row in media.items():
        cands = [embedded[mid]] if mid in embedded else []
        for f, header in packages:
            for entry in header['entries']:
                if entry.sector == 'sounds' and entry.key == mid:
                    with open(f['chunkAbsolutePath'], 'rb') as stream: cands.append(read_entry_bytes(stream, f['offset'], f['length'], entry))
        if not cands: row['status'] = 'missing'; continue
        raw = max(cands, key=len); payload = raw if raw[:4] in (b'RIFF', b'RIFX') else decrypt(raw, mid)
        p = OUT / 'wem' / f'{mid}.wem'; p.write_bytes(payload); w = OUT / 'wav' / f'{mid}.wav'
        subprocess.run([str(exe), '-i', '-o', str(w), str(p)], check=True, capture_output=True)
        row.update(status='ok', sha256=hashlib.sha256(payload).hexdigest(), seconds=sf.info(w).duration)
    RUNTIME.mkdir(parents=True, exist_ok=True)
    for key, e in found.items():
        graph = e['graph']; objects = parse_hirc_objects((OUT / e['bank']).read_bytes())
        containers = {r['objectId']: r for r in graph['containerEvidence']}
        sounds = {s: row['mediaId'] for row in graph['mediaEvidence'] for s in row.get('soundObjectIds', [])}
        def choose(oid, take):
            if oid in sounds: return [sounds[oid]]
            c = containers.get(oid)
            if not c: return []
            if c['objectType'] == 5:
                ch = c['playlistChildOrder']; return choose(ch[take % len(ch)], take) if ch else []
            if c['objectType'] not in (7, 9): return []
            n = c['childCount']; children = struct.unpack_from('<' + 'I' * n, objects[oid]['data'], c['childrenOffset'] + 4)
            return [m for child in children for m in choose(child, take)]
        e['mixes'] = []
        for take in range(3):
            mids = [m for a in graph['actionEvidence'] if a['isRootEventAction'] and a['operation'] == 'play' for m in choose(a['targetId'], take)]
            mids = [m for m in mids if media.get(m, {}).get('status') == 'ok']
            if not mids: continue
            tracks = []
            for m in mids:
                data, rate = sf.read(OUT / 'wav' / f'{m}.wav', always_2d=True)
                if rate != 48000: continue
                tracks.append(data.mean(axis=1))
            if not tracks: continue
            mix = np.zeros(max(len(t) for t in tracks))
            for t in tracks: mix[:len(t)] += t
            mix *= .85 / max(.85, float(np.abs(mix).max()))
            name = f"{e['name']}_{take}"; sf.write(RUNTIME / (name + '.ogg'), mix, 48000, format='OGG', subtype='VORBIS')
            e['mixes'].append(dict(path='Imported/Endfield/Audio/' + name, mediaIds=mids, seconds=len(mix) / 48000))
    save(OUT / 'catalog.json', dict(events=list(found.values()), media=list(media.values()),
                                    boundary='Original WEM media mixed per event (one child per random container); no Wwise DSP/RTPC.'))
    summary = {}
    for e in found.values():
        for fid in e['families']: summary.setdefault(fid, []).append((e['name'], len(e['mixes']), round(max([m['seconds'] for m in e['mixes']] or [0]), 2), e['why']))
    print(json.dumps(summary, ensure_ascii=False, indent=1))

if __name__ == '__main__':
    main()
