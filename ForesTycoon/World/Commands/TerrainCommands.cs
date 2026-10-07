namespace ForesTycoon
{
    sealed class EditElevationCommand : IWorldCommand
    {
        private readonly int nodeId;
        private readonly int delta;
        private readonly int radius;
        private readonly int strength;

        public EditElevationCommand(int nodeId, int delta, int radius, int strength)
        {
            this.nodeId = nodeId;
            this.delta = delta;
            this.radius = radius;
            this.strength = strength;
        }

        public void Execute(IWorldCommandTarget world) => world.ExecuteElevationEdit(nodeId, delta, radius, strength);
        public WorldCommandRecord ToRecord(ulong tick) =>
            new WorldCommandRecord(tick, WorldCommandKind.EditElevation, nodeId, delta, radius, strength, false);
    }

    sealed class RoadPathCommand : IWorldCommand
    {
        private readonly int startTileId;
        private readonly int endTileId;
        private readonly bool remove;

        public RoadPathCommand(int startTileId, int endTileId, bool remove)
        {
            this.startTileId = startTileId;
            this.endTileId = endTileId;
            this.remove = remove;
        }

        public void Execute(IWorldCommandTarget world) => world.ExecuteRoadPath(startTileId, endTileId, remove);
        public WorldCommandRecord ToRecord(ulong tick) =>
            new WorldCommandRecord(tick, WorldCommandKind.RoadPath, startTileId, endTileId, 0, 0, remove);
    }

    sealed class SpawnVehicleCommand : IWorldCommand
    {
        public void Execute(IWorldCommandTarget world) => world.ExecuteSpawnVehicle();
        public WorldCommandRecord ToRecord(ulong tick) =>
            new WorldCommandRecord(tick, WorldCommandKind.SpawnVehicle, 0, 0, 0, 0, false);
    }
}
