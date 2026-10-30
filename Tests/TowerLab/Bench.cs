using Engine;
using Game;

namespace TowerLab;

// lab --mode bench (0.14.0, measures only, changes nothing; user, 2026-10-30: numbers before the 0.15 design): real fights in the
// game, nobody dies (both sides' resilience x 100, health read and set back to full every frame: health lost x resilience is
// the attack power that landed, and the fight goes on).
// bench-towers: every tower against a walking RD05 and a flying RD09, each starting outside both ranges; bench-enemies: each
// regular robot against tier-1 / 2 / 3 gun towers; bench-bosses: each boss against four tier-2 towers for 45 s; bench-fortress:
// each boss with its real armour against ten tier-2 / 3 towers and three medic towers, nothing reset, until it dies or for 120 s;
// bench-sample: six duels and one boss of the above, to check another API version against a full run. Per duel:
// effective damage per second both ways (from the first hit), time to kill / to destroy with the real health, the hits the
// robot took before its first shot, its distance then, and the share of time it stood stunned (the engine's hit stun) or was
// held / slowed by a tower status. Results under "bench" (rows), one line each.
public partial class Lab {
    static IEnumerable<bool> Bench() {
        Remove(placed.Select(p=>p.unit));placed.Clear();while(Wait(1))yield return true;
        var r=new Dictionary<string,object>();results["bench"]=r;var proj=GameManager.Project;var pl=player.ComponentPlayer;var w=MDTowerWorld.For(proj);
        bool Part(string p)=>mode=="bench"||mode=="bench-"+p;
        const float Big=100;   // both sides take 1 / Big of each hit: no hit kills, the health read stays exact
        void Clear(){foreach(var c in proj.FindSubsystem<SubsystemCreatureSpawn>(true).Creatures.ToList())if(c is not ComponentPlayer&&c.Entity.IsAddedToProject)proj.RemoveEntity(c.Entity,true);}
        ComponentMDRobot Spawn(string t,Vector3 at,float yaw) {
            var e=DatabaseManager.CreateEntity(proj,t,true);var b=e.FindComponent<ComponentBody>(true);b.Position=at;b.Rotation=Quaternion.CreateFromAxisAngle(Vector3.UnitY,yaw);
            var sp=e.FindComponent<ComponentSpawn>(true);sp.SpawnDuration=0;sp.AutoDespawn=false;   // the game packs away creatures 52 m from every player
            proj.AddEntity(e);return e.FindComponent<ComponentMDRobot>(true);
        }
        pl.ComponentBody.Position=Home+new Vector3(40,0,40);
        var terrain=proj.FindSubsystem<SubsystemTerrain>(true).Terrain;
        Vector3 Dir(float a)=>new(-MathF.Sin(a),0,-MathF.Cos(a));   // a body's forward at that yaw
        // generated ground about level with the towers, not water (an RD07 58 m out ended 26 m down in water whatever the checks on the
        // top block said: the same flat ground as the towers is the safe test)
        bool Dry(Vector3 at){int x=Terrain.ToCell(at.X),z=Terrain.ToCell(at.Z);return terrain.GetChunkAtCell(x,z) is {State:>=TerrainChunkState.InvalidVertices1}&&MathF.Abs(terrain.GetTopHeight(x,z)+1-Home.Y)<3&&BlocksManager.Blocks[Terrain.ExtractContents(terrain.GetCellValueFast(x,terrain.GetTopHeight(x,z),z))] is not FluidBlock;}
        void Kill(ComponentMDRobot x){if(x.Entity.IsAddedToProject)proj.RemoveEntity(x.Entity,true);}
        void ClearPickables(){foreach(var p in proj.FindSubsystem<SubsystemPickables>(true).Pickables.ToList())p.ToRemove=true;}
        // one tower (or several) against one robot; returns a row
        // `inside`: the robot starts inside the tower's reach (the approach is measured in bench-enemies)
        IEnumerable<bool> Duel(MDTowerDef[] defs,string template,double seconds,Dictionary<string,object> row,bool boss=false,bool inside=false) {
            Clear();while(Wait(.3))yield return true;
            // the robot comes from a side with dry ground all the way (an RD07 58 m out stood in the sea and drowned, bench 2026-10-30);
            // the towers face it (a directional flame trap placed facing away never fired)
            float reach=defs.Max(d=>d.attack.range);
            float start=boss?0:inside?Math.Max(4,reach-3):Math.Max(reach,MDRobots.KindOf(template).Range)+14;
            // the side with the longest dry, level ground (checked every metre); the lab's land is an island: no side has 58 m, and an RD07
            // put there sank in the sea (bench 2026-10-30), so the start comes in to 2 m inside the shore when it must
            float Land(float a){int i=1;while(i<80&&Dry(Home+Dir(a)*i))i++;return i-1;}
            float yaw=new[]{0f,MathF.PI*.5f,-MathF.PI*.5f,MathF.PI}.OrderByDescending(Land).First();
            if(!boss&&start>Land(yaw)-2){row["startCut"]=$"{start:0}->{Land(yaw)-2:0}";start=Land(yaw)-2;}
            // the player stands beside the field, half way: terrain is kept loaded round the player (an RD07 58 m out, 105 m from the
            // player, fell through the ground into water); in Creative neither side aims at the player
            pl.ComponentBody.Position=Home+Dir(yaw)*start*.5f+Dir(yaw+MathF.PI*.5f)*14;pl.ComponentBody.Position=new Vector3(pl.ComponentBody.Position.X,terrain.GetTopHeight(Terrain.ToCell(pl.ComponentBody.Position.X),Terrain.ToCell(pl.ComponentBody.Position.Z))+1.1f,pl.ComponentBody.Position.Z);pl.ComponentBody.Velocity=Vector3.Zero;
            var towers=defs.Select((d,i)=>Place(d,Home+Vector3.Transform(new Vector3((i-(defs.Length-1)*.5f)*7,0,0),Quaternion.CreateFromAxisAngle(Vector3.UnitY,yaw)),yaw)).ToList();while(Wait(.5))yield return true;
            ComponentMDRobot rb;
            if(boss)rb=MDBoss.Spawn(proj,Home,null,template);
            else{var at=Home+Dir(yaw)*start;at.Y=terrain.GetTopHeight(Terrain.ToCell(at.X),Terrain.ToCell(at.Z))+1.05f;rb=Spawn(template,at,yaw+MathF.PI);}   // on the surface (an RD07 put inside a hill was crushed)
            float hp=rb.Health.AttackResilience;rb.Health.AttackResilience=hp*Big;
            var thp=towers.Select(t=>t.Damage.AttackResilience).ToArray();foreach(var t in towers)t.Damage.AttackResilience*=Big;
            string firstHitAt="";double t0=Time.RealTime,firstHit=-1,firstShot=-1,firstTowerHit=-1;float dmgOn=0,dmgBy=0,distAtShot=-1;int hitsBefore=0,frames=0,stunned=0,held=0,slowed=0;float distAtHit=-1,farthest=0;
            int breaks0=boss?rb.Boss.Breaks:0;double firstBreak=-1;float windowTaken=0,taken0=boss?rb.Boss.Taken:0,lastTaken=taken0;
            foreach(var x in During(seconds,()=>{
                if(!rb.Entity.IsAddedToProject)return;
                float d=(1-rb.Health.Health)*rb.Health.AttackResilience;rb.Health.Health=1;   // health lost x raised resilience = the attack power
                if(d>0){dmgOn+=d;if(firstHit<0){firstHit=Time.RealTime;firstHitAt=$"{rb.Body.Position-Home} immersion {rb.Body.ImmersionFactor:0.00} crushed {rb.Body.CrushedTime:0.00} standing {rb.Body.StandingOnValue.HasValue} big {d:0}";}if(rb.ShotsFired==0)hitsBefore++;}
                float near=towers.Min(t=>Vector3.Distance(t.Body.Position,rb.Body.Position));if(distAtHit<0&&(firstHit>=0||towers.Any(t=>near<=t.Tower.attack.range)))distAtHit=near;   // first hit or in reach
                if(distAtHit>=0)farthest=Math.Max(farthest,near);
                if(rb.ShotsFired>0&&firstShot<0){firstShot=Time.RealTime;distAtShot=towers.Min(t=>Vector3.Distance(t.Body.Position,rb.Body.Position));}
                foreach(var t in towers){float td=(1-t.Damage.Hitpoints)*t.Damage.AttackResilience;if(td>0){dmgBy+=td;if(firstTowerHit<0)firstTowerHit=Time.RealTime;t.Damage.Hitpoints=1;}}
                // control is counted from the first hit or once the robot is in a tower's reach (a control tower does no damage)
                if(firstHit>=0||towers.Any(t=>Vector3.Distance(t.Body.Position,rb.Body.Position)<=t.Tower.attack.range+1)){frames++;if(rb.Creature.ComponentLocomotion.StunTime>0)stunned++;float sf=w.SpeedFactor(rb.Body);if(sf<=0)held++;else if(sf<1)slowed++;}
                if(boss){if(rb.Boss.Breaks>breaks0&&firstBreak<0)firstBreak=Time.RealTime-t0;if(rb.Boss.Exposed>0)windowTaken+=rb.Boss.Taken-lastTaken;lastTaken=rb.Boss.Taken;}
                Cam(rb.Body.Position+new Vector3(-16,10,14),(rb.Body.Position+Home)*.5f);
            }))yield return x;
            double end=Time.RealTime;
            float realOn=dmgOn,realBy=dmgBy;
            double onSpan=firstHit<0?0:end-firstHit,bySpan=firstTowerHit<0?0:end-firstTowerHit;float dpsOn=onSpan>0?(float)(realOn/onSpan):0,dpsBy=bySpan>0?(float)(realBy/bySpan):0;
            row["dpsOnRobot"]=MathF.Round(dpsOn,1);row["dpsOnTowers"]=MathF.Round(dpsBy,1);
            row["killSeconds"]=dpsOn>0?MathF.Round(hp/dpsOn,1):-1;row["towerSeconds"]=dpsBy>0?MathF.Round(thp.Sum()/dpsBy,1):-1;
            row["firstHit"]=firstHit<0?-1:Math.Round(firstHit-t0,1);row["firstShot"]=firstShot<0?-1:Math.Round(firstShot-t0,1);row["hitsBeforeShot"]=hitsBefore;row["distAtShot"]=MathF.Round(distAtShot,1);
            row["stunShare"]=frames>0?MathF.Round(stunned/(float)frames,2):0;row["heldShare"]=frames>0?MathF.Round(held/(float)frames,2):0;row["slowShare"]=frames>0?MathF.Round(slowed/(float)frames,2):0;
            if(dpsOn>5000)row["firstHitAt"]=firstHitAt.Replace(' ','_')+"_cause_"+(rb.Health.CauseOfDeath??"-").Replace(' ','_');   // not a tower's damage: what was it
            row["pushedOut"]=distAtHit<0?0:MathF.Round(farthest-distAtHit,1);   // 0.15.0: how far the robot got beyond where it was first hit or came in reach (the water jet)
            row["robotShots"]=rb.ShotsFired;row["hp"]=hp;row["towerHp"]=thp.Sum();
            if(boss){row["armour"]=hp;row["firstBreak"]=firstBreak<0?-1:Math.Round(firstBreak,1);row["breaks"]=rb.Boss.Breaks-breaks0;row["through"]=MathF.Round(rb.Boss.Taken-taken0,0);row["inWindows"]=MathF.Round(windowTaken,0);row["moves"]=string.Join(" ",rb.Boss.Moves.Select(kv=>kv.Key+" "+kv.Value));}
            if(rb.Entity.IsAddedToProject)proj.RemoveEntity(rb.Entity,true);Remove(towers);Clear();
            while(Wait(.5))yield return true;
        }
        string Line(Dictionary<string,object> row)=>string.Join(" ",row.Select(kv=>$"{kv.Key}={kv.Value}"));
        var defs=MDTowerLibrary.All.OrderBy(d=>d.data).ToList();
        if(Part("towers")) {
            var rows=new List<string>();r["towers"]=rows;
            // not measured (known to be 0): medic and radar towers (no damage); a drone for towers that cannot reach the air (cannon) or
            // do no damage (pure control towers: against a walker their stun / hold share is the point)
            foreach(var d in defs)foreach(var t in new[]{"MDRobot05","MDRobot09"}) {
                if(d.attack.type is "heal" or "radar")continue;
                if(t=="MDRobot09"&&(d.attack.groundOnly||d.attack.power<=0))continue;
                var row=new Dictionary<string,object>{["tower"]=d.id,["type"]=d.attack.type,["range"]=d.attack.range,["robot"]=t};
                foreach(var x in Duel([d],t,Math.Max(12,3*d.attack.interval+3),row,inside:true))yield return x;rows.Add(Line(row));   // three shots at least
            }
        }
        if(Part("enemies")) {
            var rows=new List<string>();r["enemies"]=rows;
            foreach(var t in new[]{"MDRobot05","MDRobot06","MDRobot07","MDRobot14","MDRobot09","MDRobotEN02"})foreach(var id in new[]{"battle_turret_1","battle_turret_2","battle_turret_3"}) {
                var d=defs.First(x=>x.id==id);var row=new Dictionary<string,object>{["robot"]=t,["tower"]=id,["range"]=d.attack.range};
                foreach(var x in Duel([d],t,22,row))yield return x;rows.Add(Line(row));
            }
        }
        if(Part("bosses")) {
            var rows=new List<string>();r["bosses"]=rows;var four=new[]{"battle_turret_2","aa_2","cannon_2","missile_2"}.Select(id=>defs.First(x=>x.id==id)).ToArray();
            foreach(var t in MDBoss.Bosses) {
                var row=new Dictionary<string,object>{["boss"]=t,["towers"]=string.Join("+",four.Select(d=>d.id))};
                foreach(var x in Duel(four,t,45,row,true))yield return x;rows.Add(Line(row));
            }
        }
        // bench-rules (0.15.0, user 2026-10-30): the new rules, checked. Tower hits do not stun a robot (gatling: the engine's hit stun
        // kept RD05 stunned 39 % of the time) but the heavy single shots still do (cannon); a robot held by a control tower does not
        // fire (EMP III: it fired 28 shots while held 100 %); the frost tower slows instead of pushing robots out of its own reach (21 %).
        if(Part("rules")) {
            var rows=new List<string>();r["rules"]=rows;var bad=new List<string>();
            foreach(var (id,t) in new[]{("gatling_2","MDRobot05"),("cannon_2","MDRobot05"),("emp_3","MDRobot05"),("frost_2","MDRobot05"),("battle_jet_1","MDRobot05")}) {
                var d=defs.First(x=>x.id==id);var row=new Dictionary<string,object>{["tower"]=id,["robot"]=t};
                foreach(var x in Duel([d],t,Math.Max(12,3*d.attack.interval+3),row,inside:true))yield return x;rows.Add(Line(row));
                float stun=Convert.ToSingle(row["stunShare"]),slow=Convert.ToSingle(row["slowShare"]);int shots=Convert.ToInt32(row["robotShots"]);
                if(id=="gatling_2"&&stun>.03f)bad.Add($"gatling still stuns: {Line(row)}");
                if(id=="cannon_2"&&stun<.02f)bad.Add($"cannon no longer staggers: {Line(row)}");
                if(id=="emp_3"&&shots>4)bad.Add($"held by EMP III and still fired {shots}: {Line(row)}");
                if(id=="frost_2"&&slow<.6f)bad.Add($"frost tower slows only {slow:0.00}: {Line(row)}");
                if(id=="battle_jet_1"&&Convert.ToSingle(row["pushedOut"])<3)bad.Add($"the water jet does not push RD05 back: {Line(row)}");
            }
            if(bad.Count>0)throw new Exception("bench-rules: "+string.Join(" | ",bad));
        }
        // bench-trace: one RD07 against a gun tower, its position every half second (it fell 26 m into water in bench-enemies)
        if(mode=="bench-trace") {
            var log=new List<string>();r["trace"]=log;var d=defs.First(x=>x.id=="battle_turret_1");float start=58;
            float Land(float a){int i=1;while(i<80&&Dry(Home+Dir(a)*i))i++;return i-1;}
            float yaw=new[]{0f,MathF.PI*.5f,-MathF.PI*.5f,MathF.PI}.OrderByDescending(Land).First();start=Math.Min(start,Land(yaw)-2);log.Add($"land {string.Join(" ",new[]{0f,MathF.PI*.5f,-MathF.PI*.5f,MathF.PI}.Select(Land))}");
            var tw=Place(d,Home,yaw);var at=Home+Dir(yaw)*start;at.Y=terrain.GetTopHeight(Terrain.ToCell(at.X),Terrain.ToCell(at.Z))+1.05f;
            log.Add($"yaw {yaw:0.00}, spawn at {at-Home}, top there {terrain.GetTopHeight(Terrain.ToCell(at.X),Terrain.ToCell(at.Z))}, home y {Home.Y}");
            var rb=Spawn("MDRobot07",at,yaw+MathF.PI);double t0=Time.RealTime;int n=0;
            foreach(var x in During(14,()=>{if(Time.RealTime-t0>=n*.5){n++;var b=rb.Body;log.Add($"{Time.RealTime-t0:0.0}s pos {b.Position-Home} vel {b.Velocity} imm {b.ImmersionFactor:0.00} stand {b.StandingOnValue.HasValue} hp {rb.Health.Health:0.00} added {rb.Entity.IsAddedToProject} target {(rb.Target!=null)} hunt {(rb.Hunt!=null)} clip {rb.Anim.Base.name}");}Cam(rb.Body.Position+new Vector3(-10,6,8),rb.Body.Position);}))yield return x;
            Shot("bench_trace_rd07");yield return true;
        }
        // bench-sample: a few of the above, to compare an API version with the full run on another (the numbers are the mod's own)
        if(mode=="bench-sample") {
            var rows=new List<string>();r["sample"]=rows;
            foreach(var (id,t,inside) in new[]{("battle_turret_1","MDRobot05",true),("cannon_3","MDRobot05",true),("missile_2","MDRobot09",true),("battle_laser_2","MDRobot14",true),("emp_2","MDRobot05",true),("battle_turret_2","MDRobot07",false)}) {
                var d=defs.First(x=>x.id==id);var row=new Dictionary<string,object>{["tower"]=id,["robot"]=t};
                foreach(var x in Duel([d],t,inside?12:22,row,inside:inside))yield return x;rows.Add(Line(row));
            }
            var four=new[]{"battle_turret_2","aa_2","cannon_2","missile_2"}.Select(id=>defs.First(x=>x.id==id)).ToArray();
            var brow=new Dictionary<string,object>{["boss"]="MDBoss13"};foreach(var x in Duel(four,"MDBoss13",45,brow,true))yield return x;rows.Add(Line(brow));
        }
        // a large defence (user, 2026-10-30: "我有很多的炮塔和医疗塔配合，敌方boss还是不堪一击"): ten tier-2 / 3 towers and three medic towers
        // round the beacon, each boss with its real armour (scaled to this defence) for up to 120 s; nothing reset
        if(Part("fortress")) {
            var rows=new List<string>();r["fortress"]=rows;
            var ids=new[]{"battle_turret_3","gatling_3","aa_3","cannon_3","missile_3","tesla_3","battle_laser_2","battle_sniper_1","battle_rocket_1","battle_cannon_2"};
            foreach(var t in MDBoss.Bosses) {
                Clear();while(Wait(.3))yield return true;
                var towers=ids.Select((id,i)=>Place(defs.First(x=>x.id==id),Home+new Vector3(MathF.Sin(i*MathF.Tau/ids.Length),0,MathF.Cos(i*MathF.Tau/ids.Length))*9,MathF.PI)).ToList();
                var medics=Enumerable.Range(0,3).Select(i=>Place(defs.First(x=>x.attack.type=="heal"),Home+new Vector3(MathF.Sin(i*MathF.Tau/3),0,MathF.Cos(i*MathF.Tau/3))*3.5f,MathF.PI)).ToList();
                while(Wait(.5))yield return true;
                var boss=MDBoss.Spawn(proj,Home,null,t);float armour=boss.Health.AttackResilience;double t0=Time.RealTime,died=-1;int blocked=0,frames=0;float lowest=1;
                foreach(var x in During(120,()=>{
                    if(died>=0)return;if(boss.Health.Health<=0){died=Time.RealTime-t0;return;}
                    frames++;if(towers.Any(u=>!u.Destroyed&&w.HealBlocked(u.Body)))blocked++;lowest=Math.Min(lowest,towers.Concat(medics).Min(u=>u.Destroyed?0:u.Damage.Hitpoints));
                    Cam(Home+new Vector3(-26,18,22),(boss.Body.Position+Home)*.5f);
                })){if(died>=0)break;yield return x;}
                var row=new Dictionary<string,object>{["boss"]=t,["armour"]=armour,["killedAfter"]=died<0?-1:Math.Round(died,1),["healthLeft"]=MathF.Round(Math.Max(0,boss.Health.Health),2),
                    ["towersLost"]=towers.Count(u=>u.Destroyed),["medicsLost"]=medics.Count(u=>u.Destroyed),["lowestTower"]=MathF.Round(lowest,2),["healBlockedShare"]=frames>0?MathF.Round(blocked/(float)frames,2):0,
                    ["breaks"]=boss.Boss.Breaks,["injuries"]=string.Join(" ",boss.Boss.Injuries.Select(kv=>$"{kv.Key}:{kv.Value:0}")),["moves"]=string.Join(" ",boss.Boss.Moves.Select(kv=>kv.Key+" "+kv.Value))};
                rows.Add(Line(row));
                Shot($"bench_fortress_{t}");yield return true;
                Kill(boss);Remove(towers.Concat(medics));Clear();ClearPickables();while(Wait(.5))yield return true;
            }
        }
        pl.ComponentBody.Position=Home;
    }
}
