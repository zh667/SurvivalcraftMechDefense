"""Compare every tower's attack times between two lab runs of `run_isolated.py lab --mode fb184-rate` (Tests/TowerLab/Feedback184.cs).

python tools/compare_fire_rate.py <base lab-result.json> <test lab-result.json> [--expect same|half] [--json out.json]

0.18.4 removed the second cooldown countdown a frame (ComponentMDUnit.Update) and halved every cooldown source in towers.json,
so a tower must attack exactly as in 0.18.3: --expect same (the default) passes when, for every tower, the attacks in the 20 s
after its first one are as many (one more or less only when the last one falls within two frames of the window's end) and the
mean time between them differs by at most one frame (the longest frame of the two runs), and no single gap by more than two.
--expect half is the control run (the fix without the halving): it passes when every tower whose time between attacks is set by
its cooldown attacks about half as often (gaps 1.8-2.2 x); the sprays whose duration is longer than the old cooldown (frost,
flame) are set by their duration and stay the same.
"""
import argparse, json, statistics, sys

WINDOW = 20.0

def load(path):
    with open(path, encoding='utf-8') as f:
        r = json.load(f)
    return r['fb184']['rate']

def window(ev):
    if not ev: return []
    return [t for t in ev if t - ev[0] < WINDOW]

def gaps(ev):
    return [b - a for a, b in zip(ev, ev[1:])]

def main():
    ap = argparse.ArgumentParser(); ap.add_argument('base'); ap.add_argument('test')
    ap.add_argument('--expect', choices=['same', 'half'], default='same'); ap.add_argument('--json')
    a = ap.parse_args(); base, test = load(a.base), load(a.test)
    ids = [k for k in base if isinstance(base[k], dict)]
    rows, failed = [], []
    for tid in ids:
        b, t = base[tid], test.get(tid)
        if not isinstance(t, dict): failed.append(f'{tid}: missing in the test run'); continue
        eb, et = window(b['events']), window(t['events'])
        gb, gt = gaps(eb), gaps(et)
        frame = max(b['dtMax'], t['dtMax'], 1e-3)
        mb = statistics.mean(gb) if gb else float('nan'); mt = statistics.mean(gt) if gt else float('nan')
        n = min(len(gb), len(gt)); worst = max((abs(x - y) for x, y in zip(gb[:n], gt[:n])), default=float('nan'))
        ratio = mt / mb if gb and gt else float('nan')
        drift = max((abs((x - eb[0]) - (y - et[0])) for x, y in zip(eb, et)), default=float('nan'))   # the k-th attack after the first, between the runs
        row = dict(tower=tid, base=len(eb), test=len(et), baseGap=round(mb, 4), testGap=round(mt, 4), ratio=round(ratio, 3),
                   worstGapDiff=round(worst, 4), drift=round(drift, 4), frame=round(frame, 4))
        if a.expect == 'same':
            edge = len(eb) != len(et) and abs(len(eb) - len(et)) == 1 and \
                any(WINDOW - (ev[-1] - ev[0]) < 2 * frame for ev in (eb, et) if ev)
            ok = (len(eb) == len(et) or edge) and abs(mt - mb) <= frame and worst <= 2 * frame
            row['verdict'] = 'same' if ok else 'DIFFERENT'
        else:
            halved = 1.8 <= ratio <= 2.2
            row['verdict'] = 'half rate' if halved else ('same (duration-bound)' if abs(ratio - 1) < .05 else f'ratio {ratio:.2f}')
            ok = True
        rows.append(row)
        if not ok: failed.append(f"{tid}: {row}")
    w = max(len(r['tower']) for r in rows) if rows else 10
    print(f"{'tower':<{w}}  n(base) n(test)  gap(base) gap(test)  ratio  worst  drift  frame  verdict")
    for r in rows:
        print(f"{r['tower']:<{w}}  {r['base']:>7} {r['test']:>7}  {r['baseGap']:>9} {r['testGap']:>9}  {r['ratio']:>5}  {r['worstGapDiff']:>5}  {r['drift']:>5}  {r['frame']:>5}  {r['verdict']}")
    if a.expect == 'half':
        halved = [r for r in rows if r['verdict'] == 'half rate']
        print(f'{len(halved)} of {len(rows)} towers at half the rate')
        if not halved: failed.append('the control run shows no tower at half the rate: the check would not notice the bug')
    if a.json:
        with open(a.json, 'w', encoding='utf-8') as f: json.dump(dict(expect=a.expect, rows=rows, failed=failed), f, ensure_ascii=False, indent=1)
    if failed:
        print('FAILED:'); [print(' ', x) for x in failed]; sys.exit(1)
    print('PASSED')

if __name__ == '__main__':
    main()
