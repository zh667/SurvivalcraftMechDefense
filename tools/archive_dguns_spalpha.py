"""SP alpha 2.75 lossless archive. Keep both graphic-bank namespaces intact."""
from pathlib import Path
import hashlib, io, json, struct, zipfile
from collections import Counter
import archive_dguns_library as shared

ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT / 'local/dguns-spalpha/unit-library'
APK = Path('E:/projects/apk-game-workbench/local/downloads/destroy-gunners-20261010/DestroyGunnersSPalpha_2.75.apk')
EXPECTED = '40992b63ab295781fcf4273ecf32dceb521e27b2dff9680998643042eee2dd16'


def write(path, obj):
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(obj, ensure_ascii=False, indent=2), encoding='utf-8')


def main():
    raw = APK.read_bytes()
    assert hashlib.sha256(raw).hexdigest() == EXPECTED
    shared.OUT = OUT
    save = shared.save
    retained = save(OUT / 'input/DestroyGunnersSPalpha_2.75.apk', raw)
    rows, natives, audio, motions, textures = [], [], [], [], []
    with zipfile.ZipFile(io.BytesIO(raw)) as z:
        marker = save(OUT/'containers/bin.tm', z.read('assets/zip/bin.tm'))
        for name in z.namelist():
            if name.endswith('/libapp_lib.so'):
                natives.append(dict(apkEntry=name, **save(OUT/'native-input'/Path(name).parent.name/'libapp_lib.so', z.read(name))))
        source = z.read('assets/zip/bin.zip')
        source_rec = save(OUT/'containers/bin.zip', source)
        with zipfile.ZipFile(io.BytesIO(source)) as res:
            for bank in res.namelist():
                assert Path(bank).name == bank
                payload = res.read(bank)
                save(OUT/'containers'/bank, payload)
                for ident, offset, size, stored, decoded, error in shared.entries(payload):
                    if error: raise ValueError((bank, ident, error))
                    raw_rec = save(OUT/'stored'/bank/f'{ident:04}.bin', stored)
                    items = []
                    for i, data in enumerate(shared.variants(stored)):
                        rec = save(OUT/'variants'/bank/f'{ident:04}.{i}.bin', data)
                        rec.update(variant=i, magic=data[:8].hex())
                        if data.startswith(b'SHTXFs'):
                            dest = OUT/'textures'/bank/f'{ident:04}.{i}.png'
                            dest.parent.mkdir(parents=True, exist_ok=True)
                            im = shared.decode_texture(data)
                            buf = io.BytesIO(); im.save(buf, format='PNG')
                            rec['texture'] = dict(size=list(im.size), **save(dest, buf.getvalue()))
                            textures.append(dict(bank=bank, id=ident, variant=i, **rec['texture']))
                        if data.startswith(b'OggS'):
                            audio.append(dict(bank=bank, id=ident, **save(OUT/'audio'/bank/f'{ident:04}.{i}.ogg', data)))
                        if data.startswith(b'PSMd'):
                            motions.append(dict(bank=bank, id=ident, **save(OUT/'motions'/bank/f'{ident:04}.{i}.psmd', data)))
                        items.append(rec)
                    rows.append(dict(container=bank, id=ident, offset=offset, stored=raw_rec, variants=items))
    manifest = dict(sourceApk=str(APK), apkSha256=EXPECTED, retainedInput=retained,
                    sourceZip=source_rec, sourceMarker=marker, nativeInputs=natives,
                    audio=audio, independentMotions=motions, textures=textures, entries=rows,
                    decoder=[dict(file=str(shared.DECODER/n), sha256=hashlib.sha256((shared.DECODER/n).read_bytes()).hexdigest()) for n in ('shade_container.py', 'inspect_textures.py')],
                    counts=dict(entries=len(rows), variants=sum(len(r['variants']) for r in rows),
                                containers=dict(Counter(r['container'] for r in rows)), textures=len(textures), audio=len(audio), motions=len(motions)))
    write(OUT/'archive.json', manifest)
    print(json.dumps(manifest['counts']))


if __name__ == '__main__': main()
