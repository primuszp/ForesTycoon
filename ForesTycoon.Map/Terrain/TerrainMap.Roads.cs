using System;
using System.Collections.Generic;
using OpenTK.Mathematics;

namespace ForesTycoon.Map
{
    internal sealed partial class TerrainMap
    {
        public int RoadCount => roads.Count;
        public bool IsRoadTile(int tileId) => IsValidTileId(tileId) && roads.Has(tileId);

        public int[] FindDemoRoadRoute() => RoadPathfinder.FindDemoRoute(roads, nodeRows - 1);

        /// <summary>Centre and slope (dz/dx, dz/dy) of the frozen road surface of a road tile.</summary>
        public bool TryGetRoadSurface(int tileId, out Vector3 center, out Vector2 gradient)
        {
            gradient = Vector2.Zero;
            if (!TryGetRoadTileCenter(tileId, out center)) return false;
            Tile tile = tiles[tileId];
            Vector3 w = RoadCorner(tile.W), s = RoadCorner(tile.S), n = RoadCorner(tile.N);
            Vector3 normal = Vector3.Cross(s - w, n - w);
            gradient = new Vector2(-normal.X / normal.Z, -normal.Y / normal.Z);
            return true;
        }

        public bool TryGetRoadTileCenter(int tileId, out Vector3 center)
        {
            if (!IsValidTileId(tileId) || !roads.Has(tileId))
            {
                center = Vector3.Zero;
                return false;
            }

            Tile tile = tiles[tileId];
            center = (RoadCorner(tile.W) + RoadCorner(tile.S) + RoadCorner(tile.E) + RoadCorner(tile.N)) * 0.25f;
            return true;
        }

        public bool AddRoadTile(Tile t)
        {
            RoadEdge edges = RoadEdge.WS | RoadEdge.EN | GetNetworkEdges(t.Id);
            RoadPlacement placement = AnalyzeRoadPlacement(t, edges);
            if (!placement.IsValid) return false;

            bool added = roads.Add(t.Id, edges);
            if (added)
            {
                skidTrails.Remove(t.Id);
                CaptureRoadSurface(t, placement); InvalidateSurface();
                chunkIndex.MarkTileAndNeighboursDirty(t.Id, ChunkDirtyFlags.Roads | ChunkDirtyFlags.Foundations);
            }
            return added;
        }

        // OpenTTD-stílusú útépítés: az elemzés eldönti, hogy az út a természetes
        // terepre ülhet-e, vagy flat foundation vezetőfelületet kell befagyasztani.
        public bool IsRoadBuildable(Tile t) => IsRoadBuildable(t, RoadEdge.WS | RoadEdge.EN);

        private bool IsRoadBuildable(Tile t, RoadEdge edges)
        {
            return AnalyzeRoadPlacement(t, edges).IsValid;
        }

        internal RoadPlacement AnalyzeRoadPlacement(Tile t, RoadEdge requestedEdges)
        {
            return AnalyzeRoadPlacement(t, requestedEdges, n => n.W);
        }

