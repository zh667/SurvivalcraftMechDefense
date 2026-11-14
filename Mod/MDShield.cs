using Engine;
using Engine.Graphics;
using GameEntitySystem;

namespace Game;

// 1.0.0 (second batch, user 2026-11-13: "防线核心增加一个防御罩功能，可以保护范围内的炮台和主角，受到攻击会消耗防御核心的耐久。防御罩半径动态覆盖到
// 与防线核心四个面直接相连的炮台中最长的位置。炮台与防线核心直接相连后，会增加防御核心的耐久，但自身耐久下降到1%"; the rules asked back and
// answered: first a chain of towers with the radius of the farthest one, then a fixed range (below); a tower that leaves gets its durability back; the shield
// covers the towers and the players in it; when it is gone nothing more is destroyed, the core's loss takes the towers off line as before):
// - Members: every tower within Range m of the live core (horizontally), found again every Every s (user, 2026-11-13, after the first
//   cut: "这个应该是防线核心的范围内……面贴面、一座挨一座连上的炮塔，我都不知道怎么做，而且太麻烦，一个范围可以"; it took towers touching
//   the core face to face and chains of them). Range = MDThreat.DownRadius, the core's defence that goes off line with it. A member
//   hands what it has over 1 % (Keep) to the core's shield store (Points), then stays at 1 %; repaired or healed later, the extra goes
//   over too. Pooled: what each has handed over.
// - The shield: a dome of Range m round the core. A robot's hit
//   (MDRobots.Hit) on a tower, a mech or a player inside it takes Points first, then the core's own durability. Any other damage
//   to a member or the core (user, 2026-11-14: "罩内的炮塔不管受到什么伤害，都先扣罩子，再扣核心，和机兵的攻击一样"; a player's weapon,
//   animals, explosions, fire, falls; the player's hits stay: "玩家用武器打自己的炮塔要不要直接无效 不要") goes the same way
//   (ComponentMDDamage.Damage -> Takes); until 1.0.0 (11-14) it went straight to the member, which stays at 1 %. The core destroyed:
//   the store is gone, the members stay at 1 % and go off line with the towers round the core (MDThreat.CoreDestroyed).
// - A member that leaves (moved out of range, taken down, the core taken down or put out, the setting off) gets back its share of what
//   is left: Pooled x Points / (all Pooled). One destroyed takes its share with it.
// - Setting 防线核心防御罩 (MDSettings.CoreShield), on by default. Saved: the core's ShieldPoints, each member's ShieldPooled.
public static class MDShield {
    public const float Keep=.01f,Every=.5f,Range=MDThreat.DownRadius,FlashTime=.35f;
    public static int Absorbed,Taken; public static float AbsorbedPoints,TakenPoints;   // tests (Taken: damage other than a robot's hit)
    sealed class State { public ComponentMDUnit Core; public readonly HashSet<ComponentMDUnit> Members=[]; public float Radius; public double Next,FlashUntil,LastSpark; public Vector3 FlashAt; }
    static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Project,State> states=new();
    static State For(Project p)=>states.GetValue(p,_=>new State());
    static float Points(ComponentMDUnit u)=>u.Tower?.hp??u.Damage.AttackResilience;
    public static ComponentMDUnit Core(Project p)=>For(p).Core;
    public static int MemberCount(Project p)=>For(p).Members.Count;
    public static float Radius(Project p)=>For(p).Core==null?0:For(p).Radius;
    public static Vector3 Centre(ComponentMDUnit core)=>core.Body.Position;
    public static float Pooled(Project p)=>For(p).Members.Sum(u=>u.ShieldPooled);
    public static bool IsMember(ComponentMDUnit u)=>u!=null&&For(u.Project).Members.Contains(u);

