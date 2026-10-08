using OpenTK.Mathematics;
namespace ForesTycoon.Tests;
public class AnimatedGlbTests
{
    [Fact]
    public void WalkingClockMatchesTheRenderedAssetStride()
    {
        var model=AnimatedGlbModel.Load(Path.Combine(AppContext.BaseDirectory,"Assets","Wildlife","elk.glb"));
        int root=Array.FindIndex(model.Nodes,node=>node.Name=="RigRoot_01");
        var clip=model.Clips["WalkSlow"];
        var channel=Assert.Single(clip.Channels,c=>c.Node==root&&c.Path==AnimatedGlbModel.AnimationPath.Translation);
        var pose=model.CreatePose();pose.Evaluate("WalkSlow",0);
        Vector3 local=channel.Sample(clip.Duration).Xyz-channel.Sample(0).Xyz;
        Vector3 world=Vector3.TransformVector(local,pose.World[model.Nodes[root].Parent]);
        float speed=world.Length*DioramaScale.Elk/clip.Duration;
        Assert.InRange(MathF.Abs(speed-WildlifeSystem.WalkingClipSpeed),0,0.001f);
    }
    [Fact]
    public void WalkingRootMotionDoesNotResetRenderedBodyAtLoopBoundary()
    {
        var model=AnimatedGlbModel.Load(Path.Combine(AppContext.BaseDirectory,"Assets","Wildlife","elk.glb"));
        int root=Array.FindIndex(model.Nodes,node=>node.Name=="RigRoot_01");
        Assert.True(root>=0);
        double duration=model.Clips["WalkSlow"].Duration;
        var before=model.CreatePose();var after=model.CreatePose();
        before.Evaluate("WalkSlow",duration-0.001);
        after.Evaluate("WalkSlow",duration+0.001);
        // Reproduce the original visible snap, rather than just testing world position.
        Assert.True((before.World[root].Row3-after.World[root].Row3).Length>1);
        before.Evaluate("Stand_Eating_01",2,"WalkSlow",duration-0.001,1,inPlaceRoot:root);
        after.Evaluate("Stand_Eating_01",2,"WalkSlow",duration+0.001,1,inPlaceRoot:root);
        Assert.InRange((before.World[root].Row3-after.World[root].Row3).Length,0,0.001f);
        foreach(var mesh in model.Meshes.Where(mesh=>mesh.Skin>=0))
        for(int i=0;i<mesh.Vertices.Length;i+=16) {
            var v=mesh.Vertices;Vector4 vertex=new(v[i],v[i+1],v[i+2],1);
            Vector4 first=Vector4.Zero,second=Vector4.Zero;
            for(int j=0;j<4;j++) {
                int joint=(int)v[i+8+j];float weight=v[i+12+j];
                first+=Vector4.TransformRow(vertex,before.JointMatrix(mesh.Skin,joint))*weight;
                second+=Vector4.TransformRow(vertex,after.JointMatrix(mesh.Skin,joint))*weight;
            }
            Assert.InRange((first-second).Length,0,0.03f);
        }
    }
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
    public void CrossfadeEndpointsMatchEatingAndWalkingPoses()
    {
        var model=AnimatedGlbModel.Load(Path.Combine(AppContext.BaseDirectory,"Assets","Wildlife","elk.glb"));
        var eating=model.CreatePose();var walking=model.CreatePose();var blended=model.CreatePose();
        eating.Evaluate("Stand_Eating_01",2);walking.Evaluate("WalkSlow",0.5);
        blended.Evaluate("Stand_Eating_01",2,"WalkSlow",0.5,0);
        Assert.Equal(eating.World,blended.World);
        blended.Evaluate("Stand_Eating_01",2,"WalkSlow",0.5,1);
        for(int i=0;i<walking.World.Length;i++) {
            Assert.InRange((walking.World[i].Row0-blended.World[i].Row0).Length,0,0.0001f);
            Assert.InRange((walking.World[i].Row1-blended.World[i].Row1).Length,0,0.0001f);
            Assert.InRange((walking.World[i].Row2-blended.World[i].Row2).Length,0,0.0001f);
            Assert.InRange((walking.World[i].Row3-blended.World[i].Row3).Length,0,0.0001f);
        }
    }
}
