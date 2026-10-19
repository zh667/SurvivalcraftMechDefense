using Engine;
using GameEntitySystem;
using TemplatesDatabase;
using System.Runtime.CompilerServices;

namespace Game;

// The devil mech (Destroy Gunners RD17), the boss that carries the mech blueprint. A player calls it with the decoy beacon
// (MDPartBlock): placed on the ground it counts down 30 s, then the devil mech flies in from 40 m away and fights around the
// beacon. One at a time per world.
// Weak channel (after the original's three damage channels, the 90 % resistances softened to 60 %): every hit is kinetic,
// explosion or energy (MDTowerBrain.Hit tags the towers' and Mercury's; bullets and other attacks count as kinetic); only the
// current weak channel does full damage, the other two 40 %. The weak channel changes every 20 s, shown by the colour of its
// aura and a message, so a defence of one tower type is slow and a mixed one fast.
// Attacks: a homing missile volley at up to three targets (towers first), a beam sweep across its target and a sabre charge (the
// original's three-slash clip) at a target within 35 m; below half health all come faster. Model x3 (9.6 m tall, 15 m wings,
// tools/build_unit_assets.py): it hovers 3 m above the ground, circling its target 20 m away.
public sealed class MDBossState {
    public static readonly string[] Channels=["kinetic","explosion","energy"];
    public static readonly string[] ChannelNames=["冲击（子弹、狙击、水炮、近战）","爆炸（炮弹、导弹、榴弹、火焰）","能量（射线、电击、声波、毒雾）"];
    static readonly Color[] ChannelColors=[new(235,235,245),new(255,140,50),new(80,210,255)];
    public const float WeakFactor=1,OtherFactor=.4f,WeakPeriod=20,Altitude=3,Orbit=20,ChargeSpeed=16,SlashReach=7;
    readonly ComponentMDRobot r;
    public int Weak; public float WeakTimer,Volley=4,SweepCool=7,Sweep=-1,SweepTick,Orbiting,ChargeCool=12,Charge=-1,Slash=-1; public int Slashes; public Vector3? Home;
    public readonly List<ComponentBody> Volleyed=[]; float aura;
    public Vector3 LastWant,LastFly; public int Charges,Sweeps,Volleys;   // lab: where it wanted to be, the velocity it asked for, attacks started
    public MDBossState(ComponentMDRobot robot,ValuesDictionary v) {
        r=robot;Weak=Math.Clamp(v.GetValue("BossWeak",0),0,2);WeakTimer=v.GetValue("BossWeakTimer",WeakPeriod);
        if(v.GetValue("BossHasHome",false))Home=v.GetValue("BossHome",Vector3.Zero);
    }
    // Flight goes through the creature's own locomotion (FlySpeed 16 in its template): a fly order is the wanted velocity over
    // that speed. Setting the body's gravity or velocity directly does not hold: locomotion turns gravity back on every update.
    void Fly(Vector3 velocity){var l=r.Creature.ComponentLocomotion;l.FlyOrder=l.FlySpeed>0?velocity/l.FlySpeed:Vector3.Zero;}
    public void Save(ValuesDictionary v){v.SetValue("BossWeak",Weak);v.SetValue("BossWeakTimer",WeakTimer);v.SetValue("BossHasHome",Home.HasValue);if(Home.HasValue)v.SetValue("BossHome",Home.Value);}
    public bool Enraged=>r.Health.Health<.5f;
    public void Died(){r.Body.Velocity=new(0,-2,0);MDBoss.Announce(r.Project,r.Body.Position,"恶魔机甲被击毁了！它掉落了机甲设计图。",new Color(255,220,120));}
    string Clip(params string[] names)=>names.FirstOrDefault(r.Def.Has)??"idle";
    public void Update(float dt) {
        var w=MDTowerWorld.For(r.Project);var body=r.Body;
        // weak channel
        WeakTimer-=dt;if(WeakTimer<=0){WeakTimer=WeakPeriod;Weak=(Weak+1+MDRobots.Pick(new[]{0,1}))%3;MDBoss.Announce(r.Project,body.Position,"恶魔机甲的弱点变成了："+ChannelNames[Weak],ChannelColors[Weak]);}
        aura-=dt;if(aura<=0){aura=.12f;var c=MDCombat.Center(body);w.Ring(c-Vector3.UnitY*body.BoxSize.Y*.45f,4.5f,ChannelColors[Weak],.25f,0);w.Flash(r.Point("weak",out _),1.1f,ChannelColors[Weak],.12f);}
        if(Charge>=0||Slash>=0){ChargeStep(dt,w);return;}
        // movement: hover over the ground, circle the target (or the beacon), always facing what it fights
        var focus=r.Target!=null?r.Target.Position:Home??body.Position;
        Orbiting+=dt*(Enraged?.22f:.15f);
        var want=focus+new Vector3(MathF.Sin(Orbiting),0,MathF.Cos(Orbiting))*(r.Target!=null?Orbit:6);
        var ground=r.Project.FindSubsystem<SubsystemTerrain>(true).Raycast(new Vector3(want.X,body.Position.Y+4,want.Z),new Vector3(want.X,body.Position.Y-40,want.Z),false,true,(cv,cd)=>BlocksManager.Blocks[Terrain.ExtractContents(cv)].IsCollidable_(cv));
        want.Y=(ground.HasValue?body.Position.Y+4-ground.Value.Distance:focus.Y)+Altitude;
        var to=want-body.Position;var v=to.LengthSquared()>.01f?Vector3.Normalize(to)*Math.Min(r.Kind.Speed,to.Length()*.7f):Vector3.Zero;   // gentle: the fly order lags ~0.4 s
        Fly(v);LastWant=want;LastFly=v;
        if(r.Target!=null)r.Face(MDCombat.Center(r.Target),dt,1.6f);
        // attacks
        float pace=Enraged?.7f:1;Volley-=dt;SweepCool-=dt;ChargeCool-=dt;
        if(r.Windup>=0) {
            r.Windup-=dt;foreach(var b in Volleyed)if(b.Entity.IsAddedToProject)w.LockMark(MDCombat.Center(b),Math.Max(1,b.BoxSize.Y*.7f));
            if(r.Windup<0){r.ShotsLeft=r.Kind.Shots;r.Gap=0;r.Anim.PlayAction(Clip("attack_fire"));}
        }
        else if(r.ShotsLeft>0){r.Gap-=dt;if(r.Gap<=0){var alive=Volleyed.Where(r.Valid).ToList();if(alive.Count>0)r.FireOne(alive[r.ShotsFired%alive.Count]);r.ShotsLeft--;r.Gap=r.Kind.ShotGap;if(r.ShotsLeft==0)Volley=r.Kind.Interval*pace;}}
        else if(Sweep>=0)SweepStep(dt,w);
        else if(r.Target!=null&&Volley<=0) {
            Volleyed.Clear();var near=new DynamicArray<ComponentBody>();r.Project.FindSubsystem<SubsystemBodies>(true).FindBodiesAroundPoint(new(body.Position.X,body.Position.Z),r.Kind.Sight,near);
            Volleyed.AddRange(near.Where(r.Valid).OrderBy(b=>(b.Entity.FindComponent<ComponentMDUnit>()!=null?0:20)+Vector3.Distance(b.Position,body.Position)).Take(3));
            if(Volleyed.Count==0)Volleyed.Add(r.Target);r.Windup=r.Kind.Windup;Volleys++;MDRobots.Sound(r.Project,7,body.Position,1);
        }
        else if(r.Target!=null&&SweepCool<=0&&r.Sight(r.Eye,r.Target)){Sweep=0;SweepTick=0;Sweeps++;MDRobots.Sound(r.Project,10,body.Position,1);r.Anim.PlayAction(Clip("attack_fire"));}
        else if(r.Target!=null&&ChargeCool<=0&&Vector3.Distance(r.Target.Position,body.Position)<35){Charge=0;Charges++;r.Anim.PlayAction(Clip("dash_charge"));MDRobots.Sound(r.Project,31,body.Position,1);}
        var flat=new Vector3(body.Velocity.X,0,body.Velocity.Z);
        r.Anim.PlayBase(flat.Length()<3?"idle":Clip("fly_forward"),1,.3f);
    }
    // The sabre charge: flies at its target at 16 m/s, low (feet at the target's), for at most 2.5 s; within 7 m it plays the
    // original's slash clip (2.2 s, three slashes) and each slash hits what stands in a 120 degree wedge 7 m in front:
    // 3 x 45 x 2.7 = 365 against a tower, 3 x 2.5 x 1.6 = 1.2 x a player's health before armour.
    void ChargeStep(float dt,MDTowerWorld w) {
        var body=r.Body;
        if(Charge>=0) {
            Charge+=dt;if(r.Target==null||Charge>2.5f){Charge=-1;Slash=0;Slashes=0;r.Anim.PlayAction(Clip("attack_saber_once","attack_saber"));return;}
            var to=r.Target.Position-body.Position;var flat=new Vector3(to.X,0,to.Z);
            var v=flat.LengthSquared()>.01f?Vector3.Normalize(flat)*ChargeSpeed:Vector3.Zero;v.Y=Math.Clamp(to.Y,-4,4);Fly(v);r.Face(MDCombat.Center(r.Target),dt,4);
            w.Thruster(body.Position+Vector3.UnitY*body.BoxSize.Y*.5f-body.Matrix.Forward*1.5f,-body.Matrix.Forward,2);
            if(flat.Length()<SlashReach-1){Charge=-1;Slash=0;Slashes=0;r.Anim.PlayAction(Clip("attack_saber_once","attack_saber"));}
            return;
        }
        Slash+=dt;Fly(Vector3.Zero);
        if(Slashes<3&&Slash>=.55f+.55f*Slashes) {
            Slashes++;var f=body.Matrix.Forward;var c=body.Position+Vector3.UnitY*2+f*2;MDRobots.Sound(r.Project,16,c,1);
            var near=new DynamicArray<ComponentBody>();r.Project.FindSubsystem<SubsystemBodies>(true).FindBodiesAroundPoint(new(c.X,c.Z),SlashReach+2,near);
            foreach(var b in near) {
                if(b==body||MDRobots.IsRobot(b.Entity))continue;var to=MDCombat.Center(b)-body.Position;var flat=new Vector3(to.X,0,to.Z);float d=flat.Length();
                if(d>SlashReach+b.BoxSize.X*.5f||Vector3.Dot(flat/Math.Max(d,.01f),f)<.5f)continue;
                bool structure=b.Entity.FindComponent<ComponentHealth>()==null;MDRobots.Hit(r.Entity,b,r.Kind,structure?2.7f:1.6f,MDCombat.Center(b),f);
                w.Sparks(MDCombat.Center(b),-f,14,new Color(255,140,160),7,1,.4f,.06f);
            }
            w.Flash(c+f*2,2.2f,new Color(255,90,120),.12f);
        }
        if(Slash>=2.2f){Slash=-1;ChargeCool=16*(Enraged?.7f:1);}
    }
    // 2.2 s beam from the eye sweeping 50 degrees across the target, a hit every 0.15 s on what it touches first
    void SweepStep(float dt,MDTowerWorld w) {
        Sweep+=dt;if(Sweep>=2.2f||r.Target==null){Sweep=-1;SweepCool=13*(Enraged?.7f:1);return;}
        var from=r.Eye;var tc=MDCombat.Center(r.Target);var dir=Vector3.Normalize(tc-from);float a=(Sweep/2.2f-.5f)*.87f;
        dir=Vector3.Transform(dir,Quaternion.CreateFromAxisAngle(Vector3.UnitY,a));var end=from+dir*r.Kind.Range*1.2f;
        var terrain=r.Project.FindSubsystem<SubsystemTerrain>(true).Raycast(from,end,false,true,(cv,cd)=>BlocksManager.Blocks[Terrain.ExtractContents(cv)].IsCollidable_(cv));
        var hit=r.Project.FindSubsystem<SubsystemBodies>(true).Raycast(from,end,0,(b,d)=>b!=r.Body&&!MDRobots.IsRobot(b.Entity));
        float t=Math.Min(terrain?.Distance??float.MaxValue,hit?.Distance??float.MaxValue);if(t<float.MaxValue)end=from+dir*t;
        w.HoldBeam(r,from,end,.28f,new Color(255,70,90));
        SweepTick-=dt;if(SweepTick<=0){SweepTick=.15f;w.Sparks(end,-dir,5,new Color(255,120,120),5,.9f,.3f,.05f);if(hit.HasValue&&(terrain==null||hit.Value.Distance<=terrain.Value.Distance))MDRobots.Hit(r.Entity,hit.Value.ComponentBody,r.Kind,.36f,end,dir);}
    }
}

