using Engine;
using Engine.Graphics;

namespace Game;

// 0.17.0 the towers' areas of attack on the ground (MDTowerTools; the towers marked with Z, and the ghost): a ring at the tower's reach (u.Range, a radar's boost included;
// the inner ring of a minimum range; a long-range tower's band (directional) as its rectangle), laid on the terrain surface near the
// tower's own height, a faint fill inside. The ghost of a tower being placed or moved adds its footprint box, green or red, and an
// arrow for its facing. Colour: the tower's own (fx.color).
public sealed partial class MDTowerWorld {
    FlatBatch3D rangeBatch;
    readonly Dictionary<(int,int,int),float> surface=[];double surfaceAt;
    public static int RingsDrawn,GhostsDrawn,FillsDrawn;   // tests (this frame)
    // 0.18.0 every tower at once (the core's click, MDTowerTools.ShowAll): the ring of each within RingRange m of the camera, the fill
    // only within FillRange m (a fill is a few hundred quads; dozens of them far off cost a phone more than they show)
    public const float RingRange=96,FillRange=40;
    double refreshAt;
    void DrawRanges(Vector3 eye) {
        RingsDrawn=GhostsDrawn=FillsDrawn=0;
        var g=MDTowerTools.Current;var shown=MDTowerTools.Shown;
        if(MDTowerTools.ShowAll&&(time-refreshAt>1||time<refreshAt)){refreshAt=time;MDTowerTools.Refresh(project);}
        if(shown.Count==0&&g==null)return;
        rangeBatch??=SubsystemParticles.PrimitivesRenderer.FlatBatch(3,DepthStencilState.DepthRead,RasterizerState.CullNoneScissor,BlendState.AlphaBlend);
        if(time-surfaceAt>2||time<surfaceAt){surfaceAt=time;surface.Clear();}
        foreach(var u in shown) {
            if(!u.Entity.IsAddedToProject||u.Destroyed||u.Project!=SubsystemParticles.Project||u==g?.Moving)continue;
            float d=Vector2.Distance(new(u.Body.Position.X,u.Body.Position.Z),new(eye.X,eye.Z))-u.Range;   // to the ring
            if(MDTowerTools.ShowAll&&d>RingRange)continue;
            RangeArea(u.Tower,u.Body.Position,u.Body.Matrix.Forward,u.Range,1,!MDTowerTools.ShowAll||d<FillRange);
        }
        if(g!=null&&g.Has) {
            var f=Matrix.CreateFromQuaternion(g.Rotation).Forward;RangeArea(g.Def,g.At,f,g.Def.attack.range,1,true);RangeFootprint(g,f);GhostsDrawn++;
        }
    }
    static Color RangeFade(Color c,float a)=>new Color(c.R,c.G,c.B,(byte)255)*a;
    void RangeArea(MDTowerDef d,Vector3 at,Vector3 forward,float range,float strength,bool fill) {
        if(range<=.5f)return;var a=d.attack;var c=C(d.fx.color);RingsDrawn++;
        if(a.directional) {   // the band in front of it: along 0..range, a.width wide
            forward.Y=0;forward=Vector3.Normalize(forward);var side=new Vector3(-forward.Z,0,forward.X)*(a.width*.5f);
            var p0=at-side;var p1=at-side+forward*range;var p2=at+side+forward*range;var p3=at+side;   // 0.18.0 with a radar's boost, as it hits
            RangeEdge(p0,p1,at.Y,c,strength);RangeEdge(p1,p2,at.Y,c,strength);RangeEdge(p2,p3,at.Y,c,strength);RangeEdge(p3,p0,at.Y,c,strength);
            return;
        }
        RangeRing(at,range,c,strength,fill);if(fill)FillsDrawn++;
        if(a.minRange>.5f)RangeRing(at,a.minRange,new Color(255,120,100),strength*.8f,false);
    }
    // The fill: a polar grid (radial steps of about 2 m), every vertex on the ground under it and 5 cm over it; a cell whose corners differ
    // by more than half a block in height (a block's edge, a step) is left out rather than drawn as a slope across the block. 0.17.0 first cut drew a fan
    // from a centre 6 cm up to rim points on the ground itself: near the rim it lay in the ground's plane and lost the depth test there,
    // so wedges of it were missing, changing with the view (user, 11-02: "会有一些锥形区域没绘制攻击范围"); over uneven ground the fan
    // also went under the terrain.
    const float FillLift=.05f;
    Vector3[] fillPrev=[],fillRow=[];
    void RangeRing(Vector3 at,float r,Color c,float strength,bool fill) {
        int n=Math.Clamp((int)(r*4),32,180);Vector3 prev=default;
        for(int i=0;i<=n;i++) {
            float t=i*MathF.PI*2/n;var p=new Vector3(at.X+MathF.Cos(t)*r,0,at.Z+MathF.Sin(t)*r);p.Y=RangeSurface(p.X,p.Z,at.Y);
            if(i>0)RangeRibbon(prev,p,c,strength);
            prev=p;
        }
        if(!fill)return;
        var col=RangeFade(c,.16f*strength);int k=Math.Clamp((int)MathF.Ceiling(r/2),2,32);
        if(fillPrev.Length<n+1){fillPrev=new Vector3[n+1];fillRow=new Vector3[n+1];}
        var centre=new Vector3(at.X,RangeSurface(at.X,at.Z,at.Y)+FillLift,at.Z);
        for(int j=1;j<=k;j++) {
            float rr=r*j/k;
            for(int i=0;i<=n;i++){float t=i*MathF.PI*2/n;float x=at.X+MathF.Cos(t)*rr,z=at.Z+MathF.Sin(t)*rr;fillRow[i]=new Vector3(x,RangeSurface(x,z,at.Y)+FillLift,z);}
            for(int i=0;i<n;i++) {
                if(j==1){if(Flat(centre.Y,fillRow[i].Y,fillRow[i+1].Y,centre.Y))rangeBatch.QueueTriangle(centre,fillRow[i],fillRow[i+1],col);}
                else if(Flat(fillPrev[i].Y,fillPrev[i+1].Y,fillRow[i+1].Y,fillRow[i].Y))rangeBatch.QueueQuad(fillPrev[i],fillPrev[i+1],fillRow[i+1],fillRow[i],col);
            }
            (fillPrev,fillRow)=(fillRow,fillPrev);
        }
        FillCells+=n*k;
    }
    public static int FillCells;   // tests
    static bool Flat(float a,float b,float c,float d)=>MathF.Max(MathF.Max(a,b),MathF.Max(c,d))-MathF.Min(MathF.Min(a,b),MathF.Min(c,d))<=.5f;
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

// 0.18.0 health bars over the towers in MDTowerTools.Shown (the core's click shows all, Z one), within BarRange m: green over
// half, amber, red under a quarter; grey while the tower is out of action (MDThreat.CoreDestroyed). Drawn with the status icons'
// batch after them (MDElements.DrawElements).
public sealed partial class MDTowerWorld {
    public const float BarRange=64;
    public static int TowerBarsDrawn;   // tests
    void DrawTowerBars(Vector3 eye,Vector3 right,Vector3 up) {
        TowerBarsDrawn=0;if(MDTowerTools.Shown.Count==0)return;
        foreach(var u in MDTowerTools.Shown) {
            if(!u.Entity.IsAddedToProject||u.Destroyed||u.Project!=project)continue;
            float d=Vector3.Distance(u.Body.Position,eye);if(d>BarRange)continue;
            float hp=Math.Clamp(u.Damage.Hitpoints,0,1),w=1.5f+.03f*d,h=.16f+.004f*d,fade=Math.Clamp((BarRange-d)/8,0,1);
            var at=new Vector3(u.Body.Position.X,u.Body.BoundingBox.Max.Y+(MDElements.Of(u.Tower)!=MDElement.None||MDMedic.Is(u)?1.5f:.7f)+.012f*d,u.Body.Position.Z);   // over its damage-type icon
            Bar(at,right,up,w+.06f,h+.06f,0,1,new Color((byte)12,(byte)14,(byte)18,(byte)(220*fade)));
            Bar(at,right,up,w,h,0,1,new Color((byte)70,(byte)40,(byte)40,(byte)(230*fade)));
            var c=u.Offline?new Color(150,150,150):hp>.5f?new Color(110,232,120):hp>.25f?new Color(255,190,80):new Color(240,80,70);
            if(hp>0)Bar(at,right,up,w,h,0,hp,c*fade);
            TowerBarsDrawn++;
        }
    }
}
