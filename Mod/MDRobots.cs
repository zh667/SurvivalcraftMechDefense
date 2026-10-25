using Engine;
using GameEntitySystem;
using TemplatesDatabase;
using HarmonyLib;
using System.Runtime.CompilerServices;

namespace Game;

// Robot enemies (0.9.0, docs/tasks/2026-10-20-progression-robots.md): the Destroy Gunners walkers and the devil mech, built
// by tools/build_unit_assets.py into Imported/<unit>/ (unit.json + clips.bin + model*.glb, the format of Mercury's assets).
// They are ordinary creatures (template parent AICreature, category LandPredator): vanilla pathfinding, loot, spawning and
// health, so the towers (MDFaction), other mods' weapons and per-hit caps all treat them like any hostile creature. Their own
// behaviour (ComponentMDRobot) goes for the player's defences: Mercury first, then towers, a player last (user, 2026-10-21:
// "机甲>炮塔>玩家"; without towers they leave the player alone unless hit). It walks into range and fires; the fire is
// simulated by MDTowerWorld (Foreign shots). Damage is set separately against creatures (a player has 1.0 health, power /
// resilience) and against structures (towers, Mercury: hit points x armour).
// Where they come from: wild spawns (MDWildSpawns: at night from game day 3, elites from day 10, adjustable in the mod
// settings), night raids on the player's tower clusters (MDRaids) and the devil mech, summoned with the decoy beacon (MDBoss).
// Drops: MDPartBlock materials.
public sealed class MDRobotKind {
    public string Id,Name,Root; public bool Elite,Boss;
    public string Weapon;                         // twin (alternating bolts), missile (homing, lock warning), cannon (ballistic shell), devil (boss)
    public float Sight,Range,MinRange,Interval,ShotGap,ShotSpeed,Gravity,Spread,Splash,Windup,Speed;
    public int Shots;
    public float VsCreature,VsStructure;          // damage per hit against creatures (players: 1.0 health) and against towers / Mercury
    public string[] Muzzles; public int FireSe,HitSe;
    public MDTowerDef Look;
}

public static class MDRobots {
    public const string Root="Imported/";
    static MDTowerDef Look(string id,string type,float[] c,float[] c2,float scale,string kind="heavy")=>new(){id="robot_"+id,name=id,attack=new MDTowerAttack{type=type},fx=new MDTowerFx{color=c,color2=c2,scale=scale,kind=kind},muzzles=[],nodes=[]};
    // Numbers (starting values; raised 2026-10-21, user: "难度需要提高，因为玩家一般会放置很多炮塔"): a normal walker (1000)
    // beats one tier-1 gun tower (it deals 4 x 14 per 1.8 s = 31/s to the tower's 650; the tower 32/s to its 1000) and loses
    // to two; elites (2400 / 2800) take a tier-3 tower to stop. Raids and the boss grow with the defence (MDRaids, MDBoss).
    // Against a player: a bolt 1.5 / 10 resilience = 15 % health before armour, a missile 30 %, a shell 40 %.
    public static readonly MDRobotKind[] Kinds=[
        new(){Id="RD05",Name="双管步行机兵",Root=Root+"RD05/",Weapon="twin",Sight=32,Range=24,Interval=1.8f,Shots=4,ShotGap=.14f,ShotSpeed=70,Spread=.03f,Windup=.25f,
              VsCreature=1.5f,VsStructure=14,Muzzles=["gunL","gunR"],FireSe=8,HitSe=13,Speed=3.4f,Look=Look("bolt","bolt",[1,.45f,.2f],[1,.3f,.15f],.8f)},
        new(){Id="RD06",Name="导弹步行机兵",Root=Root+"RD06/",Elite=true,Weapon="missile",Sight=44,Range=40,MinRange=6,Interval=5,Shots=2,ShotGap=.4f,ShotSpeed=14,Splash=2,Windup=1,
              VsCreature=3f,VsStructure=60,Muzzles=["podL","podR"],FireSe=9,HitSe=14,Speed=3f,Look=Look("missile","shell",[1,.6f,.25f],[1,.4f,.15f],1.1f)},
        new(){Id="RD07",Name="炮击步行机兵",Root=Root+"RD07/",Elite=true,Weapon="cannon",Sight=48,Range=44,MinRange=7,Interval=5.5f,Shots=1,ShotSpeed=30,Gravity=12,Splash=3,Windup=.5f,
              VsCreature=4f,VsStructure=140,Muzzles=["cannonL","cannonR"],FireSe=11,HitSe=24,Speed=2.8f,Look=Look("shell","shell",[1,.5f,.2f],[1,.32f,.12f],1.5f)},
        new(){Id="RD17",Name="恶魔机甲",Root=Root+"RD17/",Boss=true,Weapon="devil",Sight=64,Range=40,MinRange=0,Interval=8,Shots=10,ShotGap=.2f,ShotSpeed=16,Splash=2.5f,Windup=1,
              VsCreature=3f,VsStructure=80,Muzzles=["armL","armR"],FireSe=9,HitSe=14,Speed=7,Look=Look("devil","shell",[1,.3f,.35f],[1,.2f,.25f],1.3f)},
    ];
    public static MDRobotKind Kind(string id)=>Kinds.FirstOrDefault(k=>k.Id==id)??throw new KeyNotFoundException("robot kind "+id);
    // Entity templates (tools/prepare_database.py): one per skin; elites and the boss as below.
    public static readonly string[] Normal=["MDRobot05","MDRobot05B","MDRobot05C"];
    public static readonly string[] Elites=["MDRobot06","MDRobot06B","MDRobot06C","MDRobot07","MDRobot07B","MDRobot07C"];
    public const string Boss="MDDevilMech";
    public const float HuntRange=96;                       // a robot walks to the nearest tower or Mercury this close
    // A robot that a player, the player's herd or a mech hits locks on to it and fights it until it dies, leaves the world or is
    // this far away (user, 2026-10-25: "玩家攻击了就一直攻击玩家，不要一会被炮塔拉仇恨，一会被玩家拉仇恨，拉来拉去的"; was 30 s
    // and only while in sight). Not saved: after loading the world it picks targets afresh.
    public const float LoseRange=HuntRange;
    public const double ChaseTime=300;                     // seconds a raid on a player without towers follows that player
    public static bool IsRobot(Entity e)=>e?.FindComponent<ComponentMDRobot>()!=null;
    // Leaves in reach of a robot's blast or body give way (players, 2026-10-25: "敌对机器人应该可以破坏树叶，不然在生存环境很容易卡在
    // 那里，一直在放导弹，但炸不过去"). Leaves only (any LeavesBlock, other mods' too), no drops; returns the cells cleared.
    public static int BreakLeaves(Project p,Vector3 c,float r) {
        var t=p.FindSubsystem<SubsystemTerrain>(true);int n=0,x0=(int)MathF.Floor(c.X-r),x1=(int)MathF.Floor(c.X+r),z0=(int)MathF.Floor(c.Z-r),z1=(int)MathF.Floor(c.Z+r);
        int y0=Math.Max(1,(int)MathF.Floor(c.Y-r)),y1=Math.Min(254,(int)MathF.Floor(c.Y+r));
        for(int x=x0;x<=x1;x++)for(int z=z0;z<=z1;z++) {
            if(t.Terrain.GetChunkAtCell(x,z)==null)continue;
            for(int y=y0;y<=y1;y++) {
                float dx=x+.5f-c.X,dy=y+.5f-c.Y,dz=z+.5f-c.Z;if(dx*dx+dy*dy+dz*dz>r*r)continue;
                if(BlocksManager.Blocks[Terrain.ExtractContents(t.Terrain.GetCellValueFast(x,y,z))] is not LeavesBlock)continue;
                t.DestroyCell(0,x,y,z,0,true,false);n++;
            }
        }
        return n;
    }
    // the leaves inside a robot's box and half a metre around it (above its feet): it walks or flies through tree crowns
    public static int BreakLeavesAround(ComponentBody b) {
        var t=b.Project.FindSubsystem<SubsystemTerrain>(true);var box=b.BoundingBox;int n=0;
        int x0=(int)MathF.Floor(box.Min.X-.5f),x1=(int)MathF.Floor(box.Max.X+.5f),z0=(int)MathF.Floor(box.Min.Z-.5f),z1=(int)MathF.Floor(box.Max.Z+.5f);
        int y0=Math.Max(1,(int)MathF.Floor(box.Min.Y+.3f)),y1=Math.Min(254,(int)MathF.Floor(box.Max.Y+.5f));
        for(int x=x0;x<=x1;x++)for(int z=z0;z<=z1;z++) {
            if(t.Terrain.GetChunkAtCell(x,z)==null)continue;
            for(int y=y0;y<=y1;y++)if(BlocksManager.Blocks[Terrain.ExtractContents(t.Terrain.GetCellValueFast(x,y,z))] is LeavesBlock){t.DestroyCell(0,x,y,z,0,true,false);n++;}
        }
        return n;
    }
    public static int LeavesBroken;   // tests
    static readonly System.Random random=new();
    public static float R(float a,float b)=>a+(float)random.NextDouble()*(b-a);
    public static T Pick<T>(IReadOnlyList<T> list)=>list[random.Next(list.Count)];
    // Robots fight where the game has hostile creatures: not in Harmless mode; natural spawns and raids not in Creative either
    // (a creative player builds; the beacon still works there).
    public static bool Hostile(Project p)=>p.FindSubsystem<SubsystemGameInfo>(true).WorldSettings.GameMode!=GameMode.Harmless;
    public static bool Spawns(Project p){var m=p.FindSubsystem<SubsystemGameInfo>(true).WorldSettings.GameMode;return m!=GameMode.Harmless&&m!=GameMode.Creative;}
    // a player (not the player's herd or the towers) is never a robot's target in Creative or Harmless
    public static bool FightsPlayers(Project p)=>Spawns(p);
    // ModLoader hook ProcessAttackment: a player, the player's herd or a mech that hurts a robot is fought first (towers are
    // targets anyway). A mech's own weapons hit with the mech as the attacker; a seated pilot counts as the mech (user,
    // 2026-10-25: "我登上机甲去攻击恶魔机甲，恶魔机甲还在攻击炮塔").
    public static void Provoked(Attackment a) {
        if(a.AttackPower<=0||a.Attacker==null||a.Target?.FindComponent<ComponentMDRobot>() is not {} r)return;
        ComponentBody b;
        if(a.Attacker.FindComponent<ComponentMDUnit>() is {} u){if(u.Mech==null)return;b=u.Body;}
        else if(!MDFaction.Friendly(a.Attacker))return;
        else b=MDMechPilot.Mounted(a.Attacker.FindComponent<ComponentPlayer>())?.Body??a.Attacker.FindComponent<ComponentBody>();
        if(b!=null)r.Provoke(b);
    }

