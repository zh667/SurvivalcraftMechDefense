using System.Reflection;
using System.Xml.Linq;
using Engine;
using Game;

namespace MechDefenseVerification;

// 1.0.0 package rename (user, 2026-11-06: local.mechdefense.sample -> zh667.MechDefense). tools/run_isolated.py smoke, same --label for both steps:
//   "pkg-old"  with the 0.18.4 package: a world "MDRename" with four towers and a live defence core, threat 7, 13 robots towards the next boss,
//              a tower item and robot scrap in the inventory; ten mod settings changed from their defaults and saved (ModSettings.xml, under
//              the old package name); the world saved.
//   "pkg-new"  with the 1.0.0 package: the settings read back (MDSettings.Migrate) and written under the new name; the world opened the way a
//              player opens it (PlayScreen.Play: the game first compares the mods the world was played with), no "模组缺失" dialog; towers,
//              core, threat, boss count and items still there; saved again, the world lists the new package name.
//   "pkg-new-plain" the same with MDSettings.MigrateEnabled off (what a rename without migration does): expected to fail, and records how.
// The pkg-old path uses only members 0.18.4 has (the newer ones through reflection in the other paths).
public static class Rename {
    const string WorldName="MDRename";
    static readonly string[] Towers=["battle_turret_1","gatling_1","missile_2","aa_3"];
    static int stage;static double step;static WorldInfo world;static PlayerData data;
    static readonly List<string> checks=[],problems=[];static readonly Dictionary<string,object> info=[];
    static bool plain;
    public static void NoMigration(){plain=true;typeof(MDSettings).GetField("MigrateEnabled",BindingFlags.Public|BindingFlags.Static)?.SetValue(null,false);}
    static void Check(bool ok,string what){if(ok)checks.Add(what);else problems.Add(what);}
    static ContainerWidget Games=>ScreensManager.FindScreen<GameScreen>("Game").Children.Find<ContainerWidget>("GamesWidget");
    static ComponentMDUnit[] Units()=>GameManager.Project.Entities.Select(e=>e.FindComponent<ComponentMDUnit>()).Where(u=>u!=null).ToArray();
    static int Deploy(int d)=>Terrain.MakeBlockValue(BlocksManager.GetBlockIndex<MDDeploymentBlock>(),0,d);
    static int Scrap()=>Terrain.MakeBlockValue(BlocksManager.GetBlockIndex<MDPartBlock>(),0,0);
    // the settings changed in the old world (none of them at its default)
    static Dictionary<string,string> Settings()=>new(){
        ["StartDay"]=MDSettings.StartDay.ToString(),["SpawnDensity"]=MDSettings.SpawnDensity.ToString("0.##"),["FxLevel"]=MDSettings.FxLevel.ToString(),
        ["MechTerrain"]=MDSettings.MechTerrain.ToString(),["BossKills"]=MDSettings.BossKills.ToString(),["Growth"]=MDSettings.Growth.ToString(),
        ["DamageNumbers"]=MDSettings.DamageNumbers.ToString(),["HudShow"]=MDSettings.HudShow.ToString(),["RaidChance"]=MDSettings.RaidChance.ToString("0.##"),
        ["ThreatDifficulty"]=MDSettings.ThreatDifficulty.ToString()};
    static readonly Dictionary<string,string> Changed=new(){["StartDay"]="12",["SpawnDensity"]="2.5",["FxLevel"]="0",["MechTerrain"]="True",["BossKills"]="77",
        ["Growth"]="5",["DamageNumbers"]="False",["HudShow"]="False",["RaidChance"]="40",["ThreatDifficulty"]="2"};
    // the package names that have an element in ModSettings.xml (the file as the game wrote it)
    static List<string> SettingsFile() {
        try{using var s=Storage.OpenFile(ModsManager.ModsSettingsPath,OpenFileMode.Read);
            return XElement.Load(s).Elements("Mod").Where(m=>m.Element(MDSettings.Element)!=null).Select(m=>(string)m.Attribute("PackageName")).ToList();}
        catch(Exception e){return ["(unreadable: "+e.Message+")"];}
    }
    static void Done(bool passed){info["checks"]=checks;info["problems"]=problems;Smoke.Finish(new{passed,mode=plain?"pkg-new-plain":stage<0?"pkg-old":"pkg-new",info});}

    public static void Frame(string mode) {
        if(mode=="pkg-old")Old();else New();
    }

