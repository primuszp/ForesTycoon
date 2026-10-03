using System;
using System.IO;
using OpenTK.Graphics.OpenGL;
using OpenTK.Mathematics;
using OpenTK.Windowing.Common;
using OpenTK.Windowing.Desktop;

namespace ForesTycoon
{
    // Uses the real GLB loader/shader without installing candidates in the game.
    internal static class TreeAssetPreview
    {
        internal static void Run(string path)
        {
            var model=AnimatedGlbModel.Load(path);
            var pose=model.CreatePose();pose.Evaluate(null,0);
            Matrix4 axis=Matrix4.CreateRotationX(MathF.PI/2);
            Vector3 min=new(float.MaxValue),max=new(float.MinValue);
            foreach(var mesh in model.Meshes)for(int i=0;i<mesh.Vertices.Length;i+=16) {
                var v=mesh.Vertices;
                Vector3 point=Vector3.TransformPosition(new Vector3(v[i],v[i+1],v[i+2]),pose.World[mesh.Node]*axis);
                min=Vector3.ComponentMin(min,point);max=Vector3.ComponentMax(max,point);
            }
            Vector3 extent=max-min,center=(min+max)*.5f;
            float scale=1/Math.Max(extent.Z,Math.Max(extent.X,extent.Y));
            Matrix4 placement=axis*Matrix4.CreateTranslation(-center)*Matrix4.CreateScale(scale);
            using var window=new NativeWindow(new NativeWindowSettings {
                StartVisible=false,ClientSize=new Vector2i(960,960),NumberOfSamples=4,
                API=ContextAPI.OpenGL,APIVersion=new Version(3,3),Profile=ContextProfile.Core });
            window.Context.MakeCurrent();RenderDevice.Initialize();GL.Viewport(0,0,960,960);
            try {
                using var renderer=new AnimatedModelRenderer(model);
                RenderDevice.SetCamera(Matrix4.LookAt(new Vector3(1,-3,.5f),Vector3.Zero,Vector3.UnitZ)
                    *Matrix4.CreateOrthographic(1.35f,1.35f,.01f,10));
                GL.ClearColor(.88f,.9f,.92f,1);
                GL.DepthMask(true);GL.Clear(ClearBufferMask.ColorBufferBit|ClearBufferMask.DepthBufferBit);
                renderer.Draw(pose,placement,new GraphicsSettings {Lighting=true,Shadows=false,Weather=false},sourceMaterial:true);
                if(GL.GetError()!=ErrorCode.NoError)throw new InvalidOperationException("Asset preview GL error");
                string output=Path.GetFullPath("artifacts/tree-asset-review");Directory.CreateDirectory(output);
                string filename=Path.Combine(output,Path.GetFileNameWithoutExtension(path)+"-engine.png");
                FramebufferCapture.SavePng(filename,960,960);
                Console.WriteLine("Engine GLB preview: "+filename);
            } finally {RenderDevice.Dispose();}
        }
    }
}