    // A robot's hit: structures (towers, Mercury, boats: ComponentDamage without health) take VsStructure, creatures VsCreature.
    public static void Hit(Entity attacker,ComponentBody b,MDRobotKind k,float scale,Vector3 point,Vector3 dir) {
        if(b?.ParentBody?.Entity.FindComponent<ComponentMDUnit>() is {Mech:not null} carrier)b=carrier.Body;   // the pilot sits inside: the mech takes it
        if(b==null||IsRobot(b.Entity)||!MDCombat.Living(b))return;
        bool structure=b.Entity.FindComponent<ComponentHealth>()==null;
        float power=(structure?k.VsStructure:k.VsCreature)*scale;if(power<=0)return;
        ComponentMiner.AttackBody(new MDRobotAttackment(b.Entity,attacker,point,dir,power){Explosive=k.Splash>0});
    }
    public static void Splash(Entity attacker,MDRobotKind k,Vector3 point,float r,ComponentBody skip) {
        var bodies=new DynamicArray<ComponentBody>();attacker.Project.FindSubsystem<SubsystemBodies>(true).FindBodiesAroundPoint(new(point.X,point.Z),r+1.5f,bodies);
        foreach(var b in bodies) {
            if(b==skip||IsRobot(b.Entity))continue;var c=MDCombat.Center(b);float dist=Math.Max(0,Vector3.Distance(c,point)-b.BoxSize.X*.5f);
            if(dist>r)continue;Hit(attacker,b,k,MathUtils.Lerp(.8f,.3f,dist/r),c,Vector3.Normalize(c-point+Vector3.UnitY*.01f));
        }
    }
    public static void Sound(Project p,int se,Vector3 at,float volume)=>p.FindSubsystem<SubsystemAudio>(true).PlaySound(MDMechBrain.Se(se),volume,R(-.05f,.05f),at,20,false);
    // The robots' own pose: the clip's local transforms composed down the joint tree (row form, like MDMechPose without the
    // pilot's aim): Bone[j] is the engine's bone transform T(p) L T(-p), Model[j] takes a rest point of joint j to model space.
    public static MDMechPose Pose(MDMechDef d,MDXf[] local) {
        int n=d.joints.Length;var p=new MDMechPose{Bone=new Matrix[n],Model=new Matrix[n]};
        for(int j=0;j<n;j++) {
            var L=local[j].Row;var pv=d.pivots[j];int pj=d.joints[j].parent;var M=pj>=0?L*p.Model[pj]:L;
            p.Bone[j]=Matrix.CreateTranslation(pv)*L*Matrix.CreateTranslation(-pv);p.Model[j]=M;
        }
        return p;
    }
}

