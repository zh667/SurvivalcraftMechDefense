using Engine;
using Engine.Graphics;
using GameEntitySystem;
using TemplatesDatabase;

namespace Game;

public class ComponentMDUnit : Component,IUpdateable {
    public string Kind; public ComponentBody Body; public ComponentDamage Damage; public ComponentMount Mount;
    public float Yaw,AimYaw,AimPitch;
    public string Animation="Idle"; public float AnimationTime,AnimationDuration;
    public int TurretVariant=1;
    // Towers: definition from Assets/Towers/towers.json; the model is drawn by ComponentMDTowerModel.
    public string TowerId; public MDTowerDef Tower;
    public int ShotCount; public float ScanTimer,Channel,ChannelTick; public ComponentBody Target,ChannelTarget;
    public SubsystemTime Clock;
    public ComponentBody Tracked; public Vector3 TrackedAt,TrackedVelocity;   // the target's observed movement (MDTowerBrain.Track)
    public int DismantleHits; public double DismantleAt;   // pickaxe hits towards taking the tower down (MDTowerMaintenance), not saved
    // Kind "Mech" is Mercury, the mech a player boards (MDMech.cs, MDMechPilot.cs; since 0.8.0, on the template of the 0.5.0
    // sample mech). Kind "Enemy" was the Destroy Gunners sample enemy, removed in 0.6.0: its template stays in the database so
    // that old worlds still load, and such an entity lets its rider off and removes itself on its first update.
    public MDMechState Mech;
    public bool Legacy=>Kind=="Enemy";
    float[] recoilAge=[];
    public float TowerPitch=>Tower==null?0:Tower.pitchFixed?Tower.restPitch:Math.Clamp(AimPitch,Tower.PitchMin,Tower.PitchMax);
    public void TickRecoil(float dt){for(int i=0;i<recoilAge.Length;i++)recoilAge[i]+=dt;}
    public void Kick(int muzzle) {
        if(Tower==null||Tower.muzzles.Length==0)return;int k=Array.IndexOf(Tower.recoilParts,Tower.muzzles[Math.Clamp(muzzle,0,Tower.muzzles.Length-1)].part);
        if(k>=0)recoilAge[k]=0;
    }
    // Fast kick back, slower return: offset along the barrel's own +Z (towards the breech).
    public float RecoilOffset(int k) {
        if(Tower==null||k>=recoilAge.Length)return 0;float t=recoilAge[k];
        float f=t<.05f?t/.05f:Math.Max(0,1-(t-.05f)/.32f);f=f*f*(3-2*f);return Tower.recoil[k]*f;
    }
    public float[] RecoilOffsets(){var r=new float[recoilAge.Length];for(int k=0;k<r.Length;k++)r[k]=RecoilOffset(k);return r;}
    public bool Destroyed {get;private set;}
    public float WreckTime=>wreck;
    public float Cooldown; float wreck,lastHitpoints;
    // Health set from outside (a redeployed tower, a repair): no hit reaction.
    public void SetHitpoints(float hitpoints){Damage.Hitpoints=lastHitpoints=Math.Clamp(hitpoints,0,1);}
    public UpdateOrder UpdateOrder=>(UpdateOrder)1;
    public Vector3 Muzzle=>Mech!=null?MDMechBrain.Eye(this):MDTowerBrain.Muzzle(this);
    void TowerDestroyed() {
        var w=MDTowerWorld.For(Project);var top=Body.Position+Vector3.UnitY*(Tower.bounds[1][1]*.55f);
        w.Explosion(top,Math.Max(1.5f,Tower.radius*.6f),"heavy",new Color(255,200,120),new Color(255,120,40));
        Project.FindSubsystem<SubsystemAudio>(true).PlaySound("Audio/Explosion1",.7f,0,top,10,false);
    }
    public override void Load(ValuesDictionary v,IdToEntityMap map) {
        Kind=v.GetValue<string>("Kind");Clock=Project.FindSubsystem<SubsystemTime>(true);Body=Entity.FindComponent<ComponentBody>(true);Damage=Entity.FindComponent<ComponentDamage>(true);Mount=Entity.FindComponent<ComponentMount>();
        if(Legacy)return;
        if(Kind=="Mech") {
            Mech=new MDMechState(MDMechType.ById(v.GetValue("MechType","Mercury")));Mech.Load(v);Yaw=MathF.Atan2(-Body.Matrix.Forward.X,-Body.Matrix.Forward.Z);AimYaw=v.GetValue("AimYaw",Yaw);lastHitpoints=Damage.Hitpoints;
            if(Damage.Hitpoints<=0||v.GetValue("Destroyed",false)){Damage.Hitpoints=0;Destroyed=true;wreck=v.GetValue("WreckTime",0f);}
            return;
        }
        TurretVariant=Math.Clamp(v.GetValue("TurretVariant",1),1,3);ShotCount=v.GetValue("TurretShotCount",0);
        TowerId=v.GetValue("TowerId","");if(string.IsNullOrEmpty(TowerId))TowerId="battle_turret_"+TurretVariant;
        Tower=MDTowerLibrary.Get(TowerId)??throw new InvalidOperationException("Unknown tower "+TowerId);
        recoilAge=Enumerable.Repeat(10f,Tower.recoilParts.Length).ToArray();
        Yaw=MathF.Atan2(-Body.Matrix.Forward.X,-Body.Matrix.Forward.Z);AimYaw=v.GetValue("AimYaw",Yaw);AimPitch=v.GetValue("AimPitch",0f);Cooldown=Math.Clamp(v.GetValue("Cooldown",0f),0,2);lastHitpoints=Damage.Hitpoints;PlayEvent("Deploy",.8f);
        if(Damage.Hitpoints<=0||v.GetValue("Destroyed",false)) {Damage.Hitpoints=0;PlayEvent("Death",1.4f);Destroyed=true;wreck=v.GetValue("WreckTime",0f);AnimationTime=wreck;}
    }
    public override void Save(ValuesDictionary v,EntityToIdMap map) {
        if(Legacy)return;
        if(Mech!=null){v.SetValue("AimYaw",AimYaw);v.SetValue("Destroyed",Destroyed);v.SetValue("WreckTime",wreck);Mech.Save(v);return;}
        v.SetValue("AimYaw",AimYaw);v.SetValue("AimPitch",AimPitch);v.SetValue("Cooldown",Cooldown);v.SetValue("TurretVariant",TurretVariant);v.SetValue("TurretShotCount",ShotCount);v.SetValue("Destroyed",Destroyed);v.SetValue("WreckTime",wreck);}
    public void PlayEvent(string name,float duration) {
        if(Destroyed)return;
        Animation=name;AnimationTime=0;
        AnimationDuration=Math.Max(.01f,duration);
    }
    // A rider saved on a legacy mech is set down where the mech stood (that space was free) and gets the first-person view back.
    void ReleaseRider() {
        if(Mount?.Rider is not {} r)return;var c=r.ComponentCreature;
        r.m_isAnimating=false;c.ComponentBody.ParentBody=null;c.ComponentBody.Position=Body.Position+Vector3.UnitY*.1f;c.ComponentBody.Velocity=Vector3.Zero;
        if(c is ComponentPlayer p)p.GameWidget.ActiveCamera=p.GameWidget.FindCamera<FppCamera>();
    }
    public override void OnEntityRemoved(){if(Mech!=null)MDMechPilot.RemoveHud(this);if(Mech!=null&&MDMechPilot.Driver(this) is {} p)MDMechPilot.Exit(p,this,true);else ReleaseRider();base.OnEntityRemoved();}
    public void Update(float dt) {
        if(Legacy){ReleaseRider();Project.RemoveEntity(Entity,true);return;}
        if(Mech!=null){MechUpdate(Math.Min(dt,.1f));return;}
        dt=Math.Min(dt,.1f);Cooldown=Math.Max(0,Cooldown-dt);
        if(Damage.Hitpoints<=0&&!Destroyed){PlayEvent("Death",1.4f);Destroyed=true;Target=null;Channel=0;TowerDestroyed();}
        AnimationTime+=dt;if(AnimationTime>=AnimationDuration&&Animation!="Idle"&&!Destroyed)Animation="Idle";
        if(Damage.Hitpoints<lastHitpoints-.001f&&Damage.Hitpoints>0)PlayEvent("Hit",.28f);lastHitpoints=Damage.Hitpoints;
        MDTowerBrain.Update(this,dt);
        if(Destroyed){wreck+=dt;if(wreck>8)Project.RemoveEntity(Entity,true);}
    }
    void MechUpdate(float dt) {
        if(Damage.Hitpoints<=0&&!Destroyed) {
            Destroyed=true;if(MDMechPilot.Driver(this) is {} p)MDMechPilot.Exit(p,this,true);
            var top=Body.Position+Vector3.UnitY*2.6f;MDTowerWorld.For(Project).Explosion(top,3.5f,"heavy",new Color(255,200,120),new Color(255,120,40));
            MDMechBrain.Sound(this,MDMechBrain.Se(20),top,1);   // the original's player destruction
        }
        if(!Destroyed&&Damage.Hitpoints<lastHitpoints-.001f){MDMechBrain.Sound(this,MDMechBrain.Se(19),Body.Position+Vector3.UnitY*2,.6f);if(Mech.Anim.ActionDone&&Mech.Grounded)Mech.Anim.PlayAction("hit");}
        lastHitpoints=Damage.Hitpoints;
        if(MDMechPilot.Driver(this)==null)MDMechPilot.Idle(Mech);
        MDMechPilot.Hud(this);MDMechBrain.Update(this,dt);
        if(Destroyed){wreck+=dt;if(wreck>10)Project.RemoveEntity(Entity,true);}
    }
}

