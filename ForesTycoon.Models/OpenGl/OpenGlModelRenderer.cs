using System;
using OpenTK.Graphics.OpenGL;
using OpenTK.Mathematics;
namespace ForesTycoon.Models.OpenGl
{
    internal sealed class OpenGlModelRenderer : IModelRenderBackend
    {
        private readonly AnimatedGlbModel model;
        private readonly int[] vaos,vbos,ebos,textures;
        private readonly float[] palette=new float[64*16];
        private int program, boneBuffer;
        private bool disposed;
        private ModelSceneParameters? sceneUniforms;
        private readonly record struct MaterialUniforms(AnimatedGlbModel.AlphaMode Alpha, float Cutoff,
            bool HasAlbedo, bool Skinned, Vector4 Tint, Vector3 FlatColor, bool Textured);
        private MaterialUniforms? materialUniforms;
        internal OpenGlModelRenderer(AnimatedGlbModel model)
        {
            this.model=model;vaos=new int[model.Meshes.Length];vbos=new int[vaos.Length];ebos=new int[vaos.Length];textures=new int[model.Images.Length];
        }
        // A caller drawing consecutive instances may share one depth/cull state scope.
        public IModelRenderBatch BeginBatch()
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            return new OpenGlModelRenderBatch();
        }

        public void Draw(AnimatedGlbModel.Pose pose,Matrix4 transform,in ModelRenderFrame frame,
            ReadOnlySpan<int> drawOrder,IModelRenderBatch batch)
        {
            ObjectDisposedException.ThrowIf(disposed,this);
            if (batch != null && batch is not OpenGlModelRenderBatch)
                throw new ArgumentException("Model batch belongs to another backend.", nameof(batch));
            if (batch is OpenGlModelRenderBatch suppliedBatch) suppliedBatch.ThrowIfDisposed();
            if(program==0)Initialize();
            var scene = frame.Scene;
            bool shadow=scene.Shadow;
            bool outline=!shadow&&frame.OutlineWorldWidth>0;
            Matrix4 camera=scene.Camera;
            Matrix4 light=scene.Light;
            GL.UseProgram(program);
            GL.UniformMatrix4(GlProgram.Uniform(program,"instance"),false,ref transform);
            // Uniforms belong to this renderer's private program and survive other programs
            // drawing. Compare values so camera, weather and shadow-pass changes stay live.
            if (sceneUniforms != scene)
            {
                GL.Uniform1(GlProgram.Uniform(program,"source_material"),scene.SourceMaterial?1:0);
                GL.Uniform1(GlProgram.Uniform(program,"shadow_pass"),shadow?1:0);
                GL.UniformMatrix4(GlProgram.Uniform(program,"camera"),false,ref camera);
                GL.UniformMatrix4(GlProgram.Uniform(program,"light_camera"),false,ref light);
                float az=MathHelper.DegreesToRadians(scene.SunAzimuth),el=MathHelper.DegreesToRadians(scene.SunElevation);
                GL.Uniform3(GlProgram.Uniform(program,"sun"),MathF.Cos(az)*MathF.Cos(el),MathF.Sin(az)*MathF.Cos(el),MathF.Sin(el));
                GL.Uniform1(GlProgram.Uniform(program,"lit"),scene.Lit?1:0);
                GL.Uniform1(GlProgram.Uniform(program,"shadowed"),scene.Shadowed?1:0);
                GL.Uniform1(GlProgram.Uniform(program,"shadow_map"),1);
                GL.Uniform1(GlProgram.Uniform(program,"albedo"),4);
                GL.Uniform4(GlProgram.Uniform(program,"climate"),scene.Climate);
                sceneUniforms = scene;
            }
            GL.BindBufferBase(BufferRangeTarget.UniformBuffer,2,boneBuffer);
            int uploadedSkin=-2;
            using var ownedBatch=batch==null?new OpenGlModelRenderBatch():null;
            var drawState=(OpenGlModelRenderBatch)batch??ownedBatch;
            for(int draw=0;draw<drawOrder.Length;draw++)
            {
                int i=drawOrder[draw];
                var mesh=model.Meshes[i];
                bool blend=mesh.Alpha==AnimatedGlbModel.AlphaMode.Blend&&!shadow;
                if(blend)drawState.AlphaBlend();else GL.Disable(EnableCap.Blend);
                GL.DepthMask(!blend);
                GL.Uniform1(GlProgram.Uniform(program,"outline_width"),0f);
                bool textured=frame.Textures&&mesh.Image>=0;
                var material = new MaterialUniforms(mesh.Alpha, mesh.AlphaCutoff, mesh.Image>=0,
                    mesh.Skin>=0, mesh.Color, mesh.FlatColor??mesh.Color.Xyz, textured);
                if(materialUniforms != material)
                {
                    GL.Uniform1(GlProgram.Uniform(program,"alpha_mode"),(int)mesh.Alpha);
                    GL.Uniform1(GlProgram.Uniform(program,"alpha_cutoff"),mesh.AlphaCutoff);
                    GL.Uniform1(GlProgram.Uniform(program,"has_albedo"),mesh.Image>=0?1:0);
                    GL.Uniform1(GlProgram.Uniform(program,"skinned"),mesh.Skin>=0?1:0);
                    GL.Uniform4(GlProgram.Uniform(program,"tint"),mesh.Color);
                    GL.Uniform3(GlProgram.Uniform(program,"flat_color"),mesh.FlatColor??mesh.Color.Xyz);
                    GL.Uniform1(GlProgram.Uniform(program,"textured"),textured?1:0);
                    materialUniforms=material;
                }
                if(mesh.Skin>=0 && uploadedSkin!=mesh.Skin) {
                    int count=model.Skins[mesh.Skin].Joints.Length;
                    for(int j=0;j<count;j++) {
                        Matrix4 m=pose.JointMatrix(mesh.Skin,j);
                        for(int r=0;r<4;r++)for(int c=0;c<4;c++)palette[j*16+r*4+c]=m[r,c];
                    }
                    GL.BindBuffer(BufferTarget.UniformBuffer,boneBuffer);
                    GL.BufferSubData(BufferTarget.UniformBuffer,IntPtr.Zero,count*16*sizeof(float),palette);
                    uploadedSkin=mesh.Skin;
                }
                Matrix4 node=pose.World[mesh.Node];
                GL.UniformMatrix4(GlProgram.Uniform(program,"node"),false,ref node);
                GL.ActiveTexture(TextureUnit.Texture4);GL.BindTexture(TextureTarget.Texture2D,mesh.Image>=0?textures[mesh.Image]:0);
                GL.BindVertexArray(vaos[i]);GL.DrawElements(PrimitiveType.Triangles,mesh.Indices.Length,DrawElementsType.UnsignedInt,IntPtr.Zero);
                RenderMetrics.RecordDraw(mesh.Indices.Length);
                if(outline&&!blend) {
                    using var contourState=new RenderStateScope().Enable(EnableCap.CullFace);
                    GL.GetInteger(GetPName.CullFaceMode,out int previousCull);
                    try {
                        GL.CullFace(TriangleFace.Front);
                        GL.Uniform1(GlProgram.Uniform(program,"outline_width"),frame.OutlineWorldWidth);
                        GL.DrawElements(PrimitiveType.Triangles,mesh.Indices.Length,DrawElementsType.UnsignedInt,IntPtr.Zero);
                        RenderMetrics.RecordDraw(mesh.Indices.Length);
                    } finally {
                        GL.CullFace((TriangleFace)previousCull);
                        GL.Uniform1(GlProgram.Uniform(program,"outline_width"),0f);
                    }
                }
            }
            GL.BindVertexArray(0);GL.ActiveTexture(TextureUnit.Texture0);
        }
        private void Initialize()
        {
            program=GlProgram.Create(OpenGlModelShaders.Vertex,OpenGlModelShaders.Fragment);
            boneBuffer=GL.GenBuffer();
            GL.BindBuffer(BufferTarget.UniformBuffer,boneBuffer);
            GL.BufferData(BufferTarget.UniformBuffer,palette.Length*sizeof(float),IntPtr.Zero,BufferUsageHint.StreamDraw);
            GL.UniformBlockBinding(program,GL.GetUniformBlockIndex(program,"JointPalette"),2);
            GL.ActiveTexture(TextureUnit.Texture4);
            for(int i=0;i<textures.Length;i++) {
                textures[i]=GL.GenTexture();GL.BindTexture(TextureTarget.Texture2D,textures[i]);var image=model.Images[i];
                GL.TexImage2D(TextureTarget.Texture2D,0,PixelInternalFormat.Rgba8,image.Width,image.Height,0,PixelFormat.Rgba,PixelType.UnsignedByte,image.Pixels);
                GL.TexParameter(TextureTarget.Texture2D,TextureParameterName.TextureMinFilter,(int)TextureMinFilter.LinearMipmapLinear);
                GL.TexParameter(TextureTarget.Texture2D,TextureParameterName.TextureMagFilter,(int)TextureMagFilter.Linear);
                GL.TexParameter(TextureTarget.Texture2D,TextureParameterName.TextureWrapS,(int)TextureWrapMode.Repeat);
                GL.TexParameter(TextureTarget.Texture2D,TextureParameterName.TextureWrapT,(int)TextureWrapMode.Repeat);
                GL.GenerateMipmap(GenerateMipmapTarget.Texture2D);
            }
            GL.ActiveTexture(TextureUnit.Texture0);
            for(int i=0;i<vaos.Length;i++) {
                var mesh=model.Meshes[i];vaos[i]=GL.GenVertexArray();vbos[i]=GL.GenBuffer();ebos[i]=GL.GenBuffer();
                GL.BindVertexArray(vaos[i]);GL.BindBuffer(BufferTarget.ArrayBuffer,vbos[i]);
                GL.BufferData(BufferTarget.ArrayBuffer,mesh.Vertices.Length*sizeof(float),mesh.Vertices,BufferUsageHint.StaticDraw);
                GL.BindBuffer(BufferTarget.ElementArrayBuffer,ebos[i]);GL.BufferData(BufferTarget.ElementArrayBuffer,mesh.Indices.Length*sizeof(uint),mesh.Indices,BufferUsageHint.StaticDraw);
                int[] sizes={3,3,2,4,4},offsets={0,3,6,8,12};
                for(int a=0;a<5;a++){GL.EnableVertexAttribArray(a);GL.VertexAttribPointer(a,sizes[a],VertexAttribPointerType.Float,false,16*sizeof(float),offsets[a]*sizeof(float));}
            }
            GL.BindVertexArray(0);
        }
        public void Dispose() {
            if(disposed)return;disposed=true;
            if(program!=0)GlProgram.Delete(program);
            foreach(int id in vaos)if(id!=0)GL.DeleteVertexArray(id);
            foreach(int id in vbos)if(id!=0)GL.DeleteBuffer(id);
            foreach(int id in ebos)if(id!=0)GL.DeleteBuffer(id);
            foreach(int id in textures)if(id!=0)GL.DeleteTexture(id);
            if(boneBuffer!=0)GL.DeleteBuffer(boneBuffer);
            program=boneBuffer=0;
        }
    }
}
