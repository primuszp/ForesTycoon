using System;

namespace ForesTycoon.Map;

partial class TerrainMap
{
    // The map knows footprints; the simulation supplies its resource and actor reservations.
    internal Func<int, bool> GroundReserved { get; set; }
    internal Func<int, bool> VegetationOccupied { get; set; }
    internal Func<int, bool> MobileOccupied { get; set; }
    private bool initialTerrainDefinition;
    internal bool HasGroundReservation(int id) => GroundReserved?.Invoke(id) == true;
    internal bool HasMobileObject(int id) => MobileOccupied?.Invoke(id) == true;
    internal bool IsTerrainEditBlocked(int id) => IsRoadTile(id) || IsBuildingTile(id) || IsSkidTrail(id)
        || HasGroundReservation(id) || HasMobileObject(id) || !initialTerrainDefinition && VegetationOccupied?.Invoke(id) == true;

    internal bool CanEditNode(int nodeId)
    {
        if ((uint)nodeId >= (uint)data.Nodes.Length) return false;
        foreach (var tile in data.GetTilesByNode(data.Nodes[nodeId]))
            if (IsTerrainEditBlocked(tile.Id)) return false;
        return true;
    }
}
