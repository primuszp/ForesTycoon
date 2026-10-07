using System;
using System.Collections.Generic;
using System.IO;
using OpenTK.Mathematics;
namespace ForesTycoon.Models
{
    internal sealed class ImportedSceneAsset : IDisposable
    {
        internal readonly AnimatedGlbModel Model;
        internal readonly AnimatedGlbModel.Pose Pose;
        private readonly AnimatedModelRenderer renderer;
        internal readonly Matrix4 Normalization;

        /// <param name="width">Longest horizontal side after normalisation, in world units.</param>
        /// <param name="footprint">
        /// Building mode: the square side the asset must stay inside. The scale is then fitted to
        /// the tall core of the model (the building itself), not to low props scattered around
        /// it, and those props are pulled inward so nothing spills onto neighbouring tiles.
        /// </param>
        internal ImportedSceneAsset(string path,float width,bool centerHeight=false,float footprint=0)
        {
            Model=AnimatedGlbModel.Load(Path.Combine(AppContext.BaseDirectory,path));
            Pose=Model.CreatePose();Pose.Evaluate(null,0);renderer=new AnimatedModelRenderer(Model);
            Matrix4 axis=Matrix4.CreateRotationX(MathF.PI/2);
            var nodes=new Dictionary<int,(Vector3 Min,Vector3 Max)>();
            Vector3 min=new(float.MaxValue),max=new(float.MinValue);
            foreach(var mesh in Model.Meshes){
                Vector3 meshMin=new(float.MaxValue),meshMax=new(float.MinValue);
                for(int i=0;i<mesh.Vertices.Length;i+=16){var v=mesh.Vertices;Vector4 p=new(v[i],v[i+1],v[i+2],1),point=Vector4.Zero;
                    if(mesh.Skin<0)point=Vector4.TransformRow(p,Pose.World[mesh.Node]);
                    else for(int j=0;j<4;j++)point+=Vector4.TransformRow(p,Pose.JointMatrix(mesh.Skin,(int)v[i+8+j]))*v[i+12+j];
                    Vector3 q=Vector4.TransformRow(point,axis).Xyz;meshMin=Vector3.ComponentMin(meshMin,q);meshMax=Vector3.ComponentMax(meshMax,q);
                }
                min=Vector3.ComponentMin(min,meshMin);max=Vector3.ComponentMax(max,meshMax);
                if(mesh.Skin<0)nodes[mesh.Node]=nodes.TryGetValue(mesh.Node,out var b)
                    ?(Vector3.ComponentMin(b.Min,meshMin),Vector3.ComponentMax(b.Max,meshMax)):(meshMin,meshMax);
            }
            Vector3 fitMin=min,fitMax=max;
            if(footprint>0){
                // The core is everything that rises above a quarter of the full height.
                float tall=min.Z+(max.Z-min.Z)*0.25f;
                Vector3 coreMin=new(float.MaxValue),coreMax=new(float.MinValue);
                foreach(var b in nodes.Values)if(b.Max.Z>tall){coreMin=Vector3.ComponentMin(coreMin,b.Min);coreMax=Vector3.ComponentMax(coreMax,b.Max);}
                if(coreMin.X<=coreMax.X){fitMin=new(coreMin.X,coreMin.Y,min.Z);fitMax=new(coreMax.X,coreMax.Y,max.Z);}
            }
            float scale=width/Math.Max(fitMax.X-fitMin.X,fitMax.Y-fitMin.Y);
            Vector3 center=(fitMin+fitMax)*0.5f;center.Z=centerHeight?center.Z:min.Z;
            if(footprint>0){
                // Low props outside the footprint slide inward until they touch its edge.
                float half=footprint*0.5f/scale;
                Matrix4 toRaw=axis.Inverted();
                foreach(var (node,b) in nodes){
                    Vector2 offset=Vector2.Zero;
                    for(int k=0;k<2;k++){
                        float lo=b.Min[k]-center[k],hi=b.Max[k]-center[k];
                        if(hi-lo>=2*half)continue;
                        if(hi>half)offset[k]=half-hi; else if(lo<-half)offset[k]=-half-lo;
                    }
                    if(offset==Vector2.Zero)continue;
                    Vector3 raw=Vector3.TransformVector(new Vector3(offset.X,offset.Y,0),toRaw);
                    Pose.World[node]*=Matrix4.CreateTranslation(raw);
                }
            }
            Normalization=axis*Matrix4.CreateTranslation(-center)*Matrix4.CreateScale(scale);
            if(centerHeight&&max.Y-min.Y>max.X-min.X)Normalization*=Matrix4.CreateRotationZ(-MathF.PI/2);
        }
        internal void Draw(Matrix4 placement,IShadingSettings settings)=>renderer.Draw(Pose,Normalization*placement,settings);
        public void Dispose()=>renderer.Dispose();
    }
}
