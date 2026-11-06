using Engine;
using System.Globalization;
using System.Xml.Linq;
using HarmonyLib;

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
    public const string Package="zh667.MechDefense",Element="MechDefenseSettings";
    // 1.0.0 (user, 2026-11-06): the package was "local.mechdefense.sample" up to 0.18.4. ModSettings.xml keeps each mod's settings under
    // its package name and the engine hands a mod only its own element, so the old settings would be lost: Migrate (after the engine
    // loads the file, MDSettingsMigration) reads the old element when there is none under the new name and writes it back under the
    // new one. Worlds list the package names they were played with (SubsystemUsedMods): MDOldPackageAlias answers the old name.
    public const string OldPackage="local.mechdefense.sample";
    public static bool MigrateEnabled=true;   // lab switch: false = what a plain rename does (no migration, no alias)
    public static string Migrated="";         // what Migrate did (lab, log)
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
    // the defences, as before); every BossKills destroyed robots bring the next boss (0: only the beacon)
    public static float AggroRange=16;
    // 0.13.0 (players via the user, 2026-10-29: phones run hot with many towers; "烟雾和粒子效果可以削减"): particle amount,
    // 0 完整 / 1 精简 (default) / 2 极简 (MDTowerWorld.Emit)
    public static int FxLevel=1;
    public static readonly string[] FxNames=["完整","精简（默认）","极简"];
    public static int BossKills=50;
    // 0.15.0 (user, 2026-10-30): element reactions on / off (MDElements; types, resistances and statuses stay), and the robots'
    // growth per stage in % (stages every 7 days from StartDay, at most MaxGrowthStages: MDGrowth); 0 = off
    public static bool Reactions=true;
    public static bool DamageNumbers=true;
    public static float MedicCapacity=50000;
    public static int ThreatDifficulty=1; public static bool Endless;   // 0.16.0 threat raids at the defence core (MDThreat): 简单 / 普通 / 困难 / 噩梦, endless waves   // 0.15.0: a medic tower's healing store in points, 0 = unlimited (MDMedic)
    public const float MaxMedicCapacity=200000;   // 0.15.0: damage numbers over what this mod's towers and mechs hit (MDDamageNumbers.cs)
    public static int Growth=10;
    public static bool MechTerrain;   // 0.17.0 the mech's cannon and grenade break terrain (MDTerrainBlast), off by default
    public static readonly int[] GrowthSteps=[0,5,10,15];
    public const float MaxAggroRange=48;
    public const int MaxBossKills=200;
    public static string LastError;

    public static void Defaults() {
        HudShow=HudKeys=true;Hud=new();MDMechKeys.Apply(MDMechKeys.Defaults());
        SpawnTime=MDSpawnTime.Night;SpawnInterval=60;SpawnDensity=1;StartDay=3;RaidChance=75;RaidSize=1;AggroRange=16;BossKills=50;FxLevel=1;Reactions=true;Growth=10;DamageNumbers=true;MedicCapacity=50000;ThreatDifficulty=1;Endless=false;MechTerrain=false;
    }
    public static void Normalize() {
        Hud.Normalize();SpawnInterval=Math.Clamp(Finite(SpawnInterval,60),15,600);SpawnDensity=Math.Clamp(Finite(SpawnDensity,1),.25f,4);
        StartDay=Math.Clamp(StartDay,0,MaxStartDay);RaidChance=Math.Clamp(Finite(RaidChance,75),0,100);RaidSize=Math.Clamp(Finite(RaidSize,1),.5f,3);
        AggroRange=Math.Clamp(Finite(AggroRange,16),0,MaxAggroRange);BossKills=Math.Clamp(BossKills,0,MaxBossKills);FxLevel=Math.Clamp(FxLevel,0,2);Growth=Math.Clamp(Growth,0,15);MedicCapacity=Math.Clamp(Finite(MedicCapacity,50000),0,MaxMedicCapacity);ThreatDifficulty=Math.Clamp(ThreatDifficulty,0,3);
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
            new XAttribute("AggroRange",F(AggroRange)),new XAttribute("BossKills",BossKills),new XAttribute("FxLevel",FxLevel),new XAttribute("Reactions",Reactions),new XAttribute("DamageNumbers",DamageNumbers),new XAttribute("MedicCapacity",F(MedicCapacity)),new XAttribute("ThreatDifficulty",ThreatDifficulty),new XAttribute("Endless",Endless),new XAttribute("Growth",Growth),   // 0.12.0, 0.15.0
            new XAttribute("MechTerrain",MechTerrain),
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
            AggroRange=N("AggroRange",16);BossKills=(int)MathF.Round(N("BossKills",50));FxLevel=(int)MathF.Round(N("FxLevel",1));Reactions=B("Reactions",true);DamageNumbers=B("DamageNumbers",true);MedicCapacity=N("MedicCapacity",50000);ThreatDifficulty=(int)MathF.Round(N("ThreatDifficulty",1));Endless=B("Endless",false);Growth=(int)MathF.Round(N("Growth",10));
            MechTerrain=B("MechTerrain",false);
            MDMechKeys.Apply(MDMechKeys.FromText((string)e.Attribute("MechKeys")));
            Normalize();
        } catch(Exception x){LastError=x.Message;Log.Warning("[MechDefense] settings: "+x.Message);Defaults();}
    }
    // 1.0.0: settings saved by 0.18.4 or older under OldPackage, read once and written back under Package (the old element stays, for a
    // downgrade); nothing to do when the new element exists or there is no old one
    public static void Migrate() {
        try {
            var cache=ModSettingsManager.ModSettingsCache;
            if(!MigrateEnabled){Migrated="off";return;}
            if(cache.ContainsKey(Package)){Migrated="new key present";return;}
            if(!cache.TryGetValue(OldPackage,out var old)||old?.Element(Element)==null){Migrated="no old settings";return;}
            Load(old);Persist();Migrated=cache.ContainsKey(Package)?"migrated from "+OldPackage:"read from "+OldPackage+", not written: "+LastError;
            Log.Information("[MechDefense] settings: "+Migrated);
        } catch(Exception x){Migrated="failed: "+x.Message;Log.Warning("[MechDefense] settings migration: "+x.Message);}
    }
    // writes ModSettings.xml now (the game also writes it when it closes)
    public static void Persist(){try{ModSettingsManager.SaveModSettings();}catch(Exception x){LastError=x.Message;Log.Warning("[MechDefense] saving settings: "+x.Message);}}
    // texts shared by the settings page and the docs
    public static string SpawnTimeName(MDSpawnTime t)=>t switch{MDSpawnTime.Always=>"全天",MDSpawnTime.Off=>"关闭（夜袭照常）",_=>"仅夜晚"};
    public static string AggroText(float m)=>m<=0?"关闭（被打才还手）":$"{m:0} 米";
    public static string BossKillsText(int n)=>n<=0?"关闭（只用诱敌信标）":$"每击毁 {n} 台";
    public static string MedicText(float m)=>m<=0?"不限":$"每座 {m:0} 点";
    public static string GrowthText(int g)=>g<=0?"关闭":$"每阶段 +{g} %（每 7 天一个阶段，最多 +{g*MDGrowth.MaxStages} %）";
    public static string StartDayText(int day)=>day<=0?"开始天数：0（不刷新，野外和夜袭都没有）":$"开始天数：第 {day} 天（精英第 {day+7} 天）";
}

// 1.0.0 the old settings, right after the engine has read ModSettings.xml (before anything can save it with the defaults)
[HarmonyPatch(typeof(ModSettingsManager),nameof(ModSettingsManager.LoadModSettings))]
public static class MDSettingsMigration { public static void Postfix()=>MDSettings.Migrate(); }

// 1.0.0 a world played with 0.18.4 or older lists this mod under its old package name: before it loads, the play screen looks each name
// up (ModsManager.GetModEntity) and would warn that the mod is missing. The old name finds this mod. Saving the world writes the new name.
[HarmonyPatch(typeof(ModsManager),nameof(ModsManager.GetModEntity))]
public static class MDOldPackageAlias {
    public static int Answered;   // lab
    public static void Postfix(string packagename,ref ModEntity modEntity,ref bool __result) {
        if(__result||packagename!=MDSettings.OldPackage||!MDSettings.MigrateEnabled)return;
        if(ModsManager.ModList.Find(m=>m.modInfo?.PackageName==MDSettings.Package) is {} ours){modEntity=ours;__result=true;Answered++;}
    }
}
