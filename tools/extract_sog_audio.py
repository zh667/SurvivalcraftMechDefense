"""Original sounds for the towers of E:/projects/sog-tower-model (0.9.0 round 6: towers rebuilt from a 2D tower-defence game)
-> Mod/Assets/Imported/SoG/Audio/<name>.ogg (ignored, packaged by package.py). The user chose the original sounds on 2026-10-22
("用原游戏的声音"), as the Endfield towers use theirs.

python tools/extract_sog_audio.py

Read-only from the user's APK (checked against its SHA-256). Mono, because only mono sounds are placed in 3D (the Endfield
sounds are mono too); peak-limited to 0.9; OGG Vorbis at the original rate. NAMES grows with each tower type.
"""
import hashlib, io, zipfile
from pathlib import Path
import numpy as np
import soundfile as sf

ROOT = Path(__file__).resolve().parents[1]
APK = Path(r'E:/projects/apk-game-workbench/local/packages/soldiers-of-glory/original/12c77f9c83fb-com.catstudio.sogmw.apk')
APK_SHA256 = '12c77f9c83fb7ed5439a131e74fcd1ab829cfa530ace5042ad38d631c6ae1fad'
OUT = ROOT / 'Mod/Assets/Imported/SoG/Audio'
NAMES = ['mgun0', 'mgun1', 'mgun2', 'ricochet_0', 'ricochet_1', 'ricochet_2']   # 转轮机枪塔 Ⅰ–Ⅲ: fire (one per level), bullet hits


def main():
    data = APK.read_bytes(); assert hashlib.sha256(data).hexdigest() == APK_SHA256, f'{APK} is not the APK the towers were made from'
    z = zipfile.ZipFile(io.BytesIO(data)); OUT.mkdir(parents=True, exist_ok=True)
    for n in NAMES:
        d, rate = sf.read(io.BytesIO(z.read(f'assets/sogmw/rpg/sound/{n}.ogg')), always_2d=True)
        m = d.mean(axis=1); peak = float(np.abs(m).max())
        if peak > .9: m = m * (.9 / peak)
        sf.write(OUT / f'{n}.ogg', m, rate, format='OGG', subtype='VORBIS')
        print(f'{n:12} {rate} Hz  {len(m) / rate:5.2f} s  peak {peak:.2f}  {(OUT / f"{n}.ogg").stat().st_size:>7,} B')


if __name__ == '__main__':
    main()
