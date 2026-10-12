"""Byte/pixel/mesh checks and labelled source-model contact sheets, no game execution."""
from pathlib import Path
from collections import Counter
import hashlib, io, json, math, struct
import numpy as np
from PIL import Image,ImageDraw,ImageFont

ROOT=Path(__file__).resolve().parents[1]
OUT=ROOT/'local/dguns/unit-library'


def read(p):return json.loads(p.read_text(encoding='utf-8'))
def sha(p):
    with p.open('rb') as f:return hashlib.file_digest(f,'sha256').hexdigest()


def glb(path):
    b=path.read_bytes();magic,version,size=struct.unpack_from('<3I',b)
    assert magic==0x46546c67 and version==2 and size==len(b),path
    n,t=struct.unpack_from('<2I',b,12);assert t==0x4e4f534a
    j=json.loads(b[20:20+n]);nb,tb=struct.unpack_from('<2I',b,20+n);assert tb==0x004e4942
    data=b[28+n:28+n+nb]
    def accessor(index):
        a=j['accessors'][index];v=j['bufferViews'][a['bufferView']]
        types={5121:'u1',5123:'<u2',5125:'<u4',5126:'<f4'}
        dtype=np.dtype(types[a['componentType']]);width={'SCALAR':1,'VEC2':2,'VEC3':3,'VEC4':4,'MAT4':16}[a['type']]
        start=v.get('byteOffset',0)+a.get('byteOffset',0);stride=v.get('byteStride',width*dtype.itemsize)
        arr=np.ndarray((a['count'],width),dtype=dtype,buffer=data,offset=start,strides=(stride,dtype.itemsize)).copy()
        assert np.isfinite(arr).all()
        return arr
    return j,data,accessor


def contact(entries,path,title):
    cols=5;tw=300;th=345
    im=Image.new('RGB',(cols*tw,100+math.ceil(len(entries)/cols)*th),(29,34,42));draw=ImageDraw.Draw(im)
    font=ImageFont.truetype('C:/Windows/Fonts/msyh.ttc',21);small=ImageFont.truetype('C:/Windows/Fonts/msyh.ttc',15)
    draw.text((20,14),title,font=font,fill='white')
    draw.text((20,51),'原始网格与纹理的绑定姿态预览；不是 SC 实机截图；卡片单独缩放，不代表相对尺寸',font=small,fill=(183,197,208))
    for i,e in enumerate(entries):
        pic=Image.open(OUT/'preview'/f"{e['key']}.png").convert('RGB');pic.thumbnail((290,285))
        x=i%cols*tw;y=100+i//cols*th;im.paste(pic,(x+(tw-pic.width)//2,y))
        draw.text((x+8,y+289),e['label'],font=font,fill='white')
        draw.text((x+8,y+317),e['detail'],font=small,fill=(183,197,208))
    im.save(path)


