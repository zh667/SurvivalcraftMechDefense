using Engine;
using Engine.Graphics;
using GameEntitySystem;
using TemplatesDatabase;
using System.Text.Json;

namespace Game;

// Tower definitions come from Assets/Towers/towers.json (tools/build_tower_assets.py): authored cube
// geometry split into rigid parts, plus the gameplay table. Geometry is drawn by the game's own model
// renderer (one glTF per tower), so towers get world light, fog, culling and GPU instancing.
public sealed class MDTowerNode { public string name; public string parent; public float[] translation; }
public sealed class MDTowerMuzzle { public string name; public string part; public float[] local; }
public sealed class MDTowerAttack {
    public string type;              // bolt shell snipe beam chain flame jet pulse heal radar missile
    public float range, minRange, interval, power, splash, speed = 40, charge, duration;
    public int enhancedEvery; public float enhancedPower = 1, enhancedSplash;
    public int bounces; public float bounceRange = 5;
    public float slow, slowTime, stun, armorDown, armorTime, knockback, burn, zone, zoneTime, zonePower;
    public float freeze, freezeImmune = 4;   // frost: seconds frozen solid per hit, then immune to freezing for freezeImmune seconds
    public float dot, dotTime;               // poison cloud: damage per second, still ticking dotTime seconds after leaving the cloud
    public float bossResilience = 1e9f;      // stun and freeze skip targets whose AttackResilience is at least this (bosses)
    public float pushMaxMass = 1e9f;         // knockback moves only bodies up to this mass (small and medium enemies)
    public bool needsSight = true, threatFirst, directional, highArc;   // highArc: mortar lob instead of the flat solution
    public int soundEvery = 1;      // fast guns: the fire sound plays on every n-th shot only
    public bool airFirst; public float groundFactor = 1;   // anti-air: flying targets first, ground targets take groundFactor of the damage
    public float boost = 1; public int links = 3;          // radar: range x boost for up to `links` towers within range
    public float width;              // directional rectangle width (flame/jet)
}
// A piston or strut between the turret and the elevating cradle (rest-pose (y, z) points in the turret frame).
public sealed class MDTowerLink { public string node, parent, on; public float[] hinge, at; public bool stretch; }
public sealed class MDTowerFx {
    public float[] color = [1, .8f, .3f];   // main hue of flashes and bolts
    public float[] color2 = [1, .5f, .1f];  // secondary (trails, impact)
    public float scale = 1;
    public string kind = "heavy";           // explosion look: fire acid poison frost heavy
}
public sealed class MDTowerDef {
    public string id, name, template, summary; public int data;
    public MDTowerNode[] nodes; public MDTowerMuzzle[] muzzles;
    public float[][] bounds; public float radius; public int[] pitch = [-30, 60]; public bool pitchFixed;
    public int[] cells = [2, 4, 2]; public float hp = 800;
    public float yawSpeed = 150, pitchSpeed = 90, restPitch;
    public string[] recoilParts = []; public float[] recoil = [];
    public string[] spinParts = []; public float spinSpeed = 1440;   // rotary barrel clusters turning about their own Z while a target is held (deg/s at full spin)
    public string spinAxis = "z"; public bool spinIdle;               // "y": a dish / rotor turning about the vertical; spinIdle: always turning
    public MDTowerAttack attack; public MDTowerFx fx = new(); public MDTowerLink[] links = [];
    public string[] fireSounds = [], hitSounds = [], chargeSounds = [];
    // resolved at load
    [System.Text.Json.Serialization.JsonIgnore] public int[] parents;
    [System.Text.Json.Serialization.JsonIgnore] public int yawNode = -1, pitchNode = -1;
    [System.Text.Json.Serialization.JsonIgnore] public int[] recoilNodes = [];
    [System.Text.Json.Serialization.JsonIgnore] public int[] muzzleNodes = [];
    [System.Text.Json.Serialization.JsonIgnore] public int[] linkNodes = [];
    public bool HasYaw => yawNode >= 0;
    public float PitchMin => pitchFixed ? restPitch : MathUtils.DegToRad(pitch[0]);
    public float PitchMax => pitchFixed ? restPitch : MathUtils.DegToRad(pitch[1]);
}

