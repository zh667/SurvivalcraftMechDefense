using Engine;
using Engine.Graphics;
using System.Text.Json;

namespace Game;

// Portable playback of recovered Endfield particle/mesh data, not Unity's shader pipeline.
public static class MDEndfieldEffects {
    public sealed class MaterialData { public string texture {get;set;} public bool additive {get;set;} public Texture2D Texture; }
    public sealed class ParticleData { public float birth {get;set;} public float life {get;set;} public int material {get;set;} public float[][] frames {get;set;} public int mesh {get;set;} public int renderMode {get;set;} public float lengthScale {get;set;} public float velocityScale {get;set;} public float[] right {get;set;} public float[] up {get;set;} public float[] forward {get;set;} }
    public sealed class MeshInstance { public int mesh {get;set;} public int material {get;set;} public float[] matrix {get;set;} }
    public sealed class Clip { public float fps {get;set;} public float duration {get;set;} public ParticleData[] particles {get;set;} public MeshInstance[] meshes {get;set;} }
    public sealed class Library { public MaterialData[] materials {get;set;} public MDAssets.Mesh[] meshes {get;set;} public Dictionary<string,Clip> effects {get;set;} }
    static Library library;
    public static Library Load() {
        if(library!=null)return library;
        var s=ContentManager.GetStream("Imported/Endfield/Effects/library.json");s.Position=0;
        library=JsonSerializer.Deserialize<Library>(s);
        foreach(var m in library.materials)m.Texture=ContentManager.Get<Texture2D>(m.texture);
        return library;
    }
    public static Matrix MatrixOf(float[] a)=>new(a[0],a[1],a[2],a[3],a[4],a[5],a[6],a[7],a[8],a[9],a[10],a[11],a[12],a[13],a[14],a[15]);
    static TexturedBatch3D Batch(PrimitivesRenderer3D r,MaterialData m)=>r.TexturedBatch(m.Texture,false,1,DepthStencilState.DepthRead,RasterizerState.CullNoneScissor,m.additive?BlendState.Additive:BlendState.NonPremultiplied,SamplerState.LinearClamp);
    static void Mesh(PrimitivesRenderer3D r,int id,int mat,Matrix transform,Color color) {
        var l=Load();var mesh=l.meshes[id];var b=Batch(r,l.materials[mat]);
        for(int i=0;i<mesh.indices.Length;i+=3) {
            var a=mesh.vertices[mesh.indices[i]];var c=mesh.vertices[mesh.indices[i+1]];var d=mesh.vertices[mesh.indices[i+2]];
            b.QueueTriangle(Vector3.Transform(MDAssets.V(a.p),transform),Vector3.Transform(MDAssets.V(c.p),transform),Vector3.Transform(MDAssets.V(d.p),transform),new(a.uv[0],a.uv[1]),new(c.uv[0],c.uv[1]),new(d.uv[0],d.uv[1]),color);
        }
    }
    public static void Draw(string key,float age,Matrix root,Camera camera,PrimitivesRenderer3D renderer) {
        var l=Load();var clip=l.effects[key];
        foreach(var instance in clip.meshes)Mesh(renderer,instance.mesh,instance.material,MatrixOf(instance.matrix)*root,Color.White);
        foreach(var p in clip.particles) {
            float elapsed=age-p.birth;if(elapsed<0||elapsed>=p.life||p.frames.Length==0)continue;
            int frame=Math.Min((int)(elapsed*clip.fps),p.frames.Length-1);var f=p.frames[frame];
            var color=new Color(Math.Clamp(f[6],0,1),Math.Clamp(f[7],0,1),Math.Clamp(f[8],0,1),Math.Clamp(f[9],0,1));
            if(f[3]<=0||f[4]<=0||color.A==0)continue;
            var position=Vector3.Transform(new Vector3(f[0],f[1],f[2]),root);
            if(p.mesh>=0) {
                var basis=Matrix.Identity;basis.Right=MDAssets.V(p.right);basis.Up=MDAssets.V(p.up);basis.Forward=-MDAssets.V(p.forward);
                var transform=Matrix.CreateScale(f[3],f[4],f[3])*Matrix.CreateRotationZ(f[5])*basis*Matrix.CreateTranslation(f[0],f[1],f[2])*root;
                Mesh(renderer,p.mesh,p.material,transform,color);continue;
            }
            var right=Vector3.Cross(camera.ViewDirection,camera.ViewUp);right=Vector3.Normalize(right);var up=camera.ViewUp;
            var x=(right*MathF.Cos(f[5])+up*MathF.Sin(f[5]))*(f[3]*.5f);
            var y=(up*MathF.Cos(f[5])-right*MathF.Sin(f[5]))*(f[4]*.5f);
            if(p.renderMode==1&&p.frames.Length>1) {
                var before=p.frames[Math.Max(0,frame-1)];var after=p.frames[Math.Min(p.frames.Length-1,frame+1)];
                var velocity=Vector3.TransformNormal(new Vector3(after[0]-before[0],after[1]-before[1],after[2]-before[2]),root)*clip.fps*.5f;
                if(velocity.LengthSquared()>.00001f) {
                    var along=Vector3.Normalize(velocity);var cross=Vector3.Cross(along,camera.ViewDirection);
                    if(cross.LengthSquared()>.00001f){x=Vector3.Normalize(cross)*f[3]*.5f;y=along*(f[4]*p.lengthScale+velocity.Length()*p.velocityScale)*.5f;}
                }
            }
            Batch(renderer,l.materials[p.material]).QueueQuad(position-x-y,position+x-y,position+x+y,position-x+y,new(f[10],f[13]),new(f[12],f[13]),new(f[12],f[11]),new(f[10],f[11]),color);
        }
    }
}

