using System.Reflection;
using System.Text.Json;
using System.Xml.Linq;
using Engine;
using Game;
using GameEntitySystem;
using HarmonyLib;

namespace MechDefenseUninstall;

// Uninstall and reinstall (user, 2026-11-06: "看看模组卸载，在安装会不会报错（卸载之前物品栏或者背包有机甲防线物品）"). Only looks, changes nothing in
// the mod. tools/run_isolated.py uninstall --mode <step> --label <one label for the whole sequence> [--package <pkg> | --package none]:
//   u-old          with the mod (0.18.4): a survival world "MDUninstall" with five towers (one hurt), a live defence core, Mercury, threat 7 and
//                  13 robots towards the next boss; mod items in the hotbar, the backpack and a chest (tower items, parts, handbook, beacon,
//                  summon, kit, charge cell, blueprint, a Mercury item); a mod setting changed; saved. Its record: uninstall-state.json.
//   u-none         without the mod: the world opened the player's way (PlayScreen.Play), what loads, what the items are now; 10 s in the
//                  world, then saved (the game also saves on leaving the world, when its window loses focus and every 5 minutes).
//   u-none-nosave  the same, but the process ends without a save (control).
//   u-back         with the mod again (0.18.4 or 1.0.0): opened the same way; what came back, compared with uninstall-state.json.
//   u-back-nostate the same after deleting <world>/MechDefense (1.0.0's copy aside, MDSaveGuard): what is lost then.
//   u-rounds       with the mod: the world saved and loaded three times; this mod's entities must stay as many (nothing restored twice).
//   u-other        with the mod: a new world "MDOther" (another seed) gets MDUninstall's MechDefense folder copied in; it must not restore it.
// u-none also changes the ground of two towers (a stone block in the gun tower's place, the floor under the rotary gun dug out): the terrain
// conflict for a restore. With tools/run_isolated.py --extra-blocks (Tests/LabBlocks: another mod with three new blocks) u-none also puts four
// of those blocks in the world and moves the alloy plates from slot 1 to slot 7 (an item moved while the mod was gone); u-back and
// u-rounds then report what those cells and slot 7 are.
// No reference to the mod's assembly: every mod type through reflection, so the same build runs with the mod removed.
public class Uninstall : ModLoader {
    const string WorldName="MDUninstall";
    static string mode="u-old";static int stage;static double began,step;static WorldInfo world;static PlayerData data;
    static readonly Dictionary<string,object> result=[];static readonly List<string> notes=[];
    static readonly string[] Towers=["battle_turret_1","gatling_2","missile_3","aa_1","cannon_2"];
    static string Dir=>AppContext.BaseDirectory;
    public override void __ModInitialize(){new Harmony("local.uninstall").PatchAll(typeof(Uninstall).Assembly);SettingsManager.FileAssociationEnabled=false;var m=Path.Combine(Dir,"uninstall-mode.txt");if(File.Exists(m))mode=File.ReadAllText(m).Trim();}
    public override void OnLoadingFinished(List<Action> actions){actions.Add(()=>{began=Time.RealTime;Window.Frame+=Frame;});}
    static void Finish(bool ok){result["passed"]=ok;result["mode"]=mode;result["notes"]=notes;File.WriteAllText(Path.Combine(Dir,"uninstall-result.json"),JsonSerializer.Serialize(result,new JsonSerializerOptions{WriteIndented=true}));Window.Frame-=Frame;Window.Close();}
    static void Frame() {
        try{if(Time.RealTime-began>280)throw new TimeoutException("stage "+stage+" on "+ScreensManager.CurrentScreen?.GetType().Name);if(mode=="u-old")Old();else Later();}
        catch(Exception e){result["error"]=e.ToString();Finish(false);}
    }
    // ---- the mod through reflection (null when it is not installed)
    static Type T(string n)=>AppDomain.CurrentDomain.GetAssemblies().Where(a=>a.GetName().Name=="MechDefense").Select(a=>a.GetType("Game."+n,false)).FirstOrDefault(t=>t!=null);
    static bool ModLoaded=>T("MDTowerLibrary")!=null;
    static object F(object o,string n){if(o==null)return null;var t=o as Type??o.GetType();var flags=BindingFlags.Public|BindingFlags.NonPublic|(o is Type?BindingFlags.Static:BindingFlags.Instance);
        return (object)t.GetField(n,flags)?.GetValue(o is Type?null:o)??t.GetProperty(n,flags)?.GetValue(o is Type?null:o);}
    static void Set(object o,string n,object v){var t=o.GetType();var f=t.GetField(n,BindingFlags.Public|BindingFlags.Instance);if(f!=null)f.SetValue(o,v);else t.GetProperty(n,BindingFlags.Public|BindingFlags.Instance).SetValue(o,v);}
    static void SetF(Type t,string n,object v){var f=t.GetField(n,BindingFlags.Public|BindingFlags.Static);if(f!=null)f.SetValue(null,Convert.ChangeType(v,f.FieldType));else t.GetProperty(n,BindingFlags.Public|BindingFlags.Static).SetValue(null,v);}
    static object Call(Type t,string n,params object[] a)=>t.GetMethods(BindingFlags.Public|BindingFlags.Static).First(m=>m.Name==n&&m.GetParameters().Length==a.Length).Invoke(null,a);
    static Component Comp(Entity e,string typeName)=>e.Components.FirstOrDefault(c=>c.GetType().Name==typeName);
    static int Index(string blockType)=>BlocksManager.Blocks.FirstOrDefault(b=>b.GetType().Name==blockType)?.BlockIndex??-1;
    static ContainerWidget Games=>ScreensManager.FindScreen<GameScreen>("Game").Children.Find<ContainerWidget>("GamesWidget");

