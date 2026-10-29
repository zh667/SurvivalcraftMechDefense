using Engine;
using Engine.Graphics;
using Game;
using HarmonyLib;
using System.Diagnostics;
using System.Reflection;
using System.Text.Json;

namespace TowerLab;

// Isolated in-game lab: builds a flat creative world, places every tower, and records
// screenshots (gallery, flicker sequences, firing sequences) plus frame-time measurements.
// Mode (lab-mode.txt next to the exe): "gallery", "fire", "perf" or "all"; "recipes", "hand", "effects", "artillery" or "special" (those four);
// "combat" (Combat.cs, a Harmless world with the ghoul and Wonderful Era mods); "mech" (Mech.cs, Mercury); "mech-view" (MechView.cs, before / after shots of Mercury; "mech-view-nemesis" / "mech-view-regulus" for the heroes); "robots" (Robots.cs, 0.9.0);
// "phone" (Phone.cs, 0.12.0: a Snapdragon GPU's draw limit, the mechs drawn in batches).
public partial class Lab:ModLoader {
    static int stage;static double began,step;static WorldInfo world;static PlayerData player;static Vector3 origin;
    static readonly List<string> log=[];static readonly Dictionary<string,object> results=[];
    static readonly List<(string id,ComponentMDUnit unit,Vector3 at)> placed=[];
    static IEnumerator<bool> script;static string mode="all";
    static string Out=>Path.Combine(AppContext.BaseDirectory,"lab-result.json");
    public override void __ModInitialize(){new Harmony("local.towerlab").PatchAll(typeof(Lab).Assembly);SettingsManager.FileAssociationEnabled=false;
        var m=Path.Combine(AppContext.BaseDirectory,"lab-mode.txt");if(File.Exists(m))mode=File.ReadAllText(m).Trim();
        if(mode=="phone")ModsManager.RegisterHook("OnModelDataDrawing",this);}
    public override IEnumerable<KeyValuePair<string,object>> GetKeyboardMappings(){yield return new("NUIConsle",(Engine.Input.Key)(-1));}
    public override void OnLoadingFinished(List<Action> actions){actions.Add(()=>{began=Time.RealTime;Window.Frame+=Frame;});}
    static void Log(string s){log.Add($"{Time.RealTime-began:F1}s {s}");}
    static void Save(bool passed,string error=null){results["passed"]=passed;results["camlog"]=camlog;results["modelError"]=ComponentMDTowerModel.LastError;results["drawErrors"]=DrawProbe.Errors;results["log"]=log;if(error!=null)results["error"]=error;File.WriteAllText(Out,JsonSerializer.Serialize(results,new JsonSerializerOptions{WriteIndented=true}));}
    static Vector3 lastEye,lastFocus;
    static FixedCamera Cam(Vector3 eye,Vector3 focus) {
        lastEye=eye;lastFocus=focus;var p=player.ComponentPlayer;var c=p.GameWidget.FindCamera<FixedCamera>();p.GameWidget.ActiveCamera=c;
        c.SetupPerspectiveCamera(eye,Vector3.Normalize(focus-eye),Vector3.UnitY);return c;
    }
    static readonly List<string> camlog=[];
    static IEnumerable<bool> Hold(Vector3 eye,Vector3 focus,int frames=4){for(int i=0;i<frames;i++){Cam(eye,focus);yield return true;}}
    static void Shot(string name){Cam(lastEye,lastFocus);var c=player.ComponentPlayer.GameWidget.ActiveCamera;if(camlog.Count<3)camlog.Add($"{name}: {c.GetType().Name} at {c.ViewPosition} dir {c.ViewDirection}");ScreenCaptureManager.Capture(1280,720,name+".png");}

    static void Frame() {
        try {
            if(Time.RealTime-began>1500)throw new TimeoutException("stage "+stage);
            if(stage==0) {
                if(ScreensManager.CurrentScreen is not MainMenuScreen)return;
                SettingsManager.VisibilityRange=128;SettingsManager.SoundsVolume=0;SettingsManager.MusicVolume=0;SettingsManager.ShowLogoInScreenshots=false;
                if(mode=="phone"){results["pcUniformVectors"]=GLWrapper.GL_MAX_VERTEX_UNIFORM_VECTORS;GLWrapper.GL_MAX_VERTEX_UNIFORM_VECTORS=PhoneUniformVectors;}   // read by SubsystemModelsRenderer.Load
                world=WorldsManager.CreateWorld(new WorldSettings{Name="TowerLab_"+DateTime.Now.ToString("HHmmss"),Seed="7",GameMode=mode=="combat"?GameMode.Harmless:GameMode.Creative,TerrainGenerationMode=TerrainGenerationMode.FlatContinent,TerrainLevel=64,TerrainBlockIndex=8,AreWeatherEffectsEnabled=false,TimeOfDayMode=TimeOfDayMode.Day});
                ScreensManager.FindScreen<GameLoadingScreen>("GameLoading").m_worldInfo=world;   // set by the loading screen in the real game; the ghoul mod reads it
                GameManager.LoadProject(world,ScreensManager.FindScreen<GameScreen>("Game").Children.Find<ContainerWidget>("GamesWidget"));
                player=new PlayerData(GameManager.Project){Name="Lab",SpawnPosition=new(100,65,100),InputDevice=WidgetInputDevice.None};GameManager.Project.FindSubsystem<SubsystemPlayers>().AddPlayerData(player);ScreensManager.SwitchScreen("Game");stage=1;return;
            }
            if(stage==1) {
                if(!player.IsReadyForPlaying)return;
                script=Run().GetEnumerator();stage=2;step=Time.RealTime;return;
            }
            if(stage==2) {
                foreach(var d in dums)d.Track();
                if(!script.MoveNext()){Save(true);Window.Frame-=Frame;Window.Close();}
            }
        } catch(Exception e){Save(false,e.ToString());Window.Frame-=Frame;Window.Close();}
    }
    static double t0;static bool Wait(double s){if(t0==0)t0=Time.RealTime;if(Time.RealTime-t0<s)return true;t0=0;return false;}

    static ComponentMDUnit Place(MDTowerDef d,Vector3 at,float yaw=MathF.PI) {
        var e=DatabaseManager.CreateEntity(GameManager.Project,d.template,true);var body=e.FindComponent<ComponentMDTowerBody>(true);
        var rot=Quaternion.CreateFromAxisAngle(Vector3.UnitY,yaw);body.FixAt(at,rot);
        var u=e.FindComponent<ComponentMDUnit>(true);u.Yaw=u.AimYaw=yaw;e.FindComponent<ComponentSpawn>(true).SpawnDuration=0;GameManager.Project.AddEntity(e);return u;
    }
    // Target dummy: a wolf (a land predator, which the towers attack) that cannot move and barely takes damage. Before 0.6.0
    // this was the Destroy Gunners enemy unit, removed from the mod. Creatures heal over time, so a hit is read from the lowest
    // health seen (Track runs every frame).
    sealed class Dum {
        public ComponentCreature C;public float Min=1;
        public ComponentBody Body=>C.ComponentBody;public GameEntitySystem.Entity Entity=>C.Entity;public bool Hit=>Min<1;
        public void Track(){if(C.Entity.IsAddedToProject)Min=Math.Min(Min,C.ComponentHealth.Health);}
    }
    static readonly List<Dum> dums=[];
    static Dum Dummy(Vector3 at) {
        var e=DatabaseManager.CreateEntity(GameManager.Project,"Wolf",true);e.FindComponent<ComponentBody>(true).Position=at;e.FindComponent<ComponentSpawn>(true).SpawnDuration=0;
        GameManager.Project.AddEntity(e);var c=e.FindComponent<ComponentCreature>(true);c.ComponentHealth.AttackResilience=1e7f;
        var l=c.ComponentLocomotion;l.WalkSpeed=l.JumpSpeed=l.SwimSpeed=l.FlySpeed=0;
        var d=new Dum{C=c};dums.Add(d);return d;
    }
    static void Remove(IEnumerable<Dum> ds){foreach(var d in ds.ToList()){dums.Remove(d);if(d.Entity.IsAddedToProject)GameManager.Project.RemoveEntity(d.Entity,true);}}
    static void Clear(int x0,int x1,int z0,int z1) {
        var t=GameManager.Project.FindSubsystem<SubsystemTerrain>();
        for(int x=x0;x<x1;x++)for(int z=z0;z<z1;z++)for(int y=60;y<90;y++)t.ChangeCell(x,y,z,y<70?(y==69?2:1):0);
    }
    static Vector3 Slot(int i)=>origin+new Vector3(i%5*11,0,i/5*14);
    static Vector3 Home=>origin+new Vector3(22,0,21);
    static void Remove(IEnumerable<ComponentMDUnit> units){foreach(var u in units.ToList())if(u.Entity.IsAddedToProject)GameManager.Project.RemoveEntity(u.Entity,true);}