        private RoadPlacement AnalyzeRoadPlacement(Tile t, RoadEdge requestedEdges, Func<Node, int> heightOf)
        {
            if (t == null || IsBuildingTile(t.Id)) return RoadPlacement.Invalid;
            if (hydro.ShouldDrawStandingWater(t)) return RoadPlacement.Invalid;

            RoadEdge mergedEdges = requestedEdges | GetNetworkEdges(t.Id);
            if (mergedEdges == RoadEdge.None) return RoadPlacement.Invalid;

            int w = heightOf(t.W);
            int s = heightOf(t.S);
            int e = heightOf(t.E);
            int n = heightOf(t.N);
            if (!RoadTerrainStaysAboveWater(w, s, e, n)) return RoadPlacement.Invalid;

            if (roads.Has(t.Id) && TryGetFullLockedRoadSurface(t, out int lw, out int ls, out int le, out int ln))
                return ValidateLockedRoadPlacement(mergedEdges, w, s, e, n, lw, ls, le, ln);

            TileShapeInfo shape = TileShapeInfo.FromCorners(w, s, e, n);
            if (shape.Kind == TileShapeKind.Steep
                || shape.Kind == TileShapeKind.Saddle
                || shape.Kind == TileShapeKind.OneHigh
                || shape.Kind == TileShapeKind.ThreeHigh)
                return RoadPlacement.Invalid;

            // Ramp only valid when it aligns with the road direction.
            if (shape.Kind == TileShapeKind.Ramp && !IsRampAligned(shape, mergedEdges))
                return RoadPlacement.Invalid;

            bool naturalAllowed = shape.Kind == TileShapeKind.Flat
                || (shape.Kind == TileShapeKind.Ramp && IsSimpleRoadShape(mergedEdges));
            if (naturalAllowed && RoadSurfaceLocksMatch(t, w, s, e, n))
                return new RoadPlacement(RoadPlacementKind.NaturalSurface, w, s, e, n);

            // Foundation (platform) only on flat terrain
            if (shape.Kind != TileShapeKind.Flat)
                return RoadPlacement.Invalid;

            if (!TryResolveFlatFoundationLevel(t, shape.Max, w, s, e, n, out int level))
                return RoadPlacement.Invalid;

            return new RoadPlacement(RoadPlacementKind.FoundationSurface, level, level, level, level);
        }

        private RoadPlacement ValidateLockedRoadPlacement(
            RoadEdge edges,
            int terrainW, int terrainS, int terrainE, int terrainN,
            int surfaceW, int surfaceS, int surfaceE, int surfaceN)
        {
            LockedRoadSurfaceResult result = RoadPlacementRules.ValidateLockedSurface(edges,
                terrainW, terrainS, terrainE, terrainN, surfaceW, surfaceS, surfaceE, surfaceN);
            RoadPlacementKind kind = result switch
            {
                LockedRoadSurfaceResult.NaturalSurface => RoadPlacementKind.NaturalSurface,
                LockedRoadSurfaceResult.FoundationSurface => RoadPlacementKind.FoundationSurface,
                _ => RoadPlacementKind.Invalid
            };
            return kind == RoadPlacementKind.Invalid
                ? RoadPlacement.Invalid
                : new RoadPlacement(kind, surfaceW, surfaceS, surfaceE, surfaceN);
        }

        private bool RoadTerrainStaysAboveWater(int w, int s, int e, int n)
        {
            int min = Math.Min(Math.Min(w, s), Math.Min(e, n));
            return min * tileSizeM >= SeaLevel;
        }

        private static bool IsSimpleRoadShape(RoadEdge edges)
            => RoadPlacementRules.IsSimple(edges);

        // Csak Ramp esetén: a rámpa iránya egyezik-e az út irányával.
        // WS+EN irány: W==S és E==N kell (WS él vízszintes, EN él vízszintes).
        // SE+NW irány: S==E és N==W kell (SE él vízszintes, NW él vízszintes).
        private static bool IsRampAligned(TileShapeInfo shape, RoadEdge edges)
            => RoadPlacementRules.IsRampAligned(shape, edges);

        private bool TryGetFullLockedRoadSurface(Tile t, out int w, out int s, out int e, out int n)
        {
            bool ok = roadSurfaceW.TryGetValue(t.W.Id, out w);
            ok &= roadSurfaceW.TryGetValue(t.S.Id, out s);
            ok &= roadSurfaceW.TryGetValue(t.E.Id, out e);
            ok &= roadSurfaceW.TryGetValue(t.N.Id, out n);
            return ok;
        }

        private bool RoadSurfaceLocksMatch(Tile t, int w, int s, int e, int n)
        {
            return RoadSurfaceLockMatches(t.W, w)
                && RoadSurfaceLockMatches(t.S, s)
                && RoadSurfaceLockMatches(t.E, e)
                && RoadSurfaceLockMatches(t.N, n);
        }

