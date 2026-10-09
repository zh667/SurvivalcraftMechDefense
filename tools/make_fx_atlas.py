"""Pixel-art sprite atlas for tower effects -> Mod/Assets/Towers/fx.png (8x8 slots, 32 px each).

Each sprite is drawn at 16 px and doubled with nearest sampling, so effects share the blocky look of
the cube models and Survivalcraft's own particles. RGB is white or a grey ramp; colour comes from the
particle's vertex colour. Slot order = MDTowerWorld.S.
"""
from pathlib import Path
import numpy as np
from PIL import Image

ROOT = Path(__file__).resolve().parents[1]
N = 16
yy, xx = np.mgrid[0:N, 0:N] + 0.5
cx = cy = N / 2
r = np.hypot(xx - cx, yy - cy)
rng = np.random.default_rng(20261009)

def q(a, steps=4):
    """Quantise alpha into a few hard steps (pixel-art banding)."""
    a = np.clip(a, 0, 1); return np.ceil(a * steps) / steps * (a > 0.02)

def rgba(alpha, grey=None):
    g = np.ones_like(alpha) if grey is None else grey
    return np.dstack([g, g, g, alpha])

def glow(): return rgba(q(np.clip(1 - r / 8, 0, 1) ** 1.6))
def dot(): return rgba((r < 4.2).astype(float))
def star():
    a = np.maximum(np.clip(1 - np.abs(xx - cx) / 1.6, 0, 1) * (1 - np.abs(yy - cy) / 8), np.clip(1 - np.abs(yy - cy) / 1.6, 0, 1) * (1 - np.abs(xx - cx) / 8))
    d = np.clip(1 - np.abs((xx - cx) - (yy - cy)) / 1.2, 0, 1) * (1 - r / 6) * .7
    d2 = np.clip(1 - np.abs((xx - cx) + (yy - cy)) / 1.2, 0, 1) * (1 - r / 6) * .7
    return rgba(q(np.maximum.reduce([a, d, d2, (1 - r / 3.5)])))
def streak(): return rgba(q(np.clip(1 - np.abs(xx - cx) / 2.2, 0, 1) * np.clip(1 - np.abs(yy - cy) / 8, 0, 1) ** .6))
def smoke(seed):
    g = np.random.default_rng(seed); a = np.zeros((N, N))
    for _ in range(7):
        bx, by, br = g.uniform(4.5, 11.5), g.uniform(4.5, 11.5), g.uniform(2.6, 4.6)
        a = np.maximum(a, 1 - np.hypot(xx - bx, yy - by) / br)
    a = q(np.clip(a * 1.6, 0, 1), 3) * .9
    shade = np.clip(1.05 - (yy / N) * .35 + g.uniform(-.06, .06, (N, N)), .55, 1)   # lighter on top
    return rgba(a, np.round(shade * 6) / 6)
def fire():
    a = np.clip(1 - np.hypot((xx - cx) / 5.2, (yy - cy - 1.5) / 6.8), 0, 1)
    tip = np.clip(1 - np.hypot((xx - cx) / 2.6, (yy - cy + 4) / 3.5), 0, 1)
    a = np.maximum(a, tip * .9); grey = np.clip(.65 + .35 * (1 - r / 8), 0, 1)
    return rgba(q(a, 4), np.round(grey * 5) / 5)
def ring(w): return rgba(q(np.clip(1 - np.abs(r - 6.6) / w, 0, 1), 2))
def plus():
    a = ((np.abs(xx - cx) < 1.6) & (np.abs(yy - cy) < 5.5)) | ((np.abs(yy - cy) < 1.6) & (np.abs(xx - cx) < 5.5))
    return rgba(a.astype(float))
def drop():
    a = np.clip(1 - np.hypot((xx - cx) / 3.2, (yy - cy - 2) / 3.6), 0, 1)
    a = np.maximum(a, np.clip(1 - np.abs(xx - cx) / (1 + np.clip(yy - 3, 0, 9) * .3), 0, 1) * (yy > 3) * (yy < cy + 2))
    return rgba(q(a, 3))
