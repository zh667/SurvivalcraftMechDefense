using Engine;
using GameEntitySystem;
using TemplatesDatabase;
using System.Text.Json;

namespace Game;

// Mercury (Destroy Gunners), the mech a player can board (deploy item data 1, creative inventory only). Assets come from
// tools/build_mech_assets.py (Assets/Imported/Mercury): a rigid-part glTF with one node per joint at its rest pivot, and
// the 25 clips decoded from the original game as local joint transforms every 2 ticks (1/30 s). Game frame: the mech
// faces -Z, its right is +X. Docs: docs/MECH.md.
public sealed class MDMechJoint { public string name; public int parent; public float[] pivot; }
public sealed class MDMechClip { public string name; public int slot,samples,ticks,offset,lastStep; public bool loop,hold,generated; public float? footSpeed; }
public sealed class MDMechMuzzle { public string joint; public float[] at,dir; public float spread; }
public sealed class MDMechDef {
    public MDMechJoint[] joints; public MDMechClip[] clips; public int step=2; public Dictionary<string,MDMechMuzzle> muzzles; public float height;
    [System.Text.Json.Serialization.JsonIgnore] public float[] data;
    [System.Text.Json.Serialization.JsonIgnore] public Vector3[] pivots;
    [System.Text.Json.Serialization.JsonIgnore] public Dictionary<string,int> clipIndex;
    public int Joint(string prefix)=>Array.FindIndex(joints,j=>j.name.StartsWith(prefix));
    public MDMechClip Clip(string name)=>clipIndex.TryGetValue(name,out var i)?clips[i]:throw new KeyNotFoundException("mech clip "+name);
    public float Seconds(MDMechClip c)=>c.ticks/60f;
    public MDXf Read(MDMechClip c,int sample,int joint) {
        int k=((c.offset+sample)*joints.Length+joint)*8;
        return new MDXf(new Quaternion(data[k],data[k+1],data[k+2],data[k+3]),new Vector3(data[k+4],data[k+5],data[k+6]),data[k+7]);
    }
    // The clip at a time in ticks: samples every `step` ticks, the last one at `ticks` (a shorter last step); loops wrap.
    public MDXf Sample(MDMechClip c,float tick,int joint) {
        float T=c.ticks;if(c.loop&&T>0){tick%=T;if(tick<0)tick+=T;}else tick=Math.Clamp(tick,0,T);
        int n=c.samples;if(n<2)return Read(c,0,joint);
        float lastStart=(n-2)*step;int i;float f;
        if(tick>=lastStart){i=n-2;f=c.lastStep>0?(tick-lastStart)/c.lastStep:1;}else{i=(int)(tick/step);f=(tick-i*step)/step;}
        return MDXf.Lerp(Read(c,i,joint),Read(c,i+1,joint),Math.Clamp(f,0,1));
    }
}
// A joint's deformation relative to its parent's: rotation, translation, uniform scale (column vectors).
public readonly record struct MDXf(Quaternion q,Vector3 t,float s) {
    public static readonly MDXf Identity=new(Quaternion.Identity,Vector3.Zero,1);
    public static MDXf Lerp(MDXf a,MDXf b,float f) {
        if(f<=0)return a;if(f>=1)return b;
        return new(Quaternion.Slerp(a.q,Quaternion.Dot(a.q,b.q)<0?new Quaternion(-b.q.X,-b.q.Y,-b.q.Z,-b.q.W):b.q,f),Vector3.Lerp(a.t,b.t,f),a.s+(b.s-a.s)*f);
    }
    public Matrix Row=>Matrix.CreateScale(s)*Matrix.CreateFromQuaternion(q)*Matrix.CreateTranslation(t);   // the engine's row form
}