        private bool RoadSurfaceLockMatches(Node node, int w)
        {
            return !roadSurfaceW.TryGetValue(node.Id, out int lockedW) || lockedW == w;
        }

        private bool TryResolveFlatFoundationLevel(Tile t, int minimumLevel, int terrainW, int terrainS, int terrainE, int terrainN, out int level)
        {
            level = minimumLevel;
            bool hasLockedLevel = false;
            int lockedLevel = 0;

            bool AddLock(Node node, int terrain)
            {
                if (!roadSurfaceW.TryGetValue(node.Id, out int lockedW)) return true;
                if (lockedW < terrain || lockedW < minimumLevel) return false;
                if (!hasLockedLevel)
                {
                    lockedLevel = lockedW;
                    hasLockedLevel = true;
                    return true;
                }
                return lockedLevel == lockedW;
            }

            if (!AddLock(t.W, terrainW)) return false;
            if (!AddLock(t.S, terrainS)) return false;
            if (!AddLock(t.E, terrainE)) return false;
            if (!AddLock(t.N, terrainN)) return false;

            if (hasLockedLevel) level = lockedLevel;
            return level >= terrainW && level >= terrainS && level >= terrainE && level >= terrainN
                && level - terrainW <= 1 && level - terrainS <= 1 && level - terrainE <= 1 && level - terrainN <= 1;
        }

        public RoadPaving GetRoadPaving(int tileId) => roads.GetPaving(tileId);
        /// <summary>Surface condition of a road tile: 1 = new, 0 = ruined.</summary>
        public float GetRoadCondition(int tileId) => roads.GetCondition(tileId);
        public IEnumerable<int> RoadTiles => roads.Tiles;

        /// <summary>Wears the surface of a road tile by <paramref name="amount"/> (traffic).</summary>
        public void WearRoad(int tileId, float amount)
        {
            if (amount > 0 && roads.Has(tileId)) roads.SetCondition(tileId, roads.GetCondition(tileId) - amount);
        }

        /// <summary>Restores the worn road tiles along the a → b path; returns each repaired tile and the condition it lacked.</summary>
        public (int Tile, float Damage)[] RepairRoadTilePath(int startTileId, int endTileId)
        {
            if (!IsValidTileId(startTileId) || !IsValidTileId(endTileId)) return Array.Empty<(int, float)>();
            var repaired = new List<(int, float)>();
            foreach (RoadPlanStep step in BuildRoadPlan(tiles[startTileId], tiles[endTileId]))
            {
                if (!roads.Has(step.TileId)) continue;
                float damage = 1 - roads.GetCondition(step.TileId);
                if (damage < 0.005f) continue;
                roads.SetCondition(step.TileId, 1); repaired.Add((step.TileId, damage));
            }
            return repaired.ToArray();
        }

        /// <summary>The a → b path's tiles that a repair would restore, with the condition they lack (preview and cost).</summary>
        public (int Tile, float Damage)[] PlanRoadRepair(int startTileId, int endTileId)
        {
            if (!IsValidTileId(startTileId) || !IsValidTileId(endTileId)) return Array.Empty<(int, float)>();
            var worn = new List<(int, float)>();
            foreach (RoadPlanStep step in BuildRoadPlan(tiles[startTileId], tiles[endTileId]))
                if (roads.Has(step.TileId) && 1 - roads.GetCondition(step.TileId) >= 0.005f)
                    worn.Add((step.TileId, 1 - roads.GetCondition(step.TileId)));
            return worn.ToArray();
        }

        public int[] BuildRoadTilePath(Tile a, Tile b) => BuildRoadTilePath(a, b, RoadPaving.Asphalt);

