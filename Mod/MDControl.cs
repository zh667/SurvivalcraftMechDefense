using Engine;

namespace Game;

// 0.18.0 control resistance for the bosses (players via the user, 2026-11-04: "恶魔机甲和首领级敌人很容易因为元素反应一直原地罚站。给它们加
// 控制抗性，比如控制时长按等级递减，或者被控一次后有一段免疫期"). What held a boss before: the slows (frost jets x0.5-0.7, liquid nitrogen
// and poison fog shells x0.25, the reaction 冻蚀 x0.4 for 4 s, ice and poison zones), renewed by every tick, so among a few frost or poison
// towers a boss walked at a quarter of its speed nearly all the time (lab fb18 control: the numbers in docs/COMBAT_DESIGN.md §11);
// and the staggers (an armour layer broken 3 s, off balance 4 s, RD19's main gun broken off 2.5 s), in which a boss stands. Stun and
// freeze never reached a boss (MDTowerBrain.Holdable, the reactions check Kind.Boss) and still do not.
// Rules, bosses only (normal robots and elites keep the 0.15.0 rule, user 2026-10-30: "控制要停火，被控效果不用递减"):
// - a slow on a boss is half as strong: x0.25 -> x0.625, x0.5 -> x0.75 (SlowShare);
// - control comes in episodes per kind (Soft: slows; Hard: stun, freeze, staggers). An episode lasts at most SoftCap s (a slow) or what
//   it asks, at most HardCap s (a stagger); renewing it while it runs extends it up to that. Each further episode is half as long
//   (diminishing returns: 4, 2, 1, 0.5 s; shorter than MinEpisode is resisted) until Window s pass without one; after each episode the
//   boss is immune to that kind for SoftImmune / HardImmune s.
// - the damage windows stay whole: a broken layer's 10 s and off balance's 8 s at x1.5 (首领限伤用装甲阶段), only the standing still is cut.
// MDTowerWorld.Slow / Stun / Freeze and MDBossState.StaggerFor go through Control. Lab switch TenacityEnabled shows the 0.17.0 numbers.
// 0.18.3 every other target too (user, 2026-11-05: "电磁脉冲、声波塔 现在几乎能一直控制敌人，挤压了其他控制塔"; the 10-30 rule "被控效果不用
// 递减" is replaced for hard control): until 0.18.2 a normal robot took every stun and freeze in full, each from any tower, so two EMP III
// (2.5 s every 5 s) or an EMP with the sonic tower held it all the time, and a freeze from the liquid nitrogen tower or a frost slow
// landed on a robot that stood stunned anyway (lab fb183-control: the numbers in docs/COMBAT_DESIGN.md §12). Now all hard control of a
// normal target (any tower's stun, the liquid nitrogen freeze, the reactions' paralysis and freeze) is one episode at a time, from
// whichever tower: it lasts what the first control asks, further hard control while it runs extends it to at most NormalHardCap s
// from its start, and after it the target cannot be held for NormalHardImmune s. So nothing holds it more than 3 s of every 5 s
// (60 %); one EMP III alone holds it 2.5 s of 5 (50 %), as before. Slows are not part of it (no limit on normal targets, as before):
// the frost and poison towers' slows count in full between the episodes. The control towers (attack.stun / freeze) aim first at a
// target that is neither held nor immune (MDTowerBrain.FindTarget), so in a squad they spread instead of renewing the same robot.
// Bosses keep their own rules above (tower stun and freeze still never hold them). Lab switch SharedHardEnabled shows 0.18.2.
public enum MDControlKind{Soft,Hard}
public sealed partial class MDTowerWorld {
    public static bool TenacityEnabled=true,SharedHardEnabled=true;
    public const float SlowShare=.5f,SoftCap=4,HardCap=4,Window=20,SoftImmune=5,HardImmune=6,MinEpisode=.4f;
    public const float NormalHardCap=3,NormalHardImmune=2;   // 0.18.3 every target that is not a boss
    sealed class Episode { public float start=-99,end=-99,cap=-99; public int count; }
    sealed class Tenacity { public readonly Episode[] e=[new(),new()]; }
    readonly Dictionary<ComponentBody,Tenacity> tenacity=[];
    public static int Resisted,ResistedNormal; public static float GrantedSoft,GrantedHard,GrantedNormal;   // tests
    public static bool Tenacious(ComponentBody b)=>b?.Entity.FindComponent<ComponentMDRobot>()?.Boss!=null;
    // the seconds of control of this kind a body takes from a request for `seconds` (0: resisted); a non-boss takes every slow in full
    // and its hard control through NormalHard (0.18.3)
    public float Control(ComponentBody b,MDControlKind kind,float seconds) {
        if(!TenacityEnabled||seconds<=0)return seconds;
        if(!Tenacious(b))return kind==MDControlKind.Hard&&SharedHardEnabled?NormalHard(b,seconds):seconds;
        var e=TenacityOf(b).e[(int)kind];bool soft=kind==MDControlKind.Soft;float got;
        if(time<e.end){float until=Math.Min(time+seconds,e.cap);if(until>e.end){if(soft)GrantedSoft+=until-e.end;else GrantedHard+=until-e.end;e.end=until;}return e.end-time;}
        if(e.count>0&&time<e.end+(soft?SoftImmune:HardImmune)){Resisted++;return 0;}
        if(time-e.end>Window)e.count=0;
        float allowed=(soft?SoftCap:Math.Min(seconds,HardCap))*MathF.Pow(.5f,e.count);
        if(allowed<MinEpisode){Resisted++;return 0;}
        e.count++;e.start=time;e.cap=time+allowed;e.end=Math.Min(time+seconds,e.cap);got=e.end-time;
        if(soft)GrantedSoft+=got;else GrantedHard+=got;
        return got;
    }
    // a boss's stagger (MDBossState): the seconds it gets now
    public float StaggerFor(ComponentBody b,float seconds)=>Control(b,MDControlKind.Hard,seconds);
    Tenacity TenacityOf(ComponentBody b) {
        if(!tenacity.TryGetValue(b,out var t)) {
            if(tenacity.Count>48)foreach(var k in tenacity.Keys.Where(x=>!x.Entity.IsAddedToProject||!MDCombat.Living(x)).ToList())tenacity.Remove(k);
            tenacity[b]=t=new();
        }
        return t;
    }
    // 0.18.3 hard control of a target that is not a boss: one episode at a time, at most NormalHardCap s, then NormalHardImmune s immune
    float NormalHard(ComponentBody b,float seconds) {
        var e=TenacityOf(b).e[(int)MDControlKind.Hard];
        if(time<e.end){float until=Math.Min(time+seconds,e.cap);if(until>e.end){GrantedNormal+=until-e.end;e.end=until;}return e.end-time;}
        if(e.count>0&&time<e.end+NormalHardImmune){ResistedNormal++;return 0;}
        e.count++;e.start=time;e.cap=time+NormalHardCap;e.end=Math.Min(time+seconds,e.cap);GrantedNormal+=e.end-time;
        return e.end-time;
    }
    // held now or immune to it: hard control would be wasted on this target (the control towers pick another one first)
    public bool HardResisting(ComponentBody b) {
        if(!SharedHardEnabled||!TenacityEnabled||Tenacious(b)||!tenacity.TryGetValue(b,out var t))return false;
        var e=t.e[(int)MDControlKind.Hard];return e.count>0&&time<e.end+NormalHardImmune;
    }
}
