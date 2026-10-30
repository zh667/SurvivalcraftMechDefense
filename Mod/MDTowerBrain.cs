using Engine;
using GameEntitySystem;

namespace Game;

// Tower behaviour: target choice, aim, firing per attack type, shells landing and lingering zones.
// Numbers come from towers.json (Endfield ranges/intervals; damage rescaled to Survivalcraft health).
public static class MDTowerBrain {
    const float Gravity=12;
    static readonly System.Random random=new();
    static MDTowerWorld World(ComponentMDUnit u)=>MDTowerWorld.For(u.Project);

    public static Matrix[] Pose(ComponentMDUnit u)=>MDTowerLibrary.Pose(u.Tower,u.Body.Matrix,u.AimYaw-u.Yaw,u.TowerPitch,u.RecoilOffsets());
    public static Vector3 Muzzle(ComponentMDUnit u,int index=0)=>MDTowerLibrary.Muzzle(u.Tower,Pose(u),index);
    static Vector3 Aim(ComponentMDUnit u)=>u.Tower.muzzles.Length>0?Muzzle(u,0):u.Body.Position+Vector3.UnitY*2;

    public static void Update(ComponentMDUnit u,float dt) {
        var d=u.Tower;var a=d.attack;var w=World(u);
        u.Cooldown=Math.Max(0,u.Cooldown-dt);u.TickRecoil(dt);
        if(d.spinParts.Length>0){u.SpinRate=Math.Clamp(u.SpinRate+((d.spinIdle||u.Target!=null)&&!u.Destroyed?dt*2.5f:-dt*.7f),0,1);u.Spin=(u.Spin+u.SpinRate*MathUtils.DegToRad(d.spinSpeed)*dt)%(2*MathF.PI);}
        if(u.Destroyed){w.Wreck(u.Body.Position+Vector3.UnitY*(d.bounds[1][1]*.6f),d.fx.scale+(d.bounds[1][1]>4?1:0));return;}
        if(a.type=="heal"){Heal(u,dt);return;}
        if(a.type=="radar"){Radar(u);return;}
        u.ScanTimer-=dt;
        if(u.ScanTimer<=0){u.ScanTimer=.25f;u.Target=FindTarget(u);}
        var target=u.Target;
        if(target!=null&&!MDCombat.Living(target))target=u.Target=null;
        if(u.Channel>0){if(TurningSprayer(d)&&target!=null)Steer(u,target,dt,out _);Channel(u,dt);return;}   // a turning sprayer keeps following while it sprays
        if(target==null){if(d.HasYaw)u.AimPitch=MathUtils.Lerp(u.AimPitch,d.restPitch,dt*1.5f);return;}
        var tc=Steer(u,target,dt,out float wantPitch);
        bool aligned=!d.HasYaw||Math.Abs(MDCombat.AngleDelta(u.AimYaw,MathF.Atan2(-(tc-u.Body.Position).X,-(tc-u.Body.Position).Z)))<.12f;
        if(a.type=="shell"&&LaidGun(d))aligned&=Math.Abs(u.AimPitch-wantPitch)<.04f;   // a laid gun fires along its barrel: wait until it is elevated
        if(u.Cooldown<=0&&aligned)Fire(u,target,tc);
    }