// Not a bullet (the ghouls' bullet rules do not apply): robots hit only the player's side and creatures in their blasts.
public class MDRobotAttackment:Attackment {
    public bool Explosive;   // a missile, shell or blast (复仇女神 takes less: MDMechArmour)
    public MDRobotAttackment(Entity target,Entity attacker,Vector3 hitPoint,Vector3 hitDirection,float attackPower):base(target,attacker,hitPoint,hitDirection,attackPower) {
        ImpulseFactor=1.5f;StunTimeSet=.15f;AllowImpulseAndStunWhenDamageIsZero=false;
    }
}

public class ComponentMDRobot:ComponentBehavior,IUpdateable {
    public MDRobotKind Kind; public MDMechDef Def; public MDMechAnimator Anim;
    public ComponentCreature Creature; public ComponentBody Body; public ComponentHealth Health; ComponentPathfinding path; ComponentSpawn spawn;
    SubsystemTime time; SubsystemBodies bodies; SubsystemTerrain terrain;
    public ComponentBody Target; public Vector3? RaidGoal; public bool Raider;
    public ComponentBody Hunt;                      // the nearest tower or Mercury within HuntRange, walked to while nothing is in sight
    public ComponentBody Provoker;                  // locked on: the player, herd animal or mech that hit it (see LoseRange)
    public ComponentBody Chase; public double ChaseUntil;         // the player a raid without towers comes for (MDRaids.RaidPlayer; not saved)
    public float Cooldown,Windup=-1,Gap; public int ShotsLeft,Barrel,ShotsFired; public bool Exploded;
    float lastHealth=1,hitAge=9,leafCheck; double nextScan,nextHunt; MDMechPose pose;
    public MDBossState Boss;                       // the devil mech only
    public string ForceClip; public float ForceTime; // lab: show one clip at one time, no behaviour
    public UpdateOrder UpdateOrder=>UpdateOrder.Default;
    // Above wandering (0-2) while it has something to fight, a raid to reach or a tower to walk to; else it wanders.
    public override float ImportanceLevel=>Health.Health>0&&(Target!=null||RaidGoal.HasValue||Hunt!=null||Chasing)?50:0;
    public bool Chasing=>Chase!=null&&time.GameTime<ChaseUntil;
    // A hit locks it on to the hitter. The lock stays on the first hitter while that one is valid (a second player's hit does not
    // pull it away), except from a mech nobody drives.
    public void Provoke(ComponentBody b) {
        if(ProvokerNow is {} now&&now!=b&&!(now.Entity.FindComponent<ComponentMDUnit>() is {} m&&MDMechPilot.Driver(m)==null))return;
        Provoker=b;nextScan=0;
    }
    // the locked target, or null once it is gone, invalid or LoseRange away; a player who boards a mech passes the lock to the mech.
    // Every attack aims at it (the devil mech's volley too), seen or not: out of sight the robot walks round to it.
    public ComponentBody ProvokerNow {
        get {
            if(Provoker==null)return null;
            if(MDMechPilot.Mounted(Provoker.Entity.FindComponent<ComponentPlayer>()) is {} mech)Provoker=mech.Body;
            if(!Provoker.Entity.IsAddedToProject||!Valid(Provoker)||Vector3.Distance(Provoker.Position,Body.Position)>MDRobots.LoseRange)Provoker=null;
            return Provoker;
        }
    }
    public override void Load(ValuesDictionary v,IdToEntityMap map) {
        Kind=MDRobots.Kind(v.GetValue<string>("Kind"));Def=MDMechLibrary.Load(Kind.Root,"unit");Anim=new MDMechAnimator(Def);
        Creature=Entity.FindComponent<ComponentCreature>(true);Body=Entity.FindComponent<ComponentBody>(true);Health=Entity.FindComponent<ComponentHealth>(true);path=Entity.FindComponent<ComponentPathfinding>(true);spawn=Entity.FindComponent<ComponentSpawn>();
        time=Project.FindSubsystem<SubsystemTime>(true);bodies=Project.FindSubsystem<SubsystemBodies>(true);terrain=Project.FindSubsystem<SubsystemTerrain>(true);
        Raider=v.GetValue("Raider",false);if(v.GetValue("HasRaidGoal",false))RaidGoal=v.GetValue("RaidGoal",Vector3.Zero);
        Cooldown=MDRobots.R(.5f,1.5f);lastHealth=Health.Health;
        if(Kind.Boss)Boss=new MDBossState(this,v);
    }
    public override void Save(ValuesDictionary v,EntityToIdMap map) {
        v.SetValue("Raider",Raider);v.SetValue("HasRaidGoal",RaidGoal.HasValue);if(RaidGoal.HasValue)v.SetValue("RaidGoal",RaidGoal.Value);Boss?.Save(v);
    }
    public MDMechPose Pose()=>pose??=MDRobots.Pose(Def,Anim.Evaluate());
    public float Yaw=>MathF.Atan2(-Body.Matrix.Forward.X,-Body.Matrix.Forward.Z);
    public Vector3 Point(string muzzle,out Vector3 dir) {
        if(!Def.muzzles.TryGetValue(muzzle,out var m)&&!Def.muzzles.TryGetValue("core",out m)){dir=Body.Matrix.Forward;return Body.Position+Vector3.UnitY*Body.BoxSize.Y*.6f;}
        var pz=Pose();dir=pz.Dir(Def,m,Body.Matrix);return pz.Point(Def,m,Body.Matrix);
    }
    public Vector3 Eye=>Def.muzzles.ContainsKey("eye")?Point("eye",out _):Body.Position+Vector3.UnitY*Body.BoxSize.Y*.85f;

