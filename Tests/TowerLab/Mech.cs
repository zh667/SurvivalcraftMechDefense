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

// Watches the mech's extra board key while On: each frame K is down, what the input the mod reads says, and every Board / Exit
// call with its result (0.11.0: K boarded but did not leave).
[HarmonyPatch(typeof(ComponentInput),nameof(ComponentInput.Update))]
public static class LabBoardWatch {
    public static bool On;public static readonly List<string> Log=[];
    [HarmonyPriority(Priority.Last)]
    public static void Postfix(ComponentInput __instance) {
        if(!On||Log.Count>40)return;var p=__instance.m_componentPlayer;var gi=p.GameWidget.Input;
        if(!Keyboard.IsKeyDown(Key.K)&&!Keyboard.IsKeyDownOnce(Key.K))return;
        Log.Add($"f{Time.FrameIndex} K down {Keyboard.IsKeyDown(Key.K)} once {Keyboard.IsKeyDownOnce(Key.K)} widget-once {gi.IsKeyDownOnce(Key.K)} cleared {gi.m_isCleared} devices {gi.Devices} bound {MDMechKeys.Get(MDMechAction.Board)} mounted {MDMechPilot.Mounted(p)!=null} parent {p.ComponentBody.ParentBody!=null} dialogs {DialogsManager.HasDialogs(p.GuiWidget)} modal {p.ComponentGui.ModalPanelWidget!=null}");
    }
}
[HarmonyPatch(typeof(MDMechPilot),nameof(MDMechPilot.Exit))]
public static class LabExitWatch {public static void Postfix(bool __result){if(LabBoardWatch.On)LabBoardWatch.Log.Add($"f{Time.FrameIndex} Exit -> {__result}");}}
[HarmonyPatch(typeof(MDMechPilot),nameof(MDMechPilot.Board))]
public static class LabBoardCallWatch {public static void Postfix(bool __result){if(LabBoardWatch.On)LabBoardWatch.Log.Add($"f{Time.FrameIndex} Board -> {__result}");}}

// Counts the sounds played through SubsystemAudio's positional PlaySound (the one MDMechBrain.Sound uses), by name.
[HarmonyPatch(typeof(SubsystemAudio),nameof(SubsystemAudio.PlaySound),new[]{typeof(string),typeof(float),typeof(float),typeof(Vector3),typeof(float),typeof(bool)})]
public static class LabSounds {
    public static readonly Dictionary<string,int> Count=[];
    public static void Prefix(string name){Count[name]=Count.GetValueOrDefault(name)+1;}
    public static int Of(string name)=>Count.GetValueOrDefault(name);
}

