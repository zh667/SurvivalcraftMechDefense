using System.Reflection;
using Engine;
using Game;

namespace TowerLab;

// lab --mode fb100 (1.0.0, user 2026-11-06). Results under "fb100"; screenshots f100_*. Written against the 0.18.4 API (anything newer through
// reflection), so the same build runs on the 0.18.4 package and shows what the change is for.
// hop    (user: "敌对机器人遇到障碍会跳跃，加跳跃冷却") RD05, RD06, RD07 and the tank RD14, one at a time, walk to a goal 20 m behind a wall 9 blocks
//        wide, 1, 2 or 3 blocks high, 12 m in front of them (no towers, nothing to fight); "2+2": two 2-high walls 4 blocks apart (two hops in a row).
//        Per case: whether and when it got past the wall (from the first time it stood within 1 m of it), how high it rose, whether it went
//        round (past the wall's end), hops / jumps / climbs, the time between hops. After the fix: 2 high: over within 3.5 s (2+2: 6 s) of
//        reaching it (the whole body past it), by a hop (a 1-block step the body rises over by itself, before and after); 3 high: never on top (rose < 1.5 m: round it or it stays); hops never closer than MDUnstuck.HopCooldown.
// spawn  (user: "敌对机器人会在树顶上生成然后发呆……别让敌兵生成在这些特殊位置") a forest round the player (oaks, birches, spruces about 7 m apart with a fifth left out,
//        a third of the crowns worn open at the top the way shots and robots clear leaves; a plank deck on posts, a glass roof, a fence ring):
//        150 spots per template from MDRaids.Ground (the one way every spawn picks its spot: wild groups, raids, threat waves, escorts,
//        walking bosses) and 20 real wild spawn rounds; what each spot stands on (leaves, log, planks, glass, fence, other building, natural
//        ground) and how many tries found a spot. After the fix: none on leaves, logs or building blocks, spots found no less often.
// manual the handbook's tower chapter at the rotary guns and the anti-aircraft guns (their numbers come from towers.json).
public partial class Lab {
    static IEnumerable<bool> Feedback100() {
        Remove(placed.Select(p=>p.unit));placed.Clear();while(Wait(1))yield return true;
        var r=new Dictionary<string,object>();results["fb100"]=r;var bad=new List<string>();results["fb100bad"]=bad;
        bool Part(string p)=>mode=="fb100"||mode=="fb100-"+p;
        var proj=GameManager.Project;var pl=player.ComponentPlayer;var terrain=proj.FindSubsystem<SubsystemTerrain>(true);var clock=proj.FindSubsystem<SubsystemTime>(true);
        const BindingFlags S=BindingFlags.Public|BindingFlags.Static,I=BindingFlags.Public|BindingFlags.Instance;
        bool newBuild=typeof(MDUnstuck).GetField("HopEnabled",S)!=null;r["build"]=newBuild?"1.0.0 (has MDUnstuck.HopEnabled)":"older package";
        void ClearAll(){foreach(var e in proj.Entities.Where(e=>e.FindComponent<ComponentCreature>()!=null&&e.FindComponent<ComponentPlayer>()==null).ToList())proj.RemoveEntity(e,true);dums.Clear();}
        IEnumerable<bool> Seconds(double s,Action each=null){for(double s0=Time.RealTime;Time.RealTime-s0<s;){each?.Invoke();yield return true;}}
        string Line(Dictionary<string,object> row)=>string.Join(" ",row.Select(kv=>$"{kv.Key}={kv.Value}"));
        ComponentMDRobot Robot(string t,Vector3 at,float yaw) {
            var e=DatabaseManager.CreateEntity(proj,t,true);var b=e.FindComponent<ComponentBody>(true);b.Position=at;b.Rotation=Quaternion.CreateFromAxisAngle(Vector3.UnitY,yaw);
            var sp=e.FindComponent<ComponentSpawn>(true);sp.SpawnDuration=0;sp.AutoDespawn=false;proj.AddEntity(e);return e.FindComponent<ComponentMDRobot>(true);
        }
        object Get(object o,string name)=>o.GetType().GetField(name,I)?.GetValue(o);
        var C0=Home+new Vector3(0,0,-6);int X0=(int)MathF.Floor(C0.X),Z0=(int)MathF.Floor(C0.Z);

        // ---------------------------------------------------------------- hop
        if(Part("hop")) {
            var rows=new List<string>();r["hop"]=rows;var hopGaps=new List<double>();
            float cooldown=newBuild?(float)typeof(MDUnstuck).GetField("HopCooldown",S).GetValue(null):0;r["hopCooldown"]=cooldown;
            foreach(var wall in new[]{"1","2","3","2+2"})foreach(var t in new[]{"MDRobot05","MDRobot06","MDRobot07","MDRobot14"}) {
                ClearAll();Clear(X0-14,X0+15,Z0-34,Z0+22);pl.ComponentBody.Position=C0+new Vector3(16,0,4);pl.ComponentBody.Velocity=Vector3.Zero;while(Wait(.4))yield return true;
                int h=wall=="2+2"?2:int.Parse(wall);int wz=Z0;   // the wall's cells z = wz (and wz-5 for the second one), x X0-4..X0+4, y 70..
                for(int x=X0-4;x<=X0+4;x++)for(int y=70;y<70+h;y++){terrain.ChangeCell(x,y,wz,3);if(wall=="2+2")terrain.ChangeCell(x,y,wz-5,3);}
                float back=wall=="2+2"?wz-5:wz;   // the far face of the last wall is at z = back
                var rb=Robot(t,new Vector3(X0+.5f,70.05f,wz+12.5f),0);rb.Health.AttackResilience*=100;var goal=new Vector3(X0+.5f,70,wz-20);rb.RaidGoal=goal;rb.Raider=true;
                var un=rb.Unstuck;float half=Math.Max(rb.Body.BoxSize.X,rb.Body.BoxSize.Z)*.5f;
                double t0=clock.GameTime,reached=-1,crossed=-1;float y0=rb.Body.Position.Y,rise=0,crossX=0;int lastHops=0;double jumpShot=-1;bool overShot=false,jumpShotTaken=false;var times=new List<double>();
                string tag=$"H{wall.Replace("+","x")}_{t.Replace("MDRobot","RD")}";bool shots=t=="MDRobot05"||t=="MDRobot14";
                foreach(var x in Seconds(40,()=>{
                    if(!rb.Entity.IsAddedToProject)return;var p=rb.Body.Position;double now=clock.GameTime;
                    rb.Health.Health=1;rise=Math.Max(rise,p.Y-y0);
                    if(reached<0&&p.Z-half-(wz+1)<1)reached=now;
                    if(crossed<0&&p.Z+half<back-.2f){crossed=now;crossX=p.X-(X0+.5f);}
                    int hops=newBuild?(int)Get(un,"Hops"):0;if(hops>lastHops){lastHops=hops;times.Add(now);if(jumpShot<0&&!jumpShotTaken)jumpShot=now;}
                    var c=new Vector3(X0+.5f,71.5f,wz+.5f);
                    if(shots&&jumpShot>0&&now-jumpShot>=.25&&now-jumpShot<5){Cam(c+new Vector3(13,3,1),c+new Vector3(0,.5f,0));Shot($"f100_hop_jump_{tag}");jumpShot=-1;jumpShotTaken=true;}
                    else if(shots&&!overShot&&crossed>0&&now-crossed>1){overShot=true;Cam(c+new Vector3(13,4,-2),new Vector3(p.X,71,p.Z));Shot($"f100_hop_over_{tag}");}
                    else Cam(c+new Vector3(13,3,1),c+new Vector3(0,.5f,0));
                }))if(crossed>0&&clock.GameTime-crossed>1.5&&(!shots||overShot))break;else yield return x;
                if(shots&&!overShot){Cam(new Vector3(X0+13.5f,74.5f,wz+1.5f),new Vector3(X0+.5f,71,wz+2));Shot($"f100_hop_end_{tag}");yield return true;}
                var gaps=times.Zip(times.Skip(1),(a,b)=>b-a).ToList();hopGaps.AddRange(gaps);
                var row=new Dictionary<string,object>{["wall"]=wall,["robot"]=t,["half"]=MathF.Round(half,2),["reached"]=reached<0?-1:Math.Round(reached-t0,1),
                    ["over"]=crossed<0?"no":Math.Round(crossed-(reached<0?t0:reached),1),["round"]=crossed>0&&MathF.Abs(crossX)>4.5f+half?"yes":"no",["rose"]=MathF.Round(rise,1),
                    ["hops"]=newBuild?Get(un,"Hops"):"-",["jumps"]=un.Jumps,["climbs"]=un.Climbs,["detours"]=newBuild?Get(un,"Detours"):"-",["tallWalls"]=newBuild?Get(un,"TallWalls"):"-",
                    ["gaps"]=string.Join("/",gaps.Select(g=>g.ToString("0.00"))),["end"]=$"({rb.Body.Position.X-X0-.5f:0.0},{rb.Body.Position.Y-70:0.0},{rb.Body.Position.Z-wz:0.0})"};
                rows.Add(Line(row));
                if(h<=2) {
                    if(crossed<0)bad.Add($"hop: {t} not over a {wall}-high wall in 40 s: {Line(row)}");
                    else if(h==2&&row["round"] is "no"&&crossed-reached>(wall=="2+2"?6:3.5))bad.Add($"hop: {t} took {crossed-reached:0.0} s over a {wall}-high wall: {Line(row)}");
                    if(newBuild&&h==2&&(int)Get(un,"Hops")<1&&row["round"] is "no")bad.Add($"hop: {t} crossed a {wall}-high wall without a hop: {Line(row)}");
                } else if(rise>1.5f)bad.Add($"hop: {t} got up a 3-high wall (rose {rise:0.0} m): {Line(row)}");
                if(gaps.Any(g=>g<cooldown-.05))bad.Add($"hop: {t} hopped again after {gaps.Min():0.00} s (cooldown {cooldown}): {Line(row)}");
                if(rb.Entity.IsAddedToProject)proj.RemoveEntity(rb.Entity,true);
            }
            r["hopGaps"]=hopGaps.Count==0?"none":$"{hopGaps.Count} gaps, min {hopGaps.Min():0.00} s";
            ClearAll();Clear(X0-14,X0+15,Z0-34,Z0+22);while(Wait(.5))yield return true;
        }

        // ---------------------------------------------------------------- spawn
        if(Part("spawn")) {
            var sr=new Dictionary<string,object>();r["spawn"]=sr;
            ClearAll();var P=Home;int px=(int)MathF.Floor(P.X),pz=(int)MathF.Floor(P.Z);const int R=72;
            // natural ground: grass on three dirt, the air above cleared (cells already right are skipped)
            int changed=0;
            void Set(int x,int y,int z,int v){if(terrain.Terrain.GetCellValue(x,y,z)!=v){terrain.ChangeCell(x,y,z,v);changed++;}}
            for(int x=px-R;x<=px+R;x++)for(int z=pz-R;z<=pz+R;z++){for(int y=66;y<69;y++)Set(x,y,z,2);Set(x,69,z,8);for(int y=70;y<100;y++)Set(x,y,z,0);}
            pl.ComponentBody.Position=P;pl.ComponentBody.Velocity=Vector3.Zero;while(Wait(2))yield return true;
            var rnd=new Engine.Random(11);int trees=0,worn=0,brokenLeaves=0;var types=new[]{TreeType.Oak,TreeType.Birch,TreeType.Spruce};
            for(int x=px-R+3;x<=px+R-3;x+=7)for(int z=pz-R+3;z<=pz+R-3;z+=7) {
                int tx=x+rnd.Int(-2,2),tz=z+rnd.Int(-2,2);if(Math.Abs(tx-px)<7&&Math.Abs(tz-pz)<7||rnd.Float(0,1)<.2f)continue;   // ~7 m apart, a fifth left out (clearings)
                var brushes=PlantsManager.GetTreeBrushes(types[rnd.Int(0,2)]);var brush=brushes[rnd.Int(0,brushes.Count-1)];brush.Paint(terrain,tx,70,tz);trees++;
                if(trees%3==0) {   // worn crown: the top two layers of leaves gone (shots and robots clear leaves; MDRobots.BreakLeaves)
                    var cells=brush.m_cells??brush.m_cellsDictionary.Values.ToArray();var mn=new Point3(cells.Min(c=>c.X),cells.Min(c=>c.Y),cells.Min(c=>c.Z));var mx=new Point3(cells.Max(c=>c.X),cells.Max(c=>c.Y),cells.Max(c=>c.Z));worn++;
                    for(int bx=mn.X;bx<=mx.X;bx++)for(int bz=mn.Z;bz<=mx.Z;bz++)for(int by=mx.Y-1;by<=mx.Y;by++)
                        if(BlocksManager.Blocks[Terrain.ExtractContents(terrain.Terrain.GetCellValue(tx+bx,70+by,tz+bz))] is LeavesBlock){terrain.ChangeCell(tx+bx,70+by,tz+bz,0);brokenLeaves++;}
                }
            }
            // players' building tops in the wood: a plank deck on four posts, a glass roof on walls, a fence ring
            void Box3(int x0,int y0,int z0,int x1,int y1,int z1,int v){for(int x=x0;x<=x1;x++)for(int y=y0;y<=y1;y++)for(int z=z0;z<=z1;z++)terrain.ChangeCell(x,y,z,v);}
            Box3(px+40,70,pz-4,px+40,72,pz-4,21);Box3(px+46,70,pz-4,px+46,72,pz-4,21);Box3(px+40,70,pz+2,px+40,72,pz+2,21);Box3(px+46,70,pz+2,px+46,72,pz+2,21);Box3(px+38,73,pz-6,px+48,73,pz+4,21);
            Box3(px-48,70,pz-4,px-38,72,pz-4,26);Box3(px-48,70,pz+6,px-38,72,pz+6,26);Box3(px-48,73,pz-4,px-38,73,pz+6,15);
            for(int i=-5;i<=5;i++){terrain.ChangeCell(px+i,70,pz+44,94);terrain.ChangeCell(px+i,70,pz+54,94);terrain.ChangeCell(px-5,70,pz+44+i+5,94);terrain.ChangeCell(px+5,70,pz+44+i+5,94);}
            sr["forest"]=$"{trees} trees ({worn} worn open, {brokenLeaves} leaves taken), {changed} ground cells set, radius {R}";
            while(Wait(3))yield return true;
            string Under(Vector3 spot,out bool crown) {
                int x=Terrain.ToCell(spot.X),y=Terrain.ToCell(spot.Y-.6f),z=Terrain.ToCell(spot.Z);crown=false;
                for(int dx=-1;dx<=1;dx++)for(int dz=-1;dz<=1;dz++)for(int dy=-1;dy<=1;dy++)if(BlocksManager.Blocks[Terrain.ExtractContents(terrain.Terrain.GetCellValue(x+dx,y+dy,z+dz))] is LeavesBlock or WoodBlock)crown=true;
                var b=BlocksManager.Blocks[Terrain.ExtractContents(terrain.Terrain.GetCellValue(x,y,z))];
                return b switch{LeavesBlock=>"leaves",WoodBlock=>"log",PlanksBlock=>"planks",GlassBlock or FramedGlassBlock=>"glass",FenceBlock=>"fence",GrassBlock or DirtBlock=>"ground",_=>b.GetType().Name};
            }
            var refusedF=typeof(MDRaids).GetField("SpotRefused",S);int refused0=refusedF==null?0:(int)refusedF.GetValue(null);
            var rng=new System.Random(5);int special=0,crownNear=0;var rows=new List<string>();sr["templates"]=rows;var specialSpots=new List<Vector3>();
            foreach(var t in new[]{"MDRobot05","MDRobot06","MDRobot07","MDRobot14","MDRobot09"}) {
                var counts=new Dictionary<string,int>();int found=0,near=0;var box=MDRobots.Box(t);
                for(int i=0;i<150;i++) {
                    var spot=MDRaids.Ground(terrain,P,(float)(rng.NextDouble()*Math.PI*2),32+(float)rng.NextDouble()*24,rng,box);
                    if(spot==null){counts["none"]=counts.GetValueOrDefault("none")+1;continue;}
                    found++;var u=Under(spot.Value,out bool cr);counts[u]=counts.GetValueOrDefault(u)+1;if(cr)near++;
                    if(u is not "ground"){special++;if(specialSpots.Count<6)specialSpots.Add(spot.Value);}
                }
                crownNear+=near;rows.Add($"{t}: found {found}/150, "+string.Join(", ",counts.OrderBy(k=>k.Key).Select(k=>$"{k.Key} {k.Value}"))+$", leaves or a log within 1 block {near}");
            }
            sr["refusedByRules"]=refusedF==null?"-":(int)refusedF.GetValue(null)-refused0;
            // real wild spawn rounds (MDWildSpawns: a group centre, then each robot's spot)
            var real=new Dictionary<string,int>();int sent=0,rounds=0,robotsOnSpecial=0;ComponentMDRobot shown=null;var standing=new Dictionary<string,int>();
            for(int k=0;k<20;k++) {
                ClearAll();while(Wait(.2))yield return true;
                int n=MDWildSpawns.Spawn(proj,new System.Random(100+k),true);rounds++;sent+=n;
                var bots=proj.Entities.Select(e=>e.FindComponent<ComponentMDRobot>()).Where(x=>x!=null).ToList();
                foreach(var b in bots){var u=Under(b.Body.Position,out _);real[u]=real.GetValueOrDefault(u)+1;if(u is not "ground"){robotsOnSpecial++;shown??=b;}}
                if(k==0&&shown==null)shown=bots.FirstOrDefault();
                foreach(var x in Seconds(1.5))yield return x;
                foreach(var b in bots.Where(b=>b.Entity.IsAddedToProject)){var sv=b.Body.StandingOnValue;var name=sv.HasValue?BlocksManager.Blocks[Terrain.ExtractContents(sv.Value)].GetType().Name:"(not standing)";standing[name]=standing.GetValueOrDefault(name)+1;}
                if(k==0||shown!=null&&k<3&&shown.Entity.IsAddedToProject) {
                    var focus=shown?.Entity.IsAddedToProject==true?shown.Body.Position:P;
                    foreach(var x in Seconds(.6,()=>Cam(focus+new Vector3(9,7,9),focus)))yield return x;Shot($"f100_spawn_round{k}");yield return true;
                }
            }
            sr["realRounds"]=$"{rounds} rounds, {sent} robots; under them at spawn: "+string.Join(", ",real.OrderBy(k=>k.Key).Select(k=>$"{k.Key} {k.Value}"))+"; standing on after 1.5 s: "+string.Join(", ",standing.OrderBy(k=>k.Key).Select(k=>$"{k.Key} {k.Value}"));
            sr["special"]=special;sr["crownNear"]=crownNear;sr["robotsOnSpecial"]=robotsOnSpecial;
            sr["specialSpots"]=string.Join(" ",specialSpots.Select(s=>$"({s.X-P.X:0},{s.Y:0.0},{s.Z-P.Z:0})"));
            if(specialSpots.Count>0){var s0=specialSpots[0];foreach(var x in Seconds(.5,()=>Cam(s0+new Vector3(8,6,8),s0)))yield return x;Shot("f100_spawn_special_spot");yield return true;}
            ClearAll();pl.ComponentBody.Position=P+new Vector3(0,0,0);
            foreach(var x in Seconds(.5,()=>Cam(P+new Vector3(-30,30,-30),P+new Vector3(10,0,10))))yield return x;Shot("f100_spawn_forest");yield return true;
            if(special>0||robotsOnSpecial>0)bad.Add($"spawn: {special} sampled spots and {robotsOnSpecial} spawned robots on leaves, logs or building blocks");
            ClearAll();Clear((int)P.X-30,(int)P.X+75,(int)P.Z-35,(int)P.Z+70);while(Wait(1))yield return true;
        }

        // ---------------------------------------------------------------- manual
        if(Part("manual")) {
            var mn=new Dictionary<string,object>();r["manual"]=mn;SettingsManager.ShowGuiInScreenshots=true;
            MDManualScreen.Open();var screen=(MDManualScreen)ScreensManager.CurrentScreen;screen.Chapter=1;foreach(var x in Seconds(1))yield return x;
            const BindingFlags P=BindingFlags.NonPublic|BindingFlags.Instance;var scroll=(ScrollPanelWidget)typeof(MDManualScreen).GetField("scroll",P).GetValue(screen);var content=(StackPanelWidget)typeof(MDManualScreen).GetField("content",P).GetValue(screen);
            foreach(var name in new[]{"转轮机枪塔 Ⅰ","防空炮 Ⅰ"}) {
                float y=0;Widget hit=null;foreach(var c in content.Children){if(c is ContainerWidget cw&&cw.AllChildren.OfType<LabelWidget>().Any(l=>l.Text!=null&&l.Text.StartsWith(name))){hit=c;break;}y+=c.ActualSize.Y+c.Margin.Y*2;}
                if(hit==null){bad.Add("manual: no entry "+name);continue;}
                mn[name]=string.Join(" | ",((ContainerWidget)hit).AllChildren.Select(w=>w.GetType().GetProperty("Text")?.GetValue(w) as string).Where(x=>!string.IsNullOrWhiteSpace(x)));
                scroll.ScrollPosition=Math.Max(0,y-20);foreach(var x in Seconds(.8))yield return x;ScreenCaptureManager.Capture(1280,720,$"f100_manual_{(name.StartsWith("转轮")?"gatling":"aa")}.png");yield return true;
            }
            SettingsManager.ShowGuiInScreenshots=false;ScreensManager.SwitchScreen("Game");foreach(var x in Seconds(1))yield return x;
        }
        pl.ComponentBody.Position=Home;
        if(bad.Count>0)throw new Exception("fb100: "+string.Join(" || ",bad));
    }
}
