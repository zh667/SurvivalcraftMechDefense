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
            bool hov=false;foreach(var x in HoldKeys(.8,()=>{hov|=m.Hovering;if(m.Anim.Base.name.StartsWith("dash"))clip=m.Anim.Base.name;},Key.W,Key.Shift))yield return x;k["Shift+W hovers"]=$"{hov}, {clip}";
            foreach(var x in During(.6,()=>{}))yield return x;
            var weapons=new List<string>{m.WeaponName};
            for(int i=0;i<5;i++){foreach(var x in Tap(Key.B,.5))yield return x;weapons.Add(m.WeaponName+(m.Weapon==MDMechWeapon.Cannon?"/"+m.Cannon:""));}
            k["B cycles"]=string.Join(" > ",weapons);   // the cannon stays folded until it fires
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
        // first person: standing, then a burst while walking (shake / flicker), the head sensors hidden
        pl.GameWidget.ActiveCamera=pl.GameWidget.FindCamera<FppCamera>();foreach(var x in During(.4,()=>{}))yield return x;ScreenCaptureManager.Capture(1280,720,"mp_fpp.png");yield return true;
        r["fppMeshesDrawn"]=$"{mm.MeshDrawOrders?.Length} of {mm.Model.Meshes.Count}";var eyes=new List<Vector3>();
        for(int i=0;i<6;i++){foreach(var x in During(.12,()=>m.Move=new(0,1)))yield return x;eyes.Add(pl.GameWidget.ActiveCamera.ViewPosition-u.Body.Position);ScreenCaptureManager.Capture(1280,720,$"mp_fpp_walk_{i}.png");yield return true;}
        foreach(var x in During(.6,()=>m.Move=Vector2.Zero))yield return x;
        r["fppEyeOffsetsWhileWalking"]=eyes.Select(e=>$"{e.Y:0.000}").ToList();
        pl.GameWidget.ActiveCamera=pl.GameWidget.FindCamera<TppCamera>();
        string seen="";var side=new Vector3(9,3,0);
        // each movement test starts at Home facing -Z: at the real speeds the mech leaves the cleared platform otherwise
        IEnumerable<bool> Reset(){u.Body.Position=Home;u.Body.Velocity=Vector3.Zero;m.MoveVelocity=Vector3.Zero;u.Yaw=u.AimYaw=0;u.Body.Rotation=Quaternion.Identity;for(double s0=Time.RealTime;Time.RealTime-s0<.6;){m.Move=Vector2.Zero;yield return true;}}
        // walk forward
        foreach(var x in Reset())yield return x;var p0=u.Body.Position;
        foreach(var x in During(2,()=>{m.Move=new(0,1);seen=m.Anim.Base.name;}))yield return x;
        foreach(var x in During(.15,()=>{m.Move=new(0,1);Cam(u.Body.Position+side,u.Body.Position+Vector3.UnitY*2);}))yield return x;Shot("mp_walk");yield return true;
        r["walk"]=new{metres=Vector3.Distance(Flat(p0),Flat(u.Body.Position)),clip=seen,rate=m.Anim.Rate};
        foreach(var x in During(.6,()=>m.Move=Vector2.Zero))yield return x;
        // strafe left: along the mech's -X (its left)
        foreach(var x in Reset())yield return x;p0=u.Body.Position;
        foreach(var x in During(1.5,()=>{m.Move=new(-1,0);seen=m.Anim.Base.name;}))yield return x;
        var right=new Vector3(MathF.Cos(u.Yaw),0,-MathF.Sin(u.Yaw));r["strafeLeft"]=new{alongRight=Vector3.Dot(u.Body.Position-p0,right),clip=seen};
        foreach(var x in During(.6,()=>m.Move=Vector2.Zero))yield return x;
        // hover (Shift held) forward: floats on its thrusters at the boost speed, gliding clip
        foreach(var x in Reset())yield return x;p0=u.Body.Position;float hmin=99,hmax=0;
        foreach(var x in During(1.6,()=>{m.Move=new(0,1);m.Hover=true;seen=m.Anim.Base.name;if(Time.RealTime>0){float h=u.Body.Position.Y-Home.Y;hmin=Math.Min(hmin,h);hmax=Math.Max(hmax,h);}Cam(u.Body.Position+side*1.3f,u.Body.Position+Vector3.UnitY*2);}))yield return x;
        Shot("mp_hover");yield return true;
        var hp1=u.Body.Position;foreach(var x in During(.5,()=>{m.Move=new(0,1);m.Hover=true;}))yield return x;
        r["hover"]=new{metres=Vector3.Distance(Flat(p0),Flat(u.Body.Position)),lastHalfSecondSpeed=Vector3.Distance(Flat(hp1),Flat(u.Body.Position))/.5f,heightNow=u.Body.Position.Y-Home.Y,hovering=m.Hovering,clip=seen};
        m.Hover=false;foreach(var x in During(.8,()=>m.Move=Vector2.Zero))yield return x;r["afterHover"]=new{grounded=m.Grounded,height=u.Body.Position.Y-Home.Y};
        foreach(var x in During(.8,()=>m.Move=Vector2.Zero))yield return x;
        // jump while walking forward: it leaves at once and keeps steering in the air
        foreach(var x in Reset())yield return x;float y0=u.Body.Position.Y,top=y0;var jp=u.Body.Position;m.Jump=true;var clips=new HashSet<string>();float airMetres=0;var last=u.Body.Position;
        foreach(var x in During(2.5,()=>{m.Move=new(0,1);top=Math.Max(top,u.Body.Position.Y);clips.Add(m.Anim.Base.name);if(!m.Grounded)airMetres+=Vector3.Distance(Flat(last),Flat(u.Body.Position));last=u.Body.Position;Cam(u.Body.Position+side*1.4f,u.Body.Position+Vector3.UnitY*2);}))yield return x;
        r["jump"]=new{height=top-y0,airMetres,clips=string.Join(",",clips),landed=m.Grounded};
        foreach(var x in During(.8,()=>m.Move=Vector2.Zero))yield return x;
        // turn: the turret leads, the body follows the aim
        foreach(var x in Reset())yield return x;float aim=u.Yaw+1.4f;u.AimYaw=aim;foreach(var x in During(.25,()=>{}))yield return x;float early=MDCombat.AngleDelta(u.Yaw,aim);
        foreach(var x in During(1.5,()=>{}))yield return x;r["turn"]=new{afterQuarterSecond=early,after2s=MDCombat.AngleDelta(u.Yaw,aim)};
        // ---- weapons on dummies 16 m ahead, back at Home (the movement tests above cover 40 m and more)
        u.Body.Position=Home;u.Body.Velocity=Vector3.Zero;m.MoveVelocity=Vector3.Zero;u.Yaw=u.AimYaw=0;u.Body.Rotation=Quaternion.Identity;
        foreach(var x in During(.8,()=>m.Move=Vector2.Zero))yield return x;right=new Vector3(MathF.Cos(u.Yaw),0,-MathF.Sin(u.Yaw));
        var fwd=new Vector3(-MathF.Sin(u.Yaw),0,-MathF.Cos(u.Yaw));var w=MDTowerWorld.For(GameManager.Project);
        // the towers' dummies take 1e-7 of a hit's power, which a wolf heals within a frame at the lab's ~800 fps; a resilience
        // of 2e4 still keeps them alive through every shot here and makes each hit show (lost = health lost)
        Dum Target(Vector3 at){var t=Dummy(at);t.C.ComponentHealth.AttackResilience=2e4f;return t;}
        Vector3 At(float ahead,float side)=>u.Body.Position+fwd*ahead+right*side;
        var cam=u.Body.Position-fwd*6+right*9+Vector3.UnitY*5;var mid=u.Body.Position+fwd*9+Vector3.UnitY*2;
        u.AimYaw=u.Yaw;u.AimPitch=0;
        IEnumerable<bool> SwitchTo(MDMechWeapon wanted){for(int i=0;i<6&&m.Weapon!=wanted;i++){m.Switch=true;for(double s0=Time.RealTime;Time.RealTime-s0<.35;)yield return true;}for(double s0=Time.RealTime;Time.RealTime-s0<.3;)yield return true;}
        void AimAt(Dum t){m.AimPoint=MDCombat.Center(t.Body);m.AimFrom=MDMechBrain.Eye(u);m.AimDir=Vector3.Normalize(m.AimPoint.Value-m.AimFrom);}
        // rifle
        var rifleT=Target(At(16,-2));while(Wait(.6))yield return true;int imp0=w.Impacts;
        foreach(var x in During(1.4,()=>{m.Trigger=true;AimAt(rifleT);Cam(cam,mid);}))yield return x;Shot("mp_rifle");yield return true;
        m.Trigger=false;foreach(var x in During(.6,()=>{}))yield return x;
        r["rifle"]=new{hit=rifleT.Hit,damage=(1-rifleT.Min)*2e4f,impacts=w.Impacts-imp0,weapon=m.WeaponName};Remove([rifleT]);
        // missiles: hold and sweep the crosshair over three wolves (locks), release: one homing missile per lock
        foreach(var x in SwitchTo(MDMechWeapon.Missile))yield return x;
        var mt=new[]{Target(At(20,-6)),Target(At(22,0)),Target(At(20,6))};while(Wait(.6))yield return true;
        for(int k=0;k<3;k++)foreach(var x in During(.25,()=>{m.Trigger=true;AimAt(mt[k]);}))yield return x;
        int locks=m.Locks.Count;foreach(var x in During(.1,()=>Cam(cam,mid)))yield return x;Shot("mp_missile_locked");yield return true;m.Trigger=false;
        foreach(var x in During(4,()=>Cam(cam,mid)))yield return x;Shot("mp_missile");yield return true;
        r["missile"]=new{locks,hits=mt.Count(t=>t.Hit),damage=string.Join(" ",mt.Select(t=>$"{(1-t.Min)*2e4f:0}")),weapon=m.WeaponName};Remove(mt);
        // laser: a 1 s beam on a wolf at 14 m, damage every 1/6 s
        foreach(var x in SwitchTo(MDMechWeapon.Laser))yield return x;
        var lt=Target(At(14,1));while(Wait(.6))yield return true;
        foreach(var x in During(.5,()=>{m.Trigger=true;AimAt(lt);Cam(cam,mid);}))yield return x;Shot("mp_laser");yield return true;
        foreach(var x in During(1.2,()=>{m.Trigger=true;AimAt(lt);}))yield return x;m.Trigger=false;
        r["laser"]=new{hit=lt.Hit,damage=(1-lt.Min)*2e4f,weapon=m.WeaponName};Remove([lt]);
        // grenade: thrown at a wolf at 18 m lands at 75 % of the distance, the blast reaches it
        foreach(var x in SwitchTo(MDMechWeapon.Grenade))yield return x;
        var gt=Target(At(18,0));while(Wait(.6))yield return true;
        foreach(var x in During(.05,()=>{m.Trigger=true;AimAt(gt);}))yield return x;m.Trigger=false;
        foreach(var x in During(.6,()=>Cam(cam,mid)))yield return x;Shot("mp_grenade_flight");yield return true;
        foreach(var x in During(2.2,()=>Cam(cam,mid)))yield return x;
        r["grenade"]=new{hit=gt.Hit,damage=(1-gt.Min)*2e4f,weapon=m.WeaponName};Remove([gt]);
        // cannon: folded when chosen; a single press unfolds it, fires once and folds straight back; held, it stays open and fires
        foreach(var x in SwitchTo(MDMechWeapon.Cannon))yield return x;
        var canT=Target(At(24,0));var states=new List<string>();void Track(){var s=m.Cannon.ToString();if(states.Count==0||states[^1]!=s)states.Add(s);}
        foreach(var x in During(1,Track))yield return x;
        r["cannonWhenChosen"]=m.Cannon.ToString();
        imp0=w.Impacts;var fireTimes=new List<double>();double tf=Time.RealTime;float lastFired=m.FiredAgo;
        foreach(var x in During(.05,()=>{m.Trigger=true;AimAt(canT);Track();}))yield return x;m.Trigger=false;   // one press
        foreach(var x in During(3.5,()=>{AimAt(canT);Track();if(m.FiredAgo<lastFired)fireTimes.Add(Time.RealTime-tf);lastFired=m.FiredAgo;Cam(cam,mid);}))yield return x;Shot("mp_cannon_fired");yield return true;
        r["cannon"]=new{hit=canT.Hit,damage=(1-canT.Min)*2e4f,impacts=w.Impacts-imp0,weapon=m.WeaponName,shots=fireTimes.Count,states=string.Join(">",states),energy=m.Energy};
        states.Clear();fireTimes.Clear();tf=Time.RealTime;
        foreach(var x in During(5,()=>{m.Trigger=true;AimAt(canT);Track();if(m.FiredAgo<lastFired)fireTimes.Add(Time.RealTime-tf);lastFired=m.FiredAgo;Cam(u.Body.Position+fwd*-4+right*7+Vector3.UnitY*6,u.Body.Position+Vector3.UnitY*3.5f);}))yield return x;
        Shot("mp_cannon_held");yield return true;m.Trigger=false;
        foreach(var x in During(1.5,Track))yield return x;
        r["cannonHeld"]=new{shots=fireTimes.Count,at=string.Join(" ",fireTimes.Select(t=>$"{t:0.0}")),states=string.Join(">",states)};
        Remove([canT]);foreach(var x in SwitchTo(MDMechWeapon.Rifle))yield return x;r["afterSwitch"]=m.WeaponName;
        // melee: a wolf 12 m ahead near the crosshair: the jump attack carries the mech to it and strikes
        var meleeT=Target(At(12,0));while(Wait(.6))yield return true;var lp=u.Body.Position;AimAt(meleeT);m.Melee=true;string act="";
        foreach(var x in During(1.3,()=>{AimAt(meleeT);if(m.Anim.Action!=null)act=m.Anim.Action.name;Cam(u.Body.Position+right*9+Vector3.UnitY*3,u.Body.Position+fwd*3+Vector3.UnitY*2);}))yield return x;Shot("mp_melee");yield return true;
        r["melee"]=new{hit=meleeT.Hit,damage=(1-meleeT.Min)*2e4f,action=act,lungeMetres=Vector3.Distance(Flat(lp),Flat(u.Body.Position))};Remove([meleeT]);
        // energy: hovering drains it, standing refills it
        float e0=m.Energy;foreach(var x in During(1,()=>m.Hover=true))yield return x;float e1=m.Energy;m.Hover=false;foreach(var x in During(1,()=>{}))yield return x;
        r["energy"]=new{before=e0,afterHoverSecond=e1,afterRestSecond=m.Energy};
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
