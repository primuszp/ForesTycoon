using System;
using System.Collections.Generic;

namespace ForesTycoon.Ecology
{
    internal readonly record struct ForestClearance(int TileId, ForestSpecies Species, float Volume);
    sealed partial class ForestSystem
    {
        internal bool HasPhysicalObjects(int id) => IndividualTrees.TryGet(id, out var patch)
            && (patch.Count > 0 || patch.Depot > 0 || patch.Stumps?.Count > 0 || patch.DeadTrees?.Count > 0);

        /// <summary>Editor felling leaves the recovered timber on site for the normal extraction chain.</summary>
        internal IEnumerable<int> FellForEditorWork(IEnumerable<int> ids)
        {
            bool changed = false;
            foreach (int id in ids) {
                yield return 1;
                if ((uint)id >= (uint)stands.Length) throw new ArgumentOutOfRangeException(nameof(ids));
                if (!IndividualTrees.TryGet(id, out var patch) || patch.Count == 0 && patch.DeadTrees?.Count is not > 0) continue;
                changed = true;
                while (patch.Count > 0) { FellIndividual(patch, 0); yield return 1; }
                if (patch.DeadTrees != null) {
                    foreach (var dead in patch.DeadTrees) { patch.Depot += ForestTree.Volume(dead.Tree.Dimensions); yield return 1; }
                    patch.DeadTrees.Clear();
                }
                stands[id] = default; MarkResourceArea(id);
            }
            if (!changed) yield break;
            foreach (int unit in RefreshChangedResourceRatesWork(ForestYear, currentConditions: true)) yield return unit;
            CompleteLocalEditRevision();
        }

        internal IEnumerable<int> ClearForConstructionWork(int[] ids, WorkResult<ForestClearance[]> result)
        {
            var wood = new List<ForestClearance>();
            foreach (int id in ids) {
                if ((uint)id >= (uint)stands.Length) throw new ArgumentOutOfRangeException(nameof(ids));
                yield return 1;
                if (!IndividualTrees.TryGet(id, out var patch)) continue;
                if (patch.Depot > 0) wood.Add(new(id, patch.Count > 0 ? patch.Trees[0].Species
                    : patch.Stumps?.Count > 0 ? patch.Stumps[0].Felled.Species : ForestSpecies.Spruce, patch.Depot));
                for (int i = 0; i < patch.Count; i++) {
                    var tree = patch.Trees[i]; wood.Add(new(id, tree.Species, ForestTree.Volume(tree.At(ForestYear)))); yield return 1;
                }
                if (patch.DeadTrees != null) foreach (var dead in patch.DeadTrees) {
                    wood.Add(new(id, dead.Tree.Species, ForestTree.Volume(dead.Tree.Dimensions))); yield return 1;
                }
            }
            foreach (int unit in ClearTerrainTilesWork(ids)) yield return unit;
            result.Value = wood.ToArray();
        }
        /// <summary>Road edits only remove forest on cells that actually became unavailable.</summary>
        internal void RefreshHabitat(ReadOnlySpan<int> changedTiles)
        {
            foreach (int id in changedTiles)
                if ((uint)id >= (uint)stands.Length) throw new ArgumentOutOfRangeException(nameof(changedTiles));
            List<int> unavailable = new();
            foreach (int id in changedTiles)
                if (!habitat.CanSupportForest(id)) unavailable.Add(id);
            ClearTerrainTiles(unavailable.ToArray());
        }

        /// <summary>Terraforming destroys vegetation on changed cells without harvesting or advancing growth.</summary>
        internal void ClearTerrainTiles(ReadOnlySpan<int> changedTiles) => SimulationWork.Complete(ClearTerrainTilesWork(changedTiles.ToArray()));

        internal IEnumerable<int> ClearTerrainTilesWork(int[] changedTiles)
        {
            // Validate the entire request before modifying any state.
            foreach (int id in changedTiles)
                { if ((uint)id >= (uint)stands.Length) throw new ArgumentOutOfRangeException(nameof(changedTiles)); yield return 1; }
            List<int> removed = null;
            bool plantationChanged = false;
            foreach (int id in changedTiles)
            {
                yield return 1;
                bool hasPatch = IndividualTrees.TryGet(id, out _);
                bool hasPlantation = plantations.Remove(id);
                plantationChanged |= hasPlantation;
                if (!hasPatch && !hasPlantation) continue;
                IndividualTrees.RemoveTile(id);
                stands[id] = default;
                (removed ??= new()).Add(id);
            }
            if (removed == null) yield break;
            if (plantationChanged) PlantationRevision++;
            // Repair next month's prediction locally. Surviving trees retain their exact current
            // dimensions, anchors, health, seeds and growth rates until the next simulation boundary.
            ulong before = Revision;
            Revision++; EditRevision++;
            foreach (int unit in monthlyPreparation.InvalidateLocalWork(habitat, before, Revision, (month + 1) / 12.0, removed)) yield return unit;
            foreach (int unit in RecalculateStatisticsWork()) yield return unit;
        }
    }
}
