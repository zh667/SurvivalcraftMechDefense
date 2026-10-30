using Engine;
using System.Reflection;
using System.Runtime.CompilerServices;
using GameEntitySystem;

namespace Game;

// Friend or foe for the towers. Read from each creature's own behaviour, so creatures of other mods count without a list
// of names: Wonderful Era's pirates chase with their own ComponentNewChaseBehavior, the ghouls are category Bird, and
// ScCsgoTactical's enemy soldiers aim through a TargetBody field.
// Never a target: players, towers, the player's herd (HerdName "player": hired pirates, companions) and a player's mount.
// A target: a creature that hurt a player, a tower or the player's herd in the last 30 s; one whose chase (or attack)
// target is a player or the player's herd; a land or water predator; one whose chase behaviour hunts players on sight (chase range
// above 4 m and, where the behaviour has one, a chase-player probability above 0). The 4 m keeps bulls, bisons and boars
// (1.5-4 m, they only butt what stands next to them) out until they actually attack.
// 0.13.0 (players via the user, 2026-10-29: "这炮塔不攻击黑蝎枪械模组的黑蝎恐怖分子"): NPCs of other mods that fight through an attack
// target object of their own (not a chase target) and start out neutral were never seen as hostile. Now also a target: a
// creature in a herd named in HostileHerds ("Terrorist"), and any creature holding an attack target object (a member whose type
// has a TargetBody, with Purpose Attack if it has one) that points at the player's side; one that only follows a player is not.
public static class MDFaction {
    const float Memory=30,HuntRange=4,Recheck=.5f;
    sealed class Probe { public PropertyInfo herd; public FieldInfo herdField,day,night,playerChance,chaseTarget,attackTarget; public List<(FieldInfo field,MemberInfo body,MemberInfo purpose)> aiTargets=[]; }
    public static readonly string[] HostileHerds=["Terrorist"];
    static readonly Dictionary<Type,Probe> probes=[];
    // Which fields name an attack target. m_target only in a chase behaviour (the type also has day/night chase ranges:
    // vanilla ComponentChaseBehavior, Wonderful Era's clone): stare, avoid, find-player and move-away behaviours keep the
    // player in m_target too. TargetBody: ScCsgoTactical's enemy soldier. Exact names: a companion's lastVoiceTarget is
    // the player it talks to.
    sealed class Memo { public double aggressiveUntil=double.MinValue,checkedAt=double.MinValue; public bool hostile; }
    static readonly ConditionalWeakTable<Entity,Memo> memos=new();

    static Probe ProbeOf(Type type) {
        if(probes.TryGetValue(type,out var p))return p;
        p=new Probe();const BindingFlags F=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.DeclaredOnly;
        for(var t=type;t!=null&&t!=typeof(Component);t=t.BaseType)
            foreach(var f in t.GetFields(F)) {
                var n=f.Name.ToLowerInvariant();
                bool refType=typeof(ComponentCreature).IsAssignableFrom(f.FieldType)||typeof(ComponentBody).IsAssignableFrom(f.FieldType)||f.FieldType==typeof(Entity);
                if(refType&&n=="m_target")p.chaseTarget??=f;
                else if(refType&&n is "targetbody" or "m_targetbody")p.attackTarget??=f;
                else if(f.FieldType==typeof(float))switch(n){case "m_daychaserange":p.day??=f;break;case "m_nightchaserange":p.night??=f;break;case "m_chaseplayerprobability":p.playerChance??=f;break;}
                else if(f.FieldType==typeof(string)&&n is "herdname" or "m_herdname")p.herdField??=f;
                else if(!f.FieldType.IsPrimitive&&f.FieldType!=typeof(string)&&TargetBodyOf(f.FieldType) is {} tb)p.aiTargets.Add((f,tb,(MemberInfo)f.FieldType.GetField("Purpose")??f.FieldType.GetProperty("Purpose")));
            }
        if(p.day==null||p.night==null)p.chaseTarget=null;
        var hp=type.GetProperty("HerdName",BindingFlags.Instance|BindingFlags.Public);if(hp?.PropertyType==typeof(string)&&hp.GetIndexParameters().Length==0)p.herd=hp;
        probes[type]=p;return p;
    }
    static Entity EntityOf(object o)=>o switch{Component c=>c.Entity,Entity e=>e,_=>null};
    // a public TargetBody field or property of a ComponentBody type (another mod's attack target object)
    static MemberInfo TargetBodyOf(Type t) {
        if(t.GetField("TargetBody") is {} f&&typeof(ComponentBody).IsAssignableFrom(f.FieldType))return f;
        if(t.GetProperty("TargetBody") is {} p&&typeof(ComponentBody).IsAssignableFrom(p.PropertyType)&&p.GetIndexParameters().Length==0)return p;
        return null;
    }
    static object Get(MemberInfo m,object o)=>m switch{FieldInfo f=>f.GetValue(o),PropertyInfo p=>p.GetValue(o),_=>null};
    public static string HerdOf(Entity e) {
        foreach(var c in e.Components) {
            var p=ProbeOf(c.GetType());if(p.herd==null&&p.herdField==null)continue;
            if((p.herd!=null?p.herd.GetValue(c):p.herdField.GetValue(c)) is string name&&name.Length>0)return name;
        }
        return null;
    }

