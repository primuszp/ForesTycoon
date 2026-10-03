using System;
using OpenTK.Graphics.OpenGL;
using OpenTK.Mathematics;

namespace ForesTycoon
{
    // A bounded low-cost volume above the diorama; no geometry-cache mutations.
    internal sealed class CloudRenderer : IDisposable
    {
        private int program, vao;
        internal void Draw(Terrain terrain, WeatherVisualState weather, GraphicsSettings settings)
        {
            if(weather.Cloud < 0.01f) return;
            if(program == 0) Initialize();
            terrain.GetWeatherBounds(out Vector3 min, out Vector3 max);
            Matrix4 inverse = RenderDevice.ViewProjection.Inverted();
            GL.UseProgram(program);
            GL.Uniform1(GlProgram.Uniform(program,"steps"),settings.CloudSteps);
            GL.UniformMatrix4(GlProgram.Uniform(program, "inverse_camera"), false, ref inverse);
            GL.Uniform4(GlProgram.Uniform(program, "climate"), weather.Cloud, weather.Storm, weather.Flash, (float)(weather.Time % 4096));
            GL.Uniform1(GlProgram.Uniform(program, "height"), max.Z + 18);
            using(new RenderStateScope().Disable(EnableCap.DepthTest).Disable(EnableCap.CullFace).AlphaBlend().DepthWrite(false))
            {
                GL.BindVertexArray(vao);
                GL.DrawArrays(PrimitiveType.Triangles, 0, 3);
                RenderMetrics.RecordDraw(3);
                GL.BindVertexArray(0);
            }
        }
        private void Initialize()
        {
            program = GlProgram.Create(@"#version 330 core
out vec2 uv;
void main(){
    vec2 p=vec2((gl_VertexID<<1)&2,gl_VertexID&2);
    uv=p; gl_Position=vec4(p*2-1,0,1);
}", @"#version 330 core
in vec2 uv;
out vec4 output_color;
uniform mat4 inverse_camera;
uniform vec4 climate;
uniform float height;
uniform int steps;
float hash2(vec2 p){return fract(sin(dot(p,vec2(127.1,311.7)))*43758.5453);}
float noise2(vec2 p){
    vec2 i=floor(p),f=fract(p); f=f*f*(3-2*f);
    return mix(mix(hash2(i),hash2(i+vec2(1,0)),f.x),mix(hash2(i+vec2(0,1)),hash2(i+vec2(1)),f.x),f.y);
}
float hash3(vec3 p){return fract(sin(dot(p,vec3(127.1,311.7,74.7)))*43758.5453);}
float noise3(vec3 p){
    vec3 i=floor(p),f=fract(p); f=f*f*(3-2*f);
    return mix(mix(mix(hash3(i),hash3(i+vec3(1,0,0)),f.x),
        mix(hash3(i+vec3(0,1,0)),hash3(i+vec3(1,1,0)),f.x),f.y),
        mix(mix(hash3(i+vec3(0,0,1)),hash3(i+vec3(1,0,1)),f.x),
        mix(hash3(i+vec3(0,1,1)),hash3(i+vec3(1)),f.x),f.y),f.z);
}
void main(){
    vec4 a=inverse_camera*vec4(uv*2-1,-1,1);
    vec4 b=inverse_camera*vec4(uv*2-1,1,1);
    vec3 origin=a.xyz/a.w, direction=normalize(b.xyz/b.w-origin);
    if(abs(direction.z)<0.001){output_color=vec4(0);return;}
    float entry=(height-origin.z)/direction.z;
    vec3 p=origin+direction*entry;
    float transmittance=1; vec3 scattered=vec3(0);
    // Front-to-back Beer-Lambert integration through a thin cloud slab.
    for(int i=0;i<steps;i++){
        vec3 q=(p+direction*float(i)*1.3*12.0/float(steps))/vec3(42,42,14);
        q.xy-=vec2(climate.w*0.012,climate.w*0.004);
        float n=noise3(q)*0.65+noise3(q*2.03)*0.25+noise3(q*4.1)*0.1;
        // The same coverage field drives the ground shadow shader.
        float coverage=noise2(q.xy)*0.65+noise2(q.xy*2.03)*0.25+noise2(q.xy*4.1)*0.1;
        float density=smoothstep(0.28,0.72,coverage)*(0.65+0.35*n)*climate.x;
        float alpha=1-exp(-density*0.19*12.0/float(steps));
        vec3 illumination=mix(vec3(0.92,0.94,0.96),vec3(0.32,0.37,0.44),climate.y)
            *(0.75+0.25*n)+vec3(0.6,0.7,1)*climate.z;
        scattered+=transmittance*alpha*illumination;
        transmittance*=1-alpha;
    }
    float alpha=1-transmittance;
    output_color=vec4(scattered/max(alpha,0.001),alpha*0.72);
}");
            vao=GL.GenVertexArray();
        }
        public void Dispose()
        {
            if(program != 0) GlProgram.Delete(program);
            if(vao != 0) GL.DeleteVertexArray(vao);
            program=vao=0;
        }
    }
}
