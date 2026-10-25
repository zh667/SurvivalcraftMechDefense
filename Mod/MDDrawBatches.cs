using Engine;
using Engine.Graphics;
using System.Runtime.CompilerServices;

namespace Game;

// Phones (user, 2026-10-25, Honor WIN: "机甲会隐身但是能正常玩 炮台在特定视角也会隐身"). The engine draws all parts of a model in
// one call, one matrix per bone, at most SubsystemModelsRenderer.MaxInstancesCount = (GL_MAX_VERTEX_UNIFORM_VECTORS - 16) / 8
// of them: 64 on PCs, 30 on GPUs that report the GLES 3 minimum of 256 vectors (Snapdragon's Adreno does). The three mechs have
// 38-40 bones and the devil mech 33, so ModelShader.InstancesCount throws "Invalid instances count.", SubsystemDrawing swallows
// the exception, and every model after the mech in that frame's list is skipped too: towers vanish in some views only, because
// the list order changes with what is in view (SubsystemModelsRenderer sorts by PrepareOrder only, 0 for all).
// A model of this mod with more bones than the limit is drawn here in batches of at most the limit, with the engine's own shader,
// light and material steps (as in SubsystemModelsRenderer.DrawInstancedModels); every other model goes the engine's way.
public static class MDDrawBatches {
    public static bool Enabled=true;   // the lab switches it off to show the failure
    public static int Batched;         // models drawn in batches so far (lab, the mech's log line)
    sealed class Batch { public int[] bones=[]; public readonly List<(int material,InstancedModelData data)> parts=[]; }
    static readonly ConditionalWeakTable<Model,Dictionary<int,Batch[]>> cache=new();
    static readonly HashSet<string> logged=[];
    static string lastError;

    // OnModelDataDrawing (MDModLoader): true when drawn here (the engine then skips it)
    public static bool Draw(SubsystemModelsRenderer.ModelData md,ModelShader shader,SubsystemModelsRenderer renderer) {
        var cm=md?.ComponentModel;var model=cm?.Model;var abs=cm?.AbsoluteBoneTransformsForCamera;
        if(!Enabled||model==null||abs==null||shader==null)return false;
        int limit=shader.Transforms.MaxWorldMatrices;if(abs.Length<=limit)return false;
        if(cm.Entity.FindComponent<ComponentMDUnit>()==null&&cm.Entity.FindComponent<ComponentMDRobot>()==null)return false;
        try {
            var batches=Batches(model,cm.MeshDrawOrders??[],limit);
            shader.EmissionColor=cm.EmissionColor??Vector4.Zero;
            shader.AmbientLightColor=new Vector3(LightingManager.LightAmbient*md.Light);
            shader.DiffuseLightColor1=new Vector3(md.Light);shader.DiffuseLightColor2=new Vector3(md.Light);
            foreach(var b in batches) {
                shader.InstancesCount=b.bones.Length;
                for(int i=0;i<b.bones.Length;i++)shader.Transforms.World[i]=abs[b.bones[i]];
                foreach(var (key,data) in b.parts) {
                    var material=key>=0?model.GetMaterial(key):null;
                    var colour=cm.DiffuseColor.HasValue?new Vector4(cm.DiffuseColor.Value,1):material?.BaseColorFactor??model?.GetDefaultBaseColorFactor()??Vector4.One;
                    float opacity=cm.Opacity??colour.W;shader.MaterialColor=new Vector4(new Vector3(colour.X,colour.Y,colour.Z)*opacity,opacity);
                    if(cm.TextureOverride!=null){shader.Texture=cm.TextureOverride;shader.SamplerState=SamplerState.PointClamp;}
                    else {
                        int t=material?.BaseColorTexture?.TextureIndex??-1;
                        if(t>=0){var tex=model.GetTexture(t);shader.Texture=tex;shader.SamplerState=tex.SamplerState;}
                        else{shader.Texture=Model.DefaultWhiteTexture;shader.SamplerState=SamplerState.PointClamp;}
                    }
                    Display.DrawIndexed(PrimitiveType.TriangleList,shader,data.VertexBuffer,data.IndexBuffer,0,data.IndexBuffer.IndicesCount);
                }
            }
            renderer.ModelsDrawn++;Batched++;
            string name=cm.Entity.ValuesDictionary?.DatabaseObject?.Name??"?";
            if(logged.Add(name+"/"+limit))Log.Information($"[MechDefense] {name}: {abs.Length} bones, the GPU draws at most {limit} at once: drawn in {batches.Length} batches");
            return true;
        } catch(Exception e) {
            if(lastError==null){lastError=e.Message;Log.Warning("[MechDefense] batched model drawing: "+e);}
            return false;
        }
    }

