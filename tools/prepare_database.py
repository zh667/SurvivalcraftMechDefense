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
    ('MDDevilMech','RD17','model','恶魔机甲','用诱敌信标引来的首领，按附近炮塔数量强化耐久。弱点每 20 秒在冲击、爆炸、能量之间变化，只有弱点伤害全额生效。必掉机甲设计图。',40000,7,4000,'boss'),
]
BESTIARY={'MDRobot05':3900,'MDRobot06':3901,'MDRobot07':3902,'MDDevilMech':3903}   # bestiary entries (EggTypeIndex), see below
def unit_box(kind):
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
    w,h,d=unit_box(kind);boss=loot=='boss'
    member('Body',[('BoxSize','Vector3',f'{w:g},{h:g},{d:g}'),('Mass','float',mass),('Density','float',4),('MaxSmoothRiseHeight','float',1.05),('AirDrag','Vector2','0.1,0.1' if boss else '0.25,0.25')])
    member('Health',[('AttackResilience','float',hp),('FallResilience','float',60),('FireResilience','float',500),('ExplosionResilience','float',5),('CorpseDuration','float',4 if boss else 3)])
    member('Creature',[('DisplayName','string',display),('Description','string',desc),('KillVerbs','string','killed'),('Category','Game.CreatureCategory','LandPredator')])
    # the devil mech flies through its locomotion's fly orders (MDBossState.Fly): its charge is 16 m/s
    member('Locomotion',[('WalkSpeed','float',0 if boss else speed),('FlySpeed','float',16 if boss else 0),('SwimSpeed','float',0),('LadderSpeed','float',0),('JumpSpeed','float',0),
                         ('TurnSpeed','float',2.5),('LookSpeed','float',3),('AccelerationFactor','float',1.2 if boss else .6),('WalkSpeedWhenTurning','float',.5)])
    member('Spawn',[('AutoDespawn','bool','False' if boss else 'True')])
    member('Loot',[],sets={'Loot':LOOT[loot],'LootOnFire':LOOT[loot]})
    member('Model',[('Class','string','Game.ComponentMDRobotModel'),('ModelName','string',f'Imported/{kind}/{skin}'),('TextureOverride','string',''),
                    ('CastsShadow','bool','True'),('BoundingSphereRadius','float',round(max(w,h,d)*(1.6 if boss else .8),2))],parent=CT['CreatureModel'])
    member('MDRobot',[('Class','string','Game.ComponentMDRobot'),('Kind','string',kind)],parent=CT['Generic'])
    if not boss:member('WalkAroundBehavior',[],parent=CT['WalkAround'])
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
