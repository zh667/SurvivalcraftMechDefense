"""Extract retained APK resources with the reviewed local Shade decoder."""
import argparse
import hashlib
import io
import json
from pathlib import Path
import sys
import zipfile

parser = argparse.ArgumentParser()
parser.add_argument('--decoder', type=Path, required=True)
parser.add_argument('--output', type=Path, required=True)
args = parser.parse_args()
sys.dont_write_bytecode = True
sys.path.insert(0,str(args.decoder))
from shade_container import entries
from inspect_textures import decode_texture

root = Path(__file__).resolve().parents[1]
cfg = json.loads((root/'local/config.json').read_text(encoding='utf-8'))
apk = Path(cfg['destroyGunnersApk'])
digest = hashlib.sha256(apk.read_bytes()).hexdigest()
if digest != 'd9ce01e477f22e5c96728ac879ae6200198e2a4b3141abbebf625a868fd8b8b0':
    raise ValueError('Unexpected source APK')
args.output.mkdir(parents=True,exist_ok=True)
report = []
with zipfile.ZipFile(apk) as z:
    with zipfile.ZipFile(io.BytesIO(z.read('assets/zip/bin.zip'))) as resources:
        for container in resources.namelist():
            folder = args.output/container
            folder.mkdir(exist_ok=True)
            for ident, offset, stored, raw, data, error in entries(resources.read(container)):
                if error:
                    raise ValueError((container,ident,error))
                suffix = '.ogg' if data.startswith(b'OggS') else '.bin'
                file = folder/f'{ident:04}{suffix}'
                file.write_bytes(data)
                if data.startswith(b'SHTXFs'):
                    decode_texture(data).save(folder/f'{ident:04}.png')
                report.append(dict(container=container,id=ident,offset=offset,stored=stored,
                                   size=len(data),sha256=hashlib.sha256(data).hexdigest(),header=data[:8].hex()))
(args.output/'manifest.json').write_text(json.dumps(dict(apk_sha256=digest,entries=report),indent=2),encoding='utf-8')
print(f'Extracted {len(report)} verified container entries')
