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
    // Kinds "Mech" and "Enemy" were the Destroy Gunners sample units, removed in 0.6.0. Their templates stay in the database
    // so that old worlds still load; such an entity lets its rider off and removes itself on its first update.
    public bool Legacy=>Kind!="Turret";
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
    public UpdateOrder UpdateOrder=>(UpdateOrder)1;
    public Vector3 Muzzle=>MDTowerBrain.Muzzle(this);
    void TowerDestroyed() {
        var w=MDTowerWorld.For(Project);var top=Body.Position+Vector3.UnitY*(Tower.bounds[1][1]*.55f);
        w.Explosion(top,Math.Max(1.5f,Tower.radius*.6f),"heavy",new Color(255,200,120),new Color(255,120,40));
        Project.FindSubsystem<SubsystemAudio>(true).PlaySound("Audio/Explosion1",.7f,0,top,10,false);
    }
    public override void Load(ValuesDictionary v,IdToEntityMap map) {
        Kind=v.GetValue<string>("Kind");Body=Entity.FindComponent<ComponentBody>(true);Damage=Entity.FindComponent<ComponentDamage>(true);Mount=Entity.FindComponent<ComponentMount>();
        if(Legacy)return;
        TurretVariant=Math.Clamp(v.GetValue("TurretVariant",1),1,3);ShotCount=v.GetValue("TurretShotCount",0);
        TowerId=v.GetValue("TowerId","");if(string.IsNullOrEmpty(TowerId))TowerId="battle_turret_"+TurretVariant;
        Tower=MDTowerLibrary.Get(TowerId)??throw new InvalidOperationException("Unknown tower "+TowerId);
        recoilAge=Enumerable.Repeat(10f,Tower.recoilParts.Length).ToArray();
        Yaw=MathF.Atan2(-Body.Matrix.Forward.X,-Body.Matrix.Forward.Z);AimYaw=v.GetValue("AimYaw",Yaw);AimPitch=v.GetValue("AimPitch",0f);Cooldown=Math.Clamp(v.GetValue("Cooldown",0f),0,2);lastHitpoints=Damage.Hitpoints;PlayEvent("Deploy",.8f);
        if(Damage.Hitpoints<=0||v.GetValue("Destroyed",false)) {Damage.Hitpoints=0;PlayEvent("Death",1.4f);Destroyed=true;wreck=v.GetValue("WreckTime",0f);AnimationTime=wreck;}
    }
    public override void Save(ValuesDictionary v,EntityToIdMap map) {if(Legacy)return;v.SetValue("AimYaw",AimYaw);v.SetValue("AimPitch",AimPitch);v.SetValue("Cooldown",Cooldown);v.SetValue("TurretVariant",TurretVariant);v.SetValue("TurretShotCount",ShotCount);v.SetValue("Destroyed",Destroyed);v.SetValue("WreckTime",wreck);}
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
    public override void OnEntityRemoved(){ReleaseRider();base.OnEntityRemoved();}
    public void Update(float dt) {
        if(Legacy){ReleaseRider();Project.RemoveEntity(Entity,true);return;}
        dt=Math.Min(dt,.1f);Cooldown=Math.Max(0,Cooldown-dt);
        if(Damage.Hitpoints<=0&&!Destroyed){PlayEvent("Death",1.4f);Destroyed=true;Target=null;Channel=0;TowerDestroyed();}
        AnimationTime+=dt;if(AnimationTime>=AnimationDuration&&Animation!="Idle"&&!Destroyed)Animation="Idle";
        if(Damage.Hitpoints<lastHitpoints-.001f&&Damage.Hitpoints>0)PlayEvent("Hit",.28f);lastHitpoints=Damage.Hitpoints;
        MDTowerBrain.Update(this,dt);
        if(Destroyed){wreck+=dt;if(wreck>8)Project.RemoveEntity(Entity,true);}
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
    // Towers attack hostile creatures only: land or water predators, or anything chasing a player; never a player or a tower.
    public static bool IsEnemy(ComponentMDUnit source,ComponentBody b) {
        if(!Living(b)||b==source.Body||b.ParentBody==source.Body)return false;
        if(b.Entity.FindComponent<ComponentPlayer>()!=null||b.Entity.FindComponent<ComponentMDUnit>()!=null)return false;
        var creature=b.Entity.FindComponent<ComponentCreature>();var chase=b.Entity.FindComponent<ComponentChaseBehavior>();
        return creature!=null&&(creature.Category.HasFlag(CreatureCategory.LandPredator)||creature.Category.HasFlag(CreatureCategory.WaterPredator)||chase?.m_target is ComponentPlayer);
    }
    public static ComponentBody Trace(ComponentMDUnit source,Vector3 start,Vector3 end,out Vector3 point) {
        var terrain=source.Project.FindSubsystem<SubsystemTerrain>(true).Raycast(start,end,false,true,(v,d)=>BlocksManager.Blocks[Terrain.ExtractContents(v)].IsCollidable_(v));
        var body=source.Project.FindSubsystem<SubsystemBodies>(true).Raycast(start,end,0,(b,d)=>b!=source.Body&&b.ParentBody!=source.Body);
        float max=Vector3.Distance(start,end);float t=terrain?.Distance??max;float bdist=body?.Distance??float.PositiveInfinity;
        point=start+Vector3.Normalize(end-start)*Math.Min(t,bdist);return bdist<t?body?.ComponentBody:null;
    }
    public static bool ClearShot(ComponentMDUnit source,ComponentBody target)=>Trace(source,source.Muzzle,Center(target),out _)==target;
    public static float AngleDelta(float a,float b)=>MathF.Atan2(MathF.Sin(b-a),MathF.Cos(b-a));
    public static float AngleTowards(float a,float b,float step)=>a+Math.Clamp(AngleDelta(a,b),-step,step);
}