public static class MDTowerLibrary {
    sealed class File { public MDTowerDef[] towers; }
    static Dictionary<string,MDTowerDef> byId; static MDTowerDef[] all;
    public static IReadOnlyList<MDTowerDef> All{get{Load();return all;}}
    public static MDTowerDef Get(string id){Load();return byId.TryGetValue(id??"",out var d)?d:null;}
    public static MDTowerDef ByData(int data){Load();return all.FirstOrDefault(t=>t.data==data);}
    static void Load() {
        if(all!=null)return;
        var json=ContentManager.Get<string>("Towers/towers",".json");
        var file=JsonSerializer.Deserialize<File>(json,new JsonSerializerOptions{IncludeFields=true});
        foreach(var d in file.towers) {
            d.parents=d.nodes.Select(n=>Array.FindIndex(d.nodes,m=>m.name==n.parent)).ToArray();
            d.yawNode=Array.FindIndex(d.nodes,n=>n.name=="yaw");d.pitchNode=Array.FindIndex(d.nodes,n=>n.name=="pitch");
            d.recoilNodes=d.recoilParts.Select(p=>Array.FindIndex(d.nodes,n=>n.name==p)).ToArray();
            d.muzzleNodes=d.muzzles.Select(m=>Array.FindIndex(d.nodes,n=>n.name==m.part)).ToArray();
            d.linkNodes=d.links.Select(l=>Array.FindIndex(d.nodes,n=>n.name==l.node)).ToArray();
        }
        all=file.towers;byId=all.ToDictionary(t=>t.id);
    }
    // 3D inventory icon: ~1500 largest triangles of the tower, unit-cube sized, with the tower's atlas.
    // fit/center frame the slot view (BlockIconWidget looks from (1,1,1)): tall thin towers would otherwise
    // fill barely half the slot height.
    public record struct IconData(BlockMesh mesh,Texture2D texture,float fit,Vector3 center);
    static readonly Dictionary<string,IconData> icons=[];
    const float IconExtent=1.65f;   // projected size that fills ~80% of an inventory slot (measured in the hotbar)
    public static IconData Icon(MDTowerDef d)=>IconMesh("Towers/"+d.id+"/icon.bin","Towers/"+d.id+"/atlas");
    public static IconData IconMesh(string meshPath,string texturePath) {
        if(icons.TryGetValue(meshPath,out var icon))return icon;
        var stream=ContentManager.GetStream(meshPath);stream.Position=0;using var r=new BinaryReader(stream);
        int nv=r.ReadInt32(),ni=r.ReadInt32();var mesh=new BlockMesh();
        var sx=Vector3.Normalize(new Vector3(1,0,-1));var sy=Vector3.Normalize(new Vector3(-1,2,-1));
        float x0=float.MaxValue,x1=float.MinValue,y0=float.MaxValue,y1=float.MinValue;
        for(int i=0;i<nv;i++) {
            var p=new Vector3(r.ReadSingle(),r.ReadSingle(),r.ReadSingle());var uv=new Vector2(r.ReadSingle(),r.ReadSingle());byte shade=(byte)(255*Math.Clamp(r.ReadSingle(),0,1));
            mesh.Vertices.Add(new BlockMeshVertex{Position=p,TextureCoordinates=uv,Color=new Color(shade,shade,shade)});
            float a=Vector3.Dot(p,sx),b=Vector3.Dot(p,sy);x0=MathF.Min(x0,a);x1=MathF.Max(x1,a);y0=MathF.Min(y0,b);y1=MathF.Max(y1,b);
        }
        for(int i=0;i<ni;i++)mesh.Indices.Add(r.ReadInt32());
        icon=new IconData(mesh,ContentManager.Get<Texture2D>(texturePath),IconExtent/MathF.Max(x1-x0,y1-y0),sx*(x0+x1)/2+sy*(y0+y1)/2);
        icons[meshPath]=icon;return icon;
    }
    // Slot picture (0.9.0 round 5): the whole tower pre-rendered from the slot's view by tools/render_tower_icons.py, framed
    // like the icon mesh (IconExtent). The icon mesh keeps only the largest triangles, so in a slot whole pieces were
    // missing (the medic tower's dome). Premultiplied and mipmapped on load: the 256 px picture shrinks to a ~60 px slot.
    static readonly Dictionary<string,Texture2D> pictures=[];
    public static Texture2D Picture(MDTowerDef d) {
        if(pictures.TryGetValue(d.id,out var t))return t;
        try{var s=ContentManager.GetStream("Towers/"+d.id+"/icon.png");s.Position=0;t=Texture2D.Load(s,true,5);}
        catch(Exception x){Log.Warning($"[MechDefense] slot picture of {d.id}: {x.Message}");t=null;}   // falls back to the icon mesh
        pictures[d.id]=t;return t;
    }
    // One quad facing the slot camera (BlockIconWidget looks from (1,1,1); screen axes as in IconMesh) that fills the slot
    // view: CreateOrthographic(3.6) is 1.8 units across at size 1.
    public static void DrawPicture(PrimitivesRenderer3D r,Texture2D picture,Color color,float size,Matrix matrix,DrawBlockEnvironmentData e) {
        var m=e?.ViewProjectionMatrix is {} vp?matrix*vp:matrix;float h=.9f*size;
        var sx=Vector3.Normalize(new Vector3(1,0,-1))*h;var sy=Vector3.Normalize(new Vector3(-1,2,-1))*h;
        Vector3 P(Vector3 p){var v=new Vector4(p,1);Vector4.Transform(ref v,ref m,out v);return new Vector3(v.X,v.Y,v.Z)/v.W;}
        var c=Color.MultiplyColorOnly(color,LightingManager.LightIntensityByLightValue[Math.Clamp(e?.Light??15,0,15)]);
        r.TexturedBatch(picture,false,0,DepthStencilState.None,RasterizerState.CullNoneScissor,BlendState.AlphaBlend,SamplerState.LinearClamp)
            .QueueQuad(P(-sx-sy),P(-sx+sy),P(sx+sy),P(sx-sy),new Vector2(0,1),new Vector2(0,0),new Vector2(1,0),new Vector2(1,1),c);
    }
    static Vector3 V(float[] a)=>new(a[0],a[1],a[2]);
    // Local transform of link k at cradle pitch: it turns about X at its hinge to keep pointing at its partner point,
    // so a piston's cylinder and rod stay on one axis (the rod slides in the cylinder); a stretching strut also
    // scales along its axis to keep reaching its hinge.
    public static Matrix LinkLocal(MDTowerDef d,int k,float pitch) {
        var l=d.links[k];var pv=d.nodes[d.pitchNode].translation;float c=MathF.Cos(pitch),s=MathF.Sin(pitch);
        Vector2 At(float[] p,bool cradle){if(!cradle)return new(p[0],p[1]);float y=p[0]-pv[1],z=p[1]-pv[2];return new(pv[1]+y*c-z*s,pv[2]+y*s+z*c);}
        var rest=new Vector2(l.at[0]-l.hinge[0],l.at[1]-l.hinge[1]);var now=At(l.at,l.on=="pitch")-At(l.hinge,l.parent=="pitch");
        var m=Matrix.CreateRotationX(MathF.Atan2(now.Y,now.X)-MathF.Atan2(rest.Y,rest.X)-(l.parent=="pitch"?pitch:0));
        if(!l.stretch)return m;
        float f=now.Length()/MathF.Max(1e-4f,rest.Length())-1;var u=Vector3.Normalize(new Vector3(0,rest.X,rest.Y));   // scale along u: I + f u u^T
        return new Matrix(1,0,0,0, 0,1+f*u.Y*u.Y,f*u.Y*u.Z,0, 0,f*u.Z*u.Y,1+f*u.Z*u.Z,0, 0,0,0,1)*m;
    }
    // Same composition as ComponentModel.ProcessBoneHierarchy for a rigid glTF: local rotation, then the
    // node's bind translation plus an offset (recoil) expressed in the parent frame.
    public static Matrix[] Pose(MDTowerDef d,Matrix body,float yaw,float pitch,float[] recoil) {
        var world=new Matrix[d.nodes.Length];
        for(int i=0;i<world.Length;i++) {
            Matrix local;
            if(i==0)local=body;
            else {
                int li=Array.IndexOf(d.linkNodes,i);
                var r=i==d.yawNode?Matrix.CreateRotationY(yaw):i==d.pitchNode?Matrix.CreateRotationX(pitch):li>=0?LinkLocal(d,li,pitch):Matrix.Identity;
                var t=V(d.nodes[i].translation);int k=Array.IndexOf(d.recoilNodes,i);if(k>=0&&recoil!=null)t+=new Vector3(0,0,recoil[k]);
                local=r*Matrix.CreateTranslation(t);
            }
            world[i]=i==0?local:local*world[d.parents[i]];
        }
        return world;
    }
    public static Vector3 Muzzle(MDTowerDef d,Matrix[] pose,int index) {
        index=Math.Clamp(index,0,d.muzzles.Length-1);return Vector3.Transform(V(d.muzzles[index].local),pose[d.muzzleNodes[index]]);
    }
    public static Vector3 MuzzleForward(MDTowerDef d,Matrix[] pose,int index)=>Vector3.Normalize(Vector3.TransformNormal(-Vector3.UnitZ,pose[d.muzzleNodes[Math.Clamp(index,0,d.muzzles.Length-1)]]));
}

