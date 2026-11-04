using System.Collections;
using System.Reflection;
using Engine;
using Engine.Graphics;
using Engine.Media;
using Game;
using HarmonyLib;

namespace TowerLab;

// Fog for the lab (mode fb182-fog): after the sky has worked out this frame's fog, LabFog.Mode puts in a thick weather fog (from the
// ground to the build limit) or a short-range haze (the far fog of a short view distance). The terrain and SubsystemParticles' shader
// read these fields when they draw.
[HarmonyPatch(typeof(SubsystemSky),nameof(SubsystemSky.Draw))]
public static class LabFog {
    public static int Mode;   // 0 the game's own, 1 weather fog, 2 haze
    public static void Postfix(SubsystemSky __instance) {
        if(Mode==1){__instance.m_viewFogBottom=0;__instance.m_viewFogTop=255;__instance.m_viewFogDensity=.1f;}
        else if(Mode==2){__instance.m_viewHazeStart=0;__instance.m_viewHazeDensity=1/8f;}
    }
}

// lab --mode fb182 (0.18.2, user 2026-11-04: "在雪地上，炮塔攻击范围会被覆盖（看看有没有其他的也会覆盖），平地上如果出现一个坑，那个地方也会绘制
// 不上攻击范围" and "有雾的话，敌人已经有的元素图标看不清了（可能不止这个有问题）"). Results under "fb182"; screenshots f182_*. Written against
// the 0.18.1 API only (anything newer through reflection), so the same build runs on the 0.18.1 package (the failures before the fix).
// The checks read the pixels: the frame is drawn twice into a render target in the same frame (no update in between), once with the
// overlay and once without it, and the difference at a point, against the overlay's own colour, says how strongly it shows there.
// ranges  one tower (range 15) in a field built for each scene: flat, snow layer, pits (2 deep, 1 deep, a ditch 14 deep), tall grass
//         and flowers, steps (slabs, stairs, a platform, a 2-high wall, carpet, raised ice), water and magma pools, look-through
//         things (a leaf canopy 3 m up, a fence, another tower on the ring). Every 0.25 m along the ring: the point on the top that
//         shows there (the scene's own truth: snow 1/8 up, a slab's half, the water's level, the pit's bottom; the ground under plants,
//         leaves and fences; over the 14 m ditch the tower's own height), 7 cm up, projected; shown = the ring's colour at at least
//         60 % of its strength within 2 px, faint = 30-60 % (seen through what hides it), hidden = less. Every scene: hidden 0; flat,
//         snow, steps, water: shown >= 90 %. The pit scene also checks the fill in and around a 4 x 4 pit 1 deep inside the ring.
// ghost   the snow scene with the tower's deploy item in the hand: the ghost's ring the same way.
// fog     two robots carrying heat and electric 9 and 18 m off (the near one also under a missile's lock-on mark), a reaction's name
//         over a third, a marked tower 9 m off (its damage type icon, health bar and ring): each overlay's pixels (where it changes the
//         frame without fog) with weather fog and with haze against without (the pixel, or for an additive mark what it adds): the
//         median change must stay under 15 (of 255) while the terrain behind changes a lot.
public partial class Lab {
    // the frame drawn into a render target (the game's own screenshot path, without the file); vp: the camera's matrix for it
    static Image Grab(int w,int h,out Matrix vp) {
        var pl=player.ComponentPlayer;var gui=pl.ComponentGui.ControlsContainerWidget;bool vis=gui.IsVisible;var mode=SettingsManager.ResolutionMode;
        using var rt=new RenderTarget2D(w,h,1,ColorFormat.Rgba8888,DepthFormat.Depth24Stencil8);var old=Display.RenderTarget;
        try{gui.IsVisible=false;SettingsManager.ResolutionMode=ResolutionMode.High;Display.RenderTarget=rt;ScreensManager.Draw();vp=pl.GameWidget.ActiveCamera.ViewProjectionMatrix;}
        finally{Display.RenderTarget=old;gui.IsVisible=vis;SettingsManager.ResolutionMode=mode;}
        return rt.GetData(new Rectangle(0,0,w,h));
    }
    static bool ToScreen(Matrix m,Vector3 p,int w,int h,bool flip,out float sx,out float sy) {
        float x=p.X*m.M11+p.Y*m.M21+p.Z*m.M31+m.M41,y=p.X*m.M12+p.Y*m.M22+p.Z*m.M32+m.M42,ww=p.X*m.M14+p.Y*m.M24+p.Z*m.M34+m.M44;
        sx=sy=-1;if(ww<=.01f)return false;x/=ww;y/=ww;sx=(x+1)*.5f*w;sy=(flip?(1+y):(1-y))*.5f*h;return sx>=2&&sy>=2&&sx<w-3&&sy<h-3;
    }
    static int Sum(Color a,Color b)=>Math.Abs(a.R-b.R)+Math.Abs(a.G-b.G)+Math.Abs(a.B-b.B);
    // how strongly colour c shows at (sx, sy) in a against b: the best pixel within 2 px across, 4 px up and down (the far side of a
    // ring seen low is a line 1 px thick, and a few px off the projected point); -1 where c is too close to the ground's colour
    static float Strength(Image a,Image b,float sx,float sy,Color c) {
        int w=a.Width;float best=-1;
        for(int dy=-4;dy<=4;dy++)for(int dx=-2;dx<=2;dx++) {
            int x=(int)sx+dx,y=(int)sy+dy;if(x<0||y<0||x>=w||y>=a.Height)continue;
            var pb=b.Pixels[y*w+x];int den=Sum(c,pb);if(den<80)continue;
            best=Math.Max(best,Sum(a.Pixels[y*w+x],pb)/(float)den);
        }
        return best;
    }
    static Image SaveImage(Image img,string name){try{var dir=ScreenCaptureManager.ScreenshotDir;if(!Storage.DirectoryExists(dir))Storage.CreateDirectory(dir);using var s=Storage.OpenFile(Storage.CombinePaths(dir,name+".png"),OpenFileMode.Create);Image.Save(img,s,ImageFileFormat.Png,false);}catch(Exception e){Log("save "+name+": "+e.Message);}return img;}