// lab --mode mech: Mercury, the mech a player boards. Deploys it from its item, shows the clips (forced, standing still),
// then drives it through MDMechState as the pilot input would (MDMechPilot.ExternalControls): boarding and both cameras,
// walking, strafing, dashing, jumping and turning, the rifle, missiles and cannon on target dummies, melee, leaving it and
// its destruction. Results under "mech"; screenshots mc_* (clips), mt_* (turntable), mp_* (piloting), md_* (destroyed).
public partial class Lab {
    static IEnumerable<bool> During(double seconds,Action each){for(double s0=Time.RealTime;Time.RealTime-s0<seconds;){each();yield return true;}}
    // the small messages shown since a time, by reflection (the message's label is a LabelWidget on 1.9.3.1, a FontTextWidget on 1.9.4)
    static List<string> Said(ComponentPlayer p,double since) {
        var r=new List<string>();
        if(typeof(MessageWidget).GetField("m_messages")?.GetValue(p.ComponentGui.m_messageWidget) is System.Collections.IEnumerable list)
            foreach(var m in list) {
                if(m==null||m.GetType().GetField("StartTime")?.GetValue(m) is not double t||t<since)continue;
                var label=m.GetType().GetField("LabelWidget")?.GetValue(m);
                if(label?.GetType().GetProperty("Text")?.GetValue(label) is string text)r.Add(text);
            }
        return r;
    }
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
        foreach(var x in Hold(c+new Vector3(2.6f,3.4f,-3.4f),c+new Vector3(0,3,0),3))yield return x;Shot("mt_close");yield return true;   // the texture up close
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
            bool hov=false;int hum0=LabSounds.Of(MDMechBrain.Se(31));
            foreach(var x in HoldKeys(2.5,()=>{hov|=m.Hovering;if(m.Hovering)clip=$"{m.Anim.Base.name} held at {m.Anim.BaseTime:0.0} s";},Key.W,Key.Shift))yield return x;
            k["Shift+W hovers"]=$"{hov}, {clip}";int hums=LabSounds.Of(MDMechBrain.Se(31))-hum0;k["hover hums in one 2.5 s Shift hold"]=hums;   // user: one sound per hold
            foreach(var x in During(.4,()=>{}))yield return x;foreach(var x in HoldKeys(.6,()=>{},Key.Shift))yield return x;
            int hums2=LabSounds.Of(MDMechBrain.Se(31))-hum0;k["hover hums after a second press"]=hums2;
            if(hums!=1||hums2!=2)throw new Exception($"hover hum: {hums} in one hold, {hums2} after a second press (want 1 and 2)");
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
            // 0.16.2: a key the mech used is not seen again later in the frame (Keys16.cs: what the GuiUpdate hooks and other mods'
            // keyboard reads see); X switches the ammo; a mech key on a number keeps the hotbar slot; R (leaving) is taken too
            var kx=new Dictionary<string,object>();k["keys after the mech"]=kx;
            {
                var vd=new TemplatesDatabase.ValuesDictionary();vd.SetValue(LabGuiKeys.Mapping,Key.X);ModSettingsManager.ModKeyboardMapSettings[LabGuiKeys.Package]=vd;
                kx["X shared with"]=MDMechKeys.SharedWith(Key.X);
                LabGuiKeys.Reset();LabGuiKeys.On=true;
                var ammo=new List<string>{MDMechState.AmmoName(m.Ammo)};
                for(int i=0;i<2;i++){foreach(var x in Tap(Key.X,.5))yield return x;ammo.Add(MDMechState.AmmoName(m.Ammo));}
                kx["X cycles the ammo"]=string.Join(" > ",ammo);kx["X changed it"]=m.Type.Ammo.Count()<2||ammo[1]!=ammo[0];
                foreach(var x in Tap(Key.B,.5))yield return x;foreach(var x in Tap(Key.V))yield return x;foreach(var x in Tap(Key.V))yield return x;
                bool fx=false;foreach(var x in Tap(Key.F,.1))yield return x;foreach(var x in During(.6,()=>fx|=m.Anim.Action?.name=="melee"))yield return x;kx["F strikes"]=fx;
                var hotbar=pl.ComponentMiner.Inventory;hotbar.ActiveSlotIndex=0;MDMechKeys.Apply(new Dictionary<MDMechAction,Key>{[MDMechAction.Melee]=Key.Number3});
                bool n3=false;foreach(var x in Tap(Key.Number3,.1))yield return x;foreach(var x in During(.6,()=>n3|=m.Anim.Action?.name=="melee"))yield return x;
                kx["3 (bound to melee) strikes"]=n3;kx["3 keeps the hotbar slot"]=hotbar.ActiveSlotIndex==0;MDMechKeys.Apply(MDMechKeys.Defaults());
            }
            foreach(var x in Tap(Key.R,.8))yield return x;k["R leaves"]=MDMechPilot.Mounted(pl)==null&&pl.ComponentBody.ParentBody==null;
            foreach(var x in During(.5,()=>{}))yield return x;
            {   // the control: on foot the same X reaches what reads after ComponentInput, once from the keyboard and once by the mapping
                kx["seen later while driving"]=LabGuiKeys.Text();bool clean=LabGuiKeys.Seen.Count==0&&LabGuiKeys.Mapped==0;
                LabGuiKeys.Reset();foreach(var x in Tap(Key.X,.5))yield return x;kx["seen on foot after X"]=LabGuiKeys.Text();
                bool control=LabGuiKeys.Seen.GetValueOrDefault(Key.X)==1&&LabGuiKeys.Mapped==1;
                LabGuiKeys.On=false;ModSettingsManager.ModKeyboardMapSettings.Remove(LabGuiKeys.Package);
                if(!clean||!control||kx.Values.OfType<bool>().Any(b=>!b)||!((string)kx["X shared with"]).Contains(LabGuiKeys.Mapping))
                    throw new Exception("keys after the mech: "+string.Join(", ",kx.Select(x=>$"{x.Key} {x.Value}")));
            }
            {   // 0.11.0 bound keys (MDMechKeys): K boards and leaves (extra key), G strikes, N switches weapons, C hovers; the old F and B do nothing
                MDMechKeys.Apply(new Dictionary<MDMechAction,Key>{[MDMechAction.Board]=Key.K,[MDMechAction.Melee]=Key.G,[MDMechAction.Switch]=Key.N,[MDMechAction.Hover]=Key.C});
                var kb=new Dictionary<string,object>();k["bound keys"]=kb;LabBoardWatch.On=true;
                foreach(var x in Tap(Key.K,.8))yield return x;kb["K boards"]=MDMechPilot.Mounted(pl)==u;
                bool oldMelee=false;foreach(var x in Tap(Key.F,.1))yield return x;foreach(var x in During(.5,()=>oldMelee|=m.Anim.Action?.name=="melee"))yield return x;kb["F no longer strikes"]=!oldMelee;
                foreach(var x in During(.6,()=>{}))yield return x;
                bool newMelee=false;foreach(var x in Tap(Key.G,.1))yield return x;foreach(var x in During(.5,()=>newMelee|=m.Anim.Action?.name=="melee"))yield return x;kb["G strikes"]=newMelee;
                foreach(var x in During(.6,()=>{}))yield return x;
                var w0=m.Weapon;foreach(var x in Tap(Key.B,.5))yield return x;kb["B no longer switches"]=m.Weapon==w0;
                foreach(var x in Tap(Key.N,.5))yield return x;kb["N switches"]=m.Weapon!=w0;
                bool hov2=false;foreach(var x in HoldKeys(1.5,()=>hov2|=m.Hovering,Key.C))yield return x;kb["C hovers"]=hov2;   // in place: with W it flies off the platform
                kb["C keeps the clothing panel shut"]=pl.ComponentGui.ModalPanelWidget==null;   // C is also the game's clothing key
                // leaving needs ground under the mech (MDMechPilot.Exit refuses in the air): wait for it to land, and log where it is
                kb["before K (air)"]=$"{u.Body.Position.Y-c.Y:0.00} m up, standing {u.Body.StandingOnValue.HasValue}, hovering {m.Hovering}";
                for(double tl=Time.RealTime;!(u.Body.StandingOnValue.HasValue&&!m.Hovering)&&Time.RealTime-tl<8;)yield return true;
                foreach(var x in During(.5,()=>{}))yield return x;
                kb["before K (landed)"]=$"{u.Body.Position.Y-c.Y:0.00} m up, standing {u.Body.StandingOnValue.HasValue}, {Vector3.Distance(Flat(u.Body.Position),Flat(c)):0.0} m from the start";
                double tk=Time.FrameStartTime;foreach(var x in Tap(Key.K,.8))yield return x;bool left=MDMechPilot.Mounted(pl)==null;
                kb["K leaves"]=left&&pl.ComponentBody.ParentBody==null;
                if(!left)kb["K stayed, said"]=string.Join(" / ",Said(pl,tk))+$" (modal {pl.ComponentGui.ModalPanelWidget!=null})";LabBoardWatch.On=false;kb["board key trace"]=string.Join(" | ",LabBoardWatch.Log);
                kb["hint"]=MDMechPilot.KeyHints(false);
                MDMechKeys.Apply(MDMechKeys.Defaults());
                if(kb.Values.OfType<bool>().Any(b=>!b))throw new Exception("bound mech keys: "+string.Join(", ",kb.Select(x=>$"{x.Key} {x.Value}")));
                foreach(var x in During(.5,()=>{}))yield return x;
            }
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
        r["hover"]=new{metres=Vector3.Distance(Flat(p0),Flat(u.Body.Position)),lastHalfSecondSpeed=Vector3.Distance(Flat(hp1),Flat(u.Body.Position))/.5f,heightNow=u.Body.Position.Y-Home.Y,hovering=m.Hovering,clip=seen,clipTime=m.Anim.BaseTime,rate=m.Anim.Rate};
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
        // locked missiles always hit: three wolves running sideways at 6 m/s (moved every frame)
        var run=new[]{Target(At(18,-12)),Target(At(24,-8)),Target(At(30,-14))};while(Wait(.6))yield return true;
        void Run(float dt){foreach(var t in run)if(t.Entity.IsAddedToProject)t.Body.Position+=right*6*dt;}
        double runAt=Time.RealTime;float Step(){float d=(float)(Time.RealTime-runAt);runAt=Time.RealTime;return Math.Min(d,.05f);}
        for(int k=0;k<3;k++)foreach(var x in During(.25,()=>{Run(Step());m.Trigger=true;AimAt(run[k]);}))yield return x;
        int runLocks=m.Locks.Count;m.Trigger=false;
        foreach(var x in During(6,()=>{Run(Step());Cam(cam,mid+right*8);}))yield return x;Shot("mp_missile_running");yield return true;
        r["missileRunning"]=new{locks=runLocks,hits=run.Count(t=>t.Hit),damage=string.Join(" ",run.Select(t=>$"{(1-t.Min)*2e4f:0}"))};Remove(run);
        // laser: a 1 s beam on a wolf at 14 m, damage every 1/6 s
        foreach(var x in SwitchTo(MDMechWeapon.Laser))yield return x;
        var lt=Target(At(14,1));while(Wait(.6))yield return true;
        foreach(var x in During(.5,()=>{m.Trigger=true;AimAt(lt);Cam(cam,mid);}))yield return x;Shot("mp_laser");yield return true;
        // sweep the beam sideways (aim point moving 12 m/s): one beam with a smooth ribbon behind it
        var sweep0=Time.RealTime;foreach(var x in During(.25,()=>{m.Trigger=true;m.AimPoint=MDCombat.Center(lt.Body)+right*(float)((Time.RealTime-sweep0)*12-1.5);Cam(cam,mid);}))yield return x;Shot("mp_laser_sweep");yield return true;
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
        // ---- survival upkeep: armour 4000 (3000 before 0.9.0 round 5); iron repairs it like a tower; three pickaxe hits (the miner's real hit, through
        // MDDismantlePatch) take it down into its item with its health; that item deploys it again with that health
        var terrain=GameManager.Project.FindSubsystem<SubsystemTerrain>(true);var pickables=GameManager.Project.FindSubsystem<SubsystemPickables>(true);
        float armour=u.Damage.AttackResilience;u.SetHitpoints(.5f);
        var iron=MDTowerMaintenance.Material(Terrain.MakeBlockValue(BlocksManager.GetBlockIndex<IronIngotBlock>()),false).Value;
        bool repaired=MDTowerMaintenance.Repair(pl.ComponentMiner,u,iron.points,iron.name);float afterIron=u.Damage.Hitpoints;
        inv.RemoveSlotItems(0,inv.GetSlotCount(0));inv.AddSlotItems(0,Terrain.MakeBlockValue(BlocksManager.GetBlockIndex<IronPickaxeBlock>()),1);inv.ActiveSlotIndex=0;
        var hitAt=u.Body.Position+Vector3.UnitY*2;
        for(int i=0;i<MDTowerMaintenance.HitsToDismantle;i++){pl.ComponentMiner.Hit(u.Body,hitAt,Vector3.UnitZ);foreach(var x in During(1,()=>{}))yield return x;}
        var drop=pickables.Pickables.FirstOrDefault(pk=>Terrain.ExtractContents(pk.Value)==block.BlockIndex&&MDDeploymentBlock.IsMech(pk.Value));
        r["upkeep"]=new{armour,repaired,afterIron,takenDown=!u.Entity.IsAddedToProject,item=drop==null?null:block.GetDisplayName(terrain,drop.Value),wear=drop==null?-1:MDDeploymentBlock.Wear(drop.Value)};
        if(armour!=MDMechType.Mercury.Armour||!repaired||MathF.Abs(afterIron-(.5f+200f/armour))>.002f||drop==null||u.Entity.IsAddedToProject)throw new Exception("mech upkeep: "+System.Text.Json.JsonSerializer.Serialize(r["upkeep"]));
        int back=drop.Value;drop.ToRemove=true;inv.RemoveSlotItems(0,inv.GetSlotCount(0));inv.AddSlotItems(0,back,1);inv.ActiveSlotIndex=0;
        pl.ComponentBody.Position=Home;pl.ComponentBody.Rotation=Quaternion.Identity;pl.ComponentBody.Velocity=Vector3.Zero;while(Wait(.5))yield return true;
        foot=pl.ComponentBody.Position;pl.ComponentMiner.Use(new Ray3(foot+new Vector3(0,1.6f,0),Vector3.Normalize(new Vector3(0,-1.6f,-5f))));
        u=GameManager.Project.Entities.Select(e=>e.FindComponent<ComponentMDUnit>()).FirstOrDefault(x=>x?.Mech!=null);
        r["redeployed"]=new{deployed=u!=null,hitpoints=u?.Damage.Hitpoints??-1};
        if(u==null||MathF.Abs(u.Damage.Hitpoints-(1-MDDeploymentBlock.Wear(back)/100f))>.001f)throw new Exception("redeployed mech: "+System.Text.Json.JsonSerializer.Serialize(r["redeployed"]));
        while(Wait(1))yield return true;
        u.Damage.Hitpoints=0;var dc=u.Body.Position;
        foreach(var x in During(1.8,()=>Cam(dc+new Vector3(7,3,-8),dc+Vector3.UnitY*1.5f)))yield return x;Shot("md_death");yield return true;r["destroyed"]=u.Destroyed;
        foreach(var x in During(10.5,()=>{}))yield return x;r["removedAfterWreck"]=!u.Entity.IsAddedToProject;
        r["modelError"]=ComponentMDMechModel.LastError;
    }
}
