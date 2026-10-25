using Engine;
using Game;
using GameEntitySystem;
using HarmonyLib;

namespace TowerLab;

// Mode "combat" (0.7.0), in a Harmless world with the user's ghoul and Wonderful Era mods loaded (run_isolated.py copies
// them into the isolated runtime only): friend or foe per creature template; gunfire against the ghouls' bullet defence
// (body, head) and explosion damage; where real rounds hit a ghoul; hits on a running target with and without lead;
// pickaxe dismantle, the worn deploy item, redeploy with the tower's health, and repair (since 0.12.0 with robot scrap; an
// iron ingot is refused on a tower). Dismantle,
// redeploy and repair go through real player input (Hit / Interact injected into ComponentInput), not direct calls.
public partial class Lab {
    static readonly HashSet<Entity> spawned=[];
    static IEnumerable<bool> Combat() {
        var res=new Dictionary<string,object>();results["combat"]=res;var bad=new List<string>();res["failures"]=bad;
        var proj=GameManager.Project;var pl=player.ComponentPlayer;var gw=pl.GameWidget;var towers=MDTowerLibrary.All;
        var T=origin+new Vector3(-17,0,40);
        Remove(placed.Select(p=>p.unit));placed.Clear();Remove(dums.ToList());pl.ComponentBody.Position=Home;
        // wild animals spawn in the lab world and towers would shoot them; the ghoul mod's difficulty dialog (first load) covers
        // the screenshots: closed as with its cancel button (default settings)
        void ClearWild(){DialogsManager.HideAllDialogs();foreach(var c in proj.FindSubsystem<SubsystemCreatureSpawn>(true).Creatures.ToList())if(c is not ComponentPlayer&&!spawned.Contains(c.Entity)&&Vector3.Distance(c.ComponentBody.Position,T)<90)proj.RemoveEntity(c.Entity,true);}
        ComponentCreature Spawn(string name,Vector3 at) {
            if(DatabaseManager.FindEntityValuesDictionary(name,false)==null)return null;
            var e=DatabaseManager.CreateEntity(proj,name,true);e.FindComponent<ComponentBody>(true).Position=at;
            if(e.FindComponent<ComponentSpawn>() is {} sp)sp.SpawnDuration=0;
            proj.AddEntity(e);spawned.Add(e);return e.FindComponent<ComponentCreature>(true);
        }
        void Despawn(ComponentCreature c){if(c!=null){spawned.Remove(c.Entity);if(c.Entity.IsAddedToProject)proj.RemoveEntity(c.Entity,true);}}
        void Pin(ComponentCreature c,Vector3 at){c.ComponentBody.Position=at;c.ComponentBody.Velocity=Vector3.Zero;}
        IEnumerable<bool> For(double seconds,Action each){for(double s0=Time.RealTime;Time.RealTime-s0<seconds;){each();yield return true;}}
        void View(Vector3 eye,Vector3 focus)=>Cam(eye,focus);
        res["numbers"]=towers.Select(d=>$"{d.id}: range {d.attack.range} power {d.attack.power} interval {d.attack.interval} speed {d.attack.speed}").ToList();
        foreach(var x in For(.5,ClearWild))yield return x;

        // ---- friend or foe: each template 12 m in front of a gun tower that holds fire
        var gun=towers.First(t=>t.id=="battle_turret_1");var u=Place(gun,T,0);u.Cooldown=1e6f;
        var spot=T+new Vector3(0,0,-12);var fof=new Dictionary<string,string>();res["faction"]=fof;
        string[] enemies=["Wolf","Rhino","Pirate_Normal","Pirate_Elite","Pirate_Captain","SAGhoul_Weak_01","SAGhoul_Fast_04","SAGhoul_Strong_08","SAGhoul_Monster","SAGhoul_Bear"];
        string[] others=["Pirate_Normal_Tame","Pirate_Elite_Tame","Pirate_Captain_NPC","Bull_Black","Bison","Wildboar"];
        foreach(var (name,want) in enemies.Select(n=>(n,true)).Concat(others.Select(n=>(n,false)))) {
            var c=Spawn(name,spot);
            if(c==null){fof[name]="template missing";bad.Add(name+": template missing");continue;}
            foreach(var x in For(.8,()=>{ClearWild();Pin(c,spot);View(T+new Vector3(6,4,-4),spot);}))yield return x;
            bool got=MDCombat.IsEnemy(u,c.ComponentBody);fof[name]=(got?"enemy":"not a target")+$" (category {c.Category})";
            if(got!=want)bad.Add($"faction {name}: {(got?"enemy":"not a target")}, expected {(want?"enemy":"not a target")}");
            if(name=="Pirate_Elite")Shot("c_pirate_elite");
            Despawn(c);
        }
        {   // the trading pirate becomes a target once it hurts the player, another one once it chases the player
            var npc=Spawn("Pirate_Captain_NPC",spot);
            if(npc!=null) {
                foreach(var x in For(.6,()=>Pin(npc,spot)))yield return x;
                bool before=MDCombat.IsEnemy(u,npc.ComponentBody);
                ComponentMiner.AttackBody(new MeleeAttackment(pl.Entity,npc.Entity,pl.ComponentBody.Position,Vector3.UnitX,.5f));pl.ComponentHealth.Health=1;
                foreach(var x in For(.7,()=>Pin(npc,spot)))yield return x;
                bool after=MDCombat.IsEnemy(u,npc.ComponentBody);fof["Pirate_Captain_NPC hit the player"]=$"before {before}, after {after}";
                if(before||!after)bad.Add($"aggression: before {before}, after {after}");
                Despawn(npc);
                var npc2=Spawn("Pirate_Captain_NPC",spot);var chase=npc2.Entity.Components.FirstOrDefault(c=>c.GetType().GetField("m_target")?.FieldType==typeof(ComponentCreature)&&c.GetType().GetField("m_dayChaseRange")!=null);
                foreach(var x in For(.7,()=>{Pin(npc2,spot);chase?.GetType().GetField("m_target").SetValue(chase,pl);}))yield return x;
                bool chasing=MDCombat.IsEnemy(u,npc2.ComponentBody);fof["Pirate_Captain_NPC chasing the player"]=$"{chasing} ({chase?.GetType().Name})";
                if(!chasing)bad.Add("targeting: a pirate chasing the player is not a target");
                Despawn(npc2);
                // a vanilla animal staring at (or following, avoiding) the player keeps it in m_target too: not a target
                foreach(var name in new[]{"Bull_Black","Bison","Wildboar"}) {
                    var calm=Spawn(name,spot);var look=calm?.Entity.Components.FirstOrDefault(c=>c.GetType().GetField("m_target")?.FieldType==typeof(ComponentCreature)&&c.GetType().GetField("m_dayChaseRange")==null);
                    if(look==null){Despawn(calm);continue;}
                    foreach(var x in For(.7,()=>{Pin(calm,spot);look.GetType().GetField("m_target").SetValue(look,pl);}))yield return x;
                    bool shot=MDCombat.IsEnemy(u,calm.ComponentBody);fof[$"{name} with {look.GetType().Name} on the player"]=shot?"enemy":"not a target";
                    if(shot)bad.Add($"targeting: {name} with {look.GetType().Name}.m_target = player is a target");
                    Despawn(calm);break;
                }
            }
        }
        // ---- damage per attack type on the ghouls' bullet defence: (health lost x resilience) / attack power
        var dmg=new Dictionary<string,object>();res["ghoulDamage"]=dmg;
        foreach(var name in new[]{"SAGhoul_Weak_01","SAGhoul_Strong_08","SAGhoul_Monster"}) {
            var c=Spawn(name,spot);if(c==null){dmg[name]="template missing";continue;}
            foreach(var x in For(.5,()=>{ClearWild();Pin(c,spot);}))yield return x;
            var h=c.ComponentHealth;float res0=h.AttackResilience,P=5;var eye=c.ComponentCreatureModel.EyePosition;var centre=MDCombat.Center(c.ComponentBody);
            float Drop(Vector3 point,bool bullet){h.Health=1;MDTowerBrain.Hit(u,c.ComponentBody,P,point,Vector3.UnitZ,bullet);float r=(1-h.Health)*res0/P;h.Health=1;return MathF.Round(r,3);}
            dmg[name]=new{resilience=res0,bulletBody=Drop(centre,true),bulletHead=Drop(eye,true),explosionOrBeam=Drop(centre,false)};
            Despawn(c);
        }
        // ---- where real rounds hit a pinned ghoul (the head rule: within 0.15 m of the eye height)
        var aim=new Dictionary<string,object>();res["headAim"]=aim;
        foreach(var (tid,seconds) in new[]{("battle_turret_1",7.0),("battle_sniper_1",6.0)}) {
            var t=towers.First(x=>x.id==tid);var tu=Place(t,T+new Vector3(9,0,0),0);tu.Cooldown=1e6f;var gpos=T+new Vector3(9,0,-15);
            var g=Spawn("SAGhoul_Monster",gpos);if(g==null){aim[tid]="no ghoul";Remove([tu]);continue;}
            g.ComponentHealth.AttackResilience=1e6f;AttackLog.Clear();
            foreach(var x in For(.8,()=>{ClearWild();Pin(g,gpos);}))yield return x;
            tu.Cooldown=0;
            foreach(var x in For(seconds,()=>{ClearWild();Pin(g,gpos);View(T+new Vector3(15,3,-6),gpos);}))yield return x;
            Shot("c_headaim_"+tid);
            var eyeY=g.ComponentCreatureModel.EyePosition.Y;var hits=AttackLog.Where(r=>r.attacker==tu.Entity&&r.target==g.Entity).ToList();
            aim[tid]=new{shots=tu.ShotCount,hits=hits.Count,head=hits.Count(r=>MathF.Abs(r.point.Y-eyeY)<=.15f),bullets=hits.Count(r=>r.bullet),eyeY,hitY=hits.Select(r=>MathF.Round(r.point.Y,2)).ToList()};
            if(hits.Count==0||hits.Count(r=>MathF.Abs(r.point.Y-eyeY)<=.15f)*2<hits.Count)bad.Add($"head aim {tid}: {hits.Count(r=>MathF.Abs(r.point.Y-eyeY)<=.15f)} of {hits.Count} hits on the head");
            Despawn(g);Remove([tu]);foreach(var x in For(.4,ClearWild))yield return x;
        }
        Remove([u]);
        // ---- lead: a target running 5 m/s across the line of fire 20 m out (old gun range: 15 m), with and without lead
        var lead=new Dictionary<string,object>();res["lead"]=lead;var leadCounts=new Dictionary<string,(int shots,int hits)>();
        foreach(var tid in new[]{"battle_turret_1","battle_cannon_1"})foreach(var on in new[]{true,false}) {
            var t=towers.First(x=>x.id==tid);var tu=Place(t,T,0);tu.Cooldown=1e6f;
            string runnerName=DatabaseManager.FindEntityValuesDictionary("Pirate_Normal",false)!=null?"Pirate_Normal":"Wolf";
            var r=Spawn(runnerName,T+new Vector3(-9,0,-20));r.ComponentHealth.AttackResilience=1e7f;
            var l=r.ComponentLocomotion;l.WalkSpeed=l.JumpSpeed=l.SwimSpeed=l.FlySpeed=0;
            Vector3 At(double s){double p=(s*5)%36;float x=(float)(p<18?-9+p:27-p);return T+new Vector3(x,0,-20);}   // 5 m/s back and forth over 18 m
            Vector3 Vel(double s)=>new((s*5)%36<18?5:-5,0,0);
            MDTowerBrain.LeadEnabled=on;AttackLog.Clear();
            foreach(var x in For(.6,()=>{ClearWild();Pin(r,At(0));}))yield return x;
            tu.Cooldown=0;int shots0=tu.ShotCount;double s0=Time.RealTime;bool shot=false;
            foreach(var x in For(12,()=>{ClearWild();double s=Time.RealTime-s0;r.ComponentBody.Position=At(s);r.ComponentBody.Velocity=Vel(s);View(T+new Vector3(14,6,-4),T+new Vector3(0,1,-14));}))
                {if(!shot&&on&&Time.RealTime-s0>5){Shot($"c_lead_{tid}");shot=true;}yield return x;}
            tu.Cooldown=1e6f;foreach(var x in For(1.5,()=>{double s=Time.RealTime-s0;r.ComponentBody.Position=At(s);}))yield return x;   // shells in flight land
            int hits=AttackLog.Count(e=>e.attacker==tu.Entity&&e.target==r.Entity);
            lead[$"{tid} lead {(on?"on":"off")}"]=new{runner=runnerName,shots=tu.ShotCount-shots0,hits};leadCounts[tid+on]=(tu.ShotCount-shots0,hits);
            Despawn(r);Remove([tu]);foreach(var x in For(.5,ClearWild))yield return x;
        }
        MDTowerBrain.LeadEnabled=true;
        foreach(var tid in new[]{"battle_turret_1","battle_cannon_1"}) {
            var (a,b)=(leadCounts[tid+true],leadCounts[tid+false]);
            if(a.hits<=b.hits||a.hits*2<a.shots)bad.Add($"lead {tid}: {a.hits}/{a.shots} with lead, {b.hits}/{b.shots} without");
        }
        // ---- every damaging tower damages a pinned wolf at mid range within 6 s (the fire mode only looks 2 s)
        var all=new Dictionary<string,object>();res["everyTowerOnAWolf"]=all;
        foreach(var d in towers.Where(t=>t.attack.power>0&&t.attack.type!="heal")) {
            var tu=Place(d,T,0);tu.Cooldown=1e6f;var a=d.attack;
            float dist=a.directional?Math.Min(a.range*.6f,5):Math.Max(a.minRange+3,Math.Min(a.range*.5f,12));var at=T+new Vector3(0,0,-dist);
            var wolf=Spawn("Wolf",at);wolf.ComponentHealth.AttackResilience=1e5f;var l=wolf.ComponentLocomotion;l.WalkSpeed=l.JumpSpeed=0;
            foreach(var x in For(.8,()=>{ClearWild();Pin(wolf,at);}))yield return x;
            AttackLog.Clear();var fw=MDTowerWorld.For(proj);int imp=fw.Impacts,s0=tu.ShotCount;tu.Cooldown=0;
            foreach(var x in For(6,()=>{ClearWild();Pin(wolf,at);}))yield return x;
            var hits=AttackLog.Where(e=>e.attacker==tu.Entity&&e.target==wolf.Entity).ToList();
            all[d.id]=$"{dist:F1} m: {tu.ShotCount-s0} attacks, {fw.Impacts-imp} landed, {hits.Count} hits, damage {hits.Sum(e=>e.power):F0}";
            if(hits.Count==0)bad.Add($"{d.id} did not damage a wolf at {dist:F1} m in 6 s");
            Despawn(wolf);Remove([tu]);foreach(var x in For(.4,ClearWild))yield return x;
        }
        // ---- dismantle with a pickaxe, the worn item, redeploy, repair (real input)
        var mt=new Dictionary<string,object>();res["maintenance"]=mt;
        var inv=pl.Entity.FindComponent<ComponentInventory>();
        if(inv==null){bad.Add("no survival inventory (world not Harmless?)");}
        else {
            gw.ActiveCamera=gw.FindCamera<FppCamera>();
            var M=T+new Vector3(0,0,0);var tower=Place(gun,M,0);tower.Cooldown=1e6f;tower.SetHitpoints(.62f);
            int pick=Terrain.MakeBlockValue(BlocksManager.GetBlockIndex<IronPickaxeBlock>()),ingot=Terrain.MakeBlockValue(BlocksManager.GetBlockIndex<IronIngotBlock>()),scrap=MDPartBlock.Value(MDPartBlock.Kind.Scrap);
            for(int i=0;i<inv.SlotsCount;i++)inv.RemoveSlotItems(i,inv.GetSlotCount(i));
            inv.AddSlotItems(0,pick,1);inv.AddSlotItems(1,ingot,1);inv.AddSlotItems(2,scrap,3);inv.ActiveSlotIndex=0;
            float depth=tower.Body.BoxSize.Z*.5f;var stand=M+new Vector3(0,0,-depth-.9f);
            Ray3 RayTo(Vector3 p){var eye=pl.ComponentCreatureModel.EyePosition;return new Ray3(eye,Vector3.Normalize(p-eye));}
            void Stand(){pl.ComponentBody.Position=stand;pl.ComponentBody.Velocity=Vector3.Zero;}
            foreach(var x in For(.6,()=>{ClearWild();Stand();}))yield return x;
            var hitsLog=new List<string>();
            for(int k=0;k<MDTowerMaintenance.HitsToDismantle;k++) {
                InjectInput.Hit=RayTo(M+new Vector3(0,1,0));InjectInput.Frames=2;
                foreach(var x in For(.9,Stand))yield return x;
                hitsLog.Add($"hit {k+1}: tower present {tower.Entity.IsAddedToProject}, progress {tower.DismantleHits}, hp {tower.Damage.Hitpoints:F3}");
            }
            mt["dismantleHits"]=hitsLog;
            int pickDamage=BlocksManager.Blocks[Terrain.ExtractContents(pick)].GetDamage(inv.GetSlotValue(0));mt["pickaxeDamage"]=pickDamage;
            if(tower.Entity.IsAddedToProject)bad.Add("dismantle: tower still standing after the pickaxe hits");
            int deployIdx=BlocksManager.GetBlockIndex<MDDeploymentBlock>();
            var drops=proj.FindSubsystem<SubsystemPickables>(true).Pickables.Where(p=>Terrain.ExtractContents(p.Value)==deployIdx).Select(p=>p.Value).ToList();
            foreach(var x in For(1.5,Stand))yield return x;   // the player gathers the dropped item
            int slot=Enumerable.Range(0,inv.SlotsCount).FirstOrDefault(i=>Terrain.ExtractContents(inv.GetSlotValue(i))==deployIdx&&inv.GetSlotCount(i)>0,-1);
            int value=slot>=0?inv.GetSlotValue(slot):drops.FirstOrDefault();
            var block=BlocksManager.Blocks[deployIdx];var terrain=proj.FindSubsystem<SubsystemTerrain>(true);
            mt["item"]=new{droppedAsPickable=drops.Count,inInventorySlot=slot,wear=MDDeploymentBlock.Wear(value),name=block.GetDisplayName(terrain,value),slotHealthBar=block.GetBlockHealth(value)};
            if(MDDeploymentBlock.Wear(value)!=38||MathF.Abs(block.GetBlockHealth(value)-.62f)>.001f)bad.Add($"dismantled item: wear {MDDeploymentBlock.Wear(value)}, bar {block.GetBlockHealth(value)}");
            {   // a worn gun tower does not fit the upgrade recipe (扩装铳械塔 takes mddeploy:0); a full one does
                var up=CraftingRecipesManager.Recipes.First(r=>r.ResultValue==Terrain.MakeBlockValue(deployIdx,0,3));
                string[] Grid(int v)=>up.Ingredients.Select(i=>i==null?null:i.StartsWith("mddeploy")?"mddeploy:"+Terrain.ExtractData(v):i.Contains(':')?i:i+":0").ToArray();
                var worn=CraftingRecipesManager.FindMatchingRecipe(terrain,Grid(value),0,1);var full=CraftingRecipesManager.FindMatchingRecipe(terrain,Grid(MDDeploymentBlock.Value(gun,1)),0,1);
                mt["upgradeWithWornTower"]=$"worn: {(worn==null?"no recipe":worn.ResultValue.ToString())}, full: {(full==null?"no recipe":full.ResultValue.ToString())}";
                if(worn?.ResultValue==up.ResultValue||full?.ResultValue!=up.ResultValue)bad.Add("upgrade: "+mt["upgradeWithWornTower"]);
            }
            if(slot>=0) {
                SettingsManager.ShowGuiInScreenshots=true;inv.ActiveSlotIndex=slot;foreach(var x in For(.5,Stand))yield return x;
                ScreenCaptureManager.Capture(1280,720,"c_worn_item_slot.png");yield return true;SettingsManager.ShowGuiInScreenshots=false;
                // redeploy 4 m to the side: Interact on the ground cell's top face
                var G=M+new Vector3(5,0,0);var cell=new Vector3(MathF.Floor(G.X)+.5f,G.Y-.5f,MathF.Floor(G.Z)+.5f);
                pl.ComponentBody.Position=G+new Vector3(0,0,-2.2f);foreach(var x in For(.4,()=>pl.ComponentBody.Position=G+new Vector3(0,0,-2.2f)))yield return x;
                InjectInput.Interact=RayTo(cell+new Vector3(0,.5f,0));InjectInput.Frames=2;
                foreach(var x in For(.8,()=>pl.ComponentBody.Position=G+new Vector3(0,0,-2.2f)))yield return x;
                var bodies=new DynamicArray<ComponentBody>();proj.FindSubsystem<SubsystemBodies>(true).FindBodiesAroundPoint(new(G.X,G.Z),4,bodies);
                var again=bodies.Select(b=>b.Entity.FindComponent<ComponentMDUnit>()).FirstOrDefault(x=>x!=null);
                mt["redeployed"]=again==null?"no tower":$"{again.Tower.id} hp {again.Damage.Hitpoints:F3}, item left {inv.GetSlotCount(slot)}";
                if(again==null||MathF.Abs(again.Damage.Hitpoints-.62f)>.006f)bad.Add("redeploy: "+mt["redeployed"]);
                if(again!=null) {
                    again.Cooldown=1e6f;inv.ActiveSlotIndex=1;var rep=new List<string>();var at=again.Body.Position+new Vector3(0,1,0);
                    void Near(){pl.ComponentBody.Position=again.Body.Position+new Vector3(0,0,-again.Body.BoxSize.Z*.5f-1.2f);pl.ComponentBody.Velocity=Vector3.Zero;}
                    // click 1 with the iron ingot (refused: towers take robot drops), then three with scrap: 62 % + 400 / 1300 = 93 %,
                    // full, and the third finds it full
                    float afterIron=-1;
                    for(int k=0;k<4;k++) {
                        inv.ActiveSlotIndex=k==0?1:2;
                        foreach(var x in For(.3,Near))yield return x;
                        InjectInput.Interact=RayTo(at);InjectInput.Frames=2;
                        foreach(var x in For(.5,Near))yield return x;   // each click is injected for two input frames: pacing must repair once
                        if(k==0)afterIron=again.Damage.Hitpoints;
                        rep.Add($"click {k+1} ({(k==0?"iron":"scrap")}): hp {again.Damage.Hitpoints:F3}, ingots {inv.GetSlotCount(1)}, scrap {inv.GetSlotCount(2)}");
                    }
                    mt["repair"]=rep;
                    if(MathF.Abs(afterIron-.62f)>.006f||MathF.Abs(again.Damage.Hitpoints-1)>.001f||inv.GetSlotCount(1)!=1||inv.GetSlotCount(2)!=1)bad.Add("repair: "+string.Join("; ",rep));
                    Remove([again]);
                }
            }
        }
        Log("combat done");
        if(bad.Count>0)throw new Exception("combat checks failed: "+string.Join(" | ",bad));
    }
}

