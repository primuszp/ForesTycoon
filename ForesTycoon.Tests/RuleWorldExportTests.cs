using System.Text.Json;

namespace ForesTycoon.Tests;

[Collection("SpeciesBook")]
public sealed class RuleWorldExportTests
{
    private static GameWorld World() => new(TerrainSettings.Default.WithNodeSize(17, 42), enableRendering: false);

    [Fact]
    public async Task SlicedSnapshotIsBoundedAndRemainsIndependentOfLaterCommandsAndTicks()
    {
        using var world = World();
        var expected = world.ExportRuleWorld("test");
        string expectedRevision = world.RuleProjectDocument.ContentRevision();
        Task<string>? serialized = null;
        world.RequestRuleWorldExport("test", null, task => serialized = task);
        Assert.True(world.ForegroundPending); Assert.True(world.BoundaryPending);
        Assert.False(world.ContinueUpdate(1d / 30)); Assert.Equal(0UL, world.SimulationTick);
        var replacement = world.RuleProjectDocument; replacement.Name = "After snapshot";
        world.QueueRuleProject(replacement);
        int slices = 0;
        while (world.RuleExportPending) {
            world.ContinueCommands(7); slices++;
            Assert.InRange(world.LastRuleExportWorkUnits, 0, 7);
            Assert.NotEqual(replacement.Name, world.ActiveRuleProjectName);
            Assert.True(slices < 100000);
        }
        Assert.True(slices > 10); Assert.NotNull(serialized);
        world.ExecutePendingCommands(); world.Update(1d / 30);
        var actual = RuleWorldCatalog.FromJson(await serialized!.WaitAsync(TimeSpan.FromSeconds(30)));
        Assert.Equal(0UL, actual.Tick); Assert.Equal(expected.ProjectName, actual.ProjectName);
        Assert.Equal(expectedRevision, actual.ProjectRevision);
        Assert.Equal(expected.Objects, actual.Objects);
        Assert.Equal(JsonSerializer.Serialize(expected.Fields), JsonSerializer.Serialize(actual.Fields));
        Assert.Equal(replacement.Name, world.ActiveRuleProjectName); Assert.Equal(1UL, world.SimulationTick);
    }

    [Fact]
    public async Task FileLinkPublishesACompleteSnapshotOffThePollingCall()
    {
        string directory = Path.Combine(Path.GetTempPath(), "forestycoon-export-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try {
            using var world = World(); string path = Path.Combine(directory, "project.json");
            RuleProject.WriteAtomic(path, world.RuleProjectDocument.ToJson());
            var link = new RuleProjectLink(path, adoptRunning: true, bootstrapAlgorithms: false);
            link.Poll(world, 0);
            Assert.True(world.RuleExportPending); Assert.True(link.ExportPending);
            Assert.False(File.Exists(path + ".world.json"));
            await FinishExport(world, link);
            var exported = RuleWorldCatalog.FromJson(File.ReadAllText(path + ".world.json"));
            Assert.Equal(world.RuleProjectDocument.ContentRevision(), exported.ProjectRevision);
            Assert.Contains(exported.Objects, o => o.Kind == "tree");
            Assert.Null(link.Error); Assert.False(link.ExportPending);
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task ReplacingTheWorldCancelsAnUnfinishedExportAndPublishesTheNewMap()
    {
        string directory = Path.Combine(Path.GetTempPath(), "forestycoon-export-cancel-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try {
            using var world = World(); string path = Path.Combine(directory, "project.json");
            RuleProject.WriteAtomic(path, world.RuleProjectDocument.ToJson());
            var link = new RuleProjectLink(path, adoptRunning: true, bootstrapAlgorithms: false);
            link.Poll(world, 0); world.ContinueCommands(1); Assert.True(world.RuleExportPending);
            world.Regenerate(TerrainSettings.Default.WithNodeSize(17, 43));
            Assert.False(world.RuleExportPending);
            link.Poll(world, 0); await FinishExport(world, link);
            Assert.Equal(43, RuleWorldCatalog.FromJson(File.ReadAllText(path + ".world.json")).Seed);
        }
        finally { Directory.Delete(directory, true); }
    }

    internal static async Task FinishExport(GameWorld world, RuleProjectLink link)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        while (link.ExportPending || world.RuleExportPending) {
            if (world.RuleExportPending) world.ContinueCommands(256);
            link.Poll(world, 0);
            await Task.Delay(1, timeout.Token);
        }
    }

    [Fact]
    public async Task FailedBackgroundWriteDoesNotFaultTheSimulationOrLeaveTemporaryFiles()
    {
        string directory = Path.Combine(Path.GetTempPath(), "forestycoon-export-fail-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try {
            using var world = World(); string path = Path.Combine(directory, "project.json");
            RuleProject.WriteAtomic(path, world.RuleProjectDocument.ToJson());
            Directory.CreateDirectory(path + ".world.json");
            var link = new RuleProjectLink(path, adoptRunning: true, bootstrapAlgorithms: false);
            link.Poll(world, 0); await FinishExport(world, link);
            Assert.NotNull(link.Error); Assert.False(world.IsFaulted);
            Assert.Empty(Directory.GetFiles(directory, "*.tmp"));
            world.Update(1d / 30); Assert.Equal(1UL, world.SimulationTick);
        }
        finally { Directory.Delete(directory, true); }
    }
}
