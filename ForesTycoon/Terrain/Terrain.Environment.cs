namespace ForesTycoon
{
    partial class Terrain
    {
        internal bool IsEnvironmentWaterOutlet(int id) => data.IsBorderTile(tiles[id]) ||
            ShouldDrawStandingWater(tiles[id]) || CountRiverCorners(tiles[id])>=2;
    }
}
