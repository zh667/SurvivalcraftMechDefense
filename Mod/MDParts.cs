using Engine;
using Engine.Graphics;

namespace Game;

// Progression items (0.9.0, docs/tasks/2026-10-20-progression-robots.md): what the robot enemies drop, what the higher
// towers and Mercury are built from, the boss's beacon and blueprint, and the mech's weapon kits. One block, data = Kind.
// Robots drop scrap, chips and cores (MDRobots.cs); a player who does not fight crafts plates, chips and cores from
// vanilla materials at five times their normal price (Assets/MechDefense.cr). The blueprint only drops from the boss and the
// mech recipe uses it up.
// Models: tools/build_part_models.py (plan confirmed by the user 2026-10-21; Blender files and checks in
// E:/projects/md-item-models): low-poly in the vanilla items' style, 32 px pixel textures in Assets/Parts/parts.png (cell =
// data), face shading baked into Assets/Parts/part<N>.bin; the energy core's band, the beacon's lamp and the Mk3 kit's
// connector are emissive (they stay lit in the dark). The same model in slots, in the hand and on the ground.
public class MDPartBlock : Block {
    public enum Kind { Scrap, Alloy, Chip, Core, Blueprint, Beacon, Kit1, Kit2, Kit3 }
    public const int Count=9;
    static readonly (string name,string text,int stack)[] info=[
        ("机兵残骸",$"敌方机兵被击毁后留下的装甲碎片和零件。两块残骸加一个铁锭在熔炉里炼成一块合金板。手持右键炮塔可维修（每块 {MDTowerMaintenance.ScrapPoints:0} 点耐久）。",40),
        ("合金板",$"二阶、三阶炮塔和机甲的结构材料。熔炉：机兵残骸×2 + 铁锭。不打机兵也能在工作台用铁块、铁锭、铜锭硬做（贵 5 倍）。手持右键炮塔可维修（每块 {MDTowerMaintenance.AlloyPoints:0} 点耐久）。",40),
        ("火控芯片","三阶炮塔、机甲和诱敌信标要用的火控电路。精英机兵掉落较多，普通机兵偶尔掉。也能用半导体块、锗和铜锭在工作台做（贵 5 倍）。",40),
        ("能量核心","射线类、电荷类三阶炮塔，机甲和诱敌信标的能源。精英机兵偶尔掉，恶魔机甲必掉。也能用钻石和铜在工作台做（贵 5 倍）。",40),
        ("机甲设计图","合成机甲必需，合成时消耗：水星和复仇女神各一张，轩辕十四两张。只能从恶魔机甲身上得到：用诱敌信标把它引出来。",4),
        ("诱敌信标","右键放在地面上：信号会在 30 秒后把恶魔机甲引到这里。它很强，先布好炮塔再放。白天、夜里都能用；同一时间只能有一台恶魔机甲。",4),
        ("武器改装件 Mk1","手持对着机甲右键安装：机甲全部武器伤害 ×1.15。改装件按级别依次安装（Mk1 → Mk2 → Mk3），收起机甲后仍保留。",8),
        ("武器改装件 Mk2","在已装 Mk1 的机甲上安装：全部武器伤害 ×1.3。",8),
        ("武器改装件 Mk3","在已装 Mk2 的机甲上安装：全部武器伤害 ×1.5。",8),
    ];
    public static int Value(Kind k)=>Terrain.MakeBlockValue(BlocksManager.GetBlockIndex<MDPartBlock>(),0,(int)k);
    public static Kind? Of(int value)=>Terrain.ExtractContents(value)==BlocksManager.GetBlockIndex<MDPartBlock>()&&Terrain.ExtractData(value) is int d&&d<Count?(Kind)d:null;
    public static string Name(Kind k)=>info[(int)k].name;
    static int Data(int value)=>Math.Clamp(Terrain.ExtractData(value),0,Count-1);
    public override void Initialize(){DefaultDisplayName="机兵零件";DefaultDescription=info[0].text;DefaultCategory="Items";CraftingId="mdpart";IsPlaceable=false;MaxStacking=40;base.Initialize();}
    public override string GetDisplayName(SubsystemTerrain t,int v)=>info[Data(v)].name;
    public override string GetDescription(int v)=>info[Data(v)].text;
    public override int GetMaxStacking(int v)=>info[Data(v)].stack;
    public override string GetCategory(int v)=>(Kind)Data(v) is Kind.Beacon or Kind.Kit1 or Kind.Kit2 or Kind.Kit3?"Tools":"Items";
    public override IEnumerable<int> GetCreativeValues(){for(int i=0;i<Count;i++)yield return Terrain.MakeBlockValue(BlockIndex,0,i);}
    public override bool IsUseable_(int value)=>(Kind)Data(value)==Kind.Beacon;   // right-click on the ground (MDDeployPatch -> MDBoss.PlaceBeacon)
    public override int GetTextureSlotCount(int value)=>4;
    public override int GetFaceTextureSlot(int face,int value)=>Data(value);
    public override void GenerateTerrainVertices(BlockGeometryGenerator g,TerrainGeometry t,int v,int x,int y,int z){}
    // held like the vanilla iron ingot (first person 0.4, (0.5, -0.5, -0.6), 40 degrees; in hand 0.3, tilted 60 degrees);
    // the upright core and beacon stand upright in the hand like the battery
    static bool Upright(int v)=>(Kind)Data(v) is Kind.Core or Kind.Beacon;
    public override float GetFirstPersonScale(int value)=>.4f;
    public override Vector3 GetFirstPersonOffset(int value)=>new(.5f,-.5f,-.6f);
    public override Vector3 GetFirstPersonRotation(int value)=>new(0,40,0);
    public override float GetInHandScale(int value)=>Upright(value)?.38f:.3f;
    public override Vector3 GetInHandOffset(int value)=>new(0,.2f,0);
    public override Vector3 GetInHandRotation(int value)=>Upright(value)?Vector3.Zero:new(60,0,0);
    // Draw scale (calibrated against the vanilla iron ingot, which IngotBlock draws at 1.6 blocks long): the models keep the
    // plan's proportions (alloy plate 0.6 long ...) and are drawn x8/3, so the alloy plate is as long as an iron ingot.
    public const float DrawScale=8/3f;
    static Texture2D texture; static readonly (BlockMesh mesh,float ui)?[] meshes=new (BlockMesh,float)?[Count];
    public static Texture2D Texture=>texture??=ContentManager.Get<Texture2D>("Parts/parts");
    // part<N>.bin: int32 vertex count, int32 index count, per vertex x y z u v shade (2 + shade: emissive), int32 indices
    // (clockwise front). ui: the slot scale that makes every item span what a vanilla ingot does there (1.6 x its 0.85).
    static (BlockMesh mesh,float ui) Mesh(int data) {
        if(meshes[data] is {} m)return m;
        var s=ContentManager.GetStream($"Parts/part{data}.bin");s.Position=0;using var r=new BinaryReader(s);
        int nv=r.ReadInt32(),ni=r.ReadInt32();var mesh=new BlockMesh();float half=0;
        for(int i=0;i<nv;i++) {
            var p=new Vector3(r.ReadSingle(),r.ReadSingle(),r.ReadSingle());var uv=new Vector2(r.ReadSingle(),r.ReadSingle());float sh=r.ReadSingle();
            bool emissive=sh>=1.5f;if(emissive)sh-=2;byte b=(byte)(255*Math.Clamp(sh,0,1));
            mesh.Vertices.Add(new BlockMeshVertex{Position=p,TextureCoordinates=uv,Color=new Color(b,b,b),IsEmissive=emissive});
            half=MathF.Max(half,MathF.Max(MathF.Abs(p.X),MathF.Max(MathF.Abs(p.Y),MathF.Abs(p.Z))));
        }
        for(int i=0;i<ni;i++)mesh.Indices.Add(r.ReadInt32());
        var made=(mesh,Math.Clamp(1.36f/(2*half*DrawScale),.8f,1.4f));meshes[data]=made;return made;
    }
    public override void DrawBlock(PrimitivesRenderer3D r,int value,Color color,float size,ref Matrix matrix,DrawBlockEnvironmentData e) {
        var (mesh,ui)=Mesh(Data(value));
        BlocksManager.DrawMeshBlock(r,mesh,Texture,color,size*DrawScale*(e?.DrawBlockMode==DrawBlockMode.UI?ui:1),ref matrix,e);
    }
}