public static class MDMechLibrary {
    static MDMechDef def;
    public const string Root="Imported/Mercury/";
    public static MDMechDef Def{get{
        if(def!=null)return def;
        var d=JsonSerializer.Deserialize<MDMechDef>(ContentManager.Get<string>(Root+"mech",".json"),new JsonSerializerOptions{IncludeFields=true});
        var s=ContentManager.GetStream(Root+"clips.bin");s.Position=0;var bytes=new byte[s.Length];s.ReadExactly(bytes);
        d.data=new float[bytes.Length/4];Buffer.BlockCopy(bytes,0,d.data,0,bytes.Length);
        int need=d.clips.Sum(c=>c.samples)*d.joints.Length*8;if(d.data.Length!=need)throw new InvalidDataException($"clips.bin has {d.data.Length} floats, mech.json needs {need}");
        d.pivots=d.joints.Select(j=>new Vector3(j.pivot[0],j.pivot[1],j.pivot[2])).ToArray();
        d.clipIndex=d.clips.Select((c,i)=>(c.name,i)).ToDictionary(x=>x.name,x=>x.i);
        return def=d;
    }}
    public static MDTowerLibrary.IconData Icon=>MDTowerLibrary.IconMesh(Root+"icon.bin",Root+"icon");
}

// Three layers, blended per joint: the base clip (idle, walk, dash, jump ...) with a cross-fade, a full-body action on top
// (hit, melee) and the back cannon (its three joints only) unfolding, firing and folding over whatever the body does.
public sealed class MDMechAnimator {
    readonly MDMechDef d; readonly int[] cannonJoints;
    public MDMechClip Base,PrevBase,Action,Cannon; public float BaseTime,PrevTime,Fade=1,FadeLength=.15f,Rate=1,PrevRate=1;
    public float ActionTime,ActionWeight,CannonTime,CannonWeight; public bool CannonOn;
    public MDMechAnimator(MDMechDef def){d=def;cannonJoints=new[]{"15_","16_","17_"}.Select(d.Joint).ToArray();Base=d.Clip("idle");}
    public void PlayBase(string name,float rate=1,float fade=.15f) {
        var c=d.Clip(name);if(c==Base){Rate=rate;return;}
        PrevBase=Base;PrevTime=BaseTime;PrevRate=Rate;Base=c;BaseTime=0;Rate=rate;Fade=0;FadeLength=fade;
    }
    public void PlayAction(string name){Action=d.Clip(name);ActionTime=0;}
    public void PlayCannon(string name){Cannon=d.Clip(name);CannonTime=0;CannonOn=true;}
    public bool ActionDone=>Action==null||ActionTime>=d.Seconds(Action);
    public bool CannonDone=>Cannon==null||CannonTime>=d.Seconds(Cannon);
    public void Update(float dt) {
        BaseTime+=dt*Rate;PrevTime+=dt*PrevRate;Fade=Math.Min(1,Fade+dt/Math.Max(.01f,FadeLength));
        if(Action!=null){ActionTime+=dt;ActionWeight=ActionDone?Math.Max(0,ActionWeight-dt/.15f):Math.Min(1,ActionWeight+dt/.08f);if(ActionDone&&ActionWeight<=0)Action=null;}
        if(Cannon!=null)CannonTime+=dt;
        CannonWeight=CannonOn?Math.Min(1,CannonWeight+dt/.1f):Math.Max(0,CannonWeight-dt/.15f);
    }
    public MDXf[] Evaluate() {
        var r=new MDXf[d.joints.Length];
        for(int j=0;j<r.Length;j++) {
            var x=d.Sample(Base,BaseTime*60,j);
            if(Fade<1&&PrevBase!=null)x=MDXf.Lerp(d.Sample(PrevBase,PrevTime*60,j),x,Fade*Fade*(3-2*Fade));
            if(Action!=null&&ActionWeight>0)x=MDXf.Lerp(x,d.Sample(Action,ActionTime*60,j),ActionWeight);
            r[j]=x;
        }
        if(Cannon!=null&&CannonWeight>0)foreach(int j in cannonJoints)r[j]=MDXf.Lerp(r[j],d.Sample(Cannon,CannonTime*60,j),CannonWeight);
        return r;
    }
}

