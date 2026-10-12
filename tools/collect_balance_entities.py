"""Inventory explicit XDB stats, retaining template inheritance and source lines.

Not a runtime DB merge: missing/inherited values and scripted changes are not
invented. Nested proprietary asset packs are explicitly outside this scan.
"""
from pathlib import Path
import hashlib
import json
import re
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[1]
FIELDS = {'AttackResilience', 'AttackResilienceFactor', 'WalkSpeed', 'SwimSpeed',
          'FlySpeed', 'AttackPower', 'Category', 'DisplayName', 'Health', 'Hitpoints',
          'BoxSize', 'HealthRegenerationRate', 'Class'}


def main():
    rows, errors = [], []
    mods = json.loads((ROOT/'local/balance-research/mod-audit.json').read_text(encoding='utf-8'))
    for mod in mods:
        for file in mod['files']:
            if not file['entry'].endswith('.xdb'):
                continue
            path = ROOT / file['local']
            try:
                text = path.read_text(encoding='utf-8-sig')
                tree = ET.fromstring(text)
            except (ValueError, ET.ParseError) as exc:
                errors.append(dict(file=str(path), error=str(exc)))
                continue
            for ent in tree.iter('EntityTemplate'):
                components = {}
                for c in ent.findall('MemberComponentTemplate'):
                    vals = {p.get('Name'): p.get('Value') for p in c.findall('Parameter') if p.get('Name') in FIELDS}
                    if vals:
                        components[c.get('Name')] = vals
                if not any('AttackResilience' in v for v in components.values()):
                    continue
                name = ent.get('Name')
                match = re.search(r'<EntityTemplate\b[^>]*\bName="'+re.escape(name)+'"', text)
                rows.append(dict(mod=Path(mod['source']).name, modSha256=mod['sha256'],
                                 template=name, parent=ent.get('InheritanceParent'), components=components,
                                 source=file['local'], sourceSha256=file['sha256'],
                                 line=text[:match.start()].count('\n')+1 if match else None))
    out = ROOT/'local/balance-research/entity-stats.json'
    out.write_text(json.dumps(dict(stage='explicit template fields only; not resolved runtime health', entities=rows, errors=errors), ensure_ascii=False, indent=2), encoding='utf-8')
    for mod in sorted(set(r['mod'] for r in rows)):
        print('\n'+mod)
        for row in rows:
            if row['mod'] == mod:
                flat = {k:v for c in row['components'].values() for k,v in c.items() if k not in {'Class','DisplayName','BoxSize'}}
                print(row['template'], flat)
    print('entities',len(rows),'errors',len(errors))


if __name__ == '__main__':
    main()
