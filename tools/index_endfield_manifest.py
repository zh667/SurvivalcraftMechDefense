"""Index Endfield hgmmap using the published Beyond.ManifestBinary layout.

Format reference: choopk/endfieldcontentgen, retained under local/tools/EndfieldVFS.
Only metadata is decoded here; no game installation files are modified.
"""
import argparse
import brotli
import json
from pathlib import Path
import re
import struct

p=argparse.ArgumentParser()
p.add_argument('manifest',type=Path)
p.add_argument('--output',type=Path,required=True)
a=p.parse_args()
b=brotli.decompress(a.manifest.read_bytes())
def i(o):return struct.unpack_from('<i',b,o)[0]
pos=4
assert struct.unpack_from('<I',b)[0]==0xff11ff11
def string():
    global pos
    n=i(pos);pos+=4
    s=b[pos:pos+n*2].decode('utf-16-le');pos+=n*2
    return s
version=string()
assert struct.unpack_from('<I',b,pos)[0]==0xf1f2f3f4
pos+=4
ident=string();changelist=string()
sections=[]
for _ in range(3):
    n=i(pos);pos+=4;sections.append((pos,n));pos+=n
base=pos+4
def data(o):
    n=i(base+o)
    if n<0 or base+o+4+n>len(b):raise ValueError('Bad data extent')
    return b[base+o+4:base+o+4+n]
def ints(o):
    n=i(base+o)
    return list(struct.unpack_from('<'+'i'*n,b,base+o+4))
bundles=[]
off=sections[2][0]
for idx in range(i(off)):
    row=struct.unpack_from('<6i2q2i',b,off+4+idx*48)
    bundles.append(dict(index=row[0],name=data(row[1]).decode('utf-16-le'),
                        dependencies=ints(row[2]),reverse=ints(row[3]),direct=ints(row[4])))
off,size=sections[0];start=off+4+i(off)*8
assets=[]
for at in range(start,off+size,24):
    h,o,bid,size,pad=struct.unpack_from('<q4i',b,at)
    path=brotli.decompress(data(o)).decode('utf-16-le')
    assets.append(dict(path=path,bundle=bid,size=size))
a.output.parent.mkdir(parents=True,exist_ok=True)
a.output.write_text(json.dumps(dict(version=version,hash=ident,changelist=changelist,bundles=bundles,assets=assets),ensure_ascii=True),encoding='utf-8')
hits=[x for x in assets if re.search('turret|cannon|paotai',x['path'],re.I)]
(a.output.parent/'turret-candidates.json').write_text(json.dumps(hits,indent=2),encoding='utf-8')
print(f'{len(bundles)} bundles; {len(assets)} assets; {len(hits)} turret-name candidates')
print(json.dumps(hits[:30],indent=2))
