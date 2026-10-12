"""Chinese correspondence for every SP-alpha resource; separate names from descriptions."""
import json, re
from archive_dguns_spalpha import ROOT, OUT, write

EXTRA = {
 'KN_DUMY':'三角形测试标记', 'KN_RD14B':'双管装甲战车·B型','KN_RD14C':'双管装甲战车·C型',
 'KN_RD15B':'防盾炮台·双贴图型','KN_RD16B':'低座炮台·双贴图型',
 'KN_EN01':'双肩炮步行机兵','KN_EN02':'双旋翼无人机','KN_EN03':'夹臂步行机兵',
 'KN_MR00':'白甲人形机甲','KN_MR01':'背翼人形机甲','KN_MR02':'翼装浮游机甲',
 'KN_BSM00':'重炮人形首领','KN_BSM01':'翼装人形首领','KN_BSM02':'红甲重装首领','KN_BSM03':'蓝甲重装首领',
 'KN_BOS00':'翼式飞行首领','KN_BOS01':'双旋翼飞行首领','KN_BOS02':'角翼恶魔型首领',
 'KN_RD04P2':'突击人形机甲·第二配色','KN_RD12P2':'浮游式试验机甲·第二配色','KN_MR01P2':'人形机甲 MR01·第二配色',
 'KN_BULET':'枪弹','KN_MISSILE':'导弹','KN_BOMB':'爆炸弹体','KN_JET0':'推进器喷焰',
 'KN_ITM_REPIAR':'维修补给','KN_ITM_PARTS':'零件补给','KN_ITM_BONUS':'奖励道具','KN_ITM_BULLET':'机枪弹药补给',
 'KN_ITM_CANNON':'火炮弹药补给','KN_ITM_MISSILE':'导弹补给',
 'KN_OB00':'集装箱·可交互型','KN_OB01':'集装箱·静态型','KN_OB02':'货箱','KN_OB03':'零件箱',
 'KN_OB04':'道具箱','KN_OB05':'储罐','KN_OB06':'墙体','KN_OB07':'爆炸装置',
 'KN_GENBOX':'发生装置','KN_WARP':'传送装置','KN_DOOR1':'门体一型','KN_DOOR2':'门体二型','KN_DOOR3':'门体三型',
}
PART_TERMS = {
 'FIGHTER KNUCKLE':'格斗铁拳', 'KNUCKLE FIGHTER':'铁拳强化',
 'WIDE LASERS':'广域激光炮','RIFLES':'步枪','MISSILE':'导弹','CANNONS':'火炮','LASERS':'激光炮','GRENADE':'榴弹',
 'AT-IMP BARRIER':'抗冲击屏障','AT-ELC BARRIER':'抗电击屏障','AT-EXP BARRIER':'抗爆炸屏障','AT-ALLP BARRIER':'全抗性屏障',
 'HIGH SCOPE LOCK':'多目标锁定','CN-EXP UPGRADE':'火炮爆炸范围强化','CN-SPD UPGRADE':'火炮射速强化',
 'CRIT CHANCE':'暴击率强化','EP-RCVRY-SPD':'能量回复速度','EXT-ALLOY':'强化合金装甲',
 'JAMMING SYSTEM':'干扰系统','MULTI BOOSTER':'多重推进器','HIGH SPEED JUMP':'高速跳跃',
 'MEGA MAGAZINE':'扩容弹匣','ITEM GETTER':'道具拾取范围','ITEM-DROP UP':'道具掉落率',
 'AMMO-DROP UP':'弹药掉落率','PARTS-DROP UP':'零件掉落率','FAST DASH':'高速冲刺','MY-BASE HP':'基地耐久强化',
}


def part_cn(name):
    if name.strip()=='- -':return '空槽'
    for key in sorted(PART_TERMS,key=len,reverse=True):
        if name.startswith(key):
            tail=name[len(key):].strip()
            for en,cn in [('PHY-ATK','物理攻击'),('BLA-ATK','爆炸攻击'),('ELE-ATK','电击攻击'),('DEF','抗性'),('ALL','全抗性'),('HP','耐久'),('Lv','等级'),('UP','提升')]:
                tail=tail.replace(en,cn)
            return (PART_TERMS[key]+' '+tail).strip()
    raise ValueError('Missing Chinese part name: '+name)


