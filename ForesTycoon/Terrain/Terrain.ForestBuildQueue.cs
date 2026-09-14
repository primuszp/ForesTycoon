using System.Collections.Generic;
using System.Diagnostics;
using OpenTK.Mathematics;

namespace ForesTycoon
{
    partial class Terrain
    {
        private readonly Queue<(TerrainChunk Chunk, ForestChunkGeometry Geometry, ForestLod Lod)> forestBuildQueue = new();
        private sealed class PendingForestBuild
        {
            internal TerrainChunk Chunk;
            internal ForestChunkGeometry Geometry;
            internal ForestLod Lod;
            internal TreeInstance[] Trees;
            internal int Next, NextStand;
            internal (Tile Tile, int Offset, int Count)[] Stands;
            internal ulong Revision, TerrainVersion;
            internal readonly List<Vertex> Crowns = new(), Wood = new(), Floor = new(), Understory = new();
        }
        private PendingForestBuild pendingForestBuild;
        internal int PendingForestBuildCount => forestBuildQueue.Count + (pendingForestBuild == null ? 0 : 1);

        private void ProcessForestBuildQueue(ForestSystem forest)
        {
            long start = Stopwatch.GetTimestamp();
            // Cooperative CPU budget. One stem and the final GPU upload are indivisible;
            // old geometry remains valid until the complete replacement is ready.
            while (Stopwatch.GetElapsedTime(start).TotalMilliseconds < 2)
            {
                if (pendingForestBuild == null)
                {
                    if (!forestBuildQueue.TryDequeue(out var request)) return;
                    if (!request.Geometry.Dirty) { request.Geometry.Queued = false; continue; }
                    request.Geometry.State.Refresh(forest, request.Chunk.TileIds);
                    PrepareForestStems(request.Chunk, request.Geometry, request.Lod);
                    pendingForestBuild = new PendingForestBuild { Chunk = request.Chunk, Geometry = request.Geometry,
                        Lod = request.Lod, Trees = chunkStems.ToArray(), Stands = chunkStands.ToArray(), Revision = forest.Revision, TerrainVersion = forestTerrainVersion };
                }
                var job = pendingForestBuild;
                if (job.Revision != forest.Revision || job.TerrainVersion != forestTerrainVersion || !job.Geometry.Dirty)
                {
                    job.Geometry.Queued = false;
                    pendingForestBuild = null;
                    continue;
                }
                if (job.Next < job.Trees.Length)
                {
                    TreeInstance tree = job.Trees[job.Next];
                    if (job.NextStand < job.Stands.Length && job.Stands[job.NextStand].Offset == job.Next)
                    {
                        var stand = job.Stands[job.NextStand++];
                        if (job.Lod == ForestLod.Near)
                            job.Understory.AddRange(DynamicPrimitiveBatch.BuildGeometry(OpenTK.Graphics.OpenGL.PrimitiveType.Triangles,
                                () => DrawUndergrowth(stand.Tile, tree, stand.Count)));
                    }
                    job.Next++;
                    if (job.Lod != ForestLod.Far)
                        job.Wood.AddRange(DynamicPrimitiveBatch.BuildGeometry(OpenTK.Graphics.OpenGL.PrimitiveType.Quads, () => DrawTreeWood(tree)));
                    job.Floor.AddRange(DynamicPrimitiveBatch.BuildGeometry(OpenTK.Graphics.OpenGL.PrimitiveType.Triangles, () => DrawForestFloor(tree)));
                    TreeModel model = TreeModel.For(tree.Stand.Species);
                    float height = model.CrownHeight * tree.Scale * tree.CrownRise;
                    ForestCrownMesh.Append(job.Crowns, tree.Stand.Species,
                        new Vector3(tree.X, tree.Y, tree.TrunkTop(model) - height * model.CrownDrop),
                        model.CrownRadius * tree.Scale * tree.CrownWidth, height, tree.Yaw, tree.Seed,
                        Weather(Tinted(model.CrownColor, tree.Tint), tree.Stand.Health), job.Lod);
                    continue;
                }
                job.Geometry.Crowns.SetData(job.Crowns.ToArray(), false);
                job.Geometry.Wood.SetData(job.Wood.ToArray(), false);
                job.Geometry.Floor.SetData(job.Floor.ToArray(), false);
                job.Geometry.Understory.SetData(job.Understory.ToArray(), false);
                job.Geometry.Dirty = job.Geometry.Queued = false;
                ForestChunkRebuilds++;
                pendingForestBuild = null;
                // At most one upload group per frame, even when CPU time is still available.
                return;
            }
        }
    }
}
