using System.Collections.Generic;
using System.Diagnostics;

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
            internal (TreeInstance Stem, float Decay)[] Stumps;
            internal int Next, NextStand, NextStump;
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
                        Lod = request.Lod, Trees = chunkStems.ToArray(), Stumps = chunkStumps.ToArray(), Stands = chunkStands.ToArray(), Revision = forest.Revision, TerrainVersion = forestTerrainVersion };
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
                    AppendTreeCrown(job.Crowns, tree, job.Lod);
                    continue;
                }
                if (job.NextStump < job.Stumps.Length && job.Lod != ForestLod.Far)
                {
                    var stump = job.Stumps[job.NextStump++];
                    job.Wood.AddRange(DynamicPrimitiveBatch.BuildGeometry(OpenTK.Graphics.OpenGL.PrimitiveType.Quads,
                        () => DrawStump(stump.Stem, stump.Decay)));
                    // A clear-cut chunk must obey the same cooperative budget as living trees.
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
