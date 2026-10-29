using Engine;
using Game;

namespace TowerLab;

// lab --mode enemies14 (0.14.0 new enemies, our own models from E:/projects/dguns-enemy-models/v3_new): each new unit's model,
// skins and clips load; the tank drives at a gun tower from behind its own back, its turret turns on its own (the original
// clips move no joint), it fires on the move with both barrels and its shells burst. Results under "enemies14"; screenshots e14_*.
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
        foreach(var t in MDRobots.Tanks) {
            var rb=Spawn(t,Home+new Vector3(MDRobots.Tanks.ToList().IndexOf(t)*6-6,0,-20),0);while(Wait(.5))yield return true;
            var m=rb.Entity.FindComponent<ComponentMDRobotModel>(true);int missing=rb.Def.joints.Count(j=>m.Model?.FindBone(j.name,false)==null);
            loads.Add($"{t}: model {(m.Model==null?"none":$"{m.Model.Meshes.Count} meshes, {m.Model.Bones.Count} bones")}, missing joints {missing}, clips {string.Join(",",rb.Def.clips.Select(c=>c.name))}");
            if(m.Model==null||missing>0)bad.Add("load "+t);
        }
        r["loads"]=loads;Cam(Home+new Vector3(0,7,-6),Home+new Vector3(0,1,-20));Shot("e14_tank_skins");yield return true;
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
        if(bad.Count>0)throw new Exception("enemies14: "+string.Join(" | ",bad));
    }
}
