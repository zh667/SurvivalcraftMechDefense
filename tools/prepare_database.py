"""Generate deterministic API entity templates, preserving native body/mount/save behavior.

Towers come from Mod/Assets/Towers/towers.json (tools/build_tower_assets.py). The first three gun
towers keep their 0.3.x template names (MDTurret, MDTurret2, MDTurret3) so existing saves load.
MDMech is Mercury, the mech a player boards (since 0.8.0; the 0.5.0 sample mech's template, so old ones come back as
Mercury), drawn by ComponentMDMechModel from Imported/Mercury (tools/build_mech_assets.py). MDEnemy (the Destroy Gunners
sample enemy, removed in 0.6.0) keeps its template unchanged so that old worlds load: ComponentMDUnit removes such entities.
Since 0.9.0 the robot enemies (MDRobot*, MDDevilMech) are creatures on the vanilla AICreature template (see ROBOTS below).
"""
from pathlib import Path
import json,uuid,xml.etree.ElementTree as X
root=Path(__file__).resolve().parents[1]
def guid(s):return str(uuid.uuid5(uuid.NAMESPACE_URL,'local.mechdefense/'+s))
mod=X.Element('Mod');folder=X.SubElement(mod,'Folder',Name='Entities',Guid='5fcdf35f-7001-442c-883e-e0502b15d291')
parents=dict(Unit='b05700ed-7e4e-4679-98f5-b597f421496b',Body='8edd1986-0dcc-424f-893a-491c089b821e',Mount='d0b14127-39ce-4490-af00-fb4111e9c51e',Damage='17bb7b53-159c-4eb3-b4bc-137b10bba88a',Spawn='65268743-4076-4ee3-aefe-f02a54d16b36',OnFire='93590d78-63b9-4cd6-bc41-e66864cbb050',
             Model='cc5a4e72-0df9-4acf-a42e-ecb5a2a7baf4')   # SimpleModel
def entity(key,name):
    e=X.SubElement(folder,'EntityTemplate',Name=name,Guid=guid(key))
    def comp(cname,values):
        node=X.SubElement(e,'MemberComponentTemplate',Name=cname,Guid=guid(key+'/'+cname),InheritanceParent=parents[cname])
        for k,typ,value in values:X.SubElement(node,'Parameter',Name=k,Type=typ,Value=str(value),Guid=guid(key+'/'+cname+'/'+k))
    return comp
# Mercury (MDMech) and the removed sample enemy (MDEnemy, kept only so that old worlds load; see docstring)
# Mercury's armour 3000 (user, 2026-10-17: numbers by the installed mods): above BF2042's tank (2000), below SiyahAkrep's
# armoured car (3200) and Leopard 2 (12000); see docs/MECH.md. 4000 since 0.9.0 round 5 (user, 2026-10-21: too fragile against
# the devil mech, "不要提升太多").
for kind,size,mass,hp in [('Mech','2.4,4.8,2.4',3000,4000),('Enemy','1.6,3.2,1.6',700,260)]:
    comp=entity(kind,'MD'+kind)
    comp('Unit',[('Class','string','Game.ComponentMDUnit'),('LoadOrder','int',100),('Kind','string',kind),('TurretVariant','int',1),('TowerId','string','')])
    comp('Body',[('BoxSize','Vector3',size),('Mass','float',mass),('Density','float',5),('MaxSmoothRiseHeight','float',.8),('WaterSwayAngle','float',0),('WaterTurnSpeed','float',0)])
    comp('Damage',[('Class','string','Game.ComponentMDDamage'),('AttackResilience','float',hp),('FallResilience','float',15),('FireResilience','float',100)])
    comp('Spawn',[('AutoDespawn','bool','False')] if kind=='Mech' else []);comp('OnFire',[])
    if kind=='Mech':
        comp('Mount',[('MountOffset','Vector3','0,2.5,0'),('DismountOffset','Vector3','3,0,0'),('MountAllowToStartRange','float',5)])
        # no TextureOverride: the texture embedded in the glTF is sampled linearly with mipmaps (an override is point-sampled)
        comp('Model',[('Class','string','Game.ComponentMDMechModel'),('ModelName','string','Imported/Mercury/model'),('TextureOverride','string',''),
                      ('BoundingSphereRadius','float',3.6),('CastsShadow','bool','True')])
