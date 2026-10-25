using Engine;
using Engine.Graphics;
using Game;
using HarmonyLib;

namespace TowerLab;

// lab --mode phone (0.12.0, user 2026-10-25 on an Honor WIN phone: "机甲会隐身但是能正常玩 炮台在特定视角也会隐身"). The lab
// reports the GLES 3 minimum of 256 vertex uniform vectors, as Snapdragon (Adreno) GPUs do, before the world loads, so the
// engine draws at most (256 - 16) / 8 = 30 bones at once. Three mechs (38-40 bones), the devil mech (33) and the gallery's
// towers stand in view. Every view is drawn twice: with MDDrawBatches off (the 0.11.0 behaviour: the mechs throw "Invalid
// instances count." and the models after them in the frame's list are skipped) and on (no error, nothing skipped, the mechs
// drawn in batches). A view without a mech is the control; a first-person view from Mercury's seat checks its shorter mesh
// list. Screenshots ph_off_* / ph_on_*; results under "phone".
public partial class Lab {
    public const int PhoneUniformVectors=256;
    static IEnumerable<bool> Phone() {
        var r=new Dictionary<string,object>();results["phone"]=r;var proj=GameManager.Project;var pl=player.ComponentPlayer;
        var renderer=proj.FindSubsystem<SubsystemModelsRenderer>(true);
        r["emulated"]=$"GL vertex uniform vectors {GLWrapper.GL_MAX_VERTEX_UNIFORM_VECTORS}, bones per draw {renderer.MaxInstancesCount}";
        if(renderer.MaxInstancesCount!=30)throw new Exception("phone emulation did not apply: "+r["emulated"]);
        foreach(var (_,u,_) in placed)u.Cooldown=1e6f;   // the towers stay quiet
        // the entity the deploy item makes (MDDeployPatch), placed directly: the item's own checks (free space, no creature in the
        // way) are covered by the mech and robots modes, and a wandering animal must not stop this test
        ComponentMDUnit Deploy(Vector3 at,MDMechType type) {
            var e=DatabaseManager.CreateEntity(proj,type.Template,true);var body=e.FindComponent<ComponentBody>(true);body.Position=at;body.Rotation=Quaternion.Identity;
            var u=e.FindComponent<ComponentMDUnit>(true);u.Yaw=u.AimYaw=0;e.FindComponent<ComponentSpawn>(true).SpawnDuration=0;proj.AddEntity(e);return u;
        }
        // between the gallery's columns (x 0, 11, 22, 33, 44 from the origin) in front of its first row
        var mechs=new List<ComponentMDUnit>();float[] xs=[5.5f,16.5f,27.5f];
        for(int i=0;i<MDMechType.All.Length;i++){mechs.Add(Deploy(origin+new Vector3(xs[i],.06f,7),MDMechType.All[i]));while(Wait(.4))yield return true;}
        var boss=MDBoss.Spawn(proj,origin+new Vector3(38.5f,0,7),origin+new Vector3(38.5f,4,7));boss.ForceClip="idle";
        pl.ComponentBody.Position=origin+new Vector3(60,0,-30);pl.ComponentBody.Velocity=Vector3.Zero;while(Wait(2))yield return true;   // out of every view
        r["models"]=string.Join("; ",mechs.Select(m=>$"{m.Mech.Type.Id} {m.Entity.FindComponent<ComponentMDMechModel>(true).Model.Bones.Count} bones").Append($"devil mech {boss.Entity.FindComponent<ComponentModel>(true).Model.Bones.Count} bones"));
        foreach(var m in mechs){m.Body.Velocity=Vector3.Zero;}
        var views=new (string name,Vector3 eye,Vector3 look,bool mechInView)[]{
            ("front",origin+new Vector3(22,10,-22),origin+new Vector3(22,2,20),true),
            ("low",origin+new Vector3(-2,4,-8),origin+new Vector3(30,3,12),true),
            ("high",origin+new Vector3(22,45,-12),origin+new Vector3(22,0,40),true),
            ("towers_only",origin+new Vector3(22,10,150),origin+new Vector3(22,2,90),false)};
        // per view: the terrain settles around the camera first (it is generated around the active camera), then the same frames
        // without and with batches, so each pair of shots differs only in MDDrawBatches
        var rows=new List<object>();r["views"]=rows;var bad=new List<string>();int offErrors=0,offTowersSkipped=0;
        foreach(var v in views) {
            for(double s0=Time.RealTime;Time.RealTime-s0<2.5;){Cam(v.eye,v.look);yield return true;}
            foreach(bool on in new[]{false,true}) {
                MDDrawBatches.Enabled=on;string phase=on?"on":"off";
                for(int k=0;k<6;k++){Cam(v.eye,v.look);yield return true;}
                PhoneProbe.Reset();int batched0=MDDrawBatches.Batched;PhoneProbe.On=true;
                for(int k=0;k<20;k++){Cam(v.eye,v.look);yield return true;}
                PhoneProbe.On=false;Shot($"ph_{phase}_{v.name}");yield return true;
                var row=PhoneProbe.Row(phase,v.name);row["batchedDraws"]=MDDrawBatches.Batched-batched0;rows.Add(row);
                int errors=(int)row["errors"],skippedTowers=PhoneProbe.Skipped.Where(kv=>kv.Key.StartsWith("MDTower")).Sum(kv=>kv.Value);
                if(!on&&v.mechInView){offErrors+=errors;offTowersSkipped+=skippedTowers;}
                if(on&&(errors>0||PhoneProbe.Skipped.Count>0||(v.mechInView&&MDDrawBatches.Batched==batched0)))bad.Add($"with batches, view {v.name}: {errors} errors, skipped {string.Join(",",PhoneProbe.Skipped.Keys)}, batched {MDDrawBatches.Batched-batched0}");
                if(!v.mechInView&&errors>0)bad.Add($"{phase}, view without a mech: {errors} errors");
            }
        }
        r["offSummary"]=$"without batches: {offErrors} draw errors and {offTowersSkipped} tower draws skipped over the three views with mechs";
        if(offErrors==0)bad.Add("the failure did not show without batches (the emulation does not reproduce the phone)");
        // first person in Mercury's seat: the mech draws its cockpit meshes only (another mesh list)
        var merc=mechs[0];pl.ComponentBody.Position=merc.Body.Position+new Vector3(0,0,3);while(Wait(.3))yield return true;
        if(!MDMechPilot.Board(pl,merc))throw new Exception("phone: could not board Mercury");
        var gw=pl.GameWidget;for(double s0=Time.RealTime;Time.RealTime-s0<2.5;){gw.ActiveCamera=gw.FindCamera<FppCamera>();yield return true;}
        foreach(bool on in new[]{false,true}) {
            MDDrawBatches.Enabled=on;
            for(int k=0;k<10;k++){gw.ActiveCamera=gw.FindCamera<FppCamera>();yield return true;}
            PhoneProbe.Reset();int batched0=MDDrawBatches.Batched;PhoneProbe.On=true;
            for(int k=0;k<20;k++){gw.ActiveCamera=gw.FindCamera<FppCamera>();yield return true;}
            PhoneProbe.On=false;ScreenCaptureManager.Capture(1280,720,$"ph_{(on?"on":"off")}_first_person.png");yield return true;
            var row=PhoneProbe.Row(on?"on":"off","first_person");row["batchedDraws"]=MDDrawBatches.Batched-batched0;row["firstPerson"]=MDMechPilot.FirstPerson(merc);rows.Add(row);
            if(on&&((int)row["errors"]>0||PhoneProbe.Skipped.Count>0))bad.Add($"first person with batches: {row["errors"]} errors, skipped {string.Join(",",PhoneProbe.Skipped.Keys)}");
        }
        MDMechPilot.Exit(pl,merc,true);MDDrawBatches.Enabled=true;
        if(bad.Count>0)throw new Exception("phone: "+string.Join("; ",bad));
        foreach(var m in mechs)if(m.Entity.IsAddedToProject)proj.RemoveEntity(m.Entity,true);
        if(boss.Entity.IsAddedToProject)proj.RemoveEntity(boss.Entity,true);
    }
    // the lab's own OnModelDataDrawing (registered in mode phone only): which models the renderer reached, in order
    public override void OnModelDataDrawing(SubsystemModelsRenderer.ModelData modelData,ModelShader modelShader,Camera camera,SubsystemModelsRenderer subsystemModelsRenderer,out bool skip) {
        skip=false;if(PhoneProbe.On)PhoneProbe.Reached.Add(modelData.ComponentModel);
    }
}

