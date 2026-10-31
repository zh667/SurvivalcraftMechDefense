using Engine;
using GameEntitySystem;
using TemplatesDatabase;
using System.Runtime.CompilerServices;

namespace Game;

// The bosses (0.14.0: five, one of them at random each time): the devil mech (Destroy Gunners RD17, since 0.9.0) and four of our own models
// (E:/projects/dguns-enemy-models/v3_new): 鸟形重装机甲 RD13 (walks), 双光剑飞行机甲 RD21 and 重装悬浮炮艇 RD08 (fly), 重型装甲炮车
// RD19 (an emplacement that rises out of its base). A player calls the next one with the decoy beacon (MDPartBlock), or the kill
// count brings it (MDBossKills): a 30 s countdown, then it arrives and fights around that spot. One at a time per world. All drop
// the mech blueprint.
// Each one sets the player a different problem and gives the player an answer the towers do not have (user, 2026-10-30: "用户更在意的
// 是敌人和玩家之间的对抗和交互"): its attacks are telegraphed (a crouch, a red ring on the ground where shells will land, a red line
// along a dash, an aiming beam) so a player who moves dodges them, and each has a weak spot or a window only a player or a mech can
// use (MDBossState subclasses below).
// Strength grows with the defence it meets (user, 2026-10-21: players place many towers): armour 30000 + 3000 / 6000 / 9000 per
// tier-1 / 2 / 3 tower and 6000-10000 per mech within 48 m of the beacon, at most 300000, times the boss's own factor
// (MDRobotKind.BossArmour); at 70 % and 35 % health it calls an escort of robots (2 + towers / 5, at most 8).
// Armour phases (user, 2026-10-30, replacing the 0.12.0 2 % per-hit cap: "首领限伤改装甲阶段"): two layers of plating, each soaking
// 10 % of the armour; while one holds, 20 % of every hit reaches the boss. Breaking one staggers it for 3 s and opens a 10 s window
// in which it takes 150 %, so heavy guns and mechs saved for that window count. A single hit takes at most 8 % (other mods' one-shot
// weapons).
public abstract class MDBossState {
    public const int Layers=2; public const float PlateShare=.1f,Through=.2f,ExposedTime=10,ExposedFactor=1.5f,StaggerTime=3;
    protected readonly ComponentMDRobot r;
    public float Armour; public int Escorts,EscortSize; public Vector3? Home;   // armour set at arrival (saved: the template's is the base), escorts called (bits 1, 2), their size
    public int LayersLeft=Layers; public float Plate,Exposed,Stagger;          // layers still up, what is left of the current one (attack power), window and stagger left (s)
    public float Taken;                                                         // attack power that went through since arrival (RD19's interrupt)
    public int EscortsSent,Breaks; public readonly Dictionary<string,int> Moves=[];   // lab: escorts sent, layers broken, attacks started
    public readonly Dictionary<string,float> Injuries=[]; public Attackment LastAttack;  // lab: health lost by source (in armour points), the last hit through Damage
    public Vector3 LastWant,LastFly;                                            // lab: where it wanted to be, the velocity it asked for
    // 0.16.0 poise (user, 2026-10-30: "可以首领失衡，但是反应也不能降到特别低的伤害，失衡也要有进度条，玩家可直观查看"): reactions,
    // control hits it shrugs off and heavy shots fill it (AddPoise); full, it is thrown off balance: a PoiseStagger s stagger, then
    // PoiseWindow s taking ExposedFactor damage past its armour plate; then the gauge is locked PoiseLock s. It drains after PoiseIdle s
    // without gain. A bar over the boss (MDTowerWorld.DrawBossBars) and the Neorxna panel show it.
    // Retuned the same day (user: "boss失衡值掉的太快了，炮塔一多，反应一多，boss根本没有还手之力"): what towers cause fills at most
    // TowerPoiseRate a second, a mech's reactions and cannon count in full; each break raises the next threshold x PoiseGrowth; the
    // lock after a break is longer and the gauge drains faster. Towers alone break a boss about once or twice in two minutes.
    public const float PoiseMax=100,PoiseGrowth=1.5f,PoiseIdle=2,PoiseDecay=8,PoiseStagger=4,PoiseWindow=8,PoiseLock=30,TowerPoiseRate=4;
    public const float PoisePair=12,PoiseTriad=30,PoiseControl=6,PoiseHeavy=2,PoiseMechCannon=5;
    public float Poise,Unbalanced,PoiseLockLeft; float poiseIdle,towerPoiseLeft=TowerPoiseRate; public int PoiseBreaks;
    public float PoiseNeeded=>PoiseMax*MathF.Pow(PoiseGrowth,PoiseBreaks);
    public static readonly List<MDBossState> Live=[];
    public ComponentMDRobot Robot=>r;
    public void AddPoise(float amount,bool byMech=false) {
        if(amount<=0||PoiseLockLeft>0||r.Health.Health<=0)return;
        if(!byMech){amount=Math.Min(amount,towerPoiseLeft);towerPoiseLeft-=amount;if(amount<=0)return;}   // towers: TowerPoiseRate a second at most
        Poise+=amount;poiseIdle=0;if(Poise<PoiseNeeded)return;
        Poise=0;PoiseLockLeft=PoiseLock+PoiseWindow;Unbalanced=PoiseWindow;Stagger=Math.Max(Stagger,PoiseStagger);PoiseBreaks++;
        var w=MDTowerWorld.For(P);var c=MDCombat.Center(r.Body);w.Flash(c,3.4f,new Color(255,220,90),.2f);w.Ring(r.Body.Position,5,new Color(255,220,90),.8f);MDRobots.Sound(P,24,c,1);
        MDBoss.Announce(P,c,$"{Name}失衡了！{PoiseWindow:0} 秒内它受到的伤害提高 {(ExposedFactor-1)*100:0}%，并且无视装甲层。",new Color(255,225,110));
    }
    protected MDBossState(ComponentMDRobot robot,ValuesDictionary v) {
        r=robot;Live.RemoveAll(b=>!b.r.Entity.IsAddedToProject&&b.r.Health.Health<=0);Live.Add(this);if(v.GetValue("BossHasHome",false))Home=v.GetValue("BossHome",Vector3.Zero);
        Armour=v.GetValue("BossArmour",0f);Escorts=v.GetValue("BossEscorts",0);EscortSize=v.GetValue("BossEscortSize",2);
        LayersLeft=Math.Clamp(v.GetValue("BossLayers",Layers),0,Layers);Plate=v.GetValue("BossPlate",-1f);
        if(Armour>0){r.Health.AttackResilience=Armour;if(Plate<0)Plate=PlateShare*Armour;}
    }
    public static MDBossState For(ComponentMDRobot r,ValuesDictionary v)=>r.Kind.Id switch{"RD13"=>new MDBirdState(r,v),"RD21"=>new MDSabreState(r,v),"RD08"=>new MDGunboatState(r,v),"RD19"=>new MDSiegeState(r,v),_=>new MDDevilState(r,v)};
    public virtual void Save(ValuesDictionary v) {
        v.SetValue("BossArmour",Armour);v.SetValue("BossEscorts",Escorts);v.SetValue("BossEscortSize",EscortSize);v.SetValue("BossLayers",LayersLeft);v.SetValue("BossPlate",Plate);
        v.SetValue("BossHasHome",Home.HasValue);if(Home.HasValue)v.SetValue("BossHome",Home.Value);
    }
    public void SetArmour(float armour){Armour=armour;r.Health.AttackResilience=armour;LayersLeft=Layers;Plate=PlateShare*armour;}
    public bool Enraged=>r.Health.Health<.5f;
    protected float Pace=>Enraged?.7f:1;   // cooldowns below half health
    public string Name=>r.Kind.Name;
    protected Project P=>r.Project;
    public virtual void Died(){if(r.Creature.ComponentLocomotion.FlySpeed>0)r.Body.Velocity=new(0,-2,0);MDBoss.Announce(P,r.Body.Position,$"{Name}被击毁了！它掉落了机甲设计图。",new Color(255,220,120));}
    // the pose while destroyed; false: the robot's own (its death clip)
    public virtual bool DeathPose(float dt)=>false;
    // a robot of its escort (the drone carrier sends drones)
    public virtual string EscortTemplate(int i,System.Random rng,Project p)=>i%2==0?MDRobots.PickElite(rng):MDRobots.PickNormal(p,rng);
    // what a player reads on arrival and in the bestiary
    public static string Hint(string id)=>id switch{
        "RD13"=>"它蹲下架起加特林时躲到掩体后；后仰时快离开地上的红圈；躲太久它会跳过来。玩家打它头部的发光镜头伤害更高。",
        "RD21"=>"地上出现红线就往旁边躲开光剑冲刺，冲完它会低空停顿；红色瞄准线变白前躲到掩体后。",
        "RD08"=>"它停在大多数炮塔的射程外：开机甲或亲自去追它，先打掉它放出的无人机；靠近时它会后退并用机枪扫你。",
        "RD19"=>"收进底座时几乎打不动，它会开到侧面另一处再升起；升起后绕到背后打（伤害更高），主炮蓄能时集中火力能打断它；离它 12 米内主炮和榴弹都打不到你，贴近它会逼它收回换位置。",
        _=>"脚下光环的颜色是它当前的弱点：白色冲击、橙色爆炸、蓝色能量。"};

    float dig; protected float Dt;
    public void Update(float dt) {
        var w=MDTowerWorld.For(P);Dt=dt;
        dig-=dt;if(dig<=0){dig=.25f;MDBoss.BlocksCleared+=MDBoss.ClearBlocks(r.Body);}
        foreach(var (bit,at) in new[]{(1,.7f),(2,.35f)})if((Escorts&bit)==0&&r.Health.Health<at){Escorts|=bit;MDBoss.Escort(r,EscortSize);}
        if(Exposed>0){Exposed-=dt;if(Exposed<=0&&LayersLeft>0)MDBoss.Announce(P,r.Body.Position,$"{Name}换上了下一层装甲。",new Color(255,170,120));}
        poiseIdle+=dt;if(poiseIdle>PoiseIdle)Poise=Math.Max(0,Poise-PoiseDecay*dt);towerPoiseLeft=Math.Min(TowerPoiseRate,towerPoiseLeft+TowerPoiseRate*dt);if(PoiseLockLeft>0)PoiseLockLeft-=dt;if(Unbalanced>0)Unbalanced-=dt;   // 0.16.0 poise
        WalkWanted=null;
        Marks(dt,w);Volleys(dt);
        if(Stagger>0){Stagger-=dt;Staggered(dt,w);return;}
        Act(dt,w);
    }
    protected abstract void Act(float dt,MDTowerWorld w);
    public virtual string Now=>"";                                  // lab: the attack under way
    public virtual void Force(string move,ComponentBody at=null){}   // lab: start that attack now
    protected virtual void Hurt(){}                                 // after a hit went through (Taken grew)
    protected virtual void Staggered(float dt,MDTowerWorld w)=>Hover(r.Body.Position,1.5f,2);

