using OpenTK.Mathematics;

namespace ForesTycoon.Tests;

public class ForestVisualFixtureTests
{
    [Fact]
    public void Fixture_HasFourSpeciesClearingAndStableSeed()
    {
        ForestStand[] first = ForestVisualFixture.CreateStands();
        Assert.Equal(first, ForestVisualFixture.CreateStands());
        Assert.Equal(256, first.Length);
        Assert.True(first[8 * 16 + 7].IsEmpty);
        Assert.Equal(4, first.Where(s => !s.IsEmpty).Select(s => s.Species).Distinct().Count());
        Assert.Contains(first, s => !s.IsEmpty && s.Maturity < 0.7f);
        Assert.Contains(first, s => s.Maturity >= 1f);
    }

    [Fact]
    public void SurfacePoint_UsesMeshDiagonalInsteadOfBilinearHeight()
    {
        var data = new TerrainData(TerrainSettings.Default.WithNodeSize(17, 42));
        Tile tile = data.GetTile(2, 2);
        tile.W.zPos = 0; tile.S.zPos = 2; tile.E.zPos = 0; tile.N.zPos = 2;
        Terrain.SurfacePoint(tile, 0.5f, 0.5f, out _, out _, out float z);
        Assert.Equal(0f, z); // W-E diagonal; bilinear interpolation would incorrectly give 1.
        Terrain.SurfacePoint(tile, 0.75f, 0.25f, out _, out _, out z);
        Assert.Equal(1f, z);
        tile.W.zPos = 0; tile.S.zPos = 2; tile.E.zPos = 4; tile.N.zPos = 2;
        Terrain.SurfacePoint(tile, 0.5f, 0.5f, out _, out _, out z);
        Assert.Equal(2f, z); // N-S diagonal.
    }

}
