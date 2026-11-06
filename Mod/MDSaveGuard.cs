using System.Globalization;
using System.Xml.Linq;
using Engine;
using GameEntitySystem;

namespace Game;

// 1.0.0 (user, 2026-11-06, after the uninstall test: a world saved once without this mod loses every tower, mech and robot for good):
// the engine reads Project.xml with ignoreInvalidEntities, so an entity whose template is missing is skipped ("Ignoring invalid entity")
// and the next save writes only what it loaded; the Project root's MechDefense* attributes (threat, boss count...) go the same way.
// Block values in inventories and chests are plain numbers and survive by themselves (the dynamic block names are saved too).
//   Save   (OnProjectXmlSaved, the XML about to be written): this mod's entities (every template of this mod's database) and the root's
//          MechDefense* attributes go to <world>/MechDefense/state.xml (written to state.tmp, then moved over it), with this world's own
//          id (MechDefenseWorldId on the Project root, made at the first save), seed, name and play time.
//   Restore (ProjectXmlLoad, before the engine parses the XML): only when the Project root has no MechDefenseWorldId (it was saved
//          without this mod) and state.xml is there and belongs to this world (same seed and name, its play time not later than the
//          world's): the saved entities whose Id is not taken go back into <Entities> (NextID raised past them), the missing attributes back
//          on the root. Anything wrong (no file, another world, a broken file): logged, skipped, the world loads as it is.
//   Block numbers (user, 2026-11-06): this mod's blocks get dynamic numbers (BlocksManager, saved as "number = class name" in the
//          BlocksManager subsystem). A block mod added while this one was gone takes the first free numbers, which are ours (the engine
//          checks only m_blocks[n]==null), and the save keeps one name per number. state.xml also records our "class = number" and every
//          inventory slot (any entity: player, chest, ...) holding one of our blocks: entity id, path, value. On restore, a number of ours
//          now named for another class: we take a new one Y (>= 300, not named in the map, not a static block's) and write "Y = our class",
//          so the engine puts our block there (every block class on its own); a recorded slot whose value (number and data) and count are
//          both still the recorded ones is rewritten to Y (data kept); any other (moved, used, crafted, replaced while we were gone, and
//          our items on the ground or put into another container) stays the other mod's and is listed in the log and told to the player.
//          Not certain either way: the other mod's item with the very same value and count in that slot is rewritten as well. This mod puts none of
//          its blocks into the terrain (every one is IsPlaceable false: deployments become entities), so no terrain cell needs fixing.
//   Terrain: a restored tower whose footprint is now taken by blocks or has lost its floor (MDTowerPlacement.TerrainFits) is taken down
//          as its item where it stood, as a pickaxe would (MDTowerMaintenance.Dismantle).
public static class MDSaveGuard {
    public const string Folder="MechDefense",File="state.xml",Temp="state.tmp",IdAttribute="MechDefenseWorldId",Format="1";
    public static bool Enabled=true;   // lab switch: false = 0.18.4 (nothing saved aside, nothing restored)
    public static string WorldId;      // this world's id (from Project.xml, the restored state or new)
    public static string LastSave="",LastRestore="";   // what the last save / load did (log, lab)
    public static int Restored,TakenDown;
    static readonly HashSet<int> pending=[];static string message;
    public static int Pending=>pending.Count;   // lab
    static HashSet<string> ours;
    // this mod's entity templates: those the engine marked with this package as their source when it merged our database
    public static bool Ours(string template) {
        if(ours==null) {
            var set=new HashSet<string>();
            foreach(var name in Templates())try{if(DatabaseManager.FindEntityValuesDictionary(name,false)?.GetValue<string>("ModSource",null) is MDSettings.Package or MDSettings.OldPackage)set.Add(name);}catch{}
            ours=set;
        }
        return template!=null&&ours.Contains(template);
    }
    static IEnumerable<string> Templates() {
        var db=DatabaseManager.GameDatabase;
        return db.Database.Root.GetExplicitNestingChildren(db.EntityTemplateType,false).Select(o=>o.Name).Where(n=>n!=null&&n.StartsWith("MD"));
    }
    static string Value(XElement project,string subsystem,string name)=>project.Element("Subsystems")?.Elements("Values").FirstOrDefault(v=>(string)v.Attribute("Name")==subsystem)?
        .Elements("Value").FirstOrDefault(v=>(string)v.Attribute("Name")==name)?.Attribute("Value")?.Value;
    static double Number(string s)=>double.TryParse(s,NumberStyles.Float,CultureInfo.InvariantCulture,out var d)?d:0;

