namespace ForesTycoon.Tests;

public class MapViewConstraintsTests
{
    [Fact]
    public void MinimumZoomForTileWindow_KeepsBothDimensionsWithinBudget()
    {
        float zoom = MapViewConstraints.MinimumZoomForTileWindow(1280, 720, 5, 5, 64);

        Assert.Equal(4f, zoom, 3);
        Assert.True(1280f / zoom <= 64 * 5);
        Assert.True(720f / zoom <= 64 * 5);
    }

    [Fact]
    public void MinimumZoomForTileWindow_RejectsInvalidDimensions()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            MapViewConstraints.MinimumZoomForTileWindow(0, 720, 5, 5, 64));
    }
}