// The pose the model draws and the weapons fire from (row form: Model[j] takes a rest-pose point of joint j's part to
// the posed model space). Procedural on top of the clips, in posed space about the joint's posed pivot so the subtree
// follows: the turret and cockpit (joint 02) turn about the vertical towards the aim, within TurretLimit of the body; the
// weapon arms (05, 10) pitch about the turret's own lateral axis. The engine's bone transform for a joint is
// T(p) L T(-p) with L its (adjusted) local deformation and p its rest pivot; the engine adds the node offset and the parent.
public sealed class MDMechPose {
    public Matrix[] Bone,Model;
    public static MDMechPose Of(MDMechDef d,MDXf[] local,float turretYaw,float armPitch) {
        int n=d.joints.Length,turret=d.Joint("02_"),armL=d.Joint("05_"),armR=d.Joint("10_");var p=new MDMechPose{Bone=new Matrix[n],Model=new Matrix[n]};
        for(int j=0;j<n;j++) {
            var L=local[j].Row;var pv=d.pivots[j];int pj=d.joints[j].parent;var parent=pj>=0?p.Model[pj]:Matrix.Identity;var M=L*parent;
            Matrix? rot=null;
            if(j==turret&&turretYaw!=0)rot=Matrix.CreateRotationY(turretYaw);
            if((j==armL||j==armR)&&armPitch!=0)rot=Matrix.CreateFromAxisAngle(Vector3.Normalize(Vector3.TransformNormal(Vector3.UnitX,p.Model[turret])),armPitch);
            if(rot.HasValue){var q=Vector3.Transform(pv,M);M=M*Matrix.CreateTranslation(-q)*rot.Value*Matrix.CreateTranslation(q);L=M*Matrix.Invert(parent);}
            p.Bone[j]=Matrix.CreateTranslation(pv)*L*Matrix.CreateTranslation(-pv);p.Model[j]=M;
        }
        return p;
    }
    public Vector3 Point(MDMechDef d,MDMechMuzzle m,Matrix body)=>Vector3.Transform(new Vector3(m.at[0],m.at[1],m.at[2]),Model[d.Joint(m.joint[..3])]*body);
    public Vector3 Dir(MDMechDef d,MDMechMuzzle m,Matrix body)=>Vector3.Normalize(Vector3.TransformNormal(new Vector3(m.dir[0],m.dir[1],m.dir[2]),Model[d.Joint(m.joint[..3])]*body));
}

// Drawn by the engine's model renderer like the towers (world light, fog, shadow), two-sided: the original shell is open.
// No TextureOverride: the texture embedded in the glTF is sampled linearly with mipmaps (an override is point-sampled).
public class ComponentMDMechModel : ComponentModel {
    ComponentMDUnit unit; int root=-1; int[] bones;
    public static string LastError;
    public override void Load(ValuesDictionary values,IdToEntityMap map){base.Load(values,map);unit=Entity.FindComponent<ComponentMDUnit>(true);RenderingMode=ModelRenderingMode.AlphaThreshold;}
    public override void Animate() {
        try {
            var d=MDMechLibrary.Def;
            if(bones==null){root=Model.RootBone.Index;bones=d.joints.Select(j=>Model.FindBone(j.name,true).Index).ToArray();}
            SetBoneTransform(root,m_componentFrame.Matrix);
            var pose=unit.Mech?.Pose(unit);if(pose==null)return;
            for(int j=0;j<bones.Length;j++)SetBoneTransform(bones[j],pose.Bone[j]);
        } catch(Exception e){if(LastError==null){LastError=e.ToString();Log.Error("[MechDefense] mech model: "+e);}SetBoneTransform(Model.RootBone.Index,m_componentFrame.Matrix);}
    }
}

public enum MDMechWeapon{Rifle,Missile,Cannon}