        /// <summary>
        /// Builds the a → b road with the given surface. Road tiles already on the path that carry the other surface are
        /// resurfaced. Returns every tile newly built or resurfaced.
        /// </summary>
        public int[] BuildRoadTilePath(Tile a, Tile b, RoadPaving surface)
        {
            List<int> changed = new();
            foreach (RoadPlanStep step in BuildRoadPlan(a, b))
            {
                Tile tile = tiles[step.TileId];
                RoadEdge edges = step.Edges | GetNetworkEdges(step.TileId);
                RoadPlacement placement = AnalyzeRoadPlacement(tile, edges);
                bool resurfaced = placement.IsValid && roads.Resurface(step.TileId, surface);
                if (resurfaced && !roads.Add(step.TileId, edges, surface))
                {
                    changed.Add(step.TileId);
                    chunkIndex.MarkTileAndNeighboursDirty(step.TileId, ChunkDirtyFlags.Roads);
                    continue;
                }
                if (resurfaced || (placement.IsValid && roads.Add(step.TileId, edges, surface)))
                {
                    skidTrails.Remove(step.TileId);
                    changed.Add(step.TileId);
                    CaptureRoadSurface(tile, placement);
                    InvalidateSurface();
                    chunkIndex.MarkTileAndNeighboursDirty(step.TileId, ChunkDirtyFlags.Roads | ChunkDirtyFlags.Foundations);
                }
            }
            if (changed.Count > 0) RebuildFlippedDiagonalTiles();
            return changed.ToArray();
        }

        public int[] BuildRoadTilePath(int startTileId, int endTileId, RoadPaving surface = RoadPaving.Asphalt)
        {
            if (!IsValidTileId(startTileId) || !IsValidTileId(endTileId)) return Array.Empty<int>();
            return BuildRoadTilePath(tiles[startTileId], tiles[endTileId], surface);
        }

        /// <summary>
        /// Weathering over <paramref name="years"/>: macadam washes out, much faster on wet ground and in rain; asphalt
        /// ages slowly. <paramref name="rain"/> is the present rain intensity (0 dry … 1 downpour).
        /// </summary>
        public void WeatherRoads(float years, float rain)
        {
            if (years <= 0 || roads.Count == 0) return;
            float[] moisture = hydro.TileMoisture;
            foreach (int id in roads.Tiles)
            {
                float wet = Math.Clamp(moisture[id], 0, 1);
                float rate = roads.GetPaving(id) == RoadPaving.Macadam
                    ? 0.10f * (1 + 2.5f * wet) + 1.6f * rain * (0.4f + wet)
                    : 0.015f * (1 + wet) + 0.08f * rain;
                roads.SetCondition(id, roads.GetCondition(id) - rate * years);
            }
        }

        public int[] RemoveRoadTilePath(Tile a, Tile b)
        {
            List<int> changed = new();
            foreach (RoadPlanStep step in BuildRoadPlan(a, b))
            {
                if (roads.Remove(step.TileId, step.Edges))
                {
                    changed.Add(step.TileId);
                    ReleaseRoadSurface(tiles[step.TileId]);
                    InvalidateSurface();
                    chunkIndex.MarkTileAndNeighboursDirty(step.TileId, ChunkDirtyFlags.Roads | ChunkDirtyFlags.Foundations);
                }
            }
            if (changed.Count > 0) RebuildFlippedDiagonalTiles();
            return changed.ToArray();
        }

        public int[] RemoveRoadTilePath(int startTileId, int endTileId)
        {
            if (!IsValidTileId(startTileId) || !IsValidTileId(endTileId)) return Array.Empty<int>();
            return RemoveRoadTilePath(tiles[startTileId], tiles[endTileId]);
        }

                // Teljes újraépítés minden road módosítás után: sorrendfüggetlen, univerzális.
        // Minden road tile minden szomszédos él-párjánál beállítja a diagonális szomszéd flipjét.
        private void RebuildFlippedDiagonalTiles()
        {
            flippedDiagonalTiles.Clear();
            int tpc = nodeRows - 1;
            foreach (int id in roads.Tiles)
            {
                RoadEdge e = roads.GetEdges(id);
                int u = id / tpc, v = id % tpc;
                if ((e & RoadEdge.NW) != 0 && (e & RoadEdge.WS) != 0) AddFlipTile(u - 1, v - 1);
                if ((e & RoadEdge.WS) != 0 && (e & RoadEdge.SE) != 0) AddFlipTile(u + 1, v - 1);
                if ((e & RoadEdge.SE) != 0 && (e & RoadEdge.EN) != 0) AddFlipTile(u + 1, v + 1);
                if ((e & RoadEdge.EN) != 0 && (e & RoadEdge.NW) != 0) AddFlipTile(u - 1, v + 1);
            }
            RoadDiagonalsChanged?.Invoke();
        }

