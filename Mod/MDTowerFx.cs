using Engine;
using Engine.Graphics;
using GameEntitySystem;
using System.Runtime.CompilerServices;
using HarmonyLib;

namespace Game;

// One shared simulation per world for tower effects, shells, lingering zones and status effects.
// It rides on SubsystemParticles: simulated with game time (pauses with the game) and drawn into the
// particle renderer's batches, which apply fog. Sprites come from the 8x8 pixel atlas Towers/fx.
public sealed class MDTowerWorld : ParticleSystemBase {
    static readonly ConditionalWeakTable<Project,MDTowerWorld> worlds=new();
    public static MDTowerWorld For(Project project) {
        if(worlds.TryGetValue(project,out var w)&&w.SubsystemParticles!=null)return w;
        w=new MDTowerWorld(project);worlds.AddOrUpdate(project,w);
        project.FindSubsystem<SubsystemParticles>(true).AddParticleSystem(w);return w;
    }
    public enum S{Glow=0,Dot=1,Star=2,Streak=3,Smoke=4,Smoke2=5,Fire=6,Ring=7,ThinRing=8,Plus=9,Drop=10,Crystal=11,Bubble=12,Bolt=13,Beam=14,Scorch=15,Pixel=16,Spark=17,Arc=18,Puddle=19}
    struct P { public Vector3 pos,vel; public float age,life,s0,s1,rot,spin,gravity,drag,stretch; public Color c0,c1; public S slot; public bool add,flat; }
    struct Beam { public Vector3 a,b; public float age,life,width; public Color c; public bool lightning; public int seed; }
    sealed class Shot { public ComponentMDUnit owner; public MDTowerDef def; public Vector3 pos,vel; public float age,gravity,power,splash; public bool enhanced; public int muzzle; public Vector3 last;
        public bool missile; public ComponentBody target; public float homeAfter,accel,maxSpeed,life=8; }   // a homing missile (the mech's)
    sealed class Zone { public ComponentMDUnit owner; public Vector3 c; public float r,age,life,tick,power,slow; public string kind; }
    // Fire of creatures that are not towers (the robot enemies, MDRobots.cs): a tower definition gives the look (colours, trail,
    // impact); `pass` names the bodies it flies through (its own side); `landed` deals the damage where it stops. A missile homes
    // in on its target with a limited turn rate, so it can be outrun.
    public sealed class Foreign {
        public Entity owner; public MDTowerDef look; public Vector3 pos,vel,last; public float age,gravity,life=6;
        public bool missile; public ComponentBody target; public float homeAfter,accel,maxSpeed,turn=2.5f;
        public Func<ComponentBody,bool> pass; public Action<Vector3,Vector3,ComponentBody> landed;
    }
    readonly List<Foreign> foreign=[];
    public int ForeignShots=>foreign.Count;
    public void LaunchForeign(Foreign f){f.last=f.pos;foreign.Add(f);}
    sealed class Status {
        public float slowUntil,slowFactor=1,stunUntil,armorUntil,armorFactor=1,freezeUntil,freezeImmuneUntil,poisonUntil,poisonDps,poisonTick,healBlockUntil,healedUntil;
        public ComponentMDUnit poisonOwner; public Color slowColor=new(200,235,255),armorColor=new(220,255,110);
        public float walk,fly,swim,jump,turn,look; public bool saved;
    }
    readonly Project project; readonly Texture2D texture; readonly System.Random random=new();
    P[] ps=new P[3000]; int pn; readonly List<Beam> beams=[]; readonly List<Shot> shots=[]; readonly List<Zone> zones=[];
    readonly Dictionary<ComponentBody,Status> status=[];
    TexturedBatch3D add,alpha; float time;
    public int Particles=>pn; public int Shots=>shots.Count; public int Zones=>zones.Count; public int Impacts;
    MDTowerWorld(Project p){project=p;texture=ContentManager.Get<Texture2D>("Towers/fx");}

