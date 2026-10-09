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
    public string type;              // bolt shell snipe beam chain flame jet pulse heal
    public float range, minRange, interval, power, splash, speed = 40, charge, duration;
    public int enhancedEvery; public float enhancedPower = 1, enhancedSplash;
    public int bounces; public float bounceRange = 5;
    public float slow, slowTime, stun, armorDown, armorTime, knockback, burn, zone, zoneTime, zonePower;
    public bool needsSight = true, threatFirst, directional, highArc;   // highArc: mortar lob instead of the flat solution
    public float width;              // directional rectangle width (flame/jet)
}
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
    public MDTowerAttack attack; public MDTowerFx fx = new();
    public string[] fireSounds = [], hitSounds = [], chargeSounds = [];
    // resolved at load
    [System.Text.Json.Serialization.JsonIgnore] public int[] parents;
    [System.Text.Json.Serialization.JsonIgnore] public int yawNode = -1, pitchNode = -1;
    [System.Text.Json.Serialization.JsonIgnore] public int[] recoilNodes = [];
    [System.Text.Json.Serialization.JsonIgnore] public int[] muzzleNodes = [];
    [System.Text.Json.Serialization.JsonIgnore] public Texture2D Icon;
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
        }
        all=file.towers;byId=all.ToDictionary(t=>t.id);
    }
    public static Texture2D Icon(MDTowerDef d)=>d.Icon??=ContentManager.Get<Texture2D>("Towers/"+d.id+"/icon");
    static Vector3 V(float[] a)=>new(a[0],a[1],a[2]);
    // Same composition as ComponentModel.ProcessBoneHierarchy for a rigid glTF: local rotation, then the
    // node's bind translation plus an offset (recoil) expressed in the parent frame.
    public static Matrix[] Pose(MDTowerDef d,Matrix body,float yaw,float pitch,float[] recoil) {
        var world=new Matrix[d.nodes.Length];
        for(int i=0;i<world.Length;i++) {
            Matrix local;
            if(i==0)local=body;
            else {
                var r=i==d.yawNode?Matrix.CreateRotationY(yaw):i==d.pitchNode?Matrix.CreateRotationX(pitch):Matrix.Identity;
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
    ComponentMDUnit unit; int root=-1,yaw=-1,pitch=-1; int[] recoil;
    public override void Load(ValuesDictionary values,IdToEntityMap map){base.Load(values,map);unit=Entity.FindComponent<ComponentMDUnit>(true);}
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
        }
        SetBoneTransform(root,m_componentFrame.Matrix);
        if(unit.Tower==null)return;
        if(yaw>=0)SetBoneTransform(yaw,Matrix.CreateRotationY(unit.AimYaw-unit.Yaw));
        if(pitch>=0)SetBoneTransform(pitch,Matrix.CreateRotationX(unit.TowerPitch));
        for(int k=0;k<recoil.Length;k++)if(recoil[k]>=0)SetBoneTransform(recoil[k],Matrix.CreateTranslation(0,0,unit.RecoilOffset(k)));
    }
}
