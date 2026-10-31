using Engine;
using Engine.Graphics;
using GameEntitySystem;

namespace Game;

// 0.16.0 the defence core and threat raids (a player's suggestion via the user, 2026-10-30; chosen: "做核心玩法，不强制合成"; the night
// raids and the boss by kills stay, each with its own setting). Design: docs/tasks/2026-10-31-threat-core-0.16.md (C).
// - The core is a tower kind (attack.type "core", 防线核心, towers.json defense_core, the radar III's model for now: modelOf). One per world
//   is live: placing another puts the old one out (ComponentMDUnit.CoreLive false, 已失效). Robots go for it first, like a mech.
// - Threat: robots destroyed within CoreReach m of the live core add 1 (elites 3, bosses BossThreat: user, 10-31 "boss被击败好像不会
//   增加威胁度"); full at Cap (30 / difficulty): a raid. Every game mode but Harmless: the core is the player's own choice, so
//   Creative raids too (0.16.0 first cut skipped Creative like the wild spawns, and threat filled up to 30 with nothing after).
//   Shown over the core (bar and text) and in the handbook.
// - A raid: FirstCountdown s, then 3 + raids done / 2 waves (at most 6), WaveGap s apart; each countdown names the next wave's
//   robots. A wave is the night raid's size for the defence round the core x difficulty x (1 + 0.1 x raids done) x (1 + 0.15 x
//   wave), kinds by the growth stage; every second raid ends with a boss. Beaten: threat 0, raids done + 1, a payout at the core.
//   Endless (mod setting): waves keep coming, each stronger, until the core is destroyed. The core destroyed ends any raid.
// - The world keeps the threat, the raids done and whether the handbook was given (Project.xml); a raid under way when the world
//   is saved starts again from its countdown.
public static class MDThreat {
    public const float CoreReach=120,FirstCountdown=30,WaveGap=40,WaveTimeout=240;
    public const int BossThreat=10;
    public static bool Raids(Project p)=>p.FindSubsystem<SubsystemGameInfo>(true).WorldSettings.GameMode!=GameMode.Harmless;
    public static readonly string[] DifficultyNames=["简单","普通","困难","噩梦"];
    public static readonly float[] DifficultyFactors=[.7f,1,1.4f,2];
    public static float Difficulty=>DifficultyFactors[Math.Clamp(MDSettings.ThreatDifficulty,0,3)];
    public static int Cap=>(int)MathF.Round(30/Difficulty);
    public const string ThreatAttribute="MechDefenseThreat",RaidsAttribute="MechDefenseRaidsDone",ManualAttribute="MechDefenseManualGiven";
    public static float Threat; public static int RaidsDone; public static bool ManualGiven;
    public enum Phase{Idle,Countdown,Wave}
    public static Phase Now=Phase.Idle; public static int Wave,Waves; public static float Timer;
    public static List<string> NextWave=[]; public static readonly List<ComponentMDRobot> Fighting=[];
    public const string BossTag="boss:";   // a wave's boss, its template fixed when the wave is planned (the countdown names it)
    public static int BossesComing;   // a raid boss still counting down (MDBoss.Start): the wave waits for it
    public static string LastAnnounce="";   // tests
    static readonly System.Random rng=new();
    // the live core of this world, if any
    public static ComponentMDUnit Core(Project p)=>p.Entities.Select(e=>e.FindComponent<ComponentMDUnit>()).FirstOrDefault(u=>u?.Tower?.attack.type=="core"&&u.CoreLive&&!u.Destroyed);
    static ComponentMDUnit cached;static double cachedAt=-1;
    public static ComponentMDUnit CoreCached(Project p){double t=p.FindSubsystem<SubsystemTime>(true).GameTime;if(t-cachedAt>1||cached!=null&&!cached.Entity.IsAddedToProject){cachedAt=t;cached=Core(p);}return cached;}

