using System.Runtime.CompilerServices;

namespace ForesTycoon.Tests;

[Collection("SpeciesBook")]
public sealed class MemoryPerformanceTests
{
    [Fact]
    public void ReloadAndDisposalReleaseRetiredWorldGraphsAndSpeciesContexts()
    {
        var retired = CreateReloadAndDispose();
        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        Assert.All(retired, reference => Assert.False(reference.IsAlive));
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static List<WeakReference> CreateReloadAndDispose()
    {
        var retired = new List<WeakReference>();
        using var world = SimulationSoak.Create(16, 42);
        using var save = new MemoryStream(); world.Save(save);
        for (int i = 0; i < 4; i++) TrackAndReload(world, save, retired);
        retired.Add(new(world)); retired.Add(new(world.Map)); retired.Add(new(world.Forest));
        retired.Add(new(world.WithSpecies(ForestSpeciesBook.CaptureContext)));
        return retired;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void TrackAndReload(SimulationWorld world, MemoryStream save, List<WeakReference> retired)
    {
        retired.Add(new(world.Map)); retired.Add(new(world.Forest)); retired.Add(new(world.Logistics)); retired.Add(new(world.RuleBook));
        retired.Add(new(world.WithSpecies(ForestSpeciesBook.CaptureContext)));
        save.Position = 0; world.Load(save);
    }

    [Fact]
    public async Task SpeciesScopesRestoreExceptionsAndFlowIntoBackgroundJobs()
    {
        object previous = ForestSpeciesBook.CaptureContext();
        var context = ForestSpeciesBook.Compose(new Dictionary<ForestSpecies, ForestSpeciesEdit> {
            [ForestSpecies.Oak] = new(AnnualGrowthRate: .123f)
        });
        Task<float> worker;
        using (ForestSpeciesBook.EnterContext(context)) {
            worker = Task.Run(() => ForestSpeciesProfile.For(ForestSpecies.Oak).AnnualGrowthRate);
            Assert.Throws<InvalidOperationException>((Action)(() => {
                using var nested = ForestSpeciesBook.EnterContext(null);
                throw new InvalidOperationException();
            }));
            Assert.Same(context, ForestSpeciesBook.CaptureContext());
            long start = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 1000; i++) { using var nested = ForestSpeciesBook.EnterContext(context); }
            Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - start);
        }
        Assert.Same(previous, ForestSpeciesBook.CaptureContext());
        Assert.Equal(.123f, await worker);
    }

    [Fact]
    public void KinematicWorkspaceDoesNotScaleItsAllocationWithSubstepsOrMutatePublishedStates()
    {
        var profile = VehicleGeometryProfiles.Truck(GameTuning.Default);
        var rig = new VehicleKinematics(profile, default, 0);
        rig.Advance(1, 0, .1);
        var saved = rig.CapturePresentation(); var published = rig.Units;
        var poses = published.ToArray();
        long start = GC.GetAllocatedBytesForCurrentThread();
        Assert.True(rig.Advance(10, 0, 10)); // 2000 numerical substeps, one published pose array.
        long allocated = GC.GetAllocatedBytesForCurrentThread() - start;
        Assert.InRange(allocated, 1, 256);
        Assert.Equal(poses, published); Assert.Equal(poses, saved.Units);
        Assert.NotEqual(poses[0], rig.Units[0]);
        var restored = VehicleKinematics.Restore(saved);
        Assert.Equal(poses, restored.Units);
    }
}
