using System;
using System.Collections.Generic;

namespace ForesTycoon
{
    /// <summary>
    /// Vízállapot számítása a terep magasságából: álló víz (tó/tenger) BFS-medencékkel,
    /// gravitációs spill-szint (Dijkstra) per node, valamint a nedvesség-térkép.
    /// GL-mentes; a renderer csak olvassa a kimeneti kollekciókat és a prediktátumokat.
    /// </summary>
    sealed class Hydrology
    {
        private readonly TerrainData data;
        private readonly TerrainSettings settings;

        // ── Kimeneti állapot (a renderer ezeket olvassa) ─────────────────────
        public HashSet<int> RiverNodeIds { get; } = new HashSet<int>();
        public HashSet<int> StandingWaterTileIds { get; } = new HashSet<int>();
        public float[] TileMoisture { get; private set; }
        public float[] NodeWaterDepth { get; private set; }

        private float MinimumWaterDepth => settings.MinimumWaterDepth;
        private float RiverWaterHeight => settings.RiverWaterHeight;
        private float SeaLevel => settings.SeaLevel;
        private int TileSizeM => data.TileSizeM;

        public Hydrology(TerrainData data, TerrainSettings settings)
        {
            this.data = data;
            this.settings = settings;
            TileMoisture = new float[data.Tiles.Length];
        }

        // ── Teljes újraszámítás ──────────────────────────────────────────────
        public void Rebuild()
        {
            Tile[] tiles = data.Tiles;
            StandingWaterTileIds.Clear();
            Array.Clear(TileMoisture, 0, TileMoisture.Length);

            bool[] waterCandidates = new bool[tiles.Length];
            for (int i = 0; i < tiles.Length; i++)
                waterCandidates[i] = HasWaterSurfaceCandidate(tiles[i]);

            bool[] visited = new bool[tiles.Length];
            Queue<Tile> open = new Queue<Tile>();
            List<Tile> basin = new List<Tile>();

            for (int i = 0; i < tiles.Length; i++)
            {
                if (!waterCandidates[i] || visited[i]) continue;

                open.Enqueue(tiles[i]);
                visited[i] = true;
                basin.Clear();

                bool touchesBorder = false;
                while (open.Count > 0)
                {
                    Tile current = open.Dequeue();
                    basin.Add(current);
                    if (data.IsBorderTile(current)) touchesBorder = true;

                    foreach (Tile adjacent in data.GetAdjacentTiles(current))
                    {
                        if (!waterCandidates[adjacent.Id] || visited[adjacent.Id]) continue;
                        visited[adjacent.Id] = true;
                        open.Enqueue(adjacent);
                    }
                }

                if (!touchesBorder)
                {
                    foreach (Tile basinTile in basin)
                        StandingWaterTileIds.Add(basinTile.Id);
                }
            }

            for (int i = 0; i < tiles.Length; i++)
            {
                Tile tile = tiles[i];
                float moisture = ShouldDrawStandingWater(tile) ? 1.0f : 0.0f;

                int riverCorners = CountRiverCorners(tile);
                if (riverCorners >= 2) moisture = Math.Max(moisture, 0.8f);

                foreach (Tile adjacent in data.GetAdjacentTiles(tile))
                {
                    if (ShouldDrawStandingWater(adjacent))
                        moisture = Math.Max(moisture, 0.6f);
                    else if (CountRiverCorners(adjacent) >= 2)
                        moisture = Math.Max(moisture, 0.45f);
                }

                if (tile.Low <= 2) moisture = Math.Max(moisture, 0.35f);
                TileMoisture[i] = moisture;
            }

            BuildGravityWaterFromTerrain();
        }

        private void BuildGravityWaterFromTerrain()
        {
            Node[] nodes = data.Nodes;
            NodeWaterDepth = new float[nodes.Length];

            bool[] settled = new bool[nodes.Length];
            float[] spillLevel = new float[nodes.Length];
            for (int i = 0; i < spillLevel.Length; i++)
                spillLevel[i] = float.PositiveInfinity;

            PriorityQueue<Node, float> open = new PriorityQueue<Node, float>();

            void EnqueueIfLower(Node node, float level)
            {
                if (level >= spillLevel[node.Id]) return;
                spillLevel[node.Id] = level;
                open.Enqueue(node, level);
            }

            void AddBoundary(Node node)
            {
                float boundaryLevel = node.zPos < SeaLevel ? SeaLevel : node.zPos;
                EnqueueIfLower(node, boundaryLevel);
            }

            int nodeCols = data.NodeCols, nodeRows = data.NodeRows;
            for (int u = 0; u < nodeCols; u++)
            {
                AddBoundary(data.GetNode(u, 0));
                AddBoundary(data.GetNode(u, nodeRows - 1));
            }
            for (int v = 1; v < nodeRows - 1; v++)
            {
                AddBoundary(data.GetNode(0, v));
                AddBoundary(data.GetNode(nodeCols - 1, v));
            }

            while (open.Count > 0)
            {
                Node node = open.Dequeue();
                if (settled[node.Id]) continue;

                settled[node.Id] = true;
                float currentLevel = spillLevel[node.Id];

                foreach (Node neighbor in data.GetNeighbours(node))
                {
                    if (settled[neighbor.Id]) continue;

                    float edgeLevel = GetEdgeBarrierLevel(node, neighbor);
                    float neighborLevel = Math.Max(currentLevel, Math.Max(edgeLevel, neighbor.zPos));
                    EnqueueIfLower(neighbor, neighborLevel);
                }
            }

            for (int i = 0; i < nodes.Length; i++)
            {
                if (float.IsPositiveInfinity(spillLevel[i])) continue;

                float surfaceLevel = Math.Min(spillLevel[i], SeaLevel);
                float depth = surfaceLevel - nodes[i].zPos;
                if (depth >= MinimumWaterDepth)
                    NodeWaterDepth[i] = depth;
            }
        }

