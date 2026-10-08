using System;
using System.Collections.Generic;

namespace ForesTycoon.Ecology
{
    sealed partial class ForestSystem
    {
        /// <summary>Terraforming destroys vegetation on changed cells without harvesting or advancing growth.</summary>
        internal void ClearTerrainTiles(ReadOnlySpan<int> changedTiles)
        {
            // Validate the entire request before modifying any state.
            foreach (int id in changedTiles)
                if ((uint)id >= (uint)stands.Length) throw new ArgumentOutOfRangeException(nameof(changedTiles));
            List<int> removed = null;
            bool plantationChanged = false;
            foreach (int id in changedTiles)
            {
                bool hasPatch = IndividualTrees.TryGet(id, out _);
                bool hasPlantation = plantations.Remove(id);
                plantationChanged |= hasPlantation;
                if (!hasPatch && !hasPlantation) continue;
                IndividualTrees.RemoveTile(id);
                stands[id] = default;
                (removed ??= new()).Add(id);
            }
            if (removed == null) return;
            if (plantationChanged) PlantationRevision++;
            // Repair next month's prediction locally. Surviving trees retain their exact current
            // dimensions, anchors, health, seeds and growth rates until the next simulation boundary.
            ulong before = Revision;
            Revision++; EditRevision++;
            monthlyPreparation.InvalidateLocal(habitat, before, Revision, (month + 1) / 12.0, removed);
            RecalculateStatistics();
        }
    }
}