    // ---- damage (MDBoss.Modify): the boss's own factor (weak channel, weak spot, facing), then the armour phase
    public void Damage(Attackment a) {
        float p=a.AttackPower*Factor(a);
        if(Exposed>0||Unbalanced>0)p*=ExposedFactor;   // a broken layer's window, or off balance (0.16.0)
        else if(LayersLeft>0){Plate-=p;p*=Through;if(Plate<=0)Break();}
        a.AttackPower=p;Taken+=p;LastAttack=a;Hurt();
    }
    protected virtual float Factor(Attackment a)=>1;
    // an injury that did not come through an attack (fire, mostly): the armour phase the same way (MDBoss.CapInjury); it burned
    // RD19 for 22 % of its armour past both layers (bench, 2026-10-30)
    public float Phase(float amount) {
        if(Exposed>0||Unbalanced>0)return amount*ExposedFactor;
        if(LayersLeft<=0)return amount;
        Plate-=amount*Armour;if(Plate<=0)Break();return amount*Through;
    }
    void Break() {
        LayersLeft--;Breaks++;Exposed=ExposedTime;Stagger=StaggerTime;Plate=LayersLeft>0?PlateShare*Armour:0;
        var w=MDTowerWorld.For(P);var c=MDCombat.Center(r.Body);w.Flash(c,3,new Color(255,230,160),.15f);w.Sparks(c,Vector3.UnitY,24,new Color(255,200,120),8,1,.6f,.07f);MDRobots.Sound(P,24,c,1);
        MDRewards.LayerBroken(r);   // 0.15.0: scrap for each layer
        MDBoss.Announce(P,c,$"{Name}的第 {Layers-LayersLeft} 层装甲被击破了！{ExposedTime:0} 秒内它受到的伤害提高 {(ExposedFactor-1)*100:0}%。",new Color(255,230,140));
    }
    // a player's (or a mech's) own hit, not a tower's
    protected static bool ByPlayer(Attackment a)=>a.Attacker!=null&&(a.Attacker.FindComponent<ComponentMDUnit>() is {} u?u.Mech!=null:MDFaction.Friendly(a.Attacker));

    // ---- movement through the locomotion's orders: a fly order is the wanted velocity over the template's FlySpeed, a walk order
    // the wanted velocity in its own frame (x right, y forward) over WalkSpeed. The vanilla pilot adds its own orders on top
    // (an idle flyer sinks at -0.5): both are set again right before locomotion runs (MDBossOrders).
    public Vector3? Wanted; public Vector2? WalkWanted;
    protected void Fly(Vector3 velocity){var l=r.Creature.ComponentLocomotion;Wanted=l.FlySpeed>0?velocity/l.FlySpeed:Vector3.Zero;l.FlyOrder=Wanted;}
    protected void Walk(Vector3 velocity) {
        var l=r.Creature.ComponentLocomotion;if(l.WalkSpeed<=0)return;var m=r.Body.Matrix;
        var o=new Vector2(Vector3.Dot(velocity,m.Right),Vector3.Dot(velocity,m.Forward))/l.WalkSpeed;if(o.LengthSquared()>1)o=Vector2.Normalize(o);
        WalkWanted=o;l.WalkOrder=o;r.Path.Stop();
    }
    // the ground (the top collidable block, or the surface of water) under a point, from 4 m above it down: over the sea the
    // gunboat hovered 6 m over the sea floor, under water, and drowned (bench, 2026-10-30)
    protected float GroundY(Vector3 at) {
        var hit=P.FindSubsystem<SubsystemTerrain>(true).Raycast(new Vector3(at.X,at.Y+4,at.Z),new Vector3(at.X,at.Y-40,at.Z),false,true,(cv,cd)=>BlocksManager.Blocks[Terrain.ExtractContents(cv)] is var bl&&(bl.IsCollidable_(cv)||bl is FluidBlock));
        return hit.HasValue?at.Y+4-hit.Value.Distance:at.Y-2;
    }
    // Flies to `want` (x, z) `altitude` over the ground and the towers under it, at up to `speed` (gentle: the fly order lags
    // ~0.4 s). A 4.6 m body box sinking onto a tower got wedged against it and could not rise (full lab run, 2026-10-21): it stays
    // clear of the towers under it, and pressed against one for half a second it is lifted straight out.
    float stuck; readonly DynamicArray<ComponentBody> under=new();
    protected Vector3 Hover(Vector3 want,float altitude,float speed) {
        var body=r.Body;want.Y=GroundY(new Vector3(want.X,body.Position.Y,want.Z))+altitude;
        under.Clear();P.FindSubsystem<SubsystemBodies>(true).FindBodiesAroundPoint(new(want.X,want.Z),body.BoxSize.X*.5f+3,under);
        foreach(var b in under)   // the lookup returns whole grid cells: only towers really under it
            if(b!=body&&b.Entity.FindComponent<ComponentMDUnit>()!=null&&Vector2.Distance(new(b.Position.X,b.Position.Z),new(want.X,want.Z))<(body.BoxSize.X+b.BoxSize.X)*.5f+1)want.Y=Math.Max(want.Y,b.BoundingBox.Max.Y+1);
        var to=want-body.Position;var v=to.LengthSquared()>.01f?Vector3.Normalize(to)*Math.Min(speed,to.Length()*.7f):Vector3.Zero;
        Fly(v);LastWant=want;LastFly=v;
        stuck=v.Y>.8f&&body.Velocity.Y<.2f?stuck+Dt:0;if(stuck>.5f)body.Position+=Vector3.UnitY*Math.Min(3,want.Y-body.Position.Y)*Math.Min(1,4*Dt);
        return v;
    }
    // the base clip for moving in one of four directions relative to its facing (idle below `slow` m/s); a new direction must
    // lead by 30 %
    string dirClip="idle";
    protected void DirClip(string fw,string bw,string left,string right,float slow,float rate=1) {
        var v=r.Body.Velocity;var m=r.Body.Matrix;float f=Vector3.Dot(v,m.Forward),rt=Vector3.Dot(v,m.Right);
        string want=new Vector2(v.X,v.Z).Length()<slow?"idle":MathF.Abs(f)>=MathF.Abs(rt)?(f>=0?fw:bw):(rt>=0?right:left);
        float Along(string c)=>c==fw?f:c==bw?-f:c==right?rt:-rt;
        if(want!=dirClip&&dirClip!="idle"&&want!="idle"&&Along(want)<Along(dirClip)*1.3f)want=dirClip;
        dirClip=r.Def.Has(want)?want:"idle";r.Anim.PlayBase(dirClip,rate,.3f);
    }
    protected void Hold(string clip,float time,float fade=.2f){if(r.Def.Has(clip))r.Anim.HoldBase(clip,Math.Clamp(time,0,r.Def.Seconds(r.Def.Clip(clip))),fade);}
    protected void Count(string move)=>Moves[move]=Moves.GetValueOrDefault(move)+1;

    // ---- targets: the player's side it may fight (ComponentMDRobot.Valid) within `range`
    protected List<ComponentBody> Near(float range) {
        var list=new DynamicArray<ComponentBody>();var p=r.Body.Position;P.FindSubsystem<SubsystemBodies>(true).FindBodiesAroundPoint(new(p.X,p.Z),range,list);
        return list.Where(b=>b!=r.Body&&r.Valid(b)&&Vector3.Distance(b.Position,p)<=range).OrderBy(b=>Vector3.Distance(b.Position,p)).ToList();
    }
    // the one it duels: the player or mech locked on (wherever it is), else the nearest player or mech (or the player's herd) in
    // sight within `range`; null when none
    protected ComponentBody Duelist(float range) {
        if(r.ProvokerNow is {} p&&ComponentMDRobot.Rank(p)!=1)return Vector3.Distance(p.Position,r.Body.Position)<=range?p:null;
        return Near(range).FirstOrDefault(b=>ComponentMDRobot.Rank(b)!=1&&r.Sight(r.Eye,b));
    }
    // up to n targets for shells: the duelist, then players and mechs in sight, then towers; nearest first, `min` m out at least
    protected List<ComponentBody> Spread(int n,float min,float range) {
        var all=Near(range).Where(b=>Vector3.Distance(b.Position,r.Body.Position)>=min).ToList();var d=Duelist(range);
        return all.OrderBy(b=>b==d?0:ComponentMDRobot.Rank(b)!=1&&r.Sight(r.Eye,b)?1:2).Take(n).ToList();
    }

    // ---- red rings on the ground where something will land (refreshed every 0.25 s until it has)
    readonly List<(Vector3 at,float radius,float left)> marks=[]; float markTick;
    protected void Mark(Vector3 at,float radius,float seconds)=>marks.Add((at,radius,seconds));
    public int MarksShown=>marks.Count;   // lab
    void Marks(float dt,MDTowerWorld w) {
        markTick-=dt;bool draw=markTick<=0;if(draw)markTick=.25f;
        for(int i=marks.Count-1;i>=0;i--){var m=marks[i];m.left-=dt;if(m.left<=0){marks.RemoveAt(i);continue;}marks[i]=m;if(draw)w.Ring(m.at,m.radius,new Color(255,50,40),.3f);}
    }
    // ---- shots queued at a time (bursts and volleys that go on while it moves)
    readonly List<(float at,MDRobotKind k,string muzzle,ComponentBody target,Vector3? spot)> queue=[]; float clock;
    protected void Queue(float delay,MDRobotKind k,string muzzle,ComponentBody target,Vector3? spot=null)=>queue.Add((clock+delay,k,muzzle,target,spot));
    protected bool Queued=>queue.Count>0;
    void Volleys(float dt) {
        clock+=dt;
        for(int i=0;i<queue.Count;i++) {
            var s=queue[i];if(s.at>clock)continue;queue.RemoveAt(i--);
            if(s.spot.HasValue||s.target!=null&&r.Valid(s.target))Shoot(s.k,s.muzzle,s.target,s.spot);
        }
    }
    // A shot from a muzzle with weapon k: Gravity > 0 a shell on an arc (at the marked spot when given, else where the target
    // will be), "missile" homing (it can be outrun), else straight (leading the target). Returns the flight time (s).
    protected float Shoot(MDRobotKind k,string muzzle,ComponentBody target,Vector3? spot=null) {
        var w=MDTowerWorld.For(P);var from=r.Point(muzzle,out var fwd);var tc=target!=null?MDCombat.Center(target):spot??from+fwd*20;
        var f=new MDTowerWorld.Foreign{owner=r.Entity,look=k.Look,pos=from,pass=b=>b==r.Body||MDRobots.IsRobot(b.Entity)};
        f.landed=(p,d,b)=>Landed(k,p,d,b);r.ShotsFired++;float flight;
        if(k.Gravity>0) {
            var at=spot??tc+(target?.Velocity??Vector3.Zero)*(Vector3.Distance(from,tc)/k.ShotSpeed);var v=ComponentMDRobot.Ballistic(from,at,k.ShotSpeed,k.Gravity);
            f.vel=v;f.gravity=k.Gravity;f.life=8;w.Muzzle(k.Look,from,Vector3.Normalize(v),true);flight=Vector2.Distance(new(from.X,from.Z),new(at.X,at.Z))/Math.Max(1,new Vector2(v.X,v.Z).Length());
        }
        else if(k.Weapon=="missile"&&target!=null) {
            var side=Vector3.Normalize(Vector3.Cross(r.Body.Matrix.Forward,Vector3.UnitY));var v=Vector3.Normalize(fwd*.5f+Vector3.UnitY*.8f+side*MDRobots.R(-.4f,.4f))*k.ShotSpeed*.6f;
            f.vel=v;f.missile=true;f.target=target;f.homeAfter=.3f;f.accel=24;f.maxSpeed=k.ShotSpeed*2;f.turn=2.2f;f.life=7;w.Muzzle(k.Look,from,Vector3.Normalize(v),false);flight=Vector3.Distance(from,tc)/k.ShotSpeed;
        }
        else {
            float t=Vector3.Distance(from,tc)/k.ShotSpeed;var aim=spot??tc+(target?.Velocity??Vector3.Zero)*t;
            var dir=Vector3.Normalize(Vector3.Normalize(aim-from)+new Vector3(MDRobots.R(-1,1),MDRobots.R(-1,1),MDRobots.R(-1,1))*k.Spread);
            f.vel=dir*k.ShotSpeed;f.life=2.5f;w.Muzzle(k.Look,from,dir,false);flight=t;
        }
        w.LaunchForeign(f);MDRobots.Sound(P,k.FireSe,from,.75f);return flight;
    }
    void Landed(MDRobotKind k,Vector3 p,Vector3 d,ComponentBody b) {
        var w=MDTowerWorld.For(P);MDRobots.Sound(P,k.HitSe,p,.5f);
        MDRobots.LeavesBroken+=MDRobots.BreakLeaves(P,p+d*.3f,k.Splash>0?Math.Max(1.5f,k.Splash):1.2f);
        if(k.Splash<=0){w.Impact(k.Look,p,d,false);MDRobots.Hit(r.Entity,b,k,1,p,d);return;}
        w.Explosion(p,k.Splash,"heavy",new Color(k.Look.fx.color[0],k.Look.fx.color[1],k.Look.fx.color[2]),new Color(k.Look.fx.color2[0],k.Look.fx.color2[1],k.Look.fx.color2[2]));
        MDRobots.Hit(r.Entity,b,k,1,p,d);MDRobots.Splash(r.Entity,k,p,k.Splash,b);
    }
    // where a line from `from` to `to` first meets the terrain or a body of the player's side (null: nothing)
    protected Vector3 Trace(Vector3 from,Vector3 to,out ComponentBody hit) {
        var dir=to-from;float len=dir.Length();hit=null;if(len<.01f)return to;
        var terrain=P.FindSubsystem<SubsystemTerrain>(true).Raycast(from,to,false,true,(cv,cd)=>BlocksManager.Blocks[Terrain.ExtractContents(cv)].IsCollidable_(cv));
        var body=P.FindSubsystem<SubsystemBodies>(true).Raycast(from,to,0,(b,d)=>b!=r.Body&&!MDRobots.IsRobot(b.Entity));
        float t=Math.Min(terrain?.Distance??float.MaxValue,body?.Distance??float.MaxValue);
        if(body.HasValue&&body.Value.Distance<=(terrain?.Distance??float.MaxValue))hit=body.Value.ComponentBody;
        return t<float.MaxValue?from+dir/len*t:to;
    }
    // an instant beam (railgun, main gun): it stops at the first thing in its way and hits that
    protected Vector3 Beam(MDRobotKind k,Vector3 from,Vector3 dir,float range,Color c,float width) {
        var w=MDTowerWorld.For(P);var end=Trace(from,from+dir*range,out var hit);
        w.AddBeam(from,end,width,c,.3f,false);w.Flash(from,width*3,c,.1f);MDRobots.Sound(P,k.FireSe,from,1);MDRobots.Sound(P,k.HitSe,end,.8f);
        if(hit!=null)MDRobots.Hit(r.Entity,hit,k,1,end,dir);
        if(k.Splash>0){w.Explosion(end,k.Splash,"heavy",c,new Color(255,120,40));MDRobots.Splash(r.Entity,k,end,k.Splash,hit);}
        else w.Sparks(end,-dir,8,c,6,.8f,.3f,.05f);
        return end;
    }
}

