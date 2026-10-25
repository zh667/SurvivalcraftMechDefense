using Engine;
using System.Globalization;
using System.Xml.Linq;

namespace Game;

// When wild robots may appear (MDSettingsScreen cycles through these).
public enum MDSpawnTime { Night, Always, Off }

// Where the mech's status panel sits. Not custom: above the bottom right corner, clear of the hotbar (the place before
// 0.9.0 round 4). Custom: its centre at (X, Y) of the controls area (0-1), Scale x the screen fit, turned by Rotation
// (radians, clockwise on screen). The same layout serves the panel in play (MDHudHost) and the editor (MDHudLayoutScreen).
public sealed class MDHudPlacement {
    public bool Custom; public float X=.85f,Y=.8f,Scale=1,Rotation;
    public const float Edge=20,Bottom=85,MinScale=.5f,MaxScale=2.5f;
    public MDHudPlacement Copy()=>new(){Custom=Custom,X=X,Y=Y,Scale=Scale,Rotation=Rotation};
    public MDHudPlacement Normalize() {
        X=float.IsFinite(X)?Math.Clamp(X,0,1):.85f;Y=float.IsFinite(Y)?Math.Clamp(Y,0,1):.8f;
        Scale=float.IsFinite(Scale)?Math.Clamp(Scale,MinScale,MaxScale):1;Rotation=float.IsFinite(Rotation)?MathF.IEEERemainder(Rotation,MathF.PI*2):0;return this;
    }
    // phones get 0.5-1 of the panel's size, as before
    public static float Fit(Vector2 area)=>Math.Clamp(Math.Min(area.X/700,area.Y/450),.5f,1);
    public float TotalScale(Vector2 area)=>Fit(area)*(Custom?Scale:1);
    public float Turn=>Custom?Rotation:0;
    // the screen box of a size x scale panel turned by the rotation
    public static Vector2 Extent(Vector2 size,float scale,float rotation){float c=MathF.Abs(MathF.Cos(rotation)),s=MathF.Abs(MathF.Sin(rotation));return new Vector2(size.X*c+size.Y*s,size.X*s+size.Y*c)*scale;}
    // the panel's centre in the area, kept fully on screen
    public Vector2 Centre(Vector2 area,Vector2 size) {
        var ext=Extent(size,TotalScale(area),Turn);
        if(!Custom)return new Vector2(area.X-Edge-ext.X/2,area.Y-Bottom-ext.Y/2);
        var half=Vector2.Min(ext/2,area/2);
        return new Vector2(Math.Clamp(X*area.X,half.X,area.X-half.X),Math.Clamp(Y*area.Y,half.Y,area.Y-half.Y));
    }
    // where the unscaled panel goes (top left, for CanvasWidget.SetWidgetPosition) and its render transform (scale and turn
    // about its own centre)
    public Vector2 Corner(Vector2 area,Vector2 size,out Matrix transform) {
        transform=Matrix.CreateTranslation(-size.X/2,-size.Y/2,0)*Matrix.CreateScale(TotalScale(area))*Matrix.CreateRotationZ(Turn)*Matrix.CreateTranslation(size.X/2,size.Y/2,0);
        return Centre(area,size)-size/2;
    }
    public bool Hit(Vector2 point,Vector2 area,Vector2 size) {
        var q=Vector2.Transform(point-Centre(area,size),Matrix.CreateRotationZ(-Turn))/TotalScale(area);
        return MathF.Abs(q.X)<=size.X/2+10&&MathF.Abs(q.Y)<=size.Y/2+10;
    }
    // a custom place that starts where the panel is now
    public void MakeCustom(Vector2 area,Vector2 size){if(Custom)return;var c=Centre(area,size);X=c.X/area.X;Y=c.Y/area.Y;Scale=1;Rotation=0;Custom=true;}
}

// Player options (user, 2026-10-21, rounds 3-4): the mech panel (shown, key hints, place / size / turn in MDHudLayoutScreen)
// and the robots (MDWildSpawns, MDRaids). Edited in the mod's own settings page (MDSettingsScreen, reached from the game's
// "模组设置" through one entry, like the CS weapons mod) and saved by MDModLoader into the game's ModSettings.xml
// (ModLoader.SaveSettings / LoadSettings), so they hold for every world. The per-frame code reads these plain fields.
public static class MDSettings {
    public const string Package="local.mechdefense.sample",Element="MechDefenseSettings";
    public static bool HudShow=true,HudKeys=true;
    public static MDHudPlacement Hud=new();
    // robots: wild spawns by time of day, every SpawnInterval s, SpawnDensity x the group size and the number near a player;
    // robots from game day StartDay (0: none at all, wild or raids; elites 7 days later); raids on RaidChance % of the nights,
    // RaidSize x their size
    public static MDSpawnTime SpawnTime=MDSpawnTime.Night;
    public static float SpawnInterval=60,SpawnDensity=1,RaidChance=75,RaidSize=1;
    public static int StartDay=3;
    public const int MaxStartDay=99999;   // no practical limit (user, 2026-10-25: "开始天数玩家可随意设置数值（上限不设为30）"; was 30)
    // 0.12.0 (players via the user, 2026-10-25): a robot attacks a player within AggroRange m that it sees (0: only when hit or at
    // the defences, as before); every BossKills destroyed robots bring the devil mech (0: only the beacon)
    public static float AggroRange=16;
    public static int BossKills=50;
    public const float MaxAggroRange=48;
    public const int MaxBossKills=200;
    public static string LastError;