// Per-mech state. The pilot (MDMechPilot) writes the controls every frame; without a pilot they are zero.
public sealed class MDMechState {
    public MDMechDef Def=MDMechLibrary.Def; public MDMechAnimator Anim;
    public Vector2 Move; public bool Dash,Jump,Trigger,Melee,Switch;   // move: x right, y forward; Jump/Melee/Switch are presses
    public Vector3? AimPoint;                                          // where the pilot's crosshair is (camera ray)
    public MDMechWeapon Weapon; public float Heat,Cooldown,MeleeCooldown,FiredAgo=99,JumpDelay=-1,AirTime;public bool Overheated,MeleeHit,Grounded;
    public enum CannonState{Stowed,Deploying,Ready,Firing,Retracting} public CannonState Cannon;
    public string ForceClip; public float ForceTime;                   // lab: show one clip at one time
    public MDStatusWidget Hud; public ComponentPlayer HudOwner;
    MDMechPose pose;   // cached until the next update (the renderer may ask several times per frame)
    public MDMechState(){Anim=new MDMechAnimator(Def);}
    public MDMechPose Pose(ComponentMDUnit u) {
        if(pose!=null)return pose;
        float yaw=Math.Clamp(MDCombat.AngleDelta(u.Yaw,u.AimYaw),-MDMechBrain.TurretLimit,MDMechBrain.TurretLimit);
        return pose=MDMechPose.Of(Def,Anim.Evaluate(),yaw,Math.Clamp(u.AimPitch,-.45f,.7f));
    }
    public void Invalidate()=>pose=null;
    public void Load(ValuesDictionary v){Weapon=(MDMechWeapon)Math.Clamp(v.GetValue("MechWeapon",0),0,2);Heat=Math.Clamp(v.GetValue("Heat",0f),0,1);}
    public void Save(ValuesDictionary v){v.SetValue("MechWeapon",(int)Weapon);v.SetValue("Heat",Heat);}
    public string WeaponName=>Weapon switch{MDMechWeapon.Missile=>"导弹",MDMechWeapon.Cannon=>"火炮",_=>"步枪"};
}

public static class MDMechBrain {
    public const float TurretLimit=1.05f,TurnRate=2.4f,Gravity=12;
    // planted-foot speed of the clips (m/s at this scale) -> playback rate for the walking speed, so the feet do not slide
    public const float WalkSpeed=4.5f,BackSpeed=3.2f,StrafeSpeed=3.6f,DashSpeed=10,JumpSpeed=8;
    static readonly System.Random random=new();
    // Weapons are tower-style definitions so shells, trails, impacts and blasts use the towers' effect world.
    static MDTowerDef Weapon(string type,float power,float splash,float speed,float[] color,float[] color2,float scale,string sounds) {
        var src=MDTowerLibrary.Get(sounds);
        return new MDTowerDef{id="mercury_"+type,name="水星",attack=new MDTowerAttack{type=type,power=power,splash=splash,speed=speed,needsSight=false},
            fx=new MDTowerFx{color=color,color2=color2,scale=scale,kind="heavy"},fireSounds=src?.fireSounds??[],hitSounds=src?.hitSounds??[],muzzles=[],nodes=[]};
    }
    static MDTowerDef rifle,missile,cannon;
    static MDTowerDef Rifle=>rifle??=Weapon("bolt",30,0,160,[.4f,.9f,1],[.6f,.95f,1],.8f,"battle_turret_2");
    static MDTowerDef Missile=>missile??=Weapon("shell",70,2.5f,34,[1,.7f,.25f],[1,.45f,.15f],1.1f,"battle_rocket_1");
    static MDTowerDef CannonDef=>cannon??=Weapon("shell",360,4,58,[1,.45f,.18f],[1,.3f,.1f],1.6f,"battle_cannon_2");
    public static float Interval(MDMechWeapon w)=>w switch{MDMechWeapon.Missile=>2.5f,MDMechWeapon.Cannon=>2.8f,_=>.2f};

    static Vector3 Forward(float yaw)=>new(-MathF.Sin(yaw),0,-MathF.Cos(yaw));
    static Vector3 Right(float yaw)=>new(MathF.Cos(yaw),0,-MathF.Sin(yaw));
    public static Vector3 AimDirection(ComponentMDUnit u)=>new(-MathF.Sin(u.AimYaw)*MathF.Cos(u.AimPitch),MathF.Sin(u.AimPitch),-MathF.Cos(u.AimYaw)*MathF.Cos(u.AimPitch));
    public static Vector3 Eye(ComponentMDUnit u){var m=u.Mech;return m.Pose(u).Point(m.Def,m.Def.muzzles["eye"],u.Body.Matrix);}

