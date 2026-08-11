using System;

namespace ForesTycoon
{
    /// <summary>
    /// Read-only terrain boundary used by the forest simulation. Keeping this interface
    /// free of rendering types makes the simulation independently testable.
    /// </summary>
    interface IForestHabitat
    {
        int TileCount { get; }
        int Seed { get; }
        bool CanSupportForest(int tileId);
        float GetMoisture(int tileId);
        float GetNormalizedElevation(int tileId);
        int GetAdjacentTileIds(int tileId, Span<int> destination);
    }
}