    // ---- what the world holds now
    static object Item(IInventory inv,int i,SubsystemTerrain t) {
        int v=inv.GetSlotValue(i),n=inv.GetSlotCount(i);if(n<=0)return null;var b=BlocksManager.Blocks[Terrain.ExtractContents(v)];
        string name;try{name=b.GetDisplayName(t,v);}catch(Exception e){name="(name failed: "+e.GetType().Name+")";}
        return new{slot=i,value=v,contents=Terrain.ExtractContents(v),data=Terrain.ExtractData(v),count=n,block=b.GetType().Name,name};
    }
    static List<object> Items(IInventory inv,SubsystemTerrain t)=>inv==null?[]:Enumerable.Range(0,inv.SlotsCount).Select(i=>Item(inv,i,t)).Where(x=>x!=null).ToList();
    static Point3 ChestAt;
    static IInventory Chest(Project p){var be=p.FindSubsystem<SubsystemBlockEntities>(true).GetBlockEntity(ChestAt.X,ChestAt.Y,ChestAt.Z);return be?.Entity.FindComponent<ComponentChest>();}
    static Dictionary<string,object> Snapshot(string tag) {
        var p=GameManager.Project;var t=p.FindSubsystem<SubsystemTerrain>(true);var s=new Dictionary<string,object>();
        var md=p.Entities.Where(e=>e.ValuesDictionary.DatabaseObject.Name.StartsWith("MD")||e.Components.Any(c=>c.GetType().Name.StartsWith("ComponentMD"))).ToList();
        s["entities"]=p.Entities.Count();s["modEntities"]=md.Select(e=>{
            var u=Comp(e,"ComponentMDUnit");var b=e.FindComponent<ComponentBody>();
            return $"{e.ValuesDictionary.DatabaseObject.Name} {(string)F(u,"TowerId")} at ({b?.Position.X:0.0},{b?.Position.Y:0.0},{b?.Position.Z:0.0}) hp {F(F(u,"Damage"),"Hitpoints")} {(F(u,"Mech")!=null?"mech":"")}{((F(u,"TowerId") as string)=="defense_core"?(true.Equals(F(u,"CoreLive"))?" live":" out"):"")}";
        }).OrderBy(x=>x).ToList();
        s["inventory"]=Items(data.ComponentPlayer.ComponentMiner.Inventory,t);s["chest"]=Items(Chest(p),t);s["chestBlock"]=BlocksManager.Blocks[Terrain.ExtractContents(t.Terrain.GetCellValue(ChestAt.X,ChestAt.Y,ChestAt.Z))].GetType().Name;
        var dyn=p.FindSubsystem<SubsystemBlocksManager>(true).DynamicBlockNameToIndex;s["dynamicBlocks"]=dyn.Where(kv=>kv.Key.StartsWith("MD")).ToDictionary(kv=>kv.Key,kv=>kv.Value);
        s["ghost"]=dyn.TryGetValue("GhostModBlock",out var gi)?gi:-1;s["labNumbers"]=dyn.Where(kv=>kv.Key.StartsWith("LabBlock")).ToDictionary(kv=>kv.Key,kv=>kv.Value);
        s["blockAtIndex"]=dyn.Where(kv=>kv.Key.StartsWith("MD")).ToDictionary(kv=>kv.Key,kv=>BlocksManager.Blocks[kv.Value].GetType().Name);
        s["labCells"]=Enumerable.Range(0,4).Select(i=>BlocksManager.Blocks[Terrain.ExtractContents(t.Terrain.GetCellValue((int)Origin.X+4+i,70,(int)Origin.Z+16))].GetType().Name).ToList();
        s["modPickables"]=p.FindSubsystem<SubsystemPickables>(true).Pickables.Where(k=>BlocksManager.Blocks[Terrain.ExtractContents(k.Value)].GetType().Name.StartsWith("MD")).Select(k=>$"{BlocksManager.Blocks[Terrain.ExtractContents(k.Value)].GetDisplayName(t,k.Value)} x{k.Count} at ({k.Position.X:0},{k.Position.Y:0},{k.Position.Z:0})").ToList();
        if(ModLoaded){s["threat"]=F(T("MDThreat"),"Threat");s["bossKills"]=F(T("MDBossKills"),"Count");s["startDay"]=F(T("MDSettings"),"StartDay");}
        return s;
    }
    static object Files(string tag) {
        object One(string f){var path=Storage.GetSystemPath(Storage.CombinePaths(world.DirectoryName,f));if(!File.Exists(path))return "missing";
            var x=XElement.Load(path);var ents=x.Element("Entities")?.Elements("Entity").ToList()??[];
            return new{bytes=new FileInfo(path).Length,written=File.GetLastWriteTime(path).ToString("HH:mm:ss.fff"),entities=ents.Count,modEntities=ents.Count(e=>((string)e.Attribute("Name")??"").StartsWith("MD")),
                rootAttributes=x.Attributes().Where(a=>a.Name.LocalName.StartsWith("MechDefense")).Select(a=>a.Name.LocalName+"="+a.Value).ToList(),usedMods=x.ToString().Contains("mechdefense",StringComparison.OrdinalIgnoreCase)};}
        return new{projectXml=One("Project.xml"),projectBak=One("Project.bak")};
    }
    static void Shots(string tag,Vector3 at) {
        var pl=data.ComponentPlayer;var c=pl.GameWidget.FindCamera<FixedCamera>();pl.GameWidget.ActiveCamera=c;c.SetupPerspectiveCamera(at+new Vector3(-6,9,22),Vector3.Normalize(at+new Vector3(10,0,0)-(at+new Vector3(-6,9,22))),Vector3.UnitY);
        ScreenCaptureManager.Capture(1280,720,$"u_{tag}_world.png");
    }
    static void InventoryShot(string tag) {
        var pl=data.ComponentPlayer;SettingsManager.ShowGuiInScreenshots=true;pl.GameWidget.ActiveCamera=pl.GameWidget.FindCamera<FppCamera>();
        try{pl.ComponentGui.ModalPanelWidget=new FullInventoryWidget(pl.ComponentMiner.Inventory,pl.Entity.FindComponent<ComponentCraftingTable>(true));}catch(Exception e){notes.Add("inventory panel: "+e.Message);}
    }
    static Vector3 Origin;

