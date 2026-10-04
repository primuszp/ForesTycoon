using System;
using System.Runtime.InteropServices;
using OpenTK.Mathematics;

namespace ForesTycoon
{
    [StructLayout(LayoutKind.Sequential)]
    internal readonly record struct ForestVertexGrowth(Vector3 Origin, Vector3 AnnualScale, float TreeIndex = -1)
    {
        internal const int Stride = 7 * sizeof(float);
        // Dead wood reuses the metadata layout: origin=root, rate=(-2,death year,
        // ground clearance), tree-index=yaw. It never samples living-tree texture slots.
        internal static ForestVertexGrowth DeadWood(Vector3 root, double deathYear, float clearance, float yaw)
            => new(root, new Vector3(-2, (float)deathYear, clearance), yaw);
        internal Vector3 At(Vector3 point, float elapsed) => Origin + (point - Origin) * (Vector3.One + AnnualScale * Math.Max(0, elapsed));

        // Identical transform in legacy, textured, outline and depth passes.
        internal const string Shader = @"
layout(location=3) in vec3 forest_origin;
layout(location=4) in vec3 forest_rate;
layout(location=5) in float forest_tree;
uniform float forest_elapsed;
uniform float forest_year;
uniform samplerBuffer forest_state;
uniform int forest_dynamic;
vec3 forestScale(){
    if(forest_rate.x < -1.5) return vec3(1);
    if(forest_dynamic != 0 && forest_tree >= 0 && forest_rate.x > -1.5) {
        int slot=int(forest_tree)*2;
        vec3 scale=texelFetch(forest_state,slot).xyz + texelFetch(forest_state,slot+1).xyz * max(0,forest_elapsed);
        return forest_rate.x < 0 ? scale.yyz : scale.xxz;
    }
    return vec3(1) + forest_rate * max(0, forest_elapsed);
}
vec4 forestTint(vec4 c){
    if(forest_dynamic != 0 && forest_tree >= 0 && forest_rate.x > -1.5) {
        float health=texelFetch(forest_state,int(forest_tree)*2).w;
        c.rgb=mix(c.rgb,vec3(146,118,58)/255.0,clamp(1-health,0,1)*0.55);
    }
    return c;
}
vec3 forestDeadVector(vec3 v){
    float c=cos(forest_tree), s=sin(forest_tree);
    return vec3(v.z*c-v.y*s, v.z*s+v.y*c, -v.x);
}
bool forestFallen(){return forest_rate.x < -1.5 && forest_year >= forest_rate.y + 2;}
vec3 forestPoint(vec3 p){
    if(forestFallen()) return forest_origin + forestDeadVector(p-forest_origin) + vec3(0,0,forest_rate.z);
    return forest_origin + (p - forest_origin) * forestScale();
}
vec3 forestNormal(vec3 n){
    vec3 v=forestFallen() ? forestDeadVector(n) : n / forestScale();
    return length(v)>0 ? normalize(v) : vec3(0);
}
";
    }
}