// ------------------------------------------------------------------------------------------------ 恶魔机甲 RD17 (since 0.9.0)
// Weak channel (after the original's three damage channels, the 90 % resistances softened to 60 %): every hit is kinetic,
// explosion or energy (MDTowerBrain.Hit tags the towers' and Mercury's; bullets and other attacks count as kinetic); only the
// current weak channel does full damage, the other two 40 %. The weak channel changes every 20 s, shown by the colour of the ring
// at its feet, so a defence of one tower type is slow and a mixed one fast.
// Attacks: a homing missile volley at up to three targets (towers first), a beam sweep across its target and a sabre charge (the
// original's three-slash clip) at a target within 35 m; below half health all come faster. Model x3 (9.6 m tall, 15 m wings,
// tools/build_unit_assets.py): it hovers 3 m above the ground, circling its target 20 m away.
public sealed class MDDevilState:MDBossState {
    public static readonly string[] Channels=["kinetic","explosion","energy"];
    public static readonly string[] ChannelNames=["冲击（子弹、狙击、水炮、近战）","爆炸（炮弹、导弹、榴弹、火焰）","能量（射线、电击、声波、毒雾）"];
    static readonly Color[] ChannelColors=[new(235,235,245),new(255,140,50),new(80,210,255)];
    public const float WeakFactor=1,OtherFactor=.4f,WeakPeriod=20,Altitude=3,Orbit=20,ChargeSpeed=16,SlashReach=7;
    public int Weak; public float WeakTimer,Volley=4,SweepCool=7,Sweep=-1,SweepTick,Orbiting,ChargeCool=12,Charge=-1,Slash=-1; public int Slashes;
    public readonly List<ComponentBody> Volleyed=[]; float aura;
    public int Charges,Sweeps,Volleys,WeakChanges;   // lab: attacks started
    public MDDevilState(ComponentMDRobot robot,ValuesDictionary v):base(robot,v){Weak=Math.Clamp(v.GetValue("BossWeak",0),0,2);WeakTimer=v.GetValue("BossWeakTimer",WeakPeriod);}
    public override void Save(ValuesDictionary v){base.Save(v);v.SetValue("BossWeak",Weak);v.SetValue("BossWeakTimer",WeakTimer);}
    protected override float Factor(Attackment a){string ch=a is MDTowerAttackment t?t.Channel??"kinetic":"kinetic";return ch==Channels[Weak]?WeakFactor:OtherFactor;}
    string Clip(params string[] names)=>names.FirstOrDefault(r.Def.Has)??"idle";
    protected override void Act(float dt,MDTowerWorld w) {
        var body=r.Body;
        // weak channel
        // 0.13.0 (players via the user, 2026-10-29: "恶魔机甲希望隐藏胸前那个弱点，不然屏幕会一直弹提示"; the user kept the rule): the weak
        // channel still changes every 20 s, silently, shown only by the colour of the ring at its feet (no chest flash, no message)
        WeakTimer-=dt;if(WeakTimer<=0){WeakTimer=WeakPeriod;Weak=(Weak+1+MDRobots.Pick(new[]{0,1}))%3;WeakChanges++;}
        aura-=dt;if(aura<=0){aura=.12f;var c=MDCombat.Center(body);w.Ring(c-Vector3.UnitY*body.BoxSize.Y*.45f,4.5f,ChannelColors[Weak],.25f,0);}
        if(Charge>=0||Slash>=0){ChargeStep(dt,w);return;}
        // movement: hover over the ground, circle the target (or the beacon), always facing what it fights
        var focus=r.Target!=null?r.Target.Position:Home??body.Position;
        Orbiting+=dt*(Enraged?.22f:.15f);
        Hover(focus+new Vector3(MathF.Sin(Orbiting),0,MathF.Cos(Orbiting))*(r.Target!=null?Orbit:6),Altitude,r.Kind.Speed);
        if(r.Target!=null)r.Face(MDCombat.Center(r.Target),dt,1.6f);
        // attacks
        Volley-=dt;SweepCool-=dt;ChargeCool-=dt;
        if(r.Windup>=0) {
            r.Windup-=dt;foreach(var b in Volleyed)if(b.Entity.IsAddedToProject)w.LockMark(MDCombat.Center(b),Math.Max(1,b.BoxSize.Y*.7f));
            if(r.Windup<0){r.ShotsLeft=r.Kind.Shots;r.Gap=0;r.Anim.PlayAction(Clip("attack_fire"));}
        }
        else if(r.ShotsLeft>0){r.Gap-=dt;if(r.Gap<=0){var alive=Volleyed.Where(r.Valid).ToList();if(alive.Count>0)r.FireOne(alive[r.ShotsFired%alive.Count]);r.ShotsLeft--;r.Gap=r.Kind.ShotGap;if(r.ShotsLeft==0)Volley=r.Kind.Interval*Pace;}}
        else if(Sweep>=0)SweepStep(dt,w);
        else if(r.Target!=null&&Volley<=0) {
            Volleyed.Clear();var near=new DynamicArray<ComponentBody>();P.FindSubsystem<SubsystemBodies>(true).FindBodiesAroundPoint(new(body.Position.X,body.Position.Z),r.Kind.Sight,near);
            // the one that hit it (a player, a mech, a pilot's mech) takes the whole volley (user, 2026-10-25); else up to three, towers first
            if(r.ProvokerNow is {} p)Volleyed.Add(p);
            else Volleyed.AddRange(near.Where(r.Valid).OrderBy(b=>(b.Entity.FindComponent<ComponentMDUnit>()!=null?0:20)+Vector3.Distance(b.Position,body.Position)).Take(3));
            if(Volleyed.Count==0)Volleyed.Add(r.Target);r.Windup=r.Kind.Windup;Volleys++;Count("volley");MDRobots.Sound(P,7,body.Position,1);
        }
        else if(r.Target!=null&&SweepCool<=0&&r.Sight(r.Eye,r.Target)){Sweep=0;SweepTick=0;Sweeps++;Count("sweep");MDRobots.Sound(P,10,body.Position,1);r.Anim.PlayAction(Clip("attack_fire"));}
        else if(r.Target!=null&&ChargeCool<=0&&Vector3.Distance(r.Target.Position,body.Position)<35){Charge=0;Charges++;Count("charge");r.Anim.PlayAction(Clip("dash_charge"));MDRobots.Sound(P,31,body.Position,1);}
        GlideClip();
    }
    // Flight pose (user, 2026-10-21: "一直蹬脚"): the looping fly_forward (58) and strafe (15/16) clips pedal with the legs, so
    // moving it holds the end pose of the original's one-shot dash clip in the direction it moves relative to its facing
    // (10-13: a steady glide), hovering slowly it plays the idle hover (0, legs still). A new direction must lead by 30 %.
    string glide="idle";
    void GlideClip() {
        var v=r.Body.Velocity;var f=r.Body.Matrix.Forward;var right=r.Body.Matrix.Right;float fw=Vector3.Dot(v,f),rt=Vector3.Dot(v,right);
        float speed=new Vector2(v.X,v.Z).Length();string want=speed<1.5f?"idle":MathF.Abs(fw)>=MathF.Abs(rt)?(fw>=0?"dash_forward":"dash_backward"):(rt>=0?"dash_right":"dash_left");
        if(want!=glide&&glide!="idle"&&want!="idle") {   // hysteresis between directions
            float cur=glide switch{"dash_forward"=>fw,"dash_backward"=>-fw,"dash_right"=>rt,_=>-rt};float nxt=want switch{"dash_forward"=>fw,"dash_backward"=>-fw,"dash_right"=>rt,_=>-rt};
            if(nxt<cur*1.3f)want=glide;
        }
        glide=want;
        if(glide=="idle"||!r.Def.Has(glide))r.Anim.PlayBase("idle",1,.35f);else r.Anim.HoldBase(glide,r.Def.Seconds(r.Def.Clip(glide)),.35f);
    }
    // The sabre charge: flies at its target at 16 m/s, low (feet at the target's), for at most 2.5 s; within 7 m it plays the
    // original's slash clip (2.2 s, three slashes) and each slash hits what stands in a 120 degree wedge 7 m in front:
    // 3 x 80 x 2.5 = 600 against a tower, 3 x 3 x 1.4 = 1.3 x a player's health before armour.
    void ChargeStep(float dt,MDTowerWorld w) {
        var body=r.Body;
        if(Charge>=0) {
            Charge+=dt;if(r.Target==null||Charge>2.5f){Charge=-1;Slash=0;Slashes=0;r.Anim.PlayAction(Clip("attack_saber_once","attack_saber"));return;}
            var to=r.Target.Position-body.Position;var flat=new Vector3(to.X,0,to.Z);
            var v=flat.LengthSquared()>.01f?Vector3.Normalize(flat)*ChargeSpeed:Vector3.Zero;v.Y=Math.Clamp(to.Y,-4,4);Fly(v);glide="dash_forward";if(r.Def.Has(glide))r.Anim.HoldBase(glide,r.Def.Seconds(r.Def.Clip(glide)),.2f);r.Face(MDCombat.Center(r.Target),dt,4);
            w.Thruster(body.Position+Vector3.UnitY*body.BoxSize.Y*.5f-body.Matrix.Forward*1.5f,-body.Matrix.Forward,2);
            if(flat.Length()<SlashReach-1){Charge=-1;Slash=0;Slashes=0;r.Anim.PlayAction(Clip("attack_saber_once","attack_saber"));}
            return;
        }
        Slash+=dt;Fly(Vector3.Zero);r.Anim.PlayBase("idle",1,.3f);glide="idle";
        if(Slashes<3&&Slash>=.55f+.55f*Slashes) {
            Slashes++;var f=body.Matrix.Forward;var c=body.Position+Vector3.UnitY*2+f*2;MDRobots.Sound(P,16,c,1);
            var near=new DynamicArray<ComponentBody>();P.FindSubsystem<SubsystemBodies>(true).FindBodiesAroundPoint(new(c.X,c.Z),SlashReach+2,near);
            foreach(var b in near) {
                if(b==body||MDRobots.IsRobot(b.Entity))continue;var to=MDCombat.Center(b)-body.Position;var flat=new Vector3(to.X,0,to.Z);float d=flat.Length();
                if(d>SlashReach+b.BoxSize.X*.5f||Vector3.Dot(flat/Math.Max(d,.01f),f)<.5f)continue;
                bool structure=b.Entity.FindComponent<ComponentHealth>()==null;MDRobots.Hit(r.Entity,b,r.Kind,structure?2.5f:1.4f,MDCombat.Center(b),f);
                w.Sparks(MDCombat.Center(b),-f,14,new Color(255,140,160),7,1,.4f,.06f);
            }
            w.Flash(c+f*2,2.2f,new Color(255,90,120),.12f);
        }
        if(Slash>=2.2f){Slash=-1;ChargeCool=16*Pace;}
    }
    // 2.2 s beam from the eye sweeping 50 degrees across the target, a hit every 0.15 s on what it touches first
    void SweepStep(float dt,MDTowerWorld w) {
        Sweep+=dt;if(Sweep>=2.2f||r.Target==null){Sweep=-1;SweepCool=13*Pace;return;}
        var from=r.Eye;var tc=MDCombat.Center(r.Target);var dir=Vector3.Normalize(tc-from);float a=(Sweep/2.2f-.5f)*.87f;
        dir=Vector3.Transform(dir,Quaternion.CreateFromAxisAngle(Vector3.UnitY,a));
        var end=Trace(from,from+dir*r.Kind.Range*1.2f,out var hit);
        w.HoldBeam(r,from,end,.28f,new Color(255,70,90));
        SweepTick-=dt;if(SweepTick<=0){SweepTick=.15f;w.Sparks(end,-dir,5,new Color(255,120,120),5,.9f,.3f,.05f);if(hit!=null)MDRobots.Hit(r.Entity,hit,r.Kind,.3f,end,dir);}
    }
}

