using OpenTK.Mathematics;

namespace ForesTycoon.Tests;

public class EngineReliabilityTests
{
    [Fact]
    public void GpuGrowthPreservesSizeAcrossMonthlyRateChanges()
    {
        var mesh = new ForestTreeDimensions(.2f, 12, 3);
        var tree = new ForestTree(1, 0, ForestSpecies.Oak, .5f, .5f, 42, -20, 0,
            mesh, new(.01f, .4f, .1f), .7f);
        double month = 1.0 / 12;
        var before = ForestTreeRenderState.Create(tree, mesh, 0);
        var settled = tree.Settle(month) with { AnnualGrowth = new(.02f, .2f, .3f), Health = .6f };
        var after = ForestTreeRenderState.Create(settled, mesh, month);
        Assert.True((before.Scale.Xyz + before.Rate.Xyz * (float)month - after.Scale.Xyz).Length < 1e-6f);
        var expected = settled.At(month + .03);
        Vector3 actual = (after.Scale.Xyz + after.Rate.Xyz * .03f) * new Vector3(mesh.Diameter, mesh.CrownRadius, mesh.Height);
        Assert.True((actual - new Vector3(expected.Diameter, expected.CrownRadius, expected.Height)).Length < 1e-5f);
        Assert.Equal(settled.Health, after.Scale.W);
    }

    [Fact]
    public void RecreatedPatchHasNewTopologyRevisionEvenWhenTreeCountMatches()
    {
        var store = new ForestTreeStore();
        var stand = new ForestStand(ForestSpecies.Oak, 20, .5f, 1);
        store.Create(0, stand, 0, 42);
        store.TryGet(0, out var first);
        ulong revision = first.TopologyRevision;
        store.RemoveTile(0);
        store.Create(0, stand, 0, 42);
        store.TryGet(0, out var replacement);
        Assert.Equal(first.Count, replacement.Count);
        Assert.True(replacement.TopologyRevision > revision);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReplayValidationRejectsInvalidCommandOrder(bool future)
    {
        var command = new SpawnVehicleCommand();
        var save = new WorldSaveData { Tick = 5,
            Commands = future ? [command.ToRecord(6)] : [command.ToRecord(4), command.ToRecord(3)] };
        Assert.Throws<InvalidDataException>(save.ValidateReplay);
    }

    [Fact]
    public void PreviousEcologySaveVersionCannotReplayWithChangedGrowthRules()
    {
        var save = new WorldSaveData { Version = 3 };
        Assert.Throws<NotSupportedException>(save.ValidateReplay);
    }

    [Fact]
    public void FailedSaveKeepsPreviousFileAndRemovesTemporaryFile()
    {
        string directory = Path.Combine(Path.GetTempPath(), "forestycoon-save-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "save.json");
        try
        {
            File.WriteAllText(path, "previous");
            Assert.Throws<IOException>(() => SaveGameFile.Write(path, stream => {
                stream.WriteByte(42);
                throw new IOException("Injected write failure");
            }));
            Assert.Equal("previous", File.ReadAllText(path));
            Assert.Single(Directory.GetFiles(directory));
            SaveGameFile.Write(path, stream => stream.WriteByte(65));
            Assert.Equal("A", File.ReadAllText(path));
        }
        finally { Directory.Delete(directory, true); }
    }
}