    // ---------------------------------------------------------------- targets
    static bool InArea(ComponentMDUnit u,Vector3 p) {
        var a=u.Tower.attack;var d=p-u.Body.Position;float h=new Vector2(d.X,d.Z).Length();float range=u.Range;
        if(h>range||h<a.minRange||Math.Abs(d.Y)>Math.Max(6,range*.6f))return false;
        if(!a.directional)return true;
        var f=u.Body.Matrix.Forward;f.Y=0;f=Vector3.Normalize(f);var r=new Vector3(-f.Z,0,f.X);
        float along=Vector3.Dot(d,f),side=Vector3.Dot(d,r);return along>0&&along<=a.range&&Math.Abs(side)<=a.width*.5f;
    }
    public static bool Sight(ComponentMDUnit u,Vector3 from,ComponentBody target)=>MDCombat.Trace(u,from,MDCombat.Center(target),out _)==target;
    // The nearest enemy in the area (or the most threatening one) that the tower can see. Candidates are ranked first and
    // traced in that order, so the longer ranges do not cost a line-of-sight trace per creature in the area.
    static ComponentBody FindTarget(ComponentMDUnit u) {
        var a=u.Tower.attack;var bodies=new DynamicArray<ComponentBody>();
        u.Project.FindSubsystem<SubsystemBodies>(true).FindBodiesAroundPoint(new(u.Body.Position.X,u.Body.Position.Z),u.Range+2,bodies);
        var found=new List<(float score,ComponentBody body)>();
        foreach(var b in bodies)
            if(MDCombat.IsEnemy(u,b)&&!(a.groundOnly&&Airborne(b))&&InArea(u,MDCombat.Center(b)))found.Add(((a.threatFirst?-Threat(b):Vector3.DistanceSquared(b.Position,u.Body.Position))+(a.airFirst&&!Airborne(b)?1e7f:0),b));
        found.Sort((x,y)=>x.score.CompareTo(y.score));
        var from=Aim(u);
        foreach(var (_,b) in found)if(!a.needsSight||Sight(u,from,b))return b;
        return null;
    }
    // Bosses (high AttackResilience) shrug off stun and freeze, as Endfield's elite/boss enemies do.
    static float Resilience(ComponentBody b)=>b.Entity.FindComponent<ComponentHealth>()?.AttackResilience??b.Entity.FindComponent<ComponentDamage>()?.AttackResilience??0;
    // 0.14.0: our robots by what they are (the bosses are immune, every other robot can be held), other creatures by resilience. The
    // threshold (500) predates the robots: every robot has 600 or more, so the control towers held none of them (bench, 2026-10-30)
    static bool Holdable(ComponentBody b,MDTowerAttack a)=>b.Entity.FindComponent<ComponentMDRobot>() is {} r?!r.Kind.Boss:Resilience(b)<a.bossResilience;
    static float Threat(ComponentBody b){var h=b.Entity.FindComponent<ComponentHealth>();return h==null?b.Entity.FindComponent<ComponentDamage>()?.Hitpoints??0:h.Health*h.AttackResilience;}