def crystal():
    a = np.zeros((N, N))
    for ang in (0, np.pi / 3, 2 * np.pi / 3):
        u = (xx - cx) * np.cos(ang) + (yy - cy) * np.sin(ang); v = -(xx - cx) * np.sin(ang) + (yy - cy) * np.cos(ang)
        a = np.maximum(a, (np.abs(v) < 1.0) * (np.abs(u) < 7))
    a = np.maximum(a, (np.abs(xx - cx) + np.abs(yy - cy) < 3.2))
    return rgba(a.astype(float))
def bubble(): return rgba(np.maximum(q(np.clip(1 - np.abs(r - 5.5) / 1.3, 0, 1), 2), ((np.hypot(xx - 6, yy - 6) < 1.6) * 1.0)))
def bolt(): return rgba(q(np.clip(1 - np.abs(xx - cx) / (0.6 + 2.6 * np.clip(1 - np.abs(yy - cy) / 8, 0, 1)), 0, 1), 4))
def beam(): return rgba(q(np.clip(1 - np.abs(xx - cx) / 8, 0, 1) ** 1.4 * np.ones((N, N)), 5))
def scorch():
    g = np.random.default_rng(5); a = np.clip(1 - r / 7.6, 0, 1) ** .7
    a *= np.clip(1 + g.uniform(-.35, .2, (N, N)), 0, 1)
    return rgba(q(a, 3), np.full((N, N), .2))
def pixel(): return rgba(((np.abs(xx - cx) < 3) & (np.abs(yy - cy) < 3)).astype(float), np.full((N, N), .85))
def spark(): return rgba(q(np.clip(1 - np.abs(xx - cx) / 1.1, 0, 1) * np.clip(1 - np.abs(yy - cy) / 6, 0, 1), 2))
def arc(): return rgba(q(np.clip(1 - np.abs(r - 6) / 1.2, 0, 1) * (yy < cy), 2))
def puddle():
    g = np.random.default_rng(9); a = np.zeros((N, N))
    for _ in range(6):
        ang = g.uniform(0, 6.28); rr = g.uniform(0, 3); bx, by, br = cx + rr * np.cos(ang), cy + rr * np.sin(ang), g.uniform(2.5, 4)
        a = np.maximum(a, 1 - np.hypot(xx - bx, yy - by) / br)
    a = np.maximum(a, 1 - r / 5.5)
    return rgba(q(np.clip(a * 1.4, 0, 1), 3) * (r < 7.4))

SPRITES = [glow, dot, star, streak, lambda: smoke(1), lambda: smoke(2), fire, lambda: ring(1.4), lambda: ring(.7), plus, drop, crystal, bubble, bolt, beam, scorch, pixel, spark, arc, puddle]

def main():
    atlas = np.zeros((256, 256, 4))
    for i, f in enumerate(SPRITES):
        img = f(); img = img.repeat(2, 0).repeat(2, 1)
        y, x = divmod(i, 8); atlas[y * 32:(y + 1) * 32, x * 32:(x + 1) * 32] = img
    out = ROOT / 'Mod/Assets/Towers'; out.mkdir(parents=True, exist_ok=True)
    Image.fromarray((np.clip(atlas, 0, 1) * 255).round().astype(np.uint8), 'RGBA').save(out / 'fx.png')
    # preview on a dark background, x4
    prev = atlas[..., :3] * atlas[..., 3:] + .12 * (1 - atlas[..., 3:])
    Image.fromarray((prev * 255).astype(np.uint8)).resize((1024, 1024), Image.NEAREST).save(ROOT / 'local/claude-work/fx_preview.png')
    print('fx.png', len(SPRITES), 'sprites')

if __name__ == '__main__':
    main()