// Every attack a tower makes: who, on whom, where, and whether it counted as gunfire.
[HarmonyPatch(typeof(Attackment),nameof(Attackment.ProcessAttackment))]
public static class AttackLog {
    public record struct Entry(Entity attacker,Entity target,Vector3 point,bool bullet,float power);
    static readonly List<Entry> entries=[];
    public static void Clear()=>entries.Clear();
    public static int Count(Func<Entry,bool> f)=>entries.Count(f);
    public static IEnumerable<Entry> Where(Func<Entry,bool> f)=>entries.Where(f);
    public static void Prefix(Attackment __instance){if(__instance.Attacker?.FindComponent<ComponentMDUnit>()!=null&&entries.Count<10000)entries.Add(new(__instance.Attacker,__instance.Target,__instance.HitPoint,__instance is ProjectileAttackment,__instance.AttackPower));}
}
// Real player input for the lab: a Hit or Interact ray for the next input updates.
[HarmonyPatch(typeof(ComponentInput),nameof(ComponentInput.Update))]
public static class InjectInput {
    public static Ray3? Hit,Interact;public static int Frames;
    public static void Postfix(ComponentInput __instance) {
        if(Frames<=0)return;
        if(Hit.HasValue)__instance.m_playerInput.Hit=Hit;
        if(Interact.HasValue)__instance.m_playerInput.Interact=Interact;
        if(--Frames==0)Hit=Interact=null;
    }
}
