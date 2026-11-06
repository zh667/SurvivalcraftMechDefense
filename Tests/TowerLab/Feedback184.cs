using System.Reflection;
using Engine;
using Game;

namespace TowerLab;

// lab --mode fb184 (0.18.4, user 2026-11-06). Results under "fb184"; screenshots f184_*. Written against the 0.18.3 API (anything newer
// through reflection), so the same build runs on the 0.18.3 package and shows the failures before the fix.
// healer (user: "治疗无人机会被优先攻击，但还往最前线冲，基本都是直接被秒的下场，希望尽量躲在其他敌对机器人后面，治疗距离可以给远点") four RD05 and two
//        repair drones march on three gun towers (scene guns) and on the same with an anti-air tower (guns+aa), robots x3 health, towers
//        kept whole, up to 60 s. Per drone: how long it lived, its mean distance to the nearest tower, the share of the time (once the
//        squad is within 34 m of a tower) it was at least 3 m farther from the towers than the squad's nearest RD05 (behind), its mean
//        lead over that RD05 (negative = behind), what it mended, when it was first hit; the share of the towers' aim on the drones.
//        After the fix, guns: every drone behind >= 80 % of that time, first hit after the first RD05 was hit, towers aim at the drones
//        <= 25 % of the time while they live, something mended in every scene. guns+aa3: the same with the long-range anti-air
//        tower (42 m), which still picks the drones first when they are in its reach (reported, no limit). Screenshots: the field, and a drone mending (its beam).
// jet    (user: "激流塔修一下") a water jet against one RD05 12 m off (single: the case of the report) and against three RD05 and a drone (squad) for 30 s
//        after its first spray: sprays, sprays that reached someone, the robots' distance to it every 0.5 s, the share of that time the
//        nearest robot was out of its range. After the fix: >= 8 sprays, >= 80 % of them reach, single: out of range <= 10 % of the time.
// rate   (user, the same day: the towers counted their cooldown down twice a frame; fixed with every cooldown source halved, so nothing
//        plays differently) every tower but the core, three at a time 87+ m apart, each with a pinned RD05 in reach (the medic: a hurt
//        gun tower; the radar: a gun tower in reach): the game time of every attack (its cooldown set, a shot counted) for 25 s. The
//        comparison between packages is tools/compare_fire_rate.py (0.18.3 = 0.18.4 within a frame; the fix without the halving: half
//        the rate). Also the radar's boost left right after a pulse.
// text   the help pages and the repair drone's bestiary page (the numbers the players read), and every tower's help line.
public partial class Lab {
    static IEnumerable<bool> Feedback184() {
        Remove(placed.Select(p=>p.unit));placed.Clear();while(Wait(1))yield return true;
        var r=new Dictionary<string,object>();results["fb184"]=r;var bad=new List<string>();results["fb184bad"]=bad;
        bool Part(string p)=>mode=="fb184"||mode=="fb184-"+p;
        var proj=GameManager.Project;var pl=player.ComponentPlayer;var w=MDTowerWorld.For(proj);var terrain=proj.FindSubsystem<SubsystemTerrain>(true);var clock=proj.FindSubsystem<SubsystemTime>(true);
        var keep=new List<ComponentMDRobot>();
        void ClearAll(){foreach(var e in proj.Entities.Where(e=>e.FindComponent<ComponentCreature>()!=null&&e.FindComponent<ComponentPlayer>()==null).ToList())proj.RemoveEntity(e,true);dums.Clear();keep.Clear();}
        IEnumerable<bool> Seconds(double s,Action each=null){for(double s0=Time.RealTime;Time.RealTime-s0<s;){each?.Invoke();yield return true;}}
        Vector3 Dir(float a)=>new(-MathF.Sin(a),0,-MathF.Cos(a));   // a body's forward at that yaw
        string Line(Dictionary<string,object> row)=>string.Join(" ",row.Select(kv=>$"{kv.Key}={kv.Value}"));
        float Top(Vector3 at)=>terrain.Terrain.GetTopHeight(Terrain.ToCell(at.X),Terrain.ToCell(at.Z))+1.05f;
        float Flat(Vector3 a,Vector3 b)=>new Vector2(a.X-b.X,a.Z-b.Z).Length();
        ComponentMDRobot Robot(string t,Vector3 at,float yaw) {
            at.Y=Top(at)+(MDRobots.KindOf(t).Fly>0?3:0);var e=DatabaseManager.CreateEntity(proj,t,true);var b=e.FindComponent<ComponentBody>(true);b.Position=at;b.Rotation=Quaternion.CreateFromAxisAngle(Vector3.UnitY,yaw);
            var sp=e.FindComponent<ComponentSpawn>(true);sp.SpawnDuration=0;sp.AutoDespawn=false;proj.AddEntity(e);var rb=e.FindComponent<ComponentMDRobot>(true);keep.Add(rb);return rb;
        }
        bool Alive(ComponentMDRobot rb)=>rb.Entity.IsAddedToProject&&rb.Health.Health>0;
        r["build"]=typeof(MDTowerBrain).GetField("PushCapEnabled",BindingFlags.Public|BindingFlags.Static)!=null?"0.18.4 (has PushCapEnabled)":"older package";
        var C0=Home;int X0=(int)MathF.Floor(C0.X),Z0=(int)MathF.Floor(C0.Z);
        const float yaw=0;var fwd=Dir(yaw);var side=new Vector3(-fwd.Z,0,fwd.X);

        // ---------------------------------------------------------------- healer
        if(Part("healer")) {
            var hl=new List<string>();r["healer"]=hl;var rows=new Dictionary<string,Dictionary<string,object>>();
            ClearAll();Clear(X0-32,X0+33,Z0-66,Z0+22);while(Wait(1))yield return true;
            var ducksF=typeof(ComponentMDRobot).GetField("Ducks");
            foreach(var (name,ids) in new[]{("guns",new[]{"battle_turret_1","battle_turret_2","gatling_1"}),("guns+aa",new[]{"battle_turret_1","battle_turret_2","gatling_1","aa_1"}),("guns+aa3",new[]{"battle_turret_1","battle_turret_2","gatling_1","aa_3"})}) {
                ClearAll();MDTowerTools.Shown.Clear();while(Wait(.3))yield return true;
                pl.ComponentBody.Position=C0+side*30+fwd*10;pl.ComponentBody.Velocity=Vector3.Zero;
                var towers=ids.Select((id,i)=>Place(MDTowerLibrary.Get(id),C0+side*((i-(ids.Length-1)*.5f)*5),yaw)).ToList();
                foreach(var t in towers)t.Damage.AttackResilience*=100;
                while(Wait(.5))yield return true;
                var squad=new List<ComponentMDRobot>();for(int i=0;i<4;i++)squad.Add(Robot("MDRobot05",C0+fwd*(44+(i%2)*3)+side*((i-1.5f)*3.5f),yaw+MathF.PI));
                var drones=new List<ComponentMDRobot>{Robot("MDRepair09",C0+fwd*50+side*-3,yaw+MathF.PI),Robot("MDRepair09",C0+fwd*50+side*3,yaw+MathF.PI)};
                foreach(var rb in squad.Concat(drones))rb.Health.AttackResilience*=3;
                int nd=drones.Count;var alive=new double[nd];var distSum=new double[nd];var distN=new int[nd];var behind=new int[nd];var engaged=new int[nd];var leadSum=new double[nd];var firstHit=new double[nd];
                for(int i=0;i<nd;i++){alive[i]=-1;firstHit[i]=-1;}
                double firstSquadHit=-1,firstSquadDeath=-1,mendingSince=-1;int aimFrames=0,aimOnDrones=0;bool beamShot=false,fieldShot=false;double t0=clock.GameTime,end=-1;
                float Nearest(Vector3 p)=>towers.Min(t=>Flat(t.Body.Position,p));
                while(true) {
                    double t=clock.GameTime-t0;
                    foreach(var tw in towers)tw.Damage.Hitpoints=1;
                    var front=squad.Where(Alive).ToList();
                    if(firstSquadHit<0&&squad.Any(q=>q.Health.Health<.999f))firstSquadHit=t;
                    if(firstSquadDeath<0&&squad.Any(q=>!Alive(q)))firstSquadDeath=t;
                    float dF=front.Count>0?front.Min(q=>Nearest(q.Body.Position)):float.MaxValue;
                    for(int i=0;i<nd;i++) {
                        var d=drones[i];
                        if(!Alive(d)){if(alive[i]<0)alive[i]=t;continue;}
                        if(firstHit[i]<0&&d.Health.Health<.999f)firstHit[i]=t;
                        if(t<1)continue;
                        float dD=Nearest(d.Body.Position);distSum[i]+=dD;distN[i]++;
                        if(front.Count>0&&dF<=34){engaged[i]++;if(dD>=dF+3)behind[i]++;leadSum[i]+=dF-dD;}
                    }
                    if(drones.Any(Alive)){foreach(var tw in towers){aimFrames++;if(drones.Any(d=>d.Body==tw.Target))aimOnDrones++;}}
                    var mend=drones.FirstOrDefault(d=>Alive(d)&&d.Patient!=null&&Alive(d.Patient)&&d.Patient.Health.Health<1&&Vector3.Distance(MDCombat.Center(d.Patient.Body),MDCombat.Center(d.Body))<=d.Kind.Range);
                    if(mend==null)mendingSince=-1;else if(mendingSince<0)mendingSince=t;
                    if(!fieldShot&&t>=14){fieldShot=true;Cam(C0+side*34+fwd*18+Vector3.UnitY*24,C0+fwd*22);Shot($"f184_healer_{name.Replace('+','_')}_field");}
                    else if(!beamShot&&t>6&&mend!=null&&t-mendingSince>1.2){beamShot=true;var a=MDCombat.Center(mend.Body);var b=MDCombat.Center(mend.Patient.Body);var mid=(a+b)*.5f;var across=Vector3.Normalize(Vector3.Cross(b-a,Vector3.UnitY)+new Vector3(.001f,0,0));
                        Cam(mid+across*20+Vector3.UnitY*4,mid);Shot($"f184_healer_{name.Replace('+','_')}_beam");r[$"beamShot_{name}"]=$"at {t:0.0} s, drone-patient {Vector3.Distance(a,b):0.0} m";}
                    else Cam(C0+side*34+fwd*18+Vector3.UnitY*24,C0+fwd*22);
                    if(end<0&&(t>60||!drones.Any(Alive)||front.Count==0))end=t;
                    if(end>=0&&t>end+2)break;
                    yield return true;
                }
                double Dur(int i)=>alive[i]<0?end:alive[i];
                var row=new Dictionary<string,object>{["scene"]=name,["towers"]=string.Join("+",ids),["seconds"]=Math.Round(end,1),
                    ["droneAlive"]=string.Join("/",Enumerable.Range(0,nd).Select(i=>alive[i]<0?$"survived {end:0.0}":$"{alive[i]:0.0}")),
                    ["droneDistToTower"]=string.Join("/",Enumerable.Range(0,nd).Select(i=>distN[i]>0?$"{distSum[i]/distN[i]:0.0}":"-")),
                    ["behindShare"]=string.Join("/",Enumerable.Range(0,nd).Select(i=>engaged[i]>0?$"{behind[i]/(float)engaged[i]:0.00}":"-")),
                    ["engagedFrames"]=string.Join("/",engaged),
                    ["leadOverFront"]=string.Join("/",Enumerable.Range(0,nd).Select(i=>engaged[i]>0?$"{leadSum[i]/engaged[i]:0.0}":"-")),
                    ["mended"]=string.Join("/",drones.Select(d=>$"{d.Mended:0.00}")),["mendedTotal"]=Math.Round(drones.Sum(d=>d.Mended),2),
                    ["droneFirstHit"]=string.Join("/",firstHit.Select(x=>x<0?"never":$"{x:0.0}")),["squadFirstHit"]=Math.Round(firstSquadHit,1),["squadFirstDeath"]=Math.Round(firstSquadDeath,1),
                    ["squadLeft"]=squad.Count(Alive),["towerAimOnDrones"]=aimFrames>0?Math.Round(aimOnDrones/(double)aimFrames,2):0,
                    ["ducks"]=ducksF!=null?string.Join("/",drones.Select(d=>ducksF.GetValue(d))):"n/a"};
                hl.Add(Line(row));rows[name]=row;
                if(name=="guns") {
                    for(int i=0;i<nd;i++) {
                        if(engaged[i]<60||behind[i]<.8f*engaged[i])bad.Add($"healer guns: drone {i} behind the squad only {(engaged[i]>0?behind[i]/(float)engaged[i]:0):0.00} of {engaged[i]} frames (>= 0.80): {Line(row)}");
                        if(firstHit[i]>=0&&firstSquadHit>=0&&firstHit[i]<firstSquadHit)bad.Add($"healer guns: drone {i} hit at {firstHit[i]:0.0} s, before the squad ({firstSquadHit:0.0} s)");
                        if(firstSquadDeath>=0&&Dur(i)<=firstSquadDeath)bad.Add($"healer guns: drone {i} died at {Dur(i):0.0} s, before the first RD05 ({firstSquadDeath:0.0} s)");
                    }
                    if(aimFrames>0&&aimOnDrones>.25*aimFrames)bad.Add($"healer guns: the towers aimed at the drones {aimOnDrones/(double)aimFrames:0.00} of the time (<= 0.25)");
                }
                if(drones.Sum(d=>d.Mended)<=0)bad.Add($"healer {name}: nothing mended: {Line(row)}");
                if(!beamShot)hl.Add($"{name}: no mending frame for the beam screenshot");
                foreach(var rb in keep.ToList())if(rb.Entity.IsAddedToProject)proj.RemoveEntity(rb.Entity,true);Remove(towers);ClearAll();while(Wait(.5))yield return true;
            }
        }

        // ---------------------------------------------------------------- jet
        if(Part("jet")) {
            var jl=new List<string>();r["jet"]=jl;
            ClearAll();Clear(X0-26,X0+27,Z0-40,Z0+18);while(Wait(1))yield return true;
            foreach(var (name,kinds) in new[]{("single",new[]{"MDRobot05"}),("squad",new[]{"MDRobot05","MDRobot05","MDRobot05","MDRobot09"})}) {
                ClearAll();while(Wait(.3))yield return true;
                pl.ComponentBody.Position=C0+side*22+fwd*8;pl.ComponentBody.Velocity=Vector3.Zero;
                var jet=Place(MDTowerLibrary.Get("battle_jet_1"),C0,yaw);jet.Damage.AttackResilience*=100;while(Wait(.5))yield return true;
                var robots=kinds.Select((k,i)=>Robot(k,C0+fwd*(name=="single"?12:30+(i%2)*2)+side*((i-(kinds.Length-1)*.5f)*3),yaw+MathF.PI)).ToList();   // single: inside its range, as a robot that walked up (RD05 stops 21.6 m off its target)
                foreach(var rb in robots)rb.Health.AttackResilience*=100;
                double t0=clock.GameTime,first=-1,nextSample=0,sprayAt=0;int lastShots=jet.ShotCount,sprays=0,reached=0,mark=MDTowerBrain.AreaHits,sprayHits=0,outFrames=0,frames=0;bool inSpray=false;
                var curve=new List<string>();float maxD=0,pushed=0;var last=robots.Select(q=>Flat(MDCombat.Center(q.Body),jet.Body.Position)).ToArray();
                void Finish(){inSpray=false;if(sprayAt-first<30){sprays++;if(sprayHits>0)reached++;}}
                while(true) {
                    double t=clock.GameTime-t0;
                    jet.Damage.Hitpoints=1;foreach(var rb in robots)if(rb.Entity.IsAddedToProject)rb.Health.Health=1;
                    Cam(C0+side*20+fwd*10+Vector3.UnitY*12,C0+fwd*13);
                    if(jet.ShotCount>lastShots){if(inSpray)Finish();lastShots=jet.ShotCount;if(first<0)first=t;inSpray=true;sprayHits=0;sprayAt=t;mark=MDTowerBrain.AreaHits;}
                    if(inSpray){sprayHits+=MDTowerBrain.AreaHits-mark;mark=MDTowerBrain.AreaHits;if(jet.Channel<=0)Finish();}
                    var ds=robots.Where(q=>q.Entity.IsAddedToProject).Select(q=>Flat(MDCombat.Center(q.Body),jet.Body.Position)).ToArray();
                    for(int i=0;i<robots.Count;i++){if(!robots[i].Entity.IsAddedToProject)continue;float d=Flat(MDCombat.Center(robots[i].Body),jet.Body.Position);if(jet.Channel>0&&d>last[i])pushed+=d-last[i];last[i]=d;}
                    float near=ds.Length>0?ds.Min():-1;
                    if(first>=0&&t-first<30){frames++;if(near>jet.Range)outFrames++;maxD=Math.Max(maxD,near);if(t-first>=nextSample){curve.Add($"{t-first:0.0}:{near:0.0}");nextSample+=.5;}}
                    if(first<0&&t>25)break;   // it never sprayed
                    if(first>=0&&t-first>=30.5)break;
                    yield return true;
                }
                Shot($"f184_jet_{name}");yield return true;
                var row=new Dictionary<string,object>{["scene"]=name,["robots"]=string.Join("+",kinds),["firstSpray"]=first<0?"never":$"{first:0.0}",["sprays30s"]=sprays,["reached"]=reached,
                    ["outOfRangeShare"]=frames>0?Math.Round(outFrames/(double)frames,2):1,["maxNearest"]=Math.Round(maxD,1),["range"]=jet.Range,["pushed"]=Math.Round(pushed,1),["robotShots"]=robots.Sum(q=>q.ShotsFired),
                    ["distance"]=string.Join(" ",curve)};
                jl.Add(Line(row));
                if(sprays<8)bad.Add($"jet {name}: {sprays} sprays in 30 s (>= 8): {Line(row)}");
                if(sprays>0&&reached<.8*sprays)bad.Add($"jet {name}: only {reached} of {sprays} sprays reached a robot: {Line(row)}");
                if(name=="single"&&frames>0&&outFrames>.1*frames)bad.Add($"jet single: the robot was out of range {outFrames/(double)frames:0.00} of the time: {Line(row)}");
                foreach(var rb in robots)if(rb.Entity.IsAddedToProject)proj.RemoveEntity(rb.Entity,true);Remove([jet]);ClearAll();while(Wait(.4))yield return true;
            }
        }

        // ---------------------------------------------------------------- rate
        if(Part("rate")) {
            var ra=new Dictionary<string,object>();r["rate"]=ra;
            var sites=new[]{C0+new Vector3(-62,0,0),C0+new Vector3(62,0,0),C0+new Vector3(0,0,-62)};
            ClearAll();pl.ComponentBody.Position=C0;pl.ComponentBody.Velocity=Vector3.Zero;
            foreach(var s in sites){int sx=(int)MathF.Floor(s.X),sz=(int)MathF.Floor(s.Z);Clear(sx-14,sx+15,sz-34,sz+10);}
            while(Wait(2))yield return true;
            var ids=MDTowerLibrary.All.Where(d=>d.attack.type!="core").Select(d=>d.id).ToList();ra["towers"]=ids.Count;
            for(int k=0;k<ids.Count;k+=sites.Length) {
                ClearAll();var batch=ids.Skip(k).Take(sites.Length).ToList();
                var units=new List<ComponentMDUnit>();var extra=new List<ComponentMDUnit>();var pins=new List<(ComponentMDRobot rb,Vector3 at)>();
                for(int i=0;i<batch.Count;i++) {
                    var d=MDTowerLibrary.Get(batch[i]);var at=sites[i];var u=Place(d,at,yaw);u.Damage.AttackResilience*=100;units.Add(u);
                    if(d.attack.type is "heal" or "radar"){var g=Place(MDTowerLibrary.Get("battle_turret_1"),at+side*4,yaw);g.Cooldown=1e6f;extra.Add(g);continue;}
                    float dist=Math.Clamp(d.attack.range*.5f,d.attack.minRange+4,d.attack.range-3);var rb=Robot("MDRobot05",at+fwd*dist,yaw+MathF.PI);rb.Health.AttackResilience*=100;pins.Add((rb,rb.Body.Position));
                }
                var prev=units.Select(u=>u.Cooldown).ToArray();var shots=units.Select(u=>u.ShotCount).ToArray();var events=units.Select(_=>new List<double>()).ToArray();var linger=units.Select(_=>new List<double>()).ToArray();
                double t0=clock.GameTime,lastT=t0;double dtMax=0,dtSum=0;int dtN=0;
                while(clock.GameTime-t0<25) {
                    double now=clock.GameTime,t=now-t0;if(now>lastT){dtMax=Math.Max(dtMax,now-lastT);dtSum+=now-lastT;dtN++;}lastT=now;
                    foreach(var u in units)u.Damage.Hitpoints=1;
                    foreach(var g in extra){g.Cooldown=1e6f;g.Damage.Hitpoints=.5f;}   // a hurt gun tower for the medic, a silent one in the radar's reach
                    foreach(var (rb,at) in pins)if(rb.Entity.IsAddedToProject){rb.Body.Position=at;rb.Body.Velocity=Vector3.Zero;rb.Health.Health=1;var l=rb.Creature.ComponentLocomotion;l.WalkSpeed=l.JumpSpeed=l.FlySpeed=0;rb.Cooldown=1e6f;}
                    for(int i=0;i<units.Count;i++) {
                        var u=units[i];bool set=u.Cooldown>prev[i]+1e-4f,shot=u.ShotCount>shots[i];
                        if(set||shot){events[i].Add(Math.Round(t,4));if(u.Tower.attack.type=="radar"&&extra.Count>0){var g=extra.First(x=>Vector3.Distance(x.Body.Position,u.Body.Position)<6);linger[i].Add(Math.Round(g.RangeBoostUntil-now,3));}}
                        prev[i]=u.Cooldown;shots[i]=u.ShotCount;
                    }
                    Cam(C0+Vector3.UnitY*40+side*.1f,C0+fwd*.1f);
                    yield return true;
                }
                for(int i=0;i<units.Count;i++)
                    ra[batch[i]]=new Dictionary<string,object>{["events"]=events[i],["shots"]=units[i].ShotCount,["dtMean"]=dtN>0?Math.Round(dtSum/dtN,4):0,["dtMax"]=Math.Round(dtMax,4),
                        ["boostLeftAfterPulse"]=linger[i].Count>0?linger[i].Distinct().Take(4).ToList():null};
                foreach(var (rb,_) in pins)if(rb.Entity.IsAddedToProject)proj.RemoveEntity(rb.Entity,true);Remove(units);Remove(extra);ClearAll();while(Wait(.3))yield return true;
            }
            foreach(var id in ids)if(ra[id] is Dictionary<string,object> row&&((List<double>)row["events"]).Count<3)bad.Add($"rate {id}: only {((List<double>)row["events"]).Count} attacks in 25 s (the scene does not test it)");
        }

        // ---------------------------------------------------------------- text
        if(Part("text")) {
            var tx=new Dictionary<string,object>();r["text"]=tx;SettingsManager.ShowGuiInScreenshots=true;
            IEnumerable<bool> Shot2(string name){foreach(var x in Seconds(.8))yield return x;ScreenCaptureManager.Capture(1280,720,name+".png");yield return true;}
            tx["towers"]=MDTowerLibrary.All.Where(d=>d.attack.type!="core").ToDictionary(d=>d.id,d=>MDEncyclopedia.Tower(d).Split('\n')[1]);
            tx["rp09"]=MDEncyclopedia.RobotNumbers(MDRobots.Kind("RP09"),960);
            ScreensManager.SwitchScreen("Help");foreach(var x in Seconds(.5))yield return x;
            var help=ScreensManager.FindScreen<HelpScreen>("Help");
            var ours=help.m_topics.Values.Where(t=>t.Name.StartsWith(MDEncyclopedia.Prefix)).OrderBy(t=>t.Index).ToList();
            foreach(var t in ours)MDEncyclopedia.Fill(t);
            for(int i=1;i<=3&&i<ours.Count;i++){help.ShowTopic(ours[i]);foreach(var x in Shot2($"f184_text_help_tier{i}"))yield return x;ScreensManager.SwitchScreen("Help");foreach(var x in Seconds(.3))yield return x;}
            ScreensManager.SwitchScreen("Bestiary");foreach(var x in Seconds(.5))yield return x;
            var best=ScreensManager.FindScreen<BestiaryScreen>("Bestiary");var infos=best.m_creaturesList.Items.OfType<BestiaryCreatureInfo>().ToList();
            var rp=infos.FirstOrDefault(i=>ReferenceEquals(i.EntityValuesDictionary,DatabaseManager.FindEntityValuesDictionary("MDRepair09",false)));
            if(rp!=null){ScreensManager.SwitchScreen("BestiaryDescription",rp,infos);foreach(var x in Shot2("f184_text_bestiary_rp09"))yield return x;
                tx["rp09Page"]=ScreensManager.FindScreen<BestiaryDescriptionScreen>("BestiaryDescription").m_descriptionWidget.Text;}
            else bad.Add("text: the repair drone is not in the bestiary");
            var shown=string.Join("\n",((Dictionary<string,string>)tx["towers"]).Values)+"\n"+tx["rp09"];
            var named=new[]{"终末地","毁灭枪手","原作","Soldiers of Glory","Endfield","Destroy Gunners","Arknights"}.Where(n=>shown.Contains(n,StringComparison.OrdinalIgnoreCase)).ToList();
            if(named.Count>0)bad.Add("text names a source game: "+string.Join(", ",named));
            SettingsManager.ShowGuiInScreenshots=false;ScreensManager.SwitchScreen("Game");foreach(var x in Seconds(1))yield return x;
        }
        pl.ComponentBody.Position=Home;
        if(bad.Count>0)throw new Exception("fb184: "+string.Join(" || ",bad));
    }
}
