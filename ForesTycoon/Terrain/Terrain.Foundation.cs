using System;
using System.Drawing;
using OpenTK.Graphics.OpenGL;
using OpenTK.Mathematics;

namespace ForesTycoon
{
    partial class Terrain
    {
        private enum TileRenderMaterial
        {
            Grass,
            Foundation,
            MixedFoundation
        }

        private readonly struct FoundationTileInfo
        {
            public FoundationTileInfo(TileRenderMaterial material, RoadEdge edge, bool splitGrid)
            {
                Material = material;
                Edge = edge;
                SplitGrid = splitGrid;
            }

            public TileRenderMaterial Material { get; }
            public RoadEdge Edge { get; }
            public bool SplitGrid { get; }
        }

        private readonly struct FoundationFaceData
        {
            public FoundationFaceData(int tileId, Vector3 topA, Vector3 topB, Vector3 bottomB, Vector3 bottomA)
            {
                TileId = tileId;
                TopA = topA;
                TopB = topB;
                BottomB = bottomB;
                BottomA = bottomA;
            }

            public int TileId { get; }
            public Vector3 TopA { get; }
            public Vector3 TopB { get; }
            public Vector3 BottomB { get; }
            public Vector3 BottomA { get; }
        }

        private void DrawRoadFoundations()
        {
            if (roads.Count == 0) return;
            int tpc = nodeRows - 1;

            using (new RenderStateScope().PolygonOffset(-1.0f, -1.0f))
            {
                ImmediateRenderer.Draw(PrimitiveType.Quads, () =>
                {
                    foreach (int id in roads.Tiles)
                    {
                        Tile t = tiles[id];
                        int u = id / tpc, v = id % tpc;
                        RoadFootprintCorners(t, out Vector3 iW, out Vector3 iS, out Vector3 iE, out Vector3 iN);

                        GL.Color4(RoadFoundationColor);
                        GL.Vertex3(iW); GL.Vertex3(iS); GL.Vertex3(iE); GL.Vertex3(iN);

                        DrawFoundationFacesForRoadTile(u, v, t);
                    }
                });

                DrawMixedFoundationTileSurfaces();
            }

            using (new RenderStateScope().DepthWrite(false))
            {
                GL.Color4(RoadFoundationLineColor);
                foreach (int id in roads.Tiles)
                {
                    Tile t = tiles[id];
                    int u = id / tpc, v = id % tpc;
                    RoadFootprintCorners(t, out Vector3 iW, out Vector3 iS, out Vector3 iE, out Vector3 iN);

                    ImmediateRenderer.Draw(PrimitiveType.LineLoop, () =>
                    {
                        GL.Vertex3(iW); GL.Vertex3(iS); GL.Vertex3(iE); GL.Vertex3(iN);
                    });
                }
            }
        }

        private void DrawMixedFoundationTileSurfaces()
        {
            ImmediateRenderer.Draw(PrimitiveType.Triangles, () =>
            {
                foreach (Tile tile in tiles)
                {
                    FoundationTileInfo info = GetFoundationTileInfo(tile);
                    if (info.Material != TileRenderMaterial.MixedFoundation) continue;

                    DrawMixedFoundationTile(tile, info.Edge);
                }
            });
        }

        private void DrawMixedFoundationTile(Tile tile, RoadEdge edge)
        {
            DrawMixedTriangle(tile, edge, true, RoadFoundationSlopeColor);
            DrawMixedTriangle(tile, edge, false, TerrainTopColor);
        }

        private static void DrawMixedTriangle(Tile tile, RoadEdge edge, bool roadSide, Color baseColor)
        {
            MixedTrianglePoints(tile, edge, roadSide, out Vector3 a, out Vector3 b, out Vector3 c);
            GL.Color4(ShadedTileColor(baseColor, a, b, c));
            GL.Vertex3(a);
            GL.Vertex3(b);
            GL.Vertex3(c);
        }

