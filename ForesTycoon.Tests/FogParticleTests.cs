using OpenTK.Mathematics;
namespace ForesTycoon.Tests;
public class FogParticleTests
{
    [Theory]
    [InlineData(-45,-45)]
    [InlineData(0,-25)]
    [InlineData(120,-70)]
    public void BillboardFacesCamera(float yaw,float tilt)
    {
        var basis=FogParticleMotion.CameraBasis(yaw,tilt);
        Matrix4 view=Matrix4.CreateRotationZ(MathHelper.DegreesToRadians(yaw))*
            Matrix4.CreateRotationX(MathHelper.DegreesToRadians(tilt));
        var right=Vector4.TransformRow(new Vector4(basis.Right,0),view);
        var up=Vector4.TransformRow(new Vector4(basis.Up,0),view);
        Assert.True((right.Xyz-Vector3.UnitX).Length<0.0001f);
        Assert.True((up.Xyz-Vector3.UnitY).Length<0.0001f);
    }
    [Fact]
    public void ParticlesMoveAndRemainDeterministicWithBoundedLifetimeOpacity()
    {
        var anchor=new Vector4(12,17,3,4);
        var first=FogParticleMotion.Sample(anchor,1,4);
        Assert.Equal(first,FogParticleMotion.Sample(anchor,1,4));
        Assert.NotEqual(first.Position,FogParticleMotion.Sample(anchor,1,5).Position);
        for(int i=0;i<2000;i++){
            var sample=FogParticleMotion.Sample(anchor,1,i*0.1);
            Assert.InRange(sample.Life.X,0,1);
            Assert.True(float.IsFinite(sample.Position.X));
            Assert.True(sample.Position.W>0);
        }
    }
}
