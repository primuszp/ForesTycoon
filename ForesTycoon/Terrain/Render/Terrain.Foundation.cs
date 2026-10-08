using System;
using System.Drawing;
using OpenTK.Mathematics;

namespace ForesTycoon
{
    /// <summary>Paints the road foundations (embankments and cuttings) classified by <see cref="TerrainMap"/>.</summary>
    partial class Terrain
    {
        internal void DrawRoadFoundations()
        {
            if (roads.Count == 0) return;
            int tpc = nodeRows - 1;

            using (RenderDevice.CreateStateScope().PolygonOffset(-1.0f, -1.0f))
            {
                DynamicPrimitiveBatch.Draw(PrimitiveTopology.Quads, () =>
                {
                    foreach (Tile t in visibleTiles)
                    {
                        if (!roads.Has(t.Id)) continue;
                        int u = t.Id / tpc, v = t.Id % tpc;
                        RoadFootprintCorners(t, out Vector3 iW, out Vector3 iS, out Vector3 iE, out Vector3 iN);

                        DynamicPrimitiveBatch.Color4(RoadFoundationColor);
                        DynamicPrimitiveBatch.Vertex3(iW); DynamicPrimitiveBatch.Vertex3(iS); DynamicPrimitiveBatch.Vertex3(iE); DynamicPrimitiveBatch.Vertex3(iN);

                        DrawFoundationFacesForRoadTile(u, v, t);
                    }
                });
            }

            using (RenderDevice.CreateStateScope().DepthWrite(false))
            {
                DynamicPrimitiveBatch.Color4(RoadFoundationLineColor);
                DynamicPrimitiveBatch.Draw(PrimitiveTopology.Lines, () =>
                {
                    foreach (Tile tile in visibleTiles)
                    {
                        if (!roads.Has(tile.Id)) continue;
                        RoadFootprintCorners(tile, out Vector3 iW, out Vector3 iS, out Vector3 iE, out Vector3 iN);
                        DynamicPrimitiveBatch.Vertex3(iW); DynamicPrimitiveBatch.Vertex3(iS); DynamicPrimitiveBatch.Vertex3(iE); DynamicPrimitiveBatch.Vertex3(iN);
                        DynamicPrimitiveBatch.Vertex3(iN); DynamicPrimitiveBatch.Vertex3(iW);
                    }
                });
            }
        }

        private void DrawFoundationTerrainSurfaces()
        {
            if (roads.Count == 0) return;
            DynamicPrimitiveBatch.Draw(PrimitiveTopology.Quads, () =>
            {
                foreach (Tile tile in visibleTiles)
                {
                    if (roads.Has(tile.Id)) continue;
                    TileSurface visual = GetTileSurface(tile);
                    if (visual.SurfaceMaterial != TileSurfaceMaterial.Foundation) continue;

                    if (tile.Shape.IsPlanar)
                        DrawFoundationTileQuad(tile, visual);
                }
            });

            DynamicPrimitiveBatch.Draw(PrimitiveTopology.Triangles, () =>
            {
                foreach (Tile tile in visibleTiles)
                {
                    if (roads.Has(tile.Id)) continue;
                    TileSurface visual = GetTileSurface(tile);
                    if (visual.SurfaceMaterial == TileSurfaceMaterial.Grass) continue;
                    if (visual.SurfaceMaterial == TileSurfaceMaterial.Foundation && tile.Shape.IsPlanar) continue;

                    DrawMixedFoundationTileSurface(tile, visual);
                }
            });
        }

        private static void DrawFoundationTileQuad(Tile tile, TileSurface visual)
        {
            Color baseColor = SurfaceColor(tile, visual.FirstTriangleMaterial);
            Vector3 w = Corner(tile, TileCorner.W);
            Vector3 s = Corner(tile, TileCorner.S);
            Vector3 e = Corner(tile, TileCorner.E);
            Vector3 n = Corner(tile, TileCorner.N);
            DynamicPrimitiveBatch.Color4(ShadedTileColor(baseColor, w, s, e));
            DynamicPrimitiveBatch.Vertex3(w);
            DynamicPrimitiveBatch.Vertex3(s);
            DynamicPrimitiveBatch.Vertex3(e);
            DynamicPrimitiveBatch.Vertex3(n);
        }