    // ---------------------------------------------------------------- aim
    public static bool LeadEnabled=true;   // lab switch: hits on a running target with and without lead
    // The target's velocity from its own movement, smoothed over about 0.1 s. ComponentBody.Velocity is not enough: a mod
    // creature moved by setting its position has none, and a walking creature's includes its bobbing.
    // Turn and elevate towards the target's lead point; returns that point.
    static Vector3 Steer(ComponentMDUnit u,ComponentBody target,float dt,out float wantPitch) {
        var d=u.Tower;Track(u,target,dt);var tc=AimPoint(u,target);
        if(d.HasYaw) {
            var dd=tc-u.Body.Position;
            float want=MathF.Atan2(-dd.X,-dd.Z);u.AimYaw=MDCombat.AngleTowards(u.AimYaw,want,MathUtils.DegToRad(d.yawSpeed)*dt);
        }
        wantPitch=DesiredPitch(u,tc);u.AimPitch=MDCombat.AngleTowards(u.AimPitch,wantPitch,MathUtils.DegToRad(d.pitchSpeed)*dt);
        return tc;
    }
    // A flamethrower or frost sprayer on a turret (0.10.0: 冰冻塔, 火焰塔): it hits a band of attack.width along where its
    // nozzles point, not a fixed rectangle in front of the block like the fixed sprayers.
    static bool TurningSprayer(MDTowerDef d)=>d.HasYaw&&!d.attack.directional&&d.attack.type is "flame" or "jet";
    static void Track(ComponentMDUnit u,ComponentBody target,float dt) {
        if(u.Tracked!=target){u.Tracked=target;u.TrackedAt=target.Position;u.TrackedVelocity=target.Velocity;return;}
        if(dt<=0)return;
        var v=(target.Position-u.TrackedAt)/dt;u.TrackedAt=target.Position;
        if(v.LengthSquared()<900)u.TrackedVelocity=Vector3.Lerp(u.TrackedVelocity,v,Math.Min(1,dt*10));   // a jump of 30 m/s+ is a teleport
    }
    static bool Bullet(MDTowerAttack a)=>a.type is "bolt" or "snipe";
    // Where to aim. Gunfire aims at the head of a creature whose head is its weak spot, everything else at the body centre.
    // Bolts and shells aim where the target will be when they arrive: its ground velocity times the flight time.
    public static Vector3 AimPoint(ComponentMDUnit u,ComponentBody target) {
        var a=u.Tower.attack;
        var p=Bullet(a)&&HeadIsWeak(target)&&target.Entity.FindComponent<ComponentCreatureModel>() is {} model?model.EyePosition:MDCombat.Center(target);
        if(!LeadEnabled||a.type is not ("bolt" or "shell"))return p;
        var v=u.Tracked==target?u.TrackedVelocity:target.Velocity;if(target.StandingOnValue.HasValue||target.StandingOnBody!=null)v.Y=0;   // on the ground: no vertical lead
        if(v.LengthSquared()>400)v=Vector3.Normalize(v)*20;
        var from=Aim(u);var q=p;
        for(int i=0;i<3;i++)q=p+v*FlightTime(u,from,q);
        return q;
    }
    static float FlightTime(ComponentMDUnit u,Vector3 from,Vector3 to) {
        var a=u.Tower.attack;var d=to-from;if(a.type!="shell")return d.Length()/Math.Max(1,a.speed);
        float h=new Vector2(d.X,d.Z).Length(),angle=Ballistic(a.speed,h,d.Y,a.highArc)??.785f;
        return h/Math.Max(.1f,a.speed*MathF.Cos(angle));
    }
    // A mod makes the head the weak spot when it scales gunfire by HeadHitDamageMultiplier / NonHeadHitDamageMultiplier
    // (the ghouls' ComponentBulletDefender: body hits x0.1-0.5, head hits up to x1.3). Read once per creature.
    static readonly Dictionary<Type,(System.Reflection.PropertyInfo head,System.Reflection.PropertyInfo body)> weakSpotProbes=[];
    static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Entity,object> weakHead=new();
    static bool HeadIsWeak(ComponentBody b) {
        if(weakHead.TryGetValue(b.Entity,out var known))return (bool)known;
        bool weak=false;
        foreach(var c in b.Entity.Components) {
            var t=c.GetType();
            if(!weakSpotProbes.TryGetValue(t,out var w)) {
                var h=t.GetProperty("HeadHitDamageMultiplier");var n=t.GetProperty("NonHeadHitDamageMultiplier");
                weakSpotProbes[t]=w=h?.PropertyType==typeof(float)&&n?.PropertyType==typeof(float)?(h,n):(null,null);
            }
            if(w.head!=null){weak=(float)w.head.GetValue(c)>(float)w.body.GetValue(c);break;}
        }
        weakHead.AddOrUpdate(b.Entity,weak);return weak;
    }
    static float DesiredPitch(ComponentMDUnit u,Vector3 tc) {
        var d=u.Tower;if(d.pitchFixed||d.pitchNode<0)return d.restPitch;
        var pivot=MDTowerLibrary.Pose(d,u.Body.Matrix,u.AimYaw-u.Yaw,0,null)[d.pitchNode].Translation;var dd=tc-pivot;float h=new Vector2(dd.X,dd.Z).Length();
        float p=d.attack.type=="shell"?Ballistic(d.attack.speed,h,dd.Y,d.attack.highArc)??.6f:MathF.Atan2(dd.Y,h);
        return Math.Clamp(p,d.PitchMin,d.PitchMax);
    }
    static float? Ballistic(float v,float x,float y,bool high) {
        float v2=v*v,disc=v2*v2-Gravity*(Gravity*x*x+2*y*v2);if(disc<0||x<.01f)return null;
        float r=MathF.Sqrt(disc);return MathF.Atan((v2+(high?r:-r))/(Gravity*x));
    }
    static bool LaidGun(MDTowerDef d)=>d.pitchNode>=0&&!d.pitchFixed;
    // A gun that elevates fires along its barrel: the angle is the barrel's, only the charge (muzzle speed) is solved
    // so the arc ends at the target. Out of reach it fires the strongest charge, which falls short.
    static Vector3 BarrelLaunch(ComponentMDUnit u,Vector3 from,Vector3 dir,Vector3 to) {
        var dd=to-from;float x=new Vector2(dd.X,dd.Z).Length(),h=new Vector2(dir.X,dir.Z).Length();
        float denom=2*h*h*(x*dir.Y/Math.Max(h,1e-4f)-dd.Y),vmax=u.Tower.attack.speed*1.5f;   // v^2 = g x^2 / (2 cos^2 t (x tan t - y))
        return dir*(denom>1e-4f?Math.Min(vmax,MathF.Sqrt(Gravity*x*x/denom)):vmax);
    }
    static Vector3 LaunchVelocity(ComponentMDUnit u,Vector3 from,Vector3 to) {
        var a=u.Tower.attack;var dd=to-from;var hd=new Vector2(dd.X,dd.Z);float h=hd.Length();
        bool high=a.highArc;
        float angle=Ballistic(a.speed,h,dd.Y,high)??.785f;var dir2=h>.01f?hd/h:Vector2.Zero;
        float v=a.speed;if(Ballistic(a.speed,h,dd.Y,high)==null)v=MathF.Sqrt(Gravity*Math.Max(1,h));   // out of reach: 45 deg lob
        return new Vector3(dir2.X*MathF.Cos(angle),MathF.Sin(angle),dir2.Y*MathF.Cos(angle))*v;
    }

