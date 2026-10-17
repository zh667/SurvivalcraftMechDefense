using Engine;
using Engine.Input;
using Game;
using HarmonyLib;
using System.Reflection;

namespace TowerLab;

// Real key presses for the mech: queued keys go into Engine.Input.Keyboard from inside the game's own input update (just
// before ComponentInput reads the keyboard), so the "pressed once" flags survive the frame start. The lab window is hidden,
// and the game ignores keys of an inactive window: Window's state is set to active while keys are queued.
[HarmonyPatch(typeof(ComponentInput),nameof(ComponentInput.Update))]
public static class LabKeys {
    public static readonly HashSet<Key> Held=[];public static readonly Queue<Key> Taps=new();static readonly List<Key> up=[];
    static readonly FieldInfo state=typeof(Window).GetField("m_state",BindingFlags.NonPublic|BindingFlags.Static);
    public static bool Busy=>Held.Count>0||Taps.Count>0||up.Count>0;
    public static void Prefix() {
        if(!Busy)return;
        if(state!=null&&!Window.IsActive)state.SetValue(null,Enum.Parse(state.FieldType,"Active"));
        foreach(var k in up)Keyboard.ProcessKeyUp(k);up.Clear();
        if(Taps.Count>0){var k=Taps.Dequeue();Keyboard.ProcessKeyDown(k);up.Add(k);}   // down this frame, up the next
        foreach(var k in Held)if(!Keyboard.IsKeyDown(k))Keyboard.ProcessKeyDown(k);
    }
    public static void Release(){up.AddRange(Held);Held.Clear();}
}