    // a core just placed (MDDeployPatch): the old one goes out
    public static void Placed(ComponentMDUnit core,ComponentPlayer by) {
        int outed=0;foreach(var u in core.Project.Entities.Select(e=>e.FindComponent<ComponentMDUnit>()).Where(u=>u!=null&&u!=core&&u.Tower?.attack.type=="core"&&u.CoreLive).ToList()){u.CoreLive=false;outed++;}
        core.CoreLive=true;cached=core;
        MDPlayer.Message(by,outed>0?"新的防线核心已启用，原来的核心失效了（可以拆回）。":$"防线核心已启用：附近击毁机兵会让威胁度上涨，满 {Cap} 点引来多波次袭击。");
    }
    // a robot destroyed (MDRewards.Killed)
    public static void Killed(ComponentMDRobot r) {
        // not the raid's own robots: the last wave's die a frame after the raid is over and would start the next threat (lab combat15-core)
        if(Now!=Phase.Idle||r.ThreatRaid||!Raids(r.Project)||CoreCached(r.Project) is not {} core||Vector3.Distance(core.Body.Position,r.Body.Position)>CoreReach)return;
        Threat=Math.Min(Cap,Threat+(r.Kind.Boss?BossThreat:r.Kind.Elite?3:1));
    }
    static void Say(Project p,Vector3 at,string text,Color c){LastAnnounce=text;MDBoss.Announce(p,at,text,c);}

