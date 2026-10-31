using Engine;
using Game;

namespace TowerLab;

// lab --mode aimnan (0.16.0, user 2026-10-31: 液氮塔, “毒沼”, “酸洗” showing only their base on 1.9.3.1): a tower whose aim angle
// turned NaN. With the repair off its turret bones are NaN (the screenshot shows the base alone); with it on the aim returns to a
// number within a frame and the turret is drawn again. A target with a NaN velocity no longer poisons the lead point.
// Results under "aimnan"; screenshots an_*.
public partial class Lab {
    static IEnumerable<bool> AimNan() {
        Remove(placed.Select(p=>p.unit));placed.Clear();while(Wait(1))yield return true;
        var r=new Dictionary<string,object>();results["aimnan"]=r;var bad=new List<string>();var proj=GameManager.Project;
        var defs=MDTowerLibrary.All;var ids=new[]{"battle_frost_1","battle_fog_1","battle_debuff_1"};var at0=Home+new Vector3(-6,0,12);
        var units=ids.Select((id,i)=>Place(defs.First(d=>d.id==id),at0+new Vector3(i*6,0,0),MathF.PI)).ToList();
        while(Wait(1.5))yield return true;
        var focus=at0+new Vector3(6,1.4f,0);var eye=focus+new Vector3(0,3,-13);
        bool Finite(ComponentMDUnit u){var m=u.Entity.FindComponent<ComponentMDTowerModel>(true);var abs=m.AbsoluteBoneTransformsForCamera;
            return abs!=null&&abs.All(x=>float.IsFinite(x.M11)&&float.IsFinite(x.M41)&&float.IsFinite(x.M42)&&float.IsFinite(x.M43));}
        foreach(var x in Hold(eye,focus,6))yield return x;Shot("an_0_normal");
        r["normal"]=units.Select(Finite).ToList();if(!units.All(Finite))bad.Add("bones not finite before the test");
        // the failure: NaN aim, repair off
        MDTowerBrain.UnstickEnabled=false;
        foreach(var u in units){u.AimYaw=float.NaN;u.AimPitch=float.NaN;}
        foreach(var x in Hold(eye,focus,8))yield return x;Shot("an_1_nan_repair_off");
        r["repairOff"]=units.Select(Finite).ToList();if(units.Any(Finite))bad.Add("NaN aim did not break the turret bones (the repro is wrong)");
        // the repair
        MDTowerBrain.UnstickEnabled=true;
        foreach(var x in Hold(eye,focus,8))yield return x;Shot("an_2_repair_on");
        r["repairOn"]=units.Select(u=>$"{u.TowerId} aim {u.AimYaw:0.00}/{u.AimPitch:0.00} bones finite {Finite(u)}").ToList();
        if(!units.All(u=>float.IsFinite(u.AimYaw)&&float.IsFinite(u.AimPitch)&&Finite(u)))bad.Add("the repair left a NaN");
        // a saved NaN: the same repair runs on the first update after loading (ComponentMDUnit.Load also reads NaN as the rest pose)
        r["finiteLoad"]=$"{MDCombat.Finite(float.NaN,1.5f)} {MDCombat.Finite(float.PositiveInfinity,0)} {MDCombat.Finite(.3f,0)}";
        if(MDCombat.Finite(float.NaN,1.5f)!=1.5f||MDCombat.Finite(.3f,0)!=.3f)bad.Add("Finite(): "+r["finiteLoad"]);
        // a target whose velocity is NaN: the lead point stays a number
        {
            var wolf=DatabaseManager.CreateEntity(proj,"Wolf",true);var wb=wolf.FindComponent<ComponentBody>(true);wb.Position=at0+new Vector3(6,0,14);proj.AddEntity(wolf);
            while(Wait(.3))yield return true;
            var u=units[1];u.Tracked=wb;u.TrackedVelocity=new Vector3(float.NaN,0,float.NaN);var p=MDTowerBrain.AimPoint(u,wb);
            u.Tracked=null;var q=MDTowerBrain.AimPoint(u,wb);   // not tracked yet: reads the body's velocity
            r["leadNaN"]=$"tracked NaN -> {p}, untracked -> {q}";
            if(!MDCombat.Finite(p)||!MDCombat.Finite(q))bad.Add("lead point NaN: "+r["leadNaN"]);
            proj.RemoveEntity(wolf,true);
        }
        Remove(units);
        if(bad.Count>0)throw new Exception("aimnan: "+string.Join(" || ",bad));
    }
}
