using Engine;
using Engine.Graphics;

namespace Game;

// 0.17.0 the towers' areas of attack on the ground (MDTowerTools; the towers marked with Z or by the core, and the ghost of a tower
// being placed or moved): a ring at the tower's reach (u.Range, a radar's boost included; the inner ring of a minimum range; a
// long-range tower's band (directional) as its rectangle), a faint fill inside. The ghost adds its footprint box, green or red, and
// an arrow for its facing. Colour: the tower's own (fx.color).
// 0.18.2 (user, 2026-11-04: "在雪地上，炮塔攻击范围会被覆盖（看看有没有其他的也会覆盖），平地上如果出现一个坑，那个地方也会绘制不上攻击范围"):
// - The ground under the ring was "the highest collidable block with air over it, +1". Snow is a non-collidable layer 1/8 high, so
//   the ring (7 cm up) lay inside it; water and magma are not collidable either (the ring lay on the bed, under the water's depth);
//   slabs, carpets and stairs counted as full blocks (the ring floated up to 0.94 m over them); fences lifted it a block; leaves within
//   4 m over the ground put it on the canopy. Now RangeSurface is the top that shows at that very point: a fluid's surface, the top of
//   the collision box under the point (slab 1/2, carpet 1/16, stairs by their step), a thin non-collidable layer's top (snow); plants,
//   leaves and thin blocks (fences, posts, doors: boxes under half the cell) are looked through to the ground under them.
// - The ring was a chord every 1.6 m (r 26) between points on the ground: a pit or a step between two points was spanned by a straight
//   strip through the blocks (hidden in the ground), a pit under one point pulled both chords down into the ground. Now each chord is
//   cut where it crosses a block edge, every piece lies flat on its own block's top, and a vertical strip joins two heights.
// - The fill left out every 2 m cell whose corners differ by more than half a block (a pit took 3-4 m of fill with it); such a cell
//   is now split in four, twice (down to 0.5 m), and the last pieces lie flat at their middle.
// - What still hides the ring (grass blades, leaves, a fence post, another tower, a pit's near wall, a hill) gets it drawn a second
//   time, faint (XrayAlpha), where it is behind them (depth test "greater"), within XrayRange m only: no ring through the hills far off.
// - Cost: the shapes are built once and kept (RangeShape), rebuilt every RebuildTime s (at most one a frame, new ones at most
//   NewPerFrame a frame) or at once when the tower moves; the column of blocks under each point is read once per build. A frame only
//   queues the kept quads (0.17.0 looked every vertex up every frame and read every column again every 2 s in one frame).
public sealed partial class MDTowerWorld {
    FlatBatch3D rangeBatch,rangeXray;
    static readonly DepthStencilState Behind=new(){DepthBufferWriteEnable=false,DepthBufferFunction=CompareFunction.Greater};
    public static int RingsDrawn,GhostsDrawn,FillsDrawn,ShapesBuilt,JoinsDrawn;   // tests (this frame; ShapesBuilt counts up)
    // 0.18.0 every tower at once (the core's click, MDTowerTools.ShowAll): the ring of each within RingRange m of the camera, the fill
    // only within FillRange m (a fill is a few hundred quads; dozens of them far off cost a phone more than they show)
    public const float RingRange=96,FillRange=40,XrayRange=48,XrayAlpha=.45f,RebuildTime=2,StripPerMetre=.0022f;
    const int NewPerFrame=8;
    public static bool XrayEnabled=true;   // lab switch
    double refreshAt;int builtThisFrame,newThisFrame;
    sealed class RangeShape {
        public Vector3 At,Forward;public float Range;public bool Fill,Filled;public double Built;public int Used;
        public readonly List<Vector3> Strip=[];                       // ribbon pieces, 2 ends each (their middle line)
        public readonly List<(Vector3 p,float y0,float y1,Color c)> Joins=[]; // vertical joins between two heights
        public readonly List<Vector3> FillTris=[];                    // fill triangles, 3 corners each
        public readonly List<Color> StripColors=[];                   // per quad
        public Color FillColor;public int Cells;
    }
    readonly Dictionary<object,RangeShape> shapes=[];int frameNo;
    void DrawRanges(Vector3 eye) {
        RingsDrawn=GhostsDrawn=FillsDrawn=JoinsDrawn=0;builtThisFrame=newThisFrame=0;frameNo++;
        var g=MDTowerTools.Current;var shown=MDTowerTools.Shown;
        if(MDTowerTools.ShowAll&&(time-refreshAt>1||time<refreshAt)){refreshAt=time;MDTowerTools.Refresh(project);}
        if(shown.Count==0&&g==null){if(shapes.Count>0)shapes.Clear();return;}
        rangeBatch??=SubsystemParticles.PrimitivesRenderer.FlatBatch(3,DepthStencilState.DepthRead,RasterizerState.CullNoneScissor,BlendState.AlphaBlend);
        rangeXray??=SubsystemParticles.PrimitivesRenderer.FlatBatch(3,Behind,RasterizerState.CullNoneScissor,BlendState.AlphaBlend);
        foreach(var u in shown) {
            if(!u.Entity.IsAddedToProject||u.Destroyed||u.Project!=SubsystemParticles.Project||u==g?.Moving)continue;
            float d=Vector2.Distance(new(u.Body.Position.X,u.Body.Position.Z),new(eye.X,eye.Z))-u.Range;   // to the ring
            if(MDTowerTools.ShowAll&&d>RingRange)continue;
            RangeArea(u,u.Tower,u.Body.Position,u.Body.Matrix.Forward,u.Range,!MDTowerTools.ShowAll||d<FillRange,eye,d<XrayRange);
        }
        if(g!=null&&g.Has) {
            var f=Matrix.CreateFromQuaternion(g.Rotation).Forward;RangeArea((object)g.Moving??"held",g.Def,g.At,f,g.Def.attack.range,true,eye,true);RangeFootprint(g,f);GhostsDrawn++;
        }
        if(frameNo%60==0)foreach(var k in shapes.Where(kv=>frameNo-kv.Value.Used>30).Select(kv=>kv.Key).ToList())shapes.Remove(k);   // towers no longer shown
    }
    static Color RangeFade(Color c,float a)=>new Color(c.R,c.G,c.B,(byte)255)*a;
    void RangeArea(object key,MDTowerDef d,Vector3 at,Vector3 forward,float range,bool fill,Vector3 eye,bool xray) {
        if(range<=.5f)return;var a=d.attack;
        if(!shapes.TryGetValue(key,out var s)||Vector3.DistanceSquared(s.At,at)>1e-6f||Vector3.DistanceSquared(s.Forward,forward)>1e-4f||MathF.Abs(s.Range-range)>1e-3f||s.Fill!=fill) {
            if(s!=null||newThisFrame<NewPerFrame||key is string||key==MDTowerTools.Current?.Moving){s=Build(d,at,forward,range,fill);shapes[key]=s;newThisFrame++;}
            else return;   // the rest in the next frames (a click on the core shows dozens at once)
        }
        else if((time-s.Built>RebuildTime||time<s.Built)&&builtThisFrame==0){s=Build(d,at,forward,range,fill);shapes[key]=s;builtThisFrame++;}
        s.Used=frameNo;RingsDrawn++;if(s.Filled){FillsDrawn++;FillCells+=s.Cells;}
        for(int i=0;i<s.FillTris.Count;i+=3)rangeBatch.QueueTriangle(s.FillTris[i],s.FillTris[i+1],s.FillTris[i+2],s.FillColor);
        for(int i=0,q=0;i<s.Strip.Count;i+=2,q++) {
            var p0=s.Strip[i];var p1=s.Strip[i+1];var c=s.StripColors[q];
            // 0.18.2 the strip widens with distance, and more where it is seen low (the far side of a ring seen from a tower's height was a
            // line under a pixel thick, with gaps where it fell between pixel rows): about the same few pixels everywhere, 0.14 m close by
            var m=(p0+p1)*.5f;float dist=Vector3.Distance(eye,m),low=MathF.Max(MathF.Abs(eye.Y-m.Y)/Math.Max(dist,.01f),.2f);
            float w=Math.Clamp(StripPerMetre*dist/low,.07f,.35f);var dir=new Vector3(p1.X-p0.X,0,p1.Z-p0.Z);if(dir.LengthSquared()<1e-8f)continue;dir=Vector3.Normalize(dir);var n=new Vector3(-dir.Z,0,dir.X)*w;
            rangeBatch.QueueQuad(p0-n,p1-n,p1+n,p0+n,c);
            if(xray&&XrayEnabled)rangeXray.QueueQuad(p0-n,p1-n,p1+n,p0+n,c*(XrayAlpha/.85f));
        }
        foreach(var (p,y0,y1,c) in s.Joins) {   // facing the camera about the vertical
            var w=Vector3.Cross(Vector3.UnitY,new Vector3(p.X-eye.X,0,p.Z-eye.Z));if(w.LengthSquared()<1e-6f)continue;w=Vector3.Normalize(w)*Math.Clamp(StripPerMetre*Vector3.Distance(eye,new Vector3(p.X,(y0+y1)*.5f,p.Z)),.07f,.35f);
            var q0=new Vector3(p.X,y0,p.Z);var q1=new Vector3(p.X,y1,p.Z);
            rangeBatch.QueueQuad(q0-w,q0+w,q1+w,q1-w,c);if(xray&&XrayEnabled)rangeXray.QueueQuad(q0-w,q0+w,q1+w,q1-w,c*(XrayAlpha/.85f));JoinsDrawn++;
        }
    }
    RangeShape Build(MDTowerDef d,Vector3 at,Vector3 forward,float range,bool fill) {
        var a=d.attack;var c=C(d.fx.color);var s=new RangeShape{At=at,Forward=forward,Range=range,Fill=fill,Built=time,FillColor=RangeFade(c,.16f)};
        column.Clear();terrainNow=SubsystemParticles.Project.FindSubsystem<SubsystemTerrain>(true);ShapesBuilt++;
        if(a.directional) {   // the band in front of it: along 0..range, a.width wide
            forward.Y=0;forward=Vector3.Normalize(forward);var side=new Vector3(-forward.Z,0,forward.X)*(a.width*.5f);
            var p0=at-side;var p1=at-side+forward*range;var p2=at+side+forward*range;var p3=at+side;   // 0.18.0 with a radar's boost, as it hits
            var pts=new List<Vector2>();foreach(var (p,q) in new[]{(p0,p1),(p1,p2),(p2,p3),(p3,p0)}){int n=Math.Max(1,(int)Vector3.Distance(p,q));for(int i=0;i<n;i++){var v=Vector3.Lerp(p,q,i/(float)n);pts.Add(new(v.X,v.Z));}}
            Trace(s,pts,at.Y,RangeFade(c,.85f));
            return s;
        }
        int n0=Math.Clamp((int)(range*4),32,180);
        Trace(s,Circle(at,range,n0),at.Y,RangeFade(c,.85f));
        if(a.minRange>.5f)Trace(s,Circle(at,a.minRange,Math.Clamp((int)(a.minRange*4),32,180)),at.Y,RangeFade(new Color(255,120,100),.85f*.8f));
        if(fill){Fill(s,at,range,n0);s.Filled=true;}
        return s;
    }
    static List<Vector2> Circle(Vector3 at,float r,int n){var l=new List<Vector2>(n);for(int i=0;i<n;i++){float t=i*MathF.PI*2/n;l.Add(new(at.X+MathF.Cos(t)*r,at.Z+MathF.Sin(t)*r));}return l;}
    // a closed line on the ground: each segment cut where it crosses a block edge, each piece flat on its own block's top, a vertical
    // join where two pieces differ in height; equal pieces in a row become one strip
    const float RibbonLift=.07f,FillLift=.05f;
    void Trace(RangeShape s,List<Vector2> pts,float baseY,Color c) {
        var cuts=new List<float>(8);float hPrev=float.NaN;Vector2 runStart=default,runEnd=default;Vector2 runDir=default;
        void Flush(){if(!float.IsNaN(hPrev)&&Vector2.DistanceSquared(runStart,runEnd)>1e-8f)Ribbon(s,runStart,runEnd,hPrev+RibbonLift,c);}
        for(int i=0;i<pts.Count;i++) {
            var a=pts[i];var b=pts[(i+1)%pts.Count];var d=b-a;
            cuts.Clear();cuts.Add(0);
            if(MathF.Abs(d.X)>1e-6f)for(float x=MathF.Floor(MathF.Min(a.X,b.X))+1;x<MathF.Max(a.X,b.X);x++)cuts.Add((x-a.X)/d.X);
            if(MathF.Abs(d.Y)>1e-6f)for(float z=MathF.Floor(MathF.Min(a.Y,b.Y))+1;z<MathF.Max(a.Y,b.Y);z++)cuts.Add((z-a.Y)/d.Y);
            cuts.Sort();
            cuts.Add(1);
            for(int k=0;k+1<cuts.Count;k++) {
                float t0=cuts[k],t1=cuts[k+1];if(t1-t0<1e-5f)continue;
                var p=a+d*t0;var q=a+d*t1;var m=(p+q)*.5f;
                float h=RangeSurface(m.X,m.Y,baseY);
                var dir=Vector2.Normalize(q-p);
                if(!float.IsNaN(hPrev)&&MathF.Abs(h-hPrev)<.01f&&Vector2.Dot(dir,runDir)>.9995f){runEnd=q;continue;}
                Flush();
                if(!float.IsNaN(hPrev)&&MathF.Abs(h-hPrev)>=.01f) {   // the join, just off the higher block's face into the air over the lower one
                    var off=(h<hPrev?dir:-dir)*.03f;s.Joins.Add((new Vector3(p.X+off.X,0,p.Y+off.Y),MathF.Min(h,hPrev)+RibbonLift-.07f,MathF.Max(h,hPrev)+RibbonLift+.07f,c));
                }
                hPrev=h;runStart=p;runEnd=q;runDir=dir;
            }
        }
        Flush();
    }
    // a flat strip at height y between two points (its width is set when it is drawn)
    static void Ribbon(RangeShape s,Vector2 a,Vector2 b,float y,Color c){s.Strip.Add(new(a.X,y,a.Y));s.Strip.Add(new(b.X,y,b.Y));s.StripColors.Add(c);}
    // The fill: a polar grid (radial steps of about 2 m), every vertex on the ground under it and 5 cm over it. 0.17.0 first cut drew
    // a fan from a centre 6 cm up to rim points on the ground itself: near the rim it lay in the ground's plane and lost the depth test
    // there (user, 11-02: "会有一些锥形区域没绘制攻击范围"). A cell whose corners differ by more than half a block was then left out;
    // since 0.18.2 it is split in four, twice, and the last pieces lie flat at their middle (a pit is filled at its bottom).
    void Fill(RangeShape s,Vector3 at,float r,int n) {
        int k=Math.Clamp((int)MathF.Ceiling(r/2),2,32);var prev=new Vector2[n+1];var row=new Vector2[n+1];
        var centre=new Vector2(at.X,at.Z);
        for(int j=1;j<=k;j++) {
            float rr=r*j/k;
            for(int i=0;i<=n;i++){float t=i*MathF.PI*2/n;row[i]=new(at.X+MathF.Cos(t)*rr,at.Z+MathF.Sin(t)*rr);}
            for(int i=0;i<n;i++)if(j==1)FillCell(s,centre,centre,row[i+1],row[i],at.Y,0);else FillCell(s,prev[i],prev[i+1],row[i+1],row[i],at.Y,0);
            (prev,row)=(row,prev);
        }
        s.Cells=n*k;
    }
    void FillCell(RangeShape s,Vector2 a,Vector2 b,Vector2 c,Vector2 d,float baseY,int depth) {
        float ha=RangeSurface(a.X,a.Y,baseY),hb=RangeSurface(b.X,b.Y,baseY),hc=RangeSurface(c.X,c.Y,baseY),hd=RangeSurface(d.X,d.Y,baseY);
        float lo=MathF.Min(MathF.Min(ha,hb),MathF.Min(hc,hd)),hi=MathF.Max(MathF.Max(ha,hb),MathF.Max(hc,hd));
        if(hi-lo<=.02f){Tri(s,a,ha,b,hb,c,hc);Tri(s,a,ha,c,hc,d,hd);return;}
        // corners at different heights: blocks only come in steps (a layer of snow, a slab, a block), so a slope between them would dip
        // under the higher one; split (only the pieces over an edge split again), and the last pieces lie flat: on the higher top
        // over a small step (it floats a little over the lower part), at their middle over a block's step
        if(depth==2&&hi-lo<=.5f){Tri(s,a,hi,b,hi,c,hi);Tri(s,a,hi,c,hi,d,hi);return;}
        if(depth<2) {
            var ab=(a+b)*.5f;var bc=(b+c)*.5f;var cd=(c+d)*.5f;var da=(d+a)*.5f;var m=(a+b+c+d)*.25f;
            FillCell(s,a,ab,m,da,baseY,depth+1);FillCell(s,ab,b,bc,m,baseY,depth+1);FillCell(s,m,bc,c,cd,baseY,depth+1);FillCell(s,da,m,cd,d,baseY,depth+1);
            return;
        }
        var mid=(a+b+c+d)*.25f;float h=RangeSurface(mid.X,mid.Y,baseY);Tri(s,a,h,b,h,c,h);Tri(s,a,h,c,h,d,h);
    }
    static void Tri(RangeShape s,Vector2 a,float ha,Vector2 b,float hb,Vector2 c,float hc){s.FillTris.Add(new(a.X,ha+FillLift,a.Y));s.FillTris.Add(new(b.X,hb+FillLift,b.Y));s.FillTris.Add(new(c.X,hc+FillLift,c.Y));}
    public static int FillCells;   // tests