    // the mesh bones of the drawn meshes, `limit` per batch; per batch and material one buffer whose Instance is the slot in the batch
    static Batch[] Batches(Model model,int[] orders,int limit) {
        var hash=new HashCode();hash.Add(limit);foreach(int m in orders){hash.Add(m);hash.Add(model.Meshes[m].IsVisible);}int key=hash.ToHashCode();
        var byList=cache.GetValue(model,_=>[]);
        if(byList.TryGetValue(key,out var done))return done;
        var meshesOf=new SortedDictionary<int,List<ModelMesh>>();
        foreach(int m in orders) {
            var mesh=model.Meshes[m];if(!mesh.IsVisible)continue;
            if(!meshesOf.TryGetValue(mesh.ParentBone.Index,out var l))meshesOf[mesh.ParentBone.Index]=l=[];
            l.Add(mesh);
        }
        var bones=meshesOf.Keys.ToArray();var list=new List<Batch>();
        for(int start=0;start<bones.Length;start+=limit) {
            var b=new Batch{bones=bones.Skip(start).Take(limit).ToArray()};
            var byMaterial=new SortedDictionary<int,(DynamicArray<InstancedModelsManager.InstancedVertex> v,DynamicArray<int> i)>();
            for(int slot=0;slot<b.bones.Length;slot++)foreach(var mesh in meshesOf[b.bones[slot]])foreach(var part in mesh.MeshParts) {
                if(!byMaterial.TryGetValue(part.MaterialIndex,out var acc))byMaterial[part.MaterialIndex]=acc=(new(),new());
                Extract(part,slot,acc.v,acc.i);
            }
            foreach(var (material,acc) in byMaterial) {
                if(acc.v.Count==0||acc.i.Count==0)continue;
                var data=new InstancedModelData{VertexBuffer=new VertexBuffer(InstancedModelData.VertexDeclaration,acc.v.Count),IndexBuffer=new IndexBuffer(IndexFormat.ThirtyTwoBits,acc.i.Count)};
                data.VertexBuffer.SetData(acc.v.Array,0,acc.v.Count);data.IndexBuffer.SetData(acc.i.Array,0,acc.i.Count);
                b.parts.Add((material,data));
            }
            list.Add(b);
        }
        return byList[key]=list.ToArray();
    }
    // position, normal and the first texture coordinate of each indexed vertex, as InstancedModelsManager.ExtractVertices reads them
    static void Extract(ModelMeshPart part,int slot,DynamicArray<InstancedModelsManager.InstancedVertex> vertices,DynamicArray<int> indices) {
        var vb=part.VertexBuffer;var ib=part.IndexBuffer;if(vb==null||ib==null||vb.Tag is not byte[] raw)return;
        int pos=-1,nor=-1,uv=-1;string uvName=VertexElementSemantic.TextureCoordinate.GetSemanticString();
        foreach(var el in vb.VertexDeclaration.VertexElements) {
            if(el.Semantic==VertexElementSemantic.Position.GetSemanticString())pos=el.Offset;
            else if(el.Semantic==VertexElementSemantic.Normal.GetSemanticString())nor=el.Offset;
            else if(uv<0&&el.SemanticName==uvName)uv=el.Offset;
        }
        if(pos<0||nor<0||uv<0)return;
        var index=BlockMesh.GetIndexData<int>(ib);if(index==null)return;
        int stride=vb.VertexDeclaration.VertexStride,count=raw.Length/stride,need=Math.Max(pos+12,Math.Max(nor+12,uv+8));var seen=new Dictionary<int,int>();
        for(int i=part.StartIndex;i<part.StartIndex+part.IndicesCount&&i<index.Length;i++) {
            int v=index[i];if(v<0||v>=count)continue;
            if(!seen.TryGetValue(v,out int at)) {
                int o=v*stride;if(o+need>raw.Length)continue;
                seen[v]=at=vertices.Count;
                vertices.Add(new InstancedModelsManager.InstancedVertex{X=BitConverter.ToSingle(raw,o+pos),Y=BitConverter.ToSingle(raw,o+pos+4),Z=BitConverter.ToSingle(raw,o+pos+8),
                    Nx=BitConverter.ToSingle(raw,o+nor),Ny=BitConverter.ToSingle(raw,o+nor+4),Nz=BitConverter.ToSingle(raw,o+nor+8),Tx=BitConverter.ToSingle(raw,o+uv),Ty=BitConverter.ToSingle(raw,o+uv+4),Instance=slot});
            }
            indices.Add(at);
        }
    }
}
