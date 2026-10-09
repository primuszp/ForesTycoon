using System;
using System.Collections.Generic;
using OpenTK.Mathematics;

namespace ForesTycoon.Map
{
    /// <summary>
    /// The terrain as data and rules, with no drawing. It owns the height grid (nodes/tiles), surface
    /// water and moisture, the road network and its frozen road surfaces, building footprints, the
    /// editing cursor and the ground queries (picking, surface height). It is the habitat the ecosystem
    /// grows on (<see cref="IForestHabitat"/>) and the ground that a renderer draws; renderers follow it
    /// through <see cref="NodesChanged"/>, <see cref="EditsFlushed"/>, <see cref="RoadDiagonalsChanged"/>
    /// and <see cref="SurfaceVersion"/> rather than the map knowing about them.
    /// </summary>
    internal sealed partial class TerrainMap : IForestHabitat
    {
        private readonly TerrainSettings settings;
        private readonly TerrainData data;
        private readonly Hydrology hydro;
        private readonly RoadNetwork roads = new RoadNetwork();
        private readonly TerrainChunkIndex chunkIndex;
        // Kanyar belső sarokcsempe: a normál átló-irány rossz élt ad, ezért flip-verzióban rendereljük.
        private readonly HashSet<int> flippedDiagonalTiles = new HashSet<int>();
        private readonly HashSet<int> buildingTiles = new HashSet<int>();
        private bool suppressHydrologyRebuild;
        private bool generatingTerrain;
        // Foundation-réteg: az út VEZETŐFELÜLETÉNEK befagyasztott magassága sarkonként
        // (nodeId → W az építés pillanatában). A terep alatta szabadon alakítható, de az
        // út felülete itt marad; a kettő közti rést a foundation-fal tölti ki (OpenTTD-elv).
        private readonly Dictionary<int, int> roadSurfaceW = new Dictionary<int, int>();

        /// <summary>Raised after the heights of these nodes changed (derived data is already updated).</summary>
        internal event Action<IReadOnlyList<Node>> NodesChanged;
        /// <summary>Raised when a batch of height edits is complete and a renderer should upload.</summary>
        internal event Action EditsFlushed;
        /// <summary>Raised after the set of road-flipped diagonal tiles was recomputed.</summary>
        internal event Action RoadDiagonalsChanged;

        private Node[] nodes => data.Nodes;
        private Tile[] tiles => data.Tiles;
        private int nodeRows => data.NodeRows;
        private int nodeCols => data.NodeCols;
        private int tileSizeM => data.TileSizeM;
        private float SeaLevel => settings.SeaLevel;

        public TerrainMap() : this(TerrainSettings.Default) { }

        public TerrainMap(TerrainSettings settings, Func<int, int, int> initialHeight = null)
        {
            this.settings = settings ?? throw new ArgumentNullException(nameof(settings));
            data = new TerrainData(settings);
            hydro = new Hydrology(data, settings);
            chunkIndex = new TerrainChunkIndex(data);

            if (initialHeight == null) GenerateTerrain();
            else
            {
                foreach (Node node in nodes)
                    node.W = Math.Clamp(initialHeight(node.U, node.V), 0, settings.MaxHeight);
                ApplyNodeChanges(new List<Node>(nodes));
            }
        }

        // ── Read-only model surface ─────────────────────────────────────────────────
        internal TerrainSettings Settings => settings;
        internal TerrainData Data => data;
        internal RoadNetwork Roads => roads;
        internal TerrainChunkIndex Chunks => chunkIndex;
        internal Hydrology Hydrology => hydro;
        public IReadOnlyList<Tile> Tiles => tiles;
        public IReadOnlyList<Node> Nodes => nodes;
        public int TileWidth => data.TileSizeH;
        public int TileHeight => data.TileSizeV;
        public int TileSizeM => tileSizeM;
        internal int TilesPerSide => nodeRows - 1;
        public int TotalChunkCount => chunkIndex.Chunks.Count;
        internal IReadOnlySet<int> FlippedDiagonalTiles => flippedDiagonalTiles;
        internal bool IsDiagonalFlipped(int tileId) => flippedDiagonalTiles.Contains(tileId);
        internal bool SuppressesRebuild => suppressHydrologyRebuild;