// ------------------------------------------------------------------------------------------------ 鸟形重装机甲 RD13 (0.14.0)
// The problem: a walking gun platform that out-guns anyone standing in the open. The answers: cover, footwork, its head.
// - 加特林压制: it crouches (0.8 s, sparks at the barrels: the warning), then 16 rounds from the two gatlings at its target. Cover stops
//   them; every 5 s.
// - 背炮覆盖: it rears up (1.5 s): red rings appear where its back cannons will land, then 6 shells, two at each of up to three
//   targets (the one it duels, players and mechs in sight, then towers). Stepping out of a ring before the shell lands is enough.
// - 机枪旋扫: a player or mech within 13 m: a red ring of 13 m round its feet (1 s), then the six machine-gun mounts slew round (the
//   original's sweep clip) firing outward for 2.4 s. Back off when it crouches.
// - 跃击: a player or mech that hides from it for 3 s (or stays 30-42 m off): it crouches and leaps (red ring on the landing spot,
//   0.6 s warning), landing in front of that one; the blast hurts everything within 7 m.
// - Weak spot: a player's or a mech's hit whose line passes within 1.4 m of the lit lens on its head does x 2.5 (the towers aim
//   at the middle of the body).
// It walks straight at what it fights (blocks in its way give way, MDBoss.ClearBlocks), keeps 15-29 m off, strafes sideways,
// backs off from a player at its feet; the upper body turns on its own (±100°). Model x5, 5.3 m tall.
public sealed class MDBirdState:MDBossState {
    static readonly MDRobotKind Shell=MDRobots.Weapon("bird_shell",26,3.5f,120,splash:3.5f,gravity:12,fire:11,hit:24,look:"shell",scale:1.4f),
        Mg=MDRobots.Weapon("bird_mg",60,.5f,8,spread:.06f),Stomp=MDRobots.Weapon("bird_stomp",0,3,150,splash:7);
    public const float Close=13,LeapReach=7,HideLeap=3,Apex=6,WeakRadius=1.4f,WeakFactor=2.5f;
    string move="";float t,gatling=3,barrage=8,sweep=2,leap=10,hidden,strafe=1,strafeTime,eyeFx;int shots;bool landed;
    Vector3 leapFrom,leapTo;readonly List<(ComponentBody b,Vector3 at)> spots=[];
    public int EyeHits,Stomped;   // lab
    public string Spots=>string.Join(" ",spots.Select(s=>$"({s.at.X:0.0},{s.at.Z:0.0})"));
    public MDBirdState(ComponentMDRobot robot,ValuesDictionary v):base(robot,v){}
    protected override float Factor(Attackment a) {
        if(!ByPlayer(a))return 1;
        var eye=r.Point("eye",out _);var v=eye-a.HitPoint;var d=a.HitDirection;
        float off=d.LengthSquared()<1e-4f?v.Length():(v-Vector3.Normalize(d)*Vector3.Dot(v,Vector3.Normalize(d))).Length();
        if(off>WeakRadius||v.Length()>8)return 1;
        EyeHits++;if(eyeFx<=0){eyeFx=.25f;MDTowerWorld.For(P).Flash(eye,.9f,new Color(255,240,200),.1f);}
        return WeakFactor;
    }
    protected override void Staggered(float dt,MDTowerWorld w){Hold("crouch_aim",.8f,.3f);move="";}
    public override string Now=>move;
    public override void Force(string m,ComponentBody at=null)=>Begin(m);
    void Begin(string m){move=m;t=0;shots=0;landed=false;spots.Clear();Count(m);}
    void End(ref float cool,float seconds){move="";cool=seconds*Pace;}
    protected override void Act(float dt,MDTowerWorld w) {
        var tg=r.Target;gatling-=dt;barrage-=dt;sweep-=dt;leap-=dt;eyeFx-=dt;
        if(tg!=null&&ComponentMDRobot.Rank(tg)!=1&&!r.Sight(r.Eye,tg))hidden+=dt;else hidden=0;
        if(move.Length==0&&tg!=null) {
            float d=Vector2.Distance(new(tg.Position.X,tg.Position.Z),new(r.Body.Position.X,r.Body.Position.Z));
            if(sweep<=0&&Duelist(Close)!=null)Begin("sweep");
            else if(leap<=0&&ComponentMDRobot.Rank(tg)!=1&&d>16&&d<42&&(hidden>=HideLeap||d>30))Begin("leap");
            else if(barrage<=0)Begin("barrage");
            else if(gatling<=0&&d<=r.Kind.Range&&r.Sight(r.Eye,tg)){Begin("gatling");MDRobots.Sound(P,7,r.Body.Position,1);}
        }
        switch(move){case "gatling":Gatling(dt,w);break;case "barrage":Barrage(dt);break;case "sweep":Sweep(dt);break;case "leap":Leap(dt,w);break;default:Stride(dt);break;}
    }
    void Stride(float dt) {
        var body=r.Body;var tg=r.Target;var v=Vector3.Zero;Vector3? face=null;
        Vector3 Flat(Vector3 a)=>new(a.X-body.Position.X,0,a.Z-body.Position.Z);
        if(tg!=null) {
            var to=Flat(tg.Position);float d=to.Length();var dir=d>.1f?to/d:body.Matrix.Forward;bool seen=r.Sight(r.Eye,tg);face=MDCombat.Center(tg);
            strafeTime-=dt;if(strafeTime<=0){strafeTime=MDRobots.R(4,7);strafe=-strafe;}
            if(!seen||d>r.Kind.Range*.85f)v=dir*r.Kind.Speed;
            else if(d<15&&ComponentMDRobot.Rank(tg)!=1)v=-dir*r.Kind.Speed*.8f;
            else v=Vector3.Cross(Vector3.UnitY,dir)*strafe*r.Kind.Speed*.5f;
        }
        else {
            var goal=r.Hunt?.Position??Home;
            if(goal.HasValue&&Flat(goal.Value).Length()>(r.Hunt!=null?r.Kind.Sight*.4f:8)){v=Vector3.Normalize(Flat(goal.Value))*r.Kind.Speed;face=body.Position+v;}
        }
        if(face.HasValue)r.Face(face.Value,dt,.9f);
        Walk(v);DirClip("walk_forward","walk_backward","walk_left","walk_right",.4f,Math.Clamp(new Vector2(body.Velocity.X,body.Velocity.Z).Length()/r.Kind.Speed,.6f,1.3f));
    }
    // 0.8 s crouched with sparks at the barrels, then 16 rounds alternating, 0.08 s apart
    void Gatling(float dt,MDTowerWorld w) {
        t+=dt;var tg=r.Target;r.Anim.PlayBase("crouch_aim",1,.3f);
        if(tg==null){End(ref gatling,5);return;}
        r.Face(MDCombat.Center(tg),dt,.9f);
        if(t<r.Kind.Windup){if((int)(t/.15f)!=(int)((t-dt)/.15f))foreach(var m in r.Kind.Muzzles){var p=r.Point(m,out var d);w.Sparks(p,d,3,new Color(255,190,90),4,.5f,.25f,.04f);}return;}
        if(shots<r.Kind.Shots&&t>=r.Kind.Windup+shots*r.Kind.ShotGap){Shoot(r.Kind,r.Kind.Muzzles[shots%2],tg);shots++;}
        if(shots>=r.Kind.Shots&&t>r.Kind.Windup+shots*r.Kind.ShotGap+.4f)End(ref gatling,5);
    }
    // rear up 1.47 s (the rings appear), 6 shells 0.3 s apart from 1.6 s, then back down
    void Barrage(float dt) {
        t+=dt;const float up=1.47f,fire=1.6f,gap=.3f;const int n=6;float down=fire+gap*n+.4f;
        if(t<up)Hold("rear_up",t);else if(t<down)Hold("upright_hold",0);else Hold("rear_down",t-down);
        if(t>=up&&spots.Count==0) {
            foreach(var b in Spread(3,8,r.Kind.Sight))spots.Add((b,b.Position+new Vector3(MDRobots.R(-1,1),0,MDRobots.R(-1,1))));
            if(spots.Count==0){End(ref barrage,6);return;}
            foreach(var s in spots)Mark(s.at,Shell.Splash,fire-up+gap*n+3);
        }
        if(t>=fire+shots*gap&&shots<n&&spots.Count>0){var s=spots[shots%spots.Count];Shoot(Shell,shots%2==0?"cannonF":"cannonB",s.b,s.at);shots++;}
        if(t>=down+1.37f)End(ref barrage,14);
    }
    // a 13 m ring at its feet for 1 s, then the six mounts slew round firing outward every 0.3 s
    void Sweep(float dt) {
        t+=dt;Hold("crouch_sweep",t);
        if(shots==0){shots=1;Mark(r.Body.Position,Close,3.4f);MDRobots.Sound(P,10,r.Body.Position,1);}
        if(t>=1&&t<3.4f&&(int)((t-1)/.3f)!=(int)((t-dt-1)/.3f))
            foreach(var m in new[]{"mgLF","mgLM","mgLB","mgRF","mgRM","mgRB"}) {
                var p=r.Point(m,out var d);var flat=new Vector3(d.X,0,d.Z);if(flat.LengthSquared()<.01f)continue;
                var at=p+Vector3.Normalize(flat)*10;at.Y=r.Body.Position.Y+1;Shoot(Mg,m,null,at);
            }
        if(t>=r.Def.Seconds(r.Def.Clip("crouch_sweep")))End(ref sweep,10);
    }
    // the leap: 0.6 s crouch (ring on the landing spot), 1.3 s in the air on an arc 6 m high, the blast, 2.2 s to recover
    void Leap(float dt,MDTowerWorld w) {
        t+=dt;const float crouch=.6f,air=1.3f;var body=r.Body;
        if(shots==0) {
            var tg=r.Target;if(tg==null){End(ref leap,4);return;}
            shots=1;leapFrom=body.Position;var to=new Vector3(tg.Position.X-body.Position.X,0,tg.Position.Z-body.Position.Z);float d=to.Length();
            leapTo=tg.Position-to/Math.Max(d,.1f)*Math.Min(4,d*.3f);leapTo.Y=GroundY(leapTo);Mark(leapTo,LeapReach,crouch+air+.2f);MDRobots.Sound(P,31,body.Position,1);
        }
        if(t<crouch){Hold("jump",t/crouch*.4f);r.Face(leapTo,dt,3);return;}
        if(t<crouch+air) {
            float f=(t-crouch)/air;Hold("jump",.4f+f*.6f,.1f);
            var p=Vector3.Lerp(leapFrom,leapTo,f)+Vector3.UnitY*(4*Apex*f*(1-f));
            body.Velocity=(leapTo-leapFrom)/air+Vector3.UnitY*(4*Apex*(1-2*f)/air)+(p-body.Position)*3;
            return;
        }
        if(!landed) {
            landed=true;w.Explosion(leapTo,3,"heavy",new Color(255,200,140),new Color(200,120,60));MDRobots.Sound(P,24,leapTo,1);
            var near=new DynamicArray<ComponentBody>();P.FindSubsystem<SubsystemBodies>(true).FindBodiesAroundPoint(new(leapTo.X,leapTo.Z),LeapReach+2,near);
            foreach(var b in near) {
                if(b==body||MDRobots.IsRobot(b.Entity))continue;var c=MDCombat.Center(b);float dist=Math.Max(0,Vector2.Distance(new(c.X,c.Z),new(leapTo.X,leapTo.Z))-b.BoxSize.X*.5f);
                if(dist>LeapReach||MathF.Abs(c.Y-leapTo.Y)>5)continue;MDRobots.Hit(r.Entity,b,Stomp,MathUtils.Lerp(1,.4f,dist/LeapReach),c,Vector3.Normalize(c-leapTo+Vector3.UnitY));Stomped++;
            }
        }
        Hold("jump",1+(t-crouch-air)*1.1f);
        if(t>crouch+air+2.2f)End(ref leap,16);
    }
}

