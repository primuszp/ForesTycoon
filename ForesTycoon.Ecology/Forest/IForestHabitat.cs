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
        bool CanPlantForest(int tileId) => CanSupportForest(tileId);
        float GetMoisture(int tileId);
        SoilProperties GetSoilProperties(int tileId) => SoilProperties.Standard;
        bool IsImpervious(int tileId) => false;
        bool IsWaterOutlet(int tileId) => false;
        float GetNormalizedElevation(int tileId);
        /// <summary>Ground height in metres, for water that flows by its level. Fallback: a scaled normalised elevation.</summary>
        float GetGroundHeight(int tileId) => GetNormalizedElevation(tileId) * 100f;
        int GetAdjacentTileIds(int tileId, Span<int> destination);
        /// <summary>How the initial forest should be laid out; <see cref="ForestPattern.Natural"/> follows site suitability.</summary>
        ForestPattern ForestPattern => ForestPattern.Natural;
        /// <summary>Tile grid size, needed only by the large patterned initial forests.</summary>
        (int Columns, int Rows) TileGrid => (0, 0);
        /// <summary>
        /// The landscape class of a tile, as the scene defines it: elevation zone index (0 = lowest), biome index (-1 = none)
        /// and slope (0 flat, 1 gentle, 2 steep). Site rules may read it; habitats without a scene report a flat, lowest zone.
        /// </summary>
        (int Zone, int Biome, int Slope) GetLandscape(int tileId) => (0, -1, 0);
    }
}
