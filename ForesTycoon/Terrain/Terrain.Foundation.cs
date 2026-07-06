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

        private enum TileDiagonalSplit
        {
            None,
            WE,
            NS
        }

        private enum TileCorner
        {
            W,
            S,
            E,
            N
        }

        private readonly struct TileSurfaceVisual
        {
            public TileSurfaceVisual(
                TileRenderMaterial surfaceMaterial,
                TileRenderMaterial roadTriangleMaterial,
                TileRenderMaterial terrainTriangleMaterial,
                RoadEdge splitEdge,
                TileDiagonalSplit split,
                TileRenderMaterial edgeWS,
                TileRenderMaterial edgeSE,
                TileRenderMaterial edgeEN,
                TileRenderMaterial edgeNW)
            {
                SurfaceMaterial = surfaceMaterial;
                RoadTriangleMaterial = roadTriangleMaterial;
                TerrainTriangleMaterial = terrainTriangleMaterial;
                SplitEdge = splitEdge;
                Split = split;
                EdgeWS = edgeWS;
                EdgeSE = edgeSE;
                EdgeEN = edgeEN;
                EdgeNW = edgeNW;
            }

            public TileRenderMaterial SurfaceMaterial { get; }
            public TileRenderMaterial RoadTriangleMaterial { get; }
            public TileRenderMaterial TerrainTriangleMaterial { get; }
            public RoadEdge SplitEdge { get; }
            public TileDiagonalSplit Split { get; }
            public TileRenderMaterial EdgeWS { get; }
            public TileRenderMaterial EdgeSE { get; }
            public TileRenderMaterial EdgeEN { get; }
            public TileRenderMaterial EdgeNW { get; }
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
            // Non-road tiles adjacent to 2+ road edges (e.g. outer road corners) are full Foundation.
            // DrawTerrainBase skips them, so we must draw their surface here.
            ImmediateRenderer.Draw(PrimitiveType.Triangles, () =>
            {
                foreach (Tile tile in tiles)
                {
                    if (roads.Has(tile.Id)) continue;
                    TileSurfaceVisual visual = GetTileSurfaceVisual(tile);
                    if (visual.SurfaceMaterial == TileRenderMaterial.Foundation)
                        DrawFullFoundationTile(tile);
                }
            });

            ImmediateRenderer.Draw(PrimitiveType.Triangles, () =>
            {
                foreach (Tile tile in tiles)
                {
                    TileSurfaceVisual visual = GetTileSurfaceVisual(tile);
                    if (visual.SurfaceMaterial != TileRenderMaterial.MixedFoundation) continue;
                    if (visual.Split == TileDiagonalSplit.None) continue;

                    DrawMixedFoundationTile(tile, visual);
                }
            });
        }

        private void DrawFullFoundationTile(Tile tile)
        {
            if (UseTileDiagonalWE(tile))
            {
                DrawSurfaceTriangle(tile, TileCorner.W, TileCorner.S, TileCorner.E, RoadFoundationSlopeColor);
                DrawSurfaceTriangle(tile, TileCorner.W, TileCorner.E, TileCorner.N, RoadFoundationSlopeColor);
            }
            else
            {
                DrawSurfaceTriangle(tile, TileCorner.N, TileCorner.W, TileCorner.S, RoadFoundationSlopeColor);
                DrawSurfaceTriangle(tile, TileCorner.N, TileCorner.S, TileCorner.E, RoadFoundationSlopeColor);
            }
        }

        private void DrawMixedFoundationTile(Tile tile, TileSurfaceVisual visual)
        {
            if (UseTileDiagonalWE(tile))
            {
                DrawMixedSurfaceTriangle(tile, visual, TileCorner.W, TileCorner.S, TileCorner.E);
                DrawMixedSurfaceTriangle(tile, visual, TileCorner.W, TileCorner.E, TileCorner.N);
            }
            else
            {
                DrawMixedSurfaceTriangle(tile, visual, TileCorner.N, TileCorner.W, TileCorner.S);
                DrawMixedSurfaceTriangle(tile, visual, TileCorner.N, TileCorner.S, TileCorner.E);
            }
        }

        private static Color SurfaceColor(TileRenderMaterial material)
        {
            return material == TileRenderMaterial.Foundation ? RoadFoundationSlopeColor : TerrainTopColor;
        }

        private void DrawMixedSurfaceTriangle(Tile tile, TileSurfaceVisual visual, TileCorner a, TileCorner b, TileCorner c)
        {
            bool terrainSide = TriangleContainsEdge(a, b, c, visual.SplitEdge);
            Color baseColor = SurfaceColor(terrainSide ? visual.TerrainTriangleMaterial : visual.RoadTriangleMaterial);
            DrawSurfaceTriangle(tile, a, b, c, baseColor);
        }

        private static void DrawSurfaceTriangle(Tile tile, TileCorner a, TileCorner b, TileCorner c, Color baseColor)
        {
            Vector3 pa = Corner(tile, a);
            Vector3 pb = Corner(tile, b);
            Vector3 pc = Corner(tile, c);
            GL.Color4(ShadedTileColor(baseColor, pa, pb, pc));
            GL.Vertex3(pa);
            GL.Vertex3(pb);
            GL.Vertex3(pc);
        }

        private bool UseTileDiagonalWE(Tile tile)
        {
            bool useWE = Math.Abs(tile.W.zPos - tile.E.zPos) <= Math.Abs(tile.N.zPos - tile.S.zPos);
            return flippedDiagonalTiles.Contains(tile.Id) ? !useWE : useWE;
        }

        private static Vector3 Corner(Tile tile, TileCorner corner)
        {
            switch (corner)
            {
                case TileCorner.W: return Corner(tile.W);
                case TileCorner.S: return Corner(tile.S);
                case TileCorner.E: return Corner(tile.E);
                case TileCorner.N: return Corner(tile.N);
                default: return Corner(tile.W);
            }
        }

        private static bool TriangleContainsEdge(TileCorner a, TileCorner b, TileCorner c, RoadEdge edge)
        {
            EdgeCorners(edge, out TileCorner x, out TileCorner y);
            return TriangleContainsCorner(a, b, c, x) && TriangleContainsCorner(a, b, c, y);
        }

        private bool MixedTileEdgeHasFoundation(Tile tile, TileSurfaceVisual visual, RoadEdge edge)
        {
            if (UseTileDiagonalWE(tile))
            {
                return TriangleEdgeHasFoundation(visual, TileCorner.W, TileCorner.S, TileCorner.E, edge)
                    || TriangleEdgeHasFoundation(visual, TileCorner.W, TileCorner.E, TileCorner.N, edge);
            }

            return TriangleEdgeHasFoundation(visual, TileCorner.N, TileCorner.W, TileCorner.S, edge)
                || TriangleEdgeHasFoundation(visual, TileCorner.N, TileCorner.S, TileCorner.E, edge);
        }

        private static bool TriangleEdgeHasFoundation(TileSurfaceVisual visual, TileCorner a, TileCorner b, TileCorner c, RoadEdge edge)
        {
            if (!TriangleContainsEdge(a, b, c, edge)) return false;

            bool terrainSide = TriangleContainsEdge(a, b, c, visual.SplitEdge);
            TileRenderMaterial material = terrainSide ? visual.TerrainTriangleMaterial : visual.RoadTriangleMaterial;
            return material == TileRenderMaterial.Foundation;
        }

        private static bool TriangleContainsCorner(TileCorner a, TileCorner b, TileCorner c, TileCorner corner)
        {
            return a == corner || b == corner || c == corner;
        }

        private static void EdgeCorners(RoadEdge edge, out TileCorner a, out TileCorner b)
        {
            switch (edge)
            {
                case RoadEdge.WS:
                    a = TileCorner.W; b = TileCorner.S; return;
                case RoadEdge.SE:
                    a = TileCorner.S; b = TileCorner.E; return;
                case RoadEdge.EN:
                    a = TileCorner.E; b = TileCorner.N; return;
                case RoadEdge.NW:
                    a = TileCorner.N; b = TileCorner.W; return;
                default:
                    a = TileCorner.W; b = TileCorner.W; return;
            }
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
            return GetTileSurfaceVisual(tile).SurfaceMaterial;
        }

        private TileSurfaceVisual GetTileSurfaceVisual(Tile tile)
        {
            int tpc = nodeRows - 1;
            int u = tile.Id / tpc;
            int v = tile.Id % tpc;

            if (roads.Has(tile.Id))
            {
                return new TileSurfaceVisual(
                    TileRenderMaterial.Foundation,
                    TileRenderMaterial.Foundation,
                    TileRenderMaterial.Foundation,
                    RoadEdge.None,
                    TileDiagonalSplit.None,
                    TryGetFoundationFaceForRoadEdge(u, v - 1, tile.W, tile.S, RoadEdge.WS, out _) ? TileRenderMaterial.Foundation : TileRenderMaterial.Grass,
                    TryGetFoundationFaceForRoadEdge(u + 1, v, tile.S, tile.E, RoadEdge.SE, out _) ? TileRenderMaterial.Foundation : TileRenderMaterial.Grass,
                    TryGetFoundationFaceForRoadEdge(u, v + 1, tile.E, tile.N, RoadEdge.EN, out _) ? TileRenderMaterial.Foundation : TileRenderMaterial.Grass,
                    TryGetFoundationFaceForRoadEdge(u - 1, v, tile.N, tile.W, RoadEdge.NW, out _) ? TileRenderMaterial.Foundation : TileRenderMaterial.Grass);
            }

            RoadEdge edges = GetFoundationEdgesFromAdjacentRoads(u, v, tile);
            int edgeCount = CountEdges(edges);

            if (edgeCount == 0)
            {
                return GrassSurfaceVisual(u, v, tile);
            }

            return UniformSurfaceVisual(TileRenderMaterial.Foundation);
        }

        private TileSurfaceVisual GrassSurfaceVisual(int u, int v, Tile tile)
        {
            return new TileSurfaceVisual(
                TileRenderMaterial.Grass,
                TileRenderMaterial.Grass,
                TileRenderMaterial.Grass,
                RoadEdge.None,
                TileDiagonalSplit.None,
                NeighborEdgeMaterial(u, v, tile, u, v - 1, RoadEdge.EN),
                NeighborEdgeMaterial(u, v, tile, u + 1, v, RoadEdge.NW),
                NeighborEdgeMaterial(u, v, tile, u, v + 1, RoadEdge.WS),
                NeighborEdgeMaterial(u, v, tile, u - 1, v, RoadEdge.SE));
        }

        // Returns Foundation if the neighbour edge facing this tile is a foundation edge.
        // For road neighbours an actual height difference (embankment face) must exist.
        // neighborFacingEdge: which RoadEdge of the neighbour tile points toward us.
        private TileRenderMaterial NeighborEdgeMaterial(int u, int v, Tile currentTile, int neighborU, int neighborV, RoadEdge neighborFacingEdge)
        {
            if (!checkTile(neighborU, neighborV)) return TileRenderMaterial.Grass;
            Tile neighbor = getTileByCoords(neighborU, neighborV);
            if (roads.Has(neighbor.Id))
            {
                return HasFoundationFaceTowardRoad(u, v, currentTile, neighbor, neighborFacingEdge)
                    ? TileRenderMaterial.Foundation
                    : TileRenderMaterial.Grass;
            }

            int tpc = nodeRows - 1;
            int nu = neighbor.Id / tpc;
            int nv = neighbor.Id % tpc;
            RoadEdge neighborEdges = GetFoundationEdgesFromAdjacentRoads(nu, nv, neighbor);
            int neighborEdgeCount = CountEdges(neighborEdges);
            if (neighborEdgeCount == 0) return TileRenderMaterial.Grass;
            return TileRenderMaterial.Foundation;
        }

        // Checks whether an embankment face (height difference) exists between this tile
        // and an adjacent road tile.  neighborFacingEdge is the road's edge facing us.
        // Node pairs mirror GetFoundationEdgesFromAdjacentRoads / DrawFoundationFacesForRoadTile.
        private bool HasFoundationFaceTowardRoad(int u, int v, Tile tile, Tile road, RoadEdge neighborFacingEdge)
        {
            return neighborFacingEdge switch
            {
                RoadEdge.EN => TryGetFoundationFace(u, v, road.E, road.N, tile.E, tile.N, out _),
                RoadEdge.NW => TryGetFoundationFace(u, v, road.N, road.W, tile.N, tile.W, out _),
                RoadEdge.WS => TryGetFoundationFace(u, v, road.W, road.S, tile.W, tile.S, out _),
                RoadEdge.SE => TryGetFoundationFace(u, v, road.S, road.E, tile.S, tile.E, out _),
                _           => false,
            };
        }

        private static TileSurfaceVisual UniformSurfaceVisual(TileRenderMaterial material)
        {
            return new TileSurfaceVisual(
                material,
                material,
                material,
                RoadEdge.None,
                TileDiagonalSplit.None,
                material,
                material,
                material,
                material);
        }

        private static TileDiagonalSplit SplitForEdge(RoadEdge edge)
        {
            return edge switch
            {
                RoadEdge.WS => TileDiagonalSplit.WE,
                RoadEdge.SE => TileDiagonalSplit.NS,
                RoadEdge.EN => TileDiagonalSplit.WE,
                RoadEdge.NW => TileDiagonalSplit.NS,
                _ => TileDiagonalSplit.None
            };
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