    static readonly System.Text.RegularExpressions.Regex SlotName=new("^Slot[0-9]+$");
    static Dictionary<string,int> OurBlocks()=>typeof(MDSaveGuard).Assembly.GetTypes().Where(t=>t.IsSubclassOf(typeof(Block))&&!t.IsAbstract).ToDictionary(t=>t.Name,t=>BlocksManager.Blocks.FirstOrDefault(b=>b.GetType()==t)?.BlockIndex??-1);
    static XElement Blocks(XElement project)=>new("Blocks",OurBlocks().Where(kv=>kv.Value>0).Select(kv=>new XElement("Block",new XAttribute("Name",kv.Key),new XAttribute("Index",kv.Value))));
    // every slot (Values "SlotN" with a "Contents" value) of every entity that holds one of our blocks
    static XElement Items(XElement project) {
        var ix=OurBlocks().Values.Where(v=>v>0).ToHashSet();var items=new XElement("Items");
        foreach(var e in project.Element("Entities")?.Elements("Entity")??[])
            foreach(var slot in e.Descendants("Values").Where(v=>SlotName.IsMatch((string)v.Attribute("Name")??""))) {
                var c=slot.Elements("Value").FirstOrDefault(v=>(string)v.Attribute("Name")=="Contents");if(c==null||!int.TryParse((string)c.Attribute("Value"),out int value)||!ix.Contains(Terrain.ExtractContents(value)))continue;
                var path=string.Join("/",slot.AncestorsAndSelf().TakeWhile(a=>a!=e).Reverse().Select(a=>(string)a.Attribute("Name")));
                var b=BlocksManager.Blocks[Terrain.ExtractContents(value)];string what;try{what=b.GetDisplayName(null,value);}catch{what=b.GetType().Name;}
                items.Add(new XElement("Item",new XAttribute("Entity",(string)e.Attribute("Id")),new XAttribute("Container",(string)e.Attribute("Name")??""),new XAttribute("Path",path),new XAttribute("Value",value),new XAttribute("What",what),
                    new XAttribute("Count",(string)slot.Elements("Value").FirstOrDefault(v=>(string)v.Attribute("Name")=="Count")?.Attribute("Value")??"")));
            }
        return items;
    }
    public static string LastBlocks="";public static int ItemsMoved,ItemsLost;public static readonly List<string> Unrestored=[];   // lab
    // our block numbers taken by another class while we were gone: new numbers in the map, the recorded slots moved to them
    static void Renumber(XElement project,XElement state) {
        LastBlocks="";ItemsMoved=ItemsLost=0;Unrestored.Clear();
        var map=project.Element("Subsystems")?.Elements("Values").FirstOrDefault(v=>(string)v.Attribute("Name")=="BlocksManager");if(map==null)return;
        var named=map.Elements("Value").ToDictionary(v=>(string)v.Attribute("Name"),v=>v);
        var statics=BlocksManager.BlocksAllocateData.Where(d=>d.StaticBlockIndex&&d.Block.BlockIndex>=300).Select(d=>d.Block.BlockIndex).ToHashSet();
        var remap=new Dictionary<int,int>();var notes=new List<string>();var ours2=OurBlocks().Keys.ToHashSet();
        foreach(var b in state.Element("Blocks")?.Elements("Block")??[]) {
            string name=(string)b.Attribute("Name");int x=(int?)b.Attribute("Index")??0;if(x<300)continue;
            var now=named.GetValueOrDefault(x.ToString())?.Attribute("Value")?.Value;
            if(now==name)continue;
            foreach(var stale in map.Elements("Value").Where(v=>(string)v.Attribute("Value")==name).ToList()){named.Remove((string)stale.Attribute("Name"));stale.Remove();}
            if(now==null){var v=new XElement("Value",new XAttribute("Name",x),new XAttribute("Type","string"),new XAttribute("Value",name));map.Add(v);named[x.ToString()]=v;notes.Add($"{name} back on {x}");continue;}
            // a number no name in the map has (the map also keeps numbers of mods not installed now), no static block's, no block's now
            string Busy(int n)=>named.ContainsKey(n.ToString())?"named "+named[n.ToString()].Attribute("Value")?.Value:statics.Contains(n)?"static":
                BlocksManager.Blocks[n] is {} k&&k is not AirBlock&&!ours2.Contains(k.GetType().Name)?"taken by "+k.GetType().Name:null;
            int y=300;while(y<1024&&Busy(y)!=null)y++;
            if(y>=1024){notes.Add($"{name}: no free number ({string.Join(", ",Enumerable.Range(300,8).Select(n=>n+" "+Busy(n)))})");continue;}
            var nv=new XElement("Value",new XAttribute("Name",y),new XAttribute("Type","string"),new XAttribute("Value",name));map.Add(nv);named[y.ToString()]=nv;remap[x]=y;
            notes.Add($"{name} {x} -> {y} ({x} is {now} now)");
        }
        if(remap.Count>0) {
            var byId=project.Element("Entities")?.Elements("Entity").ToDictionary(e=>(string)e.Attribute("Id"),e=>e)??[];
            foreach(var it in state.Element("Items")?.Elements("Item")??[]) {
                int value=(int?)it.Attribute("Value")??0;if(!remap.TryGetValue(Terrain.ExtractContents(value),out int y))continue;
                XElement node=byId.GetValueOrDefault((string)it.Attribute("Entity"));
                foreach(var part in ((string)it.Attribute("Path")??"").Split('/'))node=node?.Elements("Values").FirstOrDefault(v=>(string)v.Attribute("Name")==part);
                var c=node?.Elements("Value").FirstOrDefault(v=>(string)v.Attribute("Name")=="Contents");
                string count=(string)node?.Elements("Value").FirstOrDefault(v=>(string)v.Attribute("Name")=="Count")?.Attribute("Value")??"";
                if(c!=null&&(string)c.Attribute("Value")==value.ToString()&&count==((string)it.Attribute("Count")??"")){c.SetAttributeValue("Value",(value&~1023)|y);ItemsMoved++;}
                else{ItemsLost++;Unrestored.Add($"{(string)it.Attribute("Container")} #{(string)it.Attribute("Entity")} {(string)it.Attribute("Path")}: {(string)it.Attribute("What")} x{(string)it.Attribute("Count")} (now {(c==null?"empty":"value "+(string)c.Attribute("Value"))}{(count.Length>0?" x"+count:"")})");}
            }
        }
        LastBlocks=notes.Count==0?"numbers unchanged":string.Join("; ",notes)+(remap.Count>0?$"; slots moved {ItemsMoved}, not found as recorded {ItemsLost} (they stay the other mod's)":"");
        if(notes.Count>0)Log.Information("[MechDefense] block numbers: "+LastBlocks);
        foreach(var u in Unrestored)Log.Warning("[MechDefense] not restored (moved, used or replaced while the mod was gone; it stays the other mod's): "+u);
    }

