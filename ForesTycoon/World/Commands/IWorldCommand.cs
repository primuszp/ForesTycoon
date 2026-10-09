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
        void ExecuteLegacyElevationEdit(int nodeId, int delta, int radius, int strength) =>
            ExecuteElevationEdit(nodeId, delta, radius, strength);
        void ExecuteRoadPath(int startTileId, int endTileId, bool remove);
        void ExecuteRoadPath(int startTileId, int endTileId, bool remove, RoadPaving surface) =>
            ExecuteRoadPath(startTileId, endTileId, remove);
        void ExecuteRoadRepair(int startTileId, int endTileId) => throw new System.NotSupportedException();
        void ExecuteSkidTrailPath(int startTileId, int endTileId, bool remove) => throw new System.NotSupportedException();
        void ExecutePlaceDepot(int tileId) => throw new System.NotSupportedException();
        void ExecuteSendVehicle(int vehicleId, int tileId, bool truck) => throw new System.NotSupportedException();
        void ExecuteSendHome(int vehicleId, bool truck) => throw new System.NotSupportedException();
        void ExecuteSpawnVehicle();
        void ExecutePlaceSawmill(int tileId) => throw new System.NotSupportedException();
        void ExecuteWeather(WeatherPreset preset,int intensity,int duration) => throw new System.NotSupportedException();
        void ExecutePlantForest(int tileId, ForestSpecies species);
        void ExecuteHarvestForest(int tileId);
        void ExecutePlantForestArea(int startTileId, int endTileId, ForestSpecies species);
        void ExecuteHarvestForestArea(int startTileId, int endTileId);
    }
}
