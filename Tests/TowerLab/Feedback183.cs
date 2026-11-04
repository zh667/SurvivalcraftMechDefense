using System.Collections;
using System.Reflection;
using Engine;
using Engine.Graphics;
using Engine.Media;
using Game;

namespace TowerLab;

// lab --mode fb183 (0.18.3, user 2026-11-05). Results under "fb183"; screenshots f183_*. Written against the 0.18.2 API (anything newer
// through reflection), so the same build runs on the 0.18.2 package and shows the failures before the fix.
// walls    (user: "反应字样和伤害数字在 48 米内能隔墙看到……改一下吧") a robot 12 m off takes a heat and a cold hit from a tower (a damage
//          number and the reaction name 热冲击 over it). The frame is drawn twice in the same frame, with the texts and with the texts taken
//          out; the pixels that differ are the texts'. Scenes: open ground (the texts must show), a stone wall 4 m in front of the eye
//          (7 high: no text pixel in the wall's screen area, none at all), a small hill (none at all); the same frame once more with
//          the text batch's depth test off (what showed through before). close: a tank seen from right below its front, the line to
//          its texts passes through its own box: the texts must show as with the depth test off (>= 90 %). overlay: the robot inside a
//          tower's shown range (its half-transparent fill under the texts) with status icons: the texts all show, and their solid
//          pixels are the same with and without the fill (the fill is not blended over them).
// control  (user: "电磁脉冲、声波塔 现在几乎能一直控制敌人，挤压了其他控制塔") control towers against RD05 robots for 30 s (both sides x100
//          resilience, health kept full): the share of robot time stunned, frozen (hard control, the two apart) and only slowed; how many
//          freezes the liquid nitrogen tower landed, how far the water jet pushed (from its first spray on), hard control resisted. Scenes: one EMP III; two EMP III;
//          EMP III + EMP II + sonic; a mix (EMP III, sonic, liquid nitrogen, frost I, water jet); the mix without EMP and sonic; two EMP III
//          and a sonic against a squad of four. After the fix: one EMP III <= 55 %, any robot <= 65 % in every scene, and in the mix the
//          liquid nitrogen freezes, the frost slows and the jet pushes.
// frost    (user: 冰冻塔 Ⅰ 15, 液氮塔 40 and its ice 8 a tick) frost I and the liquid nitrogen tower each against a marching squad for 22 s as
//          fb18b-squad: damage that landed, held / slowed shares, reactions; after the fix both deal damage.
public partial class Lab {
    static IEnumerable<bool> Feedback183() {
        Remove(placed.Select(p=>p.unit));placed.Clear();while(Wait(1))yield return true;
        var r=new Dictionary<string,object>();results["fb183"]=r;var bad=new List<string>();results["fb183bad"]=bad;
        bool Part(string p)=>mode=="fb183"||mode=="fb183-"+p;
        var proj=GameManager.Project;var pl=player.ComponentPlayer;var w=MDTowerWorld.For(proj);var terrain=proj.FindSubsystem<SubsystemTerrain>(true);
        var keep=new List<ComponentMDRobot>();
        void ClearAll(){foreach(var e in proj.Entities.Where(e=>e.FindComponent<ComponentCreature>()!=null&&e.FindComponent<ComponentPlayer>()==null).ToList())proj.RemoveEntity(e,true);dums.Clear();keep.Clear();}
        IEnumerable<bool> Seconds(double s,Action each=null){for(double s0=Time.RealTime;Time.RealTime-s0<s;){each?.Invoke();yield return true;}}
        Vector3 Dir(float a)=>new(-MathF.Sin(a),0,-MathF.Cos(a));   // a body's forward at that yaw
        string Line(Dictionary<string,object> row)=>string.Join(" ",row.Select(kv=>$"{kv.Key}={kv.Value}"));
        float Top(Vector3 at)=>terrain.Terrain.GetTopHeight(Terrain.ToCell(at.X),Terrain.ToCell(at.Z))+1.05f;
        ComponentMDRobot Robot(string t,Vector3 at,float yaw) {
            at.Y=Top(at)+(t=="MDRobot09"?3:0);var e=DatabaseManager.CreateEntity(proj,t,true);var b=e.FindComponent<ComponentBody>(true);b.Position=at;b.Rotation=Quaternion.CreateFromAxisAngle(Vector3.UnitY,yaw);
            var sp=e.FindComponent<ComponentSpawn>(true);sp.SpawnDuration=0;sp.AutoDespawn=false;proj.AddEntity(e);var rb=e.FindComponent<ComponentMDRobot>(true);keep.Add(rb);return rb;
        }
        var C0=Home;int X0=(int)MathF.Floor(C0.X),Z0=(int)MathF.Floor(C0.Z);
        int W=Window.Size.X>=640?Window.Size.X:1280,H=Window.Size.X>=640?Window.Size.Y:720;r["grab"]=$"{W} x {H}";
        var wt=typeof(MDTowerWorld);const BindingFlags inst=BindingFlags.NonPublic|BindingFlags.Instance;
        r["build"]=wt.GetField("SharedHardEnabled",BindingFlags.Public|BindingFlags.Static)!=null?"0.18.3 (has SharedHardEnabled)":"older package";

        // ---------------------------------------------------------------- walls
        if(Part("walls")) {
            var wl=new Dictionary<string,object>();r["walls"]=wl;
            ClearAll();Clear(X0-24,X0+25,Z0-24,Z0+25);MDTowerTools.Shown.Clear();
            float G=terrain.Terrain.GetTopHeight(X0,Z0)+1;int stone=Terrain.MakeBlockValue(BlocksManager.GetBlockIndex<GraniteBlock>());
            pl.ComponentBody.Position=C0+new Vector3(0,0,20);pl.ComponentBody.Velocity=Vector3.Zero;
            var tw=Place(MDTowerLibrary.Get("battle_turret_1"),C0+new Vector3(0,0,14),0);   // the attacker of the hits, behind the eye
            var labelsF=wt.GetField("labels",inst);var numbersF=wt.GetField("numbers",inst);var batchF=wt.GetField("labelBatch",inst);
            var pendingF=wt.GetField("pending",inst);
            // the texts out of this frame (and back): every list they are drawn from
            IEnumerable<object> Take(FieldInfo f){var l=(IList)f.GetValue(w);var items=l.Cast<object>().ToList();l.Clear();return items;}
            void Put(FieldInfo f,IEnumerable<object> items){var l=(IList)f.GetValue(w);foreach(var x in items)l.Add(x);}
            Image Without(out Matrix vp){var a=Take(labelsF);var b=Take(numbersF);try{return Grab(W,H,out vp);}finally{Put(labelsF,a);Put(numbersF,b);}}
            Image NoDepth(){var batch=batchF.GetValue(w) as BaseBatch;var d0=batch?.DepthStencilState;if(batch!=null)batch.DepthStencilState=DepthStencilState.None;try{return Grab(W,H,out _);}finally{if(batch!=null)batch.DepthStencilState=d0;}}
            static int Count(Image a,Image b,(float x0,float y0,float x1,float y1)? box,out float cx,out float cy) {
                int n=0;double sx=0,sy=0;int w2=a.Width,h2=a.Height;int xa=0,ya=0,xb=w2,yb=h2;
                if(box is {} q){xa=Math.Max(0,(int)q.x0);ya=Math.Max(0,(int)q.y0);xb=Math.Min(w2,(int)q.x1+1);yb=Math.Min(h2,(int)q.y1+1);}
                for(int y=ya;y<yb;y++)for(int x=xa;x<xb;x++){int i=y*w2+x;if(Sum(a.Pixels[i],b.Pixels[i])>45){n++;sx+=x;sy+=y;}}
                cx=n>0?(float)(sx/n):-1;cy=n>0?(float)(sy/n):-1;return n;
            }
            bool? flip=null;
            // a scene: build, a robot, the two hits, then the frames; returns (text px, text px with the depth test off)
            IEnumerable<bool> Scene(string name,Action build,string template,Func<ComponentMDRobot,(Vector3 eye,Vector3 focus)> view,Dictionary<string,object> row,Func<(Vector3,Vector3)> area=null,ComponentMDUnit fillTower=null,bool icons=false) {
                ClearAll();Clear(X0-24,X0+25,Z0-24,Z0+25);build?.Invoke();MDTowerTools.Shown.Clear();
                var rb=Robot(template,C0+new Vector3(0,0,-12),0);rb.Health.AttackResilience*=100;
                void HoldStill(){var l=rb.Creature.ComponentLocomotion;l.WalkSpeed=l.JumpSpeed=l.FlySpeed=l.TurnSpeed=0;rb.Health.Health=1;tw.Cooldown=1e6f;if(fillTower!=null)fillTower.Cooldown=1e6f;}
                if(name=="close") {   // right in front of it, low: the line from the eye to its texts passes through its own box
                    var b0=rb.Body;rb.Body.Position=new Vector3(C0.X,b0.Position.Y,C0.Z-(b0.BoxSize.Z*.5f+.3f+.1f*Math.Max(b0.BoxSize.X,b0.BoxSize.Z)+.35f));
                }
                foreach(var x in Seconds(1.5,()=>{HoldStill();var (e,f)=view(rb);Cam(e,f);}))yield return x;   // the terrain's meshes
                if(fillTower!=null)MDTowerTools.Shown.Add(fillTower);
                var c=MDCombat.Center(rb.Body);
                MDTowerBrain.Hit(tw,rb.Body,30,c,-Vector3.UnitZ,false,null,MDElement.Heat);
                foreach(var x in Seconds(.05,HoldStill))yield return x;
                MDTowerBrain.Hit(tw,rb.Body,30,c,-Vector3.UnitZ,false,null,MDElement.Cold);   // 热冲击: its name and a reaction number
                if(icons){var stF=wt.GetField("status",inst);var s=((IDictionary)stF.GetValue(w))[rb.Body];var st=wt.GetNestedType("Status",BindingFlags.NonPublic);float now=(float)wt.GetField("time",inst).GetValue(w);
                    st.GetField("elecUntil").SetValue(s,now+60);st.GetField("acidUntil").SetValue(s,now+60);}
                foreach(var x in Seconds(.55,()=>{HoldStill();var (e,f)=view(rb);Cam(e,f);}))yield return x;   // the number shows after 0.35 s
                var (eye,focus)=view(rb);Cam(eye,focus);
                int nLabels=((IList)labelsF.GetValue(w)).Count,nNumbers=((IList)numbersF.GetValue(w)).Count;
                var A=Grab(W,H,out var vp);var B=Without(out _);var R=NoDepth();
                SaveImage(A,$"f183_{name}");SaveImage(R,$"f183_{name}_nodepth");
                var inFrontF=wt.GetField("InFrontEnabled",BindingFlags.Public|BindingFlags.Static);   // 0.18.3: what the target's own body hid without it
                if(inFrontF!=null){inFrontF.SetValue(null,false);try{var N=Grab(W,H,out _);row["textPxWithoutInFront"]=Count(N,B,null,out _,out _);if(name=="close")SaveImage(N,$"f183_{name}_noinfront");}finally{inFrontF.SetValue(null,true);}}
                int px=Count(A,B,null,out var cx,out var cy),ref0=Count(R,B,null,out _,out _);
                row["texts"]=$"{nLabels} name(s), {nNumbers} number(s)";row["textPx"]=px;row["textPxNoDepth"]=ref0;row["centroid"]=$"{cx:0},{cy:0}";
                if(flip==null&&px>0) {   // the render target's rows: the way that puts the label's projected point nearest the texts
                    var lp=rb.Body.Position+Vector3.UnitY*(rb.Body.BoxSize.Y+MDTowerWorld.LabelsAt);float best=1e9f;
                    foreach(bool f in new[]{false,true})if(ToScreen(vp,lp,W,H,f,out var sx,out var sy)){float dd=(sx-cx)*(sx-cx)+(sy-cy)*(sy-cy);if(dd<best){best=dd;flip=f;}}
                    r["flip"]=$"{flip}";
                }
                if(area!=null) {
                    var (lo,hi)=area();var fl=flip??false;float ax0=1e9f,ay0=1e9f,ax1=-1e9f,ay1=-1e9f;int corners=0;
                    foreach(var p in new[]{new Vector3(lo.X,lo.Y,lo.Z),new Vector3(hi.X,lo.Y,lo.Z),new Vector3(lo.X,hi.Y,lo.Z),new Vector3(hi.X,hi.Y,lo.Z)})
                        if(ToScreen(vp,p,W,H,fl,out var sx,out var sy)|!(sx==-1&&sy==-1)){corners++;ax0=Math.Min(ax0,sx);ay0=Math.Min(ay0,sy);ax1=Math.Max(ax1,sx);ay1=Math.Max(ay1,sy);}
                    var box=(Math.Max(0,ax0),Math.Max(0,ay0),Math.Min(W-1,ax1),Math.Min(H-1,ay1));
                    row["wallBox"]=$"{box.Item1:0},{box.Item2:0} - {box.Item3:0},{box.Item4:0} ({corners} corners)";
                    row["textPxInWall"]=Count(A,B,box,out _,out _);row["textPxInWallNoDepth"]=Count(R,B,box,out _,out _);
                }
                if(fillTower!=null) {   // the texts over the range fill: with and without the fill
                    MDTowerTools.Shown.Clear();var Cf=Grab(W,H,out _);var Ef=Without(out _);MDTowerTools.Shown.Add(fillTower);
                    int alone=Count(Cf,Ef,null,out _,out _);row["textPxNoFill"]=alone;
                    var diffs=new List<int>();for(int i=0;i<Cf.Pixels.Length;i++)if(Sum(Cf.Pixels[i],Ef.Pixels[i])>200)diffs.Add(Sum(A.Pixels[i],Cf.Pixels[i])/3);
                    diffs.Sort();row["solidTextPx"]=diffs.Count;row["solidChangeByFill"]=diffs.Count>0?diffs[diffs.Count/2]:-1;
                    row["fillChangesGround"]=Count(B,Ef,null,out _,out _);   // B: the fill, no texts
                    SaveImage(Cf,$"f183_{name}_nofill");MDTowerTools.Shown.Clear();
                }
                foreach(var o in keep.ToList())if(o.Entity.IsAddedToProject)proj.RemoveEntity(o.Entity,true);
                ((IList)labelsF.GetValue(w)).Clear();((IList)numbersF.GetValue(w)).Clear();((IDictionary)pendingF.GetValue(w)).Clear();
            }
            (Vector3,Vector3) Ahead(ComponentMDRobot rb)=>(C0+new Vector3(0,1.7f,0),rb.Body.Position+Vector3.UnitY*2.5f);
            var open=new Dictionary<string,object>();wl["open"]=open;
            foreach(var x in Scene("open",null,"MDRobot05",Ahead,open))yield return x;
            var wall=new Dictionary<string,object>();wl["wall"]=wall;
            foreach(var x in Scene("wall",()=>{for(int x=X0-6;x<=X0+6;x++)for(int y=(int)G;y<G+7;y++)terrain.ChangeCell(x,y,Z0-4,stone);},"MDRobot05",Ahead,wall,
                ()=>(new Vector3(X0-6,G,Z0-3),new Vector3(X0+7,G+7,Z0-3))))yield return x;
            var hill=new Dictionary<string,object>();wl["hill"]=hill;
            foreach(var x in Scene("hill",()=>{for(int k=0;k<5;k++){int h=5-k;for(int x=X0-h;x<=X0+h;x++)for(int z=Z0-6-h;z<=Z0-6+h;z++)terrain.ChangeCell(x,(int)G+k,z,stone);}},"MDRobot05",Ahead,hill))yield return x;
            var close=new Dictionary<string,object>();wl["close"]=close;
            foreach(var x in Scene("close",null,"MDRobot14",rb=>(new Vector3(C0.X+.3f,G+.35f,C0.Z),rb.Body.Position+Vector3.UnitY*(rb.Body.BoxSize.Y+2.5f)),close))yield return x;
            var over=new Dictionary<string,object>();wl["overlay"]=over;
            var ft=Place(MDTowerLibrary.Get("flame_1"),C0+new Vector3(7,0,-12),0);
            foreach(var x in Scene("overlay",null,"MDRobot05",rb=>(C0+new Vector3(0,9,-1),rb.Body.Position+Vector3.UnitY*2),over,null,ft,true))yield return x;
            Remove([ft,tw]);
            int I(Dictionary<string,object> row,string k)=>row.TryGetValue(k,out var v)?Convert.ToInt32(v):-1;
            if(I(open,"textPx")<150)bad.Add($"walls open: the texts show only {I(open,"textPx")} px: {Line(open)}");
            if(I(wall,"textPxInWall")!=0||I(wall,"textPx")!=0)bad.Add($"walls wall: text through the wall: {I(wall,"textPxInWall")} px in the wall's area, {I(wall,"textPx")} in all: {Line(wall)}");
            if(I(wall,"textPxNoDepth")<150)bad.Add($"walls wall: the scene does not test anything (texts without depth test only {I(wall,"textPxNoDepth")} px)");
            if(I(hill,"textPx")!=0)bad.Add($"walls hill: text through the hill: {I(hill,"textPx")} px: {Line(hill)}");
            if(I(close,"textPx")<.9*I(close,"textPxNoDepth")||I(close,"textPxNoDepth")<150)bad.Add($"walls close: the tank's own body hides its texts: {Line(close)}");
            if(I(over,"textPx")<.9*I(over,"textPxNoDepth")||I(over,"textPxNoDepth")<150)bad.Add($"walls overlay: texts hidden over the fill / icons: {Line(over)}");
            if(I(over,"solidChangeByFill")>8)bad.Add($"walls overlay: the fill is blended over the texts: {Line(over)}");
            if(I(over,"fillChangesGround")<500)bad.Add($"walls overlay: no fill in view: {Line(over)}");
            ClearAll();Clear(X0-24,X0+25,Z0-24,Z0+25);
        }

        // ---------------------------------------------------------------- control
        if(Part("control")) {
            var cl=new List<string>();r["control"]=cl;const float Big=100;
            var resistedF=wt.GetField("ResistedNormal",BindingFlags.Public|BindingFlags.Static);
            var scenes=new List<(string name,string[] towers,int robots)>{
                ("emp3",["emp_3"],1),("emp3x2",["emp_3","emp_3"],1),("emp3+emp2+sonic",["emp_3","emp_2","battle_shockwave_1"],1),
                ("mix",["emp_3","battle_shockwave_1","battle_jet_1","frost_1","battle_frost_1"],1),("mix-no-emp",["battle_frost_1","battle_jet_1","frost_1"],1),
                ("squad",["emp_3","emp_3","battle_shockwave_1"],4)};
            const float yaw=0;var fwd=Dir(yaw);var side=new Vector3(-fwd.Z,0,fwd.X);
            var rows=new Dictionary<string,Dictionary<string,object>>();
            foreach(var (name,ids,count) in scenes) {
                ClearAll();Clear(X0-30,X0+31,Z0-30,Z0+31);MDTowerTools.Shown.Clear();while(Wait(.3))yield return true;
                pl.ComponentBody.Position=C0+side*20+fwd*6;pl.ComponentBody.Velocity=Vector3.Zero;
                var towers=ids.Select((id,i)=>Place(MDTowerLibrary.Get(id),C0+side*((i-(ids.Length-1)*.5f)*4.5f),yaw)).ToList();
                foreach(var t in towers)t.Damage.AttackResilience*=Big;
                while(Wait(.5))yield return true;
                var robots=new List<ComponentMDRobot>();
                for(int i=0;i<count;i++)robots.Add(Robot(i==3?"MDRobot14":"MDRobot05",C0+fwd*(12+(i%2)*1.5f)+side*((i-(count-1)*.5f)*3.2f),yaw+MathF.PI));
                foreach(var rb in robots)rb.Health.AttackResilience*=Big;
                var jet=towers.FirstOrDefault(t=>t.Tower.id=="battle_jet_1");var ln=towers.FirstOrDefault(t=>t.Tower.id=="battle_frost_1");
                int n=robots.Count;var stun=new int[n];var frozen=new int[n];var hard=new int[n];var slowOnly=new int[n];var episodes=new int[n];var freezes=new int[n];
                var wasHard=new bool[n];var wasFrozen=new bool[n];var last=robots.Select(rb=>jet==null?0:Vector3.Distance(rb.Body.Position,jet.Body.Position)).ToArray();float pushed=0;int frames=0;
                int res0=resistedF!=null?(int)resistedF.GetValue(null):-1;var shots0=towers.Select(t=>t.ShotCount).ToArray();
                double t0=Time.RealTime;
                foreach(var x in Seconds(31.5,()=>{
                    foreach(var t in towers)t.Damage.Hitpoints=1;
                    foreach(var rb in robots)if(rb.Entity.IsAddedToProject)rb.Health.Health=1;
                    Cam(C0+side*16+fwd*4+Vector3.UnitY*12,C0+fwd*10);
                    if(jet!=null)for(int i=0;i<n;i++){var b=robots[i].Body;if(!b.Entity.IsAddedToProject)continue;float d=Vector3.Distance(b.Position,jet.Body.Position);if(jet.Channel>0&&d>last[i])pushed+=d-last[i];last[i]=d;}   // the push from the first spray on
                    if(Time.RealTime-t0<1.5)return;   // from 1.5 s: the towers have turned and fired; 30 s counted
                    frames++;
                    for(int i=0;i<n;i++) {
                        var b=robots[i].Body;if(!b.Entity.IsAddedToProject)continue;
                        bool st=w.IsStunned(b),fr=w.IsFrozen(b),hd=st||fr;float sf=w.SpeedFactor(b);
                        if(fr)frozen[i]++;else if(st)stun[i]++;
                        if(hd)hard[i]++;else if(sf<1)slowOnly[i]++;
                        if(hd&&!wasHard[i])episodes[i]++;if(fr&&!wasFrozen[i])freezes[i]++;wasHard[i]=hd;wasFrozen[i]=fr;
                    }
                }))yield return x;
                float Sh(int v)=>frames>0?MathF.Round(v/(float)frames,2):0;
                var row=new Dictionary<string,object>{["scene"]=name,["towers"]=string.Join("+",ids),["robots"]=n,
                    ["hard"]=string.Join("/",hard.Select(Sh)),["stun"]=string.Join("/",stun.Select(Sh)),["frozen"]=string.Join("/",frozen.Select(Sh)),["slowOnly"]=string.Join("/",slowOnly.Select(Sh)),
                    ["hardMax"]=hard.Select(Sh).Max(),["hardAvg"]=MathF.Round(hard.Select(Sh).Average(),2),["episodes"]=string.Join("/",episodes),["freezes"]=string.Join("/",freezes),
                    ["everHeld"]=hard.Count(v=>v>0),["pushed"]=MathF.Round(pushed,1),["resisted"]=resistedF!=null?(int)resistedF.GetValue(null)-res0:-1,
                    ["shots"]=string.Join("/",towers.Select((t,i)=>t.ShotCount-shots0[i])),["secondsPerShot"]=string.Join("/",towers.Select((t,i)=>t.ShotCount>shots0[i]?MathF.Round(31.5f/(t.ShotCount-shots0[i]),1):-1)),
                    ["jetDistance"]=jet==null?-1:MathF.Round(robots.Where(q=>q.Entity.IsAddedToProject).Select(q=>Vector3.Distance(q.Body.Position,jet.Body.Position)).DefaultIfEmpty(-1).Min(),1),["robotShots"]=robots.Sum(q=>q.ShotsFired)};
                cl.Add(Line(row));rows[name]=row;
                Shot($"f183_control_{name.Replace('+','_')}");yield return true;
                foreach(var rb in robots)if(rb.Entity.IsAddedToProject)proj.RemoveEntity(rb.Entity,true);Remove(towers);ClearAll();while(Wait(.4))yield return true;
            }
            float F(string s,string k)=>Convert.ToSingle(rows[s][k]);
            if(F("emp3","hardMax")>.55f)bad.Add($"control: one EMP III holds a robot {F("emp3","hardMax"):0.00} of the time (<= 0.55): {Line(rows["emp3"])}");
            foreach(var (s,row) in rows)if(Convert.ToSingle(row["hardMax"])>.65f)bad.Add($"control {s}: a robot held {row["hardMax"]} of the time (<= 0.65): {Line(row)}");
            var mix=rows["mix"];
            if(mix["freezes"] is string fz&&fz.Split('/').Sum(int.Parse)<1)bad.Add($"control mix: the liquid nitrogen tower never froze it: {Line(mix)}");
            if(mix["slowOnly"] is string so&&so.Split('/').Select(float.Parse).Max()<.1f)bad.Add($"control mix: the frost slows count less than 10 % of the time: {Line(mix)}");
            if(F("mix","pushed")<1)bad.Add($"control mix: the water jet pushed it {F("mix","pushed"):0.0} m: {Line(mix)}");
            if(Convert.ToInt32(rows["squad"]["everHeld"])<4)bad.Add($"control squad: only {rows["squad"]["everHeld"]} of 4 robots were ever held: {Line(rows["squad"])}");
        }

        // ---------------------------------------------------------------- frost
        if(Part("frost")) {
            var fr=new List<string>();r["frost"]=fr;const float Big=100;var kinds=new[]{"MDRobot05","MDRobot05","MDRobot05","MDRobot14","MDRobot09"};
            bool Dry(Vector3 at){int x=Terrain.ToCell(at.X),z=Terrain.ToCell(at.Z);var t=terrain.Terrain;return t.GetChunkAtCell(x,z) is {State:>=TerrainChunkState.InvalidVertices1}&&MathF.Abs(t.GetTopHeight(x,z)+1-Home.Y)<3&&BlocksManager.Blocks[Terrain.ExtractContents(t.GetCellValueFast(x,t.GetTopHeight(x,z),z))] is not FluidBlock;}
            float Land(float a){int i=1;while(i<60&&Dry(Home+Dir(a)*i))i++;return i-1;}
            float yaw=new[]{0f,MathF.PI*.5f,-MathF.PI*.5f,MathF.PI}.OrderByDescending(Land).First();float start=Math.Min(34,Land(yaw)-2);r["frostField"]=$"yaw {yaw:0.00}, start {start:0} m";
            var power=new Dictionary<string,float>();
            foreach(var id in new[]{"frost_1","battle_frost_1"}) {
                var d=MDTowerLibrary.Get(id);ClearAll();while(Wait(.3))yield return true;var side=Dir(yaw+MathF.PI*.5f);
                pl.ComponentBody.Position=Home+Dir(yaw)*start*.5f+side*16;pl.ComponentBody.Position=new Vector3(pl.ComponentBody.Position.X,Top(pl.ComponentBody.Position),pl.ComponentBody.Position.Z);pl.ComponentBody.Velocity=Vector3.Zero;
                var u=Place(d,Home,yaw);while(Wait(.5))yield return true;u.Damage.AttackResilience*=Big;
                var squad=new List<ComponentMDRobot>();
                for(int i=0;i<kinds.Length;i++)squad.Add(Robot(kinds[i],Home+Dir(yaw)*(start+(i%2)*2.5f)+side*((i-2)*2.8f),yaw+MathF.PI));
                foreach(var rb in squad)rb.Health.AttackResilience*=Big;
                int frames=0,held=0,slowed=0,reactions0=MDElements.Count;float dealt=0;double first=-1,t0=Time.RealTime;
                foreach(var x in Seconds(22,()=>{
                    foreach(var rb in squad) {
                        if(!rb.Entity.IsAddedToProject)continue;
                        float dmg=(1-rb.Health.Health)*rb.Health.AttackResilience;rb.Health.Health=1;if(dmg>0){dealt+=dmg;if(first<0)first=Time.RealTime-t0;}
                        frames++;float sf=w.SpeedFactor(rb.Body);if(sf<=0)held++;else if(sf<1)slowed++;
                    }
                    u.Damage.Hitpoints=1;Cam(Home+Dir(yaw)*start*.5f+side*22+Vector3.UnitY*12,Home+Dir(yaw)*start*.4f);
                }))yield return x;
                power[id]=dealt;
                var row=new Dictionary<string,object>{["tower"]=id,["power"]=d.attack.power,["zonePower"]=d.attack.zonePower,["dealt"]=MathF.Round(dealt,0),["dps"]=MathF.Round(dealt/22,1),
                    ["held"]=frames>0?MathF.Round(held/(float)frames,2):0,["slowed"]=frames>0?MathF.Round(slowed/(float)frames,2):0,["firstDamage"]=first<0?-1:Math.Round(first,1),
                    ["reactions"]=MDElements.Count-reactions0,["shots"]=u.ShotCount};
                fr.Add(Line(row));
                Shot($"f183_frost_{id}");yield return true;
                foreach(var rb in squad)if(rb.Entity.IsAddedToProject)proj.RemoveEntity(rb.Entity,true);Remove([u]);ClearAll();while(Wait(.4))yield return true;
            }
            foreach(var (id,p) in power)if(p<=0)bad.Add($"frost: {id} dealt no damage to the squad in 22 s");
        }
        pl.ComponentBody.Position=Home;
        if(bad.Count>0)throw new Exception("fb183: "+string.Join(" || ",bad));
    }
}
