namespace ForesTycoon.Tests;

public class RenderPipelineTests
{
    [Fact]
    public void EqualLayersKeepRegistrationOrderAcrossRepeatedSorts()
    {
        var pipeline = new RenderPipeline();
        var events = new List<string>();
        var expected = new List<string> { "ground" };
        pipeline.Add(RenderLayer.TerrainBase, "ground", _ => events.Add("ground"));
        pipeline.Add(RenderLayer.Interface, "hud", _ => events.Add("hud"));
        // More than the small-list sort threshold exercises the original unstable sort.
        for (int i = 0; i < 24; i++)
        {
            string name = "forest" + i;
            expected.Add(name);
            pipeline.Add(RenderLayer.Props, name, _ => events.Add(name));
        }
        expected.Add("hud");
        pipeline.Render(default);
        Assert.Equal(expected, events);
        events.Clear();
        pipeline.Add(RenderLayer.Props, "stumps", _ => events.Add("stumps"));
        expected.Insert(expected.Count - 1, "stumps");
        pipeline.Render(default);
        Assert.Equal(expected, events);
    }

    [Fact]
    public void FailingPassStillClosesItsPerformanceProbe()
    {
        var pipeline = new RenderPipeline();
        var probes = new List<(string, bool)>();
        pipeline.Add(RenderLayer.Props, "forest", _ => throw new InvalidOperationException("failure"));
        var previous = RenderPipeline.PassProbe;
        try
        {
            RenderPipeline.PassProbe = (name, begin) => probes.Add((name, begin));
            Assert.Throws<InvalidOperationException>(() => pipeline.Render(default));
            Assert.Equal(new[] { ("forest", true), ("forest", false) }, probes);
        }
        finally
        {
            RenderPipeline.PassProbe = previous;
        }
    }
}
