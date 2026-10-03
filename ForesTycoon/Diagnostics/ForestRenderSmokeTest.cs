using System;
using OpenTK.Graphics.OpenGL;
using OpenTK.Mathematics;
using OpenTK.Windowing.Common;
using OpenTK.Windowing.Desktop;

namespace ForesTycoon
{
    // Explicit opt-in GL integration test; the ordinary unit suite stays headless.
    internal static class ForestRenderSmokeTest
    {
        internal static void Run()
        {
            using var window = new NativeWindow(new NativeWindowSettings
            {
                StartVisible = false,
                ClientSize = new Vector2i(256, 256),
                API = ContextAPI.OpenGL,
                APIVersion = new Version(3, 3),
                Profile = ContextProfile.Core,
                Flags = ContextFlags.ForwardCompatible
            });
            window.Context.MakeCurrent();
            RenderDevice.Initialize();
            Terrain terrain = null;
            try
            {
                terrain = new Terrain(TerrainSettings.Default.WithNodeSize(33, 42));
                var forest = new ForestSystem(terrain);
                Require(forest.Count > 0, "Test map has no forest.");
                RenderDevice.SetCamera(Matrix4.Identity);
                int near = Draw(terrain, forest, 12);
                Require(terrain.ForestChunkRebuilds == terrain.VisibleChunkCount, "Initial chunks were not built.");
                Draw(terrain, forest, 12);
                Require(terrain.ForestChunkRebuilds == 0, "Unchanged frame rebuilt the forest.");
                int medium = Draw(terrain, forest, 5);
                int far = Draw(terrain, forest, 1);
                Require(far == medium && medium == near, "Disabled LOD changed forest detail with zoom.");
                Draw(terrain, forest, 1.5f);
                Require(terrain.ForestChunkRebuilds == 0, "Zoom inside one LOD rebuilt the forest.");
                Draw(terrain, forest, 12);
                Require(terrain.ForestChunkRebuilds == 0, "Returning to a cached LOD rebuilt the forest.");
                Draw(terrain,forest,3.5f);
                Draw(terrain,forest,9);
                Draw(terrain,forest,9);
                Require(terrain.ForestChunkRebuilds==0,"LOD blending rebuilt cached meshes.");
                Require(RenderDevice.LodRange==new Vector2(0,1),"LOD mask leaked to other objects.");
                forest.Update(ForestSystem.DefaultSecondsPerYear / 12);
                Draw(terrain, forest, 12);
                int remainingFrames = 1000;
                while (terrain.PendingForestBuildCount > 0 && remainingFrames-- > 0) Draw(terrain, forest, 12);
                Require(terrain.PendingForestBuildCount == 0, "Incremental forest refresh never completed.");
                Draw(terrain, forest, 12);
                Require(terrain.ForestChunkRebuilds == 0, "Completed incremental refresh rebuilt again.");
                Draw(terrain, forest, 1.5f);
                int tileId = 0;
                while (!forest.TryGetStand(tileId, out _)) tileId++;
                forest.Harvest(tileId, out _);
                Draw(terrain, forest, 1.5f);
                Require(terrain.ForestChunkRebuilds > 0, "Harvest did not invalidate geometry.");
                Require(terrain.ForestChunkRebuilds < terrain.VisibleChunkCount, "Harvest rebuilt unrelated chunks.");
                forest.Plant(tileId, ForestSpecies.Oak);
                Draw(terrain, forest, 1.5f);
                Require(terrain.ForestChunkRebuilds > 0, "Planting did not invalidate geometry.");
                // A central terrain edit must invalidate positions even without a forest revision.
                terrain.EditElevationAtNode(16 * 33 + 16, 1, 0, 1);
                Draw(terrain, forest, 1.5f);
                Require(terrain.ForestChunkRebuilds > 0, "Terrain edit did not invalidate geometry.");
                forest.Clear();
                Require(Draw(terrain, forest, 1.5f) == 0, "Cleared forest left stale GPU geometry.");
                Require(GL.GetError() == ErrorCode.NoError, "OpenGL reported an error.");
                CheckStaticTerrainCache();
                Console.WriteLine($"Forest GL smoke test passed: near={near}, medium={medium}, far={far} vertices; stable frames rebuild 0 chunks.");
            }
            finally
            {
                terrain?.Dispose();
                RenderDevice.Dispose();
            }
        }

        private static int Draw(Terrain terrain, ForestSystem forest, float scale)
        {
            var context = new RenderContext(0, 0, 0, 0, 0, 0, false, false, 1,
                -60, -45, -10000, -10000, 10000, 10000, scale);
            terrain.UpdateVisibleTiles(context);
            RenderMetrics.BeginFrame();
            terrain.DrawTrees(forest, context);
            return RenderMetrics.SubmittedVertices;
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }

        private static void CheckStaticTerrainCache()
        {
            var terrain = new Terrain(TerrainSettings.Default.WithNodeSize(17, 42), ForestVisualFixture.Height);
            try
            {
                var context = new RenderContext(0, 0, 0, 0, 0, 0, false, false, 1, -60, -45,
                    -10000, -10000, 10000, 10000, 10);
                terrain.UpdateVisibleTiles(context);
                Check(true);
                Check(false);
                terrain.BuildRoadTilePath(34, 37);
                Require(terrain.RoadCount > 0, "Road cache test did not build a road.");
                Check(true);
                Check(false);
                terrain.EditElevationAtNode(3 * 17 + 3, 1, 0, 1);
                Check(true);
                terrain.RemoveRoadTilePath(34, 37);
                Check(true);
                Require(GL.GetError() == ErrorCode.NoError, "Static terrain cache GL error.");
                Console.WriteLine("Static terrain cache: stable reuse, road build/removal and elevation invalidation passed.");
                void Check(bool rebuild)
                {
                    terrain.DrawTerrainBase();
                    terrain.DrawTerrainDecals();
                    Require(terrain.CachedGridHasAllTileBoundaries(), "A tile is missing one or more grid boundaries.");
                    Require((terrain.StaticTerrainRebuilds > 0) == rebuild, "Unexpected static terrain rebuild count.");
                    Require(terrain.SurfaceCacheMatchesFreshCalculation(), "Stale terrain surface classification.");
                }
            }
            finally { terrain.Dispose(); }
        }
    }
}
