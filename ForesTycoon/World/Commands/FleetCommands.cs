namespace ForesTycoon
{
    sealed class PlaceDepotCommand : IWorldCommand
    {
        private readonly int tile;
        internal PlaceDepotCommand(int tile) { this.tile = tile; }
        public void Execute(IWorldCommandTarget world) => world.ExecutePlaceDepot(tile);
        public WorldCommandRecord ToRecord(ulong tick) => new(tick, WorldCommandKind.PlaceDepot, tile, 0, 0, 0, false);
    }

    /// <summary>
    /// Gives a fleet vehicle its order. Processor: <c>tile</c> is in the site to fell. Forwarder and truck (Flag set for
    /// a truck): <c>tile</c> is the source stack and <c>destination</c> the tile (stack site or sawmill) to carry it to.
    /// </summary>
    sealed class SendVehicleCommand : IWorldCommand
    {
        private readonly int vehicleId, tile, destination;
        private readonly bool truck;
        internal SendVehicleCommand(int vehicleId, int tile, int destination, bool truck)
        { this.vehicleId = vehicleId; this.tile = tile; this.destination = destination; this.truck = truck; }
        public void Execute(IWorldCommandTarget world) => world.ExecuteSendVehicle(vehicleId, tile, destination, truck);
        // C holds destination + 1 so that 0 means "none".
        public WorldCommandRecord ToRecord(ulong tick) => new(tick, WorldCommandKind.SendVehicle, vehicleId, tile, destination + 1, 0, truck);
    }

    /// <summary>Marks (Flag false) or removes (Flag true) a stack site.</summary>
    sealed class StackSiteCommand : IWorldCommand
    {
        private readonly int tile;
        private readonly bool remove;
        internal StackSiteCommand(int tile, bool remove) { this.tile = tile; this.remove = remove; }
        public void Execute(IWorldCommandTarget world) => world.ExecuteStackSite(tile, remove);
        public WorldCommandRecord ToRecord(ulong tick) => new(tick, WorldCommandKind.StackSite, tile, 0, 0, 0, remove);
    }

    sealed class SendHomeCommand : IWorldCommand
    {
        private readonly int vehicleId;
        private readonly bool truck;
        internal SendHomeCommand(int vehicleId, bool truck) { this.vehicleId = vehicleId; this.truck = truck; }
        public void Execute(IWorldCommandTarget world) => world.ExecuteSendHome(vehicleId, truck);
        public WorldCommandRecord ToRecord(ulong tick) => new(tick, WorldCommandKind.SendHome, vehicleId, 0, 0, 0, truck);
    }
}