// Rigid-part tower model drawn by SubsystemModelsRenderer; bones follow the unit's aim and recoil.
public class ComponentMDTowerModel : ComponentModel {
    ComponentMDUnit unit; int root=-1,yaw=-1,pitch=-1; int[] recoil,links,spin;
    public override void Load(ValuesDictionary values,IdToEntityMap map) {
        base.Load(values,map);unit=Entity.FindComponent<ComponentMDUnit>(true);
        // Two-sided like the authoring view: tube interiors and slab seams were only ever seen from both sides.
        RenderingMode=ModelRenderingMode.AlphaThreshold;
    }
    public static string LastError;
    public override void Animate() {
        try{AnimateBones();}
        catch(Exception e){if(LastError==null){LastError=unit?.TowerId+": "+e;Log.Error("[MechDefense] tower model: "+e);}SetBoneTransform(Model.RootBone.Index,m_componentFrame.Matrix);}
    }
    void AnimateBones() {
        if(recoil==null) {
            // Bones resolve after every component has loaded (the unit owns the tower definition).
            root=Model.RootBone.Index;yaw=Model.FindBone("yaw",false)?.Index??-1;pitch=Model.FindBone("pitch",false)?.Index??-1;
            recoil=(unit.Tower?.recoilParts??[]).Select(p=>Model.FindBone(p,false)?.Index??-1).ToArray();
            links=(unit.Tower?.links??[]).Select(l=>Model.FindBone(l.node,false)?.Index??-1).ToArray();
            spin=(unit.Tower?.spinParts??[]).Select(p=>Model.FindBone(p,false)?.Index??-1).ToArray();
        }
        SetBoneTransform(root,m_componentFrame.Matrix);
        if(unit.Tower==null)return;
        if(yaw>=0)SetBoneTransform(yaw,Matrix.CreateRotationY(unit.AimYaw-unit.Yaw));
        if(pitch>=0)SetBoneTransform(pitch,Matrix.CreateRotationX(unit.TowerPitch));
        for(int k=0;k<recoil.Length;k++)if(recoil[k]>=0)SetBoneTransform(recoil[k],Matrix.CreateTranslation(0,0,unit.RecoilOffset(k)));
        for(int k=0;k<links.Length;k++)if(links[k]>=0)SetBoneTransform(links[k],MDTowerLibrary.LinkLocal(unit.Tower,k,unit.TowerPitch));
        for(int k=0;k<spin.Length;k++)if(spin[k]>=0){float s=k%2==0?unit.Spin:-unit.Spin;SetBoneTransform(spin[k],unit.Tower.spinAxis=="y"?Matrix.CreateRotationY(s):Matrix.CreateRotationZ(s));}   // pairs turn opposite ways
    }
}