    // ---------------------------------------------------------------- fire
    static void Sound(ComponentMDUnit u,string[] list,Vector3 at,float volume=.6f) {
        if(list==null||list.Length==0)return;
        u.Project.FindSubsystem<SubsystemAudio>(true).PlaySound(list[random.Next(list.Length)],volume,MathUtils.Lerp(-.05f,.05f,(float)random.NextDouble()),at,12,false);
    }
    static void Fire(ComponentMDUnit u,ComponentBody target,Vector3 tc) {
        var d=u.Tower;var a=d.attack;var w=World(u);
        int shot=++u.ShotCount;bool strong=a.enhancedEvery>0&&shot%a.enhancedEvery==0;
        int mi=d.muzzles.Length>0?(shot-1)%d.muzzles.Length:0;var pose=Pose(u);
        var muzzle=d.muzzles.Length>0?MDTowerLibrary.Muzzle(d,pose,mi):u.Body.Position+Vector3.UnitY*2;
        var dir=Vector3.Normalize(tc-muzzle);
        if(a.needsSight&&a.type!="shell"&&!Sight(u,muzzle,target)){u.Target=null;return;}
        u.Cooldown=a.interval;
        switch(a.type) {
            case "bolt":
                u.Kick(mi);w.Muzzle(d,muzzle,dir,strong);if((shot-1)%Math.Max(1,a.soundEvery)==0)Sound(u,d.fireSounds,muzzle);
                w.LaunchShell(u,muzzle,dir*a.speed,0,a.power*(strong?a.enhancedPower:1)*(a.groundFactor<1&&!Airborne(target)?a.groundFactor:1),strong?a.enhancedSplash:a.splash,strong);break;
            case "shell":
                u.Kick(mi);var aimAt=new Vector3(tc.X,target.Position.Y+.1f+tc.Y-MDCombat.Center(target).Y,tc.Z);   // the ground under the lead point
                var vel=LaidGun(d)?BarrelLaunch(u,muzzle,MDTowerLibrary.MuzzleForward(d,pose,mi),aimAt):LaunchVelocity(u,muzzle,aimAt);
                w.Muzzle(d,muzzle,Vector3.Normalize(vel),true);Sound(u,d.fireSounds,muzzle);
                w.LaunchShell(u,muzzle,vel,Gravity,a.power,a.splash,false);break;
            case "missile":   // 导弹塔 (0.10.0), after the original: one missile per shot from the next tube; it leaves slowly along the tube, speeds up, homes in and always hits
                u.Kick(mi);var fwd=MDTowerLibrary.MuzzleForward(d,pose,mi);w.Muzzle(d,muzzle,fwd,false);Sound(u,d.fireSounds,muzzle);
                w.LaunchMissile(u,d,muzzle,fwd*6,target,.12f,70,a.speed,5,a.power,a.splash);break;
            case "snipe":
                u.Kick(mi);w.Muzzle(d,muzzle,dir,true);Sound(u,d.fireSounds,muzzle,.8f);
                var hb=MDCombat.Trace(u,muzzle,muzzle+dir*(a.range+4),out var hp);
                w.AddBeam(muzzle,hp,.035f*d.fx.scale,MDTowerWorldColor(d.fx.color),.18f,false);w.Impact(d,hp,dir,true);
                if(hb!=null)Hit(u,hb,a.power,hp,dir,true);Sound(u,d.hitSounds,hp,.5f);break;
            case "beam": case "chain": case "flame": case "jet":
                u.Channel=a.charge+a.duration;u.ChannelTarget=target;u.ChannelTick=0;Sound(u,a.charge>0?d.chargeSounds:d.fireSounds,muzzle);
                if(a.type=="chain")ChainStrike(u,muzzle,target);
                break;
            case "pulse":
                Pulse(u);Sound(u,d.fireSounds,u.Body.Position);break;
        }
    }
    static Color MDTowerWorldColor(float[] c)=>new(c[0],c[1],c[2]);

