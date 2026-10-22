using Engine;
using Game;
using GameEntitySystem;
using System.Text.Json;
using HarmonyLib;

namespace MechDefenseVerification;

// Old-save migration test (0.6.0 removed the Destroy Gunners sample enemy; its sample mech came back as Mercury in 0.8.0).
// Installed only into an isolated runtime by
// tools/run_isolated.py smoke, never shipped. Mode in smoke-mode.txt next to the exe:
//   "old": run with an older package (0.5.0). Builds the world "MDMigration": a gun tower, a mech with the player riding
//          it, an enemy, and mech / enemy / frost tower deployment items in the inventory; saves and quits.
//   "new": run with the current package in the same runtime. Loads that world and checks that the enemy is gone and the mech
//          is Mercury with the player still seated, who gets out unhurt; the old enemy item reads as removed, the old mech
//          item as Mercury and deploys one, a tower still deploys and the old tower still shoots a wolf; saves, reloads and
//          checks again.
// Code that touches members only the current mod has (Legacy, RemovedNote) stays out of the "old" path's methods.
public class Smoke : ModLoader {
    const string WorldName="MDMigration";
    static int stage;static double began,step;static WorldInfo world;static PlayerData data;static string mode="new";
    static Vector3 origin;static ComponentCreature wolf;static float wolfMin=1;static int towerShots;
    static readonly List<string> checks=[];
    public override void __ModInitialize(){new Harmony("local.smoke").PatchAll(typeof(Smoke).Assembly);SettingsManager.FileAssociationEnabled=false;var m=Path.Combine(AppContext.BaseDirectory,"smoke-mode.txt");if(File.Exists(m))mode=File.ReadAllText(m).Trim();}
    static string Output=>Path.Combine(AppContext.BaseDirectory,"smoke-result.json");
    public override void OnLoadingFinished(List<Action> actions){actions.Add(()=>{began=Time.RealTime;Window.Frame+=Frame;});}
    static void Check(bool ok,string description){if(!ok)throw new Exception("FAILED: "+description);checks.Add(description);}
    static ContainerWidget Games=>ScreensManager.FindScreen<GameScreen>("Game").Children.Find<ContainerWidget>("GamesWidget");
    static int Deploy(int data)=>Terrain.MakeBlockValue(BlocksManager.GetBlockIndex<MDDeploymentBlock>(),0,data);
    static ComponentMDUnit[] Units()=>GameManager.Project.Entities.Select(e=>e.FindComponent<ComponentMDUnit>()).Where(u=>u!=null).ToArray();
    static void Finish(object result){File.WriteAllText(Output,JsonSerializer.Serialize(result,new JsonSerializerOptions{WriteIndented=true}));Window.Frame-=Frame;Window.Close();}
    static void Frame() {
        try {
            if(Time.RealTime-began>240)throw new TimeoutException("stage "+stage);
            if(mode=="old")Old();else New();
        }catch(Exception e){Finish(new{passed=false,mode,stage,checks,error=e.ToString()});}
    }