    // ---------------------------------------------------------------- particles
    float R(float a,float b)=>a+(float)random.NextDouble()*(b-a);
    Vector3 RDir(){Vector3 v;do v=new(R(-1,1),R(-1,1),R(-1,1));while(v.LengthSquared()>1||v.LengthSquared()<.01f);return Vector3.Normalize(v);}
    static Color C(float[] c,float a=1)=>new(c[0],c[1],c[2],a);
    // The alpha batch blends premultiplied (One, InvSrcAlpha): a straight colour with alpha < 1 adds its full RGB, and
    // overlapping clouds wash out to white. The coloured clouds and ground stains below are premultiplied.
    static Color PM(Color c)=>new((byte)(c.R*c.A/255),(byte)(c.G*c.A/255),(byte)(c.B*c.A/255),c.A);
    // 0.13.0 particle amount (MDSettings.FxLevel 0 完整 / 1 精简 / 2 极简): smoke (big alpha-blended quads, the costly overdraw on
    // phones) and the many small bits are thinned at random; flashes, glows, rings, beams and ground marks are kept; fewer may be
    // alive at once (the oldest quarter goes first).
    static readonly float[] SmokeKeep=[1,.35f,.15f],BitsKeep=[1,.5f,.25f];static readonly int[] MaxAlive=[3000,1500,800];
    public static int Thinned;   // tests
    void Emit(S slot,Vector3 pos,Vector3 vel,float life,float s0,float s1,Color c0,Color c1,bool additive,float gravity=0,float drag=0,float stretch=0,float spin=0,bool flat=false) {
        int lv=Math.Clamp(MDSettings.FxLevel,0,2);
        if(lv>0) {
            float keep=slot switch{S.Smoke or S.Smoke2=>SmokeKeep[lv],S.Spark or S.Pixel or S.Drop or S.Bubble or S.Crystal or S.Fire or S.Dot=>BitsKeep[lv],_=>1f};
            if(keep<1&&random.NextDouble()>keep){Thinned++;return;}
        }
        int cap=Math.Min(ps.Length,MaxAlive[lv]);
        if(pn>=cap){Array.Copy(ps,cap/4,ps,0,pn-cap/4);pn-=cap/4;}
        ps[pn++]=new P{pos=pos,vel=vel,life=life,s0=s0,s1=s1,c0=c0,c1=c1,slot=slot,add=additive,gravity=gravity,drag=drag,stretch=stretch,rot=R(0,6.28f),spin=spin,flat=flat};
    }
    public void Flash(Vector3 p,float size,Color c,float life=.08f) {
        Emit(S.Star,p,Vector3.Zero,life,size*.8f,size*1.1f,Deep(c),new Color(c.R,c.G,c.B,(byte)0),false);
        Emit(S.Star,p,Vector3.Zero,life,size*.5f,size*.7f,Color.White,Color.White*0f,true);
        Emit(S.Glow,p,Vector3.Zero,life*1.6f,size*1.6f,size*2.2f,c*.7f,c*0f,true);
    }
    static Color Deep(Color c)=>new((byte)Math.Min(255,c.R*.95f),(byte)(c.G*.72f),(byte)(c.B*.45f),(byte)230);   // saturated core that survives a bright sky
    public void Sparks(Vector3 p,Vector3 dir,int n,Color c,float speed=6,float spread=.6f,float life=.35f,float size=.05f) {
        for(int i=0;i<n;i++){var d=Vector3.Normalize(dir+RDir()*spread);Emit(S.Spark,p,d*R(speed*.4f,speed),R(life*.5f,life),size,size*.5f,c,new Color(c.R,c.G,c.B,(byte)0),false,9,1.5f,.035f);}
    }
    public void SmokePuffs(Vector3 p,int n,float size,Color c,float rise=.6f,float life=1.4f,float spread=.15f) {
        for(int i=0;i<n;i++)Emit(i%2==0?S.Smoke:S.Smoke2,p+RDir()*spread,new Vector3(R(-.3f,.3f),R(.2f,1)*rise,R(-.3f,.3f)),R(life*.6f,life),size*R(.6f,1),size*R(1.6f,2.4f),c,new Color(c.R,c.G,c.B,(byte)0),false,0,.8f,0,R(-.6f,.6f));
    }
    public void Muzzle(MDTowerDef d,Vector3 p,Vector3 dir,bool strong) {
        float s=d.fx.scale*(strong?1.5f:1);var c=C(d.fx.color);var c2=C(d.fx.color2);
        Flash(p+dir*.12f*s,.55f*s,c,.08f);
        Emit(S.Streak,p+dir*.45f*s,dir*6f,.07f,.22f*s,.12f*s,c,c*0f,true,0,0,.2f*s);
        Emit(S.Glow,p+dir*.3f*s,Vector3.Zero,.12f,.6f*s,.9f*s,c2*.8f,c2*0f,true);
        Sparks(p,dir,strong?12:7,c2,8,.35f,.25f,.045f*s);
        SmokePuffs(p+dir*.25f,strong?2:1,.11f*s,new Color(95,92,88,60),.4f,.7f,.05f);
    }
    public void Impact(MDTowerDef d,Vector3 p,Vector3 dir,bool strong) {
        float s=d.fx.scale*(strong?1.6f:1);var c=C(d.fx.color);var c2=C(d.fx.color2);
        Flash(p,.6f*s,c,.1f);Emit(S.Glow,p,Vector3.Zero,.25f,.5f*s,1.1f*s,c2*.7f,c2*0f,true);
        Sparks(p,-dir,strong?18:12,c2,6,.9f,.5f,.055f*s);
        Emit(S.Ring,p,Vector3.Zero,.25f,.15f*s,1.1f*s,c,c*0f,true);
        SmokePuffs(p,strong?4:2,.22f*s,new Color(110,104,98,95),.5f,1.0f,.1f);Impacts++;
    }
    public void Explosion(Vector3 p,float radius,string kind,Color c,Color c2) {
        float s=Math.Max(.6f,radius/2);
        Flash(p+Vector3.UnitY*.3f,1.0f*s,c,.12f);
        Emit(S.Ring,p+Vector3.UnitY*.08f,Vector3.Zero,.35f,.3f,radius*1.15f,c,c*0f,true,0,0,0,0,true);
        int n=(int)(14*s);
        if(kind=="fire") {
            for(int i=0;i<n;i++){var d=RDir();d.Y=Math.Abs(d.Y)*.8f+.2f;Emit(S.Fire,p+d*.2f,d*R(1.5f,3.5f)*s,R(.35f,.7f),R(.25f,.45f)*s,R(.7f,1.1f)*s,new Color(255,220,140),new Color(180,40,10,0),true,-1,2.4f,0,R(-2,2));}
            SmokePuffs(p+Vector3.UnitY*.4f,n/2,.45f*s,new Color(60,55,52,170),1.2f,2.2f,.5f*s);
            Sparks(p,Vector3.UnitY,n,new Color(255,190,90),8*s,1.1f,.8f,.05f);
            Emit(S.Scorch,p+Vector3.UnitY*.03f,Vector3.Zero,5f,radius*.9f,radius*.95f,new Color(20,18,16,170),new Color(20,18,16,0),false,0,0,0,0,true);
        } else if(kind=="acid") {
            for(int i=0;i<n;i++){var d=RDir();d.Y=Math.Abs(d.Y)+.3f;Emit(S.Drop,p,d*R(1.5f,4)*s,R(.4f,.8f),R(.08f,.14f)*s,.05f*s,c,c2*0f,false,9,.5f,.03f);}
            for(int i=0;i<n/2;i++)Emit(S.Bubble,p+new Vector3(R(-radius,radius)*.7f,.05f,R(-radius,radius)*.7f),new Vector3(0,R(.2f,.6f),0),R(.6f,1.4f),.08f,.16f,c2,c2*0f,true);
            Emit(S.Puddle,p+Vector3.UnitY*.03f,Vector3.Zero,3.5f,radius*.75f,radius*.85f,new Color((byte)(c.R*.7f),(byte)(c.G*.8f),(byte)(c.B*.6f),(byte)95),new Color(c.R,c.G,c.B,(byte)0),false,0,0,0,0,true);
            SmokePuffs(p,n/3,.4f*s,new Color(c2.R,c2.G,c2.B,(byte)90),.6f,1.6f,.4f*s);
        } else if(kind=="poison") {   // deep purple cloud fading darker (Endfield fog 159,88,176 -> 65,41,102), a few bright bubbles
            for(int i=0;i<n;i++){var d=RDir();d.Y=Math.Abs(d.Y)*.5f;Emit(i%2==0?S.Smoke:S.Smoke2,p+d*.3f,d*R(1,2.4f)*s,R(.8f,1.6f),.4f*s,1.1f*s,PM(new Color(c2.R,c2.G,c2.B,(byte)170)),Color.Transparent,false,0,2.2f,0,R(-1,1));}
            for(int i=0;i<n/3;i++)Emit(S.Bubble,p+new Vector3(R(-radius,radius)*.6f,.1f,R(-radius,radius)*.6f),new Vector3(0,R(.3f,.8f),0),R(.5f,1f),.05f,.1f,c,new Color(c.R,c.G,c.B,(byte)0),true);
        } else if(kind=="frost") {
            for(int i=0;i<n;i++){var d=RDir();Emit(S.Crystal,p+d*.2f,d*R(2,5)*s,R(.3f,.7f),R(.08f,.16f)*s,.03f,new Color(190,230,255),c*0f,true,6,1.2f,0,R(-6,6));}
            for(int i=0;i<n/2;i++){var d=RDir();d.Y=Math.Abs(d.Y)*.4f;Emit(S.Smoke,p+d*.3f,d*R(.8f,2)*s,R(.8f,1.5f),.4f*s,1f*s,PM(new Color(146,199,243,150)),Color.Transparent,false,0,2,0,R(-1,1));}   // Endfield ice mist
            Emit(S.Puddle,p+Vector3.UnitY*.04f,Vector3.Zero,3f,radius*.7f,radius*.8f,PM(new Color(159,226,255,110)),Color.Transparent,false,0,0,0,0,true);
            Ring(p,radius,new Color(170,220,255),.5f);
            for(int i=0;i<10;i++){var q=p+new Vector3(R(-radius,radius)*.6f,.05f,R(-radius,radius)*.6f);Emit(S.Crystal,q,new Vector3(0,R(.2f,.6f),0),R(.6f,1.2f),R(.12f,.2f),.05f,new Color(225,245,255),new Color(225,245,255,0),true,0,1,0,R(-2,2));}
        } else { // heavy / physical
            for(int i=0;i<n;i++){var d=RDir();d.Y=Math.Abs(d.Y)*.8f+.2f;Emit(S.Fire,p+d*.25f,d*R(2,4.5f)*s,R(.3f,.6f),R(.3f,.5f)*s,R(.8f,1.3f)*s,new Color(255,235,170),new Color(150,60,20,0),true,-1,2.6f,0,R(-2,2));}
            for(int i=0;i<n;i++){var d=RDir();d.Y=Math.Abs(d.Y)+.4f;Emit(S.Pixel,p,d*R(3,7)*s,R(.6f,1.2f),R(.05f,.1f)*s,.05f,new Color(70,66,60),new Color(70,66,60,0),false,14,.3f,0,R(-8,8));}
            SmokePuffs(p+Vector3.UnitY*.5f,n,.55f*s,new Color(80,76,70,180),1.4f,2.6f,.6f*s);
            Sparks(p,Vector3.UnitY,n,new Color(255,200,110),10*s,1.0f,.9f,.06f);
            Emit(S.Scorch,p+Vector3.UnitY*.03f,Vector3.Zero,6f,radius*.8f,radius*.85f,new Color(18,16,14,190),new Color(18,16,14,0),false,0,0,0,0,true);
        }
        Impacts++;
    }
    public void AddBeam(Vector3 a,Vector3 b,float width,Color c,float life,bool lightning)=>beams.Add(new Beam{a=a,b=b,width=width,c=c,life=life,lightning=lightning,seed=random.Next()});
    // A held beam (the mech's laser), set every update while it lasts: drawn once where it is now, with a soft ribbon through its
    // positions of the last 0.12 s that fades out. Short-lived beams added every frame showed as separate stepped copies.
    sealed class Held { public Vector3 a,b; public float width,seen; public Color c; public readonly List<(Vector3 a,Vector3 b,float age)> trail=[]; }
    readonly Dictionary<object,Held> held=[];
    public void HoldBeam(object key,Vector3 a,Vector3 b,float width,Color c){if(!held.TryGetValue(key,out var h))held[key]=h=new Held();h.a=a;h.b=b;h.width=width;h.c=c;h.seen=0;}
    public void Ring(Vector3 p,float radius,Color c,float life,float y=.06f){Emit(S.ThinRing,p+Vector3.UnitY*y,Vector3.Zero,life,.2f,radius,c,c*0f,true,0,0,0,0,true);Emit(S.Ring,p+Vector3.UnitY*y,Vector3.Zero,life*.8f,.2f,radius*.92f,c*.6f,c*0f,true,0,0,0,0,true);}
    public void Spray(string kind,Vector3 p,Vector3 dir,Color c,Color c2,float length) {
        if(kind=="flame") {
            for(int i=0;i<3;i++){var d=Vector3.Normalize(dir+RDir()*.12f);float sp=R(7,10);Emit(S.Fire,p+d*.1f,d*sp,length/sp*R(.8f,1.05f),R(.1f,.18f),R(.7f,1.1f),new Color(255,240,190),new Color(200,50,10,0),true,-1.5f,.6f,0,R(-3,3));}
            if(random.Next(3)==0)SmokePuffs(p+dir*length*.8f,1,.4f,new Color(50,45,40,120),1,1.2f,.3f);
        } else if(kind=="jet") {
            for(int i=0;i<4;i++){var d=Vector3.Normalize(dir+RDir()*.05f);float sp=R(14,18);Emit(S.Drop,p+d*.1f,d*sp,length/sp*R(.85f,1.05f),R(.07f,.12f),R(.14f,.22f),c,c2*.4f,false,3,.1f,.02f);}
            Emit(S.Streak,p+dir*.4f,dir*16,length/16,.16f,.24f,new Color(220,245,255,150),new Color(220,245,255,0),true,2,0,.08f);
        } else if(kind=="frost") {   // cold gas (冰冻塔): pale mist that widens along the jet, and a few ice glints
            for(int i=0;i<3;i++){var d=Vector3.Normalize(dir+RDir()*.1f);float sp=R(9,12);Emit(i==2?S.Smoke2:S.Smoke,p+d*.15f,d*sp,length/sp*R(.85f,1.1f),R(.12f,.2f),R(.8f,1.3f),PM(new Color(190,228,255,105)),Color.Transparent,false,.3f,.4f,0,R(-2,2));}   // mist colours premultiplied, like the ice mist
            if(random.Next(2)==0){var d=Vector3.Normalize(dir+RDir()*.15f);float sp=R(8,11);Emit(S.Crystal,p+d*.3f,d*sp,length/sp*R(.5f,.9f),.07f,.03f,new Color(225,245,255),new Color(225,245,255,0),true,0,.3f,0,R(-4,4));}
        }
    }
    // A mech thruster: a blue-white core and a short orange flame along dir (strength 1 = hovering, about 2 = a jump's burst).
    public void Thruster(Vector3 p,Vector3 dir,float s) {
        var d=Vector3.Normalize(dir+RDir()*.06f);
        Emit(S.Glow,p,d*1.2f,.07f,.32f*s,.12f*s,new Color(175,215,255,230),new Color(110,160,255,0),true);
        Emit(S.Fire,p+d*.08f,d*R(7,10)*s,R(.1f,.18f),.16f*s,.42f*s,new Color(255,240,200),new Color(255,110,40,0),true,0,1.5f,0,R(-3,3));
    }
    public void Heal(Vector3 p){for(int i=0;i<8;i++)Emit(S.Plus,p+new Vector3(R(-.5f,.5f),R(0,1.4f),R(-.5f,.5f)),new Vector3(0,R(.6f,1.2f),0),R(.7f,1.2f),.12f,.08f,new Color(120,255,150),new Color(120,255,150,0),true);}
    public void Wreck(Vector3 p,float size){if(random.Next(4)==0)SmokePuffs(p,1,.5f*size,new Color(45,42,40,160),1.6f,2.4f,.3f);if(random.Next(14)==0)Sparks(p,Vector3.UnitY,4,new Color(255,200,120),3,1.2f,.5f,.04f);}

