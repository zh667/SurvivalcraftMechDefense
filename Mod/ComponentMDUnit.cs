using Engine;
using Engine.Graphics;
using GameEntitySystem;
using TemplatesDatabase;

namespace Game;

public class ComponentMDUnit : Component,IUpdateable,IDrawable {
    public string Kind; public ComponentBody Body; public ComponentDamage Damage; public ComponentMount Mount;
    public ComponentPlayer Driver=>Mount?.Rider?.Entity.FindComponent<ComponentPlayer>();
    public float Yaw,AimYaw,AimPitch,Gait,Throttle,Steer; public bool Trigger,Walking;
    public int AmmoType; public string Animation="Idle"; public float AnimationTime,AnimationDuration;
    public int TurretVariant=1,TurretShotCount;
    public MDEndfieldCombat Endfield;
    public bool Destroyed {get;private set;}
    public float WreckTime=>wreck;
    public string AssetName=>Kind=="Turret"?"Endfield/battle_turret_"+TurretVariant:Kind;
    public float Cooldown,Heat; public Vector3 ShotStart,ShotEnd; public int ShotAmmoType=-1; public float ShotAge,ShotDuration; float wreck,scan;
    ComponentBody target; readonly PrimitivesRenderer3D renderer=new(); bool failedDraw;MDStatusWidget hud;ComponentPlayer previousDriver;float lastHitpoints;
    public UpdateOrder UpdateOrder=>(UpdateOrder)1; public int[] DrawOrders=>[5];
    public Vector3 Direction=>new(-MathF.Sin(AimYaw)*MathF.Cos(AimPitch),MathF.Sin(AimPitch),-MathF.Cos(AimYaw)*MathF.Cos(AimPitch));
    public Vector3 Muzzle=>Kind=="Mech"?MDAssets.MechMuzzle(this):Kind=="Turret"?MDAssets.TurretMuzzle(this):Body.Position+Vector3.UnitY*2.7f+Direction*.8f;
    public override void Load(ValuesDictionary v,IdToEntityMap map) {
        Kind=v.GetValue<string>("Kind");Body=Entity.FindComponent<ComponentBody>(true);Damage=Entity.FindComponent<ComponentDamage>(true);Mount=Entity.FindComponent<ComponentMount>();
        TurretVariant=Math.Clamp(v.GetValue("TurretVariant",1),1,3);TurretShotCount=v.GetValue("TurretShotCount",0);
        if(Kind=="Turret")Endfield=new(this);
        Yaw=MathF.Atan2(-Body.Matrix.Forward.X,-Body.Matrix.Forward.Z);AimYaw=v.GetValue("AimYaw",Yaw);AimPitch=v.GetValue("AimPitch",0f);AmmoType=Math.Clamp(v.GetValue("AmmoType",0),0,4);Cooldown=Math.Clamp(v.GetValue("Cooldown",0f),0,2);Heat=Math.Clamp(v.GetValue("Heat",0f),0,1);lastHitpoints=Damage.Hitpoints;PlayEvent("Deploy",.8f);
        if(Damage.Hitpoints<=0||v.GetValue("Destroyed",false)) {Damage.Hitpoints=0;PlayEvent("Death",1.4f);Destroyed=true;wreck=v.GetValue("WreckTime",0f);AnimationTime=wreck;}
    }
    public override void Save(ValuesDictionary v,EntityToIdMap map) {v.SetValue("AimYaw",AimYaw);v.SetValue("AimPitch",AimPitch);v.SetValue("AmmoType",AmmoType);v.SetValue("Cooldown",Cooldown);v.SetValue("Heat",Heat);v.SetValue("TurretVariant",TurretVariant);v.SetValue("TurretShotCount",TurretShotCount);v.SetValue("Destroyed",Destroyed);v.SetValue("WreckTime",wreck);}
    public void PlayEvent(string name,float duration) {
        if(Destroyed)return;
        Animation=name;AnimationTime=0;
        if(Kind=="Turret"&&MDAssets.Load(AssetName).animation is {} data)duration=MDEndfieldAnimation.Duration(data,name);
        AnimationDuration=Math.Max(.01f,duration);
    }
    public void CycleAmmo() { if(Kind!="Mech")return; AmmoType=(AmmoType+1)%5;PlayEvent("Reload",.35f); }
    public string AmmoName=>AmmoType switch {1=>"导弹",2=>"高爆炮",3=>"激光",4=>"榴弹",_=>"步枪"};
    void RemoveHud(){if(hud?.ParentWidget is ContainerWidget c)c.Children.Remove(hud);hud=null;}
    public override void OnEntityRemoved(){RemoveHud();if(Driver is {} p)MDPlayer.Exit(p,this,true);base.OnEntityRemoved();}
    public void Update(float dt) {
        dt=Math.Min(dt,.1f);Cooldown=Math.Max(0,Cooldown-dt);ShotAge=Math.Min(ShotDuration,ShotAge+dt);Heat=Math.Max(0,Heat-dt*.22f);
        Endfield?.Update(dt);
        if(Damage.Hitpoints<=0&&!Destroyed){PlayEvent("Death",1.4f);Destroyed=true;Throttle=Steer=0;Trigger=false;target=null;}
        AnimationTime+=dt;if(AnimationTime>=AnimationDuration&&Animation!="Idle"&&!Destroyed)Animation="Idle";
        if(Damage.Hitpoints<lastHitpoints-.001f&&Damage.Hitpoints>0)PlayEvent("Hit",.28f);lastHitpoints=Damage.Hitpoints;
        if(Driver!=previousDriver){RemoveHud();previousDriver=Driver;if(previousDriver!=null){hud=new MDStatusWidget();previousDriver.ComponentGui.ControlsContainerWidget.Children.Add(hud);}}
        hud?.Refresh(this);
        if(Destroyed) {Walking=false;if(Driver is {} p)MDPlayer.Exit(p,this,true);wreck+=dt;if(wreck>8)Project.RemoveEntity(Entity,true);return;}
        if(Kind=="Mech") {
            if(Driver==null){Throttle=Steer=0;Trigger=false;}
            if(Body.StandingOnValue.HasValue||Body.StandingOnBody!=null) {
                Yaw-=Steer*dt*1.7f;Body.Rotation=Quaternion.CreateFromAxisAngle(Vector3.UnitY,Yaw);
                Vector3 velocity=Body.Matrix.Forward*Throttle*6;Body.Velocity=new(velocity.X,Body.Velocity.Y,velocity.Z);
            }
            if(Trigger&&Heat<.9f&&Cooldown<=0)Fire(null);
        } else {
            scan-=dt;
            if(scan<=0) {scan=.25f;target=FindTarget();}
            if(target!=null&&MDCombat.Living(target)) {
                if(Animation=="Idle") {Animation=Kind=="Enemy"?"Walk":"Track";AnimationTime=0;AnimationDuration=Kind=="Enemy"?999999:.25f;}
                Vector3 delta=MDCombat.Center(target)-Muzzle;float desired=MathF.Atan2(-delta.X,-delta.Z);
                AimYaw=MDCombat.AngleTowards(AimYaw,desired,dt*3);AimPitch=Math.Clamp(MathF.Atan2(delta.Y,new Vector2(delta.X,delta.Z).Length()),-.55f,.7f);
                if(Kind=="Enemy") {
                    Yaw=AimYaw;Body.Rotation=Quaternion.CreateFromAxisAngle(Vector3.UnitY,Yaw);
                    Vector3 move=Body.Matrix.Forward;bool advance=delta.LengthSquared()>100&&MDCombat.SafeStep(this,move);
                    Body.Velocity=new(move.X*(advance?2.2f:0),Body.Velocity.Y,move.Z*(advance?2.2f:0));
                }
                if(Cooldown<=0&&!(Kind=="Turret"&&Animation is "Deploy" or "Hit")&&Math.Abs(MDCombat.AngleDelta(AimYaw,desired))<.12f&&MDCombat.ClearShot(this,target))Fire(target);
            } else {if(Animation=="Walk"||Animation=="Track")Animation="Idle";if(Kind=="Enemy")Body.Velocity=new(0,Body.Velocity.Y,0);}
        }
        Walking=Kind!="Turret"&&new Vector2(Body.Velocity.X,Body.Velocity.Z).LengthSquared()>.1f;
        if(Walking)Gait+=dt*(Kind=="Mech"?7:5);
    }
    ComponentBody FindTarget() {
        var bodies=new DynamicArray<ComponentBody>();Project.FindSubsystem<SubsystemBodies>(true).FindBodiesAroundPoint(new(Body.Position.X,Body.Position.Z),32,bodies);
        float range=Kind=="Turret"?MDEndfieldCombat.Range(TurretVariant):32;
        return bodies.Where(b=>b!=Body&&MDCombat.IsEnemy(this,b)&&Vector3.DistanceSquared(b.Position,Body.Position)<range*range&&MDCombat.ClearShot(this,b)).OrderBy(b=>Vector3.DistanceSquared(b.Position,Body.Position)).FirstOrDefault();
    }
    void Fire(ComponentBody aim) {
        var profile=Kind=="Mech"?MDCombat.Ammo(AmmoType):Kind=="Turret"?new(45,.45f,"Fire"):new(18,1.1f,"Fire");
        var start=Muzzle;var end=aim==null?start+Direction*48:MDCombat.Center(aim);
        // A barrel poking through a wall must not move the ray origin past cover.
        var torso=Body.Position+Vector3.UnitY*(Kind=="Mech"?3.5f:2.6f);
        if(Project.FindSubsystem<SubsystemTerrain>(true).Raycast(torso,start,false,true,(v,d)=>BlocksManager.Blocks[Terrain.ExtractContents(v)].IsCollidable_(v)).HasValue)return;
        if(Kind=="Turret") {if(aim!=null)Endfield.Fire(aim);return;}
        var hit=MDCombat.Trace(this,start,end,out Vector3 point);
        // Friendly bodies block a shot rather than being filtered out of the ray.
        if(hit!=null&&MDCombat.IsEnemy(this,hit))ComponentMiner.AttackBody(new ProjectileAttackment(hit.Entity,Driver?.Entity??Entity,point,Vector3.Normalize(end-start),profile.power,null));
        ShotStart=start;ShotEnd=point;ShotAmmoType=Kind=="Mech"?AmmoType:-1;ShotAge=0;ShotDuration=MDCombat.ShotDuration(Kind,ShotAmmoType);Cooldown=profile.interval;Heat=Math.Min(1,Heat+.065f);PlayEvent(profile.animation,.34f);
        // Do not route Endfield turret fire through the temporary Destroy Gunners clip.
        // Its Endfield Wwise cue is archived separately until the native cue is converted.
        if(Kind!="Turret")Project.FindSubsystem<SubsystemAudio>(true).PlaySound("Imported/Audio/Fire",.4f,0,Body.Position,14,false);
    }
    public void Draw(Camera camera,int order) {
        if(Vector3.DistanceSquared(camera.ViewPosition,Body.Position)>10000)return;
        try {
            MDAssets.Draw(this,camera,renderer);
            if(Kind=="Turret")Endfield.Draw(camera,renderer);else MDAssets.DrawShot(this,camera,renderer);
            renderer.Flush(camera.ViewProjectionMatrix);
        }catch(Exception e){renderer.Clear();if(!failedDraw){failedDraw=true;Log.Error("[MechDefense] "+e);}}
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
    public readonly record struct AmmoProfile(float power,float interval,string animation);
    public static AmmoProfile Ammo(int type)=>type switch {1=>new(120,1.0f,"Fire_Missile"),2=>new(180,1.4f,"Fire_Cannon"),3=>new(90,.4f,"Fire_Laser"),4=>new(130,1.2f,"Fire_Grenade"),_=>new(65,.16f,"Fire_Rifle")};
    public static float ShotDuration(string kind,int ammoType)=>kind=="Turret"?.18f:kind=="Enemy"?.12f:ammoType switch {1=>.34f,2=>.22f,3=>.1f,4=>.5f,_=>.1f};
    public static Color AmmoColor(ComponentMDUnit u,int ammoType=-1)=>u.Kind=="Enemy"?new Color(255,80,45):u.Kind=="Turret"?new Color(100,230,255): (ammoType<0?u.AmmoType:ammoType) switch {1=>new Color(255,180,60),2=>new Color(255,110,45),3=>new Color(90,240,255),4=>new Color(180,120,255),_=>new Color(100,230,255)};
    public static Vector3 Center(ComponentBody b)=>(b.BoundingBox.Min+b.BoundingBox.Max)*.5f;
    public static bool Living(ComponentBody b)=>b.Entity.IsAddedToProject&&(b.Entity.FindComponent<ComponentDamage>()?.Hitpoints??b.Entity.FindComponent<ComponentHealth>()?.Health??0)>0;
    public static bool IsEnemy(ComponentMDUnit source,ComponentBody b) {
        if(!Living(b)||b==source.Body||b.ParentBody==source.Body)return false;
        var unit=b.Entity.FindComponent<ComponentMDUnit>();var player=b.Entity.FindComponent<ComponentPlayer>();
        if(source.Kind=="Enemy")return player!=null||(unit!=null&&unit.Kind!="Enemy");
        if(player!=null)return false;if(unit!=null)return unit.Kind=="Enemy";
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
    public static bool SafeStep(ComponentMDUnit unit,Vector3 dir) {
        var t=unit.Project.FindSubsystem<SubsystemTerrain>(true);var p=unit.Body.Position+dir*2;
        return !t.Raycast(unit.Body.Position+Vector3.UnitY,p+Vector3.UnitY,false,true,(v,d)=>BlocksManager.Blocks[Terrain.ExtractContents(v)].IsCollidable_(v)).HasValue&&t.Raycast(p+Vector3.UnitY,p-Vector3.UnitY*2,false,true,(v,d)=>BlocksManager.Blocks[Terrain.ExtractContents(v)].IsCollidable_(v)).HasValue;
    }
    public static float AngleDelta(float a,float b)=>MathF.Atan2(MathF.Sin(b-a),MathF.Cos(b-a));
    public static float AngleTowards(float a,float b,float step)=>a+Math.Clamp(AngleDelta(a,b),-step,step);
}
