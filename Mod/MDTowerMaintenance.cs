using Engine;
using HarmonyLib;

namespace Game;

// Taking towers and Mercury down and repairing them.
// Dismantle: three pickaxe hits (at the normal hit pace, within 3 s of each other) take a tower down into its deploy item.
// The item keeps the tower's health (MDDeploymentBlock: wear in its data, shown as the slot's health bar) and deploying it
// again restores that health. A wrecked tower cannot be taken down.
// Repair: right-click a tower holding robot scrap or an alloy plate (0.12.0, players via the user 2026-10-25: towers more durable
// and repaired only with the robots' drops, "修复就用这些掉落材料，医疗塔的修复功能不变"). One item restores a fixed number of the
// tower's hit points, so big towers take more: scrap 400, an alloy plate 1500. Tower health doubled in 0.12.0: a 1300-point
// tier-1 gun tower takes 4 scrap (a normal walker drops 2-4) or one plate from nearly wrecked to full. The medic tower still
// heals a share of the health (MDTowerBrain.Heal).
// The mechs are repaired with iron or copper (ingot or block) as before: iron ingot 200, copper ingot 120, the blocks nine times
// that, against the mech's armour (Mercury 4000: an iron ingot 5 %); taken down a mech becomes its deploy item with its health
// (a rider is set down first, OnEntityRemoved).
public static class MDTowerMaintenance {
    public const int HitsToDismantle=3;
    const double DismantleWindow=3,RepairPace=.3;
    static readonly (string craftingId,float points,string name)[] metals=[("ironingot",200,"铁锭"),("copperingot",120,"铜锭"),("ironblock",1800,"铁块"),("copperblock",1080,"铜块")];
    public const float ScrapPoints=400,AlloyPoints=1500;
    static readonly System.Runtime.CompilerServices.ConditionalWeakTable<ComponentPlayer,System.Runtime.CompilerServices.StrongBox<double>> lastRepair=new();
    static readonly System.Random random=new();
    // what one item restores on a tower (robot drops) or a mech (iron, copper); null when it is no repair material for either
    public static (float points,string name)? Material(int value,bool tower) {
        if(tower)return MDPartBlock.Of(value) switch{MDPartBlock.Kind.Scrap=>(ScrapPoints,"机兵残骸"),MDPartBlock.Kind.Alloy=>(AlloyPoints,"合金板"),_=>null};
        var id=BlocksManager.Blocks[Terrain.ExtractContents(value)].GetCraftingId(value);
        foreach(var m in metals)if(m.craftingId==id)return (m.points,m.name);
        return null;
    }
    static int Percent(float f)=>(int)MathF.Round(f*100);
    // Mercury (kind Mech) has no tower definition: its name, its armour as the repair scale and a fixed height for effects.
    static string Name(ComponentMDUnit u)=>u.Tower?.name??(u.Mech?.Type.Name??"水星")+"机甲";
    static string What(ComponentMDUnit u)=>u.Tower!=null?"炮塔":"机甲";
    static float Points(ComponentMDUnit u)=>u.Tower?.hp??u.Damage.AttackResilience;
    static Vector3 Top(ComponentMDUnit u)=>u.Body.Position+Vector3.UnitY*(u.Tower!=null?Math.Min(1.5f,u.Tower.bounds[1][1]*.5f):1.5f);
    static void Sound(ComponentMDUnit u,string name,Vector3 at,float volume=1)=>u.Project.FindSubsystem<SubsystemAudio>(true).PlayRandomSound(name,volume,(float)random.NextDouble()*.4f-.2f,at,4,false);