    // ---------------------------------------------------------------- shells, zones, status
    public Vector3 LastLaunch;   // velocity of the last shell (lab checks)
    // def: the weapon (a tower's own definition by default; the mech passes one per weapon)
    public void LaunchShell(ComponentMDUnit owner,Vector3 from,Vector3 velocity,float gravity,float power,float splash,bool enhanced,MDTowerDef def=null){LastLaunch=velocity;shots.Add(new Shot{owner=owner,def=def??owner.Tower,pos=from,last=from,vel=velocity,gravity=gravity,power=power,splash=splash,enhanced=enhanced});}
    // The mech's missile, after the original: it leaves slowly, drifts for homeAfter seconds, then accelerates to maxSpeed and
    // turns towards where the target will be: 60 deg/s at first, faster the longer it homes, 600 deg/s within 13 m, straight at
    // it within 5 m. A missile with a live target always hits (user): it passes through terrain and bodies in its way and bursts
    // when it reaches the target. Without a target, or once the target is dead, it flies on and hits like a shell.
    // The 导弹塔 (0.10.0) launches the same missile, with a little splash.
    public void LaunchMissile(ComponentMDUnit owner,MDTowerDef def,Vector3 from,Vector3 velocity,ComponentBody target,float homeAfter,float accel,float maxSpeed,float life,float power,float splash=0) {
        shots.Add(new Shot{owner=owner,def=def,pos=from,last=from,vel=velocity,power=power,splash=splash,missile=true,target=target,homeAfter=homeAfter,accel=accel,maxSpeed=maxSpeed,life=life});
    }
    // a lock-on marker: a red ring around the locked creature, renewed every frame while it is locked
    public void LockMark(Vector3 p,float size){Emit(S.ThinRing,p,Vector3.Zero,.05f,size,size,new Color(255,60,50,230),new Color(255,60,50,200),true);Emit(S.Plus,p,Vector3.Zero,.05f,size*.25f,size*.25f,new Color(255,90,70,230),new Color(255,90,70,200),true);}
    public void AddZone(ComponentMDUnit owner,string kind,Vector3 c,float r,float life,float power,float slow=1) {
        zones.Add(new Zone{owner=owner,kind=kind,c=c,r=r,life=life,power=power,slow=slow});
        var fx=owner.Tower.fx;var c1=C(fx.color);var c2=C(fx.color2);
        if(kind=="poison")Emit(S.Puddle,c+Vector3.UnitY*.04f,Vector3.Zero,life,r*.85f,r*.95f,PM(new Color((byte)(c2.R*.75f),(byte)(c2.G*.75f),(byte)(c2.B*.8f),(byte)140)),Color.Transparent,false,0,0,0,0,true);
        if(kind=="ice"){Emit(S.Puddle,c+Vector3.UnitY*.05f,Vector3.Zero,life,r*.9f,r,PM(new Color(c1.R,c1.G,c1.B,(byte)130)),Color.Transparent,false,0,0,0,0,true);Ring(c,r,c2,.6f);}
    }
    Status Get(ComponentBody b){if(!status.TryGetValue(b,out var s))status[b]=s=new Status();return s;}
    public void Slow(ComponentBody b,float factor,float seconds,Color? c=null){var s=Get(b);s.slowFactor=Math.Min(factor,s.slowUntil>time?s.slowFactor:1);s.slowUntil=Math.Max(s.slowUntil,time+seconds);if(c.HasValue)s.slowColor=c.Value;}
    public void Stun(ComponentBody b,float seconds){var s=Get(b);s.stunUntil=Math.Max(s.stunUntil,time+seconds);}
    public void ArmorDown(ComponentBody b,float factor,float seconds,Color? c=null){var s=Get(b);s.armorFactor=Math.Max(factor,s.armorUntil>time?s.armorFactor:1);s.armorUntil=Math.Max(s.armorUntil,time+seconds);if(c.HasValue)s.armorColor=c.Value;}
    // Frozen solid: no walking, turning or looking around. Afterwards the target is immune for a while, so several
    // frost towers cannot keep one enemy frozen forever.
    public bool Freeze(ComponentBody b,float seconds,float immune) {
        var s=Get(b);if(time<s.freezeImmuneUntil)return false;
        s.freezeUntil=time+seconds;s.freezeImmuneUntil=s.freezeUntil+immune;
        for(int i=0;i<10;i++)Emit(S.Crystal,b.Position+new Vector3(R(-.5f,.5f)*b.BoxSize.X,R(0,b.BoxSize.Y),R(-.5f,.5f)*b.BoxSize.Z),RDir()*R(.5f,1.5f),R(.4f,.8f),R(.1f,.18f),.04f,new Color(225,245,255),new Color(225,245,255,0),true,2,1,0,R(-4,4));
        return true;
    }
    // Poison keeps ticking after the target leaves the cloud; clouds do not stack (the strongest one counts).
    public void Poison(ComponentMDUnit owner,ComponentBody b,float dps,float seconds) {
        var s=Get(b);if(s.poisonUntil<=time||dps>=s.poisonDps){s.poisonDps=dps;s.poisonOwner=owner;}
        s.poisonUntil=Math.Max(s.poisonUntil,time+seconds);
    }
    // 0.14.0 (user, 2026-10-30: "我有很多的炮塔和医疗塔配合，敌方boss还是不堪一击"): a tower a boss hit cannot be healed for a while,
    // and a medic's heal counts once per interval whatever the number of medics around it
    public void BlockHeal(ComponentBody b,float seconds){var s=Get(b);s.healBlockUntil=Math.Max(s.healBlockUntil,time+seconds);}
    public bool CanHeal(ComponentBody b)=>!status.TryGetValue(b,out var s)||s.healBlockUntil<=time&&s.healedUntil<=time;
    public bool HealBlocked(ComponentBody b)=>status.TryGetValue(b,out var s)&&s.healBlockUntil>time;
    public void Healed(ComponentBody b,float seconds){var s=Get(b);s.healedUntil=time+seconds;}
    public float DamageFactor(ComponentBody b)=>status.TryGetValue(b,out var s)&&s.armorUntil>time?s.armorFactor:1;
    public bool IsStunned(ComponentBody b)=>status.TryGetValue(b,out var s)&&s.stunUntil>time;
    public bool IsFrozen(ComponentBody b)=>status.TryGetValue(b,out var s)&&s.freezeUntil>time;
    public bool IsPoisoned(ComponentBody b)=>status.TryGetValue(b,out var s)&&s.poisonUntil>time;
    // Colour multiplier for a frozen (ice blue) or poisoned (the cloud's purple) creature; see MDStatusTint.
    public static Vector3? Tint(ComponentModel m) {
        if(!worlds.TryGetValue(m.Project,out var w)||w.status.Count==0||m.Entity.FindComponent<ComponentBody>() is not {} b||!w.status.TryGetValue(b,out var s))return null;
        if(s.freezeUntil>w.time)return new Vector3(.55f,.78f,1f);
        if(s.poisonUntil>w.time&&s.poisonOwner?.Tower?.fx is {} fx)return Vector3.Lerp(Vector3.One,new Vector3(fx.color2[0],fx.color2[1],fx.color2[2]),.5f);
        return null;
    }
    public float SpeedFactor(ComponentBody b)=>!status.TryGetValue(b,out var s)?1:s.freezeUntil>time||s.stunUntil>time?0:s.slowUntil>time?s.slowFactor:1;