// Per DrawInstancedModels call: the models handed in, the ones that reached the drawing hook, the exception that ended it.
[HarmonyPatch(typeof(SubsystemModelsRenderer),nameof(SubsystemModelsRenderer.DrawInstancedModels))]
public static class PhoneProbe {
    public static bool On; public static int Calls,Queued,ReachedCount,Errors;
    public static readonly List<ComponentModel> Reached=[];
    public static readonly Dictionary<string,int> Skipped=[],Threw=[];public static readonly HashSet<string> Messages=[];
    public static void Reset(){Calls=Queued=ReachedCount=Errors=0;Reached.Clear();Skipped.Clear();Threw.Clear();Messages.Clear();}
    static string Name(ComponentModel m){var n=m.Entity.ValuesDictionary.DatabaseObject.Name;return n.StartsWith("MDTower")?"MDTower":n;}
    public static void Prefix(){if(On){Calls++;Reached.Clear();}}
    public static Exception Finalizer(Exception __exception,List<SubsystemModelsRenderer.ModelData> modelsData) {
        if(!On)return __exception;
        Queued+=modelsData.Count;var seen=Reached.ToHashSet();
        foreach(var m in modelsData){if(seen.Contains(m.ComponentModel))ReachedCount++;else{var n=Name(m.ComponentModel);Skipped[n]=Skipped.GetValueOrDefault(n)+1;}}
        if(__exception!=null) {
            Errors++;Messages.Add(__exception.GetType().Name+": "+__exception.Message);
            if(Reached.Count>0){var n=Name(Reached[^1]);Threw[n]=Threw.GetValueOrDefault(n)+1;}
        }
        return __exception;
    }
    public static Dictionary<string,object> Row(string phase,string view)=>new(){["phase"]=phase,["view"]=view,["draws"]=Calls,["models"]=Queued,["reached"]=ReachedCount,["errors"]=Errors,
        ["threw"]=string.Join(",",Threw.Select(kv=>$"{kv.Key}x{kv.Value}")),["skipped"]=string.Join(",",Skipped.Select(kv=>$"{kv.Key}x{kv.Value}")),["messages"]=string.Join(" | ",Messages)};
}
