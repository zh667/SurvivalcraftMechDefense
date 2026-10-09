"""Generate deterministic API entity templates, preserving native body/mount/save behavior.

Towers come from Mod/Assets/Towers/towers.json (tools/build_tower_assets.py). The first three gun
towers keep their 0.3.x template names (MDTurret, MDTurret2, MDTurret3) so existing saves load.
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
# legacy Destroy Gunners sample units (old worlds)
for kind,size,mass,hp in [('Mech','2.4,4.8,2.4',3000,1500),('Enemy','1.6,3.2,1.6',700,260)]:
    comp=entity(kind,'MD'+kind)
    comp('Unit',[('Class','string','Game.ComponentMDUnit'),('LoadOrder','int',100),('Kind','string',kind),('TurretVariant','int',1),('TowerId','string','')])
    comp('Body',[('BoxSize','Vector3',size),('Mass','float',mass),('Density','float',5),('MaxSmoothRiseHeight','float',.8),('WaterSwayAngle','float',0),('WaterTurnSpeed','float',0)])
    comp('Damage',[('Class','string','Game.ComponentMDDamage'),('AttackResilience','float',hp),('FallResilience','float',15),('FireResilience','float',100)])
    comp('Spawn',[('AutoDespawn','bool','False')] if kind=='Mech' else []);comp('OnFire',[])
    if kind=='Mech':comp('Mount',[('MountOffset','Vector3','0,2.5,0'),('DismountOffset','Vector3','3,0,0'),('MountAllowToStartRange','float',5)])
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
X.indent(mod);X.ElementTree(mod).write(root/'Mod/Assets/MechDefense.xdb',encoding='utf-8',xml_declaration=True)
print('templates:',2+len(towers))