    void ApplyStatus(float dt) {
        List<ComponentBody> done=null;
        foreach(var (b,s) in status) {
            var loc=b.Entity.FindComponent<ComponentLocomotion>();
            bool alive=b.Entity.IsAddedToProject,frozen=alive&&s.freezeUntil>time,stunned=alive&&s.stunUntil>time,poisoned=alive&&s.poisonUntil>time;
            float f=!alive?1:frozen||stunned?0:s.slowUntil>time?s.slowFactor:1;
            if(loc!=null&&alive) {
                if(f<1&&!s.saved){s.walk=loc.WalkSpeed;s.fly=loc.FlySpeed;s.swim=loc.SwimSpeed;s.jump=loc.JumpSpeed;s.turn=loc.TurnSpeed;s.look=loc.LookSpeed;s.saved=true;}
                if(s.saved){loc.WalkSpeed=s.walk*f;loc.FlySpeed=s.fly*f;loc.SwimSpeed=s.swim*f;loc.JumpSpeed=s.jump*(f<1?.3f*f:1);loc.TurnSpeed=frozen?0:s.turn;loc.LookSpeed=frozen?0:s.look;}
            }
            if(alive) {
                var box=b.BoxSize;Vector3 OnBody()=>b.Position+new Vector3(R(-.5f,.5f)*box.X,R(.1f,1)*box.Y,R(-.5f,.5f)*box.Z);
                if(stunned&&!frozen&&random.Next(5)==0)Emit(S.Star,b.Position+Vector3.UnitY*(box.Y+.2f)+new Vector3(R(-.3f,.3f),0,R(-.3f,.3f)),Vector3.UnitY*.3f,.4f,.1f,.05f,new Color(255,240,120),new Color(255,240,120,0),true);
                if(frozen){if(random.Next(2)==0)Emit(S.Crystal,OnBody(),Vector3.Zero,.6f,.16f,.1f,new Color(215,242,255),new Color(215,242,255,0),true,0,0,0,R(-1,1));}
                else if(s.slowUntil>time&&s.slowFactor<1&&random.Next(8)==0)Emit(S.Crystal,OnBody(),Vector3.Zero,.5f,.08f,.02f,s.slowColor,new Color(s.slowColor.R,s.slowColor.G,s.slowColor.B,(byte)0),true);
                if(s.armorUntil>time&&random.Next(10)==0)Emit(S.Drop,OnBody(),Vector3.Zero,.6f,.07f,.04f,s.armorColor,new Color(s.armorColor.R,s.armorColor.G,s.armorColor.B,(byte)0),false,4);
                if(poisoned) {
                    var pc=s.poisonOwner?.Tower?.fx is {} pfx?C(pfx.color):new Color(228,140,255);
                    if(random.Next(5)==0)Emit(S.Bubble,OnBody(),Vector3.UnitY*R(.3f,.6f),R(.5f,.9f),.07f,.12f,pc,new Color(pc.R,pc.G,pc.B,(byte)0),true);
                    s.poisonTick-=dt;
                    if(s.poisonTick<=0){s.poisonTick=.5f;if(s.poisonOwner?.Entity.IsAddedToProject==true)MDTowerBrain.Hit(s.poisonOwner,b,s.poisonDps*.5f,MDCombat.Center(b),Vector3.UnitY);}
                }
            }
            if(f>=1&&s.saved&&loc!=null){loc.WalkSpeed=s.walk;loc.FlySpeed=s.fly;loc.SwimSpeed=s.swim;loc.JumpSpeed=s.jump;loc.TurnSpeed=s.turn;loc.LookSpeed=s.look;s.saved=false;}
            if(!alive||(s.slowUntil<=time&&s.stunUntil<=time&&s.armorUntil<=time&&s.freezeUntil<=time&&s.poisonUntil<=time&&s.freezeImmuneUntil<=time&&s.healBlockUntil<=time&&s.healedUntil<=time&&!s.saved))(done??=[]).Add(b);
        }
        if(done!=null)foreach(var b in done)status.Remove(b);
    }

