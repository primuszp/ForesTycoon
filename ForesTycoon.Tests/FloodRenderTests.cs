namespace ForesTycoon.Tests;

public sealed class FloodRenderTests
{
    [Fact]
    public void WaterInAHollowLiesFlatAtItsLevel()
    {
        Span<float> surface = stackalloc float[4];
        Terrain.FloodCorners(new float[] { 10, 10, 10, 10 }, 400, surface);
        foreach (float z in surface) Assert.Equal(10.43f, z, 3);
    }

    [Fact]
    public void AThinFilmOnASlopeIsDrapedNotFloating()
    {
        Span<float> surface = stackalloc float[4];
        float[] ground = { 0, 2, 4, 2 };
        Terrain.FloodCorners(ground, 10, surface);
        for (int i = 0; i < 4; i++) Assert.InRange(surface[i] - ground[i], 0.029f, 0.051f);
    }

    [Fact]
    public void DeeperWaterIsMoreOpaqueAndBluer()
    {
        var shallow = Terrain.FloodColor(Terrain.MinimumFloodDepth);
        var deep = Terrain.FloodColor(500);
        Assert.True(deep.A > shallow.A && deep.B > deep.R && deep.R < shallow.R);
    }
}
