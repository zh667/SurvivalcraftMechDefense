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
        if(u.Destroyed){w.Wreck(u.Body.Position+Vector3.UnitY*(d.bounds[1][1]*.6f),d.fx.scale+(d.bounds[1][1]>4?1:0));return;}
        if(a.type=="heal"){Heal(u,dt);return;}
        u.ScanTimer-=dt;
        if(u.ScanTimer<=0){u.ScanTimer=.25f;u.Target=FindTarget(u);}
        var target=u.Target;
        if(target!=null&&!MDCombat.Living(target))target=u.Target=null;
        if(u.Channel>0){Channel(u,dt);return;}
        if(target==null){if(d.HasYaw)u.AimPitch=MathUtils.Lerp(u.AimPitch,d.restPitch,dt*1.5f);return;}
        var tc=MDCombat.Center(target);
        if(d.HasYaw) {
            var dd=tc-u.Body.Position;
            float want=MathF.Atan2(-dd.X,-dd.Z);u.AimYaw=MDCombat.AngleTowards(u.AimYaw,want,MathUtils.DegToRad(d.yawSpeed)*dt);
        }
        float wantPitch=DesiredPitch(u,tc);u.AimPitch=MDCombat.AngleTowards(u.AimPitch,wantPitch,MathUtils.DegToRad(d.pitchSpeed)*dt);
        bool aligned=!d.HasYaw||Math.Abs(MDCombat.AngleDelta(u.AimYaw,MathF.Atan2(-(tc-u.Body.Position).X,-(tc-u.Body.Position).Z)))<.12f;
        if(a.type=="shell"&&LaidGun(d))aligned&=Math.Abs(u.AimPitch-wantPitch)<.04f;   // a laid gun fires along its barrel: wait until it is elevated
        if(u.Cooldown<=0&&aligned)Fire(u,target);
    }

    // ---------------------------------------------------------------- targets
    static bool InArea(ComponentMDUnit u,Vector3 p) {
        var a=u.Tower.attack;var d=p-u.Body.Position;float h=new Vector2(d.X,d.Z).Length();
        if(h>a.range||h<a.minRange||Math.Abs(d.Y)>Math.Max(6,a.range*.6f))return false;
        if(!a.directional)return true;
        var f=u.Body.Matrix.Forward;f.Y=0;f=Vector3.Normalize(f);var r=new Vector3(-f.Z,0,f.X);
        float along=Vector3.Dot(d,f),side=Vector3.Dot(d,r);return along>0&&along<=a.range&&Math.Abs(side)<=a.width*.5f;
    }
    public static bool Sight(ComponentMDUnit u,Vector3 from,ComponentBody target)=>MDCombat.Trace(u,from,MDCombat.Center(target),out _)==target;
    static ComponentBody FindTarget(ComponentMDUnit u) {
        var a=u.Tower.attack;var bodies=new DynamicArray<ComponentBody>();
        u.Project.FindSubsystem<SubsystemBodies>(true).FindBodiesAroundPoint(new(u.Body.Position.X,u.Body.Position.Z),a.range+2,bodies);
        Vector3 from=Aim(u);ComponentBody best=null;float bestScore=float.MaxValue;
        foreach(var b in bodies) {
            if(!MDCombat.IsEnemy(u,b)||!InArea(u,MDCombat.Center(b)))continue;
            if(a.needsSight&&!Sight(u,from,b))continue;
            float score=a.threatFirst?-Threat(b):Vector3.DistanceSquared(b.Position,u.Body.Position);
            if(score<bestScore){bestScore=score;best=b;}
        }
        return best;
    }
    // Bosses (high AttackResilience) shrug off stun and freeze, as Endfield's elite/boss enemies do.
    static float Resilience(ComponentBody b)=>b.Entity.FindComponent<ComponentHealth>()?.AttackResilience??b.Entity.FindComponent<ComponentDamage>()?.AttackResilience??0;
    static float Threat(ComponentBody b){var h=b.Entity.FindComponent<ComponentHealth>();return h==null?b.Entity.FindComponent<ComponentDamage>()?.Hitpoints??0:h.Health*h.AttackResilience;}

    // ---------------------------------------------------------------- aim
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
    static void Fire(ComponentMDUnit u,ComponentBody target) {
        var d=u.Tower;var a=d.attack;var w=World(u);
        int shot=++u.ShotCount;bool strong=a.enhancedEvery>0&&shot%a.enhancedEvery==0;
        int mi=d.muzzles.Length>0?(shot-1)%d.muzzles.Length:0;var pose=Pose(u);
        var muzzle=d.muzzles.Length>0?MDTowerLibrary.Muzzle(d,pose,mi):u.Body.Position+Vector3.UnitY*2;
        var tc=MDCombat.Center(target);var dir=Vector3.Normalize(tc-muzzle);
        if(a.needsSight&&a.type!="shell"&&!Sight(u,muzzle,target)){u.Target=null;return;}
        u.Cooldown=a.interval;
        switch(a.type) {
            case "bolt":
                u.Kick(mi);w.Muzzle(d,muzzle,dir,strong);Sound(u,d.fireSounds,muzzle);
                w.LaunchShell(u,muzzle,dir*a.speed,0,a.power*(strong?a.enhancedPower:1),strong?a.enhancedSplash:0,strong);break;
            case "shell":
                u.Kick(mi);var aimAt=new Vector3(tc.X,target.Position.Y+.1f,tc.Z);
                var vel=LaidGun(d)?BarrelLaunch(u,muzzle,MDTowerLibrary.MuzzleForward(d,pose,mi),aimAt):LaunchVelocity(u,muzzle,aimAt);
                w.Muzzle(d,muzzle,Vector3.Normalize(vel),true);Sound(u,d.fireSounds,muzzle);
                w.LaunchShell(u,muzzle,vel,Gravity,a.power,a.splash,false);break;
            case "snipe":
                u.Kick(mi);w.Muzzle(d,muzzle,dir,true);Sound(u,d.fireSounds,muzzle,.8f);
                var hb=MDCombat.Trace(u,muzzle,muzzle+dir*(a.range+4),out var hp);
                w.AddBeam(muzzle,hp,.035f*d.fx.scale,MDTowerWorldColor(d.fx.color),.18f,false);w.Impact(d,hp,dir,true);
                if(hb!=null)Hit(u,hb,a.power,hp,dir);Sound(u,d.hitSounds,hp,.5f);break;
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
                w.Spray(a.type,m,f,MDTowerWorldColor(d.fx.color),MDTowerWorldColor(d.fx.color2),a.range);
            }
            u.ChannelTick-=dt;
            if(u.ChannelTick<=0){u.ChannelTick=a.type=="flame"?.25f:.2f;AreaTick(u);}
        } else if(a.type=="chain"&&u.Channel<=0)u.ChannelTarget=null;
        if(u.Channel<=0)u.ChannelTarget=null;
    }
    static void AreaTick(ComponentMDUnit u) {
        var a=u.Tower.attack;var w=World(u);var bodies=new DynamicArray<ComponentBody>();
        u.Project.FindSubsystem<SubsystemBodies>(true).FindBodiesAroundPoint(new(u.Body.Position.X,u.Body.Position.Z),a.range+2,bodies);
        var f=u.Body.Matrix.Forward;f.Y=0;f=Vector3.Normalize(f);
        foreach(var b in bodies) {
            if(!MDCombat.IsEnemy(u,b)||!InArea(u,MDCombat.Center(b)))continue;
            if(a.type=="flame"){Hit(u,b,a.power*.25f/Math.Max(.25f,a.duration),MDCombat.Center(b),f);b.Entity.FindComponent<ComponentOnFire>()?.SetOnFire(null,a.burn);}
            else {var push=f*a.knockback+Vector3.UnitY*a.knockback*.25f;if(b.Mass<=a.pushMaxMass)b.Velocity+=push*.2f;if(a.power>0)Hit(u,b,a.power*.2f/Math.Max(.2f,a.duration),MDCombat.Center(b),f);if(a.slow>0)w.Slow(b,a.slow,a.slowTime);}
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
            if(Resilience(b)<a.bossResilience)w.Stun(b,a.stun);if(a.power>0)Hit(u,b,a.power,MDCombat.Center(b),Vector3.Normalize(b.Position-p+Vector3.UnitY*.01f));
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
            if(player!=null&&player.ComponentHealth.Health<1&&player.ComponentHealth.Health>0){player.ComponentHealth.Heal(a.power);w.Heal(b.Position);any=true;}
            else if(unit!=null&&unit!=u&&unit.Kind=="Turret"&&!unit.Destroyed&&unit.Damage.Hitpoints<1){unit.Damage.Hitpoints=Math.Min(1,unit.Damage.Hitpoints+a.power);w.Heal(b.Position);any=true;}
        }
        u.Cooldown=any?a.interval:.5f;
        if(any){w.Ring(p,a.range,MDTowerWorldColor(d.fx.color),.8f);w.Flash(Muzzle(u,0),.5f,MDTowerWorldColor(d.fx.color),.2f);Sound(u,d.fireSounds,p,.5f);}
    }

    // ---------------------------------------------------------------- damage
    public static void Hit(ComponentMDUnit u,ComponentBody b,float power,Vector3 point,Vector3 dir) {
        if(b==null||!MDCombat.IsEnemy(u,b)||power<=0)return;
        power*=World(u).DamageFactor(b);
        ComponentMiner.AttackBody(new ProjectileAttackment(b.Entity,u.Entity,point,dir,power,null));
    }
    public static void ShellLanded(ComponentMDUnit u,Vector3 point,Vector3 dir,ComponentBody hit,float power,float splash,bool enhanced) {
        var d=u.Tower;var a=d.attack;var w=World(u);
        Sound(u,d.hitSounds,point,.55f);
        if(a.type=="bolt"&&splash<=0){w.Impact(d,point,dir,enhanced);Hit(u,hit,power,point,dir);return;}
        string kind=d.fx.kind;float r=Math.Max(splash,.5f);
        if(a.type=="bolt")w.Impact(d,point,dir,true);
        w.Explosion(point,r,a.type=="bolt"?"heavy":kind,MDTowerWorldColor(d.fx.color),MDTowerWorldColor(d.fx.color2));
        if(hit!=null)Hit(u,hit,power,point,dir);
        Splash(u,point,r,power*(hit!=null?.6f:1),hit);
        if(a.zone>0)w.AddZone(u,a.burn>0?"burn":kind=="poison"?"poison":kind=="frost"?"ice":"zone",point,a.zone,a.zoneTime,a.zonePower,a.slow>0?a.slow:1);
    }
    static void Splash(ComponentMDUnit u,Vector3 point,float r,float power,ComponentBody skip) {
        var a=u.Tower.attack;var fx=u.Tower.fx;var w=World(u);var bodies=new DynamicArray<ComponentBody>();
        u.Project.FindSubsystem<SubsystemBodies>(true).FindBodiesAroundPoint(new(point.X,point.Z),r+1,bodies);
        foreach(var b in bodies) {
            if(!MDCombat.IsEnemy(u,b))continue;var c=MDCombat.Center(b);float dist=Vector3.Distance(c,point);
            if(dist>r+b.BoxSize.X*.5f)continue;
            var start=point+Vector3.UnitY*.3f;if(MDCombat.Trace(u,start,c,out _)!=b&&dist>.8f)continue;
            if(b!=skip)Hit(u,b,power*MathUtils.Lerp(1,.5f,Math.Clamp(dist/r,0,1)),c,Vector3.Normalize(c-point+Vector3.UnitY*.01f));
            if(a.slow>0&&fx.kind!="poison")w.Slow(b,a.slow,a.slowTime,MDTowerWorldColor(fx.color2));   // poison slows through its cloud
            if(a.freeze>0&&Resilience(b)<a.bossResilience)w.Freeze(b,a.freeze,a.freezeImmune);
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
            else Hit(u,b,power,MDCombat.Center(b),Vector3.UnitY);
            if(slow<1)w.Slow(b,slow,Math.Max(1,a.slowTime),MDTowerWorldColor(fx.color2));
            if(kind=="orb"&&random.Next(2)==0)w.AddBeam(c+Vector3.UnitY*.9f,MDCombat.Center(b),.04f,MDTowerWorldColor(fx.color2),.1f,true);
        }
    }
}