// ------------------------------------------------------------------------------------------------ 双光剑飞行机甲 RD21 (0.14.0)
// The problem: a fast hunter that comes for the player. The answers: a sidestep, cover, and the pause after it strikes.
// - 光剑冲刺: at the one it duels (the player or mech locked on, else the nearest in sight within 40 m, else its target): it stops,
//   a red line on the ground shows its path (aimed for 0.8 s, then fixed: it turns white), then it dashes along it at 24 m/s with
//   both sabres out, cutting everything within 3.5 m of the line; afterwards it hangs 1.5 m over the ground for 2.5 s. Below half
//   health it dashes twice. Every 11 s.
// - 磁轨炮: a thin red aiming beam from its rails follows the target for 1.3 s, then holds still (white) for 0.3 s and fires along
//   it: the first thing on the line is hit. Every 9 s; cover or a step aside after it holds still.
// - Nose guns in bursts of 8 while it circles its target 22 m off, 4 m over the ground; 4 missiles at towers every 10 s.
// Model x4: 5.7 m wide, the sabres up to 14 m long in the slash clips (the cut is the 3.5 m along the dash, not the blades' reach).
public sealed class MDSabreState:MDBossState {
    static readonly MDRobotKind Rail=MDRobots.Weapon("sabre_rail",0,4.5f,300,splash:1.5f,fire:10,hit:24),Blade=MDRobots.Weapon("sabre_blade",0,4,220),
        Missile=MDRobots.Weapon("sabre_missile",14,2.5f,70,splash:2,fire:9,hit:14,look:"shell",scale:1.1f,type:"missile");
    public const float Altitude=4,Orbit=22,DashSpeed=24,DashReach=3.5f,DashTell=1,RailTell=1.6f,RailLock=1.3f;
    string move="";float t,guns=2,dash=6,rail=4,missiles=8,orbit;int dashes;bool fired;ComponentBody prey;Vector3 dashDir;Vector3? dashFrom;float dashLen;
    readonly HashSet<ComponentBody> cut=[];Vector3 railDir;readonly object lineKey=new(),railKey=new();
    public int Cuts;   // lab: bodies cut by a dash
    public MDSabreState(ComponentMDRobot robot,ValuesDictionary v):base(robot,v){}
    void Begin(string m){move=m;t=0;fired=false;dashFrom=null;Count(m);}
    void End(ref float cool,float seconds){move="";cool=seconds*Pace;}
    public override string Now=>move;
    public override void Force(string m,ComponentBody at=null){prey=at??r.Target;Begin(m);}
    protected override void Staggered(float dt,MDTowerWorld w){Hover(r.Body.Position,1,2);r.Anim.PlayBase("idle",1,.3f);move="";}
    protected override void Act(float dt,MDTowerWorld w) {
        var body=r.Body;var tg=r.Target;guns-=dt;dash-=dt;rail-=dt;missiles-=dt;
        if(move=="dash"){DashStep(dt,w);return;}
        if(move=="recover"){t+=dt;Hover(body.Position,1.5f,2);r.Anim.PlayBase("idle",1,.3f);if(t>2.5f)End(ref dash,11);return;}
        if(move=="rail")RailStep(dt,w);
        else {
            orbit+=dt*(Enraged?.3f:.2f);var focus=tg?.Position??Home??body.Position;
            Hover(focus+new Vector3(MathF.Sin(orbit),0,MathF.Cos(orbit))*(tg!=null?Orbit:6),Altitude,r.Kind.Speed);
            if(tg!=null) {
                var tc=MDCombat.Center(tg);r.Face(tc,dt,2.5f);bool seen=r.Sight(r.Eye,tg);
                if(dash<=0&&(prey=Duelist(40)??tg)!=null){Begin("dash");MDRobots.Sound(P,31,body.Position,1);}
                else if(rail<=0&&seen){Begin("rail");MDRobots.Sound(P,7,body.Position,1);}
                else if(missiles<=0){missiles=10*Pace;Count("missiles");var at=Near(r.Kind.Sight).Where(b=>ComponentMDRobot.Rank(b)==1).Take(2).ToList();if(at.Count==0)at.Add(tg);for(int i=0;i<4;i++)Queue(i*.25f,Missile,"msl"+(i+1),at[i%at.Count]);}
                else if(guns<=0&&seen&&Vector3.Distance(tc,body.Position)<=r.Kind.Range){guns=r.Kind.Interval*Pace;Count("guns");for(int i=0;i<r.Kind.Shots;i++)Queue(r.Kind.Windup+i*r.Kind.ShotGap,r.Kind,r.Kind.Muzzles[i%4],tg);}
            }
        }
        DirClip("fly_forward","fly_backward","fly_left","fly_right",1.5f);
    }
    void DashStep(float dt,MDTowerWorld w) {
        t+=dt;var body=r.Body;
        if(t<DashTell) {
            Hover(body.Position,Altitude,2);
            if(t<DashTell*.8f&&prey!=null&&prey.Entity.IsAddedToProject) {
                var to=new Vector3(prey.Position.X-body.Position.X,0,prey.Position.Z-body.Position.Z);float d=to.Length();
                if(d>.5f)dashDir=to/d;dashLen=Math.Clamp(d+6,10,32);
            }
            r.Face(body.Position+dashDir,dt,6);bool locked=t>=DashTell*.8f;
            var a=new Vector3(body.Position.X,GroundY(body.Position)+.15f,body.Position.Z);
            w.HoldBeam(lineKey,a,a+dashDir*dashLen,locked?.45f:.25f,locked?new Color(255,215,215):new Color(255,50,50));r.Anim.PlayBase("idle",1,.2f);
            return;
        }
        if(dashFrom==null){dashFrom=body.Position;cut.Clear();r.Anim.PlayAction(Enraged&&dashes==1?"attack_sabre_2":"attack_sabre");MDRobots.Sound(P,16,body.Position,1);}
        float gone=Vector2.Distance(new(body.Position.X,body.Position.Z),new(dashFrom.Value.X,dashFrom.Value.Z));
        if(gone<dashLen&&t<DashTell+2) {
            var v=dashDir*DashSpeed;v.Y=Math.Clamp((GroundY(body.Position)+1.5f-body.Position.Y)*3,-6,6);Fly(v);r.Anim.PlayBase("fly_forward",1,.2f);
            w.Thruster(body.Position+Vector3.UnitY*body.BoxSize.Y*.5f-dashDir*2,-dashDir,2);
            var near=new DynamicArray<ComponentBody>();P.FindSubsystem<SubsystemBodies>(true).FindBodiesAroundPoint(new(body.Position.X,body.Position.Z),DashReach+3,near);
            foreach(var b in near) {
                if(b==body||MDRobots.IsRobot(b.Entity)||cut.Contains(b))continue;var c=MDCombat.Center(b);
                if(Vector2.Distance(new(c.X,c.Z),new(body.Position.X,body.Position.Z))>DashReach+b.BoxSize.X*.5f||MathF.Abs(c.Y-body.Position.Y)>4)continue;
                cut.Add(b);Cuts++;MDRobots.Hit(r.Entity,b,Blade,1,c,dashDir);w.Sparks(c,-dashDir,14,new Color(255,140,160),7,1,.4f,.06f);
            }
            return;
        }
        dashes++;
        if(Enraged&&dashes==1){t=DashTell*.4f;dashFrom=null;return;}   // a second, shorter warning
        dashes=0;move="recover";t=0;
    }
    void RailStep(float dt,MDTowerWorld w) {
        t+=dt;var tg=r.Target;var body=r.Body;Hover(body.Position,Altitude,2);
        if(tg==null||!r.Valid(tg)){End(ref rail,4);return;}
        r.Face(MDCombat.Center(tg),dt,2);
        var from=(r.Point("railL",out _)+r.Point("railR",out _))*.5f;
        if(t<RailLock)railDir=Vector3.Normalize(MDCombat.Center(tg)-from);
        if(t<RailTell){var end=Trace(from,from+railDir*80,out _);w.HoldBeam(railKey,from,end,t<RailLock?.06f:.14f,t<RailLock?new Color(255,50,50):new Color(255,235,235));return;}
        if(!fired){fired=true;r.Anim.PlayAction("attack_arms");Beam(Rail,from,railDir,80,new Color(255,240,240),.35f);}
        if(t>RailTell+.6f)End(ref rail,9);
    }
}

