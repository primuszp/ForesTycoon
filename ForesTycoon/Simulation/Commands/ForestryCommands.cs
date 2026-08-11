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

    sealed class HarvestForestCommand : IWorldCommand
    {
        private readonly int tileId;

        public HarvestForestCommand(int tileId) => this.tileId = tileId;

        public void Execute(IWorldCommandTarget world) => world.ExecuteHarvestForest(tileId);
        public WorldCommandRecord ToRecord(ulong tick) =>
            new WorldCommandRecord(tick, WorldCommandKind.HarvestForest, tileId, 0, 0, 0, false);
    }
}
