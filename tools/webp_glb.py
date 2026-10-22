"""Re-encode the PNG textures embedded in a glb as lossy WebP (glTF extension EXT_texture_webp, no PNG fallback).

python tools/webp_glb.py <in.glb> <out.glb> [quality]      (package.py calls webp_glb() on Assets/Imported/*/*.glb)

The mech and robot models carry 1024 / 2048 textures (upscaled from the originals): 28.6 MB of the 0.9.0 package as PNG,
2.7 MB as WebP quality 90 (the user's choice, 2026-10-21). Only the package gets WebP; the source models stay PNG.
The engine reads glTF with SharpGLTF (Texture.PrimaryImage covers EXT_texture_webp, which Engine.Media.
MaterialExtensionManager lists) and decodes the image with ImageSharp, which reads WebP (the game's own textures are WebP).
Buffer views keep their order and stay 4-byte aligned; only the image views change length.
"""
import io, json, struct, sys
from PIL import Image

EXT = 'EXT_texture_webp'


def webp_glb(data, quality=90):
    jl, = struct.unpack_from('<I', data, 12); assert struct.unpack_from('<I', data, 16)[0] == 0x4E4F534A, 'first chunk is not JSON'
    js = json.loads(data[20:20 + jl]); at = 20 + jl
    bl, bt = struct.unpack_from('<II', data, at); assert bt == 0x004E4942, 'second chunk is not BIN'
    blob = data[at + 8:at + 8 + bl]
    images = js.get('images', [])
    if not any(im.get('mimeType') == 'image/png' for im in images): return data
    image_of_view = {im['bufferView']: im for im in images if 'bufferView' in im}
    out = bytearray()
    for k, v in enumerate(js['bufferViews']):
        assert v.get('buffer', 0) == 0, 'one buffer expected'
        chunk = blob[v.get('byteOffset', 0):v.get('byteOffset', 0) + v['byteLength']]
        im = image_of_view.get(k)
        if im is not None and im.get('mimeType') == 'image/png':
            pic = Image.open(io.BytesIO(chunk)); pic = pic.convert('RGBA' if 'A' in pic.getbands() else 'RGB')
            b = io.BytesIO(); pic.save(b, 'WEBP', quality=quality, method=6); chunk = b.getvalue(); im['mimeType'] = 'image/webp'
        while len(out) % 4: out.append(0)
        v['byteOffset'] = len(out); v['byteLength'] = len(chunk); out.extend(chunk)
    for t in js.get('textures', []):
        s = t.get('source')
        if s is not None and images[s].get('mimeType') == 'image/webp':
            t.setdefault('extensions', {})[EXT] = {'source': s}; del t['source']
    for key in ('extensionsUsed', 'extensionsRequired'):
        if EXT not in js.setdefault(key, []): js[key].append(EXT)
    while len(out) % 4: out.append(0)
    js['buffers'][0]['byteLength'] = len(out)
    j = json.dumps(js, separators=(',', ':')).encode(); j += b' ' * (-len(j) % 4)
    return (struct.pack('<III', 0x46546C67, 2, 12 + 8 + len(j) + 8 + len(out)) + struct.pack('<II', len(j), 0x4E4F534A) + j
            + struct.pack('<II', len(out), 0x004E4942) + bytes(out))


if __name__ == '__main__':
    src, dst = sys.argv[1], sys.argv[2]; q = int(sys.argv[3]) if len(sys.argv) > 3 else 90
    d = open(src, 'rb').read(); r = webp_glb(d, q); open(dst, 'wb').write(r); print(src, len(d), '->', len(r))
