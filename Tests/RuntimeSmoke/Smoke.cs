using Engine;
using Game;
using GameEntitySystem;
using System.Text.Json;
using HarmonyLib;

namespace MechDefenseVerification;

// Installed only into local/runtime by tools/run_smoke.ps1, never shipped in the mod.
public class Smoke : ModLoader {
    static int stage;static double began,step;static WorldInfo world;static PlayerData data;
    static ComponentMDUnit mech,turret,enemy;static float enemyHp;static Vector3 original,mechStart,enemyStart;static readonly List<string> checks=[];
    public static bool Drive;public static bool Shooting;public static int Shots,EnemyShots,FireEvents;
    public override void __ModInitialize(){new Harmony("local.mechdefense.smoke").PatchAll(typeof(Smoke).Assembly);SettingsManager.FileAssociationEnabled=false;}
    static string Output=>Path.Combine(AppContext.BaseDirectory,"smoke-result.json");
    public override void OnLoadingFinished(List<Action> actions){actions.Add(()=>{began=Time.RealTime;Window.Frame+=Frame;});}
    static void Check(bool ok,string description){if(!ok)throw new Exception(description);checks.Add(description);File.WriteAllText(Path.Combine(AppContext.BaseDirectory,"smoke-progress.json"),JsonSerializer.Serialize(new{stage,checks}));}
    static ComponentMDUnit Spawn(string kind,Vector3 pos) {var e=DatabaseManager.CreateEntity(GameManager.Project,"MD"+kind,true);var u=e.FindComponent<ComponentMDUnit>(true);u.Body.Position=pos;e.FindComponent<ComponentSpawn>().SpawnDuration=0;GameManager.Project.AddEntity(e);return u;}
    static void Frame() {
        try {
            if(Time.RealTime-began>150)throw new TimeoutException("Stage "+stage);
            if(stage==0) {
                if(ScreensManager.CurrentScreen is not MainMenuScreen)return;
                SettingsManager.VisibilityRange=64;SettingsManager.SoundsVolume=0;SettingsManager.MusicVolume=0;
                world=WorldsManager.CreateWorld(new WorldSettings{Name="MechDefense_Automated",Seed="42",GameMode=GameMode.Creative,TerrainGenerationMode=TerrainGenerationMode.FlatContinent,TerrainLevel=64,TerrainBlockIndex=8,AreWeatherEffectsEnabled=false,TimeOfDayMode=TimeOfDayMode.Day});
                var widget=ScreensManager.FindScreen<GameScreen>("Game").Children.Find<ContainerWidget>("GamesWidget");GameManager.LoadProject(world,widget);
                data=new PlayerData(GameManager.Project){Name="Test pilot",SpawnPosition=new(100,65,100),InputDevice=WidgetInputDevice.None};GameManager.Project.FindSubsystem<SubsystemPlayers>(true).AddPlayerData(data);
                ScreensManager.SwitchScreen("Game");stage=1;return;
            }
            if(stage==1) {
                if(!data.IsReadyForPlaying)return;
                var p=data.ComponentPlayer;original=new(Terrain.ToCell(p.ComponentBody.Position.X)+.5f,70.05f,Terrain.ToCell(p.ComponentBody.Position.Z)+.5f);
                var platform=GameManager.Project.FindSubsystem<SubsystemTerrain>(true);
                for(int x=(int)original.X-24;x<=(int)original.X+24;x++)for(int z=(int)original.Z-16;z<=(int)original.Z+34;z++)for(int y=68;y<80;y++)platform.ChangeCell(x,y,z,y<70?1:0);
                p.ComponentBody.Position=original;
                turret=Spawn("Turret",original+new Vector3(0,0,6));mech=Spawn("Mech",original+new Vector3(6,0,6));enemy=Spawn("Enemy",original+new Vector3(0,0,22));enemyStart=enemy.Body.Position;
                enemy.Damage.AttackResilience=3000;enemyHp=enemy.Damage.Hitpoints;
                foreach(var kind in new[]{"Mech","Enemy"})Check(MDAssets.Load(kind).indices.Length>100,"loaded mesh and texture "+kind);
                Check(MDTowerLibrary.Get("battle_turret_1")?.muzzles.Length==2,"gun tower carries two fire sockets");
                Check(MDCombat.Ammo(0).animation!=""&&MDCombat.Ammo(1).animation!=MDCombat.Ammo(0).animation&&MDCombat.Ammo(4).animation!="","five playable ammo profiles");
                Check(MDCombat.ShotDuration("Mech",0)!=MDCombat.ShotDuration("Mech",4),"ammo effects have distinct timing");
                Check(BlocksManager.Blocks[BlocksManager.GetBlockIndex<MDDeploymentBlock>()].GetCreativeValues().Count()==3,"three creative deployment entries");
                p.ComponentBody.Position=mech.Body.Position+new Vector3(2,0,0);
                Check(MDPlayer.CanBoard(p,mech),"board within reach");p.ComponentRider.StartMounting(mech.Mount);
                Check(mech.Driver==p&&p.ComponentBody.ParentBody==mech.Body,"player mounted an independent entity");
                mechStart=mech.Body.Position;Drive=true;Shooting=true;
                step=Time.RealTime;stage=2;return;
            }
            if(stage==2) {
                if(Time.RealTime-step<3)return;
                Drive=Shooting=false;Check(Vector3.Distance(mechStart,mech.Body.Position)>2,"actual mounted mech driving moves the body");Check(Shots>0&&FireEvents>0,"mounted cannon fires with event animation");
                Check(Vector3.Distance(enemyStart,enemy.Body.Position)>.5f,"enemy advances toward a hostile target");Check(EnemyShots>0,"enemy fires at a hostile target");
                Check(enemy.Damage.Hitpoints<enemyHp,"automatic turret attacks enemy in actual world");
                Check(MDPlayer.Exit(data.ComponentPlayer,mech),"safe dismount succeeds");Check(mech.Entity.IsAddedToProject&&data.ComponentPlayer.ComponentBody.ParentBody==null,"mech remains after dismount");
                Check(!MDCombat.IsEnemy(turret,mech.Body)&&!MDCombat.IsEnemy(turret,data.ComponentPlayer.ComponentBody),"turret excludes friendly mech and player");
                enemy.Body.Position=turret.Body.Position+new Vector3(0,0,15);enemy.Damage.Hitpoints=1;enemy.Body.Velocity=Vector3.Zero;
                mech.Body.Position=turret.Body.Position+new Vector3(0,0,7);
                Check(!MDCombat.ClearShot(turret,enemy.Body),"friendly mech blocks line of fire");
                mech.Body.Position=original+new Vector3(6,0,6);
                var terrain=GameManager.Project.FindSubsystem<SubsystemTerrain>(true);
                int wx=Terrain.ToCell(turret.Body.Position.X),wz=Terrain.ToCell(turret.Body.Position.Z+7),wy=Terrain.ToCell(turret.Body.Position.Y);
                for(int x=wx-1;x<=wx+1;x++)for(int y=wy;y<wy+5;y++)terrain.ChangeCell(x,y,wz,1);
                Check(!MDCombat.ClearShot(turret,enemy.Body),"solid wall blocks line of fire");
                for(int x=wx-1;x<=wx+1;x++)for(int y=wy;y<wy+5;y++)terrain.ChangeCell(x,y,wz,0);
                mech.Damage.Hitpoints=.73f;mech.AimYaw=.43f;mech.AmmoType=2;GameManager.SaveProject(true,false);
                var widget=ScreensManager.FindScreen<GameScreen>("Game").Children.Find<ContainerWidget>("GamesWidget");GameManager.LoadProject(world,widget);
                var all=GameManager.Project.Entities.Select(e=>e.FindComponent<ComponentMDUnit>()).Where(u=>u!=null).ToArray();
                Check(all.Length==3,"three units survive world save and reload");mech=all.Single(u=>u.Kind=="Mech");enemy=all.Single(u=>u.Kind=="Enemy");turret=all.Single(u=>u.Kind=="Turret");Check(Math.Abs(mech.Damage.Hitpoints-.73f)<.001&&Math.Abs(mech.AimYaw-.43f)<.001&&mech.AmmoType==2,"health aim and ammo survive reload");
                data=GameManager.Project.FindSubsystem<SubsystemPlayers>(true).PlayersData[0];step=Time.RealTime;stage=3;return;
            }
            if(stage==3&&data.IsReadyForPlaying&&Time.RealTime-step>3) {
                Check(GameManager.Project!=null,"world runs after reload");
                var p=data.ComponentPlayer;
                var units=GameManager.Project.Entities.Select(e=>e.FindComponent<ComponentMDUnit>()).Where(u=>u!=null).ToArray();
                foreach(var u in units){u.Damage.Hitpoints=1;u.Cooldown=100;u.Body.Position=original+new Vector3(u.Kind=="Mech"?-4:u.Kind=="Enemy"?0:4,0,6);u.Body.Rotation=Quaternion.Identity;u.Yaw=u.AimYaw=u.AimPitch=0;}
                var camera=p.GameWidget.FindCamera<FppCamera>();p.GameWidget.ActiveCamera=camera;
                Vector3 cam=original+new Vector3(10,6,-7),focus=original+new Vector3(0,2.2f,6);
                camera.SetupPerspectiveCamera(cam,Vector3.Normalize(focus-cam),Vector3.UnitY);
                SettingsManager.ShowLogoInScreenshots=false;ScreenCaptureManager.Capture(1280,720,"mech-defense-smoke.png");
                enemy.Damage.Hitpoints=1;enemy.Damage.Damage(2);step=Time.RealTime;stage=4;return;
            }
            if(stage==4&&Time.RealTime-step>8.5) {
                Check(!enemy.Entity.IsAddedToProject,"enemy is removed after lethal damage");
                File.WriteAllText(Output,JsonSerializer.Serialize(new{passed=true,actualGameLaunched=true,api=ModsManager.APIVersionString,checks,world=world.DirectoryName},new JsonSerializerOptions{WriteIndented=true}));
                Window.Frame-=Frame;GameManager.SaveProject(true,false);Window.Close();
            }
        }catch(Exception e){File.WriteAllText(Output,JsonSerializer.Serialize(new{passed=false,stage,checks,error=e.ToString()},new JsonSerializerOptions{WriteIndented=true}));Window.Frame-=Frame;Window.Close();}
    }
}

[HarmonyPatch(typeof(MDPlayer),nameof(MDPlayer.Input))]
public static class SimulatedDriver {
    public static void Postfix(ComponentPlayer p){if(MDPlayer.Mounted(p) is {} u){u.Throttle=Smoke.Drive?.7f:0;u.Trigger=Smoke.Shooting;}}
}
[HarmonyPatch(typeof(ComponentMDUnit),"Fire")]
public static class ShotCounter {public static void Postfix(ComponentMDUnit __instance){if(__instance.Cooldown<=0)return;if(__instance.Kind=="Mech"){Smoke.Shots++;if(__instance.Animation.StartsWith("Fire"))Smoke.FireEvents++;}if(__instance.Kind=="Enemy")Smoke.EnemyShots++;}}
[HarmonyPatch(typeof(FileAssociationManager),nameof(FileAssociationManager.Initialize))]
public static class NoTestFileAssociation {public static bool Prefix()=>false;}