    static void Old() {
        if(stage==0) {
            if(ScreensManager.CurrentScreen is not MainMenuScreen)return;
            if(!ModLoaded)throw new Exception("u-old needs the mod");
            SettingsManager.VisibilityRange=64;SettingsManager.SoundsVolume=0;SettingsManager.MusicVolume=0;
            world=WorldsManager.CreateWorld(new WorldSettings{Name=WorldName,Seed="42",GameMode=GameMode.Survival,TerrainGenerationMode=TerrainGenerationMode.FlatContinent,TerrainLevel=64,TerrainBlockIndex=8,AreWeatherEffectsEnabled=false,TimeOfDayMode=TimeOfDayMode.Day});
            GameManager.LoadProject(world,Games);
            data=new PlayerData(GameManager.Project){Name="Uninstall",SpawnPosition=new(100,65,100),InputDevice=WidgetInputDevice.None};GameManager.Project.FindSubsystem<SubsystemPlayers>(true).AddPlayerData(data);
            ScreensManager.SwitchScreen("Game");stage=1;return;
        }
        if(stage==1) {
            if(!data.IsReadyForPlaying)return;
            var p=GameManager.Project;var pl=data.ComponentPlayer;var t=p.FindSubsystem<SubsystemTerrain>(true);
            Origin=new(Terrain.ToCell(pl.ComponentBody.Position.X)+.5f,70.05f,Terrain.ToCell(pl.ComponentBody.Position.Z)+.5f);
            for(int x=(int)Origin.X-25;x<=(int)Origin.X+25;x++)for(int z=(int)Origin.Z-25;z<=(int)Origin.Z+25;z++)for(int y=66;y<80;y++)t.ChangeCell(x,y,z,y<69?2:y==69?8:0);
            var lib=T("MDTowerLibrary");var body=T("ComponentMDTowerBody");
            Entity Place(string id,Vector3 at){var d=Call(lib,"Get",id);var e=DatabaseManager.CreateEntity(p,(string)F(d,"template"),true);var b=e.Components.First(c=>c.GetType()==body);
                body.GetMethod("FixAt").Invoke(b,[at,Quaternion.Identity]);e.FindComponent<ComponentSpawn>(true).SpawnDuration=0;p.AddEntity(e);return e;}
            for(int i=0;i<Towers.Length;i++)Place(Towers[i],Origin+new Vector3(-16+i*8,0,-10));
            var core=Place("defense_core",Origin+new Vector3(0,0,8));Call(T("MDThreat"),"Placed",Comp(core,"ComponentMDUnit"),pl);
            var mech=DatabaseManager.CreateEntity(p,"MDMech",true);mech.FindComponent<ComponentBody>(true).Position=Origin+new Vector3(12,0,8);mech.FindComponent<ComponentSpawn>(true).SpawnDuration=0;p.AddEntity(mech);
            ChestAt=new Point3((int)Origin.X-6,70,(int)Origin.Z+14);t.ChangeCell(ChestAt.X,ChestAt.Y,ChestAt.Z,BlocksManager.GetBlockIndex<ChestBlock>());
            pl.ComponentBody.Position=Origin+new Vector3(0,0,18);
            step=Time.RealTime;stage=2;return;
        }
        if(stage==2&&Time.RealTime-step>1.5) {
            var p=GameManager.Project;var pl=data.ComponentPlayer;var lib=T("MDTowerLibrary");
            int dep=Index("MDDeploymentBlock"),part=Index("MDPartBlock"),summon=Index("MDSummonBlock");
            int Tower(string id)=>Terrain.MakeBlockValue(dep,0,(int)F(Call(lib,"Get",id),"data"));int Part(int k)=>Terrain.MakeBlockValue(part,0,k);
            // hotbar 0-5, backpack 10+; part kinds: 0 scrap 1 alloy 2 chip 3 core 4 blueprint 5 beacon 6 kit1 7 kit2 8 kit3 9 charge 10 manual
            var inv=pl.ComponentMiner.Inventory;result["slots"]=inv.SlotsCount;
            void Put(IInventory i,int slot,int value,int n){i.RemoveSlotItems(slot,i.GetSlotCount(slot));i.AddSlotItems(slot,value,n);}
            Put(inv,0,Tower("gatling_2"),2);Put(inv,1,Part(1),10);Put(inv,2,Part(2),5);Put(inv,3,Part(3),3);Put(inv,4,Part(10),1);Put(inv,5,Part(5),2);
            Put(inv,10,Terrain.MakeBlockValue(summon,0,0),1);Put(inv,11,Terrain.MakeBlockValue(dep,0,1),1);Put(inv,12,Part(0),20);
            var chest=Chest(p)??throw new Exception("no chest entity at "+ChestAt);Put(chest,0,Tower("battle_turret_3"),1);Put(chest,1,Part(6),1);Put(chest,2,Part(9),2);Put(chest,3,Part(4),1);
            var units=p.Entities.Select(e=>Comp(e,"ComponentMDUnit")).Where(u=>u!=null).ToList();
            var hurt=units.First(u=>(string)F(u,"TowerId")=="gatling_2");Set(F(hurt,"Damage"),"Hitpoints",.6f);
            SetF(T("MDThreat"),"Threat",7f);SetF(T("MDBossKills"),"Count",13);SetF(T("MDSettings"),"StartDay",12);Call(T("MDSettings"),"Persist");
            step=Time.RealTime;stage=3;return;
        }
        if(stage==3&&Time.RealTime-step>1.5) {
            var s=Snapshot("old");result["snapshot"]=s;Shots("old",Origin);
            GameManager.SaveProject(true,false);result["files"]=Files("old");
            var state=new Dictionary<string,object>{["origin"]=new[]{Origin.X,Origin.Y,Origin.Z},["chest"]=new[]{ChestAt.X,ChestAt.Y,ChestAt.Z},["snapshot"]=s,["world"]=world.DirectoryName};
            File.WriteAllText(Path.Combine(Dir,"uninstall-state.json"),JsonSerializer.Serialize(state,new JsonSerializerOptions{WriteIndented=true}));
            step=Time.RealTime;stage=4;return;
        }
        if(stage==4&&Time.RealTime-step>.5){InventoryShot("old");step=Time.RealTime;stage=5;return;}
        if(stage==5&&Time.RealTime-step>1){ScreenCaptureManager.Capture(1280,720,"u_old_inventory.png");Finish(true);}
    }

