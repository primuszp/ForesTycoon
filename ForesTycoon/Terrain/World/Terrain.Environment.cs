namespace ForesTycoon
{
    partial class Terrain
    {
        bool IForestHabitat.IsImpervious(int id) => IsRoadTile(id) || IsBuildingTile(id);
        bool IForestHabitat.IsWaterOutlet(int id) => IsEnvironmentWaterOutlet(id);

        internal bool IsEnvironmentWaterOutlet(int id) => data.IsBorderTile(tiles[id]) ||
            ShouldDrawStandingWater(tiles[id]) || CountRiverCorners(tiles[id])>=2;
    }
}
