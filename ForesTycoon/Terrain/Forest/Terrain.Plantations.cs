using System;
using System.Collections.Generic;
using System.Drawing;
using OpenTK.Mathematics;

namespace ForesTycoon
{
    partial class Terrain
    {
        private sealed class PlantationGeometry : IDisposable
        {
            internal readonly VertexBuffer Fill = new(PrimitiveTopology.Triangles);
            internal readonly VertexBuffer Lines = new(PrimitiveTopology.Lines);
            internal ulong DesignationVersion, TerrainVersion;
            internal bool Ready;
            public void Dispose() { Fill.Dispose(); Lines.Dispose(); }
        }
        private readonly Dictionary<TerrainChunk, PlantationGeometry> plantationGeometry = new();
        private readonly List<Vertex> plantationFill = new(), plantationLines = new();
        internal int PlantationMeshRebuilds { get; private set; }

        internal void DrawPlantations(ForestSystem forest, GraphicsSettings graphics)
        {
            PlantationMeshRebuilds = 0;
            if (!graphics.ShowPlantations) return;
            using var state = RenderDevice.CreateStateScope().AlphaBlend().DepthWrite(false).PolygonOffset(-2, -2).ThinLines();
            foreach (var chunk in visibleChunks)
            {
                if (!plantationGeometry.TryGetValue(chunk, out var mesh))
                    plantationGeometry.Add(chunk, mesh = new());
                if (!mesh.Ready || mesh.DesignationVersion != forest.PlantationRevision || mesh.TerrainVersion != chunk.PropVersion)
                {
                    plantationFill.Clear(); plantationLines.Clear();
                    foreach (int id in chunk.TileIds)
                    {
                        if (!forest.TryGetPlantation(id, out var plot)) continue;
                        Tile tile = tiles[id];
                        if (!roads.Has(id))
                        {
                            // Sample the actual terrain diagonal, including nonplanar slopes.
                            Point(plantationFill, tile, 0, 0, Color.FromArgb(35, 238, 199, 78));
                            Point(plantationFill, tile, 1, 0, Color.FromArgb(35, 238, 199, 78));
                            Point(plantationFill, tile, .5f, .5f, Color.FromArgb(35, 238, 199, 78));
                            Point(plantationFill, tile, 1, 0, Color.FromArgb(35, 238, 199, 78));
                            Point(plantationFill, tile, 1, 1, Color.FromArgb(35, 238, 199, 78));
                            Point(plantationFill, tile, .5f, .5f, Color.FromArgb(35, 238, 199, 78));
                            Point(plantationFill, tile, 1, 1, Color.FromArgb(35, 238, 199, 78));
                            Point(plantationFill, tile, 0, 1, Color.FromArgb(35, 238, 199, 78));
                            Point(plantationFill, tile, .5f, .5f, Color.FromArgb(35, 238, 199, 78));
                            Point(plantationFill, tile, 0, 1, Color.FromArgb(35, 238, 199, 78));
                            Point(plantationFill, tile, 0, 0, Color.FromArgb(35, 238, 199, 78));
                            Point(plantationFill, tile, .5f, .5f, Color.FromArgb(35, 238, 199, 78));
                            for (int row = 0; row < ForestTreeStore.PlantingRows; row++)
                            {
                                float v = (row + .5f) / ForestTreeStore.PlantingRows;
                                float a = Math.Min(v, 1 - v), b = Math.Max(v, 1 - v);
                                Line(tile, 0, v, a, v, Color.FromArgb(100, 115, 79, 37));
                                Line(tile, a, v, b, v, Color.FromArgb(100, 115, 79, 37));
                                Line(tile, b, v, 1, v, Color.FromArgb(100, 115, 79, 37));
                            }
                        }
                        int u = id / settings.TileRows, vTile = id % settings.TileRows;
                        Color edge = Color.FromArgb(240, 255, 211, 87);
                        if (!SameArea(u, vTile - 1, plot.AreaId)) Line(tile, 0, 0, 1, 0, edge);
                        if (!SameArea(u + 1, vTile, plot.AreaId)) Line(tile, 1, 0, 1, 1, edge);
                        if (!SameArea(u, vTile + 1, plot.AreaId)) Line(tile, 1, 1, 0, 1, edge);
                        if (!SameArea(u - 1, vTile, plot.AreaId)) Line(tile, 0, 1, 0, 0, edge);
                    }
                    mesh.Fill.SetData(plantationFill.ToArray(), false); mesh.Lines.SetData(plantationLines.ToArray(), false);
                    mesh.Ready = true; mesh.DesignationVersion = forest.PlantationRevision; mesh.TerrainVersion = chunk.PropVersion;
                    PlantationMeshRebuilds++;
                }
                mesh.Fill.DrawArray(); mesh.Lines.DrawArray();
            }
            bool SameArea(int u, int v, int area) => data.CheckTile(u, v)
                && forest.TryGetPlantation(data.GetTile(u, v).Id, out var plot) && plot.AreaId == area;
            void Line(Tile tile, float u1, float v1, float u2, float v2, Color color)
            { Point(plantationLines, tile, u1, v1, color); Point(plantationLines, tile, u2, v2, color); }
            static void Point(List<Vertex> target, Tile tile, float u, float v, Color color)
            {
                SurfacePoint(tile, u, v, out float x, out float y, out float z);
                uint packed = (uint)color.A << 24 | (uint)color.R | (uint)color.G << 8 | (uint)color.B << 16;
                target.Add(new(new(x, y, z + .025f), Vector3.UnitZ, packed));
            }
        }

        private void DisposePlantations()
        {
            foreach (var geometry in plantationGeometry.Values) geometry.Dispose();
            plantationGeometry.Clear(); plantationFill.Clear(); plantationLines.Clear();
        }
    }
}