    // every frame from MDThreat.Update with the live core (null: none)
    public static void Update(Project p,ComponentMDUnit core) {
        var s=For(p);double now=p.FindSubsystem<SubsystemTime>(true).GameTime;
        if(s.Core!=null&&s.Core!=core){Release(s,s.Core.Entity.IsAddedToProject&&!s.Core.Destroyed);}   // another core, or this one gone
        if(core==null||!MDSettings.CoreShield||core.Destroyed){if(s.Core!=null)Release(s,true);s.Core=null;return;}
        s.Core=core;if(now<s.Next&&s.Next-now<=Every)return;s.Next=now+Every;
        // the towers in range (not one being carried: it joins again where it is put down)
        var c=core.Body.Position;
        var inRange=p.Entities.Select(e=>e.FindComponent<ComponentMDUnit>()).Where(u=>u?.Tower!=null&&u!=core&&!u.Destroyed&&u.Tower.attack.type!="core"&&u!=MDTowerTools.Moving&&Vector2.Distance(new(u.Body.Position.X,u.Body.Position.Z),new(c.X,c.Z))<=Range).ToHashSet();
        foreach(var u in s.Members.Where(u=>!inRange.Contains(u)).ToList())Leave(s,u,u.Entity.IsAddedToProject&&!u.Destroyed);
        foreach(var u in inRange){s.Members.Add(u);Hand(s,u);}
        s.Radius=Range;
    }
    // a member's durability over Keep goes to the store
    static void Hand(State s,ComponentMDUnit u) {
        float over=u.Damage.Hitpoints-Keep;if(over<=1e-4f)return;
        float pts=over*Points(u);u.ShieldPooled+=pts;s.Core.ShieldPoints+=pts;u.SetHitpoints(Keep);
    }
    static float Share(State s){float all=s.Members.Sum(m=>m.ShieldPooled);return all<=0?0:Math.Clamp(s.Core.ShieldPoints/all,0,1);}
    static void Leave(State s,ComponentMDUnit u,bool back) {
        float give=u.ShieldPooled*Share(s);s.Core.ShieldPoints=Math.Max(0,s.Core.ShieldPoints-give);
        if(back){u.SetHitpoints(Math.Min(1,u.Damage.Hitpoints+give/Points(u)));MDTowerWorld.For(u.Project).Sparks(u.Body.Position+Vector3.UnitY*1.2f,Vector3.UnitY,8,new Color(140,220,255),3,.8f,.3f,.04f);}
        u.ShieldPooled=0;s.Members.Remove(u);
    }
    // all members leave: with their share of what is left (back), or with nothing (the core destroyed)
    static void Release(State s,bool back) {
        if(s.Core==null)return;
        if(!back){foreach(var u in s.Members)u.ShieldPooled=0;s.Members.Clear();s.Core.ShieldPoints=0;return;}
        foreach(var u in s.Members.ToList())Leave(s,u,u.Entity.IsAddedToProject&&!u.Destroyed);
    }
    // the core destroyed (ComponentMDUnit.TowerDestroyed): the members are taken off line with the others and keep their 1 %
    public static List<ComponentMDUnit> CoreLost(ComponentMDUnit core) {
        var s=For(core.Project);List<ComponentMDUnit> lost=s.Core==core?s.Members.ToList():[];
        if(s.Core==core){Release(s,false);s.Core=null;}
        core.ShieldPoints=0;return lost;
    }
    // a member taken down with a pickaxe: its share back first, so its item keeps the durability (MDTowerMaintenance.Dismantle)
    public static void Detach(ComponentMDUnit u){var s=For(u.Project);if(s.Core!=null&&s.Members.Contains(u))Leave(s,u,true);}
    // the core taken down or put out by a new one: the members get their share back
    public static void CoreRemoved(ComponentMDUnit core){var s=For(core.Project);if(s.Core==core){Release(s,true);s.Core=null;}}