    // ProjectXmlSave (before the engine writes its part): the world's id on the root
    public static void SaveId(XElement project){if(!Enabled||project==null)return;WorldId??=Guid.NewGuid().ToString("N");project.SetAttributeValue(IdAttribute,WorldId);}

    // OnProjectXmlSaved: the XML as it goes to disk; runs on the engine's save thread
    public static void Saved(XElement project) {
        if(!Enabled||project==null)return;
        try {
            var dir=GameManager.WorldInfo?.DirectoryName;if(string.IsNullOrEmpty(dir)){LastSave="no world folder";return;}
            var mine=project.Element("Entities")?.Elements("Entity").Where(e=>Ours((string)e.Attribute("Name"))).Select(e=>new XElement(e)).ToList()??[];
            var state=new XElement("MechDefenseState",new XAttribute("Format",Format),new XAttribute("WorldId",(string)project.Attribute(IdAttribute)??WorldId??""),
                new XAttribute("WorldSeed",Value(project,"GameInfo","WorldSeed")??""),new XAttribute("WorldName",Value(project,"GameInfo","WorldName")??""),
                new XAttribute("PlayTime",Value(project,"GameInfo","TotalElapsedGameTime")??"0"),new XAttribute("Saved",DateTime.Now.ToString("s",CultureInfo.InvariantCulture)),
                new XElement("Attributes",project.Attributes().Where(a=>a.Name.LocalName.StartsWith("MechDefense")).Select(a=>new XAttribute(a))),
                new XElement("Entities",mine),Blocks(project));
            var folder=Storage.GetSystemPath(Storage.CombinePaths(dir,Folder));Directory.CreateDirectory(folder);
            string tmp=Path.Combine(folder,Temp),dst=Path.Combine(folder,File);
            state.Add(Items(project));state.Save(tmp);System.IO.File.Move(tmp,dst,true);
            LastSave=$"{mine.Count} entities, {state.Element("Items")?.Elements().Count()??0} item slots";
        } catch(Exception x){LastSave="failed: "+x.Message;Log.Warning("[MechDefense] saving MechDefense/state.xml: "+x.Message);}
    }

