using System;
using OpenTK.Mathematics;

namespace ForesTycoon.Map
{
    /// <summary>
    /// Classification of how every tile's surface looks structurally: natural ground, or road
    /// foundation (embankment/cutting) fully or on part of the tile. Renderers only paint it.
    /// </summary>
    internal sealed partial class TerrainMap
    {
        internal bool UseTileDiagonalWE(Tile tile)
        {
            bool useWE = Math.Abs(tile.W.zPos - tile.E.zPos) <= Math.Abs(tile.N.zPos - tile.S.zPos);
            return flippedDiagonalTiles.Contains(tile.Id) ? !useWE : useWE;
        }

        internal bool UseTileDiagonalWE(Tile tile, TileSurface visual)
        {
            if (visual.SplitDirection == TileDiagonalDirection.WE) return true;
            if (visual.SplitDirection == TileDiagonalDirection.NS) return false;
            return UseTileDiagonalWE(tile);
        }

        internal static Vector3 Corner(Tile tile, TileCorner corner)
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

        internal bool TileEdgeHasFoundation(Tile tile, TileSurface visual, RoadEdge edge)
        {
            if (UseTileDiagonalWE(tile, visual))
            {
                return TriangleEdgeHasFoundation(visual, TileCorner.W, TileCorner.S, TileCorner.E, edge)
                    || TriangleEdgeHasFoundation(visual, TileCorner.W, TileCorner.E, TileCorner.N, edge);
            }

            return TriangleEdgeHasFoundation(visual, TileCorner.N, TileCorner.W, TileCorner.S, edge)
                || TriangleEdgeHasFoundation(visual, TileCorner.N, TileCorner.S, TileCorner.E, edge);
        }

        private static bool TriangleEdgeHasFoundation(TileSurface visual, TileCorner a, TileCorner b, TileCorner c, RoadEdge edge)
        {
            if (!TriangleContainsEdge(a, b, c, edge)) return false;

            TileSurfaceMaterial material = FirstTriangleCornersMatch(a, b, c)
                ? visual.FirstTriangleMaterial
                : visual.SecondTriangleMaterial;
            return material == TileSurfaceMaterial.Foundation;
        }

        private static bool FirstTriangleCornersMatch(TileCorner a, TileCorner b, TileCorner c)
        {
            return (a == TileCorner.W && b == TileCorner.S && c == TileCorner.E)
                || (a == TileCorner.N && b == TileCorner.W && c == TileCorner.S);
        }

        internal static bool TriangleContainsCorner(TileCorner a, TileCorner b, TileCorner c, TileCorner corner)
        {
            return a == corner || b == corner || c == corner;
        }

        internal static void EdgeCorners(RoadEdge edge, out TileCorner a, out TileCorner b)
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

        internal bool TryGetFoundationFaceForRoadEdge(int adjacentU, int adjacentV, Node sharedA, Node sharedB, RoadEdge edge, out FoundationFaceData face)
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