    static void Old() {
        if(stage==0) {
            if(ScreensManager.CurrentScreen is not MainMenuScreen)return;
            SettingsManager.VisibilityRange=64;SettingsManager.SoundsVolume=0;SettingsManager.MusicVolume=0;
            world=WorldsManager.CreateWorld(new WorldSettings{Name=WorldName,Seed="42",GameMode=GameMode.Creative,TerrainGenerationMode=TerrainGenerationMode.FlatContinent,TerrainLevel=64,TerrainBlockIndex=8,AreWeatherEffectsEnabled=false,TimeOfDayMode=TimeOfDayMode.Day});
            GameManager.LoadProject(world,Games);
            data=new PlayerData(GameManager.Project){Name="Rename",SpawnPosition=new(100,65,100),InputDevice=WidgetInputDevice.None};GameManager.Project.FindSubsystem<SubsystemPlayers>(true).AddPlayerData(data);
            ScreensManager.SwitchScreen("Game");stage=1;return;
        }
        if(stage==1) {
            if(!data.IsReadyForPlaying)return;
            var p=data.ComponentPlayer;var o=new Vector3(Terrain.ToCell(p.ComponentBody.Position.X)+.5f,70.05f,Terrain.ToCell(p.ComponentBody.Position.Z)+.5f);
            var t=GameManager.Project.FindSubsystem<SubsystemTerrain>(true);
            for(int x=(int)o.X-30;x<=(int)o.X+30;x++)for(int z=(int)o.Z-30;z<=(int)o.Z+30;z++)for(int y=68;y<80;y++)t.ChangeCell(x,y,z,y<70?1:0);
            for(int i=0;i<Towers.Length;i++)Place(MDTowerLibrary.Get(Towers[i]),o+new Vector3(-15+i*8,0,-10));
            var core=Place(MDTowerLibrary.Get("defense_core"),o+new Vector3(0,0,10));MDThreat.Placed(core,p);
            p.ComponentBody.Position=o+new Vector3(0,0,22);
            var inv=p.ComponentMiner.Inventory;inv.RemoveSlotItems(0,inv.GetSlotCount(0));inv.AddSlotItems(0,Deploy(MDTowerLibrary.Get("gatling_2").data),3);
            inv.RemoveSlotItems(1,inv.GetSlotCount(1));inv.AddSlotItems(1,Scrap(),5);
            MDThreat.Threat=7;MDBossKills.Count=13;
            MDSettings.StartDay=12;MDSettings.SpawnDensity=2.5f;MDSettings.FxLevel=0;MDSettings.MechTerrain=true;MDSettings.BossKills=77;MDSettings.Growth=5;
            MDSettings.DamageNumbers=false;MDSettings.HudShow=false;MDSettings.RaidChance=40;MDSettings.ThreatDifficulty=2;MDSettings.Persist();
            step=Time.RealTime;stage=2;return;
        }
        if(stage==2&&Time.RealTime-step>3) {
            var units=Units();
            Check(Towers.All(id=>units.Count(u=>u.TowerId==id&&!u.Destroyed)==1),"old: four towers placed");
            Check(units.Count(u=>u.TowerId=="defense_core"&&u.CoreLive&&!u.Destroyed)==1,"old: a live defence core");
            Check(Settings().OrderBy(k=>k.Key).SequenceEqual(Changed.OrderBy(k=>k.Key)),"old: ten settings changed: "+string.Join(" ",Settings().Select(k=>k.Key+"="+k.Value)));
            var file=SettingsFile();info["settingsFileOld"]=file;Check(file.Contains("local.mechdefense.sample"),"old: ModSettings.xml has them under local.mechdefense.sample");
            var mod=ModsManager.ModList.First(m=>m.modInfo.PackageName=="local.mechdefense.sample");info["oldMod"]=$"{mod.modInfo.Name} {mod.modInfo.Version} {mod.modInfo.PackageName}";
            GameManager.SaveProject(true,false);
            info["world"]=world.DirectoryName;stage=-1;Done(problems.Count==0);
        }
    }
    static ComponentMDUnit Place(MDTowerDef d,Vector3 at) {
        var e=DatabaseManager.CreateEntity(GameManager.Project,d.template,true);e.FindComponent<ComponentMDTowerBody>(true).FixAt(at,Quaternion.Identity);
        e.FindComponent<ComponentSpawn>(true).SpawnDuration=0;GameManager.Project.AddEntity(e);return e.FindComponent<ComponentMDUnit>(true);
    }