    public static void Update(Project p,float dt) {
        GiveManual(p);
        var core=CoreCached(p);
        if(core==null){if(Now!=Phase.Idle){Fighting.Clear();Now=Phase.Idle;Say(p,p.FindSubsystem<SubsystemPlayers>(true).ComponentPlayers.FirstOrDefault()?.ComponentBody.Position??Vector3.Zero,"防线核心被摧毁了，袭击结束。",new Color(255,120,100));Threat=0;}return;}
        if(!Raids(p))return;   // Harmless: no raids
        var at=core.Body.Position;
        switch(Now) {
            case Phase.Idle:
                if(Threat>=Cap){Now=Phase.Countdown;Wave=0;Waves=Math.Min(6,3+RaidsDone/2);Timer=FirstCountdown;NextWave=Plan(p,core,0);
                    Say(p,at,$"威胁度已满！{FirstCountdown:0} 秒后袭击开始，共 {(MDSettings.Endless?"无尽":Waves.ToString())} 波。第 1 波：{Describe(NextWave)}",new Color(255,110,90));}
                break;
            case Phase.Countdown:
                float before=Timer;Timer-=dt;
                foreach(int s in new[]{10,5})if(before>s&&Timer<=s)Say(p,at,$"第 {Wave+1} 波 {s} 秒后到达。",new Color(255,170,110));
                if(Timer<=0){Send(p,core,NextWave);Now=Phase.Wave;Timer=WaveTimeout;Say(p,at,$"第 {Wave+1} 波来了：{Describe(NextWave)}",new Color(255,120,100));}
                break;
            case Phase.Wave:
                Timer-=dt;Fighting.RemoveAll(r=>!r.Entity.IsAddedToProject||r.Health.Health<=0);
                if((Fighting.Count>0||BossesComing>0)&&Timer>0)break;
                Wave++;
                if(Wave<Waves||MDSettings.Endless){Now=Phase.Countdown;Timer=WaveGap;NextWave=Plan(p,core,Wave);
                    Say(p,at,$"第 {Wave} 波已打退。第 {Wave+1} 波 {WaveGap:0} 秒后到：{Describe(NextWave)}",new Color(160,255,160));}
                else{Now=Phase.Idle;Threat=0;RaidsDone++;int total=Math.Max(4,Waves*4);MDRewards.RaidBeaten(p,at+Vector3.UnitY*2,total);
                    Say(p,at,$"袭击已全部打退（共 {Waves} 波）！威胁度清零，下一次袭击会更强一些。",new Color(160,255,160));}
                break;
        }
    }
    // the robots of wave `wave`: templates
    public static List<string> Plan(Project p,ComponentMDUnit core,int wave) {
        var at=core.Body.Position;int day=MDWildSpawns.Day(p);
        int defence=p.Entities.Select(e=>e.FindComponent<ComponentMDUnit>()).Where(u=>u!=null&&!u.Destroyed&&u!=core&&(u.Tower!=null||u.Mech!=null)&&Vector3.Distance(u.Body.Position,at)<30).Sum(MDRaids.Weight);
        float baseSize=Math.Clamp(3+(int)MathF.Round(defence*.5f)+day/8,3,20)*MDSettings.RaidSize;
        int size=Math.Clamp((int)MathF.Round(baseSize*Difficulty*(1+.1f*RaidsDone)*(1+.15f*wave)),2,40);
        bool stage3=MDSettings.StartDay>0&&day>=MDSettings.StartDay+3,stage7=MDSettings.StartDay>0&&day>=MDSettings.StartDay+7;
        int elites=(int)MathF.Round(size*(stage7?1/3f:stage3?.25f:0));
        var list=new List<string>();for(int i=0;i<size;i++)list.Add(i<elites?MDRobots.PickElite(rng):MDRobots.PickNormal(p,rng));
        if(wave==Waves-1&&!MDSettings.Endless&&RaidsDone%2==1||MDSettings.Endless&&wave>0&&wave%5==4){list.Add(BossTag+MDBoss.NextTemplate);MDBoss.Next=MDBoss.Draw();}   // every second raid ends with a boss; endless: every fifth wave
        return list;
    }
    public static string Describe(List<string> wave) {
        var names=wave.Where(t=>!t.StartsWith(BossTag)).GroupBy(t=>MDRobots.KindOf(t).Name).Select(g=>$"{g.Key} ×{g.Count()}").ToList();
        foreach(var t in wave.Where(t=>t.StartsWith(BossTag)))names.Add("首领"+MDRobots.KindOf(t[BossTag.Length..]).Name);
        return string.Join("、",names);
    }
    static void Send(Project p,ComponentMDUnit core,List<string> wave) {
        var terrain=p.FindSubsystem<SubsystemTerrain>(true);var at=core.Body.Position;float dir=(float)(rng.NextDouble()*Math.PI*2);
        foreach(var t in wave) {
            if(t.StartsWith(BossTag)){BossesComing++;MDBoss.Start(p,at,"袭击的最后一波：{0}正在赶来！",b=>{BossesComing=Math.Max(0,BossesComing-1);if(b==null)return;b.ThreatRaid=true;Fighting.Add(b);},t[BossTag.Length..]);continue;}   // another boss already about: comes anyway
            var spot=MDRaids.Ground(terrain,at,dir+(float)(rng.NextDouble()-.5)*1.1f,44+(float)rng.NextDouble()*16,rng);if(spot==null)continue;
            var e=DatabaseManager.CreateEntity(p,t,true);e.FindComponent<ComponentBody>(true).Position=spot.Value+(MDRobots.KindOf(t).Fly>0?Vector3.UnitY*3:Vector3.Zero);
            var r=e.FindComponent<ComponentMDRobot>(true);r.Raider=true;r.RaidGoal=at;r.ThreatRaid=true;e.FindComponent<ComponentSpawn>(true).AutoDespawn=false;p.AddEntity(e);Fighting.Add(r);
        }
    }
    // the handbook, once per world, to every player in it
    static void GiveManual(Project p) {
        if(ManualGiven)return;var players=p.FindSubsystem<SubsystemPlayers>(true).ComponentPlayers;if(players.Count==0)return;
        ManualGiven=true;
        foreach(var pl in players){var inv=pl.ComponentMiner.Inventory;if(inv!=null&&ComponentInventoryBase.AcquireItems(inv,MDPartBlock.Value(MDPartBlock.Kind.Manual),1)==0)MDPlayer.Message(pl,"你得到了一本《机甲防线手册》：手持右键打开。");}
    }
    public static void Load(System.Xml.Linq.XElement project) {
        Threat=float.TryParse((string)project?.Attribute(ThreatAttribute),System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.InvariantCulture,out var t)?t:0;
        RaidsDone=int.TryParse((string)project?.Attribute(RaidsAttribute),out var n)?n:0;
        ManualGiven=(string)project?.Attribute(ManualAttribute)=="True";
        Now=Phase.Idle;Fighting.Clear();BossesComing=0;cached=null;cachedAt=-1;
    }
    public static void Save(System.Xml.Linq.XElement project) {
        if(project==null)return;project.SetAttributeValue(ThreatAttribute,Threat.ToString(System.Globalization.CultureInfo.InvariantCulture));
        project.SetAttributeValue(RaidsAttribute,RaidsDone);project.SetAttributeValue(ManualAttribute,ManualGiven);
    }
    // the core's line over it and in the Neorxna panel
    public static string Status(ComponentMDUnit core)=>!core.CoreLive?"已失效（每个存档只有一个有效的核心）":!Raids(core.Project)?"无害模式：不涨威胁度，不会袭击":
        Now==Phase.Idle?$"威胁度 {Threat:0} / {Cap}（{DifficultyNames[Math.Clamp(MDSettings.ThreatDifficulty,0,3)]}{(MDSettings.Endless?"，无尽模式":"")}）":
        Now==Phase.Countdown?$"第 {Wave+1} 波 {Timer:0} 秒后：{Describe(NextWave)}":$"第 {Wave+1}/{(MDSettings.Endless?"∞":Waves.ToString())} 波：还剩 {Fighting.Count} 台";
    public static string ManualText()=>
        $"防线核心是一种部署单位（铁锭、铜锭、合金板、火控芯片合成），每个存档只有一个有效：放下新的，旧的就失效（可以拆回）。机兵会优先攻击它。\n\n"+
        $"威胁度：核心 {CoreReach:0} 米内每击毁一台普通机兵 +1，精英 +3，首领 +{BossThreat}。满 {Cap} 点（难度“{DifficultyNames[Math.Clamp(MDSettings.ThreatDifficulty,0,3)]}”；难度越高满得越快）时袭击开始：{FirstCountdown:0} 秒倒计时后，一波接一波（3 波起，袭击越多波次越多，最多 6 波），每波之间 {WaveGap:0} 秒，倒计时会说出下一波的兵种和数量，核心头上也显示。每两次袭击最后一波有首领。\n\n"+
        $"全部打退：威胁度清零，在核心旁掉落奖励，下一次袭击略强。无尽模式（模组设置）：袭击不停，越来越强，直到核心被摧毁。核心被摧毁时袭击结束。\n\n"+
        $"创造模式也会袭击，无害模式不会。原来的夜袭和“击毁机兵召来首领”照常，模组设置里可以分别关掉。";
}

