using Engine;
using GameEntitySystem;
using TemplatesDatabase;
using HarmonyLib;
using System.Reflection;

namespace Game;

// A fixed structure still participates in other bodies' collision queries, but
// never integrates gravity, contact impulses, fluid drift or collision nudges.
public sealed class ComponentMDTowerBody:ComponentBody {
    Vector3 anchor;Quaternion rotation;bool anchored;
    public Vector3 Anchor=>anchor;
    public void FixAt(Vector3 position,Quaternion orientation) {
        anchor=position;rotation=orientation;anchored=true;Position=anchor;Rotation=rotation;
        CanBePushedByOtherBodies=false;IsGravityEnabled=false;Velocity=Vector3.Zero;
    }
    public override void Load(ValuesDictionary v,IdToEntityMap map) {
        base.Load(v,map);CanBePushedByOtherBodies=false;IsGravityEnabled=false;
        if(v.GetValue("MDAnchored",false))FixAt(v.GetValue<Vector3>("MDAnchor"),v.GetValue<Quaternion>("MDAnchorRotation"));
    }
    public override void Save(ValuesDictionary v,EntityToIdMap map) {
        if(!anchored)FixAt(Position,Rotation);
        base.Save(v,map);v.SetValue("MDAnchored",true);v.SetValue("MDAnchor",anchor);v.SetValue("MDAnchorRotation",rotation);
    }
    public override void ApplyImpulse(Vector3 impulse) { }
    public override void ApplyDirectMove(Vector3 move) { }
    public override void Update(float dt) {
        if(!anchored)FixAt(Position,Rotation);
        Position=anchor;Rotation=rotation;Velocity=Vector3.Zero;CollisionVelocityChange=Vector3.Zero;
        m_totalImpulse=Vector3.Zero;m_directMove=Vector3.Zero;CrushedTime=0;
        IsGravityEnabled=false;CanBePushedByOtherBodies=false;
    }
}

public static class MDTowerPlacement {
    // Footprint from the authored geometry: base/head reach rounded up to whole blocks (width, height, depth).
    public static Point3 Cells(MDTowerDef d)=>new(d.cells[0],d.cells[1],d.cells[2]);
    public static string Name(MDTowerDef d)=>d.name;
    public static string SizeText(MDTowerDef d){var s=Cells(d);return $"{s.X}×{s.Z} 格底座，{s.Y} 格净高";}
    public static Vector3 Snap(CellFace face,MDTowerDef d) {
        var size=Cells(d);
        return new(face.X+size.X*.5f,face.Y+1,face.Z+size.Z*.5f);
    }
    public static Quaternion SnapRotation(Quaternion rotation) {
        var f=rotation.GetForwardVector();float yaw=MathF.Atan2(-f.X,-f.Z);
        return Quaternion.CreateFromAxisAngle(Vector3.UnitY,MathF.Round(yaw/(MathF.PI/2))*MathF.PI/2);
    }
    public static bool IsReserved(SubsystemBodies bodies,int x,int y,int z) {
        var near=new DynamicArray<ComponentBody>();bodies.FindBodiesAroundPoint(new(x+.5f,z+.5f),5,near);
        var box=new BoundingBox(new(x+.001f,y+.001f,z+.001f),new(x+.999f,y+.999f,z+.999f));
        return near.Any(b=>b.Entity.FindComponent<ComponentMDUnit>()?.Kind=="Turret"&&box.Intersection(b.BoundingBox));
    }
    public static bool Validate(SubsystemTerrain terrain,SubsystemBodies bodies,Vector3 position,MDTowerDef d,out string reason) {
        var size=Cells(d);int x0=Terrain.ToCell(position.X-size.X*.5f),y0=Terrain.ToCell(position.Y+.001f),z0=Terrain.ToCell(position.Z-size.Z*.5f);
        for(int x=x0;x<x0+size.X;x++)for(int z=z0;z<z0+size.Z;z++) {
            if(terrain.Terrain.GetChunkAtCell(x,z)==null){reason="部署区域尚未加载。";return false;}
            int support=terrain.Terrain.GetCellValue(x,y0-1,z);
            if(!BlocksManager.Blocks[Terrain.ExtractContents(support)].IsCollidable_(support)){reason=$"{Name(d)}需要完整平整的 {size.X}×{size.Z} 格支撑面。";return false;}
            for(int y=y0;y<y0+size.Y;y++)if(Terrain.ExtractContents(terrain.Terrain.GetCellValue(x,y,z))!=0){reason=$"空间不足：{Name(d)}需要 {SizeText(d)}。";return false;}
        }
        var box=new BoundingBox(new(x0+.001f,y0+.001f,z0+.001f),new(x0+size.X-.001f,y0+size.Y-.001f,z0+size.Z-.001f));
        var near=new DynamicArray<ComponentBody>();bodies.FindBodiesAroundPoint(new(position.X,position.Z),8,near);
        if(near.Any(b=>box.Intersection(b.BoundingBox))){reason="占地区域已有生物、载具或炮塔，请退后再放置。";return false;}
        reason=null;return true;
    }
}

// Optional integration: patch only the mod's existing info presentation method.
// Does not alter its archive, entity identity, faction or creature components.
public static class MDNeorxnaCompatibility {
    static bool installed;
    public static bool Installed=>installed;
    public static void Install() {
        if(installed)return;
        var type=AppDomain.CurrentDomain.GetAssemblies().Select(a=>a.GetType("Neorxna.NUI.NUInfoPanelWidget",false)).FirstOrDefault(t=>t!=null);
        var method=type?.GetMethod("SetInfo_Entity",[typeof(Entity)]);
        if(method==null)return;
        new Harmony("local.mechdefense.neorxna").Patch(method,postfix:new HarmonyMethod(typeof(MDNeorxnaCompatibility),nameof(AfterEntityInfo)));
        installed=true;
    }
    public static void AfterEntityInfo(object __instance,Entity __0) {
        var unit=__0.FindComponent<ComponentMDUnit>();if(unit==null)return;
        var type=__instance.GetType();
        if(type.GetField("m_name")?.GetValue(__instance) is LabelWidget title)title.Text=unit.Kind=="Turret"?unit.Tower.name:unit.Kind=="Mech"?"毁灭枪手机甲":"敌方机兵";
        if(type.GetField("m_details")?.GetValue(__instance) is LabelWidget details)details.Text="机甲防线 · 终末地防御塔";
        if(unit.Kind=="Turret")type.GetMethod("SetAdditionaLabel",[typeof(string),typeof(string)])?.Invoke(__instance,["MDTowerFootprint",MDTowerPlacement.SizeText(unit.Tower)+" · 固定部署"]);
    }
}
