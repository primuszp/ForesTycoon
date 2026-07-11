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
    }

    sealed class SpawnVehicleCommand : IWorldCommand
    {
        public void Execute(IWorldCommandTarget world) => world.ExecuteSpawnVehicle();
    }
}
