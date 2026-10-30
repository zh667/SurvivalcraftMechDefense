"""0.15.0 damage-type and status icons -> Mod/Assets/Towers/status.png (and docs/status_icons_preview.png).

python tools/build_status_icons.py

A 4 x 4 atlas of 64 px icons (256 x 256), drawn here with PIL shapes at 4x and scaled down (smooth edges). Each icon is a dark
round badge with a ring and a glyph in the element's colour, readable over sky, grass and robots. The slots match MDIcon
(Mod/MDElements.cs): 0 kinetic, 1 explosive, 2 heat, 3 cold, 4 electric, 5 corrosive, 6 slowed, 7 frozen, 8 paralysed,
9 armour down, 10 brittle; 15 is solid white (bars, MDTowerWorld.Bar). Colours match MDElements.Colors.
"""
import math, pathlib, sys
from PIL import Image, ImageDraw

ROOT = pathlib.Path(__file__).resolve().parents[1]
CELL, SS = 64, 4                       # icon size in the atlas, supersampling
S = CELL * SS
COLORS = {                             # MDElements.Colors and the status colours in MDIcon
    'kinetic': (226, 226, 232), 'explosive': (255, 150, 40), 'heat': (255, 84, 48), 'cold': (110, 215, 255),
    'electric': (186, 120, 255), 'corrosive': (120, 228, 80), 'slowed': (150, 200, 255), 'frozen': (200, 238, 255),
    'paralysed': (255, 226, 80), 'armour': (214, 236, 90), 'brittle': (190, 250, 255),
}
ORDER = ['kinetic', 'explosive', 'heat', 'cold', 'electric', 'corrosive', 'slowed', 'frozen', 'paralysed', 'armour', 'brittle']


def lighter(c, k=.55):
    return tuple(int(v + (255 - v) * k) for v in c)


def darker(c, k=.45):
    return tuple(int(v * (1 - k)) for v in c)


def P(*pts):                           # points given on a 0..100 grid
    return [(x * S / 100, y * S / 100) for x, y in pts]


def badge(d, c):
    m = S * .04
    d.ellipse([m, m, S - m, S - m], fill=(14, 16, 22, 215), outline=c + (255,), width=int(S * .06))


def star(cx, cy, r1, r2, n, rot=0):
    pts = []
    for i in range(n * 2):
        r = r1 if i % 2 == 0 else r2
        a = rot + math.pi * i / n
        pts.append((cx + r * math.sin(a), cy - r * math.cos(a)))
    return pts


