using Engine;
using Engine.Graphics;
using GameEntitySystem;

namespace Game;

// 0.15.0 damage types, element statuses and reactions (user, 2026-10-30: "每座塔归哪种伤害你来做吧（要有每种伤害的图标，敌人受到攻击，
// 现在处于什么状态要有图标，发生反应要有特效，这样玩家才能看明白），而且现在的反应类型太少，最好是多种元素可以互相组合，而且发生反应要有cd").
// Design: docs/tasks/2026-10-30-combat-0.15.md (B).
// - Every tower has one damage type (towers.json attack.element); Mercury's rifle and blade are kinetic, its missiles, cannon and
//   grenades explosive, its laser electric. Each robot kind takes x 0.75 .. x 1.5 per type (Resist; the bestiary lists them).
// - Heat, cold, electric and corrosive hits put that element's status on the target for AuraTime s (burning counts as heat,
//   frozen as cold, poisoned as corrosive). Kinetic and explosive put none but react with one.
// - A hit of another element on a status reacts (table below). A target reacts at most once per GlobalCooldown s and the same
//   reaction at most once per SameCooldown s. Both elements are used up (spread and shatter: see below).
// - Icons over the target (MDIcon, atlas Towers/status from tools/build_status_icons.py) show its statuses; a reaction shows a
//   ring in both colours, a flash and its name rising over the target. Particles follow the 特效数量 setting (Emit).
// - The mod setting 元素反应 turns reactions off (types, resistances and statuses stay).
public enum MDElement{None,Kinetic,Explosive,Heat,Cold,Electric,Corrosive}
public enum MDReaction{None,Thermal,Overload,Corrode,Brittle,FrostEtch,Electrolysis,Spread,Shatter,Storm,Meltdown,Plasma,Crystal}   // the last four: three elements
public enum MDIcon{Kinetic,Explosive,Heat,Cold,Electric,Corrosive,Slowed,Frozen,Paralysed,ArmourDown,Brittle}