        private void AddFlipTile(int iu, int iv)
        {
            if (!checkTile(iu, iv)) return;
            Tile inner = getTileByCoords(iu, iv);
            if (roads.Has(inner.Id)) return;
            flippedDiagonalTiles.Add(inner.Id);
                    }

        // Az út-csempe 4 sarkának aktuális magasságát befagyasztjuk vezetőfelületnek
        // (csak ha még nincs rögzítve, hogy a meglévő szomszéd-úttal folytonos maradjon).
        private void CaptureRoadSurface(Tile t, RoadPlacement placement)
        {
            // MINDEN út-csempe befagy: az út szintje soha nem változik. A terep alatta/
            // körülötte szabadon alakítható; a különbséget a földmű tölti ki (lejjebb →
            // töltés, feljebb → bevágás).
            if (!placement.IsValid) return;
            CaptureRoadSurfaceNode(t.W, placement.W);
            CaptureRoadSurfaceNode(t.S, placement.S);
            CaptureRoadSurfaceNode(t.E, placement.E);
            CaptureRoadSurfaceNode(t.N, placement.N);
        }

        private void CaptureRoadSurfaceNode(Node n, int w)
        {
            if (!roadSurfaceW.ContainsKey(n.Id)) roadSurfaceW[n.Id] = w;
        }

        // Bontáskor a sarok felület-magasságát elengedjük, ha már egyetlen szomszédos
        // csempe sem út (különben a maradék út folytonosságát megőrizzük).
        private void ReleaseRoadSurface(Tile t)
        {
            Tile[] nodeTiles = new Tile[4];
            ReleaseRoadSurfaceNode(t.W, nodeTiles);
            ReleaseRoadSurfaceNode(t.S, nodeTiles);
            ReleaseRoadSurfaceNode(t.E, nodeTiles);
            ReleaseRoadSurfaceNode(t.N, nodeTiles);
        }

        private void ReleaseRoadSurfaceNode(Node node, Tile[] nodeTiles)
        {
            bool stillRoad = false;
            int nodeTileCount = data.GetTilesByNode(node, nodeTiles);
            for (int i = 0; i < nodeTileCount; i++)
            {
                if (!roads.Has(nodeTiles[i].Id)) continue;
                stillRoad = true;
                break;
            }

            if (!stillRoad) roadSurfaceW.Remove(node.Id);
        }

        // Az út vezetőfelületének z-je egy sarokban: a befagyasztott magasság, ha van,
        // különben a jelenlegi terep (még szerkesztetlen út, vagy nem-út sarok).
        internal float RoadSurfaceZ(Node n) =>
            (roadSurfaceW.TryGetValue(n.Id, out int w) ? w : n.W) * tileSizeM;

        internal Vector3 RoadCorner(Node n) => new Vector3(n.xPos, n.yPos, RoadSurfaceZ(n));

        private Vector3 RoadCorner(Node n, int w) => new Vector3(n.xPos, n.yPos, w * tileSizeM);

        // Csempe-útvonal bejárása a rácson (egyenes lépcsős út a → b között).
        internal readonly struct RoadPlanStep
        {
            public readonly int TileId;
            public readonly RoadEdge Edges;

            public RoadPlanStep(int tileId, RoadEdge edges)
            {
                TileId = tileId;
                Edges = edges;
            }
        }