// ------------------------------------------------------------------------------------------------ 重装悬浮炮艇 RD08 (0.14.0)
// The problem: a flying gun platform that stays outside the towers' reach and sends drones. The answers: go out after it (on foot
// or in a mech), take out its drones, the long-range towers.
// - It hovers 6 m over the ground and keeps 34 m from its target (most towers reach 28-36 m; sniper and missile towers 48 m).
// - 双轨炮: the rails glow for 1.4 s, then two heavy shells at its target. Every 6 s.
// - A player or mech within 16 m: it backs off to 26 m from that one, turning its three machine guns on it (bursts of 12).
// - 导弹: a lock warning on the one it duels (else its target) for 1.2 s, then 4 homing missiles. Every 12 s.
// - 2 small drones (RD09) every 30 s while fewer than 3 of its own are about; its escorts are drones too.
// Model x3: 5.2 m wide, 8.6 m long.
public sealed class MDGunboatState:MDBossState {
    static readonly MDRobotKind Rail=MDRobots.Weapon("gunboat_rail",90,3,150,splash:2,fire:11,hit:24,look:"shell",scale:1.3f),
        Missile=MDRobots.Weapon("gunboat_missile",14,2.5f,60,splash:2,fire:9,hit:14,look:"shell",type:"missile");
    // 0.15.0 (user, 2026-10-30: RD08 "要在塔的射程内"): 22 m from what it fights (most towers reach that), backing off to 26 m from a
    // player or mech that comes within 16 m (was 34 m and 32 m: only the sniper and missile towers reached it)
    public const float Altitude=6,StandOff=22,Evade=16,Retreat=26,RailTell=1.4f;
    string move="";float t,rail=3,mg,missiles=6,drones=4,drift=1,driftTime;bool fired;ComponentBody lockOn;readonly List<ComponentMDRobot> fleet=[];
    ComponentBody sideFor;double sideAt;Vector3? sideTower;   // 0.15.0: the tower nearest to whom it duels (Act)
    public int DronesSent;   // lab
    public MDGunboatState(ComponentMDRobot robot,ValuesDictionary v):base(robot,v){}
    public override string EscortTemplate(int i,System.Random rng,Project p)=>i%3==2?MDRobots.Gunships[rng.Next(MDRobots.Gunships.Length)]:MDRobots.Drones[rng.Next(MDRobots.Drones.Length)];
    void Begin(string m){move=m;t=0;fired=false;Count(m);}
    void End(ref float cool,float seconds){move="";cool=seconds*Pace;}
    public override string Now=>move;
    public override void Force(string m,ComponentBody at=null){if(m=="missiles")lockOn=at??r.Target;if(m=="drones"){Release();return;}Begin(m);}
    protected override void Staggered(float dt,MDTowerWorld w){Hover(r.Body.Position,2,2);r.Anim.PlayBase("idle",1,.3f);move="";}
    protected override void Act(float dt,MDTowerWorld w) {
        var body=r.Body;var tg=r.Target;rail-=dt;mg-=dt;missiles-=dt;drones-=dt;driftTime-=dt;if(driftTime<=0){driftTime=MDRobots.R(5,8);drift=-drift;}
        var threat=Duelist(Evade);
        Vector3 Away(ComponentBody b){var d=new Vector3(body.Position.X-b.Position.X,0,body.Position.Z-b.Position.Z);return d.LengthSquared()>.01f?Vector3.Normalize(d):-body.Matrix.Forward;}
        // 0.15.0: round a player (or mech) with towers within 40 m it stays in the towers' reach (bosses14: duelling the player it once
        // hovered 34 m from the towers on the player's far side): facing them from the towers' side, and backing off from one that
        // comes close at most Retreat m from the nearest tower
        Vector3? Tower(ComponentBody b) {
            if(sideFor!=b||Time.FrameStartTime>=sideAt){sideFor=b;sideAt=Time.FrameStartTime+1;sideTower=null;
                var list=new DynamicArray<ComponentBody>();P.FindSubsystem<SubsystemBodies>(true).FindBodiesAroundPoint(new(b.Position.X,b.Position.Z),40,list);float bd=1600;
                foreach(var o in list)if(o.Entity.FindComponent<ComponentMDUnit>() is {Tower:not null} u&&!u.Destroyed){float d=Vector2.DistanceSquared(new(o.Position.X,o.Position.Z),new(b.Position.X,b.Position.Z));if(d<bd&&d>1){bd=d;sideTower=o.Position;}}}
            return ComponentMDRobot.Rank(b)==1?null:sideTower;
        }
        Vector3 Side(ComponentBody b)=>Tower(b) is {} t?Vector3.Normalize(new Vector3(t.X-b.Position.X,0,t.Z-b.Position.Z)):Away(b);
        Vector3 Keep(Vector3 p,ComponentBody b){if(Tower(b) is not {} t)return p;var d=new Vector3(p.X-t.X,0,p.Z-t.Z);float l=d.Length();return l>Retreat?new Vector3(t.X,p.Y,t.Z)+d/l*Retreat:p;}
        Vector3 want;
        if(threat!=null)want=Keep(threat.Position+Away(threat)*Retreat+Vector3.Cross(Vector3.UnitY,Away(threat))*drift*4,threat);
        else if(tg!=null)want=tg.Position+Side(tg)*StandOff+Vector3.Cross(Vector3.UnitY,Side(tg))*drift*6;
        else want=Home??body.Position;
        Hover(want,Altitude,r.Kind.Speed);
        var facing=threat??tg;if(facing!=null)r.Face(MDCombat.Center(facing),dt,1.4f);
        if(threat!=null&&mg<=0&&r.Sight(r.Eye,threat)){mg=r.Kind.Interval*Pace;Count("mg");for(int i=0;i<r.Kind.Shots;i++)Queue(r.Kind.Windup+i*r.Kind.ShotGap,r.Kind,r.Kind.Muzzles[i%3],threat);}
        if(move=="rail") {
            t+=dt;if(tg==null){End(ref rail,3);return;}
            if((int)(t/.2f)!=(int)((t-dt)/.2f))foreach(var m in new[]{"railL","railR"})w.Flash(r.Point(m,out _),.5f+t*.3f,new Color(255,170,80),.1f);
            if(t>=RailTell&&!fired){fired=true;Shoot(Rail,"railL",tg);Queue(.15f,Rail,"railR",tg);}
            if(t>=RailTell+.3f)End(ref rail,6);
        }
        else if(move=="missiles") {
            t+=dt;if(lockOn==null||!r.Valid(lockOn)){End(ref missiles,4);return;}
            if(t<1.2f)w.LockMark(MDCombat.Center(lockOn),Math.Max(1,lockOn.BoxSize.Y*.7f));
            else{for(int i=0;i<4;i++)Queue(i*.3f,Missile,i%2==0?"missileL":"missileR",lockOn);End(ref missiles,12);}
        }
        else if(tg!=null&&rail<=0&&r.Sight(r.Eye,tg)){Begin("rail");MDRobots.Sound(P,10,body.Position,.8f);}
        else if(missiles<=0&&(lockOn=Duelist(r.Kind.Sight)??tg)!=null){Begin("missiles");MDRobots.Sound(P,7,body.Position,1);}
        if(drones<=0){drones=30;Release();}
        DirClip("move_forward","move_backward","move_left","move_right",1);
    }
    void Release() {
        fleet.RemoveAll(d=>!d.Entity.IsAddedToProject||d.Health.Health<=0);if(fleet.Count>=3)return;
        var body=r.Body;int sent=0;
        foreach(float side in new[]{-1f,1f}) {
            var e=DatabaseManager.CreateEntity(P,MDRobots.Drones[0],true);e.FindComponent<ComponentBody>(true).Position=body.Position+body.Matrix.Right*side*3-Vector3.UnitY*.5f;
            var d=e.FindComponent<ComponentMDRobot>(true);d.Raider=true;d.RaidGoal=Home??body.Position;e.FindComponent<ComponentSpawn>(true).AutoDespawn=false;P.AddEntity(e);fleet.Add(d);sent++;
        }
        DronesSent+=sent;Count("drones");MDBoss.Announce(P,body.Position,$"{Name}放出了 {sent} 架无人机！",new Color(255,150,110));
    }
}

