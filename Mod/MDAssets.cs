using Engine;
using Engine.Graphics;
using System.Text.Json;

namespace Game;

public static class MDAssets {
    public sealed class Vertex { public float[] p {get;set;} public float[] n {get;set;} public float[] uv {get;set;} public int bone {get;set;} }
    public sealed class Joint { public string name {get;set;} public int parent {get;set;} public float[] pivot {get;set;} public int vatTrack {get;set;}=-1; }
    public sealed class Mesh { public Vertex[] vertices {get;set;} public int[] indices {get;set;} public Joint[] joints {get;set;} public bool pointSampling {get;set;} public Texture2D Texture; }
    static readonly Dictionary<string,Mesh> cache=[];
    public static Vector3 V(float[] a)=>new(a[0],a[1],a[2]);
    public static Mesh Load(string name) {
        if(cache.TryGetValue(name,out var old))return old;
        var stream=ContentManager.GetStream("Imported/"+name+"/model.json");stream.Position=0;
        var mesh=JsonSerializer.Deserialize<Mesh>(stream);mesh.Texture=ContentManager.Get<Texture2D>("Imported/"+name+"/texture");
        cache.Add(name,mesh);return mesh;
    }
    static Matrix Around(Vector3 p,Matrix r)=>Matrix.CreateTranslation(-p)*r*Matrix.CreateTranslation(p);
    public static Matrix[] Pose(Mesh mesh,string kind,float gait,float amplitude,float yaw,float pitch,string animation="Idle",float animationTime=0,float animationDuration=1,int ammoType=0) {
        Matrix[] result=new Matrix[mesh.joints.Length];bool[] done=new bool[result.Length];
        float t=Math.Clamp(animationDuration<=0?0:animationTime/animationDuration,0,1),pulse=MathF.Sin(t*MathF.PI),recoil=pulse*pulse;
        int left=kind=="Mech"?19:7,right=kind=="Mech"?25:12;
        Matrix Bone(int i) {
            if(done[i])return result[i];var j=mesh.joints[i];Matrix r=Matrix.Identity;
            if(kind=="Turret") {
                // The converter reflects Unity Z once for both mesh and sockets.
                if(i==1)r=Matrix.CreateRotationY(yaw);
                if(i==2)r=Matrix.CreateRotationX(pitch);
            }
            else {
                if(i==2)r=Matrix.CreateRotationY(yaw);
                // The source bind pose folds the shoulder cannon sideways. Deploy it
                // about its original hinge; pitch then follows the player's sight.
                if(kind=="Mech"&&i==18)r=Matrix.CreateRotationY(MathF.PI/2)*Matrix.CreateRotationX(pitch);
                if(i==left||i==right)r=Matrix.CreateRotationX(MathF.Sin(gait+(i==right?MathF.PI:0))*amplitude);
                if(i==left+2||i==right+2)r=Matrix.CreateRotationX(Math.Max(0,-MathF.Sin(gait+(i==right+2?MathF.PI:0)))*amplitude*.8f);
            }
            // Event poses are retargeted to the recovered bind joints. The raw
            // PSMd containers remain indexed alongside these deterministic poses.
            if(animation=="Fire"||animation.StartsWith("Fire_")) {
                if((kind=="Mech"&&i==18)||(kind=="Turret"&&i==2))r=Matrix.CreateRotationX(-.22f*recoil)*r;
                if(kind=="Enemy"&&i==2)r=Matrix.CreateRotationZ(.08f*recoil)*r;
            } else if(animation=="Reload") {
                if((kind=="Mech"&&i==18)||i==2)r=Matrix.CreateRotationX((t<.5f?-.3f:.3f)*MathF.Sin(t*MathF.PI*2))*r;
            } else if(animation=="Hit") {
                if(i==2)r=Matrix.CreateRotationZ(.12f*pulse)*r;
            } else if(animation=="Death") {
                if(kind=="Enemy") {
                    // Fall from the feet so the whole recovered enemy lands as one body.
                    if(i==0)r=Matrix.CreateRotationZ(-1.35f*t)*Matrix.CreateRotationX(.18f*t)*r;
                    if(i==2)r=Matrix.CreateRotationZ(.14f*pulse)*r;
                    if(i==left||i==right)r=Matrix.CreateRotationX(-.35f*t)*r;
                } else {
                    if(i==2)r=Matrix.CreateRotationZ(.75f*t)*Matrix.CreateRotationX(.45f*t)*r;
                    if(kind!="Turret"&&(i==left||i==right))r=Matrix.CreateRotationX(-.8f*t)*r;
                }
            } else if(animation=="Deploy") {
                if(i==0)r=Matrix.CreateScale(1,.75f+.25f*MathF.Sin(t*MathF.PI),1)*r;
            } else if(animation=="Track"&&kind=="Turret"&&i==1) {
                r=Matrix.CreateRotationY(.035f*MathF.Sin(t*MathF.PI))*r;
            }
            result[i]=Around(V(j.pivot),r)*(j.parent<0?Matrix.Identity:Bone(j.parent));done[i]=true;return result[i];
        }
        for(int i=0;i<result.Length;i++)Bone(i);return result;
    }
    public static Vector3 MechMuzzle(ComponentMDUnit u) {
        var mesh=Load("Mech");var pose=Pose(mesh,"Mech",u.Gait,u.Walking?.35f:0,u.AimYaw-u.Yaw,u.AimPitch,u.Animation,u.AnimationTime,u.AnimationDuration,u.AmmoType);
        return Vector3.Transform(new Vector3(3.41f,3.96f,-1.11f),pose[18]*u.Body.Matrix);
    }
    static void Diamond(FlatBatch3D batch,Vector3 center,Vector3 axis,Vector3 side,float size,Color color) {
        axis=Vector3.Normalize(axis);side=Vector3.Normalize(side-axis*Vector3.Dot(side,axis));
        var up=Vector3.Normalize(Vector3.Cross(axis,side));var a=center+side*size;var b=center+up*size;var c=center-side*size;var d=center-up*size;
        batch.QueueTriangle(a,b,c,color);batch.QueueTriangle(a,c,d,color);
    }
    public static void DrawShot(ComponentMDUnit unit,Camera camera,PrimitivesRenderer3D renderer) {
        if(unit.ShotDuration<=0||unit.ShotAge>=unit.ShotDuration)return;
        var color=MDCombat.AmmoColor(unit,unit.ShotAmmoType);var flat=renderer.FlatBatch();
        var start=unit.ShotStart;var end=unit.ShotEnd;var direction=Vector3.Normalize(end-start);if(direction.LengthSquared()<.001f)direction=unit.Direction;
        float progress=Math.Clamp(unit.ShotAge/unit.ShotDuration,0,1);float travel=unit.ShotAmmoType==3?1:Math.Clamp(progress*1.35f,0,1);var head=Vector3.Lerp(start,end,travel);
        var side=Vector3.Normalize(Vector3.Cross(direction,Math.Abs(direction.Y)>.9f?Vector3.UnitX:Vector3.UnitY));
        switch(unit.ShotAmmoType) {
            case 1: // Missile: long orange exhaust and a pointed head.
                flat.QueueLine(start,head,color);flat.QueueLine(start-side*.08f,head-side*.08f,color*0.45f);Diamond(flat,head,direction,side,.14f,color);break;
            case 2: // Cannon: compact red shell with a short shock trail.
                flat.QueueLine(start,head,color*0.45f);Diamond(flat,head,direction,side,.22f,color);flat.QueueLine(head-side*.25f,head+side*.25f,color);break;
            case 3: // Laser: hitscan beam with a bright core and two side rails.
                flat.QueueLine(start,end,color);flat.QueueLine(start+side*.055f,end+side*.055f,color*0.5f);flat.QueueLine(start-side*.055f,end-side*.055f,color*0.5f);break;
            case 4: // Grenade: an arcing purple shell rather than a straight tracer.
                var arc=new List<Vector3>();for(int i=0;i<=8;i++){float t=i/8f;arc.Add(Vector3.Lerp(start,end,t)+Vector3.UnitY*(MathF.Sin(t*MathF.PI)*Math.Min(3,Vector3.Distance(start,end)*.18f)));}flat.QueueLineStrip(arc,color);Diamond(flat,head,direction,side,.18f,color);break;
            default: // Rifle: a thin cyan tracer with a brighter core.
                flat.QueueLine(start,head,color);flat.QueueLine(start+side*.035f,head+side*.035f,color*0.45f);break;
        }
    }
    public static void Draw(ComponentMDUnit unit,Camera camera,PrimitivesRenderer3D renderer) {
        var mesh=Load(unit.AssetName);var pose=Pose(mesh,unit.Kind,unit.Gait,unit.Walking?.35f:0,unit.AimYaw-unit.Yaw,unit.AimPitch,unit.Animation,unit.AnimationTime,unit.AnimationDuration,unit.AmmoType);
        var b=renderer.TexturedBatch(mesh.Texture,false,0,DepthStencilState.Default,RasterizerState.CullNoneScissor,BlendState.Opaque,mesh.pointSampling?SamplerState.PointClamp:SamplerState.LinearWrap);
        Vector3[] points=new Vector3[mesh.vertices.Length];
        for(int i=0;i<points.Length;i++)points[i]=Vector3.Transform(V(mesh.vertices[i].p),pose[mesh.vertices[i].bone]*unit.Body.Matrix);
        float light=Math.Max(.23f,unit.Project.FindSubsystem<SubsystemSky>(true).SkyLightIntensity);
        for(int i=0;i<mesh.indices.Length;i+=3) {
            int a=mesh.indices[i],c=mesh.indices[i+1],d=mesh.indices[i+2];var va=mesh.vertices[a];
            // Authored atlas already carries per-face shading; do not darken it twice.
            float shade=mesh.pointSampling?light:light*(.55f+.45f*Math.Abs(va.n[1]));
            b.QueueTriangle(points[a],points[c],points[d],new(va.uv[0],va.uv[1]),new(mesh.vertices[c].uv[0],mesh.vertices[c].uv[1]),new(mesh.vertices[d].uv[0],mesh.vertices[d].uv[1]),new Color(shade,shade,shade));
        }
    }
}
