"""Build source-vs-proposal table and simple, explicitly hypothetical SC DPS checks."""
from pathlib import Path
import json

ROOT=Path(__file__).resolve().parents[1]
OUT=ROOT/'local/dguns/unit-library'


def main():
    table=json.loads((OUT/'tables.json').read_text(encoding='utf-8'))
    names=json.loads((OUT/'names.zh-CN.json').read_text(encoding='utf-8'))
    proposal=json.loads((ROOT/'docs/dguns-sc-candidate.json').read_text(encoding='utf-8'))
    cn={p['nameEN']:p['nameCN'] for p in names['playerBodies']}
    assert set(proposal['players'])=={p['name'] for p in table['playerBodies']}
    rows=[]
    for p in table['playerBodies']:
        c=dict(zip(proposal['playerColumns'],proposal['players'][p['name']],strict=True))
        assert 0<c['hp']<=6000 and 0<c['walkSpeed']<=6 and 4<=c['moduleSlots']<=8
        assert all(0<=c[k]<50 for k in ('impactArmorPct','explosionArmorPct','electroArmorPct'))
        rows.append(dict(name=p['name'],nameCN=cn[p['name']],source=dict(hp=p['baseHP'],attack=p['baseAttack'],energy=p['baseEnergy'],slots=p['slots']),candidate=c))
    rifle=proposal['weaponBase']['rifle']
    sustained=rifle['power']*rifle['magazine']/(rifle['magazine']*rifle['interval']+rifle['reload'])
    tower_dps=162.5+480/4.5+640/7
    assumptions=dict(hitRate=.7,activeFraction=.65,postArmorFactor=.8)
    effective=tower_dps*assumptions['hitRate']*assumptions['activeFraction']*assumptions['postArmorFactor']
    result=dict(stage='paper planning only; not runtime balance or TTK verification',players=rows,
        rifle=dict(burstDps=rifle['power']/rifle['interval'],sustainedPlanningDps=sustained,cycleDefinition='N*interval + reload; simplified budget convention'),
        eventExample=dict(towers='1 proposed 暴雨 + 1 proposed 哨戒 + 1 proposed 迫击炮',directDps=tower_dps,assumptions=assumptions,
                          estimatedDps=effective,bossHP=12000,continuousBudgetSeconds=12000/effective,
                          exclusions='first-shot alignment, discrete bursts, target evasion, minRange, terrain, healing, breakdown, source-script phases'))
    (OUT/'sc-paper-analysis.json').write_text(json.dumps(result,ensure_ascii=False,indent=2),encoding='utf-8')
    lines=['# 主角机甲：原始数值与SC候选','',
        '本表未应用到游戏。原始数值是基础配置，不含等级、装备和任务倍率；SC候选是设计，不是换算后的官方数值。','',
        '| 中文 / 原名 | 原HP / 原攻击 | SC耐久 | 行走格/秒 | 武器倍率 | 模块槽 | DU容量 | 阶段 |',
        '|---|---:|---:|---:|---:|---:|---:|---|']
    for r in rows:
        c=r['candidate'];s=r['source']
        lines.append(f"| {r['nameCN']} / {r['name']} | {s['hp']} / {s['attack']} | {c['hp']} | {c['walkSpeed']} | {c['weaponMultiplier']} | {c['moduleSlots']} | {c['energyCapacity']} | T{c['tier']} |")
    lines+=['','三类护甲候选、武器、敌兵/Boss范围和限制见 `dguns-sc-candidate.json`；总设计见 `DGUNS_SC_RESEARCH.md`。','']
    (ROOT/'docs/DGUNS_PLAYER_STATS.md').write_text('\n'.join(lines),encoding='utf-8')
    print(json.dumps(dict(players=len(rows),rifle=result['rifle'],event=result['eventExample']),ensure_ascii=False,indent=2))


if __name__=='__main__':main()
