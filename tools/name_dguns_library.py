"""Join existing accepted local Chinese terminology without editing that project.

Bodies can share a mesh; enemy descriptive names remain explicitly non-official.
"""
from pathlib import Path
import hashlib,json,re,sys
ROOT=Path(__file__).resolve().parents[1]
OUT=ROOT/'local/dguns/unit-library'
TEXT=Path('E:/projects/apk-game-workbench/projects/destroy-gunners-s/text')
PATCHES=TEXT.parent/'patches'
sys.dont_write_bytecode=True;sys.path.insert(0,str(PATCHES))
from extra_localization import ITEM_PREFIXES


def main():
    tables=json.loads((OUT/'tables.json').read_text(encoding='utf-8'))
    glossary=json.loads((TEXT/'items.zh-CN.json').read_text(encoding='utf-8'))
    full=json.loads((TEXT/'full.zh-CN.json').read_text(encoding='utf-8'))
    descriptions=json.loads((TEXT/'story.zh-CN.json').read_text(encoding='utf-8'))
    enemies=json.loads((ROOT/'docs/dguns-enemy-labels.json').read_text(encoding='utf-8'))['labels']
    bodies=[];kinds=[];parts=[]
    for p in tables['playerBodies']:
        bodies.append(dict(index=p['index'],nameEN=p['name'],nameCN=glossary[p['name']],kind=p['kindName'],
            modelId=p['modelId'],section=p['meshSection'],status='existing-local-accepted-translation; not official claim',
            descriptionCN=descriptions.get(str(271+p['index'])),source='items.zh-CN.json:'+p['name']))
    for k in tables['kinds']:
        counterparts=[p['nameCN'] for p in bodies if p['kind']==k['name']]
        cn=enemies.get(k['name'])
        if not cn:continue
        kinds.append(dict(kind=k['name'],nameCN=cn,modelId=k['modelId'],playerCounterparts=counterparts,
            status='local-translated-event-name' if k['name']=='KN_RD17' else 'descriptive-catalog-label-not-official'))
    for p in tables['parts']:
        name=p['nameEN'];cn=glossary.get(name);status='existing-local-translation'
        if cn is None:
            for prefix,replacement in ITEM_PREFIXES.items():
                if name.startswith(prefix):
                    cn=replacement+name[len(prefix):].replace('Lv','等级');break
        if cn is None and name.strip()=='- -':cn='空槽'
        if cn is None:cn=name;status='untranslated-source-retained'
        parts.append(dict(id=p['id'],nameEN=name,nameCN=cn,status=status))
    result=dict(playerBodies=bodies,kinds=kinds,parts=parts,
        note='Do not infer enemy identity or ownership from a shared player mesh. RASALHAGUE follows the existing local name 候.',
        sources=[dict(path=str(TEXT/n),sha256=hashlib.sha256((TEXT/n).read_bytes()).hexdigest()) for n in ('items.zh-CN.json','full.zh-CN.json','story.zh-CN.json')])
    (OUT/'names.zh-CN.json').write_text(json.dumps(result,ensure_ascii=False,indent=2),encoding='utf-8')
    lines=['# 毁灭枪手资源中文名对照','',
        '主角名沿用本机已验收汉化词表，不宣称官方中文名。敌方多数只有原始RD编号，描述名不是找到的专有名；共用模型不等于同一身份。','',
        '## 主角机甲（20台）','','| 中文名 | 原名 | 原内部型号 | 模型/外形段 |','|---|---|---|---|']
    lines += [f"| {p['nameCN']} | {p['nameEN']} | {p['kind']} | {p['modelId']}/{p['section']} |" for p in bodies]
    lines += ['','## 敌方/机体型号','','除恶魔机甲事件名有现有译文，以下中文为目录描述名；没有可靠专有名的项继续保留RD编号。','',
        '| 源型号 | 中文目录名 | 共用外形的玩家名称（不是敌名证据） |','|---|---|---|']
    lines += [f"| {k['kind']} | {k['nameCN']} | {'、'.join(k['playerCounterparts']) or '—'} |" for k in kinds]
    lines += ['','## 武器与部件（126条，含空槽）','','| ID | 中文名 | 源名称 | 状态 |','|---|---|---|---|']
    lines += [f"| {p['id']} | {p['nameCN']} | {p['nameEN']} | {'沿用本地汉化' if p['status']=='existing-local-translation' else '待确认'} |" for p in parts]
    lines+=['','完整来源路径、文件哈希和中文描述在 `local/dguns/unit-library/names.zh-CN.json`。本轮未改动汉化工程及其成品。','']
    (ROOT/'docs/DGUNS_NAMES_ZH.md').write_text('\n'.join(lines),encoding='utf-8')
    index=['# 毁灭枪手 Σ · 本地资源库','',
        '原始网格/贴图/骨骼已转换，动画完整播放与SC移植尚未完成。',
        '中文主角名来自本机已验收汉化；敌方描述名不是官方专名。','',
        '[主角总览](players-overview.png) · [敌方总览1](enemies-overview-1.png) · [敌方总览2](enemies-overview-2.png)',
        '', '[详细设计报告](../../../docs/DGUNS_SC_RESEARCH.md)',
        '', '## 主角（点击模型；每台另有skin1/2/3）','']
    for p in bodies:
        folder=f"views/player-{p['index']+1:02}-{p['nameEN']}-skin0"
        index.append(f"- {p['nameCN']} / {p['nameEN']}：[GLB]({folder}/model.glb) · [OBJ]({folder}/model.obj)")
    index+=['','## 敌方型号 / 描述名','']
    for k in kinds:
        index.append(f"- {k['nameCN']}（{k['kind']}）：[GLB](views/{k['kind']}_s0/model.glb)，{k['status']}")
    index+=['','## 原始档案','',
        '- [来源和原件哈希](archive.json) / [数值原表](tables.json) / [中文名来源](names.zh-CN.json)',
        '- [完整验证](verification.json) / [SC纸面分析](sc-paper-analysis.json)',
        '- `audio/`：10首原音乐、32段原音效；文件号不等于已确认播放事件。',
        '- `motions/`与`variants/`：26份独立原始动作及其伴随段；`models/`内另有内嵌采样轨道。',
        '- `unbound-0236`（rd32）、`unbound-0237`（rd33）、`unbound-0260`：原始网格，贴图绑定未知，不是假造模型。','']
    (OUT/'README.md').write_text('\n'.join(index),encoding='utf-8')
    print('named players',len(bodies),'kind labels',len(kinds),'parts',len(parts),'untranslated',sum(p['status'].startswith('untranslated') for p in parts))


if __name__=='__main__':main()