# Hero mechs (0.9.0 round 5, user 2026-10-21: 水星 < 复仇女神 < 轩辕十四, plan confirmed): Mercury's components with their own model
# (tools/build_hero_assets.py -> Imported/<id>), body box and armour, and the Unit's MechType (ComponentMDUnit -> MDMechType).
for mech_id,size,mass,hp,radius in [('Nemesis','2.4,4.8,2.4',3400,6000,3.7),('Regulus','2.8,5.6,2.8',3800,8000,4.3)]:
    comp=entity('Mech'+mech_id,'MDMech'+mech_id)
    comp('Unit',[('Class','string','Game.ComponentMDUnit'),('LoadOrder','int',100),('Kind','string','Mech'),('TurretVariant','int',1),('TowerId','string',''),('MechType','string',mech_id)])
    comp('Body',[('BoxSize','Vector3',size),('Mass','float',mass),('Density','float',5),('MaxSmoothRiseHeight','float',.8),('WaterSwayAngle','float',0),('WaterTurnSpeed','float',0)])
    comp('Damage',[('Class','string','Game.ComponentMDDamage'),('AttackResilience','float',hp),('FallResilience','float',15),('FireResilience','float',100)])
    comp('Spawn',[('AutoDespawn','bool','False')]);comp('OnFire',[])
    comp('Mount',[('MountOffset','Vector3','0,2.5,0'),('DismountOffset','Vector3','3,0,0'),('MountAllowToStartRange','float',5)])
    comp('Model',[('Class','string','Game.ComponentMDMechModel'),('ModelName','string',f'Imported/{mech_id}/model'),('TextureOverride','string',''),
                  ('BoundingSphereRadius','float',radius),('CastsShadow','bool','True')])
towers=json.loads((root/'Mod/Assets/Towers/towers.json').read_text(encoding='utf-8'))['towers']
legacy={'MDTurret':('Turret',1),'MDTurret2':('Turret2',2),'MDTurret3':('Turret3',3)}
for t in towers:
    key,variant=legacy.get(t['template'],(t['template'],1))
    comp=entity(key,t['template']);w,h,d=t['cells']
    comp('Unit',[('Class','string','Game.ComponentMDUnit'),('LoadOrder','int',100),('Kind','string','Turret'),('TurretVariant','int',variant),('TowerId','string',t['id'])])
    comp('Body',[('Class','string','Game.ComponentMDTowerBody'),('BoxSize','Vector3',f'{w-.1:g},{h:g},{d-.1:g}'),('Mass','float',900+150*w*d),('Density','float',5),('MaxSmoothRiseHeight','float',0),('WaterSwayAngle','float',0),('WaterTurnSpeed','float',0)])
    comp('Damage',[('Class','string','Game.ComponentMDDamage'),('AttackResilience','float',t['hp']),('FallResilience','float',15),('FireResilience','float',100)])
    # Structures must not auto-despawn: SubsystemSpawn would drop them 60 m from the camera and later rebuild
    # them from the template only (facing, anchor and counters lost).
    comp('Spawn',[('AutoDespawn','bool','False')]);comp('OnFire',[])
    comp('Model',[('Class','string','Game.ComponentMDTowerModel'),('ModelName','string',f"Towers/{t['id']}/model"),('TextureOverride','string',f"Towers/{t['id']}/atlas"),
                  ('BoundingSphereRadius','float',round(t['radius'],2)),('CastsShadow','bool','True')])
# Robot enemies (0.9.0, Mod/MDRobots.cs): creatures on the vanilla AICreature template (pathfinding, pilot, behaviour selector,
# loot, health), category LandPredator. Members named like the parent's override it by name; new members name their component
# template. Body box from the unit's unit.json (tools/build_unit_assets.py). One template per skin. Numbers: docs/tasks/2026-10-20-progression-robots.md.
AICREATURE='3f077159-f492-419b-859a-bb051de6339f'
CT=dict(CreatureModel='681a5886-5bff-418a-bf5f-ac84f290a311',Generic='b05700ed-7e4e-4679-98f5-b597f421496b',WalkAround='aebb86ce-4ee6-4ca0-850d-cd60051e1d71')
SCRAP,ALLOY,CHIP,CORE,PRINT='MDPartBlock:0','MDPartBlock:1','MDPartBlock:2','MDPartBlock:3','MDPartBlock:4'
LOOT=dict(normal=[f'{SCRAP};2;4;1',f'{CHIP};1;1;0.05','ExperienceBlock;2;3;1'],
          elite=[f'{SCRAP};4;6;1',f'{CHIP};1;1;0.6',f'{CORE};1;1;0.15','ExperienceBlock;5;8;1'],
          boss=[f'{PRINT};1;1;1',f'{CORE};3;5;1',f'{CHIP};2;3;1',f'{SCRAP};8;12;1','ExperienceBlock;30;40;1'])
