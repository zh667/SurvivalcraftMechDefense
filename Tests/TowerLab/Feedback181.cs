using System.Collections;
using System.Reflection;
using Engine;
using Game;

namespace TowerLab;

// lab --mode fb181 (0.18.1, a player report via the user, 2026-11-04: "声波塔和电磁脉冲命中敌人，并没有附加元素，而且机甲防线那个栏目太靠后了，
// 放到武器栏目之后就可以"). Results under "fb181"; screenshots f181_*. Written against the 0.18.0 API only (the private status table
// through reflection), so the same build runs on the 0.18.0 package (the failures before the fix) and on 0.18.1:
// elements  every tower whose damage type leaves a status (heat, cold, electric, corrosive), alone against one RD05 walker held 10 m
//           off: the element's own status on the robot (its own timer: freezing, burning and poison count as the element too, but
//           they are not what the hit leaves), how long after the first shot, and its icon over the robot. Then the sonic tower and
//           the EMP towers against two robots 16 m off, one of them carrying heat: electric on the other one; since 0.18.2 (user,
//           2026-11-04: "电磁脉冲、声波只附加元素") electric on the hot one too, no reaction, its heat kept, and its reaction cooldown
//           free: a cold hit right after reacts at once (0.18.1: 过载 used the heat up and the 1.5 s cooldown, the cold hit did nothing).
// mech      Mercury: a grenade thrown with cold ammo that lands after the pilot switched to standard ammo and the laser (it stays
//           cold), and a melee blow with the missiles selected on a chilled robot (kinetic: 碎冰, not 扩散).
// category  the creative inventory's categories: MechDefense once and right after Weapons (BlocksManager, the inventory widget, the
//           Recipaedia), the others in the game's order; screenshots of Weapons and the next two pages, as the arrow turns them.
public partial class Lab {
    static IEnumerable<bool> Feedback181() {
        Remove(placed.Select(p=>p.unit));placed.Clear();while(Wait(1))yield return true;
        var r=new Dictionary<string,object>();results["fb181"]=r;var bad=new List<string>();results["fb181bad"]=bad;
        bool Part(string p)=>mode=="fb181"||mode=="fb181-"+p;
        var proj=GameManager.Project;var pl=player.ComponentPlayer;var w=MDTowerWorld.For(proj);var terrain=proj.FindSubsystem<SubsystemTerrain>(true);
        var keep=new HashSet<GameEntitySystem.Entity>();
        void Tidy(){foreach(var e in proj.Entities.Where(e=>e.FindComponent<ComponentCreature>()!=null&&e.FindComponent<ComponentPlayer>()==null&&!keep.Contains(e)&&!dums.Any(d=>d.Entity==e)).ToList())proj.RemoveEntity(e,true);}
        void ClearAll(){foreach(var e in proj.Entities.Where(e=>e.FindComponent<ComponentCreature>()!=null&&e.FindComponent<ComponentPlayer>()==null).ToList())proj.RemoveEntity(e,true);keep.Clear();dums.Clear();}
        IEnumerable<bool> Seconds(double s,Action each=null){for(double s0=Time.RealTime;Time.RealTime-s0<s;){each?.Invoke();yield return true;}}
        Vector3 Dir(float a)=>new(-MathF.Sin(a),0,-MathF.Cos(a));   // a body's forward at that yaw
        float Top(Vector3 at)=>terrain.Terrain.GetTopHeight(Terrain.ToCell(at.X),Terrain.ToCell(at.Z))+1.05f;
        ComponentMDRobot Robot(string t,Vector3 at,float yaw) {
            var e=DatabaseManager.CreateEntity(proj,t,true);var b=e.FindComponent<ComponentBody>(true);b.Position=at;b.Rotation=Quaternion.CreateFromAxisAngle(Vector3.UnitY,yaw);
            var sp=e.FindComponent<ComponentSpawn>(true);sp.SpawnDuration=0;sp.AutoDespawn=false;proj.AddEntity(e);keep.Add(e);var rb=e.FindComponent<ComponentMDRobot>(true);rb.Health.AttackResilience*=100;return rb;
        }
        void Hold(ComponentMDRobot rb){var l=rb.Creature.ComponentLocomotion;l.WalkSpeed=l.JumpSpeed=l.FlySpeed=0;}
        string Line(Dictionary<string,object> row)=>string.Join(" ",row.Select(kv=>$"{kv.Key}={kv.Value}"));
        // the element's own timer in the status table (MDTowerWorld.Status heatUntil / coldUntil / elecUntil / acidUntil)
        var stF=typeof(MDTowerWorld).GetField("status",BindingFlags.NonPublic|BindingFlags.Instance);var timeF=typeof(MDTowerWorld).GetField("time",BindingFlags.NonPublic|BindingFlags.Instance);
        var stType=typeof(MDTowerWorld).GetNestedType("Status",BindingFlags.NonPublic);string[] auraField=["","","","heatUntil","coldUntil","elecUntil","acidUntil"];
        bool Own(ComponentBody b,MDElement e){var t=(IDictionary)stF.GetValue(w);if(!t.Contains(b))return false;return (float)stType.GetField(auraField[(int)e]).GetValue(t[b])>(float)timeF.GetValue(w);}
        MDIcon IconOf(MDElement e)=>e switch{MDElement.Heat=>MDIcon.Heat,MDElement.Cold=>MDIcon.Cold,MDElement.Electric=>MDIcon.Electric,_=>MDIcon.Corrosive};
        string Reactions(int from)=>string.Join(", ",MDElements.Log.TakeLast(Math.Min(MDElements.Count-from,MDElements.Log.Count)));
        var attackers=MDTowerLibrary.All.OrderBy(d=>d.data).Where(d=>d.attack.type is not ("heal" or "radar" or "core")).ToList();
        void Field(){Clear((int)Home.X-20,(int)Home.X+30,(int)Home.Z-36,(int)Home.Z+6);}

        // ---------------------------------------------------------------- elements
        if(Part("elements")) {
            var rows=new List<string>();r["elements"]=rows;const float yaw=0;var fwd=Dir(yaw);var side=new Vector3(-fwd.Z,0,fwd.X);
            var auraTowers=attackers.Where(d=>MDElements.Aura(MDElements.Of(d))).ToList();
            r["census"]=attackers.Select(d=>$"{d.id} {d.attack.type} {MDElements.Name(MDElements.Of(d))} power {d.attack.power:0.#}").ToList();
            foreach(var d in auraTowers) {
                var el=MDElements.Of(d);ClearAll();Field();while(Wait(.3))yield return true;
                pl.ComponentBody.Position=Home+side*-14+fwd*4;pl.ComponentBody.Velocity=Vector3.Zero;
                var u=Place(d,Home,yaw);u.Cooldown=1e6f;while(Wait(.4))yield return true;
                var at=Home+fwd*10;at.Y=Top(at);var rb=Robot("MDRobot05",at,yaw+MathF.PI);Hold(rb);
                while(Wait(.6))yield return true;Hold(rb);
                int s0=u.ShotCount,n0=MDElements.Count;u.Cooldown=0;double t0=Time.RealTime,fired=-1,own=-1,any=-1;bool icon=false;
                var eye=Home+fwd*6+side*9+Vector3.UnitY*5;
                foreach(var x in Seconds(9,()=>{
                    Tidy();rb.Health.Health=1;u.Damage.Hitpoints=1;if(fired<0&&u.ShotCount>s0)fired=Time.RealTime;
                    if(any<0&&w.HasStatus(rb.Body,el))any=Time.RealTime-t0;
                    if(own<0&&Own(rb.Body,el)){own=Time.RealTime-(fired>=0?fired:t0);var ic=w.Icons(rb.Body);icon=ic.Contains(IconOf(el))||el==MDElement.Cold&&ic.Contains(MDIcon.Frozen);}
                    Cam(eye,at);
                })){
                    if(own>=0){if(d.id is "battle_shockwave_1" or "emp_2" or "frost_1" or "battle_frost_1"){Shot($"f181_el_{d.id}");yield return true;}break;}
                    yield return x;
                }
                var row=new Dictionary<string,object>{["tower"]=d.id,["type"]=d.attack.type,["element"]=MDElements.Name(el),["power"]=d.attack.power,["shots"]=u.ShotCount-s0,
                    ["firstShot"]=fired<0?-1:Math.Round(fired-t0,2),["ownStatusAfterShot"]=Math.Round(own,2),["anyStatus"]=Math.Round(any,2),["icon"]=icon,["reactions"]=MDElements.Count-n0};
                rows.Add(Line(row));
                if(own<0||!icon)bad.Add("element: "+Line(row));
                if(rb.Entity.IsAddedToProject)proj.RemoveEntity(rb.Entity,true);Remove([u]);ClearAll();while(Wait(.3))yield return true;
            }
            // the control towers' hits react like any other: one robot carries heat (+ electric = 过载), the other gets electric
            var react=new List<string>();r["react"]=react;
            foreach(var id in new[]{"battle_shockwave_1","emp_1","emp_2","emp_3"}) {
                var d=MDTowerLibrary.Get(id);ClearAll();Field();while(Wait(.3))yield return true;
                pl.ComponentBody.Position=Home+side*-14+fwd*4;pl.ComponentBody.Velocity=Vector3.Zero;
                var u=Place(d,Home,yaw);u.Cooldown=1e6f;while(Wait(.4))yield return true;
                var two=new List<ComponentMDRobot>();
                foreach(var off in new[]{-1.5f,1.5f}){var at=Home+fwd*16+side*off;at.Y=Top(at);var rb=Robot("MDRobot05",at,yaw+MathF.PI);Hold(rb);two.Add(rb);}
                while(Wait(.6))yield return true;foreach(var rb in two)Hold(rb);
                var hot=two[0].Body;var clean=two[1].Body;w.Elemental(u,hot,MDElement.Heat,0);   // 6 s of heat on the first one
                int s0=u.ShotCount,n0=MDElements.Count;u.Cooldown=0;double t0=Time.RealTime,fired=-1,elec=-1,reacted=-1;bool icon=false,shot=false;float cdMax=-1e9f;
                var eye=Home+fwd*10+side*12+Vector3.UnitY*7;var focus=Home+fwd*16;
                foreach(var x in Seconds(4,()=>{
                    Tidy();foreach(var rb in two)rb.Health.Health=1;u.Damage.Hitpoints=1;if(fired<0&&u.ShotCount>s0)fired=Time.RealTime;
                    if(elec<0&&Own(clean,MDElement.Electric)){elec=Time.RealTime-t0;icon=w.Icons(clean).Contains(MDIcon.Electric);}
                    if(reacted<0&&MDElements.Count>n0)reacted=Time.RealTime-t0;
                    {var tb=(IDictionary)stF.GetValue(w);if(tb.Contains(hot))cdMax=Math.Max(cdMax,(float)stType.GetField("reactUntil").GetValue(tb[hot])-(float)timeF.GetValue(w));}   // a reaction cooldown taken at any time
                    Cam(eye,focus);
                })){
                    if(!shot&&elec>=0&&Time.RealTime-t0-elec>.15){Shot($"f181_react_{id}");shot=true;}
                    yield return x;
                }
                // the hot one: electric on it too, its heat kept, no reaction, its cooldown free (reactUntil, reactAt through reflection)
                bool hotElec=Own(hot,MDElement.Electric),heatKept=Own(hot,MDElement.Heat);var hotIcons=w.Icons(hot);
                float now=(float)timeF.GetValue(w);var hs=((IDictionary)stF.GetValue(w))[hot];float reactUntil=(float)stType.GetField("reactUntil").GetValue(hs);
                var reactAt=(float[])stType.GetField("reactAt").GetValue(hs);bool cooldownFree=cdMax<=0&&reactUntil<=now&&reactAt.All(x=>x<=now);
                string during=Reactions(n0);int nDuring=MDElements.Count-n0;
                // a cold hit right after (a tower of cold, here through Elemental itself): a reaction at once if nothing took the cooldown
                int n1=MDElements.Count;w.Elemental(u,hot,MDElement.Cold,100);string after=Reactions(n1);
                var row=new Dictionary<string,object>{["tower"]=id,["shots"]=u.ShotCount-s0,["firstShot"]=fired<0?-1:Math.Round(fired-t0,2),["electricOnClean"]=Math.Round(elec,2),["icon"]=icon,
                    ["reactionsDuring"]=nDuring,["reactionNames"]=during,["hotElectric"]=hotElec,["hotHeatKept"]=heatKept,["hotIcons"]=string.Join("+",hotIcons),
                    ["hotCooldownFree"]=cooldownFree,["cooldownLeftMax"]=cdMax<-1e8f?"none":Math.Round(cdMax,2),["coldHitAfter"]=after.Length>0?after:"none"};
                react.Add(Line(row));
                if(elec<0||!icon||nDuring!=0||!hotElec||!heatKept||!hotIcons.Contains(MDIcon.Heat)||!hotIcons.Contains(MDIcon.Electric)||!cooldownFree||after.Length==0)bad.Add("react: "+Line(row));
                foreach(var rb in two)if(rb.Entity.IsAddedToProject)proj.RemoveEntity(rb.Entity,true);Remove([u]);ClearAll();while(Wait(.3))yield return true;
            }
        }

        // ---------------------------------------------------------------- mech
        if(Part("mech")) {
            var mr=new Dictionary<string,object>();r["mech"]=mr;const float yaw=0;var fwd=Dir(yaw);var side=new Vector3(-fwd.Z,0,fwd.X);
            ClearAll();Field();while(Wait(.3))yield return true;
            pl.ComponentBody.Position=Home+side*-16;pl.ComponentBody.Velocity=Vector3.Zero;
            var me=DatabaseManager.CreateEntity(proj,MDMechType.Mercury.Template,true);var mp=Home+side*-6;mp.Y=Top(mp);me.FindComponent<ComponentBody>(true).Position=mp;
            me.FindComponent<ComponentSpawn>(true).SpawnDuration=0;proj.AddEntity(me);keep.Add(me);while(Wait(1))yield return true;
            var u=me.FindComponent<ComponentMDUnit>(true);var m=u.Mech;
            // melee: a chilled robot 3 m in front, the missiles selected, standard ammo
            var front=u.Body.Position+Dir(u.Yaw)*3;front.Y=Top(front);var rb=Robot("MDRobot05",front,u.Yaw+MathF.PI);Hold(rb);while(Wait(.6))yield return true;Hold(rb);
            m.Weapon=MDMechWeapon.Missile;m.Ammo=MDElement.Kinetic;w.Elemental(u,rb.Body,MDElement.Cold,0);
            int n0=MDElements.Count;typeof(MDMechBrain).GetMethod("MeleeStrike",BindingFlags.NonPublic|BindingFlags.Static).Invoke(null,[u]);
            foreach(var x in Seconds(.3,()=>{rb.Health.Health=1;}))yield return x;
            string melee=Reactions(n0);mr["melee (missiles selected, chilled robot)"]=melee;
            if(!melee.Contains(MDElements.Reactions[(int)MDReaction.Shatter].Name)||melee.Contains(MDElements.Reactions[(int)MDReaction.Spread].Name))bad.Add("mech melee: "+melee);
            proj.RemoveEntity(rb.Entity,true);while(Wait(.5))yield return true;
            // grenade: thrown with cold ammo, then standard ammo and the laser before it lands
            var at=Home+fwd*14;at.Y=Top(at);rb=Robot("MDRobot05",at,yaw+MathF.PI);Hold(rb);while(Wait(.6))yield return true;Hold(rb);
            m.Weapon=MDMechWeapon.Grenade;m.Ammo=MDElement.Cold;var g=MDMechBrain.GrenadeWeapon;
            w.LaunchShell(u,rb.Body.Position+Vector3.UnitY*6,new Vector3(0,-30,0),0,g.attack.power,g.attack.splash,false,g);
            m.Ammo=MDElement.Kinetic;m.Weapon=MDMechWeapon.Laser;bool cold=false,elec=false;
            foreach(var x in Seconds(1.2,()=>{rb.Health.Health=1;cold|=Own(rb.Body,MDElement.Cold);elec|=Own(rb.Body,MDElement.Electric);}))yield return x;
            mr["grenade (cold ammo, switched to the laser in flight)"]=$"cold {cold}, electric {elec}";
            if(!cold||elec)bad.Add($"mech grenade: cold {cold}, electric {elec}");
            m.Weapon=MDMechWeapon.Rifle;proj.RemoveEntity(rb.Entity,true);proj.RemoveEntity(me,true);ClearAll();while(Wait(.5))yield return true;
        }

        // ---------------------------------------------------------------- category
        if(Part("category")) {
            var cr=new Dictionary<string,object>();r["category"]=cr;
            string[] vanilla=["Terrain","Minerals","Plants","Construction","Items","Tools","Weapons","Clothes","Electrics","Food","Spawner Eggs","Painted","Dyed","Fireworks"];
            void Check(string where,List<string> list) {
                int wi=list.IndexOf("Weapons"),mi=list.IndexOf(MDCategory.Name),n=list.Count(c=>c==MDCategory.Name);
                cr[where]=$"Weapons {wi}, MechDefense {mi}, x{n}: {string.Join(" | ",list)}";
                var rest=list.Where(c=>c!=MDCategory.Name).ToList();
                if(n!=1||mi!=wi+1||!rest.Take(vanilla.Length).SequenceEqual(vanilla))bad.Add($"category {where}: {cr[where]}");
            }
            Check("BlocksManager",BlocksManager.Categories.ToList());
            var cinvW=new CreativeInventoryWidget(pl.Entity);var widget=cinvW.m_categories.Select(c=>c.Name).ToList();Check("inventory",widget);
            if(ScreensManager.FindScreen<RecipaediaScreen>("Recipaedia") is {} rs)Check("recipaedia",rs.m_categories.Where(c=>c!=null).ToList());else cr["recipaedia"]="no screen";
            // the inventory turned with the right arrow from Weapons: Weapons, 机甲防线, then the next one
            var cinv=pl.Entity.FindComponent<ComponentCreativeInventory>(true);SettingsManager.ShowGuiInScreenshots=true;
            int start=widget.IndexOf("Weapons");var pages=new List<string>();
            for(int k=0;k<3&&start>=0;k++) {
                pl.ComponentGui.ModalPanelWidget=cinvW;cinv.CategoryIndex=start+k;cinv.PageIndex=0;
                foreach(var x in Seconds(.8,()=>Cam(Home+new Vector3(-8,7,-12),Home+new Vector3(3,1,-1))))yield return x;
                Shot($"f181_category_{k}");yield return true;
                pages.Add($"{cinv.CategoryIndex}: {cinvW.m_categoryButton.Text}");
            }
            pl.ComponentGui.ModalPanelWidget=null;SettingsManager.ShowGuiInScreenshots=false;for(int i=0;i<4;i++)yield return true;
            cr["pages"]=pages;
            if(pages.Count!=3||!pages[1].EndsWith(LanguageControl.Get("BlocksManager",MDCategory.Name))||!pages[0].EndsWith(LanguageControl.Get("BlocksManager","Weapons")))bad.Add("category pages: "+string.Join(" | ",pages));
        }
        pl.ComponentBody.Position=Home;
        if(bad.Count>0)throw new Exception("fb181: "+string.Join(" || ",bad));
    }
}
