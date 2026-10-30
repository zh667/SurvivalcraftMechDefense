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
    // Grass, flowers, dry bushes and snow give way to a tower (0.13.0); crops, saplings, torches and every solid block do not.
    public static bool Replaceable(int value) {
        var b=BlocksManager.Blocks[Terrain.ExtractContents(value)];
        return Terrain.ExtractContents(value)!=0&&!b.IsCollidable_(value)&&b is TallGrassBlock or FlowerBlock or DryBushBlock or LargeDryBushBlock or SnowBlock;
    }
    static bool Free(int value)=>Terrain.ExtractContents(value)==0||Replaceable(value);
    static bool Solid(int value)=>BlocksManager.Blocks[Terrain.ExtractContents(value)].IsCollidable_(value);
    // Where a click puts a tower (players via the user, 2026-10-29: "有时候明明空间足够，但是就是放置不下去"). Until 0.12.0 only a
    // block's top face counted (a side face did nothing at all), the clicked cell was always the footprint's south-west corner,
    // and a click on grass (the ray stops at the grass) took the grass as the floor, so "no full support". Now: the floor cell is
    // the grass / flower cell itself, the cell on a top face, or the cell beside a side face (down to the ground below it); the
    // footprint is the one of all placements that contain that cell which is free and closest to the click.
    public static bool Find(SubsystemTerrain terrain,SubsystemBodies bodies,TerrainRaycastResult hit,MDTowerDef d,out Vector3 position,out string reason) {
        position=default;var size=Cells(d);var f=hit.CellFace;int bx=f.X,by=f.Y,bz=f.Z;
        if(Replaceable(hit.Value)){}   // the plant's own cell is the floor cell
        else if(f.Face==4)by++;
        else if(f.Face==5){reason="对着地面或方块侧面右键放置。";return false;}
        else {
            var n=CellFace.FaceToPoint3(f.Face);bx+=n.X;bz+=n.Z;
            for(int k=0;k<4&&!Solid(terrain.Terrain.GetCellValue(bx,by-1,bz));k++)by--;   // beside a wall: down to the ground
        }
        var click=hit.HitPoint();string first=null;
        var tries=new List<(float d,int x0,int z0)>();
        for(int ox=0;ox<size.X;ox++)for(int oz=0;oz<size.Z;oz++){int x0=bx-ox,z0=bz-oz;var c=new Vector2(x0+size.X*.5f,z0+size.Z*.5f);tries.Add((Vector2.Distance(c,new Vector2(click.X,click.Z)),x0,z0));}
        foreach(var (_,x0,z0) in tries.OrderBy(t=>t.d)) {
            var at=new Vector3(x0+size.X*.5f,by,z0+size.Z*.5f);
            if(Validate(terrain,bodies,at,d,out reason)){position=at;return true;}
            first??=reason;
        }
        reason=first;return false;
    }
    // clears the grass and flowers in a validated footprint
    public static void ClearPlants(SubsystemTerrain terrain,Vector3 position,MDTowerDef d) {
        var size=Cells(d);int x0=Terrain.ToCell(position.X-size.X*.5f),y0=Terrain.ToCell(position.Y+.001f),z0=Terrain.ToCell(position.Z-size.Z*.5f);
        for(int x=x0;x<x0+size.X;x++)for(int z=z0;z<z0+size.Z;z++)for(int y=y0;y<y0+size.Y;y++)if(Replaceable(terrain.Terrain.GetCellValue(x,y,z)))terrain.DestroyCell(0,x,y,z,0,false,false);
    }
    public static bool Validate(SubsystemTerrain terrain,SubsystemBodies bodies,Vector3 position,MDTowerDef d,out string reason) {
        var size=Cells(d);int x0=Terrain.ToCell(position.X-size.X*.5f),y0=Terrain.ToCell(position.Y+.001f),z0=Terrain.ToCell(position.Z-size.Z*.5f);
        for(int x=x0;x<x0+size.X;x++)for(int z=z0;z<z0+size.Z;z++) {
            if(terrain.Terrain.GetChunkAtCell(x,z)==null){reason="部署区域尚未加载。";return false;}
            if(!Solid(terrain.Terrain.GetCellValue(x,y0-1,z))){reason=$"{Name(d)}需要完整平整的 {size.X}×{size.Z} 格支撑面。";return false;}
            for(int y=y0;y<y0+size.Y;y++)if(!Free(terrain.Terrain.GetCellValue(x,y,z))){reason=$"空间不足：{Name(d)}需要 {SizeText(d)}。";return false;}
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
    static bool installed,listening;
    public static bool Installed=>installed;
    // 0.15.0 (user, 2026-10-30: "炮塔都是英文的命名在那个信息显示模组里"): Neorxna 1.4.3 shows a non-creature entity under its
    // template name (MDTurret...), our postfix renames it. Install ran once at OnLoadingFinished and gave up when Neorxna's assembly
    // was not loaded yet; it now also patches as soon as that assembly loads and on every world load.
    public static void Install() {
        if(installed)return;
        if(!listening){listening=true;AppDomain.CurrentDomain.AssemblyLoad+=(_,e)=>{if(!installed&&e.LoadedAssembly.GetName().Name?.Contains("eorxna",StringComparison.OrdinalIgnoreCase)==true)TryPatch(e.LoadedAssembly);};}
        foreach(var a in AppDomain.CurrentDomain.GetAssemblies())if(TryPatch(a))return;
    }
    static bool TryPatch(System.Reflection.Assembly a) {
        try {
            var method=a.GetType("Neorxna.NUI.NUInfoPanelWidget",false)?.GetMethod("SetInfo_Entity",[typeof(Entity)]);
            if(method==null||installed)return installed;
            new Harmony("local.mechdefense.neorxna").Patch(method,postfix:new HarmonyMethod(typeof(MDNeorxnaCompatibility),nameof(AfterEntityInfo)));
            installed=true;Log.Information("[MechDefense] Neorxna info panel: names and status of towers and mechs");return true;
        } catch(Exception x){Log.Warning("[MechDefense] Neorxna patch: "+x.Message);return false;}
    }
    // Mercury (since 0.8.0) lives on the template of the 0.5.0 sample mech (Kind "Mech"); only the removed enemy (Kind "Enemy")
    // is a removed unit. Until 0.9.0 round 4 every unit that was not a tower read 已移除的旧单位 here, Mercury too (user report).
    public static string Name(ComponentMDUnit unit)=>unit.Kind=="Turret"?unit.Tower.name:unit.Mech!=null?unit.Mech.Type.Name+"机甲":"已移除的旧单位";
    public static void AfterEntityInfo(object __instance,Entity __0) {
        var unit=__0.FindComponent<ComponentMDUnit>();if(unit==null)return;
        var type=__instance.GetType();
        if(type.GetField("m_name")?.GetValue(__instance) is LabelWidget title)title.Text=Name(unit);
        if(type.GetField("m_details")?.GetValue(__instance) is LabelWidget details)details.Text=unit.Mech!=null?"机甲防线 · 机甲":"机甲防线 · 防御塔";
        var label=type.GetMethod("SetAdditionaLabel",[typeof(string),typeof(string)]);
        if(unit.Kind=="Turret") {
            label?.Invoke(__instance,["MDTowerFootprint",MDTowerPlacement.SizeText(unit.Tower)+" · 固定部署"]);
            label?.Invoke(__instance,["MDTowerHealth",unit.Destroyed?"已损毁":$"耐久 {(int)MathF.Round(unit.Damage.Hitpoints*100)}%"]);
            if(MDElements.Of(unit.Tower) is var e&&e!=MDElement.None)label?.Invoke(__instance,["MDTowerElement",$"伤害类型：{MDElements.Name(e)}"]);
            if(MDMedic.Is(unit)) {   // 0.15.0: the medic's store, as text and as a bar (green left, grey used up)
                label?.Invoke(__instance,["MDMedicCharge",MDMedic.Text(unit)]);
                if(!MDMedic.Unlimited&&type.GetMethod("SetAdditionalWidget",[typeof(string),typeof(Widget)]) is {} widget) {
                    const float W=180,H=10;var bar=new CanvasWidget{Size=new Vector2(W+4,H+4),Margin=new Vector2(10,2),IsHitTestVisible=false};
                    bar.Children.Add(new RectangleWidget{Size=new Vector2(W+4,H+4),FillColor=new Color(12,14,18),OutlineColor=Color.Transparent});
                    var grey=new RectangleWidget{Size=new Vector2(W,H),FillColor=new Color(95,98,104),OutlineColor=Color.Transparent};bar.Children.Add(grey);CanvasWidget.SetPosition(grey,new Vector2(2,2));
                    float share=MDMedic.Share(unit);
                    if(share>0){var left=new RectangleWidget{Size=new Vector2(W*share,H),FillColor=share>.25f?new Color(110,232,150):new Color(255,190,90),OutlineColor=Color.Transparent};bar.Children.Add(left);CanvasWidget.SetPosition(left,new Vector2(2,2));}
                    widget.Invoke(__instance,["MDMedicBar",bar]);
                }
            }
        }
        else if(unit.Mech!=null) {
            label?.Invoke(__instance,["MDMechArmour",unit.Destroyed?"已损毁":$"装甲 {(int)MathF.Round(unit.Damage.Hitpoints*100)}%"]);
            label?.Invoke(__instance,["MDMechKit",unit.Mech.Kit>0?$"武器改装 Mk{unit.Mech.Kit}":"未改装"]);
            label?.Invoke(__instance,["MDMechPilot",MDMechPilot.Driver(unit)!=null?"驾驶中":$"空闲 · 靠近按 {MDMechKeys.Hint(MDMechAction.Board)} 上车"]);
        }
    }
}
