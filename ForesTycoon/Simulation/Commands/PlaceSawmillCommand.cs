namespace ForesTycoon
{
    sealed class PlaceSawmillCommand : IWorldCommand
    {
        private readonly int tile;
        internal PlaceSawmillCommand(int tile){this.tile=tile;}
        public void Execute(IWorldCommandTarget world)=>world.ExecutePlaceSawmill(tile);
        public WorldCommandRecord ToRecord(ulong tick)=>new(tick,WorldCommandKind.PlaceSawmill,tile,0,0,0,false);
    }
}