    // API 1.9.4 on phones (player log 2026-10-23): Engine.Vector4.Transform and the array forms of Vector2/3/4.Transform and
    // TransformNormal take an x86-only (Sse) path when the runtime reports vector acceleration but no ARM AdvSimd, and throw
    // PlatformNotSupportedException; drawing a tower icon that way dropped players out of the world. The mod must not call
    // them. A PC cannot reproduce it (Sse works there), so the lab scans the mod's IL instead; SimdTrapProbe proves the scan
    // finds such a call.
    static Vector4 SimdTrapProbe(Vector4 v,Matrix m)=>Vector4.Transform(v,m);
    static List<string> SimdTraps(IEnumerable<Type> types) {
        var bad=new List<string>();const BindingFlags all=BindingFlags.Instance|BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.DeclaredOnly;
        foreach(var t in types)foreach(var m in t.GetMethods(all).Cast<MethodBase>().Concat(t.GetConstructors(all))) {
            byte[] il;try{il=m.GetMethodBody()?.GetILAsByteArray();}catch{continue;}if(il==null)continue;
            for(int i=0;i+4<il.Length;i++) {
                if(il[i]!=0x28&&il[i]!=0x6F)continue;   // call, callvirt
                MethodBase c;try{c=m.Module.ResolveMethod(BitConverter.ToInt32(il,i+1),t.IsGenericType?t.GetGenericArguments():null,m.IsGenericMethod?m.GetGenericArguments():null);}catch{continue;}
                if(c?.DeclaringType is not {Namespace:"Engine"} dt||dt.Name is not ("Vector2" or "Vector3" or "Vector4")||c.Name is not ("Transform" or "TransformNormal"))continue;
                var ps=c.GetParameters();
                if(dt.Name=="Vector4"||ps.Length>0&&ps[0].ParameterType.IsArray)bad.Add($"{t.FullName}.{m.Name} -> {dt.Name}.{c.Name}({string.Join(",",ps.Select(p=>p.ParameterType.Name))})");
            }
        }
        return bad.Distinct().ToList();
    }
    static IEnumerable<bool> Run() {
        {   // first, in every mode: the probe must be found, the mod must have none
            if(SimdTraps([typeof(Lab)]).Count==0)throw new Exception("the SIMD trap scan misses SimdTrapProbe");
            var traps=SimdTraps(typeof(MDTowerLibrary).Assembly.GetTypes());results["simdTraps"]=traps.Count==0?"none":traps;
            if(traps.Count>0)throw new Exception("calls that crash on API 1.9.4 phones: "+string.Join("; ",traps));
        }
        var p0=player.ComponentPlayer.ComponentBody.Position;origin=new(MathF.Floor(p0.X)+.5f,70,MathF.Floor(p0.Z)+.5f);
        var towers=MDTowerLibrary.All;results["towers"]=towers.Count;
        Clear((int)origin.X-30,(int)origin.X+75,(int)origin.Z-35,(int)origin.Z+70);
        player.ComponentPlayer.ComponentBody.Position=Home;
        while(Wait(1))yield return true;
        // ---- gallery: one row, 9 m apart, facing +Z (camera side)
        var checks=new List<string>();
        for(int i=0;i<towers.Count;i++) {
            var d=towers[i];var at=Slot(i);var u=Place(d,at,0);placed.Add((d.id,u,at));
            var model=u.Entity.FindComponent<ComponentMDTowerModel>(true);
            if(model.Model==null)throw new Exception("model missing "+d.id);
            foreach(var n in d.nodes.Skip(1))if(model.Model.FindBone(n.name,false)==null)throw new Exception($"bone {n.name} missing in {d.id}");
            if(MDTowerLibrary.Icon(d).mesh.Vertices.Count<100)throw new Exception("icon mesh missing "+d.id);
            if(MDTowerLibrary.Picture(d) is not {Width:256,Height:256})throw new Exception("slot picture missing "+d.id);
            checks.Add($"{d.id}: model {model.Model.Meshes.Count} meshes, {model.Model.Bones.Count} bones, icon ok");
        }
        results["loadChecks"]=checks;
        {   // optional Neorxna info panel: every tower shows its Chinese name and footprint
            MDNeorxnaCompatibility.Install();
            var panelType=AppDomain.CurrentDomain.GetAssemblies().Select(a=>a.GetType("Neorxna.NUI.NUInfoPanelWidget",false)).FirstOrDefault(t=>t!=null);
            if(panelType==null)results["neorxna"]="not installed";
            else {
                var panel=Activator.CreateInstance(panelType);var names=new List<string>();
                foreach(var (id,u,at) in placed){panelType.GetMethod("SetInfo_Entity").Invoke(panel,[u.Entity]);names.Add(((LabelWidget)panelType.GetField("m_name").GetValue(panel)).Text);}
                if(!names.SequenceEqual(placed.Select(p=>p.unit.Tower.name)))throw new Exception("Neorxna names: "+string.Join(",",names));
                results["neorxna"]=$"{names.Count} towers named: "+string.Join(" ",names);
            }
        }
        var diag=new List<string>();
        foreach(var (id,u,at) in placed)diag.Add($"{id} placed {at} body {u.Body.Position} anchor {(u.Body as ComponentMDTowerBody)?.Anchor}");
        while(Wait(1.5))yield return true;
        Cam(origin+new Vector3(-6,5,-9),origin+new Vector3(4,1.5f,0));yield return true;
        foreach(var (id,u,at) in placed) {
            var m=u.Entity.FindComponent<ComponentMDTowerModel>(true);
            diag.Add($"{id} after 1.5s body {u.Body.Position} vel {u.Body.Velocity} added {u.Entity.IsAddedToProject} visible {m.IsVisibleForCamera} root {(m.AbsoluteBoneTransformsForCamera?.Length>0?m.AbsoluteBoneTransformsForCamera[0].Translation.ToString():"-")} hp {u.Damage.Hitpoints} destroyed {u.Destroyed}");
        }
        results["diag"]=diag;Shot("diag_origin");yield return true;
        {   // model internals + a floating copy to see where geometry sits relative to the body
            var m=placed[0].unit.Entity.FindComponent<ComponentMDTowerModel>(true);var mi=new List<string>();
            foreach(var b in m.Model.Bones)mi.Add($"bone {b.Index} {b.Name} parent {b.ParentBone?.Index} T {b.Transform.Translation} R {b.Transform.Right}");
            foreach(var mesh in m.Model.Meshes)mi.Add($"mesh {mesh.Name} bone {mesh.ParentBone.Index} box {mesh.BoundingBox.Min}..{mesh.BoundingBox.Max}");
            mi.Add($"abs root {m.AbsoluteBoneTransformsForCamera[0].Translation} scale {m.ModelScale} offset {m.ModelOffset} mode {m.RenderingMode} opacity {m.Opacity}");
            foreach(var (pid,pu,pat) in placed.Take(0).Where(x=>x.id is "battle_turret_1" or "battle_rocket_1" or "battle_laser_1")) {
                var pm=pu.Entity.FindComponent<ComponentMDTowerModel>(true);
                foreach(var mesh in pm.Model.Meshes)foreach(var part in mesh.MeshParts)
                    mi.Add($"{pid} part {mesh.Name} mat {part.MaterialIndex} start {part.StartIndex} n {part.IndicesCount} vb {part.VertexBuffer?.VerticesCount} ib {part.IndexBuffer?.IndicesCount} {part.IndexBuffer?.IndexFormat} tag {(part.VertexBuffer?.Tag as byte[])?.Length} stride {part.VertexBuffer?.VertexDeclaration.VertexStride} elems {string.Join(",",part.VertexBuffer?.VertexDeclaration.VertexElements.Select(e=>e.Semantic+"@"+e.Offset)??[])}");
                var data=InstancedModelsManager.GetInstancedModelDataByMaterial(pm.Model,pm.MeshDrawOrders);
                foreach(var kv in data)mi.Add($"{pid} instanced mat {kv.Key}: vb {kv.Value?.VertexBuffer?.VerticesCount} ib {kv.Value?.IndexBuffer?.IndicesCount}");
                mi.Add($"{pid} drawOrders {string.Join(",",pm.MeshDrawOrders)} tex {pm.TextureOverride?.Width}x{pm.TextureOverride?.Height}");
            }
            results["modelInternals"]=mi;
            var wolf=DatabaseManager.CreateEntity(GameManager.Project,"Wolf",true);wolf.FindComponent<ComponentBody>(true).Position=origin+new Vector3(1.5f,0,-2.5f);GameManager.Project.AddEntity(wolf);
            while(Wait(.5))yield return true;
            var mr=GameManager.Project.FindSubsystem<SubsystemModelsRenderer>(true);
            Cam(origin+new Vector3(3.5f,3,-6),origin+new Vector3(.5f,1.2f,0));Shot("diag_wolf_tower");
            var tm=placed[0].unit.Entity.FindComponent<ComponentMDTowerModel>(true);var wm=wolf.FindComponent<ComponentModel>(true);
            mi.Add($"after capture: drawn {mr.ModelsDrawn} towerVisible {tm.IsVisibleForCamera} wolfVisible {wm.IsVisibleForCamera} registered {mr.m_componentModels.ContainsKey(tm)} light {mr.m_componentModels[tm].Light} wolfLight {mr.m_componentModels[wm].Light} towerRoot {tm.AbsoluteBoneTransformsForCamera[0].Translation} wolfRoot {wm.AbsoluteBoneTransformsForCamera[0].Translation} mode {tm.RenderingMode}/{wm.RenderingMode}");
            yield return true;
            Shot("diag_wolf_tower_next");
            mi.Add($"next frame: drawn {mr.ModelsDrawn} towerVisible {tm.IsVisibleForCamera} towerRoot {tm.AbsoluteBoneTransformsForCamera[0].Translation}");
            if(wolf.IsAddedToProject)GameManager.Project.RemoveEntity(wolf,true);
            var f=Place(placed[0].unit.Tower,origin+new Vector3(-12,6,8),0);while(Wait(.5))yield return true;
            Cam(origin+new Vector3(-12,7.5f,-2),origin+new Vector3(-12,7.4f,8));Shot("diag_float_side");yield return true;
            Cam(origin+new Vector3(-12,40,8.1f),origin+new Vector3(-12,0,8));Shot("diag_top");yield return true;
            Remove([f]);
        }
        if(mode=="combat")foreach(var x in Combat())yield return x;   // Tests/TowerLab/Combat.cs
        if(mode=="mech")foreach(var x in Mech())yield return x;       // Tests/TowerLab/Mech.cs
        if(mode=="phone")foreach(var x in Phone())yield return x;     // Tests/TowerLab/Phone.cs
        if(mode=="fb13"||mode=="faction-mods")foreach(var x in Feedback13())yield return x;   // Tests/TowerLab/Feedback13.cs
        if(mode=="mech-view"||mode.StartsWith("mech-view-"))foreach(var x in MechView())yield return x;   // Tests/TowerLab/MechView.cs
        if(mode=="robots"||mode.StartsWith("robots-"))foreach(var x in Robots())yield return x;   // Tests/TowerLab/Robots.cs
        if(mode=="probe") {
            var (pid,pu,pat)=placed[0];var focus=pat+Vector3.UnitY*1.4f;var eye=focus+new Vector3(.75f,.45f,-1f)*3.6f;
            foreach(var x in Hold(eye,focus))yield return x;Shot("p_a_hold");yield return true;
            Cam(eye+new Vector3(.01f,0,0),focus);Shot("p_b_moved");yield return true;
            foreach(var x in Hold(eye,focus,1))yield return x;Shot("p_c_hold1");yield return true;
            SettingsManager.ObjectsShadowsEnabled=false;foreach(var x in Hold(eye,focus))yield return x;Shot("p_d_noshadow");yield return true;SettingsManager.ObjectsShadowsEnabled=true;
            Remove(placed.Skip(4).Select(p=>p.unit));while(Wait(.5))yield return true;
            foreach(var x in Hold(eye,focus))yield return x;Shot("p_e_four_towers");yield return true;
            Remove(placed.Skip(1).Take(3).Select(p=>p.unit));while(Wait(.5))yield return true;
            foreach(var x in Hold(eye,focus))yield return x;Shot("p_f_one_tower");yield return true;
        }
        if(mode is "recipes" or "special") {
            // every tower, Mercury and progression item recipe resolves from its own grid (no clash with vanilla or each other); tiers (Robots.cs)
            results["recipes"]=RecipeChecks(towers);
        }
        if(mode is "hand" or "special") {
            // the deploy item held in first person (game camera) and third person (fixed camera in front of the player)
            Remove(placed.Select(p=>p.unit));placed.Clear();while(Wait(.5))yield return true;
            var block=BlocksManager.Blocks.First(b=>b is MDDeploymentBlock);var pl=player.ComponentPlayer;var gw=pl.GameWidget;
            var inv=pl.Entity.FindComponent<ComponentCreativeInventory>(true);var H=origin+new Vector3(-17,0,40);
            (float s,Vector3 o,Vector3 r)[] first=[(MDDeploymentBlock.FirstScale,MDDeploymentBlock.FirstOffset,MDDeploymentBlock.FirstRotation)];   // add candidates here to compare poses
            (float s,Vector3 o,Vector3 r)[] third=[(MDDeploymentBlock.HandScale,MDDeploymentBlock.HandOffset,MDDeploymentBlock.HandRotation)];
            IEnumerable<bool> Settle(double seconds,Action each){for(double s0=Time.RealTime;Time.RealTime-s0<seconds;){each();yield return true;}}   // the lab runs at ~800 fps: wait in time, not frames
            foreach(var id in new[]{"battle_turret_1","battle_sniper_1","battle_rocket_1","battle_cannon_1_dg002","battle_jet_1","battle_laser_2"}) {
                var d=towers.First(t=>t.id==id);
                inv.RemoveSlotItems(0,inv.GetSlotCount(0));inv.AddSlotItems(0,Terrain.MakeBlockValue(block.BlockIndex,0,d.data),1);inv.ActiveSlotIndex=0;
                for(int v=0;v<first.Length;v++) {
                    (MDDeploymentBlock.FirstScale,MDDeploymentBlock.FirstOffset,MDDeploymentBlock.FirstRotation)=first[v];
                    foreach(var x in Settle(1.2,()=>{pl.ComponentBody.Position=H;pl.ComponentBody.Rotation=Quaternion.Identity;pl.ComponentLocomotion.LookAngles=new(0,-.15f);gw.ActiveCamera=gw.FindCamera<FppCamera>();}))yield return x;
                    ScreenCaptureManager.Capture(1280,720,$"h_first{v}_{id}.png");yield return true;
                }
                for(int v=0;v<third.Length;v++) {
                    (MDDeploymentBlock.HandScale,MDDeploymentBlock.HandOffset,MDDeploymentBlock.HandRotation)=third[v];
                    foreach(var x in Settle(.6,()=>{pl.ComponentBody.Position=H;pl.ComponentBody.Rotation=Quaternion.Identity;Cam(H+new Vector3(1.1f,1.3f,-1.25f),H+new Vector3(.15f,.85f,-.1f));}))yield return x;
                    Shot($"h_third{v}_{id}");yield return true;
                    foreach(var x in Settle(.3,()=>{pl.ComponentBody.Position=H;pl.ComponentBody.Rotation=Quaternion.Identity;Cam(H+new Vector3(1.9f,1.0f,.2f),H+new Vector3(.2f,.8f,-.1f));}))yield return x;
                    Shot($"h_side{v}_{id}");yield return true;
                }
                foreach(var x in Settle(.6,()=>{pl.ComponentBody.Position=H;pl.ComponentBody.Rotation=Quaternion.Identity;gw.ActiveCamera=gw.FindCamera<TppCamera>();}))yield return x;
                ScreenCaptureManager.Capture(1280,720,$"h_tppcam_{id}.png");yield return true;
            }
            (MDDeploymentBlock.FirstScale,MDDeploymentBlock.FirstOffset,MDDeploymentBlock.FirstRotation)=first[0];(MDDeploymentBlock.HandScale,MDDeploymentBlock.HandOffset,MDDeploymentBlock.HandRotation)=third[0];
            inv.RemoveSlotItems(0,inv.GetSlotCount(0));pl.ComponentBody.Position=Home;results["hand"]="screenshots h_*";
        }
        if(mode is "effects" or "special") {
            // special effects on real creatures: a wolf (AttackResilience 400) and a boss-strength one (5000);
            // for the water jet the second wolf weighs 1500 kg instead (rhino class) and must not be pushed
            Remove(placed.Select(p=>p.unit));placed.Clear();player.ComponentPlayer.ComponentBody.Position=Home;while(Wait(.5))yield return true;
            var w=MDTowerWorld.For(GameManager.Project);var T=origin+new Vector3(-17,0,40);var eff=new Dictionary<string,object>();
            ComponentCreature Wolf(Vector3 at,float res) {
                var e=DatabaseManager.CreateEntity(GameManager.Project,"Wolf",true);e.FindComponent<ComponentBody>(true).Position=at;e.FindComponent<ComponentSpawn>(true).SpawnDuration=0;
                GameManager.Project.AddEntity(e);var c=e.FindComponent<ComponentCreature>(true);c.ComponentHealth.AttackResilience=res;return c;
            }
            string State(ComponentCreature c)=>!c.Entity.IsAddedToProject?"gone":$"{(w.IsFrozen(c.ComponentBody)?"F":w.IsStunned(c.ComponentBody)?"S":"-")}{(w.IsPoisoned(c.ComponentBody)?"P":"-")} v{w.SpeedFactor(c.ComponentBody):F2} walk{c.ComponentLocomotion.WalkSpeed:F2} dmg x{w.DamageFactor(c.ComponentBody):F2} hp{c.ComponentHealth.Health:F4}";
            foreach(var id in new[]{"battle_frost_1","battle_fog_1","battle_debuff_1","battle_lightning_2","battle_shockwave_1","battle_jet_1"}) {
                var d=towers.First(t=>t.id==id);var u=Place(d,T,0);u.Cooldown=1e6f;bool jet=id=="battle_jet_1";
                float dist=d.attack.directional?3.5f:Math.Min(d.attack.range*.5f,7);
                var pa=T+new Vector3(-.5f,0,-dist);var pb=T+new Vector3(.8f,0,-dist-.5f);
                var a=Wolf(pa,400);var b=Wolf(pb,jet?400:5000);if(jet){b.ComponentBody.Mass=1500;a.ComponentLocomotion.WalkSpeed=b.ComponentLocomotion.WalkSpeed=0;}   // jet: only the push may move them
                for(int i=0;i<40;i++){a.ComponentBody.Position=pa;b.ComponentBody.Position=pb;a.ComponentBody.Velocity=b.ComponentBody.Velocity=Vector3.Zero;Cam(T+new Vector3(5,3.5f,-dist+4),T+new Vector3(0,.8f,-dist));yield return true;}
                var line=new List<string>();u.Cooldown=0;int shots=u.ShotCount;double start=Time.RealTime;double next=0;int k=0;bool moved=false;var a0=a.ComponentBody.Position;var b0=b.ComponentBody.Position;
                while(Time.RealTime-start<8.5) {
                    double t=Time.RealTime-start;if(u.ShotCount>shots)u.Cooldown=1e6f;   // one attack only
                    if(id=="battle_fog_1"&&!moved&&t>2.5){moved=true;a.ComponentBody.Position=T+new Vector3(-7,0,-dist);line.Add($"{t:F2} wolf A carried out of the cloud");}
                    if(t>=next){line.Add($"{t:F2} A[{State(a)}] B[{State(b)}] zones {w.Zones}");next+=.5;}
                    if(k<3&&t>=new[]{.9,1.6,3.0}[k]){Shot($"e_{id}_{k}");if(k==1&&id=="battle_frost_1"){var ap=a.ComponentBody.Position;Cam(ap+new Vector3(2.2f,1.3f,1.9f),ap+Vector3.UnitY*.4f);Shot($"e_frozen_close_{(w.IsFrozen(a.ComponentBody)?"frozen":"thawed")}");}k++;}
                    Cam(T+new Vector3(3f,2.2f,-dist+2.6f),T+new Vector3(0,.5f,-dist));yield return true;
                }
                if(jet)line.Add($"pushed along -Z: A {(a0-a.ComponentBody.Position).Z:F2} m, B (1500 kg) {(b0-b.ComponentBody.Position).Z:F2} m");
                eff[id]=line;
                foreach(var c in new[]{a,b})if(c.Entity.IsAddedToProject)GameManager.Project.RemoveEntity(c.Entity,true);
                Remove([u]);while(Wait(1))yield return true;
            }
            results["effects"]=eff;
        }
        if(mode is "artillery" or "special") {
            // the prologue cannon elevates and its shells leave along the barrel: targets at three distances
            Remove(placed.Select(p=>p.unit));placed.Clear();player.ComponentPlayer.ComponentBody.Position=Home;while(Wait(.5))yield return true;
            var w=MDTowerWorld.For(GameManager.Project);var T=origin+new Vector3(-17,0,30);var art=new List<string>();
            var gun=towers.First(t=>t.id=="battle_cannon_1_dg002");var u=Place(gun,T,0);u.Cooldown=1e6f;
            art.Add($"pitch range {MathUtils.RadToDeg(gun.PitchMin):F0}..{MathUtils.RadToDeg(gun.PitchMax):F0} deg, fixed {gun.pitchFixed}");
            foreach(var dist in new[]{14f,26f,38f}) {
                foreach(var c in GameManager.Project.FindSubsystem<SubsystemCreatureSpawn>(true).Creatures.ToList())   // wild animals spawn in the lab world;
                    if(c is not ComponentPlayer&&Vector3.Distance(c.ComponentBody.Position,T)<70)GameManager.Project.RemoveEntity(c.Entity,true);   // the gun must shoot the dummy
                var spot=T+new Vector3(0,0,-dist);var target=Dummy(spot);while(Wait(.3))yield return true;
                void Pin(){target.Body.Position=spot;target.Body.Velocity=Vector3.Zero;}   // keep the dummy on its mark (shots push it)
                u.Cooldown=0;int shots=u.ShotCount;double start=Time.RealTime;
                while(u.ShotCount==shots&&Time.RealTime-start<8){Pin();Cam(T+new Vector3(10,4,-5),T+new Vector3(0,3,-5));yield return true;}
                u.Cooldown=1e6f;bool onDummy=u.Target==target.Body;
                var bd=MDTowerLibrary.MuzzleForward(gun,MDTowerBrain.Pose(u),0);var lv=w.LastLaunch;
                float off=MathUtils.RadToDeg(MathF.Acos(Math.Clamp(Vector3.Dot(bd,Vector3.Normalize(lv)),-1,1)));
                Shot($"a_dg002_{(int)dist}_fire");
                double t1=Time.RealTime;int imp=w.Impacts;bool flight=false;
                while(Time.RealTime-t1<5&&w.Impacts==imp){Pin();if(!flight&&Time.RealTime-t1>.35){Shot($"a_dg002_{(int)dist}_flight");flight=true;}Cam(T+new Vector3(16,5,-dist*.5f),T+new Vector3(0,3,-dist*.5f));yield return true;}
                art.Add($"target {dist} m: barrel {MathUtils.RadToDeg(u.TowerPitch):F1} deg, shell {MathUtils.RadToDeg(MathF.Asin(Vector3.Normalize(lv).Y)):F1} deg at {lv.Length():F1} m/s, shell vs barrel {off:F2} deg, fired after {Time.RealTime-start:F1} s, aimed at the dummy {onDummy}, landed {w.Impacts>imp}, dummy hit {target.Hit}");
                Remove([target]);while(Wait(.5))yield return true;
            }
            // the cradle at both ends of its range: pistons and the centre strut must follow without gaps or clipping
            var pv=MDTowerLibrary.Pose(gun,u.Body.Matrix,0,0,null)[gun.pitchNode].Translation;
            foreach(var (tag,pd) in new[]{("min",gun.PitchMin),("mid",(gun.PitchMin+gun.PitchMax)/2),("max",gun.PitchMax)})
                foreach(var (side,off) in new[]{("l",new Vector3(-4.2f,.4f,-.6f)),("r",new Vector3(4.2f,.4f,-.6f))}) {
                    foreach(var x in Hold(pv+off,pv-Vector3.UnitY*.7f,10)){u.AimYaw=u.Yaw;u.AimPitch=pd;yield return x;}
                    Shot($"a_dg002_pitch_{tag}_{side}");yield return true;
                }
            results["artillery"]=art;Remove([u]);while(Wait(.5))yield return true;
        }
        if(mode is "fx" or "joints") {
            // isolated test site: no other towers around
            Remove(placed.Select(p=>p.unit));placed.Clear();while(Wait(.5))yield return true;
            var T=origin+new Vector3(-17,0,40);var w=MDTowerWorld.For(GameManager.Project);var fxlog=new Dictionary<string,object>();
            foreach(var d in towers) {
                var u=Place(d,T,0);var a=d.attack;float h=d.bounds[1][1];
                if(mode=="joints") {
                    if(!d.HasYaw&&d.pitchNode<0){Remove([u]);continue;}
                    u.Cooldown=1e6f;float yb=d.nodes[Math.Max(1,d.yawNode)].translation[1];
                    var jy=T+new Vector3(0,d.HasYaw?Math.Max(.3f,h*.35f):h*.5f,0);
                    float[] yaws=d.HasYaw?new float[]{0,45,90,135,180}:new float[]{0};
                    foreach(var yd in yaws) {
                        for(int i=0;i<6;i++){u.AimYaw=u.Yaw+MathUtils.DegToRad(yd);u.AimPitch=0;Cam(jy+new Vector3(1.6f,.7f,-1.9f)*Math.Max(1.3f,h*.55f),jy);yield return true;}
                        Shot($"j_{d.id}_yaw{yd}");yield return true;
                    }
                    if(d.pitchNode>=0&&!d.pitchFixed) {
                        var pv=MDTowerLibrary.Pose(d,u.Body.Matrix,0,0,null)[d.pitchNode].Translation;
                        foreach(var pd in new[]{d.PitchMin,d.PitchMax}) {
                            for(int i=0;i<8;i++){u.AimYaw=u.Yaw;u.AimPitch=pd;Cam(pv+new Vector3(2.6f,.3f,-.4f)*Math.Max(1f,h*.45f),pv);yield return true;}
                            Shot($"j_{d.id}_pitch{(int)MathUtils.RadToDeg(pd)}");yield return true;
                        }
                    }
                    Remove([u]);while(Wait(.2))yield return true;continue;
                }
                float dist=a.type=="heal"?3:a.directional?Math.Min(a.range*.6f,5):Math.Max(a.minRange+3,Math.Min(a.range*.6f,8));
                var target=a.type=="heal"?null:Dummy(T+new Vector3(0,0,-dist));
                if(a.type=="heal"){player.ComponentPlayer.ComponentBody.Position=T+new Vector3(2,0,-1.5f);player.ComponentPlayer.ComponentHealth.Health=.7f;}
                u.Cooldown=1e6f;
                var eye=T+new Vector3(dist*.38f+3f+h*.45f,1.6f+h*.22f,-dist*.45f);var focus=T+new Vector3(0,h*.42f,-dist*.5f);
                foreach(var x in Hold(eye,focus,40))yield return x;        // let it acquire and aim
                int impacts=w.Impacts,shots=u.ShotCount;u.Cooldown=0;double start=Time.RealTime;int k=0;
                foreach(var dt in new[]{.03,.08,.15,.25,.4,.6,.9,1.3,2.0}) {
                    while(Time.RealTime-start<dt){Cam(eye,focus);yield return true;}
                    Shot($"x_{d.id}_{k++}");yield return true;
                }
                fxlog[d.id]=new{fired=u.ShotCount-shots,impacts=w.Impacts-impacts,targetDamaged=target!=null&&target.Hit,particles=w.Particles,
                    healed=a.type=="heal"?player.ComponentPlayer.ComponentHealth.Health:0f};
                Remove([u]);if(target!=null)Remove([target]);player.ComponentPlayer.ComponentBody.Position=Home;
                while(Wait(.6))yield return true;
            }
            results["fx"]=fxlog;
        }
        if(mode is "gallery" or "all") {
            foreach(var (id,u,at) in placed) {
                var d=u.Tower;float h=d.bounds[1][1];float s=Math.Max(h,Math.Max(d.bounds[1][0]-d.bounds[0][0],d.bounds[1][2]-d.bounds[0][2]));
                var focus=at+Vector3.UnitY*h*.5f;
                // tower forward is -Z after yaw 0; front-right three-quarter view and a back-left one
                foreach(var x in Hold(focus+new Vector3(.75f,.45f,-1f)*s*1.25f,focus))yield return x;Shot($"g_{id}_fr");{var tm=u.Entity.FindComponent<ComponentMDTowerModel>(true);var mr=GameManager.Project.FindSubsystem<SubsystemModelsRenderer>(true);camlog.Add($"{id}: visible {tm.IsVisibleForCamera} registered {mr.m_componentModels.ContainsKey(tm)} added {u.Entity.IsAddedToProject} hp {u.Damage.Hitpoints} destroyed {u.Destroyed} wreck {u.WreckTime}");}yield return true;
                foreach(var x in Hold(focus+new Vector3(-.8f,.35f,.9f)*s*1.25f,focus))yield return x;Shot($"g_{id}_bl");yield return true;
                if(d.muzzles.Length>0) {   // look into the muzzle from just in front and below (bore see-through), then from under the head (seams)
                    var pose=MDTowerBrain.Pose(u);var mz=MDTowerLibrary.Muzzle(d,pose,0);var fw=MDTowerLibrary.MuzzleForward(d,pose,0);
                    var sideV=Vector3.Normalize(Vector3.Cross(fw,Vector3.UnitY)+new Vector3(.001f,0,0));
                    foreach(var x in Hold(mz+fw*1.1f-Vector3.UnitY*.45f+sideV*.2f,mz-fw*.4f))yield return x;Shot($"m_{id}_bore");yield return true;
                    var hv=d.pitchNode>=0?pose[d.pitchNode].Translation:mz-fw*.5f;
                    foreach(var x in Hold(hv+fw*1.6f-Vector3.UnitY*1.3f+sideV*.6f,hv))yield return x;Shot($"m_{id}_under");yield return true;
                }
            }
            // flicker sequences: tiny camera steps, near and far
            foreach(var id in new[]{"battle_turret_3","battle_turret_1","battle_cannon_2","battle_rocket_1"}) {
                var (_,u,at)=placed.First(p=>p.id==id);float h=u.Tower.bounds[1][1];var focus=at+Vector3.UnitY*h*.6f;
                for(int k=0;k<6;k++){var eye=focus+new Vector3(.9f+k*.004f,.35f,-1.1f)*4.2f;foreach(var x in Hold(eye,focus,2))yield return x;Shot($"f_{id}_near_{k}");yield return true;}
                for(int k=0;k<6;k++){var eye=focus+new Vector3(.9f+k*.002f,.25f,-1.1f)*22f;foreach(var x in Hold(eye,focus,2))yield return x;Shot($"f_{id}_far_{k}");yield return true;}
            }
            foreach(var x in Hold(origin+new Vector3(22,26,-30),origin+new Vector3(22,0,20)))yield return x;Shot("overview_far");yield return true;
            foreach(var x in Hold(origin+new Vector3(-8,7,-12),origin+new Vector3(25,1,20)))yield return x;Shot("overview_row");yield return true;
            // hotbar icons: two pages of nine deploy items, GUI visible
            var block=BlocksManager.Blocks.First(b=>b is MDDeploymentBlock);var inv=player.ComponentPlayer.Entity.FindComponent<ComponentCreativeInventory>(true);
            SettingsManager.ShowGuiInScreenshots=true;
            for(int page=0;page<3;page++) {
                var values=block.GetCreativeValues().Skip(page*9).Take(9).ToList();
                for(int i=0;i<9;i++){inv.RemoveSlotItems(i,inv.GetSlotCount(i));if(i<values.Count)inv.AddSlotItems(i,values[i],1);}
                foreach(var x in Hold(origin+new Vector3(-8,7,-12),origin+new Vector3(25,1,20)))yield return x;Shot($"icons_{page}");yield return true;
            }
            SettingsManager.ShowGuiInScreenshots=false;
        }
        if(mode=="icons") {
            // slot pictures (0.9.0 round 5): hotbar pages of every tower and mech, then the robot summons, GUI on; each page
            // also at 2560x1440, where the slots are twice as large
            var block=BlocksManager.Blocks.First(b=>b is MDDeploymentBlock);var summon=BlocksManager.Blocks.First(b=>b is MDSummonBlock);
            var inv=player.ComponentPlayer.Entity.FindComponent<ComponentCreativeInventory>(true);
            var pages=block.GetCreativeValues().Chunk(9).Concat(summon.GetCreativeValues().Chunk(9)).ToList();
            SettingsManager.ShowGuiInScreenshots=true;
            for(int page=0;page<pages.Count;page++) {
                for(int i=0;i<9;i++){inv.RemoveSlotItems(i,inv.GetSlotCount(i));if(i<pages[page].Length)inv.AddSlotItems(i,pages[page][i],1);}
                foreach(var x in Hold(origin+new Vector3(-8,7,-12),origin+new Vector3(25,1,20)))yield return x;
                Shot($"icons_{page}");ScreenCaptureManager.Capture(2560,1440,$"icons_{page}_big.png");yield return true;
            }
            SettingsManager.ShowGuiInScreenshots=false;results["iconPages"]=pages.Count;
        }
        if(mode.StartsWith("view-")) {
            // one tower (mode view-<id>) seen the way a player sees it; the same shots from two packages make a before / after
            // (0.9.0 round 5: the reduced battle_rocket_1). Aim locked (no targets: a long-range tower turns to whatever wild
            // creature is around), no clouds, so only the model differs. Around, player height, close-ups from outside its box,
            // night, in the hand, tiny camera steps near and far (flicker), firing at a wolf, then fog and a torch-lit cave.
            var d=towers.First(t=>t.id==mode[5..]);var views=new List<string>();
            // 5 s for the gallery's shells to land and their ground effects to fade: an acid puddle (3.5 s) from the 腐蚀塔
            // showed up in the first shots of a run
            Remove(placed.Select(p=>p.unit));placed.Clear();while(Wait(5))yield return true;
            SettingsManager.SkyRenderingMode=SkyRenderingMode.NoClouds;
            var at=origin+new Vector3(10,0,14);var u=Place(d,at,0);   // faces -Z
            void Freeze(){u.Target=null;u.ScanTimer=1e6f;u.Cooldown=1e6f;u.Yaw=u.AimYaw=0;}
            for(double s0=Time.RealTime;Time.RealTime-s0<1.5;){Freeze();yield return true;}
            float h=d.bounds[1][1],s=Math.Max(h,Math.Max(d.bounds[1][0]-d.bounds[0][0],d.bounds[1][2]-d.bounds[0][2]));var focus=at+Vector3.UnitY*h*.5f;
            IEnumerable<bool> View(string name,Vector3 eye,Vector3 look,int frames=4){for(int k=0;k<frames;k++){Freeze();Cam(eye,look);yield return true;}Freeze();Shot("v_"+name);views.Add(name);yield return true;}
            Vector3 P(float x,float y,float z)=>at+new Vector3(x*s,y*h,z*s);   // x, z in tower sizes from its axis, y in heights
            for(int k=0;k<8;k++){float a=k*MathF.PI/4;foreach(var x in View($"around_{k*45:000}",focus+new Vector3(-MathF.Sin(a),.36f,-MathF.Cos(a))*s*1.6f,focus))yield return x;}
            foreach(var x in View("top",focus+new Vector3(.02f,s*1.8f,-.3f),focus))yield return x;
            foreach(var x in View("player_front",at+new Vector3(.8f,1.7f,-5),focus))yield return x;
            foreach(var x in View("player_back",at+new Vector3(-1.2f,1.7f,4.5f),focus))yield return x;
            foreach(var x in View("close_upper_front",P(.25f,.95f,-.8f),P(0,.75f,-.15f)))yield return x;
            foreach(var x in View("close_upper_right",P(.8f,.85f,-.1f),P(0,.7f,0)))yield return x;
            foreach(var x in View("close_upper_back",P(-.2f,.9f,.8f),P(0,.7f,0)))yield return x;
            foreach(var x in View("close_mid_right",P(.8f,.45f,-.3f),P(0,.4f,0)))yield return x;
            foreach(var x in View("close_base_front",P(.35f,.3f,-.8f),P(0,.15f,0)))yield return x;
            foreach(var x in View("close_base_back",P(-.4f,.3f,.8f),P(0,.15f,0)))yield return x;
            var pose=MDTowerBrain.Pose(u);var mz=d.muzzles.Length>0?MDTowerLibrary.Muzzle(d,pose,0):P(0,.9f,0);
            foreach(var x in View("close_muzzle",mz+new Vector3(.45f,.5f,-.7f),mz))yield return x;
            foreach(var x in View("very_close_muzzle",mz+new Vector3(.22f,.25f,-.35f),mz))yield return x;
            var gi=GameManager.Project.FindSubsystem<SubsystemGameInfo>(true);gi.WorldSettings.TimeOfDayMode=TimeOfDayMode.Night;
            foreach(var x in View("night_front",at+new Vector3(.8f,1.7f,-5),focus,20))yield return x;
            gi.WorldSettings.TimeOfDayMode=TimeOfDayMode.Day;foreach(var x in View("day_again",at+new Vector3(.8f,1.7f,-5),focus,20))yield return x;
            var near=P(0,.7f,0);
            for(int k=0;k<6;k++)foreach(var x in View($"flicker_near_{k}",near+new Vector3(.9f+k*.004f,.35f,-1.1f)*s*.9f,near,2))yield return x;
            for(int k=0;k<6;k++)foreach(var x in View($"flicker_far_{k}",focus+new Vector3(.9f+k*.002f,.25f,-1.1f)*22f,focus,2))yield return x;
            {   // in the hand, first person (the full model is drawn there)
                var block=BlocksManager.Blocks.First(b=>b is MDDeploymentBlock);var pl=player.ComponentPlayer;var gw=pl.GameWidget;var H=origin+new Vector3(-17,0,40);
                var inv=pl.Entity.FindComponent<ComponentCreativeInventory>(true);inv.RemoveSlotItems(0,inv.GetSlotCount(0));inv.AddSlotItems(0,Terrain.MakeBlockValue(block.BlockIndex,0,d.data),1);inv.ActiveSlotIndex=0;
                foreach(var x in During(1.2,()=>{Freeze();pl.ComponentBody.Position=H;pl.ComponentBody.Rotation=Quaternion.Identity;pl.ComponentLocomotion.LookAngles=new(0,-.15f);gw.ActiveCamera=gw.FindCamera<FppCamera>();}))yield return x;
                ScreenCaptureManager.Capture(1280,720,"v_hand_first.png");views.Add("hand_first");yield return true;
                inv.RemoveSlotItems(0,inv.GetSlotCount(0));pl.ComponentBody.Position=Home;
            }
            {   // firing at a wolf (aim free again)
                var a=d.attack;float dist=Math.Max(a.minRange+2,Math.Min(a.range*.6f,9));var target=Dummy(at+new Vector3(0,0,-dist));
                u.ScanTimer=0;u.Cooldown=1e6f;while(Wait(.6))yield return true;
                u.Cooldown=0;int shots0=u.ShotCount;var f=at+new Vector3(0,h*.45f,-dist*.5f);Cam(f+new Vector3(1.0f,.55f,-.25f)*Math.Max(9,dist*1.6f),f);
                double start=Time.RealTime;int k=0;
                foreach(var dt in new[]{.04,.25,.8,2.0}){while(Time.RealTime-start<dt){Cam(lastEye,lastFocus);yield return true;}Shot($"v_fire_{k}");views.Add($"fire_{k++}");yield return true;}
                if(d.spinParts.Length>0) {   // rotary barrels (0.9.0 round 6): close to the first muzzle while still firing, two shots two frames apart
                    var m0=MDTowerBrain.Muzzle(u,0);var ce=m0+new Vector3(1.4f,.5f,-1.1f);var spins=new List<string>();
                    for(int sp=0;sp<2;sp++){for(int n=0;n<(sp==0?6:2);n++){Cam(ce,m0);yield return true;}spins.Add($"{u.Spin:0.000}");Shot($"v_fire_spin_{sp}");views.Add($"fire_spin_{sp}");yield return true;}
                    results["spin"]=$"rate {u.SpinRate:0.00}, angle at the two shots {string.Join(" / ",spins)} rad, {d.spinParts.Length} parts, speed {d.spinSpeed} deg/s";
                }
                results["fired"]=u.ShotCount-shots0;Remove([target]);
            }
            if(d.attack.type=="radar") {   // 雷达 (0.10.0): every tower in range gets range x boost within a pulse (four guns, more than the old limit of 3), one beyond it doesn't
                var gun=towers.First(t=>t.id=="gatling_1");var gs=new List<ComponentMDUnit>();
                foreach(var off in new[]{6f,9f,12f,15f})gs.Add(Place(gun,at+new Vector3(-off,0,off%2==0?0:5),0));
                var far=Place(gun,at+new Vector3(d.attack.range+4,0,0),0);float r0=far.Range;
                u.Cooldown=0;var ce=at+new Vector3(-6,h*.55f,2);
                for(double st=Time.RealTime;Time.RealTime-st<1.4;){foreach(var g in gs)g.Cooldown=1e6f;far.Cooldown=1e6f;Cam(ce+new Vector3(3,h*.8f,-14),ce);yield return true;}
                Shot("v_radar_link");views.Add("radar_link");yield return true;
                results["radar"]=$"guns in range {string.Join(" / ",gs.Select(g=>$"{g.Range:0.0}"))} (from {r0:0.0}, expected x{d.attack.boost:0.00}), gun beyond range {far.Range:0.0}, spin {u.Spin:0.00} rad at rate {u.SpinRate:0.00}";
                bool ok=gs.All(g=>Math.Abs(g.Range-r0*d.attack.boost)<.05f)&&Math.Abs(far.Range-r0)<.01f&&u.SpinRate>.5f;Remove(gs);Remove([far]);
                if(!ok)throw new Exception("radar: "+results["radar"]);   // the runner saves passed=false with this message
            }
            {   // fog: weather on, rain kept away, full fog. The fog layer's height comes from a seed taken from the fog start
                // time, so step the start time back until the layer reaches the ground around the tower.
                for(double s0=Time.RealTime;Time.RealTime-s0<4;){Freeze();yield return true;}   // the shot's explosion and smoke fade first
                var wx=GameManager.Project.FindSubsystem<SubsystemWeather>(true);var sky=GameManager.Project.FindSubsystem<SubsystemSky>(true);
                gi.WorldSettings.AreWeatherEffectsEnabled=true;wx.m_precipitationStartTime=wx.m_precipitationEndTime=double.MaxValue;
                double fogNow=Math.Floor(gi.TotalElapsedGameTime);string fogInfo="none";
                for(int k=1;k<=400&&fogInfo=="none";k++) {
                    for(int n=0;n<2;n++){Freeze();wx.m_fogStartTime=fogNow-k;wx.m_fogEndTime=double.PositiveInfinity;wx.FogIntensity=1;Cam(at+new Vector3(.8f,1.7f,-5),focus);yield return true;}
                    if(sky.ViewFogBottom<=at.Y+.5f&&sky.ViewFogTop>=at.Y+h+2&&sky.ViewFogDensity>=.05f)fogInfo=$"start -{k} s: bottom {sky.ViewFogBottom:0.0}, top {sky.ViewFogTop:0.0}, density {sky.ViewFogDensity:0.000}";
                }
                results["fog"]=fogInfo;
                foreach(var x in View("fog_front",at+new Vector3(.8f,1.7f,-5),focus,10))yield return x;
                foreach(var x in View("fog_mid",focus+new Vector3(.9f,.25f,-1.1f)*9f,focus,6))yield return x;
                foreach(var x in View("fog_far",focus+new Vector3(.9f,.25f,-1.1f)*22f,focus,6))yield return x;
                wx.FogIntensity=0;gi.WorldSettings.AreWeatherEffectsEnabled=false;for(int n=0;n<3;n++){Freeze();yield return true;}
            }
            {   // cave: a closed granite room around the tower, four torches on the floor
                var ter=GameManager.Project.FindSubsystem<SubsystemTerrain>(true);int ax=(int)MathF.Floor(at.X),ay=(int)MathF.Floor(at.Y),az=(int)MathF.Floor(at.Z);
                for(int cx=ax-7;cx<=ax+7;cx++)for(int cz=az-9;cz<=az+7;cz++)for(int cy=ay-1;cy<=ay+8;cy++)
                    if(cx==ax-7||cx==ax+7||cz==az-9||cz==az+7||cy==ay-1||cy==ay+8)ter.ChangeCell(cx,cy,cz,3);
                foreach(var (cx,cz) in new[]{(ax-3,az-3),(ax+3,az-3),(ax-3,az+3),(ax+3,az+3)})ter.ChangeCell(cx,ay,cz,Terrain.MakeBlockValue(31,0,4));
                for(double s0=Time.RealTime;Time.RealTime-s0<3;){Freeze();yield return true;}
                results["caveLight"]=Terrain.ExtractLight(ter.Terrain.GetCellValue(ax,ay+2,az-4));
                foreach(var x in View("cave_front",at+new Vector3(.8f,1.7f,-5),focus,10))yield return x;
                foreach(var x in View("cave_right",at+new Vector3(5,1.7f,-2.5f),focus,6))yield return x;
                foreach(var x in View("cave_back",at+new Vector3(-1.2f,1.7f,4.5f),focus,6))yield return x;
            }
            SettingsManager.SkyRenderingMode=SkyRenderingMode.Full;results["views"]=views;results["box"]=$"{s:0.00} x {h:0.00}";
        }
        if(mode is "fire" or "all") {
            // one tower at a time: a dummy target in range, capture the sequence after its first shot
            var shotsLog=new Dictionary<string,object>();
            foreach(var (id,u,at) in placed) {
                foreach(var o in placed)o.unit.Cooldown=1e6f;
                var d=u.Tower;var a=d.attack;float dist=a.type=="heal"?3:a.directional?Math.Min(a.range*.6f,5):Math.Max(a.minRange+2,Math.Min(a.range*.6f,9));
                var target=Dummy(at+new Vector3(0,0,-dist));var w=MDTowerWorld.For(GameManager.Project);int impacts=w.Impacts;
                if(a.type=="heal"){player.ComponentPlayer.ComponentBody.Position=at+new Vector3(2,0,-1);player.ComponentPlayer.ComponentHealth.Health=.7f;}
                while(Wait(.6))yield return true;
                u.Cooldown=0;int shots=u.ShotCount;var focus=at+new Vector3(0,d.bounds[1][1]*.45f,-dist*.5f);
                Cam(focus+new Vector3(1.0f,.55f,-.25f)*Math.Max(9,dist*1.6f),focus);
                double start=Time.RealTime;int k=0;
                foreach(var dt in new[]{.04,.12,.25,.45,.8,1.3,2.0}) {
                    while(Time.RealTime-start<dt){Cam(lastEye,lastFocus);yield return true;}
                    Shot($"x_{id}_{k++}");yield return true;
                }
                shotsLog[id]=new{fired=u.ShotCount-shots,impacts=w.Impacts-impacts,targetHit=target.Hit,particles=w.Particles,
                    healed=a.type=="heal"?player.ComponentPlayer.ComponentHealth.Health:0f};
                Remove([target]);player.ComponentPlayer.ComponentBody.Position=Home;
                while(Wait(.4))yield return true;
            }
            results["fire"]=shotsLog;
        }
        if(mode is "perf" or "all") {
            var perf=new Dictionary<string,object>();
            IEnumerable<bool> Measure(string name,double seconds,Vector3 eye,Vector3 focus) {
                Cam(eye,focus);for(int i=0;i<30;i++)yield return true;
                var frames=new List<double>();double last=Time.RealTime,end=Time.RealTime+seconds;var sw=Stopwatch.StartNew();
                while(Time.RealTime<end){yield return true;frames.Add(Time.RealTime-last);last=Time.RealTime;}
                frames.Sort();perf[name]=new{frames=frames.Count,avgMs=frames.Average()*1000,p95Ms=frames[(int)(frames.Count*.95)]*1000,maxMs=frames[^1]*1000,
                    particles=MDTowerWorld.For(GameManager.Project).Particles};
                Log($"perf {name}: {frames.Average()*1000:F2} ms");
            }
            foreach(var o in placed)o.unit.Cooldown=1e6f;
            player.ComponentPlayer.ComponentBody.Position=origin+new Vector3(-20,0,-25);   // keep the player clear of the extra rows
            var eye=origin+new Vector3(22,22,-26);var focus=origin+new Vector3(22,0,24);
            foreach(var x in Measure("row19_idle",4,eye,focus))yield return x;
            // 3 more rows of every tower: 76 towers total in view
            var extra=new List<ComponentMDUnit>();
            for(int r=1;r<=3;r++)for(int i=0;i<towers.Count;i++)extra.Add(Place(towers[i],Slot(i)+new Vector3(r%2*5.5f,0,r/2*7+(r==1?7:0)),0));
            while(Wait(1))yield return true;
            foreach(var x in Measure("rows76_idle",4,eye,focus))yield return x;
            // enemies in front of everything, all towers firing
            var dummies=new List<Dum>();
            for(int i=0;i<towers.Count;i++)dummies.Add(Dummy(Slot(i)+new Vector3(2.7f,0,-4)));
            foreach(var o in placed)o.unit.Cooldown=0;foreach(var o in extra)o.Cooldown=0;
            while(Wait(2))yield return true;
            foreach(var x in Measure("rows76_firing",6,eye,focus))yield return x;
            Shot("perf_firing");yield return true;
            Remove(dummies);Remove(extra);
            while(Wait(1))yield return true;
            foreach(var x in Measure("empty_view",3,origin+new Vector3(22,30,-30),origin+new Vector3(22,40,-90)))yield return x;
            results["perf"]=perf;
        }
        Log("done");
    }
}
[HarmonyPatch(typeof(SubsystemModelsRenderer),nameof(SubsystemModelsRenderer.DrawInstancedModels))]
public static class DrawProbe {
    public static readonly List<string> Errors=[];
    public static Exception Finalizer(Exception __exception,List<SubsystemModelsRenderer.ModelData> modelsData) {
        if(__exception!=null&&Errors.Count<5)Errors.Add(string.Join(",",modelsData.Select(m=>m.ComponentModel.Entity.ValuesDictionary.DatabaseObject.Name).Distinct())+" :: "+__exception);
        return __exception;
    }
}
[HarmonyPatch(typeof(FileAssociationManager),nameof(FileAssociationManager.Initialize))]
public static class NoFileAssociation { public static bool Prefix()=>false; }
// The test game runs while the user keeps working. With no menu open the engine sets the mouse to raw mode in
// Engine.Input.Mouse.BeforeFrame and again in AfterFrame whenever its window counts as active, which it does even behind other
// windows; raw mode clips the system cursor to the game window (measured: clipped in 7 of 133 samples over a 27 s run). The
// tests never read the mouse, so both places see a visible mouse and keep the cursor normal.
[HarmonyPatch(typeof(Engine.Input.Mouse),"BeforeFrame")]
public static class KeepCursorFree { public static void Prefix()=>Engine.Input.Mouse.IsMouseVisible=true; }
[HarmonyPatch(typeof(Engine.Input.Mouse),"AfterFrame")]
public static class KeepCursorFreeAfter { public static void Prefix()=>Engine.Input.Mouse.IsMouseVisible=true; }
