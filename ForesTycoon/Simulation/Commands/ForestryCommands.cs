using System;

namespace ForesTycoon
{
    sealed class PlantForestCommand : IWorldCommand
    {
        private readonly int tileId;
        private readonly ForestSpecies species;

        public PlantForestCommand(int tileId, ForestSpecies species)
        {
            if (!Enum.IsDefined(species) || species == ForestSpecies.None)
                throw new ArgumentOutOfRangeException(nameof(species));
            this.tileId = tileId;
            this.species = species;
        }

        public void Execute(IWorldCommandTarget world) => world.ExecutePlantForest(tileId, species);
        public WorldCommandRecord ToRecord(ulong tick) =>
            new WorldCommandRecord(tick, WorldCommandKind.PlantForest, tileId, (int)species, 0, 0, false);
    }

    /// <summary>
    /// Plants every plantable tile of the rectangle spanned by two corner tiles. Stored as the
    /// two corners rather than as a tile list, so the journal stays compact and a replay
    /// rebuilds exactly the same area from the same terrain.
    /// </summary>
    sealed class PlantForestAreaCommand : IWorldCommand
    {
        private readonly int startTileId;
        private readonly int endTileId;
        private readonly ForestSpecies species;

        public PlantForestAreaCommand(int startTileId, int endTileId, ForestSpecies species)
        {
            if (!Enum.IsDefined(species) || species == ForestSpecies.None)
                throw new ArgumentOutOfRangeException(nameof(species));
            this.startTileId = startTileId;
            this.endTileId = endTileId;
            this.species = species;
        }

        public void Execute(IWorldCommandTarget world) =>
            world.ExecutePlantForestArea(startTileId, endTileId, species);

        public WorldCommandRecord ToRecord(ulong tick) => new WorldCommandRecord(
            tick, WorldCommandKind.PlantForestArea, startTileId, endTileId, (int)species, 0, false);
    }

    sealed class HarvestForestAreaCommand : IWorldCommand
    {
        private readonly int startTileId;
        private readonly int endTileId;

        public HarvestForestAreaCommand(int startTileId, int endTileId)
        {
            this.startTileId = startTileId;
            this.endTileId = endTileId;
        }

        public void Execute(IWorldCommandTarget world) =>
            world.ExecuteHarvestForestArea(startTileId, endTileId);

        public WorldCommandRecord ToRecord(ulong tick) => new WorldCommandRecord(
            tick, WorldCommandKind.HarvestForestArea, startTileId, endTileId, 0, 0, false);
    }

    sealed class HarvestForestCommand : IWorldCommand
    {
        private readonly int tileId;

        public HarvestForestCommand(int tileId) => this.tileId = tileId;

        public void Execute(IWorldCommandTarget world) => world.ExecuteHarvestForest(tileId);
        public WorldCommandRecord ToRecord(ulong tick) =>
            new WorldCommandRecord(tick, WorldCommandKind.HarvestForest, tileId, 0, 0, 0, false);
    }
}
