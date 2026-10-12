"""Reproducible paper comparison; no game files or live parameters are modified."""
from pathlib import Path
import json
import math

ROOT=Path(__file__).resolve().parents[1]


def direct_dps(a):
    if a['type']=='heal': return None
    cycle=max(a['interval'],a.get('charge',0)+a.get('duration',0))
    multiplier=1+(a.get('enhancedPower',1)-1)/a['enhancedEvery'] if a.get('enhancedEvery') else 1
    return a['power']*multiplier/cycle


def hit_ttk(a,hp,cap=None):
    """Static unarmored single target, instant first hit, no flight/AOE/DOT/AI.
    Limit the demonstration to instantaneous/direct bolt or snipe attacks.
    """
    if a['type'] not in {'bolt','snipe'}: return None
    total=0
    for shot in range(1,100000):
        strong=a.get('enhancedEvery',0)>0 and shot%a['enhancedEvery']==0
        damage=a['power']*(a.get('enhancedPower',1) if strong else 1)
        total+=min(damage,cap) if cap is not None else damage
        if total>=hp: return round((shot-1)*a['interval'],3)
    raise ValueError('Unreachable target')


def main():
    old=json.loads((ROOT/'tools/towers.json').read_text(encoding='utf-8'))['towers']
    proposals=json.loads((ROOT/'docs/tower-balance-candidate.json').read_text(encoding='utf-8'))['towers']
    assert set(proposals)=={t['id'] for t in old}
    rows=[]
    for t in old:
        before=t['attack']; after=before|proposals[t['id']]
        assert after['interval']>0 and after['range']>after.get('minRange',0)>=0 and after['power']>=0
        rows.append(dict(id=t['id'],name=t['name'],current=before,candidate=after,
            directDpsCurrent=direct_dps(before),directDpsCandidate=direct_dps(after),
            idealTTK={str(h):dict(current=hit_ttk(before,h),candidate=hit_ttk(after,h)) for h in (60,260,1024,1600,3200)},
            cappedTTK={name:dict(hp=hp,capPerHit=cap,current=hit_ttk(before,hp,cap),candidate=hit_ttk(after,hp,cap))
                for name,hp,cap in [('SirenHead',1024,1024*.012),('Fenrir',1600,1600*.05),('Smolder',3200,3200*.012)]}))
    report=dict(status='paper model only; NOT in-game results',
        exclusions=['armor, damage hooks, misses, rotation, flight, first-acquisition delay, crowd control, area damage, DOT'],
        towerSource='tools/towers.json',proposalSource='docs/tower-balance-candidate.json',towers=rows)
    out=ROOT/'local/balance-research/paper-comparison.json'
    out.parent.mkdir(parents=True,exist_ok=True)
    out.write_text(json.dumps(report,ensure_ascii=False,indent=2),encoding='utf-8')
    assert abs(direct_dps(old[1]['attack'])-29.4)<1e-6
    assert hit_ttk({'type':'bolt','power':24,'interval':.6},60)==1.2
    for r in rows:
        print(r['name'],r['directDpsCurrent'],r['directDpsCandidate'],r['cappedTTK']['Fenrir'])
    print('19 candidates validated; paper model only')


if __name__=='__main__':
    main()
