using System;

namespace ForesTycoon.Ecology
{
    /// <summary>
    /// Read-only terrain boundary used by the forest simulation. Keeping this interface
    /// free of rendering types makes the simulation independently testable.
    /// </summary>
    interface IForestHabitat
    {
        int TileCount { get; }
        int Seed { get; }
        // Fallback keeps small headless habitats usable; real terrain supplies metric positions.
        ForestTileGeometry GetForestTileGeometry(int tileId) => new(tileId * 16, 0, 16, 16);
        bool CanSupportForest(int tileId);
        float GetMoisture(int tileId);
        SoilProperties GetSoilProperties(int tileId) => SoilProperties.Standard;
        bool IsImpervious(int tileId) => false;
        bool IsWaterOutlet(int tileId) => false;
        float GetNormalizedElevation(int tileId);
        int GetAdjacentTileIds(int tileId, Span<int> destination);
        /// <summary>How the initial forest should be laid out; <see cref="ForestPattern.Natural"/> follows site suitability.</summary>
        ForestPattern ForestPattern => ForestPattern.Natural;
        /// <summary>Tile grid size, needed only by the large patterned initial forests.</summary>
        (int Columns, int Rows) TileGrid => (0, 0);
    }
}
