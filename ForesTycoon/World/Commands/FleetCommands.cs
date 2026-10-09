namespace ForesTycoon
{
    sealed class PlaceDepotCommand : IWorldCommand
    {
        private readonly int tile;
        internal PlaceDepotCommand(int tile) { this.tile = tile; }
        public void Execute(IWorldCommandTarget world) => world.ExecutePlaceDepot(tile);
        public WorldCommandRecord ToRecord(ulong tick) => new(tick, WorldCommandKind.PlaceDepot, tile, 0, 0, 0, false);
    }

    /// <summary>Sends a fleet vehicle to work the harvest site that contains <c>tile</c>: a machine (Flag false) or a truck (Flag true).</summary>
    sealed class SendVehicleCommand : IWorldCommand
    {
        private readonly int vehicleId, tile;
        private readonly bool truck;
        internal SendVehicleCommand(int vehicleId, int tile, bool truck) { this.vehicleId = vehicleId; this.tile = tile; this.truck = truck; }
        public void Execute(IWorldCommandTarget world) => world.ExecuteSendVehicle(vehicleId, tile, truck);
        public WorldCommandRecord ToRecord(ulong tick) => new(tick, WorldCommandKind.SendVehicle, vehicleId, tile, 0, 0, truck);
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
