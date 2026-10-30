using Engine;
using Engine.Graphics;
using Game;

namespace TowerLab;

// lab --mode combat15 (0.15.0, user 2026-10-30; design docs/tasks/2026-10-30-combat-0.15.md). Parts (combat15-<part> runs one):
// reactions: each of the eight reactions from two hits of real towers' elements on RD05s (damage read from health x resilience),
//   the cooldowns, the setting off, resistances, the tank's front / rear armour, the icons over a target and the tower icons;
// behaviour: the repair drone mends a hurt RD05 (and is tinted green), RD05 steps aside when a player aims at it and backs off
//   when hurt, RD05 and the drones go for a player at the defences / alone (Survival), RD07 marks where its shell lands;
// growth: 21 days on, a new RD05 has 1.3 x the health; rewards: a kill mostly by a player drops extra, a boss's broken layer
//   and its first kill drop extra, a beaten raid pays out; texts: help, bestiary, item text, settings round trip.
// Results under "combat15"; screenshots c15_*.
public partial class Lab {
    static IEnumerable<bool> Combat15() {
        Remove(placed.Select(p=>p.unit));placed.Clear();while(Wait(1))yield return true;
        var r=new Dictionary<string,object>();results["combat15"]=r;var proj=GameManager.Project;var pl=player.ComponentPlayer;var bad=new List<string>();
        var w=MDTowerWorld.For(proj);var gameInfo=proj.FindSubsystem<SubsystemGameInfo>(true);var terrain=proj.FindSubsystem<SubsystemTerrain>(true).Terrain;
        bool Part(string p)=>mode=="combat15"||mode=="combat15-"+p;
        var defs=MDTowerLibrary.All;MDTowerDef D(string id)=>defs.First(x=>x.id==id);
        Vector3 Ground(Vector3 at){at.Y=terrain.GetTopHeight(Terrain.ToCell(at.X),Terrain.ToCell(at.Z))+1.05f;return at;}
        ComponentMDRobot Spawn(string t,Vector3 at,float yaw=0) {
            var e=DatabaseManager.CreateEntity(proj,t,true);var b=e.FindComponent<ComponentBody>(true);b.Position=Ground(at);b.Rotation=Quaternion.CreateFromAxisAngle(Vector3.UnitY,yaw);
            var sp=e.FindComponent<ComponentSpawn>(true);sp.SpawnDuration=0;sp.AutoDespawn=false;proj.AddEntity(e);return e.FindComponent<ComponentMDRobot>(true);
        }
        void Kill(){foreach(var x in proj.Entities.Select(e=>e.FindComponent<ComponentMDRobot>()).Where(x=>x!=null).ToList())proj.RemoveEntity(x.Entity,true);}
        void ClearPickables(){foreach(var p in proj.FindSubsystem<SubsystemPickables>(true).Pickables.ToList())p.ToRemove=true;}
        // towers whose elements are used, far from the fights (they never fire at these robots)
        var far=Home+new Vector3(0,0,70);
        var src=new Dictionary<string,ComponentMDUnit>{["heat"]=Place(D("flame_1"),far),["cold"]=Place(D("frost_1"),far+new Vector3(6,0,0)),["electric"]=Place(D("tesla_1"),far+new Vector3(12,0,0)),
            ["corrosive"]=Place(D("battle_debuff_1"),far+new Vector3(18,0,0)),["explosive"]=Place(D("cannon_1"),far+new Vector3(24,0,0)),["kinetic"]=Place(D("battle_turret_1"),far+new Vector3(30,0,0)),["sniper"]=Place(D("battle_sniper_1"),far+new Vector3(36,0,0))};
        while(Wait(.5))yield return true;
        float Deal(string el,ComponentMDRobot t,float power,MDElement? e=null,Vector3? point=null) {
            var h=t.Health;float before=h.Health;MDTowerBrain.Hit(src[el],t.Body,power,point??MDCombat.Center(t.Body),Vector3.UnitZ,false,null,e);return (before-h.Health)*h.AttackResilience;
        }
        // three RD05s close together at a spot, tough (no deaths), facing +Z
        List<ComponentMDRobot> Cluster(Vector3 at){var l=new List<ComponentMDRobot>{Spawn("MDRobot05",at),Spawn("MDRobot05",at+new Vector3(2,0,0)),Spawn("MDRobot05",at+new Vector3(-2,0,1))};foreach(var x in l)x.Health.AttackResilience=1e5f;return l;}
        pl.ComponentBody.Position=Home+new Vector3(0,0,20);
        if(Part("reactions")) {
            var rr=new Dictionary<string,object>();r["reactions"]=rr;var spot=Home+new Vector3(0,0,-6);
            // one case: a cluster, hit 1 then hit 2 on the first robot, then the check
            IEnumerable<bool> Case(string name,string first,string second,Func<List<ComponentMDRobot>,float,string> check,float power2=100,bool shot=false) {
                var c=Cluster(spot);while(Wait(.4))yield return true;int n0=MDElements.Count;
                Deal(first,c[0],100);float d2=Deal(second,c[0],power2);
                if(shot){var at=c[0].Body.Position;Cam(at+new Vector3(-5,3.5f,6),at+Vector3.UnitY*2.5f);for(int i=0;i<6;i++)yield return true;Shot("c15_"+name);yield return true;}
                string why=check(c,d2);rr[name]=$"{d2:0} damage, reactions +{MDElements.Count-n0}, last: {(MDElements.Log.Count>0?MDElements.Log[^1]:"-")}"+(why!=null?" | "+why:"");
                if(why!=null)bad.Add($"{name}: {rr[name]}");
                Kill();ClearPickables();while(Wait(.4))yield return true;
            }
            string Last(string want)=>MDElements.Log.Count>0&&MDElements.Log[^1].StartsWith(want)?null:$"want {want}";
            foreach(var x in Case("thermal","heat","cold",(c,d)=>Last("热冲击")??(d<3000?"no bonus":null)??(w.HasStatus(c[0].Body,MDElement.Heat)||w.HasStatus(c[0].Body,MDElement.Cold)?"statuses left":null),shot:true))yield return x;
            foreach(var x in Case("overload","heat","electric",(c,d)=>Last("过载")??(!w.IsStunned(c[0].Body)?"not paralysed":null)??(c[1].Health.Health>=1&&c[2].Health.Health>=1?"no blast":null),shot:true))yield return x;
            foreach(var x in Case("corrode","heat","corrosive",(c,d)=>Last("腐燃")??(c[0].Entity.FindComponent<ComponentOnFire>()?.IsOnFire!=true?"not burning":null)??(w.DamageFactor(c[0].Body)<=1?"no armour down":null)))yield return x;
            foreach(var x in Case("brittle","cold","electric",(c,d)=>{
                if(Last("脆化") is {} e)return e;if(!w.Brittle(c[0].Body))return "not brittle";
                float k=Deal("kinetic",c[0],100);return k<125||k>145?$"kinetic on brittle {k:0} (want 135)":null;}))yield return x;
            foreach(var x in Case("frostetch","cold","corrosive",(c,d)=>Last("冻蚀")??(w.SpeedFactor(c[0].Body)>=1?"not slowed":null)??(w.DamageFactor(c[0].Body)<=1?"no armour down":null),shot:true))yield return x;
            foreach(var x in Case("electrolysis","electric","corrosive",(c,d)=>Last("电解")??(!w.HasStatus(c[1].Body,MDElement.Corrosive)?"no corrosive passed on":null)??(c[1].Health.Health>=1?"no arc damage":null)))yield return x;
            foreach(var x in Case("spread","heat","explosive",(c,d)=>Last("扩散")??(!w.HasStatus(c[1].Body,MDElement.Heat)?"heat not passed on":null)))yield return x;
            foreach(var x in Case("shatter","cold","kinetic",(c,d)=>Last("碎冰")??(d<150||d>170?$"{d:0} (want 160)":null)??(w.HasStatus(c[0].Body,MDElement.Cold)?"still cold":null)))yield return x;
            // three elements (user, 2026-10-30): a two-element reaction, then 1.6 s later a third element
            IEnumerable<bool> Triad(string name,string first,string second,string third,Func<List<ComponentMDRobot>,float,string> check) {
                var c=Cluster(spot);while(Wait(.4))yield return true;int n0=MDElements.Count;
                Deal(first,c[0],100);Deal(second,c[0],100);while(Wait(1.6))yield return true;float d3=Deal(third,c[0],100);
                var at=c[0].Body.Position;Cam(at+new Vector3(-5,3.5f,6),at+Vector3.UnitY*2.5f);for(int i=0;i<6;i++)yield return true;Shot("c15_"+name);yield return true;
                string why=check(c,d3);rr[name]=$"third hit {d3:0}, reactions +{MDElements.Count-n0}, {string.Join(" | ",MDElements.Log.TakeLast(2))}"+(why!=null?" | "+why:"");
                if(why!=null)bad.Add($"{name}: {rr[name]}");
                Kill();ClearPickables();while(Wait(.4))yield return true;
            }
            foreach(var x in Triad("storm","heat","cold","electric",(c,d)=>Last("雷暴")??(d<300?"no x3":null)??(c[1].Health.Health>=1?"no blast on the others":null)??(!w.IsStunned(c[0].Body)?"not paralysed":null)))yield return x;
            foreach(var x in Triad("crystal","cold","electric","corrosive",(c,d)=>Last("晶化")??(d<300?"no x3":null)??(!w.IsFrozen(c[0].Body)?"not frozen":null)))yield return x;
            foreach(var x in Triad("meltdown","heat","corrosive","cold",(c,d)=>Last("熔解")??(d<350?"no x3.5":null)??(w.DamageFactor(c[0].Body)<1.45f?"no armour down":null)))yield return x;
            // cooldowns: right after a reaction no other one (1.5 s); after 2 s the same reaction still waits (6 s), another one goes
            {
                var c=Cluster(spot);while(Wait(.4))yield return true;
                Deal("heat",c[0],100);Deal("cold",c[0],100);int n1=MDElements.Count;
                Deal("heat",c[0],100);Deal("cold",c[0],100);int n2=MDElements.Count;
                while(Wait(2))yield return true;
                Deal("heat",c[0],100);Deal("cold",c[0],100);int n3=MDElements.Count;
                Deal("electric",c[0],100);int n4=MDElements.Count;
                rr["cooldown"]=$"right after: +{n2-n1}, same reaction 2 s later: +{n3-n2}, another reaction then: +{n4-n3} ({MDElements.Log[^1]})";
                if(n2!=n1||n3!=n2||n4!=n3+1)bad.Add("cooldown: "+rr["cooldown"]);
                Kill();while(Wait(.4))yield return true;
            }
            // the setting off: statuses stay, no reaction
            {
                MDSettings.Reactions=false;var c=Cluster(spot);while(Wait(.4))yield return true;int n0=MDElements.Count;
                Deal("heat",c[0],100);float d=Deal("cold",c[0],100);bool both=w.HasStatus(c[0].Body,MDElement.Heat)&&w.HasStatus(c[0].Body,MDElement.Cold);
                rr["off"]=$"reactions +{MDElements.Count-n0}, cold hit {d:0}, both statuses {both}";MDSettings.Reactions=true;
                if(MDElements.Count!=n0||!both||d>110)bad.Add("setting off: "+rr["off"]);
                Kill();while(Wait(.4))yield return true;
            }
            // resistances (RD05: explosive x 1.25) and the tank's armour (front x 0.75, rear x 1.4; element none to see only that)
            {
                var c=Cluster(spot);while(Wait(.4))yield return true;
                float ex=Deal("explosive",c[0],100),ki=Deal("kinetic",c[1],100);
                var tank=Spawn("MDRobot14",spot+new Vector3(0,0,-8),0);tank.Health.AttackResilience=1e5f;while(Wait(.4))yield return true;
                var f=tank.Body.Matrix.Forward;var cc=MDCombat.Center(tank.Body);
                float front=Deal("kinetic",tank,100,MDElement.None,cc+f*2),rear=Deal("kinetic",tank,100,MDElement.None,cc-f*2),side=Deal("kinetic",tank,100,MDElement.None,cc+tank.Body.Matrix.Right*2);
                rr["resist"]=$"RD05 explosive {ex:0}, kinetic {ki:0}; RD14 front {front:0}, rear {rear:0}, side {side:0}";
                if(MathF.Abs(ex-125)>6||MathF.Abs(ki-100)>6||MathF.Abs(front-75)>5||MathF.Abs(rear-140)>7||MathF.Abs(side-100)>6)bad.Add("resist: "+rr["resist"]);
                Kill();while(Wait(.4))yield return true;
            }
            // icons: a robot with three statuses shows three icons; the towers show theirs
            {
                var c=Cluster(spot);while(Wait(.4))yield return true;
                Deal("heat",c[0],100);while(Wait(1.6))yield return true;MDTowerWorld.For(proj).Slow(c[0].Body,.5f,8);
                w.ArmorDown(c[0].Body,1.2f,8);var icons=w.Icons(c[0].Body);
                var at=c[0].Body.Position;Cam(at+new Vector3(-4,3,5),at+Vector3.UnitY*3);for(int i=0;i<5;i++)yield return true;int drawn=MDTowerWorld.IconsDrawn;Shot("c15_icons");yield return true;
                var tEye=far+new Vector3(15,4,-7);Cam(tEye,far+new Vector3(15,2,0));for(int i=0;i<5;i++)yield return true;int towerIcons=MDTowerWorld.IconsDrawn;Shot("c15_tower_icons");yield return true;
                int inReach=src.Values.Count(u=>Vector3.Distance(u.Body.Position,tEye)<=MDTowerWorld.TowerIconRange);
                var tex=ContentManager.Get<Texture2D>("Towers/status");
                rr["icons"]=$"{string.Join(",",icons)}; drawn near it {drawn}, by the towers {towerIcons} ({inReach} towers within {MDTowerWorld.TowerIconRange:0} m); atlas {tex?.Width}x{tex?.Height}";
                if(icons.Count<3||drawn<3||inReach<3||towerIcons!=inReach||tex==null||tex.Width!=256)bad.Add("icons: "+rr["icons"]);
                Kill();while(Wait(.4))yield return true;
            }
        }
        if(Part("behaviour")) {
            var bh=new Dictionary<string,object>();r["behaviour"]=bh;var mode0=gameInfo.WorldSettings.GameMode;var res0=pl.ComponentHealth.AttackResilience;
            // the repair drone
            {
                var hurt=Spawn("MDRobot05",Home+new Vector3(0,0,-14));var rp=Spawn("MDRepair09",Home+new Vector3(8,0,-14));while(Wait(.5))yield return true;
                hurt.Health.Health=.5f;hurt.Creature.ComponentLocomotion.WalkSpeed=0;
                foreach(var x in During(8,()=>{hurt.Health.Health=Math.Min(hurt.Health.Health,1);}))yield return x;
                var at=hurt.Body.Position;Cam(at+new Vector3(-8,8,10),at+Vector3.UnitY*4);for(int i=0;i<3;i++)yield return true;Shot("c15_repair");yield return true;
                var tint=MDTowerWorld.Tint(rp.Entity.FindComponent<ComponentMDRobotModel>(true));
                bh["repair"]=$"RD05 0.50 -> {hurt.Health.Health:0.00}, mended {rp.Mended:0.00}, patient {(rp.Patient==hurt?"the hurt RD05":rp.Patient?.Kind.Id??"-")}, shots {rp.ShotsFired}, tint {tint}";
                if(hurt.Health.Health<.6f||rp.ShotsFired>0||tint==null)bad.Add("repair: "+bh["repair"]);
                Kill();while(Wait(.5))yield return true;
            }
            // RD05 steps aside when a player aims at it, backs off when hurt (it fights a gun tower meanwhile; tough, so it lives)
            {
                var tw=Place(D("battle_turret_1"),Home+new Vector3(0,0,-30),0);var rd=Spawn("MDRobot05",Home+new Vector3(0,0,-8),MathF.PI);rd.Health.AttackResilience=1e5f;
                pl.ComponentBody.Position=Ground(Home+new Vector3(0,0,10));while(Wait(2))yield return true;
                var aimAt=MDCombat.Center(rd.Body);int s0=rd.Strafes;bool aimed=false;
                foreach(var x in During(4,()=>{var v=MDCombat.Center(rd.Body)-pl.ComponentBody.Position;pl.ComponentBody.Rotation=Quaternion.CreateFromAxisAngle(Vector3.UnitY,MathF.Atan2(-v.X,-v.Z));
                    pl.ComponentLocomotion.LookAngles=new Vector2(0,MathF.Atan2(v.Y-1.6f,new Vector2(v.X,v.Z).Length()));aimed|=rd.AimedAtBy()!=null;}))yield return x;
                var p0=rd.Body.Position;rd.Health.Health=.25f;float d0=Vector3.Distance(p0,tw.Body.Position);foreach(var x in During(4,()=>{}))yield return x;
                bh["evade"]=$"aimed seen {aimed}, strafes {rd.Strafes-s0}; hurt: retreats {rd.Retreats}, {d0:0} m -> {Vector3.Distance(rd.Body.Position,tw.Body.Position):0} m from the tower";
                if(!aimed||rd.Strafes==s0||rd.Retreats==0||Vector3.Distance(rd.Body.Position,tw.Body.Position)<d0+3)bad.Add("evade: "+bh["evade"]);
                Kill();Remove([tw]);while(Wait(.5))yield return true;
            }
            // Survival: RD05 at the defences goes for the player near the tower; a drone goes for a player alone
            {
                gameInfo.WorldSettings.GameMode=GameMode.Survival;pl.ComponentHealth.AttackResilience=1e6f;
                var tw=Place(D("battle_turret_1"),Home+new Vector3(0,0,4),0);pl.ComponentBody.Position=Ground(Home+new Vector3(6,0,8));
                var rd=Spawn("MDRobot05",Home+new Vector3(0,0,-14),0);rd.Health.AttackResilience=1e5f;foreach(var x in During(3,()=>{pl.ComponentBody.Position=Ground(Home+new Vector3(6,0,8));pl.ComponentHealth.Health=1;}))yield return x;
                string rdT=rd.Target==pl.ComponentBody?"the player":rd.Target==null?"-":ComponentMDRobot.Rank(rd.Target)==1?"the tower":"other";
                Kill();Remove([tw]);while(Wait(.5))yield return true;
                var lone=Home+new Vector3(-40,0,30);pl.ComponentBody.Position=Ground(lone);var dr=Spawn("MDRobot09",lone+new Vector3(0,0,-22),0);
                foreach(var x in During(4,()=>{pl.ComponentBody.Position=Ground(lone);pl.ComponentHealth.Health=1;}))yield return x;
                string drT=dr.Target==pl.ComponentBody?"the player":dr.Target==null?"-":"other";
                gameInfo.WorldSettings.GameMode=mode0;pl.ComponentHealth.AttackResilience=res0;
                bh["players"]=$"RD05 at the defences targets {rdT}; RD09 by a lone player targets {drT}";
                if(rdT!="the player"||drT!="the player")bad.Add("players: "+bh["players"]);
                Kill();while(Wait(.5))yield return true;pl.ComponentBody.Position=Home+new Vector3(0,0,20);
            }
            // RD07: a ring where its shell lands (screenshot)
            {
                var tw=Place(D("battle_turret_1"),Home+new Vector3(0,0,4),0);var rd=Spawn("MDRobot07",Home+new Vector3(0,0,-36),0);rd.Health.AttackResilience=1e5f;int n0=0;
                foreach(var x in During(14,()=>{if(rd.ShotsFired>n0){n0=rd.ShotsFired;}}))yield return x;
                Cam(Home+new Vector3(-14,12,14),Home+new Vector3(0,0,4));Shot("c15_rd07");yield return true;
                bh["rd07"]=$"shots {rd.ShotsFired}";Kill();Remove([tw]);while(Wait(.5))yield return true;
            }
        }
        if(Part("growth")) {
            var tod=proj.FindSubsystem<SubsystemTimeOfDay>(true);double off0=tod.TimeOfDayOffset;int sd0=MDSettings.StartDay,g0=MDSettings.Growth;
            MDSettings.StartDay=1;MDSettings.Growth=10;int day0=MDWildSpawns.Day(proj);tod.TimeOfDayOffset=off0+(22-day0);
            int stage=MDGrowth.Stage(proj);var rd=Spawn("MDRobot05",Home+new Vector3(0,0,-10));while(Wait(.5))yield return true;
            r["growth"]=$"day {MDWildSpawns.Day(proj)}, stage {stage}, growth {rd.Growth:0.00}, resilience {rd.Health.AttackResilience:0}";
            tod.TimeOfDayOffset=off0;MDSettings.StartDay=sd0;MDSettings.Growth=g0;
            if(stage!=3||MathF.Abs(rd.Health.AttackResilience-1300)>1)bad.Add("growth: "+r["growth"]);
            Kill();while(Wait(.5))yield return true;
        }
        if(Part("rewards")) {
            var rw=new Dictionary<string,object>();r["rewards"]=rw;
            void PlayerHit(ComponentMDRobot t,float power)=>ComponentMiner.AttackBody(new MeleeAttackment(t.Entity,pl.Entity,MDCombat.Center(t.Body),Vector3.UnitZ,power));
            // a kill by the player: extra drop
            {
                ClearPickables();var rd=Spawn("MDRobot05",Home+new Vector3(0,0,-10));while(Wait(.5))yield return true;int e0=MDRewards.Extra;
                for(int i=0;i<4&&rd.Health.Health>0;i++){PlayerHit(rd,400);while(Wait(.2))yield return true;}
                while(Wait(1))yield return true;rw["played"]=$"share {rd.PlayerDamageShare:0.00}/{rd.DamageShare:0.00}, extra {MDRewards.Extra-e0}, {(MDRewards.Log.Count>0?MDRewards.Log[^1]:"-")}";
                if(MDRewards.Extra-e0<3)bad.Add("played kill: "+rw["played"]);
                Kill();ClearPickables();while(Wait(.5))yield return true;
            }
            // a boss: broken layers drop scrap, the first kill of its kind more
            {
                MDRewards.Defeated.Remove("RD13");int e0=MDRewards.Extra;var boss=MDBoss.Spawn(proj,Home+new Vector3(0,0,-14),null,"MDBoss13");while(Wait(1))yield return true;
                boss.Boss.SetArmour(4000);int layers0=boss.Boss.LayersLeft;
                for(int i=0;i<80&&boss.Health.Health>0;i++){PlayerHit(boss,600);while(Wait(.1))yield return true;}
                while(Wait(2))yield return true;
                rw["boss"]=$"layers {layers0} -> {boss.Boss.LayersLeft}, dead {boss.Health.Health<=0}, defeated {MDRewards.Defeated.Contains("RD13")}, extra {MDRewards.Extra-e0}; {string.Join(" | ",MDRewards.Log.TakeLast(4))}";
                if(boss.Health.Health>0||!MDRewards.Defeated.Contains("RD13")||!MDRewards.Log.Any(x=>x=="RD13: layer broken")||MDRewards.Extra-e0<30)bad.Add("boss: "+rw["boss"]);
                Kill();ClearPickables();while(Wait(.5))yield return true;
            }
            // a raid on the towers, all destroyed: the payout
            {
                var tws=new[]{Place(D("battle_turret_1"),Home+new Vector3(0,0,4),0),Place(D("battle_turret_1"),Home+new Vector3(7,0,4),0)};pl.ComponentBody.Position=Home+new Vector3(0,0,10);
                while(Wait(1))yield return true;int sent=MDRaids.Raid(proj,MDWildSpawns.Day(proj),new System.Random(5));while(Wait(1))yield return true;
                var raiders=proj.Entities.Select(e=>e.FindComponent<ComponentMDRobot>()).Where(x=>x!=null&&x.Raider).ToList();
                foreach(var x in raiders)x.Health.Injure(2,null,true,"lab");
                while(Wait(3))yield return true;
                rw["raid"]=$"sent {sent}, raiders {raiders.Count}, {string.Join(" | ",MDRewards.Log.TakeLast(1))}";
                if(sent==0||!MDRewards.Log.Any(x=>x.StartsWith("raid beaten")))bad.Add("raid: "+rw["raid"]);
                Kill();Remove(tws);ClearPickables();while(Wait(.5))yield return true;
            }
        }
        if(Part("texts")) {
            var tx=new Dictionary<string,object>();r["texts"]=tx;
            string help=MDEncyclopedia.Text("Elements"),rp=MDEncyclopedia.RobotNumbers(MDRobots.Kind("RP09"),800),rd06=MDEncyclopedia.RobotNumbers(MDRobots.Kind("RD06"),2400),
                flame=MDEncyclopedia.Tower(D("flame_1")),item=BlocksManager.Blocks[BlocksManager.GetBlockIndex<MDDeploymentBlock>()].GetDescription(MDDeploymentBlock.Value(D("tesla_1"),1));
            tx["help"]=help.Length>400?help[..400]:help;tx["rp09"]=rp;tx["rd06"]=rd06;tx["flame"]=flame;tx["item"]=item;
            if(!help.Contains("热冲击")||!help.Contains("电解")||!rp.Contains("修复")||!rd06.Contains("受到伤害：电 ×1.25")||!rd06.Contains("射程比一、二阶铳械塔远")||!flame.Contains("伤害类型：热")||!item.Contains("伤害类型：电"))bad.Add("texts");
            // the settings survive a save and load
            bool re0=MDSettings.Reactions;int g0=MDSettings.Growth;MDSettings.Reactions=false;MDSettings.Growth=15;var x=new System.Xml.Linq.XElement("Mod");MDSettings.Save(x);
            MDSettings.Reactions=true;MDSettings.Growth=10;MDSettings.Load(x);tx["settings"]=$"reactions {MDSettings.Reactions}, growth {MDSettings.Growth}";
            if(MDSettings.Reactions||MDSettings.Growth!=15)bad.Add("settings: "+tx["settings"]);
            MDSettings.Reactions=re0;MDSettings.Growth=g0;
        }
        // damage numbers (user, 2026-10-30: no screen full of numbers with many enemies, mind the phones): hits on one target within
        // MergeTime make one number; with many targets at most MaxShown at once; off in the settings: none. And towers' knockback on a
        // creature that is not ours (a wolf): none from a gun tower, still from the sniper tower (attack.stagger).
        if(Part("numbers")) {
            var nm=new Dictionary<string,object>();r["numbers"]=nm;MDSettings.DamageNumbers=true;
            var c=Cluster(Home+new Vector3(0,0,-6));while(Wait(.4))yield return true;var at=c[0].Body.Position;Cam(at+new Vector3(-5,3.5f,6),at+Vector3.UnitY*2.5f);
            int s0=MDTowerWorld.NumbersShown,m0=MDTowerWorld.NumbersMerged;
            for(int i=0;i<10;i++)Deal("kinetic",c[0],50);
            foreach(var x in During(.6,()=>{}))yield return x;Shot("c15_numbers");yield return true;
            int one=MDTowerWorld.NumbersShown-s0,merged=MDTowerWorld.NumbersMerged-m0;Kill();while(Wait(.4))yield return true;
            var many=new List<ComponentMDRobot>();for(int i=0;i<30;i++){var m=Spawn("MDRobot05",Home+new Vector3(i%6*3-8,0,-10-i/6*3));m.Health.AttackResilience=1e5f;many.Add(m);}
            while(Wait(.6))yield return true;Cam(Home+new Vector3(0,9,6),Home+new Vector3(0,1,-16));int s1=MDTowerWorld.NumbersShown;
            for(int i=0;i<many.Count;i++)Deal("kinetic",many[i],30+i*10);
            foreach(var x in During(.5,()=>{}))yield return x;int alive=w.NumbersAlive;Shot("c15_numbers_many");yield return true;
            int cap=MDTowerWorld.MaxShown[Math.Clamp(MDSettings.FxLevel,0,2)];
            Kill();while(Wait(.4))yield return true;
            MDSettings.DamageNumbers=false;var q=Cluster(Home+new Vector3(0,0,-6));while(Wait(.4))yield return true;int s2=MDTowerWorld.NumbersShown;Deal("kinetic",q[0],100);
            foreach(var x in During(.6,()=>{}))yield return x;int off=MDTowerWorld.NumbersShown-s2;MDSettings.DamageNumbers=true;Kill();while(Wait(.4))yield return true;
            var wolf=Dummy(Home+new Vector3(4,0,-6));wolf.C.ComponentHealth.AttackResilience=1e4f;while(Wait(.6))yield return true;
            // the engine applies a hit's impulse on the body's next update: the fastest sideways speed over 0.3 s after the hit
            float gun=0,sniper=0;
            foreach(var el in new[]{"kinetic","sniper"}) {
                wolf.Body.Velocity=Vector3.Zero;while(Wait(.3))yield return true;MDTowerBrain.Hit(src[el],wolf.Body,400,MDCombat.Center(wolf.Body),Vector3.UnitZ,true);float top=0;
                foreach(var x in During(.3,()=>{top=Math.Max(top,new Vector2(wolf.Body.Velocity.X,wolf.Body.Velocity.Z).Length());}))yield return x;
                if(el=="kinetic")gun=top;else sniper=top;
            }
            Remove([wolf]);
            nm["numbers"]=$"10 hits on one: {one} number ({merged} merged); 30 targets: {alive} shown at once (cap {cap}), {MDTowerWorld.NumbersShown-s1} made; setting off: {off}";
            nm["knockback"]=$"wolf pushed by the gun tower {gun:0.00} m/s, by the sniper tower {sniper:0.00} m/s";
            if(one!=1||merged<9||alive>cap||alive<cap-1||off!=0)bad.Add("numbers: "+nm["numbers"]);
            if(gun>.05f||sniper<=.05f)bad.Add("knockback: "+nm["knockback"]);
        }
        // the mechs' ammo elements (user, 2026-10-30: "机甲也要做多种元素的炮弹……高级的可以做多一点支持的元素"): Mercury 3, Nemesis 4,
        // Regulus 5; the ammo key cycles through them; what each weapon then deals
        if(Part("mechammo")) {
            var ma=new Dictionary<string,object>();r["mechammo"]=ma;var counts=new List<string>();
            foreach(var t in MDMechType.All) {
                var e=DatabaseManager.CreateEntity(proj,t.Template,true);e.FindComponent<ComponentBody>(true).Position=Ground(Home+new Vector3(-12,0,-12));e.FindComponent<ComponentSpawn>(true).SpawnDuration=0;proj.AddEntity(e);
                var u=e.FindComponent<ComponentMDUnit>(true);var m=u.Mech;var cycle=new List<string>();
                for(int i=0;i<=t.Ammo.Length;i++){cycle.Add(MDMechState.AmmoName(m.Ammo));m.NextAmmo();}
                m.Ammo=MDElement.Heat;m.Weapon=MDMechWeapon.Rifle;var rifleHeat=MDElements.Of(u);m.Weapon=MDMechWeapon.Laser;var laser=MDElements.Of(u);
                m.Ammo=MDElement.Kinetic;m.Weapon=MDMechWeapon.Cannon;var cannon=MDElements.Of(u);m.Weapon=MDMechWeapon.Rifle;var rifle=MDElements.Of(u);
                counts.Add($"{t.Name}: {t.Ammo.Length} [{string.Join(" > ",cycle)}]; heat rifle {rifleHeat}, laser {laser}, standard cannon {cannon}, standard rifle {rifle}");
                int want=t==MDMechType.Mercury?3:t==MDMechType.Nemesis?4:5;
                if(t.Ammo.Length!=want||cycle[0]!=cycle[^1]||rifleHeat!=MDElement.Heat||laser!=MDElement.Electric||cannon!=MDElement.Explosive||rifle!=MDElement.Kinetic)bad.Add("mech ammo: "+counts[^1]);
                proj.RemoveEntity(e,true);while(Wait(.3))yield return true;
            }
            ma["types"]=counts;string mechs=MDEncyclopedia.Text("Mechs");ma["key"]=$"{MDMechKeys.KeyName(MDMechKeys.Get(MDMechAction.Ammo))}, {MDMechKeys.ActionName(MDMechAction.Ammo)}";
            if(MDMechKeys.Get(MDMechAction.Ammo)!=Engine.Input.Key.X||!mechs.Contains("弹药元素：标准弹、热元素弹、冷元素弹"))bad.Add($"mech ammo key / help: {ma["key"]}");
        }
        // medic charge (user, 2026-10-30): the store runs out and the medic stops; a charge cell fills it; taken down it keeps its charge;
        // the bar over it; the Neorxna info panel (when loaded) shows the tower's name in Chinese, the charge and its bar, patched on
        // its own (not by this lab calling Install)
        if(Part("medic")) {
            var md=new Dictionary<string,object>();r["medic"]=md;float cap0=MDSettings.MedicCapacity;
            var panelType=AppDomain.CurrentDomain.GetAssemblies().Select(a=>a.GetType("Neorxna.NUI.NUInfoPanelWidget",false)).FirstOrDefault(t=>t!=null);
            md["neorxna"]=panelType==null?"not loaded":$"{panelType.Assembly.GetName().Name} {panelType.Assembly.GetName().Version}, patched on its own {MDNeorxnaCompatibility.Installed}";
            if(panelType!=null&&!MDNeorxnaCompatibility.Installed)bad.Add("neorxna not patched: "+md["neorxna"]);
            MDSettings.MedicCapacity=600;var mdef=D("battle_medic_1");
            var medic=Place(mdef,Home+new Vector3(0,0,-4),0);var tw=Place(D("battle_turret_1"),Home+new Vector3(5,0,-4),0);while(Wait(.6))yield return true;
            tw.Damage.Hitpoints=.1f;var hp=new List<string>();double t0=Time.RealTime;int n=0;
            foreach(var x in During(20,()=>{if(Time.RealTime-t0>=n*2){n++;hp.Add($"{tw.Damage.Hitpoints:0.00}/{MDMedic.Left(medic):0}");}}))yield return x;
            float after=tw.Damage.Hitpoints,left=MDMedic.Left(medic);foreach(var x in During(6,()=>{}))yield return x;float later=tw.Damage.Hitpoints;
            md["drain"]=$"tower hp / charge every 2 s: {string.Join(" ",hp)}; empty {left:0}, then {after:0.00} -> {later:0.00} in 6 s";
            if(left>0||MathF.Abs(after-(.1f+600f/D("battle_turret_1").hp))>.03f||later>after+.005f)bad.Add("drain: "+md["drain"]);
            var c=medic.Body.Position;Cam(c+new Vector3(-5,4,6),c+Vector3.UnitY*2.5f);for(int i=0;i<4;i++)yield return true;int bars=MDTowerWorld.BarsDrawn;Shot("c15_medic_empty");yield return true;
            string msg=MDMedic.Charge(null,medic);for(int i=0;i<4;i++)yield return true;Shot("c15_medic_full");yield return true;
            medic.HealCharge=MDMedic.Max*.4f;for(int i=0;i<4;i++)yield return true;Shot("c15_medic_part");yield return true;
            md["charge"]=$"\"{msg}\" -> {MDMedic.Text(medic)}; bars drawn {bars}";
            if(!msg.StartsWith("医疗塔已充满")||bars<1)bad.Add("charge: "+md["charge"]);
            medic.HealCharge=MDMedic.Max*.5f;int used=MDMedic.UsedEighths(medic);int value=MDDeploymentBlock.Value(mdef,1,used);
            string itemName=BlocksManager.Blocks[BlocksManager.GetBlockIndex<MDDeploymentBlock>()].GetDisplayName(null,value);
            md["item"]=$"used {used}/7 -> item {itemName}, back to {MDMedic.FromEighths(MDDeploymentBlock.MedicUsed(value)):0}";
            if(MDDeploymentBlock.MedicUsed(value)!=used||used!=4||!itemName.Contains("储能约"))bad.Add("item: "+md["item"]);
            if(panelType!=null) {
                var panel=Activator.CreateInstance(panelType);var set=panelType.GetMethod("SetInfo_Entity");
                set.Invoke(panel,[tw.Entity]);string twName=((LabelWidget)panelType.GetField("m_name").GetValue(panel)).Text;
                set.Invoke(panel,[medic.Entity]);string mName=((LabelWidget)panelType.GetField("m_name").GetValue(panel)).Text;
                var all=(panel as ContainerWidget)?.AllChildren.ToList()??[];var texts=all.OfType<LabelWidget>().Select(l=>l.Text).Where(x=>!string.IsNullOrWhiteSpace(x)).ToList();
                bool bar=all.Any(wd=>wd.Name=="MDMedicBar");
                md["panel"]=$"gun tower: {twName}; medic: {mName}; labels {string.Join(" | ",texts)}; bar {bar}";
                if(twName!="铳械塔"||mName!=mdef.name||!texts.Any(x=>x.StartsWith("储能"))||!bar)bad.Add("panel: "+md["panel"]);
            }
            MDSettings.MedicCapacity=cap0;Remove([medic,tw]);while(Wait(.5))yield return true;
        }
        // views for the user (decisions 14, 15, 17 of 10-30): drones shot down falling and on the ground, RD19's nose close up, EN02's
        // open ducts close up
        if(Part("views")) {
            var vw=new Dictionary<string,object>();r["views"]=vw;
            foreach(var t in new[]{"MDRobot09","MDRobotEN02"}) {
                var at=Home+new Vector3(0,0,-16);var dr=Spawn(t,at,0);while(Wait(3))yield return true;float up0=dr.Body.Position.Y-Ground(dr.Body.Position).Y;
                dr.Health.Injure(2,null,true,"lab");var c=dr.Body.Position;Cam(c+new Vector3(-9,2,9),c-Vector3.UnitY*2);
                foreach(var x in During(.7,()=>{}))yield return x;Shot($"c15_fall_{t}");yield return true;
                float minUp=99;foreach(var x in During(3,()=>{if(dr.Entity.IsAddedToProject)minUp=Math.Min(minUp,dr.Body.Position.Y-Ground(dr.Body.Position).Y);}))yield return x;
                if(dr.Entity.IsAddedToProject){var g=dr.Body.Position;Cam(g+new Vector3(-6,3,6),g);for(int i=0;i<3;i++)yield return true;Shot($"c15_down_{t}");yield return true;}
                vw[t]=$"flew {up0:0.0} m up, after its death down to {minUp:0.0} m, clip {dr.Anim.Base.name}";
                if(minUp>1.5f)bad.Add($"views {t} did not come down: {vw[t]}");
                Kill();while(Wait(.5))yield return true;
            }
            {
                var boss=MDBoss.Spawn(proj,Home+new Vector3(0,0,-18),null,"MDBoss19");boss.ForceClip="idle";boss.ForceTime=0;while(Wait(1.5))yield return true;
                var nose=boss.Point("main",out var fwd);fwd.Y=0;fwd=fwd.LengthSquared()>.01f?Vector3.Normalize(fwd):boss.Body.Matrix.Forward;
                Cam(nose+fwd*5+Vector3.UnitY*1.2f+boss.Body.Matrix.Right*2,nose);for(int i=0;i<4;i++)yield return true;Shot("c15_rd19_nose");yield return true;
                Cam(nose+fwd*3-Vector3.UnitY*.5f,nose);for(int i=0;i<4;i++)yield return true;Shot("c15_rd19_nose_low");yield return true;
                Kill();while(Wait(.5))yield return true;
            }
            {
                var en=Spawn("MDRobotEN02",Home+new Vector3(0,0,-14),0);en.ForceClip="hover";en.ForceTime=0;while(Wait(1))yield return true;var c=MDCombat.Center(en.Body);
                Cam(c+new Vector3(-3.5f,3.5f,4),c);for(int i=0;i<4;i++)yield return true;Shot("c15_en02_fans_above");yield return true;
                Cam(c+new Vector3(-4,-.4f,4.5f),c);for(int i=0;i<4;i++)yield return true;Shot("c15_en02_fans_side");yield return true;
                Kill();while(Wait(.5))yield return true;
            }
        }
        Remove(src.Values);
        if(bad.Count>0)throw new Exception("combat15: "+string.Join(" || ",bad));
    }
}
