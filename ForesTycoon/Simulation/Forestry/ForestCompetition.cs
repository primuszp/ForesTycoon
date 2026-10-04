using System;
using System.Collections.Generic;

namespace ForesTycoon
{
    internal readonly record struct ForestTileGeometry(float X, float Y, float Width, float Height);
    internal readonly record struct ForestResources(float Light, float Water, float Space)
    {
        internal float LightResponse(ForestSpecies species) => MathF.Pow(Math.Clamp(Light, 0, 1),
            1 - ForestSpeciesProfile.For(species).ShadeTolerance * 0.65f);
        internal float Limitation(ForestSpecies species) => Math.Min(Space, Math.Min(Water, LightResponse(species)));
    }

    // FORMIND-inspired mechanisms, with local crown/root overlap rather than a
    // calibrated carbon balance. Snapshot all trees before updating any of them.
    internal sealed class ForestCompetition
    {
        private readonly record struct Sample(ulong Id, float X, float Y, ForestTreeDimensions Size, float Health);
        private sealed class Cell { internal Sample[] Trees = Array.Empty<Sample>(); internal int Count; }
        private readonly Dictionary<int, Cell> cells = new();
        private readonly Dictionary<int, int[]> neighbours = new();
        internal void Clear() { cells.Clear(); neighbours.Clear(); }
        internal void Snapshot(IForestHabitat habitat, ForestTreeStore store, double year)
        {
            foreach (var cell in cells.Values) cell.Count = 0;
            Span<int> adjacent = stackalloc int[16];
            foreach (var entry in store.Patches)
            {
                if (!cells.TryGetValue(entry.Key, out var cell)) cells.Add(entry.Key, cell = new());
                if (cell.Trees.Length < entry.Value.Trees.Length) cell.Trees = new Sample[entry.Value.Trees.Length];
                var geometry = habitat.GetForestTileGeometry(entry.Key);
                cell.Count = entry.Value.Count;
                for (int i = 0; i < cell.Count; i++)
                {
                    var tree = entry.Value.Trees[i];
                    cell.Trees[i] = new(tree.Id, geometry.X + tree.U * geometry.Width,
                        geometry.Y + tree.V * geometry.Height, tree.At(year), tree.Health);
                }
                if (!neighbours.ContainsKey(entry.Key))
                {
                    var ids = new List<int> { entry.Key };
                    int firstRing = habitat.GetAdjacentTileIds(entry.Key, adjacent);
                    for (int i = 0; i < firstRing; i++) if (!ids.Contains(adjacent[i])) ids.Add(adjacent[i]);
                    int end = ids.Count;
                    for (int n = 1; n < end; n++)
                    {
                        int count = habitat.GetAdjacentTileIds(ids[n], adjacent);
                        for (int i = 0; i < count; i++) if (!ids.Contains(adjacent[i])) ids.Add(adjacent[i]);
                    }
                    neighbours.Add(entry.Key, ids.ToArray());
                }
            }
        }

        internal ForestResources Evaluate(IForestHabitat habitat, in ForestTree tree, double year, float siteWater)
        {
            if (!neighbours.TryGetValue(tree.TileId, out var ids)) return new(1, siteWater, 1);
            var geometry = habitat.GetForestTileGeometry(tree.TileId);
            float x = geometry.X + tree.U * geometry.Width, y = geometry.Y + tree.V * geometry.Height;
            var size = tree.At(year);
            float shade = 0, crownLoad = 0, rootLoad = 0;
            foreach (int id in ids)
            {
                if (!cells.TryGetValue(id, out var cell)) continue;
                for (int i = 0; i < cell.Count; i++)
                {
                    var other = cell.Trees[i];
                    if (other.Id == tree.Id) continue;
                    float dx = other.X - x, dy = other.Y - y, distance2 = dx * dx + dy * dy;
                    float reach = size.CrownRadius + other.Size.CrownRadius;
                    float overlap = Overlap(distance2, reach);
                    float demandRatio = Math.Clamp(other.Size.CrownRadius * other.Size.CrownRadius
                        / Math.Max(0.0225f, size.CrownRadius * size.CrownRadius), 0.001f, 4);
                    crownLoad += overlap * demandRatio;
                    // Height-asymmetric interception: taller neighbours shade more strongly.
                    float dominance = Math.Clamp(0.5f + (other.Size.Height - size.Height) / Math.Max(0.8f, size.Height) * 2, 0, 1);
                    shade += overlap * 2 * dominance * other.Health;
                    rootLoad += Overlap(distance2, Math.Max(1, reach * 1.25f)) * MathF.Sqrt(demandRatio);
                }
            }
            return new(MathF.Exp(-0.65f * shade), Math.Clamp(siteWater, 0, 1) / (1 + rootLoad * 0.22f),
                1 / (1 + crownLoad * 0.55f));
        }

        private static float Overlap(float distance2, float reach)
        {
            float t = Math.Max(0, 1 - distance2 / Math.Max(0.01f, reach * reach));
            return t * t;
        }
    }
}
