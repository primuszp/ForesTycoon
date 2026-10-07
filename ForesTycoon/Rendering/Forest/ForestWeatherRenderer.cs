using System;
using System.Collections.Generic;
using System.Drawing;
using OpenTK.Graphics.OpenGL;
using OpenTK.Mathematics;

namespace ForesTycoon
{
    internal sealed class ForestWeatherRenderer : IDisposable
    {
        private readonly List<FogSource> mist = new();
        private readonly List<Vector3> crowns = new();
        private readonly List<(Vector3 A, Vector3 B, float Width)> segments = new();
        private float[] upload = Array.Empty<float>();
        private long eventId = long.MinValue;
        private int fogProgram, vao, buffer, depthTexture, depthFramebuffer, depthWidth, depthHeight;
        private readonly List<(Vector4 Position, Vector4 Life)> particles = new();
        internal void Draw(Terrain terrain, ForestSystem forest, WeatherVisualState weather, GraphicsSettings settings, RenderContext context,EnvironmentSystem environment=null)
        {
            if((!settings.Fog || settings.FogDensity <= 0) && weather.Flash < 0.002f) return;
            terrain.CollectForestWeather(mist, forest, crowns, settings.Lightning && weather.Flash >= 0.002f && eventId != weather.LightningEvent,environment);
            var basis=FogParticleMotion.CameraBasis(context.CameraYaw,context.CameraTilt);
            Vector3 right=basis.Right, up=basis.Up;
            if(settings.Fog && settings.FogDensity > 0 && mist.Count>0)
            {
                if(fogProgram == 0) InitializeFog();

                particles.Clear();
                int sourceStride = Math.Max(1, (int)Math.Ceiling(mist.Count / (double)settings.FogSourceBudget));
                for(int sourceIndex = 0; sourceIndex < mist.Count; sourceIndex += sourceStride)
                {
                    FogSource source = mist[sourceIndex];
                    float heating=(1-weather.Cloud)*Math.Clamp(settings.SunElevation/80,0,1);
                    float density=FogHabitat.Density(source,weather.Time,weather.Wetness,weather.Wind.Length,heating);
                    if(density<0.015f) continue;
                    Vector4 anchor=source.Position;
                    for(int layer=0;layer<settings.FogLayers;layer++)
                    {
                        var sample=FogParticleMotion.Sample(anchor,layer,weather.Time);
                        Vector4 position=sample.Position;
                        if(!terrain.Map.TryGetSurfaceZ(position.X,position.Y,out float ground)) continue;
                        position.Z=Math.Max(ground,source.Water>0?source.Position.Z-1.4f:ground)+2.2f+layer*0.8f;
                        Vector4 life=sample.Life; life.X*=density;
                        particles.Add((position,life));
                    }
                }
                if (particles.Count > 0)
                {
                CaptureDepth();
                // Back-to-front ordering avoids view-dependent alpha seams.
                Vector3 forward=Vector3.Cross(right,up);
                particles.Sort((a,b)=>Vector3.Dot(a.Position.Xyz,forward).CompareTo(Vector3.Dot(b.Position.Xyz,forward)));
                GL.UseProgram(fogProgram);
                Matrix4 camera=RenderDevice.ViewProjection;
                Matrix4 inverse=camera.Inverted();
                GL.UniformMatrix4(GlProgram.Uniform(fogProgram,"inverse_camera"),false,ref inverse);
                GL.Uniform4(GlProgram.Uniform(fogProgram,"viewport"),(float)viewport[0],(float)viewport[1],(float)viewport[2],(float)viewport[3]);
                GL.ActiveTexture(TextureUnit.Texture3); GL.BindTexture(TextureTarget.Texture2D,depthTexture);
                GL.Uniform1(GlProgram.Uniform(fogProgram,"scene_depth"),3);
                GL.UniformMatrix4(GlProgram.Uniform(fogProgram,"camera"),false,ref camera);
                GL.Uniform3(GlProgram.Uniform(fogProgram,"right"),right);
                GL.Uniform3(GlProgram.Uniform(fogProgram,"up"),up);
                GL.Uniform3(GlProgram.Uniform(fogProgram,"climate"),(float)(weather.Time%4096),
                    settings.FogDensity,weather.Flash);
                GL.BindVertexArray(vao); GL.BindBuffer(BufferTarget.ArrayBuffer,buffer);
                if (upload.Length < particles.Count * 8) Array.Resize(ref upload, Math.Max(particles.Count * 8, upload.Length * 2));
                float[] data = upload;
                for(int i=0;i<particles.Count;i++){
                    var p=particles[i];
                    data[i*8]=p.Position.X; data[i*8+1]=p.Position.Y; data[i*8+2]=p.Position.Z; data[i*8+3]=p.Position.W;
                    data[i*8+4]=p.Life.X; data[i*8+5]=p.Life.Y; data[i*8+6]=p.Life.Z; data[i*8+7]=p.Life.W;
                }
                GL.BufferData(BufferTarget.ArrayBuffer,particles.Count*8*sizeof(float),data,BufferUsageHint.StreamDraw);
                using(new RenderStateScope().Enable(EnableCap.DepthTest).Disable(EnableCap.CullFace).AlphaBlend().DepthWrite(false))
                {
                    GL.DrawArraysInstanced(PrimitiveType.Triangles,0,6,particles.Count);
                    RenderMetrics.RecordDraw(particles.Count*6);
                }
                GL.BindVertexArray(0); GL.ActiveTexture(TextureUnit.Texture0);
                }
            }
            if(weather.Flash < 0.002f || !settings.Lightning) return;
            if(eventId != weather.LightningEvent)
            {
                eventId=weather.LightningEvent; segments.Clear();
                if(crowns.Count == 0) return;
                var random=new Random(unchecked((int)eventId*7919+20261003));
                Vector3 target=crowns[random.Next(crowns.Count)];
                terrain.GetWeatherBounds(out _,out Vector3 max);
                Vector3 start=target+new Vector3((random.NextSingle()-0.5f)*12,(random.NextSingle()-0.5f)*12,
                    Math.Max(24,max.Z+18-target.Z));
                Vector3 previous=start;
                const int steps=24;
                for(int i=1;i<=steps;i++)
                {
                    float t=i/(float)steps;
                    Vector3 point=Vector3.Lerp(start,target,t);
                    if(i<steps) point+=new Vector3((random.NextSingle()-0.5f)*3,(random.NextSingle()-0.5f)*3,0);
                    segments.Add((previous,point,1));
                    if(i%5==0 && i<steps-3)
                    {
                        Vector3 branch=point;
                        for(int j=0;j<4;j++){
                            Vector3 end=branch+new Vector3((random.NextSingle()-0.5f)*4,(random.NextSingle()-0.5f)*4,-1.4f);
                            segments.Add((branch,end,0.45f)); branch=end;
                        }
                    }
                    previous=point;
                }
            }
            if(segments.Count == 0) return;
            RenderDevice.Visuals.Kind=SurfaceKind.Plain;
            using(new RenderStateScope().Enable(EnableCap.DepthTest).Disable(EnableCap.CullFace).AlphaBlend().DepthWrite(false))
            {
                DynamicPrimitiveBatch.Draw(PrimitiveType.Quads,()=>{
                    float power=Math.Clamp(weather.Flash/0.22f,0,1);
                    foreach(var segment in segments)
                    {
                        Ribbon(segment.A,segment.B,right,0.24f*segment.Width,Color.FromArgb((int)(75*power),135,175,255));
                        Ribbon(segment.A,segment.B,right,0.055f*segment.Width,Color.FromArgb((int)(255*power),235,244,255));
                    }
                });
            }
        }
        private static void Ribbon(Vector3 a,Vector3 b,Vector3 right,float width,Color color)
        {
            Vector3 offset=right*width;
            DynamicPrimitiveBatch.Color4(color);
            DynamicPrimitiveBatch.Vertex3(a-offset); DynamicPrimitiveBatch.Vertex3(a+offset);
            DynamicPrimitiveBatch.Vertex3(b+offset); DynamicPrimitiveBatch.Vertex3(b-offset);
        }
        private void InitializeFog()
        {
            fogProgram=GlProgram.Create(@"#version 330 core
layout(location=0) in vec4 anchor;
layout(location=1) in vec4 life;
uniform mat4 camera;
uniform vec3 right,up;
out vec2 uv;
out vec3 world;
out vec4 particle;
const vec2 corners[6]=vec2[6](vec2(-1,-1),vec2(1,-1),vec2(1,1),vec2(-1,-1),vec2(1,1),vec2(-1,1));
void main(){
    uv=corners[gl_VertexID]; particle=life;
    world=anchor.xyz+right*uv.x*anchor.w+up*uv.y*(3.2+life.y);
    gl_Position=camera*vec4(world,1);
}",@"#version 330 core
in vec2 uv;
in vec3 world;
in vec4 particle;
uniform vec3 climate;
uniform mat4 inverse_camera;
uniform vec4 viewport;
uniform sampler2D scene_depth;
out vec4 output_color;
float hash(vec2 p){return fract(sin(dot(p,vec2(127.1,311.7)))*43758.5453);}
float noise(vec2 p){
    vec2 i=floor(p),f=fract(p); f=f*f*(3-2*f);
    return mix(mix(hash(i),hash(i+vec2(1,0)),f.x),mix(hash(i+vec2(0,1)),hash(i+vec2(1)),f.x),f.y);
}
void main(){
    vec2 screen=(gl_FragCoord.xy-viewport.xy)/viewport.zw;
    float depth=texture(scene_depth,screen).r;
    vec4 scene=inverse_camera*vec4(screen*2-1,depth*2-1,1);
    float intersection=clamp(length(scene.xyz/scene.w-world)/1.8,0,1);
    float edge=(1-smoothstep(0.5,1,length(uv)));
    vec2 q=uv*3.2+vec2(particle.z,particle.w)+vec2(climate.x*0.018,-climate.x*0.012);
    float density=noise(q)*0.65+noise(q*2.03)*0.25+noise(q*4.1)*0.1;
    float alpha=edge*smoothstep(0.1,0.6,density)*particle.x*climate.y*0.38*intersection;
    if(alpha<0.003) discard;
    output_color=vec4(vec3(0.74,0.79,0.81)+vec3(0.5,0.6,0.8)*climate.z,alpha);
}");
            vao=GL.GenVertexArray(); buffer=GL.GenBuffer();
            GL.BindVertexArray(vao); GL.BindBuffer(BufferTarget.ArrayBuffer,buffer);
            GL.EnableVertexAttribArray(0);
            GL.VertexAttribPointer(0,4,VertexAttribPointerType.Float,false,32,0);
            GL.VertexAttribDivisor(0,1);
            GL.EnableVertexAttribArray(1);
            GL.VertexAttribPointer(1,4,VertexAttribPointerType.Float,false,32,16);
            GL.VertexAttribDivisor(1,1); GL.BindVertexArray(0);
        }
        private readonly int[] viewport=new int[4];
        private void CaptureDepth()
        {
            GL.GetInteger(GetPName.Viewport,viewport);
            GL.GetInteger(GetPName.DrawFramebufferBinding,out int draw);
            GL.GetInteger(GetPName.ReadFramebufferBinding,out int read);
            try
            {
                if(depthTexture==0){depthTexture=GL.GenTexture(); depthFramebuffer=GL.GenFramebuffer();}
                GL.ActiveTexture(TextureUnit.Texture3); GL.BindTexture(TextureTarget.Texture2D,depthTexture);
                if(depthWidth!=viewport[2] || depthHeight!=viewport[3]){
                    depthWidth=viewport[2]; depthHeight=viewport[3];
                    GL.TexImage2D(TextureTarget.Texture2D,0,PixelInternalFormat.DepthComponent24,depthWidth,depthHeight,0,PixelFormat.DepthComponent,PixelType.Float,IntPtr.Zero);
                    GL.TexParameter(TextureTarget.Texture2D,TextureParameterName.TextureMinFilter,(int)TextureMinFilter.Nearest);
                    GL.TexParameter(TextureTarget.Texture2D,TextureParameterName.TextureMagFilter,(int)TextureMagFilter.Nearest);
                    GL.TexParameter(TextureTarget.Texture2D,TextureParameterName.TextureWrapS,(int)TextureWrapMode.ClampToEdge);
                    GL.TexParameter(TextureTarget.Texture2D,TextureParameterName.TextureWrapT,(int)TextureWrapMode.ClampToEdge);
                }
                GL.BindFramebuffer(FramebufferTarget.DrawFramebuffer,depthFramebuffer);
                GL.FramebufferTexture2D(FramebufferTarget.DrawFramebuffer,FramebufferAttachment.DepthAttachment,TextureTarget.Texture2D,depthTexture,0);
                GL.DrawBuffer(DrawBufferMode.None);
                if(GL.CheckFramebufferStatus(FramebufferTarget.DrawFramebuffer)!=FramebufferErrorCode.FramebufferComplete)
                    throw new InvalidOperationException("Fog depth framebuffer is incomplete.");
                GL.BindFramebuffer(FramebufferTarget.ReadFramebuffer,draw);
                GL.BlitFramebuffer(viewport[0],viewport[1],viewport[0]+depthWidth,viewport[1]+depthHeight,0,0,depthWidth,depthHeight,ClearBufferMask.DepthBufferBit,BlitFramebufferFilter.Nearest);
            }
            finally{
                GL.BindFramebuffer(FramebufferTarget.DrawFramebuffer,draw);
                GL.BindFramebuffer(FramebufferTarget.ReadFramebuffer,read);
                GL.ActiveTexture(TextureUnit.Texture0);
            }
        }
        public void Dispose()
        {
            if(fogProgram!=0) GlProgram.Delete(fogProgram);
            if(vao!=0) GL.DeleteVertexArray(vao);
            if(buffer!=0) GL.DeleteBuffer(buffer);
            if(depthTexture!=0) GL.DeleteTexture(depthTexture);
            if(depthFramebuffer!=0) GL.DeleteFramebuffer(depthFramebuffer);
            fogProgram=vao=buffer=depthTexture=depthFramebuffer=0;
        }
    }
}
