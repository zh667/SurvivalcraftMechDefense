"""Verify archived data and make an explicitly labelled source-icon contact sheet.

This is file integrity/decoding QA, NOT 3D scene assembly or runtime validation.
"""
from pathlib import Path
from collections import Counter
import hashlib
import json
import math
from PIL import Image, ImageDraw, ImageFont
from archive_endfield_industry import MACHINES

ROOT=Path(__file__).resolve().parents[1]
OUT=ROOT/'local/endfield/industry-library'


def digest(path):
    with path.open('rb') as stream:
        return hashlib.file_digest(stream,'sha256').hexdigest()


def main():
    catalog=json.loads((OUT/'catalog.json').read_text(encoding='utf-8'))
    for b in catalog['bundles']:
        path=OUT/b['file']
        assert path.stat().st_size==b['bytes'] and digest(path)==b['sha256'], path
        assert hashlib.md5(path.read_bytes()).digest()==bytes.fromhex(b['vfs']['fileDataMd5'])[::-1],path
    counts={}
    hashes=[]
    empty_meshes=[]
    geometry_meshes=0
    for section in ['converted','object-json','scene-json','config']:
        files=[p for p in (OUT/section).rglob('*') if p.is_file()]
        counts[section]=dict(Counter(p.suffix for p in files))
        for p in files:
            if p.suffix=='.json': json.loads(p.read_text(encoding='utf-8-sig'))
            if p.suffix=='.png':
                with Image.open(p) as im: im.verify()
            if p.suffix=='.obj':
                vertices=0; faces=[]
                mesh_text=p.read_text(encoding='utf-8-sig')
                for line in mesh_text.splitlines():
                    if line.startswith('v '):
                        xyz=[float(n) for n in line.split()[1:4]]
                        assert len(xyz)==3 and all(math.isfinite(n) for n in xyz),p
                        vertices+=1
                    elif line.startswith('f '):
                        faces.append([int(n.split('/')[0]) for n in line.split()[1:]])
                if not vertices:
                    assert '# AnimeStudio empty Mesh' in mesh_text and not faces,p
                    empty_meshes.append(p.relative_to(OUT).as_posix())
                else:
                    assert faces and all(len(f)>=3 and all(1<=i<=vertices for i in f) for f in faces),p
                    geometry_meshes+=1
            hashes.append(dict(file=p.relative_to(OUT).as_posix(),bytes=p.stat().st_size,sha256=digest(p)))
    for name in MACHINES:
        assert list((OUT/'converted/MonoBehaviour').glob(name+'_p*.json')),name
    effects=['P_interactive_boundary_weapontower_01','P_interactive_rectangle_boundary_weapontower_01']
    for name in effects:
        assert list((OUT/'scene-json/GameObject').glob(name+'_p*.json')),name
    for i in range(1,6):
        assert list((OUT/'converted/Texture2D').glob(f'item_proc_battery_{i}_p*.png'))
    manifests={}
    empty_animators=[]
    for name in ['export-manifest.jsonl','scene-manifest.jsonl','renderers.jsonl']:
        rows=[json.loads(x) for x in (OUT/name).read_text(encoding='utf-8-sig').splitlines() if x.strip()]
        assert any(r.get('kind')=='summary' and r.get('complete') for r in rows),name
        if name!='renderers.jsonl':
            base=OUT/('converted' if name=='export-manifest.jsonl' else 'scene-json')
            for r in rows:
                if r.get('kind')!='output': continue
                path=base/r['output']
                if path.exists(): continue
                marker=Path(str(path)+'.empty.json')
                assert r.get('type')=='Animator' and marker.exists(),path
                data=json.loads(marker.read_text(encoding='utf-8-sig'))
                assert data['animeStudio']['kind']=='empty_animator_marker' and data['meshCount']==0,marker
                empty_animators.append(dict(requested=r['output'],marker=marker.relative_to(OUT).as_posix(),reason=data['animeStudio']['reason']))
        manifests[name]=dict(records=len(rows),kinds=dict(Counter(r.get('kind','') for r in rows)),sha256=digest(OUT/name))
    # Shared sprite bundles also export unrelated images: the overview only selects requested IDs.
    names=[('power_diffuser_1','供电桩'),('power_pole_2','中继器'),('power_station_1','热能池'),
           ('miner_2','电驱矿机'),('furnance_1','精炼炉'),('grinder_1','粉碎机'),
           ('shaper_1','塑形机'),('component_mc_1','配件机'),('tools_assebling_mc_1','封装机'),
           ('storager_1','协议储存箱'),('loader_1','存货口'),('unloader_1','取货口')]
    entries=[('image_'+key,label) for key,label in names]+[(f'item_proc_battery_{i}',f'电池 {i}') for i in range(1,6)]
    font=ImageFont.truetype('C:/Windows/Fonts/msyh.ttc',21)
    small=ImageFont.truetype('C:/Windows/Fonts/msyh.ttc',16)
    sheet=Image.new('RGB',(1100,100+math.ceil(len(entries)/5)*210),(26,31,37))
    draw=ImageDraw.Draw(sheet)
    draw.text((25,18),'终末地 · 已提取工业资源图标',font=font,fill='white')
    draw.text((25,52),'原游戏 UI 图标解码预览；不是场景拼装结果，也不是新建方块模型',font=small,fill=(182,192,205))
    selected=[]
    for i,(key,label) in enumerate(entries):
        p=sorted((OUT/'converted/Texture2D').glob(key+'_p*.png'))[0]
        im=Image.open(p).convert('RGBA'); im.thumbnail((190,158))
        x=(i%5)*220; y=100+(i//5)*210
        sheet.paste(im,(x+(220-im.width)//2,y),im)
        draw.text((x+20,y+168),label,font=font,fill='white')
        selected.append(dict(id=key,label=label,source=p.relative_to(OUT).as_posix()))
    sheet.save(OUT/'resource-preview.png')
    verification=dict(status='file integrity and decoding checks passed; NOT scene/runtime validation',
        summary=catalog['summary'],counts=counts,manifests=manifests,requiredBuildingRoots=len(MACHINES),
        confirmedAttackRangeRoots=effects,geometryMeshes=geometry_meshes,emptyMeshMarkers=empty_meshes,
        emptyAnimatorMarkers=empty_animators,previewSources=selected,files=hashes)
    (OUT/'verification.json').write_text(json.dumps(verification,ensure_ascii=False,indent=2),encoding='utf-8')
    audit=json.loads((ROOT/'local/balance-research/mod-audit.json').read_text(encoding='utf-8'))
    receipt=dict(date='2026-10-09',scope='analysis and local extraction only',
        sourceMods=[dict(name=Path(x['source']).name,sha256=x['sha256']) for x in audit],
        runtimeAssemblySha256=digest(Path('D:/下载/[Windows]SurvivalcraftAPI_1.9.3.1/Survivalcraft.dll')),
        endfieldManifestHash=catalog['manifestHash'],archive=catalog['summary'],convertedCounts=counts,
        verificationSha256=digest(OUT/'verification.json'),emptyAnimatorMarkers=len(empty_animators),
        geometryMeshes=geometry_meshes,emptyMeshMarkers=len(empty_meshes),
        exclusions=['no runtime changes','no world changes','no packaging','no industrial scene assembly','no gameplay tests'])
    (ROOT/'docs/industry-research-receipt.json').write_text(json.dumps(receipt,ensure_ascii=False,indent=2),encoding='utf-8')
    print(json.dumps(dict(mods=len(audit),summary=catalog['summary'],counts=counts,manifests=manifests,
        geometryMeshes=geometry_meshes,emptyMeshMarkers=len(empty_meshes)),ensure_ascii=False,indent=2))


if __name__=='__main__':
    main()
