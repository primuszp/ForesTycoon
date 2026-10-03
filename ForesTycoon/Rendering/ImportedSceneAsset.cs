using System;
using System.IO;
using OpenTK.Mathematics;
namespace ForesTycoon
{
    internal sealed class ImportedSceneAsset : IDisposable
    {
        internal readonly AnimatedGlbModel Model;
        internal readonly AnimatedGlbModel.Pose Pose;
        private readonly AnimatedModelRenderer renderer;
        internal readonly Matrix4 Normalization;
        internal ImportedSceneAsset(string path,float width,bool centerHeight=false)
        {
            Model=AnimatedGlbModel.Load(Path.Combine(AppContext.BaseDirectory,path));
            Pose=Model.CreatePose();Pose.Evaluate(null,0);renderer=new AnimatedModelRenderer(Model);
            Matrix4 axis=Matrix4.CreateRotationX(MathF.PI/2);
            Vector3 min=new(float.MaxValue),max=new(float.MinValue);
            foreach(var mesh in Model.Meshes)for(int i=0;i<mesh.Vertices.Length;i+=16){var v=mesh.Vertices;Vector4 p=new(v[i],v[i+1],v[i+2],1),point=Vector4.Zero;
                if(mesh.Skin<0)point=Vector4.TransformRow(p,Pose.World[mesh.Node]);
                else for(int j=0;j<4;j++)point+=Vector4.TransformRow(p,Pose.JointMatrix(mesh.Skin,(int)v[i+8+j]))*v[i+12+j];
                Vector3 q=Vector4.TransformRow(point,axis).Xyz;min=Vector3.ComponentMin(min,q);max=Vector3.ComponentMax(max,q);
            }
            float scale=width/Math.Max(max.X-min.X,max.Y-min.Y);
            Vector3 center=(min+max)*0.5f;center.Z=centerHeight?center.Z:min.Z;
            Normalization=axis*Matrix4.CreateTranslation(-center)*Matrix4.CreateScale(scale);
            if(centerHeight&&max.Y-min.Y>max.X-min.X)Normalization*=Matrix4.CreateRotationZ(-MathF.PI/2);
        }
        internal void Draw(Matrix4 placement,GraphicsSettings settings)=>renderer.Draw(Pose,Normalization*placement,settings);
        public void Dispose()=>renderer.Dispose();
    }
}
