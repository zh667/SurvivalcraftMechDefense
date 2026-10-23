using Engine;
using Game;
using GameEntitySystem;
using System.Reflection;

namespace TowerLab;

// lab --mode robots (0.9.0, docs/tasks/2026-10-20-progression-robots.md): the progression items and recipes, the robot
// enemies (models, clips, a fight against a tower, loot), a night raid on a tower cluster, the devil mech (beacon countdown,
// flight, weak channel, fight, blueprint drop) and Mercury's weapon kits; since round 3 (2026-10-21) the mod settings, the mech
// panel's options and the rifle status, the wild spawner (with a short survival run) and the robots' targets (the nearest
// tower, Mercury before towers, players only when provoked or at the defences, a tower behind a tower). Results under
// "robots" and "recipes"; screenshots ri_* (items), rb_* (robots and clips), rf_* (fight), rr_* (raid), rx_* (boss),
// rk_* (kits), rh_* (mech panel), rt_* (targets).
public partial class Lab {
    // Every tower, Mercury and progression item recipe resolves from its own grid at its own heat and player level; tier 2
    // takes alloy plates, tier 3 also fire-control chips; Mercury needs level 8 and the blueprint.
    static object RecipeChecks(IReadOnlyList<MDTowerDef> towers) {
        var deploy=BlocksManager.GetBlockIndex<MDDeploymentBlock>();var parts=BlocksManager.GetBlockIndex<MDPartBlock>();var terrain=GameManager.Project.FindSubsystem<SubsystemTerrain>(true);
        var mine=CraftingRecipesManager.Recipes.Where(r=>Terrain.ExtractContents(r.ResultValue) is int c&&(c==deploy||c==parts)).ToList();
        var rec=new Dictionary<string,object>();var bad=new List<string>();
        string[] Grid(CraftingRecipe r)=>r.Ingredients.Select(s=>s==null?null:s.Contains(':')?s:s+":0").ToArray();   // what a table builds from real items
        foreach(var r in mine) {
            int c=Terrain.ExtractContents(r.ResultValue),d=Terrain.ExtractData(r.ResultValue);
            string id=c==deploy?(MDMechType.ByData(d)?.Id.ToLowerInvariant()??MDTowerLibrary.ByData(d)?.id??"tower?"+d):"part"+d+(r.RequiredHeatLevel>0?"_furnace":"");
            var found=CraftingRecipesManager.FindMatchingRecipe(terrain,Grid(r),r.RequiredHeatLevel,Math.Max(1,r.RequiredPlayerLevel));
            bool ok=found==r;if(!ok)bad.Add($"{id}: grid resolves to {(found==null?"nothing":found.ResultValue+" "+found.Message)}");
            rec[id+(rec.ContainsKey(id)?"_2":"")]=new{grid=string.Join(" | ",Enumerable.Range(0,3).Select(y=>string.Join(",",Enumerable.Range(0,3).Select(x=>r.Ingredients[x+y*3]??"-")))),heat=r.RequiredHeatLevel,level=r.RequiredPlayerLevel,count=r.ResultCount,description=r.Description,resolves=ok};
        }
        foreach(var t in towers)if(mine.Count(r=>Terrain.ExtractContents(r.ResultValue)==deploy&&Terrain.ExtractData(r.ResultValue)==t.data)!=1)bad.Add(t.id+": not exactly one recipe");
        var tier2=new[]{3,6,14,16,18,19,33,36,39,42};var tier3=new[]{4,7,8,13,15,17,34,37,40,43};   // 2D tower family Ⅱ, Ⅲ: 转轮机枪塔 33/34, 冰冻塔 36/37, 火焰塔 39/40, 导弹塔 42/43
        foreach(var r in mine.Where(r=>Terrain.ExtractContents(r.ResultValue)==deploy)) {
            int d=Terrain.ExtractData(r.ResultValue);
            if(tier2.Contains(d)&&r.Ingredients.Count(s=>s=="mdpart:1")!=3)bad.Add($"tier 2 tower {d}: not 3 alloy plates");
            if(tier3.Contains(d)&&r.Ingredients.Count(s=>s=="mdpart:2")!=2)bad.Add($"tier 3 tower {d}: not 2 chips");
            if(!tier2.Contains(d)&&!tier3.Contains(d)&&MDMechType.ByData(d)==null&&r.Ingredients.Any(s=>s?.StartsWith("mdpart")==true))bad.Add($"tier 1 tower {d} uses a progression item");
        }
        var merc=mine.First(r=>Terrain.ExtractContents(r.ResultValue)==deploy&&Terrain.ExtractData(r.ResultValue)==1);
        var at7=CraftingRecipesManager.FindMatchingRecipe(terrain,Grid(merc),0,7);
        bool blocked7=at7!=null&&at7!=merc&&!string.IsNullOrEmpty(at7.Message);
        if(merc.RequiredPlayerLevel!=8||!blocked7)bad.Add($"mercury: level {merc.RequiredPlayerLevel}, at level 7 {(at7==merc?"craftable":at7?.Message)}");
        if(!merc.Ingredients.Contains("mdpart:4")||merc.RemainsValue!=0)bad.Add("mercury: blueprint missing or given back");
        // the hero mechs (round 5): 复仇女神 from Mercury at level 12 with one blueprint, 轩辕十四 from 复仇女神 at level 16 with two
        foreach(var (data,level,from,prints) in new[]{(30,12,"mddeploy:1",1),(31,16,"mddeploy:30",2)}) {
            var hr=mine.FirstOrDefault(r=>Terrain.ExtractContents(r.ResultValue)==deploy&&Terrain.ExtractData(r.ResultValue)==data);
            if(hr==null){bad.Add($"hero {data}: no recipe");continue;}
            var below=CraftingRecipesManager.FindMatchingRecipe(terrain,Grid(hr),0,level-1);
            if(hr.RequiredPlayerLevel!=level||below==hr||!hr.Ingredients.Contains(from)||hr.Ingredients.Count(s=>s=="mdpart:4")!=prints)bad.Add($"hero {data}: level {hr.RequiredPlayerLevel}, below {(below==hr?"craftable":below?.Message)}");
        }
        int expect=towers.Count+MDMechType.All.Length+8;
        if(mine.Count!=expect)bad.Add($"{mine.Count} recipes, want {expect}");
        if(bad.Count>0)throw new Exception("recipes: "+string.Join("; ",bad));
        return new{count=mine.Count,mercuryAtLevel7=at7?.Message,rec};
    }