    // channelled attacks: laser charge+beam, flamethrower, water jet, lightning afterglow
    static void Channel(ComponentMDUnit u,float dt) {
        var d=u.Tower;var a=d.attack;var w=World(u);float before=u.Channel;u.Channel=Math.Max(0,u.Channel-dt);
        var target=u.ChannelTarget;var pose=Pose(u);var muzzle=d.muzzles.Length>0?MDTowerLibrary.Muzzle(d,pose,0):u.Body.Position+Vector3.UnitY*2;
        float elapsed=a.charge+a.duration-u.Channel;
        if(a.type=="beam") {
            var c=MDTowerWorldColor(d.fx.color);
            if(elapsed<a.charge){float k=elapsed/a.charge;w.Flash(muzzle,.15f+.45f*k*d.fx.scale,c*(.5f+.5f*k),.05f);return;}
            if(target==null||!MDCombat.Living(target))return;
            var dir=Vector3.Normalize(MDCombat.Center(target)-muzzle);var hb=MDCombat.Trace(u,muzzle,muzzle+dir*(a.range+3),out var hp);
            w.AddBeam(muzzle,hp,.18f*d.fx.scale,c,.06f,false);w.Flash(muzzle,.5f*d.fx.scale,c,.05f);w.Flash(hp,.4f*d.fx.scale,c,.05f);w.Sparks(hp,-dir,2,MDTowerWorldColor(d.fx.color2),4,.8f,.3f,.04f);
            if(before>a.duration&&u.Channel<=a.duration)Sound(u,d.fireSounds,muzzle,.8f);
            u.ChannelTick-=dt;if(u.ChannelTick<=0&&hb!=null){u.ChannelTick=.1f;Hit(u,hb,a.power*.1f/Math.Max(.1f,a.duration),hp,dir);}
        } else if(a.type=="flame"||a.type=="jet") {
            for(int i=0;i<d.muzzles.Length;i++) {
                var m=MDTowerLibrary.Muzzle(d,pose,i);var f=MDTowerLibrary.MuzzleForward(d,pose,i);
                w.Spray(d.fx.kind=="frost"?"frost":a.type,m,f,MDTowerWorldColor(d.fx.color),MDTowerWorldColor(d.fx.color2),a.range);
            }
            u.ChannelTick-=dt;
            if(u.ChannelTick<=0){u.ChannelTick=a.type=="flame"?.25f:.2f;AreaTick(u);}
        } else if(a.type=="chain"&&u.Channel<=0)u.ChannelTarget=null;
        if(u.Channel<=0)u.ChannelTarget=null;
    }
    // A creature in the air: one that can fly and is not standing on anything (birds, flying mod creatures). Anti-air towers
    // (attack.airFirst) pick these first and do attack.groundFactor of their damage to anything else.
    public static bool Airborne(ComponentBody b){var loc=b.Entity.FindComponent<ComponentLocomotion>();return loc!=null&&loc.FlySpeed>0&&!b.StandingOnValue.HasValue&&b.ImmersionFactor<=0;}
    // Radar (0.10.0): every interval, every working tower within range (attack.links > 0: only that many, nearest first) gets
    // range x attack.boost until a little after the next pulse (several radars: the strongest boost). Silent and without link
    // lines (user, 10-23: with many radars the lines and pings were too busy).
    static void Radar(ComponentMDUnit u) {
        var d=u.Tower;var a=d.attack;if(u.Cooldown>0)return;u.Cooldown=a.interval;
        var p=u.Body.Position;var bodies=new DynamicArray<ComponentBody>();u.Project.FindSubsystem<SubsystemBodies>(true).FindBodiesAroundPoint(new(p.X,p.Z),a.range+1,bodies);
        var towers=bodies.Select(b=>b.Entity.FindComponent<ComponentMDUnit>()).Where(t=>t!=null&&t!=u&&t.Kind=="Turret"&&!t.Destroyed&&t.Tower!=null&&t.Tower.attack.type is not ("radar" or "heal")
            &&Vector3.DistanceSquared(t.Body.Position,p)<=a.range*a.range).OrderBy(t=>Vector3.DistanceSquared(t.Body.Position,p)).Take(a.links>0?a.links:int.MaxValue).ToList();
        double now=u.Clock.GameTime;
        foreach(var t in towers) {
            if(t.RangeBoostUntil<now||a.boost>=t.RangeBoost)t.RangeBoost=a.boost;
            t.RangeBoostUntil=Math.Max(t.RangeBoostUntil,now+a.interval+.5);
        }
        u.ShotCount+=towers.Count>0?1:0;
    }
    static void AreaTick(ComponentMDUnit u) {
        var d=u.Tower;var a=d.attack;var w=World(u);var bodies=new DynamicArray<ComponentBody>();var p=u.Body.Position;
        u.Project.FindSubsystem<SubsystemBodies>(true).FindBodiesAroundPoint(new(p.X,p.Z),a.range+2,bodies);
        var f=u.Body.Matrix.Forward;bool turning=TurningSprayer(d);
        if(turning&&d.muzzles.Length>0)f=MDTowerLibrary.MuzzleForward(d,Pose(u),0);
        f.Y=0;f=Vector3.Normalize(f);var side=new Vector3(-f.Z,0,f.X);
        foreach(var b in bodies) {
            var c=MDCombat.Center(b);
            if(!MDCombat.IsEnemy(u,b)||!InArea(u,c))continue;
            if(turning){var dd=c-p;float along=Vector3.Dot(dd,f);if(along<0||Math.Abs(Vector3.Dot(dd,side))>a.width*.5f+along*.15f)continue;}   // the band widens a little with distance
            if(a.type=="flame"){Hit(u,b,a.power*.25f/Math.Max(.25f,a.duration),c,f);b.Entity.FindComponent<ComponentOnFire>()?.SetOnFire(null,a.burn);}
            else {var push=f*a.knockback+Vector3.UnitY*a.knockback*.25f;if(a.knockback>0&&b.Mass<=a.pushMaxMass)b.Velocity+=push*.2f;if(a.power>0)Hit(u,b,a.power*.2f/Math.Max(.2f,a.duration),c,f);if(a.slow>0)w.Slow(b,a.slow,a.slowTime,MDTowerWorldColor(d.fx.color2));}
        }
    }
    static void ChainStrike(ComponentMDUnit u,Vector3 from,ComponentBody first) {
        var d=u.Tower;var a=d.attack;var w=World(u);var c=MDTowerWorldColor(d.fx.color);var hit=new HashSet<ComponentBody>();var cur=first;var p=from;
        w.Flash(from,.45f*d.fx.scale,c,.12f);
        for(int i=0;i<=a.bounces&&cur!=null;i++) {
            var tc=MDCombat.Center(cur);w.AddBeam(p,tc,.11f*d.fx.scale,c,.28f,true);w.AddBeam(p,tc,.05f*d.fx.scale,c,.18f,true);w.Impact(d,tc,Vector3.Normalize(tc-p),i==0);
            Hit(u,cur,a.power*(i==0?1:.7f),tc,Vector3.Normalize(tc-p));hit.Add(cur);
            if(i==0&&a.zone>0)w.AddZone(u,"orb",cur.Position,a.zone,a.zoneTime,a.zonePower);
            p=tc;var bodies=new DynamicArray<ComponentBody>();
            u.Project.FindSubsystem<SubsystemBodies>(true).FindBodiesAroundPoint(new(tc.X,tc.Z),a.bounceRange,bodies);
            cur=bodies.Where(b=>!hit.Contains(b)&&MDCombat.IsEnemy(u,b)&&Vector3.DistanceSquared(MDCombat.Center(b),tc)<a.bounceRange*a.bounceRange&&MDCombat.Trace(u,tc,MDCombat.Center(b),out _)==b)
                .OrderBy(b=>Vector3.DistanceSquared(b.Position,tc)).FirstOrDefault();
        }
        Sound(u,d.hitSounds,first.Position,.5f);
    }
    static void Pulse(ComponentMDUnit u) {
        var d=u.Tower;var a=d.attack;var w=World(u);var c=MDTowerWorldColor(d.fx.color);var p=u.Body.Position;
        w.Ring(p,a.range,c,.6f);w.Ring(p,a.range*.6f,c,.45f,.8f);
        var top=d.muzzles.Length>0?Muzzle(u,0):p+Vector3.UnitY*2;w.Flash(top,.8f,c,.15f);
        var bodies=new DynamicArray<ComponentBody>();u.Project.FindSubsystem<SubsystemBodies>(true).FindBodiesAroundPoint(new(p.X,p.Z),a.range+1,bodies);
        foreach(var b in bodies) {
            if(!MDCombat.IsEnemy(u,b)||!InArea(u,MDCombat.Center(b)))continue;
            if(Holdable(b,a))w.Stun(b,a.stun);if(a.power>0)Hit(u,b,a.power,MDCombat.Center(b),Vector3.Normalize(b.Position-p+Vector3.UnitY*.01f));
        }
    }
    static void Heal(ComponentMDUnit u,float dt) {
        var d=u.Tower;var a=d.attack;var w=World(u);u.Cooldown=Math.Max(0,u.Cooldown);
        if(u.Cooldown>0)return;
        var bodies=new DynamicArray<ComponentBody>();var p=u.Body.Position;bool any=false;
        u.Project.FindSubsystem<SubsystemBodies>(true).FindBodiesAroundPoint(new(p.X,p.Z),a.range+1,bodies);
        foreach(var b in bodies) {
            if(Vector3.DistanceSquared(b.Position,p)>a.range*a.range)continue;
            var player=b.Entity.FindComponent<ComponentPlayer>();var unit=b.Entity.FindComponent<ComponentMDUnit>();
            if(MDMedic.Left(u)<=0)break;   // 0.15.0: the store is empty (MDMedic)
            if(player!=null&&player.ComponentHealth.Health<1&&player.ComponentHealth.Health>0){float want=Math.Min(a.power,1-player.ComponentHealth.Health),got=MDMedic.Take(u,want*MDMedic.PlayerPoints);if(got>0){player.ComponentHealth.Heal(got/MDMedic.PlayerPoints);w.Heal(b.Position);any=true;}}
            // not another medic tower (players via the user, 2026-10-29: two medics healing each other were all but invulnerable);
            // 0.14.0: not a tower a boss hit in the last MDBoss.HealBlock seconds, and once per interval however many medics reach it
            else if(unit!=null&&unit!=u&&(unit.Kind=="Turret"&&unit.Tower?.attack.type!="heal"||unit.Mech!=null)&&!unit.Destroyed&&unit.Damage.Hitpoints<1&&w.CanHeal(b)){float pts=unit.Tower?.hp??unit.Damage.AttackResilience,want=Math.Min(a.power,1-unit.Damage.Hitpoints),got=MDMedic.Take(u,want*pts);
                if(got>0){unit.Damage.Hitpoints=Math.Min(1,unit.Damage.Hitpoints+got/pts);w.Healed(b,a.interval);w.Heal(b.Position);any=true;}}   // towers and, since 0.9.0 round 5, Mercury
        }
        u.Cooldown=any?a.interval:.5f;
        if(any){w.Ring(p,a.range,MDTowerWorldColor(d.fx.color),.8f);w.Flash(Muzzle(u,0),.5f,MDTowerWorldColor(d.fx.color),.2f);Sound(u,d.fireSounds,p,.5f);}
    }