def main():
    t=json.loads((OUT/'tables.json').read_text(encoding='utf-8'))
    u=json.loads((OUT/'units.json').read_text(encoding='utf-8'))
    archive=json.loads((OUT/'archive.json').read_text(encoding='utf-8'))
    previous=json.loads((ROOT/'docs/dguns-enemy-labels.json').read_text(encoding='utf-8'))['labels']
    labels={**previous,**EXTRA}
    for base in ['KN_EN01','KN_EN02','KN_EN03']:
        for suffix in ['B','C']:labels[base+suffix]=labels[base]+'·'+suffix+'型'
    kinds=[dict(kind=k['id'],sourceName=k['name'],nameCN=labels[k['name']],modelId=k['modelId'],textures=k['textures'],
                status='中文描述暂名；保留原作内部编号，不宣称官方中文专名') for k in t['kinds']]
    parts=[dict(id=p['id'],nameEN=p['nameEN'],nameJP=p['nameJP'],nameCN=part_cn(p['nameEN']),status='据 SPα 原始部件名翻译') for p in t['parts']]
    players=[dict(index=b['index'],nameEN=b['name'],nameCN=b['nameCN'],kind=b['kindName'],modelId=b['modelId'],section=b['meshSection'],status=b['nameStatus']) for b in t['playerBodies']]
    refs={}
    for k in t['kinds']:
        for ident in [k['modelId']]+[x for x in k['textures'] if x>0]:refs.setdefault(ident,[]).append(labels[k['name']])
    resources=[]
    categories={'dat.bin':'配置与关卡数据','grp.bin':'第一图形库','grp2.bin':'第二图形库','bgm.bin':'背景音乐','se.bin':'音效'}
    for r in archive['entries']:
        bank=r['container']; ident=r['id']; b=bytes.fromhex(r['variants'][0]['magic'])
        if b.startswith(b'PSCf'):category='三维模型'
        elif b.startswith(b'SHTXFs'):category='贴图'
        elif b.startswith(b'PSMd'):category='独立动作容器'
        elif b.startswith(b'PSB8'):category='场景地图容器'
        elif b.startswith(b'PTC'):category='特效容器'
        elif b.startswith(b'OggS'):category=categories[bank]
        else:category=categories[bank]+'载荷'
        global_id=ident+(4000 if bank=='grp2.bin' else 0) if bank in ('grp.bin','grp2.bin') else None
        resources.append(dict(bank=bank,id=ident,nameCN=f'{category} {ident:04}',associatedNamesCN=refs.get(global_id,[]),
                              nameStatus='资源分类/编号描述；未确认的具体音效、地图与动作名不猜测',categoryCN=category,
                              original=r['variants'][0]['file'],variants=[v['file'] for v in r['variants']]))
    result=dict(playerBodies=players,kinds=kinds,parts=parts,resources=resources,
                note='主角沿用本作 α/β 编号。敌兵/Boss 使用中文描述暂名；不把 Σ 机体专名或恶魔机甲身份硬套到本作。',
                categories=categories)
    write(OUT/'names.zh-CN.json',result)
    lines=['# 毁灭枪手 SPα 2.75：中文名与资源对应','',result['note'],'',
           '## 主角机体','', '| 原作名称 | 中文对应 | 内部型号 | 模型/外形段 |','|---|---|---|---|']
    for p in players:lines.append(f"| {p['nameEN']} | {p['nameCN']} | {p['kind']} | {p['modelId']} / {p['section']} |")
    lines+=['','两套各6条主角参数，合计12条；对应4个模型容器、6种模型/贴图组合。二代是参数升级，不能算12个不同模型。普通模式还使用“通用型、试作型、新型”等名称。',
            '', '## 敌兵、首领与其他对象（中文描述暂名）','', '| 原编号 | 中文对应 | 模型 | 贴图 |','|---|---|---|---|']
    for k in kinds:lines.append(f"| {k['sourceName']} | {k['nameCN']} | {k['modelId']} | {k['textures']} |")
    lines+=['','## 武器与部件','', '| ID | 原作英文 | 中文对应 |','|---|---|---|']
    for p in parts:lines.append(f"| {p['id']} | {p['nameEN']} | {p['nameCN']} |")
    lines+=['','音频、贴图、动作、场景、配置的每个原始条目均在 `names.zh-CN.json/resources` 中有中文分类与编号；关联到机体的模型和贴图另有 `associatedNamesCN`。',
            '', '“中文对应”是目录与翻译资料，本轮没有修改 APK 内游戏文本。']
    (ROOT/'docs/DGUNS_SPALPHA_NAMES_ZH.md').write_text('\n'.join(lines)+'\n',encoding='utf-8')
    print(json.dumps(dict(players=len(players),kinds=len(kinds),parts=len(parts),resources=len(resources))))


if __name__=='__main__':main()