    public override bool Simulate(float dt) {
        dt=Math.Min(dt,.1f);time+=dt;
        for(int i=0;i<pn;i++) {
            ref var p=ref ps[i];p.age+=dt;
            if(p.age>=p.life){ps[i]=ps[--pn];i--;continue;}
            p.vel.Y-=p.gravity*dt;p.vel*=MathF.Max(0,1-p.drag*dt);p.pos+=p.vel*dt;p.rot+=p.spin*dt;
        }
        for(int i=beams.Count-1;i>=0;i--){var b=beams[i];b.age+=dt;if(b.age>=b.life)beams.RemoveAt(i);else beams[i]=b;}
        List<object> gone=null;
        foreach(var (k,h) in held) {
            h.seen+=dt;
            for(int j=h.trail.Count-1;j>=0;j--){var e=h.trail[j];e.age+=dt;if(e.age>.12f)h.trail.RemoveAt(j);else h.trail[j]=e;}
            if(h.seen<.06f&&(h.trail.Count==0||h.trail[^1].age>=1/90f))h.trail.Add((h.a,h.b,0));
            if(h.seen>=.06f&&h.trail.Count==0)(gone??=[]).Add(k);
        }
        if(gone!=null)foreach(var k in gone)held.Remove(k);
        for(int i=shots.Count-1;i>=0;i--) {
            var s=shots[i];s.age+=dt;s.last=s.pos;s.vel.Y-=s.gravity*dt;
            if(s.missile)Steer(s,dt);
            var next=s.pos+s.vel*dt;
            if(!s.owner.Entity.IsAddedToProject||s.age>s.life){shots.RemoveAt(i);continue;}
            if(s.missile&&s.target!=null&&s.target.Entity.IsAddedToProject&&MDCombat.Living(s.target)) {
                var c=MDCombat.Center(s.target);float r=Math.Max(1.2f,s.target.BoxSize.X*.6f+.5f);var seg=next-s.pos;
                float k=seg.LengthSquared()>1e-8f?Math.Clamp(Vector3.Dot(c-s.pos,seg)/seg.LengthSquared(),0,1):0;
                if(Vector3.Distance(s.pos+seg*k,c)<=r){shots.RemoveAt(i);MDTowerBrain.ShellLanded(s.owner,s.def,c,Vector3.Normalize(s.vel),s.target,s.power,s.splash,s.enhanced);continue;}
                s.pos=next;ShellTrail(s,dt);continue;
            }
            var hit=MDCombat.Trace(s.owner,s.pos,next,out var point);
            bool collided=hit!=null||Vector3.DistanceSquared(point,next)>1e-6f;
            if(collided){shots.RemoveAt(i);MDTowerBrain.ShellLanded(s.owner,s.def,point,Vector3.Normalize(s.vel),hit,s.power,s.splash,s.enhanced);continue;}
            s.pos=next;ShellTrail(s,dt);
        }
        for(int i=foreign.Count-1;i>=0;i--) {
            var f=foreign[i];f.age+=dt;f.last=f.pos;f.vel.Y-=f.gravity*dt;
            if(f.age>f.life||!f.owner.IsAddedToProject){foreign.RemoveAt(i);continue;}
            if(f.missile)SteerForeign(f,dt);
            var next=f.pos+f.vel*dt;var hit=TraceForeign(f,f.pos,next,out var point);
            if(hit!=null||Vector3.DistanceSquared(point,next)>1e-6f){foreign.RemoveAt(i);f.landed?.Invoke(point,f.vel.LengthSquared()>1e-6f?Vector3.Normalize(f.vel):Vector3.UnitY,hit);continue;}
            f.pos=next;ForeignTrail(f);
        }
        for(int i=zones.Count-1;i>=0;i--) {
            var z=zones[i];z.age+=dt;z.tick-=dt;
            if(z.age>=z.life||!z.owner.Entity.IsAddedToProject){zones.RemoveAt(i);continue;}
            ZoneVisual(z);
            if(z.tick<=0){z.tick=.5f;MDTowerBrain.ZoneTick(z.owner,z.kind,z.c,z.r,z.power*.5f,z.slow);}
        }
        ApplyStatus(dt);
        return false;
    }
    ComponentBody TraceForeign(Foreign f,Vector3 a,Vector3 b,out Vector3 point) {
        var terrain=project.FindSubsystem<SubsystemTerrain>(true).Raycast(a,b,false,true,(v,d)=>BlocksManager.Blocks[Terrain.ExtractContents(v)].IsCollidable_(v));
        var body=project.FindSubsystem<SubsystemBodies>(true).Raycast(a,b,0,(x,d)=>f.pass==null||!f.pass(x));
        float max=Vector3.Distance(a,b);float tt=terrain?.Distance??max;float bd=body?.Distance??float.PositiveInfinity;
        point=a+(max>1e-6f?(b-a)/max:Vector3.Zero)*Math.Min(tt,bd);return bd<tt?body?.ComponentBody:null;
    }
    static void SteerForeign(Foreign f,float dt) {
        float speed=Math.Min(f.maxSpeed,f.vel.Length()+f.accel*dt);var dir=f.vel.LengthSquared()>1e-6f?Vector3.Normalize(f.vel):Vector3.UnitY;
        if(f.target!=null&&f.age>=f.homeAfter&&f.target.Entity.IsAddedToProject) {
            var want=Vector3.Normalize(MDCombat.Center(f.target)-f.pos);float angle=MathF.Acos(Math.Clamp(Vector3.Dot(dir,want),-1,1)),turn=f.turn*dt;
            dir=angle<=turn?want:Vector3.Normalize(Vector3.Lerp(dir,want,turn/angle));
        }
        f.vel=dir*speed;
    }
    void ForeignTrail(Foreign f) {
        var c=C(f.look.fx.color);var c2=C(f.look.fx.color2);var dir=f.vel.LengthSquared()>.01f?Vector3.Normalize(f.vel):Vector3.UnitY;
        if(f.look.attack.type=="bolt"){Emit(S.Glow,f.pos-dir*.3f,Vector3.Zero,.15f,.18f*f.look.fx.scale,.06f,c2*.8f,c2*0f,true);Emit(S.Streak,f.pos,dir*2,.05f,.2f*f.look.fx.scale,.1f,c,c*0f,true,0,0,.25f);return;}
        Emit(S.Glow,f.pos,Vector3.Zero,.12f,.18f,.05f,c*.8f,c*0f,true);
        if(f.missile){Emit(S.Fire,f.pos-dir*.3f,-dir*2,.12f,.13f,.04f,new Color(255,220,160),new Color(255,120,40,0),true);Emit(S.Smoke,f.pos-dir*.4f,RDir()*.15f,R(.7f,1.2f),.1f,.45f,new Color(200,200,200,140),new Color(200,200,200,0),false,0,.8f);return;}
        if(random.Next(2)==0)Emit(S.Smoke,f.pos,RDir()*.2f,R(.5f,.9f),.08f,.3f,new Color(c2.R,c2.G,c2.B,(byte)120),new Color(c2.R,c2.G,c2.B,(byte)0),false,0,1);
    }
    static void Steer(Shot s,float dt) {
        float speed=Math.Min(s.maxSpeed,s.vel.Length()+s.accel*dt);var dir=s.vel.LengthSquared()>1e-6f?Vector3.Normalize(s.vel):Vector3.UnitY;
        if(s.target!=null&&s.age>=s.homeAfter&&s.target.Entity.IsAddedToProject&&MDCombat.Living(s.target)) {
            var c=MDCombat.Center(s.target);float dist=Vector3.Distance(c,s.pos);
            if(dist<5)dir=Vector3.Normalize(c-s.pos);   // straight at it
            else {
                float t=dist/Math.Max(1,speed);var want=Vector3.Normalize(c+s.target.Velocity*t-s.pos);
                float near=new Vector2(c.X-s.pos.X,c.Z-s.pos.Z).Length(),turn=(near<13?10.5f:1.05f+4*(s.age-s.homeAfter))*dt,angle=MathF.Acos(Math.Clamp(Vector3.Dot(dir,want),-1,1));
                dir=angle<=turn?want:Vector3.Normalize(Vector3.Lerp(dir,want,turn/angle));
            }
        }
        s.vel=dir*speed;
    }
    void ShellTrail(Shot s,float dt) {
        var c=C(s.def.fx.color);var c2=C(s.def.fx.color2);var dir=s.vel.LengthSquared()>.01f?Vector3.Normalize(s.vel):Vector3.UnitY;
        if(s.def.attack.type=="bolt"){Emit(S.Glow,s.pos-dir*.3f,Vector3.Zero,.15f,.16f*s.def.fx.scale,.05f,c2*.7f,c2*0f,true);return;}
        Emit(S.Glow,s.pos,Vector3.Zero,.12f,.16f,.05f,c*.8f,c*0f,true);
        if(s.missile) {
            if(s.def.attack.type=="missile"){var body=PM(new Color(214,208,196,255));Emit(S.Streak,s.pos-dir*.2f,dir,Math.Max(dt,.02f)*1.5f,.06f*s.def.fx.scale+.03f,.06f*s.def.fx.scale+.03f,body,body,false,0,0,.25f);}   // the missile itself: a short pale body along its flight
            Emit(S.Fire,s.pos-dir*.3f,-dir*2,.12f,.12f,.04f,new Color(255,220,160),new Color(255,120,40,0),true);Emit(S.Smoke,s.pos-dir*.4f,RDir()*.15f,R(.7f,1.2f),.1f,.45f,new Color(200,200,200,140),new Color(200,200,200,0),false,0,.8f);return;
        }
        if(random.Next(2)==0)Emit(S.Smoke,s.pos,RDir()*.2f,R(.5f,.9f),.08f,.3f,new Color(c2.R,c2.G,c2.B,(byte)120),new Color(c2.R,c2.G,c2.B,(byte)0),false,0,1);
    }
    void ZoneVisual(Zone z) {
        float fade=Math.Min(1,(z.life-z.age)/1.2f);
        if(z.kind=="burn"&&random.Next(2)==0){var p=z.c+new Vector3(R(-z.r,z.r)*.8f,.05f,R(-z.r,z.r)*.8f);Emit(S.Fire,p,new Vector3(0,R(.6f,1.4f),0),R(.3f,.6f),.15f,.35f,new Color(255,200,120)*fade,new Color(200,50,10,0),true,-1,1);}
        var fx=z.owner.Tower.fx;var c1=C(fx.color);var c2=C(fx.color2);
        if(z.kind=="poison") {   // purple cloud low over the ground, bubbles rising out of it
            if(random.Next(2)==0){var p=z.c+new Vector3(R(-z.r,z.r)*.8f,R(.05f,.6f),R(-z.r,z.r)*.8f);Emit(random.Next(2)==0?S.Smoke:S.Smoke2,p,new Vector3(R(-.2f,.2f),R(.05f,.2f),R(-.2f,.2f)),R(1,1.8f),.5f,1.1f,PM(new Color(c2.R,c2.G,c2.B,(byte)(130*fade))),Color.Transparent,false,0,.5f,0,R(-.5f,.5f));}
            if(random.Next(6)==0)Emit(S.Bubble,z.c+new Vector3(R(-z.r,z.r)*.7f,.05f,R(-z.r,z.r)*.7f),new Vector3(0,R(.3f,.7f),0),R(.6f,1.1f),.05f,.1f,c1*fade,new Color(c1.R,c1.G,c1.B,(byte)0),true);
        }
        if(z.kind=="ice"&&random.Next(3)==0)Emit(S.Crystal,z.c+new Vector3(R(-z.r,z.r)*.7f,.05f,R(-z.r,z.r)*.7f),new Vector3(0,R(.1f,.4f),0),R(.6f,1.1f),R(.08f,.14f),.04f,c2*fade,new Color(c2.R,c2.G,c2.B,(byte)0),true,0,1,0,R(-2,2));
        if(z.kind=="orb"){var top=z.c+Vector3.UnitY*.9f;Emit(S.Glow,top,Vector3.Zero,.1f,.5f,.45f,c1*fade,c1*0f,true);if(random.Next(3)==0){var t=top+RDir()*R(.6f,z.r);AddBeam(top,t,.05f,c2*fade,.08f,true);}}
    }