public class ComponentMDDamage : ComponentDamage {
    public override void Update(float dt) {
        if(m_componentBody.CrushedTime>0)Damage(dt*.25f);
        if(m_componentBody.Position.Y<0)Damage(1);
        if(m_componentOnFire?.IsOnFire==true)Damage(dt/100);
        float fall=Math.Abs(m_componentBody.CollisionVelocityChange.Y);if(fall>15)Damage((fall-15)*.015f);
    }
}

public static class MDCombat {
    public static Vector3 Center(ComponentBody b)=>(b.BoundingBox.Min+b.BoundingBox.Max)*.5f;
    public static bool Living(ComponentBody b)=>b.Entity.IsAddedToProject&&(b.Entity.FindComponent<ComponentDamage>()?.Hitpoints??b.Entity.FindComponent<ComponentHealth>()?.Health??0)>0;
    // Towers attack hostile creatures only (MDFaction); never a player, a tower or the player's herd. The mech's pilot chooses
    // the targets: its fire hurts any creature except the player's side (players, towers, the player's herd and mounts).
    public static bool IsEnemy(ComponentMDUnit source,ComponentBody b) {
        if(!Living(b)||b==source.Body||b.ParentBody==source.Body)return false;
        var creature=b.Entity.FindComponent<ComponentCreature>();
        if(source.Mech!=null)return creature!=null&&!MDFaction.Friendly(b.Entity);
        return creature!=null&&MDFaction.Hostile(creature,source.Clock.GameTime);
    }
    // The first body or terrain on a segment, for sight checks and shots. Bodies of the player's side (other towers, Mercury,
    // players, the player's herd and mounts) are passed through: a tower behind another tower used to see only its neighbour
    // and never fired, and its shots stopped on the neighbour (user, 2026-10-21: towers and robots sometimes miss each other).
    public static ComponentBody Trace(ComponentMDUnit source,Vector3 start,Vector3 end,out Vector3 point) {
        var terrain=source.Project.FindSubsystem<SubsystemTerrain>(true).Raycast(start,end,false,true,(v,d)=>BlocksManager.Blocks[Terrain.ExtractContents(v)].IsCollidable_(v));
        var body=source.Project.FindSubsystem<SubsystemBodies>(true).Raycast(start,end,0,(b,d)=>b!=source.Body&&b.ParentBody!=source.Body&&!MDFaction.Friendly(b.Entity));
        float max=Vector3.Distance(start,end);float t=terrain?.Distance??max;float bdist=body?.Distance??float.PositiveInfinity;
        point=start+Vector3.Normalize(end-start)*Math.Min(t,bdist);return bdist<t?body?.ComponentBody:null;
    }
    public static bool ClearShot(ComponentMDUnit source,ComponentBody target)=>Trace(source,source.Muzzle,Center(target),out _)==target;
    public static float AngleDelta(float a,float b)=>MathF.Atan2(MathF.Sin(b-a),MathF.Cos(b-a));
    public static float AngleTowards(float a,float b,float step)=>a+Math.Clamp(AngleDelta(a,b),-step,step);
}