        // ── Prediktátumok ────────────────────────────────────────────────────
        public int CountRiverCorners(Tile tile)
        {
            int count = 0;
            if (RiverNodeIds.Contains(tile.W.Id)) count++;
            if (RiverNodeIds.Contains(tile.S.Id)) count++;
            if (RiverNodeIds.Contains(tile.E.Id)) count++;
            if (RiverNodeIds.Contains(tile.N.Id)) count++;
            return count;
        }

        public bool HasDynamicWater(Tile tile)
        {
            return NodeWaterDepth[tile.N.Id] >= MinimumWaterDepth
                || NodeWaterDepth[tile.S.Id] >= MinimumWaterDepth
                || NodeWaterDepth[tile.E.Id] >= MinimumWaterDepth
                || NodeWaterDepth[tile.W.Id] >= MinimumWaterDepth;
        }

        public float GetEdgeBarrierLevel(Node a, Node b)
        {
            float edgeLevel = Math.Max(a.zPos, b.zPos) + 0.001f;

            foreach (Tile tile in data.GetSharedTiles(a, b))
            {
                if (!IsRiverWaterTile(tile)) continue;
                edgeLevel = Math.Min(edgeLevel, tile.Low * TileSizeM + RiverWaterHeight + 0.001f);
            }

            return edgeLevel;
        }

        private int CountCornersBelowWater(Tile tile)
        {
            int count = 0;
            if (tile.W.zPos < SeaLevel) count++;
            if (tile.S.zPos < SeaLevel) count++;
            if (tile.E.zPos < SeaLevel) count++;
            if (tile.N.zPos < SeaLevel) count++;
            return count;
        }

        private bool HasAdjacentWetBankPair(Tile tile)
        {
            bool westWet = tile.W.zPos < SeaLevel;
            bool southWet = tile.S.zPos < SeaLevel;
            bool eastWet = tile.E.zPos < SeaLevel;
            bool northWet = tile.N.zPos < SeaLevel;

            return (westWet && southWet)
                || (southWet && eastWet)
                || (eastWet && northWet)
                || (northWet && westWet);
        }

        private bool HasWaterSurfaceCandidate(Tile tile)
        {
            if (CountRiverCorners(tile) >= 2) return false;

            bool hasHigherBank = false;
            foreach (Tile adjacent in data.GetAdjacentTiles(tile))
            {
                if (adjacent.Low < tile.Low) return false;
                if (adjacent.Low > tile.Low) hasHigherBank = true;
            }

            return hasHigherBank;
        }

        public bool ShouldDrawStandingWater(Tile tile)
        {
            return StandingWaterTileIds.Contains(tile.Id) || tile.Low < 0
                || (NodeWaterDepth != null && HasDynamicWater(tile));
        }

        private bool HasAdjacentRiverBankPair(Tile tile)
        {
            bool westRiver = RiverNodeIds.Contains(tile.W.Id);
            bool southRiver = RiverNodeIds.Contains(tile.S.Id);
            bool eastRiver = RiverNodeIds.Contains(tile.E.Id);
            bool northRiver = RiverNodeIds.Contains(tile.N.Id);

            return (westRiver && southRiver)
                || (southRiver && eastRiver)
                || (eastRiver && northRiver)
                || (northRiver && westRiver);
        }

        public bool CanRenderFallbackRiver(Tile tile)
        {
            if (!IsRiverWaterTile(tile)) return false;
            if (tile.Low * TileSizeM >= SeaLevel) return false;
            return CountCornersBelowWater(tile) >= 2 && HasAdjacentWetBankPair(tile);
        }

        public bool IsRiverWaterTile(Tile tile)
        {
            if (StandingWaterTileIds.Contains(tile.Id)) return false;
            int rc = CountRiverCorners(tile);
            if (rc < 2) return false;
            if (rc == 2 && !HasAdjacentRiverBankPair(tile)) return false;
            return true;
        }
    }
}