    public static void Update(ComponentMDUnit u,float dt) {
        var m=u.Mech;var a=m.Anim;var body=u.Body;dt=Math.Min(dt,.1f);m.Invalidate();
        m.Cooldown=Math.Max(0,m.Cooldown-dt);m.MeleeCooldown=Math.Max(0,m.MeleeCooldown-dt);m.FiredAgo+=dt;
        m.Heat=Math.Max(0,m.Heat-dt*.3f);if(m.Overheated&&m.Heat<.35f)m.Overheated=false;
        if(m.ForceClip!=null){a.PlayBase(m.ForceClip,1,.01f);a.BaseTime=m.ForceTime;a.Fade=1;a.Action=null;a.Update(0);return;}
        if(u.Destroyed) {
            a.PlayBase("death",1,.1f);a.Update(dt);body.Velocity=new(0,body.Velocity.Y,0);
            MDTowerWorld.For(u.Project).Wreck(body.Position+Vector3.UnitY*2.2f,1.6f);return;
        }
        m.Grounded=body.StandingOnValue.HasValue||body.StandingOnBody!=null;m.AirTime=m.Grounded?0:m.AirTime+dt;
        // turning: the body follows the aim; the turret covers the rest (up to TurretLimit)
        float delta=MDCombat.AngleDelta(u.Yaw,u.AimYaw);bool moving=m.Move.LengthSquared()>.01f;
        if(m.Grounded&&(moving||Math.Abs(delta)>TurretLimit*.85f)) {
            u.Yaw+=Math.Clamp(delta,-TurnRate*dt,TurnRate*dt);
            if(Math.Abs(MDCombat.AngleDelta(u.Yaw,u.AimYaw))>TurretLimit)u.Yaw=u.AimYaw-Math.Sign(MDCombat.AngleDelta(u.Yaw,u.AimYaw))*TurretLimit;
            body.Rotation=Quaternion.CreateFromAxisAngle(Vector3.UnitY,u.Yaw);
        }
        // movement: walk / strafe / dash on the ground; jumping keeps the take-off velocity
        var mv=m.Move.LengthSquared()>1?Vector2.Normalize(m.Move):m.Move;bool dash=m.Dash&&moving;
        string dir=Math.Abs(mv.X)>Math.Abs(mv.Y)?(mv.X<0?"left":"right"):mv.Y>=0?"forward":"backward";
        if(m.Grounded&&m.JumpDelay<0) {
            float sp=dash?DashSpeed:dir switch{"forward"=>WalkSpeed,"backward"=>BackSpeed,_=>StrafeSpeed};
            var v=(Forward(u.Yaw)*mv.Y+Right(u.Yaw)*mv.X)*sp;body.Velocity=new(v.X,body.Velocity.Y,v.Z);
            if(m.Jump&&a.ActionDone){m.JumpDelay=.22f;a.PlayBase("jump",1,.08f);}
        }
        if(m.JumpDelay>=0){m.JumpDelay-=dt;if(m.JumpDelay<0)body.Velocity=new(body.Velocity.X,JumpSpeed,body.Velocity.Z);}
        // base clip (a jump keeps its own clip through the take-off)
        bool jumping=m.JumpDelay>=0||(!m.Grounded&&a.Base.name=="jump"&&a.BaseTime<1.1f);
        if(jumping){}
        else if(!m.Grounded&&m.AirTime>.15f)a.PlayBase("fall",1,.2f);
        else if(moving) {
            var clip=m.Def.Clip((dash?"dash_":"walk_")+dir);float speed=new Vector2(body.Velocity.X,body.Velocity.Z).Length();
            a.PlayBase(clip.name,!dash&&clip.footSpeed is float fs&&fs>.5f?Math.Clamp(speed/fs,.6f,2.2f):1,.18f);
        }
        else a.PlayBase(m.FiredAgo<1.5f?"combat_stance":"idle",1,.25f);
        // actions: hit reaction (from ComponentMDUnit), melee
        if(m.Melee&&m.MeleeCooldown<=0&&m.Grounded&&a.ActionDone){a.PlayAction("melee");m.MeleeCooldown=1.2f;m.MeleeHit=false;}
        if(a.Action?.name=="melee"&&!m.MeleeHit&&a.ActionTime>=.33f){m.MeleeHit=true;MeleeStrike(u);}
        // weapons: B cycles; the cannon unfolds before it can fire and folds when another weapon is chosen
        if(m.Switch){m.Weapon=(MDMechWeapon)(((int)m.Weapon+1)%3);m.Cooldown=Math.Max(m.Cooldown,.3f);}
        CannonStep(m,a);
        if(m.Trigger&&m.Cooldown<=0&&!m.Overheated&&a.Action?.name!="melee")Fire(u);
        a.Update(dt);
        m.Jump=m.Melee=m.Switch=false;
    }
    static void CannonStep(MDMechState m,MDMechAnimator a) {
        bool want=m.Weapon==MDMechWeapon.Cannon;
        switch(m.Cannon) {
            case MDMechState.CannonState.Stowed:if(want){a.PlayCannon("cannon_deploy_fire");m.Cannon=MDMechState.CannonState.Deploying;}break;
            case MDMechState.CannonState.Deploying:if(!want){a.PlayCannon("cannon_retract");m.Cannon=MDMechState.CannonState.Retracting;}else if(a.CannonDone)m.Cannon=MDMechState.CannonState.Ready;break;
            case MDMechState.CannonState.Firing:if(a.CannonDone){a.PlayCannon("cannon_deploy_fire");a.CannonTime=99;m.Cannon=MDMechState.CannonState.Ready;}break;   // back to the held, unfolded pose
            case MDMechState.CannonState.Ready:if(!want){a.PlayCannon("cannon_retract");m.Cannon=MDMechState.CannonState.Retracting;}break;
            case MDMechState.CannonState.Retracting:if(a.CannonDone){a.CannonOn=false;m.Cannon=MDMechState.CannonState.Stowed;}else if(want){a.PlayCannon("cannon_deploy_fire");m.Cannon=MDMechState.CannonState.Deploying;}break;
        }
    }
    public static string Status(ComponentMDUnit u) {
        var m=u.Mech;if(m.Overheated)return "过热";
        if(m.Weapon==MDMechWeapon.Cannon&&m.Cannon is MDMechState.CannonState.Deploying or MDMechState.CannonState.Stowed)return "展开中";
        return m.Cooldown>0?$"装填 {m.Cooldown:0.0} s":"就绪";
    }
    static Vector3 Target(ComponentMDUnit u,Vector3 from)=>u.Mech.AimPoint??from+AimDirection(u)*60;
    static void Fire(ComponentMDUnit u) {
        var m=u.Mech;var d=m.Def;var w=MDTowerWorld.For(u.Project);var pose=m.Pose(u);var bm=u.Body.Matrix;
        if(m.Weapon==MDMechWeapon.Cannon&&m.Cannon!=MDMechState.CannonState.Ready)return;
        string key=m.Weapon switch{MDMechWeapon.Missile=>"missile",MDMechWeapon.Cannon=>"cannon",_=>"rifle"};var mz=d.muzzles[key];
        var from=pose.Point(d,mz,bm);var fwd=pose.Dir(d,mz,bm);var to=Target(u,from);
        // a muzzle poking through a wall must not shoot past cover
        var cockpit=Eye(u);if(u.Project.FindSubsystem<SubsystemTerrain>(true).Raycast(cockpit,from,false,true,(v,_)=>BlocksManager.Blocks[Terrain.ExtractContents(v)].IsCollidable_(v)).HasValue){m.Cooldown=.2f;return;}
        m.Cooldown=Interval(m.Weapon);m.FiredAgo=0;
        switch(m.Weapon) {
            case MDMechWeapon.Rifle: {
                var dir=Vector3.Normalize(to-from);w.Muzzle(Rifle,from,dir,false);Sound(u,Rifle.fireSounds,from,.55f);
                w.LaunchShell(u,from,dir*Rifle.attack.speed,0,Rifle.attack.power,0,false,Rifle);
                m.Heat=Math.Min(1,m.Heat+.055f);if(m.Heat>=1)m.Overheated=true;break;
            }
            case MDMechWeapon.Missile: {
                var side=Vector3.Normalize(Vector3.Cross(fwd,Vector3.UnitY));float span=Math.Max(.3f,mz.spread);
                for(int i=0;i<4;i++) {
                    var p=from+side*span*((i-1.5f)/3f)+Vector3.UnitY*(i%2==0?.08f:-.08f);
                    var jitter=new Vector3((float)random.NextDouble()-.5f,(float)random.NextDouble()*.5f,(float)random.NextDouble()-.5f)*.9f;
                    var dir=Vector3.Normalize(to+jitter-p);w.Muzzle(Missile,p,dir,false);
                    w.LaunchShell(u,p,dir*Missile.attack.speed,1.5f,Missile.attack.power,Missile.attack.splash,false,Missile);
                }
                Sound(u,Missile.fireSounds,from,.7f);break;
            }
            case MDMechWeapon.Cannon: {
                var vel=Ballistic(from,to,CannonDef.attack.speed)??Vector3.Normalize(to-from)*CannonDef.attack.speed;
                w.Muzzle(CannonDef,from,Vector3.Normalize(vel),true);Sound(u,CannonDef.fireSounds,from,.85f);
                w.LaunchShell(u,from,vel,Gravity,CannonDef.attack.power,CannonDef.attack.splash,false,CannonDef);
                m.Anim.PlayCannon("cannon_fire_loop");m.Cannon=MDMechState.CannonState.Firing;break;
            }
        }
    }
    // low-arc launch velocity reaching `to` at muzzle speed v under the shells' gravity, or null when out of reach
    static Vector3? Ballistic(Vector3 from,Vector3 to,float v) {
        var dd=to-from;var hd=new Vector2(dd.X,dd.Z);float x=hd.Length();if(x<.5f)return null;
        float v2=v*v,disc=v2*v2-Gravity*(Gravity*x*x+2*dd.Y*v2);if(disc<0)return null;
        float angle=MathF.Atan((v2-MathF.Sqrt(disc))/(Gravity*x));hd/=x;
        return new Vector3(hd.X*MathF.Cos(angle),MathF.Sin(angle),hd.Y*MathF.Cos(angle))*v;
    }
    // melee: the creatures in a 120 deg wedge up to 5.5 m in front, at the hit frame of the clip
    static void MeleeStrike(ComponentMDUnit u) {
        var w=MDTowerWorld.For(u.Project);var f=Forward(u.Yaw);var c=u.Body.Position+Vector3.UnitY*1.6f;var bodies=new DynamicArray<ComponentBody>();bool any=false;
        u.Project.FindSubsystem<SubsystemBodies>(true).FindBodiesAroundPoint(new(c.X,c.Z),6.5f,bodies);
        foreach(var b in bodies) {
            if(!MDCombat.IsEnemy(u,b))continue;var to=MDCombat.Center(b)-c;var flat=new Vector3(to.X,0,to.Z);float dist=flat.Length();
            if(dist>5.5f+b.BoxSize.X*.5f||Vector3.Dot(flat/Math.Max(dist,.01f),f)<.5f)continue;
            var at=MDCombat.Center(b);MDTowerBrain.Hit(u,b,300,at,f);w.Impact(CannonDef,at,f,true);any=true;
            if(b.Mass<=800)b.Velocity+=f*7+Vector3.UnitY*3;
        }
        if(any)Sound(u,CannonDef.hitSounds,c,.8f);
    }
    public static void Sound(ComponentMDUnit u,string[] list,Vector3 at,float volume) {
        if(list==null||list.Length==0)return;
        u.Project.FindSubsystem<SubsystemAudio>(true).PlaySound(list[random.Next(list.Length)],volume,MathUtils.Lerp(-.05f,.05f,(float)random.NextDouble()),at,14,false);
    }
}
