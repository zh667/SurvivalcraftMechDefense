"""Build a local sample archive, with a complete asset and hash inventory."""
from pathlib import Path
import hashlib,json,zipfile,shutil,subprocess
root=Path(__file__).resolve().parents[1]
subprocess.run(['dotnet','build',str(root/'Mod/MechDefense.csproj'),'-c','Release','--nologo'],check=True)
assets=root/'Mod/Assets';audio=assets/'Imported/Audio';audio.mkdir(parents=True,exist_ok=True)
shutil.copyfile(root/'local/dguns/assets/se.bin/0002.ogg',audio/'Fire.ogg')
files=[(root/'Mod/bin/Release/net10.0/MechDefense.dll','MechDefense.dll'),(root/'Mod/modinfo.json','modinfo.json'),(assets/'MechDefense.xdb','Assets/MechDefense.xdb')]
files.append((assets/'Animations/catalog.json','Assets/Animations/catalog.json'))
for name in ['Mech','Enemy']:
    data=json.loads((assets/f'Imported/{name}/model.json').read_text())
    assert len(data['indices'])>100 and all(0<=i<len(data['vertices']) for i in data['indices'])
    for f in ['model.json','texture.png']:files.append((assets/f'Imported/{name}/{f}',f'Assets/Imported/{name}/{f}'))
files.append((audio/'Fire.ogg','Assets/Imported/Audio/Fire.ogg'))
for p in sorted((assets/'Imported/Endfield').rglob('*')):
    if p.is_file() and p.suffix in ['.json','.png','.ogg']:files.append((p,'Assets/'+p.relative_to(assets).as_posix()))
manifest=[dict(path=n,bytes=p.stat().st_size,sha256=hashlib.sha256(p.read_bytes()).hexdigest()) for p,n in files]
out=root/'dist';out.mkdir(exist_ok=True)
version=json.loads((root/'Mod/modinfo.json').read_text(encoding='utf-8'))['Version']
package=out/f'MechDefense-{version}.scmod'
with zipfile.ZipFile(package,'w',zipfile.ZIP_DEFLATED) as z:
    for p,n in files:z.write(p,n)
    z.writestr('asset-manifest.json',json.dumps(manifest,indent=2))
(out/'package-manifest.json').write_text(json.dumps(manifest,indent=2))
print('Package:',package,'bytes:',package.stat().st_size)