    // ---- the ground under a point
    // The top that shows at (x, z): the column from 6 m over the tower's base to 12 m under it, from the top down, the first cell that
    // is a surface with no surface right over it (else the base's own height: inside a hill higher than that, or over a drop deeper).
    readonly Dictionary<(int,int),(int y,int v)> column=[];SubsystemTerrain terrainNow;
    public float RangeSurface(float x,float z,float baseY) {
        terrainNow??=SubsystemParticles.Project.FindSubsystem<SubsystemTerrain>(true);var t=terrainNow.Terrain;
        int bx=(int)MathF.Floor(x),bz=(int)MathF.Floor(z),by=(int)MathF.Floor(baseY);
        if(!column.TryGetValue((bx,bz),out var cv)) {
            cv=(int.MinValue,0);
            if(t.GetChunkAtCell(bx,bz)!=null) {
                bool over=IsSurface(t.GetCellValue(bx,Math.Min(by+7,255),bz));
                for(int y=Math.Min(by+6,254);y>=by-12&&y>0;y--){int v=t.GetCellValue(bx,y,bz);bool here=IsSurface(v);if(here&&!over){cv=(y,v);break;}over=here;}
            }
            column[(bx,bz)]=cv;
        }
        if(cv.y==int.MinValue)return baseY;
        return cv.y+TopAt(cv.v,x-bx,z-bz);
    }
    // a surface: a fluid; a block whose boxes cover at least half the cell (collidable, or a layer no higher than 1/4 like snow);
    // not leaves, plants or thin blocks (fences, posts, doors, torches)
    bool IsSurface(int v) {
        int c=Terrain.ExtractContents(v);if(c==0)return false;var b=BlocksManager.Blocks[c];
        if(b is FluidBlock)return true;if(b is LeavesBlock)return false;
        var boxes=b.GetCustomCollisionBoxes(terrainNow,v);if(boxes==null||boxes.Length==0)return false;
        float area=0,top=0;foreach(var x in boxes){area+=Math.Max(0,x.Max.X-x.Min.X)*Math.Max(0,x.Max.Z-x.Min.Z);top=Math.Max(top,x.Max.Y);}
        if(area<.5f)return false;
        return b.IsCollidable_(v)||top<=.25f;
    }
    float TopAt(int v,float fx,float fz) {
        var b=BlocksManager.Blocks[Terrain.ExtractContents(v)];
        if(b is FluidBlock fl)return fl.GetLevelHeight(FluidBlock.GetLevel(Terrain.ExtractData(v)));
        float h=-1,any=0;
        foreach(var x in b.GetCustomCollisionBoxes(terrainNow,v)){any=Math.Max(any,x.Max.Y);if(fx>=x.Min.X-1e-4f&&fx<=x.Max.X+1e-4f&&fz>=x.Min.Z-1e-4f&&fz<=x.Max.Z+1e-4f)h=Math.Max(h,x.Max.Y);}
        return Math.Clamp(h>=0?h:any,0,1);
    }
    // the footprint of the ghost (its cells x its height), green if it can go there, red if not, and its facing as an arrow on top;
    // 0.18.2 its floor on the highest top showing inside it (a layer of snow, a carpet), not under it
    void RangeFootprint(MDTowerTools.Ghost g,Vector3 forward) {
        var size=MDTowerPlacement.Cells(g.Def);var c=g.Ok?new Color(110,255,150):new Color(255,90,80);
        float floor=g.At.Y;column.Clear();
        for(int i=0;i<size.X;i++)for(int j=0;j<size.Z;j++){float h=RangeSurface(g.At.X-size.X*.5f+i+.5f,g.At.Z-size.Z*.5f+j+.5f,g.At.Y);if(h>floor&&h<g.At.Y+.5f)floor=h;}
        var lo=new Vector3(g.At.X-size.X*.5f,floor+.02f,g.At.Z-size.Z*.5f);var hi=new Vector3(lo.X+size.X,g.At.Y+size.Y,lo.Z+size.Z);
        rangeBatch.QueueBoundingBox(new BoundingBox(lo,hi),RangeFade(c,.9f));
        if(XrayEnabled)rangeXray.QueueBoundingBox(new BoundingBox(lo,hi),RangeFade(c,XrayAlpha));
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
