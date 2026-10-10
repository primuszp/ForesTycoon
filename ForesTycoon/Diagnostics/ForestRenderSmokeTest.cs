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
                var forest = new ForestSystem(terrain.Map);
                Require(forest.Count > 0, "Test map has no forest.");
                RenderDevice.SetCamera(Matrix4.Identity);
                int near = Draw(terrain, forest, 12);
                Require(terrain.ForestChunkRebuilds == terrain.VisibleChunkCount, "Initial chunks were not built.");
                Draw(terrain, forest, 12);
                Require(terrain.ForestChunkRebuilds == 0, "Unchanged frame rebuilt the forest.");
                // Cold LODs keep drawing a current mesh while their replacement builds.
                // Compare completed geometry rather than the transitional fallback.
                terrain.WarmIndividualForest(forest, new GraphicsSettings { Enhanced = false });
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
                // Resource refresh can queue crown-shape work independently of edits.
                // Drain it before isolating the local harvest/terrain invalidation checks.
                terrain.SynchronousForestBuilds = true;
                Draw(terrain, forest, 1.5f);
                terrain.SynchronousForestBuilds = false;
                int tileId = 0;
                while (!forest.TryGetStand(tileId, out _)) tileId++;
                forest.Harvest(tileId, out _);
                // Player edits rebuild in prioritised background steps: the frame never stalls, the result lands within a few frames.
                int rebuilt = DrawUntilRebuilt(terrain, forest);
                Require(rebuilt > 0, "Harvest did not invalidate geometry.");
                Require(rebuilt < terrain.VisibleChunkCount, "Harvest rebuilt unrelated chunks.");
                forest.Plant(tileId, ForestSpecies.Oak);
                Require(DrawUntilRebuilt(terrain, forest) > 0, "Planting did not invalidate geometry.");
                var editTimer = System.Diagnostics.Stopwatch.StartNew();
                terrain.Map.EditElevationAtNode(4 * 33 + 4, 1, 0, 1);
                Draw(terrain, forest, 1.5f);
                editTimer.Stop();
                rebuilt = terrain.ForestChunkRebuilds + DrawUntilRebuilt(terrain, forest);
                Console.WriteLine($"Local terrain edit frame: {editTimer.Elapsed.TotalMilliseconds:F1} ms; {rebuilt}/{terrain.VisibleChunkCount} forest chunks rebuilt.");
                Require(rebuilt > 0 && rebuilt < terrain.VisibleChunkCount, "Local terrain edit rebuilt unrelated forest chunks.");
                Draw(terrain, forest, 1.5f);
                Require(terrain.ForestChunkRebuilds == 0, "Edited forest cache did not settle.");
                // A central terrain edit must invalidate positions even without a forest revision.
                terrain.Map.EditElevationAtNode(16 * 33 + 16, 1, 0, 1);
                Require(DrawUntilRebuilt(terrain, forest) > 0, "Terrain edit did not invalidate geometry.");
                forest.Clear();
                Require(Draw(terrain, forest, 1.5f) == 0, "Cleared forest left stale GPU geometry.");
                Require(GL.GetError() == ErrorCode.NoError, "OpenGL reported an error.");
                CheckStaticTerrainCache();
                CheckScreenLineWidth();
                CheckLocalRoadEdit();
                CheckTerraformForestStability();
                CheckDeferredForestBuild();
                CheckFastForwardDetail();
                CheckPagedForestUpload();
                CheckDeadTreeLodState();
                CheckGrowingForestLodState();
                CheckStreamingForest();
                CheckColdWorldScaling();
                CheckStaticCacheEviction();
                CheckPreparedMapContinuity();
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

        private static void CheckFastForwardDetail()
        {
            using var terrain = new Terrain(TerrainSettings.Default.WithNodeSize(17, 42), (_, _) => 4);
            var stands = new ForestStand[256];
            stands[34] = new ForestStand(ForestSpecies.Spruce, 30, .6f, 1);
            var forest = new ForestSystem(terrain.Map, stands);
            terrain.WarmIndividualForest(forest, new GraphicsSettings { Enhanced = false });
            terrain.StreamGeometry = true;
            Draw(terrain, forest, 12);
            for (int frame = 0; frame < 180; frame++)
            {
                forest.Update(256 * Viewport.GamePace / 60);
                Draw(terrain, forest, 12);
                Require(terrain.ReadyVisibleForestChunks(ForestLod.Near) == terrain.VisibleChunkCount,
                    $"256x fast-forward downgraded detailed crowns at frame {frame}.");
            }
            for (int frame = 0; frame < 500 && terrain.HasPendingForestBuild; frame++) Draw(terrain, forest, 12);
            Require(!terrain.HasPendingForestBuild, "Forest updates did not settle after fast-forward stopped.");
            Console.WriteLine("256x fast-forward: detailed spruce crowns retained through growth and background LOD publication.");
        }

        private static void CheckStreamingForest()
        {
            using var terrain = new Terrain(TerrainSettings.Default.WithNodeSize(33, 42));
            var forest = new ForestSystem(terrain.Map);
            Require(forest.Count > 0, "Streaming test map has no forest.");
            terrain.StreamGeometry = true;
            Require(terrain.ForestResidentLods == 0, "Cold streaming terrain allocated forest meshes.");
            int first = Draw(terrain, forest, 12);
            Require(first == 0 && terrain.ForestChunkRebuilds == 0 && terrain.HasPendingForestBuild,
                "Cold streaming published partial geometry or built trees synchronously.");
            int frames = 0;
            while (terrain.TotalForestChunkRebuilds < terrain.VisibleChunkCount && frames++ < 2000)
            {
                Draw(terrain, forest, 12);
                Require(terrain.ReadyVisibleForestChunks(ForestLod.Far) == 0,
                    "Cold close-up displayed a distant proxy before its detailed trees.");
            }
            Require(terrain.ReadyVisibleForestChunks(ForestLod.Near) == terrain.VisibleChunkCount,
                "Cold close-up streaming did not build the requested detailed models directly.");
            Require(terrain.ForestGpuPayloadBytes > 0 && terrain.ForestCpuPayloadBytes > 0,
                "Streaming payload accounting omitted resident geometry.");
            // Populate all levels as an explicit diagnostic, then enforce a deliberately
            // smaller-than-visible budget. Only continuity meshes and one build may remain.
            terrain.WarmIndividualForest(forest, new GraphicsSettings { Enhanced = false });
            int warmCount = terrain.ForestResidentLods;
            terrain.ForestCacheBudgetBytes = 1;
            int farVertices = Draw(terrain, forest, 1);
            Require(farVertices > 0, "Budget pressure removed visible trees.");
            Require(terrain.ForestResidentLods < warmCount && terrain.ForestCacheEvictions > 0,
                "Budget pressure did not evict unused LODs.");
            Require(terrain.ForestResidentLods <= terrain.VisibleChunkCount + 1,
                "Budget pressure retained unused forest levels.");
            Require(terrain.ForestBudgetExcessBytes == terrain.ForestCpuPayloadBytes + terrain.ForestGpuPayloadBytes - 1,
                "Visible working-set excess was not reported exactly.");
            int detailedVertices = Draw(terrain, forest, 12);
            for (int frame = 0; frame < 2000 && terrain.ReadyVisibleForestChunks(ForestLod.Near) < terrain.VisibleChunkCount; frame++)
                detailedVertices = Draw(terrain, forest, 12);
            Require(detailedVertices > farVertices && terrain.ReadyVisibleForestChunks(ForestLod.Near) == terrain.VisibleChunkCount,
                "Cache budget pressure permanently downgraded visible tree models.");
            var offscreen = new RenderContext(0, 0, 0, 0, 0, 0, false, false, 1,
                -60, -45, 100000, 100000, 100032, 100032, 12);
            terrain.UpdateVisibleTiles(offscreen);
            terrain.DrawTrees(forest, offscreen);
            Require(terrain.VisibleChunkCount == 0 && terrain.ForestResidentLods == 0 && terrain.ForestGpuPayloadBytes == 0,
                "Panning away retained offscreen forest GPU resources under pressure.");
            Require(!terrain.HasPendingForestBuild, "Offscreen build was not cancelled.");
            Require(Draw(terrain, forest, 12) == 0 && terrain.HasPendingForestBuild,
                "Returning to an evicted region did not restart a complete streaming build.");
            bool returned = false;
            for (int frame = 0; frame < 500 && !returned; frame++) returned = Draw(terrain, forest, 12) > 0;
            Require(returned, "Returning to an evicted region never restored visible trees.");
            forest.Clear();
            for (int frame = 0; frame < 8; frame++)
                Require(Draw(terrain, forest, 12) == 0, "Streaming cancellation resurrected cleared trees.");
            Require(GL.GetError() == ErrorCode.NoError, "Streaming/cache eviction produced an OpenGL error.");
            Console.WriteLine($"Streaming forest: cold frame submitted 0 vertices; requested Near coverage in {frames} frames; unused LOD eviction and visible-budget excess passed.");
        }

        private static void CheckPreparedMapContinuity()
        {
            using var terrain = new Terrain(TerrainSettings.Default.WithNodeSize(33, 42));
            var forest = new ForestSystem(terrain.Map);
            var graphics = new GraphicsSettings { Enhanced = false };
            terrain.StreamGeometry = true;
            terrain.ForestCacheBudgetBytes = terrain.StaticCacheBudgetBytes = 1;
            float progress = 0;
            foreach (float next in terrain.PrepareMapGeometry(forest, graphics))
            {
                Require(next >= progress && next <= 1, "Map loading progress regressed.");
                progress = next;
            }
            Require(progress == 1, "Map preparation did not finish.");
            int resident = terrain.ForestResidentLods, ground = terrain.StaticResidentChunks;
            Require(resident == ground * 3 && ground > 0, "Preparation missed terrain or a tree LOD.");
            long rebuilds = terrain.TotalForestChunkRebuilds;
            foreach (float scale in new[] { 12f, 1f, 5f, 20f, 2f, 12f })
            {
                var away = new RenderContext(0, 0, 0, 0, 0, 0, false, false, 1,
                    -60, -45, 100000, 100000, 100032, 100032, scale);
                terrain.UpdateVisibleTiles(away);
                terrain.DrawTerrainBase(); terrain.DrawTrees(forest, away, graphics);
                Require(Draw(terrain, forest, scale) > 0, "Prepared forest disappeared after a camera jump.");
                terrain.DrawTerrainBase(); terrain.DrawTerrainDecals();
                Require(terrain.StaticTerrainRebuilds == 0 && terrain.TotalForestChunkRebuilds == rebuilds,
                    "Pan/zoom rebuilt prepared geometry.");
                Require(!terrain.HasPendingForestBuild, "Pan/zoom queued a cold model load.");
                Require(terrain.ReadyVisibleForestChunks(ForestLodPolicy.Select(scale, null)) == terrain.VisibleChunkCount,
                    "Prepared map did not immediately display the requested detail.");
            }
            Require(terrain.ForestResidentLods == resident && terrain.StaticResidentChunks == ground &&
                terrain.ForestCacheEvictions == 0 && terrain.StaticCacheEvictions == 0,
                "Camera movement evicted prepared map geometry.");
            Console.WriteLine($"Prepared map continuity: {ground} chunks, all 3 LODs, no rebuilds or evictions; CPU/GPU {(terrain.ForestCpuPayloadBytes + terrain.ForestGpuPayloadBytes) / 1048576.0:F1} MiB.");
        }

        private static void CheckColdWorldScaling()
        {
            foreach (int nodes in new[] { 65, 129 })
            {
                long allocated = GC.GetAllocatedBytesForCurrentThread();
                var timer = System.Diagnostics.Stopwatch.StartNew();
                using var world = new GameWorld(TerrainSettings.Default.WithNodeSize(nodes, 42));
                double constructionMs = timer.Elapsed.TotalMilliseconds;
                Require(world.ForestResidentLods == 0 && world.ForestGpuPayloadBytes == 0,
                    "World construction warmed forest meshes before its camera was known.");
                RenderDevice.SetCamera(Matrix4.Identity);
                var context = new RenderContext(0, 0, 0, 0, 0, 0, false, false, 1,
                    -60, -45, -32, -32, 32, 32, 12);
                timer.Restart();
                world.Draw(context);
                GL.Finish();
                Require(world.ForestGpuPayloadBytes > 0,
                    "The opening diorama was published without forest geometry.");
                Require(world.ReadyVisibleForestChunks(ForestLod.Near) == world.VisibleChunkCount,
                    "The opening diorama did not retain the camera's detailed tree models in every visible chunk.");
                Require(world.VisibleChunkCount < world.TotalChunkCount,
                    "Scaling test failed to restrict the initial viewport.");
                Require(world.ForestResidentLods < world.TotalChunkCount * 3,
                    "Opening preparation warmed every LOD across the entire map.");
                Require(GL.GetError() == ErrorCode.NoError, "Cold world scaling produced an OpenGL error.");
                Console.WriteLine($"Cold world {nodes - 1}x{nodes - 1}: constructor {constructionMs:F1} ms; first draw {timer.Elapsed.TotalMilliseconds:F1} ms; thread allocations {(GC.GetAllocatedBytesForCurrentThread() - allocated) / 1048576.0:F1} MiB; visible {world.VisibleChunkCount}/{world.TotalChunkCount}, resident forest levels {world.ForestResidentLods}.");
            }
        }

        private static void CheckStaticCacheEviction()
        {
            using var terrain = new Terrain(TerrainSettings.Default.WithNodeSize(33, 42));
            terrain.StreamGeometry = true;
            terrain.StaticCacheBudgetBytes = 1;
            var visible = new RenderContext(0, 0, 0, 0, 0, 0, false, false, 1,
                -60, -45, -10000, -10000, 10000, 10000, 12);
            terrain.UpdateVisibleTiles(visible);
            terrain.DrawTerrainBase(); terrain.DrawTerrainDecals();
            Require(terrain.StaticResidentChunks == terrain.VisibleChunkCount && terrain.StaticGpuPayloadBytes > 0,
                "Static cache budget discarded visible terrain.");
            Require(terrain.StaticBudgetExcessBytes == terrain.StaticCpuPayloadBytes + terrain.StaticGpuPayloadBytes - 1,
                "Static cache omitted its visible working-set excess.");
            var offscreen = new RenderContext(0, 0, 0, 0, 0, 0, false, false, 1,
                -60, -45, 100000, 100000, 100032, 100032, 12);
            terrain.UpdateVisibleTiles(offscreen); terrain.DrawTerrainBase();
            Require(terrain.StaticResidentChunks == 0 && terrain.StaticGpuPayloadBytes == 0 && terrain.StaticCacheEvictions > 0,
                "Static cache retained unused GPU resources under pressure.");
            terrain.UpdateVisibleTiles(visible); terrain.DrawTerrainBase(); terrain.DrawTerrainDecals();
            Require(terrain.StaticTerrainRebuilds == terrain.VisibleChunkCount && terrain.CachedGridHasAllTileBoundaries(),
                "Evicted terrain/grid did not rebuild correctly on return.");
            Require(GL.GetError() == ErrorCode.NoError, "Static cache eviction produced an OpenGL error.");
            Console.WriteLine("Static cache: visible continuity, exact payload excess, offscreen eviction and rebuilt grid passed.");
        }

        private static void CheckGrowingForestLodState()
        {
            using var terrain = new Terrain(TerrainSettings.Default.WithNodeSize(17, 42), (_, _) => 4);
            var forest = new ForestSystem(terrain.Map);
            var graphics = new GraphicsSettings { Enhanced = false };
            Draw(terrain, forest, 12);
            // Build previously missing LODs much later, while physical tree dimensions have grown.
            forest.Update(.5);
            terrain.WarmIndividualForest(forest, graphics);
            Require(terrain.CheckForestLodConsistency(forest, graphics) > 0, "No cached LODs were compared.");
            string before = System.Text.Json.JsonSerializer.Serialize(forest.Capture());
            foreach (float zoom in new[] { 1f, 5f, 12f, 3f, 10f, 1f, 12f })
            {
                Draw(terrain, forest, zoom);
                Require(terrain.CheckForestLodConsistency(forest, graphics) > 0, "Zoom discarded shared LOD state.");
            }
            Require(before == System.Text.Json.JsonSerializer.Serialize(forest.Capture()), "Zoom modified the forest simulation.");
            // Exercise stale fallback and background publication after a real monthly state change.
            forest.Update(10);
            foreach (float zoom in new[] { 1f, 12f, 5f, 1f, 12f }) Draw(terrain, forest, zoom);
            terrain.WarmIndividualForest(forest, graphics);
            Require(terrain.CheckForestLodConsistency(forest, graphics) > 0, "Growth produced inconsistent LOD snapshots.");
            Console.WriteLine("Living forest LOD: shared tree/site snapshots, current GPU clocks and read-only zoom passed.");
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }

        private static int DrawUntilRebuilt(Terrain terrain, ForestSystem forest)
        {
            int total = 0;
            for (int frame = 0; frame < 600; frame++)
            {
                Draw(terrain, forest, 1.5f);
                total += terrain.ForestChunkRebuilds;
                if (total > 0 && !terrain.HasPendingForestBuild) break;
            }
            return total;
        }

        private static void CheckStaticTerrainCache()
        {
            var terrain = new Terrain(TerrainSettings.Default.WithNodeSize(33, 42), (_, _) => 4);
            try
            {
                var context = new RenderContext(0, 0, 0, 0, 0, 0, false, false, 1, -60, -45,
                    -10000, -10000, 10000, 10000, 10);
                terrain.UpdateVisibleTiles(context);
                Check(true);
                Check(false);
                foreach (float zoom in new[] { 1.5f, 5f, 12f })
                {
                    var camera = RenderDevice.ViewProjection;
                    RenderMetrics.BeginFrame();
                    terrain.DrawTerrainDecals(new RenderContext(0,0,0,0,0,0,false,false,1,
                        -60,-45,-10000,-10000,10000,10000,zoom));
                    Require(RenderMetrics.DrawCalls == terrain.VisibleChunkCount, "Grid used more than one pass at this zoom.");
                    Require(RenderDevice.ViewProjection == camera, "Grid changed the camera projection.");
                }
                terrain.Map.BuildRoadTilePath(34, 37);
                Require(terrain.Map.RoadCount > 0, "Road cache test did not build a road.");
                Check(true);
                Check(false);
                terrain.Map.EditElevationAtNode(5 * 33 + 5, 1, 0, 1);
                Check(true);
                Require(terrain.StaticTerrainRebuilds == 1, "Single-node edit rebuilt unrelated terrain chunks.");
                terrain.Map.EditElevationAtNode(10 * 33 + 10, 1, 2, 2);
                Check(true);
                Require(terrain.StaticTerrainRebuilds < terrain.VisibleChunkCount, "Brush rebuilt the whole terrain.");
                terrain.Map.RemoveRoadTilePath(34, 37);
                Check(true);
                Require(GL.GetError() == ErrorCode.NoError, "Static terrain cache GL error.");
                Console.WriteLine("Static terrain cache: stable reuse, road build/removal and elevation invalidation passed.");
                void Check(bool rebuild)
                {
                    terrain.DrawTerrainBase();
                    terrain.DrawTerrainDecals();
                    Require(terrain.CachedGridHasAllTileBoundaries(), "A tile is missing one or more grid boundaries.");
                    Require((terrain.StaticTerrainRebuilds > 0) == rebuild, "Unexpected static terrain rebuild count.");
                    Require(terrain.Map.SurfaceCacheMatchesFreshCalculation(), "Stale terrain surface classification.");
                }
            }
            finally { terrain.Dispose(); }
        }

        private static void CheckTerraformForestStability()
        {
            using var terrain = new Terrain(TerrainSettings.Default.WithNodeSize(33, 42), (_, _) => 4);
            terrain.SynchronousForestBuilds = true;
            var stands = new ForestStand[32 * 32];
            stands[132] = new(ForestSpecies.Oak, 40, .6f, 1);
            stands[792] = new(ForestSpecies.Birch, 40, .6f, 1);
            var forest = new ForestSystem(terrain.Map, stands);
            forest.UseEnvironmentTempo(1200);
            var environment = new EnvironmentSystem(terrain.Map, forest);
            new ForestEnvironmentCoordinator(forest, environment).Update(21.5);
            terrain.Map.TryGetTileCenter(792, out var center);
            var previousCamera = RenderDevice.ViewProjection;
            try
            {
                RenderDevice.SetCamera(Matrix4.CreateTranslation(-center) * Matrix4.CreateRotationX(-1f)
                    * Matrix4.CreateOrthographic(12, 12, -100, 100));
                byte[] before = Capture();
                int coloured = 0;
                for (int i = 0; i < before.Length; i += 4)
                    if (before[i] != 0 || before[i + 1] != 0 || before[i + 2] != 0) coloured++;
                Require(coloured > 20, "Unedited forest fixture was not visible.");
                double year = forest.ForestYear;
                int[] changed = terrain.Map.EditElevationAtNode(5 * 33 + 5, 1, 0, 1);
                forest.ClearTerrainTiles(changed);
                environment.RefreshRouting(changed);
                Require(!forest.TryGetStand(132, out _), "Terraforming left trees on changed ground.");
                Require(forest.ForestYear == year, "Terraforming advanced forest time.");
                Require(SamePixels(before, Capture()), "Local edit changed unedited forest pixels.");
                terrain.Map.Chunks.GetByTile(792).MarkDirty(ChunkDirtyFlags.Props);
                Require(SamePixels(before, Capture()), "Rebuilding the same forest changed its pixels.");
                Console.WriteLine("Terraform forest: unedited pixels stable after local clearing and forced rebuild (1/255 tolerance).");
            }
            finally { RenderDevice.SetCamera(previousCamera); }

            byte[] Capture()
            {
                RenderDevice.SetViewport(256, 256);
                RenderDevice.Clear(new Vector4(0, 0, 0, 1));
                Draw(terrain, forest, 12);
                GL.Finish();
                var pixels = new byte[256 * 256 * 4];
                RenderDevice.ReadPixels(0, 0, 256, 256, pixels);
                return pixels;
            }
            static bool SamePixels(byte[] a, byte[] b)
            {
                for (int i = 0; i < a.Length; i++) if (Math.Abs(a[i] - b[i]) > 1) return false;
                return true;
            }
        }

        private static void CheckDeferredForestBuild()
        {
            using var terrain = new Terrain(TerrainSettings.Default.WithNodeSize(17, 42), (_, _) => 4);
            var forest = new ForestSystem(terrain.Map);
            forest.Clear(); forest.Plant(34, ForestSpecies.Oak);
            forest.IndividualTrees.TryGet(34, out var patch);
            void SetBoundary()
            {
                for (int i = 0; i < patch.Count; i++)
                    patch.Trees[i] = patch.Trees[i].Settle(forest.ForestYear) with
                        { BirthYear = forest.ForestYear - 2.99, AnnualGrowth = default };
                forest.NotifyIndividualVisualEdit();
                Draw(terrain, forest, 12);
                // Remove neighbour-LOD work before measuring the requested stage build.
                // Otherwise an unrelated pending LOD can publish on the next frame.
                terrain.SynchronousForestBuilds = true;
                terrain.WarmIndividualForest(forest, new GraphicsSettings { Enhanced = false });
                terrain.SynchronousForestBuilds = false;
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
            terrain.SynchronousForestBuilds = true;
            terrain.WarmIndividualForest(forest, new GraphicsSettings { Enhanced = false });
            terrain.SynchronousForestBuilds = false;
            Draw(terrain, forest, 12);
            Require(terrain.ForestChunkRebuilds == 0, "Completed forest build did not settle.");
            SetBoundary(); forest.Update(.6); Draw(terrain, forest, 12);
            forest.Clear();
            Require(Draw(terrain, forest, 12) == 0, "Clear retained stale deferred geometry.");
            for (int frame = 0; frame < 5; frame++)
                Require(Draw(terrain, forest, 12) == 0, "Cancelled build reintroduced old trees.");
            Console.WriteLine("Deferred forest geometry: stage publication and cancellation after clear passed.");
        }

        private static void CheckDeadTreeLodState()
        {
            using var terrain = new Terrain(TerrainSettings.Default.WithNodeSize(17, 42), (_, _) => 4);
            var forest = new ForestSystem(terrain.Map);
            forest.Clear(); forest.Plant(34, ForestSpecies.Spruce);
            forest.IndividualTrees.TryGet(34, out var patch);
            var tree = patch.Trees[0] with { Health = 0, AnnualGrowth = default,
                Dimensions = new(.5f, 20, 3), BirthYear = -100 };
            patch.Count = 0;
            patch.DeadTrees = new() { new(tree, -1.99) };
            forest.NotifyIndividualVisualEdit();
            var graphics = new GraphicsSettings { Enhanced = false, Weather = false, Fog = false, Wildlife = false };
            terrain.WarmIndividualForest(forest, graphics);
            using var scene = new TerrainRenderer(terrain,new VehicleSystem(),new WorldEffectSystem(),forest,graphics);
            terrain.Map.TryGetTileCenter(34, out var root);
            var camera = Matrix4.CreateTranslation(-root) * Matrix4.CreateRotationZ(-MathF.PI/4)
                * Matrix4.CreateRotationX(-MathF.PI/4) * Matrix4.CreateOrthographic(18,18,-100,100);
            var standing = Frame(12);
            forest.Update(.6); // Cross death+2 years without a month/topology change.
            var fallen = Frame(12);
            Require(!standing.AsSpan().SequenceEqual(fallen), "Dead spruce did not fall without a mesh rebuild.");
            foreach (float scale in new[] { 1f, 5f, 12f, 1f })
                Require(fallen.AsSpan().SequenceEqual(Frame(scale)), "Cached LOD changed fallen spruce back into a snag.");
            graphics.Enhanced = true;
            var textured = Frame(12);
            foreach (float scale in new[] { 1f, 5f, 12f })
                Require(textured.AsSpan().SequenceEqual(Frame(scale)), "Textured/shadow LOD changed dead-tree fall state.");
            Require(GL.GetError() == ErrorCode.NoError, "Dead tree LOD shader GL error.");
            Console.WriteLine("Dead spruce: exact fallen pixels in all warmed LODs, no topology rebuild at fall threshold.");
            byte[] Frame(float scale)
            {
                RenderDevice.SetCamera(camera);
                GL.Viewport(0,0,256,256);
                GL.Clear(ClearBufferMask.ColorBufferBit|ClearBufferMask.DepthBufferBit);
                var context = new RenderContext(0,0,0,0,0,0,false,false,1,-45,-45,-10000,-10000,10000,10000,scale);
                scene.Draw(context); GL.Finish();
                Require(terrain.ForestChunkRebuilds == 0, "Dead fall state rebuilt LOD geometry.");
                var pixels = new byte[256*256*4];
                GL.ReadPixels(0,0,256,256,PixelFormat.Rgba,PixelType.UnsignedByte,pixels);
                return pixels;
            }
        }

        private static void CheckScreenLineWidth()
        {
            using var line = new VertexBuffer(PrimitiveTopology.Lines);
            line.SetData(new[] { new Vertex(new Vector3(0, -100, 0), Vector3.UnitZ, 0xff000000),
                new Vertex(new Vector3(0, 100, 0), Vector3.UnitZ, 0xff000000) });
            GL.Viewport(0, 0, 256, 256);
            var widths = new double[3];
            int i = 0;
            foreach (float zoom in new[] { .5f, 1f, 4f })
            {
                RenderDevice.SetCamera(Matrix4.CreateScale(zoom));
                GL.ClearColor(1, 1, 1, 1);
                GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);
                using (RenderDevice.CreateStateScope().AlphaBlend().DepthWrite(false))
                {
                    RenderDevice.UseScreenLineShader(1.6f);
                    line.DrawArray(false);
                }
                var pixels = new byte[256 * 4];
                GL.ReadPixels(0, 128, 256, 1, PixelFormat.Rgba, PixelType.UnsignedByte, pixels);
                double width = 0;
                for (int x = 0; x < 256; x++) width += 1 - pixels[x * 4] / 255.0;
                Require(width > 1.4 && width < 2.3, "Grid line coverage is too thin or thick.");
                widths[i++] = width;
            }
            Require(Math.Abs(widths[0] - widths[2]) < .02, "Grid line width changed with zoom.");
            RenderDevice.Clear(new Vector4(1, 1, 1, 1));
            using (RenderDevice.CreateStateScope().AlphaBlend().DepthWrite(false))
            {
                RenderDevice.UseScreenLineShader(1.6f, new Vector4(.27f, .30f, .33f, 115f / 255));
                line.DrawArray(false);
            }
            var winterPixels = new byte[256 * 4];
            GL.ReadPixels(0, 128, 256, 1, PixelFormat.Rgba, PixelType.UnsignedByte, winterPixels);
            Require(winterPixels[127 * 4] < winterPixels[127 * 4 + 1]
                && winterPixels[127 * 4 + 1] < winterPixels[127 * 4 + 2], "Winter grid did not use the cool grey palette.");
            Require(GL.GetError() == ErrorCode.NoError, "Screen line GL error.");
            Console.WriteLine($"Grid pixel coverage: {widths[0]:F2}/{widths[1]:F2}/{widths[2]:F2} at 0.5/1/4 zoom; stable antialiased width passed.");
        }

        private static void CheckLocalRoadEdit()
        {
            var map = new TerrainMap(TerrainSettings.Default.WithNodeSize(65, 42));
            var ecosystem = new Ecosystem(map);
            var forest = ecosystem.Forest;
            int trees = forest.IndividualTreeCount;
            // Compare the old whole-world refresh to the new CPU edit path, excluding scene uploads.
            var timer = System.Diagnostics.Stopwatch.StartNew();
            forest.RefreshHabitat(); ecosystem.Environment.RefreshRouting();
            timer.Stop();
            double globalMs = timer.Elapsed.TotalMilliseconds;
            timer.Restart();
            int[] changed = map.BuildRoadTilePath(20 * 64 + 20, 20 * 64 + 24);
            forest.RefreshHabitat(changed); ecosystem.Environment.RefreshRouting(changed);
            timer.Stop();
            Require(changed.Length > 0 && trees > 0, "Road benchmark needs forest and a valid road segment.");
            ulong revision = forest.Revision;
            Require(map.BuildRoadTilePath(20 * 64 + 20, 20 * 64 + 24).Length == 0, "Repeated road edit was not a no-op.");
            forest.RefreshHabitat(Array.Empty<int>());
            Require(forest.Revision == revision, "Empty road edit refreshed the forest.");
            Console.WriteLine($"Road CPU edit ({trees} trees, {changed.Length} cells): old global refresh {globalMs:F1} ms, local build + refresh {timer.Elapsed.TotalMilliseconds:F1} ms; repeated placement no-op passed.");
        }

        private static void CheckPagedForestUpload()
        {
            foreach (int count in new[] { 0, 3, 16389, 32769 })
            {
                var vertices = new System.Collections.Generic.List<Vertex>();
                var growth = new System.Collections.Generic.List<ForestVertexGrowth>();
                for (int i = 0; i < count; i++)
                {
                    int triangle = i / 3;
                    var origin = new Vector3((triangle % 32) / 16f - 1, (triangle / 32 % 32) / 16f - 1, 0);
                    var corner = i % 3 == 0 ? Vector3.Zero : i % 3 == 1 ? new Vector3(.05f, 0, 0) : new Vector3(0, .05f, 0);
                    vertices.Add(new Vertex(origin + corner, Vector3.UnitZ, 0xff0077aau + (uint)(triangle % 64)));
                    growth.Add(new(origin, new Vector3(.15f, .1f, .05f)));
                }
                using var direct = new VertexBuffer(PrimitiveTopology.Triangles);
                using var paged = new VertexBuffer(PrimitiveTopology.Triangles);
                direct.SetData(vertices.ToArray(), false); direct.SetForestGrowth(growth.ToArray());
                using var work = paged.UploadForestPages(vertices, growth).GetEnumerator();
                int steps = 0;
                while (work.MoveNext())
                {
                    steps++;
                    RenderMetrics.BeginFrame(); paged.DrawArray();
                    Require(RenderMetrics.DrawCalls == 0, "Incomplete upload exposed a partial mesh.");
                }
                if (count > 16384) Require(steps >= 6, "Large mesh upload did not yield between pages.");
                var readback = new Vertex[count];
                paged.ReadVertices(readback);
                Require(System.Runtime.InteropServices.MemoryMarshal.AsBytes(vertices.ToArray().AsSpan()).SequenceEqual(
                    System.Runtime.InteropServices.MemoryMarshal.AsBytes(readback.AsSpan())), "Paged vertex bytes differ from source.");
                foreach (float elapsed in new[] { 0f, .7f, 1.5f })
                {
                    direct.ForestElapsedYears = paged.ForestElapsedYears = elapsed;
                    Require(Frame(direct).AsSpan().SequenceEqual(Frame(paged)), "Paged geometry/growth pixels differ from direct upload.");
                }
            }
            Require(GL.GetError() == ErrorCode.NoError, "Paged forest upload GL error.");
            Console.WriteLine("Paged forest uploads: empty/small/page-tail/multiple-page buffers, exact vertex bytes, growth pixels and no partial draws passed.");
            static byte[] Frame(VertexBuffer buffer)
            {
                RenderDevice.SetCamera(Matrix4.Identity);
                GL.Viewport(0, 0, 256, 256);
                GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);
                buffer.DrawArray(); GL.Finish();
                var pixels = new byte[256 * 256 * 4];
                GL.ReadPixels(0, 0, 256, 256, PixelFormat.Rgba, PixelType.UnsignedByte, pixels);
                return pixels;
            }
        }
    }
}
