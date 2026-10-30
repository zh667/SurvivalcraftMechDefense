using Engine;
using GameEntitySystem;
using TemplatesDatabase;
using System.Text.Json;

namespace Game;

// Mercury (Destroy Gunners), the mech a player can board (deploy item data 1: crafting table or creative inventory). Assets come from
// tools/build_mech_assets.py (Assets/Imported/Mercury): a rigid-part glTF with one node per joint at its rest pivot, and
// the 25 clips decoded from the original game as local joint transforms every 2 ticks (1/30 s). Game frame: the mech
// faces -Z, its right is +X. Docs: docs/MECH.md.
public sealed class MDMechJoint { public string name; public int parent; public float[] pivot; }
public sealed class MDMechClip { public string name; public int slot,samples,ticks,offset,lastStep; public bool loop,hold,generated; public float? footSpeed; }
public sealed class MDMechMuzzle { public string joint; public float[] at,dir; public float spread; }
// Which joints do which job (prefixes of joint names). Mercury's mech.json has none: the defaults are Mercury's. The hero mechs'
// come from the original's per-joint attribute words (tools/build_hero_assets.py; 复仇女神's 10_ is a shield pile driver, its right
// arm is 11_).
public sealed class MDMechRoles { public string turret="02_",armL="05_",armR="10_",grenade="15_"; public string[] cannon=["15_","16_","17_"],firstPerson=["13_","14_","15_","16_","17_"]; }
public sealed class MDMechCockpit { public float[] firstPersonEye; }
public sealed class MDMechDef {
    public MDMechJoint[] joints; public MDMechClip[] clips; public int step=2; public Dictionary<string,MDMechMuzzle> muzzles; public float height;
    public MDMechRoles roles=new(); public MDMechCockpit cockpit;
    // robot units (tools/build_unit_assets.py, unit.json): suggested body box (m), lowest idle point, skin model names
    public float scale=1,feet; public float[] box; public string[] skins;
    public bool Has(string clip)=>clipIndex.ContainsKey(clip);
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
    static readonly Dictionary<string,MDMechDef> defs=[];
    public const string Root="Imported/Mercury/";
    public static MDMechDef Def=>Load(Root,"mech");
    // Mercury (mech.json) and the robot enemies (Imported/<unit>/unit.json) share the format of tools/build_mech_assets.py.
    public static MDMechDef Load(string root,string file) {
        if(defs.TryGetValue(root,out var d))return d;
        d=JsonSerializer.Deserialize<MDMechDef>(ContentManager.Get<string>(root+file,".json"),new JsonSerializerOptions{IncludeFields=true});
        var s=ContentManager.GetStream(root+"clips.bin");s.Position=0;var bytes=new byte[s.Length];s.ReadExactly(bytes);
        d.data=new float[bytes.Length/4];Buffer.BlockCopy(bytes,0,d.data,0,bytes.Length);
        int need=d.clips.Sum(c=>c.samples)*d.joints.Length*8;if(d.data.Length!=need)throw new InvalidDataException($"{root}clips.bin has {d.data.Length} floats, {file}.json needs {need}");
        d.pivots=d.joints.Select(j=>new Vector3(j.pivot[0],j.pivot[1],j.pivot[2])).ToArray();
        d.clipIndex=d.clips.Select((c,i)=>(c.name,i)).ToDictionary(x=>x.name,x=>x.i);
        return defs[root]=d;
    }
    public static MDTowerLibrary.IconData Icon=>MDTowerLibrary.IconMesh(Root+"icon.bin",Root+"icon");
}

// The pilotable mechs (user, 2026-10-21, round 5: "主角机甲可以再加个复仇女神和轩辕十四（实力方面：水星<复仇女神<轩辕十四）"; plan
// confirmed: the original models like Mercury, armour 4000 / 6000 / 8000 (database templates), damage x1 / x1.25 / x1.5, energy
// 100000 / 130000 / 160000, 复仇女神 with the original's 3-round burst rifle and better blast protection, 轩辕十四 the most mobile).
// Same controls for all; the deploy item's low data byte is Data. Original numbers: docs/HEROES_ORIGINAL.md.
public sealed class MDMechType {
    public string Id,Name,Root,Template; public int Data,Armour;
    public float Damage=1,EnergyMax=100000,Speed=1,ExplosionTaken=1,BossArmour=3000; public int Locks=3,Weight=4; public bool BurstRifle;
    public Vector3 Box=new(2.4f,4.8f,2.4f);
    // 0.15.0 (user, 2026-10-30: "机甲也要做多种元素的炮弹……高级的可以做多一点支持的元素"): the ammo elements it can load (MDMechState.Ammo);
    // Kinetic stands for the standard rounds (rifle kinetic, cannon / missiles / grenades explosive). The laser is always electric.
    public MDElement[] Ammo=[MDElement.Kinetic,MDElement.Heat,MDElement.Cold];
    public string HoverClip="jump"; public float HoverTime=1;   // the pose held while hovering (轩辕十四: its gliding dash, whose feet stay off the ground)
    public MDMechDef Def=>MDMechLibrary.Load(Root,"mech");
    public MDTowerLibrary.IconData Icon=>MDTowerLibrary.IconMesh(Root+"icon.bin",Root+"icon");
    public static readonly MDMechType Mercury=new(){Id="Mercury",Name="水星",Root="Imported/Mercury/",Template="MDMech",Data=1,Armour=4000};
    public static readonly MDMechType Nemesis=new(){Id="Nemesis",Name="复仇女神",Root="Imported/Nemesis/",Template="MDMechNemesis",Data=30,Armour=6000,
        Damage=1.25f,EnergyMax=130000,BurstRifle=true,ExplosionTaken=.8f,Weight=5,BossArmour=4000,Ammo=[MDElement.Kinetic,MDElement.Heat,MDElement.Cold,MDElement.Electric]};
    public static readonly MDMechType Regulus=new(){Id="Regulus",Name="轩辕十四",Root="Imported/Regulus/",Template="MDMechRegulus",Data=31,Armour=8000,
        Damage=1.5f,EnergyMax=160000,Speed=1.15f,Locks=2,Weight=6,BossArmour=5000,Box=new(2.8f,5.6f,2.8f),HoverClip="dash_forward",HoverTime=-1,
        Ammo=[MDElement.Kinetic,MDElement.Heat,MDElement.Cold,MDElement.Electric,MDElement.Corrosive]};
    public static readonly MDMechType[] All=[Mercury,Nemesis,Regulus];
    public static MDMechType ById(string id)=>All.FirstOrDefault(t=>t.Id==id)??Mercury;
    public static MDMechType ByData(int data)=>All.FirstOrDefault(t=>t.Data==data);
}

