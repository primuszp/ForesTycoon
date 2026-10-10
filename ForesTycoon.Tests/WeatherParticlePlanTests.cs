using OpenTK.Mathematics;

namespace ForesTycoon.Tests;

public class WeatherParticlePlanTests
{
    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(25)] [InlineData(49)]
    [InlineData(1500)] [InlineData(3000)] [InlineData(6000)] [InlineData(65536)]
    public void EveryValidBudgetTerminatesAndCapsSubmission(int budget)
    {
        foreach (float extent in new[] { .001f, 10, 10000, 1e20f }) {
            var plan = WeatherParticlePlan.Create(new Vector2(-extent, -extent), new Vector2(extent, extent), budget);
            Assert.InRange(plan.Count, 0, budget);
            if (budget > 0) { Assert.True(plan.Count > 0); Assert.InRange(plan.PerCell, 1, 24); Assert.True(float.IsFinite(plan.CellSize)); }
        }
    }

    [Theory]
    [InlineData(-1)] [InlineData(65537)]
    public void InvalidBudgetsAreRejected(int budget)
        => Assert.Throws<ArgumentOutOfRangeException>(() => WeatherParticlePlan.Create(Vector2.Zero, Vector2.One, budget));

    [Fact]
    public void EmptyViewportSubmitsNothingAndNonFiniteBoundsAreRejected()
    {
        Assert.Equal(0, WeatherParticlePlan.Create(Vector2.One, Vector2.Zero, 6000).Count);
        Assert.Throws<ArgumentOutOfRangeException>(() => WeatherParticlePlan.Create(new(float.NaN, 0), Vector2.One, 6000));
    }

    [Fact]
    public void PanningWithinCellPreservesWorldAnchors()
    {
        var first = WeatherParticlePlan.Create(new(1, 1), new(100, 100), 6000);
        var second = WeatherParticlePlan.Create(new(2, 2), new(101, 101), 6000);
        Assert.Equal(first.CellSize, second.CellSize); Assert.Equal(first.Origin, second.Origin);
    }

    [Fact]
    public void CpuBudgetExcessIsExplicitAndQualityLimitsAreMonotonic()
    {
        Assert.True(new EffectMetrics(1, 6, 16, 16, 3, 2).CpuBudgetExceeded);
        Assert.False(new EffectMetrics(1, 6, 16, 16, 1, 2).CpuBudgetExceeded);
        var qualities = new[] { GraphicsQuality.Low, GraphicsQuality.Medium, GraphicsQuality.High };
        int previousParticles = 0, previousSteps = 0; double previousCpu = 0;
        foreach (var quality in qualities) {
            IWeatherSettings settings = new GraphicsSettings { Quality = quality };
            Assert.InRange(settings.RainBudget, previousParticles + 1, WeatherParticlePlan.MaxParticles);
            Assert.InRange(settings.CloudSteps, previousSteps + 1, 32);
            Assert.True(settings.EffectCpuBudgetMilliseconds > previousCpu);
            previousParticles = settings.RainBudget; previousSteps = settings.CloudSteps; previousCpu = settings.EffectCpuBudgetMilliseconds;
        }
    }

    [Fact]
    public void RendererWrappersDisposeOwnedBackendsOnceAndRejectFurtherUse()
    {
        var backend = new RecordingBackend(); var cloudBackend = new RecordingBackend(); var weather = new WeatherRenderer(backend); var clouds = new CloudRenderer(cloudBackend);
        weather.Dispose(); weather.Dispose(); clouds.Dispose(); clouds.Dispose();
        Assert.Equal(1, backend.Disposals); Assert.Equal(1, cloudBackend.Disposals);
        Assert.Throws<ObjectDisposedException>(() => weather.Draw(null!, null!, default, null!));
        Assert.Throws<ObjectDisposedException>(() => clouds.Draw(null!, null!, null!));
    }

    private sealed class RecordingBackend : IWeatherRenderBackend, ICloudRenderBackend
    {
        internal int Disposals;
        public void Dispose() => Disposals++;
        public void Draw(IWeatherSurface surface, WeatherVisualState weather, RenderContext context, IWeatherSettings settings) { }
        public void Draw(IWeatherSurface surface, WeatherVisualState weather, IWeatherSettings settings) { }
    }
}