        private static void MixedTrianglePoints(Tile tile, RoadEdge edge, bool roadSide, out Vector3 a, out Vector3 b, out Vector3 c)
        {
            if (roadSide)
            {
                switch (edge)
                {
                    case RoadEdge.WS:
                        a = Corner(tile.W); b = Corner(tile.S); c = Corner(tile.E); return;
                    case RoadEdge.SE:
                        a = Corner(tile.S); b = Corner(tile.E); c = Corner(tile.N); return;
                    case RoadEdge.EN:
                        a = Corner(tile.E); b = Corner(tile.N); c = Corner(tile.W); return;
                    case RoadEdge.NW:
                        a = Corner(tile.N); b = Corner(tile.W); c = Corner(tile.S); return;
                }
            }
            else
            {
                switch (edge)
                {
                    case RoadEdge.WS:
                        a = Corner(tile.W); b = Corner(tile.E); c = Corner(tile.N); return;
                    case RoadEdge.SE:
                        a = Corner(tile.S); b = Corner(tile.N); c = Corner(tile.W); return;
                    case RoadEdge.EN:
                        a = Corner(tile.E); b = Corner(tile.W); c = Corner(tile.S); return;
                    case RoadEdge.NW:
                        a = Corner(tile.N); b = Corner(tile.S); c = Corner(tile.E); return;
                }
            }

            a = b = c = Corner(tile.W);
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

        private bool TryGetFoundationFaceForRoadEdge(int adjacentU, int adjacentV, Node sharedA, Node sharedB, RoadEdge edge, out FoundationFaceData face)
        {
            face = default;
            if (!checkTile(adjacentU, adjacentV)) return false;
            Tile adjacent = getTileByCoords(adjacentU, adjacentV);
            return edge switch
            {
                RoadEdge.WS => TryGetFoundationFace(adjacentU, adjacentV, sharedA, sharedB, adjacent.W, adjacent.S, out face),
                RoadEdge.SE => TryGetFoundationFace(adjacentU, adjacentV, sharedA, sharedB, adjacent.S, adjacent.E, out face),
                RoadEdge.EN => TryGetFoundationFace(adjacentU, adjacentV, sharedA, sharedB, adjacent.E, adjacent.N, out face),
                RoadEdge.NW => TryGetFoundationFace(adjacentU, adjacentV, sharedA, sharedB, adjacent.N, adjacent.W, out face),
                _ => false
            };
        }

        private void DrawFoundationFace(FoundationFaceData face)
        {
            GL.Color4(FoundationFaceColor(face.BottomA, face.BottomB, face.TopB));
            GL.Vertex3(face.BottomA); GL.Vertex3(face.BottomB); GL.Vertex3(face.TopB); GL.Vertex3(face.TopA);
        }

        private bool TryGetFoundationFace(int nu, int nv, Node sharedA, Node sharedB, Node outerA, Node outerB,
            out FoundationFaceData face)
        {
            face = default;
            if (!checkTile(nu, nv)) return false;
            Tile tile = getTileByCoords(nu, nv);
            if (roads.Has(tile.Id)) return false;

            float shAT = RoadSurfaceZ(sharedA);
            float shBT = RoadSurfaceZ(sharedB);
            float outAT = outerA.W * tileSizeM;
            float outBT = outerB.W * tileSizeM;

            if (Math.Abs(shAT - outAT) <= 0.001f && Math.Abs(shBT - outBT) <= 0.001f)
                return false;

            face = new FoundationFaceData(
                tile.Id,
                new Vector3(sharedA.xPos, sharedA.yPos, shAT),
                new Vector3(sharedB.xPos, sharedB.yPos, shBT),
                new Vector3(outerB.xPos, outerB.yPos, outBT),
                new Vector3(outerA.xPos, outerA.yPos, outAT));
            return true;
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

        private TileRenderMaterial GetTileRenderMaterial(Tile tile)
        {
            return GetFoundationTileInfo(tile).Material;
        }

        private FoundationTileInfo GetFoundationTileInfo(Tile tile)
        {
            if (roads.Has(tile.Id)) return new FoundationTileInfo(TileRenderMaterial.Foundation, RoadEdge.None, false);

            int tpc = nodeRows - 1;
            int u = tile.Id / tpc;
            int v = tile.Id % tpc;

            RoadEdge edges = GetFoundationEdgesFromAdjacentRoads(u, v, tile);
            int edgeCount = CountEdges(edges);

            if (edgeCount == 0)
            {
                return new FoundationTileInfo(TileRenderMaterial.Grass, RoadEdge.None, false);
            }

            if (IsCoplanarTile(tile))
                return new FoundationTileInfo(TileRenderMaterial.Grass, RoadEdge.None, false);

            if (edgeCount == 1)
            {
                return new FoundationTileInfo(TileRenderMaterial.MixedFoundation, edges, true);
            }
            return new FoundationTileInfo(TileRenderMaterial.Foundation, edges, false);
        }

        private static bool IsCoplanarTile(Tile tile)
        {
            Vector3 w = Corner(tile.W);
            Vector3 s = Corner(tile.S);
            Vector3 e = Corner(tile.E);
            Vector3 n = Corner(tile.N);
            Vector3 normal = Vector3.Cross(s - w, e - w);
            if (normal.LengthSquared <= 0.0001f) return true;
            float distance = Math.Abs(Vector3.Dot(Vector3.Normalize(normal), n - w));
            return distance <= 0.01f;
        }

        private RoadEdge GetFoundationEdgesFromAdjacentRoads(int u, int v, Tile tile)
        {
            RoadEdge edges = RoadEdge.None;

            if (TryGetRoadTile(u, v + 1, out Tile roadNorth)
                && TryGetFoundationFace(u, v, roadNorth.W, roadNorth.S, tile.W, tile.S, out _))
                edges |= RoadEdge.WS;

            if (TryGetRoadTile(u - 1, v, out Tile roadWest)
                && TryGetFoundationFace(u, v, roadWest.S, roadWest.E, tile.S, tile.E, out _))
                edges |= RoadEdge.SE;

            if (TryGetRoadTile(u, v - 1, out Tile roadSouth)
                && TryGetFoundationFace(u, v, roadSouth.E, roadSouth.N, tile.E, tile.N, out _))
                edges |= RoadEdge.EN;

            if (TryGetRoadTile(u + 1, v, out Tile roadEast)
                && TryGetFoundationFace(u, v, roadEast.N, roadEast.W, tile.N, tile.W, out _))
                edges |= RoadEdge.NW;

            return edges;
        }

        private bool TryGetRoadTile(int u, int v, out Tile roadTile)
        {
            roadTile = null;
            if (!checkTile(u, v)) return false;
            roadTile = getTileByCoords(u, v);
            return roads.Has(roadTile.Id);
        }
    }
}
