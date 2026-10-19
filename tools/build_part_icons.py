"""Pixel icons of the progression items (MDPartBlock, Mod/MDParts.cs) and their hand-held extrusion meshes.

python tools/build_part_icons.py [--preview <png>]

Output (committed, our own art): Mod/Assets/Parts/parts.png, 128x128 RGBA atlas of 4x4 cells of 32 px (cell = item data,
row-major), and Mod/Assets/Parts/part<N>.bin, one extrusion mesh per item in MDTowerLibrary.IconMesh's format
(int32 vertex count, int32 index count, per vertex x y z u v shade as float32, then int32 indices): every opaque pixel
becomes a 1.5 px thick block (front, back and the outer side faces), unit size, centred, both windings.
Drawn by hand-written primitives on a fixed palette (no generated images). Colours: the walkers' gunmetal and hazard
yellow (Destroy Gunners texture 143/144), vanilla copper and iron, PCB green, energy cyan, blueprint blue.
"""
import struct, sys
from pathlib import Path
from PIL import Image, ImageDraw

ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT / 'Mod/Assets/Parts'
CELL, GRID = 32, 4
K = (26, 28, 33, 255)                     # outline
M0, M1, M2, M3 = (62, 66, 74, 255), (104, 110, 120, 255), (156, 162, 172, 255), (212, 216, 222, 255)   # gunmetal ramp
Y0, Y1 = (176, 138, 24, 255), (232, 196, 52, 255)          # hazard yellow
R0, R1 = (120, 70, 44, 255), (168, 104, 60, 255)           # rust
C0, C1, C2 = (150, 84, 44, 255), (196, 118, 66, 255), (236, 168, 112, 255)    # copper
G0, G1, G2 = (18, 58, 34, 255), (34, 104, 60, 255), (60, 150, 86, 255)       # PCB
AU = (220, 176, 64, 255)                  # gold pins
RED, RED2 = (210, 44, 36, 255), (255, 120, 100, 255)
E0, E1, E2 = (20, 110, 160, 255), (80, 210, 250, 255), (220, 250, 255, 255)   # energy
B0, B1, B2 = (22, 52, 110, 255), (40, 86, 164, 255), (186, 214, 255, 255)     # blueprint
NAMES = ['scrap', 'alloy', 'chip', 'core', 'blueprint', 'beacon', 'kit1', 'kit2', 'kit3']


def cell():
    im = Image.new('RGBA', (CELL, CELL), (0, 0, 0, 0)); return im, ImageDraw.Draw(im)


def outline(im):
    """1 px dark outline around every opaque shape (4-neighbourhood), drawn outside the shape."""
    px = im.load(); w, h = im.size; add = []
    for y in range(h):
        for x in range(w):
            if px[x, y][3] == 0 and any(0 <= x + dx < w and 0 <= y + dy < h and px[x + dx, y + dy][3] and px[x + dx, y + dy] != K
                                        for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1))):
                add.append((x, y))
    for p in add: px[p] = K
    return im


def scrap():
    im, d = cell()
    d.polygon([(4, 22), (9, 13), (15, 15), (17, 24), (12, 28), (5, 27)], fill=M1)          # bent plate
    d.polygon([(4, 22), (9, 13), (10, 15), (6, 22)], fill=M2)
    d.line([(6, 25), (14, 26)], fill=M0)
    d.polygon([(14, 9), (24, 4), (28, 10), (22, 17), (16, 15)], fill=M2)                   # torn armour piece
    d.polygon([(16, 13), (26, 9), (27, 11), (21, 16)], fill=Y1)                             # hazard stripe
    d.line([(18, 14), (25, 10)], fill=Y0)
    d.polygon([(14, 9), (24, 4), (25, 6), (16, 11)], fill=M3)
    d.rectangle([20, 20, 27, 27], fill=M0); d.rectangle([21, 21, 26, 26], fill=R1)          # rusty bracket
    d.rectangle([22, 22, 25, 25], fill=R0); d.point((23, 23), fill=M2)
    d.rectangle([9, 17, 10, 18], fill=M3)                                                    # bolt
    return outline(im)


def alloy():
    im, d = cell()
    d.rectangle([3, 8, 28, 24], fill=M1)
    d.rectangle([3, 8, 28, 9], fill=M3); d.rectangle([3, 8, 4, 24], fill=M2)                 # bevel light (top, left)
    d.rectangle([3, 23, 28, 24], fill=M0); d.rectangle([27, 8, 28, 24], fill=M0)             # bevel shadow
    for y in (12, 15, 18, 21): d.line([(7, y), (24, y)], fill=(118, 124, 134, 255))           # rolling marks
    d.line([(6, 11), (25, 11)], fill=M2)
    for x, y in ((6, 11), (25, 11), (6, 21), (25, 21)): d.rectangle([x, y, x + 1, y + 1], fill=M3); d.point((x + 1, y + 1), fill=M0)
    d.rectangle([12, 14, 19, 18], fill=Y1); d.line([(12, 18), (19, 18)], fill=Y0)            # stamped tag
    return outline(im)