    // ---------------------------------------------------------------- drawing
    public override void Draw(Camera camera) {
        if(add==null||alpha==null) {
            add=SubsystemParticles.PrimitivesRenderer.TexturedBatch(texture,true,0,DepthStencilState.DepthRead,RasterizerState.CullNoneScissor,BlendState.Additive,SamplerState.PointClamp);
            alpha=SubsystemParticles.PrimitivesRenderer.TexturedBatch(texture,true,1,DepthStencilState.DepthRead,RasterizerState.CullNoneScissor,BlendState.AlphaBlend,SamplerState.PointClamp);
        }
        var front=camera.ViewDirection;var right=Vector3.Normalize(Vector3.Cross(front,Vector3.UnitY));var up=Vector3.Normalize(Vector3.Cross(right,front));
        float vis=SubsystemParticles.SubsystemSky.VisibilityRange;var eye=camera.ViewPosition;
        for(int i=0;i<pn;i++) {
            ref var p=ref ps[i];if(Vector3.DistanceSquared(p.pos,eye)>vis*vis)continue;
            float t=p.age/p.life;float size=p.s0+(p.s1-p.s0)*t;var c=Color.Lerp(p.c0,p.c1,t);
            Vector3 x,y;
            if(p.flat){float cs=MathF.Cos(p.rot),sn=MathF.Sin(p.rot);x=new Vector3(cs,0,sn)*size;y=new Vector3(-sn,0,cs)*size;}
            else if(p.stretch>0&&p.vel.LengthSquared()>.01f) {
                var v=Vector3.Normalize(p.vel);var side=Vector3.Cross(v,front);if(side.LengthSquared()<1e-5f)side=right;side=Vector3.Normalize(side);
                x=side*size;y=v*(size+p.vel.Length()*p.stretch);
            } else {float cs=MathF.Cos(p.rot),sn=MathF.Sin(p.rot);x=(right*cs+up*sn)*size;y=(up*cs-right*sn)*size;}
            Quad(p.add?add:alpha,p.pos,x,y,p.slot,c);
        }
        foreach(var b in beams)DrawBeam(b,eye,front);
        foreach(var h in held.Values) {
            int sb=(int)S.Beam;float bu=sb%8/8f,bv=sb/8/8f,bq=1/8f;
            for(int j=0;j+1<h.trail.Count;j++) {   // ribbon between successive positions, older = fainter
                var p=h.trail[j];var q=h.trail[j+1];float f=1-p.age/.12f;
                add.QueueQuad(p.a,q.a,q.b,p.b,new Vector2(bu,bv+bq),new Vector2(bu+bq,bv+bq),new Vector2(bu+bq,bv),new Vector2(bu,bv),h.c*(.35f*f*f));
            }
            if(h.seen<.06f){Segment(h.a,h.b,h.width,h.c,eye);Segment(h.a,h.b,h.width*.35f,Color.White,eye);Quad(add,h.a,right*.35f,up*.35f,S.Glow,h.c);Quad(add,h.b,right*.5f,up*.5f,S.Glow,h.c);}
        }
        foreach(var s in shots) {
            var c=C(s.def.fx.color);var dir=s.vel.LengthSquared()>.01f?Vector3.Normalize(s.vel):Vector3.UnitY;float sc=s.def.fx.scale*(s.enhanced?1.5f:1);
            if(s.def.attack.type=="bolt") {
                var side=Vector3.Cross(dir,front);side=side.LengthSquared()<1e-5f?right:Vector3.Normalize(side);
                float k=Math.Max(1,s.vel.Length()/45);   // longer tracer for faster bolts (about one frame of flight at 60 fps)
                Quad(alpha,s.pos-dir*.45f*sc*k,side*.11f*sc,dir*.75f*sc*k,S.Bolt,Deep(c));Quad(add,s.pos-dir*.45f*sc*k,side*.05f*sc,dir*.6f*sc*k,S.Bolt,Color.White);Quad(add,s.pos,right*.3f*sc,up*.3f*sc,S.Glow,c*.9f);
            } else {Quad(add,s.pos,right*.2f*sc,up*.2f*sc,S.Glow,c);Quad(alpha,s.pos,right*.11f*sc,up*.11f*sc,S.Dot,C(s.def.fx.color2));}
        }
    }
    void Quad(TexturedBatch3D batch,Vector3 p,Vector3 x,Vector3 y,S slot,Color c) {
        int s=(int)slot;float u=s%8/8f,v=s/8/8f,d=1/8f;
        batch.QueueQuad(p-x-y,p+x-y,p+x+y,p-x+y,new Vector2(u,v+d),new Vector2(u+d,v+d),new Vector2(u+d,v),new Vector2(u,v),c);
    }
    void DrawBeam(Beam b,Vector3 eye,Vector3 front) {
        float t=b.age/b.life;var c=b.c*(1-t*t);
        if(!b.lightning){Segment(b.a,b.b,b.width*(1-.5f*t),c,eye);Segment(b.a,b.b,b.width*.35f,Color.White*(1-t),eye);return;}
        var r=new System.Random(b.seed+(int)(b.age*24));var prev=b.a;int n=Math.Max(4,(int)(Vector3.Distance(b.a,b.b)*2.5f));
        var dir=b.b-b.a;var side=Vector3.Normalize(Vector3.Cross(dir,Vector3.UnitY+new Vector3(.1f,0,.07f)));var side2=Vector3.Normalize(Vector3.Cross(dir,side));
        Segment(b.a,b.b,b.width*2.2f,c*.22f,eye);
        for(int i=1;i<=n;i++) {
            float f=i/(float)n;var p=Vector3.Lerp(b.a,b.b,f);
            if(i<n){float amp=MathF.Sin(f*MathF.PI)*Math.Min(.5f,dir.Length()*.08f);p+=side*((float)r.NextDouble()*2-1)*amp+side2*((float)r.NextDouble()*2-1)*amp;}
            Segment(prev,p,b.width,c,eye);Segment(prev,p,b.width*.4f,Color.White*(1-t),eye);prev=p;
        }
    }
    void Segment(Vector3 a,Vector3 b,float w,Color c,Vector3 eye) {
        var d=b-a;if(d.LengthSquared()<1e-6f)return;var side=Vector3.Cross(d,(a+b)*.5f-eye);if(side.LengthSquared()<1e-8f)return;side=Vector3.Normalize(side)*w;
        int s=(int)S.Beam;float u=s%8/8f,v=s/8/8f,q=1/8f;
        add.QueueQuad(a-side,a+side,b+side,b-side,new Vector2(u,v+q),new Vector2(u+q,v+q),new Vector2(u+q,v),new Vector2(u,v),c);
    }
}

// The creature model rewrites DiffuseColor every frame (injury flash), so the status tint is multiplied in after it.
[HarmonyPatch(typeof(ComponentCreatureModel),nameof(ComponentCreatureModel.Animate))]
public static class MDStatusTint {
    public static void Postfix(ComponentCreatureModel __instance){if(MDTowerWorld.Tint(__instance) is {} t)__instance.DiffuseColor=(__instance.DiffuseColor??Vector3.One)*t;}
}
