using System;
using System.Collections.Generic;
using ForesTycoon.Rules;

namespace ForesTycoon.Ecology
{
    internal readonly record struct ForestHydrologyInputs(double Cover, double InterceptionCapacity, double LeafAreaIndex);

    sealed partial class ForestSystem
    {
        // Demand is proportional to each living tree's projected crown/leaf area and health.
        // Aggregating per cell permits proportional uptake without visiting each tree per water tick.
        internal ForestHydrologyInputs HydrologyInputs(int tileId)
        {
            if (!IndividualTrees.TryGet(tileId, out var patch)) return default;
            double crownArea = 0, interception = 0, leafArea = 0;
            for (int i = 0; i < patch.Count; i++)
            {
                AccumulateHydrology(patch.Trees[i], ref crownArea, ref interception, ref leafArea);
            }
            return ResolveHydrology(tileId, crownArea, interception, leafArea);
        }
        IEnumerable<int> IForestCanopy.HydrologyInputsWork(int tileId, WorkResult<ForestHydrologyInputs> result)
        {
            if (!IndividualTrees.TryGet(tileId, out var patch)) { result.Value = default; yield break; }
            double crownArea = 0, interception = 0, leafArea = 0;
            for (int i = 0; i < patch.Count; i++) {
                yield return 1;
                AccumulateHydrology(patch.Trees[i], ref crownArea, ref interception, ref leafArea);
            }
            result.Value = ResolveHydrology(tileId, crownArea, interception, leafArea);
        }
        private void AccumulateHydrology(ForestTree tree, ref double crownArea, ref double interception, ref double leafArea)
        {
            var size = tree.At(ForestYear);
            double crown = Responses.Environment(EnvironmentResponse.CrownArea, [size.CrownRadius, tree.Health]);
            crownArea += crown;
            interception += Responses.Environment(EnvironmentResponse.Interception, [crown, ForestSpeciesTraits.For(tree.Species).Evergreen ? 1 : 0]);
            leafArea += Responses.Environment(EnvironmentResponse.LeafArea, [crown, ForestSpeciesTraits.For(tree.Species).Evergreen ? 1 : 0]);
        }
        private ForestHydrologyInputs ResolveHydrology(int tileId, double crownArea, double interception, double leafArea)
        {
            var geometry = habitat.GetForestTileGeometry(tileId);
            double area = Math.Max(0.01, geometry.Width * geometry.Height);
            double cover = Responses.Environment(EnvironmentResponse.Cover, [crownArea, area]);
            return new(cover, Responses.Environment(EnvironmentResponse.CanopyCapacity, [cover, interception, crownArea]),
                Responses.Environment(EnvironmentResponse.LeafIndex, [leafArea, area]));
        }
    }
}
