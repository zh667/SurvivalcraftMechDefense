using Engine;
using Engine.Graphics;

namespace Game;

// Creative-only robot summons (user, 2026-10-21: "敌方机兵最好做成那种可放置的", chose items in the creative inventory, not
// craftable): one per robot template; right-click the ground to place an enemy robot there, facing the player. It fights
// like any robot (towers and Mercury; players only outside Creative) and drops its loot. A boss (0.14.0: each of the five) only
// while none is about, scaled to the towers around it like one called by the beacon. Icons: each unit's icon mesh (tools/build_unit_assets.py).
public class MDSummonBlock : Block {
    public static readonly string[] Templates=[..MDRobots.Normal,..MDRobots.Elites,MDRobots.Boss,..MDRobots.Tanks,..MDRobots.Drones,..MDRobots.Gunships,..MDBoss.Bosses[1..]];   // new units at the end: an item's data keeps its unit
    static readonly string[] SkinNames=["","（B 型涂装）","（C 型涂装）"];
    public static int Count=>Templates.Length;
    static int Data(int value)=>Math.Clamp(Terrain.ExtractData(value),0,Templates.Length-1);
    public static string Template(int value)=>Templates[Data(value)];
    // the unit and skin from the item's template (its ComponentMDRobot Kind; skins end in B / C): 0.14.0 adds units after the boss,
    // so data ranges no longer say which one (the tank showed as the devil mech)
    static MDRobotKind KindOf(int value)=>MDRobots.KindOf(Template(value));
    static string Skin(int value){var t=Template(value);return KindOf(value).Boss?"":t.EndsWith("B")?SkinNames[1]:t.EndsWith("C")?SkinNames[2]:"";}
    public static int Value(int data)=>Terrain.MakeBlockValue(BlocksManager.GetBlockIndex<MDSummonBlock>(),0,data);
    public override void Initialize(){DefaultDisplayName="机兵召唤";DefaultCategory="Spawn";CraftingId="mdsummon";IsPlaceable=false;DefaultIsUseable=true;MaxStacking=1;base.Initialize();}
    public override string GetDisplayName(SubsystemTerrain t,int v)=>$"召唤：{KindOf(v).Name}{Skin(v)}";
    public override string GetDescription(int v)=>KindOf(v).Boss
        ?$"创造模式测试用：右键地面放出首领{KindOf(v).Name}。它按附近炮塔的数量强化耐久，会攻击炮塔和机甲（创造模式下不打玩家），击毁后照常掉落机甲设计图。同一时间只能有一台首领。"
        :$"创造模式测试用：右键地面放出一台敌方{KindOf(v).Name}。它会攻击炮塔和机甲（创造模式下不打玩家），击毁后照常掉落材料。";
    public override IEnumerable<int> GetCreativeValues(){for(int i=0;i<Templates.Length;i++)yield return Terrain.MakeBlockValue(BlockIndex,0,i);}
    public override bool IsUseable_(int value)=>true;
    public override int GetTextureSlotCount(int value)=>1;
    public override int GetFaceTextureSlot(int face,int value)=>0;
    public override void GenerateTerrainVertices(BlockGeometryGenerator g,TerrainGeometry t,int v,int x,int y,int z){}
    public override float GetFirstPersonScale(int value)=>.36f;
    public override Vector3 GetFirstPersonOffset(int value)=>new(.46f,-.28f,-.62f);
    public override Vector3 GetFirstPersonRotation(int value)=>new(0,90,0);
    public override float GetInHandScale(int value)=>.4f;
    public override Vector3 GetInHandOffset(int value)=>new(0,.12f,0);
    public override Vector3 GetInHandRotation(int value)=>new(90,0,0);
    public override void DrawBlock(PrimitivesRenderer3D r,int value,Color color,float size,ref Matrix matrix,DrawBlockEnvironmentData e) {
        var root=KindOf(value).Root;var icon=MDTowerLibrary.IconMesh(root+"icon.bin",root+"icon");
        if(e?.DrawBlockMode==DrawBlockMode.UI){size*=icon.fit;var m=Matrix.CreateTranslation(-icon.center*size)*matrix;BlocksManager.DrawMeshBlock(r,icon.mesh,icon.texture,color,size,ref m,e);}
        else BlocksManager.DrawMeshBlock(r,icon.mesh,icon.texture,color,size,ref matrix,e);
    }

    // Right-click on the ground (MDDeployPatch): the robot stands on the clicked face, facing the player.
    public static bool Place(ComponentMiner miner,Ray3 ray) {
        var p=miner.Project;var pl=miner.ComponentPlayer;var value=miner.ActiveBlockValue;
        var hit=miner.Raycast<TerrainRaycastResult>(ray,RaycastMode.Digging);if(!hit.HasValue||hit.Value.CellFace.Face!=4){MDPlayer.Message(pl,"对着地面右键放出机兵。");return false;}
        var at=hit.Value.HitPoint()+Vector3.UnitY*.05f;var kind=KindOf(value);
        if(kind.Boss) {
            if(MDBoss.Alive(p) is {} boss){MDPlayer.Message(pl,$"已经有一台首领（{boss.Kind.Name}）在附近了。");return false;}
            MDBoss.Spawn(p,at,at+Vector3.UnitY*3,Template(value));miner.RemoveActiveTool(1);return true;
        }
        var e=DatabaseManager.CreateEntity(p,Template(value),true);var body=e.FindComponent<ComponentBody>(true);
        if(!MDMechPilot.Clear(p.FindSubsystem<SubsystemTerrain>(true),at,body.BoxSize)){MDPlayer.Message(pl,"空间不足，放不下这台机兵。");return false;}
        var f=miner.ComponentCreature.ComponentBody.Position-at;body.Position=at;body.Rotation=Quaternion.CreateFromAxisAngle(Vector3.UnitY,MathF.Atan2(-f.X,-f.Z));
        p.AddEntity(e);miner.RemoveActiveTool(1);return true;
    }
}
