"""Generate deterministic API entity templates, preserving native body/mount/save behavior."""
from pathlib import Path
import uuid,xml.etree.ElementTree as X
root=Path(__file__).resolve().parents[1]
def guid(s):return str(uuid.uuid5(uuid.NAMESPACE_URL,'local.mechdefense/'+s))
mod=X.Element('Mod');folder=X.SubElement(mod,'Folder',Name='Entities',Guid='5fcdf35f-7001-442c-883e-e0502b15d291')
parents=dict(Unit='b05700ed-7e4e-4679-98f5-b597f421496b',Body='8edd1986-0dcc-424f-893a-491c089b821e',Mount='d0b14127-39ce-4490-af00-fb4111e9c51e',Damage='17bb7b53-159c-4eb3-b4bc-137b10bba88a',Spawn='65268743-4076-4ee3-aefe-f02a54d16b36',OnFire='93590d78-63b9-4cd6-bc41-e66864cbb050')
for kind,size,mass,hp in [('Turret','2,3,2',900,650),('Mech','2.4,4.8,2.4',3000,1500),('Enemy','1.6,3.2,1.6',700,260),('Turret2','2,4,2',950,800),('Turret3','3,4,3',1100,1100)]:
    entity=X.SubElement(folder,'EntityTemplate',Name='MD'+kind,Guid=guid(kind))
    def comp(name,values):
        node=X.SubElement(entity,'MemberComponentTemplate',Name=name,Guid=guid(kind+'/'+name),InheritanceParent=parents[name])
        for key,typ,value in values:X.SubElement(node,'Parameter',Name=key,Type=typ,Value=str(value),Guid=guid(kind+'/'+name+'/'+key))
    turret=kind.startswith('Turret')
    comp('Unit',[('Class','string','Game.ComponentMDUnit'),('LoadOrder','int',100),('Kind','string','Turret' if turret else kind),('TurretVariant','int',int(kind[-1]) if kind in ['Turret2','Turret3'] else 1)])
    comp('Body',([('Class','string','Game.ComponentMDTowerBody')] if turret else [])+[('BoxSize','Vector3',size),('Mass','float',mass),('Density','float',5),('MaxSmoothRiseHeight','float',0 if turret else .8),('WaterSwayAngle','float',0),('WaterTurnSpeed','float',0)])
    comp('Damage',[('Class','string','Game.ComponentMDDamage'),('AttackResilience','float',hp),('FallResilience','float',15),('FireResilience','float',100)])
    comp('Spawn',[]);comp('OnFire',[])
    if kind=='Mech':comp('Mount',[('MountOffset','Vector3','0,2.5,0'),('DismountOffset','Vector3','3,0,0'),('MountAllowToStartRange','float',5)])
X.indent(mod);X.ElementTree(mod).write(root/'Mod/Assets/MechDefense.xdb',encoding='utf-8',xml_declaration=True)