    static void Old() {
        if(stage==0) {
            if(ScreensManager.CurrentScreen is not MainMenuScreen)return;
            SettingsManager.VisibilityRange=64;SettingsManager.SoundsVolume=0;SettingsManager.MusicVolume=0;
            world=WorldsManager.CreateWorld(new WorldSettings{Name=WorldName,Seed="42",GameMode=GameMode.Creative,TerrainGenerationMode=TerrainGenerationMode.FlatContinent,TerrainLevel=64,TerrainBlockIndex=8,AreWeatherEffectsEnabled=false,TimeOfDayMode=TimeOfDayMode.Day});
            GameManager.LoadProject(world,Games);
            data=new PlayerData(GameManager.Project){Name="Migration",SpawnPosition=new(100,65,100),InputDevice=WidgetInputDevice.None};GameManager.Project.FindSubsystem<SubsystemPlayers>(true).AddPlayerData(data);
            ScreensManager.SwitchScreen("Game");stage=1;return;
        }
        if(stage==1) {
            if(!data.IsReadyForPlaying)return;
            var p=data.ComponentPlayer;origin=new(Terrain.ToCell(p.ComponentBody.Position.X)+.5f,70.05f,Terrain.ToCell(p.ComponentBody.Position.Z)+.5f);
            var t=GameManager.Project.FindSubsystem<SubsystemTerrain>(true);
            for(int x=(int)origin.X-30;x<=(int)origin.X+30;x++)for(int z=(int)origin.Z-30;z<=(int)origin.Z+60;z++)for(int y=68;y<80;y++)t.ChangeCell(x,y,z,y<70?1:0);
            Spawn("MDTurret",origin+new Vector3(-12,0,0));                  // gun tower (data 0)
            var mech=Spawn("MDMech",origin+new Vector3(6,0,6));Spawn("MDEnemy",origin+new Vector3(0,0,55));   // the enemy far from tower and player
            p.ComponentBody.Position=mech.Body.Position+new Vector3(2,0,0);p.ComponentRider.StartMounting(mech.Mount);
            var inv=p.ComponentMiner.Inventory;foreach(var (slot,d) in new[]{(0,1),(1,2),(2,11)}){inv.RemoveSlotItems(slot,inv.GetSlotCount(slot));inv.AddSlotItems(slot,Deploy(d),1);}
            step=Time.RealTime;stage=2;return;
        }
        if(stage==2&&Time.RealTime-step>3) {
            var p=data.ComponentPlayer;var units=Units();
            Check(units.Count(u=>u.Kind=="Mech")==1&&units.Count(u=>u.Kind=="Enemy")==1&&units.Count(u=>u.Kind=="Turret")==1,"old world: one mech, one enemy, one tower");
            Check(p.ComponentBody.ParentBody!=null&&p.ComponentBody.ParentBody==units.Single(u=>u.Kind=="Mech").Body,"old world: player rides the mech");
            Check(p.ComponentMiner.Inventory.GetSlotValue(0)==Deploy(1)&&p.ComponentMiner.Inventory.GetSlotValue(1)==Deploy(2),"old world: mech and enemy deployment items in the inventory");
            GameManager.SaveProject(true,false);
            Finish(new{passed=true,mode,checks,world=world.DirectoryName,riderAt=p.ComponentBody.Position.ToString()});
        }
    }
    static ComponentMDUnit Spawn(string template,Vector3 pos) {
        var e=DatabaseManager.CreateEntity(GameManager.Project,template,true);var u=e.FindComponent<ComponentMDUnit>(true);u.Body.Position=pos;
        e.FindComponent<ComponentSpawn>().SpawnDuration=0;GameManager.Project.AddEntity(e);return u;
    }

