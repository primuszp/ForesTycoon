namespace ForesTycoon.Tests;
public class ForestryLogisticsTests
{
    private sealed class Habitat : IForestHabitat
    {
        public int TileCount=>4;public int Seed=>42;
        public bool CanSupportForest(int id)=>true;
        public float GetMoisture(int id)=>0.5f;
        public float GetNormalizedElevation(int id)=>0.5f;
        public int GetAdjacentTileIds(int id,Span<int> result)=>0;
    }
    [Fact] public void ExtractionReducesCubicMetresGraduallyAndCannotOverdraw()
    {
        var forest=new ForestSystem(new Habitat(),new[]{new ForestStand(ForestSpecies.Oak,50,1,1),default,default,default});
        Assert.Equal(100,ForestSystem.TimberCubicMetres(Get()));
        Assert.Equal(10,forest.ExtractTimber(0,10));
        Assert.Equal(90,ForestSystem.TimberCubicMetres(Get()),3);
        Assert.Equal(50,Get().AgeYears);
        Assert.Equal(90,forest.ExtractTimber(0,200),3);
        Assert.False(forest.TryGetStand(0,out _));Assert.Equal(0,forest.ExtractTimber(0,10));
        ForestStand Get(){Assert.True(forest.TryGetStand(0,out var stand));return stand;}
    }
    [Fact] public void LocalTruckLoadsAtSourceAndDeliversOnlyAtDestination()
    {
        float source=40,received=0;var inventory=new TimberCargoSystem();
        var vehicles=new VehicleSystem(inventory);
        vehicles.SourceLoader=(_,request)=>{float taken=Math.Min(source,request);source-=taken;return taken;};
        vehicles.DestinationReceiver=(_,amount)=>received+=amount;
        var truck=vehicles.SpawnLogistics(new[]{1,2,3},new[]{0},9);
        Assert.Equal(40,source);Assert.Equal(0,truck.CargoAmount);
        vehicles.Update(1);Assert.InRange(truck.CargoAmount,8,9);Assert.Equal(0,received);
        for(int i=0;i<1800;i++)vehicles.Update(1.0/30);
        Assert.InRange(source,0,0.001f);Assert.InRange(received,39.99f,40.01f);
        Assert.InRange(Math.Abs(inventory.Delivered-received),0,0.001f);
    }
    [Fact] public void NewModelsLoadWithTexturesAndFishAnimation()
    {
        var fish=AnimatedGlbModel.Load(Path.Combine(AppContext.BaseDirectory,"Assets","Wildlife","fish.glb"));
        Assert.Contains("ArmatureAction",fish.Clips.Keys);Assert.NotEmpty(fish.Skins);Assert.NotEmpty(fish.Images);
        var mill=AnimatedGlbModel.Load(Path.Combine(AppContext.BaseDirectory,"Assets","Buildings","sawmill.glb"));
        Assert.True(mill.Meshes.Length>1);Assert.NotEmpty(mill.Images);
    }
    [Fact] public void BrokenRoadKeepsLocalCargoAndResumesAfterRepair()
    {
        var vehicles=new VehicleSystem();vehicles.SourceLoader=(_,requested)=>requested;
        var truck=vehicles.SpawnLogistics(new[]{1,2,3},new[]{0},9);
        vehicles.Update(1);float cargo=truck.CargoAmount;
        Assert.Equal(0,vehicles.RemoveInvalidRoutes(id=>id!=2));Assert.True(truck.RouteBlocked);
        vehicles.Update(10);Assert.Equal(cargo,truck.CargoAmount);
        vehicles.RefreshLogisticsRoutes(_=>true);Assert.False(truck.RouteBlocked);
        vehicles.Update(1);Assert.True(truck.CargoAmount>cargo);
    }
}
