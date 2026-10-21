using Engine;

namespace Game;

// When wild robots may appear (mod setting Robots/SpawnTime). The names shown in the settings are in Assets/Lang.
public enum MDSpawnTime { Off, Night, Always }

// Player options (user, 2026-10-21: the mech panel covers a phone screen; the rifle status flickers; robot spawning should be
// adjustable). They live in the game's "模组设置" (API 1.9.3+, declared in modinfo.json "Settings"; ModSettingsManager keeps the
// values in ModSettings.xml, for every world). Read once after loading and again whenever one changes (MDModLoader), so the
// per-frame code reads plain fields; tests set the fields directly.
public static class MDSettings {
    public const string Package="local.mechdefense.sample";
    // mech panel (MDStatusWidget): shown, size in percent, position in percent of the free space (0 left / top, 100 right /
    // bottom; 100 / 100 is the old place above the bottom right corner), the key hints (panel line, boarding and weapon messages)
    public static bool HudShow=true,HudKeys=true;
    public static float HudScale=100,HudX=100,HudY=100;
    // robots (MDWildSpawns, MDRaids): wild spawns by time of day, every Interval seconds, Density x the group size and the number
    // near a player; robots from game day StartDay (elites 7 days later); raids on RaidChance % of the nights, RaidSize x the size
    public static MDSpawnTime SpawnTime=MDSpawnTime.Night;
    public static float SpawnInterval=60,SpawnDensity=1,StartDay=3,RaidChance=75,RaidSize=1;
    public static string LastError;

    public static void Load() {
        try {
            HudShow=Get("Hud","Show",true);HudKeys=Get("Hud","Keys",true);
            HudScale=Math.Clamp(Get("Hud","Scale",100f),50,150);HudX=Math.Clamp(Get("Hud","X",100f),0,100);HudY=Math.Clamp(Get("Hud","Y",100f),0,100);
            SpawnTime=Get("Robots","SpawnTime",MDSpawnTime.Night);
            SpawnInterval=Math.Clamp(Get("Robots","Interval",60f),15,600);SpawnDensity=Math.Clamp(Get("Robots","Density",1f),.25f,4);
            StartDay=Math.Clamp(MathF.Round(Get("Robots","StartDay",3f)),1,30);
            RaidChance=Math.Clamp(Get("Robots","RaidChance",75f),0,100);RaidSize=Math.Clamp(Get("Robots","RaidSize",1f),.5f,3);
        } catch(Exception e){LastError=e.Message;Log.Warning("[MechDefense] settings: "+e.Message);}
    }
    // a missing or mistyped value (no settings support, an edited ModSettings.xml) keeps the default
    static T Get<T>(string page,string item,T fallback)=>ModSettingsManager.TryGet<T>(out var v,Package,page,item)?v:fallback;
    public static void Defaults() {
        HudShow=HudKeys=true;HudScale=HudX=HudY=100;
        SpawnTime=MDSpawnTime.Night;SpawnInterval=60;SpawnDensity=1;StartDay=3;RaidChance=75;RaidSize=1;
    }
}
