"""Verify original archive, cross-ABI scalars, ELF instructions and GLB/pixels."""
from pathlib import Path
from collections import Counter
import hashlib, io, json, struct, zipfile
import numpy as np
import soundfile as sf
from PIL import Image
from elftools.elf.elffile import ELFFile
from capstone import Cs, CS_ARCH_ARM64, CS_MODE_ARM
from archive_dguns_spalpha import ROOT, OUT, write
import verify_dguns_library as shared


def read(name):return json.loads((OUT/name).read_text(encoding='utf-8'))
def sha(path):return shared.sha(path)


def main():
    a=read('archive.json');u=read('units.json');t=read('tables.json');names=read('names.zh-CN.json')
    assert not u['errors']
    assert sha(Path(a['sourceApk']))==a['apkSha256']==sha(OUT/a['retainedInput']['file'])
    records=[a['retainedInput'],a['sourceZip'],a['sourceMarker']]+a['nativeInputs']+a['audio']+a['independentMotions']
    for r in a['entries']:records.extend([r['stored']]+r['variants'])
    for r in records:
        p=OUT/r['file'];assert p.stat().st_size==r['bytes'] and sha(p)==r['sha256'],p
    with zipfile.ZipFile(OUT/a['sourceZip']['file']) as z:
        for bank in z.namelist():assert z.read(bank)==(OUT/'containers'/bank).read_bytes()
    import archive_dguns_library as decoder
    for tex in a['textures']:
        b=(OUT/f"variants/{tex['bank']}/{tex['id']:04}.{tex['variant']}.bin").read_bytes()
        src=decoder.decode_texture(b);im=Image.open(OUT/tex['file']).convert('RGBA')
        assert src.size==im.size and src.tobytes()==im.tobytes()
    audio_checks=[]
    for row in a['audio']:
        path=OUT/row['file']
        samples,rate=sf.read(str(path),dtype='float32',always_2d=True)
        assert len(samples)>0 and np.isfinite(samples).all()
        audio_checks.append(dict(file=row['file'],frames=len(samples),sampleRate=rate,channels=samples.shape[1],seconds=len(samples)/rate))
    # Independent scalar fields in 32-/64-bit companion representations.
    d32=(OUT/'variants/dat.bin/0001.0.bin').read_bytes();d64=(OUT/'variants/dat.bin/0001.1.bin').read_bytes()
    abi={}
    for name,header,stride in [('plbody1',216,28),('plbody3',232,28),('enepara',152,60),('enepara2',168,60),('enepara3',184,60),('attacks',200,12)]:
        p32=struct.unpack_from('<I',d32,header//2)[0];p64=struct.unpack_from('<Q',d64,header)[0]
        count=struct.unpack_from('<Q',d64,header+8)[0]-1
        assert struct.unpack_from('<I',d32,header//2+4)[0]-1==count
        assert d32[p32:p32+count*stride]==d64[p64:p64+count*stride],name
        abi[name]=count
    native=OUT/'native-input/arm64-v8a/libapp_lib.so';insns=[]
    with native.open('rb') as f:
        elf=ELFFile(f)
        def at(address,length):
            sec=next(s for s in elf.iter_sections() if s['sh_type']!='SHT_NOBITS' and s['sh_addr']<=address and address+length<=s['sh_addr']+s['sh_size'])
            return sec.data()[address-sec['sh_addr']:address-sec['sh_addr']+length]
        md=Cs(CS_ARCH_ARM64,CS_MODE_ARM)
        for address,op in [(0x780b4,'#0xc]'),(0x780bc,'#0x10]'),(0x780c4,'#0xa]'),(0xb3d00,'#8]'),(0xb3d04,'#6]'),(0x700d4,'#0x14]'),(0x6ff40,'#0x16]'),(0x7595c,'#0x12]')]:
            code=at(address,4);ins=next(md.disasm(code,address))
            assert ins.mnemonic.startswith('ldr') and op in ins.op_str,(address,ins.mnemonic,ins.op_str)
            insns.append(dict(address=hex(address),bytes=code.hex(),mnemonic=ins.mnemonic,operands=ins.op_str))
        banks=list(at(0xe196f,5));bases=list(struct.unpack('<3h',at(0xe1990,6)))
        assert banks==[1,1,0,255,2] and bases==[2000,0,4000]
        ptrs=struct.unpack('<3Q',at(0x1031a8,24))
        banknames=[at(p,16).split(b'\0')[0].decode('ascii') for p in ptrs]
        assert banknames==['dat.bin','grp.bin','grp2.bin']
    results=[];models={};textures={}
    for v in u['views']+u['playerViews']:
        mk=(v['modelId'],v['section'])
        if mk not in models:models[mk]=read(f'models/{mk[0]:04}/section-{mk[1]}/model.json')
        m=models[mk];path=OUT/'views'/v['key']/'model.glb';j,data,access=shared.glb(path)
        expected=np.array([p['p'] for p in m['vertices']],dtype=np.float32)[:,[0,2,1]];expected[:,2]*=-1
        expected_uv=np.array([p['uv'] for p in m['vertices']],dtype=np.float32)
        source_indices=np.array(m['indices'],dtype=np.int32).reshape(-1,3)
        source_bones=np.array([p['bone'] for p in m['vertices']])
        source_slots=np.array([p['textureSlot'] for p in m['faces']]);triangles=0
        for mesh in j['meshes']:
            for p in mesh['primitives']:
                mat=j['materials'][p['material']];slot=int(mat['name'].rsplit('_',1)[1])
                inds=access(p['indices']).ravel().astype(int);pos=access(p['attributes']['POSITION'])
                assert len(inds)%3==0 and inds.min()>=0 and inds.max()<len(pos)
                src=source_indices[source_slots==slot].ravel()
                assert len(src)==len(inds)
                assert np.allclose(pos[inds],expected[src],atol=2e-5),(v['key'],'position')
                assert np.allclose(access(p['attributes']['TEXCOORD_0'])[inds],expected_uv[src],atol=2e-6),(v['key'],'UV')
                weights=access(p['attributes']['WEIGHTS_0']);assert np.allclose(weights.sum(axis=1),1)
                bones=access(p['attributes']['JOINTS_0']).astype(int)
                dominant=bones[np.arange(len(bones)),weights.argmax(axis=1)]
                bone_numbers=np.array([int(j['nodes'][n]['name'].split('_',1)[0]) for n in j['skins'][0]['joints']])
                assert np.array_equal(bone_numbers[dominant][inds],source_bones[src]),(v['key'],'bones')
                triangles+=len(inds)//3
                tex=mat.get('pbrMetallicRoughness',{}).get('baseColorTexture')
                if slot<0 or slot>=len(v['textures']) or v['textures'][slot]<=0:
                    assert tex is None;continue
                assert tex is not None
                im=j['images'][j['textures'][tex['index']]['source']];bv=j['bufferViews'][im['bufferView']]
                payload=data[bv.get('byteOffset',0):bv.get('byteOffset',0)+bv['byteLength']]
                actual=Image.open(io.BytesIO(payload)).convert('RGBA');tid=v['textures'][slot]
                if tid not in textures:textures[tid]=Image.open(OUT/f'textures/{tid:04}.0.png').convert('RGBA')
                assert actual.size==textures[tid].size and actual.tobytes()==textures[tid].tobytes(),(v['key'],'texture pixels')
        assert triangles==len(m['indices'])//3
        results.append(dict(key=v['key'],file=path.relative_to(OUT).as_posix(),sha256=sha(path),triangles=triangles))
    assert len(names['resources'])==len(a['entries'])
    assert all(r['nameCN'] for group in ['resources','parts','kinds','playerBodies'] for r in names[group])
    shared.OUT=OUT
    players=[dict(key=v['key'],label=names['playerBodies'][v['bodyIndex']]['nameCN'],detail=t['playerBodies'][v['bodyIndex']]['kindName']+' · 源HP '+str(t['playerBodies'][v['bodyIndex']]['baseHP'])) for v in u['playerViews']]
    shared.contact(players,OUT/'players-overview-zh.png','毁灭枪手 SPα · 主角中文对应（12条配置，非12种独立外形）')
    labelmap={k['kind']:k['nameCN'] for k in names['kinds']}
    rows=[dict(key=v['key'],label=labelmap[v['kind']],detail=v['key']) for v in u['views'] if v.get('previewGroup')]
    for i in range(0,len(rows),20):shared.contact(rows[i:i+20],OUT/f'resources-overview-zh-{i//20+1}.png','毁灭枪手 SPα · 资源中文描述暂名（非官方专名）')
    bosses=[dict(key=v['key'],label=labelmap[v['kind']],detail=v['key']) for v in u['views'] if v.get('previewGroup')=='boss']
    shared.contact(bosses,OUT/'bosses-overview-zh.png','毁灭枪手 SPα · 七类首领模型（中文描述暂名）')
    pngs=list((OUT/'preview').glob('*.png'))
    for p in pngs:
        with Image.open(p) as im:im.verify()
    verification=dict(stage='offline archive/ABI/ELF/GLB/PNG verification passed; not animation playback or SC runtime',
        sourceApkSha256=a['apkSha256'],archiveRecordsVerified=len(records),counts={**a['counts'],**u['counts'], 'glb':len(results),'previews':len(pngs)},
        abiChecks=abi,audioDecodeChecks=audio_checks,nativeSha256=sha(native),instructions=insns,bankLookup=dict(indices=banks,bases=bases,names=banknames),
        unboundAlternateViews=[v['key'] for v in u['views'] if v.get('unboundTextureSlots')],
        outputs=results,sourceArchiveSha256=sha(OUT/'archive.json'),tablesSha256=sha(OUT/'tables.json'),namesSha256=sha(OUT/'names.zh-CN.json'))
    write(OUT/'verification.json',verification)
    receipt={k:v for k,v in verification.items() if k!='outputs'}
    receipt['verificationSha256']=sha(OUT/'verification.json')
    write(ROOT/'docs/dguns-spalpha-research-receipt.json',receipt)
    artifacts=[]
    for folder in ['models','views','preview','textures','audio','motions']:
        for path in sorted((OUT/folder).rglob('*')):
            if path.is_file():artifacts.append(dict(file=path.relative_to(OUT).as_posix(),bytes=path.stat().st_size,sha256=sha(path)))
    write(OUT/'delivery-manifest.json',dict(files=artifacts,count=len(artifacts)))
    print(json.dumps(receipt,ensure_ascii=False,indent=2))


if __name__=='__main__':main()