    // a robot's hit on b (MDRobots.Hit): true when the shield took it
    public static bool Absorb(Entity attacker,ComponentBody b,float power,Vector3 point) {
        if(b==null||power<=0)return false;var s=For(b.Project);var core=s.Core;
        if(core==null||!core.Entity.IsAddedToProject||core.Destroyed||!MDSettings.CoreShield)return false;
        bool covered=b.Entity.FindComponent<ComponentMDUnit>() is {} u?u==core||u.Tower!=null||u.Mech!=null:b.Entity.FindComponent<ComponentPlayer>()!=null;
        if(!covered)return false;
        var c=Centre(core);if(Vector3.DistanceSquared(MDCombat.Center(b),c)>s.Radius*s.Radius)return false;
        Spend(core,power);Absorbed++;AbsorbedPoints+=power;
        // the flash where the hit met the dome: on the line from the core towards the attacker
        var from=attacker?.FindComponent<ComponentBody>()?.Position??point;var d=from-c;s.FlashAt=d.LengthSquared()>1e-4f?c+Vector3.Normalize(d)*s.Radius:point;s.FlashUntil=b.Project.FindSubsystem<SubsystemTime>(true).GameTime+FlashTime;
        MDTowerWorld.For(b.Project).Sparks(s.FlashAt,-Vector3.Normalize(d+new Vector3(0,.001f,0)),6,new Color(140,220,255),4,.9f,.25f,.05f);
        return true;
    }
    // the store first, then the core itself (destroyed at 0: ComponentMDUnit.Update)
    static void Spend(ComponentMDUnit core,float points) {
        float fromStore=Math.Min(points,core.ShieldPoints);core.ShieldPoints-=fromStore;float rest=points-fromStore;
        if(rest>0)core.SetHitpoints(Math.Max(0,core.Damage.Hitpoints-rest/Points(core)));
    }
    // any other damage (amount: a share of the durability) to a member or the live core (ComponentMDDamage.Damage): true when the shield took it
    public static bool Takes(Entity e,float amount) {
        if(!MDSettings.CoreShield||e?.FindComponent<ComponentMDUnit>() is not {} u)return false;var s=For(u.Project);var core=s.Core;
        if(core==null||!core.Entity.IsAddedToProject||core.Destroyed||u!=core&&!s.Members.Contains(u))return false;
        float points=amount*Points(u);Spend(core,points);Taken++;TakenPoints+=points;
        var at=MDCombat.Center(u.Body);double now=u.Project.FindSubsystem<SubsystemTime>(true).GameTime;s.FlashAt=at;s.FlashUntil=now+FlashTime;
        if(now-s.LastSpark>.2){s.LastSpark=now;MDTowerWorld.For(u.Project).Sparks(at,Vector3.UnitY,4,new Color(140,220,255),3,.8f,.25f,.04f);}   // fire and crushing hurt every frame
        return true;
    }
    public static string Line(ComponentMDUnit core) {
        var s=For(core.Project);if(s.Core!=core||!MDSettings.CoreShield)return "";
        return $"\n防御罩：{core.ShieldPoints:0} 点 + 核心 {core.Damage.Hitpoints*Points(core):0} 点，罩内 {s.Members.Count} 座炮塔，半径 {s.Radius:0} 米";
    }
    // the dome: three rings and eight arcs, brighter for a moment after a hit (MDTowerWorld.DrawShield)
    public static bool Dome(Project p,out Vector3 centre,out float radius,out float flash) {
        var s=For(p);centre=default;radius=0;flash=0;if(s.Core==null||!s.Core.Entity.IsAddedToProject||s.Core.Destroyed)return false;
        centre=Centre(s.Core);radius=s.Radius;double now=p.FindSubsystem<SubsystemTime>(true).GameTime;flash=s.FlashUntil>now?(float)((s.FlashUntil-now)/FlashTime):0;return true;
    }
}

public sealed partial class MDTowerWorld {
    FlatBatch3D shieldBatch;
    public static int ShieldSegments;   // tests (this frame)
    void DrawShield(Vector3 eye) {
        ShieldSegments=0;if(!MDShield.Dome(project,out var c,out float r,out float flash))return;
        shieldBatch??=SubsystemParticles.PrimitivesRenderer.FlatBatch(3,DepthStencilState.DepthRead,RasterizerState.CullNoneScissor,BlendState.AlphaBlend);
        var col=new Color(120,210,255)*(.28f+.5f*flash);const int n=48;
        void Seg(Vector3 a,Vector3 b) {
            var m=(a+b)*.5f;var w=Vector3.Cross(b-a,m-eye);if(w.LengthSquared()<1e-8f)return;w=Vector3.Normalize(w)*Math.Clamp(.003f*Vector3.Distance(eye,m),.05f,.3f);
            shieldBatch.QueueQuad(a-w,b-w,b+w,a+w,col);ShieldSegments++;
        }
        foreach(float h in new[]{.05f,.45f,.8f}) {   // rings at heights h x r
            float rr=r*MathF.Sqrt(Math.Max(0,1-h*h)),y=c.Y+h*r;
            for(int i=0;i<n;i++){float t0=i*MathF.PI*2/n,t1=(i+1)*MathF.PI*2/n;Seg(new(c.X+MathF.Cos(t0)*rr,y,c.Z+MathF.Sin(t0)*rr),new(c.X+MathF.Cos(t1)*rr,y,c.Z+MathF.Sin(t1)*rr));}
        }
        for(int k=0;k<8;k++) {   // arcs from the ground to the top
            float a=k*MathF.PI/4;var dir=new Vector3(MathF.Cos(a),0,MathF.Sin(a));
            for(int i=0;i<12;i++){float t0=i*MathF.PI/24,t1=(i+1)*MathF.PI/24;Seg(c+dir*MathF.Cos(t0)*r+Vector3.UnitY*MathF.Sin(t0)*r,c+dir*MathF.Cos(t1)*r+Vector3.UnitY*MathF.Sin(t1)*r);}
        }
    }
}