// Three layers, blended per joint: the base clip (idle, walk, dash, jump ...) with a cross-fade, a full-body action on top
// (hit, melee) and the back cannon (its three joints only) unfolding, firing and folding over whatever the body does.
public sealed class MDMechAnimator {
    readonly MDMechDef d; readonly int[] cannonJoints;
    public MDMechClip Base,PrevBase,Action,Cannon; public float BaseTime,PrevTime,Fade=1,FadeLength=.15f,Rate=1,PrevRate=1;
    public float ActionTime,ActionWeight,CannonTime,CannonWeight; public bool CannonOn;
    public MDMechAnimator(MDMechDef def){d=def;cannonJoints=d.roles.cannon.Select(d.Joint).Where(j=>j>=0).ToArray();Base=d.Clip("idle");}
    public void PlayBase(string name,float rate=1,float fade=.15f) {
        var c=d.Clip(name);if(c==Base){Rate=rate;return;}
        PrevBase=Base;PrevTime=BaseTime;PrevRate=Rate;Base=c;BaseTime=0;Rate=rate;Fade=0;FadeLength=fade;
    }
    // a clip held still at one time (hover holds the jump's airborne pose)
    public void HoldBase(string name,float time,float fade=.2f){var c=d.Clip(name);if(c!=Base){PrevBase=Base;PrevTime=BaseTime;PrevRate=Rate;Base=c;Fade=0;FadeLength=fade;}BaseTime=time;Rate=0;}
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
// weapon arms (05, 10) pitch about the turret's own lateral axis so that the rifle points at the aim pitch: measured on the
// clip's pose each frame (the walk's arm swing in pitch is taken out, its sideways swing stays), faded out by `aimWeight`
// during melee / hit reactions and when destroyed. The engine's bone transform for a joint is T(p) L T(-p) with L its
// (adjusted) local deformation and p its rest pivot; the engine adds the node offset and the parent.
public sealed class MDMechPose {
    public Matrix[] Bone,Model;
    public static MDMechPose Of(MDMechDef d,MDXf[] local,float turretYaw,float aimPitch,float aimWeight,float recoil=0) {
        if(recoil>0){var c=d.muzzles["cannon"];int k=d.Joint(c.joint[..3]);var back=new Vector3(c.dir[0],c.dir[1],c.dir[2])*-recoil;
            local=(MDXf[])local.Clone();var l=local[k];local[k]=new MDXf(l.q,l.t+Vector3.TransformNormal(back,Matrix.CreateFromQuaternion(l.q))*l.s,l.s);}   // along the barrel in its rest frame
        var first=Build(d,local,turretYaw,0);if(aimWeight<=0)return first;
        var mz=d.muzzles["rifle"];var dir=Vector3.Normalize(Vector3.TransformNormal(new Vector3(mz.dir[0],mz.dir[1],mz.dir[2]),first.Model[d.Joint(mz.joint[..3])]));
        float delta=Math.Clamp(aimPitch-MathF.Asin(Math.Clamp(dir.Y,-1,1)),-.9f,.9f)*aimWeight;
        return MathF.Abs(delta)<1e-4f?first:Build(d,local,turretYaw,delta);
    }
    static MDMechPose Build(MDMechDef d,MDXf[] local,float turretYaw,float armPitch) {
        int n=d.joints.Length,turret=d.Joint(d.roles.turret),armL=d.Joint(d.roles.armL),armR=d.Joint(d.roles.armR);var p=new MDMechPose{Bone=new Matrix[n],Model=new Matrix[n]};
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
    ComponentMDUnit unit; int root=-1; int[] bones,allMeshes,cockpitMeshes;
    // first person draws the rifle on its forearm and the cannon only: the head, the shoulder units right beside the eye, the
    // arms, the legs and the back units sweep through the view at every step. The instanced data is cached per list.
    public static string LastError;
    public override void Load(ValuesDictionary values,IdToEntityMap map){base.Load(values,map);unit=Entity.FindComponent<ComponentMDUnit>(true);RenderingMode=ModelRenderingMode.AlphaThreshold;}
    public override void Animate() {
        try {
            var d=unit.Mech?.Def??MDMechLibrary.Def;
            if(bones==null){root=Model.RootBone.Index;bones=d.joints.Select(j=>Model.FindBone(j.name,true).Index).ToArray();}
            SetBoneTransform(root,m_componentFrame.Matrix);
            var pose=unit.Mech?.Pose(unit);if(pose==null)return;
            for(int j=0;j<bones.Length;j++)SetBoneTransform(bones[j],pose.Bone[j]);
            if(allMeshes==null){allMeshes=Enumerable.Range(0,Model.Meshes.Count).ToArray();cockpitMeshes=allMeshes.Where(i=>d.roles.firstPerson.Any(p=>Model.Meshes[i].ParentBone.Name.StartsWith(p))).ToArray();}
            MeshDrawOrders=MDMechPilot.FirstPerson(unit)?cockpitMeshes:allMeshes;
        } catch(Exception e){if(LastError==null){LastError=e.ToString();Log.Error("[MechDefense] mech model: "+e);}SetBoneTransform(Model.RootBone.Index,m_componentFrame.Matrix);}
        if(++diagFrames is 2 or 200)Diagnose();
    }
    // A player's phone (API 1.9.4, 2026-10-23): "机甲放置出来不可见", with nothing in the log. One line at the 2nd and the 200th
    // frame of each placed mech says what the renderer sees, so the next log tells why. Read by reflection: some members are
    // missing in older APIs, and nothing here may break the mech.
    int diagFrames;
    void Diagnose() {
        try {
            const System.Reflection.BindingFlags any=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.NonPublic;
            object Get(object o,string name){var t=o as Type??o.GetType();var f=t.GetField(name,any);if(f!=null)return f.GetValue(o is Type?null:o);var p=t.GetProperty(name,any);return p?.GetValue(o is Type?null:o);}
            var renderer=Project.FindSubsystem<SubsystemModelsRenderer>(false);var sky=Project.FindSubsystem<SubsystemSky>(false);
            bool? registered=(Get(renderer,"m_componentModels") as System.Collections.IDictionary)?.Contains(this);
            var hooks=new List<string>();
            if(Get(typeof(ModsManager),"ModLoaders") is System.Collections.IEnumerable loaders)
                foreach(var l in loaders)if(l!=null&&l.GetType().GetMethod("OnModelCalculateIsVisible")?.DeclaringType is {} dt&&dt!=typeof(ModLoader))hooks.Add(l.GetType().FullName);
            Log.Information($"[MechDefense] mech {unit?.Mech?.Type?.Id} frame {diagFrames} at {m_componentFrame?.Position}: model {(Model==null?"none":$"{Model.Meshes.Count} meshes, {Model.Bones.Count} bones")}, "+
                $"registered {registered}, IsSet {Get(this,"IsSet")}, DisableDrawing {Get(this,"DisableDrawing")}, visible {IsVisibleForCamera}, opacity {Opacity?.ToString()??"null"}, "+
                $"draw orders {MeshDrawOrders?.Length??-1}, mode {RenderingMode}, sphere {Get(this,"m_boundingSphereRadius")}, range {sky?.VisibilityRange}, visibility hooks [{string.Join(",",hooks)}], "+
                $"bones per draw {Get(renderer,"MaxInstancesCount")} (GL vertex uniform vectors {Get(typeof(Engine.Graphics.GLWrapper),"GL_MAX_VERTEX_UNIFORM_VECTORS")}), batched draws {MDDrawBatches.Batched}");   // 0.12.0: see MDDrawBatches
        } catch(Exception e){Log.Warning("[MechDefense] mech diagnostics: "+e.Message);}
    }
}

public enum MDMechWeapon{Rifle,Missile,Cannon,Laser,Grenade}

// Per-mech state. The pilot (MDMechPilot) writes the controls every frame; without a pilot they are zero.
public sealed class MDMechState {
    public readonly MDMechType Type; public readonly MDMechDef Def; public MDMechAnimator Anim;
    public Vector2 Move; public bool Hover,Jump,Trigger,Melee,Switch;  // move: x right, y forward; Jump/Melee/Switch are presses
    public Vector3? AimPoint; public Vector3 AimFrom,AimDir;          // the crosshair: its point and the camera ray
    public MDMechWeapon Weapon; public float Cooldown,MeleeCooldown,FiredAgo=99,AirTime,ShownSpeed;public bool MeleeHit,Grounded,PendingShot,WasTrigger,WasHover;
    public float Energy,EnergyLock;                                    // EP of the original (per mech type); at 0 no boost for 3 s
    public int BurstShot;                                              // 复仇女神's rifle: shot of the 3-round burst
    public readonly List<ComponentBody> Locks=[]; public readonly Queue<ComponentBody> Salvo=new(); public float SalvoTimer;   // missile locks; missiles still to launch
    public float BeamTime=-1,BeamTick,BeamLength;                       // laser: seconds since the beam started (<0 off), next damage tick, length
    public ComponentBody LungeTarget;
    public Vector3? CameraAt;                                          // smoothed first-person camera position
    public Vector3 MoveVelocity;                                       // the mech's own ground velocity (the body's is cut by friction)
    public float Recoil=>CannonKick<=0?0:CannonKick>.28f?(.35f-CannonKick)/.07f*.45f:CannonKick/.28f*.45f;   // barrel kick back (m)
    public enum CannonState{Stowed,Deploying,Ready,Retracting} public CannonState Cannon;
    public bool Hovering; public float JetBurst,JetCarry,CannonKick;   // hovering now; jump burst left (s); flame particles owed; cannon recoil left (s)
    public string ForceClip; public float ForceTime;                   // lab: show one clip at one time
    public MDHudHost Hud; public ComponentPlayer HudOwner;
    MDMechPose pose;   // cached until the next update (the renderer may ask several times per frame)
    public MDMechState(MDMechType type){Type=type;Def=type.Def;Anim=new MDMechAnimator(Def);Energy=type.EnergyMax;}
    public MDMechPose Pose(ComponentMDUnit u) {
        if(pose!=null)return pose;
        float yaw=Math.Clamp(MDCombat.AngleDelta(u.Yaw,u.AimYaw),-MDMechBrain.TurretLimit,MDMechBrain.TurretLimit);
        float aim=u.Destroyed||ForceClip!=null?0:1-(Anim.Action!=null?Anim.ActionWeight:0);
        return pose=MDMechPose.Of(Def,Anim.Evaluate(),yaw,Math.Clamp(u.AimPitch,-.45f,.7f),aim,Recoil);
    }
    public void Invalidate()=>pose=null;
    // Weapon kits Mk1-Mk3 (MDPartBlock, installed one level after the other): every weapon's damage x1.15 / x1.3 / x1.5.
    public int Kit;
    public static float KitFactor(int kit)=>kit switch{1=>1.15f,2=>1.3f,3=>1.5f,_=>1};
    public MDElement Ammo=MDElement.Kinetic;   // 0.15.0: the loaded ammo element (one of Type.Ammo)
    public void NextAmmo(){int i=Array.IndexOf(Type.Ammo,Ammo);Ammo=Type.Ammo[(i+1)%Type.Ammo.Length];}
    public static string AmmoName(MDElement e)=>e==MDElement.Kinetic?"标准弹":MDElements.Name(e)+"元素弹";
    public void Load(ValuesDictionary v){Ammo=(MDElement)v.GetValue("MechAmmo",(int)MDElement.Kinetic);if(!Type.Ammo.Contains(Ammo))Ammo=MDElement.Kinetic;Weapon=(MDMechWeapon)Math.Clamp(v.GetValue("MechWeapon",0),0,4);Energy=Math.Clamp(v.GetValue("Energy",Type.EnergyMax),0,Type.EnergyMax);Kit=Math.Clamp(v.GetValue("MechKit",0),0,3);}
    public void Save(ValuesDictionary v){v.SetValue("MechAmmo",(int)Ammo);v.SetValue("MechWeapon",(int)Weapon);v.SetValue("Energy",Energy);v.SetValue("MechKit",Kit);}
    public static string Name(MDMechWeapon w)=>w switch{MDMechWeapon.Missile=>"导弹",MDMechWeapon.Cannon=>"火炮",MDMechWeapon.Laser=>"激光",MDMechWeapon.Grenade=>"榴弹",_=>"步枪"};
    public string WeaponName=>Name(Weapon);
}

// Mercury's behaviour after the original (Destroy Gunners Σ, decoded native code and tables; research report in
// docs/MECH.md): one game world unit is Unit metres here (MERCURY is 1.68 units tall in the original and 4.48 m here).
public static class MDMechBrain {
    public const float TurretLimit=1.05f,TurnRate=3.5f,Gravity=12,Unit=2.67f;
    // Speeds: the original's root motion (move/16384 units per tick x60) x Unit with the clips at their own rate: walk 5.7,
    // back 5.2, sideways 5.7 m/s; boost (hover, Shift held) 13 m/s HoverHeight above the ground on the back thrusters (the
    // original's boost is x1.4 = 8 m/s, which the user could not feel), in the
    // jump's airborne pose with both legs kicked back (user: like the jump, only without jumping; the original's gliding
    // "dash" clips looked crouched). Short acceleration on the ground, steering in the air at half of it; the jump leaves at
    // once with a thruster burst. Sounds: the jump take-off is shot_missile as in the original's code (no other take-off
    // sound exists in either version's 32 effects); hover plays the game's boss_hover once per Shift press.
    public const float HoverPoseTime=1;   // s into the jump clip: its steady airborne pose (from 0.6 s on)
    public const float WalkSpeed=5.7f,BackSpeed=5.2f,StrafeSpeed=5.7f,HoverSpeed=13,JumpSpeed=8.5f,Accel=24,AirAccel=12,HoverHeight=.8f;
    // Energy (EP): 100000, +9000/s, boost -15360/s, a cannon shot -640; empty: 3 s without boost.
    public const float EnergyMax=100000,EnergyRegen=9000,BoostCost=15360,CannonCost=640;
    // Damage: the original's (pilot attack 1000 x part power %) x DamageScale. Mercury's default parts (rifle NO-100,
    // missile NO-100, cannon NO-200) and the shop's first laser (NO-150) and grenade (NO-200); the punch is 1000 %.
    // 0.1 (user: numbers by the installed mods, docs/MECH.md): the rifle's 400 per second is above the strongest hand guns
    // there (about 300-350) and below the vehicles' heavy weapons (BF2042 tank shell 600-2700, helicopter rockets 563/s).
    public const float DamageScale=.1f;
    public const int MaxLocks=3;                                        // MERCURY's missile lock count
    static readonly System.Random random=new();
    static float R(float a,float b)=>a+(float)random.NextDouble()*(b-a);
    public static string Se(int id)=>$"Imported/Mercury/Audio/se{id:00}";   // the original sound effects by id (se.bin)
    static MDTowerDef Def(string id,string type,float power,float splash,float speed,float[] color,float[] color2,float scale,int hitSe)=>
        new(){id="mercury_"+id,name="水星",attack=new MDTowerAttack{type=type,power=power,splash=splash,speed=speed,needsSight=false},
              fx=new MDTowerFx{color=color,color2=color2,scale=scale,kind="heavy"},fireSounds=[],hitSounds=[Se(hitSe)],muzzles=[],nodes=[]};
    static MDTowerDef rifle,missile,cannon,laser,grenade;
    static MDTowerDef Rifle=>rifle??=Def("rifle","bolt",1000*DamageScale,0,60*Unit,[.4f,.9f,1],[.6f,.95f,1],.8f,13);           // 160 m/s, hit_gun
    static MDTowerDef Missile=>missile??=Def("missile","shell",1000*DamageScale,0,12*Unit,[1,.7f,.25f],[1,.45f,.15f],1.1f,14);   // direct hit only, hit_missile
    static MDTowerDef CannonDef=>cannon??=Def("cannon","shell",2000*DamageScale,5*Unit,120*Unit,[1,.45f,.18f],[1,.3f,.1f],1.6f,13);   // 320 m/s, blast 5 units
    static MDTowerDef Laser=>laser??=Def("laser","beam",1500*DamageScale,0,0,[.35f,.85f,1],[.7f,.95f,1],1,13);
    static MDTowerDef Grenade=>grenade??=Def("grenade","shell",2000*DamageScale,5*Unit,0,[1,.75f,.3f],[.9f,.4f,.15f],1.4f,24);   // bomb_explosion
    static readonly int[] SwitchSe=[25,26,27,30,29];   // change_to_gun / missile / cannon / laser / bomb