    public void Update(float dt) {
        dt=Math.Min(dt,.1f);pose=null;var w=MDTowerWorld.For(Project);
        if(ForceClip!=null){Anim.PlayBase(ForceClip,1,.01f);Anim.BaseTime=ForceTime;Anim.Fade=1;Anim.Action=null;Anim.Update(0);pose=null;return;}
        if(Health.Health<=0) {
            Target=null;
            if(!Exploded){Exploded=true;MDBossKills.Killed(this);var c=MDCombat.Center(Body);w.Explosion(c,Kind.Boss?4.5f:Kind.Elite?3.5f:2.5f,"heavy",new Color(255,200,120),new Color(255,120,40));MDRobots.Sound(Project,Kind.Boss?20:24,c,1);Boss?.Died();}
            if(Def.Has("death"))Anim.PlayBase("death",1,.1f);else Anim.PlayBase("idle",0,.1f);
            Anim.Update(dt);w.Wreck(MDCombat.Center(Body),Kind.Boss?3:1.4f);return;
        }
        hitAge+=dt;
        if(Health.Health<lastHealth-.03f&&hitAge>1.2f&&Def.Has("hit")&&Anim.ActionDone&&!Kind.Boss){Anim.PlayAction("hit");hitAge=0;}
        lastHealth=Health.Health;Cooldown-=dt;
        if(time.GameTime>=nextHunt){nextHunt=time.GameTime+2+MDRobots.R(0,.5f);Hunt=FindHunt();}
        if(Hunt!=null&&!Valid(Hunt))Hunt=null;
        if(time.GameTime>=nextScan){nextScan=time.GameTime+.5f+MDRobots.R(0,.2f);Target=FindTarget();}
        // The game packs away every creature that may despawn and is over 60 m from all players (SubsystemSpawn, every 2 s)
        // and brings it back later as a fresh copy without its orders. Raids and the devil mech's escorts arrive 40-64 m beyond
        // the towers, often farther from the player: they vanished on arrival (lab, 2026-10-21, user: escorts never attack).
        // Raiders stay while they have something to do; idle, they may be packed away again.
        if(Raider&&spawn!=null){bool busy=Target!=null||Hunt!=null||RaidGoal.HasValue||Chasing;if(spawn.AutoDespawn==busy)spawn.AutoDespawn=!busy;}
        if(Chase!=null&&(!Chasing||!Chase.Entity.IsAddedToProject))Chase=null;
        leafCheck-=dt;if(leafCheck<=0){leafCheck=.3f;MDRobots.LeavesBroken+=MDRobots.BreakLeavesAround(Body);}
        if(Target!=null&&!Valid(Target))Target=null;
        if(Boss!=null)Boss.Update(dt);
        else if(IsActive)Engage(dt);
        else if(ShotsLeft>0||Windup>=0){ShotsLeft=0;Windup=-1;}
        if(Boss==null)BaseClip();
        Anim.Update(dt);pose=null;
    }
    // walk / idle from the body's own movement; the clip's foot speed sets the playback rate (no sliding feet)
    void BaseClip() {
        var v=Body.Velocity;v.Y=0;float sp=v.Length();var f=Body.Matrix.Forward;
        if(sp>.35f) {
            string clip=Vector3.Dot(v,f)<-.3f*sp&&Def.Has("walk_backward")?"walk_backward":Def.Has("walk_forward")?"walk_forward":"idle";
            float foot=Def.Clip(clip).footSpeed is float fs&&fs>.2f?fs:Kind.Speed;Anim.PlayBase(clip,Math.Clamp(sp/foot,.5f,2f),.2f);
        }
        else Anim.PlayBase("idle",1,.3f);
    }
    // What a robot fights (user, 2026-10-21: "敌兵刷新之后应该直接找离自己最近的炮塔攻击（没有炮塔就正常呆着，不要找玩家）……优先级，
    // 机甲>炮塔>玩家"): Mercury and towers that are not wrecked, wherever they are; a player or the player's herd only after it
    // hit this robot (locked on, Provoker), or while the robot is at the player's defences (a tower or Mercury within HuntRange) and
    // the player is within its weapon range. Players never in Harmless; in Creative only the one it is locked on to.
    public bool Valid(ComponentBody b) {
        if(b==null||!b.Entity.IsAddedToProject||!MDCombat.Living(b))return false;
        var e=b.Entity;
        if(e.FindComponent<ComponentMDUnit>() is {} u)return !u.Destroyed&&!u.Legacy;
        if(MDMechPilot.Mounted(e.FindComponent<ComponentPlayer>())!=null)return false;   // seated in a mech: the mech is the target
        bool provoked=b==Provoker;
        if(e.FindComponent<ComponentPlayer>()!=null) {
            if(!MDRobots.Hostile(Project))return false;                    // Harmless: never a player
            if(!MDRobots.FightsPlayers(Project))return provoked;           // Creative: only a player who hit it (user, 2026-10-25: "拉仇恨…创造模式下应该也是这样")
        }
        else if(!(MDFaction.Friendly(e)&&e.FindComponent<ComponentCreature>()!=null&&MDRobots.Hostile(Project)))return false;
        if(provoked)return true;
        if(b==Chase&&Chasing)return true;
        float d=Vector3.Distance(b.Position,Body.Position);
        if(Hunt!=null&&d<=Kind.Range)return true;
        // 0.12.0 (players, 2026-10-25: "建议给他个寻敌范围 靠近一定范围内就会主动攻击玩家"): a player it sees within AggroRange (kept
        // while within 1.5 x that range once it is the target)
        return e.FindComponent<ComponentPlayer>()!=null&&MDSettings.AggroRange>0&&(b==Target?d<=MDSettings.AggroRange*1.5f:d<=MDSettings.AggroRange&&Sight(Eye,b));
    }
    // 0 Mercury, 1 a tower, 2 a player or the player's herd
    public static int Rank(ComponentBody b)=>b.Entity.FindComponent<ComponentMDUnit>() is {} u?(u.Mech!=null?0:1):2;
    // The locked-on hitter first, wherever it is (0.12.0, players 2026-10-25: "旁边有炮台的话 玩家打敌对机器人 机器人也不打玩家 只打炮台去了
    // 应该给玩家拉仇恨"; user: no switching back and forth). Else within sight range, in this order: Mercury in sight, a tower in sight,
    // Mercury or a tower out of sight (it walks around to it), a player or the herd in sight, then out of sight. Nearest first
    // within each; the current target counts 20 % nearer.
    public ComponentBody FindTarget() {
        if(ProvokerNow is {} locked)return locked;
        var list=new DynamicArray<ComponentBody>();var p=Body.Position;bodies.FindBodiesAroundPoint(new(p.X,p.Z),Kind.Sight,list);
        ComponentBody best=null;var bestKey=(int.MaxValue,float.MaxValue);var eye=Eye;
        foreach(var b in list) {
            if(b==Body||!Valid(b))continue;float d=Vector3.Distance(b.Position,p);if(d>Kind.Sight)continue;
            int rank=Rank(b);bool seen=Sight(eye,b);
            var key=(rank<2?(seen?rank:2):seen?3:4,d*(b==Target?.8f:1));
            if(key.CompareTo(bestKey)<0){bestKey=key;best=b;}
        }
        return best;
    }
    // the nearest tower or Mercury within HuntRange (a robot that spawns away from the towers goes there at once)
    public ComponentBody FindHunt() {
        var list=new DynamicArray<ComponentBody>();var p=Body.Position;bodies.FindBodiesAroundPoint(new(p.X,p.Z),MDRobots.HuntRange,list);
        ComponentBody best=null;float bestD=MDRobots.HuntRange;
        foreach(var b in list){if(Rank(b)>1||!Valid(b))continue;float d=Vector3.Distance(b.Position,p);if(d<bestD){bestD=d;best=b;}}
        return best;
    }
    public bool Sight(Vector3 from,ComponentBody b) {
        var to=MDCombat.Center(b);var hit=terrain.Raycast(from,to,false,true,(cv,cd)=>BlocksManager.Blocks[Terrain.ExtractContents(cv)].IsCollidable_(cv));
        return !hit.HasValue||hit.Value.Distance>=Vector3.Distance(from,to)-Math.Max(.6f,b.BoxSize.X*.6f);
    }
    public void Face(Vector3 at,float dt,float rate=2.2f) {
        float want=MathF.Atan2(-(at.X-Body.Position.X),-(at.Z-Body.Position.Z));
        Body.Rotation=Quaternion.CreateFromAxisAngle(Vector3.UnitY,MDCombat.AngleTowards(Yaw,want,rate*dt));
    }
    // Walk into range (or around cover), stop and turn to fire; without a target, to the nearest tower or Mercury (Hunt), or a
    // raider to its raid's goal.
    void Engage(float dt) {
        if(Target!=null) {
            var tc=MDCombat.Center(Target);var flat=new Vector2(tc.X-Body.Position.X,tc.Z-Body.Position.Z);float dist=flat.Length();bool sight=Sight(Eye,Target);
            if(!sight||dist>Kind.Range*.9f)path.SetDestination(Target.Position,1,sight?Kind.Range*.6f:2.5f,300,true,false,true,Target);
            else if(dist<Kind.MinRange){var away=Body.Position-new Vector3(flat.X,0,flat.Y)/Math.Max(dist,.1f)*6;path.SetDestination(away,1,1.5f,100,true,true,false,null);}
            else path.Stop();
            if(sight&&dist<=Kind.Range&&path.Destination==null)Face(tc,dt);
            bool aligned=MathF.Abs(MDCombat.AngleDelta(Yaw,MathF.Atan2(-flat.X,-flat.Y)))<.35f;
            Fire(dt,sight&&dist<=Kind.Range&&dist>=Kind.MinRange*.7f&&aligned);
        }
        else {
            ShotsLeft=0;Windup=-1;
            // the nearest tower or Mercury first (user, 2026-10-21: straight for the nearest tower); the raid's goal only
            // without one (an escort around a devil mech summoned in the open walks to it)
            if(Hunt!=null)path.SetDestination(Hunt.Position,1,Kind.Sight*.5f,300,true,false,true,Hunt);
            else if(Chasing&&Valid(Chase))path.SetDestination(Chase.Position,1,Kind.Range*.6f,300,true,false,true,Chase);
            else if(RaidGoal.HasValue) {
                if(Vector2.Distance(new(RaidGoal.Value.X,RaidGoal.Value.Z),new(Body.Position.X,Body.Position.Z))<8)RaidGoal=null;
                else path.SetDestination(RaidGoal.Value,1,6,300,true,false,false,null);
            }
        }
    }
    // A wind-up (the missile lock is a visible warning on the target), then a burst of Shots, Gap apart, then Interval.
    void Fire(float dt,bool ready) {
        var w=MDTowerWorld.For(Project);
        if(Windup>=0) {
            Windup-=dt;if(Kind.Weapon=="missile"&&Target!=null)w.LockMark(MDCombat.Center(Target),Math.Max(.8f,Target.BoxSize.Y*.7f));
            if(Windup<0){ShotsLeft=Kind.Shots;Gap=0;if(Def.Has("fire"))Anim.PlayAction("fire");}
            return;
        }
        if(ShotsLeft>0){Gap-=dt;if(Gap<=0&&Target!=null){FireOne(Target);ShotsLeft--;Gap=Kind.ShotGap;if(ShotsLeft==0)Cooldown=Kind.Interval;}return;}
        if(ready&&Cooldown<=0&&Target!=null){Windup=Kind.Windup;if(Kind.Weapon=="missile")MDRobots.Sound(Project,7,Body.Position,.8f);}
    }
    public void FireOne(ComponentBody target,string muzzle=null) {
        var w=MDTowerWorld.For(Project);var k=Kind;var from=Point(muzzle??k.Muzzles[Barrel++%k.Muzzles.Length],out var fwd);var tc=MDCombat.Center(target);
        var f=new MDTowerWorld.Foreign{owner=Entity,look=k.Look,pos=from,pass=b=>b==Body||MDRobots.IsRobot(b.Entity)};
        f.landed=(p,d,b)=>Landed(p,d,b);ShotsFired++;
        switch(k.Weapon) {
            case "twin": {
                float t=Vector3.Distance(from,tc)/k.ShotSpeed;var aim=tc+target.Velocity*t;
                var dir=Vector3.Normalize(Vector3.Normalize(aim-from)+new Vector3(MDRobots.R(-1,1),MDRobots.R(-1,1),MDRobots.R(-1,1))*k.Spread);
                f.vel=dir*k.ShotSpeed;f.life=2;w.Muzzle(k.Look,from,dir,false);break;
            }
            case "cannon": {
                var v=Ballistic(from,tc+target.Velocity*(Vector3.Distance(from,tc)/k.ShotSpeed),k.ShotSpeed,k.Gravity);
                f.vel=v;f.gravity=k.Gravity;f.life=8;w.Muzzle(k.Look,from,Vector3.Normalize(v),true);break;
            }
            default: {   // missiles: leave slowly upward and aside along the body's facing (the devil mech's arm muzzles point down at rest), then home with a limited turn rate (they can be outrun)
                var face=Body.Matrix.Forward;var side=Vector3.Normalize(Vector3.Cross(face,Vector3.UnitY));
                var v=Vector3.Normalize(face+Vector3.UnitY*.7f+side*MDRobots.R(-.4f,.4f))*k.ShotSpeed*.6f;
                f.vel=v;f.missile=true;f.target=target;f.homeAfter=.3f;f.accel=24;f.maxSpeed=k.ShotSpeed*2;f.turn=2.2f;f.life=7;w.Muzzle(k.Look,from,Vector3.Normalize(v),false);break;
            }
        }
        w.LaunchForeign(f);MDRobots.Sound(Project,k.FireSe,from,k.Elite||k.Boss?.75f:.5f);
    }
    void Landed(Vector3 p,Vector3 d,ComponentBody b) {
        var w=MDTowerWorld.For(Project);var k=Kind;MDRobots.Sound(Project,k.HitSe,p,.5f);
        MDRobots.LeavesBroken+=MDRobots.BreakLeaves(Project,p+d*.3f,k.Splash>0?Math.Max(1.5f,k.Splash):1.2f);   // bolts clear the leaf they hit and its neighbours
        if(k.Splash<=0){w.Impact(k.Look,p,d,false);MDRobots.Hit(Entity,b,k,1,p,d);return;}
        w.Explosion(p,k.Splash,"heavy",new Color(k.Look.fx.color[0],k.Look.fx.color[1],k.Look.fx.color[2]),new Color(k.Look.fx.color2[0],k.Look.fx.color2[1],k.Look.fx.color2[2]));
        MDRobots.Hit(Entity,b,k,1,p,d);MDRobots.Splash(Entity,k,p,k.Splash,b);
    }
    // the flatter of the two arcs that reach the point at this speed; out of reach: 45 degrees
    static Vector3 Ballistic(Vector3 from,Vector3 to,float v,float g) {
        var flat=new Vector3(to.X-from.X,0,to.Z-from.Z);float x=flat.Length(),y=to.Y-from.Y;var h=x>1e-3f?flat/x:Vector3.UnitX;
        float disc=v*v*v*v-g*(g*x*x+2*y*v*v);float a=disc<0?MathF.PI/4:MathF.Atan((v*v-MathF.Sqrt(disc))/(g*Math.Max(x,.1f)));
        return h*v*MathF.Cos(a)+Vector3.UnitY*v*MathF.Sin(a);
    }
}