    // mode "robots" runs every part; "robots-<part>" (items, gallery, fight, raid, boss, summons, kits, settings, hud, spawns,
    // targets) one of them, after the recipe checks
    static bool Part(string name)=>mode=="robots"||mode=="robots-"+name;
    static IEnumerable<bool> Robots() {
        Remove(placed.Select(p=>p.unit));placed.Clear();while(Wait(.5))yield return true;
        var r=new Dictionary<string,object>();results["robots"]=r;var proj=GameManager.Project;var pl=player.ComponentPlayer;var w=MDTowerWorld.For(proj);
        var terrain=proj.FindSubsystem<SubsystemTerrain>(true);
        results["recipes"]=RecipeChecks(MDTowerLibrary.All);
        ComponentMDRobot Spawn(string t,Vector3 at,float yaw=0) {
            var e=DatabaseManager.CreateEntity(proj,t,true);var b=e.FindComponent<ComponentBody>(true);b.Position=at;b.Rotation=Quaternion.CreateFromAxisAngle(Vector3.UnitY,yaw);
            e.FindComponent<ComponentSpawn>(true).SpawnDuration=0;proj.AddEntity(e);return e.FindComponent<ComponentMDRobot>(true);
        }
        void Kill(IEnumerable<ComponentMDRobot> rs){foreach(var x in rs.ToList())if(x.Entity.IsAddedToProject)proj.RemoveEntity(x.Entity,true);}
        List<(int value,int count)> Pickables(Vector3 at,float radius)=>proj.FindSubsystem<SubsystemPickables>(true).Pickables.Where(p=>Vector3.Distance(p.Position,at)<radius).GroupBy(p=>p.Value).Select(g=>(g.Key,g.Sum(p=>p.Count))).ToList();
        string Names(List<(int value,int count)> l)=>string.Join(", ",l.Select(x=>$"{BlocksManager.Blocks[Terrain.ExtractContents(x.value)].GetDisplayName(terrain,x.value)} ×{x.count}"));
        void ClearPickables(){foreach(var p in proj.FindSubsystem<SubsystemPickables>(true).Pickables.ToList())p.ToRemove=true;}

        // ---- items: names, stacks, creative values; on the ground and in the hand
        if(Part("items")) {
            var block=(MDPartBlock)BlocksManager.Blocks[BlocksManager.GetBlockIndex<MDPartBlock>()];var it=new List<string>();
            foreach(var v in block.GetCreativeValues())it.Add($"{block.GetDisplayName(terrain,v)} (stack {block.GetMaxStacking(v)}, {block.GetCategory(v)}, useable {block.IsUseable_(v)})");
            r["items"]=it;if(it.Count!=MDPartBlock.Count)throw new Exception("items: "+it.Count);
            // 3D item models (tools/build_part_models.py): on the ground by day and by night (the core band, beacon lamp and Mk3
            // connector glow), every item in the first-person hand, a few in third person, the hotbar with GUI (slot icons)
            var pick=proj.FindSubsystem<SubsystemPickables>(true);var gi=proj.FindSubsystem<SubsystemGameInfo>(true);
            for(int i=0;i<MDPartBlock.Count;i++)pick.AddPickable(Terrain.MakeBlockValue(block.BlockIndex,0,i),1,Home+new Vector3(i*.9f-3.6f,.2f,-3),Vector3.Zero,null);
            while(Wait(1.5))yield return true;
            foreach(var x in Hold(Home+new Vector3(0,2.4f,-.2f),Home+new Vector3(0,0,-3)))yield return x;Shot("ri_ground");yield return true;
            foreach(var x in Hold(Home+new Vector3(-1.5f,1.2f,-1.2f),Home+new Vector3(.5f,0,-3)))yield return x;Shot("ri_ground_low");yield return true;
            gi.WorldSettings.TimeOfDayMode=TimeOfDayMode.Night;foreach(var x in Hold(Home+new Vector3(0,2.4f,-.2f),Home+new Vector3(0,0,-3),20))yield return x;Shot("ri_ground_night");yield return true;
            gi.WorldSettings.TimeOfDayMode=TimeOfDayMode.Day;
            ClearPickables();
            var inv=pl.ComponentMiner.Inventory;pl.GameWidget.ActiveCamera=pl.GameWidget.FindCamera<FppCamera>();
            for(int i=0;i<MDPartBlock.Count;i++) {
                inv.RemoveSlotItems(0,inv.GetSlotCount(0));inv.AddSlotItems(0,Terrain.MakeBlockValue(block.BlockIndex,0,i),1);inv.ActiveSlotIndex=0;
                foreach(var x in During(1.2,()=>{}))yield return x;ScreenCaptureManager.Capture(1280,720,$"ri_hand_{i}.png");yield return true;
            }
            foreach(var (name,cls) in new[]{("ingot","IronIngotBlock"),("battery","BatteryBlock"),("diamond","DiamondBlock")}) {   // vanilla references, same camera
                inv.RemoveSlotItems(0,inv.GetSlotCount(0));inv.AddSlotItems(0,Terrain.MakeBlockValue(BlocksManager.GetBlockIndex(cls,true),0,0),1);
                foreach(var x in During(1.2,()=>{}))yield return x;ScreenCaptureManager.Capture(1280,720,$"ri_hand_vanilla_{name}.png");yield return true;
            }
            pl.GameWidget.ActiveCamera=pl.GameWidget.FindCamera<TppCamera>();
            foreach(int i in new[]{1,3,5}){inv.RemoveSlotItems(0,inv.GetSlotCount(0));inv.AddSlotItems(0,Terrain.MakeBlockValue(block.BlockIndex,0,i),1);foreach(var x in During(1,()=>{}))yield return x;ScreenCaptureManager.Capture(1280,720,$"ri_third_{i}.png");yield return true;}
            pl.GameWidget.ActiveCamera=pl.GameWidget.FindCamera<FppCamera>();inv.RemoveSlotItems(0,inv.GetSlotCount(0));
            SettingsManager.ShowGuiInScreenshots=true;var cinv=pl.Entity.FindComponent<ComponentCreativeInventory>(true);
            var pages=new[]{block.GetCreativeValues().ToList(),((MDSummonBlock)BlocksManager.Blocks[BlocksManager.GetBlockIndex<MDSummonBlock>()]).GetCreativeValues().Take(9).ToList()};
            for(int page=0;page<pages.Length;page++) {
                for(int i=0;i<9;i++){cinv.RemoveSlotItems(i,cinv.GetSlotCount(i));if(i<pages[page].Count)cinv.AddSlotItems(i,pages[page][i],1);}
                foreach(var x in Hold(Home+new Vector3(-8,7,-12),Home+new Vector3(3,1,-1)))yield return x;Shot($"ri_hotbar_{page}");yield return true;
            }
            for(int i=0;i<9;i++)cinv.RemoveSlotItems(i,cinv.GetSlotCount(i));SettingsManager.ShowGuiInScreenshots=false;
        }
        // ---- every robot template: model, joints, loot; a gallery and each unit's clips
        if(Part("gallery")) {
        var tpl=MDRobots.Normal.Concat(MDRobots.Elites).ToList();var loads=new Dictionary<string,object>();var gallery=new List<ComponentMDRobot>();
        for(int i=0;i<tpl.Count;i++)gallery.Add(Spawn(tpl[i],origin+new Vector3(i*4.5f-4,0,12),MathF.PI));
        foreach(var g in gallery){g.ForceClip="idle";g.ForceTime=0;}
        while(Wait(1.5))yield return true;
        for(int i=0;i<gallery.Count;i++) {
            var g=gallery[i];var m=g.Entity.FindComponent<ComponentMDRobotModel>(true);var loot=g.Entity.FindComponent<ComponentLoot>(true);
            loads[tpl[i]]=new{kind=g.Kind.Id,model=m.Model==null?"missing":$"{m.Model.Meshes.Count} meshes, {m.Model.Bones.Count} bones",missingJoints=g.Def.joints.Count(j=>m.Model?.FindBone(j.name,false)==null),
                clips=string.Join(",",g.Def.clips.Select(c=>c.name)),box=g.Body.BoxSize.ToString(),resilience=g.Health.AttackResilience,
                loot=string.Join("; ",loot.m_lootList.Select(l=>$"{BlocksManager.Blocks[Terrain.ExtractContents(l.Value)].GetDisplayName(terrain,l.Value)} {l.MinCount}-{l.MaxCount} p{l.Probability}")),category=g.Creature.Category.ToString()};
            if(m.Model==null||g.Def.joints.Any(j=>m.Model.FindBone(j.name,false)==null))throw new Exception("robot model "+tpl[i]);
        }
        r["templates"]=loads;r["modelError"]=ComponentMDRobotModel.LastError;
        var gc=origin+new Vector3(14,1.6f,12);
        foreach(var x in Hold(gc+new Vector3(-4,5,-26),gc))yield return x;Shot("rb_gallery");yield return true;
        foreach(var x in Hold(gc+new Vector3(18,4,-14),gc))yield return x;Shot("rb_gallery_side");yield return true;
        foreach(var unit in new[]{0,3,6}) {   // RD05, RD06, RD07: every clip at start / middle / end
            var g=gallery[unit];var c=g.Body.Position+Vector3.UnitY*1.6f;
            foreach(var clip in g.Def.clips)for(int k=0;k<3;k++){g.ForceClip=clip.name;g.ForceTime=g.Def.Seconds(clip)*k/2f;foreach(var x in Hold(c+new Vector3(5,2,-6),c,3))yield return x;Shot($"rb_{g.Kind.Id}_{clip.name}_{k}");yield return true;}
            g.ForceClip="idle";g.ForceTime=0;
        }
        Kill(gallery);while(Wait(.5))yield return true;
        }

        // ---- a normal robot raids two tier-1 gun towers (since 2026-10-21 it beats one): walks into range, fires, the towers kill it, it drops scrap
        if(Part("fight")) {
            var f=new Dictionary<string,object>();r["fight"]=f;var gun=MDTowerLibrary.All.First(t=>t.data==0);
            pl.ComponentBody.Position=Home+new Vector3(10,0,10);while(Wait(.3))yield return true;   // not where the tower goes (a tower placed on the player crushes it)
            var tower=Place(gun,Home,0);var tower2=Place(gun,Home+new Vector3(5,0,0),0);var start=Home+new Vector3(0,0,-38);var rb=Spawn("MDRobot05",start,0);rb.Raider=true;rb.RaidGoal=Home;
            float towerMin=1,closest=99;double firstShot=-1,t0f=Time.RealTime;Vector3 diedAt=Vector3.Zero;bool died=false;int frames=0,shots=0;var track=new List<string>();
            foreach(var x in During(45,()=>{
                double el=Time.RealTime-t0f;if(shots<4&&el>=4+shots*7){var rp=rb.Body.Position;track.Add($"{el:0}s robot {Vector3.Distance(rp,Home):0.0} m, hp {rb.Health.Health:0.00}, shots {rb.ShotsFired}, tower {tower.Damage.Hitpoints:0.00}, clip {rb.Anim.Base?.name}");Cam(Home+new Vector3(14,9,-12),(Home+rp)/2+Vector3.UnitY*1.5f);ScreenCaptureManager.Capture(1280,720,$"rf_mid_{shots}.png");shots++;}
                towerMin=Math.Min(towerMin,Math.Min(tower.Damage.Hitpoints,tower2.Damage.Hitpoints));if(rb.Entity.IsAddedToProject){closest=Math.Min(closest,Vector3.Distance(rb.Body.Position,Home));if(rb.ShotsFired>0&&firstShot<0)firstShot=Time.RealTime-t0f;}
                if(!died&&rb.Health.Health<=0){died=true;diedAt=rb.Body.Position;}
                if(++frames%40==0)Cam(Home+new Vector3(14,9,-12),(Home+rb.Body.Position)/2+Vector3.UnitY*1.5f);
            }))yield return x;
            Cam(Home+new Vector3(14,9,-12),Home+Vector3.UnitY*1.5f);Shot("rf_after");yield return true;
            f["track"]=track;f["robotPosEnd"]=rb.Body.Position.ToString();f["robotShots"]=rb.ShotsFired;f["firstShotAfter"]=firstShot;f["closest"]=closest;f["towerHealthMin"]=towerMin;f["robotDied"]=died;f["robotHealth"]=rb.Health.Health;
            foreach(var x in During(4,()=>{}))yield return x;
            var drops=died?Pickables(diedAt,6):[];f["drops"]=Names(drops);
            if(rb.ShotsFired==0||towerMin>=1||!died||!drops.Any(d=>MDPartBlock.Of(d.value)==MDPartBlock.Kind.Scrap))throw new Exception("fight: "+string.Join(", ",f.Select(kv=>kv.Key+"="+kv.Value)));
            ClearPickables();Remove([tower,tower2]);Kill([rb]);while(Wait(.5))yield return true;
        }

        // ---- a raid on a strong cluster (day 12): four towers plus five tier-3 ones = defence 21: elites, and a second wave after 60 s
        if(Part("raid")) {
            var q=new Dictionary<string,object>();r["raid"]=q;var defs=MDTowerLibrary.All;
            var cluster=new[]{Place(defs.First(t=>t.data==0),Home+new Vector3(-6,0,0),0),Place(defs.First(t=>t.data==5),Home+new Vector3(6,0,0),0),
                              Place(defs.First(t=>t.data==3),Home+new Vector3(0,0,-6),0),Place(defs.First(t=>t.data==14),Home+new Vector3(0,0,6),0),
                              Place(defs.First(t=>t.data==4),Home+new Vector3(-6,0,-6),0),Place(defs.First(t=>t.data==4),Home+new Vector3(6,0,-6),0),Place(defs.First(t=>t.data==4),Home+new Vector3(-6,0,6),0),
                              Place(defs.First(t=>t.data==4),Home+new Vector3(6,0,6),0),Place(defs.First(t=>t.data==4),Home+new Vector3(-12,0,0),0)};
            pl.ComponentBody.Position=Home+new Vector3(0,0,14);while(Wait(1))yield return true;
            int sent=MDRaids.Raid(proj,12,new System.Random(5));q["sent"]=sent;q["defence"]=MDRaids.LastDefence;q["elites"]=MDRaids.LastElites;q["secondWave"]=MDRaids.LastSecondWave;double raidAt=Time.RealTime;
            var raiders=proj.Entities.Select(e=>e.FindComponent<ComponentMDRobot>()).Where(x=>x!=null&&x.Raider).ToList();
            q["units"]=string.Join(",",raiders.Select(x=>x.Kind.Id));q["startDistance"]=string.Join(" ",raiders.Select(x=>$"{Vector3.Distance(x.Body.Position,Home):0}"));
            foreach(var x in During(1,()=>Cam(Home+new Vector3(0,60,-.1f),Home)))yield return x;Shot("rr_start_top");yield return true;
            foreach(var x in During(28,()=>{}))yield return x;
            q["distanceAfter28s"]=string.Join(" ",raiders.Select(x=>x.Entity.IsAddedToProject&&x.Health.Health>0?$"{Vector3.Distance(x.Body.Position,Home):0}":"dead"));
            q["shots"]=string.Join(" ",raiders.Select(x=>x.ShotsFired));q["towers"]=string.Join(" ",cluster.Select(t=>$"{t.Damage.Hitpoints:0.00}"));
            int vanished=raiders.Count(x=>!x.Entity.IsAddedToProject&&x.Health.Health>0);q["vanishedAlive"]=vanished;   // packed away by the game's despawn (round 4)
            if(vanished>0)throw new Exception($"raid: {vanished} raiders vanished alive");
            Cam(Home+new Vector3(20,22,-30),Home);Shot("rr_fight");yield return true;
            int before=proj.Entities.Count(e=>e.FindComponent<ComponentMDRobot>() is {Raider:true});while(MDRaids.SecondWavesSent==0&&Time.RealTime-raidAt<90)yield return true;q["secondWaveAfter"]=Time.RealTime-raidAt;foreach(var x in During(4,()=>{}))yield return x;   // 60 s of game time (~12 % slower than real time in the lab)
            var all=proj.Entities.Select(e=>e.FindComponent<ComponentMDRobot>()).Where(x=>x!=null&&x.Raider).ToList();q["raidersAfterSecondWave"]=$"{before} -> {all.Count}";q["towersAfterSecondWave"]=string.Join(" ",cluster.Select(t=>$"{t.Damage.Hitpoints:0.00}"));raiders=all.Union(raiders).ToList();
            Cam(Home+new Vector3(20,22,-30),Home);Shot("rr_second_wave");yield return true;
            q["secondWaveSent"]=MDRaids.SecondWavesSent;
            if(MDRaids.LastDefence!=21||MDRaids.LastSecondWave==0||MDRaids.SecondWavesSent==0||sent<12||!raiders.Any(x=>x.Kind.Elite))throw new Exception("raid: "+string.Join(", ",q.Select(kv=>kv.Key+"="+kv.Value)));
            List<ComponentMDRobot> AllRobots()=>proj.Entities.Select(e=>e.FindComponent<ComponentMDRobot>()).Where(x=>x!=null).ToList();
            Kill(AllRobots());Remove(cluster);ClearPickables();while(Wait(.5))yield return true;
            // the raid size setting: four tier-1 towers (defence 4, no second wave), x1 and x2 in the same frame (same defence)
            var gun0=defs.First(t=>t.data==0);var small=new[]{Place(gun0,Home+new Vector3(-6,0,0),0),Place(gun0,Home+new Vector3(6,0,0),0),Place(gun0,Home+new Vector3(0,0,-6),0),Place(gun0,Home+new Vector3(0,0,6),0)};
            while(Wait(1))yield return true;
            int a1=MDRaids.Raid(proj,12,new System.Random(7)),p1=MDRaids.LastPlanned;Kill(AllRobots());
            MDSettings.RaidSize=2;int a2=MDRaids.Raid(proj,12,new System.Random(7)),p2=MDRaids.LastPlanned;Kill(AllRobots());MDSettings.Defaults();
            q["raidSize"]=$"defence {MDRaids.LastDefence}: x1 plans {p1} (sent {a1}), x2 plans {p2} (sent {a2})";
            if(p2!=2*p1||a2<p2-4)throw new Exception("raid size: "+q["raidSize"]);
            Remove(small);ClearPickables();while(Wait(.5))yield return true;
        }

        // ---- the devil mech: beacon countdown, arrival, weak channel, a fight against tier-3 towers, the blueprint
        if(Part("boss")) {
            var x2=new Dictionary<string,object>();r["boss"]=x2;var defs=MDTowerLibrary.All;
            pl.ComponentBody.Position=Home+new Vector3(0,0,18);pl.ComponentBody.Rotation=Quaternion.Identity;while(Wait(.5))yield return true;
            var inv=pl.ComponentMiner.Inventory;inv.RemoveSlotItems(0,inv.GetSlotCount(0));inv.AddSlotItems(0,MDPartBlock.Value(MDPartBlock.Kind.Beacon),1);inv.ActiveSlotIndex=0;
            var foot=pl.ComponentBody.Position;pl.ComponentMiner.Use(new Ray3(foot+new Vector3(0,1.6f,0),Vector3.Normalize(new Vector3(0,-1.6f,-6f))));
            x2["countdown"]=MDBoss.Counting(proj);x2["beaconUsedUp"]=inv.GetSlotCount(0)==0;
            if(!MDBoss.Counting(proj))throw new Exception("beacon did not start");
            var towers=new[]{Place(defs.First(t=>t.data==4),Home+new Vector3(-7,0,4),0),Place(defs.First(t=>t.data==15),Home+new Vector3(7,0,4),0),Place(defs.First(t=>t.data==8),Home+new Vector3(0,0,10),0)};
            double tb=Time.RealTime;ComponentMDRobot boss=null;
            foreach(var x in During(34,()=>boss??=MDBoss.Alive(proj)))yield return x;
            x2["arrivedAfter"]=boss==null?-1:Time.RealTime-tb;if(boss==null)throw new Exception("boss did not come");
            x2["flyingAfterArrival"]=boss.Creature.ComponentLocomotion.m_flying;x2["armour"]=boss.Health.AttackResilience;x2["escortSize"]=boss.Boss.EscortSize;
            if(boss.Health.AttackResilience!=39000)throw new Exception("boss armour "+boss.Health.AttackResilience+" (want 30000 + 3 tier-3 towers x 3000)");
            var hover=new List<string>();double th=Time.RealTime;int hs=0;
            foreach(var x in During(8,()=>{if(Time.RealTime-th>=hs*.5){hs++;var l=boss.Creature.ComponentLocomotion;hover.Add($"{Time.RealTime-th:0.0}s y {boss.Body.Position.Y-Home.Y:0.00} want {boss.Boss.LastWant.Y-Home.Y:0.00} ask {boss.Boss.LastFly} vel {boss.Body.Velocity} grav {boss.Body.IsGravityEnabled} stand {boss.Body.StandingOnValue.HasValue} fly {l.FlySpeed:0.0} order {l.LastFlyOrder} flying {l.m_flying} imm {boss.Body.ImmersionFactor:0.00} crushed {boss.Body.CrushedTime:0.00} stun {l.StunTime:0.00} onBody {boss.Body.StandingOnBody?.Entity.FindComponent<ComponentCreature>()?.DisplayName} charge {boss.Boss.Charge:0.0}/{boss.Boss.Slash:0.0} target {boss.Target?.Entity.FindComponent<ComponentMDUnit>()?.TowerId}");}}))yield return x;
            x2["hover"]=hover;var ground=Home.Y;x2["altitude"]=boss.Body.Position.Y-ground;x2["flying"]=boss.Creature.ComponentLocomotion.m_flying;
            if(boss.Body.Position.Y-ground<1.5f)throw new Exception($"boss does not hover: {boss.Body.Position.Y-ground:0.00} m");x2["distanceToBeacon"]=Vector3.Distance(new Vector3(boss.Body.Position.X,0,boss.Body.Position.Z),new Vector3(Home.X,0,Home.Z+12));
            Cam(Home+new Vector3(26,14,-26),boss.Body.Position);Shot("rx_arrived");yield return true;
            // weak channel: the same hit in the weak channel and in another one
            boss.Boss.WeakTimer=999;string weak=MDBossState.Channels[boss.Boss.Weak],other=MDBossState.Channels[(boss.Boss.Weak+1)%3];
            float h0=boss.Health.Health;ComponentMiner.AttackBody(new MDTowerAttackment(boss.Entity,towers[0].Entity,boss.Body.Position,Vector3.UnitY,900){Channel=weak});float dWeak=h0-boss.Health.Health;
            h0=boss.Health.Health;ComponentMiner.AttackBody(new MDTowerAttackment(boss.Entity,towers[0].Entity,boss.Body.Position,Vector3.UnitY,900){Channel=other});float dOther=h0-boss.Health.Health;
            x2["weak"]=$"{weak}: {dWeak*boss.Health.AttackResilience:0} of 900, {other}: {dOther*boss.Health.AttackResilience:0} of 900";
            if(Math.Abs(dOther/dWeak-MDBossState.OtherFactor)>.02f)throw new Exception("weak channel: "+x2["weak"]);
            boss.Boss.WeakTimer=3;float bh=boss.Health.Health;float lowest=99;int weakChanges=0,lastWeak=boss.Boss.Weak;
            var clipsSeen=new HashSet<string>();
            foreach(var x in During(30,()=>{lowest=Math.Min(lowest,boss.Body.Position.Y-Home.Y);if(boss.Boss.Weak!=lastWeak){weakChanges++;lastWeak=boss.Boss.Weak;}clipsSeen.Add(boss.Anim.Base.name);}))yield return x;
            x2["baseClips"]=string.Join(",",clipsSeen);if(clipsSeen.Overlaps(new[]{"fly_forward","strafe_left","strafe_right"}))throw new Exception("boss pedals: "+x2["baseClips"]);
            boss.Health.Health=Math.Min(boss.Health.Health,.69f);foreach(var x in During(1,()=>{}))yield return x;x2["escortsAt69"]=boss.Boss.EscortsSent;
            if(boss.Boss.EscortsSent<2)throw new Exception("no escort at 69 %");
            x2["attacks"]=$"volleys {boss.Boss.Volleys}, sweeps {boss.Boss.Sweeps}, charges {boss.Boss.Charges}";x2["lowestIn30s"]=lowest;x2["weakChanges"]=weakChanges;
            if(boss.Boss.Volleys==0||boss.Boss.Sweeps==0||boss.Boss.Charges==0||weakChanges==0)throw new Exception("boss attacks: "+x2["attacks"]+", weak changes "+weakChanges);
            x2["healthLostIn30s"]=(bh-boss.Health.Health)*boss.Health.AttackResilience;x2["bossShots"]=boss.ShotsFired;x2["towers"]=string.Join(" ",towers.Select(t=>$"{t.Damage.Hitpoints:0.00}"));
            x2["weakNow"]=MDBossState.Channels[boss.Boss.Weak];
            Cam(Home+new Vector3(-24,16,-20),boss.Body.Position);Shot("rx_fight");yield return true;
            // finish it: it falls, explodes and drops the blueprint
            boss.Health.Injure(2f,null,true,"lab");var died=Time.RealTime;
            foreach(var x in During(7,()=>{}))yield return x;
            var drops=Pickables(boss.Body.Position,10);x2["drops"]=Names(drops);x2["corpseGone"]=!boss.Entity.IsAddedToProject;
            Cam(boss.Body.Position+new Vector3(8,6,-8),boss.Body.Position);Shot("rx_drops");yield return true;
            if(!drops.Any(d=>MDPartBlock.Of(d.value)==MDPartBlock.Kind.Blueprint))throw new Exception("boss: no blueprint, "+x2["drops"]);
            Remove(towers);ClearPickables();Kill([boss]);while(Wait(.5))yield return true;
        }

        Kill(proj.Entities.Select(e=>e.FindComponent<ComponentMDRobot>()).Where(x=>x!=null).ToList());ClearPickables();while(Wait(.5))yield return true;   // the escorts
        // ---- creative summons: every robot template placed from its item, facing the player; the devil mech too
        if(Part("summons")) {
            var s=new List<string>();r["summons"]=s;var inv=pl.ComponentMiner.Inventory;var placedRobots=new List<ComponentMDRobot>();
            var block=(MDSummonBlock)BlocksManager.Blocks[BlocksManager.GetBlockIndex<MDSummonBlock>()];
            if(block.GetCreativeValues().Count()!=MDSummonBlock.Count)throw new Exception("summons in the creative list: "+block.GetCreativeValues().Count());
            for(int i=0;i<MDSummonBlock.Count;i++) {
                var spot=Home+new Vector3(-27+i*6,0,-22);pl.ComponentBody.Position=spot+new Vector3(0,0,5);pl.ComponentBody.Rotation=Quaternion.Identity;pl.ComponentBody.Velocity=Vector3.Zero;
                foreach(var x in During(.3,()=>{}))yield return x;
                int v=MDSummonBlock.Value(i);inv.RemoveSlotItems(0,inv.GetSlotCount(0));inv.AddSlotItems(0,v,1);inv.ActiveSlotIndex=0;
                var before=proj.Entities.Select(e=>e.FindComponent<ComponentMDRobot>()).Where(x=>x!=null).ToHashSet();
                pl.ComponentMiner.Use(new Ray3(pl.ComponentBody.Position+new Vector3(0,1.6f,0),Vector3.Normalize(new Vector3(0,-1.6f,-5f))));
                var made=proj.Entities.Select(e=>e.FindComponent<ComponentMDRobot>()).FirstOrDefault(x=>x!=null&&!before.Contains(x));
                s.Add($"{block.GetDisplayName(terrain,v)} -> {(made==null?"nothing":made.Kind.Id+" at "+Vector3.Distance(made.Body.Position,spot).ToString("0.0")+" m, armour "+made.Health.AttackResilience)}");
                if(made==null)throw new Exception("summon "+i+": "+s[^1]);placedRobots.Add(made);
            }
            Cam(Home+new Vector3(0,14,-6),Home+new Vector3(0,2,-22));Shot("rs_summons");yield return true;
            Kill(placedRobots);while(Wait(.5))yield return true;
        }
        // ---- Mercury's weapon kits: Mk1 before Mk2, kept through taking it down and deploying it again
        if(Part("kits")) {
            var k=new Dictionary<string,object>();r["kits"]=k;var inv=pl.ComponentMiner.Inventory;
            pl.ComponentBody.Position=Home;pl.ComponentBody.Rotation=Quaternion.Identity;pl.ComponentBody.Velocity=Vector3.Zero;while(Wait(.5))yield return true;
            inv.RemoveSlotItems(0,inv.GetSlotCount(0));inv.AddSlotItems(0,MDDeploymentBlock.MechValue(1),1);inv.ActiveSlotIndex=0;
            var foot=pl.ComponentBody.Position;pl.ComponentMiner.Use(new Ray3(foot+new Vector3(0,1.6f,0),Vector3.Normalize(new Vector3(0,-1.6f,-5f))));
            var u=proj.Entities.Select(e=>e.FindComponent<ComponentMDUnit>()).First(x=>x?.Mech!=null);
            var steps=new List<string>{MDKits.Install(u,2,null)+" -> "+u.Mech.Kit,MDKits.Install(u,1,null)+" -> "+u.Mech.Kit,MDKits.Install(u,1,null)+" -> "+u.Mech.Kit,MDKits.Install(u,2,null)+" -> "+u.Mech.Kit};
            k["install"]=steps;if(u.Mech.Kit!=2)throw new Exception("kits: "+string.Join(" | ",steps));
            var dummy=Dummy(u.Body.Position+new Vector3(0,0,-10));dummy.C.ComponentHealth.AttackResilience=1e4f;while(Wait(.3))yield return true;
            float hd=dummy.C.ComponentHealth.Health;MDTowerBrain.Hit(u,dummy.Body,1000,MDCombat.Center(dummy.Body),Vector3.UnitZ);float dealt=(hd-dummy.C.ComponentHealth.Health)*1e4f;
            k["hit1000WithMk2"]=dealt;Remove([dummy]);
            // the Neorxna info panel (the user's item / entity info mod): Mercury by its name, not 已移除的旧单位 (user, 2026-10-21)
            var panelType=AppDomain.CurrentDomain.GetAssemblies().Select(a=>a.GetType("Neorxna.NUI.NUInfoPanelWidget",false)).FirstOrDefault(t=>t!=null);
            if(panelType==null)k["neorxna"]="not installed";
            else {
                MDNeorxnaCompatibility.Install();var panel=Activator.CreateInstance(panelType);
                var tw=Place(MDTowerLibrary.All.First(t=>t.data==0),u.Body.Position+new Vector3(8,0,0),0);
                panelType.GetMethod("SetInfo_Entity").Invoke(panel,[tw.Entity]);panelType.GetMethod("SetInfo_Entity").Invoke(panel,[u.Entity]);
                string name=((LabelWidget)panelType.GetField("m_name").GetValue(panel)).Text;
                var texts=(panel as ContainerWidget)?.AllChildren.OfType<LabelWidget>().Select(l=>l.Text).Where(x=>!string.IsNullOrWhiteSpace(x)).ToList()??[];
                k["neorxna"]=$"name {name}; labels: "+string.Join(" | ",texts);Remove([tw]);
                if(name!="水星机甲"||!texts.Any(x=>x.Contains("武器改装 Mk2"))||texts.Any(x=>x.Contains("已移除")))throw new Exception("Neorxna on Mercury: "+k["neorxna"]);
            }
            int item=MDTowerMaintenance.Dismantle(u,null);k["item"]=$"{BlocksManager.Blocks[Terrain.ExtractContents(item)].GetDisplayName(terrain,item)}, kit {MDDeploymentBlock.Kit(item)}";
            ClearPickables();inv.RemoveSlotItems(0,inv.GetSlotCount(0));inv.AddSlotItems(0,item,1);while(Wait(.5))yield return true;
            pl.ComponentMiner.Use(new Ray3(foot+new Vector3(0,1.6f,0),Vector3.Normalize(new Vector3(0,-1.6f,-5f))));
            var u2=proj.Entities.Select(e=>e.FindComponent<ComponentMDUnit>()).First(x=>x?.Mech!=null);k["redeployedKit"]=u2.Mech.Kit;
            if(MDDeploymentBlock.Kit(item)!=2||u2.Mech.Kit!=2||Math.Abs(dealt-1300)>15)throw new Exception("kits: "+string.Join(", ",k.Select(kv=>kv.Key+"="+kv.Value)));
            proj.RemoveEntity(u2.Entity,true);
        }
        ComponentMDUnit Deploy(Vector3 standAt,MDMechType type=null,float hitpoints=1,int kit=0) {   // a mech from its item, 5 m ahead of the player (facing -Z)
            var inv=pl.ComponentMiner.Inventory;pl.ComponentBody.Position=standAt;pl.ComponentBody.Rotation=Quaternion.Identity;pl.ComponentBody.Velocity=Vector3.Zero;
            var before=proj.Entities.Select(e=>e.FindComponent<ComponentMDUnit>()).Where(x=>x?.Mech!=null).ToHashSet();
            inv.RemoveSlotItems(0,inv.GetSlotCount(0));inv.AddSlotItems(0,MDDeploymentBlock.MechValue(hitpoints,kit,type),1);inv.ActiveSlotIndex=0;
            pl.ComponentMiner.Use(new Ray3(standAt+new Vector3(0,1.6f,0),Vector3.Normalize(new Vector3(0,-1.6f,-5f))));
            return proj.Entities.Select(e=>e.FindComponent<ComponentMDUnit>()).FirstOrDefault(x=>x?.Mech!=null&&!before.Contains(x))??throw new Exception((type??MDMechType.Mercury).Name+" did not deploy");
        }
        var pkg=MDSettings.Package;var gameInfo=proj.FindSubsystem<SubsystemGameInfo>(true);
        // ---- mod settings (round 4, user: one page like the CS weapons mod, the panel adjusted on a game page): the game's
        // "模组设置" has one entry for this mod, which opens the mod's own page; Save writes ModSettings.xml and a reload reads it
        // back; the HUD editor opens from the page, edits a copy, and Save stores the place. Screenshots rs_* of both pages.
        if(Part("settings")) {
            var s=new Dictionary<string,object>();r["settings"]=s;MDSettings.Defaults();
            var pages=ModSettingsManager.GetPages(pkg)??throw new Exception("settings: no pages registered");
            s["pages"]=pages.Select(pg=>$"{pg.Id} {pg.Name}: "+string.Join(", ",pg.Items.OfType<ModSettingItem>().Select(i=>$"{i.Id} {i.Type.Name} {i.WidgetType?.Name}"))).ToList();
            if(pages.Count!=1||pages[0].Items.Count!=1||pages[0].Items.OfType<ModSettingItem>().First().WidgetType!=typeof(MDSettingsEntryWidget))throw new Exception("settings: "+string.Join(" | ",(List<string>)s["pages"]));
            SettingsManager.ShowGuiInScreenshots=true;
            IEnumerable<bool> Until(Func<bool> done,double limit){for(double s0=Time.RealTime;!done()&&Time.RealTime-s0<limit;)yield return true;}
            IEnumerable<bool> Snap(string name){foreach(var x in During(.8,()=>{}))yield return x;ScreenCaptureManager.Capture(1280,720,name+".png");yield return true;}
            string Texts(ContainerWidget w)=>string.Join(" | ",w.AllChildren.Select(c=>c switch{LabelWidget l=>l.Text,BevelledButtonWidget b=>"["+b.Text+"]",CheckboxWidget c2=>(c2.IsChecked?"☑":"☐")+c2.Text,SliderWidget sl=>"<"+sl.Text+">",_=>null}).Where(x=>!string.IsNullOrWhiteSpace(x)).Distinct());
            // the game's list -> this mod's page -> the entry opens MDSettingsScreen
            ScreensManager.SwitchScreen("ModSettings");foreach(var x in During(.5,()=>{}))yield return x;
            var list=ScreensManager.FindScreen<ModSettingsScreen>("ModSettings");var F=BindingFlags.NonPublic|BindingFlags.Instance;
            var stack=(Stack<(string,string[],string)>)typeof(ModSettingsScreen).GetField("m_pageStack",F).GetValue(list);
            stack.Clear();stack.Push((pkg,new[]{pages[0].Id},pages[0].Title));typeof(ModSettingsScreen).GetMethod("NavigateCurrent",F).Invoke(list,null);
            foreach(var x in Until(()=>ScreensManager.CurrentScreen is MDSettingsScreen,5))yield return x;
            s["entryOpens"]=ScreensManager.CurrentScreen?.GetType().Name;
            if(ScreensManager.CurrentScreen is not MDSettingsScreen page)throw new Exception("settings: the entry opened "+s["entryOpens"]);
            foreach(var x in Snap("rs_settings"))yield return x;s["page"]=Texts(page);
            page.Working.StartDay=0;page.Working.Time=MDSpawnTime.Always;foreach(var x in Snap("rs_settings_day0"))yield return x;s["pageDay0"]=Texts(page);
            page.SaveNow();foreach(var x in During(.5,()=>{}))yield return x;
            s["saved"]=$"back on {ScreensManager.CurrentScreen?.GetType().Name}; start day {MDSettings.StartDay}, time {MDSettings.SpawnTime}";
            string xml;using(var st=Storage.OpenFile(ModsManager.ModsSettingsPath,OpenFileMode.Read))using(var rd=new StreamReader(st))xml=rd.ReadToEnd();
            int at=xml.IndexOf(MDSettings.Element);s["file"]=at<0?"no element":xml.Substring(at,Math.Min(400,xml.Length-at));
            MDSettings.StartDay=5;MDSettings.SpawnTime=MDSpawnTime.Night;ModSettingsManager.LoadModSettings();s["reloaded"]=$"start day {MDSettings.StartDay}, time {MDSettings.SpawnTime}";
            if(MDSettings.StartDay!=0||MDSettings.SpawnTime!=MDSpawnTime.Always||at<0)throw new Exception("settings save/load: "+s["saved"]+"; "+s["reloaded"]+"; "+s["file"]);
            // the HUD editor from the page
            MDSettingsScreen.Open();foreach(var x in During(.5,()=>{}))yield return x;((MDSettingsScreen)ScreensManager.CurrentScreen).OpenEditor();
            foreach(var x in Until(()=>ScreensManager.CurrentScreen is MDHudLayoutScreen,3))yield return x;
            if(ScreensManager.CurrentScreen is not MDHudLayoutScreen ed)throw new Exception("settings: no editor ("+ScreensManager.CurrentScreen?.GetType().Name+")");
            foreach(var x in Snap("rs_editor_default"))yield return x;s["editor"]=Texts(ed);
            bool rightBefore=ed.ControlsOnRight;ed.MoveTo(new Vector2(.3f,.3f));ed.Scale(1.21f);ed.Turn(-MathF.PI/12);foreach(var x in Snap("rs_editor_custom"))yield return x;
            s["editorWorking"]=$"custom {ed.Working.Custom} at {ed.Working.X:0.00},{ed.Working.Y:0.00} scale {ed.Working.Scale:0.00} turn {ed.Working.Rotation*180/MathF.PI:0}°; controls on the right {rightBefore} -> {ed.ControlsOnRight}; saved before Save: custom {MDSettings.Hud.Custom}";
            if(rightBefore||!ed.ControlsOnRight)throw new Exception("editor: the controls did not move out of the way: "+s["editorWorking"]);
            bool untouched=!MDSettings.Hud.Custom;ed.SaveNow();foreach(var x in During(.5,()=>{}))yield return x;
            s["editorSaved"]=$"back on {ScreensManager.CurrentScreen?.GetType().Name}; custom {MDSettings.Hud.Custom} at {MDSettings.Hud.X:0.00},{MDSettings.Hud.Y:0.00} scale {MDSettings.Hud.Scale:0.00} turn {MDSettings.Hud.Rotation*180/MathF.PI:0}°";
            foreach(var x in Snap("rs_settings_after_editor"))yield return x;s["pageAfterEditor"]=Texts(ScreensManager.CurrentScreen);
            if(!untouched||!MDSettings.Hud.Custom||Math.Abs(MDSettings.Hud.X-.3f)>.01f||Math.Abs(MDSettings.Hud.Scale-1.21f)>.01f||ScreensManager.CurrentScreen is not MDSettingsScreen)throw new Exception("editor: "+s["editorWorking"]+"; "+s["editorSaved"]);
            MDSettings.Defaults();MDSettings.Persist();SettingsManager.ShowGuiInScreenshots=false;
            ScreensManager.SwitchScreen("Game");foreach(var x in During(1,()=>{}))yield return x;
        }
        // ---- the mech panel in play: default place, small at the top left, large and turned in the middle, without the key
        // line, hidden; its box read from the transform the game draws it with. Then the rifle's status while firing.
        if(Part("hud")) {
            var h=new Dictionary<string,object>();r["hud"]=h;MDSettings.Defaults();
            var u=Deploy(Home);var m=u.Mech;while(Wait(1))yield return true;
            pl.ComponentBody.Position=u.Body.Position+new Vector3(2.4f,.1f,0);pl.ComponentBody.Velocity=Vector3.Zero;while(Wait(.3))yield return true;
            MDMechPilot.ExternalControls=true;if(!MDMechPilot.Board(pl,u))throw new Exception("hud: cannot board");
            SettingsManager.ShowGuiInScreenshots=true;
            // the panel's box and centre in the host (the controls area), from the transforms the game draws it with
            (Vector2 min,Vector2 max) Box(){var w=m.Hud;var to=w.Panel.GlobalTransform*Matrix.Invert(w.GlobalTransform);var s=w.Panel.Size;
                var pts=new[]{Vector2.Zero,new Vector2(s.X,0),s,new Vector2(0,s.Y)}.Select(q=>Vector2.Transform(q,to)).ToArray();return(pts.Aggregate(Vector2.Min),pts.Aggregate(Vector2.Max));}
            string Panel(){var w=m.Hud;if(w==null)return "none";var b=Box();return $"box {b.min.X:0},{b.min.Y:0} - {b.max.X:0},{b.max.Y:0} in {w.LastArea.X:0}x{w.LastArea.Y:0}, size {w.Panel.Size.X:0}x{w.Panel.Size.Y:0}";}
            IEnumerable<bool> Snap(string name){foreach(var x in During(1,()=>{}))yield return x;ScreenCaptureManager.Capture(1280,720,name+".png");foreach(var x in During(.3,()=>{}))yield return x;h[name]=Panel();}
            bool Near(float a,float b,float tol=2)=>Math.Abs(a-b)<tol;
            foreach(var x in Snap("rh_default"))yield return x;
            var area=m.Hud.LastArea;float fit=MDHudPlacement.Fit(area);var b0=Box();
            if(!Near(b0.max.X,area.X-20)||!Near(b0.max.Y,area.Y-85)||!Near(b0.max.X-b0.min.X,320*fit))throw new Exception("hud default place: "+Panel());
            MDSettings.Hud=new MDHudPlacement{Custom=true,X=.2f,Y=.25f,Scale=.6f};foreach(var x in Snap("rh_small_top_left"))yield return x;
            var b1=Box();if(!Near((b1.min.X+b1.max.X)/2,.2f*area.X)||!Near((b1.min.Y+b1.max.Y)/2,.25f*area.Y)||!Near(b1.max.X-b1.min.X,320*fit*.6f))throw new Exception("hud small: "+Panel());
            MDSettings.Hud=new MDHudPlacement{Custom=true,X=.5f,Y=.5f,Scale=1.5f,Rotation=MathF.PI/6};foreach(var x in Snap("rh_turned_centre"))yield return x;
            var b2=Box();var ext=MDHudPlacement.Extent(new Vector2(320,112),fit*1.5f,MathF.PI/6);
            if(!Near((b2.min.X+b2.max.X)/2,area.X/2)||!Near((b2.min.Y+b2.max.Y)/2,area.Y/2)||!Near(b2.max.X-b2.min.X,ext.X,3)||!Near(b2.max.Y-b2.min.Y,ext.Y,3))throw new Exception("hud turned: "+Panel()+$", want extent {ext}");
            MDSettings.HudKeys=false;foreach(var x in Snap("rh_no_keys"))yield return x;
            if(m.Hud.Panel.Size.Y!=86)throw new Exception("hud without keys: "+Panel());
            MDSettings.HudShow=false;foreach(var x in Snap("rh_hidden"))yield return x;
            if(m.Hud!=null||pl.ComponentGui.ControlsContainerWidget.AllChildren.OfType<MDStatusWidget>().Any())throw new Exception("hud still shown");
            MDSettings.Defaults();foreach(var x in Snap("rh_back"))yield return x;
            // the rifle held down for 2.5 s, then released: its status and the old rule's on the same frames
            m.Weapon=MDMechWeapon.Rifle;var seen=new List<string>();string last=null,lastOld=null;int oldChanges=0;int shots0=LabSounds.Of(MDMechBrain.Se(8));
            void Sample(){var st=MDMechBrain.Status(u);if(st!=last){seen.Add(st);last=st;}var old=m.Cooldown>.15f?"装填中":"就绪";if(old!=lastOld){oldChanges++;lastOld=old;}}
            foreach(var x in During(2.5,()=>{m.Trigger=true;Sample();}))yield return x;
            m.Trigger=false;foreach(var x in During(1,Sample))yield return x;
            h["rifleStatus"]=string.Join(" -> ",seen);h["oldRuleChanges"]=oldChanges;h["rifleShots"]=LabSounds.Of(MDMechBrain.Se(8))-shots0;
            if(seen.Count>3||!seen.Contains("射击中")||seen[^1]!="就绪")throw new Exception("rifle status: "+h["rifleStatus"]);
            MDMechPilot.Exit(pl,u,true);MDMechPilot.ExternalControls=false;SettingsManager.ShowGuiInScreenshots=false;proj.RemoveEntity(u.Entity,true);while(Wait(.5))yield return true;
        }
        // ---- the wild spawner: its rules as a survival world sees them, the number near the player at two densities, then
        // 40 s of a real survival world (every 15 s, any time of day): robots appear and leave a player without towers alone
        if(Part("spawns")) {
            var s=new Dictionary<string,object>();r["spawns"]=s;MDSettings.Defaults();Kill(MDWildSpawns.Wild(proj));
            pl.ComponentBody.Position=Home;pl.ComponentBody.Velocity=Vector3.Zero;while(Wait(.5))yield return true;
            s["creative"]=MDWildSpawns.Blocked(proj);s["labDay"]=MDWildSpawns.Day(proj);
            gameInfo.WorldSettings.GameMode=GameMode.Survival;var rules=new Dictionary<string,string>();   // for these calls only: no frame passes
            MDSettings.StartDay=1;rules["night only, by day"]=MDWildSpawns.Blocked(proj);
            MDSettings.SpawnTime=MDSpawnTime.Always;rules["all day"]=MDWildSpawns.Blocked(proj);
            MDSettings.StartDay=30;rules["from day 30"]=MDWildSpawns.Blocked(proj);
            MDSettings.SpawnTime=MDSpawnTime.Off;rules["off"]=MDWildSpawns.Blocked(proj);
            MDSettings.SpawnTime=MDSpawnTime.Always;MDSettings.StartDay=0;rules["start day 0"]=MDWildSpawns.Blocked(proj);
            gameInfo.WorldSettings.GameMode=GameMode.Creative;MDSettings.Defaults();s["rules"]=rules;
            if(s["creative"] is not "game mode"||rules["night only, by day"]!="daytime"||rules["all day"]!=""||rules["from day 30"]!="before day 30"||rules["off"]!="off"||rules["start day 0"]!="start day 0")throw new Exception("spawn rules: "+string.Join(", ",rules)+" creative "+s["creative"]);
            var rounds=new List<string>();s["rounds"]=rounds;
            foreach(var dens in new[]{1f,2f}) {
                MDSettings.SpawnDensity=dens;Kill(MDWildSpawns.Wild(proj));while(Wait(.3))yield return true;var rng=new System.Random(11);
                for(int i=0;i<8;i++){int n=MDWildSpawns.Spawn(proj,rng,true);rounds.Add($"x{dens} round {i}: +{n} {MDWildSpawns.LastReason}");}
                var wild=MDWildSpawns.Wild(proj);var dist=wild.Select(q=>Vector3.Distance(Flat(q.Body.Position),Flat(pl.ComponentBody.Position))).DefaultIfEmpty(0).ToList();
                s[$"density{dens}"]=$"{wild.Count} robots (limit {MDWildSpawns.Cap}): {string.Join(",",wild.Select(q=>q.Kind.Id))}, {dist.Min():0}-{dist.Max():0} m from the player";
                if(wild.Count!=MDWildSpawns.Cap||dist.Min()<24||dist.Max()>64)throw new Exception("wild spawns: "+s[$"density{dens}"]);
            }
            Kill(MDWildSpawns.Wild(proj));MDSettings.Defaults();while(Wait(.5))yield return true;
            Cam(Home+new Vector3(0,70,-.1f),Home);Shot("rt_spawn_area");yield return true;
            MDSettings.SpawnTime=MDSpawnTime.Always;MDSettings.SpawnInterval=15;MDSettings.StartDay=1;
            var health=pl.ComponentHealth;float resilience=health.AttackResilience;health.AttackResilience=1e6f;
            gameInfo.WorldSettings.GameMode=GameMode.Survival;int most=0;bool chased=false;double ts=Time.RealTime,first=-1;
            foreach(var x in During(40,()=>{var wl=MDWildSpawns.Wild(proj);if(wl.Count>0&&first<0)first=Time.RealTime-ts;most=Math.Max(most,wl.Count);if(wl.Any(q=>q.Target==pl.ComponentBody||q.Hunt!=null))chased=true;}))yield return x;
            s["survival"]=$"first robots after {first:0} s, at most {most}, last round: +{MDWildSpawns.LastSpawned} {MDWildSpawns.LastReason}, a robot went for the player: {chased}";
            // a robot 14 m away ignores the player until hit, then fights back
            var rb=Spawn("MDRobot05",Home+new Vector3(0,0,-14),0);while(Wait(1.5))yield return true;
            string before=rb.Target==null?"none":"rank "+ComponentMDRobot.Rank(rb.Target);
            ComponentMiner.AttackBody(new ProjectileAttackment(rb.Entity,pl.Entity,MDCombat.Center(rb.Body),-Vector3.UnitZ,1,null));
            foreach(var x in During(4,()=>{}))yield return x;
            s["provoked"]=$"target before the hit: {before}; after: {(rb.Target==pl.ComponentBody?"the player":rb.Target==null?"none":"other")}, shots {rb.ShotsFired}";
            bool fought=rb.Target==pl.ComponentBody&&rb.ShotsFired>0;
            gameInfo.WorldSettings.GameMode=GameMode.Creative;health.AttackResilience=resilience;health.Health=1;MDSettings.Defaults();Kill(MDWildSpawns.Wild(proj));Kill([rb]);while(Wait(.5))yield return true;
            if(first<0||chased||before!="none"||!fought)throw new Exception("survival spawns: "+s["survival"]+"; "+s["provoked"]);
        }
        // ---- targets: a robot 50 m from the only tower walks there and attacks; Mercury before a nearer tower; players only when
        // provoked or within weapon range at the defences; a tower behind another tower sees and hits a robot
        if(Part("targets")) {
            var g=new Dictionary<string,object>();r["targets"]=g;var gun=MDTowerLibrary.All.First(t=>t.data==0);
            pl.ComponentBody.Position=Home+new Vector3(16,0,16);pl.ComponentBody.Velocity=Vector3.Zero;while(Wait(.3))yield return true;
            var tower=Place(gun,Home,0);var rb=Spawn("MDRobot05",Home+new Vector3(0,0,-50),0);double ta=Time.RealTime,hunted=-1,targeted=-1,fired=-1;
            foreach(var x in During(35,()=>{double el=Time.RealTime-ta;if(hunted<0&&rb.Hunt==tower.Body)hunted=el;if(targeted<0&&rb.Target==tower.Body)targeted=el;if(fired<0&&rb.ShotsFired>0){fired=el;Cam(Home+new Vector3(16,10,-24),(Home+rb.Body.Position)/2+Vector3.UnitY*1.5f);Shot("rt_hunt");}}))yield return x;
            g["hunt"]=$"walks to the tower after {hunted:0.0} s, targets it after {targeted:0.0} s, first shot after {fired:0.0} s; now {Vector3.Distance(rb.Body.Position,Home):0} m away, tower {tower.Damage.Hitpoints:0.00}";
            if(hunted<0||targeted<0||fired<0)throw new Exception("targets/hunt: "+g["hunt"]);
            Kill([rb]);Remove([tower]);while(Wait(.5))yield return true;
            // Mercury 20 m away before a tower 12 m away; the tower once Mercury is gone
            var t2=Place(gun,Home+new Vector3(-8,0,0),0);var mech=Deploy(Home+new Vector3(10,0,5));pl.ComponentBody.Position=Home+new Vector3(16,0,16);
            var rb2=Spawn("MDRobot05",Home+new Vector3(-8,0,-12),0);while(Wait(2))yield return true;
            string first=rb2.Target==mech.Body?"Mercury":rb2.Target==t2.Body?"tower":"none";
            // players (decision only; switched to survival for these calls): not while the robot sees the tower, not without
            // towers around, yes within weapon range at the defences, yes when provoked
            gameInfo.WorldSettings.GameMode=GameMode.Survival;var pb=pl.ComponentBody;var keepHunt=rb2.Hunt;
            pb.Position=rb2.Body.Position+new Vector3(10,0,0);bool atDefences=rb2.Valid(pb);var pick=rb2.FindTarget();
            rb2.Hunt=null;bool noTowers=rb2.Valid(pb);rb2.Provoke(pb);bool provoked=rb2.Valid(pb);rb2.ProvokedUntil=0;rb2.Hunt=keepHunt;
            gameInfo.WorldSettings.GameMode=GameMode.Creative;pb.Position=Home+new Vector3(16,0,16);
            proj.RemoveEntity(mech.Entity,true);while(Wait(1.5))yield return true;
            string then=rb2.Target==t2.Body?"tower":rb2.Target==null?"none":"other";
            g["priority"]=$"with Mercury: {first}; without: {then}; player 10 m away at the defences valid {atDefences} (picked {(pick==mech.Body?"Mercury":pick==t2.Body?"tower":pick==pb?"player":"none")}), without towers {noTowers}, provoked {provoked}";
            if(first!="Mercury"||then!="tower"||!atDefences||pick==pb||noTowers||!provoked)throw new Exception("targets/priority: "+g["priority"]);
            Kill([rb2]);Remove([t2]);while(Wait(.5))yield return true;
            // a tower behind another tower: the robot stands still in front of both
            float reach=Math.Min(gun.attack.range*.7f,20);var back=Place(gun,Home,0);var front=Place(gun,Home+new Vector3(0,0,-3.5f),0);
            var still=Spawn("MDRobot05",Home+new Vector3(0,0,-reach),0);still.ForceClip="idle";still.Creature.ComponentLocomotion.WalkSpeed=0;
            while(Wait(.5))yield return true;
            var muzzle=back.Muzzle;var old=proj.FindSubsystem<SubsystemBodies>(true).Raycast(muzzle,MDCombat.Center(still.Body),0,(b,d)=>b!=back.Body);   // the rule before: the first body on the line
            int b0=back.ShotCount,f0=front.ShotCount;float h0=still.Health.Health;
            foreach(var x in During(6,()=>{}))yield return x;
            Cam(Home+new Vector3(12,6,-reach/2),Home+new Vector3(0,1.5f,-reach/2));Shot("rt_tower_behind");yield return true;
            g["behind"]=$"first body on the back tower's line (old rule): {(old?.ComponentBody==front.Body?"the front tower":old?.ComponentBody==still.Body?"the robot":"none")}; back tower target {(back.Target==still.Body?"the robot":"none")}, shots {back.ShotCount-b0}; front shots {front.ShotCount-f0}; robot health {h0:0.00} -> {still.Health.Health:0.00}";
            if(back.Target!=still.Body||back.ShotCount<=b0)throw new Exception("targets/behind: "+g["behind"]);
            Kill([still]);Remove([back,front]);ClearPickables();while(Wait(.5))yield return true;
        }
        // ---- the devil mech's escorts (user, 2026-10-21: "恶魔机甲召唤的敌兵不会自动找机甲，炮塔，玩家攻击"): summoned from its
        // item beside three towers and Mercury (A), and 70 m away from them (B); every escort's state every 3 s for 40 s
        if(Part("escorts")) {
            var es=new Dictionary<string,object>();r["escorts"]=es;var defs=MDTowerLibrary.All;var inv=pl.ComponentMiner.Inventory;
            foreach(var (name,summonAt) in new[]{("A",Home+new Vector3(0,0,-16)),("B",Home+new Vector3(0,0,-50))}) {
                var towers=new[]{Place(defs.First(t=>t.data==0),Home+new Vector3(-7,0,4),0),Place(defs.First(t=>t.data==0),Home+new Vector3(7,0,4),0),Place(defs.First(t=>t.data==4),Home+new Vector3(0,0,10),0)};
                var mech=Deploy(Home+new Vector3(14,0,14));
                pl.ComponentBody.Position=summonAt+new Vector3(0,0,6);pl.ComponentBody.Rotation=Quaternion.Identity;pl.ComponentBody.Velocity=Vector3.Zero;while(Wait(.5))yield return true;
                inv.RemoveSlotItems(0,inv.GetSlotCount(0));inv.AddSlotItems(0,MDSummonBlock.Value(MDSummonBlock.Count-1),1);inv.ActiveSlotIndex=0;
                pl.ComponentMiner.Use(new Ray3(pl.ComponentBody.Position+new Vector3(0,1.6f,0),Vector3.Normalize(new Vector3(0,-1.6f,-5f))));
                var boss=MDBoss.Alive(proj)??throw new Exception("escorts: no boss");pl.ComponentBody.Position=Home+new Vector3(-20,0,24);
                while(Wait(2))yield return true;
                boss.Health.Health=.69f;while(Wait(.5))yield return true;
                var escorts=proj.Entities.Select(e=>e.FindComponent<ComponentMDRobot>()).Where(x=>x!=null&&x.Boss==null).ToList();
                var log=new List<string>();es[name]=log;log.Add($"boss at {Vector3.Distance(boss.Body.Position,Home):0} m from Home, escorts sent {boss.Boss.EscortsSent}, here {escorts.Count}");
                double t0e=Time.RealTime;int step=0;
                string State(ComponentMDRobot x){
                    var units=proj.Entities.Select(e=>e.FindComponent<ComponentMDUnit>()).Where(u=>u!=null&&!u.Destroyed).ToList();
                    float near=units.Count==0?-1:units.Min(u=>Vector3.Distance(u.Body.Position,x.Body.Position));
                    var path=x.Entity.FindComponent<ComponentPathfinding>(true);
                    return $"{x.Kind.Id} hp {x.Health.Health:0.00} near-unit {near:0} m, target {(x.Target==null?"-":ComponentMDRobot.Rank(x.Target).ToString())}, hunt {(x.Hunt==null?"-":"yes")}, raidGoal {(x.RaidGoal.HasValue?$"{Vector3.Distance(x.RaidGoal.Value,x.Body.Position):0} m":"-")}, active {x.IsActive}, dest {(path.Destination.HasValue?"yes":"-")} stuck {path.IsStuck}, speed {new Vector2(x.Body.Velocity.X,x.Body.Velocity.Z).Length():0.0}, shots {x.ShotsFired}";
                }
                foreach(var x in During(40,()=>{if(Time.RealTime-t0e>=step*3){step++;log.Add($"{Time.RealTime-t0e:0}s: "+string.Join(" | ",escorts.Where(q=>q.Entity.IsAddedToProject).Select(State)));}}))yield return x;
                Cam(Home+new Vector3(30,25,-30),Home);Shot("re_"+name);yield return true;
                int gone=escorts.Count(q=>!q.Entity.IsAddedToProject&&q.Health.Health>0),idle=escorts.Count(q=>q.ShotsFired==0&&q.Health.Health>0);
                es[name+"_summary"]=$"{escorts.Count} escorts, {escorts.Count(q=>q.ShotsFired>0)} fired, vanished alive {gone}, alive without a shot {idle}, towers {string.Join(" ",towers.Select(t=>$"{t.Damage.Hitpoints:0.00}"))}, mech {mech.Damage.Hitpoints:0.00}";
                if(escorts.Count<2||gone>0||idle>0)throw new Exception($"escorts {name}: "+es[name+"_summary"]);
                Kill(proj.Entities.Select(e=>e.FindComponent<ComponentMDRobot>()).Where(x=>x!=null).ToList());Remove(towers);if(mech.Entity.IsAddedToProject)proj.RemoveEntity(mech.Entity,true);ClearPickables();while(Wait(1))yield return true;
            }
        }
        // ---- round 5 (user, 2026-10-21): the pilot takes no damage in the mech (and a robot's shot at the pilot hits the mech), the
        // medic tower heals Mercury and the player, the devil mech's armour by tower tier, and 30 s of the devil mech against three
        // tier-1 towers and Mercury firing its rifle (lab pilot). Screenshots rb5_*.
        if(Part("balance")) {
            var bl=new Dictionary<string,object>();r["balance"]=bl;var defs=MDTowerLibrary.All;var health=pl.ComponentHealth;
            var u=Deploy(Home);var m=u.Mech;while(Wait(1))yield return true;
            bl["armour"]=u.Damage.AttackResilience;if(u.Damage.AttackResilience!=4000)throw new Exception("Mercury armour "+u.Damage.AttackResilience);
            pl.ComponentBody.Position=u.Body.Position+new Vector3(2.4f,.1f,0);pl.ComponentBody.Velocity=Vector3.Zero;while(Wait(.3))yield return true;
            MDMechPilot.ExternalControls=true;if(!MDMechPilot.Board(pl,u))throw new Exception("balance: cannot board");
            var mode=gameInfo.WorldSettings.GameMode;gameInfo.WorldSettings.GameMode=GameMode.Survival;   // creative players take no damage anyway
            float h0=health.Health;int blocked0=MDMechPilotShield.Blocked;
            health.Injure(new Injury(.3f,null,true,"lab"));   // ignores invulnerability (a creative player has it): the shield must still block
            var shooter=Spawn("MDRobot05",Home+new Vector3(0,0,-20),0);shooter.ForceClip="idle";float mh0=u.Damage.Hitpoints;
            MDRobots.Hit(shooter.Entity,pl.ComponentBody,shooter.Kind,10,MDCombat.Center(pl.ComponentBody),Vector3.UnitZ);
            bl["seated"]=$"player {h0:0.00} -> {health.Health:0.00} (blocked {MDMechPilotShield.Blocked-blocked0}); robot shot at the pilot: mech {mh0:0.000} -> {u.Damage.Hitpoints:0.000}";
            bool shielded=health.Health==h0&&u.Damage.Hitpoints<mh0;
            MDMechPilot.Exit(pl,u,true);MDMechPilot.ExternalControls=false;while(Wait(.3))yield return true;
            health.Injure(new Injury(.2f,null,true,"lab"));float hOut=health.Health;bl["outside"]=$"player {h0:0.00} -> {hOut:0.00} after an injury outside the mech";
            gameInfo.WorldSettings.GameMode=mode;Kill([shooter]);
            if(!shielded||hOut>=h0-.1f)throw new Exception("pilot shield: "+bl["seated"]+"; "+bl["outside"]);
            // the medic tower: Mercury at 50 % and the player at 60 % beside it
            var medic=Place(defs.First(t=>t.data==19),u.Body.Position+new Vector3(-6,0,0),0);u.Damage.Hitpoints=.5f;health.Health=.6f;pl.ComponentBody.Position=u.Body.Position+new Vector3(-3,.1f,3);
            foreach(var x in During(6,()=>{}))yield return x;
            bl["medic"]=$"Mercury 0.50 -> {u.Damage.Hitpoints:0.00}, player 0.60 -> {health.Health:0.00} in 6 s";
            if(u.Damage.Hitpoints<=.5f||health.Health<=.6f)throw new Exception("medic: "+bl["medic"]);
            health.Health=1;Remove([medic]);while(Wait(.5))yield return true;
            // three tier-1 gun towers and Mercury around a devil mech placed in the middle: armour 30000 + 3 x 1000 + 3000
            var tw=new[]{Place(defs.First(t=>t.data==0),Home+new Vector3(-8,0,10),0),Place(defs.First(t=>t.data==0),Home+new Vector3(8,0,10),0),Place(defs.First(t=>t.data==0),Home+new Vector3(0,0,16),0)};
            u.Damage.Hitpoints=1;u.Body.Position=Home+new Vector3(14,.1f,4);pl.ComponentBody.Position=u.Body.Position+new Vector3(2.4f,.1f,0);while(Wait(.5))yield return true;
            MDMechPilot.ExternalControls=true;MDMechPilot.Board(pl,u);
            var boss=MDBoss.Spawn(proj,Home+new Vector3(0,0,4),Home+new Vector3(0,3,4));while(Wait(.5))yield return true;
            bl["bossArmour"]=boss.Health.AttackResilience;if(boss.Health.AttackResilience!=36000)throw new Exception("boss armour "+boss.Health.AttackResilience+" (want 36000)");
            m.Weapon=MDMechWeapon.Rifle;float bh=boss.Health.Health;double tf=Time.RealTime;var track=new List<string>();int step=0;
            foreach(var x in During(30,()=>{
                if(!boss.Entity.IsAddedToProject||u.Destroyed)return;var c=MDCombat.Center(boss.Body);var from=MDMechBrain.Eye(u);var d=c-from;
                u.AimYaw=MathF.Atan2(-d.X,-d.Z);u.AimPitch=Math.Clamp(MathF.Asin(Math.Clamp(d.Y/d.Length(),-1,1)),-.5f,.65f);m.AimPoint=c;m.Trigger=true;
                if(Time.RealTime-tf>=step*5){step++;track.Add($"{Time.RealTime-tf:0}s boss {boss.Health.Health*100:0}% Mercury {u.Damage.Hitpoints*100:0}% towers {string.Join(" ",tw.Select(t=>$"{t.Damage.Hitpoints*100:0}"))}");}
            }))yield return x;
            m.Trigger=false;bl["fight30s"]=track;bl["fightEnd"]=$"boss lost {(bh-boss.Health.Health)*boss.Health.AttackResilience:0} of {boss.Health.AttackResilience:0} ({(bh-boss.Health.Health)*100:0}%), Mercury {u.Damage.Hitpoints*100:0}%{(u.Destroyed?" (wrecked)":"")}, towers {string.Join(" ",tw.Select(t=>$"{t.Damage.Hitpoints*100:0}%"))}";
            Cam(Home+new Vector3(24,14,-18),Home+new Vector3(0,3,6));Shot("rb5_fight");yield return true;
            if(MDMechPilot.Mounted(pl)!=null)MDMechPilot.Exit(pl,u,true);MDMechPilot.ExternalControls=false;
            Kill(proj.Entities.Select(e=>e.FindComponent<ComponentMDRobot>()).Where(x=>x!=null).ToList());Remove(tw);if(u.Entity.IsAddedToProject)proj.RemoveEntity(u.Entity,true);ClearPickables();health.Health=1;while(Wait(1))yield return true;
        }
        // ---- the hero mechs (round 5): 复仇女神 and 轩辕十四 next to Mercury. Each from its item: model, joints, clips, armour; its
        // clips at start / middle / end; then piloted by the lab: walking speed, hover, rifle timing (复仇女神's burst), cannon,
        // damage factor, blast protection, first and third person, taken down and deployed again, the Neorxna name. Shots rh5_*.
        if(Part("heroes")) {
            var hz=new Dictionary<string,object>();r["heroes"]=hz;var types=MDMechType.All;var units=new List<ComponentMDUnit>();
            for(int i=0;i<types.Length;i++)units.Add(Deploy(Home+new Vector3(-12+i*12,0,10),types[i]));
            pl.ComponentBody.Position=Home+new Vector3(0,0,24);while(Wait(1.5))yield return true;
            var lineup=Home+new Vector3(0,2.6f,5);foreach(var x in Hold(lineup+new Vector3(4,3,-22),lineup,4))yield return x;Shot("rh5_lineup");yield return true;
            foreach(var x in Hold(lineup+new Vector3(22,2,-6),lineup,4))yield return x;Shot("rh5_lineup_side");yield return true;
            for(int i=0;i<types.Length;i++) {
                var u=units[i];var tp=types[i];var mm=u.Entity.FindComponent<ComponentMDMechModel>(true);var d=u.Mech.Def;
                hz[tp.Id]=$"type {u.Mech.Type.Id}, armour {u.Damage.AttackResilience}, box {u.Body.BoxSize}, height {d.height:0.00} m, {mm.Model?.Meshes.Count} meshes, missing bones {d.joints.Count(j=>mm.Model?.FindBone(j.name,false)==null)}, clips {d.clips.Length}, roles turret {d.roles.turret} arms {d.roles.armL}/{d.roles.armR} cannon {string.Join(",",d.roles.cannon)}";
                if(u.Mech.Type!=tp||u.Damage.AttackResilience!=tp.Armour||mm.Model==null||d.joints.Any(j=>mm.Model.FindBone(j.name,false)==null))throw new Exception("hero "+tp.Id+": "+hz[tp.Id]);
            }
            foreach(var (u,tp) in units.Skip(1).Zip(types.Skip(1))) {   // the heroes' clips
                var c=u.Body.Position+Vector3.UnitY*2.4f;var m=u.Mech;
                foreach(var name in new[]{"idle","walk_forward","dash_forward","jump","melee","cannon_deploy_fire","death"})for(int k=0;k<3;k++){m.ForceClip=name;m.ForceTime=m.Def.Seconds(m.Def.Clip(name))*k/2f;foreach(var x in Hold(c+new Vector3(7,1.5f,-9),c,3))yield return x;Shot($"rh5_{tp.Id}_{name}_{k}");yield return true;}
                m.ForceClip=null;
            }
            foreach(var u in units)proj.RemoveEntity(u.Entity,true);while(Wait(.5))yield return true;
            var dummy=Dummy(Home+new Vector3(0,0,-30));dummy.C.ComponentHealth.AttackResilience=1e4f;
            var shooter=Spawn("MDRobot06",Home+new Vector3(30,0,-30),0);shooter.ForceClip="idle";
            foreach(var tp in types) {
                var res=new Dictionary<string,object>();hz[tp.Id+"_drive"]=res;
                var u=Deploy(Home,tp);var m=u.Mech;while(Wait(1))yield return true;
                pl.ComponentBody.Position=u.Body.Position+new Vector3(tp.Box.X/2+1.4f,.1f,0);pl.ComponentBody.Velocity=Vector3.Zero;while(Wait(.3))yield return true;
                MDMechPilot.ExternalControls=true;if(!MDMechPilot.Board(pl,u))throw new Exception("cannot board "+tp.Id);
                // walk 2 s forward, then hover 2 s
                var p0=u.Body.Position;m.Move=new Vector2(0,1);foreach(var x in During(2,()=>m.Move=new Vector2(0,1)))yield return x;m.Move=Vector2.Zero;
                float walked=Vector3.Distance(Flat(p0),Flat(u.Body.Position));while(Wait(1))yield return true;
                string hoverClip="";foreach(var x in During(2,()=>{m.Hover=true;hoverClip=m.Anim.Base.name;}))yield return x;bool hovering=m.Hovering;m.Hover=false;while(Wait(1))yield return true;
                res["move"]=$"walked {walked:0.0} m in 2 s, hovering {hovering} in {hoverClip}, energy {m.Energy:0} / {tp.EnergyMax:0}";
                // the rifle for 1.6 s at the dummy: the times between shots
                var dc=MDCombat.Center(dummy.Body);var times=new List<double>();int s0=LabSounds.Of(MDMechBrain.Se(8));double t0r=Time.RealTime;
                void Aim(){var from=MDMechBrain.Eye(u);var dd=dc-from;u.AimYaw=MathF.Atan2(-dd.X,-dd.Z);u.AimPitch=Math.Clamp(MathF.Asin(dd.Y/dd.Length()),-.5f,.65f);m.AimPoint=dc;}
                m.Weapon=MDMechWeapon.Rifle;foreach(var x in During(1.6,()=>{Aim();m.Trigger=true;int n=LabSounds.Of(MDMechBrain.Se(8))-s0;while(times.Count<n)times.Add(Time.RealTime-t0r);}))yield return x;m.Trigger=false;
                var gaps=times.Zip(times.Skip(1),(a,b)=>b-a).Select(g=>g.ToString("0.00")).ToList();res["rifleGaps"]=string.Join(" ",gaps);
                // the cannon: one press, the shot leaves once it is open
                int c0=LabSounds.Of(MDMechBrain.Se(11));m.Weapon=MDMechWeapon.Cannon;foreach(var x in During(2.5,()=>{Aim();m.Trigger=LabSounds.Of(MDMechBrain.Se(11))==c0;}))yield return x;m.Trigger=false;
                res["cannon"]=$"shots {LabSounds.Of(MDMechBrain.Se(11))-c0}, state {m.Cannon}";
                // damage factor and blast protection
                float hd=dummy.C.ComponentHealth.Health;MDTowerBrain.Hit(u,dummy.Body,1000,dc,Vector3.UnitZ);float dealt=(hd-dummy.C.ComponentHealth.Health)*1e4f;dummy.C.ComponentHealth.Health=1;
                float mh=u.Damage.Hitpoints;MDRobots.Hit(shooter.Entity,u.Body,shooter.Kind,1,MDCombat.Center(u.Body),Vector3.UnitZ);float taken=(mh-u.Damage.Hitpoints)*u.Damage.AttackResilience;u.Damage.Hitpoints=1;
                res["damage"]=$"a 1000 hit deals {dealt:0}; a {shooter.Kind.VsStructure:0} missile takes {taken:0}";
                // first and third person
                pl.GameWidget.ActiveCamera=pl.GameWidget.FindCamera<FppCamera>();foreach(var x in During(1,Aim))yield return x;ScreenCaptureManager.Capture(1280,720,$"rh5_{tp.Id}_first_person.png");yield return true;
                pl.GameWidget.ActiveCamera=pl.GameWidget.FindCamera<TppCamera>();foreach(var x in During(1,Aim))yield return x;ScreenCaptureManager.Capture(1280,720,$"rh5_{tp.Id}_third_person.png");yield return true;
                MDMechPilot.Exit(pl,u,true);MDMechPilot.ExternalControls=false;
                // Neorxna, then taken down with a kit and deployed again
                var panelType=AppDomain.CurrentDomain.GetAssemblies().Select(a=>a.GetType("Neorxna.NUI.NUInfoPanelWidget",false)).FirstOrDefault(x=>x!=null);
                if(panelType!=null){MDNeorxnaCompatibility.Install();var panel=Activator.CreateInstance(panelType);panelType.GetMethod("SetInfo_Entity").Invoke(panel,[u.Entity]);res["neorxna"]=((LabelWidget)panelType.GetField("m_name").GetValue(panel)).Text;}
                u.Mech.Kit=2;int item=MDTowerMaintenance.Dismantle(u,null);ClearPickables();
                res["item"]=$"{BlocksManager.Blocks[Terrain.ExtractContents(item)].GetDisplayName(terrain,item)}: type {MDDeploymentBlock.MechType(item)?.Id}, kit {MDDeploymentBlock.Kit(item)}";
                var u2=Deploy(Home,MDDeploymentBlock.MechType(item),1,MDDeploymentBlock.Kit(item));res["redeployed"]=$"{u2.Mech.Type.Id} kit {u2.Mech.Kit}";
                proj.RemoveEntity(u2.Entity,true);while(Wait(.5))yield return true;
                bool burstOk=!tp.BurstRifle||gaps.Count>=4&&gaps.Any(g=>double.Parse(g)>.3);   // 复仇女神: short gaps inside a burst, a long one between bursts
                if(walked<5*tp.Speed||!hovering||times.Count<3||!burstOk||LabSounds.Of(MDMechBrain.Se(11))==c0||Math.Abs(dealt-1000*tp.Damage)>20||Math.Abs(taken-shooter.Kind.VsStructure*tp.ExplosionTaken)>3
                   ||MDDeploymentBlock.MechType(item)!=tp||u2.Mech.Type!=tp||u2.Mech.Kit!=2||panelType!=null&&(string)res["neorxna"]!=tp.Name+"机甲")
                    throw new Exception("hero drive "+tp.Id+": "+string.Join("; ",res.Select(kv=>kv.Key+"="+kv.Value)));
            }
            Remove([dummy]);Kill([shooter]);pl.GameWidget.ActiveCamera=pl.GameWidget.FindCamera<FppCamera>();while(Wait(.5))yield return true;
        }
    }
}
