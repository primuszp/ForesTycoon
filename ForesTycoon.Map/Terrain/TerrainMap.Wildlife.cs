using System;
using System.Collections.Generic;
using OpenTK.Mathematics;
namespace ForesTycoon.Map
{
    internal readonly record struct WildlifeSpot(int TileId, Vector3 Position, uint Rank);
    internal sealed partial class TerrainMap
    {
        internal ForesTycoon.Rules.GameplayProgram Gameplay { get; set; } = ForesTycoon.Rules.GameplayProgram.Default;
        internal bool CanWildlifeWalkAt(float x,float y) =>
            TryGetTileCoordinates(x,y,out int u,out int v,out _,out _) &&
            TryGetWildlifeDestination(GetTile(u,v).Id,out _);
        internal bool TryGetWildlifeDestination(int id, out Vector3 position)
        {
            position = default;
            Tile tile = tiles[id];
            float low = Math.Min(Math.Min(tile.W.zPos, tile.S.zPos), Math.Min(tile.E.zPos, tile.N.zPos));
            float high = Math.Max(Math.Max(tile.W.zPos, tile.S.zPos), Math.Max(tile.E.zPos, tile.N.zPos));
            if (Gameplay.Evaluate("wildlife.walkable", roads.Has(id) ? 1 : 0, ShouldDrawStandingWater(tile) ? 1 : 0,
                CountRiverCorners(tile) > 0 ? 1 : 0, ((IForestHabitat)this).CanSupportForest(id) ? 1 : 0,
                (high - low) / Math.Min(data.TileSizeH, data.TileSizeV)) < .5) return false;
            return TryGetTileCenter(id, out position);
        }
        internal void CollectWildlifeSpots(ForestSystem forest,List<WildlifeSpot> output,bool stopAfterFirst=false)
            => SimulationWork.Complete(CollectWildlifeSpotsWork(forest, output, stopAfterFirst));
        internal IEnumerable<int> CollectWildlifeSpotsWork(ForestSystem forest, List<WildlifeSpot> output, bool stopAfterFirst = false)
        {
            output.Clear();
            // Reuse one flattened sample for all nine candidate positions. Preserve the
            // original own-tile/adjacent-tile order so minimum-distance ties stay identical.
            Vector2[] stems=new Vector2[ForestTreeStore.PlantedTreesPerTile * 5];
            int limit = (int)Gameplay.Evaluate("wildlife.habitatCapacity");
            if (limit == 0) yield break;
            foreach(Tile tile in tiles)
            {
                yield return 1;
                if (!TryGetWildlifeDestination(tile.Id, out _)) continue;
                forest.TryGetStand(tile.Id,out var stand);
                int forestNeighbours=0;bool nearRoad=false;
                foreach(Tile adjacent in data.GetAdjacentTiles(tile).ToArray()) {
                    if(roads.Has(adjacent.Id))nearRoad=true;
                    forest.TryGetStand(adjacent.Id, out var neighbouring);
                    if (Gameplay.Evaluate("wildlife.neighbour", neighbouring.IsEmpty ? 1 : 0, neighbouring.Maturity) >= .5) forestNeighbours++;
                }
                if (Gameplay.Evaluate("wildlife.habitat", nearRoad ? 1 : 0, stand.IsEmpty ? 1 : 0, forestNeighbours, stand.Maturity) < .5) continue;
                // Stable seeded selection across the whole map, independent of camera.
                uint rank=unchecked((uint)(tile.Id*747796405+settings.Seed*2891336453L));rank=(rank^(rank>>16))*2246822519u;rank^=rank>>13;
                rank = (uint)Gameplay.Evaluate("wildlife.habitatRank", rank & 0x7fffffffu, stand.IsEmpty ? 1 : 0, stand.Maturity);
                if(output.Count==limit&&rank>=output[^1].Rank)continue;
                Vector3 center=new((tile.W.xPos+tile.E.xPos)*0.5f,(tile.W.yPos+tile.E.yPos)*0.5f,0);
                int count=CollectWildlifeStemPositions(forest,tile,stems.AsSpan(0,ForestTreeStore.PlantedTreesPerTile));
                foreach(Tile adjacent in data.GetAdjacentTiles(tile).ToArray()) {
                    if(!forest.TryGetStand(adjacent.Id,out _))continue;
                    count+=CollectWildlifeStemPositions(forest,adjacent,stems.AsSpan(count,ForestTreeStore.PlantedTreesPerTile));
                }
                Vector2 best=center.Xy;float bestDistance=-1;
                for(int x=-1;x<=1;x++)for(int y=-1;y<=1;y++) {
                    Vector2 point=center.Xy+new Vector2(x*data.TileSizeH*0.2f,y*data.TileSizeV*0.2f);float distance=float.MaxValue;
                    for(int j=0;j<count;j++)distance=Math.Min(distance,(point-stems[j]).LengthSquared);
                    if(distance>bestDistance){bestDistance=distance;best=point;}
                }
                if(!TryGetSurfaceZ(best.X,best.Y,out float z))continue;
                var spot=new WildlifeSpot(tile.Id,new Vector3(best.X,best.Y,z),rank);
                int index=0;while(index<output.Count&&output[index].Rank<rank)index++;
                output.Insert(index,spot);if(output.Count>limit)output.RemoveAt(limit);
                if(stopAfterFirst)yield break;
            }
        }

        // Habitat selection needs physical stem positions, not species rendering models,
        // life-stage meshes or appearance/scale parameters.
        private static int CollectWildlifeStemPositions(ForestSystem forest, Tile tile, Span<Vector2> output)
        {
            if (!forest.IndividualTrees.TryGet(tile.Id, out var patch)) return 0;
            int count = Math.Min(patch.Count, output.Length);
            for (int i = 0; i < count; i++)
            {
                var tree = patch.Trees[i];
                SurfacePoint(tile, tree.U, tree.V, out float x, out float y, out _);
                output[i] = new(x, y);
            }
            return count;
        }
    }
}