public static class MDElements {
    public const float AuraTime=6,GlobalCooldown=1.5f,SameCooldown=6,ResidueTime=4,TriadCooldown=10;
    public const float BossShare=.7f;   // 0.16.0: a reaction adds this share of its extra damage on a boss (and fills its poise, MDBossState)
    public static readonly string[] Names=["","动能","爆炸","热","冷","电","腐蚀"];
    public static readonly Color[] Colors=[Color.White,new(226,226,232),new(255,150,40),new(255,84,48),new(110,215,255),new(186,120,255),new(120,228,80)];
    public static readonly string[] Statuses=["","","","灼烧","冰寒","带电","腐蚀"];   // the status each element leaves
    public static readonly (string Name,MDElement A,MDElement B,string Effect)[] Reactions=[
        ("",MDElement.None,MDElement.None,""),
        ("热冲击",MDElement.Heat,MDElement.Cold,"这一击伤害翻倍，再扣 3 % 最大耐久"),
        ("过载",MDElement.Heat,MDElement.Electric,"3 米爆炸，周围敌人受这一击 60 %；目标瘫痪 1.5 秒（停火，首领除外）"),
        ("腐燃",MDElement.Heat,MDElement.Corrosive,"着火 4 秒，破甲（受伤 +25 %）6 秒"),
        ("脆化",MDElement.Cold,MDElement.Electric,"6 秒内受到的动能、爆炸伤害 +35 %"),
        ("冻蚀",MDElement.Cold,MDElement.Corrosive,"减速到 40 % 共 4 秒，破甲（受伤 +15 %）6 秒"),
        ("电解",MDElement.Electric,MDElement.Corrosive,"电弧跳到 6 米内 3 个敌人，各受这一击 50 % 并沾上腐蚀"),
        ("扩散",MDElement.Explosive,MDElement.None,"爆炸命中带状态的目标：把它的状态传给 4 米内的敌人"),
        ("碎冰",MDElement.Kinetic,MDElement.Cold,"动能命中冰寒或冻结的目标：这一击 +60 %，冰消掉"),
        // 0.15.0 (user, 2026-10-30: "可以加三元素反应的，当然伤害倍率要提高"): a third element within ResidueTime s of a two-element
        // reaction on the same target (or on a target carrying two other statuses) — see Triads
        ("雷暴",MDElement.Heat,MDElement.Cold,"这一击 ×3，5 米内的敌人各受一次原伤害，目标瘫痪 2 秒（首领除外）"),
        ("熔解",MDElement.Heat,MDElement.Cold,"这一击 ×3.5，目标破甲（受伤 +50 %）8 秒"),
        ("等离子蚀",MDElement.Heat,MDElement.Electric,"这一击 ×3，电弧跳到 8 米内 5 个敌人各受 80 % 并着火，目标着火 5 秒"),
        ("晶化",MDElement.Cold,MDElement.Electric,"这一击 ×3，目标冻结 2.5 秒（停火；首领不冻结，改为这一击再 ×1.5）"),
    ];
    public static readonly Dictionary<MDReaction,MDElement[]> Triads=new(){[MDReaction.Storm]=[MDElement.Heat,MDElement.Cold,MDElement.Electric],
        [MDReaction.Meltdown]=[MDElement.Heat,MDElement.Cold,MDElement.Corrosive],[MDReaction.Plasma]=[MDElement.Heat,MDElement.Electric,MDElement.Corrosive],
        [MDReaction.Crystal]=[MDElement.Cold,MDElement.Electric,MDElement.Corrosive]};
    // the three-element reaction an element hit makes with these other elements on the target (two of them besides e)
    public static MDReaction Triad(MDElement e,ICollection<MDElement> others){if(!Aura(e))return MDReaction.None;foreach(var (r,els) in Triads)if(els.Contains(e)&&els.Count(x=>x!=e&&others.Contains(x))==2)return r;return MDReaction.None;}
    public static string Combo(MDReaction r)=>Triads.TryGetValue(r,out var els)?string.Join(" + ",els.Select(Name)):Reactions[(int)r].B==MDElement.None?Name(Reactions[(int)r].A)+" + 任一状态":Name(Reactions[(int)r].A)+" + "+Name(Reactions[(int)r].B);
    public static MDElement Parse(string s)=>s switch{"kinetic"=>MDElement.Kinetic,"explosive"=>MDElement.Explosive,"heat"=>MDElement.Heat,"cold"=>MDElement.Cold,
        "electric"=>MDElement.Electric,"corrosive"=>MDElement.Corrosive,_=>MDElement.None};
    public static MDElement Of(MDTowerDef d)=>d==null?MDElement.None:Parse(d.attack.element);
    public static MDElement Of(ComponentMDUnit u)=>u.Tower!=null?Of(u.Tower):u.Mech==null?MDElement.None:u.Mech.Weapon==MDMechWeapon.Laser?MDElement.Electric:
        u.Mech.Ammo!=MDElement.Kinetic?u.Mech.Ammo:u.Mech.Weapon is MDMechWeapon.Missile or MDMechWeapon.Cannon or MDMechWeapon.Grenade?MDElement.Explosive:MDElement.Kinetic;   // 0.15.0 ammo
    public static string Name(MDElement e)=>Names[(int)e];
    public static bool Aura(MDElement e)=>e is MDElement.Heat or MDElement.Cold or MDElement.Electric or MDElement.Corrosive;
    // damage taken per type, by robot kind (x 0.75 .. x 1.5; 1 where not listed). The devil mech has its own weak point instead.
    static readonly Dictionary<string,(MDElement e,float f)[]> resist=new(){
        ["RD05"]=[(MDElement.Explosive,1.25f)],
        ["RD06"]=[(MDElement.Electric,1.25f),(MDElement.Kinetic,.85f)],
        ["RD07"]=[(MDElement.Heat,1.25f),(MDElement.Cold,.85f)],
        ["RD14"]=[(MDElement.Explosive,1.25f),(MDElement.Corrosive,1.25f),(MDElement.Kinetic,.75f)],
        ["RD09"]=[(MDElement.Electric,1.5f),(MDElement.Cold,1.25f)],
        ["RP09"]=[(MDElement.Electric,1.5f),(MDElement.Cold,1.25f)],
        ["EN02"]=[(MDElement.Electric,1.25f),(MDElement.Kinetic,.85f)],
        ["RD13"]=[(MDElement.Electric,1.15f),(MDElement.Explosive,.85f)],
        ["RD21"]=[(MDElement.Cold,1.15f),(MDElement.Kinetic,.85f)],
        ["RD08"]=[(MDElement.Electric,1.25f),(MDElement.Explosive,.85f)],
        ["RD19"]=[(MDElement.Corrosive,1.25f),(MDElement.Kinetic,.75f)],
    };
    public static float Resist(MDRobotKind k,MDElement e){if(k!=null&&resist.TryGetValue(k.Id,out var l))foreach(var (x,f) in l)if(x==e)return f;return 1;}
    public static IEnumerable<(MDElement e,float f)> ResistList(MDRobotKind k)=>k!=null&&resist.TryGetValue(k.Id,out var l)?l:[];
    // the reaction an element hit makes on a target with these statuses (pair reactions first, in this order)
    public static MDReaction Pick(MDElement e,Func<MDElement,bool> has)=>e switch{
        MDElement.Kinetic=>has(MDElement.Cold)?MDReaction.Shatter:MDReaction.None,
        MDElement.Explosive=>has(MDElement.Heat)||has(MDElement.Cold)||has(MDElement.Electric)||has(MDElement.Corrosive)?MDReaction.Spread:MDReaction.None,
        MDElement.Heat=>has(MDElement.Cold)?MDReaction.Thermal:has(MDElement.Electric)?MDReaction.Overload:has(MDElement.Corrosive)?MDReaction.Corrode:MDReaction.None,
        MDElement.Cold=>has(MDElement.Heat)?MDReaction.Thermal:has(MDElement.Electric)?MDReaction.Brittle:has(MDElement.Corrosive)?MDReaction.FrostEtch:MDReaction.None,
        MDElement.Electric=>has(MDElement.Heat)?MDReaction.Overload:has(MDElement.Cold)?MDReaction.Brittle:has(MDElement.Corrosive)?MDReaction.Electrolysis:MDReaction.None,
        MDElement.Corrosive=>has(MDElement.Heat)?MDReaction.Corrode:has(MDElement.Cold)?MDReaction.FrostEtch:has(MDElement.Electric)?MDReaction.Electrolysis:MDReaction.None,
        _=>MDReaction.None};
    public static int Count;                                         // tests: reactions so far
    public static float Bonus;                                       // tests: power reactions added to the hits that made them
    public static readonly List<string> Log=[];                      // tests: the last reactions (name, target, power before / after)
}

