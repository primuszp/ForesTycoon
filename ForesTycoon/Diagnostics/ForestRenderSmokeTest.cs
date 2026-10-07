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
                Draw(terrain, forest, 1.5f);
                Require(terrain.ForestChunkRebuilds > 0, "Harvest did not invalidate geometry.");
                Require(terrain.ForestChunkRebuilds < terrain.VisibleChunkCount, "Harvest rebuilt unrelated chunks.");
                forest.Plant(tileId, ForestSpecies.Oak);
                Draw(terrain, forest, 1.5f);
                Require(terrain.ForestChunkRebuilds > 0, "Planting did not invalidate geometry.");
                var editTimer = System.Diagnostics.Stopwatch.StartNew();
                terrain.Map.EditElevationAtNode(4 * 33 + 4, 1, 0, 1);
                Draw(terrain, forest, 1.5f);
                editTimer.Stop();
                Console.WriteLine($"Local terrain edit + forest refresh: {editTimer.Elapsed.TotalMilliseconds:F1} ms, {terrain.ForestChunkRebuilds}/{terrain.VisibleChunkCount} forest chunks rebuilt.");
                Require(terrain.ForestChunkRebuilds > 0 && terrain.ForestChunkRebuilds < terrain.VisibleChunkCount,
                    "Local terrain edit rebuilt unrelated forest chunks.");
                Draw(terrain, forest, 1.5f);
                Require(terrain.ForestChunkRebuilds == 0, "Edited forest cache did not settle.");
                // A central terrain edit must invalidate positions even without a forest revision.
                terrain.Map.EditElevationAtNode(16 * 33 + 16, 1, 0, 1);
                Draw(terrain, forest, 1.5f);
                Require(terrain.ForestChunkRebuilds > 0, "Terrain edit did not invalidate geometry.");
                forest.Clear();
                Require(Draw(terrain, forest, 1.5f) == 0, "Cleared forest left stale GPU geometry.");
                Require(GL.GetError() == ErrorCode.NoError, "OpenGL reported an error.");
                CheckStaticTerrainCache();
                CheckDeferredForestBuild();
                CheckPagedForestUpload();
                CheckDeadTreeLodState();
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
                terrain.Map.EditElevationAtNode(3 * 17 + 3, 1, 0, 1);
                Check(true);
                int uploads = terrain.TerrainEdgeUploads;
                terrain.Map.EditElevationAtNode(10 * 17 + 10, 1, 2, 2);
                Require(terrain.TerrainEdgeUploads == uploads + 1, "Brush uploaded the entire terrain grid more than once.");
                Check(true);
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
                using var direct = new VertexBuffer(PrimitiveType.Triangles);
                using var paged = new VertexBuffer(PrimitiveType.Triangles);
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
                GL.BindBuffer(BufferTarget.ArrayBuffer, paged.VboId);
                if (count > 0) GL.GetBufferSubData(BufferTarget.ArrayBuffer, IntPtr.Zero, count * Vertex.Stride, readback);
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