// lab --mode mech: Mercury, the mech a player boards. Deploys it from its item, shows the clips (forced, standing still),
// then drives it through MDMechState as the pilot input would (MDMechPilot.ExternalControls): boarding and both cameras,
// walking, strafing, dashing, jumping and turning, the rifle, missiles and cannon on target dummies, melee, leaving it and
// its destruction. Results under "mech"; screenshots mc_* (clips), mt_* (turntable), mp_* (piloting), md_* (destroyed).
public partial class Lab {
    static IEnumerable<bool> During(double seconds,Action each){for(double s0=Time.RealTime;Time.RealTime-s0<seconds;){each();yield return true;}}
    static Vector3 Flat(Vector3 v)=>new(v.X,0,v.Z);
    static IEnumerable<bool> Mech() {
        Remove(placed.Select(p=>p.unit));placed.Clear();while(Wait(.5))yield return true;
        var r=new Dictionary<string,object>();results["mech"]=r;var pl=player.ComponentPlayer;var inv=pl.ComponentMiner.Inventory;var d=MDMechLibrary.Def;
        r["clips"]=d.clips.Length;r["joints"]=d.joints.Length;
        // ---- deploy from the item like a player: at Home facing -Z, using it on the ground 5 m ahead
        pl.ComponentBody.Position=Home;pl.ComponentBody.Rotation=Quaternion.Identity;pl.ComponentBody.Velocity=Vector3.Zero;while(Wait(.5))yield return true;
        var block=(MDDeploymentBlock)BlocksManager.Blocks[BlocksManager.GetBlockIndex<MDDeploymentBlock>()];
        int item=Terrain.MakeBlockValue(block.BlockIndex,0,1);inv.RemoveSlotItems(0,inv.GetSlotCount(0));inv.AddSlotItems(0,item,1);inv.ActiveSlotIndex=0;
        r["itemName"]=block.GetDisplayName(GameManager.Project.FindSubsystem<SubsystemTerrain>(true),item);r["inCreative"]=block.GetCreativeValues().Contains(item);
        var foot=pl.ComponentBody.Position;bool used=pl.ComponentMiner.Use(new Ray3(foot+new Vector3(0,1.6f,0),Vector3.Normalize(new Vector3(0,-1.6f,-5f))));
        var u=GameManager.Project.Entities.Select(e=>e.FindComponent<ComponentMDUnit>()).FirstOrDefault(x=>x?.Mech!=null);
        r["deployed"]=u!=null;r["itemUsedUp"]=inv.GetSlotCount(0)==0;
        if(u==null)throw new Exception("Mercury did not deploy (Use returned "+used+")");
        var mm=u.Entity.FindComponent<ComponentMDMechModel>(true);var m=u.Mech;
        r["model"]=$"{mm.Model?.Meshes.Count} meshes, {mm.Model?.Bones.Count} bones, rendering {mm.RenderingMode}, texture override {(mm.TextureOverride==null?"none":"set")}";
        r["missingBones"]=d.joints.Where(j=>mm.Model.FindBone(j.name,false)==null).Select(j=>j.name).ToList();
        while(Wait(1.5))yield return true;
        var c=u.Body.Position;r["deployAt"]=c.ToString();r["deployYaw"]=u.Yaw;r["iconTriangles"]=MDMechLibrary.Icon.mesh.Indices.Count/3;
        // ---- clips at start, middle and end (forced: the body stands still), then an idle turntable
        var eye=c+new Vector3(6,3.2f,-9);var focus=c+new Vector3(0,2.2f,0);
        foreach(var name in new[]{"idle","combat_stance","walk_forward","walk_left","dash_forward","jump","fall","hit","death","lockon_special","melee","cannon_deploy_fire","cannon_fire_loop","cannon_retract"}) {
            var clip=d.Clip(name);
            for(int k=0;k<3;k++){m.ForceClip=name;m.ForceTime=d.Seconds(clip)*k/2f;foreach(var x in Hold(eye,focus,3))yield return x;Shot($"mc_{name}_{k}");yield return true;}
        }
        m.ForceClip="idle";m.ForceTime=0;
        for(int k=0;k<4;k++){float a=MathF.PI*.25f+k*MathF.PI*.5f;foreach(var x in Hold(c+new Vector3(MathF.Sin(a)*10,3.5f,-MathF.Cos(a)*10),focus,3))yield return x;Shot($"mt_{k}");yield return true;}
        m.ForceClip=null;
        // ---- real keys: R boards from 2.4 m (the game's mount key), W walks, Shift dashes, B cycles weapons, F strikes,
        // V switches the camera, E still opens the inventory, R leaves
        {
            var k=new Dictionary<string,object>();r["keys"]=k;MDMechPilot.ExternalControls=false;
            pl.ComponentBody.Position=c+new Vector3(3.6f,.1f,0);pl.ComponentBody.Velocity=Vector3.Zero;pl.GameWidget.ActiveCamera=pl.GameWidget.FindCamera<FppCamera>();
            while(Wait(.5))yield return true;
            IEnumerable<bool> Tap(Key key,double after=.4){LabKeys.Taps.Enqueue(key);for(double s0=Time.RealTime;Time.RealTime-s0<after||LabKeys.Busy;)yield return true;}
            IEnumerable<bool> HoldKeys(double seconds,Action each,params Key[] keys){foreach(var key in keys)LabKeys.Held.Add(key);for(double s0=Time.RealTime;Time.RealTime-s0<seconds;){each();yield return true;}LabKeys.Release();for(int i=0;i<4;i++)yield return true;}
            foreach(var x in Tap(Key.R,.8))yield return x;k["R boards (2.4 m away)"]=MDMechPilot.Mounted(pl)==u;
            var kp=u.Body.Position;string clip="";
            foreach(var x in HoldKeys(1.2,()=>clip=m.Anim.Base.name,Key.W))yield return x;k["W walks"]=$"{Vector3.Distance(Flat(kp),Flat(u.Body.Position)):0.0} m, {clip}";
            foreach(var x in During(.6,()=>{}))yield return x;
            foreach(var x in HoldKeys(.7,()=>{if(m.Anim.Base.name.StartsWith("dash"))clip=m.Anim.Base.name;},Key.W,Key.Shift))yield return x;k["Shift+W dashes"]=clip;
            foreach(var x in During(.6,()=>{}))yield return x;
            var weapons=new List<string>{m.WeaponName};
            for(int i=0;i<3;i++){foreach(var x in Tap(Key.B,.9))yield return x;weapons.Add(m.WeaponName+(m.Weapon==MDMechWeapon.Cannon?"/"+m.Cannon:""));}
            k["B cycles"]=string.Join(" > ",weapons);
            bool melee=false;foreach(var x in Tap(Key.F,.1))yield return x;foreach(var x in During(.5,()=>melee|=m.Anim.Action?.name=="melee"))yield return x;k["F strikes"]=melee;
            var cams=new List<string>{pl.GameWidget.ActiveCamera.GetType().Name};
            for(int i=0;i<2;i++){foreach(var x in Tap(Key.V))yield return x;cams.Add(pl.GameWidget.ActiveCamera.GetType().Name);}
            k["V switches"]=string.Join(" > ",cams);
            foreach(var x in Tap(Key.E,.6))yield return x;bool invOpen=pl.ComponentGui.ModalPanelWidget!=null;var still=u.Body.Position;
            foreach(var x in During(.4,()=>{}))yield return x;k["E opens the inventory, mech stays"]=invOpen&&Vector3.Distance(still,u.Body.Position)<.05f;
            foreach(var x in Tap(Key.E,.6))yield return x;k["E closes it"]=pl.ComponentGui.ModalPanelWidget==null;
            foreach(var x in Tap(Key.R,.8))yield return x;k["R leaves"]=MDMechPilot.Mounted(pl)==null&&pl.ComponentBody.ParentBody==null;
            foreach(var x in During(.5,()=>{}))yield return x;
        }
        // ---- board and look through both pilot cameras
        MDMechPilot.ExternalControls=true;
        c=u.Body.Position;pl.ComponentBody.Position=c+new Vector3(2.4f,.1f,0);pl.ComponentBody.Velocity=Vector3.Zero;while(Wait(.4))yield return true;
        r["board"]=MDMechPilot.Board(pl,u);while(Wait(.6))yield return true;
        r["mounted"]=MDMechPilot.Mounted(pl)==u;r["camera"]=pl.GameWidget.ActiveCamera.GetType().Name;r["hud"]=m.Hud?.ParentWidget!=null;
        ScreenCaptureManager.Capture(1280,720,"mp_tpp.png");yield return true;
        pl.GameWidget.ActiveCamera=pl.GameWidget.FindCamera<FppCamera>();foreach(var x in During(.4,()=>{}))yield return x;ScreenCaptureManager.Capture(1280,720,"mp_fpp.png");yield return true;
        pl.GameWidget.ActiveCamera=pl.GameWidget.FindCamera<TppCamera>();
        string seen="";var side=new Vector3(9,3,0);
        // walk forward
        var p0=u.Body.Position;
        foreach(var x in During(2,()=>{m.Move=new(0,1);seen=m.Anim.Base.name;}))yield return x;
        foreach(var x in During(.15,()=>{m.Move=new(0,1);Cam(u.Body.Position+side,u.Body.Position+Vector3.UnitY*2);}))yield return x;Shot("mp_walk");yield return true;
        r["walk"]=new{metres=Vector3.Distance(Flat(p0),Flat(u.Body.Position)),clip=seen,rate=m.Anim.Rate};
        foreach(var x in During(.6,()=>m.Move=Vector2.Zero))yield return x;
        // strafe left: along the mech's -X (its left)
        p0=u.Body.Position;
        foreach(var x in During(1.5,()=>{m.Move=new(-1,0);seen=m.Anim.Base.name;}))yield return x;
        var right=new Vector3(MathF.Cos(u.Yaw),0,-MathF.Sin(u.Yaw));r["strafeLeft"]=new{alongRight=Vector3.Dot(u.Body.Position-p0,right),clip=seen};
        foreach(var x in During(.6,()=>m.Move=Vector2.Zero))yield return x;
        // dash forward
        p0=u.Body.Position;
        foreach(var x in During(1,()=>{m.Move=new(0,1);m.Dash=true;seen=m.Anim.Base.name;}))yield return x;
        r["dash"]=new{metres=Vector3.Distance(Flat(p0),Flat(u.Body.Position)),clip=seen};m.Dash=false;
        foreach(var x in During(.8,()=>m.Move=Vector2.Zero))yield return x;
        // jump
        float y0=u.Body.Position.Y,top=y0;m.Jump=true;var clips=new HashSet<string>();
        foreach(var x in During(2.5,()=>{top=Math.Max(top,u.Body.Position.Y);clips.Add(m.Anim.Base.name);Cam(u.Body.Position+side*1.4f,u.Body.Position+Vector3.UnitY*2);}))yield return x;
        r["jump"]=new{height=top-y0,clips=string.Join(",",clips),landed=m.Grounded};
        // turn: the turret leads, the body follows the aim
        float aim=u.Yaw+1.4f;u.AimYaw=aim;foreach(var x in During(.25,()=>{}))yield return x;float early=MDCombat.AngleDelta(u.Yaw,aim);
        foreach(var x in During(1.5,()=>{}))yield return x;r["turn"]=new{afterQuarterSecond=early,after2s=MDCombat.AngleDelta(u.Yaw,aim)};
        // ---- weapons on dummies 16 m ahead
        var fwd=new Vector3(-MathF.Sin(u.Yaw),0,-MathF.Cos(u.Yaw));var w=MDTowerWorld.For(GameManager.Project);
        // the towers' dummies take 1e-7 of a hit's power, which a wolf heals within a frame at the lab's ~800 fps; a resilience
        // of 2e4 still keeps them alive through every shot here and makes each hit show (lost = health lost)
        Dum Target(Vector3 at){var t=Dummy(at);t.C.ComponentHealth.AttackResilience=2e4f;return t;}
        var rifleT=Target(u.Body.Position+fwd*16+right*-2);var missT=Target(u.Body.Position+fwd*18+right*2);var canT=Target(u.Body.Position+fwd*20);
        u.AimYaw=u.Yaw;u.AimPitch=0;while(Wait(.8))yield return true;
        var cam=u.Body.Position-fwd*6+right*9+Vector3.UnitY*5;var mid=u.Body.Position+fwd*9+Vector3.UnitY*2;
        int imp0=w.Impacts;
        foreach(var x in During(1.4,()=>{m.Trigger=true;m.AimPoint=MDCombat.Center(rifleT.Body);Cam(cam,mid);}))yield return x;Shot("mp_rifle");yield return true;
        m.Trigger=false;foreach(var x in During(.6,()=>{}))yield return x;
        r["rifle"]=new{hit=rifleT.Hit,lost=1-rifleT.Min,impacts=w.Impacts-imp0,heat=m.Heat,weapon=m.WeaponName};
        m.Switch=true;foreach(var x in During(.5,()=>{}))yield return x;imp0=w.Impacts;bool fired=false;
        foreach(var x in During(2.8,()=>{if(!fired){m.Trigger=true;m.AimPoint=MDCombat.Center(missT.Body);}if(m.FiredAgo<.05f){fired=true;m.Trigger=false;}Cam(cam,mid);}))yield return x;Shot("mp_missile");yield return true;
        m.Trigger=false;r["missile"]=new{hit=missT.Hit,lost=1-missT.Min,impacts=w.Impacts-imp0,weapon=m.WeaponName,fired};
        m.Switch=true;var states=new List<string>();
        foreach(var x in During(1.5,()=>{var s=m.Cannon.ToString();if(states.Count==0||states[^1]!=s)states.Add(s);Cam(u.Body.Position+fwd*-4+right*7+Vector3.UnitY*6,u.Body.Position+Vector3.UnitY*3.5f);}))yield return x;Shot("mp_cannon_ready");yield return true;
        imp0=w.Impacts;fired=false;
        foreach(var x in During(3,()=>{if(!fired){m.Trigger=true;m.AimPoint=MDCombat.Center(canT.Body);}if(m.Cannon==MDMechState.CannonState.Firing){fired=true;m.Trigger=false;}var s=m.Cannon.ToString();if(states[^1]!=s)states.Add(s);Cam(cam,mid);}))yield return x;Shot("mp_cannon_fired");yield return true;
        r["cannon"]=new{hit=canT.Hit,lost=1-canT.Min,impacts=w.Impacts-imp0,weapon=m.WeaponName,fired,states=string.Join(">",states)};
        m.Switch=true;foreach(var x in During(1,()=>{var s=m.Cannon.ToString();if(states[^1]!=s)states.Add(s);}))yield return x;
        r["cannonAfterSwitch"]=new{state=m.Cannon.ToString(),weapon=m.WeaponName,states=string.Join(">",states)};
        Remove([rifleT,missT,canT]);
        // melee on a dummy 4.5 m ahead
        var meleeT=Target(u.Body.Position+fwd*4.5f);while(Wait(.5))yield return true;m.Melee=true;
        foreach(var x in During(1.3,()=>Cam(u.Body.Position+right*8+Vector3.UnitY*3,u.Body.Position+fwd*2+Vector3.UnitY*2)))yield return x;Shot("mp_melee");yield return true;
        r["melee"]=new{hit=meleeT.Hit,lost=1-meleeT.Min};Remove([meleeT]);
        r["shells"]=w.Shots;
        // ---- leave, then destroy it
        r["exit"]=MDMechPilot.Exit(pl,u);r["afterExit"]=new{mounted=MDMechPilot.Mounted(pl)!=null,riding=pl.ComponentBody.ParentBody!=null,camera=pl.GameWidget.ActiveCamera.GetType().Name,hud=m.Hud!=null};
        MDMechPilot.ExternalControls=false;
        u.Damage.Hitpoints=0;var dc=u.Body.Position;
        foreach(var x in During(1.8,()=>Cam(dc+new Vector3(7,3,-8),dc+Vector3.UnitY*1.5f)))yield return x;Shot("md_death");yield return true;r["destroyed"]=u.Destroyed;
        foreach(var x in During(10.5,()=>{}))yield return x;r["removedAfterWreck"]=!u.Entity.IsAddedToProject;
        r["modelError"]=ComponentMDMechModel.LastError;
    }
}
