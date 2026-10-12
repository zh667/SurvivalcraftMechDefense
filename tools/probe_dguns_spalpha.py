"""Development probe for exact-build structures; stdout only."""
from pathlib import Path
import struct
import catalog_dguns_units as c
P = Path(__file__).resolve().parents[1]/'local/dguns-spalpha/unit-library'
k=c.Data((P/'variants/dat.bin/0002.1.bin').read_bytes())
names={k.read('<h',o)[0]:k.text(k.ptr(o+8)) for o in range(k.ptr(72),k.ptr(72)+(k.ptr(80)-1)*16,16)}
for i in range(k.ptr(48)-1):
    o=k.ptr(40)+64*i
    print('KIND', names[k.read('<h',o)[0]],k.read('<h',o)[0],k.read('<4h',o+18), 'program',k.b[o+17])
for bank in ['grp.bin','grp2.bin']:
    for f in (P/'variants'/bank).glob('*.0.bin'):
        b=f.read_bytes()
        if b[:4]!=b'PSCf':continue
        d=c.Data(b); sections=[]
        for s in range(b[5]):
            try:
                m=c.mesh(f,s)
                if m:sections.append((s,len(m['indices'])//3))
            except Exception as e:sections.append((s,str(e)))
        print('MODEL',bank,f.name,d.text(d.ptr(112)),sections)