def chip():
    im, d = cell()
    for i in range(5):
        p = 9 + i * 3
        d.rectangle([p, 3, p + 1, 6], fill=AU); d.rectangle([p, 25, p + 1, 28], fill=AU)
        d.rectangle([3, p, 6, p + 1], fill=AU); d.rectangle([25, p, 28, p + 1], fill=AU)
    d.rectangle([6, 6, 25, 25], fill=G1); d.rectangle([6, 6, 25, 7], fill=G2); d.rectangle([6, 24, 25, 25], fill=G0)
    d.line([(8, 10), (12, 10), (12, 13)], fill=AU); d.line([(23, 21), (19, 21), (19, 18)], fill=AU)
    d.line([(8, 22), (11, 22)], fill=AU); d.line([(20, 9), (23, 9)], fill=AU)
    d.rectangle([11, 11, 20, 20], fill=K); d.rectangle([12, 12, 19, 19], fill=M0)          # die
    d.rectangle([14, 14, 17, 17], fill=RED); d.rectangle([14, 14, 15, 15], fill=RED2)       # fire-control core
    return outline(im)


def core():
    im, d = cell()
    d.rectangle([8, 3, 23, 7], fill=M1); d.rectangle([8, 3, 23, 4], fill=M3); d.rectangle([8, 7, 23, 7], fill=M0)
    d.rectangle([8, 24, 23, 28], fill=M1); d.rectangle([8, 24, 23, 25], fill=M2); d.rectangle([8, 28, 23, 28], fill=M0)
    d.rectangle([10, 8, 21, 23], fill=E0); d.rectangle([12, 8, 19, 23], fill=E1); d.rectangle([14, 9, 17, 22], fill=E2)
    for y in (11, 16, 21): d.line([(10, y), (21, y)], fill=E0)                              # coil rings
    d.rectangle([9, 8, 9, 23], fill=M0); d.rectangle([22, 8, 22, 23], fill=M0)              # frame struts
    d.rectangle([5, 12, 6, 19], fill=C1); d.rectangle([25, 12, 26, 19], fill=C1)            # copper terminals
    d.point((5, 12), fill=C2); d.point((25, 12), fill=C2)
    return outline(im)


def blueprint():
    im, d = cell()
    d.polygon([(4, 6), (26, 4), (28, 25), (6, 28)], fill=B1)
    d.polygon([(4, 6), (26, 4), (26, 6), (5, 8)], fill=B2)                                  # curled top edge
    for x in range(8, 26, 4): d.line([(x, 7), (x + 1, 26)], fill=B0)                        # grid
    for y in range(10, 27, 4): d.line([(6, y), (26, y - 1)], fill=B0)
    w = (214, 232, 255, 255)                                                                 # mech outline
    d.rectangle([13, 9, 18, 13], outline=w); d.rectangle([12, 14, 19, 19], outline=w)
    d.line([(12, 15), (9, 18)], fill=w); d.line([(19, 15), (23, 13)], fill=w); d.line([(23, 13), (25, 13)], fill=w)
    d.line([(14, 20), (12, 25)], fill=w); d.line([(17, 20), (19, 25)], fill=w)
    d.rectangle([22, 21, 25, 23], fill=RED)                                                  # red stamp
    return outline(im)


def beacon():
    im, d = cell()
    d.polygon([(8, 28), (12, 18), (14, 18), (11, 28)], fill=M0); d.polygon([(23, 28), (19, 18), (17, 18), (20, 28)], fill=M0)
    d.rectangle([10, 14, 21, 21], fill=M1); d.rectangle([10, 14, 21, 15], fill=M2); d.rectangle([10, 21, 21, 21], fill=M0)
    d.rectangle([12, 17, 19, 19], fill=Y1); d.line([(12, 19), (19, 19)], fill=Y0)
    d.line([(15, 13), (15, 5)], fill=M2); d.line([(16, 13), (16, 5)], fill=M1)              # antenna
    d.rectangle([13, 2, 18, 5], fill=RED); d.rectangle([14, 2, 15, 3], fill=RED2)           # red lamp
    d.point((11, 4), fill=RED2); d.point((20, 4), fill=RED2); d.point((10, 6), fill=RED); d.point((21, 6), fill=RED)   # signal
    return outline(im)


