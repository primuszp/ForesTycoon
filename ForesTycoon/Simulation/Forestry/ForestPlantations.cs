using System;
using System.Collections.Generic;

namespace ForesTycoon
{
    internal readonly record struct ForestPlantation(int AreaId, ForestSpecies Species, double PlantedYear, int InitialTrees);
    internal readonly record struct PlantationStatus(ForestPlantation Plantation, int Living, int Dead, ForestResources Resources);
    sealed partial class ForestSystem
    {
        private readonly Dictionary<int, ForestPlantation> plantations = new();
        private int nextPlantationId = 1;
        internal ulong PlantationRevision { get; private set; }
        internal int PlantationTileCount => plantations.Count;
        internal int AllocatePlantationId() => nextPlantationId++;
        internal bool TryGetPlantation(int tileId, out ForestPlantation plantation) => plantations.TryGetValue(tileId, out plantation);
        internal void FinishPlantingArea(int areaId)
        {
            foreach (var entry in plantations)
                if (entry.Value.AreaId == areaId) MarkResourceArea(entry.Key);
            RefreshChangedResourceRates(ForestYear, currentConditions: true);
        }
        private void ClearPlantations() { plantations.Clear(); nextPlantationId = 1; PlantationRevision++; }
        internal bool TryGetPlantationStatus(int tileId, out PlantationStatus status)
        {
            status = default;
            if (!plantations.TryGetValue(tileId, out var plantation)) return false;
            int count = 0, dead = 0; float light = 0, water = 0, space = 0;
            if (IndividualTrees.TryGet(tileId, out var patch))
            {
                count = patch.Count; dead = patch.DeadTrees?.Count ?? 0;
                for (int i = 0; i < count; i++)
                {
                    light += patch.Trees[i].Resources.Light; water += patch.Trees[i].Resources.Water; space += patch.Trees[i].Resources.Space;
                }
            }
            status = new(plantation, count, dead, count > 0 ? new(light / count, water / count, space / count) : default);
            return true;
        }
    }
}
