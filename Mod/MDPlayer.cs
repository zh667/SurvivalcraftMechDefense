using Engine;
using Engine.Input;
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
    public override IEnumerable<KeyValuePair<string, object>> GetKeyboardMappings(){yield return new("机甲：切换弹药",Key.B);yield return new("机甲：换弹动作",Key.R);}
}

public class MDDeploymentBlock : Block {
    // Data values 0/3/4 keep the first three gun towers of older saves, 1/2 the legacy mech/enemy; 5+ the other towers.
    static readonly string[] Legacy=["","毁灭枪手机甲 · 部署","敌方机兵 · 部署"];
    public override void Initialize(){DefaultDisplayName="终末地防御塔 · 部署";DefaultDescription="防御塔在完整平整的支撑面上对齐方格固定部署，自动攻击敌兵和捕食者。机甲靠近按 E 上下车，WASD 驾驶，左键开火。";DefaultCategory="Weapons";IsPlaceable=false;DefaultIsUseable=true;IsAimable=false;MaxStacking=20;CraftingId="mddeploy";base.Initialize();}
    public static MDTowerDef Tower(int value)=>MDTowerLibrary.ByData(Terrain.ExtractData(value));
    public override string GetDisplayName(SubsystemTerrain t,int v)=>Tower(v) is {} d?d.name+" · 部署":Legacy[Math.Clamp(Terrain.ExtractData(v),1,2)];
    public override string GetDescription(int v)=>Tower(v) is {} d?$"{d.summary}\n占地：{MDTowerPlacement.SizeText(d)}。右键选择的方块是占地西南角。":DefaultDescription;
    public override IEnumerable<int> GetCreativeValues() {
        foreach(var d in MDTowerLibrary.All)yield return Terrain.MakeBlockValue(BlockIndex,0,d.data);
        yield return Terrain.MakeBlockValue(BlockIndex,0,1);yield return Terrain.MakeBlockValue(BlockIndex,0,2);
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
        var icon=Tower(value) is {} d?MDTowerLibrary.Icon(d):Terrain.ExtractData(value)==1
            ?MDTowerLibrary.IconMesh("Towers/_legacy/mech_icon.bin","Imported/Mech/texture"):MDTowerLibrary.IconMesh("Towers/_legacy/enemy_icon.bin","Imported/Enemy/texture");
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
    public static ComponentMDUnit Mounted(ComponentPlayer p)=>p?.ComponentRider?.Mount?.Entity.FindComponent<ComponentMDUnit>();
    public static void Message(ComponentPlayer p,string text)=>p?.ComponentGui.DisplaySmallMessage(text,Color.White,true,false);
    public static bool Clear(SubsystemTerrain t,Vector3 foot,Vector3 size) {
        var min=foot-new Vector3(size.X*.5f,0,size.Z*.5f);var max=foot+new Vector3(size.X*.5f,size.Y,size.Z*.5f);
        for(int x=Terrain.ToCell(min.X);x<=Terrain.ToCell(max.X);x++)for(int z=Terrain.ToCell(min.Z);z<=Terrain.ToCell(max.Z);z++)for(int y=Terrain.ToCell(min.Y);y<=Terrain.ToCell(max.Y);y++) {
            int v=t.Terrain.GetCellValue(x,y,z);if(t.Terrain.GetChunkAtCell(x,z)==null||BlocksManager.Blocks[Terrain.ExtractContents(v)].IsCollidable_(v))return false;
        }return true;
    }
    public static bool CanBoard(ComponentPlayer p,ComponentMDUnit u) {
        if(u.Kind!="Mech"||u.Driver!=null||u.Damage.Hitpoints<=0)return false;
        var from=p.ComponentBody.Position+Vector3.UnitY;var box=u.Body.BoundingBox;var near=Vector3.Min(Vector3.Max(from,box.Min),box.Max);
        return Vector3.DistanceSquared(from,near)<2.25f&&!u.Project.FindSubsystem<SubsystemTerrain>(true).Raycast(from,near,false,true,(v,d)=>BlocksManager.Blocks[Terrain.ExtractContents(v)].IsCollidable_(v)).HasValue;
    }
    public static bool Exit(ComponentPlayer p,ComponentMDUnit u,bool force=false) {
        var t=u.Project.FindSubsystem<SubsystemTerrain>(true);var bodies=u.Project.FindSubsystem<SubsystemBodies>(true);Vector3? destination=null;
        for(int radius=3;radius<=6&&destination==null;radius++)for(int i=0;i<8&&destination==null;i++) {
            Vector3 q=u.Body.Position+new Vector3(MathF.Cos(i*MathF.PI/4)*radius,2,MathF.Sin(i*MathF.PI/4)*radius);
            var ground=t.Raycast(q,q-Vector3.UnitY*5,false,true,(v,d)=>BlocksManager.Blocks[Terrain.ExtractContents(v)].IsCollidable_(v));if(!ground.HasValue)continue;
            q.Y-=ground.Value.Distance-.08f;if(!Clear(t,q,p.ComponentBody.StanceBoxSize))continue;
            var near=new DynamicArray<ComponentBody>();bodies.FindBodiesAroundPoint(new(q.X,q.Z),2,near);
            if(near.Any(b=>b!=p.ComponentBody&&b!=u.Body&&Vector3.DistanceSquared(CenterXZ(b.Position),CenterXZ(q))<2))continue;
            destination=q;
        }
        if(destination==null&&!force){Message(p,"机甲周围没有安全下车位置，请移动到空地。");return false;}
        destination??=u.Body.Position+Vector3.UnitY*(u.Body.StanceBoxSize.Y+.2f);
        p.ComponentRider.m_isAnimating=false;p.ComponentBody.ParentBody=null;p.ComponentBody.Position=destination.Value;p.ComponentBody.Velocity=Vector3.Zero;
        p.GameWidget.ActiveCamera=p.GameWidget.FindCamera<FppCamera>();u.Throttle=u.Steer=0;u.Trigger=false;return true;
    }
    static Vector3 CenterXZ(Vector3 v)=>new(v.X,0,v.Z);
    public static void Input(ComponentPlayer p,float dt) {
        var u=Mounted(p);bool blocked=p.ComponentHealth.Health<=0||DialogsManager.HasDialogs(p.GuiWidget)||p.ComponentGui.ModalPanelWidget!=null;
        if(!blocked&&p.GameWidget.Input.IsKeyDownOnce(Key.E)) {
            if(u!=null)Exit(p,u);
            else if(p.ComponentRider.Mount==null) {
                var near=new DynamicArray<ComponentBody>();p.Project.FindSubsystem<SubsystemBodies>(true).FindBodiesAroundPoint(new(p.ComponentBody.Position.X,p.ComponentBody.Position.Z),5,near);
                var found=near.Select(b=>b.Entity.FindComponent<ComponentMDUnit>()).Where(x=>x!=null&&CanBoard(p,x)).OrderBy(x=>Vector3.DistanceSquared(x.Body.Position,p.ComponentBody.Position)).FirstOrDefault();
                if(found!=null){found.AimYaw=found.Yaw;found.AimPitch=0;p.ComponentRider.StartMounting(found.Mount);p.GameWidget.ActiveCamera=p.GameWidget.FindCamera<TppCamera>();Message(p,"已登上机甲：WASD 驾驶／左键射击／V 视角／E 下车");}
            }
            if(u!=null||Mounted(p)!=null)p.ComponentInput.m_playerInput.ToggleInventory=false;
        }
        u=Mounted(p);if(u==null)return;
        var input=p.ComponentInput.PlayerInput;
        if(!blocked&&p.GameWidget.Input.IsKeyDownOnce(Key.B)){u.CycleAmmo();Message(p,$"机甲弹药：{u.AmmoName}");}
        if(!blocked&&p.GameWidget.Input.IsKeyDownOnce(Key.R)){u.Cooldown=Math.Max(u.Cooldown,.45f);u.PlayEvent("Reload",.8f);Message(p,"换弹动作");}
        u.Throttle=blocked?0:input.Move.Z;u.Steer=blocked?0:input.Move.X;
        u.Trigger=!blocked&&(p.GameWidget.Input.IsMouseButtonDown(MouseButton.Left)||input.Dig.HasValue||input.Hit.HasValue);
        if(!blocked) {
            u.AimYaw-=input.Look.X*p.ComponentLocomotion.LookSpeed*dt;
            u.AimPitch=Math.Clamp(u.AimPitch+input.Look.Y*p.ComponentLocomotion.LookSpeed*dt*(SettingsManager.FlipVerticalAxis?-1:1),-.5f,.65f);
            if(p.GameWidget.Input.IsKeyDownOnce(Key.V))p.GameWidget.ActiveCamera=p.GameWidget.ActiveCamera is FppCamera?p.GameWidget.FindCamera<TppCamera>():p.GameWidget.FindCamera<FppCamera>();
        }
        ref var v=ref p.ComponentInput.m_playerInput;v.Move=Vector3.Zero;v.Look=Vector2.Zero;v.Dig=v.Hit=v.Aim=v.Interact=null;v.ToggleMount=false;v.ToggleCreativeFly=false;v.Jump=false;v.ToggleCrouch=false;v.SwitchCameraMode=false;
    }
}

[HarmonyPatch(typeof(ComponentPlayer),nameof(ComponentPlayer.Update))]
public static class MDInputPatch {public static void Prefix(ComponentPlayer __instance,float dt)=>MDPlayer.Input(__instance,dt);}

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
        int kind=Math.Clamp(Terrain.ExtractData(__instance.ActiveBlockValue),1,2);Vector3 size=kind==1?new(2.4f,4.8f,2.4f):new(1.6f,3.2f,1.6f);
        Vector3 at=hit.Value.HitPoint()+Vector3.UnitY*.06f;
        if(!MDPlayer.Clear(terrain,at,size)){MDPlayer.Message(__instance.ComponentPlayer,"空间不足：机甲需要约 4×4 格空地、5 格净高。");return false;}
        var bodies=new DynamicArray<ComponentBody>();__instance.Project.FindSubsystem<SubsystemBodies>(true).FindBodiesAroundPoint(new(at.X,at.Z),4,bodies);
        var box=new BoundingBox(at-new Vector3(size.X/2,0,size.Z/2),at+new Vector3(size.X/2,size.Y,size.Z/2));
        if(bodies.Any(b=>box.Intersection(b.BoundingBox))){MDPlayer.Message(__instance.ComponentPlayer,"部署区域有生物，请稍微退后。");return false;}
        var unitEntity=DatabaseManager.CreateEntity(__instance.Project,kind==1?"MDMech":"MDEnemy",true);var body=unitEntity.FindComponent<ComponentBody>(true);body.Position=at;body.Rotation=__instance.ComponentCreature.ComponentBody.Rotation;
        var unit=unitEntity.FindComponent<ComponentMDUnit>();unit.Yaw=unit.AimYaw=MathF.Atan2(-body.Matrix.Forward.X,-body.Matrix.Forward.Z);
        unitEntity.FindComponent<ComponentSpawn>(true).SpawnDuration=0;__instance.Project.AddEntity(unitEntity);__instance.RemoveActiveTool(1);return false;
    }
}
[HarmonyPatch(typeof(ComponentRider),nameof(ComponentRider.StartMounting))]
public static class MDBoardGuard {public static bool Prefix(ComponentRider __instance,ComponentMount componentMount)=>componentMount.Entity.FindComponent<ComponentMDUnit>() is not {} u||(__instance.ComponentCreature is ComponentPlayer p&&MDPlayer.CanBoard(p,u));}
[HarmonyPatch(typeof(ComponentRider),nameof(ComponentRider.StartDismounting))]
public static class MDExitGuard {public static bool Prefix(ComponentRider __instance){if(__instance.ComponentCreature is ComponentPlayer p&&MDPlayer.Mounted(p) is {} u){MDPlayer.Exit(p,u);return false;}return true;}}
[HarmonyPatch(typeof(FppCamera),nameof(FppCamera.Update))]
public static class MDFpp {public static void Postfix(FppCamera __instance){if(MDPlayer.Mounted(__instance.GameWidget?.PlayerData?.ComponentPlayer) is {} u)__instance.SetupPerspectiveCamera(u.Body.Position+Vector3.UnitY*3.9f+u.Direction*.5f,u.Direction,Vector3.UnitY);}}
[HarmonyPatch(typeof(TppCamera),nameof(TppCamera.Update))]
public static class MDTpp {public static void Postfix(TppCamera __instance){if(MDPlayer.Mounted(__instance.GameWidget?.PlayerData?.ComponentPlayer) is not {} u)return;var focus=u.Body.Position+Vector3.UnitY*3.3f;var end=focus-u.Direction*9+Vector3.UnitY*2;var hit=u.Project.FindSubsystem<SubsystemTerrain>(true).Raycast(focus,end,false,true,(v,d)=>BlocksManager.Blocks[Terrain.ExtractContents(v)].IsCollidable_(v));var pos=hit.HasValue?focus+Vector3.Normalize(end-focus)*Math.Max(.3f,hit.Value.Distance-.3f):end;__instance.SetupPerspectiveCamera(pos,Vector3.Normalize(focus+u.Direction*20-pos),Vector3.UnitY);}}
[HarmonyPatch(typeof(ComponentModel),nameof(ComponentModel.CalculateIsVisible))]
public static class MDHideRider {public static void Postfix(ComponentModel __instance){if(MDPlayer.Mounted(__instance.Entity.FindComponent<ComponentPlayer>())!=null)__instance.IsVisibleForCamera=false;}}
[HarmonyPatch(typeof(ComponentFirstPersonModel),nameof(ComponentFirstPersonModel.Draw))]
public static class MDHideHands {public static bool Prefix(ComponentFirstPersonModel __instance)=>MDPlayer.Mounted(__instance.m_componentPlayer)==null;}