        private Tile getTileByCoords(int u, int v) => data.GetTile(u, v);
        private bool checkTile(int u, int v) => data.CheckTile(u, v);
        internal Node GetNode(int u, int v) => data.GetNode(u, v);
        internal Tile GetTile(int u, int v) => data.GetTile(u, v);
        internal bool CheckTile(int u, int v) => data.CheckTile(u, v);
        internal int CountRiverCorners(Tile tile) => hydro.CountRiverCorners(tile);
        internal bool HasDynamicWater(Tile tile) => hydro.HasDynamicWater(tile);
        internal bool ShouldDrawStandingWater(Tile tile) => hydro.ShouldDrawStandingWater(tile);
        internal bool IsValidTileId(int tileId) => tileId >= 0 && tileId < tiles.Length;

        private void GenerateTerrain()
        {
            int maxHeight = settings.MaxHeight;
            new TerrainGenerator(settings.Seed)
                .Generate(nodeCols, nodeRows, maxHeight, out int[,] targetW, out bool[,] isRiver);

            // ── ElevationManager – szomszéd-meredekség szabály ────────────────
            suppressHydrologyRebuild = true;
            generatingTerrain = true;
            try
            {
                for (int pass = 0; pass < maxHeight; pass++)
                    for (int u = 0; u < nodeCols; u++)
                        for (int v = 0; v < nodeRows; v++)
                        {
                            Node node = GetNode(u, v);
                            if (node.W < targetW[u, v]) RaiseOrLower(node, +1);
                        }
                // No renderer or ecosystem observes construction. Derive tile geometry
                // once from the completed height grid instead of after every raise.
                ApplyNodeChanges(new List<Node>(nodes));
            }
            finally
            {
                generatingTerrain = false;
                suppressHydrologyRebuild = false;
            }

            // ── River node-ok megjelölése ─────────────────────────────────────
            hydro.RiverNodeIds.Clear();
            for (int u = 0; u < nodeCols; u++)
                for (int v = 0; v < nodeRows; v++)
                    if (isRiver[u, v])
                        hydro.RiverNodeIds.Add(GetNode(u, v).Id);

            RebuildHydrology();
        }

        private void RebuildHydrology()
        {
            var previousWater = new bool[tiles.Length];
            for (int id = 0; id < tiles.Length; id++)
                previousWater[id] = ShouldDrawStandingWater(tiles[id]);
            // A hidrológia node.zPos-t használ; mielőtt fut, MINDEN node zPos-át a friss
            // magasságból állítjuk be, hogy se betöltéskor, se szerkesztés után ne a régi
            // (stale) érték alapján higgyen vizet a magas terepre.
            foreach (Node node in data.Nodes)
                node.zPos = node.W * tileSizeM;

            hydro.Rebuild();
            for (int id = 0; id < tiles.Length; id++)
                if (previousWater[id] != ShouldDrawStandingWater(tiles[id]))
                    chunkIndex.MarkTileDirty(id, ChunkDirtyFlags.Water);
            InvalidateSurface();
        }

        /// <summary>Re-derives everything that depends on node heights after they changed.</summary>
        private void ApplyNodeChanges(List<Node> changed)
        {
            InvalidateSurface();
            Tile[] nodeTiles = new Tile[4];
            float[] nodeWaterDepth = hydro.NodeWaterDepth;
            foreach (Node node in changed)
            {
                node.zPos = node.W * tileSizeM;

                // Ha a terep emelkedett, a víz nem lebeghet a magasban; ha süllyedt, marad szárazon (majd folyik bele)
                if (nodeWaterDepth != null)
                    nodeWaterDepth[node.Id] = Math.Max(0f, nodeWaterDepth[node.Id]);

                int nodeTileCount = data.GetTilesByNode(node, nodeTiles);
                for (int i = 0; i < nodeTileCount; i++)
                {
                    Tile tile = nodeTiles[i];
                    elevationEditTiles?.Add(tile.Id);
                    chunkIndex.MarkTileDirty(tile.Id, ChunkDirtyFlags.All);
                    // Crown contact shadows can cross into the neighbouring tile.
                    chunkIndex.MarkTileAndNeighboursDirty(tile.Id, ChunkDirtyFlags.Props);
                    // Shape, minimum height and render offset are map facts. Refresh them
                    // before hydrology or followers read the tile, even without a renderer.
                    tile.getCode();
                    tile.LowPos = tile.Low * tileSizeM;
                }
            }

            NodesChanged?.Invoke(changed);
            if (!suppressHydrologyRebuild)
            {
                EditsFlushed?.Invoke();
                RebuildHydrology();
            }
        }

        // ── Habitat for the forest simulation ───────────────────────────────────────
        int IForestHabitat.TileCount => tiles.Length;
        int IForestHabitat.Seed => settings.Seed;

        bool IForestHabitat.CanSupportForest(int tileId)
        {
            if (!IsValidTileId(tileId) || roads.Has(tileId) || IsBuildingTile(tileId)) return false;
            Tile tile = tiles[tileId];
            return !data.IsBorderTile(tile)
                && !ShouldDrawStandingWater(tile)
                && CountRiverCorners(tile) < 2;
        }

