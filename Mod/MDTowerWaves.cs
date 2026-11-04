using Engine;

namespace Game;

// 0.18.0 (players via the user, 2026-11-04: "声波塔感觉没效果，可以改成发射声波"): the sonic tower's wave. A front that leaves the
// emitter towards the target at attack.speed, attack.arc degrees wide, and runs out at the tower's range. It is drawn where it is
// (three stacked arcs of short beams, renewed every 1/60 s) and it stuns what it passes in the same step (MDTowerBrain.WaveTick),
// so what the player sees and what it holds agree. Not saved: a wave lasts about a second.
public sealed partial class MDTowerWorld {
    sealed class Wave { public ComponentMDUnit owner; public Vector3 from,dir; public float r,range,speed,half,emit; public readonly HashSet<ComponentBody> done=[]; }
    readonly List<Wave> waves=[];
    public int Waves=>waves.Count;
    public void LaunchWave(ComponentMDUnit owner,Vector3 from,Vector3 dir,float range,float speed,float arc) {
        dir.Y=0;if(dir.LengthSquared()<1e-6f){dir=owner.Body.Matrix.Forward;dir.Y=0;}
        dir=dir.LengthSquared()>1e-6f?Vector3.Normalize(dir):-Vector3.UnitZ;
        waves.Add(new Wave{owner=owner,from=from,dir=dir,r=.4f,range=range,speed=Math.Max(4,speed),half=MathUtils.DegToRad(Math.Clamp(arc,10,360))*.5f});
    }
    void SimulateWaves(float dt) {
        for(int i=waves.Count-1;i>=0;i--) {
            var v=waves[i];
            if(!v.owner.Entity.IsAddedToProject||v.owner.Tower==null||v.r>=v.range){waves.RemoveAt(i);continue;}
            float r0=v.r;v.r=Math.Min(v.range,v.r+v.speed*dt);
            MDTowerBrain.WaveTick(v.owner,v.from,v.dir,r0,v.r,v.half,v.done);
            v.emit-=dt;if(v.emit<=0){v.emit=1/60f;WaveFront(v);}
        }
    }
    // the front: from the emitter's height down to about a metre over the ground in the first 6 m, an arc of short beams with a
    // fainter one over and under it (one arc only at the reduced particle settings)
    void WaveFront(Wave v) {
        var fx=v.owner.Tower.fx;var c=C(fx.color);var c2=C(fx.color2);float t=Math.Min(1,v.r/6);
        float y=MathUtils.Lerp(v.from.Y,v.owner.Body.Position.Y+1.1f,t);float fade=v.r>v.range-2?Math.Max(.2f,(v.range-v.r)/2):1;
        int n=Math.Clamp((int)(v.r*v.half*2/.7f),4,28);float a0=MathF.Atan2(v.dir.X,v.dir.Z);
        int layers=MDSettings.FxLevel>0?1:3;
        for(int l=0;l<layers;l++) {
            float dy=(l-1)*.45f*Math.Min(1,v.r/3),w=l==1||layers==1?.2f:.1f;var col=(l==1||layers==1?c:c2)*fade*(l==1||layers==1?1:.6f);
            var prev=Vector3.Zero;
            for(int k=0;k<=n;k++) {
                float a=a0-v.half+2*v.half*k/n;var p=new Vector3(v.from.X+MathF.Sin(a)*v.r,y+dy,v.from.Z+MathF.Cos(a)*v.r);
                if(k>0)AddBeam(prev,p,w*fx.scale,col,.05f,false);
                if((l==1||layers==1)&&k%3==1)Emit(S.Glow,p,Vector3.Zero,.06f,.35f,.5f,c*.45f*fade,c*0f,true);   // a soft glow along the front
                prev=p;
            }
        }
        if(random.Next(3)==0)Emit(S.Glow,new Vector3(v.from.X,y,v.from.Z)+v.dir*v.r,Vector3.Zero,.1f,.6f,.9f,c*.4f*fade,c*0f,true);
    }
}
