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

        private static void DrawFullFoundationTile(Tile tile)
        {
            Vector3 w = Corner(tile.W);
            Vector3 s = Corner(tile.S);
            Vector3 e = Corner(tile.E);
            Vector3 n = Corner(tile.N);
            GL.Color4(ShadedTileColor(RoadFoundationSlopeColor, w, s, e));
            GL.Vertex3(w); GL.Vertex3(s); GL.Vertex3(e);
            GL.Color4(ShadedTileColor(RoadFoundationSlopeColor, w, e, n));
            GL.Vertex3(w); GL.Vertex3(e); GL.Vertex3(n);
        }

        private void DrawMixedFoundationTile(Tile tile, TileSurfaceVisual visual)
        {
            // roadSide=false is the triangle that shares its two vertices with the
            // adjacent road tile (e.g. N+E for WS edge) → paint it as foundation (brown).
            // roadSide=true is the opposite triangle that sits flat on the terrain → green.
            DrawMixedTriangle(tile, visual.SplitEdge, false, SurfaceColor(visual.RoadTriangleMaterial));
            DrawMixedTriangle(tile, visual.SplitEdge, true, SurfaceColor(visual.TerrainTriangleMaterial));
        }

        private static Color SurfaceColor(TileRenderMaterial material)
        {
            return material == TileRenderMaterial.Foundation ? RoadFoundationSlopeColor : TerrainTopColor;
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
            return GetTileSurfaceVisual(tile).SurfaceMaterial;
        }

        private TileSurfaceVisual GetTileSurfaceVisual(Tile tile)
        {
            int tpc = nodeRows - 1;
            int u = tile.Id / tpc;
            int v = tile.Id % tpc;

            if (roads.Has(tile.Id))
            {
                // Road tile: each border is brown only when an embankment face actually
                // exists toward that neighbour (height difference > 0).  On flat terrain
                // the edge is grass-coloured so both sides of the shared line agree.
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

            if (IsCoplanarTile(tile))
                return GrassSurfaceVisual(u, v, tile);

            if (edgeCount == 1)
            {
                // Road-facing edge is always brown.
                // Other three edges: brown only if the neighbour on that side also
                // warrants a brown border (Foundation or road with height diff).
                return new TileSurfaceVisual(
                    TileRenderMaterial.MixedFoundation,
                    TileRenderMaterial.Foundation,
                    TileRenderMaterial.Grass,
                    edges,
                    SplitForEdge(edges),
                    edges.HasFlag(RoadEdge.WS) ? TileRenderMaterial.Foundation : NeighborEdgeMaterial(u, v, tile, u, v + 1, RoadEdge.EN),
                    edges.HasFlag(RoadEdge.SE) ? TileRenderMaterial.Foundation : NeighborEdgeMaterial(u, v, tile, u - 1, v, RoadEdge.NW),
                    edges.HasFlag(RoadEdge.EN) ? TileRenderMaterial.Foundation : NeighborEdgeMaterial(u, v, tile, u, v - 1, RoadEdge.WS),
                    edges.HasFlag(RoadEdge.NW) ? TileRenderMaterial.Foundation : NeighborEdgeMaterial(u, v, tile, u + 1, v, RoadEdge.SE));
            }

            return UniformSurfaceVisual(TileRenderMaterial.Foundation);
        }

        // Grass surface visual that colours each shared edge brown only when the
        // neighbour's edge that faces us is actually a Foundation edge.
        // For road tiles and full-Foundation neighbours all edges are brown.
        // For MixedFoundation neighbours only the road-facing edge is brown.
        private TileSurfaceVisual GrassSurfaceVisual(int u, int v, Tile tile)
        {
            // Neighbour directions and which edge of the neighbour faces back at us:
            //   EdgeWS → neighbour at (u, v+1),  neighbour's EN edge faces us
            //   EdgeSE → neighbour at (u-1, v),  neighbour's NW edge faces us
            //   EdgeEN → neighbour at (u, v-1),  neighbour's WS edge faces us
            //   EdgeNW → neighbour at (u+1, v),  neighbour's SE edge faces us
            return new TileSurfaceVisual(
                TileRenderMaterial.Grass,
                TileRenderMaterial.Grass,
                TileRenderMaterial.Grass,
                RoadEdge.None,
                TileDiagonalSplit.None,
                NeighborEdgeMaterial(u, v, tile, u, v + 1, RoadEdge.EN),
                NeighborEdgeMaterial(u, v, tile, u - 1, v, RoadEdge.NW),
                NeighborEdgeMaterial(u, v, tile, u, v - 1, RoadEdge.WS),
                NeighborEdgeMaterial(u, v, tile, u + 1, v, RoadEdge.SE));
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
                // Brown only when an embankment face actually exists (height diff).
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
            if (IsCoplanarTile(neighbor)) return TileRenderMaterial.Grass;
            // Full Foundation neighbour (2+ road edges): all its edges are brown.
            if (neighborEdgeCount > 1) return TileRenderMaterial.Foundation;
            // Mixed neighbour: only the road-facing edge is brown.
            return neighborEdges.HasFlag(neighborFacingEdge) ? TileRenderMaterial.Foundation : TileRenderMaterial.Grass;
        }

        // Checks whether an embankment face (height difference) exists between this tile
        // and an adjacent road tile.  neighborFacingEdge is the road's edge facing us.
        // Node pairs mirror GetFoundationEdgesFromAdjacentRoads / DrawFoundationFacesForRoadTile.
        private bool HasFoundationFaceTowardRoad(int u, int v, Tile tile, Tile road, RoadEdge neighborFacingEdge)
        {
            return neighborFacingEdge switch
            {
                RoadEdge.EN => TryGetFoundationFace(u, v, road.W, road.S, tile.W, tile.S, out _),
                RoadEdge.NW => TryGetFoundationFace(u, v, road.S, road.E, tile.S, tile.E, out _),
                RoadEdge.WS => TryGetFoundationFace(u, v, road.E, road.N, tile.E, tile.N, out _),
                RoadEdge.SE => TryGetFoundationFace(u, v, road.N, road.W, tile.N, tile.W, out _),
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
