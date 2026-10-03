using OpenTK.Mathematics;
namespace ForesTycoon.Tests;
public class VehicleVisualMotionTests
{
    private static VehicleRoadRoute Corner()=>new(
        new[]{new Vector3(-10,0,0),Vector3.Zero,new Vector3(10,0,0),new Vector3(10,10,0),new Vector3(10,20,0)},
        new Vector2[5]);
    [Fact]
    public void TruckTravelsThroughCornerWithoutStoppingOrTurningInPlace()
    {
        var road=Corner();var truck=new Vehicle(1,new[]{0,1,2,3,4},1.5,roadRoute:road);
        int samples=0;Vector3 previous=Vector3.Zero;bool seen=false;
        for(int i=0;i<1000 && truck.RoutePosition<3.2;i++){
            truck.Update(1.0/60);
            if(truck.RoutePosition<1.2)continue;
            Assert.True(truck.CurrentSpeed>0.1);
            road.GetPose(truck.RoutePosition,out var p,out var f,out _,out _,4.5f);
            if(seen)Assert.True((p-previous).Length>0.00001f);
            previous=p;seen=true;samples++;
        }
        Assert.True(samples>50);
        float before=road.Curvature(1.1),turn=road.Curvature(2);
        Assert.InRange(MathF.Abs(before),0,0.001f);Assert.True(turn>0.1);
        Assert.True(road.Curvature(6)<0);
        Vector3? heading=null;
        for(double p=1.0;p<=3;p+=0.005){
            road.GetPose(p,out _,out var f,out _,out _,4.5f);
            if(heading.HasValue)Assert.True(Vector3.Dot(heading.Value,f)>0.999f);
            heading=f;
        }
    }
    [Fact]
    public void InnerFrontWheelSteersMoreThanOuterAndStraightWheelsStayStraight()
    {
        Assert.Equal(0,VehicleVisualMotion.Steering(0,4,1));
        float inner=VehicleVisualMotion.Steering(0.15f,4,1);
        float outer=VehicleVisualMotion.Steering(0.15f,4,-1);
        Assert.True(inner>outer);Assert.True(outer>0);
        Assert.Equal(-inner,VehicleVisualMotion.Steering(-0.15f,4,-1),4);
    }
    [Fact]
    public void RoughRoadExcitesBodyButUniformSlopeDoesNot_AndStoppedTruckIsStill()
    {
        var smooth=new VehicleRoadRoute(new[]{new Vector3(0,0,0),new Vector3(10,0,2),new Vector3(20,0,4)},new[]{new Vector2(.2f,0),new Vector2(.2f,0),new Vector2(.2f,0)});
        var rough=new VehicleRoadRoute(new[]{Vector3.Zero,new Vector3(10,0,2),new Vector3(20,0,0)},new[]{new Vector2(.2f,0),new Vector2(0,0),new Vector2(-.2f,0)});
        Assert.InRange(smooth.Roughness(1,4),0,0.0001f);
        Assert.True(rough.Roughness(1,4)>0.1);
        var calm=VehicleVisualMotion.Suspension(1,1,0,0);
        var bumpy=VehicleVisualMotion.Suspension(1,1,1,0);
        Assert.True(MathF.Abs(bumpy.M43)>MathF.Abs(calm.M43)*5);
        Assert.Equal(Matrix4.Identity,VehicleVisualMotion.Suspension(1,0,1,1));
        Assert.Equal(bumpy,VehicleVisualMotion.Suspension(1,1,1,0));
    }
}
