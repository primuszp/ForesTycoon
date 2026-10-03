using OpenTK.Mathematics;
namespace ForesTycoon.Tests;
public class AnimatedGlbTests
{
    [Fact]
    public void ElkAssetLoadsSkinTexturesAndDeformsEatingPoseDeterministically()
    {
        var model=AnimatedGlbModel.Load(Path.Combine(AppContext.BaseDirectory,"Assets","Wildlife","elk.glb"));
        Assert.Equal(38,model.Skins[0].Joints.Length);
        Assert.Equal(4,model.Meshes.Length);
        Assert.Contains("Stand_Eating_01",model.Clips.Keys);Assert.Contains("WalkSlow",model.Clips.Keys);
        Assert.True(model.Clips.Count>40);
        Assert.Equal(1024,model.Images[0].Width);
        var a=model.CreatePose();var b=model.CreatePose();
        a.Evaluate("Stand_Eating_01",0);b.Evaluate("Stand_Eating_01",2);
        Assert.Contains(Enumerable.Range(0,38),j=>a.JointMatrix(0,j)!=b.JointMatrix(0,j));
        float duration=model.Clips["Stand_Eating_01"].Duration;
        b.Evaluate("Stand_Eating_01",duration);
        for(int j=0;j<38;j++)Assert.Equal(a.JointMatrix(0,j),b.JointMatrix(0,j));
        b.Evaluate("Stand_Eating_01",0,"WalkSlow",0.5,0.5f);
        Assert.All(b.World,m=>Assert.True(float.IsFinite(m.M41)));
        // Weighted skin positions must be in metre-scale world space, including antlers.
        a.Evaluate("Stand_Eating_01",0);
        var mesh=model.Meshes[2];Vector3 min=new(float.MaxValue),max=new(float.MinValue);
        for(int i=0;i<mesh.Vertices.Length;i+=16) {
            var v=mesh.Vertices;Vector4 p=new(v[i],v[i+1],v[i+2],1),skinned=Vector4.Zero;
            for(int j=0;j<4;j++)skinned+=Vector4.TransformRow(p,a.JointMatrix(mesh.Skin,(int)v[i+8+j]))*v[i+12+j];
            min=Vector3.ComponentMin(min,skinned.Xyz);max=Vector3.ComponentMax(max,skinned.Xyz);
        }
        Assert.InRange(max.Y-min.Y,1,3);Assert.InRange(max.Z-min.Z,1,3);
    }
    [Fact]
    public void GrazingAndWalkingCycleIsRepeatableAndCrossfadesAtBoundaries()
    {
        Assert.False(WildlifeRenderer.Activity(20,0).Walking);
        Assert.Equal(0,WildlifeRenderer.Activity(30,0).Blend);
        Assert.Equal(1,WildlifeRenderer.Activity(35,0).Blend);
        Assert.Equal(WildlifeRenderer.Activity(35,0),WildlifeRenderer.Activity(75,0) with { ClipTime=5 });
    }
}
