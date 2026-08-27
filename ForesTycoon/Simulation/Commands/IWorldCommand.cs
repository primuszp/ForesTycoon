namespace ForesTycoon
{
    interface IWorldCommand
    {
        void Execute(IWorldCommandTarget world);
        WorldCommandRecord ToRecord(ulong tick);
    }

    interface IWorldCommandTarget
    {
        void ExecuteElevationEdit(int nodeId, int delta, int radius, int strength);
        void ExecuteRoadPath(int startTileId, int endTileId, bool remove);
        void ExecuteSpawnVehicle();
        void ExecutePlantForest(int tileId, ForestSpecies species);
        void ExecuteHarvestForest(int tileId);
        void ExecutePlantForestArea(int startTileId, int endTileId, ForestSpecies species);
        void ExecuteHarvestForestArea(int startTileId, int endTileId);
    }
}
