using Engine;
using GameEntitySystem;
using HarmonyLib;

namespace Game;

// 0.17.0 (user, 2026-11-02: "希望机甲模组里的大炮能摧毁方块、载具……给机甲的火炮和榴弹做就可以，防御塔的炮弹都不做这个功能，并在模组设置里
// 可设置，默认关闭地形破坏"). With the setting on (MDSettings.MechTerrain), the mech's cannon shell and grenade also set off the game's
// own explosion where they burst (SubsystemExplosions.AddExplosion, no sound of its own: the shell's plays): it breaks blocks by
// their blast resistance, throws their drops, sets fire to wood now and then, and hits vehicles (a boat, other mods' vehicles)
// and animals the way gunpowder does. This mod's towers and mechs, the robots (their damage is the shell's own) and players take
// nothing from it, and its smoke cells and the white smoke trails of the thrown blocks are left out (the shell has its own blast,
// and the user asked for less smoke). Towers' shells never do this.
public static class MDTerrainBlast {
    static readonly HashSet<Point3> ours=[];
    public static bool Active;   // the explosions being simulated now include ours
    public static int Blasts,Spared;   // tests
    // the air cell just before the burst point (back along the flight); none within 1.8 m: no blast (under water, inside a wall)
    public static bool Blast(Project p,Vector3 point,Vector3 dir,float pressure) {
        var t=p.FindSubsystem<SubsystemTerrain>(true).Terrain;var back=dir.LengthSquared()>1e-4f?Vector3.Normalize(dir):-Vector3.UnitY;
        for(float k=.3f;k<=1.81f;k+=.3f) {
            var c=Terrain.ToCell(point-back*k);if(c.Y<2||c.Y>=254||t.GetChunkAtCell(c.X,c.Z)==null)return false;
            if(Terrain.ExtractContents(t.GetCellValue(c.X,c.Y,c.Z))!=0)continue;
            p.FindSubsystem<SubsystemExplosions>(true).AddExplosion(c.X,c.Y,c.Z,pressure,false,true);ours.Add(c);Blasts++;return true;
        }
        return false;
    }
    // SubsystemExplosions.Update simulates the first queued explosion and those within 4 cells of it together
    public static void Begin(List<SubsystemExplosions.ExplosionData> q) {
        Active=false;if(q.Count==0||ours.Count==0)return;var f=q[0];
        Active=q.Any(e=>Math.Abs(e.X-f.X)<=4&&Math.Abs(e.Y-f.Y)<=4&&Math.Abs(e.Z-f.Z)<=4&&ours.Contains(new Point3(e.X,e.Y,e.Z)));
    }
    public static void End(List<SubsystemExplosions.ExplosionData> q){Active=false;if(ours.Count>0)ours.RemoveWhere(c=>!q.Any(e=>e.X==c.X&&e.Y==c.Y&&e.Z==c.Z));}
    // the blocks it throws (SubsystemExplosions.m_generatedProjectiles) hit the same ones as before: not the spared
    static readonly HashSet<Projectile> debris=[];public static int DebrisSpared;
    public static void Thrown(IEnumerable<Projectile> ps){if(debris.Count>400)debris.Clear();foreach(var x in ps)debris.Add(x);}
    public static void Hit(Attackment a){if(a is ProjectileAttackment{Projectile:{} pr}&&debris.Contains(pr)&&a.Target?.FindComponent<ComponentBody>() is {} b&&Spare(b)){a.AttackPower=0;DebrisSpared++;}}   // MDModLoader.ProcessAttackment
    public static bool Spare(ComponentBody b)=>b.Entity.FindComponent<ComponentMDUnit>()!=null||b.Entity.FindComponent<ComponentMDRobot>()!=null||b.Entity.FindComponent<ComponentPlayer>()!=null;
}
[HarmonyPatch(typeof(SubsystemExplosions),nameof(SubsystemExplosions.Update))]
public static class MDTerrainBlastUpdate {
    static readonly HashSet<Point3> before=[];public static int CellsOff;   // the lab counts them
    public static void Prefix(SubsystemExplosions __instance) {
        MDTerrainBlast.Begin(__instance.m_queuedExplosions);before.Clear();
        if(MDTerrainBlast.Active&&__instance.m_explosionParticleSystem is {} ps)foreach(var k in ps.m_particlesByPoint.Keys)before.Add(k);
    }
    // the smoke cells this explosion added go out again (SetExplosionCell may be inlined into PostprocessExplosions, past the prefix below)
    public static void Postfix(SubsystemExplosions __instance) {
        if(MDTerrainBlast.Active&&__instance.m_explosionParticleSystem is {} ps)
            foreach(var (k,part) in ps.m_particlesByPoint.Where(kv=>!before.Contains(kv.Key)).ToList()){part.IsActive=false;ps.m_particlesByPoint.Remove(k);ps.m_inactiveParticles.Add(part);CellsOff++;}
        if(MDTerrainBlast.Active)MDTerrainBlast.Thrown(__instance.m_generatedProjectiles.Keys);
        MDTerrainBlast.End(__instance.m_queuedExplosions);
    }
}
[HarmonyPatch(typeof(ComponentBody),nameof(ComponentBody.UnderExplosion))]
public static class MDTerrainBlastBodies {
    public static bool Prefix(ComponentBody __instance){if(!MDTerrainBlast.Active||!MDTerrainBlast.Spare(__instance))return true;MDTerrainBlast.Spared++;return false;}
}
[HarmonyPatch(typeof(ExplosionParticleSystem),nameof(ExplosionParticleSystem.SetExplosionCell))]
public static class MDTerrainBlastSmoke {public static int Skipped;public static bool Prefix(){if(!MDTerrainBlast.Active)return true;Skipped++;return false;}}
[HarmonyPatch(typeof(SubsystemProjectiles),nameof(SubsystemProjectiles.AddTrail))]
public static class MDTerrainBlastTrails {public static int Skipped;public static bool Prefix(){if(!MDTerrainBlast.Active)return true;Skipped++;return false;}}   // the lab counts them
