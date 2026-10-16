using System;
using System.Collections.Generic;

namespace ForesTycoon.Map
{
    /// <summary>
    /// GL-mentes terep-modell: rács, node-ok, tile-ok és a tisztán topológiai
    /// koordináta-lekérdezések. Semmilyen OpenGL hívást nem tartalmaz, így
    /// önállóan tesztelhető és túléli a renderer cseréjét.
    /// </summary>
    sealed class TerrainData
    {
        public TerrainSettings Settings { get; }

        public int NodeCols { get; }
        public int NodeRows { get; }
        public int TileSizeH { get; }
        public int TileSizeV { get; }
        public int TileSizeM { get; }
        public int OffsetX { get; }
        public int OffsetY { get; }

        public Node[] Nodes { get; private set; }
        public Tile[] Tiles { get; private set; }
        private Node[] nodeNeighbours;
        private byte[] neighbourCounts, nodeTileCounts, adjacentCounts;
        private Tile[] nodeTiles;
        private Tile[] adjacentTiles;

        public TerrainData(TerrainSettings settings, bool deferInitialization = false)
        {
            Settings = settings;
            NodeCols = settings.NodeColumns;
            NodeRows = settings.NodeRows;
            TileSizeH = settings.TileWidth;
            TileSizeV = settings.TileHeight;
            TileSizeM = settings.HeightScale;
            OffsetX = settings.OffsetX;
            OffsetY = settings.OffsetY;

            if (!deferInitialization) SimulationWork.Complete(InitializeWork());
        }

        internal IEnumerable<int> InitializeWork()
        {
            foreach (int unit in BuildNodesWork()) yield return unit;
            foreach (int unit in BuildTilesWork()) yield return unit;
            foreach (int unit in BuildTopologyCacheWork()) yield return unit;
        }

        private IEnumerable<int> BuildNodesWork()
        {
            Nodes = new Node[NodeCols * NodeRows];
            for (int u = 0; u < NodeCols; u++)
            {
                for (int v = 0; v < NodeRows; v++)
                {
                    yield return 1;
                    Node node = new Node(u * NodeRows + v)
                    {
                        U = u,
                        V = v,
                        W = 0,
                        xPos = (u * TileSizeH) - OffsetX,
                        yPos = (v * TileSizeV) - OffsetY,
                        zPos = 0
                    };
                    Nodes[node.Id] = node;
                }
            }
        }

        private IEnumerable<int> BuildTilesWork()
        {
            Tiles = new Tile[(NodeCols - 1) * (NodeRows - 1)];
            for (int u = 0; u < NodeCols - 1; u++)
            {
                for (int v = 0; v < NodeRows - 1; v++)
                {
                    yield return 1;
                    Node n = GetNode(u + 0, v + 1);
                    Node s = GetNode(u + 1, v + 0);
                    Node e = GetNode(u + 1, v + 1);
                    Node w = GetNode(u + 0, v + 0);

                    Tile tile = new Tile(n, s, e, w) { Id = u * (NodeRows - 1) + v };
                    Tiles[tile.Id] = tile;
                }
            }
        }