        internal bool TryGetFoundationFace(int nu, int nv, Node sharedA, Node sharedB, Node outerA, Node outerB,
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

        internal TileSurfaceMaterial GetTileSurfaceMaterial(Tile tile)
        {
            return GetTileSurface(tile).SurfaceMaterial;
        }

        private TileSurface[] surfaceVisualCache;
        private ulong[] surfaceVisualVersions;
        private ulong surfaceVisualVersion = 1;

        /// <summary>Bumped whenever the surface classification may have changed; renderers key caches on it.</summary>
        internal ulong SurfaceVersion => surfaceVisualVersion;

        internal void InvalidateSurface() => surfaceVisualVersion++;

        internal bool SurfaceCacheMatchesFreshCalculation()
        {
            foreach (Tile tile in tiles)
                if (!GetTileSurface(tile).Equals(ComputeTileSurface(tile))) return false;
            return true;
        }

        internal TileSurface GetTileSurface(Tile tile)
        {
            if (surfaceVisualCache == null)
            {
                surfaceVisualCache = new TileSurface[tiles.Length];
                surfaceVisualVersions = new ulong[tiles.Length];
            }
            if (surfaceVisualVersions[tile.Id] != surfaceVisualVersion)
            {
                surfaceVisualCache[tile.Id] = ComputeTileSurface(tile);
                surfaceVisualVersions[tile.Id] = surfaceVisualVersion;
            }
            return surfaceVisualCache[tile.Id];
        }

        private TileSurface ComputeTileSurface(Tile tile)
        {
            int tpc = nodeRows - 1;
            int u = tile.Id / tpc;
            int v = tile.Id % tpc;

            if (roads.Has(tile.Id))
            {
                return new TileSurface(
                    TileSurfaceMaterial.Foundation,
                    TileSurfaceMaterial.Foundation,
                    TileSurfaceMaterial.Foundation,
                    TileDiagonalDirection.Natural,
                    false,
                    TryGetFoundationFaceForRoadEdge(u, v - 1, tile.W, tile.S, RoadEdge.WS, out _) ? TileSurfaceMaterial.Foundation : TileSurfaceMaterial.Grass,
                    TryGetFoundationFaceForRoadEdge(u + 1, v, tile.S, tile.E, RoadEdge.SE, out _) ? TileSurfaceMaterial.Foundation : TileSurfaceMaterial.Grass,
                    TryGetFoundationFaceForRoadEdge(u, v + 1, tile.E, tile.N, RoadEdge.EN, out _) ? TileSurfaceMaterial.Foundation : TileSurfaceMaterial.Grass,
                    TryGetFoundationFaceForRoadEdge(u - 1, v, tile.N, tile.W, RoadEdge.NW, out _) ? TileSurfaceMaterial.Foundation : TileSurfaceMaterial.Grass);
            }

            TileSurface visual = GetTileFoundationSurfaceMask(tile, u, v);
            if (visual.SurfaceMaterial == TileSurfaceMaterial.Grass)
                return GrassSurfaceVisual(u, v, tile);

            return new TileSurface(
                visual.SurfaceMaterial,
                visual.FirstTriangleMaterial,
                visual.SecondTriangleMaterial,
                visual.SplitDirection,
                visual.DrawFoundationDiagonal,
                TileEdgeHasFoundation(tile, visual, RoadEdge.WS) ? TileSurfaceMaterial.Foundation : NeighborEdgeMaterial(u, v, tile, u, v - 1, RoadEdge.EN),
                TileEdgeHasFoundation(tile, visual, RoadEdge.SE) ? TileSurfaceMaterial.Foundation : NeighborEdgeMaterial(u, v, tile, u + 1, v, RoadEdge.NW),
                TileEdgeHasFoundation(tile, visual, RoadEdge.EN) ? TileSurfaceMaterial.Foundation : NeighborEdgeMaterial(u, v, tile, u, v + 1, RoadEdge.WS),
                TileEdgeHasFoundation(tile, visual, RoadEdge.NW) ? TileSurfaceMaterial.Foundation : NeighborEdgeMaterial(u, v, tile, u - 1, v, RoadEdge.SE));
        }

        private TileSurface GrassSurfaceVisual(int u, int v, Tile tile)
        {
            return new TileSurface(
                TileSurfaceMaterial.Grass,
                TileSurfaceMaterial.Grass,
                TileSurfaceMaterial.Grass,
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
        private TileSurfaceMaterial NeighborEdgeMaterial(int u, int v, Tile currentTile, int neighborU, int neighborV, RoadEdge neighborFacingEdge)
        {
            if (!checkTile(neighborU, neighborV)) return TileSurfaceMaterial.Grass;
            Tile neighbor = getTileByCoords(neighborU, neighborV);
            if (roads.Has(neighbor.Id))
            {
                return HasFoundationFaceTowardRoad(u, v, currentTile, neighbor, neighborFacingEdge)
                    ? TileSurfaceMaterial.Foundation
                    : TileSurfaceMaterial.Grass;
            }

            int tpc = nodeRows - 1;
            int nu = neighbor.Id / tpc;
            int nv = neighbor.Id % tpc;
            TileSurface neighborVisual = GetTileFoundationSurfaceMask(neighbor, nu, nv);
            return TileEdgeHasFoundation(neighbor, neighborVisual, neighborFacingEdge)
                ? TileSurfaceMaterial.Foundation
                : TileSurfaceMaterial.Grass;
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

        private static TileSurfaceMaterial SurfaceMaterial(TileSurfaceMaterial first, TileSurfaceMaterial second)
        {
            if (first == TileSurfaceMaterial.Foundation && second == TileSurfaceMaterial.Foundation)
                return TileSurfaceMaterial.Foundation;
            if (first == TileSurfaceMaterial.Grass && second == TileSurfaceMaterial.Grass)
                return TileSurfaceMaterial.Grass;
            return TileSurfaceMaterial.MixedFoundation;
        }

        private TileSurface GetTileFoundationSurfaceMask(Tile tile, int u, int v)
        {
            RoadEdge foundationEdges = GetRoadFacingFoundationEdgesFromAdjacentRoads(u, v, tile);
            TileSurfaceMaterial first = TileSurfaceMaterial.Grass;
            TileSurfaceMaterial second = TileSurfaceMaterial.Grass;
            TileDiagonalDirection splitDirection = TileDiagonalDirection.Natural;
            if (foundationEdges != RoadEdge.None)
            {
                first = TileSurfaceMaterial.Foundation;
                second = TileSurfaceMaterial.Foundation;
            }
            else
            {
                ApplyDiagonalRoadCornerMask(u, v, tile, ref first, ref second, ref splitDirection);
            }

            if (first != second && tile.Shape.IsPlanar)
            {
                first = TileSurfaceMaterial.Grass;
                second = TileSurfaceMaterial.Grass;
                splitDirection = TileDiagonalDirection.Natural;
            }

            return new TileSurface(
                SurfaceMaterial(first, second),
                first,
                second,
                splitDirection,
                false,
                TileSurfaceMaterial.Grass,
                TileSurfaceMaterial.Grass,
                TileSurfaceMaterial.Grass,
                TileSurfaceMaterial.Grass);
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
            ref TileSurfaceMaterial first, ref TileSurfaceMaterial second, ref TileDiagonalDirection splitDirection)
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
            ref TileSurfaceMaterial first, ref TileSurfaceMaterial second, ref TileDiagonalDirection splitDirection)
        {
            TileDiagonalDirection desiredSplit = SplitToIsolateCorner(corner);
            if (splitDirection == TileDiagonalDirection.Natural)
                splitDirection = desiredSplit;

            bool useWE = splitDirection == TileDiagonalDirection.WE;
            if (useWE)
            {
                if (TriangleContainsCorner(TileCorner.W, TileCorner.S, TileCorner.E, corner))
                    first = TileSurfaceMaterial.Foundation;
                if (TriangleContainsCorner(TileCorner.W, TileCorner.E, TileCorner.N, corner))
                    second = TileSurfaceMaterial.Foundation;
                return;
            }

            if (TriangleContainsCorner(TileCorner.N, TileCorner.W, TileCorner.S, corner))
                first = TileSurfaceMaterial.Foundation;
            if (TriangleContainsCorner(TileCorner.N, TileCorner.S, TileCorner.E, corner))
                second = TileSurfaceMaterial.Foundation;
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

        /// <summary>Point on the actual terrain triangles (forest tiles do not have road diagonal overrides).</summary>
        internal static void SurfacePoint(Tile tile, float u, float v,
            out float x, out float y, out float z)
        {
            float wW = (1f - u) * (1f - v);
            float wS = u * (1f - v);
            float wE = u * v;
            float wN = (1f - u) * v;

            x = tile.W.xPos * wW + tile.S.xPos * wS + tile.E.xPos * wE + tile.N.xPos * wN;
            y = tile.W.yPos * wW + tile.S.yPos * wS + tile.E.yPos * wE + tile.N.yPos * wN;
            bool diagonalWE = Math.Abs(tile.W.zPos - tile.E.zPos) <= Math.Abs(tile.N.zPos - tile.S.zPos);
            z = diagonalWE
                ? (u >= v ? tile.W.zPos * (1 - u) + tile.S.zPos * (u - v) + tile.E.zPos * v
                          : tile.W.zPos * (1 - v) + tile.E.zPos * u + tile.N.zPos * (v - u))
                : (u + v <= 1 ? tile.W.zPos * (1 - u - v) + tile.S.zPos * u + tile.N.zPos * v
                              : tile.S.zPos * (1 - v) + tile.E.zPos * (u + v - 1) + tile.N.zPos * (1 - u));
        }
    }
}
