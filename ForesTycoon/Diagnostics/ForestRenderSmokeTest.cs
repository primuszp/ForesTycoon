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
                Require(far < medium && medium < near, "Forest LOD did not reduce geometry with zoom.");
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
                Require(terrain.ForestChunkRebuilds == 0, "Monthly growth rebuilt topology instead of refreshing GPU state.");
                Draw(terrain, forest, 12);
                Require(terrain.ForestChunkRebuilds == 0, "Stable frame rebuilt monthly geometry.");
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
                var editTimer = System.Diagnostics.Stopwatch.StartNew();
                terrain.EditElevationAtNode(4 * 33 + 4, 1, 0, 1);
                Draw(terrain, forest, 1.5f);
                editTimer.Stop();
                Console.WriteLine($"Local terrain edit + forest refresh: {editTimer.Elapsed.TotalMilliseconds:F1} ms, {terrain.ForestChunkRebuilds}/{terrain.VisibleChunkCount} forest chunks rebuilt.");
                Require(terrain.ForestChunkRebuilds > 0 && terrain.ForestChunkRebuilds < terrain.VisibleChunkCount,
                    "Local terrain edit rebuilt unrelated forest chunks.");
                Draw(terrain, forest, 1.5f);
                Require(terrain.ForestChunkRebuilds == 0, "Edited forest cache did not settle.");
                // A central terrain edit must invalidate positions even without a forest revision.
                terrain.EditElevationAtNode(16 * 33 + 16, 1, 0, 1);
                Draw(terrain, forest, 1.5f);
                Require(terrain.ForestChunkRebuilds > 0, "Terrain edit did not invalidate geometry.");
                forest.Clear();
                Require(Draw(terrain, forest, 1.5f) == 0, "Cleared forest left stale GPU geometry.");
                Require(GL.GetError() == ErrorCode.NoError, "OpenGL reported an error.");
                CheckStaticTerrainCache();
                CheckDeferredForestBuild();
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
                int uploads = terrain.TerrainEdgeUploads;
                terrain.EditElevationAtNode(10 * 17 + 10, 1, 2, 2);
                Require(terrain.TerrainEdgeUploads == uploads + 1, "Brush uploaded the entire terrain grid more than once.");
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

        private static void CheckDeferredForestBuild()
        {
            using var terrain = new Terrain(TerrainSettings.Default.WithNodeSize(17, 42), (_, _) => 4);
            var forest = new ForestSystem(terrain);
            forest.Clear(); forest.Plant(34, ForestSpecies.Oak);
            forest.IndividualTrees.TryGet(34, out var patch);
            void SetBoundary()
            {
                for (int i = 0; i < patch.Count; i++)
                    patch.Trees[i] = patch.Trees[i].Settle(forest.ForestYear) with
                        { BirthYear = forest.ForestYear - 2.99, AnnualGrowth = default };
                forest.NotifyIndividualVisualEdit();
                Draw(terrain, forest, 12);
            }
            SetBoundary();
            forest.Update(.6);
            Draw(terrain, forest, 12);
            Require(terrain.ForestChunkRebuilds == 0, "Life-stage geometry was built synchronously.");
            bool published = false;
            for (int frame = 0; frame < 500 && !published; frame++)
            {
                Draw(terrain, forest, 12);
                published = terrain.ForestChunkRebuilds > 0;
            }
            Require(published, "Deferred forest build never published.");
            Draw(terrain, forest, 12);
            Require(terrain.ForestChunkRebuilds == 0, "Completed forest build did not settle.");
            SetBoundary(); forest.Update(.6); Draw(terrain, forest, 12);
            forest.Clear();
            Require(Draw(terrain, forest, 12) == 0, "Clear retained stale deferred geometry.");
            for (int frame = 0; frame < 5; frame++)
                Require(Draw(terrain, forest, 12) == 0, "Cancelled build reintroduced old trees.");
            Console.WriteLine("Deferred forest geometry: stage publication and cancellation after clear passed.");
        }
    }
}
