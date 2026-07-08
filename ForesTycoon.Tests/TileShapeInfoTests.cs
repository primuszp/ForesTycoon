namespace ForesTycoon.Tests;

public class TileShapeInfoTests
{
    [Fact]
    public void FromCorners_ClassifiesFlatTile()
    {
        TileShapeInfo shape = TileShapeInfo.FromCorners(2, 2, 2, 2);

        Assert.Equal(TileShapeKind.Flat, shape.Kind);
        Assert.True(shape.IsFlat);
        Assert.True(shape.IsPlanar);
        Assert.Equal("0000", shape.RelativeCodeNESW);
    }

    [Fact]
    public void FromCorners_ClassifiesOneHighTile()
    {
        TileShapeInfo shape = TileShapeInfo.FromCorners(3, 2, 2, 2);

        Assert.Equal(TileShapeKind.OneHigh, shape.Kind);
        Assert.Equal(2, shape.Min);
        Assert.Equal(3, shape.Max);
        Assert.True(shape.WRaised);
        Assert.False(shape.SRaised);
        Assert.False(shape.ERaised);
        Assert.False(shape.NRaised);
        Assert.Equal("0001", shape.RelativeCodeNESW);
    }

    [Fact]
    public void FromCorners_ClassifiesRampTile()
    {
        TileShapeInfo shape = TileShapeInfo.FromCorners(2, 2, 3, 3);

        Assert.Equal(TileShapeKind.Ramp, shape.Kind);
        Assert.True(shape.IsRamp);
        Assert.True(shape.IsPlanar);
        Assert.Equal("1100", shape.RelativeCodeNESW);
    }

    [Fact]
    public void FromCorners_ClassifiesSaddleTile()
    {
        TileShapeInfo shape = TileShapeInfo.FromCorners(3, 2, 3, 2);

        Assert.Equal(TileShapeKind.Saddle, shape.Kind);
        Assert.False(shape.IsPlanar);
        Assert.Equal("0101", shape.RelativeCodeNESW);
    }

    [Fact]
    public void FromCorners_ClassifiesThreeHighTile()
    {
        TileShapeInfo shape = TileShapeInfo.FromCorners(2, 3, 3, 3);

        Assert.Equal(TileShapeKind.ThreeHigh, shape.Kind);
        Assert.False(shape.WRaised);
        Assert.True(shape.SRaised);
        Assert.True(shape.ERaised);
        Assert.True(shape.NRaised);
        Assert.Equal("1110", shape.RelativeCodeNESW);
    }

    [Fact]
    public void FromCorners_ClassifiesSteepTile()
    {
        TileShapeInfo shape = TileShapeInfo.FromCorners(0, 0, 2, 0);

        Assert.Equal(TileShapeKind.Steep, shape.Kind);
        Assert.Equal(0, shape.Min);
        Assert.Equal(2, shape.Max);
        Assert.Equal("0200", shape.RelativeCodeNESW);
    }
}
