"""Remove overlapping coplanar faces from a rigid cuboid mesh without moving it.

Later authored faces (trim/decals) take priority. Earlier faces are cut around
them, with UVs interpolated in their original plane. Atlas pixels are unchanged.
"""
from collections import defaultdict
import numpy as np
from shapely.geometry import Polygon
from shapely import constrained_delaunay_triangles
from shapely.strtree import STRtree

def resolve(vertices):
    groups=defaultdict(list)
    for i in range(0,len(vertices),4):
        face=vertices[i:i+4];p=np.array([v['p'] for v in face]);n=np.array(face[0]['n'])
        drop=int(np.argmax(abs(n)));sign=1 if n[drop]>=0 else -1;n*=sign
        key=(face[0]['bone'],*np.round(n,5),round(round(float(n@p[0]),8),5))
        axes=[j for j in range(3) if j!=drop];poly=Polygon(p[:,axes])
        groups[key].append((i,face,p,n,drop,axes,poly))
    output=[];indices=[];removed=0;cut=0;overlap=0;max_area_error=0
    for rows in groups.values():
        polys=[r[-1] for r in rows];tree=STRtree(polys)
        for row_index,row in enumerate(rows):
            ordinal,face,p,n,drop,axes,poly=row;visible=poly
            for j in tree.query(poly):
                if j<=row_index:continue
                if poly.intersection(polys[j]).area<=1e-10:continue
                overlap+=1;visible=visible.difference(polys[j])
                if visible.is_empty:break
            if visible.is_empty or visible.area<1e-10:removed+=1;continue
            if abs(visible.area-poly.area)<1e-10:
                start=len(output);output+=face;indices += [start,start+1,start+2,start,start+2,start+3];continue
            cut+=1
            # Original quad is affine in position and texture space.
            plane=np.column_stack((p[:,axes],np.ones(4)))
            uvmap=np.linalg.lstsq(plane,np.array([v['uv'] for v in face]),rcond=None)[0]
            plane_offset=float(n@p[0]);polygons=[visible] if visible.geom_type=='Polygon' else [q for q in visible.geoms if q.geom_type=='Polygon']
            area=0
            for part in polygons:
                for tri in constrained_delaunay_triangles(part).geoms:
                    area+=tri.area
                    if tri.area<1e-12:continue
                    start=len(output)
                    coords=list(tri.exterior.coords)[:3]
                    for x,y in coords:
                        pos=np.zeros(3);pos[axes]=[x,y];pos[drop]=(plane_offset-n[axes]@np.array([x,y]))/n[drop]
                        uv=np.array([x,y,1])@uvmap
                        output.append(dict(p=pos.tolist(),n=face[0]['n'],uv=uv.tolist(),bone=face[0]['bone']))
                    indices.extend([start,start+1,start+2])
            max_area_error=max(max_area_error,abs(area-visible.area))
    assert max_area_error<1e-7,max_area_error
    return output,indices,dict(inputFaces=len(vertices)//4,overlapPairsResolved=overlap,removedFaces=removed,clippedFaces=cut,maxProjectedAreaError=max_area_error)
