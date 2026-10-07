using System;
using OpenTK.Graphics.OpenGL;
using OpenTK.Mathematics;
namespace ForesTycoon.Models
{
    internal sealed class AnimatedModelRenderer : IDisposable
    {
        private readonly AnimatedGlbModel model;
        private readonly int[] vaos,vbos,ebos,textures;
        private readonly float[] palette=new float[64*16];
        private readonly int[] drawOrder;
        private readonly float[] drawDepth;
        private int program, boneBuffer;
        private bool disposed;
        private readonly record struct SceneUniforms(Matrix4 Camera, Matrix4 Light, Vector4 Climate,
            float SunAzimuth, float SunElevation, bool Lit, bool Shadowed, bool Shadow, bool SourceMaterial);
        private SceneUniforms? sceneUniforms;
        private readonly record struct MaterialUniforms(AnimatedGlbModel.AlphaMode Alpha, float Cutoff,
            bool HasAlbedo, bool Skinned, Vector4 Tint, Vector3 FlatColor, bool Textured);
        private MaterialUniforms? materialUniforms;
        internal AnimatedModelRenderer(AnimatedGlbModel model)
        {
            this.model=model;vaos=new int[model.Meshes.Length];vbos=new int[vaos.Length];ebos=new int[vaos.Length];textures=new int[model.Images.Length];
            drawOrder=new int[vaos.Length];drawDepth=new float[vaos.Length];
        }
        // A caller drawing consecutive instances may share one depth/cull state scope.
        internal void Draw(AnimatedGlbModel.Pose pose,Matrix4 transform,IShadingSettings settings,float outlineWorldWidth=0,bool sourceMaterial=false,RenderStateScope sharedState=null)
        {
            ObjectDisposedException.ThrowIf(disposed,this);
            if(program==0)Initialize();
            var visuals=RenderDevice.Visuals;
            bool shadow=visuals?.ShadowPass==true;
            bool outline=!shadow&&settings.Enhanced&&settings.ModelOutlines&&outlineWorldWidth>0;
            Matrix4 camera=shadow?visuals.ShadowCamera:RenderDevice.ViewProjection;
            Matrix4 light=visuals?.ShadowCamera??Matrix4.Identity;
            GL.UseProgram(program);
            GL.UniformMatrix4(GlProgram.Uniform(program,"instance"),false,ref transform);
            var scene = new SceneUniforms(camera, light, visuals?.Atmosphere??Vector4.Zero,
                settings.SunAzimuth, settings.SunElevation, settings.Enhanced&&settings.Lighting,
                !shadow&&visuals?.ShadowsReady==true, shadow, sourceMaterial);
            // Uniforms belong to this renderer's private program and survive other programs
            // drawing. Compare values so camera, weather and shadow-pass changes stay live.
            if (sceneUniforms != scene)
            {
                GL.Uniform1(GlProgram.Uniform(program,"source_material"),sourceMaterial?1:0);
                GL.Uniform1(GlProgram.Uniform(program,"shadow_pass"),shadow?1:0);
                GL.UniformMatrix4(GlProgram.Uniform(program,"camera"),false,ref camera);
                GL.UniformMatrix4(GlProgram.Uniform(program,"light_camera"),false,ref light);
                float az=MathHelper.DegreesToRadians(settings.SunAzimuth),el=MathHelper.DegreesToRadians(settings.SunElevation);
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
            // Solid/cutout surfaces first, then blended primitives back to front.
            // Sorting is per model instance; intersecting instances need a scene queue.
            for(int i=0;i<model.Meshes.Length;i++) {
                drawOrder[i]=i;
                var mesh=model.Meshes[i];
                drawDepth[i]=float.NegativeInfinity;
                if(mesh.Alpha==AnimatedGlbModel.AlphaMode.Blend&&!shadow)
                {
                    Vector4 projected=Vector4.TransformRow(new Vector4(mesh.Center,1),pose.World[mesh.Node]*transform*camera);
                    drawDepth[i]=-(MathF.Abs(projected.W)>0.000001f?projected.Z/projected.W:projected.Z);
                }
            }
            Array.Sort(drawDepth,drawOrder);
            using var ownedState=sharedState==null?new RenderStateScope().Enable(EnableCap.DepthTest).Disable(EnableCap.CullFace):null;
            var drawState=sharedState??ownedState;
            for(int draw=0;draw<drawOrder.Length;draw++)
            {
                int i=drawOrder[draw];
                var mesh=model.Meshes[i];
                bool blend=mesh.Alpha==AnimatedGlbModel.AlphaMode.Blend&&!shadow;
                if(blend)drawState.AlphaBlend();else GL.Disable(EnableCap.Blend);
                GL.DepthMask(!blend);
                GL.Uniform1(GlProgram.Uniform(program,"outline_width"),0f);
                bool textured=settings.Enhanced&&settings.Textures&&mesh.Image>=0;
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
                        GL.Uniform1(GlProgram.Uniform(program,"outline_width"),outlineWorldWidth);
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
            program=GlProgram.Create(@"#version 330 core
layout(location=0) in vec3 position;
layout(location=1) in vec3 normal;
layout(location=2) in vec2 texcoord;
layout(location=3) in vec4 joints;
layout(location=4) in vec4 weights;
layout(std140) uniform JointPalette { mat4 bones[64]; };
uniform mat4 node,instance,camera,light_camera;
uniform int skinned;
uniform float outline_width;
out vec2 uv;
out vec3 n;
out vec4 light_position;
void main(){
    mat4 pose=node;
    if(skinned!=0) pose=weights.x*bones[int(joints.x)]+weights.y*bones[int(joints.y)]+weights.z*bones[int(joints.z)]+weights.w*bones[int(joints.w)];
    mat4 model=instance*pose;
    vec4 p=model*vec4(position,1);
    n=normalize(mat3(transpose(inverse(model)))*normal);
    // Extrude after skinning in world units, including rigid animated attachments.
    p.xyz+=n*outline_width;
    uv=texcoord;light_position=light_camera*p;
    gl_Position=camera*p;
}",@"#version 330 core
in vec2 uv;
in vec3 n;
in vec4 light_position;
uniform sampler2D albedo;
uniform sampler2DShadow shadow_map;
uniform vec4 tint,climate;
uniform vec3 sun;
uniform vec3 flat_color;
uniform int textured,lit,shadowed,source_material,alpha_mode,has_albedo,shadow_pass;
uniform float outline_width,alpha_cutoff;
out vec4 output_color;
void main(){
    // Coverage is geometry: keep alpha sampling even when colour textures are off.
    vec4 texel=(textured!=0||(alpha_mode!=0&&has_albedo!=0))?texture(albedo,uv):vec4(1);
    float alpha=texel.a*tint.a;
    if(alpha_mode==1&&alpha<alpha_cutoff)discard;
    // Conventional depth shadows approximate BLEND with 50% coverage.
    if(alpha_mode==2&&(shadow_pass!=0?alpha<0.5:alpha<=0.0))discard;
    if(outline_width>0){output_color=vec4(0.07,0.085,0.09,1);return;}
    vec4 color=textured!=0?texel*tint:vec4(0.42,0.28,0.16,1)*tint;
    vec3 base=source_material!=0?(textured!=0?max(tint.rgb,vec3(0))*pow(max(texel.rgb,vec3(0)),vec3(2.2)):flat_color):pow(max(color.rgb,vec3(0)),vec3(2.2));
    vec3 normal=normalize(n);if(!gl_FrontFacing)normal=-normal;
    float shade=1;
    if(shadowed!=0){
        vec3 p=light_position.xyz/light_position.w*0.5+0.5;
        if(p.z>0&&p.z<1&&all(greaterThanEqual(p.xy,vec2(0)))&&all(lessThanEqual(p.xy,vec2(1))))
            shade=texture(shadow_map,vec3(p.xy,p.z-0.001));
    }
    if(lit!=0){
        vec3 ambient=mix(vec3(0.66,0.71,0.78),vec3(0.76,0.81,0.88),climate.x);
        vec3 direct=mix(vec3(0.56,0.49,0.38),vec3(0.13,0.14,0.15),climate.x);
        base*=ambient*(1-climate.y*0.16)+direct*max(dot(normal,sun),0)*shade;
        base+=vec3(0.65,0.75,1)*climate.z*(0.35+max(normal.z,0)*0.65);
    }
    output_color=vec4(pow(max(base,vec3(0)),vec3(1.0/2.2)),alpha_mode==2&&shadow_pass==0?alpha:1);
}");
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
