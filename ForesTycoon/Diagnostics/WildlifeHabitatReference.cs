using System;
using System.Collections.Generic;
using OpenTK.Mathematics;

namespace ForesTycoon
{
    // Frozen habitat selection before batching and position-only sampling. Diagnostic oracle only.
    partial class Terrain
    {
        internal void DiagnosticCollectWildlifeSpotsReference(ForestSystem forest,List<WildlifeSpot> output)
        {
            output.Clear();
            Span<TreeInstance> stems=stackalloc TreeInstance[ForestTreeStore.PlantedTreesPerTile];
            Span<TreeInstance> neighboursBuffer=stackalloc TreeInstance[ForestTreeStore.PlantedTreesPerTile];
            foreach(Tile tile in tiles)
            {
                if(roads.Has(tile.Id)||ShouldDrawStandingWater(tile)||CountRiverCorners(tile)>0||!((IForestHabitat)this).CanSupportForest(tile.Id))continue;
                float low=Math.Min(Math.Min(tile.W.zPos,tile.S.zPos),Math.Min(tile.E.zPos,tile.N.zPos));
                float high=Math.Max(Math.Max(tile.W.zPos,tile.S.zPos),Math.Max(tile.E.zPos,tile.N.zPos));
                if(high-low>Math.Min(tileSizeH,tileSizeV)*0.4f)continue;
                forest.TryGetStand(tile.Id,out var stand);
                int forestNeighbours=0;bool nearRoad=false;
                foreach(Tile adjacent in data.GetAdjacentTiles(tile)) {
                    if(roads.Has(adjacent.Id))nearRoad=true;
                    if(forest.TryGetStand(adjacent.Id,out var neighbouring)&&neighbouring.Maturity>=0.2f)forestNeighbours++;
                }
                if(nearRoad)continue;
                if(stand.IsEmpty ? forestNeighbours<1 : stand.Maturity<0.2f)continue;
                // Stable seeded selection across the whole map, independent of camera.
                uint rank=unchecked((uint)(tile.Id*747796405+settings.Seed*2891336453L));rank=(rank^(rank>>16))*2246822519u;rank^=rank>>13;
                rank=(rank&0x7fffffffu)|(stand.IsEmpty?0u:0x80000000u);
                if(output.Count==16&&rank>=output[^1].Rank)continue;
                Vector3 center=new((tile.W.xPos+tile.E.xPos)*0.5f,(tile.W.yPos+tile.E.yPos)*0.5f,0);
                int count=CollectIndividualStems(forest,tile,stems);
                Vector2 best=center.Xy;float bestDistance=-1;
                for(int x=-1;x<=1;x++)for(int y=-1;y<=1;y++) {
                    Vector2 point=center.Xy+new Vector2(x*tileSizeH*0.2f,y*tileSizeV*0.2f);float distance=float.MaxValue;
                    for(int j=0;j<count;j++)distance=Math.Min(distance,(point-new Vector2(stems[j].X,stems[j].Y)).LengthSquared);
                    foreach(Tile adjacent in data.GetAdjacentTiles(tile)) {
                        if(!forest.TryGetStand(adjacent.Id,out var nearby))continue;
                        Span<TreeInstance> neighbourStems=neighboursBuffer;
                        int nearbyCount=CollectIndividualStems(forest,adjacent,neighbourStems);
                        for(int j=0;j<nearbyCount;j++)distance=Math.Min(distance,(point-new Vector2(neighbourStems[j].X,neighbourStems[j].Y)).LengthSquared);
                    }
                    if(distance>bestDistance){bestDistance=distance;best=point;}
                }
                if(!TryGetSurfaceZ(best.X,best.Y,out float z))continue;
                var spot=new WildlifeSpot(tile.Id,new Vector3(best.X,best.Y,z),rank);
                int index=0;while(index<output.Count&&output[index].Rank<rank)index++;
                output.Insert(index,spot);if(output.Count>16)output.RemoveAt(16);
            }
        }
    }
}
