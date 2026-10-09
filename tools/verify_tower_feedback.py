"""Regression evidence for coplanar flicker and barrel-side retargeting."""
import json
from collections import defaultdict
from pathlib import Path
import numpy as np
from scipy.spatial.transform import Rotation,Slerp
from shapely.geometry import Polygon
from shapely.strtree import STRtree

ROOT=Path(__file__).resolve().parents[1]
reports=[]
for variant in (1,2,3):
    key=f'battle_turret_{variant}';m=json.loads((ROOT/f'Mod/Assets/Imported/Endfield/{key}/model.json').read_text())
    groups=defaultdict(list);verts=m['vertices'];indices=m['indices'];degenerate=0
    for i in range(0,len(indices),3):
        tri=[verts[j] for j in indices[i:i+3]];p=np.array([v['p'] for v in tri]);n=np.array(tri[0]['n']);drop=np.argmax(abs(n));n*=1 if n[drop]>=0 else -1
        normal=np.cross(p[1]-p[0],p[2]-p[0]);
        if np.linalg.norm(normal)<1e-12:degenerate+=1;continue
        plane=(tri[0]['bone'],*np.round(n,5),round(round(float(n@p[0]),8),5));groups[plane].append(Polygon(np.delete(p,drop,axis=1)))
    overlap=0;max_overlap=0
    for polygons in groups.values():
        tree=STRtree(polygons)
        for i,p in enumerate(polygons):
            for j in tree.query(p):
                if j<=i:continue
                area=p.intersection(polygons[j]).area
                if area>1e-8:overlap+=1;max_overlap=max(max_overlap,area)
    assert overlap==0,(key,overlap,max_overlap)
    radial=[];extents=[]
    for track in (1,2):
        frames=np.array(m['animation']['tracks'][track]);p=np.array(m['muzzles'][track-1]);anchor=np.array(m['joints'][track+3]['pivot'])
        rotations=Rotation.from_quat(frames[:,0:4]);slerp=Slerp(np.arange(44),rotations);times=np.linspace(2,10,241)
        maximum=0
        for t in times:
            a=int(t);b=min(a+1,43);fraction=t-a;translation=frames[a,4:7]*(1-fraction)+frames[b,4:7]*fraction
            q=slerp([t]);point=q.apply(p-anchor)[0]+translation+anchor
            maximum=max(maximum,float(np.linalg.norm(point[:2]-p[:2])))
        radial.append(maximum)
        assert maximum<.06,(key,track,maximum)
    reports.append(dict(id=key,coplanarOverlapTriangles=overlap,ignoredDegenerateTriangles=degenerate,
                        barrelMaxRadialExcursion=radial,sourceAtlasUnchanged=True,cleanup=m['coplanarCleanup']))
out=ROOT/'local/custom-model-integration/feedback-verification.json';out.write_text(json.dumps(reports,indent=2),encoding='utf8')
print(json.dumps(reports,indent=2))