def main():
    archive=read(OUT/'archive.json');units=read(OUT/'units.json');tables=read(OUT/'tables.json')
    names=read(OUT/'names.zh-CN.json')
    player_names={p['nameEN']:p['nameCN'] for p in names['playerBodies']}
    kind_names={p['kind']:p['nameCN'] for p in names['kinds']}
    assert not units['errors']
    assert sha(Path(archive['sourceApk']))==archive['apkSha256']
    # Independent ABI representation check: these scalar prefixes contain no pointers.
    d32=(OUT/'variants/dat.bin/0001.0.bin').read_bytes();d64=(OUT/'variants/dat.bin/0001.1.bin').read_bytes()
    abi_checks={}
    for name,h32,h64,stride32,stride64,count,prefix in [
        ('playerBodies',84,168,48,56,20,40),('enemies',68,136,64,64,68,64),('attacks',76,152,12,12,19,12),('parts',52,104,36,48,126,28)]:
        p32=struct.unpack_from('<I',d32,h32)[0];p64=struct.unpack_from('<Q',d64,h64)[0]
        assert all(d32[p32+i*stride32:p32+i*stride32+prefix]==d64[p64+i*stride64:p64+i*stride64+prefix] for i in range(count)),name
        abi_checks[name]=count
    # Read instruction bytes directly from the matched ELF, not IDA pseudocode.
    from elftools.elf.elffile import ELFFile
    from capstone import Cs,CS_ARCH_ARM64,CS_MODE_ARM
    native=OUT/'native-input/arm64-v8a/libapp_lib.so'
    assert sha(native)==sha(ROOT/'local/dguns/libapp_lib.so')
    instructions=[]
    with native.open('rb') as stream:
        elf=ELFFile(stream);md=Cs(CS_ARCH_ARM64,CS_MODE_ARM)
        for address,expected in [(0xdd9a0,'#8]'),(0xdd9a4,'#6]'),(0x81b50,'#0xc]'),(0x81b58,'#0x10]'),(0x81b60,'#8]')]:
            section=next(s for s in elf.iter_sections() if s['sh_type']!='SHT_NOBITS' and s['sh_addr']<=address<s['sh_addr']+s['sh_size'])
            code=section.data()[address-section['sh_addr']:address-section['sh_addr']+4]
            ins=next(md.disasm(code,address));assert ins.mnemonic in ('ldr','ldrsh') and expected in ins.op_str,(address,ins.op_str)
            instructions.append(dict(address=hex(address),bytes=code.hex(),mnemonic=ins.mnemonic,operands=ins.op_str))
    verified=0
    for r in archive['entries']:
        for f in [r['stored']]+r['variants']:
            p=OUT/f['file'];assert p.stat().st_size==f['bytes'] and sha(p)==f['sha256'],p
            verified+=1
    for f in archive['nativeInputs']+archive['audio']+archive['independentMotions']+[archive['sourceZip'],archive['sourceMarker']]:
        assert (OUT/f['file']).stat().st_size==f['bytes'] and sha(OUT/f['file'])==f['sha256']
    textures={}
    for p in (OUT/'textures').glob('*.png'):
        with Image.open(p) as im:im.verify()
    models={};results=[]
    for v in units['views']+units['playerViews']:
        mkey=(v['modelId'],v['section'])
        if mkey not in models:models[mkey]=read(OUT/f'models/{mkey[0]:04}/section-{mkey[1]}/model.json')
        m=models[mkey];path=OUT/'views'/v['key']/'model.glb';j,data,access=glb(path)
        expected=np.array([p['p'] for p in m['vertices']],dtype=np.float32)[:,[0,2,1]];expected[:,2]*=-1
        expected_uv=np.array([p['uv'] for p in m['vertices']],dtype=np.float32)
        source_indices=np.array(m['indices'],dtype=np.int32)
        actual=[];uv=[];actual_bones=[];triangles=0
        for mesh in j['meshes']:
            for p in mesh['primitives']:
                pos=access(p['attributes']['POSITION']);inds=access(p['indices']).ravel().astype(int)
                assert len(inds)%3==0 and inds.min()>=0 and inds.max()<len(pos)
                actual.append(pos[inds]);uv.append(access(p['attributes']['TEXCOORD_0'])[inds]);triangles+=len(inds)//3
                weights=access(p['attributes']['WEIGHTS_0']);assert np.allclose(weights.sum(axis=1),1)
                joints=access(p['attributes']['JOINTS_0']).astype(int)
                dominant=joints[np.arange(len(joints)),weights.argmax(axis=1)]
                skin=j['skins'][0]
                bone_names=[j['nodes'][n]['name'] for n in skin['joints']]
                bone_numbers=np.array([int(name.split('_',1)[0]) for name in bone_names])
                actual_bones.append(bone_numbers[dominant][inds])
        assert triangles==len(source_indices)//3,(v['key'],'triangles')
        assert np.allclose(np.concatenate(actual),expected[source_indices],atol=2e-5),(v['key'],'position')
        assert np.allclose(np.concatenate(uv),expected_uv[source_indices],atol=2e-6),(v['key'],'UV')
        assert np.array_equal(np.concatenate(actual_bones),np.array([p['bone'] for p in m['vertices']])[source_indices]),(v['key'],'joint bindings')
        for mat in j.get('materials',[]):
            tex=mat.get('pbrMetallicRoughness',{}).get('baseColorTexture')
            slot=int(mat['name'].rsplit('_',1)[1])
            if not v['textures'] or slot<0:
                assert tex is None;continue
            assert tex is not None,(v['key'],'texture missing')
            image=j['images'][j['textures'][tex['index']]['source']];view=j['bufferViews'][image['bufferView']]
            payload=data[view.get('byteOffset',0):view.get('byteOffset',0)+view['byteLength']]
            decoded=Image.open(io.BytesIO(payload)).convert('RGBA')
            texture_id=v['textures'][slot]
            if texture_id not in textures:textures[texture_id]=Image.open(OUT/f'textures/{texture_id:04}.0.png').convert('RGBA')
            assert decoded.size==textures[texture_id].size and decoded.tobytes()==textures[texture_id].tobytes(),(v['key'],'image pixels')
        results.append(dict(key=v['key'],file=path.relative_to(OUT).as_posix(),sha256=sha(path),triangles=triangles))
    players=[dict(key=f"player-{p['index']+1:02}-{p['name']}-skin0",label=player_names[p['name']],detail=f"{p['name']} · 源HP {p['baseHP']}") for p in tables['playerBodies']]
    contact(players,OUT/'players-overview.png','毁灭枪手 Σ · 20 台主角机甲（默认涂装）')
    bykind={r['id']:r for r in tables['kinds']};enemies=[]
    for kind in sorted({e['kind'] for e in tables['enemies']}):
        rows=[e for e in tables['enemies'] if e['kind']==kind];k=bykind[kind];section=rows[0]['meshSection']
        label=kind_names[k['name']]
        enemies.append(dict(key=f"{k['name']}_s{section}",label=label,detail=f"{k['name']} {'Boss型' if any(e['boss'] for e in rows) else '参数型'} · {len(rows)}条配置"))
    for page in range(0,len(enemies),20):contact(enemies[page:page+20],OUT/f'enemies-overview-{page//20+1}.png','毁灭枪手 Σ · 敌方中文描述名（非官方专名）+源编号')
    verification=dict(stage='archive + PNG + GLB geometry/UV/texture pixel checks passed; NOT original animation playback or SC runtime',
        archiveRecordsVerified=verified,modelContainers=units['counts']['models'],meshSections=len(models),glbCount=len(results),
        playerBodies=len(players),playerPaintVariants=len(units['playerViews']),enemyParameterRows=len(tables['enemies']),
        bossParameterRows=sum(e['boss'] for e in tables['enemies']),unboundModelIds=sorted({v['modelId'] for v in units['views'] if v['kind'] is None}),
        sourceApkSha256=archive['apkSha256'],abiScalarChecks=abi_checks,nativeSha256=sha(native),independentInstructions=instructions,outputs=results)
    verification.update(textures=len(list((OUT/'textures').glob('*.png'))),audio=len(archive['audio']),independentMotionContainers=len(archive['independentMotions']))
    write=lambda p,x:p.write_text(json.dumps(x,ensure_ascii=False,indent=2),encoding='utf-8')
    write(OUT/'verification.json',verification)
    receipt={k:v for k,v in verification.items() if k!='outputs'}
    receipt.update(archiveSha256=sha(OUT/'archive.json'),tablesSha256=sha(OUT/'tables.json'),verificationSha256=sha(OUT/'verification.json'))
    write(ROOT/'docs/dguns-research-receipt.json',receipt)
    print(json.dumps(receipt,ensure_ascii=False,indent=2))


if __name__=='__main__':main()
