using System.Collections.Generic;

namespace ForesTycoon
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

        public TerrainData(TerrainSettings settings)
        {
            Settings = settings;
            NodeCols = settings.NodeColumns;
            NodeRows = settings.NodeRows;
            TileSizeH = settings.TileWidth;
            TileSizeV = settings.TileHeight;
            TileSizeM = settings.HeightScale;
            OffsetX = settings.OffsetX;
            OffsetY = settings.OffsetY;

            BuildNodes();
            BuildTiles();
        }

        private void BuildNodes()
        {
            Nodes = new Node[NodeCols * NodeRows];
            for (int u = 0; u < NodeCols; u++)
            {
                for (int v = 0; v < NodeRows; v++)
                {
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

        private void BuildTiles()
        {
            Tiles = new Tile[(NodeCols - 1) * (NodeRows - 1)];
            for (int u = 0; u < NodeCols - 1; u++)
            {
                for (int v = 0; v < NodeRows - 1; v++)
                {
                    Node n = GetNode(u + 0, v + 1);
                    Node s = GetNode(u + 1, v + 0);
                    Node e = GetNode(u + 1, v + 1);
                    Node w = GetNode(u + 0, v + 0);

                    Tile tile = new Tile(n, s, e, w) { Id = u * (NodeRows - 1) + v };
                    Tiles[tile.Id] = tile;
                }
            }
        }

        // ── Koordináta-lekérdezések ──────────────────────────────────────────
        public Node GetNode(int u, int v) => Nodes[u * NodeRows + v];

        public Tile GetTile(int u, int v) => Tiles[u * (NodeRows - 1) + v];

        public bool CheckNode(int u, int v) =>
            u >= 0 && u < NodeCols && v >= 0 && v < NodeRows;

        public bool CheckTile(int u, int v) =>
            u >= 0 && u < NodeCols - 1 && v >= 0 && v < NodeRows - 1;

        public List<Node> GetNeighbours(Node node)
        {
            List<Node> neighbors = new List<Node>(4);
            if (CheckNode(node.U, node.V - 1)) neighbors.Add(GetNode(node.U, node.V - 1));
            if (CheckNode(node.U + 1, node.V)) neighbors.Add(GetNode(node.U + 1, node.V));
            if (CheckNode(node.U, node.V + 1)) neighbors.Add(GetNode(node.U, node.V + 1));
            if (CheckNode(node.U - 1, node.V)) neighbors.Add(GetNode(node.U - 1, node.V));
            return neighbors;
        }

        public List<Tile> GetTilesByNode(Node node)
        {
            List<Tile> result = new List<Tile>(4);
            if (CheckTile(node.U - 1, node.V - 1)) result.Add(GetTile(node.U - 1, node.V - 1));
            if (CheckTile(node.U - 1, node.V - 0)) result.Add(GetTile(node.U - 1, node.V - 0));
            if (CheckTile(node.U - 0, node.V - 1)) result.Add(GetTile(node.U - 0, node.V - 1));
            if (CheckTile(node.U - 0, node.V - 0)) result.Add(GetTile(node.U - 0, node.V - 0));
            return result;
        }

        public IEnumerable<Tile> GetAdjacentTiles(Tile tile)
        {
            int tilesPerColumn = NodeRows - 1;
            int u = tile.Id / tilesPerColumn;
            int v = tile.Id % tilesPerColumn;

            if (CheckTile(u - 1, v)) yield return GetTile(u - 1, v);
            if (CheckTile(u + 1, v)) yield return GetTile(u + 1, v);
            if (CheckTile(u, v - 1)) yield return GetTile(u, v - 1);
            if (CheckTile(u, v + 1)) yield return GetTile(u, v + 1);
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

        public List<Tile> GetSharedTiles(Node a, Node b)
        {
            List<Tile> shared = new List<Tile>(2);
            foreach (Tile tile in GetTilesByNode(a))
                if (TileContainsNode(tile, b))
                    shared.Add(tile);
            return shared;
        }
    }
}