    public static void Defaults() {
        HudShow=HudKeys=true;Hud=new();MDMechKeys.Apply(MDMechKeys.Defaults());
        SpawnTime=MDSpawnTime.Night;SpawnInterval=60;SpawnDensity=1;StartDay=3;RaidChance=75;RaidSize=1;AggroRange=16;BossKills=50;
    }
    public static void Normalize() {
        Hud.Normalize();SpawnInterval=Math.Clamp(Finite(SpawnInterval,60),15,600);SpawnDensity=Math.Clamp(Finite(SpawnDensity,1),.25f,4);
        StartDay=Math.Clamp(StartDay,0,MaxStartDay);RaidChance=Math.Clamp(Finite(RaidChance,75),0,100);RaidSize=Math.Clamp(Finite(RaidSize,1),.5f,3);
        AggroRange=Math.Clamp(Finite(AggroRange,16),0,MaxAggroRange);BossKills=Math.Clamp(BossKills,0,MaxBossKills);
        if(!Enum.IsDefined(SpawnTime))SpawnTime=MDSpawnTime.Night;
    }
    static float Finite(float v,float d)=>float.IsFinite(v)?v:d;
    static string F(float v)=>v.ToString("R",CultureInfo.InvariantCulture);
    public static void Save(XElement mod) {
        mod.Element(Element)?.Remove();
        mod.Add(new XElement(Element,
            new XAttribute("HudShow",HudShow),new XAttribute("HudKeys",HudKeys),new XAttribute("HudCustom",Hud.Custom),
            new XAttribute("HudX",F(Hud.X)),new XAttribute("HudY",F(Hud.Y)),new XAttribute("HudScale",F(Hud.Scale)),new XAttribute("HudRotation",F(Hud.Rotation)),
            new XAttribute("SpawnTime",SpawnTime),new XAttribute("SpawnInterval",F(SpawnInterval)),new XAttribute("SpawnDensity",F(SpawnDensity)),
            new XAttribute("StartDay",StartDay),new XAttribute("RaidChance",F(RaidChance)),new XAttribute("RaidSize",F(RaidSize)),
            new XAttribute("AggroRange",F(AggroRange)),new XAttribute("BossKills",BossKills),   // 0.12.0
            new XAttribute("MechKeys",MDMechKeys.ToText(MDMechKeys.Current()))));   // 0.11.0: the mech's bound keys (MDMechKeys)
    }
    // a missing or unreadable value keeps the default
    public static void Load(XElement mod) {
        try {
            Defaults();var e=mod?.Element(Element);if(e==null)return;
            bool B(string n,bool d)=>bool.TryParse((string)e.Attribute(n),out var v)?v:d;
            float N(string n,float d)=>float.TryParse((string)e.Attribute(n),NumberStyles.Float,CultureInfo.InvariantCulture,out var v)?v:d;
            HudShow=B("HudShow",true);HudKeys=B("HudKeys",true);
            Hud=new MDHudPlacement{Custom=B("HudCustom",false),X=N("HudX",.85f),Y=N("HudY",.8f),Scale=N("HudScale",1),Rotation=N("HudRotation",0)};
            SpawnTime=Enum.TryParse<MDSpawnTime>((string)e.Attribute("SpawnTime"),out var st)?st:MDSpawnTime.Night;
            SpawnInterval=N("SpawnInterval",60);SpawnDensity=N("SpawnDensity",1);StartDay=(int)MathF.Round(N("StartDay",3));RaidChance=N("RaidChance",75);RaidSize=N("RaidSize",1);
            AggroRange=N("AggroRange",16);BossKills=(int)MathF.Round(N("BossKills",50));
            MDMechKeys.Apply(MDMechKeys.FromText((string)e.Attribute("MechKeys")));
            Normalize();
        } catch(Exception x){LastError=x.Message;Log.Warning("[MechDefense] settings: "+x.Message);Defaults();}
    }
    // writes ModSettings.xml now (the game also writes it when it closes)
    public static void Persist(){try{ModSettingsManager.SaveModSettings();}catch(Exception x){LastError=x.Message;Log.Warning("[MechDefense] saving settings: "+x.Message);}}
    // texts shared by the settings page and the docs
    public static string SpawnTimeName(MDSpawnTime t)=>t switch{MDSpawnTime.Always=>"全天",MDSpawnTime.Off=>"关闭（夜袭照常）",_=>"仅夜晚"};
    public static string AggroText(float m)=>m<=0?"关闭（被打才还手）":$"{m:0} 米";
    public static string BossKillsText(int n)=>n<=0?"关闭（只用诱敌信标）":$"每击毁 {n} 台";
    public static string StartDayText(int day)=>day<=0?"开始天数：0（不刷新，野外和夜袭都没有）":$"开始天数：第 {day} 天（精英第 {day+7} 天）";
}
