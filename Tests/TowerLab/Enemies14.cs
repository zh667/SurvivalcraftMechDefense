using Engine;
using Game;

namespace TowerLab;

// lab --mode enemies14 (0.14.0 new enemies, our own models from E:/projects/dguns-enemy-models/v3_new): each new unit's model,
// skins and clips load; the tank drives at a gun tower from behind its own back, its turret turns on its own (the original
// clips move no joint), it fires on the move with both barrels and its shells burst. The drones rise to their height, circle a gun
// tower and fire from the air; an anti-air tower takes them, a cannon tower never aims at them; one shot down falls.
// Results under "enemies14"; screenshots e14_*.
public partial class Lab {
    static IEnumerable<bool> Enemies14() {
        Remove(placed.Select(p=>p.unit));placed.Clear();while(Wait(1))yield return true;
        var r=new Dictionary<string,object>();results["enemies14"]=r;var proj=GameManager.Project;var pl=player.ComponentPlayer;var bad=new List<string>();
        ComponentMDRobot Spawn(string t,Vector3 at,float yaw) {
            var e=DatabaseManager.CreateEntity(proj,t,true);var b=e.FindComponent<ComponentBody>(true);b.Position=at;b.Rotation=Quaternion.CreateFromAxisAngle(Vector3.UnitY,yaw);
            e.FindComponent<ComponentSpawn>(true).SpawnDuration=0;proj.AddEntity(e);return e.FindComponent<ComponentMDRobot>(true);
        }
        // ---- every skin loads, with every joint of unit.json in the model
        var loads=new List<string>();
        var units=MDRobots.Tanks.Concat(MDRobots.Drones).Concat(MDRobots.Gunships).ToList();
        foreach(var t in units) {
            var rb=Spawn(t,Home+new Vector3(units.IndexOf(t)*6-21,0,-20),0);rb.ForceClip="idle";rb.ForceTime=0;while(Wait(.5))yield return true;
            var m=rb.Entity.FindComponent<ComponentMDRobotModel>(true);int missing=rb.Def.joints.Count(j=>m.Model?.FindBone(j.name,false)==null);
            loads.Add($"{t}: model {(m.Model==null?"none":$"{m.Model.Meshes.Count} meshes, {m.Model.Bones.Count} bones")}, missing joints {missing}, clips {string.Join(",",rb.Def.clips.Select(c=>c.name))}");
            if(m.Model==null||missing>0)bad.Add("load "+t);
        }
        r["loads"]=loads;Cam(Home+new Vector3(0,9,-2),Home+new Vector3(0,1,-20));Shot("e14_unit_skins");yield return true;
        foreach(var rb in proj.Entities.Select(e=>e.FindComponent<ComponentMDRobot>()).Where(x=>x!=null).ToList())proj.RemoveEntity(rb.Entity,true);
        while(Wait(.5))yield return true;
        // ---- the tank against a gun tower: it starts 34 m away facing away from the tower
        var gun=MDTowerLibrary.All.First(t=>t.data==0);var tower=Place(gun,Home,0);pl.ComponentBody.Position=Home+new Vector3(-30,0,30);
        var tank=Spawn("MDRobot14",Home+new Vector3(34,0,0),-MathF.PI/2);   // its forward (-Z rotated) points +X, away from the tower
        float maxYaw=0,maxPitch=-9;int shots0=tank.ShotsFired;string clips="";float th0=tower.Damage.Hitpoints;bool shotMoving=false;double t0=Time.RealTime,firstShot=-1;
        var seen=new HashSet<string>();
        foreach(var x in During(30,()=>{
            maxYaw=Math.Max(maxYaw,MathF.Abs(tank.AimYawNow));maxPitch=Math.Max(maxPitch,tank.AimPitchNow);seen.Add(tank.Anim.Base.name);
            if(tank.ShotsFired>shots0&&firstShot<0){firstShot=Time.RealTime-t0;Cam(tank.Body.Position+new Vector3(-6,5,12),tank.Body.Position+Vector3.UnitY*1.5f);}
            if(tank.ShotsFired>shots0&&new Vector2(tank.Body.Velocity.X,tank.Body.Velocity.Z).Length()>.5f)shotMoving=true;
        }))yield return x;
        Cam(tank.Body.Position+new Vector3(-7,6,10),(tank.Body.Position+Home)/2+Vector3.UnitY*1.5f);Shot("e14_tank_fight");yield return true;
        r["fight"]=$"first shot after {firstShot:0.0} s, shots {tank.ShotsFired-shots0}, fired while moving {shotMoving}; turret yaw up to {maxYaw*180/MathF.PI:0}°, pitch up to {maxPitch*180/MathF.PI:0}°; clips {string.Join(",",seen)}; tower {th0:0.00} -> {tower.Damage.Hitpoints:0.00}; tank health {tank.Health.Health:0.00}, {Vector3.Distance(tank.Body.Position,Home):0} m from the tower";
        if(firstShot<0||tank.ShotsFired-shots0<2||tower.Damage.Hitpoints>=th0||!seen.Contains("move_forward"))bad.Add("tank fight: "+r["fight"]);
        // ---- a close-up of the turret aimed to the side (frozen)
        tank.Target=tower.Body;foreach(var x in During(1.5,()=>{}))yield return x;
        Cam(tank.Body.Position+new Vector3(4,4,-6),tank.Body.Position+Vector3.UnitY*1.5f);Shot("e14_tank_close");yield return true;
        Remove([tower]);if(tank.Entity.IsAddedToProject)proj.RemoveEntity(tank.Entity,true);
        while(Wait(.5))yield return true;
        // ---- the drones against a gun tower, with an anti-air tower and a cannon tower beside it
        var terrain=proj.FindSubsystem<SubsystemTerrain>(true);
        float Above(ComponentBody b)=>b.Position.Y-terrain.Terrain.GetTopHeight(Terrain.ToCell(b.Position.X),Terrain.ToCell(b.Position.Z))-1;
        var gt=Place(gun,Home,0);var aa=Place(MDTowerLibrary.All.First(t=>t.id=="aa_1"),Home+new Vector3(-8,0,0),0);var cannon=Place(MDTowerLibrary.All.First(t=>t.id=="cannon_1"),Home+new Vector3(8,0,0),0);
        var drones=new[]{Spawn("MDRobot09",Home+new Vector3(30,0,4),0),Spawn("MDRobotEN02",Home+new Vector3(0,0,-32),0)};
        var hs=drones.Select(_=>new List<float>()).ToArray();var air=new int[2];var n=new int[2];var dshots=drones.Select(x=>x.ShotsFired).ToArray();var dclips=drones.Select(_=>new HashSet<string>()).ToArray();
        bool aaOnDrone=false,cannonOnDrone=false;float gt0=gt.Damage.Hitpoints;double d0=Time.RealTime;
        void Sample(){
            for(int i=0;i<2;i++){var dr=drones[i];if(dr.Health.Health<=0||Time.RealTime-d0<6)continue;hs[i].Add(Above(dr.Body));n[i]++;if(MDTowerBrain.Airborne(dr.Body))air[i]++;dclips[i].Add(dr.Anim.Base.name);}
            if(aa.Target!=null&&drones.Any(dr=>dr.Body==aa.Target))aaOnDrone=true;if(cannon.Target!=null&&drones.Any(dr=>dr.Body==cannon.Target))cannonOnDrone=true;
        }
        void Follow(){var live=drones.FirstOrDefault(dr=>dr.Health.Health>0)??drones[0];Cam(live.Body.Position+new Vector3(-8,4,10),live.Body.Position);}
        foreach(var x in During(8,()=>{Sample();Follow();}))yield return x;
        foreach(var dr in drones.Where(dr=>dr.Health.Health>0)) {   // close-ups in the air, from the side and from below
            foreach(var x in Hold(dr.Body.Position+new Vector3(-5,1.5f,6),dr.Body.Position+Vector3.UnitY))yield return x;Shot($"e14_air_{dr.Kind.Id}");yield return true;
            foreach(var x in Hold(dr.Body.Position+new Vector3(3,-5,5),dr.Body.Position+Vector3.UnitY))yield return x;Shot($"e14_air_{dr.Kind.Id}_below");yield return true;
        }
        foreach(var x in During(22,()=>{Sample();Follow();}))yield return x;
        Shot("e14_drone_fight");yield return true;
        var fights=new List<string>();
        for(int i=0;i<2;i++) {
            var dr=drones[i];float avg=hs[i].Count>0?hs[i].Average():-1,lo=hs[i].Count>0?hs[i].Min():-1,hi=hs[i].Count>0?hs[i].Max():-1;float frac=n[i]>0?air[i]/(float)n[i]:0;int sh=dr.ShotsFired-dshots[i];
            fights.Add($"{dr.Kind.Id}: height {lo:0.0}-{hi:0.0} m (mean {avg:0.0}, wanted {dr.Kind.Fly:0}), airborne {frac:P0}, shots {sh}, clips {string.Join(",",dclips[i])}, health {dr.Health.Health:0.00}, {Vector3.Distance(dr.Body.Position,Home):0} m from the tower");
            if(n[i]<10||frac<.9f||avg<dr.Kind.Fly-3||avg>dr.Kind.Fly+4||sh<3)bad.Add("drone fight: "+fights[^1]);
        }
        r["drones"]=fights;r["drone_towers"]=$"gun tower {gt0:0.00} -> {gt.Damage.Hitpoints:0.00}; anti-air on a drone {aaOnDrone}; cannon on a drone {cannonOnDrone}";
        if(gt.Damage.Hitpoints>=gt0||!aaOnDrone||cannonOnDrone)bad.Add("drone towers: "+r["drone_towers"]);
        // ---- one shot down falls
        var down=drones.FirstOrDefault(dr=>dr.Health.Health>0);
        if(down!=null) {
            float y0=down.Body.Position.Y,h0=Above(down.Body);down.Health.Health=0;
            foreach(var x in During(4,()=>Cam(down.Body.Position+new Vector3(-8,3,10),down.Body.Position)))yield return x;
            r["drone_down"]=$"{down.Kind.Id}: {h0:0.0} m above the ground -> {Above(down.Body):0.0} m after 4 s, clip {down.Anim.Base.name}";
            if(down.Body.Position.Y>y0-Math.Min(3,h0*.5f))bad.Add("drone down: "+r["drone_down"]);
        }
        foreach(var dr in drones)if(dr.Entity.IsAddedToProject)proj.RemoveEntity(dr.Entity,true);
        Remove([gt,aa,cannon]);
        if(bad.Count>0)throw new Exception("enemies14: "+string.Join(" | ",bad));
    }
}