ROBOTS=[  # template, kind, skin, name, description, resilience, walk speed, mass, loot
    ('MDRobot05','RD05','model','双管步行机兵','双管机炮点射的步行机兵。夜里出没，也会成群来袭击炮塔。击毁后掉落机兵残骸。',1000,3.4,700,'normal'),
    ('MDRobot05B','RD05','model_b','双管步行机兵','双管机炮点射的步行机兵。夜里出没，也会成群来袭击炮塔。击毁后掉落机兵残骸。',1000,3.4,700,'normal'),
    ('MDRobot05C','RD05','model_c','双管步行机兵','双管机炮点射的步行机兵。夜里出没，也会成群来袭击炮塔。击毁后掉落机兵残骸。',1000,3.4,700,'normal'),
    ('MDRobot06','RD06','model','导弹步行机兵','精英机兵：锁定后齐射追踪导弹（导弹能被甩开）。常掉火控芯片，偶尔掉能量核心。',2400,3.0,1200,'elite'),
    ('MDRobot06B','RD06','model_b','导弹步行机兵','精英机兵：锁定后齐射追踪导弹（导弹能被甩开）。常掉火控芯片，偶尔掉能量核心。',2400,3.0,1200,'elite'),
    ('MDRobot06C','RD06','model_c','导弹步行机兵','精英机兵：锁定后齐射追踪导弹（导弹能被甩开）。常掉火控芯片，偶尔掉能量核心。',2400,3.0,1200,'elite'),
    ('MDRobot07','RD07','model','炮击步行机兵','精英机兵：远程曲射炮击，近身有盲区。常掉火控芯片，偶尔掉能量核心。',2800,2.8,1400,'elite'),
    ('MDRobot07B','RD07','model_b','炮击步行机兵','精英机兵：远程曲射炮击，近身有盲区。常掉火控芯片，偶尔掉能量核心。',2800,2.8,1400,'elite'),
    ('MDRobot07C','RD07','model_c','炮击步行机兵','精英机兵：远程曲射炮击，近身有盲区。常掉火控芯片，偶尔掉能量核心。',2800,2.8,1400,'elite'),
    # 0.14.0: the tank (our own model, tools/stage_v3_units.py), three paint schemes
    ('MDRobot14','RD14','model','双管装甲战车','双管炮塔战车：血厚、走得慢，炮塔自己转向，边走边打，炮弹小范围溅射。开始刷新后第 3 天起出现。击毁后掉落机兵残骸。',2000,3.0,2500,'normal'),
    ('MDRobot14B','RD14','model_b','双管装甲战车','双管炮塔战车：血厚、走得慢，炮塔自己转向，边走边打，炮弹小范围溅射。开始刷新后第 3 天起出现。击毁后掉落机兵残骸。',2000,3.0,2500,'normal'),
    ('MDRobot14C','RD14','model_c','双管装甲战车','双管炮塔战车：血厚、走得慢，炮塔自己转向，边走边打，炮弹小范围溅射。开始刷新后第 3 天起出现。击毁后掉落机兵残骸。',2000,3.0,2500,'normal'),
    # 0.14.0: the drones (our own models); they fly (FLY below)
    ('MDRobot09','RD09','model','旋翼机炮无人机','旋翼无人机：在离地七米左右的空中绕着目标飞，机炮点射。防空炮优先打它，加农炮打不到。开始刷新后第 3 天起出现。击毁后掉落机兵残骸。',600,6,300,'normal'),
    ('MDRobotEN02','EN02','model','双旋翼无人机','精英无人机：双旋翼重型无人机，在离地九米左右的空中盘旋，两门机炮交替扫射。防空炮优先打它，加农炮打不到。常掉火控芯片，偶尔掉能量核心。',2200,5,900,'elite'),
    ('MDRobotEN02B','EN02','model_b','双旋翼无人机','精英无人机：双旋翼重型无人机，在离地九米左右的空中盘旋，两门机炮交替扫射。防空炮优先打它，加农炮打不到。常掉火控芯片，偶尔掉能量核心。',2200,5,900,'elite'),
    ('MDRobotEN02C','EN02','model_c','双旋翼无人机','精英无人机：双旋翼重型无人机，在离地九米左右的空中盘旋，两门机炮交替扫射。防空炮优先打它，加农炮打不到。常掉火控芯片，偶尔掉能量核心。',2200,5,900,'elite'),
    ('MDDevilMech','RD17','model','恶魔机甲','用诱敌信标引来的首领，按附近炮塔数量强化耐久。弱点每 20 秒在冲击、爆炸、能量之间变化，只有弱点伤害全额生效。必掉机甲设计图。',40000,7,4000,'boss'),
    # 0.14.0: four more bosses (our own models, tools/stage_v3_units.py), one of the five at random with the devil mech (MDBoss.Bosses)
    ('MDBoss13','RD13','model','鸟形重装机甲','首领：步行的重装火力平台，双加特林、背炮和六挺机枪，躲起来太久它会跳过来。玩家打它头部的发光镜头伤害更高。和其他首领随机出现，必掉机甲设计图。',30000,2.6,9000,'boss'),
    ('MDBoss21','RD21','model','双光剑飞行机甲','首领：高速飞行的双光剑机甲，会沿地上的红线冲刺砍杀，还会用磁轨炮狙击。看到红线往旁边躲。和其他首领随机出现，必掉机甲设计图。',30000,9,5000,'boss'),
    ('MDBoss08','RD08','model','重装悬浮炮艇','首领：悬浮炮艇，停在大多数炮塔的射程外用轨道炮轰击阵地，还会放出无人机。要主动去追它。和其他首领随机出现，必掉机甲设计图。',30000,4,7000,'boss'),
    ('MDBoss19','RD19','model','重型装甲炮车','首领：从地下钻出的重型炮车，主炮和榴弹炮从远处轰击阵地，收回底座后会开到侧面另一处再升起。升起时绕到背后打，主炮蓄能时集中火力能打断，贴近它会逼它换位置。和其他首领随机出现，必掉机甲设计图。',30000,3,20000,'boss'),
]
BESTIARY={'MDRobot05':3900,'MDRobot06':3901,'MDRobot07':3902,'MDDevilMech':3903,'MDRobot14':3904,'MDRobot09':3905,'MDRobotEN02':3906,
          'MDBoss13':3907,'MDBoss21':3908,'MDBoss08':3909,'MDBoss19':3910}   # bestiary entries (EggTypeIndex), see below
