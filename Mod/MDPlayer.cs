using Engine;
using Engine.Graphics;
using HarmonyLib;

namespace Game;

public class MDModLoader : ModLoader {
    public override void __ModInitialize(){new Harmony("local.mechdefense.sample").PatchAll();ModsManager.RegisterHook("OnLoadingFinished",this);ModsManager.RegisterHook("BeforeMinerPlace",this);}
    public override void OnLoadingFinished(List<Action> actions){actions.Add(MDNeorxnaCompatibility.Install);}
    public override void BeforeMinerPlace(ComponentMiner miner,TerrainRaycastResult hit,int x,int y,int z,BlockPlacementData data,out bool placementNotAllowed) {
        placementNotAllowed=MDTowerPlacement.IsReserved(miner.Project.FindSubsystem<SubsystemBodies>(true),x,y,z);
        if(placementNotAllowed)MDPlayer.Message(miner.ComponentPlayer,"此格属于炮塔固定占地区域。");
    }
}

public class MDDeploymentBlock : Block {
    // Data values 0/3/4 keep the first three gun towers of older saves; 5+ the other towers. 1/2 were the Destroy Gunners
    // mech and enemy (removed in 0.6.0): such items stay in old inventories under a placeholder name and cannot be deployed.
    const string RemovedName="已移除的旧单位";public const string RemovedNote="这个单位已在 0.6.0 移除，不能再部署。";
    public override void Initialize(){DefaultDisplayName="防御塔 · 部署";DefaultDescription="防御塔在完整平整的支撑面上对齐方格固定部署，自动攻击捕食者和追击玩家的生物。";DefaultCategory="Weapons";IsPlaceable=false;DefaultIsUseable=true;IsAimable=false;MaxStacking=20;CraftingId="mddeploy";base.Initialize();}
    public static MDTowerDef Tower(int value)=>MDTowerLibrary.ByData(Terrain.ExtractData(value));
    public override string GetDisplayName(SubsystemTerrain t,int v)=>Tower(v) is {} d?d.name+" · 部署":RemovedName;
    public override string GetDescription(int v)=>Tower(v) is {} d?$"{d.summary}\n占地：{MDTowerPlacement.SizeText(d)}。右键选择的方块是占地西南角。":RemovedNote;
    public override IEnumerable<int> GetCreativeValues() {
        foreach(var d in MDTowerLibrary.All)yield return Terrain.MakeBlockValue(BlockIndex,0,d.data);
    }
    // Held item. The icon mesh is centred on its bounding box and at most one unit long; without these the engine draws
    // it at scale 1 on the camera itself (first person: nothing visible) or centred on the hand (third person).
    public static float FirstScale=.36f,HandScale=.4f;
    public static Vector3 FirstOffset=new(.46f,-.28f,-.62f),FirstRotation=new(0,90,0),HandOffset=new(0,.12f,0),HandRotation=new(90,0,0);   // side view; upright in the hand
    public override float GetFirstPersonScale(int value)=>FirstScale;
    public override Vector3 GetFirstPersonOffset(int value)=>FirstOffset;
    public override Vector3 GetFirstPersonRotation(int value)=>FirstRotation;
    public override float GetInHandScale(int value)=>HandScale;
    public override Vector3 GetInHandOffset(int value)=>HandOffset;
    public override Vector3 GetInHandRotation(int value)=>HandRotation;
    public override int GetTextureSlotCount(int value)=>1;
    public override int GetFaceTextureSlot(int face,int value)=>0;
    public override void GenerateTerrainVertices(BlockGeometryGenerator g,TerrainGeometry t,int v,int x,int y,int z){}
    // Small 3D icon (~1500 triangles, the tower's own atlas) instead of the full 10-40k-triangle model.
    public override void DrawBlock(PrimitivesRenderer3D r,int value,Color color,float size,ref Matrix matrix,DrawBlockEnvironmentData e) {
        if(e?.DrawBlockMode is DrawBlockMode.FirstPerson or DrawBlockMode.ThirdPerson&&Tower(value) is {} held&&MDHeldModel.Draw(held,color,size,matrix,e))return;
        var d=Tower(value);if(d==null){d=MDTowerLibrary.All[0];color*=.35f;}   // a removed unit (data 1/2): the first tower's icon, dimmed
        var icon=MDTowerLibrary.Icon(d);
        if(e?.DrawBlockMode==DrawBlockMode.UI) {
            size*=icon.fit;var m=Matrix.CreateTranslation(-icon.center*size)*matrix;
            BlocksManager.DrawMeshBlock(r,icon.mesh,icon.texture,color,size,ref m,e);
        }
        else BlocksManager.DrawMeshBlock(r,icon.mesh,icon.texture,color,size,ref matrix,e);
    }
}

