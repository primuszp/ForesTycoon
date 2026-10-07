using System;
using System.Collections.Generic;
using System.IO;
using OpenTK.Mathematics;

namespace ForesTycoon
{
    internal enum ForestAsset { SpruceTall, SpruceSmall, SpruceMedium, OakSmall, OakMedium, OakLarge, BroadleafA, BroadleafB, Birch,
        GeneratedPineA,GeneratedPineB,GeneratedOakA,GeneratedOakB }

    /// <summary>Rigid, grounded single-tree extraction; source files stay untouched.</summary>
    internal sealed class ImportedTreeModel : IDisposable
    {
        internal readonly AnimatedGlbModel Model;
        internal readonly int TriangleCount;
        internal readonly Matrix4 Normalization;
        private readonly Vector3 sourceMin,sourceMax;
        private readonly Vector2 sourceCenter;
        private readonly AnimatedGlbModel.Pose pose;
        private readonly AnimatedModelRenderer renderer;

        internal ImportedTreeModel(AnimatedGlbModel source, params string[] roots)
            : this(source,null,roots) { }

        internal ImportedTreeModel(AnimatedGlbModel source, ImportedTreeModel reference, params string[] roots)
        {
            if(source.Skins.Length!=0||source.Clips.Count!=0)throw new NotSupportedException("Forest extraction requires rigid assets.");
            var included=new HashSet<int>();
            foreach(string name in roots) {
                int root=Array.FindIndex(source.Nodes,node=>node.Name==name);
                if(root<0)throw new InvalidDataException("Missing tree group: "+name);
                for(int i=0;i<source.Nodes.Length;i++)
                    for(int parent=i;parent>=0;parent=source.Nodes[parent].Parent)
                        if(parent==root){included.Add(i);break;}
            }
            var sourcePose=source.CreatePose();sourcePose.Evaluate(null,0);
            var axis=Matrix4.CreateRotationX(MathF.PI/2);
            var groups=new Dictionary<(int,Vector4,AnimatedGlbModel.AlphaMode,float,bool), (List<float> Vertices,List<uint> Indices)>();
            Vector3 min=new(float.MaxValue),max=new(float.MinValue);
            foreach(var mesh in source.Meshes) {
                if(!included.Contains(mesh.Node))continue;
                var key=(mesh.Image,mesh.Color,mesh.Alpha,mesh.AlphaCutoff,mesh.DoubleSided);
                if(!groups.TryGetValue(key,out var group))groups.Add(key,group=(new(),new()));
                uint offset=(uint)(group.Vertices.Count/16);
                Matrix4 transform=sourcePose.World[mesh.Node]*axis;
                Matrix4 normalTransform=transform.Inverted().Transposed();
                for(int i=0;i<mesh.Vertices.Length;i+=16) {
                    var v=mesh.Vertices;
                    Vector3 p=Vector3.TransformPosition(new(v[i],v[i+1],v[i+2]),transform);
                    Vector3 n=Vector3.TransformVector(new(v[i+3],v[i+4],v[i+5]),normalTransform).Normalized();
                    min=Vector3.ComponentMin(min,p);max=Vector3.ComponentMax(max,p);
                    group.Vertices.AddRange(new[] {p.X,p.Y,p.Z,n.X,n.Y,n.Z,v[i+6],v[i+7],0,0,0,0,0,0,0,0});
                }
                foreach(uint index in mesh.Indices)group.Indices.Add(index+offset);
            }
            if(reference!=null) {min=reference.sourceMin;max=reference.sourceMax;}
            sourceMin=min;sourceMax=max;
            float height=max.Z-min.Z;
            if(groups.Count==0||height<=0)throw new InvalidDataException("Empty forest model.");
            // Locate the bole at ground level, rather than centring an asymmetric crown.
            Vector2 baseMin=new(float.MaxValue),baseMax=new(float.MinValue);
            foreach(var group in groups.Values)for(int i=0;i<group.Vertices.Count;i+=16)
                if(group.Vertices[i+2]<=min.Z+height*.025f) {
                    Vector2 point=new(group.Vertices[i],group.Vertices[i+1]);
                    baseMin=Vector2.ComponentMin(baseMin,point);baseMax=Vector2.ComponentMax(baseMax,point);
                }
            Vector2 center=reference?.sourceCenter??(baseMin+baseMax)*.5f;
            sourceCenter=center;
            Normalization=Matrix4.CreateTranslation(-center.X,-center.Y,-min.Z)*Matrix4.CreateScale(1/height);
            var meshes=new List<AnimatedGlbModel.Mesh>();var images=new List<PngImage>();var imageMap=new Dictionary<int,int>();
            foreach(var entry in groups) {
                var key=entry.Key;float[] vertices=entry.Value.Vertices.ToArray();
                for(int i=0;i<vertices.Length;i+=16) {vertices[i]=(vertices[i]-center.X)/height;vertices[i+1]=(vertices[i+1]-center.Y)/height;vertices[i+2]=(vertices[i+2]-min.Z)/height;}
                int image=-1;
                if(key.Item1>=0&&!imageMap.TryGetValue(key.Item1,out image)) {
                    image=images.Count;imageMap.Add(key.Item1,image);images.Add(source.Images[key.Item1]);
                }
                // Leaf textures depict solid foliage and empty space: depth-writing cutouts
                // avoid per-tree BLEND sorting artefacts and keep forest overdraw bounded.
                var mesh=new AnimatedGlbModel.Mesh {Node=0,Image=image,Color=key.Item2,
                    Alpha=key.Item3==AnimatedGlbModel.AlphaMode.Blend?AnimatedGlbModel.AlphaMode.Mask:key.Item3,
                    AlphaCutoff=key.Item4,DoubleSided=key.Item5,Vertices=vertices,Indices=entry.Value.Indices.ToArray(),
                    Center=new Vector3(0,0,.5f)};
                mesh.FlatColor=image<0?mesh.Color.Xyz:AverageColor(images[image])*mesh.Color.Xyz;
                TriangleCount+=mesh.Indices.Length/3;meshes.Add(mesh);
            }
            Model=new AnimatedGlbModel {Nodes=new[] {new AnimatedGlbModel.Node()},Order=new[] {0},
                Skins=Array.Empty<AnimatedGlbModel.Skin>(),Meshes=meshes.ToArray(),Images=images.ToArray()};
            pose=Model.CreatePose();pose.Evaluate(null,0);renderer=new AnimatedModelRenderer(Model);
        }

