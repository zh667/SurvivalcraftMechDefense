using Engine;
using GameEntitySystem;

namespace Game;

// 0.15.0 growth (user, 2026-10-30: "按你的建议来，不过这个我记得玩家可以在设置里调整的"): stages from the mod setting 开始天数, one
// every StageDays days; each stage adds MDSettings.Growth % (设置“敌人成长”, default 10) to a robot's health and damage, at most
// MaxStages stages. A robot keeps the factor it was made with (saved). Bosses grow with the defence instead (MDBoss.Spawn).
// The kinds still come by stage as before: tanks and small drones 3 days after the start day, elites 7, repair drones 14.
public static class MDGrowth {
    public const int MaxStages=5,StageDays=7,RepairDay=14;
    public static int Stage(Project p)=>MDSettings.StartDay<=0?0:Math.Clamp((MDWildSpawns.Day(p)-MDSettings.StartDay)/StageDays,0,MaxStages);
    public static float Factor(Project p)=>1+MDSettings.Growth/100f*Stage(p);
}

// 0.15.0 rewards (user, 2026-10-30: "这个提案都可以做，专属部件先不做"; design docs/tasks/2026-10-30-combat-0.15.md E):
// - loot by toughness (tools/prepare_database.py LOOT: drones less scrap and more chips, the tank more);
// - a kill where players and mechs did at least PlayerShare of the damage: half its scrap and experience again;
// - a boss: each armour layer broken drops scrap; killed in its break window, after a weak-point hit or an interrupted main gun:
//   more experience; the first kill of each boss kind in a world: a core, chips and experience on top (the bestiary says 已击败);
// - a night raid beaten (most of it destroyed): scrap and experience by its size at the tower nearest to it.
public static class MDRewards {
    public const float PlayerShare=.3f;
    public const string DefeatedAttribute="MechDefenseBossesDefeated";
    public static readonly HashSet<string> Defeated=[];   // boss kinds killed in this world (Project.xml; one world open at a time)
    public static int Extra;                               // tests: extra items dropped
    public static readonly List<string> Log=[];            // tests
    static int Value(int part)=>Terrain.MakeBlockValue(BlocksManager.GetBlockIndex<MDPartBlock>(),0,part);
    static int Xp=>BlocksManager.GetBlockIndex<ExperienceBlock>();
    static void Drop(Project p,Vector3 at,int value,int count) {
        if(count<=0)return;var pk=p.FindSubsystem<SubsystemPickables>(true);
        for(int i=0;i<count;i++)pk.AddPickable(value,1,at+new Vector3(MDRobots.R(-.6f,.6f),.4f,MDRobots.R(-.6f,.6f)),new Vector3(MDRobots.R(-1.5f,1.5f),MDRobots.R(2,4),MDRobots.R(-1.5f,1.5f)),null);
        Extra+=count;
    }
    // ModLoader ProcessAttackment: who hurt it (players, their herd and mechs vs. everything else)
    public static void Track(Attackment a) {
        if(a.AttackPower<=0||a.Target?.FindComponent<ComponentMDRobot>() is not {} r||r.Health.Health<=0)return;
        float share=a.AttackPower/Math.Max(1,r.Health.AttackResilience);r.DamageShare+=share;
        bool player=a.Attacker?.FindComponent<ComponentMDUnit>() is {} u?u.Mech!=null:a.Attacker!=null&&MDFaction.Friendly(a.Attacker);
        if(player)r.PlayerDamageShare+=share;
    }
    // a robot just destroyed (ComponentMDRobot.Update, once)
    public static void Killed(ComponentMDRobot r) {
        var p=r.Project;var at=MDCombat.Center(r.Body);var k=r.Kind;var notes=new List<string>();
        bool played=r.DamageShare>0&&r.PlayerDamageShare/r.DamageShare>=PlayerShare;
        if(played) {   // half the scrap and experience of its loot again
            var (scrap,xp)=k.Boss?(5,18):k.Elite?(3,3):k.Id=="RD14"?(3,2):k.Fly>0?(1,1):(2,1);
            Drop(p,at,Value(0),scrap);Drop(p,at,Terrain.MakeBlockValue(Xp),xp);notes.Add($"played {r.PlayerDamageShare/r.DamageShare:0.00}: +{scrap} scrap +{xp} xp");
        }
        if(r.Boss is {} b) {
            int bonus=0;
            if(b.Exposed>0){bonus+=8;notes.Add("in its break window");}
            if(b is MDBirdState{EyeHits:>0}||b is MDSiegeState{Interrupts:>0}){bonus+=6;notes.Add("weak point / interrupt");}
            Drop(p,at,Terrain.MakeBlockValue(Xp),bonus);
            if(Defeated.Add(k.Id)) {   // the first of its kind in this world
                Drop(p,at,Value(3),2);Drop(p,at,Value(2),2);Drop(p,at,Value(9),1);Drop(p,at,Terrain.MakeBlockValue(Xp),20);notes.Add("first kill");
                MDBoss.Announce(p,r.Body.Position,$"第一次击败{k.Name}！额外奖励：能量核心、火控芯片、医疗充能核心和经验。",new Color(255,220,120));
            }
        }
        if(notes.Count>0){if(Log.Count>=100)Log.RemoveAt(0);Log.Add($"{k.Id}: "+string.Join(", ",notes));}
    }
    // a boss armour layer broken (MDBossState.Break)
    public static void LayerBroken(ComponentMDRobot r){Drop(r.Project,MDCombat.Center(r.Body),Value(0),3+(int)MDRobots.R(0,2.99f));if(Log.Count<100)Log.Add($"{r.Kind.Id}: layer broken");}
    // a night raid with most of it destroyed (MDRaids)
    public static void RaidBeaten(Project p,Vector3 at,int size) {
        Drop(p,at,Value(0),size);Drop(p,at,Terrain.MakeBlockValue(Xp),size*2);
        MDBoss.Announce(p,at,$"夜袭已击退！炮塔旁掉落了 {size} 个机兵残骸和经验。",new Color(160,255,160));
        if(Log.Count<100)Log.Add($"raid beaten: {size}");
    }
    public static void Load(System.Xml.Linq.XElement project){Defeated.Clear();foreach(var id in ((string)project?.Attribute(DefeatedAttribute)??"").Split(',',StringSplitOptions.RemoveEmptyEntries))Defeated.Add(id);}
    public static void Save(System.Xml.Linq.XElement project){if(project!=null)project.SetAttributeValue(DefeatedAttribute,string.Join(",",Defeated));}
}