// A tower in the hand is drawn from its full glTF model (GPU buffers), the way the engine draws the bare first-person
// hand. The ~1500-triangle icon mesh is fine in an inventory slot but falls apart when it fills part of the screen:
// the V5 models are mostly small bevel strips, and those are the triangles the icon leaves out.
public static class MDHeldModel {
    public static Camera Camera;   // the camera being drawn (set by the patches below)
    static readonly LitShader shader=new(2,false,false,true,false,false);
    sealed class Entry { public Model model; public Texture2D texture; public Matrix[] bones; public Matrix fit; }
    static readonly Dictionary<string,Entry> cache=[];
    public static bool Draw(MDTowerDef d,Color color,float size,Matrix matrix,DrawBlockEnvironmentData e) {
        if(Camera==null)return false;
        if(!cache.TryGetValue(d.id,out var c)) {
            var model=ContentManager.Get<Model>("Towers/"+d.id+"/model");var bones=new Matrix[model.Bones.Count];
            foreach(var b in model.Bones)bones[b.Index]=b.ParentBone==null?b.Transform:b.Transform*bones[b.ParentBone.Index];   // rest pose
            var lo=new Vector3(d.bounds[0][0],d.bounds[0][1],d.bounds[0][2]);var hi=new Vector3(d.bounds[1][0],d.bounds[1][1],d.bounds[1][2]);var ext=hi-lo;
            // same frame as the icon mesh: centred on the bounding box, longest side one unit
            c=new Entry{model=model,texture=ContentManager.Get<Texture2D>("Towers/"+d.id+"/atlas"),bones=bones,fit=Matrix.CreateTranslation(-(lo+hi)/2)*Matrix.CreateScale(1/MathF.Max(ext.X,MathF.Max(ext.Y,ext.Z)))};
            cache[d.id]=c;
        }
        float light=LightingManager.LightIntensityByLightValue[Math.Clamp(e.Light,0,15)];
        shader.InstancesCount=1;shader.Texture=c.texture;shader.SamplerState=SamplerState.PointClamp;shader.MaterialColor=new Vector4(color);
        shader.AmbientLightColor=new Vector3(light*LightingManager.LightAmbient);shader.DiffuseLightColor1=shader.DiffuseLightColor2=new Vector3(light);
        shader.LightDirection1=Vector3.TransformNormal(LightingManager.DirectionToLight1,Camera.ViewMatrix);shader.LightDirection2=Vector3.TransformNormal(LightingManager.DirectionToLight2,Camera.ViewMatrix);
        shader.Transforms.View=Matrix.Identity;shader.Transforms.Projection=Camera.ProjectionMatrix;   // the matrix passed in already includes the view
        Display.DepthStencilState=DepthStencilState.Default;Display.RasterizerState=RasterizerState.CullNoneScissor;Display.BlendState=BlendState.Opaque;
        var place=c.fit*Matrix.CreateScale(size)*matrix;
        foreach(var mesh in c.model.Meshes) {
            shader.Transforms.World[0]=c.bones[mesh.ParentBone.Index]*place;
            foreach(var part in mesh.MeshParts)if(part.IndicesCount>0)Display.DrawIndexed(PrimitiveType.TriangleList,shader,part.VertexBuffer,part.IndexBuffer,part.StartIndex,part.IndicesCount);
        }
        return true;
    }
}
[HarmonyPatch(typeof(ComponentFirstPersonModel),nameof(ComponentFirstPersonModel.Draw))]
public static class MDHeldCameraFirst {public static void Prefix(Camera camera)=>MDHeldModel.Camera=camera;}
[HarmonyPatch(typeof(ComponentHumanModel),nameof(ComponentHumanModel.DrawExtras))]
public static class MDHeldCameraThird {public static void Prefix(Camera camera)=>MDHeldModel.Camera=camera;}

public static class MDPlayer {
    public static void Message(ComponentPlayer p,string text)=>p?.ComponentGui.DisplaySmallMessage(text,Color.White,true,false);
}

[HarmonyPatch(typeof(ComponentMiner),nameof(ComponentMiner.Use))]
public static class MDDeployPatch {
    public static bool Prefix(ComponentMiner __instance,Ray3 ray,ref bool __result) {
        if(Terrain.ExtractContents(__instance.ActiveBlockValue)!=BlocksManager.GetBlockIndex<MDDeploymentBlock>())return true;
        __result=true;var hit=__instance.Raycast<TerrainRaycastResult>(ray,RaycastMode.Digging);if(!hit.HasValue||hit.Value.CellFace.Face!=4)return false;
        var terrain=__instance.Project.FindSubsystem<SubsystemTerrain>(true);
        if(MDDeploymentBlock.Tower(__instance.ActiveBlockValue) is {} tower) {
            var pos=MDTowerPlacement.Snap(hit.Value.CellFace,tower);var subsystemBodies=__instance.Project.FindSubsystem<SubsystemBodies>(true);
            if(!MDTowerPlacement.Validate(terrain,subsystemBodies,pos,tower,out var reason)){MDPlayer.Message(__instance.ComponentPlayer,reason);return false;}
            var entity=DatabaseManager.CreateEntity(__instance.Project,tower.template,true);
            var fixedBody=entity.FindComponent<ComponentMDTowerBody>(true);var rot=MDTowerPlacement.SnapRotation(__instance.ComponentCreature.ComponentBody.Rotation);
            fixedBody.FixAt(pos,rot);var state=entity.FindComponent<ComponentMDUnit>();state.Yaw=state.AimYaw=MathF.Atan2(-fixedBody.Matrix.Forward.X,-fixedBody.Matrix.Forward.Z);
            entity.FindComponent<ComponentSpawn>(true).SpawnDuration=0;__instance.Project.AddEntity(entity);__instance.RemoveActiveTool(1);return false;
        }
        MDPlayer.Message(__instance.ComponentPlayer,MDDeploymentBlock.RemovedNote);return false;   // data 1/2: a removed unit
    }
}