    static IEnumerable<bool> Feedback182() {
        Remove(placed.Select(p=>p.unit));placed.Clear();while(Wait(1))yield return true;
        var r=new Dictionary<string,object>();results["fb182"]=r;var bad=new List<string>();results["fb182bad"]=bad;
        bool Part(string p)=>mode=="fb182"||mode=="fb182-"+p;
        var proj=GameManager.Project;var pl=player.ComponentPlayer;var w=MDTowerWorld.For(proj);var terrain=proj.FindSubsystem<SubsystemTerrain>(true);
        var particles=proj.FindSubsystem<SubsystemParticles>(true);
        void ClearAll(){foreach(var e in proj.Entities.Where(e=>e.FindComponent<ComponentCreature>()!=null&&e.FindComponent<ComponentPlayer>()==null).ToList())proj.RemoveEntity(e,true);dums.Clear();}
        IEnumerable<bool> Seconds(double s,Action each=null){for(double s0=Time.RealTime;Time.RealTime-s0<s;){each?.Invoke();yield return true;}}
        var C0=Home;int X0=(int)MathF.Floor(C0.X),Z0=(int)MathF.Floor(C0.Z);
        void Ground(){Clear(X0-22,X0+23,Z0-22,Z0+23);}
        int W=Window.Size.X>=640?Window.Size.X:1280,H=Window.Size.X>=640?Window.Size.Y:720;r["grab"]=$"{W} x {H}";
        var newer=typeof(MDTowerWorld).GetField("JoinsDrawn",BindingFlags.Public|BindingFlags.Static);r["build"]=newer!=null?"0.18.2 (has JoinsDrawn)":"older package";
        int Idx<T>() where T:Block=>BlocksManager.GetBlockIndex<T>();
        int V(int c,int data=0)=>Terrain.MakeBlockValue(c,0,data);
        void Set(int x,int y,int z,int v)=>terrain.ChangeCell(x,y,z,v);
        bool? flip=null;   // the render target's rows (found on the flat scene: whichever way shows the ring there)

        // ---------------------------------------------------------------- ranges
        var tower=MDTowerLibrary.Get("flame_1");float R=tower.attack.range;var ringColor=new Color(tower.fx.color[0],tower.fx.color[1],tower.fx.color[2]);
        // a scene: build it, then the truth at (x, z): the height the ring should lie on (NaN: several, see alts) and whether something may hide it
        IEnumerable<bool> Measure(string scene,Func<float,float,(float top,bool mayHide,float alt)> truth,Dictionary<string,object> into,Func<IEnumerable<(Vector3 p,string what)>> extra=null,MDTowerTools.Ghost ghost=null,ComponentMDUnit u=null) {
            var eye=C0+new Vector3(0,20,24);var focus=C0+new Vector3(0,0,-1);
            foreach(var x in Hold(eye,focus,6))yield return x;
            Cam(eye,focus);
            if(ghost!=null)MDTowerTools.Current=ghost;   // the input set it from this camera's ray in the update: the one kept from close by
            var A=Grab(W,H,out var vp);int rings=MDTowerWorld.RingsDrawn,joins=newer!=null?(int)newer.GetValue(null):-1;
            if(ghost!=null)MDTowerTools.Current=null;else MDTowerTools.Shown.Clear();
            var B=Grab(W,H,out _);
            if(ghost==null&&u!=null)MDTowerTools.Shown.Add(u);
            SaveImage(A,$"f182_{scene}");
            if(flip==null) {   // the flat scene first: the orientation that shows the ring
                int Count(bool f){int n=0;for(int i=0;i<120;i++){float t=i*MathF.PI*2/120;float x=C0.X+MathF.Cos(t)*R,z=C0.Z+MathF.Sin(t)*R;if(ToScreen(vp,new Vector3(x,truth(x,z).top+.07f,z),W,H,f,out var sx,out var sy)&&Strength(A,B,sx,sy,ringColor)>=.6f)n++;}return n;}
                int up=Count(false),down=Count(true);flip=down>up;r["flip"]=$"{flip} (rows top-down {up} / bottom-up {down} of 120 shown)";
            }
            int shown=0,faint=0,hidden=0,off=0,odd=0;var hiddenAt=new List<string>();
            int n=(int)(MathF.PI*2*R/.25f);
            for(int i=0;i<n;i++) {
                float t=i*MathF.PI*2/n;float x=C0.X+MathF.Cos(t)*R,z=C0.Z+MathF.Sin(t)*R;var (top,mayHide,alt)=truth(x,z);
                float s=-1;bool on=false;float px=0,py=0;
                foreach(var y in float.IsNaN(alt)?new[]{top}:new[]{top,alt})if(ToScreen(vp,new Vector3(x,y+.07f,z),W,H,flip.Value,out var sx,out var sy)){on=true;px=sx;py=sy;s=Math.Max(s,Strength(A,B,sx,sy,ringColor));}
                if(!on){off++;continue;}if(s<0){odd++;continue;}
                if(s>=.6f)shown++;else if(s>=.3f)faint++;else{hidden++;if(hiddenAt.Count<6){float bs=-1;int by=0;for(int dy=-16;dy<=16;dy++){float q=Strength(A,B,px,py+dy,ringColor);if(q>bs){bs=q;by=dy;}}
                    hiddenAt.Add($"{t*180/MathF.PI:0}° ({x-C0.X:0.0},{z-C0.Z:0.0}) top {top:0.00}{(mayHide?" (may hide)":"")} {s:0.00} at px ({px:0},{py:0}); best within 16 px up/down {bs:0.00} at {by:+0;-0}");}}
            }
            int seen=shown+faint+hidden;
            var row=new Dictionary<string,object>{["samples"]=n,["shown"]=shown,["faint"]=faint,["hidden"]=hidden,["offscreen"]=off,["sameColour"]=odd,
                ["shownShare"]=seen>0?Math.Round(shown/(double)seen,3):0,["joins"]=joins,["rings"]=rings,["hiddenAt"]=hiddenAt};
            if(extra!=null) {
                var ex=new List<string>();int present=0,total=0;
                foreach(var (p,what) in extra()){if(!ToScreen(vp,p,W,H,flip.Value,out var sx,out var sy))continue;float s=Strength(A,B,sx,sy,ringColor);if(s<0)continue;total++;if(s>=.07f)present++;else if(ex.Count<6)ex.Add($"{what} {s:0.00}");}
                row["fill"]=$"{present} of {total} points show the fill"+(ex.Count>0?"; missing: "+string.Join(", ",ex):"");row["fillShare"]=total>0?Math.Round(present/(double)total,3):0;
            }
            into[scene]=row;
        }
        if(Part("ranges")||Part("ghost")) {
            var rg=new Dictionary<string,object>();r["ranges"]=rg;
            int snow=Idx<SnowBlock>(),grass=Idx<TallGrassBlock>(),flower=Idx<RedFlowerBlock>(),stone=V(Idx<GraniteBlock>()),water=Idx<WaterBlock>(),magma=Idx<MagmaBlock>();
            int slab=Idx<StoneSlabBlock>(),stairs=Idx<StoneStairsBlock>(),carpet=Idx<CarpetBlock>(),ice=Idx<IceBlock>(),leaves=Idx<OakLeavesBlock>(),fence=Idx<WoodenFenceBlock>();
            float waterTop=69+((FluidBlock)BlocksManager.Blocks[water]).GetLevelHeight(0),magmaTop=69+((FluidBlock)BlocksManager.Blocks[magma]).GetLevelHeight(0);
            rg["tower"]=$"{tower.id} range {R}, colour {ringColor}; water top {waterTop:0.000}, magma top {magmaTop:0.000}";
            float Ang(int x,int z){var a=MathF.Atan2(z+.5f-C0.Z,x+.5f-C0.X)*180/MathF.PI;return a<0?a+360:a;}
            float Dist(int x,int z)=>MathF.Sqrt((x+.5f-C0.X)*(x+.5f-C0.X)+(z+.5f-C0.Z)*(z+.5f-C0.Z));
            bool Band(int x,int z,float a0,float a1,float half=2.5f){float a=Ang(x,z);return MathF.Abs(Dist(x,z)-R)<=half&&(a0<=a1?a>=a0&&a<a1:a>=a0||a<a1);}
            bool In(float x,float z,float a0,float a1)=>Band((int)MathF.Floor(x),(int)MathF.Floor(z),a0,a1);
            void Each(Action<int,int> f){for(int x=X0-20;x<=X0+20;x++)for(int z=Z0-20;z<=Z0+20;z++)if(MathF.Abs(x-X0)>1||MathF.Abs(z-Z0)>1)f(x,z);}   // not under the tower
            ComponentMDUnit Tower(){var u=Place(tower,C0,0);u.Cooldown=1e6f;return u;}
            var scenes=new List<(string name,Action build,Func<float,float,(float,bool,float)> truth,Func<IEnumerable<(Vector3,string)>> extra)>{
                ("flat",()=>{},(x,z)=>(70,false,float.NaN),null),
                ("snow",()=>Each((x,z)=>Set(x,70,z,V(snow))),(x,z)=>(70.125f,false,float.NaN),null),
                ("pits",()=>{
                    Each((x,z)=>{
                        if(Band(x,z,80,100,1.5f))for(int y=68;y<70;y++)Set(x,y,z,0);              // near side: 3 wide, 2 deep
                        else if(Band(x,z,265,275,1.5f))Set(x,69,z,0);                              // far side: 2 wide, 1 deep
                        else if(Band(x,z,355,5,.6f))Set(x,69,z,0);                                 // east: 1 x 1, 1 deep
                        else if(Band(x,z,172,188,2.5f))for(int y=56;y<70;y++)Set(x,y,z,0);        // west: a ditch 14 deep
                    });
                    for(int x=X0-7;x<X0-3;x++)for(int z=Z0-2;z<Z0+2;z++)Set(x,69,z,0);            // inside the ring: 4 x 4, 1 deep
                },(x,z)=>{int bx=(int)MathF.Floor(x),bz=(int)MathF.Floor(z);
                    if(Band(bx,bz,80,100,1.5f))return (68,true,float.NaN);if(Band(bx,bz,265,275,1.5f)||Band(bx,bz,355,5,.6f))return (69,true,float.NaN);
                    if(Band(bx,bz,172,188,2.5f))return (70,false,float.NaN);   // deeper than the ring looks: bridged at the tower's height
                    return (70,false,float.NaN);},
                 ()=>{var l=new List<(Vector3,string)>();for(int x=X0-7;x<X0-3;x++)for(int z=Z0-2;z<Z0;z++)l.Add((new Vector3(x+.5f,69.05f,z+.5f),$"pit bottom ({x-X0},{z-Z0})"));
                     for(int x=X0-8;x<X0-2;x++)foreach(int z in new[]{Z0-3,Z0+2})l.Add((new Vector3(x+.5f,70.05f,z+.5f),$"rim ({x-X0},{z-Z0})"));
                     foreach(int x in new[]{X0-8,X0-3})for(int z=Z0-2;z<Z0+2;z++)l.Add((new Vector3(x+.5f,70.05f,z+.5f),$"rim ({x-X0},{z-Z0})"));return l;}),
                ("grass",()=>Each((x,z)=>Set(x,70,z,V((x+z)%5==0?flower:grass))),(x,z)=>(70,true,float.NaN),null),
                ("steps",()=>Each((x,z)=>{
                    if(Band(x,z,0,50))Set(x,70,z,V(slab));                                        // bottom slabs: 0.5
                    else if(Band(x,z,50,110))Set(x,70,z,V(stairs));                               // stairs: 0.5 / 1
                    else if(Band(x,z,110,190))Set(x,70,z,stone);                                  // a platform: 1
                    else if(Band(x,z,190,230)){Set(x,70,z,stone);Set(x,71,z,stone);}              // a wall: 2
                    else if(Band(x,z,230,300))Set(x,70,z,V(carpet));                              // carpet: 1/16
                    else if(Band(x,z,300,360))Set(x,70,z,V(ice));                                 // raised ice: 1
                }),(x,z)=>{int bx=(int)MathF.Floor(x),bz=(int)MathF.Floor(z);float a=Ang(bx,bz);if(!In(x,z,0,360))return (70,false,float.NaN);
                    return a<50?(70.5f,false,float.NaN):a<110?(70.5f,false,71f):a<190?(71,false,float.NaN):a<230?(72,false,float.NaN):a<300?(70.0625f,false,float.NaN):(71,false,float.NaN);},null),
                ("water",()=>Each((x,z)=>{
                    if(Band(x,z,330,30,3)){Set(x,68,z,V(water));Set(x,69,z,V(water));}             // a pool 2 deep
                    else if(Band(x,z,150,210,3))Set(x,69,z,V(magma));                              // magma 1 deep (stone around: nothing to burn)
                }),(x,z)=>{int bx=(int)MathF.Floor(x),bz=(int)MathF.Floor(z);
                    if(Band(bx,bz,330,30,3))return (waterTop,false,float.NaN);if(Band(bx,bz,150,210,3))return (magmaTop,false,float.NaN);return (70,false,float.NaN);},null),
                ("lookthrough",()=>{
                    Each((x,z)=>{
                        if(Band(x,z,200,300,2))Set(x,73,z,V(leaves));                               // a canopy 3 m over the ground
                        else if(Band(x,z,0,60,.6f))Set(x,70,z,V(fence));                            // a fence along the ring
                    });
                },(x,z)=>(70,true,float.NaN),null),
            };
            if(Part("ranges"))foreach(var (name,build,truth,extra) in scenes) {
                ClearAll();Ground();build();MDTowerTools.Shown.Clear();
                pl.ComponentBody.Position=C0+new Vector3(0,0,24);pl.ComponentBody.Velocity=Vector3.Zero;
                var u=Tower();var others=new List<ComponentMDUnit>{u};
                if(name=="lookthrough"){var t2=Place(MDTowerLibrary.Get("battle_turret_1"),C0+new Vector3(MathF.Cos(2.1f)*R,0,MathF.Sin(2.1f)*R),0);t2.Cooldown=1e6f;others.Add(t2);}   // another tower on the ring (120°)
                foreach(var x in Seconds(1.5,()=>{foreach(var o in others)o.Cooldown=1e6f;}))yield return x;   // the terrain's meshes
                MDTowerTools.Shown.Add(u);foreach(var x in Seconds(.5))yield return x;
                foreach(var x in Measure(name,truth,rg,extra,null,u))yield return x;
                var row=(Dictionary<string,object>)rg[name];int hidden=(int)row["hidden"];double share=(double)row["shownShare"];
                bool must=name is "flat" or "snow" or "steps" or "water";
                if(hidden>0||must&&share<.9||row.ContainsKey("fillShare")&&(double)row["fillShare"]<.9)bad.Add($"ranges {name}: shown {row["shown"]} faint {row["faint"]} hidden {hidden} (share {share}){(row.TryGetValue("fill",out var f)?"; "+f:"")}; at {string.Join(" | ",(List<string>)row["hiddenAt"])}");
                MDTowerTools.Shown.Clear();Remove(others);
            }
            if(Part("ghost")) {
                ClearAll();Ground();scenes[1].build();MDTowerTools.Shown.Clear();
                var inv=pl.ComponentMiner.Inventory;inv.ActiveSlotIndex=0;inv.RemoveSlotItems(0,inv.GetSlotCount(0));inv.AddSlotItems(0,MDDeploymentBlock.Value(tower,1),1);
                // the held item's ghost where a camera close by looks (as the player would: within reach), then measured from the far camera
                pl.ComponentBody.Position=C0+new Vector3(0,0,3);pl.ComponentBody.Velocity=Vector3.Zero;
                foreach(var x in Seconds(1.5))yield return x;
                foreach(var x in Hold(C0+new Vector3(0,2.4f,3),C0+new Vector3(0,0,-.5f),8))yield return x;
                var g=MDTowerTools.Current;var gh=new Dictionary<string,object>();rg["ghost"]=gh;
                gh["ghost"]=g==null?"none":$"has {g.Has} ok {g.Ok} at {g.At-C0}";
                if(g?.Has==true) {
                    var c0=C0;C0=g.At;
                    foreach(var x in Measure("ghost_snow",(x,z)=>(70.125f,false,float.NaN),gh,null,g))yield return x;
                    C0=c0;var row=(Dictionary<string,object>)gh["ghost_snow"];
                    if((int)row["hidden"]>0||(double)row["shownShare"]<.9)bad.Add($"ghost on snow: shown {row["shown"]} faint {row["faint"]} hidden {row["hidden"]}");
                } else bad.Add("ghost: none over the snow ("+gh["ghost"]+")");
                inv.RemoveSlotItems(0,inv.GetSlotCount(0));MDTowerTools.Current=null;
            }
            ClearAll();Ground();foreach(var x in Seconds(.5))yield return x;
        }

        // ---------------------------------------------------------------- fog
        if(Part("fog")) {
            var fg=new Dictionary<string,object>();r["fog"]=fg;ClearAll();Ground();MDTowerTools.Shown.Clear();
            pl.ComponentBody.Position=C0+new Vector3(0,0,30);pl.ComponentBody.Velocity=Vector3.Zero;
            var eye=C0+new Vector3(0,3,12);var focus=C0+new Vector3(0,1.6f,0);
            var keep=new List<ComponentMDRobot>();
            ComponentMDRobot Robot(Vector3 at){at.Y=terrain.Terrain.GetTopHeight(Terrain.ToCell(at.X),Terrain.ToCell(at.Z))+1.05f;var e=DatabaseManager.CreateEntity(proj,"MDRobot05",true);var b=e.FindComponent<ComponentBody>(true);b.Position=at;b.Rotation=Quaternion.CreateFromAxisAngle(Vector3.UnitY,0);
                var sp=e.FindComponent<ComponentSpawn>(true);sp.SpawnDuration=0;sp.AutoDespawn=false;proj.AddEntity(e);var rb=e.FindComponent<ComponentMDRobot>(true);rb.Health.AttackResilience*=100;keep.Add(rb);return rb;}
            void Hold2(){foreach(var rb in keep){var l=rb.Creature.ComponentLocomotion;l.WalkSpeed=l.JumpSpeed=l.FlySpeed=0;rb.Health.Health=1;}}
            var near=Robot(C0+new Vector3(2.5f,0,3));var far=Robot(C0+new Vector3(-3,0,-6));var third=Robot(C0+new Vector3(6,0,-1));
            var tw=Place(MDTowerLibrary.Get("battle_turret_1"),C0+new Vector3(-4.5f,0,5),0);tw.Cooldown=1e6f;tw.Damage.Hitpoints=.6f;
            foreach(var x in Seconds(1.5,()=>{Hold2();tw.Cooldown=1e6f;Cam(eye,focus);}))yield return x;
            var stF=typeof(MDTowerWorld).GetField("status",BindingFlags.NonPublic|BindingFlags.Instance);var timeF=typeof(MDTowerWorld).GetField("time",BindingFlags.NonPublic|BindingFlags.Instance);
            var stType=typeof(MDTowerWorld).GetNestedType("Status",BindingFlags.NonPublic);
            // a kinetic hit of no power makes the robot's status entry and reacts with nothing here; then heat and electric for a minute
            void Statuses(ComponentMDRobot rb){w.Elemental(tw,rb.Body,MDElement.Kinetic,0);var s=((IDictionary)stF.GetValue(w))[rb.Body];float now=(float)timeF.GetValue(w);
                stType.GetField("heatUntil").SetValue(s,now+60);stType.GetField("elecUntil").SetValue(s,now+60);}
            MDTowerTools.Shown.Add(tw);
            // a reaction's name over the third one (put in the list directly, held: a real reaction's burst would be in the way)
            Statuses(near);Statuses(far);
            {var lt=typeof(MDTowerWorld).GetNestedType("ReactionLabel",BindingFlags.NonPublic);var lb=Activator.CreateInstance(lt);var b=third.Body;
             lt.GetField("pos").SetValue(lb,b.Position+Vector3.UnitY*(b.BoxSize.Y+MDTowerWorld.LabelsAt));lt.GetField("text").SetValue(lb,MDElements.Reactions[(int)MDReaction.Overload].Name);
             lt.GetField("c").SetValue(lb,new Color(255,200,120));lt.GetField("c2").SetValue(lb,new Color(186,120,255));lt.GetField("life").SetValue(lb,1e6f);
             ((IList)typeof(MDTowerWorld).GetField("labels",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(w)).Add(lb);}
            foreach(var x in Seconds(.3,()=>{Hold2();tw.Cooldown=1e6f;Cam(eye,focus);Statuses(near);Statuses(far);}))yield return x;
            Cam(eye,focus);
            var lockAt=MDCombat.Center(near.Body);float lockSize=Math.Max(1,near.Body.BoxSize.Y*.7f);w.LockMark(lockAt,lockSize);   // a missile's lock-on mark (the mech's, a robot's, a boss's)
            var shots=new Dictionary<int,(Image on,Image off)>();Matrix vp=default;
            foreach(int m in new[]{0,1,2}){LabFog.Mode=m;var on=Grab(W,H,out vp);particles.ParticleSystemsDraw=false;var offImg=Grab(W,H,out _);particles.ParticleSystemsDraw=true;shots[m]=(on,offImg);}
            LabFog.Mode=0;
            SaveImage(shots[0].on,"f182_fog_none");SaveImage(shots[1].on,"f182_fog_weather");SaveImage(shots[2].on,"f182_fog_haze");
            bool fl=flip??false;
            // the overlays' screen areas: (name, test on a pixel)
            (float x0,float y0,float x1,float y1) Box(Vector3 a,Vector3 b2,float padPx){
                ToScreen(vp,a,W,H,fl,out var ax,out var ay);ToScreen(vp,b2,W,H,fl,out var bx,out var by);
                return (Math.Min(ax,bx)-padPx,Math.Min(ay,by)-padPx,Math.Max(ax,bx)+padPx,Math.Max(ay,by)+padPx);}
            var right=Vector3.Normalize(Vector3.Cross(focus-eye,Vector3.UnitY));
            var areas=new List<(string name,(float x0,float y0,float x1,float y1) box)>();
            foreach(var (rb,label) in new[]{(near,"enemy icons 9 m"),(far,"enemy icons 18 m")}) {
                var b=rb.Body;float d=Vector3.Distance(b.Position,eye),size=.32f+.012f*d;var top=b.Position+Vector3.UnitY*(b.BoxSize.Y+MDTowerWorld.IconsAt+size);
                areas.Add((label,Box(top-right*size*3.2f-Vector3.UnitY*size*1.2f,top+right*size*3.2f+Vector3.UnitY*size*1.2f,1)));
            }
            {var b=third.Body;var at=b.Position+Vector3.UnitY*(b.BoxSize.Y+MDTowerWorld.LabelsAt);areas.Add(("reaction name",Box(at-right*1.4f-Vector3.UnitY*.6f,at+right*1.4f+Vector3.UnitY*.6f,2)));}
            areas.Add(("lock-on mark",Box(lockAt-right*lockSize*.9f-Vector3.UnitY*lockSize*.9f,lockAt+right*lockSize*.9f+Vector3.UnitY*lockSize*.9f,0)));
            {var b=tw.Body;var top=new Vector3(b.Position.X,b.BoundingBox.Max.Y,b.Position.Z);areas.Add(("tower icon and health bar",Box(top-right*1.3f+Vector3.UnitY*.35f,top+right*1.3f+Vector3.UnitY*2.4f,1)));}
            var rows=new List<string>();fg["areas"]=rows;
            var noneOn=shots[0].on;var noneOff=shots[0].off;
            foreach(int m in new[]{1,2}) {
                var (on,offImg)=shots[m];string fogName=m==1?"weather fog":"haze";
                // how much the fog changes the frame where there is no overlay (the terrain and robots): it must be strong
                var bg=new List<int>();for(int i=0;i<noneOff.Pixels.Length;i+=97)bg.Add(Sum(noneOff.Pixels[i],offImg.Pixels[i]));bg.Sort();int bgMed=bg[bg.Count/2];
                foreach(var (name,(x0,y0,x1,y1)) in areas) {
                    var diffs=new List<int>();int px=0;
                    for(int y=Math.Max(0,(int)y0);y<Math.Min(H,(int)y1);y++)for(int x=Math.Max(0,(int)x0);x<Math.Min(W,(int)x1);x++) {
                        int i=y*W+x;if(Sum(noneOn.Pixels[i],noneOff.Pixels[i])<90)continue;   // not the overlay's own pixel
                        // the overlay's own colour changed by the fog: for an opaque one the pixel itself, for an additive one (the lock-on mark)
                        // what it adds to the frame (its pixel also brightens the fogged ground under it); the smaller of the two
                        var o=on.Pixels[i];var n0=noneOn.Pixels[i];var fo=offImg.Pixels[i];var nf=noneOff.Pixels[i];
                        int added=Math.Abs((o.R-fo.R)-(n0.R-nf.R))+Math.Abs((o.G-fo.G)-(n0.G-nf.G))+Math.Abs((o.B-fo.B)-(n0.B-nf.B));
                        px++;diffs.Add(Math.Min(Sum(o,n0),added)/3);
                    }
                    diffs.Sort();int med=diffs.Count>0?diffs[diffs.Count/2]:-1;
                    rows.Add($"{fogName}: {name}: {px} px, median change {med} (the scene behind: {bgMed/3})");
                    if(px<12||med>15)bad.Add($"fog {fogName}: {name}: {px} px, median change {med} (scene {bgMed/3})");
                }
                if(bgMed/3<(m==1?25:10))bad.Add($"fog {fogName}: the fog itself is too weak to tell ({bgMed/3})");
            }
            // the ring under fog: the same pixel measure as the ranges (flat), with and without fog
            {var ring=new List<string>();foreach(int m in new[]{0,1,2}){int shown=0,n=0;for(int i=0;i<160;i++){float t=i*MathF.PI*2/160;var p=tw.Body.Position+new Vector3(MathF.Cos(t)*tw.Range,.07f,MathF.Sin(t)*tw.Range);p.Y=70.07f;if(!ToScreen(vp,p,W,H,fl,out var sx,out var sy))continue;float s=Strength(shots[m].on,shots[m].off,sx,sy,new Color(tw.Tower.fx.color[0],tw.Tower.fx.color[1],tw.Tower.fx.color[2]));if(s<0)continue;n++;if(s>=.6f)shown++;}ring.Add($"fog {m}: ring shown at {shown} of {n}");}fg["ring"]=ring;}
            ((IList)typeof(MDTowerWorld).GetField("labels",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(w)).Clear();
            MDTowerTools.Shown.Clear();Remove([tw]);foreach(var rb in keep)if(rb.Entity.IsAddedToProject)proj.RemoveEntity(rb.Entity,true);ClearAll();
        }
        pl.ComponentBody.Position=Home;
        if(bad.Count>0)throw new Exception("fb182: "+string.Join(" || ",bad));
    }
}
