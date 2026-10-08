using ForesTycoon;
using Xunit;

public class VehicleDynamicsTests
{
    private static readonly TruckSpec Spec = TruckSpec.Default;

    [Fact]
    public void LoadedTruck_ClimbsSlowerThanEmpty()
    {
        float empty = VehicleDynamics.BalanceSpeed(Spec, VehicleDynamics.Mass(Spec, 0), 0.08f, RoadSurface.Gravel, 0);
        float loaded = VehicleDynamics.BalanceSpeed(Spec, VehicleDynamics.Mass(Spec, 25), 0.08f, RoadSurface.Gravel, 0);
        Assert.True(loaded < empty * 0.7f);
        Assert.InRange(loaded, 3f, 12f);
    }

    [Fact]
    public void Climb_SlowsTruckAtFullThrottle()
    {
        float mass = VehicleDynamics.Mass(Spec, 25), speed = 20;
        for (int i = 0; i < 600; i++) speed = VehicleDynamics.Step(Spec, mass, speed, 25, 0.1f, RoadSurface.Gravel, 0, 0.1f).Speed;
        Assert.InRange(speed, 0.5f, VehicleDynamics.BalanceSpeed(Spec, mass, 0.1f, RoadSurface.Gravel, 0) + 0.2f);
    }

    [Fact]
    public void RougherSurfaceAndClimb_BurnMoreFuel()
    {
        RoadSurface smooth = RoadSurface.Asphalt, rough = RoadSurface.Dirt;
        float mass = VehicleDynamics.Mass(Spec, 25);
        float Fuel(RoadSurface s, float grade, float roughness) =>
            VehicleDynamics.Step(Spec, mass, 15, 15, grade, s, roughness, 1).Fuel;
        Assert.True(Fuel(rough, 0, 0) > Fuel(smooth, 0, 0));
        Assert.True(Fuel(smooth, 0, 0.8f) > Fuel(smooth, 0, 0));
        Assert.True(Fuel(smooth, 0.05f, 0) > Fuel(smooth, 0, 0) * 2);
    }

    [Fact]
    public void Downhill_BrakesInsteadOfRunningAway()
    {
        float mass = VehicleDynamics.Mass(Spec, 25), speed = 10;
        for (int i = 0; i < 300; i++) speed = VehicleDynamics.Step(Spec, mass, speed, 10, -0.12f, RoadSurface.Gravel, 0, 0.1f).Speed;
        Assert.InRange(speed, 9.9f, 10.01f);
    }
}