    public static bool InPlayerHerd(Entity e) {
        foreach(var c in e.Components) {
            var p=ProbeOf(c.GetType());if(p.herd==null&&p.herdField==null)continue;
            var name=p.herd!=null?p.herd.GetValue(c) as string:p.herdField.GetValue(c) as string;
            if(string.Equals(name,"player",StringComparison.OrdinalIgnoreCase))return true;
        }
        return false;
    }
    // The player's side: players, towers and the player's herd. Attacking one of these makes a creature an aggressor.
    public static bool PlayerSide(Entity e)=>e!=null&&(e.FindComponent<ComponentPlayer>()!=null||e.FindComponent<ComponentMDUnit>()!=null||InPlayerHerd(e));
    public static bool Friendly(Entity e)=>PlayerSide(e)||e.FindComponent<ComponentMount>()?.Rider?.Entity.FindComponent<ComponentPlayer>()!=null;

    // Called for every attack (ModLoader hook "ProcessAttackment"): remember creatures that hurt the player's side.
    public static void Attacked(Attackment a) {
        if(a.Attacker==null||a.Target==null||a.AttackPower<=0||!PlayerSide(a.Target))return;
        if(a.Attacker.FindComponent<ComponentCreature>()==null||Friendly(a.Attacker))return;
        var time=a.Target.Project?.FindSubsystem<SubsystemTime>();if(time==null)return;
        var m=memos.GetOrCreateValue(a.Attacker);m.aggressiveUntil=time.GameTime+Memory;m.checkedAt=double.MinValue;
    }

    public static bool Hostile(ComponentCreature creature,double now) {
        var e=creature.Entity;var m=memos.GetOrCreateValue(e);
        if(now-m.checkedAt<Recheck&&now>=m.checkedAt)return m.hostile;
        m.checkedAt=now;return m.hostile=!Friendly(e)&&(m.aggressiveUntil>now||Predator(creature)||HostileHerd(e)||Targeting(e)||Hunts(e));
    }
    // the same rules without the 0.5 s memo (tests)
    public static bool HostileNow(ComponentCreature creature,double now)=>!Friendly(creature.Entity)&&(memos.GetOrCreateValue(creature.Entity).aggressiveUntil>now||Predator(creature)||HostileHerd(creature.Entity)||Targeting(creature.Entity)||Hunts(creature.Entity));
    static bool Predator(ComponentCreature c)=>c.Category.HasFlag(CreatureCategory.LandPredator)||c.Category.HasFlag(CreatureCategory.WaterPredator);
    static bool HostileHerd(Entity e)=>HerdOf(e) is {} h&&HostileHerds.Any(x=>string.Equals(x,h,StringComparison.OrdinalIgnoreCase));
    static bool Targeting(Entity e) {
        bool At(FieldInfo f,Component c)=>f!=null&&EntityOf(f.GetValue(c)) is {} t&&t!=e&&PlayerSide(t);
        foreach(var c in e.Components) {
            var p=ProbeOf(c.GetType());if(At(p.chaseTarget,c)||At(p.attackTarget,c))return true;
            foreach(var (field,body,purpose) in p.aiTargets) {
                var target=field.GetValue(c);if(target==null)continue;
                if(purpose!=null&&Get(purpose,target)?.ToString()!="Attack")continue;   // following or escorting the player is not hostile
                if(EntityOf(Get(body,target)) is {} t&&t!=e&&PlayerSide(t))return true;
            }
        }
        return false;
    }
    static bool Hunts(Entity e) {
        foreach(var c in e.Components) {
            var p=ProbeOf(c.GetType());if(p.day==null||p.night==null)continue;
            if(Math.Max((float)p.day.GetValue(c),(float)p.night.GetValue(c))>HuntRange&&(p.playerChance==null||(float)p.playerChance.GetValue(c)>0))return true;
        }
        return false;
    }
}

// Damage of explosions, beams, lightning, flames, jets and poison: not a bullet. Mods that treat ProjectileAttackment as
// gunfire (the ghouls' ComponentBulletDefender cuts body hits to 0.1-0.5) leave it alone; per-hit caps and boss rules that
// apply to every injury still apply. Impulse and stun as for a projectile.
public class MDTowerAttackment:Attackment {
    public string Channel;   // kinetic / explosion / energy (MDTowerBrain.Hit), read by the devil mech's weak channel
    public MDTowerAttackment(Entity target,Entity attacker,Vector3 hitPoint,Vector3 hitDirection,float attackPower):base(target,attacker,hitPoint,hitDirection,attackPower) {
        ImpulseFactor=2;StunTimeSet=.2f;AllowImpulseAndStunWhenDamageIsZero=false;
    }
}