    public static void DismantleHit(ComponentMiner miner,ComponentMDUnit u,Vector3 hitPoint) {
        var p=miner.ComponentPlayer;miner.Poke(false);
        if(u.Destroyed){MDPlayer.Message(p,$"{What(u)}已损毁，无法拆卸。");return;}
        double now=u.Clock.GameTime;
        if(now-u.DismantleAt>DismantleWindow)u.DismantleHits=0;
        u.DismantleHits++;u.DismantleAt=now;miner.DamageActiveTool(1);
        Sound(u,"Audio/Impacts/Metal",hitPoint);MDTowerWorld.For(u.Project).Sparks(hitPoint,Vector3.UnitY,6,new Color(255,210,140),4,.9f,.35f,.04f);
        if(u.DismantleHits<HitsToDismantle){MDPlayer.Message(p,$"拆卸{Name(u)}：{u.DismantleHits}/{HitsToDismantle}");return;}
        Dismantle(u,p?.ComponentBody.Position);
        MDPlayer.Message(p,$"已拆下{Name(u)}（耐久 {Percent(u.Damage.Hitpoints)}%）");
    }
    // Removes the tower (or Mercury) and drops its deploy item (with its health), thrown a little towards the player.
    public static int Dismantle(ComponentMDUnit u,Vector3? towards) {
        int value=u.Tower!=null?MDDeploymentBlock.Value(u.Tower,u.Damage.Hitpoints,MDMedic.Is(u)?MDMedic.UsedEighths(u):0):MDDeploymentBlock.MechValue(u.Damage.Hitpoints,u.Mech?.Kit??0,u.Mech?.Type);
        var at=Top(u);
        var push=towards.HasValue?Vector3.Normalize(new Vector3(towards.Value.X-at.X,0,towards.Value.Z-at.Z)+new Vector3(.001f,0,0))*2:Vector3.Zero;
        u.Project.FindSubsystem<SubsystemPickables>(true).AddPickable(value,1,at,push+Vector3.UnitY*3,null);
        MDTowerWorld.For(u.Project).SmokePuffs(u.Body.Position+Vector3.UnitY*.5f,6,.5f,new Color(120,115,108,140),.8f,1.4f,.6f);
        u.Project.FindSubsystem<SubsystemAudio>(true).PlaySound("Audio/BlockPlaced",1,0,at,4,false);   // a single sound, not a folder
        u.Project.RemoveEntity(u.Entity,true);return value;
    }

    // Right-click with a repair material (ModLoader hook OnPlayerInputInteract). True when the click was about a tower or a mech.
    // Iron or copper on a tower (the old way) and robot drops on a mech say what to use instead.
    public static bool TryRepair(ComponentPlayer p) {
        var ray=p.ComponentInput.PlayerInput.Interact;var miner=p.ComponentMiner;int value=miner.ActiveBlockValue;
        if(!ray.HasValue||Material(value,true)==null&&Material(value,false)==null)return false;
        var u=miner.Raycast<BodyRaycastResult>(ray.Value,RaycastMode.Interaction)?.ComponentBody.Entity.FindComponent<ComponentMDUnit>();
        if(u==null||u.Legacy)return false;
        double now=u.Clock.GameTime;
        var last=lastRepair.GetValue(p,_=>new(double.MinValue));
        if(now-last.Value<RepairPace&&now>=last.Value)return true;   // a held button repeats every frame
        last.Value=now;
        if(Material(value,u.Tower!=null) is not {} m) {
            MDPlayer.Message(p,u.Tower!=null?"炮塔要用机兵残骸或合金板维修（铁、铜只能修机甲）。":"机甲用铁锭、铜锭（或铁块、铜块）维修。");
            return true;
        }
        Repair(miner,u,m.points,m.name);return true;
    }
    public static bool Repair(ComponentMiner miner,ComponentMDUnit u,float points,string material) {
        var p=miner.ComponentPlayer;
        if(u.Destroyed){MDPlayer.Message(p,$"{What(u)}已损毁，无法维修。");return false;}
        float hp=u.Damage.Hitpoints;
        if(hp>=.995f){MDPlayer.Message(p,$"{Name(u)}耐久已满，不需要维修。");return false;}
        float add=Math.Min(1-hp,points/Points(u));u.SetHitpoints(hp+add);miner.RemoveActiveTool(1);
        var at=Top(u);
        Sound(u,"Audio/Impacts/Metal",at,.8f);MDTowerWorld.For(u.Project).Heal(u.Body.Position);
        MDPlayer.Message(p,$"{material}修复{Name(u)} +{Percent(add)}%，耐久 {Percent(u.Damage.Hitpoints)}%");
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