    static void Later() {
        if(stage==0) {
            if(ScreensManager.CurrentScreen is not MainMenuScreen)return;
            SettingsManager.VisibilityRange=64;SettingsManager.SoundsVolume=0;SettingsManager.MusicVolume=0;
            var st=JsonDocument.Parse(File.ReadAllText(Path.Combine(Dir,"uninstall-state.json"))).RootElement;
            var o=st.GetProperty("origin");Origin=new(o[0].GetSingle(),o[1].GetSingle(),o[2].GetSingle());var c=st.GetProperty("chest");ChestAt=new(c[0].GetInt32(),c[1].GetInt32(),c[2].GetInt32());
            result["modLoaded"]=ModLoaded;result["mods"]=ModsManager.ModList.Select(m=>$"{m.modInfo.Name} {m.modInfo.Version} {m.modInfo.PackageName}").ToList();
            WorldsManager.UpdateWorldsList();world=WorldsManager.WorldInfos.FirstOrDefault(w=>w.WorldSettings.Name==WorldName)??throw new Exception("no world "+WorldName);
            var aside=Storage.GetSystemPath(Storage.CombinePaths(world.DirectoryName,"MechDefense"));result["asideBefore"]=Directory.Exists(aside)?Directory.GetFiles(aside).Select(f=>$"{Path.GetFileName(f)} {new FileInfo(f).Length} B").ToList():null;
            if(mode=="u-back-nostate"&&Directory.Exists(aside)){Directory.Delete(aside,true);notes.Add("deleted "+aside);}
            if(mode=="u-other") {   // another world gets this world's MechDefense folder
                var other=WorldsManager.CreateWorld(new WorldSettings{Name="MDOther",Seed="77",GameMode=GameMode.Survival,TerrainGenerationMode=TerrainGenerationMode.FlatContinent,TerrainLevel=64,TerrainBlockIndex=8,AreWeatherEffectsEnabled=false,TimeOfDayMode=TimeOfDayMode.Day});
                var to=Storage.GetSystemPath(Storage.CombinePaths(other.DirectoryName,"MechDefense"));Directory.CreateDirectory(to);
                foreach(var f in Directory.GetFiles(aside))File.Copy(f,Path.Combine(to,Path.GetFileName(f)),true);
                notes.Add($"copied {aside} to {to}");WorldsManager.UpdateWorldsList();world=WorldsManager.WorldInfos.First(w=>w.WorldSettings.Name=="MDOther");
            }
            result["filesBefore"]=Files("before");
            ScreensManager.SwitchScreen("Play");step=Time.RealTime;stage=1;return;
        }
        if(stage==1&&Time.RealTime-step>1){ScreensManager.FindScreen<PlayScreen>("Play").Play(world);step=Time.RealTime;stage=2;return;}
        if(stage==2&&Time.RealTime-step>1.5) {
            var d=DialogsManager.Dialogs.FirstOrDefault();
            result["dialog"]=d==null?null:d.AllChildren.OfType<LabelWidget>().Select(l=>l.Text).Where(x=>!string.IsNullOrWhiteSpace(x)).ToList();
            bool missing=d!=null&&d.AllChildren.OfType<LabelWidget>().Any(l=>l.Text!=null&&l.Text.Contains("缺少"));
            if(d!=null){DialogsManager.HideAllDialogs();if(missing||ScreensManager.CurrentScreen is PlayScreen)ScreensManager.FindScreen<PlayScreen>("Play").GameLoad(world);}   // the player's "是" (other dialogs: a notice, or the loading screen's own)
            step=Time.RealTime;stage=3;return;
        }
        if(stage==3) {
            if(Time.RealTime-step>90){result["loaded"]=false;result["screen"]=ScreensManager.CurrentScreen?.GetType().Name;Finish(false);return;}
            if(ScreensManager.CurrentScreen is PlayerScreen&&GameManager.Project!=null) {   // a new world asks for a player first
                var ps=GameManager.Project.FindSubsystem<SubsystemPlayers>(true);if(ps.PlayersData.Count==0)ps.AddPlayerData(new PlayerData(GameManager.Project){Name="Other",SpawnPosition=new(100,65,100),InputDevice=WidgetInputDevice.None});
                ScreensManager.SwitchScreen("Game");return;
            }
            if(ScreensManager.CurrentScreen is not GameScreen||GameManager.Project==null)return;
            var players=GameManager.Project.FindSubsystem<SubsystemPlayers>(true);
            if(players.PlayersData.Count==0){data=new PlayerData(GameManager.Project){Name="Other",SpawnPosition=new(100,65,100),InputDevice=WidgetInputDevice.None};players.AddPlayerData(data);}
            data=players.PlayersData.FirstOrDefault();if(data==null||!data.IsReadyForPlaying)return;
            result["loaded"]=true;result["loadSeconds"]=Math.Round(Time.RealTime-step,1);step=Time.RealTime;stage=4;return;
        }
        if(stage==4&&Time.RealTime-step>3){
            result["snapshot"]=Snapshot(mode);Shots(mode,Origin);
            if(ModLoaded){var g=T("MDSaveGuard");result["saveGuard"]=g==null?"(no MDSaveGuard in this package)":$"{F(g,"LastRestore")}; taken down {F(g,"TakenDown")}";}
            if(ModLoaded&&T("MDSaveGuard") is {} g2&&F(g2,"Unrestored") is System.Collections.IEnumerable un)result["unrestored"]=un.Cast<object>().Select(x=>x.ToString()).ToList();
            if(mode.StartsWith("u-none")) {   // the terrain conflict: a stone where the gun tower stands, no floor under the rotary gun
                var t=GameManager.Project.FindSubsystem<SubsystemTerrain>(true);var g=Origin+new Vector3(-16,0,-10);var r=Origin+new Vector3(-8,0,-10);
                t.ChangeCell(Terrain.ToCell(g.X),70,Terrain.ToCell(g.Z),3);
                for(int dx=-1;dx<=0;dx++)for(int dz=-1;dz<=0;dz++)for(int y=66;y<70;y++)t.ChangeCell(Terrain.ToCell(r.X)+dx,y,Terrain.ToCell(r.Z)+dz,0);
                if(BlocksManager.Blocks.Any(b=>b.GetType().Name=="LabBlockA")) {   // another mod's blocks in the world, one of our items moved
                    int la=BlocksManager.Blocks.First(b=>b.GetType().Name=="LabBlockA").BlockIndex;var lb=BlocksManager.Blocks.First(b=>b.GetType().Name=="LabBlockB").BlockIndex;
                    for(int i=0;i<4;i++)t.ChangeCell((int)Origin.X+4+i,70,(int)Origin.Z+16,i%2==0?la:lb);
                    var inv=(ComponentInventoryBase)data.ComponentPlayer.ComponentMiner.Inventory;int v=inv.GetSlotValue(1),n=inv.GetSlotCount(1);
                    void SetSlot(int i,int value,int count){inv.m_slots[i].Value=value;inv.m_slots[i].Count=count;}   // the player's drag (capacity of an unknown block is 0)
                    if(n>0){SetSlot(7,v,n);SetSlot(1,Terrain.MakeBlockValue(la,0,0),n);notes.Add($"moved slot 1 ({v} x{n}, now {BlocksManager.Blocks[Terrain.ExtractContents(v)].GetType().Name}) to slot 7 and put LabBlockA ({Terrain.MakeBlockValue(la,0,0)}) x{n} there (another value)");}
                    int v12=inv.GetSlotValue(12),n12=inv.GetSlotCount(12);   // the known limit: the same value and count put back where ours was
                    if(n12>0){SetSlot(13,v12,n12);SetSlot(12,v12,n12);notes.Add($"moved slot 12 ({v12} x{n12}, now {BlocksManager.Blocks[Terrain.ExtractContents(v12)].GetType().Name}) to slot 13 and put the same value and count back into slot 12");}
                    var dyn=GameManager.Project.FindSubsystem<SubsystemBlocksManager>(true).DynamicBlockNameToIndex;int ghost=300;while(dyn.ContainsValue(ghost))ghost++;
                    dyn["GhostModBlock"]=ghost;notes.Add($"a mod not installed now keeps number {ghost} (GhostModBlock) in the block map");result["ghost"]=ghost;
                    notes.Add($"lab blocks A {la}, B {lb} at x {(int)Origin.X+4}..{(int)Origin.X+7}, y 70, z {(int)Origin.Z+16}");
                }
                notes.Add($"terrain: stone at ({Terrain.ToCell(g.X)},70,{Terrain.ToCell(g.Z)}), floor under ({Terrain.ToCell(r.X)},{Terrain.ToCell(r.Z)}) dug out");
            }
            step=Time.RealTime;stage=5;return;}
        if(stage==5&&Time.RealTime-step>.5){InventoryShot(mode);step=Time.RealTime;stage=6;return;}
        if(stage==6&&Time.RealTime-step>1) {
            ScreenCaptureManager.Capture(1280,720,$"u_{mode}_inventory.png");data.ComponentPlayer.ComponentGui.ModalPanelWidget=null;SettingsManager.ShowGuiInScreenshots=false;
            step=Time.RealTime;stage=7;return;
        }
        if(stage==7&&Time.RealTime-step>(mode=="u-back"?2:10)) {   // a while in the world
            result["snapshotLater"]=Snapshot(mode+"-later");
            if(ModLoaded&&T("MDSaveGuard") is {} sg)result["saveGuardLater"]=$"{F(sg,"LastRestore")}; taken down {F(sg,"TakenDown")}; pending {F(sg,"Pending")}; last save {F(sg,"LastSave")}";
            if(mode=="u-rounds") {   // save and load again, three times
                var counts=new List<string>();
                for(int k=0;k<3;k++){GameManager.SaveProject(true,false);var w=world;GameManager.LoadProject(w,Games);
                    counts.Add($"round {k+1}: {GameManager.Project.Entities.Count(e=>e.ValuesDictionary.DatabaseObject.Name.StartsWith("MD"))} mod entities, {F(T("MDSaveGuard"),"LastRestore")}");}
                result["rounds"]=counts;Finish(true);return;
            }
            if(mode!="u-none-nosave")GameManager.SaveProject(true,false);
            result["filesAfter"]=Files("after");
            if(mode=="u-back")Compare();
            Finish(true);
        }
    }
    // what u-back has against u-old's record
    static void Compare() {
        var st=JsonDocument.Parse(File.ReadAllText(Path.Combine(Dir,"uninstall-state.json"))).RootElement.GetProperty("snapshot");
        var now=JsonDocument.Parse(JsonSerializer.Serialize(result["snapshot"])).RootElement;var lost=new List<string>();
        string Key(JsonElement x)=>x.ValueKind==JsonValueKind.Object?$"slot {x.GetProperty("slot")} {x.GetProperty("block")} data {x.GetProperty("data")} x{x.GetProperty("count")} {x.GetProperty("name")}":x.ToString();   // block numbers may differ
        foreach(var k in new[]{"modEntities","inventory","chest"}){var a=st.GetProperty(k).EnumerateArray().Select(Key).ToList();var b=now.GetProperty(k).EnumerateArray().Select(Key).ToList();
            foreach(var x in a.Except(b))lost.Add($"{k}: {x}");foreach(var x in b.Except(a))lost.Add($"{k} now: {x}");}
        foreach(var k in new[]{"threat","bossKills","startDay"}){var a=st.TryGetProperty(k,out var x)?x.ToString():"-";var b=now.TryGetProperty(k,out var y)?y.ToString():"-";if(a!=b)lost.Add($"{k}: {a} -> {b}");}
        result["differences"]=lost;
    }
}

[HarmonyPatch(typeof(Engine.Input.Mouse),"BeforeFrame")]
public static class KeepCursorFree { public static void Prefix()=>Engine.Input.Mouse.IsMouseVisible=true; }
[HarmonyPatch(typeof(Engine.Input.Mouse),"AfterFrame")]
public static class KeepCursorFreeAfter { public static void Prefix()=>Engine.Input.Mouse.IsMouseVisible=true; }