def glyph(d, name, c):
    hi, lo = lighter(c), darker(c)
    w = int(S * .045)
    if name == 'kinetic':          # a round, pointing up
        d.polygon(P((42, 44), (50, 22), (58, 44)), fill=hi)
        d.rectangle(P((42, 44), (58, 74)), fill=c)
        d.rectangle(P((40, 70), (60, 78)), fill=lo)
    elif name == 'explosive':      # burst
        d.polygon([(x * S / 100, y * S / 100) for x, y in star(50, 50, 34, 15, 8)], fill=c)
        d.polygon([(x * S / 100, y * S / 100) for x, y in star(50, 50, 17, 8, 8, .2)], fill=(255, 226, 110))
    elif name == 'heat':           # flame
        d.polygon(P((50, 18), (66, 42), (70, 60), (62, 76), (50, 81), (38, 76), (30, 60), (35, 44), (42, 52), (44, 36)), fill=c)
        d.polygon(P((50, 44), (59, 60), (57, 72), (50, 76), (43, 72), (41, 61)), fill=(255, 196, 70))
    elif name == 'cold':           # snowflake
        for k in range(3):
            a = k * math.pi / 3
            dx, dy = math.sin(a) * 31, math.cos(a) * 31
            d.line(P((50 - dx, 50 - dy), (50 + dx, 50 + dy)), fill=hi, width=w)
            for sgn in (1, -1):
                bx, by = 50 + sgn * dx * .62, 50 + sgn * dy * .62
                for t in (-.6, .6):
                    ex = bx + sgn * (math.sin(a + t) * 10)
                    ey = by + sgn * (math.cos(a + t) * 10)
                    d.line(P((bx, by), (ex, ey)), fill=hi, width=int(w * .8))
        d.ellipse(P((45, 45), (55, 55)), fill=c)
    elif name == 'electric':       # lightning bolt
        d.polygon(P((56, 16), (32, 54), (48, 54), (42, 84), (68, 42), (52, 42), (60, 16)), fill=c)
        d.polygon(P((54, 26), (40, 50), (52, 50), (49, 66), (60, 46), (49, 46)), fill=(240, 228, 255))
    elif name == 'corrosive':      # drop
        d.polygon(P((50, 16), (68, 50), (32, 50)), fill=c)
        d.ellipse(P((31, 38), (69, 78)), fill=c)
        d.ellipse(P((39, 50), (49, 62)), fill=hi)
    elif name == 'slowed':         # hourglass
        d.polygon(P((32, 20), (68, 20), (54, 50), (68, 80), (32, 80), (46, 50)), fill=c)
        d.polygon(P((40, 70), (60, 70), (64, 76), (36, 76)), fill=lo)
        d.rectangle(P((28, 16), (72, 22)), fill=hi); d.rectangle(P((28, 78), (72, 84)), fill=hi)
    elif name == 'frozen':         # ice block
        d.polygon(P((50, 18), (78, 34), (50, 50), (22, 34)), fill=hi)
        d.polygon(P((22, 34), (50, 50), (50, 82), (22, 66)), fill=c)
        d.polygon(P((78, 34), (50, 50), (50, 82), (78, 66)), fill=darker(c, .25))
    elif name == 'paralysed':      # stars round a head
        for i in range(3):
            a = i * 2 * math.pi / 3 - math.pi / 2
            cx, cy = 50 + math.cos(a) * 20, 52 + math.sin(a) * 20
            d.polygon([(x * S / 100, y * S / 100) for x, y in star(cx, cy, 14, 6, 5)], fill=c)
    elif name == 'armour':         # shield with a crack
        d.polygon(P((50, 16), (76, 26), (72, 58), (50, 84), (28, 58), (24, 26)), fill=c)
        d.line(P((52, 18), (44, 40), (56, 52), (46, 70), (50, 82)), fill=(14, 16, 22), width=int(w * 1.1))
    elif name == 'brittle':        # cracked crystal
        d.polygon(P((50, 14), (72, 44), (50, 86), (28, 44)), fill=c)
        d.polygon(P((50, 14), (72, 44), (50, 50)), fill=hi)
        d.line(P((36, 40), (50, 54), (44, 66), (54, 78)), fill=(30, 60, 80), width=w)
        d.line(P((50, 54), (64, 48)), fill=(30, 60, 80), width=w)


def icon(name):
    im = Image.new('RGBA', (S, S), (0, 0, 0, 0))
    d = ImageDraw.Draw(im)
    c = COLORS[name]
    badge(d, c)
    glyph(d, name, c)
    return im.resize((CELL, CELL), Image.LANCZOS)


def main():
    atlas = Image.new('RGBA', (CELL * 4, CELL * 4), (0, 0, 0, 0))
    for i, n in enumerate(ORDER):
        atlas.paste(icon(n), ((i % 4) * CELL, (i // 4) * CELL))
    atlas.paste(Image.new('RGBA', (CELL, CELL), (255, 255, 255, 255)), (3 * CELL, 3 * CELL))   # slot 15: solid, for bars
    out = ROOT / 'Mod/Assets/Towers/status.png'
    if len(sys.argv) > 1:
        out = pathlib.Path(sys.argv[1])
    out.parent.mkdir(parents=True, exist_ok=True)
    atlas.save(out)
    prev = Image.new('RGBA', (CELL * 4 * 2, CELL * 4 * 2), (90, 140, 60, 255))   # over grass-like green, twice the size
    prev.alpha_composite(atlas.resize((CELL * 8, CELL * 8), Image.NEAREST))
    prev.save(out.with_name('status_preview.png') if len(sys.argv) > 1 else ROOT / 'docs/status_icons_preview.png')
    print(out, atlas.size)


if __name__ == '__main__':
    main()
