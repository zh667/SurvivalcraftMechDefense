using Engine;
using Game;

namespace TowerLab;

// lab --mode bosses14 (0.14.0 bosses, our own models, Mod/MDBoss.cs); bosses14-RD13+RD21 runs only those. For each: its model,
// clips and muzzles load; it arrives the way it does in a game (RD13 through the countdown as the next boss drawn) next to three tier-1 towers (gun, anti-air, cannon) and a player in Survival who hits it once (the player takes no
// harm: resilience 1e6); 40 s of fight with every attack it starts and every hit on the player counted, a screenshot when each
// attack is under way; then the player's answers: a telegraphed attack hits a player who stays and misses one who moves (RD13's
// shells, RD21's dash), RD13's head lens takes x 2.5, RD19's back x 1.5 / front x 0.7 and its main gun broken off by damage, RD08
// backs off from a player close by; it is destroyed, falls / sinks and drops the blueprint. Results under "bosses14"; screenshots b14_*.
public partial class Lab {
    static IEnumerable<bool> Bosses14() {
        Remove(placed.Select(p=>p.unit));placed.Clear();while(Wait(1))yield return true;
        var r=new Dictionary<string,object>();results["bosses14"]=r;var proj=GameManager.Project;var pl=player.ComponentPlayer;var bad=new List<string>();
        var gameInfo=proj.FindSubsystem<SubsystemGameInfo>(true);var terrain=proj.FindSubsystem<SubsystemTerrain>(true);
        var only=mode.Contains('-')?mode[(mode.IndexOf('-')+1)..].Split('+'):null;
        var bosses=new[]{("MDBoss13","RD13"),("MDBoss21","RD21"),("MDBoss08","RD08"),("MDBoss19","RD19")}.Where(b=>only==null||only.Contains(b.Item2)).ToList();
        void Kill(){foreach(var x in proj.Entities.Select(e=>e.FindComponent<ComponentMDRobot>()).Where(x=>x!=null).ToList())proj.RemoveEntity(x.Entity,true);}
        void ClearPickables(){foreach(var p in proj.FindSubsystem<SubsystemPickables>(true).Pickables.ToList())p.ToRemove=true;}
        float Above(ComponentBody b)=>b.Position.Y-terrain.Terrain.GetTopHeight(Terrain.ToCell(b.Position.X),Terrain.ToCell(b.Position.Z))-1;
        float FlatD(Vector3 a,Vector3 b)=>Vector2.Distance(new(a.X,a.Z),new(b.X,b.Z));
        var health=pl.ComponentHealth;float resilience=health.AttackResilience;
        void Hit(ComponentMDRobot boss,Vector3 point,Vector3 dir,float power)=>ComponentMiner.AttackBody(new MeleeAttackment(boss.Entity,pl.Entity,point,dir,power));
        // ---- models: every joint of unit.json in the model, the muzzles the code fires from
        var loads=new List<string>();int col=0;
        foreach(var (t,id) in bosses) {
            var e=DatabaseManager.CreateEntity(proj,t,true);var b=e.FindComponent<ComponentBody>(true);b.Position=Home+new Vector3(col++*12-18,0,-24);e.FindComponent<ComponentSpawn>(true).SpawnDuration=0;proj.AddEntity(e);
            var rb=e.FindComponent<ComponentMDRobot>(true);rb.ForceClip="idle";rb.ForceTime=0;while(Wait(.5))yield return true;
            var m=e.FindComponent<ComponentMDRobotModel>(true);int missing=rb.Def.joints.Count(j=>m.Model?.FindBone(j.name,false)==null);
            var need=id switch{"RD13"=>new[]{"gatlingL","gatlingR","cannonF","cannonB","mgLF","mgLM","mgLB","mgRF","mgRM","mgRB","eye"},"RD21"=>new[]{"railL","railR","gun1","gun2","gun3","gun4","msl1","msl2","msl3","msl4"},
                "RD08"=>new[]{"railL","railR","mgC","mgL","mgR","missileL","missileR"},_=>new[]{"main","hou1","hou2","hou3","hou4","hou5","hou6","msl1","msl2"}};
            var noMuzzle=need.Where(x=>!rb.Def.muzzles.ContainsKey(x)).ToList();
            loads.Add($"{t}: model {(m.Model==null?"none":$"{m.Model.Meshes.Count} meshes, {m.Model.Bones.Count} bones")}, box {b.BoxSize}, missing joints {missing}, missing muzzles [{string.Join(",",noMuzzle)}], clips {string.Join(",",rb.Def.clips.Select(c=>c.name))}");
            if(m.Model==null||missing>0||noMuzzle.Count>0)bad.Add("load "+t);
        }
        r["loads"]=loads;Cam(Home+new Vector3(0,12,4),Home+new Vector3(0,2,-24));Shot("b14_models");yield return true;
        Kill();while(Wait(.5))yield return true;
        var fights=new Dictionary<string,object>();r["fights"]=fights;
        foreach(var (template,id) in bosses) {
            var f=new Dictionary<string,object>();fights[id]=f;
            gameInfo.WorldSettings.GameMode=GameMode.Survival;health.AttackResilience=1e6f;health.Health=1;
            var defs=MDTowerLibrary.All;var towers=new[]{Place(defs.First(t=>t.data==0),Home,0),Place(defs.First(t=>t.id=="aa_1"),Home+new Vector3(-8,0,0),0),Place(defs.First(t=>t.id=="cannon_1"),Home+new Vector3(8,0,0),0)};
            pl.ComponentBody.Position=Home+new Vector3(0,0,14);pl.ComponentBody.Velocity=Vector3.Zero;while(Wait(1))yield return true;
            ComponentMDRobot boss=null;
            if(id=="RD13") {   // through the countdown, as the next boss drawn
                MDBoss.Next=Array.IndexOf(MDBoss.Bosses,template);MDBoss.Start(proj,Home,"{0}");f["countdown"]=MDBoss.Counting(proj);
                foreach(var x in During(34,()=>{boss??=MDBoss.Alive(proj);pl.ComponentBody.Position=Home+new Vector3(0,0,14);}))yield return x;
                f["next"]=$"arrived {boss?.Kind.Id ?? "nothing"}, the next one drawn: {MDBoss.NextTemplate}";
                if(boss?.Kind.Id!="RD13"){bad.Add("countdown: "+f["next"]);continue;}
            }
            else boss=MDBoss.Spawn(proj,Home,null,template);
            var arrive=boss.Body.Position;f["arrival"]=$"{FlatD(arrive,Home):0} m from the towers, {Above(boss.Body):0.0} m up, armour {boss.Health.AttackResilience:0}";
            float wantArmour=MathF.Round((MDBoss.BaseArmour+3000)*boss.Kind.BossArmour);if(boss.Health.AttackResilience!=wantArmour)bad.Add($"{id} armour {boss.Health.AttackResilience} (want {wantArmour})");
            // the player hits it once: it duels the player
            foreach(var x in During(3,()=>{}))yield return x;
            Hit(boss,MDCombat.Center(boss.Body),Vector3.Normalize(MDCombat.Center(boss.Body)-pl.ComponentBody.Position),1);
            f["locked"]=boss.ProvokerNow==pl.ComponentBody;
            // 40 s of fight; a screenshot when each attack is 0.5 s under way
            int hits0=MDRobots.PlayerHits;var th=towers.Select(t=>t.Damage.Hitpoints).ToArray();var heights=new List<float>();var towerD=new List<float>();
            var shots=new Dictionary<string,double>();double t0=Time.RealTime;float walked=0;var lastPos=boss.Body.Position;string last="";double since=0;var clips=new HashSet<string>();
            foreach(var x in During(40,()=>{
                if(boss.Health.Health<=0)return;
                pl.ComponentBody.Position=Home+new Vector3(0,0,14);pl.ComponentBody.Velocity=Vector3.Zero;health.Health=1;
                heights.Add(Above(boss.Body));walked+=FlatD(boss.Body.Position,lastPos);lastPos=boss.Body.Position;towerD.Add(FlatD(boss.Body.Position,Home));clips.Add(boss.Anim.Base.name);
                var now=boss.Boss.Now;if(now!=last){last=now;since=Time.RealTime;}
                var mid=(boss.Body.Position+pl.ComponentBody.Position)*.5f;Cam(mid+new Vector3(-18,12,16),mid+Vector3.UnitY*2);
                if(now.Length>0&&!shots.ContainsKey(now)&&Time.RealTime-since>.5){shots[now]=Time.RealTime-t0;Shot($"b14_{id}_{now.Replace('/','_')}");}
            }))yield return x;
            f["moves"]=string.Join(", ",boss.Boss.Moves.Select(kv=>$"{kv.Key} {kv.Value}"));f["shots"]=string.Join(", ",shots.Keys);f["clips"]=string.Join(",",clips);
            f["fight"]=$"hits on the player {MDRobots.PlayerHits-hits0}, shots fired {boss.ShotsFired}, towers {string.Join(" ",th.Select((h,i)=>$"{h:0.00}->{towers[i].Damage.Hitpoints:0.00}"))}, boss health {boss.Health.Health:0.00}, layers {boss.Boss.LayersLeft}, height {(heights.Count>0?heights.Average():0):0.0} m (min {(heights.Count>0?heights.Min():0):0.0}), from the towers {(towerD.Count>0?towerD.Average():0):0} m";
            int kinds=boss.Boss.Moves.Count;
            if(MDRobots.PlayerHits-hits0==0||kinds<2||!(bool)f["locked"])bad.Add($"{id} fight: {f["fight"]}; moves {f["moves"]}");
            if(heights.Count==0)bad.Add($"{id}: no samples");
            else if(id=="RD21"&&(heights.Average()<2||heights.Average()>7))bad.Add($"{id} height {heights.Average():0.0}");
            else if(id=="RD08"&&(heights.Average()<3.5f||heights.Average()>9))bad.Add($"{id} height {heights.Average():0.0}");
            if(id=="RD19"&&FlatD(boss.Body.Position,arrive)>.5f)bad.Add($"{id} moved {FlatD(boss.Body.Position,arrive):0.0} m");
            if(id=="RD13"&&walked<10)bad.Add($"{id} did not walk: {walked:0.0} m in 40 s");
            f["walked"]=walked;
            // ---- the player's answers
            foreach(var x in proj.Entities.Select(e=>e.FindComponent<ComponentMDRobot>()).Where(x=>x!=null&&x!=boss).ToList())proj.RemoveEntity(x.Entity,true);   // its escorts
            boss.Boss.SetArmour(boss.Boss.Armour);boss.Boss.Exposed=0;boss.Boss.Stagger=0;boss.Health.Health=1;
            var ans=new List<string>();f["answers"]=ans;
            // a forced attack; the player stays, or steps 10 m sideways (across the line from the boss) once moveAway() says so
            IEnumerable<bool> Attack(string move,Func<bool> moveAway,double seconds,Action<bool> done) {
                bool moved=false;int h0=MDRobots.PlayerHits;MDRobots.PlayerHitLog.Clear();var stand=Home+new Vector3(0,0,14);boss.Boss.Force(move,pl.ComponentBody);var trace=new List<string>();
                foreach(var x in During(seconds,()=>{
                    if(!moved&&moveAway!=null&&moveAway()){moved=true;trace.Add($"moved at {boss.Boss.Now} from ({stand.X:0.0},{stand.Z:0.0})");var to=stand-boss.Body.Position;var side=Vector3.Normalize(Vector3.Cross(Vector3.UnitY,new Vector3(to.X,0,to.Z)));if(FlatD(stand-side*10,Home)>FlatD(stand+side*10,Home))side=-side;stand+=side*10;stand.Y=terrain.Terrain.GetTopHeight(Terrain.ToCell(stand.X),Terrain.ToCell(stand.Z))+1.1f;}
                    pl.ComponentBody.Position=stand;pl.ComponentBody.Velocity=Vector3.Zero;health.Health=1;}))yield return x;
                trace.Add($"stood at ({stand.X:0.0},{stand.Z:0.0}), boss at ({boss.Body.Position.X:0.0},{boss.Body.Position.Z:0.0}){(boss.Boss is MDBirdState bs?" spots "+bs.Spots:"")}; hits: {string.Join(", ",MDRobots.PlayerHitLog)}");
                ans.Add(move+": "+string.Join("; ",trace));
                done(MDRobots.PlayerHits>h0);
            }
            if(id=="RD13") {
                var bird=(MDBirdState)boss.Boss;
                // the head lens: the same 100 through it and into the hip
                var eye=boss.Point("eye",out _);var core=boss.Point("core",out _);var from=pl.ComponentBody.Position+Vector3.UnitY*1.6f;
                float h0=boss.Health.Health;Hit(boss,eye,Vector3.Normalize(eye-from),100);float dEye=h0-boss.Health.Health;h0=boss.Health.Health;Hit(boss,core,Vector3.Normalize(core-from),100);float dCore=h0-boss.Health.Health;
                ans.Add($"lens {dEye*boss.Health.AttackResilience:0.0} vs hip {dCore*boss.Health.AttackResilience:0.0} (x {dEye/Math.Max(1e-9f,dCore):0.00}), lens hits {bird.EyeHits}");
                if(MathF.Abs(dEye/dCore-MDBirdState.WeakFactor)>.05f)bad.Add("RD13 lens: "+ans[^1]);
                // the shells: the player in a ring stays (hit) / moves out when the rings appear (missed)
                foreach(var x in During(3,()=>{}))yield return x;
                bool stayHit=false,moveHit=true;
                foreach(var x in Attack("barrage",null,5.5,h=>stayHit=h))yield return x;
                foreach(var x in During(2.5,()=>{}))yield return x;   // the first volley's rings gone
                foreach(var x in Attack("barrage",()=>bird.Spots.Length>0,5.5,h=>moveHit=h))yield return x;   // once its own spots are picked (a sweep's ring may still be up)
                ans.Add($"back cannons: stayed in the ring -> hit {stayHit}, left the ring -> hit {moveHit}");
                if(!stayHit||moveHit)bad.Add("RD13 shells: "+ans[^1]);
                int st0=bird.Stomped;foreach(var x in Attack("leap",null,5,_=>{}))yield return x;ans.Add($"leap: {bird.Stomped-st0} bodies in the blast, {FlatD(boss.Body.Position,pl.ComponentBody.Position):0} m from the player after it");
                if(bird.Stomped==st0)bad.Add("RD13 leap: "+ans[^1]);
            }
            if(id=="RD21") {
                var sabre=(MDSabreState)boss.Boss;bool stayHit=false,moveHit=true;
                foreach(var x in During(3,()=>{}))yield return x;
                double tf=0;foreach(var x in Attack("dash",null,4,h=>stayHit=h))yield return x;
                foreach(var x in During(1,()=>{}))yield return x;
                tf=Time.RealTime;foreach(var x in Attack("dash",()=>Time.RealTime-tf>MDSabreState.DashTell*.85f,4,h=>moveHit=h))yield return x;
                ans.Add($"dash: stayed -> hit {stayHit}, stepped aside after the line turned white -> hit {moveHit}; cuts {sabre.Cuts}");
                if(!stayHit||moveHit)bad.Add("RD21 dash: "+ans[^1]);
            }
            if(id=="RD08") {
                var boat=(MDGunboatState)boss.Boss;
                var near=boss.Body.Position+Vector3.Normalize(new Vector3(Home.X-boss.Body.Position.X,0,Home.Z-boss.Body.Position.Z))*8;near.Y=terrain.Terrain.GetTopHeight(Terrain.ToCell(near.X),Terrain.ToCell(near.Z))+1.1f;
                int mg0=boss.Boss.Moves.GetValueOrDefault("mg");float d0=FlatD(boss.Body.Position,near);
                foreach(var x in During(6,()=>{pl.ComponentBody.Position=near;pl.ComponentBody.Velocity=Vector3.Zero;health.Health=1;}))yield return x;
                ans.Add($"a player 8 m off: {d0:0} m -> {FlatD(boss.Body.Position,near):0} m after 6 s, machine-gun bursts {boss.Boss.Moves.GetValueOrDefault("mg")-mg0}; drones sent {boat.DronesSent}");
                if(FlatD(boss.Body.Position,near)<d0+6||boss.Boss.Moves.GetValueOrDefault("mg")==mg0||boat.DronesSent<2)bad.Add("RD08 evade: "+ans[^1]);
            }
            if(id=="RD19") {
                var siege=(MDSiegeState)boss.Boss;siege.Force("up");foreach(var x in During(.5,()=>{}))yield return x;
                var c=MDCombat.Center(boss.Body);var fwd=boss.Body.Matrix.Forward;
                float h0=boss.Health.Health;Hit(boss,c,fwd,100);float dBack=h0-boss.Health.Health;h0=boss.Health.Health;Hit(boss,c,-fwd,100);float dFront=h0-boss.Health.Health;
                ans.Add($"from behind {dBack*boss.Health.AttackResilience:0.0}, from the front {dFront*boss.Health.AttackResilience:0.0} (x {dBack/Math.Max(1e-9f,dFront):0.00})");
                if(MathF.Abs(dBack/dFront-MDSiegeState.RearFactor/MDSiegeState.FrontFactor)>.05f)bad.Add("RD19 facing: "+ans[^1]);
                boss.Boss.SetArmour(boss.Boss.Armour);siege.Force("up");siege.Force("main");int i0=siege.Interrupts,m0=siege.MainShots;
                foreach(var x in During(1.2,()=>{}))yield return x;Cam(boss.Body.Position+new Vector3(-12,8,10),boss.Body.Position+Vector3.UnitY*2);Shot("b14_RD19_main_charge");yield return true;
                for(int k=0;k<20&&siege.Interrupts==i0;k++)Hit(boss,c,fwd,MDSiegeState.MainBreak*boss.Boss.Armour);   // from behind, a few times the threshold over the plate
                foreach(var x in During(3,()=>{}))yield return x;
                ans.Add($"main gun charge under fire: interrupted {siege.Interrupts-i0}, fired {siege.MainShots-m0}, stagger {boss.Boss.Stagger:0.0} s");
                if(siege.Interrupts==i0||siege.MainShots>m0)bad.Add("RD19 interrupt: "+ans[^1]);
            }
            // ---- destroyed: it falls or sinks and drops the blueprint
            pl.ComponentBody.Position=Home+new Vector3(-40,0,40);pl.ComponentBody.Velocity=Vector3.Zero;foreach(var x in During(.3,()=>{}))yield return x;   // not where the loot falls: a player picks it up
            var at=boss.Body.Position;boss.Health.Health=.01f;boss.Health.Injure(2f,null,true,"lab");
            foreach(var x in During(2,()=>Cam(boss.Body.Position+new Vector3(-14,8,12),boss.Body.Position+Vector3.UnitY)))yield return x;
            Shot($"b14_{id}_destroyed");yield return true;
            foreach(var x in During(5,()=>{}))yield return x;
            var drops=proj.FindSubsystem<SubsystemPickables>(true).Pickables.Where(p=>Vector3.Distance(p.Position,boss.Body.Position)<16||Vector3.Distance(p.Position,at)<16).ToList();
            bool print=drops.Any(d=>MDPartBlock.Of(d.Value)==MDPartBlock.Kind.Blueprint);f["death"]=$"clip {boss.Anim.Base.name}, {Above(boss.Body):0.0} m up after 7 s, blueprint {print}, corpse gone {!boss.Entity.IsAddedToProject}";
            if(!print)bad.Add($"{id} death: {f["death"]}");
            Kill();Remove(towers);ClearPickables();gameInfo.WorldSettings.GameMode=GameMode.Creative;health.AttackResilience=resilience;health.Health=1;
            while(Wait(1))yield return true;
        }
        MDBoss.Next=0;
        if(bad.Count>0)throw new Exception("bosses14: "+string.Join(" | ",bad));
    }
}