        internal void Draw(in ForestTree tree,double year,in Terrain.TreeInstance stem,GraphicsSettings graphics)
        {
            var size=tree.At(year);
            float height=size.Height*Terrain.TreeMetresToWorld;
            float nativeCrownRatio=tree.Species switch {ForestSpecies.Spruce=>.26f,ForestSpecies.Oak=>.28f,ForestSpecies.Birch=>.16f,_=>.22f};
            float width=size.CrownRadius/nativeCrownRatio*Terrain.TreeMetresToWorld;
            Matrix4 placement=Matrix4.CreateScale(width,width,height)*Matrix4.CreateRotationZ(stem.Yaw)
                *Matrix4.CreateTranslation(stem.X,stem.Y,stem.BaseZ);
            renderer.Draw(pose,placement,graphics,sourceMaterial:true);
        }
        private static Vector3 AverageColor(PngImage image)
        {
            Vector3 sum=Vector3.Zero;float weight=0;
            int step=Math.Max(1,image.Pixels.Length/4/4096);
            for(int i=0;i<image.Pixels.Length;i+=4*step) {
                float alpha=image.Pixels[i+3]/255f;
                sum+=new Vector3(MathF.Pow(image.Pixels[i]/255f,2.2f),MathF.Pow(image.Pixels[i+1]/255f,2.2f),MathF.Pow(image.Pixels[i+2]/255f,2.2f))*alpha;
                weight+=alpha;
            }
            return weight>0?sum/weight:Vector3.Zero;
        }
        public void Dispose()=>renderer.Dispose();
    }

