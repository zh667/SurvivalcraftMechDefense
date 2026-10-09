using Engine;
using Engine.Graphics;
using Game;
using HarmonyLib;
using System.Diagnostics;
using System.Text.Json;

namespace TowerLab;

// Isolated in-game lab: builds a flat creative world, places every tower, and records
// screenshots (gallery, flicker sequences, firing sequences) plus frame-time measurements.
// Mode (lab-mode.txt next to the exe): "gallery", "fire", "perf" or "all".
public class Lab:ModLoader {
    static int stage;static double began,step;static WorldInfo world;static PlayerData player;static Vector3 origin;
    static readonly List<string> log=[];static readonly Dictionary<string,object> results=[];
    static readonly List<(string id,ComponentMDUnit unit,Vector3 at)> placed=[];
    static IEnumerator<bool> script;static string mode="all";
    static string Out=>Path.Combine(AppContext.BaseDirectory,"lab-result.json");
    public override void __ModInitialize(){new Harmony("local.towerlab").PatchAll(typeof(Lab).Assembly);SettingsManager.FileAssociationEnabled=false;
        var m=Path.Combine(AppContext.BaseDirectory,"lab-mode.txt");if(File.Exists(m))mode=File.ReadAllText(m).Trim();}
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
                world=WorldsManager.CreateWorld(new WorldSettings{Name="TowerLab_"+DateTime.Now.ToString("HHmmss"),Seed="7",GameMode=GameMode.Creative,TerrainGenerationMode=TerrainGenerationMode.FlatContinent,TerrainLevel=64,TerrainBlockIndex=8,AreWeatherEffectsEnabled=false,TimeOfDayMode=TimeOfDayMode.Day});
                GameManager.LoadProject(world,ScreensManager.FindScreen<GameScreen>("Game").Children.Find<ContainerWidget>("GamesWidget"));
                player=new PlayerData(GameManager.Project){Name="Lab",SpawnPosition=new(100,65,100),InputDevice=WidgetInputDevice.None};GameManager.Project.FindSubsystem<SubsystemPlayers>().AddPlayerData(player);ScreensManager.SwitchScreen("Game");stage=1;return;
            }
            if(stage==1) {
                if(!player.IsReadyForPlaying)return;
                script=Run().GetEnumerator();stage=2;step=Time.RealTime;return;
            }
            if(stage==2) {
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
    static ComponentMDUnit Dummy(Vector3 at) {
        var e=DatabaseManager.CreateEntity(GameManager.Project,"MDEnemy",true);var u=e.FindComponent<ComponentMDUnit>();u.Body.Position=at;u.Cooldown=1e6f;
        u.Damage.AttackResilience=1e7f;e.FindComponent<ComponentSpawn>().SpawnDuration=0;GameManager.Project.AddEntity(e);return u;
    }
    static void Clear(int x0,int x1,int z0,int z1) {
        var t=GameManager.Project.FindSubsystem<SubsystemTerrain>();
        for(int x=x0;x<x1;x++)for(int z=z0;z<z1;z++)for(int y=60;y<90;y++)t.ChangeCell(x,y,z,y<70?(y==69?2:1):0);
    }
    static Vector3 Slot(int i)=>origin+new Vector3(i%5*11,0,i/5*14);
    static Vector3 Home=>origin+new Vector3(22,0,21);
    static void Remove(IEnumerable<ComponentMDUnit> units){foreach(var u in units.ToList())if(u.Entity.IsAddedToProject)GameManager.Project.RemoveEntity(u.Entity,true);}

    static IEnumerable<bool> Run() {
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
            if(MDTowerLibrary.Icon(d)==null)throw new Exception("icon missing "+d.id);
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
                fxlog[d.id]=new{fired=u.ShotCount-shots,impacts=w.Impacts-impacts,targetDamaged=target!=null&&target.Damage.Hitpoints<1,particles=w.Particles,
                    healed=a.type=="heal"?player.ComponentPlayer.ComponentHealth.Health:0f};
                Remove(target==null?new[]{u}:new[]{u,target});player.ComponentPlayer.ComponentBody.Position=Home;
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
                shotsLog[id]=new{fired=u.ShotCount-shots,impacts=w.Impacts-impacts,targetHit=target.Damage.Hitpoints<1,particles=w.Particles,
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
            var dummies=new List<ComponentMDUnit>();
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
