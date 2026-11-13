using System.Reflection;
using System.Xml.Linq;
using Engine;
using Game;
using TemplatesDatabase;

namespace TowerLab;

// lab --mode fb101 (1.0.0 (11-13), user 2026-11-13; parts fb101-<part>). Results under "fb101"; screenshots f101_*. Written against the API of 1.0.0 as released on 11-07
// (anything newer through reflection), so the same build runs on the 1.0.0 package released on 11-07 and shows what each change is for (it fails there).
// wave    endless raid: the countdown of wave 3 and wave 3 under way, each saved (MDThreat.Save) and read back (MDThreat.Load, as a reload
//         does): after the fix the raid goes on at wave 3 (1.0.0: back to wave 1); a raid robot keeps ThreatRaid in its saved values.
// armour  a robot's hit (MDRobotAttackment.CalculateInjuryAmount, 40 points, 60 hits per set) on the player without clothes, in the
//         game's strongest armour (AP 1, sturdiness 200, renewed every hit) and in the ghoul mod's soul set (AP 1, sturdiness infinity;
//         run_isolated copies the ghoul mod for this mode): after the fix every set lets at least 20 % through (1.0.0: the soul set 0).
// vanilla a gun tower and an immobile wolf (a vanilla predator) 8 m off: setting 炮塔攻击原版生物 on: hit; off: not hit in 6 s, and hit
//         after the wolf "attacked" the player (MDFaction.Attacked); a robot stays an enemy with the setting off.
// hard    RD19 (frozen) beside a granite cell, a dirt cell, and a tower on a 2-high dirt platform reaching into its box: one clearing
//         pass takes the dirt only; then, trying to drive (WalkWanted), passes every 0.25 s: nothing hard for HardWait s, then the footing
//         and the granite ground through; HardOnWay counts a granite wall on a drive and none on a free one.
// ranges  a gun tower on bumpy ground (steps of 1-2 blocks), its ring shown: level by default (no vertical joins), with the setting
//         攻击范围贴合地形 on folded on the ground (joins), as 0.18.2.
// click   an empty-hand right-click on a tower picks it up (MDTowerTools.Moving), the same click does not put it down, a left-click on
//         the ground 6 m off does; with an item in the hand the right-click does not pick it up.
// shield  the core's shield: two towers in a chain against the core's face (one corner to corner, one far off): both hand all over 1 % to
//         the store, the dome reaches the far one; a robot's hit on a member or the player inside takes the store, one outside hurts that
//         tower; a member moved off gets its share back; the core destroyed leaves the other at 1 % and off line.
// core    items 3-4 m from the core (scrap, dirt) go into its store within 3 s, experience and items 25 m off stay; a right-click on the
//         core opens the store panel; the store saved and read back is the same; the core taken down drops what it held.
public partial class Lab {
    static IEnumerable<bool> Feedback101() {
        Remove(placed.Select(p=>p.unit));placed.Clear();while(Wait(1))yield return true;
        DialogsManager.HideAllDialogs();   // the ghoul mod's difficulty question covered the screenshots
        var r=new Dictionary<string,object>();results["fb101"]=r;var bad=new List<string>();results["fb101bad"]=bad;
        bool Part(string p)=>mode=="fb101"||mode=="fb101-"+p;
        var proj=GameManager.Project;var pl=player.ComponentPlayer;var terrain=proj.FindSubsystem<SubsystemTerrain>(true);var clock=proj.FindSubsystem<SubsystemTime>(true);
        const BindingFlags S=BindingFlags.Public|BindingFlags.Static,I=BindingFlags.Public|BindingFlags.Instance,N=BindingFlags.NonPublic|BindingFlags.Instance;
        object SG(Type t,string n)=>t.GetField(n,S) is {} f?f.GetValue(null):t.GetProperty(n,S)?.GetValue(null);
        bool SS(Type t,string n,object v){var f=t.GetField(n,S);if(f==null)return false;f.SetValue(null,v);return true;}
        object IG(object o,string n)=>o?.GetType().GetField(n,I) is {} f?f.GetValue(o):o?.GetType().GetProperty(n,I)?.GetValue(o);
        bool newBuild=typeof(MDThreat).GetField("Resumed",S)!=null;r["build"]=newBuild?"1.0.0 (11-13) (has MDThreat.Resumed)":"older package";
        void ClearAll(){foreach(var e in proj.Entities.Where(e=>e.FindComponent<ComponentCreature>()!=null&&e.FindComponent<ComponentPlayer>()==null).ToList())proj.RemoveEntity(e,true);dums.Clear();}
        IEnumerable<bool> Seconds(double s,Action each=null){for(double s0=Time.RealTime;Time.RealTime-s0<s;){each?.Invoke();yield return true;}}
        ComponentMDRobot Robot(string t,Vector3 at) {
            var e=DatabaseManager.CreateEntity(proj,t,true);e.FindComponent<ComponentBody>(true).Position=at;
            var sp=e.FindComponent<ComponentSpawn>(true);sp.SpawnDuration=0;sp.AutoDespawn=false;proj.AddEntity(e);return e.FindComponent<ComponentMDRobot>(true);
        }
        var C0=Home+new Vector3(0,0,-6);int X0=(int)MathF.Floor(C0.X),Z0=(int)MathF.Floor(C0.Z);
        void Fresh(){foreach(var p in proj.FindSubsystem<SubsystemPickables>(true).m_pickables)p.ToRemove=true;ClearAll();Remove(proj.Entities.Select(e=>e.FindComponent<ComponentMDUnit>()).Where(u=>u!=null));Clear(X0-34,X0+35,Z0-34,Z0+35);pl.ComponentBody.Position=C0+new Vector3(0,0,-12);pl.ComponentBody.Velocity=Vector3.Zero;}
        var inv=pl.ComponentMiner.Inventory;
        void EmptyHand(){inv.RemoveSlotItems(inv.ActiveSlotIndex,inv.GetSlotCount(inv.ActiveSlotIndex));}

        // ---------------------------------------------------------------- wave
        if(Part("wave")) {
            var w=new Dictionary<string,object>();r["wave"]=w;Fresh();while(Wait(.5))yield return true;
            var core=Place(MDTowerLibrary.Get("defense_core"),C0,0);foreach(var x in Seconds(1.5))yield return x;
            MDSettings.Endless=true;MDThreat.Threat=MDThreat.Cap;
            foreach(var x in Seconds(3))if(MDThreat.Now==MDThreat.Phase.Countdown)break;else yield return x;
            w["started"]=$"{MDThreat.Now} wave {MDThreat.Wave}";
            MDThreat.Wave=2;MDThreat.Timer=25;
            var saved=new XElement("Project");MDThreat.Save(saved);w["savedCountdown"]=string.Join(" ",saved.Attributes().Select(a=>$"{a.Name}={a.Value}"));
            MDThreat.Load(saved);foreach(var x in Seconds(1))yield return x;
            w["afterCountdown"]=$"{MDThreat.Now} wave {MDThreat.Wave + 1} timer {MDThreat.Timer:0} ({MDThreat.LastAnnounce})";
            if(MDThreat.Now!=MDThreat.Phase.Countdown||MDThreat.Wave!=2)bad.Add($"wave: the countdown of wave 3 read back as {MDThreat.Now} wave {MDThreat.Wave+1}");
            MDThreat.Timer=0;foreach(var x in Seconds(4))if(MDThreat.Now==MDThreat.Phase.Wave&&MDThreat.Fighting.Count>0)break;else yield return x;
            foreach(var rb in MDThreat.Fighting)rb.Health.AttackResilience*=1000;
            int n=MDThreat.Fighting.Count;w["wave3"]=$"{MDThreat.Now} wave {MDThreat.Wave+1}, {n} robots";
            if(n>0){var vd=new ValuesDictionary();MDThreat.Fighting[0].Save(vd,null);bool kept=vd.GetValue("ThreatRaid",false);w["robotSavesThreatRaid"]=kept;if(!kept)bad.Add("wave: a raid robot does not save ThreatRaid");}
            saved=new XElement("Project");MDThreat.Save(saved);w["savedWave"]=string.Join(" ",saved.Attributes().Select(a=>$"{a.Name}={a.Value}"));
            if(newBuild)foreach(var rb in MDThreat.Fighting.ToList()){var vd=new ValuesDictionary();rb.Save(vd,null);rb.ThreatRaid=vd.GetValue("ThreatRaid",false);}   // as the robots load
            MDThreat.Load(saved);foreach(var x in Seconds(1))yield return x;
            w["afterWave"]=$"{MDThreat.Now} wave {MDThreat.Wave+1}, {MDThreat.Fighting.Count} robots ({MDThreat.LastAnnounce}); resumed {SG(typeof(MDThreat),"Resumed")}";
            if(MDThreat.Now!=MDThreat.Phase.Wave||MDThreat.Wave!=2||MDThreat.Fighting.Count!=n)bad.Add($"wave: wave 3 under way read back as {MDThreat.Now} wave {MDThreat.Wave+1} with {MDThreat.Fighting.Count} of {n} robots");
            foreach(var x in Seconds(.6,()=>Cam(C0+new Vector3(12,10,-14),C0)))yield return x;Shot("f101_wave_resumed");yield return true;
            MDSettings.Endless=false;MDThreat.Load(new XElement("Project"));MDThreat.Threat=0;Fresh();while(Wait(.5))yield return true;
        }

        // ---------------------------------------------------------------- armour
        if(Part("armour")) {
            var a=new Dictionary<string,object>();r["armour"]=a;Fresh();while(Wait(.5))yield return true;
            var att=Robot("MDRobot05",C0+new Vector3(20,0,20));att.ForceClip="idle";
            var clothing=pl.Entity.FindComponent<ComponentClothing>(true);var cb=BlocksManager.Blocks[ClothingBlock.Index];
            int Cloth(int index)=>Terrain.MakeBlockValue(ClothingBlock.Index,0,ClothingBlock.SetClothingIndex(0,index));
            var slots=new[]{ClothingSlot.Head,ClothingSlot.Torso,ClothingSlot.Legs,ClothingSlot.Feet};
            void Wear(int[] set){for(int i=0;i<4;i++)clothing.SetClothes(slots[i],set==null?[]:[Cloth(set[i])]);}
            float Mean(int[] set) {
                float sum=0;const int hits=60;const float power=40;
                for(int i=0;i<hits;i++){Wear(set);var hit=new MDRobotAttackment(pl.Entity,att.Entity,pl.ComponentBody.Position+Vector3.UnitY,Vector3.UnitX,power);sum+=hit.CalculateInjuryAmount()*pl.ComponentHealth.AttackResilience;}
                return sum/hits;
            }
            int[] diamond=[21,19,20,22];int[] soul=[90,92,91,93];
            bool hasSoul=cb.GetClothingData(Cloth(90)) is {} sd&&float.IsPositiveInfinity(sd.Sturdiness);a["ghoulMod"]=hasSoul?"soul set loaded (AP 1, sturdiness infinity)":"not loaded";
            float none=Mean(null),strong=Mean(diamond),soulMean=hasSoul?Mean(soul):-1;Wear(null);
            a["none"]=MathF.Round(none,2);a["gameArmour"]=MathF.Round(strong,2);a["soulSet"]=MathF.Round(soulMean,2);a["capped"]=SG(typeof(MDRobotAttackment),"Capped");
            if(none<39)bad.Add($"armour: no clothes took {none:0.0} of 40");
            if(strong<40*.2f-.01f)bad.Add($"armour: the game's armour let {strong:0.00} of 40 through (at least 8 wanted)");
            if(!hasSoul)bad.Add("armour: the ghoul mod's soul set is not loaded");else if(soulMean<40*.2f-.01f)bad.Add($"armour: the soul set let {soulMean:0.00} of 40 through (at least 8 wanted)");
            if(att.Entity.IsAddedToProject)proj.RemoveEntity(att.Entity,true);
        }

        // ---------------------------------------------------------------- vanilla
        if(Part("vanilla")) {
            var v=new Dictionary<string,object>();r["vanilla"]=v;Fresh();while(Wait(.5))yield return true;
            var gun=Place(MDTowerLibrary.Get("battle_turret_1"),C0,MathF.PI);var wolf=Dummy(C0+new Vector3(0,0,8));
            bool hasSetting=typeof(MDSettings).GetField("AttackVanilla",S)!=null;v["setting"]=hasSetting;
            // "hit": the gun hurt the wolf or locked on to it (the setting decides the targeting; on API 1.9.4 with the ghoul mod's 1.9.4 build its
            // bullets did not hurt the lab's wolf: v[label+"Detail"] says which)
            IEnumerable<bool> Watch(string label,double s){wolf.Min=1;bool locked=false;int shots0=gun.ShotCount;foreach(var x in Seconds(s,()=>{Cam(C0+new Vector3(9,5,-5),C0+new Vector3(0,1,4));locked|=gun.Target==wolf.Body;}))if(wolf.Hit)break;else yield return x;
                v[label]=wolf.Hit||locked?"hit":"not hit";v[label+"Detail"]=$"hurt {wolf.Hit}, locked {locked}, shots {gun.ShotCount-shots0}";}
            SS(typeof(MDSettings),"AttackVanilla",true);foreach(var x in Watch("on",6))yield return x;
            SS(typeof(MDSettings),"AttackVanilla",false);foreach(var x in Seconds(1.5))yield return x;foreach(var x in Watch("off",6))yield return x;
            v["offIsEnemy"]=MDCombat.IsEnemy(gun,wolf.Body);Shot("f101_vanilla_off");yield return true;
            MDFaction.Attacked(new Attackment(pl.Entity,wolf.Entity,pl.ComponentBody.Position,Vector3.UnitX,1));foreach(var x in Watch("offAfterAttack",6))yield return x;
            var rb=Robot("MDRobot05",C0+new Vector3(-12,0,6));rb.ForceClip="idle";v["robotEnemyOff"]=MDCombat.IsEnemy(gun,rb.Body);
            SS(typeof(MDSettings),"AttackVanilla",true);
            if((string)v["on"]!="hit")bad.Add("vanilla: setting on, the wolf was not hit");
            if((string)v["off"]!="not hit")bad.Add("vanilla: setting off, the wolf was hit");
            if((string)v["offAfterAttack"]!="hit")bad.Add("vanilla: setting off, the wolf that attacked the player was not hit");
            if(!(bool)v["robotEnemyOff"])bad.Add("vanilla: setting off, a robot is no longer an enemy");
        }

        // ---------------------------------------------------------------- hard
        if(Part("hard")) {
            var h=new Dictionary<string,object>();r["hard"]=h;Fresh();while(Wait(.5))yield return true;
            int granite=BlocksManager.GetBlockIndex<GraniteBlock>(),dirt=2;
            var boss=Robot("MDBoss19",C0+new Vector3(.5f,0,.5f));boss.ForceClip="idle";foreach(var x in Seconds(.5))yield return x;
            var box=boss.Body.BoundingBox;h["box"]=$"{box.Min} .. {box.Max}";
            // cells in the half-metre margin round its box but not in the box (a cell in its body pushed it up, out of reach: first run)
            int y0=(int)MathF.Floor(box.Min.Y+.6f),zc=(int)MathF.Floor((box.Min.Z+box.Max.Z)*.5f);
            int gx=(int)MathF.Floor(box.Max.X+.5f),dx=gx,dz=zc-2;
            // a 2-high dirt platform in the +z margin, a small tower on it (put first; the cells right before the pass, before physics moves it)
            int pz=(int)MathF.Floor(box.Max.Z+.5f);int px=(int)MathF.Floor(box.Min.X)+1;
            var tower=Place(MDTowerLibrary.Get("battle_turret_1"),new Vector3(px+1.5f,72,pz+1f),0);foreach(var x in Seconds(1))yield return x;
            for(int x=px;x<px+3;x++)for(int z=pz;z<pz+3;z++)for(int y=70;y<72;y++)terrain.ChangeCell(x,y,z,dirt);
            terrain.ChangeCell(gx,y0,zc,granite);terrain.ChangeCell(dx,y0,dz,dirt);
            var tb=tower.Body.BoundingBox;h["tower"]=$"{tb.Min} .. {tb.Max}";
            int Cell(int x,int y,int z)=>Terrain.ExtractContents(terrain.Terrain.GetCellValue(x,y,z));
            var footing=new List<Point3>();for(int x=(int)MathF.Floor(tb.Min.X+.05f);x<=(int)MathF.Floor(tb.Max.X-.05f);x++)for(int z=(int)MathF.Floor(tb.Min.Z+.05f);z<=(int)MathF.Floor(tb.Max.Z-.05f);z++)foreach(int y in new[]{70,71})if(z<=pz&&Cell(x,y,z)==dirt)footing.Add(new Point3(x,y,z));
            h["footingInReach"]=footing.Count;
            var clear=typeof(MDBoss).GetMethod("ClearBlocks",S);var state=boss.Boss;bool three=clear.GetParameters().Length==3;
            int Pass()=>(int)clear.Invoke(null,three?[boss.Body,false,state]:[boss.Body,false]);
            int first=Pass();
            h["firstPass"]=$"cleared {first}; granite {(Cell(gx,y0,zc)==granite?"kept":"gone")}, dirt {(Cell(dx,y0,dz)==dirt?"kept":"gone")}, footing kept {footing.Count(c=>Cell(c.X,c.Y,c.Z)==dirt)}/{footing.Count}";
            if(Cell(gx,y0,zc)!=granite)bad.Add("hard: granite cleared at once");
            if(Cell(dx,y0,dz)==dirt)bad.Add("hard: dirt not cleared");
            if(footing.Count==0)bad.Add("hard: the platform under the tower did not reach the boss's box (test setup)");
            else if(footing.Any(c=>Cell(c.X,c.Y,c.Z)!=dirt))bad.Add("hard: a tower's footing cleared at once");
            Cam(C0+new Vector3(14,8,14),C0);Shot("f101_hard_first_pass");yield return true;
            if(three) {
                state.WalkWanted=new Vector2(0,1);typeof(MDBossState).GetField("walkVelocity",BindingFlags.NonPublic|BindingFlags.Instance).SetValue(state,new Vector3(3,0,0));
                int graniteGone=-1,footingGone=-1;
                for(int i=1;i<=80;i++){Pass();if(graniteGone<0&&Cell(gx,y0,zc)!=granite)graniteGone=i;if(footingGone<0&&footing.Any(c=>Cell(c.X,c.Y,c.Z)!=dirt))footingGone=i;}
                float wait=(float)SG(typeof(MDBoss),"HardWait");
                h["grind"]=$"passes of 0.25 s: granite gone at {graniteGone} ({graniteGone*.25f:0.00} s), first footing cell at {footingGone} ({footingGone*.25f:0.00} s); wait {wait} s; ground {SG(typeof(MDBoss),"HardGround")}";
                if(graniteGone<0||graniteGone*.25f<wait+3.5f||graniteGone*.25f>wait+6)bad.Add($"hard: granite ground through after {graniteGone*.25f:0.00} s (wanted {wait+4.2f:0.0} s or so)");
                if(footing.Count>0&&(footingGone<0||footingGone*.25f<wait+2.5f))bad.Add($"hard: footing ground through after {footingGone*.25f:0.00} s");
                state.WalkWanted=null;
                var hw=typeof(MDBoss).GetMethod("HardOnWay",S);var from=boss.Body.Position;var to=from+new Vector3(-20,0,0);
                for(int y=70;y<73;y++)for(int z=zc-4;z<=zc+4;z++)terrain.ChangeCell((int)MathF.Floor(from.X)-12,y,z,granite);
                int wall=(int)hw.Invoke(null,[boss.Body,state,from,to,2f,.3f,3f]),free=(int)hw.Invoke(null,[boss.Body,state,from,from+new Vector3(0,0,-20),2f,.3f,3f]);
                h["hardOnWay"]=$"granite wall {wall}, free drive {free}";
                if(wall<=0||free!=0)bad.Add($"hard: HardOnWay wall {wall}, free {free}");
            } else bad.Add("hard: no hard-block rule in this build");
            if(tower.Entity.IsAddedToProject)Remove([tower]);
            Fresh();while(Wait(.5))yield return true;
        }

        // ---------------------------------------------------------------- ranges
        if(Part("ranges")) {
            var g=new Dictionary<string,object>();r["ranges"]=g;Fresh();while(Wait(.5))yield return true;
            var rnd=new System.Random(5);
            for(int bx=-8;bx<8;bx++)for(int bz=-8;bz<8;bz++){if(Math.Abs(bx)<=1&&Math.Abs(bz)<=1)continue;int hgt=rnd.Next(0,3);for(int x=X0+bx*3;x<X0+bx*3+3;x++)for(int z=Z0+bz*3;z<Z0+bz*3+3;z++)for(int y=70;y<70+hgt;y++)terrain.ChangeCell(x,y,z,dirt0());}
            int dirt0()=>2;
            var gun=Place(MDTowerLibrary.Get("battle_turret_1"),C0,0);MDTowerTools.Shown.Clear();MDTowerTools.Shown.Add(gun);
            int Joins(){return MDTowerWorld.JoinsDrawn;}
            var eye=C0+new Vector3(-22,24,-22);
            bool flatSet=SS(typeof(MDSettings),"RangeTerrain",false);
            int jf=0;foreach(var x in Seconds(2.6,()=>{Cam(eye,C0);jf=Joins();}))yield return x;
            g["flat"]=$"joins {jf}, rings {MDTowerWorld.RingsDrawn}";Shot("f101_ranges_flat");yield return true;
            SS(typeof(MDSettings),"RangeTerrain",true);
            int jt=0;foreach(var x in Seconds(2.6,()=>{Cam(eye,C0);jt=Joins();}))yield return x;
            g["terrain"]=$"joins {jt}, rings {MDTowerWorld.RingsDrawn}";Shot("f101_ranges_terrain");yield return true;
            SS(typeof(MDSettings),"RangeTerrain",false);
            if(!flatSet)bad.Add("ranges: no RangeTerrain setting in this build");
            if(jf!=0)bad.Add($"ranges: the default ring has {jf} vertical joins (level wanted)");
            if(jt==0)bad.Add("ranges: the terrain ring has no joins on bumpy ground");
            MDTowerTools.Shown.Clear();Fresh();while(Wait(.5))yield return true;
        }

        // ---------------------------------------------------------------- click
        if(Part("click")) {
            var c=new Dictionary<string,object>();r["click"]=c;Fresh();while(Wait(.5))yield return true;
            var gun=Place(MDTowerLibrary.Get("battle_turret_1"),C0,0);foreach(var x in Seconds(1))yield return x;
            var eye=C0+new Vector3(0,2.4f,-3.4f);pl.ComponentBody.Position=C0+new Vector3(0,0,-3.2f);pl.ComponentBody.Velocity=Vector3.Zero;
            IEnumerable<bool> Interact(Vector3 at){foreach(var x in Hold(eye,at,3))yield return x;LabInteract.Pending=true;for(double s0=Time.RealTime;Time.RealTime-s0<.4||LabInteract.Pending;){Cam(eye,at);yield return true;}}
            IEnumerable<bool> Hit(Vector3 at){foreach(var x in Hold(eye,at,3))yield return x;LabClick.Pending=true;for(double s0=Time.RealTime;Time.RealTime-s0<.4||LabClick.Pending;){Cam(eye,at);yield return true;}}
            // with an item: nothing
            inv.RemoveSlotItems(inv.ActiveSlotIndex,inv.GetSlotCount(inv.ActiveSlotIndex));inv.AddSlotItems(inv.ActiveSlotIndex,2,1);
            foreach(var x in Interact(gun.Body.Position+Vector3.UnitY*1.2f))yield return x;
            c["withItem"]=MDTowerTools.Moving==null?"not picked":"picked";if(MDTowerTools.Moving!=null){bad.Add("click: picked up with dirt in the hand");MDTowerTools.Stop(pl,"");}
            EmptyHand();var before=gun.Body.Position;int moves=MDTowerTools.Moves;
            foreach(var x in Interact(gun.Body.Position+Vector3.UnitY*1.2f))yield return x;
            foreach(var x in Seconds(.3,()=>Cam(eye,gun.Body.Position+Vector3.UnitY*1.2f)))yield return x;
            c["emptyHand"]=MDTowerTools.Moving==gun?"picked":"not picked";c["clicks"]=SG(typeof(MDTowerTools),"TowerClicks");
            if(MDTowerTools.Moving!=gun)bad.Add("click: an empty-hand right-click did not pick the tower up");
            else {
                Shot("f101_click_picked");yield return true;
                var spot=C0+new Vector3(6,0,2);foreach(var x in Hit(spot))yield return x;foreach(var x in Seconds(.4))yield return x;
                c["put"]=$"moves {MDTowerTools.Moves-moves}, from {before} to {gun.Body.Position}, moving {(MDTowerTools.Moving==null?"no":"yes")} ({MDTowerTools.LastText})";
                if(MDTowerTools.Moves-moves!=1||Vector3.Distance(before,gun.Body.Position)<3)bad.Add("click: the left-click did not put it down there: "+c["put"]);
                if(MDTowerTools.Moving!=null)MDTowerTools.Stop(pl,"");
            }
            Fresh();while(Wait(.5))yield return true;
        }

        // ---------------------------------------------------------------- core
        if(Part("core")) {
            var k=new Dictionary<string,object>();r["core"]=k;Fresh();while(Wait(.5))yield return true;
            var core=Place(MDTowerLibrary.Get("defense_core"),C0,0);foreach(var x in Seconds(1))yield return x;
            foreach(var p in proj.FindSubsystem<SubsystemPickables>(true).m_pickables)p.ToRemove=true;foreach(var x in Seconds(.3))yield return x;   // nothing left from the parts before
            var store=IG(core,"Storage");k["store"]=store==null?"none":store.GetType().Name;
            var pk=proj.FindSubsystem<SubsystemPickables>(true);int scrap=MDPartBlock.Value(MDPartBlock.Kind.Scrap),xp=Terrain.MakeBlockValue(BlocksManager.GetBlockIndex<ExperienceBlock>());
            pk.AddPickable(scrap,5,C0+new Vector3(3,1,0),Vector3.Zero,null);pk.AddPickable(2,3,C0+new Vector3(-3,1,2),Vector3.Zero,null);
            pk.AddPickable(xp,2,C0+new Vector3(2,1,3),Vector3.Zero,null);pk.AddPickable(2,3,C0+new Vector3(25,1,0),Vector3.Zero,null);
            int Lying(int value,Vector3 at,float r0)=>pk.m_pickables.Where(p=>!p.ToRemove&&p.Value==value&&Vector3.Distance(p.Position,at)<r0).Sum(p=>p.Count);
            foreach(var x in Seconds(3.2,()=>Cam(C0+new Vector3(-8,6,-10),C0)))yield return x;
            int held=store==null?0:(int)IG(store,"Items");
            k["absorbed"]=$"store {held} items; lying: scrap {Lying(scrap,C0,8)}, dirt near {Lying(2,C0,8)}, experience {Lying(xp,C0,8)}, dirt 25 m off {Lying(2,C0+new Vector3(25,1,0),3)}";
            if(held!=8)bad.Add("core: the store took "+k["absorbed"]);
            if(Lying(xp,C0,8)!=2)bad.Add("core: experience was taken in");
            if(Lying(2,C0+new Vector3(25,1,0),3)!=3)bad.Add("core: an item 25 m off was taken in");
            var eye=C0+new Vector3(0,2.6f,-3.6f);pl.ComponentBody.Position=C0+new Vector3(0,0,-3.2f);pl.ComponentBody.Velocity=Vector3.Zero;EmptyHand();
            foreach(var x in Hold(eye,core.Body.Position+Vector3.UnitY*2.5f,3))yield return x;LabInteract.Pending=true;
            for(double s0=Time.RealTime;Time.RealTime-s0<.6||LabInteract.Pending;){Cam(eye,core.Body.Position+Vector3.UnitY*2.5f);yield return true;}
            var panel=pl.ComponentGui.ModalPanelWidget;k["panel"]=panel?.GetType().Name??"none";
            if(panel?.GetType().Name!="MDCoreStorageWidget")bad.Add($"core: the right-click opened {k["panel"]}");
            SettingsManager.ShowGuiInScreenshots=true;foreach(var x in Seconds(.6,()=>Cam(eye,core.Body.Position+Vector3.UnitY*2.5f)))yield return x;ScreenCaptureManager.Capture(1280,720,"f101_core_store.png");yield return true;SettingsManager.ShowGuiInScreenshots=false;
            pl.ComponentGui.ModalPanelWidget=null;foreach(var x in Seconds(.5))yield return x;
            if(store!=null) {
                var vd=new ValuesDictionary();store.GetType().GetMethod("Save").Invoke(store,[vd]);
                var copy=Activator.CreateInstance(store.GetType(),core);copy.GetType().GetMethod("Load").Invoke(copy,[vd]);
                k["saved"]=$"{IG(copy,"Items")} items read back of {held}";if((int)IG(copy,"Items")!=held)bad.Add("core: "+k["saved"]);
            }
            MDTowerMaintenance.Dismantle(core,null);foreach(var x in Seconds(.5))yield return x;
            k["spilled"]=$"scrap {Lying(scrap,C0,6)}, dirt {Lying(2,C0,6)} lying round the core's spot after it was taken down";
            if(store!=null&&(Lying(scrap,C0,6)!=5||Lying(2,C0,6)!=3))bad.Add("core: "+k["spilled"]);
            foreach(var p in pk.m_pickables)p.ToRemove=true;Fresh();while(Wait(.5))yield return true;
        }
        // ---------------------------------------------------------------- shield
        if(Part("shield")) {
            var h=new Dictionary<string,object>();r["shield"]=h;Fresh();while(Wait(.5))yield return true;
            var shieldType=typeof(MDTowerLibrary).Assembly.GetType("Game.MDShield");h["type"]=shieldType==null?"none":"MDShield";
            if(shieldType==null)bad.Add("shield: no MDShield in this build");
            else {
                var core=Place(MDTowerLibrary.Get("defense_core"),C0,0);var gd=MDTowerLibrary.Get("battle_turret_1");
                foreach(var x in Seconds(.6))yield return x;
                var cb=core.Body.BoundingBox;float cw=cb.Max.X-cb.Min.X+.1f;
                var probe=Place(gd,C0+new Vector3(0,0,-20),0);foreach(var x in Seconds(.3))yield return x;float tw=probe.Body.BoxSize.X+.1f,td=probe.Body.BoxSize.Z+.1f;Remove([probe]);
                // t1 against the core's +x face, t2 against t1's, t3 corner to corner with t2 (not a member), t4 far off
                var cx=(cb.Min.X+cb.Max.X)*.5f;var cz=(cb.Min.Z+cb.Max.Z)*.5f;
                var t1=Place(gd,new Vector3(cx+cw*.5f+tw*.5f,70,cz),0);var t2=Place(gd,new Vector3(cx+cw*.5f+tw*1.5f,70,cz),0);
                var t3=Place(gd,new Vector3(cx+cw*.5f+tw*2.5f,70,cz+td),0);var t4=Place(gd,new Vector3(cx,70,cz+40),0);
                t1.SetHitpoints(.8f);t2.SetHitpoints(.5f);t3.SetHitpoints(1);t4.SetHitpoints(1);   // in the same frame: the shield's next look finds them so (a first run set them 0.3 s late, after it had taken 99 % of each)
                float hp=gd.hp;foreach(var x in Seconds(1.6))yield return x;
                float F(ComponentMDUnit u,string n)=>(float)typeof(ComponentMDUnit).GetField(n).GetValue(u);
                int Members()=>(int)shieldType.GetMethod("MemberCount").Invoke(null,[proj]);
                int members=Members();float radius=(float)shieldType.GetMethod("Radius").Invoke(null,[proj]);
                float pts=F(core,"ShieldPoints"),want=(.79f+.49f)*hp;
                h["joined"]=$"members {members}, radius {radius:0.0}, store {pts:0} (wanted {want:0}), hp t1 {t1.Damage.Hitpoints:0.000} t2 {t2.Damage.Hitpoints:0.000} t3 {t3.Damage.Hitpoints:0.000}";
                if(members!=2)bad.Add("shield: "+h["joined"]);
                if(MathF.Abs(pts-want)>2||MathF.Abs(t1.Damage.Hitpoints-.01f)>1e-3f||MathF.Abs(t2.Damage.Hitpoints-.01f)>1e-3f||t3.Damage.Hitpoints<.999f)bad.Add("shield: handed over "+h["joined"]);
                float far=Vector2.Distance(new(t2.Body.BoundingBox.Max.X,t2.Body.BoundingBox.Max.Z),new(core.Body.Position.X,core.Body.Position.Z));if(radius<far)bad.Add($"shield: radius {radius:0.0} short of t2's far corner {far:0.0}");
                foreach(var x in Seconds(.6,()=>Cam(C0+new Vector3(-10,9,-16),C0+new Vector3(3,2,0))))yield return x;Shot("f101_shield_dome");yield return true;
                var rb=Robot("MDRobot05",C0+new Vector3(-30,0,0));rb.ForceClip="idle";var k=rb.Kind;
                float before=F(core,"ShieldPoints");MDRobots.Hit(rb.Entity,t1.Body,k,1,t1.Body.Position+Vector3.UnitY,Vector3.UnitX);float after=F(core,"ShieldPoints");
                h["hitMember"]=$"store {before:0} -> {after:0}, t1 {t1.Damage.Hitpoints:0.000}";if(after>=before||t1.Damage.Hitpoints<.0099f)bad.Add("shield: a hit on a member "+h["hitMember"]);
                pl.ComponentBody.Position=C0+new Vector3(-1.5f,0,-3);foreach(var x in Seconds(.2))yield return x;
                before=F(core,"ShieldPoints");MDRobots.Hit(rb.Entity,pl.ComponentBody,k,1,pl.ComponentBody.Position+Vector3.UnitY,Vector3.UnitX);after=F(core,"ShieldPoints");
                h["hitPlayer"]=$"store {before:0} -> {after:0}";if(after>=before)bad.Add("shield: a hit on the player inside "+h["hitPlayer"]);
                float t4before=t4.Damage.Hitpoints;MDRobots.Hit(rb.Entity,t4.Body,k,1,t4.Body.Position+Vector3.UnitY,Vector3.UnitX);
                h["hitOutside"]=$"t4 {t4before:0.000} -> {t4.Damage.Hitpoints:0.000}";if(t4.Damage.Hitpoints>=t4before)bad.Add("shield: a tower outside took no damage "+h["hitOutside"]);
                // t2 moved off the chain: its share back
                float share=F(core,"ShieldPoints")/(F(t1,"ShieldPooled")+F(t2,"ShieldPooled"));float back=F(t2,"ShieldPooled")*share/hp;
                t2.Entity.FindComponent<ComponentMDTowerBody>(true).FixAt(C0+new Vector3(-14,0,14),Quaternion.Identity);foreach(var x in Seconds(1.2))yield return x;
                h["left"]=$"t2 {t2.Damage.Hitpoints:0.000} (wanted {.01f+back:0.000}), members {Members()}";
                if(MathF.Abs(t2.Damage.Hitpoints-(.01f+back))>.01f)bad.Add("shield: left "+h["left"]);
                // the core destroyed: t1 stays at 1 % and goes off line
                typeof(ComponentMDUnit).GetField("ShieldPoints").SetValue(core,0f);core.SetHitpoints(0);foreach(var x in Seconds(1.5))yield return x;
                h["coreLost"]=$"t1 {t1.Damage.Hitpoints:0.000}, offline {t1.Offline}";
                if(t1.Damage.Hitpoints>.0101f||!t1.Offline)bad.Add("shield: core lost "+h["coreLost"]);
                if(rb.Entity.IsAddedToProject)proj.RemoveEntity(rb.Entity,true);
            }
            Fresh();while(Wait(.5))yield return true;
        }
        pl.ComponentBody.Position=Home;
        if(bad.Count>0)throw new Exception("fb101: "+string.Join(" || ",bad));
    }
}
