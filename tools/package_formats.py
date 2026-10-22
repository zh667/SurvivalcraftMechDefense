"""Image formats used inside the .scmod (package.py, stage_tower_preview.py).

The 1.9.3.1 engine looks textures up by name and tries .astc, .webp, .png, .jpg in that order, so a texture loaded without
an extension (Towers/<id>/atlas, Towers/fx, Parts/parts, Imported/<unit>/icon) can ship as WebP with no code change.
Lossless WebP keeps every pixel, including the colour under fully transparent pixels (exact=True): the effect sprites are
drawn additively, where that colour still counts. Lossy WebP quality 90 is only for the mech and robot textures
(the user's choice, 2026-10-21).
"""
import io
from pathlib import Path
from PIL import Image


def webp(src, lossless=True, quality=90):
    """PNG file (or bytes) -> WebP bytes."""
    im = Image.open(io.BytesIO(src) if isinstance(src, (bytes, bytearray)) else Path(src))
    im = im.convert('RGBA' if 'A' in im.getbands() else 'RGB')
    out = io.BytesIO()
    if lossless: im.save(out, 'WEBP', lossless=True, quality=100, method=6, exact=True)
    else: im.save(out, 'WEBP', quality=quality, method=6)
    return out.getvalue()
