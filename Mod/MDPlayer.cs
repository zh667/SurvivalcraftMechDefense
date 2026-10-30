using Engine;
using Engine.Graphics;
using HarmonyLib;

namespace Game;

public class MDModLoader : ModLoader {
    public override void __ModInitialize() {
        new Harmony("local.mechdefense.sample").PatchAll();
        foreach(var hook in new[]{"OnLoadingFinished","BeforeMinerPlace","ProcessAttackment","OnPlayerInputInteract","OnModelDataDrawing","ProjectXmlLoad","ProjectXmlSave","CalculateCreatureInjuryAmount"})ModsManager.RegisterHook(hook,this);
    }
    // Models with more bones than the GPU draws at once (the mechs on Snapdragon phones) are drawn in batches (MDDrawBatches).
    public override void OnModelDataDrawing(SubsystemModelsRenderer.ModelData modelData,ModelShader modelShader,Camera camera,SubsystemModelsRenderer subsystemModelsRenderer,out bool skip)=>
        skip=MDDrawBatches.Draw(modelData,modelShader,subsystemModelsRenderer);
    // A boss takes at most MDBoss.MaxHitShare of its armour per hit.
    public override void CalculateCreatureInjuryAmount(Injury injury){MDBoss.CapInjury(injury);MDTowerWorld.Injured(injury);}
    // The world's count of destroyed robots towards the next boss and the rotation's next one (MDBossKills) are attributes of Project.xml.
    public override void ProjectXmlLoad(System.Xml.Linq.XElement xElement,WorldInfo worldInfo,ContainerWidget gameWidget){MDBossKills.Load(xElement);MDRewards.Load(xElement);}
    public override void ProjectXmlSave(System.Xml.Linq.XElement xElement){MDBossKills.Save(xElement);MDRewards.Save(xElement);}
    public override void OnLoadingFinished(List<Action> actions){actions.Add(MDNeorxnaCompatibility.Install);actions.Add(MDSettingsScreen.EnsureScreens);}
    // The mod's settings (MDSettings) live in the game's ModSettings.xml, under this mod's own element; the engine calls these
    // directly (load: while loading, when the file has this mod; save: whenever it writes the file).
    public override void LoadSettings(System.Xml.Linq.XElement xElement)=>MDSettings.Load(xElement);
    public override void SaveSettings(System.Xml.Linq.XElement xElement)=>MDSettings.Save(xElement);
    public override void BeforeMinerPlace(ComponentMiner miner,TerrainRaycastResult hit,int x,int y,int z,BlockPlacementData data,out bool placementNotAllowed) {
        placementNotAllowed=MDTowerPlacement.IsReserved(miner.Project.FindSubsystem<SubsystemBodies>(true),x,y,z);
        if(placementNotAllowed)MDPlayer.Message(miner.ComponentPlayer,"此格属于炮塔固定占地区域。");
    }
    // Creatures that hurt a player, a tower or the player's herd become tower targets (MDFaction).
    // A boss's own factor and armour phase scale the hit first (MDBoss).
    // A robot remembers a player (or the player's herd) that hits it and fights back (MDRobots.Provoked).
    public override void ProcessAttackment(Attackment attackment){MDRobots.Unstaggered(attackment);MDRobots.Armoured(attackment);MDBoss.Modify(attackment);MDMechArmour.Modify(attackment);MDFaction.Attacked(attackment);MDRobots.Provoked(attackment);MDRewards.Track(attackment);}
    // Right-click on a tower with robot scrap or an alloy plate (on a mech: iron or copper) repairs it (MDTowerMaintenance); the
    // engine's own use/place is skipped then.
    public override void OnPlayerInputInteract(ComponentPlayer componentPlayer,ref bool playerOperated,ref double timeIntervalLastActionTime,ref int priorityUse,ref int priorityInteract,ref int priorityPlace) {
        if(!playerOperated&&(MDKits.TryInstall(componentPlayer)||MDTowerMaintenance.TryRepair(componentPlayer)))playerOperated=true;
    }
}

