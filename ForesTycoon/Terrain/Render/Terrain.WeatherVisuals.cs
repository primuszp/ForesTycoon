using System;
using OpenTK.Mathematics;

namespace ForesTycoon
{
    partial class Terrain
    {
        internal ulong WeatherSurfaceRevision => map.SurfaceVersion;

        internal void GetWeatherBounds(out Vector3 min, out Vector3 max)
        {
            // Map footprint is stable; no per-frame scan through every node.
            min = new Vector3(nodes[0].xPos, nodes[0].yPos, 0);
            max = new Vector3(nodes[^1].xPos, nodes[^1].yPos, settings.MaxHeight * settings.HeightScale + 22);
        }

        internal void GetVisibleWeatherBounds(out Vector2 min, out Vector2 max)
        {
            min = new Vector2(float.MaxValue); max = new Vector2(float.MinValue);
            foreach (Tile tile in visibleTiles)
            {
                min = Vector2.ComponentMin(min, new Vector2(tile.W.xPos, tile.W.yPos));
                max = Vector2.ComponentMax(max, new Vector2(tile.E.xPos, tile.E.yPos));
            }
        }

        internal void FillWeatherHeights(float[] heights, ForestSystem forest)
        {
            foreach (Tile tile in tiles)
            {
                float z = Math.Max(Math.Max(tile.W.zPos, tile.S.zPos), Math.Max(tile.E.zPos, tile.N.zPos));
                z = Math.Max(z, settings.SeaLevel);
                if (forest.TryGetStand(tile.Id, out ForestStand stand))
                    z += 2 + 6 * Math.Clamp(stand.Maturity, 0, 1.5f);
                // Tile IDs are column-major; OpenGL images are row-major.
                int u = tile.Id / settings.TileRows, v = tile.Id % settings.TileRows;
                heights[v * settings.TileColumns + u] = z;
            }
        }

        private readonly System.Collections.Generic.Dictionary<int, (bool HasTrees, ulong TerrainRevision, FogSource? Source)> fogSources = new();
        internal void CollectForestWeather(System.Collections.Generic.List<FogSource> mist, ForestSystem forest,
            System.Collections.Generic.List<Vector3> crowns, bool collectCrowns,EnvironmentSystem environment=null)
        {
            mist.Clear(); crowns.Clear();
            foreach (TerrainChunk chunk in visibleChunks)
            {
                for(int i=0;i<chunk.TileIds.Length;i++)
                {
                    Tile tile=tiles[chunk.TileIds[i]];
                    forest.IndividualTrees.TryGet(tile.Id, out var patch);
                    int count = patch?.Count ?? 0;
                    bool cached = fogSources.TryGetValue(tile.Id, out var entry)
                        && entry.HasTrees == (count > 0) && entry.TerrainRevision == WeatherSurfaceRevision;
                    if (!cached)
                    {
                        float x=(tile.W.xPos+tile.E.xPos)*0.5f, y=(tile.W.yPos+tile.E.yPos)*0.5f;
                        float z=(tile.W.zPos+tile.E.zPos+tile.N.zPos+tile.S.zPos)*0.25f;
                        float water=ShouldDrawStandingWater(tile)||CountRiverCorners(tile)>0?1:0;
                        float surrounding=0; int neighbours=0;
                        foreach(Tile adjacent in data.GetAdjacentTiles(tile)){
                            surrounding+=(adjacent.W.zPos+adjacent.E.zPos+adjacent.N.zPos+adjacent.S.zPos)*0.25f;
                            neighbours++;
                            if(ShouldDrawStandingWater(adjacent)||CountRiverCorners(adjacent)>0) water=Math.Max(water,0.7f);
                        }
                        float valley=neighbours>0?Math.Clamp((surrounding/neighbours-z)/2.5f,0,1):0;
                        FogSource? source = null;
                        if(count>0||water>0||valley>0.25f)
                            source = new FogSource(new Vector4(x,y,Math.Max(z,water>0?settings.SeaLevel:z)+1.4f,
                                Math.Max(3,(tile.E.xPos-tile.W.xPos)*0.7f)),count>0?1:0,water,valley,tileMoisture[tile.Id]);
                        entry = (count > 0, WeatherSurfaceRevision, source);
                        fogSources[tile.Id] = entry;
                    }
                    if (entry.Source.HasValue && mist.Count < 768) {
                        var source=entry.Source.Value;
                        if(environment!=null){var cell=environment.Cell(tile.Id);source=source with {Moisture=(float)Math.Clamp(cell.Soil/180+cell.Surface/5,0,1)};}
                        mist.Add(source);
                    }
                    for(int j=0;collectCrowns && j<count;j++){
                        TreeInstance tree=IndividualStem(tile,patch.Trees[j],forest.ForestYear); TreeModel model=TreeModel.For(tree.Stand.Species);
                        float h=model.CrownHeight*tree.Scale*tree.CrownRise;
                        crowns.Add(new Vector3(tree.X,tree.Y,tree.TrunkTop(model)+h*(1-model.CrownDrop)));
                    }
                }
            }
        }
    }
}
