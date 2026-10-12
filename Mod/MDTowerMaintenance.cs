using Engine;
using HarmonyLib;

namespace Game;

// Taking towers down and repairing them.
// Dismantle: three pickaxe hits (at the normal hit pace, within 3 s of each other) take a tower down into its deploy item.
// The item keeps the tower's health (MDDeploymentBlock: wear in its data, shown as the slot's health bar) and deploying it
// again restores that health. A wrecked tower cannot be taken down.
// Repair: right-click a tower holding iron or copper (ingot or block). One item restores a fixed number of the tower's hit
// points, so big towers take more: iron ingot 200, copper ingot 120, the blocks nine times that. A 650-point gun tower
// takes 4 iron ingots from nearly wrecked to full (it is built from 5), the 2600-point prologue cannon 13 (built from 7
// iron blocks). Iron and copper are the towers' own build materials; in the user's pack the Industrial Era sieve turns
// gravel into ore and its iron/copper plates stand in for ingots, so they are easier to come by than with vanilla smelting
// alone. Wonderful Era's wrench, for comparison, restores 10-75% of a vehicle per use by armour class.
public static class MDTowerMaintenance {
    public const int HitsToDismantle=3;
    const double DismantleWindow=3,RepairPace=.3;
    static readonly (string craftingId,float points,string name)[] materials=[("ironingot",200,"铁锭"),("copperingot",120,"铜锭"),("ironblock",1800,"铁块"),("copperblock",1080,"铜块")];
    static readonly System.Runtime.CompilerServices.ConditionalWeakTable<ComponentPlayer,System.Runtime.CompilerServices.StrongBox<double>> lastRepair=new();
    static readonly System.Random random=new();
    public static (float points,string name)? Material(int value) {
        var id=BlocksManager.Blocks[Terrain.ExtractContents(value)].GetCraftingId(value);
        foreach(var m in materials)if(m.craftingId==id)return (m.points,m.name);
        return null;
    }
    static int Percent(float f)=>(int)MathF.Round(f*100);
    static void Sound(ComponentMDUnit u,string name,Vector3 at,float volume=1)=>u.Project.FindSubsystem<SubsystemAudio>(true).PlayRandomSound(name,volume,(float)random.NextDouble()*.4f-.2f,at,4,false);

    public static void DismantleHit(ComponentMiner miner,ComponentMDUnit u,Vector3 hitPoint) {
        var p=miner.ComponentPlayer;miner.Poke(false);
        if(u.Destroyed){MDPlayer.Message(p,"炮塔已损毁，无法拆卸。");return;}
        double now=u.Clock.GameTime;
        if(now-u.DismantleAt>DismantleWindow)u.DismantleHits=0;
        u.DismantleHits++;u.DismantleAt=now;miner.DamageActiveTool(1);
        Sound(u,"Audio/Impacts/Metal",hitPoint);MDTowerWorld.For(u.Project).Sparks(hitPoint,Vector3.UnitY,6,new Color(255,210,140),4,.9f,.35f,.04f);
        if(u.DismantleHits<HitsToDismantle){MDPlayer.Message(p,$"拆卸{u.Tower.name}：{u.DismantleHits}/{HitsToDismantle}");return;}
        Dismantle(u,p?.ComponentBody.Position);
        MDPlayer.Message(p,$"已拆下{u.Tower.name}（耐久 {Percent(u.Damage.Hitpoints)}%）");
    }
    // Removes the tower and drops its deploy item (with the tower's health), thrown a little towards the player.
    public static int Dismantle(ComponentMDUnit u,Vector3? towards) {
        int value=MDDeploymentBlock.Value(u.Tower,u.Damage.Hitpoints);
        var at=u.Body.Position+Vector3.UnitY*Math.Min(1.5f,u.Tower.bounds[1][1]*.5f);
        var push=towards.HasValue?Vector3.Normalize(new Vector3(towards.Value.X-at.X,0,towards.Value.Z-at.Z)+new Vector3(.001f,0,0))*2:Vector3.Zero;
        u.Project.FindSubsystem<SubsystemPickables>(true).AddPickable(value,1,at,push+Vector3.UnitY*3,null);
        MDTowerWorld.For(u.Project).SmokePuffs(u.Body.Position+Vector3.UnitY*.5f,6,.5f,new Color(120,115,108,140),.8f,1.4f,.6f);
        u.Project.FindSubsystem<SubsystemAudio>(true).PlaySound("Audio/BlockPlaced",1,0,at,4,false);   // a single sound, not a folder
        u.Project.RemoveEntity(u.Entity,true);return value;
    }

    // Right-click with a repair material (ModLoader hook OnPlayerInputInteract). True when the click was about a tower.
    public static bool TryRepair(ComponentPlayer p) {
        var ray=p.ComponentInput.PlayerInput.Interact;var miner=p.ComponentMiner;
        if(!ray.HasValue||Material(miner.ActiveBlockValue) is not {} m)return false;
        var u=miner.Raycast<BodyRaycastResult>(ray.Value,RaycastMode.Interaction)?.ComponentBody.Entity.FindComponent<ComponentMDUnit>();
        if(u==null||u.Legacy)return false;
        double now=u.Clock.GameTime;
        var last=lastRepair.GetValue(p,_=>new(double.MinValue));
        if(now-last.Value<RepairPace&&now>=last.Value)return true;   // a held button repeats every frame
        last.Value=now;Repair(miner,u,m.points,m.name);return true;
    }
    public static bool Repair(ComponentMiner miner,ComponentMDUnit u,float points,string material) {
        var p=miner.ComponentPlayer;
        if(u.Destroyed){MDPlayer.Message(p,"炮塔已损毁，无法维修。");return false;}
        float hp=u.Damage.Hitpoints;
        if(hp>=.995f){MDPlayer.Message(p,$"{u.Tower.name}耐久已满，不需要维修。");return false;}
        float add=Math.Min(1-hp,points/u.Tower.hp);u.SetHitpoints(hp+add);miner.RemoveActiveTool(1);
        var at=u.Body.Position+Vector3.UnitY*Math.Min(1.5f,u.Tower.bounds[1][1]*.5f);
        Sound(u,"Audio/Impacts/Metal",at,.8f);MDTowerWorld.For(u.Project).Heal(u.Body.Position);
        MDPlayer.Message(p,$"{material}修复{u.Tower.name} +{Percent(add)}%，耐久 {Percent(u.Damage.Hitpoints)}%");
        return true;
    }
}

// A player hitting a tower with a pickaxe takes it apart instead of damaging it.
[HarmonyPatch(typeof(ComponentMiner),nameof(ComponentMiner.Hit))]
public static class MDDismantlePatch {
    public static bool Prefix(ComponentMiner __instance,ComponentBody componentBody,Vector3 hitPoint) {
        if(__instance.ComponentPlayer==null||componentBody?.Entity.FindComponent<ComponentMDUnit>() is not {Legacy:false} u)return true;
        if(BlocksManager.Blocks[Terrain.ExtractContents(__instance.ActiveBlockValue)] is not PickaxeBlock)return true;
        double now=__instance.m_subsystemTime.GameTime;
        if(now-__instance.m_lastHitTime<=__instance.HitInterval)return false;   // the pace of ordinary hits
        __instance.m_lastHitTime=now;MDTowerMaintenance.DismantleHit(__instance,u,hitPoint);return false;
    }
}
