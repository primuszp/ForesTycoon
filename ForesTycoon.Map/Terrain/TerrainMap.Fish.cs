using System;
using System.Collections.Generic;
using OpenTK.Mathematics;
namespace ForesTycoon.Map
{
    internal readonly record struct FishHabitat(Vector3 Position,float Depth,uint Seed);
    internal sealed partial class TerrainMap
    {
        /// <param name="waterLevel">Water surface height at a node (the renderer owns the wave shape).</param>
        internal void CollectFishHabitats(List<FishHabitat> result, Func<Node, float> waterLevel)
        {
            result.Clear();var visited=new bool[tiles.Length];var queue=new Queue<int>();var suitable=new List<FishHabitat>();
            foreach(Tile origin in tiles){
                if(visited[origin.Id]||!hydro.HasDynamicWater(origin))continue;
                visited[origin.Id]=true;queue.Enqueue(origin.Id);suitable.Clear();int area=0;bool border=false;
                while(queue.Count>0){Tile tile=tiles[queue.Dequeue()];area++;border|=data.IsBorderTile(tile);
                    float depth=Math.Min(Math.Min(hydro.NodeWaterDepth[tile.W.Id],hydro.NodeWaterDepth[tile.S.Id]),Math.Min(hydro.NodeWaterDepth[tile.E.Id],hydro.NodeWaterDepth[tile.N.Id]));
                    if(depth>0.3f&&TryGetTileCenter(tile.Id,out Vector3 center)){
                        float level=(waterLevel(tile.W)+waterLevel(tile.S)+waterLevel(tile.E)+waterLevel(tile.N))*0.25f;
                        center.Z=level-Math.Min(depth*0.45f,0.55f);
                        suitable.Add(new FishHabitat(center,depth,unchecked((uint)(tile.Id*7919+settings.Seed))));
                    }
                    foreach(Tile next in data.GetAdjacentTiles(tile))if(!visited[next.Id]&&hydro.HasDynamicWater(next)){visited[next.Id]=true;queue.Enqueue(next.Id);}
                }
                if(suitable.Count==0)continue;
                bool sea=border&&area>=32;
                int count=sea?Math.Clamp(area/4,8,120):Math.Min(2,suitable.Count);
                for(int i=0;i<count;i++)result.Add(suitable[(int)((long)i*suitable.Count/count)]);
            }
        }
    }
}