    // ---------------------------------------------------------------- damage
    // Gunfire (bolts, sniper rounds) is a ProjectileAttackment; explosions, beams, lightning, flames, jets and poison are
    // MDTowerAttackment, so a mod that cuts bullet damage (the ghouls' body-hit multiplier) does not cut them.
    // Since 0.9.0 the non-bullet hits carry a damage channel (the devil mech takes full damage only from its current weak
    // channel, MDBoss): kinetic (bolts, sniper rounds, water jets, melee), explosion (shells, missiles, grenades, flames,
    // burning ground) or energy (beams, lightning, sonic pulses, poison and frost fields). Mercury's weapon kits multiply its damage.
    // 0.15.0: `element` (default: the tower's or mech weapon's damage type) goes through resistances, statuses and reactions
    // (MDTowerWorld.Elemental); MDElement.None for the secondary hits of a reaction.
    public static void Hit(ComponentMDUnit u,ComponentBody b,float power,Vector3 point,Vector3 dir,bool bullet=false,string channel=null,MDElement? element=null) {
        if(b==null||!MDCombat.IsEnemy(u,b)||power<=0)return;
        power*=World(u).DamageFactor(b);
        if(u.Mech!=null)power*=MDMechState.KitFactor(u.Mech.Kit)*u.Mech.Type.Damage;   // weapon kit x the mech's own factor
        var el=element??MDElements.Of(u);int reactions=MDElements.Count;power=World(u).Elemental(u,b,el,power);
        MDTowerWorld.HitElement=el;MDTowerWorld.HitReacted=MDElements.Count>reactions;   // for its damage number (MDDamageNumbers.cs)
        ComponentMiner.AttackBody(bullet?new ProjectileAttackment(b.Entity,u.Entity,point,dir,power,null):new MDTowerAttackment(b.Entity,u.Entity,point,dir,power){Channel=channel??Channel(u)});
        MDTowerWorld.HitElement=MDElement.None;MDTowerWorld.HitReacted=false;
    }
    public static string Channel(ComponentMDUnit u)=>(u.Tower?.attack.type??(u.Mech?.Weapon==MDMechWeapon.Laser?"beam":"bolt")) switch{"shell" or "flame"=>"explosion","beam" or "chain" or "pulse" or "heal"=>"energy",_=>"kinetic"};
    public static void ShellLanded(ComponentMDUnit u,MDTowerDef d,Vector3 point,Vector3 dir,ComponentBody hit,float power,float splash,bool enhanced) {
        var a=d.attack;var w=World(u);
        Sound(u,d.hitSounds,point,.55f);
        if(a.type=="bolt"&&splash<=0){w.Impact(d,point,dir,enhanced);Hit(u,hit,power,point,dir,true);return;}
        string kind=d.fx.kind;float r=Math.Max(splash,.5f);
        if(a.type=="bolt")w.Impact(d,point,dir,true);
        w.Explosion(point,r,a.type=="bolt"?"heavy":kind,MDTowerWorldColor(d.fx.color),MDTowerWorldColor(d.fx.color2));
        if(hit!=null)Hit(u,hit,power,point,dir,a.type=="bolt","explosion");   // an enhanced bolt: the round itself, then its blast
        Splash(u,d,point,r,power*(hit!=null?.6f:1),hit);
        if(a.zone>0)w.AddZone(u,a.burn>0?"burn":kind=="poison"?"poison":kind=="frost"?"ice":"zone",point,a.zone,a.zoneTime,a.zonePower,a.slow>0?a.slow:1);
    }
    static void Splash(ComponentMDUnit u,MDTowerDef d,Vector3 point,float r,float power,ComponentBody skip) {
        var a=d.attack;var fx=d.fx;var w=World(u);var bodies=new DynamicArray<ComponentBody>();
        u.Project.FindSubsystem<SubsystemBodies>(true).FindBodiesAroundPoint(new(point.X,point.Z),r+1,bodies);
        foreach(var b in bodies) {
            if(!MDCombat.IsEnemy(u,b))continue;var c=MDCombat.Center(b);float dist=Vector3.Distance(c,point);
            if(dist>r+b.BoxSize.X*.5f)continue;
            var start=point+Vector3.UnitY*.3f;if(MDCombat.Trace(u,start,c,out _)!=b&&dist>.8f)continue;
            if(b!=skip)Hit(u,b,power*MathUtils.Lerp(1,.5f,Math.Clamp(dist/r,0,1)),c,Vector3.Normalize(c-point+Vector3.UnitY*.01f),false,"explosion");
            if(a.slow>0&&fx.kind!="poison")w.Slow(b,a.slow,a.slowTime,MDTowerWorldColor(fx.color2));   // poison slows through its cloud
            if(a.freeze>0&&Holdable(b,a))w.Freeze(b,a.freeze,a.freezeImmune);
            if(a.armorDown>0)w.ArmorDown(b,a.armorDown,a.armorTime,MDTowerWorldColor(fx.color));
            if(a.burn>0)b.Entity.FindComponent<ComponentOnFire>()?.SetOnFire(null,a.burn);
        }
    }
    public static void ZoneTick(ComponentMDUnit u,string kind,Vector3 c,float r,float power,float slow) {
        var a=u.Tower.attack;var fx=u.Tower.fx;var w=World(u);var bodies=new DynamicArray<ComponentBody>();
        u.Project.FindSubsystem<SubsystemBodies>(true).FindBodiesAroundPoint(new(c.X,c.Z),r+1,bodies);
        foreach(var b in bodies) {
            if(!MDCombat.IsEnemy(u,b)||Vector3.DistanceSquared(b.Position,c)>r*r||Math.Abs(b.Position.Y-c.Y)>2.5f)continue;
            if(kind=="poison"&&a.dot>0)w.Poison(u,b,a.dot,a.dotTime);   // damage comes from the poison status, also after leaving
            else Hit(u,b,power,MDCombat.Center(b),Vector3.UnitY,false,kind=="burn"?"explosion":"energy",kind switch{"burn"=>MDElement.Heat,"ice"=>MDElement.Cold,"orb"=>MDElement.Electric,_=>null});
            if(slow<1)w.Slow(b,slow,Math.Max(1,a.slowTime),MDTowerWorldColor(fx.color2));
            if(kind=="orb"&&random.Next(2)==0)w.AddBeam(c+Vector3.UnitY*.9f,MDCombat.Center(b),.04f,MDTowerWorldColor(fx.color2),.1f,true);
        }
    }
}