    /// <summary>One shared CPU/GPU asset per variant, scoped to its owning terrain.</summary>
    internal sealed class ImportedForestModels : IDisposable
    {
        private readonly Dictionary<(ForestAsset,ForestLod),ImportedTreeModel> models=new();
        internal static bool UsesImported(ForestSpecies species,GraphicsSettings graphics)=>
            graphics.ForestModels!=ForestModelStyle.Procedural
            &&species is ForestSpecies.Spruce or ForestSpecies.Birch or ForestSpecies.Oak or ForestSpecies.Beech
            &&(species!=ForestSpecies.Birch||graphics.ImportedBirch);
        internal static ForestAsset Select(in ForestTree tree,ForestModelStyle style=ForestModelStyle.Imported)=>tree.Species switch {
            ForestSpecies.Spruce when style==ForestModelStyle.Generated=>tree.Seed%2==0?ForestAsset.GeneratedPineA:ForestAsset.GeneratedPineB,
            ForestSpecies.Oak when style==ForestModelStyle.Generated=>tree.Seed%2==0?ForestAsset.GeneratedOakA:ForestAsset.GeneratedOakB,
            ForestSpecies.Spruce=>(ForestAsset)((int)ForestAsset.SpruceTall+tree.Seed%3),
            ForestSpecies.Oak=>(ForestAsset)((int)ForestAsset.OakSmall+tree.Seed%3),
            ForestSpecies.Birch=>ForestAsset.Birch,
            _=>tree.Seed%2==0?ForestAsset.BroadleafA:ForestAsset.BroadleafB };

        internal ImportedTreeModel Get(ForestAsset asset,ForestLod lod=ForestLod.Near)
        {
            bool generated=asset>=ForestAsset.GeneratedPineA;
            if(!generated)lod=ForestLod.Near;
            if(models.TryGetValue((asset,lod),out var result))return result;
            if(generated) {
                string species=asset<=ForestAsset.GeneratedPineB?"pine":"oak";
                int variant=asset is ForestAsset.GeneratedPineA or ForestAsset.GeneratedOakA?1:2;
                var source=Load($"Generated/ez-{species}-{variant:00}-{lod.ToString().ToLowerInvariant()}.glb");
                result=lod==ForestLod.Near?new ImportedTreeModel(source,"Tree")
                    :new ImportedTreeModel(source,Get(asset,ForestLod.Near),"Tree");
                models.Add((asset,lod),result);return result;
            }
            if(asset<=ForestAsset.SpruceMedium) {
                var source=Load("forest-pack-original.glb");
                Add(ForestAsset.SpruceTall,source,"Tree_Branches_01","Tree_Trunk_01.001");
                Add(ForestAsset.SpruceSmall,source,"Tree_Branches_01.001","Tree_Trunk_01");
                Add(ForestAsset.SpruceMedium,source,"Tree_Branches_01.002","Tree_Trunk_01.002");
            } else if(asset<=ForestAsset.OakLarge) {
                var source=Load("oak-trees-original.glb");
                Add(ForestAsset.OakSmall,source,"Treesmall");Add(ForestAsset.OakMedium,source,"Treemedium");Add(ForestAsset.OakLarge,source,"treelarge");
            } else if(asset<=ForestAsset.BroadleafB) {
                var source=Load("trees-low-poly-original.glb");
                Add(ForestAsset.BroadleafA,source,"tree4");Add(ForestAsset.BroadleafB,source,"tree6");
            } else Add(ForestAsset.Birch,Load("birch-original.glb"),"Cylinder");
            return models[(asset,lod)];
        }
        private void Add(ForestAsset key,AnimatedGlbModel source,params string[] roots)=>models.Add((key,ForestLod.Near),new ImportedTreeModel(source,roots));
        private static AnimatedGlbModel Load(string filename)=>AnimatedGlbModel.Load(Path.Combine(AppContext.BaseDirectory,"Assets","Forest",filename));
        public void Dispose(){foreach(var model in models.Values)model.Dispose();models.Clear();}
    }
}
