using System;
using System.IO;
using OpenTK.Graphics.OpenGL;
using OpenTK.Mathematics;
using OpenTK.Windowing.Desktop;
using OpenTK.Windowing.Common;

namespace ForesTycoon
{
    internal static class MaterialAlphaSmokeTest
    {
        internal static void Run()
        {
            using var window=new NativeWindow(new NativeWindowSettings {
                StartVisible=false,ClientSize=new Vector2i(64,64),API=ContextAPI.OpenGL,
                APIVersion=new Version(3,3),Profile=ContextProfile.Core });
            window.Context.MakeCurrent();RenderDevice.Initialize();GL.Viewport(0,0,64,64);
            try {
                var mesh=new AnimatedGlbModel.Mesh { Node=0,Image=0,Color=new Vector4(0,1,0,1),
                    Alpha=AnimatedGlbModel.AlphaMode.Mask,Indices=new uint[] {0,1,2,0,2,3},
                    Vertices=new float[64] };
                float[] positions={-1,-1,0, 1,-1,0, 1,1,0, -1,1,0};
                float[] uvs={0,0,1,0,1,1,0,1};
                for(int i=0;i<4;i++) {
                    Array.Copy(positions,i*3,mesh.Vertices,i*16,3);
                    mesh.Vertices[i*16+5]=1;
                    Array.Copy(uvs,i*2,mesh.Vertices,i*16+6,2);
                }
                var model=new AnimatedGlbModel { Nodes=new[] {new AnimatedGlbModel.Node()},Order=new[] {0},
                    Skins=Array.Empty<AnimatedGlbModel.Skin>(),Meshes=new[] {mesh},
                    Images=new[] {new PngImage(2,1,new byte[] {255,255,255,0,255,255,255,255})} };
                var pose=model.CreatePose();pose.Evaluate(null,0);
                using var renderer=new AnimatedModelRenderer(model);
                var settings=new GraphicsSettings {Lighting=false,Textures=true,Enhanced=true,Quality=GraphicsQuality.Low};
                RenderDevice.SetCamera(Matrix4.Identity);
                Clear();renderer.Draw(pose,Matrix4.Identity,settings,sourceMaterial:true);
                Require(Pixel(16)[0]>245&&Pixel(48)[1]>245,"MASK must preserve background through transparent texels");
                RenderDevice.SetCamera(Matrix4.CreateTranslation(3,0,0));
                Clear();renderer.Draw(pose,Matrix4.Identity,settings,sourceMaterial:true);
                Require(Pixel(48)[0]>245,"Cached scene uniforms ignored a camera change");
                RenderDevice.SetCamera(Matrix4.Identity);
                Clear();renderer.Draw(pose,Matrix4.CreateTranslation(3,0,0),settings,sourceMaterial:true);
                Require(Pixel(48)[0]>245,"Repeated instances reused a stale placement");
                Clear();renderer.Draw(pose,Matrix4.Identity,settings,sourceMaterial:true);
                Require(Pixel(48)[1]>245,"Camera or instance restoration left stale uniforms");
                string output=Path.GetFullPath("artifacts/tree-asset-review");Directory.CreateDirectory(output);
                FramebufferCapture.SavePng(Path.Combine(output,"alpha-mask.png"),64,64);
                settings.Enhanced=false;settings.Textures=false;
                Clear();renderer.Draw(pose,Matrix4.Identity,settings,sourceMaterial:true);
                Require(Pixel(16)[0]>245&&Pixel(48)[1]>245,"Texture quality switches must preserve coverage");
                mesh.Alpha=AnimatedGlbModel.AlphaMode.Opaque;
                Clear();renderer.Draw(pose,Matrix4.Identity,settings,sourceMaterial:true);
                Require(Pixel(16)[1]>245,"OPAQUE must ignore texture alpha");
                mesh.Alpha=AnimatedGlbModel.AlphaMode.Blend;mesh.Color.W=.5f;
                Clear();
                GL.Disable(EnableCap.Blend);GL.DepthMask(false);
                GL.BlendFunc(BlendingFactor.One,BlendingFactor.Zero);
                renderer.Draw(pose,Matrix4.Identity,settings,sourceMaterial:true);
                byte[] pixel=Pixel(48);
                Require(pixel[0]>=120&&pixel[0]<=135&&pixel[1]>=120&&pixel[1]<=135,"BLEND must mix foreground and background");
                GL.GetBoolean(GetPName.DepthWritemask,out bool writes);
                GL.GetInteger(GetPName.BlendSrcRgb,out int source);
                Require(!writes&&!GL.IsEnabled(EnableCap.Blend)&&source==(int)BlendingFactor.One,"GL blend/depth state must be restored");
                FramebufferCapture.SavePng(Path.Combine(output,"alpha-blend.png"),64,64);
                Clear();
                renderer.Draw(pose,Matrix4.Identity,settings,sourceMaterial:true);
                renderer.Draw(pose,Matrix4.Identity,settings,sourceMaterial:true);
                byte[] separate=Pixel(48);
                Clear(); GL.Disable(EnableCap.Blend); GL.DepthMask(false);
                using(var batch=new RenderStateScope().Enable(EnableCap.DepthTest).Disable(EnableCap.CullFace))
                {
                    renderer.Draw(pose,Matrix4.Identity,settings,sourceMaterial:true,sharedState:batch);
                    renderer.Draw(pose,Matrix4.Identity,settings,sourceMaterial:true,sharedState:batch);
                }
                Require(separate.AsSpan().SequenceEqual(Pixel(48)),"Shared model state changed blended instance pixels");
                GL.GetBoolean(GetPName.DepthWritemask,out writes);
                Require(!writes&&!GL.IsEnabled(EnableCap.Blend),"Shared model state was not restored");
                // Intentionally list the near blue surface before the far green one.
                // Correct compositing is far green first, then near blue.
                float[] nearVertices=(float[])mesh.Vertices.Clone(),farVertices=(float[])mesh.Vertices.Clone();
                for(int i=2;i<64;i+=16) {nearVertices[i]=-.3f;farVertices[i]=.3f;}
                var layered=new AnimatedGlbModel {Nodes=model.Nodes,Order=model.Order,Skins=model.Skins,Images=model.Images,
                    Meshes=new[] {
                        new AnimatedGlbModel.Mesh {Node=0,Image=0,Alpha=AnimatedGlbModel.AlphaMode.Blend,Color=new Vector4(0,0,1,.5f),
                            Center=new Vector3(0,0,-.3f),Vertices=nearVertices,Indices=mesh.Indices},
                        new AnimatedGlbModel.Mesh {Node=0,Image=0,Alpha=AnimatedGlbModel.AlphaMode.Blend,Color=new Vector4(0,1,0,.5f),
                            Center=new Vector3(0,0,.3f),Vertices=farVertices,Indices=mesh.Indices} } };
                var layeredPose=layered.CreatePose();layeredPose.Evaluate(null,0);
                using(var layers=new AnimatedModelRenderer(layered)) {
                    Clear();layers.Draw(layeredPose,Matrix4.Identity,settings,sourceMaterial:true);
                    pixel=Pixel(48);
                    Require(pixel[0]>=58&&pixel[0]<=70&&pixel[1]>=58&&pixel[1]<=70&&pixel[2]>=120&&pixel[2]<=135,
                        "Blended primitives must draw back to front");
                }
                mesh.Alpha=AnimatedGlbModel.AlphaMode.Mask;mesh.Color.W=1;
                settings.Enhanced=true;settings.Lighting=true;settings.Shadows=true;
                using var terrain=new Terrain(TerrainSettings.Default.WithNodeSize(17,42),(_,_)=>4);
                using var visuals=new SurfaceVisualRenderer(settings,new WeatherVisualState());
                RenderDevice.Visuals=visuals;visuals.BeginFrame();
                visuals.RenderShadows(terrain,()=> {
                    renderer.Draw(pose,visuals.ShadowCamera.Inverted(),settings,sourceMaterial:true);
                    GL.GetInteger(GetPName.DrawFramebufferBinding,out int framebuffer);
                    GL.GetInteger(GetPName.ReadFramebufferBinding,out int previous);
                    GL.BindFramebuffer(FramebufferTarget.ReadFramebuffer,framebuffer);
                    try {
                        float[] left=new float[1],right=new float[1];int size=settings.ShadowResolution;
                        GL.ReadPixels(size/4,size/2,1,1,PixelFormat.DepthComponent,PixelType.Float,left);
                        GL.ReadPixels(size*3/4,size/2,1,1,PixelFormat.DepthComponent,PixelType.Float,right);
                        Require(left[0]>.99f&&right[0]<.9f,"Shadow depth must discard transparent texels");
                    } finally {GL.BindFramebuffer(FramebufferTarget.ReadFramebuffer,previous);}
                });
                Require(GL.GetError()==ErrorCode.NoError,"OpenGL error");
                Console.WriteLine("Material alpha smoke passed: MASK, BLEND, OPAQUE, quality switches, state restoration, shadow cutout.");
            } finally {RenderDevice.Dispose();}
        }
        private static void Clear() { GL.DepthMask(true);GL.ClearColor(1,0,0,1);GL.Clear(ClearBufferMask.ColorBufferBit|ClearBufferMask.DepthBufferBit); }
        private static byte[] Pixel(int x) {byte[] p=new byte[4];GL.ReadPixels(x,32,1,1,PixelFormat.Rgba,PixelType.UnsignedByte,p);return p;}
        private static void Require(bool ok,string message) {if(!ok)throw new InvalidOperationException(message);}
    }
}