        private IEnumerable<int> BuildTopologyCacheWork()
        {
            nodeNeighbours = new Node[Nodes.Length * 4]; neighbourCounts = new byte[Nodes.Length];
            nodeTiles = new Tile[Nodes.Length * 4]; nodeTileCounts = new byte[Nodes.Length];
            adjacentTiles = new Tile[Tiles.Length * 4]; adjacentCounts = new byte[Tiles.Length];

            for (int i = 0; i < Nodes.Length; i++)
            {
                yield return 1;
                Node node = Nodes[i];
                int count = 0;
                if (CheckNode(node.U, node.V - 1)) nodeNeighbours[i * 4 + count++] = GetNode(node.U, node.V - 1);
                if (CheckNode(node.U + 1, node.V)) nodeNeighbours[i * 4 + count++] = GetNode(node.U + 1, node.V);
                if (CheckNode(node.U, node.V + 1)) nodeNeighbours[i * 4 + count++] = GetNode(node.U, node.V + 1);
                if (CheckNode(node.U - 1, node.V)) nodeNeighbours[i * 4 + count++] = GetNode(node.U - 1, node.V);
                neighbourCounts[i] = (byte)count;

                count = 0;
                if (CheckTile(node.U - 1, node.V - 1)) nodeTiles[i * 4 + count++] = GetTile(node.U - 1, node.V - 1);
                if (CheckTile(node.U - 1, node.V - 0)) nodeTiles[i * 4 + count++] = GetTile(node.U - 1, node.V - 0);
                if (CheckTile(node.U - 0, node.V - 1)) nodeTiles[i * 4 + count++] = GetTile(node.U - 0, node.V - 1);
                if (CheckTile(node.U - 0, node.V - 0)) nodeTiles[i * 4 + count++] = GetTile(node.U - 0, node.V - 0);
                nodeTileCounts[i] = (byte)count;
            }

            for (int i = 0; i < Tiles.Length; i++)
            {
                yield return 1;
                Tile tile = Tiles[i];
                int tilesPerColumn = NodeRows - 1;
                int u = tile.Id / tilesPerColumn;
                int v = tile.Id % tilesPerColumn;
                int count = 0;
                if (CheckTile(u - 1, v)) adjacentTiles[i * 4 + count++] = GetTile(u - 1, v);
                if (CheckTile(u + 1, v)) adjacentTiles[i * 4 + count++] = GetTile(u + 1, v);
                if (CheckTile(u, v - 1)) adjacentTiles[i * 4 + count++] = GetTile(u, v - 1);
                if (CheckTile(u, v + 1)) adjacentTiles[i * 4 + count++] = GetTile(u, v + 1);
                adjacentCounts[i] = (byte)count;
            }
        }

        // ── Koordináta-lekérdezések ──────────────────────────────────────────
        public Node GetNode(int u, int v) => Nodes[u * NodeRows + v];

        public Tile GetTile(int u, int v) => Tiles[u * (NodeRows - 1) + v];

        public bool CheckNode(int u, int v) =>
            u >= 0 && u < NodeCols && v >= 0 && v < NodeRows;

        public bool CheckTile(int u, int v) =>
            u >= 0 && u < NodeCols - 1 && v >= 0 && v < NodeRows - 1;

        public ReadOnlySpan<Node> GetNeighbours(Node node) => nodeNeighbours.AsSpan(node.Id * 4, neighbourCounts[node.Id]);

        public int GetNeighbours(Node node, Span<Node> buffer)
        {
            var cached = GetNeighbours(node);
            cached.CopyTo(buffer);
            return cached.Length;
        }

        public ReadOnlySpan<Tile> GetTilesByNode(Node node) => nodeTiles.AsSpan(node.Id * 4, nodeTileCounts[node.Id]);

        public int GetTilesByNode(Node node, Span<Tile> buffer)
        {
            var cached = GetTilesByNode(node);
            cached.CopyTo(buffer);
            return cached.Length;
        }

        public ReadOnlySpan<Tile> GetAdjacentTiles(Tile tile) => adjacentTiles.AsSpan(tile.Id * 4, adjacentCounts[tile.Id]);

        public int GetAdjacentTiles(Tile tile, Span<Tile> buffer)
        {
            var cached = GetAdjacentTiles(tile);
            cached.CopyTo(buffer);
            return cached.Length;
        }

        public bool IsBorderTile(Tile tile)
        {
            int tilesPerColumn = NodeRows - 1;
            int u = tile.Id / tilesPerColumn;
            int v = tile.Id % tilesPerColumn;
            return u == 0 || v == 0 || u == NodeCols - 2 || v == NodeRows - 2;
        }

        public static bool TileContainsNode(Tile tile, Node node) =>
            tile.W.Id == node.Id || tile.S.Id == node.Id ||
            tile.E.Id == node.Id || tile.N.Id == node.Id;

        public int GetSharedTiles(Node a, Node b, Span<Tile> buffer)
        {
            int count = 0;
            var cached = GetTilesByNode(a);
            for (int i = 0; i < cached.Length; i++)
            {
                Tile tile = cached[i];
                if (TileContainsNode(tile, b)) buffer[count++] = tile;
            }
            return count;
        }
    }
}