    static object Dialog() {
        var d=DialogsManager.Dialogs.FirstOrDefault(x=>x.GetType().Name!="BulletinDialog");if(d==null)return null;   // not the game's community bulletin (fetched online; seen on 1.9.4, on 1.9.3.1 since 2026-11-13)
        return new{type=d.GetType().Name,texts=d.AllChildren.OfType<LabelWidget>().Select(l=>l.Text).Where(x=>!string.IsNullOrWhiteSpace(x)).ToList()};
    }
    static void New() {
        if(stage==0) {
            if(ScreensManager.CurrentScreen is not MainMenuScreen)return;
            SettingsManager.VisibilityRange=64;SettingsManager.SoundsVolume=0;SettingsManager.MusicVolume=0;
            var ours=ModsManager.ModList.FirstOrDefault(m=>m.modInfo.PackageName==MDSettings.Package);info["newMod"]=ours==null?"missing":$"{ours.modInfo.Name} {ours.modInfo.Version} {ours.modInfo.PackageName} {ours.modInfo.Author} {ours.modInfo.Link}";
            info["migrated"]=MDSettings.Migrated;
            var now=Settings();info["settings"]=now;
            var lost=Changed.Where(k=>now[k.Key]!=k.Value).Select(k=>$"{k.Key} {k.Value} -> {now[k.Key]}").ToList();info["settingsLost"]=lost;
            Check(lost.Count==0,"new: all ten settings as the old world left them"+(lost.Count>0?" (lost: "+string.Join(", ",lost)+")":""));
            var file=SettingsFile();info["settingsFileNew"]=file;Check(file.Contains(MDSettings.Package),"new: ModSettings.xml now has them under "+MDSettings.Package);
            WorldsManager.UpdateWorldsList();world=WorldsManager.WorldInfos.Where(w=>w.WorldSettings.Name==WorldName).OrderByDescending(w=>w.LastSaveTime).FirstOrDefault()??throw new Exception("no world "+WorldName+": run pkg-old first");
            var used=File.ReadAllText(Storage.GetSystemPath(Storage.CombinePaths(world.DirectoryName,"Project.xml")));info["usedOldBefore"]=used.Contains("local.mechdefense.sample");info["usedNewBefore"]=used.Contains(MDSettings.Package);
            ScreensManager.SwitchScreen("Play");step=Time.RealTime;stage=1;return;
        }
        if(stage==1&&Time.RealTime-step>1) {   // the world list's own way into a world: the used-mods check, then the loading screen
            ScreensManager.FindScreen<PlayScreen>("Play").Play(world);step=Time.RealTime;stage=2;return;
        }
        if(stage==2&&Time.RealTime-step>1.5) {
            var dlg=Dialog();info["dialog"]=dlg;
            Check(dlg==null,"new: no dialog before loading (模组缺失 / 不兼容)");
            info["aliasAnswered"]=typeof(MDOldPackageAlias).GetField("Answered")?.GetValue(null);
            if(dlg!=null){DialogsManager.HideAllDialogs();ScreensManager.FindScreen<PlayScreen>("Play").GameLoad(world);}   // what the player's "是" does: load anyway
            step=Time.RealTime;stage=3;return;
        }
        if(stage==3) {
            if(Time.RealTime-step>120)throw new TimeoutException("the world did not load");
            if(ScreensManager.CurrentScreen is not GameScreen||GameManager.Project==null)return;
            data=GameManager.Project.FindSubsystem<SubsystemPlayers>(true).PlayersData.FirstOrDefault();if(data==null||!data.IsReadyForPlaying)return;
            step=Time.RealTime;stage=4;return;
        }
        if(stage==4&&Time.RealTime-step>3) {
            var units=Units();info["units"]=units.Select(u=>$"{u.TowerId}{(u.Destroyed?" destroyed":"")}{(u.TowerId=="defense_core"?(u.CoreLive?" live":" out"):"")}").ToList();
            Check(Towers.All(id=>units.Count(u=>u.TowerId==id&&!u.Destroyed)==1),"new: the four towers are still there");
            Check(units.Count(u=>u.TowerId=="defense_core"&&u.CoreLive&&!u.Destroyed)==1&&MDThreat.Core(GameManager.Project)!=null,"new: the defence core is still the live one");
            Check(MDThreat.Threat==7,$"new: threat 7 ({MDThreat.Threat})");Check(MDBossKills.Count==13,$"new: 13 robots towards the next boss ({MDBossKills.Count})");
            var inv=data.ComponentPlayer.ComponentMiner.Inventory;var t=GameManager.Project.FindSubsystem<SubsystemTerrain>(true);
            string Item(int slot)=>inv.GetSlotCount(slot)==0?"empty":$"{inv.GetSlotCount(slot)} x {BlocksManager.Blocks[Terrain.ExtractContents(inv.GetSlotValue(slot))].GetDisplayName(t,inv.GetSlotValue(slot))}";
            info["items"]=new[]{Item(0),Item(1)};
            Check(inv.GetSlotValue(0)==Deploy(MDTowerLibrary.Get("gatling_2").data)&&inv.GetSlotCount(0)>0,"new: the tower item in slot 1 ("+Item(0)+"; a creative inventory shows any stack as endless)");
            Check(inv.GetSlotValue(1)==Scrap()&&inv.GetSlotCount(1)>0,"new: robot scrap in slot 2 ("+Item(1)+")");
            GameManager.SaveProject(true,false);
            var used=File.ReadAllText(Storage.GetSystemPath(Storage.CombinePaths(world.DirectoryName,"Project.xml")));info["usedOldAfter"]=used.Contains("local.mechdefense.sample");info["usedNewAfter"]=used.Contains(MDSettings.Package);
            Check(used.Contains(MDSettings.Package)&&!used.Contains("local.mechdefense.sample"),"new: saved again, the world lists "+MDSettings.Package+" only");
            Done(problems.Count==0);
        }
    }
}
