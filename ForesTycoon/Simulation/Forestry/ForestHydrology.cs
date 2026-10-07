using System;

namespace ForesTycoon
{
    internal readonly record struct ForestHydrologyInputs(double Cover, double InterceptionCapacity, double LeafAreaIndex);

    sealed partial class ForestSystem
    {
        // Demand is proportional to each living tree's projected crown/leaf area and health.
        // Aggregating per cell permits proportional uptake without visiting each tree per water tick.
        internal ForestHydrologyInputs HydrologyInputs(int tileId)
        {
            if (!IndividualTrees.TryGet(tileId, out var patch)) return default;
            var geometry = habitat.GetForestTileGeometry(tileId);
            double area = Math.Max(0.01, geometry.Width * geometry.Height);
            double crownArea = 0, interception = 0, leafArea = 0;
            for (int i = 0; i < patch.Count; i++)
            {
                var tree = patch.Trees[i];
                var size = tree.At(ForestYear);
                double crown = Math.PI * size.CrownRadius * size.CrownRadius * tree.Health;
                crownArea += crown;
                interception += crown * (ForestSpeciesTraits.For(tree.Species).Evergreen ? 2.5 : 1.5);
                leafArea += crown * (ForestSpeciesTraits.For(tree.Species).Evergreen ? 3.5 : 2.5);
            }
            double cover = 1 - Math.Exp(-crownArea / area);
            return new(cover, crownArea > 0 ? cover * interception / crownArea : 0,
                Math.Clamp(leafArea / area, 0, 6));
        }
    }
}