    // ProjectXmlLoad, before MDBossKills / MDRewards / MDThreat read the root
    public static void Load(XElement project,WorldInfo world) {
        pending.Clear();message=null;Restored=TakenDown=0;LastRestore="";WorldId=(string)project?.Attribute(IdAttribute);
        if(!Enabled||project==null)return;
        if(WorldId!=null){LastRestore="saved with the mod";return;}
        try {
            var dir=world?.DirectoryName??GameManager.WorldInfo?.DirectoryName;if(string.IsNullOrEmpty(dir)){LastRestore="no world folder";return;}
            var path=Storage.GetSystemPath(Storage.CombinePaths(Storage.CombinePaths(dir,Folder),File));
            if(!System.IO.File.Exists(path)){LastRestore="nothing saved aside";return;}
            var state=XElement.Load(path);
            if((string)state.Attribute("Format")!=Format){LastRestore="unknown format "+(string)state.Attribute("Format");Log.Warning("[MechDefense] MechDefense/state.xml: "+LastRestore+", not restored");return;}
            string seed=Value(project,"GameInfo","WorldSeed")??"",name=Value(project,"GameInfo","WorldName")??"";double time=Number(Value(project,"GameInfo","TotalElapsedGameTime"));
            if((string)state.Attribute("WorldSeed")!=seed||(string)state.Attribute("WorldName")!=name||Number((string)state.Attribute("PlayTime"))>time+1) {
                LastRestore=$"another world (seed {(string)state.Attribute("WorldSeed")} / {seed}, name {(string)state.Attribute("WorldName")} / {name}, play time {(string)state.Attribute("PlayTime")} / {time:0})";
                Log.Warning("[MechDefense] MechDefense/state.xml belongs to "+LastRestore+", not restored");return;
            }
            Renumber(project,state);
            var entities=project.Element("Entities");if(entities==null){entities=new XElement("Entities",new XAttribute("NextID",1000001));project.Add(entities);}
            var taken=entities.Elements("Entity").Select(e=>(int?)e.Attribute("Id")??0).ToHashSet();int skipped=0,next=(int?)entities.Attribute("NextID")??1000001;
            var kinds=new Dictionary<string,int>();
            foreach(var e in state.Element("Entities")?.Elements("Entity")??[]) {
                int id=(int?)e.Attribute("Id")??0;if(id==0||taken.Contains(id)){skipped++;continue;}
                entities.Add(new XElement(e));taken.Add(id);pending.Add(id);next=Math.Max(next,id+1);Restored++;
                string t=(string)e.Attribute("Name")??"";string k=t.StartsWith("MDTower_")||t.StartsWith("MDTurret")?"towers":t.StartsWith("MDMech")?"mechs":"others";kinds[k]=kinds.GetValueOrDefault(k)+1;
            }
            entities.SetAttributeValue("NextID",next);
            int attrs=0;foreach(var a in state.Element("Attributes")?.Attributes()??[])if(project.Attribute(a.Name)==null){project.SetAttributeValue(a.Name,a.Value);attrs++;}
            WorldId=(string)state.Attribute("WorldId");if(string.IsNullOrEmpty(WorldId))WorldId=null;
            LastRestore=$"{LastBlocks}; restored {Restored} entities ({string.Join(", ",kinds.Select(k=>$"{k.Key} {k.Value}"))}), {attrs} world values, {skipped} skipped (id taken), saved {(string)state.Attribute("Saved")}";
            Log.Information("[MechDefense] MechDefense/state.xml: "+LastRestore);
            if(Restored>0||ItemsMoved>0)message=$"机甲防线：已从本世界的备份恢复 {kinds.GetValueOrDefault("towers")} 座防御塔、{kinds.GetValueOrDefault("mechs")} 台机甲（模组曾被移除）。"+(ItemsMoved+ItemsLost>0?$"物品编号被别的模组占用，已纠正 {ItemsMoved} 格{(ItemsLost>0?$"；有 {ItemsLost} 件机甲防线物品没能恢复（移除期间被挪动、用掉或换掉，现在是别的模组的物品，清单见 Game.log）":"")}。":"");
        } catch(Exception x){LastRestore="failed: "+x.Message;Log.Warning("[MechDefense] restoring from MechDefense/state.xml: "+x.Message+" (the world loads without it)");}
    }