    static Vector3 Forward(float yaw)=>new(-MathF.Sin(yaw),0,-MathF.Cos(yaw));
    static Vector3 Right(float yaw)=>new(MathF.Cos(yaw),0,-MathF.Sin(yaw));
    public static Vector3 AimDirection(ComponentMDUnit u)=>new(-MathF.Sin(u.AimYaw)*MathF.Cos(u.AimPitch),MathF.Sin(u.AimPitch),-MathF.Cos(u.AimYaw)*MathF.Cos(u.AimPitch));
    public static Vector3 Eye(ComponentMDUnit u){var m=u.Mech;return m.Pose(u).Point(m.Def,m.Def.muzzles["eye"],u.Body.Matrix);}
    static bool Solid(int v,float _)=>BlocksManager.Blocks[Terrain.ExtractContents(v)].IsCollidable_(v);

    public static void Update(ComponentMDUnit u,float dt) {
        var m=u.Mech;var a=m.Anim;var body=u.Body;dt=Math.Min(dt,.1f);m.Invalidate();
        m.Cooldown=Math.Max(0,m.Cooldown-dt);m.MeleeCooldown=Math.Max(0,m.MeleeCooldown-dt);m.FiredAgo+=dt;m.SalvoTimer-=dt;m.EnergyLock=Math.Max(0,m.EnergyLock-dt);
        if(m.ForceClip!=null){a.PlayBase(m.ForceClip,1,.01f);a.BaseTime=m.ForceTime;a.Fade=1;a.Action=null;a.Update(0);return;}
        if(u.Destroyed) {
            a.PlayBase("death",1,.1f);a.Update(dt);body.Velocity=new(0,body.Velocity.Y,0);m.Locks.Clear();m.Salvo.Clear();m.BeamTime=-1;
            MDTowerWorld.For(u.Project).Wreck(body.Position+Vector3.UnitY*2.2f,1.6f);return;
        }
        m.Grounded=body.StandingOnValue.HasValue||body.StandingOnBody!=null;
        // hover: a spring towards HoverHeight over the ground below (terrain within 4 m), a slow sink over a drop; it costs energy
        bool hover=m.Hover&&m.EnergyLock<=0&&m.Energy>0;float? ground=null;
        if(hover&&m.JetBurst<=.15f){var g=u.Project.FindSubsystem<SubsystemTerrain>(true).Raycast(body.Position+Vector3.UnitY*.3f,body.Position-Vector3.UnitY*4,false,true,Solid);if(g.HasValue)ground=g.Value.Distance-.3f;}
        m.Hovering=hover&&ground.HasValue&&!(body.Velocity.Y>2&&ground>HoverHeight);
        if(m.Hovering)body.Velocity=new(body.Velocity.X,Math.Clamp((HoverHeight-ground.Value)*7,-5,6),body.Velocity.Z);
        else if(hover&&!m.Grounded&&body.Velocity.Y<-2.5f)body.Velocity=new(body.Velocity.X,-2.5f,body.Velocity.Z);
        if(m.Hover&&!m.WasHover&&hover)Sound(u,Se(31),body.Position,.5f);m.WasHover=m.Hover;   // one hum per Shift press (user: not a repeating one)
        m.Energy=Math.Clamp(m.Energy+(m.Hovering?-BoostCost:EnergyRegen)*dt,0,m.Type.EnergyMax);if(m.Energy<=0&&m.EnergyLock<=0)m.EnergyLock=3;
        bool footing=m.Grounded||m.Hovering;m.AirTime=footing?0:m.AirTime+dt;
        // turning: the body follows the aim; the turret covers the rest (up to TurretLimit)
        float delta=MDCombat.AngleDelta(u.Yaw,u.AimYaw);bool moving=m.Move.LengthSquared()>.01f;
        if(footing&&(moving||Math.Abs(delta)>TurretLimit*.85f)) {
            u.Yaw+=Math.Clamp(delta,-TurnRate*dt,TurnRate*dt);
            if(Math.Abs(MDCombat.AngleDelta(u.Yaw,u.AimYaw))>TurretLimit)u.Yaw=u.AimYaw-Math.Sign(MDCombat.AngleDelta(u.Yaw,u.AimYaw))*TurretLimit;
            body.Rotation=Quaternion.CreateFromAxisAngle(Vector3.UnitY,u.Yaw);
        }
        // movement, eased in the mech's own velocity and written to the body every update (easing the body's velocity, which
        // friction cuts after every step, never reaches the speed on the ground); in the air without input it keeps its momentum
        var mv=m.Move.LengthSquared()>1?Vector2.Normalize(m.Move):m.Move;
        string dir=Math.Abs(mv.X)>Math.Abs(mv.Y)?(mv.X<0?"left":"right"):mv.Y>=0?"forward":"backward";
        if(a.Action?.name=="lockon_special"&&m.LungeTarget!=null&&a.ActionTime<.3f) {   // the jump attack carries it at its target
            var to=MDCombat.Center(m.LungeTarget)-body.Position;to.Y=0;float dist=to.Length();
            m.MoveVelocity=dist>3?to/dist*Math.Min(22,dist/.3f):Vector3.Zero;body.Velocity=new(m.MoveVelocity.X,body.Velocity.Y,m.MoveVelocity.Z);
        }
        else if(footing||moving) {
            float sp=(m.Hovering?HoverSpeed:dir switch{"forward"=>WalkSpeed,"backward"=>BackSpeed,_=>StrafeSpeed})*m.Type.Speed;
            var want=(Forward(u.Yaw)*mv.Y+Right(u.Yaw)*mv.X)*sp;var dv=new Vector3(want.X,0,want.Z)-m.MoveVelocity;
            float step=(footing?Accel:AirAccel)*dt;if(dv.Length()>step)dv=Vector3.Normalize(dv)*step;
            m.MoveVelocity+=dv;body.Velocity=new(m.MoveVelocity.X,body.Velocity.Y,m.MoveVelocity.Z);
        }
        else m.MoveVelocity=new(body.Velocity.X,0,body.Velocity.Z);
        if(m.Jump&&footing&&a.ActionDone){body.Velocity=new(body.Velocity.X,JumpSpeed,body.Velocity.Z);a.PlayBase("jump",1,.06f);m.Grounded=m.Hovering=false;m.AirTime=0;m.JetBurst=.35f;footing=false;Sound(u,Se(9),body.Position,.6f);}
        // base clip (a jump keeps its own clip on the way up)
        bool jumping=!footing&&a.Base.name=="jump"&&a.BaseTime<1.1f;
        if(jumping){}
        else if(!footing&&m.AirTime>.15f)a.PlayBase("fall",1,.2f);
        else if(m.Hovering)a.HoldBase(m.Type.HoverClip,m.Type.HoverTime>=0?m.Type.HoverTime:m.Def.Seconds(m.Def.Clip(m.Type.HoverClip)),.2f);
        else if(moving)a.PlayBase("walk_"+dir,m.Type.Speed,.18f);   // a faster mech steps faster (no sliding feet)
        else a.PlayBase(m.FiredAgo<1.5f?"combat_stance":"idle",1,.25f);
        // melee (F): with a locked creature, or one near the crosshair within 20 m, the original's jump attack at it; else the
        // punch combo. Both strike once at their hit frame.
        if(m.Melee&&m.MeleeCooldown<=0&&footing&&a.ActionDone) {
            m.LungeTarget=m.Locks.FirstOrDefault(b=>MDCombat.Living(b))??NearCrosshair(u,20,.35f);
            a.PlayAction(m.LungeTarget!=null?"lockon_special":"melee");m.MeleeCooldown=1.2f;m.MeleeHit=false;Sound(u,Se(16),body.Position,.8f);
        }
        if(a.Action?.name is "melee" or "lockon_special"&&!m.MeleeHit&&a.ActionTime>=.33f){m.MeleeHit=true;MeleeStrike(u);}
        // weapons: B cycles rifle > missiles > cannon > laser > grenade
        if(m.Switch){m.Weapon=(MDMechWeapon)(((int)m.Weapon+1)%5);m.Cooldown=Math.Max(m.Cooldown,.3f);m.PendingShot=false;m.Locks.Clear();m.BeamTime=-1;Sound(u,Se(SwitchSe[(int)m.Weapon]),body.Position,.6f);}
        bool pressed=m.Trigger&&!m.WasTrigger,released=!m.Trigger&&m.WasTrigger;bool busy=a.Action?.name is "melee" or "lockon_special";
        CannonStep(m,a,dt);
        switch(m.Weapon) {
            case MDMechWeapon.Rifle:if(m.Trigger&&m.Cooldown<=0&&!busy)FireRifle(u);break;
            case MDMechWeapon.Missile:if(m.Trigger)LockSweep(u);if(released){if(m.Locks.Count==0)m.Salvo.Enqueue(null);foreach(var b in m.Locks)m.Salvo.Enqueue(b);m.Locks.Clear();}break;
            case MDMechWeapon.Cannon:if((m.Trigger||m.PendingShot)&&m.Cooldown<=0&&m.Cannon==MDMechState.CannonState.Ready&&m.Energy>=CannonCost&&!busy)FireCannon(u);break;
            case MDMechWeapon.Laser:if(m.Trigger&&m.Cooldown<=0&&m.BeamTime<0&&!busy){m.BeamTime=0;m.BeamLength=0;m.BeamTick=0;m.FiredAgo=0;Sound(u,Se(10),body.Position,.7f);}break;
            case MDMechWeapon.Grenade:if(pressed&&m.Cooldown<=0&&!busy)ThrowGrenade(u);break;
        }
        if(m.Salvo.Count>0&&m.SalvoTimer<=0){LaunchMissile(u,m.Salvo.Dequeue());m.SalvoTimer=.3f;}   // one missile per lock, 0.3 s apart
        if(m.BeamTime>=0)Beam(u,dt);
        LockMarks(u);
        a.Update(dt);m.CannonKick=Math.Max(0,m.CannonKick-dt);
        Thrusters(u,dt);
        m.WasTrigger=m.Trigger;m.Jump=m.Melee=m.Switch=false;
    }
    // The cannon stays folded on the back until the pilot fires it: a press unfolds it (the original firing clip, held at its
    // end) and the shot leaves once it is open. Holding the trigger keeps it open and firing (each shot kicks the barrel back);
    // a single press folds it straight after its shot (the recoil), as does choosing another weapon. The original then plays
    // clip 55 for every shot; it swings the cannon up and back down, which read as folding and snapping open again (user).
    static void CannonStep(MDMechState m,MDMechAnimator a,float dt) {
        bool selected=m.Weapon==MDMechWeapon.Cannon,fire=selected&&m.Trigger;
        switch(m.Cannon) {
            case MDMechState.CannonState.Stowed:if(fire){a.PlayCannon("cannon_deploy_fire");m.Cannon=MDMechState.CannonState.Deploying;m.PendingShot=true;}break;
            case MDMechState.CannonState.Deploying:if(fire)m.PendingShot=true;if(a.CannonDone)m.Cannon=MDMechState.CannonState.Ready;break;
            case MDMechState.CannonState.Ready:if(!selected||(!fire&&!m.PendingShot&&m.CannonKick<=0)){a.PlayCannon("cannon_retract");m.Cannon=MDMechState.CannonState.Retracting;m.PendingShot=false;}break;
            case MDMechState.CannonState.Retracting:if(a.CannonDone){a.CannonOn=false;m.Cannon=MDMechState.CannonState.Stowed;}else if(fire){a.PlayCannon("cannon_deploy_fire");m.Cannon=MDMechState.CannonState.Deploying;m.PendingShot=true;}break;
        }
    }
    // flames from the back thrusters: steady while hovering, a strong burst at a jump (time-based, independent of the frame rate)
    static void Thrusters(ComponentMDUnit u,float dt) {
        var m=u.Mech;m.JetBurst=Math.Max(0,m.JetBurst-dt);float s=m.JetBurst>0?2.1f:m.Hovering?1:0;if(s<=0){m.JetCarry=0;return;}
        m.JetCarry+=dt*(m.JetBurst>0?70:40);if(m.JetCarry<1)return;
        var w=MDTowerWorld.For(u.Project);var pose=m.Pose(u);var bm=u.Body.Matrix;
        for(;m.JetCarry>=1;m.JetCarry-=1)foreach(var k in new[]{"thrusterL","thrusterR"}){var t=m.Def.muzzles[k];w.Thruster(pose.Point(m.Def,t,bm),pose.Dir(m.Def,t,bm),s);}
    }
    public static string Status(ComponentMDUnit u) {
        var m=u.Mech;
        if(m.EnergyLock>0&&m.Hover)return "能量耗尽";
        switch(m.Weapon) {
            case MDMechWeapon.Missile:return m.Salvo.Count>0?"发射中":$"锁定 {m.Locks.Count}/{m.Type.Locks}";
            case MDMechWeapon.Cannon:return m.Cannon switch{MDMechState.CannonState.Deploying=>"展开中",MDMechState.CannonState.Ready=>m.Cooldown>.15f?"装填中":"就绪",_=>"收起（开火即展开）"};
            case MDMechWeapon.Laser:return m.BeamTime>=0?"照射中":m.Cooldown>.15f?"冷却中":"就绪";
            case MDMechWeapon.Rifle:return m.Trigger&&m.FiredAgo<.5f?"射击中":"就绪";   // 4 shots a second: its 0.25 s between shots is no reload (it flickered 装填中 / 就绪, user 2026-10-21)
        }
        return m.Cooldown>.15f?"装填中":"就绪";
    }
    static Vector3 Target(ComponentMDUnit u,Vector3 from)=>u.Mech.AimPoint??from+AimDirection(u)*60;
    static bool Covered(ComponentMDUnit u,Vector3 muzzle)=>u.Project.FindSubsystem<SubsystemTerrain>(true).Raycast(Eye(u),muzzle,false,true,Solid).HasValue;   // a muzzle poking through a wall
    static Vector3 Muzzle(ComponentMDUnit u,string key,out Vector3 fwd){var m=u.Mech;var pose=m.Pose(u);var mz=m.Def.muzzles[key];fwd=pose.Dir(m.Def,mz,u.Body.Matrix);return pose.Point(m.Def,mz,u.Body.Matrix);}
    static void FireRifle(ComponentMDUnit u) {
        var m=u.Mech;var w=MDTowerWorld.For(u.Project);var from=Muzzle(u,"rifle",out _);
        // 复仇女神: the original's 3-round burst, 0.133 / 0.133 / 0.433 s between shots; a new press starts a new burst
        if(m.Type.BurstRifle){if(!m.WasTrigger)m.BurstShot=0;m.Cooldown=m.BurstShot%3<2?.133f:.433f;m.BurstShot++;}else m.Cooldown=.25f;
        if(Covered(u,from))return;
        var dir=Vector3.Normalize(Target(u,from)-from);m.FiredAgo=0;w.Muzzle(Rifle,from,dir,false);Sound(u,Se(8),from,.55f);
        w.LaunchShell(u,from,dir*Rifle.attack.speed,0,Rifle.attack.power,0,false,Rifle);
    }
    static void FireCannon(ComponentMDUnit u) {
        var m=u.Mech;var w=MDTowerWorld.For(u.Project);var from=Muzzle(u,"cannon",out _);m.Cooldown=1.63f;m.PendingShot=false;m.CannonKick=.35f;
        if(Covered(u,from))return;
        m.Energy-=CannonCost;m.FiredAgo=0;var dir=Vector3.Normalize(Target(u,from)-from);
        w.Muzzle(CannonDef,from,dir,true);Sound(u,Se(11),from,.85f);w.LaunchShell(u,from,dir*CannonDef.attack.speed,0,CannonDef.attack.power,CannonDef.attack.splash,false,CannonDef);
    }
    // Missiles lock while the trigger is held: a creature the crosshair sweeps over (within 3.5 deg of the camera ray or 1.3 m of
    // it, 0.8-133 m away, in sight beyond 13 m) locks at once, up to MaxLocks; releasing launches one missile per lock.
    static void LockSweep(ComponentMDUnit u) {
        var m=u.Mech;m.Locks.RemoveAll(b=>!b.Entity.IsAddedToProject||!MDCombat.Living(b));if(m.Locks.Count>=m.Type.Locks||m.AimDir.LengthSquared()<.5f)return;
        var bodies=new DynamicArray<ComponentBody>();u.Project.FindSubsystem<SubsystemBodies>(true).FindBodiesAroundPoint(new(u.Body.Position.X,u.Body.Position.Z),50*Unit,bodies);
        ComponentBody best=null;float bestAlong=float.MaxValue;
        foreach(var b in bodies) {
            if(m.Locks.Contains(b)||!MDCombat.IsEnemy(u,b))continue;var v=MDCombat.Center(b)-m.AimFrom;float along=Vector3.Dot(v,m.AimDir);
            float range=Vector3.Distance(MDCombat.Center(b),u.Body.Position);if(along<=0||range<.3f*Unit||range>50*Unit)continue;
            float off=(v-m.AimDir*along).Length();if(off>Math.Max(1.3f,along*.06f)||along>=bestAlong)continue;
            if(range>=5*Unit&&MDCombat.Trace(u,Eye(u),MDCombat.Center(b),out _)!=b)continue;
            best=b;bestAlong=along;
        }
        if(best!=null){m.Locks.Add(best);Sound(u,Se(7),u.Body.Position,.7f);}
    }
    static void LockMarks(ComponentMDUnit u){var m=u.Mech;if(m.Locks.Count==0)return;var w=MDTowerWorld.For(u.Project);foreach(var b in m.Locks)if(b.Entity.IsAddedToProject)w.LockMark(MDCombat.Center(b),Math.Max(.7f,b.BoxSize.Y*.7f));}
    static void LaunchMissile(ComponentMDUnit u,ComponentBody target) {
        var m=u.Mech;var w=MDTowerWorld.For(u.Project);var from=Muzzle(u,"missile",out var fwd);if(Covered(u,from))return;
        var side=Vector3.Normalize(Vector3.Cross(fwd,Vector3.UnitY));var aim=target==null?Vector3.Normalize(Target(u,from)-from):fwd;
        var v=Vector3.Normalize(aim+Vector3.UnitY*.35f+side*R(-.3f,.3f))*(.05f*Unit*60);   // leaves slowly, drifting up and aside
        w.Muzzle(Missile,from,Vector3.Normalize(v),false);Sound(u,Se(9),from,.6f);m.FiredAgo=0;
        w.LaunchMissile(u,Missile,from,v,target,target==null?0:R(.33f,.67f),.004f*Unit*3600,12*Unit,R(8,12),Missile.attack.power);
    }
    // The laser: a beam from the rifle that follows the crosshair for 1 s, growing at 160 m/s up to 59 m, hurting what it
    // touches every 1/6 s; then 1 s cooldown.
    static void Beam(ComponentMDUnit u,float dt) {
        var m=u.Mech;var w=MDTowerWorld.For(u.Project);m.BeamTime+=dt;
        if(m.BeamTime>=1){m.BeamTime=-1;m.Cooldown=1;return;}
        var from=Muzzle(u,"rifle",out _);if(Covered(u,from))return;var dir=Vector3.Normalize(Target(u,from)-from);
        m.BeamLength=Math.Min(m.BeamLength+60*Unit*dt,22*Unit);var hit=MDCombat.Trace(u,from,from+dir*m.BeamLength,out var end);
        var c=new Color(Laser.fx.color[0],Laser.fx.color[1],Laser.fx.color[2]);w.HoldBeam(u,from,end,.16f,c);   // one beam, a smooth fading ribbon behind it
        m.BeamTick-=dt;if(m.BeamTick<=0&&hit!=null){m.BeamTick=1/6f;MDTowerBrain.Hit(u,hit,Laser.attack.power,end,dir);w.Sparks(end,-dir,4,c,4,.8f,.3f,.04f);Sound(u,Se(13),end,.4f);}
    }
    // The grenade: thrown from the cannon's base in an arc 4 m high that lands at 75 % of the crosshair's distance (40 m at most).
    static void ThrowGrenade(ComponentMDUnit u) {
        var m=u.Mech;var d=m.Def;var pose=m.Pose(u);int j=Math.Max(0,d.Joint(d.roles.grenade));var from=Vector3.Transform(d.pivots[j],pose.Model[j]*u.Body.Matrix)+Vector3.UnitY*.3f;
        m.Cooldown=1.33f;if(Covered(u,from))return;
        var aim=Target(u,from);var flat=new Vector3(aim.X-from.X,0,aim.Z-from.Z);float dist=Math.Min(flat.Length()*.75f,15*Unit);flat=flat.LengthSquared()>1e-4f?Vector3.Normalize(flat):Forward(u.Yaw);
        float landY=aim.Y,top=Math.Max(from.Y,landY)+1.5f*Unit,vy=MathF.Sqrt(2*Gravity*(top-from.Y)),t=(vy+MathF.Sqrt(2*Gravity*(top-landY)))/Gravity;
        var v=flat*(dist/t)+Vector3.UnitY*vy;m.FiredAgo=0;Sound(u,Se(23),from,.7f);
        MDTowerWorld.For(u.Project).LaunchShell(u,from,v,Gravity,Grenade.attack.power,Grenade.attack.splash,false,Grenade);
    }
    static ComponentBody NearCrosshair(ComponentMDUnit u,float range,float cone) {
        var m=u.Mech;var dir=m.AimDir.LengthSquared()>.5f?m.AimDir:AimDirection(u);var bodies=new DynamicArray<ComponentBody>();
        u.Project.FindSubsystem<SubsystemBodies>(true).FindBodiesAroundPoint(new(u.Body.Position.X,u.Body.Position.Z),range,bodies);
        return bodies.Where(b=>MDCombat.IsEnemy(u,b)&&Vector3.Distance(b.Position,u.Body.Position)<=range&&Vector3.Dot(Vector3.Normalize(MDCombat.Center(b)-u.Body.Position-Vector3.UnitY*2),dir)>MathF.Cos(cone))
            .OrderBy(b=>Vector3.DistanceSquared(b.Position,u.Body.Position)).FirstOrDefault();
    }
    // melee: the creatures in a 120 deg wedge up to 5.5 m in front, at the hit frame of the clip (the punch is 1000 %)
    static void MeleeStrike(ComponentMDUnit u) {
        var w=MDTowerWorld.For(u.Project);var f=Forward(u.Yaw);var c=u.Body.Position+Vector3.UnitY*1.6f;var bodies=new DynamicArray<ComponentBody>();bool any=false;
        u.Project.FindSubsystem<SubsystemBodies>(true).FindBodiesAroundPoint(new(c.X,c.Z),7,bodies);
        foreach(var b in bodies) {
            if(!MDCombat.IsEnemy(u,b))continue;var to=MDCombat.Center(b)-c;var flat=new Vector3(to.X,0,to.Z);float dist=flat.Length();
            if(dist>5.5f+b.BoxSize.X*.5f||Vector3.Dot(flat/Math.Max(dist,.01f),f)<.5f)continue;
            var at=MDCombat.Center(b);MDTowerBrain.Hit(u,b,10000*DamageScale,at,f,false,"kinetic");w.Impact(CannonDef,at,f,true);any=true;
            if(b.Mass<=800)b.Velocity+=f*7+Vector3.UnitY*3;
        }
        if(any)Sound(u,Se(17),c,.8f);
    }
    public static void Sound(ComponentMDUnit u,string name,Vector3 at,float volume)=>
        u.Project.FindSubsystem<SubsystemAudio>(true).PlaySound(name,volume,MathUtils.Lerp(-.05f,.05f,(float)random.NextDouble()),at,16,false);
}