// Weapon kits: right-click Mercury with a kit (ModLoader hook OnPlayerInputInteract, before repairs). Mk1, Mk2, Mk3 in turn.
public static class MDKits {
    static readonly System.Runtime.CompilerServices.ConditionalWeakTable<ComponentPlayer,System.Runtime.CompilerServices.StrongBox<double>> last=new();
    public static bool TryInstall(ComponentPlayer p) {
        var ray=p.ComponentInput.PlayerInput.Interact;var miner=p.ComponentMiner;
        if(!ray.HasValue||MDPartBlock.Of(miner.ActiveBlockValue) is not {} k||k<MDPartBlock.Kind.Kit1)return false;
        var u=miner.Raycast<BodyRaycastResult>(ray.Value,RaycastMode.Interaction)?.ComponentBody.Entity.FindComponent<ComponentMDUnit>();
        if(u==null||u.Legacy)return false;
        var clock=u.Clock.GameTime;var l=last.GetValue(p,_=>new(double.MinValue));if(clock-l.Value<.4&&clock>=l.Value)return true;l.Value=clock;   // a held button repeats
        MDPlayer.Message(p,Install(u,k-MDPartBlock.Kind.Kit1+1,miner));return true;
    }
    // Installs kit `level` on a unit and uses one up from the miner's hand when it fits; returns the message for the player.
    public static string Install(ComponentMDUnit u,int level,ComponentMiner miner) {
        if(u.Mech==null)return "武器改装件只能装在机甲上。";
        if(u.Destroyed)return "机甲已损毁，无法改装。";
        if(u.Mech.Kit>=level)return $"这台机甲已经装了 Mk{u.Mech.Kit}。";
        if(u.Mech.Kit<level-1)return $"先装 Mk{level-1}（现在是 {(u.Mech.Kit==0?"未改装":"Mk"+u.Mech.Kit)}）。";
        u.Mech.Kit=level;miner?.RemoveActiveTool(1);
        var at=u.Body.Position+Vector3.UnitY*2;MDTowerWorld.For(u.Project).Sparks(at,Vector3.UnitY,16,new Color(255,210,140),5,1,.5f,.05f);
        u.Project.FindSubsystem<SubsystemAudio>(true).PlaySound(MDMechBrain.Se(25),1,0,at,8,false);
        return $"{u.Mech.Type.Name}机甲武器改装 Mk{level}：全部武器伤害 ×{MDMechState.KitFactor(level):0.##}";
    }
}
