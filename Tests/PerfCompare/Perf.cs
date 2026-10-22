using Engine;
using Game;
using HarmonyLib;
using System.Diagnostics;
using System.Text.Json;

namespace PerfCompare;

// Same scene for any MechDefense version: 30 gun towers (MDTurret/2/3 templates) in view and the
// three tower items in the hotbar. Frame time is measured as CPU time of one frame (vsync off).
public class Perf:ModLoader {
    static int stage;static double began,mark;static WorldInfo world;static PlayerData player;static readonly Dictionary<string,object> results=[];
    static readonly Stopwatch frame=new();static readonly List<double> samples=[];static string phase="";static double phaseEnd;
    static string Out=>Path.Combine(AppContext.BaseDirectory,"perf-result.json");
    public override void __ModInitialize(){new Harmony("local.perfcompare").PatchAll(typeof(Perf).Assembly);SettingsManager.FileAssociationEnabled=false;}
    public override IEnumerable<KeyValuePair<string,object>> GetKeyboardMappings(){yield return new("NUIConsle",(Engine.Input.Key)(-1));}
    public override void OnLoadingFinished(List<Action> actions){actions.Add(()=>{began=Time.RealTime;Window.Frame+=Frame;});}
    static void Save(bool ok,string err=null){results["passed"]=ok;if(err!=null)results["error"]=err;File.WriteAllText(Out,JsonSerializer.Serialize(results,new JsonSerializerOptions{WriteIndented=true}));}
    static void Frame() {
        try {
            if(frame.IsRunning&&phase!=""){samples.Add(frame.Elapsed.TotalMilliseconds);}
            frame.Restart();
            if(Time.RealTime-began>400)throw new TimeoutException("stage "+stage);
            if(stage==0) {
                if(ScreensManager.CurrentScreen is not MainMenuScreen)return;
                SettingsManager.VisibilityRange=96;SettingsManager.SoundsVolume=0;SettingsManager.MusicVolume=0;
                try{typeof(Window).GetProperty("PresentationInterval")?.SetValue(null,0);}catch{}
                world=WorldsManager.CreateWorld(new WorldSettings{Name="Perf_"+DateTime.Now.ToString("HHmmss"),Seed="7",GameMode=GameMode.Creative,TerrainGenerationMode=TerrainGenerationMode.FlatContinent,TerrainLevel=64,TerrainBlockIndex=8,AreWeatherEffectsEnabled=false,TimeOfDayMode=TimeOfDayMode.Day});
                GameManager.LoadProject(world,ScreensManager.FindScreen<GameScreen>("Game").Children.Find<ContainerWidget>("GamesWidget"));
                player=new PlayerData(GameManager.Project){Name="Perf",SpawnPosition=new(100,65,100),InputDevice=WidgetInputDevice.None};GameManager.Project.FindSubsystem<SubsystemPlayers>().AddPlayerData(player);ScreensManager.SwitchScreen("Game");stage=1;return;
            }
            if(stage==1) {
                if(!player.IsReadyForPlaying)return;
                var pos=player.ComponentPlayer.ComponentBody.Position;var o=new Vector3(MathF.Floor(pos.X)+.5f,70,MathF.Floor(pos.Z)+.5f);
                var t=GameManager.Project.FindSubsystem<SubsystemTerrain>();
                for(int x=-20;x<40;x++)for(int z=-10;z<40;z++)for(int y=60;y<80;y++)t.ChangeCell((int)o.X+x,y,(int)o.Z+z,y<70?(y==69?2:1):0);
                player.ComponentPlayer.ComponentBody.Position=o+new Vector3(10,0,-8);
                var cam=player.ComponentPlayer.GameWidget.FindCamera<FixedCamera>();player.ComponentPlayer.GameWidget.ActiveCamera=cam;
                cam.SetupPerspectiveCamera(o+new Vector3(10,9,-14),Vector3.Normalize(new Vector3(0,-7,26)),Vector3.UnitY);
                // hotbar: the three tower deploy items (data 0, 3, 4) in every version
                var block=BlocksManager.Blocks.First(b=>b!=null&&b.GetType().Name=="MDDeploymentBlock");
                var inv=player.ComponentPlayer.Entity.FindComponent<ComponentCreativeInventory>();
                int[] data=[0,3,4,0,3,4,0,3,4];
                for(int i=0;i<9;i++){inv.RemoveSlotItems(i,inv.GetSlotCount(i));inv.AddSlotItems(i,Terrain.MakeBlockValue(block.BlockIndex,0,data[i]),1);}
                results["hotbar"]=$"{block.GetType().FullName} x9";
                mark=Time.RealTime;stage=2;return;
            }
            if(stage==2) {
                if(Time.RealTime-mark<2)return;
                phase="hotbar_only";samples.Clear();phaseEnd=Time.RealTime+5;stage=3;return;
            }
            if(stage==3) {
                if(Time.RealTime<phaseEnd)return;
                Record();
                var pos=player.ComponentPlayer.ComponentBody.Position;var o=new Vector3(MathF.Floor(pos.X)-9.5f,70,MathF.Floor(pos.Z)+8.5f);
                string[] names=["MDTurret","MDTurret2","MDTurret3"];
                for(int i=0;i<30;i++) {
                    var e=DatabaseManager.CreateEntity(GameManager.Project,names[i%3],true);var body=e.FindComponent<ComponentBody>(true);
                    var at=o+new Vector3(i%10*3.2f,0,i/10*4);
                    var fix=body.GetType().GetMethod("FixAt");if(fix!=null)fix.Invoke(body,[at,Quaternion.CreateFromAxisAngle(Vector3.UnitY,MathF.PI)]);else body.Position=at;
                    e.FindComponent<ComponentSpawn>(true).SpawnDuration=0;GameManager.Project.AddEntity(e);
                }
                mark=Time.RealTime;stage=4;return;
            }
            if(stage==4) {
                if(Time.RealTime-mark<3)return;
                phase="towers30_hotbar";samples.Clear();phaseEnd=Time.RealTime+6;stage=5;return;
            }
            if(stage==5) {
                if(Time.RealTime<phaseEnd)return;
                Record();ScreenCaptureManager.Capture(1280,720,"perf-scene.png");Save(true);Window.Frame-=Frame;Window.Close();
            }
        } catch(Exception e){Save(false,e.ToString());Window.Frame-=Frame;Window.Close();}
    }
    static void Record() {
        var s=samples.OrderBy(x=>x).ToList();
        results[phase]=new{frames=s.Count,avgMs=s.Average(),medianMs=s[s.Count/2],p95Ms=s[(int)(s.Count*.95)],fps=1000/s.Average()};
        phase="";
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
