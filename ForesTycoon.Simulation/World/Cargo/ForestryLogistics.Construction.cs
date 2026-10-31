using System;
using System.Collections.Generic;

namespace ForesTycoon;

partial class ForestryLogistics
{
    internal IEnumerable<int> ConstructionLandingWork(int[] stroke, WorkResult<int> result)
    {
        result.Value = -1;
        var excluded = new HashSet<int>(); var candidates = new List<int>(); var seen = new HashSet<int>();
        foreach (int tile in stroke) { excluded.Add(tile); yield return 1; }
        var neighbours = new int[4];
        foreach (int tile in stroke) {
            int count = terrain.GetTileNeighbours(tile, neighbours);
            for (int i = 0; i < count; i++) {
                int next = neighbours[i];
                if (!excluded.Contains(next) && seen.Add(next)) candidates.Add(next);
                yield return 1;
            }
        }
        foreach (int tile in candidates) {
            yield return 1;
            if (StackAt(tile) != null) { result.Value = tile; yield break; }
        }
        foreach (int tile in candidates) {
            yield return 1;
            if (!terrain.IsNetworkTile(tile) && terrain.CanCarrySkidTrail(tile)) { result.Value = tile; yield break; }
        }
    }

    internal void StoreConstructionTimber(int tile, ForestClearance item)
    {
        if (item.Volume <= 0) return;
        var stack = StackAt(tile);
        if (stack == null) {
            if (terrain.IsNetworkTile(tile) || !terrain.CanCarrySkidTrail(tile) || forest.HasPhysicalObjects(tile))
                throw new InvalidOperationException("Az építési faanyag saranghelye foglalt vagy nem járható.");
            stack = new TimberStack { Id = nextStackId++, Tile = tile }; Stacks.Add(stack); InvalidateOrderPreviews();
        }
        stack.Add(item.Volume, item.Volume * TimberPrice(item.Species));
    }
}
