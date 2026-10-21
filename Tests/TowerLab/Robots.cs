using Engine;
using Game;
using GameEntitySystem;

namespace TowerLab;

// lab --mode robots (0.9.0, docs/tasks/2026-10-20-progression-robots.md): the progression items and recipes, the robot
// enemies (models, clips, a fight against a tower, loot), a night raid on a tower cluster, the devil mech (beacon countdown,
// flight, weak channel, fight, blueprint drop) and Mercury's weapon kits. Results under "robots" and "recipes"; screenshots
// ri_* (items), rb_* (robots and clips), rf_* (fight), rr_* (raid), rx_* (boss), rk_* (kits).
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
            string id=c==deploy?(d==1?"mercury":MDTowerLibrary.ByData(d)?.id??"tower?"+d):"part"+d+(r.RequiredHeatLevel>0?"_furnace":"");
            var found=CraftingRecipesManager.FindMatchingRecipe(terrain,Grid(r),r.RequiredHeatLevel,Math.Max(1,r.RequiredPlayerLevel));
            bool ok=found==r;if(!ok)bad.Add($"{id}: grid resolves to {(found==null?"nothing":found.ResultValue+" "+found.Message)}");
            rec[id+(rec.ContainsKey(id)?"_2":"")]=new{grid=string.Join(" | ",Enumerable.Range(0,3).Select(y=>string.Join(",",Enumerable.Range(0,3).Select(x=>r.Ingredients[x+y*3]??"-")))),heat=r.RequiredHeatLevel,level=r.RequiredPlayerLevel,count=r.ResultCount,description=r.Description,resolves=ok};
        }
        foreach(var t in towers)if(mine.Count(r=>Terrain.ExtractContents(r.ResultValue)==deploy&&Terrain.ExtractData(r.ResultValue)==t.data)!=1)bad.Add(t.id+": not exactly one recipe");
        var tier2=new[]{3,6,14,16,18,19};var tier3=new[]{4,7,8,13,15,17};
        foreach(var r in mine.Where(r=>Terrain.ExtractContents(r.ResultValue)==deploy)) {
            int d=Terrain.ExtractData(r.ResultValue);
            if(tier2.Contains(d)&&r.Ingredients.Count(s=>s=="mdpart:1")!=3)bad.Add($"tier 2 tower {d}: not 3 alloy plates");
            if(tier3.Contains(d)&&r.Ingredients.Count(s=>s=="mdpart:2")!=2)bad.Add($"tier 3 tower {d}: not 2 chips");
            if(!tier2.Contains(d)&&!tier3.Contains(d)&&d!=1&&r.Ingredients.Any(s=>s?.StartsWith("mdpart")==true))bad.Add($"tier 1 tower {d} uses a progression item");
        }
        var merc=mine.First(r=>Terrain.ExtractContents(r.ResultValue)==deploy&&Terrain.ExtractData(r.ResultValue)==1);
        var at7=CraftingRecipesManager.FindMatchingRecipe(terrain,Grid(merc),0,7);
        bool blocked7=at7!=null&&at7!=merc&&!string.IsNullOrEmpty(at7.Message);
        if(merc.RequiredPlayerLevel!=8||!blocked7)bad.Add($"mercury: level {merc.RequiredPlayerLevel}, at level 7 {(at7==merc?"craftable":at7?.Message)}");
        if(!merc.Ingredients.Contains("mdpart:4")||merc.RemainsValue!=0)bad.Add("mercury: blueprint missing or given back");
        int expect=towers.Count+1+8;
        if(mine.Count!=expect)bad.Add($"{mine.Count} recipes, want {expect}");
        if(bad.Count>0)throw new Exception("recipes: "+string.Join("; ",bad));
        return new{count=mine.Count,mercuryAtLevel7=at7?.Message,rec};
    }

    // mode "robots" runs every part; "robots-<part>" (items, gallery, fight, raid, boss, summons, kits) one of them, after the recipe checks
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
            var types=proj.FindSubsystem<SubsystemCreatureSpawn>(true).m_creatureTypes.Select(t=>t.Name).Where(n=>n.StartsWith("MechDefense")).ToList();q["spawnTypes"]=types;
            int sent=MDRaids.Raid(proj,12,new System.Random(5));q["sent"]=sent;q["defence"]=MDRaids.LastDefence;q["elites"]=MDRaids.LastElites;q["secondWave"]=MDRaids.LastSecondWave;double raidAt=Time.RealTime;
            // the natural spawn rules spawn their robots (their suitability is off in Creative, so the spawn function is called directly)
            var spawner=proj.FindSubsystem<SubsystemCreatureSpawn>(true);var natural=new List<string>();
            foreach(var ct in spawner.m_creatureTypes.Where(c=>c.Name.StartsWith("MechDefense"))){var pt=new Point3((int)Home.X-30,(int)Home.Y,(int)Home.Z+30);
                float off=ct.SpawnSuitabilityFunction(ct,pt);MDRobots.LabSpawnAnytime=true;natural.Add($"{ct.Name}: suitability in this creative day world {off}, forced {ct.SpawnSuitabilityFunction(ct,pt)}, spawned {ct.SpawnFunction(ct,pt)}");MDRobots.LabSpawnAnytime=false;
                if(off!=0)throw new Exception("natural spawn on in Creative / by day");}
            q["natural"]=natural;
            var raiders=proj.Entities.Select(e=>e.FindComponent<ComponentMDRobot>()).Where(x=>x!=null&&x.Raider).ToList();
            var wild=proj.Entities.Select(e=>e.FindComponent<ComponentMDRobot>()).Where(x=>x!=null&&!x.Raider).ToList();q["naturalUnits"]=string.Join(",",wild.Select(x=>x.Kind.Id));
            q["units"]=string.Join(",",raiders.Select(x=>x.Kind.Id));q["startDistance"]=string.Join(" ",raiders.Select(x=>$"{Vector3.Distance(x.Body.Position,Home):0}"));
            foreach(var x in During(1,()=>Cam(Home+new Vector3(0,60,-.1f),Home)))yield return x;Shot("rr_start_top");yield return true;
            foreach(var x in During(28,()=>{}))yield return x;
            q["distanceAfter28s"]=string.Join(" ",raiders.Select(x=>x.Entity.IsAddedToProject&&x.Health.Health>0?$"{Vector3.Distance(x.Body.Position,Home):0}":"dead"));
            q["shots"]=string.Join(" ",raiders.Select(x=>x.ShotsFired));q["towers"]=string.Join(" ",cluster.Select(t=>$"{t.Damage.Hitpoints:0.00}"));
            Cam(Home+new Vector3(20,22,-30),Home);Shot("rr_fight");yield return true;
            int before=proj.Entities.Count(e=>e.FindComponent<ComponentMDRobot>() is {Raider:true});while(MDRaids.SecondWavesSent==0&&Time.RealTime-raidAt<90)yield return true;q["secondWaveAfter"]=Time.RealTime-raidAt;foreach(var x in During(4,()=>{}))yield return x;   // 60 s of game time (~12 % slower than real time in the lab)
            var all=proj.Entities.Select(e=>e.FindComponent<ComponentMDRobot>()).Where(x=>x!=null&&x.Raider).ToList();q["raidersAfterSecondWave"]=$"{before} -> {all.Count}";q["towersAfterSecondWave"]=string.Join(" ",cluster.Select(t=>$"{t.Damage.Hitpoints:0.00}"));raiders=all.Union(raiders).ToList();
            Cam(Home+new Vector3(20,22,-30),Home);Shot("rr_second_wave");yield return true;
            q["secondWaveSent"]=MDRaids.SecondWavesSent;
            if(MDRaids.LastDefence!=21||MDRaids.LastSecondWave==0||MDRaids.SecondWavesSent==0||sent<12||types.Count!=2||!raiders.Any(x=>x.Kind.Elite)||wild.Count<2||!wild.Any(x=>x.Kind.Elite))throw new Exception("raid: "+string.Join(", ",q.Select(kv=>kv.Key+"="+kv.Value)));
            Kill(raiders);Kill(wild);Remove(cluster);ClearPickables();while(Wait(.5))yield return true;
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
            if(boss.Health.AttackResilience!=46000)throw new Exception("boss armour "+boss.Health.AttackResilience+" (want 40000 + 3 towers x 2000)");
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
            int item=MDTowerMaintenance.Dismantle(u,null);k["item"]=$"{BlocksManager.Blocks[Terrain.ExtractContents(item)].GetDisplayName(terrain,item)}, kit {MDDeploymentBlock.Kit(item)}";
            ClearPickables();inv.RemoveSlotItems(0,inv.GetSlotCount(0));inv.AddSlotItems(0,item,1);while(Wait(.5))yield return true;
            pl.ComponentMiner.Use(new Ray3(foot+new Vector3(0,1.6f,0),Vector3.Normalize(new Vector3(0,-1.6f,-5f))));
            var u2=proj.Entities.Select(e=>e.FindComponent<ComponentMDUnit>()).First(x=>x?.Mech!=null);k["redeployedKit"]=u2.Mech.Kit;
            if(MDDeploymentBlock.Kit(item)!=2||u2.Mech.Kit!=2||Math.Abs(dealt-1300)>15)throw new Exception("kits: "+string.Join(", ",k.Select(kv=>kv.Key+"="+kv.Value)));
            proj.RemoveEntity(u2.Entity,true);
        }
    }
}