public sealed class MDEndfieldCombat {
    sealed class Effect { public string Key;public Vector3 Position,Direction;public float Age; }
    sealed class Bolt { public Vector3 Position,Direction;public float Range,Age,Power;public string Key,Hit; }
    readonly ComponentMDUnit owner;
    readonly List<Effect> effects=[];readonly List<Bolt> bolts=[];
    public int Shots {get;private set;} public int Impacts {get;private set;} public int ActiveBolts=>bolts.Count;
    public int ActiveEffects=>effects.Count;public string LastMuzzleEffect {get;private set;} public string LastSound {get;private set;}
    public MDEndfieldCombat(ComponentMDUnit u){owner=u;}
    public static float Range(int variant)=>variant==3?18:15;
    public void EffectAt(string key,Vector3 point,Vector3 direction) {
        if(effects.Count>=64)effects.RemoveAt(0);
        effects.Add(new(){Key=key,Position=point,Direction=direction});
    }
    public void Sound(string name,Vector3 point,float volume=.55f) {
        LastSound="Imported/Endfield/Audio/"+name+"_"+(Shots%3);
        owner.Project.FindSubsystem<SubsystemAudio>(true).PlaySound(LastSound,volume,0,point,18,false);
    }
    public void Fire(ComponentBody target) {
        if(owner.Destroyed||owner.Damage.Hitpoints<=0)return;
        int variant=owner.TurretVariant;int count=owner.TurretShotCount+1;
        bool strong=variant==2?count%4==0:variant==3&&count%6==0;
        var muzzle=MDAssets.TurretMuzzle(owner,(count-1)%2);var end=MDCombat.Center(target);var dir=Vector3.Normalize(end-muzzle);
        var torso=owner.Body.Position+Vector3.UnitY*2.6f;
        if(owner.Project.FindSubsystem<SubsystemTerrain>(true).Raycast(torso,muzzle,false,true,(v,d)=>BlocksManager.Blocks[Terrain.ExtractContents(v)].IsCollidable_(v)).HasValue)return;
        owner.TurretShotCount=count;Shots++;
        string attack=variant switch {1=>"p_interactive_machineguntower_attack_801",2=>strong?"p_fxbat_machineguntower_upgrade_attack_801":"p_fxbat_machineguntower_attack_801",_=>strong?"p_fxbat_machineguntower_super_attack_2502":"p_fxbat_machineguntower_super_attack_2501"};
        string hit=variant switch {1=>"p_interactive_machineguntower_hitspot_801",2=>strong?"p_fxbat_machineguntower_upgrade_hitspot_801":"p_interactive_machineguntower_upgrade_hitspot_801",_=>strong?"p_fxbat_machineguntower_super_hit_2502":"p_fxbat_machineguntower_super_hit_2501"};
        LastMuzzleEffect=attack;EffectAt(attack,muzzle,dir);Sound($"au_int_fac_battle_turret_{variant}_skill",muzzle);
        float power=(variant==1?45:variant==2?95:150)*(strong?1.8f:1);
        if(variant==3) {
            // The source super-tower skill authors direct attack/hit effects, not a named ProjectileTemplate.
            var body=MDCombat.Trace(owner,muzzle,end,out var point);Impact(point,dir,body,power,hit,strong);
        } else {
            string projectile=variant==1?"p_interactive_machineguntower_bullet_801":strong?"p_fxbat_machineguntower_bullet_801":"p_interactive_machineguntower_upgrade_bullet_801";
            bolts.Add(new(){Position=muzzle,Direction=dir,Range=Range(variant)+2,Power=power,Key=projectile,Hit=hit});
        }
        owner.ShotStart=muzzle;owner.ShotEnd=end;owner.Cooldown=2;owner.PlayEvent("Fire",.3f);
    }
    void Impact(Vector3 point,Vector3 direction,ComponentBody body,float power,string hit,bool splash) {
        Impacts++;EffectAt(hit,point,direction);
        Sound($"au_int_fac_battle_turret_{owner.TurretVariant}_skill_hit"+(splash?"_bomb":""),point,.4f);
        if(body!=null&&MDCombat.IsEnemy(owner,body))ComponentMiner.AttackBody(new ProjectileAttackment(body.Entity,owner.Entity,point,direction,power,null));
        if(!splash)return;
        var near=new DynamicArray<ComponentBody>();owner.Project.FindSubsystem<SubsystemBodies>(true).FindBodiesAroundPoint(new(point.X,point.Z),3,near);
        foreach(var b in near) {
            if(b==body||!MDCombat.IsEnemy(owner,b)||Vector3.DistanceSquared(MDCombat.Center(b),point)>9)continue;
            var start=point-direction*.05f;if(MDCombat.Trace(owner,start,MDCombat.Center(b),out var pos)!=b)continue;
            ComponentMiner.AttackBody(new ProjectileAttackment(b.Entity,owner.Entity,pos,direction,power*.5f,null));
        }
    }
    public void Update(float dt) {
        foreach(var e in effects)e.Age+=dt;
        if(effects.Count>0)effects.RemoveAll(e=>e.Age>=MDEndfieldEffects.Load().effects[e.Key].duration);
        for(int i=bolts.Count-1;i>=0;i--) {
            var b=bolts[i];float distance=Math.Min(60*dt,b.Range);var end=b.Position+b.Direction*distance;
            var hit=MDCombat.Trace(owner,b.Position,end,out var point);
            bool collision=hit!=null||Vector3.DistanceSquared(point,end)>.000001f;
            b.Range-=distance;b.Age+=dt;
            if(collision){Impact(point,b.Direction,hit,b.Power,b.Hit,false);bolts.RemoveAt(i);}
            else if(b.Range<=0)bolts.RemoveAt(i);else b.Position=end;
        }
    }
    public void Draw(Camera camera,PrimitivesRenderer3D renderer) {
        foreach(var e in effects)MDEndfieldEffects.Draw(e.Key,e.Age,Matrix.CreateWorld(e.Position,e.Direction,Vector3.UnitY),camera,renderer);
        foreach(var b in bolts)MDEndfieldEffects.Draw(b.Key,b.Age,Matrix.CreateWorld(b.Position,b.Direction,Vector3.UnitY),camera,renderer);
    }
}