        float IForestHabitat.GetMoisture(int tileId) => hydro.TileMoisture[tileId];
        ForestPattern IForestHabitat.ForestPattern => settings.ForestPattern;
        (int Columns, int Rows) IForestHabitat.TileGrid => (settings.TileColumns, settings.TileRows);
        ForestTileGeometry IForestHabitat.GetForestTileGeometry(int tileId) => new(
            tiles[tileId].W.xPos / WorldScale.MetresToWorld, tiles[tileId].W.yPos / WorldScale.MetresToWorld,
            data.TileSizeH / WorldScale.MetresToWorld, data.TileSizeV / WorldScale.MetresToWorld);

        float IForestHabitat.GetNormalizedElevation(int tileId) =>
            Math.Clamp(tiles[tileId].Low / (float)Math.Max(1, settings.MaxHeight), 0f, 1f);

        int IForestHabitat.GetAdjacentTileIds(int tileId, Span<int> destination)
        {
            if (!IsValidTileId(tileId)) return 0;
            ReadOnlySpan<Tile> adjacent = data.GetAdjacentTiles(tiles[tileId]);
            int count = Math.Min(adjacent.Length, destination.Length);
            for (int i = 0; i < count; i++) destination[i] = adjacent[i].Id;
            return count;
        }

        bool IForestHabitat.IsImpervious(int id) => IsRoadTile(id) || IsBuildingTile(id);
        bool IForestHabitat.IsWaterOutlet(int id) => IsEnvironmentWaterOutlet(id);

        internal bool IsEnvironmentWaterOutlet(int id) => data.IsBorderTile(tiles[id]) ||
            ShouldDrawStandingWater(tiles[id]) || CountRiverCorners(tiles[id]) >= 2;

        // ── Geometry queries ────────────────────────────────────────────────────────
        public bool TryGetNodePosition(int nodeId, out Vector3 position)
        {
            if (nodeId < 0 || nodeId >= nodes.Length)
            {
                position = Vector3.Zero;
                return false;
            }

            Node node = nodes[nodeId];
            position = new Vector3(node.xPos, node.yPos, node.zPos);
            return true;
        }

        public bool TryGetTileCenter(int tileId, out Vector3 position)
        {
            if (!IsValidTileId(tileId))
            {
                position = Vector3.Zero;
                return false;
            }

            Tile tile = tiles[tileId];
            position = new Vector3(
                (tile.W.xPos + tile.S.xPos + tile.E.xPos + tile.N.xPos) * 0.25f,
                (tile.W.yPos + tile.S.yPos + tile.E.yPos + tile.N.yPos) * 0.25f,
                Math.Max(Math.Max(tile.W.zPos, tile.S.zPos), Math.Max(tile.E.zPos, tile.N.zPos)));
            return true;
        }

        /// <summary>
        /// Fills <paramref name="destination"/> with the tile ids of the axis-aligned rectangle
        /// spanned by two corner tiles, and returns how many were written. The rectangle is
        /// clamped to <see cref="MaximumAreaSide"/> per side, so a stray drag across the whole
        /// map cannot turn into a single command that edits tens of thousands of tiles.
        /// </summary>
        public int GetTileRectangle(int startTileId, int endTileId, Span<int> destination)
        {
            if (!IsValidTileId(startTileId) || !IsValidTileId(endTileId)) return 0;

            int tilesPerColumn = settings.TileRows;
            int startU = startTileId / tilesPerColumn;
            int startV = startTileId % tilesPerColumn;
            int endU = endTileId / tilesPerColumn;
            int endV = endTileId % tilesPerColumn;

            int minU = Math.Min(startU, endU);
            int minV = Math.Min(startV, endV);
            int maxU = Math.Min(Math.Max(startU, endU), minU + MaximumAreaSide - 1);
            int maxV = Math.Min(Math.Max(startV, endV), minV + MaximumAreaSide - 1);

            int count = 0;
            for (int u = minU; u <= maxU; u++)
                for (int v = minV; v <= maxV; v++)
                {
                    if (count >= destination.Length) return count;
                    destination[count++] = u * tilesPerColumn + v;
                }

            return count;
        }

        /// <summary>Tiles a single area gesture may cover along one axis.</summary>
        public const int MaximumAreaSide = 24;

        /// <summary>Upper bound on the tiles one area command touches.</summary>
        public const int MaximumAreaTiles = MaximumAreaSide * MaximumAreaSide;
    }
}