// The robots' model: the rigid glTF of tools/build_unit_assets.py, posed from the robot's clips. Two-sided (the original
// shells are open, as with Mercury).
public class ComponentMDRobotModel:ComponentCreatureModel {
    ComponentMDRobot robot; int root=-1; int[] bones;
    public static string LastError;
    public override void Load(ValuesDictionary values,IdToEntityMap map){base.Load(values,map);robot=Entity.FindComponent<ComponentMDRobot>(true);}
    public override void Animate(){base.Animate();if(RenderingMode==ModelRenderingMode.Solid)RenderingMode=ModelRenderingMode.AlphaThreshold;}
    public override void AnimateCreature() {
        try {
            var d=robot.Def;
            if(bones==null){root=Model.RootBone.Index;bones=d.joints.Select(j=>Model.FindBone(j.name,true).Index).ToArray();}
            SetBoneTransform(root,m_componentFrame.Matrix);var p=robot.Pose();
            for(int j=0;j<bones.Length;j++)SetBoneTransform(bones[j],p.Bone[j]);
        } catch(Exception e){if(LastError==null){LastError=e.ToString();Log.Error("[MechDefense] robot model: "+e);}SetBoneTransform(Model.RootBone.Index,m_componentFrame.Matrix);}
    }
}

// ------------------------------------------------------------------------------------------------ spawning
// Wild robots (own spawner since 2026-10-21, user: spawn time and density should be adjustable; the vanilla night spawner
// shared its small limit with wolves and bears and offered nothing to adjust). Mod settings (MDSettings): every SpawnInterval
// seconds, at the times SpawnTime allows (night: the sky is dark), from game day StartDay (0: never; elites 7 days later, a quarter
// of the robots), a group of 1-2.5 x SpawnDensity robots appears 32-56 m from each player on dry open ground, while fewer than
// 4 x SpawnDensity wild robots are within 80 m of that player. Not in Creative or Harmless, not with a static environment,
// not for a player more than 8 m under the surface. Constant-spawn creatures like the vanilla night spawns.
public static class MDWildSpawns {
    sealed class State { public double next=-1; }
    static readonly ConditionalWeakTable<Project,State> states=new();
    public const float Near=80,MinDistance=32,MaxDistance=56;
    public static int Cap=>Math.Max(1,(int)MathF.Round(4*MDSettings.SpawnDensity));
    public static int LastSpawned; public static string LastReason="";   // the last round (tests)
    public static void Update(Project p,float dt) {
        var st=states.GetOrCreateValue(p);double clock=p.FindSubsystem<SubsystemTime>(true).GameTime;
        if(st.next<0||st.next-clock>MDSettings.SpawnInterval)st.next=clock+MDSettings.SpawnInterval;   // one interval after loading; a shorter interval applies at once
        if(clock<st.next)return;st.next=clock+MDSettings.SpawnInterval;
        Spawn(p,new System.Random(),false);
    }
    public static bool Night(Project p)=>p.FindSubsystem<SubsystemSky>(true).SkyLightIntensity<.1f;
    public static int Day(Project p)=>(int)Math.Floor(p.FindSubsystem<SubsystemTimeOfDay>(true).Day)+1;   // the game's day number, 1 on the first day
    // why a spawn round does nothing now ("" when robots may spawn)
    public static string Blocked(Project p) {
        if(MDSettings.StartDay<=0)return "start day 0";
        if(MDSettings.SpawnTime==MDSpawnTime.Off)return "off";
        if(!MDRobots.Spawns(p))return "game mode";
        if(p.FindSubsystem<SubsystemGameInfo>(true).WorldSettings.EnvironmentBehaviorMode!=EnvironmentBehaviorMode.Living)return "static environment";
        if(Day(p)<MDSettings.StartDay)return "before day "+MDSettings.StartDay;
        if(MDSettings.SpawnTime==MDSpawnTime.Night&&!Night(p))return "daytime";
        return "";
    }
    public static List<ComponentMDRobot> Wild(Project p)=>p.Entities.Select(x=>x.FindComponent<ComponentMDRobot>()).Where(r=>r!=null&&!r.Raider&&r.Boss==null&&r.Health.Health>0).ToList();
    // one spawn round, for every player; `lab` skips the time, day and game mode rules (tests); returns the robots added
    public static int Spawn(Project p,System.Random rng,bool lab) {
        LastSpawned=0;LastReason=lab?"":Blocked(p);if(LastReason.Length>0)return 0;
        var terrain=p.FindSubsystem<SubsystemTerrain>(true);bool elites=lab||Day(p)>=MDSettings.StartDay+7;var wild=Wild(p);int sent=0;
        foreach(var pl in p.FindSubsystem<SubsystemPlayers>(true).ComponentPlayers) {
            var at=pl.ComponentBody.Position;int room=Cap-wild.Count(r=>Vector3.Distance(r.Body.Position,at)<Near);
            if(room<=0){LastReason="enough robots near the player";continue;}
            if(at.Y<terrain.Terrain.GetTopHeight(Terrain.ToCell(at.X),Terrain.ToCell(at.Z))-8){LastReason="player underground";continue;}
            int group=Math.Clamp((int)MathF.Round((1+(float)rng.NextDouble()*1.5f)*MDSettings.SpawnDensity),1,room);
            var centre=MDRaids.Ground(terrain,at,(float)(rng.NextDouble()*Math.PI*2),MinDistance+(float)rng.NextDouble()*(MaxDistance-MinDistance),rng);
            if(centre==null){LastReason="no open ground";continue;}
            for(int i=0;i<group;i++) {
                var spot=i==0?centre:MDRaids.Ground(terrain,centre.Value,(float)(rng.NextDouble()*Math.PI*2),3+(float)rng.NextDouble()*3,rng);if(spot==null)continue;
                var e=DatabaseManager.CreateEntity(p,elites&&rng.NextDouble()<.25?MDRobots.Pick(MDRobots.Elites):MDRobots.Pick(MDRobots.Normal),true);
                var b=e.FindComponent<ComponentBody>(true);b.Position=spot.Value;b.Rotation=Quaternion.CreateFromAxisAngle(Vector3.UnitY,(float)(rng.NextDouble()*Math.PI*2));
                e.FindComponent<ComponentCreature>(true).ConstantSpawn=true;p.AddEntity(e);sent++;
            }
        }
        LastSpawned=sent;return sent;
    }
}
[HarmonyPatch(typeof(SubsystemCreatureSpawn),nameof(SubsystemCreatureSpawn.Update))]
public static class MDRobotSpawnTick {public static void Postfix(SubsystemCreatureSpawn __instance,float dt){MDWildSpawns.Update(__instance.Project,dt);MDRaids.Update(__instance.Project,dt);MDBossKills.Tick(__instance.Project);MDBoss.Tick(__instance.Project,dt);}}

