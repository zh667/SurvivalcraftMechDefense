using Engine;

namespace Game;

// Source VAT RGBAHalf texels decoded at build time; no generated recoil curve.
public static class MDEndfieldAnimation {
    public sealed class Clip {
        public string clipName {get;set;}
        public int startFrame {get;set;}
        public int frameDuration {get;set;}
        public int loop {get;set;}
    }
    public sealed class Data {
        public float fps {get;set;}
        public float[][][] tracks {get;set;}
        public Clip[] clips {get;set;}
        public string sourceSha256 {get;set;}
    }
    static Clip Find(Data d,string suffix)=>d.clips.Single(c=>c.clipName.EndsWith(suffix,StringComparison.Ordinal));
    public static float Duration(Data d,string state)=>state switch {
        "Fire"=>Find(d,"attack_loop_01").frameDuration/d.fps,
        "Hit"=>(Find(d,"damage_start_01").frameDuration+Find(d,"damage_end_01").frameDuration)/d.fps,
        "Death"=>Find(d,"damage_start_01").frameDuration/d.fps,
        "Deploy"=>Find(d,"open_idle_01").frameDuration/d.fps,
        _=>1
    };
    public static float Frame(Data data,string state,float time) {
        Clip clip;
        if(state=="Fire"||state.StartsWith("Fire_"))clip=Find(data,"attack_loop_01");
        else if(state is "Hit" or "Death") {
            clip=Find(data,"damage_start_01");float duration=clip.frameDuration/data.fps;
            if(time>=duration){time-=duration;clip=Find(data,state=="Death"?"damage_loop_01":"damage_end_01");}
        } else clip=Find(data,"open_idle_01");
        return clip.startFrame+Math.Clamp(time*data.fps,0,clip.frameDuration-1);
    }
    public static Matrix Sample(Data data,int track,float frame) {
        var frames=data.tracks[track];int a=Math.Clamp((int)frame,0,frames.Length-1),b=Math.Min(a+1,frames.Length-1);
        float fraction=Math.Clamp(frame-a,0,1);var p=frames[a];var q=frames[b];
        var r=Quaternion.Slerp(new(p[0],p[1],p[2],p[3]),new(q[0],q[1],q[2],q[3]),fraction);
        var pos=Vector3.Lerp(new(p[4],p[5],p[6]),new(q[4],q[5],q[6]),fraction);
        return Matrix.CreateScale(p[7]+(q[7]-p[7])*fraction)*Matrix.CreateFromQuaternion(r)*Matrix.CreateTranslation(pos);
    }
    public static Matrix[] Pose(MDAssets.Mesh mesh,float yaw,float pitch,string state,float time) {
        float frame=Frame(mesh.animation,state,time);var pose=new Matrix[mesh.joints.Length];var solved=new bool[pose.Length];
        Matrix Bone(int i) {
            if(solved[i])return pose[i];var j=mesh.joints[i];var transform=Matrix.Identity;
            if(j.name=="yaw")transform=Matrix.CreateRotationY(yaw);
            else if(j.name=="aim_pitch")transform=Matrix.CreateRotationX(pitch);
            else if(j.vatTrack>=0)transform=Sample(mesh.animation,j.vatTrack,frame);
            var pivot=MDAssets.V(j.pivot);
            pose[i]=Matrix.CreateTranslation(-pivot)*transform*Matrix.CreateTranslation(pivot)*(j.parent<0?Matrix.Identity:Bone(j.parent));
            solved[i]=true;return pose[i];
        }
        for(int i=0;i<pose.Length;i++)Bone(i);
        return pose;
    }
}
