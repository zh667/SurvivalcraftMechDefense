"""Lossless local archive of all Sigma 1.10 BIN entries and compressed segments.

No runtime assets/packages are changed. Shared decoder is invoked read-only;
every compressed segment is retained, unlike the original first-variant probe.
"""
from pathlib import Path
from collections import Counter
import hashlib, io, json, struct, sys, zipfile

ROOT=Path(__file__).resolve().parents[1]
OUT=ROOT/'local/dguns/unit-library'
DECODER=Path('E:/projects/apk-game-workbench/projects/destroy-gunners-s/patches')
sys.dont_write_bytecode=True
sys.path.insert(0,str(DECODER))
from shade_container import entries, unpack, MAGIC
from inspect_textures import decode_texture


def save(path,data):
    path.parent.mkdir(parents=True,exist_ok=True)
    if path.exists() and path.read_bytes()!=data: raise ValueError('Refusing changed archive: '+str(path))
    path.write_bytes(data)
    return dict(file=path.relative_to(OUT).as_posix(),bytes=len(data),sha256=hashlib.sha256(data).hexdigest())


def variants(data):
    if not data.startswith(MAGIC): return [data]
    out=[]; pos=0
    while data[pos:pos+4]==MAGIC:
        size=struct.unpack_from('<I',data,pos+8)[0]
        if size<13 or pos+size>len(data): raise ValueError('Bad compressed extent')
        out.append(unpack(data[pos:pos+size])); pos+=size
    if any(data[pos:]): raise ValueError('Unaccounted nonzero container tail')
    return out


def main():
    cfg=json.loads((ROOT/'local/config.json').read_text(encoding='utf-8'))
    apk=Path(cfg['destroyGunnersApk']); raw=apk.read_bytes()
    assert hashlib.sha256(raw).hexdigest()=='d9ce01e477f22e5c96728ac879ae6200198e2a4b3141abbebf625a868fd8b8b0'
    rows=[]; natives=[]; audio=[]; motions=[]
    with zipfile.ZipFile(io.BytesIO(raw)) as z:
        marker=save(OUT/'containers/bin.tm',z.read('assets/zip/bin.tm'))
        for name in z.namelist():
            if name.endswith('/libapp_lib.so'):
                natives.append(dict(apkEntry=name,**save(OUT/'native-input'/Path(name).parent.name/'libapp_lib.so',z.read(name))))
        source=z.read('assets/zip/bin.zip')
        source_rec=save(OUT/'containers/bin.zip',source)
        with zipfile.ZipFile(io.BytesIO(source)) as res:
            for name in res.namelist():
                payload=res.read(name)
                save(OUT/'containers'/name,payload)
                for ident,offset,size,stored,decoded,error in entries(payload):
                    if error: raise ValueError((name,ident,error))
                    raw_rec=save(OUT/'stored'/name/f'{ident:04}.bin',stored)
                    items=[]
                    for i,data in enumerate(variants(stored)):
                        rec=save(OUT/'variants'/name/f'{ident:04}.{i}.bin',data)
                        rec.update(variant=i,magic=data[:8].hex())
                        if data.startswith(b'SHTXFs'):
                            dest=OUT/'textures'/f'{ident:04}.{i}.png';dest.parent.mkdir(exist_ok=True)
                            im=decode_texture(data);im.save(dest)
                            rec['texture']=dict(file=dest.relative_to(OUT).as_posix(),size=list(im.size),sha256=hashlib.sha256(dest.read_bytes()).hexdigest())
                        if data.startswith(b'OggS'):
                            audio.append(dict(container=name,id=ident,**save(OUT/'audio'/name/f'{ident:04}.ogg',data)))
                        if data.startswith(b'PSMd'):
                            motions.append(dict(id=ident,**save(OUT/'motions'/f'{ident:04}.psmd',data),
                                status='original independent motion container retained; playback not reconstructed',
                                companionSegments=len(variants(stored))-1))
                        items.append(rec)
                    rows.append(dict(container=name,id=ident,offset=offset,stored=raw_rec,variants=items))
    manifest=dict(sourceApk=str(apk),apkSha256=hashlib.sha256(raw).hexdigest(),sourceZip=source_rec,sourceMarker=marker,
        segmentPolicy='Variants include ABI table representations AND format companion payloads; segment1 is not universally an ABI alternative.',
        decoder=[dict(file=str(DECODER/n),sha256=hashlib.sha256((DECODER/n).read_bytes()).hexdigest()) for n in ('shade_container.py','inspect_textures.py')],
        nativeInputs=natives,audio=audio,independentMotions=motions,entries=rows,counts=dict(entries=len(rows),variants=sum(len(r['variants']) for r in rows),containers=dict(Counter(r['container'] for r in rows))))
    (OUT/'archive.json').write_text(json.dumps(manifest,ensure_ascii=False,indent=2),encoding='utf-8')
    print(json.dumps(manifest['counts']))


if __name__=='__main__': main()