public sealed partial class MDTowerWorld {
    // statuses per element on a body (heat also while burning, cold while frozen, corrosive while poisoned)
    bool Has(ComponentBody b,Status s,MDElement e)=>e switch{
        MDElement.Heat=>s.heatUntil>time||b.Entity.FindComponent<ComponentOnFire>()?.IsOnFire==true,
        MDElement.Cold=>s.coldUntil>time||s.freezeUntil>time,
        MDElement.Electric=>s.elecUntil>time,
        MDElement.Corrosive=>s.acidUntil>time||s.poisonUntil>time,
        _=>false};
    public bool HasStatus(ComponentBody b,MDElement e)=>status.TryGetValue(b,out var s)&&Has(b,s,e);
    void SetAura(Status s,MDElement e,float until){switch(e){case MDElement.Heat:s.heatUntil=Math.Max(s.heatUntil,until);break;case MDElement.Cold:s.coldUntil=Math.Max(s.coldUntil,until);break;
        case MDElement.Electric:s.elecUntil=Math.Max(s.elecUntil,until);break;case MDElement.Corrosive:s.acidUntil=Math.Max(s.acidUntil,until);break;}}
    void ClearAura(ComponentBody b,Status s,MDElement e){switch(e){case MDElement.Heat:s.heatUntil=time;b.Entity.FindComponent<ComponentOnFire>()?.Extinguish();break;
        case MDElement.Cold:s.coldUntil=time;s.freezeUntil=Math.Min(s.freezeUntil,time);break;case MDElement.Electric:s.elecUntil=time;break;case MDElement.Corrosive:s.acidUntil=time;s.poisonUntil=Math.Min(s.poisonUntil,time);break;}}
    public bool Brittle(ComponentBody b)=>status.TryGetValue(b,out var s)&&s.brittleUntil>time;