def kit(level):
    im, d = cell()
    d.rectangle([4, 12, 20, 24], fill=M1); d.rectangle([4, 12, 20, 13], fill=M3); d.rectangle([4, 23, 20, 24], fill=M0)
    d.rectangle([20, 15, 29, 19], fill=M2); d.rectangle([20, 15, 29, 15], fill=M3); d.rectangle([20, 19, 29, 19], fill=M0)   # barrel
    d.rectangle([27, 14, 29, 20], fill=M1)                                                   # muzzle
    d.rectangle([7, 24, 10, 28], fill=M0)                                                    # mount
    d.rectangle([7, 15, 12, 20], fill=K); d.rectangle([8, 16, 11, 19], fill=E1 if level == 3 else C1)   # connector
    col = [Y1, (240, 140, 40, 255), RED][level - 1]
    for i in range(level): d.rectangle([5 + i * 5, 4, 8 + i * 5, 9], fill=col)             # rank bars
    return outline(im)


ICONS = [scrap, alloy, chip, core, blueprint, beacon, lambda: kit(1), lambda: kit(2), lambda: kit(3)]


def extrusion(im, n):
    """Pixel extrusion of one cell: per row, runs of opaque pixels become front/back quads; outer edges get side quads."""
    px = im.load(); cx, cy = (n % GRID) * CELL, (n // GRID) * CELL; S = CELL * GRID; t = 1.5 / CELL / 2
    V, I = [], []
    def quad(ps, uvs, shade):
        b = len(V)
        for p, uv in zip(ps, uvs): V.append((*p, *uv, shade))
        I.extend([b, b + 1, b + 2, b, b + 2, b + 3, b, b + 2, b + 1, b, b + 3, b + 2])   # both windings
    def X(x): return (x - CELL / 2) / CELL
    def Y(y): return (CELL / 2 - y) / CELL
    def U(x): return (cx + x) / S
    def W(y): return (cy + y) / S
    op = lambda x, y: 0 <= x < CELL and 0 <= y < CELL and px[x, y][3] > 0
    for y in range(CELL):
        x = 0
        while x < CELL:
            if not op(x, y): x += 1; continue
            x1 = x
            while x1 < CELL and op(x1, y): x1 += 1
            for z, sh in ((t, 1.0), (-t, .78)):
                quad([(X(x), Y(y), z), (X(x1), Y(y), z), (X(x1), Y(y + 1), z), (X(x), Y(y + 1), z)],
                     [(U(x), W(y)), (U(x1), W(y)), (U(x1), W(y + 1)), (U(x), W(y + 1))], sh)
            x = x1
    for y in range(CELL):
        for x in range(CELL):
            if not op(x, y): continue
            uv = [(U(x + .5), W(y + .5))] * 4
            if not op(x, y - 1): quad([(X(x), Y(y), -t), (X(x + 1), Y(y), -t), (X(x + 1), Y(y), t), (X(x), Y(y), t)], uv, .92)
            if not op(x, y + 1): quad([(X(x), Y(y + 1), t), (X(x + 1), Y(y + 1), t), (X(x + 1), Y(y + 1), -t), (X(x), Y(y + 1), -t)], uv, .62)
            if not op(x - 1, y): quad([(X(x), Y(y), -t), (X(x), Y(y), t), (X(x), Y(y + 1), t), (X(x), Y(y + 1), -t)], uv, .72)
            if not op(x + 1, y): quad([(X(x + 1), Y(y), t), (X(x + 1), Y(y), -t), (X(x + 1), Y(y + 1), -t), (X(x + 1), Y(y + 1), t)], uv, .72)
    blob = struct.pack('<ii', len(V), len(I)) + b''.join(struct.pack('<6f', *v) for v in V) + struct.pack('<%di' % len(I), *I)
    return blob, len(V), len(I) // 3


def main():
    OUT.mkdir(parents=True, exist_ok=True)
    atlas = Image.new('RGBA', (CELL * GRID, CELL * GRID), (0, 0, 0, 0))
    for n, f in enumerate(ICONS):
        im = f(); atlas.paste(im, ((n % GRID) * CELL, (n // GRID) * CELL))
        blob, nv, nt = extrusion(im, n); (OUT / f'part{n}.bin').write_bytes(blob)
        print(f'{n} {NAMES[n]:10s} opaque px {sum(1 for p in im.getdata() if p[3]):4d}  mesh {nv} verts {nt} tris')
    atlas.save(OUT / 'parts.png')
    if '--preview' in sys.argv:
        p = Path(sys.argv[sys.argv.index('--preview') + 1]); bg = Image.new('RGBA', atlas.size, (96, 104, 112, 255)); bg.alpha_composite(atlas)
        bg.resize((atlas.width * 6, atlas.height * 6), Image.NEAREST).save(p); print('preview', p)


if __name__ == '__main__':
    main()