    static void New() {
        if(stage==0) {
            if(ScreensManager.CurrentScreen is not MainMenuScreen)return;
            SettingsManager.VisibilityRange=64;SettingsManager.SoundsVolume=0;SettingsManager.MusicVolume=0;
            WorldsManager.UpdateWorldsList();world=WorldsManager.WorldInfos.Where(w=>w.WorldSettings.Name==WorldName).OrderByDescending(w=>w.LastSaveTime).FirstOrDefault()??throw new Exception("no world "+WorldName+": run the old mode first");
            GameManager.LoadProject(world,Games);data=GameManager.Project.FindSubsystem<SubsystemPlayers>(true).PlayersData[0];
            ScreensManager.SwitchScreen("Game");step=Time.RealTime;stage=1;return;
        }
        if(stage==1&&data.IsReadyForPlaying&&Time.RealTime-step>3){AfterLoad("first load");stage=2;step=Time.RealTime;return;}
        if(stage==2) {
            if(wolf!=null&&wolf.Entity.IsAddedToProject)wolfMin=Math.Min(wolfMin,wolf.ComponentHealth.Health);
            if(Time.RealTime-step<6)return;
            var gun=Units().First(u=>u.TowerId=="battle_turret_1");
            Check(gun.ShotCount>towerShots,"old gun tower fires at a wolf");Check(wolfMin<1,"the wolf takes damage");
            GameManager.SaveProject(true,false);GameManager.LoadProject(world,Games);data=GameManager.Project.FindSubsystem<SubsystemPlayers>(true).PlayersData[0];
            step=Time.RealTime;stage=3;return;
        }
        if(stage==3&&data.IsReadyForPlaying&&Time.RealTime-step>3) {
            var units=Units();
            Check(units.Length>0&&units.All(u=>(u.Kind=="Turret"&&u.Tower!=null)||(u.Kind=="Mech"&&u.Mech!=null)),"after saving and reloading: towers and Mercury only");
            Check(units.Count(u=>u.TowerId=="battle_frost_1")==1,"the tower deployed after migration survives save and reload");
            Check(units.Count(u=>u.Kind=="Mech"&&!u.Destroyed)==2,"both Mercury mechs (the old one and the deployed one) survive save and reload");
            Finish(new{passed=true,mode,checks,world=world.DirectoryName,units=units.Select(u=>u.Kind=="Mech"?"Mercury":u.TowerId).ToArray()});
        }
    }
    static void AfterLoad(string when) {
        var p=data.ComponentPlayer;var units=Units();var block=(MDDeploymentBlock)BlocksManager.Blocks[BlocksManager.GetBlockIndex<MDDeploymentBlock>()];
        Check(units.All(u=>u.Kind is "Turret" or "Mech"),$"{when}: the enemy is gone");
        var oldGun=units.First(u=>u.TowerId=="battle_turret_1");origin=new(oldGun.Body.Position.X+12,70.05f,oldGun.Body.Position.Z);   // the old run put it at origin-(12,0,0)
        var mech=units.SingleOrDefault(u=>u.Kind=="Mech");
        Check(mech?.Mech!=null&&!mech.Destroyed&&mech.Entity.FindComponent<ComponentMDMechModel>()?.Model!=null,$"{when}: the 0.5.0 mech is Mercury now, with its model");
        Check(units.Count(u=>u.TowerId=="battle_turret_1"&&!u.Destroyed)==1,$"{when}: the old gun tower is still there");
        Check(MDMechPilot.Mounted(p)==mech,$"{when}: the player still sits in it");
        Check(MDMechPilot.Exit(p,mech,true)&&p.ComponentBody.ParentBody==null&&p.ComponentRider.Mount==null,$"{when}: and gets out");
        Check(p.ComponentHealth.Health>.99f,$"{when}: the player is unhurt");
        Check(p.GameWidget.ActiveCamera is FppCamera,$"{when}: first-person camera");
        var t=GameManager.Project.FindSubsystem<SubsystemTerrain>(true);
        Check(block.GetDisplayName(t,Deploy(1))=="水星机甲 · 部署"&&block.GetDisplayName(t,Deploy(2))=="已移除的旧单位",$"{when}: the old mech item reads as Mercury, the enemy item as removed");
        var creative=block.GetCreativeValues().ToArray();
        Check(creative.Length==MDTowerLibrary.All.Count+MDMechType.All.Length&&creative.Count(v=>MDDeploymentBlock.Tower(v)!=null)==MDTowerLibrary.All.Count&&MDMechType.All.All(m=>creative.Contains(Deploy(m.Data))),"creative inventory lists the towers and the three mechs");
        var names=creative.Select(v=>block.GetDisplayName(t,v)).Append(block.DefaultDisplayName).Append(block.GetDescription(Deploy(1))).Append(block.GetDescription(Deploy(2))).ToArray();
        Check(names.All(n=>!n.Contains("终末地")&&!n.Contains("毁灭枪手")&&!n.Contains("机兵")&&!n.Contains("原作")),"no Endfield / Destroy Gunners names, no 原作: "+string.Join(" ",names.Take(4)));   // AGENTS.md product rule
        // Use the removed enemy item, then the frost tower item, on the platform in front of the player
        var inv=p.ComponentMiner.Inventory;p.ComponentBody.Position=origin+new Vector3(-10,0,22);p.ComponentBody.Velocity=Vector3.Zero;int before=Units().Length;
        var foot=p.ComponentBody.Position;var ray=new Ray3(foot+new Vector3(0,1.6f,0),Vector3.Normalize(new Vector3(0,-1.6f,-4.5f)));
        inv.ActiveSlotIndex=1;int count1=inv.GetSlotCount(1),value1=inv.GetSlotValue(1);bool used=p.ComponentMiner.Use(ray);
        Check(Units().Length==before,$"the removed enemy item does not deploy (units {before} -> {Units().Length}, Use returned {used})");
        Check(inv.GetSlotCount(1)==count1&&inv.GetSlotValue(1)==value1,$"the removed enemy item is not used up (slot 1: {count1} x {value1} -> {inv.GetSlotCount(1)} x {inv.GetSlotValue(1)}, {inv.GetType().Name})");
        inv.ActiveSlotIndex=2;p.ComponentMiner.Use(ray);
        Check(Units().Count(u=>u.TowerId=="battle_frost_1")==1,"a frost tower still deploys from its item");
        // the old mech item deploys Mercury (on clear ground further along the platform)
        p.ComponentBody.Position=origin+new Vector3(8,0,26);p.ComponentBody.Velocity=Vector3.Zero;foot=p.ComponentBody.Position;ray=new Ray3(foot+new Vector3(0,1.6f,0),Vector3.Normalize(new Vector3(0,-1.6f,-4.5f)));
        inv.ActiveSlotIndex=0;int mechs=Units().Count(u=>u.Kind=="Mech");used=p.ComponentMiner.Use(ray);   // a creative inventory never runs out
        Check(Units().Count(u=>u.Kind=="Mech")==mechs+1,$"the old mech item deploys a second Mercury (mechs {mechs} -> {Units().Count(u=>u.Kind=="Mech")}, Use returned {used})");
        var gun=Units().First(u=>u.TowerId=="battle_turret_1");towerShots=gun.ShotCount;
        var e=DatabaseManager.CreateEntity(GameManager.Project,"Wolf",true);e.FindComponent<ComponentBody>(true).Position=gun.Body.Position+new Vector3(0,0,-8);e.FindComponent<ComponentSpawn>(true).SpawnDuration=0;
        GameManager.Project.AddEntity(e);wolf=e.FindComponent<ComponentCreature>(true);var l=wolf.ComponentLocomotion;l.WalkSpeed=l.JumpSpeed=0;
        Check(MDCombat.IsEnemy(gun,wolf.ComponentBody)&&!MDCombat.IsEnemy(gun,p.ComponentBody),"towers treat the wolf as hostile and the player as friendly");
    }
}

// The test game runs while the user keeps working. With no menu open the engine sets the mouse to raw mode in
// Engine.Input.Mouse.BeforeFrame and again in AfterFrame whenever its window counts as active, which it does even behind other
// windows; raw mode clips the system cursor to the game window (measured: clipped in 7 of 133 samples over a 27 s run). The
// tests never read the mouse, so both places see a visible mouse and keep the cursor normal.
[HarmonyPatch(typeof(Engine.Input.Mouse),"BeforeFrame")]
public static class KeepCursorFree { public static void Prefix()=>Engine.Input.Mouse.IsMouseVisible=true; }
[HarmonyPatch(typeof(Engine.Input.Mouse),"AfterFrame")]
public static class KeepCursorFreeAfter { public static void Prefix()=>Engine.Input.Mouse.IsMouseVisible=true; }