public class MDDeploymentBlock : Block {
    // Data: the tower's data value in the low 8 bits (0/3/4 keep the first three gun towers of older saves; 5+ the other
    // towers); bits 8-14 the wear of a dismantled tower in percent, 0 for full health, so crafted, creative and older items
    // are unchanged and stack. 1 is Mercury, the mech a player boards (crafting table or creative inventory, same wear bits when taken down; since 0.8.0, the data value
    // of the 0.5.0 sample mech, so such items deploy Mercury). 2 was the Destroy Gunners enemy (removed in 0.6.0): such items
    // stay in old inventories under a placeholder name and cannot be deployed.
    const string RemovedName="已移除的旧单位";public const string RemovedNote="这个单位已在 0.6.0 移除，不能再部署。";

    // Mercury's weapon kit level (0-3) in bits 15-16 (since 0.9.0; older items have 0).
    public static int Kit(int value)=>(Terrain.ExtractData(value)>>15)&3;
    static string KitText(int v)=>Kit(v)>0?$"（改装 Mk{Kit(v)}）":"";
    // Mercury and, since 0.9.0 round 5, the hero mechs 复仇女神 (data 30) and 轩辕十四 (data 31): MDMechType
    public static MDMechType MechType(int value)=>MDMechType.ByData(Terrain.ExtractData(value)&0xFF);
    public static bool IsMech(int value)=>MechType(value)!=null;
    static string MechName(MDMechType t)=>t.Name+"机甲 · 部署";
    static string MechNote(MDMechType t)=>$"可驾驶的机甲「{t.Name}」：装甲 {t.Armour}，武器伤害 ×{t.Damage:0.##}，能量 {t.EnergyMax:0}"+
        (t.BurstRifle?"，步枪三连发，抗爆炸":"")+(t.Speed>1?"，机动最好":"")+$"。右键地面部署，需要约 {MathF.Ceiling(t.Box.X):0}×{MathF.Ceiling(t.Box.Z):0} 格空地、{MathF.Ceiling(t.Box.Y):0} 格净高。\n"+
        $"靠近按 {MDMechKeys.Hint(MDMechAction.Board)} 上下车：鼠标转向瞄准，WASD 移动，按住 {MDMechKeys.Hint(MDMechAction.Hover)} 悬浮（耗能量），{MDMechKeys.Hint(MDMechAction.Jump)} 跳，{MDMechKeys.Hint(MDMechAction.Fire)} 开火，{MDMechKeys.Hint(MDMechAction.Melee)} 近战（有目标时冲过去打），{MDMechKeys.Hint(MDMechAction.View)} 切换视角。"+
        $"{MDMechKeys.Hint(MDMechAction.Switch)} 切换武器：步枪、导弹（按住开火扫过目标锁定，最多 {t.Locks} 个，松开发射追踪导弹）、火炮（开火时展开，按住连发）、激光、榴弹。\n"+
        $"维修：手持铁锭、铜锭（或铁块、铜块）右键机甲。收起：下车后用镐敲 {MDTowerMaintenance.HitsToDismantle} 下，收起的机甲保留耐久和改装。";
    public override void Initialize(){DefaultDisplayName="防御塔 · 部署";DefaultDescription="防御塔在完整平整的支撑面上对齐方格固定部署，自动攻击敌对生物。";DefaultCategory="Weapons";IsPlaceable=false;DefaultIsUseable=true;IsAimable=false;MaxStacking=20;CraftingId="mddeploy";base.Initialize();}
    public static MDTowerDef Tower(int value)=>MDTowerLibrary.ByData(Terrain.ExtractData(value)&0xFF);
    public static int Wear(int value)=>(Terrain.ExtractData(value)>>8)&0x7F;
    public static float Hitpoints(int value)=>1-Wear(value)/100f;
    public static int Value(MDTowerDef d,float hitpoints)=>Terrain.MakeBlockValue(BlocksManager.GetBlockIndex<MDDeploymentBlock>(),0,d.data|Math.Clamp((int)MathF.Round((1-hitpoints)*100),0,99)<<8);
    public static int MechValue(float hitpoints,int kit=0,MDMechType type=null)=>Terrain.MakeBlockValue(BlocksManager.GetBlockIndex<MDDeploymentBlock>(),0,(type??MDMechType.Mercury).Data|Math.Clamp((int)MathF.Round((1-hitpoints)*100),0,99)<<8|Math.Clamp(kit,0,3)<<15);
    public override string GetDisplayName(SubsystemTerrain t,int v)=>Tower(v) is {} d?d.name+" · 部署"+(Wear(v)>0?$"（耐久 {100-Wear(v)}%）":""):MechType(v) is {} mt?MechName(mt)+KitText(v)+(Wear(v)>0?$"（耐久 {100-Wear(v)}%）":""):RemovedName;
    public override string GetDescription(int v)=>Tower(v) is {} d?$"{d.summary}\n{MDEncyclopedia.ElementText(d)}占地：{MDTowerPlacement.SizeText(d)}。右键选择的方块是占地西南角。\n"+
        $"维修：手持机兵残骸或合金板右键炮塔（铁、铜只能修机甲）。拆卸：用镐敲 {MDTowerMaintenance.HitsToDismantle} 下，拆下的炮塔保留耐久。"+(Wear(v)>0?"\n受损的炮塔要部署修好后才能用作升级材料。":""):MechType(v) is {} mt?MechNote(mt):RemovedNote;
    public override float GetBlockHealth(int value)=>Wear(value)>0?Hitpoints(value):-1;   // the slot's health bar for a worn tower only
    public override IEnumerable<int> GetCreativeValues() {
        foreach(var d in MDTowerLibrary.All)yield return Terrain.MakeBlockValue(BlockIndex,0,d.data);
        foreach(var t in MDMechType.All)yield return Terrain.MakeBlockValue(BlockIndex,0,t.Data);   // Mercury, 复仇女神, 轩辕十四
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
    // In a slot a tower is its pre-rendered picture; in the hand its full model; elsewhere (dropped) and for the mechs a small
    // 3D icon mesh (the largest ~1500 triangles, the tower's own atlas) instead of the full model.
    public override void DrawBlock(PrimitivesRenderer3D r,int value,Color color,float size,ref Matrix matrix,DrawBlockEnvironmentData e) {
        if(e?.DrawBlockMode is DrawBlockMode.FirstPerson or DrawBlockMode.ThirdPerson&&Tower(value) is {} held&&MDHeldModel.Draw(held,color,size,matrix,e))return;
        var d=Tower(value);
        if(d==null&&!IsMech(value)){d=MDTowerLibrary.All[0];color*=.35f;}   // the removed enemy (data 2): the first tower's icon, dimmed
        if(e?.DrawBlockMode==DrawBlockMode.UI&&d!=null&&MDTowerLibrary.Picture(d) is {} picture){MDTowerLibrary.DrawPicture(r,picture,color,size,matrix,e);return;}
        var icon=d!=null?MDTowerLibrary.Icon(d):(MechType(value)??MDMechType.Mercury).Icon;
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
        if(MDPartBlock.Of(__instance.ActiveBlockValue)==MDPartBlock.Kind.Beacon){__result=true;MDBoss.PlaceBeacon(__instance,ray);return false;}   // the devil mech's beacon
        if(Terrain.ExtractContents(__instance.ActiveBlockValue)==BlocksManager.GetBlockIndex<MDSummonBlock>()){__result=true;MDSummonBlock.Place(__instance,ray);return false;}   // creative robot summons
        if(Terrain.ExtractContents(__instance.ActiveBlockValue)!=BlocksManager.GetBlockIndex<MDDeploymentBlock>())return true;
        __result=true;var hit=__instance.Raycast<TerrainRaycastResult>(ray,RaycastMode.Digging);if(!hit.HasValue)return false;
        var terrain=__instance.Project.FindSubsystem<SubsystemTerrain>(true);
        if(MDDeploymentBlock.Tower(__instance.ActiveBlockValue) is {} tower) {
            var subsystemBodies=__instance.Project.FindSubsystem<SubsystemBodies>(true);
            if(!MDTowerPlacement.Find(terrain,subsystemBodies,hit.Value,tower,out var pos,out var reason)){MDPlayer.Message(__instance.ComponentPlayer,reason);return false;}
            MDTowerPlacement.ClearPlants(terrain,pos,tower);
            var entity=DatabaseManager.CreateEntity(__instance.Project,tower.template,true);
            var fixedBody=entity.FindComponent<ComponentMDTowerBody>(true);var rot=MDTowerPlacement.SnapRotation(__instance.ComponentCreature.ComponentBody.Rotation);
            fixedBody.FixAt(pos,rot);var state=entity.FindComponent<ComponentMDUnit>();state.Yaw=state.AimYaw=MathF.Atan2(-fixedBody.Matrix.Forward.X,-fixedBody.Matrix.Forward.Z);
            if(MDDeploymentBlock.Wear(__instance.ActiveBlockValue)>0)state.SetHitpoints(MDDeploymentBlock.Hitpoints(__instance.ActiveBlockValue));   // a dismantled tower keeps its health
            entity.FindComponent<ComponentSpawn>(true).SpawnDuration=0;__instance.Project.AddEntity(entity);__instance.RemoveActiveTool(1);return false;
        }
        if(hit.Value.CellFace.Face!=4&&MDDeploymentBlock.MechType(__instance.ActiveBlockValue)!=null)return false;   // a mech: top faces only, as before
        if(MDDeploymentBlock.MechType(__instance.ActiveBlockValue) is {} mechType) {   // the mech stands on the hit point, facing like the player
            var size=mechType.Box;var at=hit.Value.HitPoint()+Vector3.UnitY*.06f;
            if(!MDMechPilot.Clear(terrain,at,size)){MDPlayer.Message(__instance.ComponentPlayer,$"空间不足：{mechType.Name}需要约 {MathF.Ceiling(size.X):0}×{MathF.Ceiling(size.Z):0} 格空地、{MathF.Ceiling(size.Y):0} 格净高。");return false;}
            var bodies=new DynamicArray<ComponentBody>();__instance.Project.FindSubsystem<SubsystemBodies>(true).FindBodiesAroundPoint(new(at.X,at.Z),4,bodies);
            var box=new BoundingBox(at-new Vector3(size.X/2,0,size.Z/2),at+new Vector3(size.X/2,size.Y,size.Z/2));
            if(bodies.Any(b=>box.Intersection(b.BoundingBox))){MDPlayer.Message(__instance.ComponentPlayer,"部署区域有生物，请稍微退后。");return false;}
            var mech=DatabaseManager.CreateEntity(__instance.Project,mechType.Template,true);var body=mech.FindComponent<ComponentBody>(true);body.Position=at;
            var facing=__instance.ComponentCreature.ComponentBody.Matrix.Forward;float yaw=MathF.Atan2(-facing.X,-facing.Z);body.Rotation=Quaternion.CreateFromAxisAngle(Vector3.UnitY,yaw);
            var unit=mech.FindComponent<ComponentMDUnit>(true);unit.Yaw=unit.AimYaw=yaw;
            if(MDDeploymentBlock.Wear(__instance.ActiveBlockValue)>0)unit.SetHitpoints(MDDeploymentBlock.Hitpoints(__instance.ActiveBlockValue));   // taken down, it keeps its health
            unit.Mech.Kit=MDDeploymentBlock.Kit(__instance.ActiveBlockValue);   // and its weapon kit
            mech.FindComponent<ComponentSpawn>(true).SpawnDuration=0;__instance.Project.AddEntity(mech);__instance.RemoveActiveTool(1);return false;
        }
        MDPlayer.Message(__instance.ComponentPlayer,MDDeploymentBlock.RemovedNote);return false;   // data 2: the removed enemy
    }
}
