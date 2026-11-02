using Engine;
using Engine.Graphics;

namespace Game;

// 0.17.0 the towers' areas of attack on the ground (MDTowerTools): a ring at the tower's reach (u.Range, a radar's boost included;
// the inner ring of a minimum range; a long-range tower's band (directional) as its rectangle), laid on the terrain surface near the
// tower's own height, a faint fill inside. The ghost of a tower being placed or moved adds its footprint box, green or red, and an
// arrow for its facing. Colour: the tower's own (fx.color).
public sealed partial class MDTowerWorld {
    FlatBatch3D rangeBatch;
    readonly List<ComponentMDUnit> rangeTowers=[];double rangeListAt=-10;
    readonly Dictionary<(int,int,int),float> surface=[];double surfaceAt;
    public static int RingsDrawn,GhostsDrawn;   // tests (this frame)
    void DrawRanges() {
        RingsDrawn=GhostsDrawn=0;
        var g=MDTowerTools.Current;bool all=MDTowerTools.ShowAll;
        if(!all&&g==null)return;
        rangeBatch??=SubsystemParticles.PrimitivesRenderer.FlatBatch(3,DepthStencilState.DepthRead,RasterizerState.CullNoneScissor,BlendState.AlphaBlend);
        if(time-rangeListAt>.5||time<rangeListAt) {
            rangeListAt=time;rangeTowers.Clear();
            foreach(var e in SubsystemParticles.Project.Entities)if(e.FindComponent<ComponentMDUnit>() is {Kind:"Turret",Tower:not null} u)rangeTowers.Add(u);
        }
        if(time-surfaceAt>2||time<surfaceAt){surfaceAt=time;surface.Clear();}
        float others=all?1:.45f;
        foreach(var u in rangeTowers)if(u.Entity.IsAddedToProject&&!u.Destroyed)RangeArea(u.Tower,u.Body.Position,u.Body.Matrix.Forward,u.Range,others*(u==g?.Moving?.5f:1));
        if(g!=null&&g.Has) {
            var f=Matrix.CreateFromQuaternion(g.Rotation).Forward;RangeArea(g.Def,g.At,f,g.Def.attack.range,1);RangeFootprint(g,f);GhostsDrawn++;
        }
    }
    static Color RangeFade(Color c,float a)=>new Color(c.R,c.G,c.B,(byte)255)*a;
    void RangeArea(MDTowerDef d,Vector3 at,Vector3 forward,float range,float strength) {
        if(range<=.5f)return;var a=d.attack;var c=C(d.fx.color);RingsDrawn++;
        if(a.directional) {   // the band in front of it: along 0..range, a.width wide
            forward.Y=0;forward=Vector3.Normalize(forward);var side=new Vector3(-forward.Z,0,forward.X)*(a.width*.5f);
            var p0=at-side;var p1=at-side+forward*a.range;var p2=at+side+forward*a.range;var p3=at+side;
            RangeEdge(p0,p1,at.Y,c,strength);RangeEdge(p1,p2,at.Y,c,strength);RangeEdge(p2,p3,at.Y,c,strength);RangeEdge(p3,p0,at.Y,c,strength);
            return;
        }
        RangeRing(at,range,c,strength,true);
        if(a.minRange>.5f)RangeRing(at,a.minRange,new Color(255,120,100),strength*.8f,false);
    }
    void RangeRing(Vector3 at,float r,Color c,float strength,bool fill) {
        int n=Math.Clamp((int)(r*4),32,180);Vector3 prev=default;var centre=new Vector3(at.X,at.Y+.06f,at.Z);
        for(int i=0;i<=n;i++) {
            float t=i*MathF.PI*2/n;var p=new Vector3(at.X+MathF.Cos(t)*r,0,at.Z+MathF.Sin(t)*r);p.Y=RangeSurface(p.X,p.Z,at.Y);
            if(i>0) {
                RangeRibbon(prev,p,c,strength);
                if(fill)rangeBatch.QueueTriangle(centre,prev,p,RangeFade(c,.10f*strength));
            }
            prev=p;
        }
    }
    void RangeEdge(Vector3 a,Vector3 b,float y,Color c,float strength) {
        int n=Math.Max(1,(int)(Vector3.Distance(a,b)));Vector3 prev=default;
        for(int i=0;i<=n;i++){var p=Vector3.Lerp(a,b,i/(float)n);p.Y=RangeSurface(p.X,p.Z,y);if(i>0)RangeRibbon(prev,p,c,strength);prev=p;}
    }
    // a flat strip 0.14 m wide just over the ground between two points, bright at the edge
    void RangeRibbon(Vector3 a,Vector3 b,Color c,float strength) {
        var d=b-a;d.Y=0;if(d.LengthSquared()<1e-6f)return;d=Vector3.Normalize(d);var s=new Vector3(-d.Z,0,d.X)*.07f;var up=Vector3.UnitY*.07f;
        rangeBatch.QueueQuad(a-s+up,b-s+up,b+s+up,a+s+up,RangeFade(c,.85f*strength));
    }
    // the top of the ground at (x, z): the highest solid block with air above it from 4 m over the tower's base to 10 m under it
    float RangeSurface(float x,float z,float baseY) {
        int bx=(int)MathF.Floor(x),bz=(int)MathF.Floor(z),by=(int)MathF.Floor(baseY);
        if(surface.TryGetValue((bx,bz,by),out float h))return h;
        var t=SubsystemParticles.Project.FindSubsystem<SubsystemTerrain>(true).Terrain;h=baseY;
        if(t.GetChunkAtCell(bx,bz)!=null)
            for(int y=by+4;y>=by-10&&y>0;y--){int v=t.GetCellValue(bx,y,bz),above=t.GetCellValue(bx,y+1,bz);if(RangeSolid(v)&&!RangeSolid(above)){h=y+1;break;}}
        return surface[(bx,bz,by)]=h;
    }
    static bool RangeSolid(int v)=>BlocksManager.Blocks[Terrain.ExtractContents(v)].IsCollidable_(v);
    // the footprint of the ghost (its cells x its height), green if it can go there, red if not, and its facing as an arrow on top
    void RangeFootprint(MDTowerTools.Ghost g,Vector3 forward) {
        var size=MDTowerPlacement.Cells(g.Def);var c=g.Ok?new Color(110,255,150):new Color(255,90,80);
        var lo=new Vector3(g.At.X-size.X*.5f,g.At.Y+.02f,g.At.Z-size.Z*.5f);var hi=new Vector3(lo.X+size.X,g.At.Y+size.Y,lo.Z+size.Z);
        rangeBatch.QueueBoundingBox(new BoundingBox(lo,hi),RangeFade(c,.9f));
        var b=new Vector3(lo.X,lo.Y+.03f,lo.Z);var e=new Vector3(hi.X,lo.Y+.03f,hi.Z);
        rangeBatch.QueueQuad(b,new Vector3(e.X,b.Y,b.Z),e,new Vector3(b.X,b.Y,e.Z),RangeFade(c,.28f));   // floor
        foreach(var (p,q) in new[]{(new Vector3(lo.X,0,lo.Z),new Vector3(hi.X,0,lo.Z)),(new Vector3(hi.X,0,lo.Z),new Vector3(hi.X,0,hi.Z)),(new Vector3(hi.X,0,hi.Z),new Vector3(lo.X,0,hi.Z)),(new Vector3(lo.X,0,hi.Z),new Vector3(lo.X,0,lo.Z))})
            rangeBatch.QueueQuad(new Vector3(p.X,lo.Y,p.Z),new Vector3(q.X,lo.Y,q.Z),new Vector3(q.X,hi.Y,q.Z),new Vector3(p.X,hi.Y,p.Z),RangeFade(c,.10f));   // walls
        forward.Y=0;if(forward.LengthSquared()<1e-6f)return;forward=Vector3.Normalize(forward);var side=new Vector3(-forward.Z,0,forward.X);
        var m=new Vector3(g.At.X,lo.Y+.05f,g.At.Z);float half=Math.Min(size.X,size.Z)*.5f;
        rangeBatch.QueueTriangle(m+forward*(half+.6f),m+side*.45f+forward*half,m-side*.45f+forward*half,RangeFade(c,.95f));   // facing
    }
}
