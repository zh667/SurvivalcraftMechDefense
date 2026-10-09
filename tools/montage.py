"""montage.py <out.jpg> <cols> <width> <glob...>: tile screenshots with file-name captions."""
import sys, glob, os
from PIL import Image, ImageDraw
out, cols, w = sys.argv[1], int(sys.argv[2]), int(sys.argv[3])
files = [f for g in sys.argv[4:] for f in sorted(glob.glob(g))]
ims = []
for f in files:
    im = Image.open(f).convert('RGB'); h = int(im.height * w / im.width); im = im.resize((w, h), Image.LANCZOS)
    d = ImageDraw.Draw(im); d.rectangle([0, 0, w, 16], fill=(0, 0, 0)); d.text((4, 2), os.path.basename(f)[:60], fill=(255, 255, 0)); ims.append(im)
rows = (len(ims) + cols - 1) // cols; h = ims[0].height
sheet = Image.new('RGB', (cols * w, rows * h), (20, 20, 20))
for i, im in enumerate(ims): sheet.paste(im, ((i % cols) * w, (i // cols) * h))
sheet.save(out, quality=88); print(out, sheet.size, len(ims))