    // A hit of element e and this power from u on b: resistance, brittleness, the reaction if any, the status. Returns the power to
    // deal. Secondary hits of reactions come back through MDTowerBrain.Hit with MDElement.None (no chains of reactions).
    public float Elemental(ComponentMDUnit u,ComponentBody b,MDElement e,float power) {
        if(e==MDElement.None)return power;
        var robot=b.Entity.FindComponent<ComponentMDRobot>();
        if(robot!=null)power*=MDElements.Resist(robot.Kind,e);
        var s=Get(b);
        if(s.brittleUntil>time&&e is MDElement.Kinetic or MDElement.Explosive)power*=1.35f;
        // three elements first: two statuses on it, or a two-element reaction on it within ResidueTime, and a third element
        if(MDSettings.Reactions&&MDElements.Aura(e)&&time>=s.reactUntil) {
            var others=new List<MDElement>();
            for(var x=MDElement.Heat;x<=MDElement.Corrosive;x++)if(x!=e&&(Has(b,s,x)||time<s.residueUntil&&(s.residueMask&1<<(int)x)!=0))others.Add(x);
            var tr=MDElements.Triad(e,others);
            if(tr!=MDReaction.None&&time>=s.reactAt[(int)tr]) {
                float before3=power;power=React(u,b,s,e,tr,power,robot);if(robot?.Boss!=null){power=before3+(power-before3)*MDElements.BossShare;robot.Boss.AddPoise(MDBossState.PoiseTriad,u.Mech!=null);}MDElements.Bonus+=power-before3;
                s.reactUntil=time+MDElements.GlobalCooldown;s.reactAt[(int)tr]=time+MDElements.TriadCooldown;s.residueUntil=time;
                MDElements.Count++;if(MDElements.Log.Count>=200)MDElements.Log.RemoveAt(0);
                MDElements.Log.Add($"{MDElements.Reactions[(int)tr].Name} {robot?.Kind.Id??"-"} {MDElements.Name(e)} {before3:0}->{power:0}");
                return power;
            }
        }
        var r=MDSettings.Reactions?MDElements.Pick(e,x=>Has(b,s,x)):MDReaction.None;
        if(r!=MDReaction.None&&time>=s.reactUntil&&time>=s.reactAt[(int)r]) {
            float before=power;var info0=MDElements.Reactions[(int)r];
            if(r is not (MDReaction.Spread or MDReaction.Shatter)){s.residueMask=1<<(int)info0.A|1<<(int)info0.B;s.residueUntil=time+MDElements.ResidueTime;}   // for a third element
            power=React(u,b,s,e,r,power,robot);if(robot?.Boss!=null){power=before+(power-before)*MDElements.BossShare;robot.Boss.AddPoise(MDBossState.PoisePair+(r==MDReaction.Overload?MDBossState.PoiseControl:0),u.Mech!=null);}MDElements.Bonus+=power-before;
            s.reactUntil=time+MDElements.GlobalCooldown;s.reactAt[(int)r]=time+MDElements.SameCooldown;
            MDElements.Count++;if(MDElements.Log.Count>=200)MDElements.Log.RemoveAt(0);
            MDElements.Log.Add($"{MDElements.Reactions[(int)r].Name} {robot?.Kind.Id??b.Entity.ValuesDictionary?.DatabaseObject?.Name} {MDElements.Name(e)} {before:0}->{power:0}");
            if(r is not (MDReaction.Spread or MDReaction.Shatter))return power;   // a pair reaction uses up the incoming element too
        }
        if(MDElements.Aura(e))SetAura(s,e,time+MDElements.AuraTime);
        return power;
    }
    float React(ComponentMDUnit u,ComponentBody b,Status s,MDElement e,MDReaction r,float power,ComponentMDRobot robot) {
        var c=MDCombat.Center(b);var info=MDElements.Reactions[(int)r];
        var other=r==MDReaction.Spread?Enumerable.Range(3,4).Select(i=>(MDElement)i).First(x=>Has(b,s,x)):MDElements.Triads.TryGetValue(r,out var els)?els.First(x=>x!=e):info.A==e?info.B:info.A;
        Color c1=MDElements.Colors[(int)e],c2=MDElements.Colors[(int)other];
        if(MDElements.Triads.TryGetValue(r,out var used))foreach(var x in used)ClearAura(b,s,x);   // three elements: all used up, before the effect (晶化 freezes)
        switch(r) {
            case MDReaction.Thermal:{   // x 2, and 3 % of its full health on top (armour phases cap a boss as usual)
                var h=b.Entity.FindComponent<ComponentHealth>();power=power*2+(h!=null?.03f*h.AttackResilience*(robot?.Boss!=null?.5f:1):0);   // a boss: half the share of its health
                ClearAura(b,s,MDElement.Heat);ClearAura(b,s,MDElement.Cold);
                for(int i=0;i<8;i++)Emit(S.Smoke,c+RDir()*.4f,RDir()*R(1,2.5f)+Vector3.UnitY,R(.6f,1),.4f,1.2f,new Color(235,235,240,150),new Color(235,235,240,0),false,0,1.5f);
                break;}
            case MDReaction.Overload:{
                foreach(var o in Near(u,b,3))MDTowerBrain.Hit(u,o,power*.6f,MDCombat.Center(o),Vector3.Normalize(MDCombat.Center(o)-c+Vector3.UnitY*.01f),false,"explosion",MDElement.None);
                if(robot==null||!robot.Kind.Boss)Stun(b,1.5f);
                Explosion(c,3,"heavy",c2,c1);ClearAura(b,s,MDElement.Heat);ClearAura(b,s,MDElement.Electric);
                break;}
            case MDReaction.Corrode:
                b.Entity.FindComponent<ComponentOnFire>()?.SetOnFire(null,4);ArmorDown(b,1.25f,6,c2);ClearAura(b,s,MDElement.Corrosive);s.heatUntil=time;
                break;
            case MDReaction.Brittle:
                s.brittleUntil=time+6;ClearAura(b,s,MDElement.Cold);ClearAura(b,s,MDElement.Electric);
                break;
            case MDReaction.FrostEtch:
                Slow(b,.4f,4,c1);ArmorDown(b,1.15f,6,c2);ClearAura(b,s,MDElement.Cold);ClearAura(b,s,MDElement.Corrosive);
                break;
            case MDReaction.Electrolysis:
                foreach(var o in Near(u,b,6).Take(3)) {
                    var oc=MDCombat.Center(o);AddBeam(c,oc,.07f,MDElements.Colors[(int)MDElement.Electric],.3f,true);
                    MDTowerBrain.Hit(u,o,power*.5f,oc,Vector3.Normalize(oc-c+Vector3.UnitY*.01f),false,"energy",MDElement.None);SetAura(Get(o),MDElement.Corrosive,time+MDElements.AuraTime);
                }
                ClearAura(b,s,MDElement.Electric);ClearAura(b,s,MDElement.Corrosive);
                break;
            case MDReaction.Spread:
                foreach(var o in Near(u,b,4)){var os=Get(o);foreach(var x in new[]{MDElement.Heat,MDElement.Cold,MDElement.Electric,MDElement.Corrosive})if(Has(b,s,x)){SetAura(os,x,time+MDElements.AuraTime);AddBeam(c,MDCombat.Center(o),.05f,MDElements.Colors[(int)x],.25f,false);}}
                break;
            case MDReaction.Storm:{
                float hit=power;power*=3;
                foreach(var o in Near(u,b,5)){var oc=MDCombat.Center(o);AddBeam(c,oc,.09f,MDElements.Colors[(int)MDElement.Electric],.35f,true);MDTowerBrain.Hit(u,o,hit,oc,Vector3.Normalize(oc-c+Vector3.UnitY*.01f),false,"energy",MDElement.None);}
                if(robot==null||!robot.Kind.Boss)Stun(b,2);Explosion(c,5,"heavy",c1,c2);
                break;}
            case MDReaction.Meltdown:
                power*=3.5f;ArmorDown(b,1.5f,8,MDElements.Colors[(int)MDElement.Corrosive]);
                break;
            case MDReaction.Plasma:{
                float hit=power;power*=3;
                foreach(var o in Near(u,b,8).Take(5)){var oc=MDCombat.Center(o);AddBeam(c,oc,.08f,new Color(255,140,255),.35f,true);MDTowerBrain.Hit(u,o,hit*.8f,oc,Vector3.Normalize(oc-c+Vector3.UnitY*.01f),false,"energy",MDElement.None);o.Entity.FindComponent<ComponentOnFire>()?.SetOnFire(null,3);}
                b.Entity.FindComponent<ComponentOnFire>()?.SetOnFire(null,5);
                break;}
            case MDReaction.Crystal:
                power*=3;if(robot==null||!robot.Kind.Boss){s.freezeImmuneUntil=time;Freeze(b,2.5f,0);}else power*=1.5f;
                break;
            case MDReaction.Shatter:
                power*=1.6f;ClearAura(b,s,MDElement.Cold);
                for(int i=0;i<10;i++)Emit(S.Crystal,c+RDir()*.3f,RDir()*R(2,5),R(.4f,.7f),.12f,.05f,new Color(215,242,255),new Color(215,242,255,0),true,9);
                break;
        }
        // the look: a ring in each colour, a flash, a few sparks, and the name rising over the target
        Ring(b.Position,Math.Max(1.2f,b.BoxSize.X*.8f),c1,.5f);Ring(b.Position,Math.Max(1.6f,b.BoxSize.X),c2,.6f,.12f);Flash(c,.9f,c1,.12f);Flash(c,.6f,c2,.16f);
        Sparks(c,Vector3.UnitY,8,c1,5,1,.4f);Sparks(c,Vector3.UnitY,8,c2,5,1,.4f);
        int cap=MaxLabels[Math.Clamp(MDSettings.FxLevel,0,2)];while(labels.Count>=cap)labels.RemoveAt(0);   // many reactions at once: the newest names only
        bool triad=MDElements.Triads.TryGetValue(r,out var three);
        if(triad){Ring(b.Position,Math.Max(2.4f,b.BoxSize.X*1.4f),MDElements.Colors[(int)three[2]],.8f,.18f);Flash(c,1.6f,Color.White,.2f);}   // all three used up, a bigger look
        labels.Add(new ReactionLabel{pos=b.Position+Vector3.UnitY*(b.BoxSize.Y+LabelsAt),text=info.Name,c=triad?new Color(255,236,140):Color.Lerp(c1,Color.White,.35f),c2=c2,life=triad?2:1.6f,big=triad});   // above the status icons
        return power;
    }
    IEnumerable<ComponentBody> Near(ComponentMDUnit u,ComponentBody b,float r) {
        var p=b.Position;var list=new DynamicArray<ComponentBody>();project.FindSubsystem<SubsystemBodies>(true).FindBodiesAroundPoint(new(p.X,p.Z),r+1,list);
        return list.Where(o=>o!=b&&MDCombat.IsEnemy(u,o)&&Vector3.DistanceSquared(o.Position,p)<=r*r).OrderBy(o=>Vector3.DistanceSquared(o.Position,p)).ToList();
    }

