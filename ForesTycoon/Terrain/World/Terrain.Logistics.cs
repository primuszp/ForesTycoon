using System;
using System.Collections.Generic;
using System.Drawing;
using OpenTK.Graphics.OpenGL;
using OpenTK.Mathematics;
namespace ForesTycoon
{
    partial class Terrain
    {
        private readonly HashSet<int> buildingTiles=new();
        internal void SetBuildingFootprint(int[] ids){foreach(int id in ids)buildingTiles.Add(id);}
        internal bool IsBuildingTile(int id)=>buildingTiles.Contains(id);
        internal bool TryGetSawmillFootprint(int id,out int[] footprint,out Vector3 position)
        {
            footprint=Array.Empty<int>();position=default;
            if(!IsValidTileId(id))return false;
            int u=id/(nodeRows-1),v=id%(nodeRows-1);
            if(!checkTile(u+1,v+1))return false;
            footprint=new[]{id,getTileByCoords(u+1,v).Id,getTileByCoords(u,v+1).Id,getTileByCoords(u+1,v+1).Id};
            float low=float.MaxValue,high=float.MinValue;
            foreach(int tileId in footprint){Tile tile=tiles[tileId];if(roads.Has(tileId)||ShouldDrawStandingWater(tile)||CountRiverCorners(tile)>0||buildingTiles.Contains(tileId))return false;
                foreach(Node node in new[]{tile.W,tile.S,tile.E,tile.N}){low=Math.Min(low,node.zPos);high=Math.Max(high,node.zPos);}}
            if(high-low>0.05f)return false;
            TryGetTileCenter(id,out var first);TryGetTileCenter(footprint[3],out var last);position=(first+last)*0.5f;return true;
        }
        internal List<int> FindRoadDocks(int[] ids)
        {
            var result=new List<int>();
            foreach(int id in ids)foreach(Tile tile in data.GetAdjacentTiles(tiles[id]))if(roads.Has(tile.Id)&&!result.Contains(tile.Id))result.Add(tile.Id);
            result.Sort();return result;
        }
        internal int[] FindLogisticsRoadPath(int start,int end)=>RoadPathfinder.FindPath(roads,nodeRows-1,start,end);
        internal void DrawHarvestSites(ForestryLogistics logistics)
        {
            if(logistics==null)return;
            using var state=new RenderStateScope().AlphaBlend().DepthWrite(false);
            DynamicPrimitiveBatch.Draw(PrimitiveType.Quads,()=>{
                DynamicPrimitiveBatch.Color4(Color.FromArgb(35,255,177,50));
                foreach(var site in logistics.Sites){if(logistics.Volume(site)<=0.001f)continue;foreach(int id in site.Tiles)TileQuad(tiles[id]);}
            });
            DynamicPrimitiveBatch.Draw(PrimitiveType.Lines,()=>{
                DynamicPrimitiveBatch.Color4(Color.FromArgb(235,255,180,55));
                foreach(var site in logistics.Sites){if(logistics.Volume(site)<=0.001f)continue;foreach(int id in site.Tiles){Tile t=tiles[id];DrawTileEdgesByMaterial(t,GetTileSurfaceVisual(t),Color.FromArgb(255,180,55));}}
            });
        }
    }
}