        internal List<RoadPlanStep> BuildRoadPlan(Tile a, Tile b)
        {
            List<RoadPlanStep> result = new List<RoadPlanStep>();
            if (a == null || b == null) return result;
            int tpc = nodeRows - 1;
            int startU = a.Id / tpc, startV = a.Id % tpc;
            int endU = b.Id / tpc, endV = b.Id % tpc;
            int du = endU - startU;
            int dv = endV - startV;

            if (du == 0 && dv == 0)
            {
                RoadEdge existing = roads.GetEdges(a.Id);
                result.Add(new RoadPlanStep(a.Id, existing != RoadEdge.None ? existing : RoadEdge.WS | RoadEdge.EN));
                return result;
            }

            // L-alakú útvonal: előbb a domináns tengely mentén a törésig, majd a másik
            // tengely mentén a célig. A töréscsempe így 2 szomszédos élt kap → ív-kanyar.
            bool uFirst = Math.Abs(du) >= Math.Abs(dv);

            int u = startU, v = startV;
            RoadEdge previousEntry = RoadEdge.None;
            while (true)
            {
                int nextU = u;
                int nextV = v;
                if (uFirst)
                {
                    if (u != endU) nextU += Math.Sign(endU - u);
                    else if (v != endV) nextV += Math.Sign(endV - v);
                }
                else
                {
                    if (v != endV) nextV += Math.Sign(endV - v);
                    else if (u != endU) nextU += Math.Sign(endU - u);
                }

                // A csempe élei = ahonnan jöttünk | ahová tovább lépünk. A végpontokon
                // nincs fantom-egyenes: 1 él = zsákutca-csonk. Így amikor egy másik húzás
                // ráfut, a tényleges élek összegződnek (2 szomszédos = ív, 3 = T, 4 = +).
                RoadEdge exit = nextU != u || nextV != v ? EdgeToNeighbor(u, v, nextU, nextV) : RoadEdge.None;
                RoadEdge edges = previousEntry | exit;
                if (edges != RoadEdge.None)
                    result.Add(new RoadPlanStep(getTileByCoords(u, v).Id, edges));

                if (u == endU && v == endV) break;

                previousEntry = Opposite(exit);
                u = nextU;
                v = nextV;
            }

            return result;
        }

        private static RoadEdge Opposite(RoadEdge edge) => edge switch
        {
            RoadEdge.WS => RoadEdge.EN,
            RoadEdge.SE => RoadEdge.NW,
            RoadEdge.EN => RoadEdge.WS,
            RoadEdge.NW => RoadEdge.SE,
            _ => RoadEdge.None
        };

        private static RoadEdge EdgeToNeighbor(int u, int v, int neighborU, int neighborV)
        {
            if (neighborU == u + 1 && neighborV == v) return RoadEdge.SE;
            if (neighborU == u - 1 && neighborV == v) return RoadEdge.NW;
            if (neighborU == u && neighborV == v + 1) return RoadEdge.EN;
            if (neighborU == u && neighborV == v - 1) return RoadEdge.WS;
            return RoadEdge.None;
        }

        internal static Vector3 Corner(Node n) => new Vector3(n.xPos, n.yPos, n.zPos);

        // ── Földmű (platform-rézsű): lokális, csempénként ───────────────────────
        // Az út a befagyasztott vezetőfelületen ül. Ahol a szomszéd NEM út ÉS a terep a
        // felület alá süllyedt, ott a csempe NYITOTT éle fél csempényi, legfeljebb 1:1
        // rézsűvel csatlakozik a terephez (terep-stílusú lap). A megosztott (út-szomszéd)
        // éleken nincs rézsű → folytonos platform; ezeken az út-lábnyom sem húzódik be.

        // Egy él behúzási hányada [0..1]: a rés (felület−terep) / fél csempe, 1:1-ig.
        // 0, ha az él megosztott (szomszéd is út) vagy nincs rés.

        internal static int CountEdges(RoadEdge edges)
        {
            int count = 0;
            if ((edges & RoadEdge.WS) != 0) count++;
            if ((edges & RoadEdge.SE) != 0) count++;
            if ((edges & RoadEdge.EN) != 0) count++;
            if ((edges & RoadEdge.NW) != 0) count++;
            return count;
        }
    }
}
