namespace ForesTycoon.Tests;

[Collection("SpeciesBook")]
public sealed class SimulationSoakTests
{
    [Fact]
    public void AuthoredForestAndFleetSurviveOneMonthOfSlicingAndReload()
    {
        string directory = Path.Combine(Path.GetTempPath(), "forestycoon-soak-" + Guid.NewGuid().ToString("N"));
        try {
            SimulationSoak.Run(1, 16, 42, directory);
            var rows = File.ReadAllLines(Path.Combine(directory, "months.jsonl"));
            Assert.Single(rows);
            using var row = System.Text.Json.JsonDocument.Parse(rows[0]);
            Assert.Equal(2250, row.RootElement.GetProperty("tick").GetInt32());
            Assert.True(row.RootElement.GetProperty("exact").GetBoolean());
        } finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
}