// over the live core: its threat bar or the raid's line
public sealed partial class MDTowerWorld {
    void DrawCore(Vector3 eye,Vector3 right,Vector3 up) {
        var core=MDThreat.CoreCached(project);if(core==null)return;float d=Vector3.Distance(core.Body.Position,eye);if(d>56)return;
        float w=2.6f+.03f*d,h=.2f+.005f*d;var at=new Vector3(core.Body.Position.X,core.Body.BoundingBox.Max.Y+1.1f,core.Body.Position.Z);
        Bar(at,right,up,w+.08f,h+.08f,0,1,new Color(12,14,18,220));Bar(at,right,up,w,h,0,1,new Color(60,50,70,230));
        float share=MDThreat.Now==MDThreat.Phase.Idle?MDThreat.Threat/Math.Max(1,MDThreat.Cap):1;
        Bar(at,right,up,w,h,0,share,MDThreat.Now==MDThreat.Phase.Idle?new Color(190,120,255):new Color(255,110,90)*(.75f+.25f*MathF.Sin(time*6)));
        labelBatch??=SubsystemParticles.PrimitivesRenderer.FontBatch(LabelWidget.BitmapFont,3,DepthStencilState.None,RasterizerState.CullNoneScissor,BlendState.AlphaBlend,SamplerState.LinearClamp);
        float sc=.016f+.0006f*d;var anchor=TextAnchor.HorizontalCenter|TextAnchor.VerticalCenter;var text=MDThreat.Status(core);var tp=at+up*(h+sc*28);
        labelBatch.QueueText(text,tp+right*sc*1.5f-up*sc*1.5f,right*sc,-up*sc,new Color(0,0,0,200),anchor);labelBatch.QueueText(text,tp,right*sc,-up*sc,Color.White,anchor);
    }
}