public static class MDBoss {
    public const float Countdown=30;
    sealed class Pending { public Vector3 at; public float left=-1,blink; }
    static readonly ConditionalWeakTable<Project,Pending> pending=new();
    public static ComponentMDRobot Alive(Project p)=>p.Entities.Select(e=>e.FindComponent<ComponentMDRobot>()).FirstOrDefault(x=>x?.Kind.Boss==true&&x.Health.Health>0);
    public static bool Counting(Project p)=>pending.TryGetValue(p,out var c)&&c.left>=0;
    public static void Announce(Project p,Vector3 at,string text,Color c) {
        foreach(var pl in p.FindSubsystem<SubsystemPlayers>(true).ComponentPlayers)if(Vector3.Distance(pl.ComponentBody.Position,at)<150)pl.ComponentGui.DisplaySmallMessage(text,c,true,true);
    }
    // Right-click the ground with a beacon (MDDeployPatch): it is used up and the countdown starts.
    public static bool PlaceBeacon(ComponentMiner miner,Ray3 ray) {
        var p=miner.Project;var pl=miner.ComponentPlayer;
        var hit=miner.Raycast<TerrainRaycastResult>(ray,RaycastMode.Digging);if(!hit.HasValue||hit.Value.CellFace.Face!=4){MDPlayer.Message(pl,"对着地面右键放下信标。");return false;}
        if(Alive(p)!=null){MDPlayer.Message(pl,"已经有一台恶魔机甲在附近了。");return false;}
        if(Counting(p)){MDPlayer.Message(pl,"已有一个信标在倒计时。");return false;}
        var c=pending.GetOrCreateValue(p);c.at=hit.Value.HitPoint()+Vector3.UnitY*.05f;c.left=Countdown;miner.RemoveActiveTool(1);
        Announce(p,c.at,$"诱敌信标已启动：{Countdown:0} 秒后恶魔机甲会来到这里。",new Color(255,120,100));
        p.FindSubsystem<SubsystemAudio>(true).PlaySound(MDMechBrain.Se(7),1,0,c.at,20,false);return true;
    }
    public static void Tick(Project p,float dt) {
        if(!pending.TryGetValue(p,out var c)||c.left<0)return;
        var w=MDTowerWorld.For(p);float before=c.left;c.left-=dt;c.blink-=dt;
        if(c.blink<=0){c.blink=c.left<10?.25f:.6f;w.Flash(c.at+Vector3.UnitY*.6f,.8f,new Color(255,50,40),.15f);w.Ring(c.at,2.5f,new Color(255,60,50),.5f);}
        foreach(int s in new[]{20,10,5,3,2,1})if(before>s&&c.left<=s)Announce(p,c.at,$"恶魔机甲还有 {s} 秒到达。",new Color(255,120,100));
        if(c.left>0)return;
        c.left=-1;Spawn(p,c.at);
    }
    public static ComponentMDRobot Spawn(Project p,Vector3 at) {
        var rng=new System.Random();float a=(float)(rng.NextDouble()*Math.PI*2);var from=at+new Vector3(MathF.Sin(a),0,MathF.Cos(a))*40+Vector3.UnitY*18;
        var e=DatabaseManager.CreateEntity(p,MDRobots.Boss,true);e.FindComponent<ComponentBody>(true).Position=from;
        var r=e.FindComponent<ComponentMDRobot>(true);p.AddEntity(e);r.Boss.Home=at;r.RaidGoal=at;
        Announce(p,at,"恶魔机甲来了！当前弱点："+MDBossState.ChannelNames[r.Boss.Weak],new Color(255,90,90));
        MDRobots.Sound(p,31,from,1);return r;
    }
    // ModLoader hook ProcessAttackment (MDModLoader): the weak channel takes full damage, the others 40 %. No knockback and no
    // stun: every hit stuns a creature 0.2 s, and a stunned creature's locomotion stops flying and turns gravity back on, so
    // under tower fire the boss sank to the ground (lab trace, 2026-10-20).
    public static void Modify(Attackment a) {
        if(a.Target?.FindComponent<ComponentMDRobot>() is not {Boss:{} b})return;
        a.ImpulseFactor=0;a.StunTimeSet=null;a.StunTimeAdd=0;
        string ch=a is MDTowerAttackment t?t.Channel??"kinetic":"kinetic";
        a.AttackPower*=ch==MDBossState.Channels[b.Weak]?MDBossState.WeakFactor:MDBossState.OtherFactor;
    }
}
