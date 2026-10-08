namespace ForesTycoon.Models.OpenGl
{
    internal static class OpenGlModelShaders
    {
        internal const string Vertex = @"#version 330 core
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
}";
        internal const string Fragment = @"#version 330 core
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
}";
    }
}
