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

        private enum TileCorner
        {
            W,
            S,
            E,
            N
        }

        private enum TileDiagonalDirection
        {
            Natural,
            WE,
            NS
        }

        private readonly struct TileSurfaceVisual
        {
            public TileSurfaceVisual(
                TileRenderMaterial surfaceMaterial,
                TileRenderMaterial firstTriangleMaterial,
                TileRenderMaterial secondTriangleMaterial,
                TileDiagonalDirection splitDirection,
                bool drawFoundationDiagonal,
                TileRenderMaterial edgeWS,
                TileRenderMaterial edgeSE,
                TileRenderMaterial edgeEN,
                TileRenderMaterial edgeNW)
            {
                SurfaceMaterial = surfaceMaterial;
                FirstTriangleMaterial = firstTriangleMaterial;
                SecondTriangleMaterial = secondTriangleMaterial;
                SplitDirection = splitDirection;
                DrawFoundationDiagonal = drawFoundationDiagonal;
                EdgeWS = edgeWS;
                EdgeSE = edgeSE;
                EdgeEN = edgeEN;
                EdgeNW = edgeNW;
            }

            public TileRenderMaterial SurfaceMaterial { get; }
            public TileRenderMaterial FirstTriangleMaterial { get; }
            public TileRenderMaterial SecondTriangleMaterial { get; }
            public TileDiagonalDirection SplitDirection { get; }
            public bool DrawFoundationDiagonal { get; }
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

        internal void DrawRoadFoundations()
        {
            if (roads.Count == 0) return;
            int tpc = nodeRows - 1;

            using (new RenderStateScope().PolygonOffset(-1.0f, -1.0f))
            {
                ImmediateRenderer.Draw(PrimitiveType.Quads, () =>
                {
                    foreach (int id in roads.Tiles)
                    {
                        if (!IsTileVisible(id)) continue;
                        Tile t = tiles[id];
                        int u = id / tpc, v = id % tpc;
                        RoadFootprintCorners(t, out Vector3 iW, out Vector3 iS, out Vector3 iE, out Vector3 iN);

                        GL.Color4(RoadFoundationColor);
                        GL.Vertex3(iW); GL.Vertex3(iS); GL.Vertex3(iE); GL.Vertex3(iN);

                        DrawFoundationFacesForRoadTile(u, v, t);
                    }
                });
            }

            using (new RenderStateScope().DepthWrite(false))
            {
                GL.Color4(RoadFoundationLineColor);
                foreach (int id in roads.Tiles)
                {
                    if (!IsTileVisible(id)) continue;
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

        private void DrawFoundationTerrainSurfaces()
        {
            ImmediateRenderer.Draw(PrimitiveType.Quads, () =>
            {
                foreach (Tile tile in visibleTiles)
                {
                    if (roads.Has(tile.Id)) continue;
                    TileSurfaceVisual visual = GetTileSurfaceVisual(tile);
                    if (visual.SurfaceMaterial != TileRenderMaterial.Foundation) continue;

                    if (tile.Shape.IsPlanar)
                        DrawFoundationTileQuad(tile, visual);
                }
            });

            ImmediateRenderer.Draw(PrimitiveType.Triangles, () =>
            {
                foreach (Tile tile in visibleTiles)
                {
                    if (roads.Has(tile.Id)) continue;
                    TileSurfaceVisual visual = GetTileSurfaceVisual(tile);
                    if (visual.SurfaceMaterial == TileRenderMaterial.Grass) continue;
                    if (visual.SurfaceMaterial == TileRenderMaterial.Foundation && tile.Shape.IsPlanar) continue;

                    DrawMixedFoundationTileSurface(tile, visual);
                }
            });
        }

        private static void DrawFoundationTileQuad(Tile tile, TileSurfaceVisual visual)
        {
            Color baseColor = SurfaceColor(tile, visual.FirstTriangleMaterial);
            Vector3 w = Corner(tile, TileCorner.W);
            Vector3 s = Corner(tile, TileCorner.S);
            Vector3 e = Corner(tile, TileCorner.E);
            Vector3 n = Corner(tile, TileCorner.N);
            GL.Color4(ShadedTileColor(baseColor, w, s, e));
            GL.Vertex3(w);
            GL.Vertex3(s);
            GL.Vertex3(e);
            GL.Vertex3(n);
        }

        private void DrawMixedFoundationTileSurface(Tile tile, TileSurfaceVisual visual)
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

        private static Color SurfaceColor(Tile tile, TileRenderMaterial material)
        {
            return material == TileRenderMaterial.Foundation
                ? RoadFoundationSlopeColor
                : TerrainSurfaceColor(tile.Code, tile.Low);
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

        private bool UseTileDiagonalWE(Tile tile, TileSurfaceVisual visual)
        {
            if (visual.SplitDirection == TileDiagonalDirection.WE) return true;
            if (visual.SplitDirection == TileDiagonalDirection.NS) return false;
            return UseTileDiagonalWE(tile);
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

        private bool TileEdgeHasFoundation(Tile tile, TileSurfaceVisual visual, RoadEdge edge)
        {
            if (UseTileDiagonalWE(tile, visual))
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

            TileRenderMaterial material = FirstTriangleCornersMatch(a, b, c)
                ? visual.FirstTriangleMaterial
                : visual.SecondTriangleMaterial;
            return material == TileRenderMaterial.Foundation;
        }

        private static bool FirstTriangleCornersMatch(TileCorner a, TileCorner b, TileCorner c)
        {
            return (a == TileCorner.W && b == TileCorner.S && c == TileCorner.E)
                || (a == TileCorner.N && b == TileCorner.W && c == TileCorner.S);
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
                    TileDiagonalDirection.Natural,
                    false,
                    TryGetFoundationFaceForRoadEdge(u, v - 1, tile.W, tile.S, RoadEdge.WS, out _) ? TileRenderMaterial.Foundation : TileRenderMaterial.Grass,
                    TryGetFoundationFaceForRoadEdge(u + 1, v, tile.S, tile.E, RoadEdge.SE, out _) ? TileRenderMaterial.Foundation : TileRenderMaterial.Grass,
                    TryGetFoundationFaceForRoadEdge(u, v + 1, tile.E, tile.N, RoadEdge.EN, out _) ? TileRenderMaterial.Foundation : TileRenderMaterial.Grass,
                    TryGetFoundationFaceForRoadEdge(u - 1, v, tile.N, tile.W, RoadEdge.NW, out _) ? TileRenderMaterial.Foundation : TileRenderMaterial.Grass);
            }

            TileSurfaceVisual visual = GetTileFoundationSurfaceMask(tile, u, v);
            if (visual.SurfaceMaterial == TileRenderMaterial.Grass)
                return GrassSurfaceVisual(u, v, tile);

            return new TileSurfaceVisual(
                visual.SurfaceMaterial,
                visual.FirstTriangleMaterial,
                visual.SecondTriangleMaterial,
                visual.SplitDirection,
                visual.DrawFoundationDiagonal,
                TileEdgeHasFoundation(tile, visual, RoadEdge.WS) ? TileRenderMaterial.Foundation : NeighborEdgeMaterial(u, v, tile, u, v - 1, RoadEdge.EN),
                TileEdgeHasFoundation(tile, visual, RoadEdge.SE) ? TileRenderMaterial.Foundation : NeighborEdgeMaterial(u, v, tile, u + 1, v, RoadEdge.NW),
                TileEdgeHasFoundation(tile, visual, RoadEdge.EN) ? TileRenderMaterial.Foundation : NeighborEdgeMaterial(u, v, tile, u, v + 1, RoadEdge.WS),
                TileEdgeHasFoundation(tile, visual, RoadEdge.NW) ? TileRenderMaterial.Foundation : NeighborEdgeMaterial(u, v, tile, u - 1, v, RoadEdge.SE));
        }

        private TileSurfaceVisual GrassSurfaceVisual(int u, int v, Tile tile)
        {
            return new TileSurfaceVisual(
                TileRenderMaterial.Grass,
                TileRenderMaterial.Grass,
                TileRenderMaterial.Grass,
                TileDiagonalDirection.Natural,
                false,
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
            TileSurfaceVisual neighborVisual = GetTileFoundationSurfaceMask(neighbor, nu, nv);
            return TileEdgeHasFoundation(neighbor, neighborVisual, neighborFacingEdge)
                ? TileRenderMaterial.Foundation
                : TileRenderMaterial.Grass;
        }

        // Checks whether an embankment face (height difference) exists between this tile
        // and an adjacent road tile. neighborFacingEdge is the road's edge facing us.
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

        private static TileRenderMaterial SurfaceMaterial(TileRenderMaterial first, TileRenderMaterial second)
        {
            if (first == TileRenderMaterial.Foundation && second == TileRenderMaterial.Foundation)
                return TileRenderMaterial.Foundation;
            if (first == TileRenderMaterial.Grass && second == TileRenderMaterial.Grass)
                return TileRenderMaterial.Grass;
            return TileRenderMaterial.MixedFoundation;
        }

        private TileSurfaceVisual GetTileFoundationSurfaceMask(Tile tile, int u, int v)
        {
            RoadEdge foundationEdges = GetRoadFacingFoundationEdgesFromAdjacentRoads(u, v, tile);
            TileRenderMaterial first = TileRenderMaterial.Grass;
            TileRenderMaterial second = TileRenderMaterial.Grass;
            TileDiagonalDirection splitDirection = TileDiagonalDirection.Natural;
            if (foundationEdges != RoadEdge.None)
            {
                first = TileRenderMaterial.Foundation;
                second = TileRenderMaterial.Foundation;
            }
            else
            {
                ApplyDiagonalRoadCornerMask(u, v, tile, ref first, ref second, ref splitDirection);
            }

            if (first != second && tile.Shape.IsPlanar)
            {
                first = TileRenderMaterial.Grass;
                second = TileRenderMaterial.Grass;
                splitDirection = TileDiagonalDirection.Natural;
            }

            return new TileSurfaceVisual(
                SurfaceMaterial(first, second),
                first,
                second,
                splitDirection,
                false,
                TileRenderMaterial.Grass,
                TileRenderMaterial.Grass,
                TileRenderMaterial.Grass,
                TileRenderMaterial.Grass);
        }

        private RoadEdge GetRoadFacingFoundationEdgesFromAdjacentRoads(int u, int v, Tile tile)
        {
            RoadEdge edges = RoadEdge.None;

            if (TryGetRoadTile(u, v - 1, out Tile roadSouth)
                && TryGetFoundationFace(u, v, roadSouth.E, roadSouth.N, tile.E, tile.N, out _))
                edges |= RoadEdge.WS;

            if (TryGetRoadTile(u + 1, v, out Tile roadEast)
                && TryGetFoundationFace(u, v, roadEast.N, roadEast.W, tile.N, tile.W, out _))
                edges |= RoadEdge.SE;

            if (TryGetRoadTile(u, v + 1, out Tile roadNorth)
                && TryGetFoundationFace(u, v, roadNorth.W, roadNorth.S, tile.W, tile.S, out _))
                edges |= RoadEdge.EN;

            if (TryGetRoadTile(u - 1, v, out Tile roadWest)
                && TryGetFoundationFace(u, v, roadWest.S, roadWest.E, tile.S, tile.E, out _))
                edges |= RoadEdge.NW;

            return edges;
        }

        private void ApplyDiagonalRoadCornerMask(int u, int v, Tile tile,
            ref TileRenderMaterial first, ref TileRenderMaterial second, ref TileDiagonalDirection splitDirection)
        {
            if (IsRoadTileWithFoundationEdges(u - 1, v - 1, RoadEdge.SE | RoadEdge.EN)
                && HasFoundationConnectorForCorner(u, v, TileCorner.W))
                ApplyFoundationToTriangleContainingCorner(tile, TileCorner.W, ref first, ref second, ref splitDirection);
            if (IsRoadTileWithFoundationEdges(u + 1, v - 1, RoadEdge.NW | RoadEdge.EN)
                && HasFoundationConnectorForCorner(u, v, TileCorner.S))
                ApplyFoundationToTriangleContainingCorner(tile, TileCorner.S, ref first, ref second, ref splitDirection);
            if (IsRoadTileWithFoundationEdges(u + 1, v + 1, RoadEdge.NW | RoadEdge.WS)
                && HasFoundationConnectorForCorner(u, v, TileCorner.E))
                ApplyFoundationToTriangleContainingCorner(tile, TileCorner.E, ref first, ref second, ref splitDirection);
            if (IsRoadTileWithFoundationEdges(u - 1, v + 1, RoadEdge.SE | RoadEdge.WS)
                && HasFoundationConnectorForCorner(u, v, TileCorner.N))
                ApplyFoundationToTriangleContainingCorner(tile, TileCorner.N, ref first, ref second, ref splitDirection);
        }

        private bool HasFoundationConnectorForCorner(int u, int v, TileCorner corner)
        {
            switch (corner)
            {
                case TileCorner.W:
                    return HasDirectFoundationAtTile(u - 1, v) || HasDirectFoundationAtTile(u, v - 1);
                case TileCorner.S:
                    return HasDirectFoundationAtTile(u + 1, v) || HasDirectFoundationAtTile(u, v - 1);
                case TileCorner.E:
                    return HasDirectFoundationAtTile(u + 1, v) || HasDirectFoundationAtTile(u, v + 1);
                case TileCorner.N:
                    return HasDirectFoundationAtTile(u - 1, v) || HasDirectFoundationAtTile(u, v + 1);
                default:
                    return false;
            }
        }

        private bool HasDirectFoundationAtTile(int u, int v)
        {
            if (!checkTile(u, v)) return false;
            Tile tile = getTileByCoords(u, v);
            if (roads.Has(tile.Id)) return false;
            return GetRoadFacingFoundationEdgesFromAdjacentRoads(u, v, tile) != RoadEdge.None;
        }

        private bool IsRoadTileWithFoundationEdges(int u, int v, RoadEdge roadEdges)
        {
            if (!TryGetRoadTile(u, v, out Tile roadTile)) return false;

            int tpc = nodeRows - 1;
            int roadU = roadTile.Id / tpc;
            int roadV = roadTile.Id % tpc;
            bool hasFirst = (roadEdges & RoadEdge.WS) != 0
                && TryGetFoundationFaceForRoadEdge(roadU, roadV - 1, roadTile.W, roadTile.S, RoadEdge.WS, out _);
            bool hasSecond = (roadEdges & RoadEdge.SE) != 0
                && TryGetFoundationFaceForRoadEdge(roadU + 1, roadV, roadTile.S, roadTile.E, RoadEdge.SE, out _);
            bool hasThird = (roadEdges & RoadEdge.EN) != 0
                && TryGetFoundationFaceForRoadEdge(roadU, roadV + 1, roadTile.E, roadTile.N, RoadEdge.EN, out _);
            bool hasFourth = (roadEdges & RoadEdge.NW) != 0
                && TryGetFoundationFaceForRoadEdge(roadU - 1, roadV, roadTile.N, roadTile.W, RoadEdge.NW, out _);
            return hasFirst || hasSecond || hasThird || hasFourth;
        }

        private void ApplyFoundationToTriangleContainingCorner(Tile tile, TileCorner corner,
            ref TileRenderMaterial first, ref TileRenderMaterial second, ref TileDiagonalDirection splitDirection)
        {
            TileDiagonalDirection desiredSplit = SplitToIsolateCorner(corner);
            if (splitDirection == TileDiagonalDirection.Natural)
                splitDirection = desiredSplit;

            bool useWE = splitDirection == TileDiagonalDirection.WE;
            if (useWE)
            {
                if (TriangleContainsCorner(TileCorner.W, TileCorner.S, TileCorner.E, corner))
                    first = TileRenderMaterial.Foundation;
                if (TriangleContainsCorner(TileCorner.W, TileCorner.E, TileCorner.N, corner))
                    second = TileRenderMaterial.Foundation;
                return;
            }

            if (TriangleContainsCorner(TileCorner.N, TileCorner.W, TileCorner.S, corner))
                first = TileRenderMaterial.Foundation;
            if (TriangleContainsCorner(TileCorner.N, TileCorner.S, TileCorner.E, corner))
                second = TileRenderMaterial.Foundation;
        }

        private static TileDiagonalDirection SplitToIsolateCorner(TileCorner corner)
        {
            return corner == TileCorner.W || corner == TileCorner.E
                ? TileDiagonalDirection.NS
                : TileDiagonalDirection.WE;
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
