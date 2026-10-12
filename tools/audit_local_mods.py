"""Read local mod archives for balance research; never load/execute their DLLs.

Third-party contents remain under ignored local/balance-research. The two
standard SC API packaging envelopes follow ModsManager.GetDecipherStream;
unknown formats are recorded as unreadable. Originals are never modified.
"""
from pathlib import Path
import argparse
import hashlib
import json
import zipfile
import io

ROOT = Path(__file__).resolve().parents[1]


def container(source):
    if zipfile.is_zipfile(source):
        return source, 'zip'
    data = source.read_bytes()
    h1 = '有头有脸天才少年,耍猴表演敢为人先'.encode()
    h2 = '修改他人mod请获得原作者授权，否则小心出名！'.encode()
    if data.startswith(h1):
        return io.BytesIO(data[len(h1):][::-1]), 'SC API reverse envelope'
    if data.startswith(h2):
        data = data[len(h2):]
        size = (len(data)+1)//2
        decoded = bytearray(len(data))
        decoded[::2], decoded[1::2] = data[:size], data[size:]
        return io.BytesIO(decoded), 'SC API even/odd envelope'
    return None, 'unknown container; not decoded'


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('--mods', type=Path, default=Path('D:/下载/[Windows]SurvivalcraftAPI_1.9.3.1/Mods'))
    args = ap.parse_args()
    out = ROOT / 'local/balance-research/mods'
    rows = []
    for source in sorted(args.mods.glob('*.scmod')):
        digest = hashlib.file_digest(source.open('rb'), 'sha256').hexdigest()
        row = dict(source=str(source), bytes=source.stat().st_size, sha256=digest, files=[])
        rows.append(row)
        archive_source, row['container'] = container(source)
        if archive_source is None:
            row['status'] = 'unknown container; not decoded'
            continue
        target = out / digest[:12]
        target.mkdir(parents=True, exist_ok=True)
        with zipfile.ZipFile(archive_source) as archive:
            row['entries'] = [e.filename for e in archive.infolist()]
            for e in archive.infolist():
                path = Path(e.filename)
                if e.is_dir() or path.suffix.lower() not in {'.dll', '.xdb', '.csv', '.cr', '.ier', '.xml', '.json'}:
                    continue
                if path.name.lower() in {'box2d.net.dll', 'xamarines.dll'} or e.file_size > 64*1024*1024:
                    continue
                dest = (target / path).resolve()
                if not dest.is_relative_to(target.resolve()):
                    raise ValueError('Unsafe archive path: ' + e.filename)
                payload = archive.read(e)
                dest.parent.mkdir(parents=True, exist_ok=True)
                if dest.exists() and dest.read_bytes() != payload:
                    raise ValueError('Refusing changed output: ' + str(dest))
                dest.write_bytes(payload)
                row['files'].append(dict(entry=e.filename, local=str(dest.relative_to(ROOT)), bytes=len(payload), sha256=hashlib.sha256(payload).hexdigest()))
            row['status'] = 'selected metadata and assemblies extracted; not executed'
        print(source.name, len(row['files']))
    manifest = ROOT / 'local/balance-research/mod-audit.json'
    manifest.parent.mkdir(parents=True, exist_ok=True)
    manifest.write_text(json.dumps(rows, ensure_ascii=False, indent=2), encoding='utf-8')
    print(manifest)


if __name__ == '__main__':
    main()