// ------------------------------------------------------------------------------------------------ 重型装甲炮车 RD19 (0.14.0)
// The problem: a siege emplacement that shells the defences from 60 m and cannot be moved. The answers: go to it, get behind it,
// break its main gun's charge.
// - It comes out of the ground 34-40 m from the beacon facing it, rises out of its base (4 s), fights for 16 s, sinks back (hits then
//   do 30 %) and drives on its base at 3 m/s to another firing spot 30-40 m from its target, 30-50 degrees round to one side (away
//   from a player close by), and rises again (user, 2026-10-30: "那个固定在地面上的装甲炮车好像不能移动"). A player or mech within
//   10 m for 1.5 s makes it sink back early and move. Hits from behind do 150 %, from the front 70 %.
// - 主炮: the gun turns slowly (0.5 rad/s) after its target and a red beam shows where it points, brighter for 3 s; then a beam that
//   hits the first thing on its line (500 against a tower). 3 % of its armour in damage during the charge breaks it off and staggers
//   it for 2.5 s. Every 10 s.
// - 榴弹: red rings where 6 shells will land (two at each of up to three targets 12 m out at least): the front four tubes for
//   targets ahead, the two rear ones for those behind. Every 7 s.
// - 2 homing missiles at a player or mech within 50 m (lock warning 0.8 s). Every 9 s.
// - Nothing but the missiles reaches 12 m from it.
// Model x3: 5.3 m wide, 15.3 m long with the gun.
public sealed class MDSiegeState:MDBossState {
    static readonly MDRobotKind Main=MDRobots.Weapon("siege_main",0,6,500,splash:3,fire:10,hit:24),
        Missile=MDRobots.Weapon("siege_missile",14,2.5f,50,splash:2,fire:9,hit:14,look:"shell",type:"missile");
    public const float DeployTime=4,StowTime=2,UpTime=16,DownTime=3,MaxDrive=12,MainTell=3,MainBreak=.03f,Close=12,Crowd=10,StowedFactor=.3f,RearFactor=1.5f,FrontFactor=.7f;
    public string Phase="stowed"; float phaseT=DownTime-1,t,howitzer=3,main=5,missiles=4,charged,deathT,crowded,dust;string move="";ComponentBody lockOn;readonly object mainKey=new();
    public Vector3? Spot;                // where it drives to while sunk in its base
    public int Interrupts,MainShots,Moves2,Evades;   // lab: interrupted charges, main gun shots, drives, early sinks with a player close
    public MDSiegeState(ComponentMDRobot robot,ValuesDictionary v):base(robot,v){}
    public bool Up=>Phase=="up";
    public override string Now=>Phase+(move.Length>0?"/"+move:"");
    public override void Force(string m,ComponentBody at=null){if(m=="up"){Phase="up";phaseT=0;Spot=null;return;}if(m=="stow"){Phase="stow";phaseT=0;move="";return;}move=m;t=0;charged=Taken;if(m=="missiles")lockOn=at??r.Target;Count(m);}
    // 3 % of its armour in damage during the main gun's charge breaks it off
    protected override void Hurt() {
        if(move!="main"||Taken-charged<MainBreak*Armour)return;
        move="";main=8*Pace;Interrupts++;Stagger=Math.Max(Stagger,2.5f);
        var from=r.Point("main",out var dir);MDTowerWorld.For(P).Sparks(from,dir,16,new Color(255,200,120),6,1,.5f,.06f);
        MDBoss.Announce(P,r.Body.Position,$"{Name}的主炮蓄能被打断了！",new Color(255,230,140));
    }
    protected override float Factor(Attackment a) {
        if(!Up&&Phase!="deploy")return StowedFactor;
        float along=a.HitDirection.LengthSquared()>1e-4f?Vector3.Dot(Vector3.Normalize(a.HitDirection),r.Body.Matrix.Forward):0;   // a hit from behind travels along its facing
        return along>.5f?RearFactor:along<-.5f?FrontFactor:1;
    }
    public override bool DeathPose(float dt){deathT+=dt;Hold("deploy",DeployTime-Math.Min(DeployTime,deathT*1.5f),.3f);return true;}   // sinks back into its base
    protected override void Staggered(float dt,MDTowerWorld w){move="";Pose();}
    void Pose(){switch(Phase){case "stowed":Hold("stowed",0);break;case "deploy":Hold("deploy",phaseT);break;case "stow":Hold("deploy",DeployTime*(1-phaseT/StowTime));break;default:r.Anim.PlayBase("idle",1,.3f);break;}}
    protected override void Act(float dt,MDTowerWorld w) {
        var body=r.Body;phaseT+=dt;howitzer-=dt;main-=dt;missiles-=dt;
        switch(Phase) {
            case "stowed": Drive(dt,w);if(phaseT>=DownTime&&Spot==null||phaseT>=DownTime+MaxDrive){Spot=null;Phase="deploy";phaseT=0;MDRobots.Sound(P,31,body.Position,1);}break;
            case "deploy": if(phaseT>=DeployTime){Phase="up";phaseT=0;}break;
            case "up":
                crowded=Duelist(Crowd)!=null?crowded+dt:0;
                if((phaseT>=UpTime||crowded>=1.5f)&&move.Length==0&&!Queued){if(crowded>=1.5f)Evades++;Phase="stow";phaseT=0;crowded=0;Count("stow");}
                break;
            case "stow": if(phaseT>=StowTime){Phase="stowed";phaseT=0;PickSpot();}break;
        }
        Pose();
        var tg=r.Target;
        if(Phase=="deploy"&&tg!=null)r.Face(tg.Position,dt,.8f);
        else if(tg!=null&&Phase!="stowed"&&MathF.Abs(MDCombat.AngleDelta(r.Yaw,MathF.Atan2(-(tg.Position.X-body.Position.X),-(tg.Position.Z-body.Position.Z))))>2)r.Face(tg.Position,dt,.15f);   // beyond the gun's traverse: the whole body creeps round
        if(!Up)return;
        if(move=="main"){MainStep(dt,w);return;}
        if(move=="missiles") {
            t+=dt;if(lockOn==null||!r.Valid(lockOn)){move="";missiles=4;return;}
            if(t<.8f)w.LockMark(MDCombat.Center(lockOn),Math.Max(1,lockOn.BoxSize.Y*.7f));else{Queue(0,Missile,"msl1",lockOn);Queue(.4f,Missile,"msl2",lockOn);move="";missiles=9*Pace;}
            return;
        }
        if(tg!=null&&main<=0&&Vector3.Distance(tg.Position,body.Position)>=Close&&r.Sight(r.Eye,tg)){move="main";t=0;charged=Taken;Count("main");MDRobots.Sound(P,10,body.Position,.8f);}
        else if(howitzer<=0) {
            var at=Spread(3,Close,r.Kind.Range);
            if(at.Count>0) {
                howitzer=r.Kind.Interval*Pace;Count("howitzer");
                for(int i=0;i<r.Kind.Shots;i++) {
                    var b=at[i%at.Count];var spot=b.Position+new Vector3(MDRobots.R(-1,1),0,MDRobots.R(-1,1));bool ahead=Vector3.Dot(b.Position-body.Position,body.Matrix.Forward)>=0;
                    var m=ahead?r.Kind.Muzzles[i%4]:(i%2==0?"hou5":"hou6");Mark(spot,r.Kind.Splash,r.Kind.Windup+i*r.Kind.ShotGap+3.5f);Queue(r.Kind.Windup+i*r.Kind.ShotGap,r.Kind,m,b,spot);
                }
            }
            else howitzer=2;
        }
        else if(missiles<=0&&(lockOn=Duelist(50))!=null){move="missiles";t=0;Count("missiles");MDRobots.Sound(P,7,body.Position,1);}
    }
    // the next firing spot: 30-40 m from its target (or the beacon), 30-50 degrees round to the side away from a player close by
    void PickSpot() {
        var body=r.Body;var focus=r.Target?.Position??Home??body.Position;var from=new Vector3(body.Position.X-focus.X,0,body.Position.Z-focus.Z);
        float d=Math.Clamp(from.Length(),30,40),a=MathF.Atan2(from.X,from.Z),turn=MDRobots.R(.5f,.9f);
        Vector3 At(float s,float tn,float dd)=>focus+new Vector3(MathF.Sin(a+s*tn),0,MathF.Cos(a+s*tn))*dd;
        var near=Duelist(20);float side=MDRobots.R(0,1)<.5f?-1:1;if(near!=null)side=Vector3.Distance(At(1,turn,d),near.Position)>=Vector3.Distance(At(-1,turn,d),near.Position)?1:-1;
        var t=P.FindSubsystem<SubsystemTerrain>(true).Terrain;
        bool Dry(Vector3 q){int x=Terrain.ToCell(q.X),z=Terrain.ToCell(q.Z);return t.GetChunkAtCell(x,z)!=null&&BlocksManager.Blocks[Terrain.ExtractContents(t.GetCellValueFast(x,t.GetTopHeight(x,z),z))] is not FluidBlock;}
        // the wanted side first, then the other, a few turns and distances each (two tries alone often both fell in a lake or the
        // sea and it never moved: lab bosses14 1.9.3.1, 10-30); nothing dry or loaded: it rises where it is
        foreach(float s in new[]{side,-side})foreach(float tn in new[]{turn,.5f,.9f,.3f,1.2f})foreach(float dd in new[]{d,30,40}) {
            var p=At(s,tn,dd);if(!Dry(p))continue;
            p.Y=GroundY(p);Spot=p;Moves2++;Count("drive");return;
        }
    }
    // sunk in its base it drives to the spot (blocks in the way give way, MDBoss.ClearBlocks), facing where it goes
    void Drive(float dt,MDTowerWorld w) {
        if(Spot is not {} s)return;var body=r.Body;var to=new Vector3(s.X-body.Position.X,0,s.Z-body.Position.Z);
        if(to.Length()<1.5f){Spot=null;return;}
        var v=Vector3.Normalize(to)*r.Kind.Speed;Walk(v);r.Face(body.Position+v,dt,.8f);
        dust-=dt;if(dust<=0){dust=.4f;w.SmokePuffs(body.Position+Vector3.UnitY*.3f,1,1.2f,new Color(150,130,100,140),.4f,1.2f,.4f);}
    }
    void MainStep(float dt,MDTowerWorld w) {
        t+=dt;var tg=r.Target;
        if(tg==null||!r.Valid(tg)){move="";main=3;return;}
        var from=r.Point("main",out var dir);
        var end=Trace(from,from+dir*90,out _);float f=t/MainTell;
        if(t<MainTell){w.HoldBeam(mainKey,from,end,.06f+.2f*f,new Color(255,(int)(60+120*f),(int)(40+40*f)));return;}
        Beam(Main,from,dir,90,new Color(255,190,90),.7f);MainShots++;move="";main=10*Pace;
    }
}