        private void DrawMixedFoundationTileSurface(Tile tile, TileSurface visual)
        {
            if (UseTileDiagonalWE(tile, visual))
            {
                DrawSurfaceTriangle(tile, TileCorner.W, TileCorner.S, TileCorner.E, SurfaceColor(tile, visual.FirstTriangleMaterial));
                DrawSurfaceTriangle(tile, TileCorner.W, TileCorner.E, TileCorner.N, SurfaceColor(tile, visual.SecondTriangleMaterial));
            }
            else
            {
                DrawSurfaceTriangle(tile, TileCorner.N, TileCorner.W, TileCorner.S, SurfaceColor(tile, visual.FirstTriangleMaterial));
                DrawSurfaceTriangle(tile, TileCorner.N, TileCorner.S, TileCorner.E, SurfaceColor(tile, visual.SecondTriangleMaterial));
            }
        }

        private static Color SurfaceColor(Tile tile, TileSurfaceMaterial material)
        {
            return material == TileSurfaceMaterial.Foundation
                ? RoadFoundationSlopeColor
                : TerrainSurfaceColor(tile.Code, tile.Low);
        }

        private static void DrawSurfaceTriangle(Tile tile, TileCorner a, TileCorner b, TileCorner c, Color baseColor)
        {
            Vector3 pa = Corner(tile, a);
            Vector3 pb = Corner(tile, b);
            Vector3 pc = Corner(tile, c);
            DynamicPrimitiveBatch.Color4(ShadedTileColor(baseColor, pa, pb, pc));
            DynamicPrimitiveBatch.Vertex3(pa);
            DynamicPrimitiveBatch.Vertex3(pb);
            DynamicPrimitiveBatch.Vertex3(pc);
        }

        private static Color ShadedTileColor(Color baseColor, Vector3 a, Vector3 b, Vector3 c)
        {
            Vector3 cross = Vector3.Cross(b - a, c - a);
            Vector3 normal = cross.LengthSquared > 0.0001f ? Vector3.Normalize(cross) : Vector3.UnitZ;
            Vector3 light = Vector3.Normalize(new Vector3(0.4f, 0.6f, 1.5f));
            float shade = Math.Max(0.55f, Math.Min(1.0f, Math.Abs(Vector3.Dot(normal, light))));
            if (float.IsNaN(shade)) shade = 1.0f;
            return Color.FromArgb(baseColor.A,
                (int)(baseColor.R * shade),
                (int)(baseColor.G * shade),
                (int)(baseColor.B * shade));
        }

        private void DrawFoundationFacesForRoadTile(int u, int v, Tile roadTile)
        {
            if (TryGetFoundationFaceForRoadEdge(u, v - 1, roadTile.W, roadTile.S, RoadEdge.WS, out FoundationFaceData ws))
            {
                DrawFoundationFace(ws);
            }

            if (TryGetFoundationFaceForRoadEdge(u + 1, v, roadTile.S, roadTile.E, RoadEdge.SE, out FoundationFaceData se))
            {
                DrawFoundationFace(se);
            }

            if (TryGetFoundationFaceForRoadEdge(u, v + 1, roadTile.E, roadTile.N, RoadEdge.EN, out FoundationFaceData en))
            {
                DrawFoundationFace(en);
            }

            if (TryGetFoundationFaceForRoadEdge(u - 1, v, roadTile.N, roadTile.W, RoadEdge.NW, out FoundationFaceData nw))
            {
                DrawFoundationFace(nw);
            }
        }

        private void DrawFoundationFace(FoundationFaceData face)
        {
            DynamicPrimitiveBatch.Color4(FoundationFaceColor(face.BottomA, face.BottomB, face.TopB));
            DynamicPrimitiveBatch.Vertex3(face.BottomA); DynamicPrimitiveBatch.Vertex3(face.BottomB); DynamicPrimitiveBatch.Vertex3(face.TopB); DynamicPrimitiveBatch.Vertex3(face.TopA);
        }

        private static Color FoundationFaceColor(Vector3 p0, Vector3 p1, Vector3 p2)
        {
            Vector3 cross = Vector3.Cross(p1 - p0, p2 - p0);
            Vector3 normal = cross.LengthSquared > 0.0001f ? Vector3.Normalize(cross) : Vector3.UnitZ;
            Vector3 light = Vector3.Normalize(new Vector3(0.4f, 0.6f, 1.5f));
            float shade = Math.Max(0.55f, Math.Min(1.0f, Math.Abs(Vector3.Dot(normal, light))));
            if (float.IsNaN(shade)) shade = 1.0f;
            return Color.FromArgb(255, (int)(RoadFoundationSlopeColor.R * shade),
                (int)(RoadFoundationSlopeColor.G * shade), (int)(RoadFoundationSlopeColor.B * shade));
        }

    }
}