    // a restored tower, once its ground is loaded: still room for it? (ComponentMDUnit.Update)
    public static bool Check(ComponentMDUnit u) {
        if(pending.Count==0||!pending.Contains(u.Entity.Id))return false;
        if(u.Tower==null){pending.Remove(u.Entity.Id);return false;}   // a mech: its body finds its own place
        var t=u.Project.FindSubsystem<SubsystemTerrain>(true);
        switch(MDTowerPlacement.TerrainFits(t,u.Body.Position,u.Tower,out var reason)) {
            case null: return false;   // its ground is not loaded yet
            case true: pending.Remove(u.Entity.Id);return false;
            default:
                pending.Remove(u.Entity.Id);TakenDown++;Log.Warning($"[MechDefense] restored {u.Tower.name} at {u.Body.Position}: {reason}; taken down as its item");
                MDTowerMaintenance.Dismantle(u,null);return true;
        }
    }
    // the player's message once the world runs (MDRobotSpawnTick)
    static double shown=-1;
    public static void Tick(Project p) {
        if(message==null)return;var pl=p.FindSubsystem<SubsystemPlayers>(true).ComponentPlayers.FirstOrDefault();if(pl==null){shown=-1;return;}
        double now=p.FindSubsystem<SubsystemTime>(true).GameTime;if(shown<0||now<shown){shown=now;return;}if(now-shown<4)return;   // after the towers' ground check
        pl.ComponentGui.DisplaySmallMessage(message+(TakenDown>0?$" 其中 {TakenDown} 座的位置被方块占住或没了地面，已变回物品掉在原地。":""),new Color(160,220,255),true,true);message=null;
    }
}