FLY={'RD09','EN02','RD17','RD21','RD08'}   # no walking: they fly through fly orders (the drones ComponentMDRobot.EngageAir, the bosses MDBossState.Fly)
FLY_SPEED={'RD17':16,'RD21':24,'RD08':12}    # a boss's top fly speed (its dash); the drones fly at their speed
# the bosses' body boxes (unit.json suggests the whole footprint; RD13's tail and beak, RD19's gun stick out of these)
BOSS_BOX={'RD13':[3.2,5.0,3.2],'RD21':[4.0,3.6,4.0],'RD08':[4.2,2.9,4.2],'RD19':[5.0,3.6,5.0]}
def unit_box(kind):
    if kind in BOSS_BOX:return BOSS_BOX[kind]
    u=root/f'Mod/Assets/Imported/{kind}/unit.json'
    if u.exists():
        j=json.loads(u.read_text(encoding='utf-8'))
        if j.get('box'):return j['box']
    return {'RD05':[2.0,3.2,2.0],'RD06':[2.6,2.6,2.6],'RD07':[2.4,3.8,2.4],'RD17':[3.0,5.0,3.0]}[kind]
for name,kind,skin,display,desc,hp,speed,mass,loot in ROBOTS:
    e=X.SubElement(folder,'EntityTemplate',Name=name,Guid=guid('robot/'+name),InheritanceParent=AICREATURE)
    def member(cname,values,parent=None,sets=None):
        attrs=dict(Name=cname,Guid=guid(f'robot/{name}/{cname}'))
        if parent:attrs['InheritanceParent']=parent
        node=X.SubElement(e,'MemberComponentTemplate',**attrs)
        for k,typ,value in values:X.SubElement(node,'Parameter',Name=k,Type=typ,Value=str(value),Guid=guid(f'robot/{name}/{cname}/{k}'))
        for sname,items in (sets or {}).items():
            s=X.SubElement(node,'ParameterSet',Name=sname,Guid=guid(f'robot/{name}/{cname}/{sname}'))
            for i,v in enumerate(items,1):X.SubElement(s,'Parameter',Name=str(i),Type='string',Value=v,Guid=guid(f'robot/{name}/{cname}/{sname}/{i}'))
    w,h,d=unit_box(kind);boss=loot=='boss';fly=kind in FLY
    member('Body',[('BoxSize','Vector3',f'{w:g},{h:g},{d:g}'),('Mass','float',mass),('Density','float',4),('MaxSmoothRiseHeight','float',1.05),('AirDrag','Vector2','0.1,0.1' if fly else '0.25,0.25')])
    member('Health',[('AttackResilience','float',hp),('FallResilience','float',60),('FireResilience','float',500),('ExplosionResilience','float',5),('CorpseDuration','float',4 if boss else 3)])
    member('Creature',[('DisplayName','string',display),('Description','string',desc),('KillVerbs','string','killed'),('Category','Game.CreatureCategory','LandPredator')])
    # the flying bosses through their locomotion's fly orders (MDBossState.Fly): the devil mech's charge is 16 m/s, RD21's dash 24;
    # the drones at their own speed. RD13 walks (walk orders, MDBossState.Walk), RD19 does not move.
    member('Locomotion',[('WalkSpeed','float',0 if fly else speed),('FlySpeed','float',FLY_SPEED.get(kind,speed) if fly else 0),('SwimSpeed','float',0),('LadderSpeed','float',0),('JumpSpeed','float',0),
                         ('TurnSpeed','float',2.5),('LookSpeed','float',3),('AccelerationFactor','float',1.2 if fly else .6),('WalkSpeedWhenTurning','float',.5)])
    member('Spawn',[('AutoDespawn','bool','False' if boss else 'True')])
    member('Loot',[],sets={'Loot':LOOT[loot],'LootOnFire':LOOT[loot]})
    member('Model',[('Class','string','Game.ComponentMDRobotModel'),('ModelName','string',f'Imported/{kind}/{skin}'),('TextureOverride','string',''),
                    ('CastsShadow','bool','True'),('BoundingSphereRadius','float',round(max(w,h,d)*(1.6 if boss else .8),2))],parent=CT['CreatureModel'])
    member('MDRobot',[('Class','string','Game.ComponentMDRobot'),('Kind','string',kind)],parent=CT['Generic'])
    if not fly and not boss:member('WalkAroundBehavior',[],parent=CT['WalkAround'])
    # 0.11.0: the game's bestiary lists templates whose CreatureEggData has EggTypeIndex >= 0 (and sorts by it); skin A of each
    # walker and the devil mech get one. EggBlock then registers a hidden egg type for each (the lab sees it: the set still
    # counts as the game's egg data), as for the game's own creatures without eggs: ShowEgg False = no egg item, NutritionalValue
    # 0 = no cooking recipe. EggBlock keys egg types by EggTypeIndex & 0xFFF and a repeated index replaces another creature's,
    # so these sit far above the game's 0-49. Mod/MDEncyclopedia.cs adds the numbers to the descriptions.
    if name in BESTIARY:
        eggs=X.SubElement(e,'ParameterSet',Name='CreatureEggData',Guid=guid(f'robot/{name}/CreatureEggData'))
        for k,typ,value in [('EggTypeIndex','int',BESTIARY[name]),('ShowEgg','bool','False'),('NutritionalValue','float',0),('DisplayName','string',display)]:
            X.SubElement(eggs,'Parameter',Name=k,Type=typ,Value=str(value),Guid=guid(f'robot/{name}/CreatureEggData/{k}'))
X.indent(mod);X.ElementTree(mod).write(root/'Mod/Assets/MechDefense.xdb',encoding='utf-8',xml_declaration=True)
print('templates:',2+len(towers)+len(ROBOTS))