    // ---- icons over the targets and reaction names (drawn after the particles)
    sealed class ReactionLabel { public Vector3 pos; public string text; public Color c,c2; public float age,life; public bool big; }
    readonly List<ReactionLabel> labels=[];
    Texture2D iconTexture;TexturedBatch3D iconBatch;FontBatch3D labelBatch;
    public const float IconRange=32,TowerIconRange=14;
    // 0.16.0 one stack over a target, from the top of its box (user, 2026-10-31: "元素图标和敌人状态条会有重叠部分"): status icons, then
    // damage numbers, then reaction names (they rise), then a boss's bars; the bars also climb with distance as the icons grow
    public const float IconsAt=1f,NumbersAt=2f,LabelsAt=2.8f,BarsAt=4.4f;
    public static int IconsDrawn;   // tests: icons queued in the last frame
    public List<MDIcon> Icons(ComponentBody b){var l=new List<MDIcon>();if(status.TryGetValue(b,out var s))Icons(b,s,l);return l;}
    void Icons(ComponentBody b,Status s,List<MDIcon> l) {
        if(Has(b,s,MDElement.Heat))l.Add(MDIcon.Heat);if(s.coldUntil>time&&s.freezeUntil<=time)l.Add(MDIcon.Cold);if(s.elecUntil>time)l.Add(MDIcon.Electric);
        if(Has(b,s,MDElement.Corrosive))l.Add(MDIcon.Corrosive);
        if(s.freezeUntil>time)l.Add(MDIcon.Frozen);else if(s.stunUntil>time)l.Add(MDIcon.Paralysed);else if(s.slowUntil>time&&s.slowFactor<1)l.Add(MDIcon.Slowed);
        if(s.armorUntil>time)l.Add(MDIcon.ArmourDown);if(s.brittleUntil>time)l.Add(MDIcon.Brittle);
    }
    void SimulateLabels(float dt){for(int i=labels.Count-1;i>=0;i--){labels[i].age+=dt;labels[i].pos+=Vector3.UnitY*dt*.8f;if(labels[i].age>=labels[i].life)labels.RemoveAt(i);}}
    void DrawElements(Vector3 eye,Vector3 right,Vector3 up) {
        iconTexture??=ContentManager.Get<Texture2D>("Towers/status");
        iconBatch??=SubsystemParticles.PrimitivesRenderer.TexturedBatch(iconTexture,true,2,DepthStencilState.DepthRead,RasterizerState.CullNoneScissor,BlendState.AlphaBlend,SamplerState.LinearClamp);
        DrawNumbers(eye,right,up);   // MDDamageNumbers.cs
        DrawBossBars(eye,right,up);  // MDMedic.cs (0.16.0)
        DrawCore(eye,right,up);      // MDThreat.cs (0.16.0)
        IconsDrawn=0;BarsDrawn=0;var l=new List<MDIcon>(6);
        foreach(var (b,s) in status) {
            if(!b.Entity.IsAddedToProject||!MDCombat.Living(b))continue;
            float d=Vector3.Distance(b.Position,eye);if(d>IconRange)continue;
            l.Clear();Icons(b,s,l);if(l.Count==0)continue;
            float size=.32f+.012f*d;                                     // a little bigger far off, so they stay readable
            var top=b.Position+Vector3.UnitY*(b.BoxSize.Y+IconsAt+size);
            for(int i=0;i<l.Count&&i<5;i++) {
                var p=top+right*((i-(Math.Min(l.Count,5)-1)*.5f)*size*2.1f);int k=(int)l[i];float u=k%4/4f,v=k/4/4f,q=1/4f;
                var x=right*size;var y=up*size;
                iconBatch.QueueQuad(p-x-y,p+x-y,p+x+y,p-x+y,new Vector2(u,v+q),new Vector2(u+q,v+q),new Vector2(u+q,v),new Vector2(u,v),Color.White);IconsDrawn++;
            }
        }
        // each tower's damage type over it, near the camera
        var near=new DynamicArray<ComponentBody>();project.FindSubsystem<SubsystemBodies>(true).FindBodiesAroundPoint(new(eye.X,eye.Z),TowerIconRange,near);
        foreach(var b in near) {
            if(b.Entity.FindComponent<ComponentMDUnit>() is not {Tower:{} d} tu||tu.Destroyed)continue;
            float dist=Vector3.Distance(b.Position,eye);if(dist>TowerIconRange)continue;
            if(MDMedic.Is(tu)&&!MDMedic.Unlimited){ChargeBar(tu,new Vector3(b.Position.X,b.BoundingBox.Max.Y+.6f,b.Position.Z),right,up,Math.Clamp((TowerIconRange-dist)/3,0,1),dist);continue;}   // MDMedic.cs
            var e=MDElements.Of(d);if(e==MDElement.None)continue;
            float size=.3f+.012f*dist,fade=Math.Clamp((TowerIconRange-dist)/3,0,1);var p=new Vector3(b.Position.X,b.BoundingBox.Max.Y+.5f+size,b.Position.Z);
            int k=(int)e-1;float u=k%4/4f,v=k/4/4f,q=1/4f;var x=right*size;var y=up*size;
            iconBatch.QueueQuad(p-x-y,p+x-y,p+x+y,p-x+y,new Vector2(u,v+q),new Vector2(u+q,v+q),new Vector2(u+q,v),new Vector2(u,v),Color.White*fade);IconsDrawn++;
        }
        if(labels.Count==0)return;
        labelBatch??=SubsystemParticles.PrimitivesRenderer.FontBatch(LabelWidget.BitmapFont,3,DepthStencilState.None,RasterizerState.CullNoneScissor,BlendState.AlphaBlend,SamplerState.LinearClamp);
        foreach(var lb in labels) {
            float d=Vector3.Distance(lb.pos,eye);if(d>48)continue;
            float t=lb.age/lb.life,a=t<.7f?1:1-(t-.7f)/.3f,sc=(.032f+.0012f*d)*(t<.12f?.6f+3.3f*t:1)*(lb.big?1.5f:1);
            var outline=new Color((byte)10,(byte)12,(byte)18,(byte)(220*a));var anchor=TextAnchor.HorizontalCenter|TextAnchor.VerticalCenter;
            foreach(var (ox,oy) in new[]{(-1,0),(1,0),(0,-1),(0,1),(-1,-1),(1,1),(-1,1),(1,-1)})   // a dark outline, readable over sky and ground
                labelBatch.QueueText(lb.text,lb.pos+(right*ox-up*oy)*sc*2.2f,right*sc,-up*sc,outline,anchor);
            labelBatch.QueueText(lb.text,lb.pos,right*sc,-up*sc,lb.c*a,anchor);
        }
    }
}