public static class MDBoss {
    public const float Countdown=30;
    // At most this share of its armour per hit (0.12.0: 2 %, user, 2026-10-25: "恶魔机甲要设置限伤害…可参考美好时代，深海迷航的一些boss";
    // 0.14.0: 8 % with the armour phases, MDBossState): other mods' one-shot weapons. ModLoader hook CalculateCreatureInjuryAmount.
    public const float MaxHitShare=.08f;
    // a tower or mech a boss hits cannot be healed for this long (MDTowerWorld.BlockHeal; user, 2026-10-30: many towers with medics
    // made the bosses "不堪一击")
    public const float HealBlock=8;
    public static void CapInjury(Injury injury) {
        if(injury?.ComponentHealth?.Entity.FindComponent<ComponentMDRobot>() is not {Boss:{} b})return;
        if(injury.Attackment==null||!ReferenceEquals(injury.Attackment,b.LastAttack))injury.Amount=b.Phase(injury.Amount);   // not through MDBoss.Modify
        if(injury.Amount>MaxHitShare)injury.Amount=MaxHitShare;
        var key=injury.Attackment!=null?injury.Attackment.GetType().Name+(ReferenceEquals(injury.Attackment,b.LastAttack)?"":" (no armour)"):"cause "+(injury.Cause??"-");   // lab
        b.Injuries[key]=b.Injuries.GetValueOrDefault(key)+injury.Amount*b.Armour;
    }
    // Any block in its way gives way (user, 2026-10-25: "恶魔机甲作为boss，应该可以清理一切阻挡他的方块（现在放恶魔机甲在方块下面会把它卡死）"):
    // every collidable block in its body box, half a metre around and above it and 1.5 m ahead of its movement, from just above its
    // feet up (it does not dig down). Not bedrock; no drops (a flight through a hill would leave hundreds of items).
    public static int BlocksCleared;   // tests
    public static int ClearBlocks(ComponentBody b) {
        var t=b.Project.FindSubsystem<SubsystemTerrain>(true);var box=b.BoundingBox;var v=b.Velocity;
        var min=new Vector3(box.Min.X-.5f,box.Min.Y+.3f,box.Min.Z-.5f);var max=new Vector3(box.Max.X+.5f,box.Max.Y+.6f,box.Max.Z+.5f);
        v.Y=Math.Max(v.Y,0);   // ahead never reaches below its feet: coming down onto a mech it dug a pit there (lab robots-escorts, 10-30)
        if(v.LengthSquared()>.25f){var ahead=Vector3.Normalize(v)*1.5f;min=Vector3.Min(min,min+ahead);max=Vector3.Max(max,max+ahead);}
        int n=0;
        for(int x=(int)MathF.Floor(min.X);x<=(int)MathF.Floor(max.X);x++)for(int z=(int)MathF.Floor(min.Z);z<=(int)MathF.Floor(max.Z);z++) {
            if(t.Terrain.GetChunkAtCell(x,z)==null)continue;
            for(int y=Math.Max(1,(int)MathF.Floor(min.Y));y<=Math.Min(254,(int)MathF.Floor(max.Y));y++) {
                int value=t.Terrain.GetCellValueFast(x,y,z);var block=BlocksManager.Blocks[Terrain.ExtractContents(value)];
                if(block is BedrockBlock||!block.IsCollidable_(value))continue;
                t.DestroyCell(0,x,y,z,0,true,false);n++;
            }
        }
        return n;
    }
    // armour at arrival: BaseArmour + PerTier x the towers' tiers (1 / 2 / 3) + PerMech x the mechs' BossArmour within 48 m, at most
    // MaxArmour, times the boss's factor (MDRobotKind.BossArmour) (user, 2026-10-21, round 5: early players only have tier-1 towers;
    // was 40000 + 2000 per tower + 4000 per Mercury)
    // 0.14.0 (user, 2026-10-30: "我有很多的炮塔和医疗塔配合，敌方boss还是不堪一击"; bench-fortress: ten tier-2 / 3 towers and three medics
    // killed the devil mech in 70 s and RD21 in 28 s, none of them lost a tower): x 3 per tower, x 2 per mech, the cap 160000 -> 300000
    public const float BaseArmour=36000,MaxArmour=360000,PerTier=3600,   // 0.16.0: +20 % (bench-mix, poise windows)
        PerMech=2;   // per mech: PerMech x Mercury 3000, 复仇女神 4000, 轩辕十四 5000 (MDMechType.BossArmour)
    // 0.14.0 (user, 2026-10-29, "和恶魔机甲轮换": "击杀数到了随机来 5 个 boss 里的一个；诱敌信标也随机"): the beacon and the kill
    // count bring one of these at random. The next one is drawn when the last one arrives, so the countdown and the kill-count
    // warning can name it; it is an attribute of the world's Project.xml (MDBossKills.Load / Save).
    public static readonly string[] Bosses=[MDRobots.Boss,"MDBoss13","MDBoss21","MDBoss08","MDBoss19"];
    public static int Next;
    public static int Draw()=>new System.Random().Next(Bosses.Length);
    public static string NextTemplate=>Bosses[(Next%Bosses.Length+Bosses.Length)%Bosses.Length];
    public static string NextName=>MDRobots.KindOf(NextTemplate).Name;
    // 0.16.0: any number at once (user, 10-31: "可以取消同一个地方只能出现一个boss的限制"); each draws its boss when it starts
    sealed class Pending { public Vector3 at; public float left=-1,blink; public string template; public Action<ComponentMDRobot> arrived; }
    static readonly ConditionalWeakTable<Project,List<Pending>> pending=new();
    public static ComponentMDRobot Alive(Project p)=>p.Entities.Select(e=>e.FindComponent<ComponentMDRobot>()).FirstOrDefault(x=>x?.Kind.Boss==true&&x.Health.Health>0);
    public static bool Counting(Project p)=>pending.TryGetValue(p,out var l)&&l.Count>0;
    public static void Hurry(Project p,float left){if(pending.TryGetValue(p,out var l))foreach(var c in l)if(c.left>left)c.left=left;}   // tests: a shorter countdown
    public static void Announce(Project p,Vector3 at,string text,Color c) {
        foreach(var pl in p.FindSubsystem<SubsystemPlayers>(true).ComponentPlayers)if(Vector3.Distance(pl.ComponentBody.Position,at)<150)pl.ComponentGui.DisplaySmallMessage(text,c,true,true);
    }
    // Right-click the ground with a beacon (MDDeployPatch): it is used up and the countdown starts.
    public static bool PlaceBeacon(ComponentMiner miner,Ray3 ray) {
        var p=miner.Project;var pl=miner.ComponentPlayer;
        var hit=miner.Raycast<TerrainRaycastResult>(ray,RaycastMode.Digging);if(!hit.HasValue||hit.Value.CellFace.Face!=4){MDPlayer.Message(pl,"对着地面右键放下信标。");return false;}
        miner.RemoveActiveTool(1);Start(p,hit.Value.HitPoint()+Vector3.UnitY*.05f,$"诱敌信标已启动：{Countdown:0} 秒后{{0}}会来到这里。");return true;
    }
    // the countdown (a blinking red light at `at`), then the next boss (MDBoss.Next) arrives there: from a beacon or after
    // MDBossKills. `text` names it with {0}.
    // `template`: a boss already drawn (a raid's, named by its countdown); otherwise the next one, and the one after is drawn
    public static void Start(Project p,Vector3 at,string text,Action<ComponentMDRobot> arrived=null,string template=null) {
        var c=new Pending{at=at,left=Countdown,template=template??NextTemplate,arrived=arrived};pending.GetOrCreateValue(p).Add(c);if(template==null)Next=Draw();
        Announce(p,c.at,string.Format(text,MDRobots.KindOf(c.template).Name),new Color(255,120,100));
        p.FindSubsystem<SubsystemAudio>(true).PlaySound(MDMechBrain.Se(7),1,0,c.at,20,false);
    }
    public static void Tick(Project p,float dt) {
        if(!pending.TryGetValue(p,out var l)||l.Count==0)return;
        var w=MDTowerWorld.For(p);
        foreach(var c in l.ToList()) {
            float before=c.left;c.left-=dt;c.blink-=dt;
            if(c.blink<=0){c.blink=c.left<10?.25f:.6f;w.Flash(c.at+Vector3.UnitY*.6f,.8f,new Color(255,50,40),.15f);w.Ring(c.at,2.5f,new Color(255,60,50),.5f);}
            foreach(int s in new[]{20,10,5,3,2,1})if(before>s&&c.left<=s)Announce(p,c.at,$"{MDRobots.KindOf(c.template).Name}还有 {s} 秒到达。",new Color(255,120,100));
            if(c.left>0)continue;
            l.Remove(c);ComponentMDRobot b=null;try{b=Spawn(p,c.at,null,c.template);}finally{c.arrived?.Invoke(b);}
        }
    }
    // Arrives and fights around `at`: a flyer from `arrive` (default 40 m away, 18 m up), RD13 on the ground 40 m away (it walks
    // in), RD19 out of the ground 34-40 m away facing `at`; a creative summon (`arrive` given) right there.
    public static ComponentMDRobot Spawn(Project p,Vector3 at,Vector3? arrive=null,string template=null) {
        template??=MDRobots.Boss;var kind=MDRobots.KindOf(template);
        var rng=new System.Random();float a=(float)(rng.NextDouble()*Math.PI*2);var from=arrive??at+new Vector3(MathF.Sin(a),0,MathF.Cos(a))*40+Vector3.UnitY*18;
        if(arrive==null&&(kind.Id=="RD13"||kind.Id=="RD19"))from=MDRaids.Ground(p.FindSubsystem<SubsystemTerrain>(true),at,a,kind.Id=="RD19"?37:40,rng)??at;
        else if(arrive.HasValue&&kind.Id is "RD13" or "RD19")from=at;
        var e=DatabaseManager.CreateEntity(p,template,true);var body=e.FindComponent<ComponentBody>(true);body.Position=from;
        var face=at-from;if(new Vector2(face.X,face.Z).LengthSquared()>1)body.Rotation=Quaternion.CreateFromAxisAngle(Vector3.UnitY,MathF.Atan2(-face.X,-face.Z));
        var r=e.FindComponent<ComponentMDRobot>(true);
        var units=p.Entities.Select(x=>x.FindComponent<ComponentMDUnit>()).Where(u=>u!=null&&!u.Legacy&&!u.Destroyed&&Vector3.Distance(u.Body.Position,at)<48).ToList();
        int towers=units.Count(u=>u.Tower!=null),mechs=units.Count(u=>u.Mech!=null),tiers=units.Where(u=>u.Tower!=null).Sum(MDRaids.Weight);
        r.Boss.SetArmour(MathF.Round(Math.Clamp(BaseArmour+PerTier*tiers+PerMech*units.Where(u=>u.Mech!=null).Sum(u=>u.Mech.Type.BossArmour),BaseArmour,MaxArmour)*kind.BossArmour));r.Boss.EscortSize=Math.Clamp(2+(towers+2*mechs)/5,2,8);
        p.AddEntity(e);r.Boss.Home=at;r.RaidGoal=at;
        Announce(p,at,$"{kind.Name}来了！它感应到 {towers} 座炮塔，耐久强化到 {r.Boss.Armour:0}。{(kind.Id=="RD17"?"":MDBossState.Hint(kind.Id))}",new Color(255,90,90));
        MDRobots.Sound(p,31,from,1);return r;
    }
    // An escort wave: robots 40-50 m from the beacon (the boss chooses which, MDBossState.EscortTemplate); they go for the nearest
    // tower or Mercury (or, with none within 96 m, to the beacon) and are not packed away while they have that to do.
    public static void Escort(ComponentMDRobot boss,int size) {
        var p=boss.Project;var home=boss.Boss.Home??boss.Body.Position;var rng=new System.Random();var terrain=p.FindSubsystem<SubsystemTerrain>(true);int sent=0;float dir=(float)(rng.NextDouble()*Math.PI*2);
        for(int i=0;i<size;i++) {
            var at=MDRaids.Ground(terrain,home,dir+(float)(rng.NextDouble()-.5)*1.4f,40+(float)rng.NextDouble()*10,rng);if(at==null)continue;
            var e=DatabaseManager.CreateEntity(p,boss.Boss.EscortTemplate(i,rng,p),true);var r=e.FindComponent<ComponentMDRobot>(true);
            e.FindComponent<ComponentBody>(true).Position=at.Value+(r.Kind.Fly>0?Vector3.UnitY*3:Vector3.Zero);r.Raider=true;r.RaidGoal=home;e.FindComponent<ComponentSpawn>(true).AutoDespawn=false;p.AddEntity(e);sent++;   // kept while busy (ComponentMDRobot.Update)
        }
        boss.Boss.EscortsSent+=sent;
        if(sent>0)Announce(p,home,$"{boss.Kind.Name}呼叫了 {sent} 台护卫机兵！",new Color(255,120,100));
    }
    // ModLoader hook ProcessAttackment (MDModLoader): the boss's own factor and armour phase (MDBossState.Damage). No knockback
    // and no stun: every hit stuns a creature 0.2 s, and a stunned creature's locomotion stops flying and turns gravity back on, so
    // under tower fire the boss sank to the ground (lab trace, 2026-10-20). The drones (0.14.0) the same: under an anti-air tower
    // they kept dropping to the ground (lab, 2026-10-30).
    public static void Modify(Attackment a) {
        if(a.Target?.FindComponent<ComponentMDRobot>() is not {} r||r.Boss==null&&r.Kind.Fly<=0)return;
        a.ImpulseFactor=0;a.StunTimeSet=null;a.StunTimeAdd=0;
        if(r.Boss!=null&&a.Attacker?.FindComponent<ComponentMDUnit>() is {} u)   // 0.16.0: heavy shots shake it (poise)
            r.Boss.AddPoise(u.Tower?.attack.stagger==true?MDBossState.PoiseHeavy:u.Mech?.Weapon==MDMechWeapon.Cannon?MDBossState.PoiseMechCannon:0,u.Mech!=null);
        r.Boss?.Damage(a);
    }
}

// 0.12.0 (players via the user, 2026-10-25: "恶魔机甲这种boss应该固定刷新，比如对机器人总击杀数达到多少刷新一只"): every
// MDSettings.BossKills robots destroyed in a world (survival modes; Creative has no spawns and summons there do not count) bring
// the next boss (0.14.0, MDBoss.Next): once no boss is alive or on its way, a 30 s countdown starts at the tower nearest to the
// player closest to the last kill (within 80 m; else at that player) and it arrives there as from a beacon. The world's count and
// its next boss are attributes of its Project.xml (MDModLoader.ProjectXmlLoad / ProjectXmlSave; one world is open at a time).
public static class MDBossKills {
    public const string Attribute="MechDefenseBossKills",NextAttribute="MechDefenseBossNext";
    public static int Count;                 // robots destroyed in this world since the last boss came this way
    static Vector3 last;                     // where the last one was destroyed
    public static string LastStart="";       // tests
    public static void Load(System.Xml.Linq.XElement project) {
        Count=int.TryParse((string)project?.Attribute(Attribute),out int n)&&n>=0?n:0;
        MDBoss.Next=int.TryParse((string)project?.Attribute(NextAttribute),out int k)&&k>=0&&k<MDBoss.Bosses.Length?k:MDBoss.Draw();
    }
    public static void Save(System.Xml.Linq.XElement project){project?.SetAttributeValue(Attribute,Count);project?.SetAttributeValue(NextAttribute,MDBoss.Next);}
    public static void Killed(ComponentMDRobot r) {
        if(r.Kind.Boss||MDSettings.BossKills<=0||!MDRobots.Spawns(r.Project))return;
        Count++;last=r.Body.Position;
        if(MDSettings.BossKills>=20&&MDSettings.BossKills-Count==10)MDBoss.Announce(r.Project,last,$"再击毁 10 台机兵，{MDBoss.NextName}就会被惊动。",new Color(255,170,120));
    }
    public static void Tick(Project p) {
        int need=MDSettings.BossKills;
        if(need<=0||Count<need||!MDRobots.Spawns(p))return;   // 0.16.0: also while another boss is about (user: one boss at a time lifted)
        var player=p.FindSubsystem<SubsystemPlayers>(true).ComponentPlayers.Where(pl=>pl.ComponentHealth.Health>0).OrderBy(pl=>Vector3.Distance(pl.ComponentBody.Position,last)).FirstOrDefault();
        if(player==null)return;
        var pos=player.ComponentBody.Position;
        var tower=p.Entities.Select(e=>e.FindComponent<ComponentMDUnit>()).Where(u=>u?.Tower!=null&&!u.Legacy&&!u.Destroyed&&Vector3.Distance(u.Body.Position,pos)<80).OrderBy(u=>Vector3.Distance(u.Body.Position,pos)).FirstOrDefault();
        var at=tower?.Body.Position??pos+Vector3.UnitY*.05f;
        LastStart=tower!=null?"tower "+tower.Tower.id:"player";Count=0;
        MDBoss.Start(p,at,$"已击毁 {need} 台机兵，{{0}}被惊动了：{MDBoss.Countdown:0} 秒后{(tower!=null?"来到你的炮塔阵地":"来到你这里")}！");
    }
}

// The bosses' and the drones' orders, set again right before locomotion runs (see MDBossState.Fly / Walk).
[HarmonyLib.HarmonyPatch(typeof(ComponentLocomotion),nameof(ComponentLocomotion.Update))]
public static class MDBossFlyOrder {
    public static void Prefix(ComponentLocomotion __instance) {
        if(__instance.Entity.FindComponent<ComponentMDRobot>() is not {} r||r.Health.Health<=0)return;
        if((r.Boss!=null?r.Boss.Wanted:r.FlyWanted) is {} w)__instance.FlyOrder=w;
        if(r.Boss?.WalkWanted is {} k)__instance.WalkOrder=k;
    }
}
