using System.Reflection;
using Engine;
using Game;

namespace TowerLab;

// lab --mode fb18b (0.18.0, second batch of player reports via the user, 2026-11-04). Results under "fb18b"; screenshots f18b_*.
// Written to run on the 0.18.0 first-batch package too (it reads only what that build has), so the same parts give the numbers
// before and after the change:
// squad1 / squad2 (one each, not part of "fb18b": ~9 min each) every attacking tower alone against a squad marching at it (three
//         RD05 walkers, one RD14 tank, one RD09 drone; both sides' health kept full, x100 resilience as in bench): the attack power that
//         landed, the share of robot time held (stunned / frozen) or slowed, the share of robot time within the tower's range,
//         the robots' closest approach, the first effect and the tower's shots.
// spray   every sprayer (flame / jet types) firing along a line of probe wolves 1 m apart: how far its particles go (along the
//         nozzle, from the tower) and the farthest probe it affects; a side screenshot with a post every 5 m and a brick post at
//         its range.
// wave    the sonic tower and the three EMP towers against two robots held 16 m off (beyond the old 9-13 m): shots, who is
//         stunned and how long after the shot; screenshots 0.12 / 0.4 / 0.85 s after the shot.
// ui      the creative inventory's categories (names, which hold this mod's items) with a screenshot of the one with the towers;
//         the handbook: every chapter at 1280x720 and 560x720, tab and heading widths against the space they have.
public partial class Lab {
    static IEnumerable<bool> Feedback18b() {
        Remove(placed.Select(p=>p.unit));placed.Clear();while(Wait(1))yield return true;
        var r=new Dictionary<string,object>();results["fb18b"]=r;var bad=new List<string>();results["fb18bbad"]=bad;
        bool Part(string p)=>mode=="fb18b"&&p is "spray" or "wave" or "ui" or "mortar" or "turn"||mode=="fb18b-"+p;
        var proj=GameManager.Project;var pl=player.ComponentPlayer;var w=MDTowerWorld.For(proj);var terrain=proj.FindSubsystem<SubsystemTerrain>(true);
        var keep=new HashSet<GameEntitySystem.Entity>();
        void Tidy(){foreach(var e in proj.Entities.Where(e=>e.FindComponent<ComponentCreature>()!=null&&e.FindComponent<ComponentPlayer>()==null&&!keep.Contains(e)&&!dums.Any(d=>d.Entity==e)).ToList())proj.RemoveEntity(e,true);}
        void ClearAll(){foreach(var e in proj.Entities.Where(e=>e.FindComponent<ComponentCreature>()!=null&&e.FindComponent<ComponentPlayer>()==null).ToList())proj.RemoveEntity(e,true);keep.Clear();dums.Clear();}
        IEnumerable<bool> Seconds(double s,Action each=null){for(double s0=Time.RealTime;Time.RealTime-s0<s;){each?.Invoke();yield return true;}}
        Vector3 Dir(float a)=>new(-MathF.Sin(a),0,-MathF.Cos(a));   // a body's forward at that yaw
        ComponentMDRobot Robot(string t,Vector3 at,float yaw) {
            var e=DatabaseManager.CreateEntity(proj,t,true);var b=e.FindComponent<ComponentBody>(true);b.Position=at;b.Rotation=Quaternion.CreateFromAxisAngle(Vector3.UnitY,yaw);
            var sp=e.FindComponent<ComponentSpawn>(true);sp.SpawnDuration=0;sp.AutoDespawn=false;proj.AddEntity(e);keep.Add(e);return e.FindComponent<ComponentMDRobot>(true);
        }
        float Top(Vector3 at)=>terrain.Terrain.GetTopHeight(Terrain.ToCell(at.X),Terrain.ToCell(at.Z))+1.05f;
        string Line(Dictionary<string,object> row)=>string.Join(" ",row.Select(kv=>$"{kv.Key}={kv.Value}"));
        var defs=MDTowerLibrary.All.OrderBy(d=>d.data).ToList();
        var attackers=defs.Where(d=>d.attack.type is not ("heal" or "radar" or "core")).ToList();
        r["attacks"]=attackers.Select(d=>$"{d.id} {d.name} {d.attack.type} range {d.attack.range:0.#} power {d.attack.power:0.#} interval {d.attack.interval:0.#} splash {d.attack.splash:0.#} zone {d.attack.zone:0.#} stun {d.attack.stun:0.#} slow {d.attack.slow:0.##} freeze {d.attack.freeze:0.#} speed {d.attack.speed:0}").ToList();
        pl.ComponentBody.Position=Home+new Vector3(-12,0,0);
        bool after=MDTowerLibrary.All.Any(d=>d.attack.type=="wave");r["build"]=after?"0.18.0 second batch":"0.18.0 first batch";

        // ---------------------------------------------------------------- squad
        foreach(var part in new[]{"squad1","squad2","mortar"}) {
            if(!Part(part))continue;
            var rows=new List<string>();r[part]=rows;
            // mortar: the heavy mortar twice, taking flying targets (0.18.0 first batch) and not (attack.groundOnly)
            var mortar=MDTowerLibrary.Get("battle_rocket_1");bool ground0=mortar.attack.groundOnly;
            var list=part=="mortar"?new List<MDTowerDef>{mortar,mortar}:part=="squad1"?attackers.Take((attackers.Count+1)/2).ToList():attackers.Skip((attackers.Count+1)/2).ToList();int iter=0;
            bool Dry(Vector3 at){int x=Terrain.ToCell(at.X),z=Terrain.ToCell(at.Z);var t=terrain.Terrain;return t.GetChunkAtCell(x,z) is {State:>=TerrainChunkState.InvalidVertices1}&&MathF.Abs(t.GetTopHeight(x,z)+1-Home.Y)<3&&BlocksManager.Blocks[Terrain.ExtractContents(t.GetCellValueFast(x,t.GetTopHeight(x,z),z))] is not FluidBlock;}
            float Land(float a){int i=1;while(i<60&&Dry(Home+Dir(a)*i))i++;return i-1;}
            float yaw=new[]{0f,MathF.PI*.5f,-MathF.PI*.5f,MathF.PI}.OrderByDescending(Land).First();float start=Math.Min(34,Land(yaw)-2);
            r[part+"Field"]=$"yaw {yaw:0.00}, start {start:0} m";
            const float Big=100;var kinds=new[]{"MDRobot05","MDRobot05","MDRobot05","MDRobot14","MDRobot09"};
            foreach(var d in list) {
                if(part=="mortar")d.attack.groundOnly=iter==1;iter++;
                ClearAll();while(Wait(.3))yield return true;
                var side=Dir(yaw+MathF.PI*.5f);
                pl.ComponentBody.Position=Home+Dir(yaw)*start*.5f+side*16;pl.ComponentBody.Position=new Vector3(pl.ComponentBody.Position.X,Top(pl.ComponentBody.Position),pl.ComponentBody.Position.Z);pl.ComponentBody.Velocity=Vector3.Zero;
                var u=Place(d,Home,yaw);while(Wait(.5))yield return true;
                float thp=u.Damage.AttackResilience;u.Damage.AttackResilience*=Big;
                var squad=new List<ComponentMDRobot>();
                for(int i=0;i<kinds.Length;i++){var at=Home+Dir(yaw)*(start+(i%2)*2.5f)+side*((i-2)*2.8f);at.Y=Top(at)+(kinds[i]=="MDRobot09"?3:0);squad.Add(Robot(kinds[i],at,yaw+MathF.PI));}
                foreach(var rb in squad)rb.Health.AttackResilience*=Big;
                int shots0=u.ShotCount,area0=MDTowerBrain.AreaHits,frames=0,held=0,slowed=0,reach=0,stunned=0,aimed=0,aimedAir=0;float power=0;double t0=Time.RealTime,first=-1;var closest=squad.Select(_=>1e9f).ToArray();
                foreach(var x in Seconds(22,()=>{
                    Tidy();bool any=false;
                    for(int i=0;i<squad.Count;i++) {
                        var rb=squad[i];if(!rb.Entity.IsAddedToProject)continue;
                        float dmg=(1-rb.Health.Health)*rb.Health.AttackResilience;rb.Health.Health=1;if(dmg>0){power+=dmg;any=true;}
                        var dd=rb.Body.Position-u.Body.Position;float h=new Vector2(dd.X,dd.Z).Length();closest[i]=Math.Min(closest[i],h);
                        frames++;if(h<=u.Range)reach++;
                        float sf=w.SpeedFactor(rb.Body);if(sf<=0){held++;any=true;}else if(sf<1){slowed++;any=true;}
                        if(rb.Creature.ComponentLocomotion.StunTime>0)stunned++;
                    }
                    if(any&&first<0)first=Time.RealTime-t0;
                    u.Damage.Hitpoints=1;if(u.Target!=null){aimed++;if(MDTowerBrain.Airborne(u.Target))aimedAir++;}
                    Cam(Home+Dir(yaw)*start*.5f+side*22+Vector3.UnitY*12,Home+Dir(yaw)*start*.4f);
                }))yield return x;
                var row=new Dictionary<string,object>{["tower"]=d.id+(part=="mortar"?(d.attack.groundOnly?"(groundOnly)":"(air too)"):""),["type"]=d.attack.type,["range"]=d.attack.range,
                    ["power"]=MathF.Round(power,0),["dps"]=MathF.Round(power/22,1),["held"]=frames>0?MathF.Round(held/(float)frames,2):0,["slowed"]=frames>0?MathF.Round(slowed/(float)frames,2):0,
                    ["engineStun"]=frames>0?MathF.Round(stunned/(float)frames,2):0,["inRange"]=frames>0?MathF.Round(reach/(float)frames,2):0,
                    ["closest"]=MathF.Round(closest.Where(c=>c<1e8f).DefaultIfEmpty(-1).Average(),1),["nearest"]=MathF.Round(closest.Min(),1),["first"]=first<0?-1:Math.Round(first,1),
                    ["shots"]=u.ShotCount-shots0,["robotShots"]=squad.Sum(q=>q.ShotsFired),["onDrone"]=aimed>0?MathF.Round(aimedAir/(float)aimed,2):0,["sprayHits"]=MDTowerBrain.AreaHits-area0};
                rows.Add(Line(row));
                if(d.id is "battle_shockwave_1" or "emp_2" or "flame_2" or "frost_2" or "battle_jet_1" or "battle_trap_1"){Shot($"f18b_squad_{d.id}");yield return true;}
                foreach(var rb in squad)if(rb.Entity.IsAddedToProject)proj.RemoveEntity(rb.Entity,true);Remove([u]);ClearAll();while(Wait(.4))yield return true;
            }
            mortar.attack.groundOnly=ground0;
        }

        // ---------------------------------------------------------------- spray
        if(Part("spray")) {
            var rows=new List<string>();r["spray"]=rows;
            var psF=typeof(MDTowerWorld).GetField("ps",BindingFlags.NonPublic|BindingFlags.Instance);var pnF=typeof(MDTowerWorld).GetField("pn",BindingFlags.NonPublic|BindingFlags.Instance);
            var pType=typeof(MDTowerWorld).GetNestedType("P",BindingFlags.NonPublic);var posF=pType.GetField("pos");var slotF=pType.GetField("slot");
            var sprayers=attackers.Where(d=>d.attack.type is "flame" or "jet").ToList();
            int granite=Terrain.MakeBlockValue(3);var postName=new[]{"BrickBlock","MalachiteBlock","SandBlock"}.FirstOrDefault(n=>BlocksManager.GetBlockIndex(n,false)>0);int brick=Terrain.MakeBlockValue(postName!=null?BlocksManager.GetBlockIndex(postName,false):3);r["sprayPost"]=postName??"granite";
            const float yaw=0;var fwd=Dir(yaw);var side=new Vector3(-fwd.Z,0,fwd.X);
            foreach(var d in sprayers) {
                ClearAll();Clear((int)Home.X-20,(int)Home.X+30,(int)Home.Z-36,(int)Home.Z+6);while(Wait(.3))yield return true;
                pl.ComponentBody.Position=Home+side*-14+fwd*4;pl.ComponentBody.Velocity=Vector3.Zero;
                var u=Place(d,Home,yaw);u.Cooldown=1e6f;while(Wait(.4))yield return true;
                float range=u.Range;int n=(int)MathF.Ceiling(range)+4;
                var probes=new List<(float at,Dum dum,Vector3 p0)>();
                // probes 1.3 m off the line (alternating sides, inside every band), so they do not hide the aim wolf from the nozzle;
                // the sprayer is held on a wolf on the line 1 m inside its range (it would take the nearest, 2 m off, and spray into the ground)
                for(int k=2;k<=n;k++){var at=Home+fwd*k+side*(k%2==0?1.3f:-1.3f);at.Y=Top(at);var dm=Dummy(at);probes.Add((k,dm,at));}
                var aimAt=Home+fwd*MathF.Floor(range-1);aimAt.Y=Top(aimAt);var aimDum=Dummy(aimAt);var aim=aimDum.Body;
                // the water jet does no damage and its push shoves one probe into the next: for this count it does 1e5 power (a 1e7-resilience wolf shows it) and no push
                float power0=d.attack.power,push0=d.attack.knockback;if(d.attack.power<=0&&d.attack.slow<=0){d.attack.power=1e5f;d.attack.knockback=0;}
                // posts 3 m to the far side: one block every 5 m, a brick post three high at the tower's range
                int gy=Terrain.ToCell(Top(Home)-1.05f)+1;   // the first air cell over the ground
                for(int k=5;k<=n;k+=5){var p=Home+fwd*k-side*3.5f;for(int y=0;y<2;y++)terrain.ChangeCell(Terrain.ToCell(p.X),gy+y,Terrain.ToCell(p.Z),granite);}
                {var p=Home+fwd*range-side*3.5f;for(int y=0;y<4;y++)terrain.ChangeCell(Terrain.ToCell(p.X),gy+y,Terrain.ToCell(p.Z),brick);r[$"post_{d.id}"]=$"{Terrain.ToCell(p.X)},{gy},{Terrain.ToCell(p.Z)}";}
                while(Wait(.6))yield return true;
                u.Cooldown=0;var maxPerFrame=new List<float>();float far=0;int frame=0;double t0=Time.RealTime;bool shot=false;
                foreach(var x in Seconds(2.6,()=>{
                    frame++;if(u.Channel>0)shot=true;u.ScanTimer=1e6f;u.Target=aim;
                    if(frame%3==0) {
                        var arr=(Array)psF.GetValue(w);int pn=(int)pnF.GetValue(w);float m=0;
                        for(int i=0;i<pn;i++){var o=arr.GetValue(i);var s=(MDTowerWorld.S)slotF.GetValue(o);if(s is not (MDTowerWorld.S.Fire or MDTowerWorld.S.Smoke or MDTowerWorld.S.Smoke2 or MDTowerWorld.S.Drop or MDTowerWorld.S.Streak or MDTowerWorld.S.Crystal))continue;
                            var dd=(Vector3)posF.GetValue(o)-u.Body.Position;float along=Vector3.Dot(new Vector3(dd.X,0,dd.Z),fwd);if(MathF.Abs(Vector3.Dot(dd,side))<6)m=Math.Max(m,along);}
                        if(u.Channel>0)maxPerFrame.Add(m);far=Math.Max(far,m);
                    }
                    Cam(Home+fwd*(range*.5f)+side*(range*.7f+4)+Vector3.UnitY*(range*.3f+2),Home+fwd*(range*.5f)+Vector3.UnitY*.8f);
                }))yield return x;
                // the screenshot during a second burst
                u.Cooldown=0;foreach(var x in Seconds(.9,()=>{u.ScanTimer=1e6f;u.Target=aim;Cam(Home+fwd*(range*.5f)+side*(range*.7f+4)+Vector3.UnitY*(range*.3f+2),Home+fwd*(range*.5f)+Vector3.UnitY*.8f);}))yield return x;
                Shot($"f18b_spray_{d.id}");yield return true;
                float hitFar=0;foreach(var (at,dm,p0) in probes){var b=dm.Body;bool moved=Vector3.Distance(new Vector3(b.Position.X,0,b.Position.Z),new Vector3(p0.X,0,p0.Z))>.15f;if(dm.Hit||w.SpeedFactor(b)<1||moved)hitFar=Math.Max(hitFar,at);}
                maxPerFrame.Sort();float median=maxPerFrame.Count>0?maxPerFrame[maxPerFrame.Count/2]:0;
                var row=new Dictionary<string,object>{["tower"]=d.id,["type"]=d.attack.type,["range"]=MathF.Round(range,1),["fired"]=shot,["particlesMedianFar"]=MathF.Round(median,1),["particlesMax"]=MathF.Round(far,1),["probeFar"]=hitFar,["probes"]=probes.Count};
                rows.Add(Line(row));
                // after the change (the waves exist): the stream ends where the hits end (within 1.5 m) and goes no farther than the range + 1.5 m
                if(after&&(!shot||MathF.Abs(median-hitFar)>1.5f||far>range+1.5f||hitFar<range-1.5f))bad.Add("spray: "+Line(row));
                d.attack.power=power0;d.attack.knockback=push0;
                Remove(probes.Select(p=>p.dum));Remove([aimDum]);Remove([u]);
                Clear((int)Home.X-20,(int)Home.X+30,(int)Home.Z-36,(int)Home.Z+6);while(Wait(.3))yield return true;
            }
        }

        // ---------------------------------------------------------------- turn
        // the water jet and the flame trap (0.18.0 round 3: they turn as a whole, turnWhole): placed facing -Z, a wolf 10 m off to the
        // side (+X); how far round they turn, how long it takes, whether they hit it; screenshots before and after the turn
        if(Part("turn")) {
            var rows=new List<string>();r["turn"]=rows;
            foreach(var id in new[]{"battle_jet_1","battle_trap_1"}) {
                var d=MDTowerLibrary.Get(id);ClearAll();Clear((int)Home.X-20,(int)Home.X+30,(int)Home.Z-36,(int)Home.Z+6);while(Wait(.3))yield return true;
                pl.ComponentBody.Position=Home+new Vector3(-10,0,6);pl.ComponentBody.Velocity=Vector3.Zero;
                var u=Place(d,Home,0);u.Cooldown=1e6f;while(Wait(.4))yield return true;
                var at=Home+new Vector3(10,0,0);at.Y=Top(at);var wolf=Dummy(at);float power0=d.attack.power;if(power0<=0)d.attack.power=1e5f;   // the jet does no damage: let the hit show for this count
                float yaw0=u.AimYaw;var eye=Home+new Vector3(3,9,14);var focus=Home+new Vector3(4,0,-1);
                Cam(eye,focus);Shot($"f18b_turn_{id}_0");yield return true;
                u.Cooldown=0;double t0=Time.RealTime,aligned=-1;int a0=MDTowerBrain.AreaHits;
                foreach(var x in Seconds(3,()=>{float want=MathF.Atan2(-(at-Home).X,-(at-Home).Z);if(aligned<0&&MathF.Abs(MDCombat.AngleDelta(u.AimYaw,want))<.12f)aligned=Time.RealTime-t0;Cam(eye,focus);}))yield return x;
                Shot($"f18b_turn_{id}_1");yield return true;
                d.attack.power=power0;
                var row=new Dictionary<string,object>{["tower"]=id,["turned"]=MathF.Round(MathUtils.RadToDeg(MDCombat.AngleDelta(yaw0,u.AimYaw)),0),["alignedAfter"]=aligned<0?-1:Math.Round(aligned,2),["sprayHits"]=MDTowerBrain.AreaHits-a0,["hit"]=wolf.Hit,["turnWhole"]=d.turnWhole};
                rows.Add(Line(row));
                if(after&&(!d.turnWhole||aligned<0||MDTowerBrain.AreaHits-a0<1))bad.Add("turn: "+Line(row));
                Remove([wolf]);Remove([u]);ClearAll();while(Wait(.3))yield return true;
            }
        }

        // ---------------------------------------------------------------- wave
        if(Part("wave")) {
            var rows=new List<string>();r["wave"]=rows;const float yaw=0;var fwd=Dir(yaw);var side=new Vector3(-fwd.Z,0,fwd.X);
            foreach(var id in new[]{"battle_shockwave_1","emp_1","emp_2","emp_3"}) {
                var d=MDTowerLibrary.Get(id);ClearAll();Clear((int)Home.X-20,(int)Home.X+30,(int)Home.Z-36,(int)Home.Z+6);while(Wait(.3))yield return true;
                pl.ComponentBody.Position=Home+side*-14+fwd*4;pl.ComponentBody.Velocity=Vector3.Zero;
                var u=Place(d,Home,yaw);u.Cooldown=1e6f;while(Wait(.4))yield return true;
                var near=new List<(float at,ComponentMDRobot rb)>();
                foreach(var (k,off) in new[]{(16f,-1.5f),(16f,1.5f)}){var at=Home+fwd*k+side*off;at.Y=Top(at);var rb=Robot("MDRobot05",at,yaw+MathF.PI);rb.Health.AttackResilience*=100;var l=rb.Creature.ComponentLocomotion;l.WalkSpeed=l.JumpSpeed=l.FlySpeed=0;near.Add((k,rb));}
                while(Wait(.6))yield return true;
                foreach(var (_,rb) in near){var l=rb.Creature.ComponentLocomotion;l.WalkSpeed=l.JumpSpeed=l.FlySpeed=0;}
                int s0=u.ShotCount;u.Cooldown=0;double fired=-1,t0=Time.RealTime;var stunAt=near.Select(_=>-1.0).ToArray();int shotsTaken=0;
                var eye=Home+fwd*9+side*15+Vector3.UnitY*9;var focus=Home+fwd*9;
                foreach(var x in Seconds(4.5,()=>{
                    Tidy();if(fired<0&&u.ShotCount>s0)fired=Time.RealTime;
                    for(int i=0;i<near.Count;i++){var rb=near[i].rb;rb.Health.Health=1;if(stunAt[i]<0&&fired>=0&&w.IsStunned(rb.Body))stunAt[i]=Time.RealTime-fired;}
                    Cam(eye,focus);
                })){
                    if(fired>=0&&shotsTaken<3){double since=Time.RealTime-fired;double want=new[]{.12,.4,.85}[shotsTaken];if(since>=want){Shot($"f18b_wave_{id}_{shotsTaken}");shotsTaken++;}}
                    yield return x;
                }
                var row=new Dictionary<string,object>{["tower"]=id,["type"]=d.attack.type,["range"]=d.attack.range,["shots"]=u.ShotCount-s0,["firstShot"]=fired<0?-1:Math.Round(fired-t0,2),
                    ["stun16mA"]=Math.Round(stunAt[0],2),["stun16mB"]=Math.Round(stunAt[1],2),["robotShots"]=near.Sum(q=>q.rb.ShotsFired)};
                rows.Add(Line(row));
                // after the change: it fires at 16 m and holds both robots, the wave / orb taking its time to get there
                if(d.attack.type is "wave" or "orb"&&(u.ShotCount-s0<1||stunAt.Any(t=>t<.3)))bad.Add("wave: "+Line(row));
                foreach(var (_,rb) in near)if(rb.Entity.IsAddedToProject)proj.RemoveEntity(rb.Entity,true);Remove([u]);ClearAll();while(Wait(.3))yield return true;
            }
        }

        // ---------------------------------------------------------------- ui
        if(Part("ui")) {
            var ui=new Dictionary<string,object>();r["ui"]=ui;
            // categories: the game builds the list from every block's creative values (BlocksManager.AddCategory); names through
            // the "BlocksManager" language node
            var cats=BlocksManager.Categories.ToList();
            // every block this mod registers (its assembly), all its creative values (0.18.0 round 3, user: 这个模组新加的东西都放到这个分类)
            var modBlocks=BlocksManager.Blocks.Where(b=>b!=null&&b.GetType().Assembly==typeof(MDTowerLibrary).Assembly).ToList();
            ui["modBlocks"]=string.Join(", ",modBlocks.Select(b=>$"{b.GetType().Name} {b.GetCreativeValues().Count()} values"));
            var ours=new HashSet<int>(modBlocks.SelectMany(b=>b.GetCreativeValues()));
            var astray=modBlocks.SelectMany(b=>b.GetCreativeValues().Where(v=>b.GetCategory(v)!=MDCategory.Name).Select(v=>$"{b.GetDisplayName(null,v)} [{b.GetCategory(v)}]")).ToList();
            ui["modItems"]=ours.Count;ui["notInCategory"]=astray.Count==0?"0":string.Join("; ",astray);
            var byCat=new List<string>();string oursIn=null;
            foreach(var c in cats) {
                var vals=new List<int>();foreach(var b in BlocksManager.Blocks.Where(b=>b!=null)){try{vals.AddRange(b.GetCreativeValues().Where(v=>b.GetCategory(v)==c));}catch{}}
                int mine=vals.Count(ours.Contains);if(mine>0)oursIn??=c;
                byCat.Add($"{c} [{LanguageControl.Get("BlocksManager",c)}] {vals.Count} items, {mine} of ours");
            }
            ui["categories"]=byCat;ui["language"]=LanguageControl.CurrentLanguageName;
            var cinvW=new CreativeInventoryWidget(pl.Entity);var widgetCats=cinvW.m_categories.Select(c=>c.Name).ToList();ui["widgetCategories"]=string.Join(" | ",widgetCats.Select(n=>$"{n}={LanguageControl.Get("BlocksManager",n)}"));
            var cinv=pl.Entity.FindComponent<ComponentCreativeInventory>(true);
            SettingsManager.ShowGuiInScreenshots=true;
            foreach(var c in new[]{oursIn??"Weapons","Weapons","Spawn"}.Distinct()) {
                int idx=widgetCats.IndexOf(c);if(idx<0)continue;
                pl.ComponentGui.ModalPanelWidget=cinvW;cinv.CategoryIndex=idx;cinv.PageIndex=0;
                foreach(var x in Seconds(.8,()=>Cam(Home+new Vector3(-8,7,-12),Home+new Vector3(3,1,-1))))yield return x;
                Shot($"f18b_creative_{c.Replace(' ','_')}");yield return true;
                ui[$"shown {c}"]=$"category index {cinv.CategoryIndex}, button text {cinvW.m_categoryButton.Text}";
                if(c==MDCategory.Name){cinv.PageIndex=1;foreach(var x in Seconds(.8,()=>Cam(Home+new Vector3(-8,7,-12),Home+new Vector3(3,1,-1))))yield return x;Shot("f18b_creative_MechDefense_p2");yield return true;cinv.PageIndex=99;foreach(var x in Seconds(.8,()=>Cam(Home+new Vector3(-8,7,-12),Home+new Vector3(3,1,-1))))yield return x;Shot("f18b_creative_MechDefense_last");yield return true;ui["lastPage"]=cinv.PageIndex;}
            }
            pl.ComponentGui.ModalPanelWidget=null;SettingsManager.ShowGuiInScreenshots=false;
            for(int i=0;i<4;i++)yield return true;
            // the handbook, every chapter wide and narrow
            MDManualScreen.Open();var screen=(MDManualScreen)ScreensManager.CurrentScreen;while(Wait(1))yield return true;
            var over=new List<string>();var checks=new List<string>();
            for(int c=0;c<MDManualScreen.Chapters.Length;c++) {
                screen.Chapter=c;for(int i=0;i<4;i++)yield return true;
                // the GUI's virtual width is 850 / UIScale (ScreensManager.LayoutAndDrawWidgets): 1133 at the default 0.75, 708 at the
                // largest setting 1.2 (phones); a 4:3 screen at 1.2 is the narrowest page a player gets
                foreach(var (wd,ht,tag,scale) in new[]{(1280,720,"wide",.75f),(1280,720,"bigui",1.2f),(960,720,"bigui43",1.2f)}) {
                    float ui0=SettingsManager.UIScale;SettingsManager.UIScale=scale;for(int i=0;i<3;i++)yield return true;
                    ScreenCaptureManager.Capture(wd,ht,$"f18b_manual_{c}_{tag}.png");SettingsManager.UIScale=ui0;
                    // right after the capture the widgets hold the layout at that size
                    var buttons=screen.AllChildren.OfType<ButtonWidget>().ToList();float avail=screen.ActualSize.X;
                    foreach(var b in buttons)foreach(var l in b.AllChildren.OfType<LabelWidget>()) {
                        float tw=MDStatusWidget.TextWidth(l.Text,l.FontScale);
                        if(tw>b.ActualSize.X-4)over.Add($"{tag} chapter {c}: button \"{l.Text}\" text {tw:0} in {b.ActualSize.X:0}");
                    }
                    // a heading line (icons + an unwrapped title) wider than the page
                    var page=screen.AllChildren.OfType<ScrollPanelWidget>().Select(s=>s.ActualSize.X).DefaultIfEmpty(avail).Max();
                    foreach(var line in screen.AllChildren.OfType<StackPanelWidget>().Where(s=>s.Direction==LayoutDirection.Horizontal&&s.Children.Any(x=>x is LabelWidget)&&!s.Children.Any(x=>x is ButtonWidget))) {
                        float lw=line.Children.Sum(x=>x is LabelWidget l?MDStatusWidget.TextWidth(l.Text,l.FontScale)+l.Margin.X*2:x.ActualSize.X+x.Margin.X*2);
                        if(lw>page-8)over.Add($"{tag} chapter {c}: line \"{string.Join(" ",line.Children.OfType<LabelWidget>().Select(l=>l.Text))}\" {lw:0} in {page:0}");
                    }
                    checks.Add($"{MDManualScreen.Chapters[c]} {tag}: screen {screen.ActualSize.X:0}, page {page:0}, entries {MDManualScreen.Entries}");
                    yield return true;yield return true;
                }
            }
            ScreensManager.SwitchScreen("Game");for(int i=0;i<4;i++)yield return true;
            ui["manual"]=checks;ui["manualOverflow"]=over.Count==0?"none":over;
            if(after) {
                var catOurs=byCat.FirstOrDefault(x=>x.StartsWith("MechDefense "));
                if(oursIn!="MechDefense"||catOurs==null||astray.Count>0||!catOurs.Contains("[机甲防线]")||!catOurs.Contains($"{ours.Count} items, {ours.Count} of ours")||byCat.Any(x=>!x.StartsWith("MechDefense ")&&!x.EndsWith(" 0 of ours"))||cats.Contains("Spawn"))
                    bad.Add($"ui: category {oursIn}: {string.Join(" | ",byCat)}");
                if(over.Count>0)bad.Add("ui: manual overflow "+string.Join(" | ",over));
            }
        }
        pl.ComponentBody.Position=Home;
        if(bad.Count>0)throw new Exception("fb18b: "+string.Join(" || ",bad));
    }
}
