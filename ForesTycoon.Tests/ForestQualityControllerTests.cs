using System.IO;

namespace ForesTycoon.Tests;

public class ForestQualityControllerTests
{
    [Fact]
    public void CameraScaleDoesNotChangeAdaptiveModelAndSustainedHeadroomPromotes()
    {
        var controller = Adaptive();
        Assert.Equal(ForestLod.Medium, controller.Select(1, null));
        Assert.Equal(ForestLod.Medium, controller.Select(30, null));

        Samples(controller, 0, 35, frame: 7, forest: 2, nearFits: true);
        Assert.Equal(ForestLod.Medium, controller.AdaptiveLod);
        Samples(controller, 35, 12, frame: 7, forest: 2, nearFits: true);
        Assert.Equal(ForestLod.Near, controller.Select(1, ForestLod.Medium));
        Assert.Equal(ForestLod.Near, controller.Select(30, ForestLod.Medium));
    }

    [Fact]
    public void ASlowForestReducesQualityAndARecoveryDoesNotImmediatelyReverseIt()
    {
        var controller = Adaptive();
        Samples(controller, 0, 11, frame: 33, forest: 12);
        Assert.Equal(ForestLod.Far, controller.AdaptiveLod);
        Samples(controller, 11, 220, frame: 6, forest: 1);
        Assert.Equal(ForestLod.Far, controller.AdaptiveLod);
        Samples(controller, 311, 50, frame: 6, forest: 1);
        Assert.Equal(ForestLod.Medium, controller.AdaptiveLod);
    }

    [Fact]
    public void BriefSpikesAndSlowFramesWithLittleForestWorkKeepQuality()
    {
        var controller = Adaptive();
        Samples(controller, 0, 60, frame: 44, forest: 2);
        Samples(controller, 60, 2, frame: 71, forest: 30);
        Samples(controller, 62, 15, frame: 20, forest: 6);
        Assert.Equal(ForestLod.Medium, controller.AdaptiveLod);
    }

    [Fact]
    public void PromotionNeedsASettledSceneAndEnoughMemory()
    {
        var controller = Adaptive();
        Samples(controller, 0, 50, frame: 4, forest: .5, nearFits: false);
        Samples(controller, 50, 50, frame: 4, forest: .5, ready: false);
        Assert.Equal(ForestLod.Medium, controller.AdaptiveLod);
        Samples(controller, 100, 45, frame: 4, forest: .5);
        Assert.Equal(ForestLod.Near, controller.AdaptiveLod);
        controller.LimitNearForMemory(15);
        Samples(controller, 150, 50, frame: 4, forest: .5);
        Assert.Equal(ForestLod.Medium, controller.AdaptiveLod);
    }

    [Fact]
    public void TheFixedModesIgnoreCameraAndFrameSamples()
    {
        var controller = Adaptive();
        controller.Mode = ForestDetailMode.FixedHigh;
        Samples(controller, 0, 90, frame: 65, forest: 43);
        Assert.Equal(ForestLod.Near, controller.Select(1, null));
        controller.Mode = ForestDetailMode.FixedMedium;
        Assert.Equal(ForestLod.Medium, controller.Select(30, null));
    }

    [Fact]
    public void LaunchPreferenceRestoresTheChosenModeAndHandlesAnInvalidFile()
    {
        var folder = Directory.CreateTempSubdirectory("forest-detail-");
        string path = Path.Combine(folder.FullName, "forest-detail.txt");
        try {
            Assert.Equal(ForestDetailMode.Adaptive, ForestDetailPreference.Read(path));
            Assert.True(ForestDetailPreference.Save(ForestDetailMode.FixedHigh, path));
            Assert.Equal(ForestDetailMode.FixedHigh, ForestDetailPreference.Read(path));
            Assert.True(ForestDetailPreference.Save(ForestDetailMode.FixedMedium, path));
            Assert.Equal(ForestDetailMode.FixedMedium, ForestDetailPreference.Read(path));
            File.WriteAllText(path, "invalid");
            Assert.Equal(ForestDetailMode.Adaptive, ForestDetailPreference.Read(path));
        }
        finally {
            if (File.Exists(path)) File.Delete(path);
            folder.Delete();
        }
    }

    private static ForestQualityController Adaptive() => new() { Mode = ForestDetailMode.Adaptive };

    private static void Samples(ForestQualityController controller, int start, int count, double frame, double forest,
        bool nearFits = true, bool ready = true)
    {
        for (int i = start; i < start + count; i++)
            controller.Observe(new(i / 10.0, frame, forest, controller.AdaptiveLod, ready, nearFits));
    }
}
