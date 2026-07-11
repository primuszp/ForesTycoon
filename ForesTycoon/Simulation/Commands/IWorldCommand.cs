namespace ForesTycoon
{
    interface IWorldCommand
    {
        void Execute(IWorldCommandTarget world);
    }

    interface IWorldCommandTarget
    {
        void ExecuteElevationEdit(int nodeId, int delta, int radius, int strength);
        void ExecuteRoadPath(int startTileId, int endTileId, bool remove);
        void ExecuteSpawnVehicle();
    }
}
