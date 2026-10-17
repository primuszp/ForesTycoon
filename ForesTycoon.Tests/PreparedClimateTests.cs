namespace ForesTycoon.Tests;

public class PreparedClimateTests
{
    private sealed class Habitat : IForestHabitat
    {
        public int TileCount => 128 * 128;
        public int Seed => 42;
        public (int Columns, int Rows) TileGrid => (128, 128);
        public bool CanSupportForest(int id) => true;
        public float GetMoisture(int id) => .5f;
        public float GetNormalizedElevation(int id) => id % 17 / 17f;
        public int GetAdjacentTileIds(int id, Span<int> result) => 0;
    }

    [Fact]
    public void PreparedQueriesMatchLazyQueriesAfterWeatherAndProgramChanges()
    {
        var habitat = new Habitat();
        var prepared = new EnvironmentSystem(habitat, null, climate: ClimateDefinition.Default);
        var lazy = new EnvironmentSystem(habitat, null, climate: ClimateDefinition.Default);
        var model = new BehaviorModel { Graphs = EcologyResponseProgram.Hooks.Select(EcologyResponseProgram.DefaultGraph).ToList() };
        for (int pass = 0; pass < 4; pass++) {
            if (pass == 2) {
                var graph = model.Graphs.Single(g => g.Hook == "climate.temperature");
                graph.Nodes = new() { new() { Id = "constant", Name = "Temperature", Operation = BehaviorOperation.Constant, Value = 7 } }; graph.Output = "constant";
            }
            if (pass == 3) {
                var graph = model.Graphs.Single(g => g.Hook == "weather.evaporation");
                graph.Nodes = new() {
                    new() { Id = "zero", Name = "Zero", Operation = BehaviorOperation.Constant },
                    new() { Id = "error", Name = "Invalid evaporation", Operation = BehaviorOperation.Divide, A = "zero", B = "zero" }
                }; graph.Output = "error";
            }
            prepared.ResponseRules = new(model); lazy.ResponseRules = new(model);
            var weather = pass == 0 ? WeatherPreset.Rain : WeatherPreset.Snow;
            prepared.ForceWeather(weather, 20, 100); lazy.ForceWeather(weather, 20, 100);
            SimulationWork.Complete(prepared.PrepareRuleClimateWork(true));
            Assert.Equal(lazy.ResponseRules.LastError, prepared.ResponseRules.LastError);
            for (int id = habitat.TileCount - 1; id >= 0; id--) {
                Assert.Equal(lazy.LocalClimate(id), prepared.LocalClimate(id));
                Assert.Equal(lazy.PotentialEvaporation(id), prepared.PotentialEvaporation(id));
            }
            Assert.Equal(lazy.ResponseRules.LastError, prepared.ResponseRules.LastError);
        }
    }
}