// Night raids: shortly after nightfall (from game day StartDay, on RaidChance % of the nights, decided per day and world; mod
// settings, default day 3 and 75 %) a group of robots comes for the strongest cluster of the player's towers near a player
// (within 30 m of each other), from 48-64 m away. The group grows with the defence (user, 2026-10-21: players place many
// towers): defence = tier-1 towers 1, tier-2 2, tier-3 3, a Mercury 4; size 3 + defence / 2 + day / 8 (3-20), times RaidSize
// (at most 40); elites from 3 days after StartDay a quarter, from 7 days after a third, half when the cluster has a tier-3
// tower; a defence of 20 or more gets a second wave of half the size 60 s later.
// 0.12.0 (players via the user, 2026-10-25: "希望能增加机器人定时攻城。增加一些生存压力"; user: without towers they come for the
// player): a night without towers near any player sends 3 + day / 8 robots (3-20, times RaidSize) at a player on the surface,
// and they follow that player for ChaseTime.
public static class MDRaids {
    sealed class Wave { public double at; public Vector3 centre; public int size,elites; public float dir; }
    sealed class State { public float lastTime=-1; public readonly List<Wave> waves=[]; }
    static readonly ConditionalWeakTable<Project,State> states=new();
    static readonly HashSet<int> Tier2=[3,6,14,16,18,19],Tier3=[4,7,8,13,15,17];
    public static int Weight(ComponentMDUnit u)=>u.Mech!=null?u.Mech.Type.Weight:Tier3.Contains(u.Tower.data)?3:Tier2.Contains(u.Tower.data)?2:1;
    public static int LastRaid,LastPlanned,LastDefence,LastElites,LastSecondWave,SecondWavesSent;   // the last raid (tests; planned = before ground failures)
    public static ComponentPlayer LastPlayer;   // the last raid's player when no towers were near (tests)
    public static void Update(Project p,float dt) {
        var st=states.GetOrCreateValue(p);var tod=p.FindSubsystem<SubsystemTimeOfDay>(true);float now=tod.TimeOfDay;
        var clock=p.FindSubsystem<SubsystemTime>(true).GameTime;
        for(int i=st.waves.Count-1;i>=0;i--)if(clock>=st.waves[i].at){var w=st.waves[i];st.waves.RemoveAt(i);Send(p,w.centre,w.size,w.elites,w.dir,new System.Random(),"第二波：");}
        float at=IntervalUtils.Add(tod.NightStart,tod.NightInterval*.15f);bool crossed=st.lastTime>=0&&IntervalUtils.Interval(st.lastTime,now)<.5f&&IntervalUtils.Interval(st.lastTime,at)<IntervalUtils.Interval(st.lastTime,now);
        st.lastTime=now;
        if(!crossed||!MDRobots.Spawns(p)||p.FindSubsystem<SubsystemGameInfo>(true).WorldSettings.EnvironmentBehaviorMode!=EnvironmentBehaviorMode.Living)return;
        int day=(int)Math.Floor(tod.Day);if(MDSettings.StartDay<=0||day+1<MDSettings.StartDay)return;   // start day 0: no raids
        var rng=new System.Random(p.FindSubsystem<SubsystemGameInfo>(true).WorldSeed*31+day*7919);if(rng.NextDouble()*100>=MDSettings.RaidChance)return;
        Raid(p,day,rng);
    }
    // Starts a raid now on the strongest tower cluster near a player, without towers on a player; returns the robots sent.
    public static int Raid(Project p,int day,System.Random rng) {
        var units=p.Entities.Select(e=>e.FindComponent<ComponentMDUnit>()).Where(u=>u!=null&&!u.Legacy&&!u.Destroyed).ToList();
        var players=p.FindSubsystem<SubsystemPlayers>(true).ComponentPlayers;LastPlayer=null;
        var near=units.Where(t=>t.Tower!=null&&players.Any(pl=>Vector3.Distance(pl.ComponentBody.Position,t.Body.Position)<80)).ToList();if(near.Count==0)return RaidPlayer(p,day,rng);
        int Score(ComponentMDUnit a)=>near.Where(o=>Vector3.Distance(o.Body.Position,a.Body.Position)<30).Sum(Weight);
        var anchor=near.OrderByDescending(Score).First();
        var cluster=near.Where(o=>Vector3.Distance(o.Body.Position,anchor.Body.Position)<30).ToList();
        var centre=cluster.Aggregate(Vector3.Zero,(a,t)=>a+t.Body.Position)/cluster.Count;
        int defence=cluster.Sum(Weight)+units.Where(u=>u.Mech!=null&&Vector3.Distance(u.Body.Position,centre)<30).Sum(Weight);
        int size=Math.Clamp((int)MathF.Round(Math.Clamp(3+(int)MathF.Round(defence*.5f)+day/8,3,20)*MDSettings.RaidSize),2,40);
        float share=day+1>=MDSettings.StartDay+7?(cluster.Any(t=>Tier3.Contains(t.Tower.data))?.5f:1/3f):day+1>=MDSettings.StartDay+3?.25f:0;
        int elites=(int)MathF.Round(size*share);float dir=(float)(rng.NextDouble()*Math.PI*2);
        LastDefence=defence;LastElites=elites;LastSecondWave=0;LastPlanned=size;
        int sent=Send(p,centre,size,elites,dir,rng,"");
        if(defence>=20&&sent>0){var st=states.GetOrCreateValue(p);LastSecondWave=size/2;st.waves.Add(new Wave{at=p.FindSubsystem<SubsystemTime>(true).GameTime+60,centre=centre,size=size/2,elites=(int)MathF.Round(size/2*share),dir=dir+MathF.PI*.5f});}
        return sent;
    }
    // no towers near any player: a group for a player on the surface (not in a cave: robots cannot reach one)
    static int RaidPlayer(Project p,int day,System.Random rng) {
        var terrain=p.FindSubsystem<SubsystemTerrain>(true);
        var open=p.FindSubsystem<SubsystemPlayers>(true).ComponentPlayers.Where(pl=>pl.ComponentHealth.Health>0&&pl.ComponentBody.Position.Y>=terrain.Terrain.GetTopHeight(Terrain.ToCell(pl.ComponentBody.Position.X),Terrain.ToCell(pl.ComponentBody.Position.Z))-8).ToList();
        if(open.Count==0)return 0;
        var target=open[rng.Next(open.Count)];LastPlayer=target;
        int size=Math.Clamp((int)MathF.Round(Math.Clamp(3+day/8,3,20)*MDSettings.RaidSize),2,40);
        float share=day+1>=MDSettings.StartDay+7?1/3f:day+1>=MDSettings.StartDay+3?.25f:0;
        int elites=(int)MathF.Round(size*share);LastDefence=0;LastElites=elites;LastSecondWave=0;LastPlanned=size;
        return Send(p,target.ComponentBody.Position,size,elites,(float)(rng.NextDouble()*Math.PI*2),rng,"",target);
    }
    static int Send(Project p,Vector3 centre,int size,int elites,float dir,System.Random rng,string prefix,ComponentPlayer chase=null) {
        var terrain=p.FindSubsystem<SubsystemTerrain>(true);int sent=0;double now=p.FindSubsystem<SubsystemTime>(true).GameTime;
        for(int i=0;i<size;i++) {
            var at=Ground(terrain,centre,dir+(float)(rng.NextDouble()-.5)*.9f,48+(float)rng.NextDouble()*16,rng);if(at==null)continue;
            var e=DatabaseManager.CreateEntity(p,i<elites?MDRobots.Pick(MDRobots.Elites):MDRobots.Pick(MDRobots.Normal),true);
            e.FindComponent<ComponentBody>(true).Position=at.Value;var r=e.FindComponent<ComponentMDRobot>(true);r.Raider=true;r.RaidGoal=centre;e.FindComponent<ComponentSpawn>(true).AutoDespawn=false;   // see ComponentMDRobot.Update
            if(chase!=null){r.Chase=chase.ComponentBody;r.ChaseUntil=now+MDRobots.ChaseTime;}
            p.AddEntity(e);sent++;
        }
        LastRaid=sent;if(prefix.Length>0)SecondWavesSent+=sent;
        if(sent>0)foreach(var pl in p.FindSubsystem<SubsystemPlayers>(true).ComponentPlayers)if(Vector3.Distance(pl.ComponentBody.Position,centre)<120)
            pl.ComponentGui.DisplaySmallMessage(chase!=null?(pl==chase?$"警报：{sent} 台机兵正朝你逼近（约 {Math.Round(Vector3.Distance(pl.ComponentBody.Position,centre)+55)} 米外）！附近没有炮塔，小心应战。":$"警报：{sent} 台机兵正朝附近的玩家逼近。")
                :$"警报：{prefix}{sent} 台机兵正在逼近你的炮塔阵地（约 {Math.Round(Vector3.Distance(pl.ComponentBody.Position,centre)+55)} 米外）。",new Color(255,120,100),true,true);
        return sent;
    }
    // a dry surface cell around `centre` at `dist`, in loaded terrain, with room for a 4 m robot: near `angle` first, then from
    // any side (a second wave aimed over the sea found nothing at all in the lab, 2026-10-21)
    public static Vector3? Ground(SubsystemTerrain t,Vector3 centre,float angle,float dist,System.Random rng) {
        for(int k=0;k<24;k++) {
            float a=k==0?angle:k<10?angle+(float)(rng.NextDouble()-.5)*1.2f:(float)(rng.NextDouble()*Math.PI*2),d=dist+(k==0?0:(float)(rng.NextDouble()-.5)*10);
            int x=(int)MathF.Floor(centre.X-MathF.Sin(a)*d),z=(int)MathF.Floor(centre.Z-MathF.Cos(a)*d);
            if(t.Terrain.GetChunkAtCell(x,z) is not {State:>=TerrainChunkState.InvalidVertices1})continue;
            int y=t.Terrain.GetTopHeight(x,z);if(y<=1||y>=250)continue;
            var top=BlocksManager.Blocks[Terrain.ExtractContents(t.Terrain.GetCellValueFast(x,y,z))];
            if(top is FluidBlock or LeavesBlock||!top.IsCollidable_(t.Terrain.GetCellValueFast(x,y,z)))continue;   // not on a tree top
            bool clear=true;for(int h=1;h<=4&&clear;h++)clear=!BlocksManager.Blocks[Terrain.ExtractContents(t.Terrain.GetCellValueFast(x,y+h,z))].IsCollidable_(t.Terrain.GetCellValueFast(x,y+h,z));
            if(clear)return new Vector3(x+.5f,y+1.05f,z+.5f);
        }
        return null;
    }
}
