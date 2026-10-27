using System;
using System.Collections.Generic;
using System.Drawing;
using OpenTK.Mathematics;

namespace ForesTycoon
{
    /// <summary>
    /// The terrain <em>scene</em>: the GPU-side presentation of a <see cref="TerrainMap"/>. The map owns
    /// every fact and rule about the ground (heights, water, roads, buildings, picking, editing); this
    /// class owns only what is needed to draw it — vertex buffers, visible-chunk selection, the water,
    /// road, prop, forest and weather passes. It follows the map through its change events and never
    /// mutates the ground itself, so it can be replaced or run headless without touching the model.
    /// </summary>
    partial class Terrain : IDisposable
    {
        private readonly RenderEnvironment renderOwner = RenderDevice.Environment;
        private bool disposed;
        private readonly TerrainMap map;
        private readonly Dictionary<string, VertexBuffer> vbos = new Dictionary<string, VertexBuffer>();


        private static readonly Color RoadFoundationColor = Color.FromArgb(154, 120, 72);
        private static readonly Color RoadFoundationSlopeColor = Color.FromArgb(126, 88, 48);
        private static readonly Color RoadFoundationLineColor = Color.FromArgb(74, 43, 20);

        // The map's facts, under the short names the render partials grew up with.
        private TerrainSettings settings => map.Settings;
        private Hydrology hydro => map.Hydrology;
        private RoadNetwork roads => map.Roads;
        private TerrainChunkIndex chunkIndex => map.Chunks;
        private TerrainData data => map.Data;
        private Node[] nodes => data.Nodes;
        private Tile[] tiles => data.Tiles;
        private int nodeRows => data.NodeRows;
        private int nodeCols => data.NodeCols;
        private int tileSizeH => data.TileSizeH;
        private int tileSizeV => data.TileSizeV;
        private int tileSizeM => data.TileSizeM;
        private int offsetX => data.OffsetX;
        private int offsetY => data.OffsetY;
        private IReadOnlySet<int> flippedDiagonalTiles => map.FlippedDiagonalTiles;
        private HashSet<int> riverNodeIds => hydro.RiverNodeIds;
        private float[] tileMoisture => hydro.TileMoisture;
        private float[] nodeWaterDepth => hydro.NodeWaterDepth;
        private float MinimumWaterDepth => settings.MinimumWaterDepth;
        private float RiverWaterHeight => settings.RiverWaterHeight;
        private float SeaLevel => settings.SeaLevel;
        internal static void SurfacePoint(Tile tile, float u, float v, out float x, out float y, out float z) =>
            TerrainMap.SurfacePoint(tile, u, v, out x, out y, out z);
        private Node getNodeByCoords(int u, int v) => map.GetNode(u, v);
        private Tile getTileByCoords(int u, int v) => map.GetTile(u, v);
        private bool checkTile(int u, int v) => map.CheckTile(u, v);
        private int CountRiverCorners(Tile tile) => map.CountRiverCorners(tile);
        private bool HasDynamicWater(Tile tile) => map.HasDynamicWater(tile);
        private bool ShouldDrawStandingWater(Tile tile) => map.ShouldDrawStandingWater(tile);
        private TileSurface GetTileSurface(Tile tile) => map.GetTileSurface(tile);
        private TileSurfaceMaterial GetTileSurfaceMaterial(Tile tile) => map.GetTileSurfaceMaterial(tile);
        private bool UseTileDiagonalWE(Tile tile, TileSurface visual) => map.UseTileDiagonalWE(tile, visual);
        private Vector3 RoadCorner(Node n) => map.RoadCorner(n);
        private static Vector3 Corner(Tile tile, TileCorner corner) => TerrainMap.Corner(tile, corner);
        private bool TryGetFoundationFaceForRoadEdge(int u, int v, Node a, Node b, RoadEdge edge, out FoundationFaceData face) =>
            map.TryGetFoundationFaceForRoadEdge(u, v, a, b, edge, out face);

        /// <summary>The ground this scene draws.</summary>
        internal TerrainMap Map => map;

        public Terrain()
            : this(TerrainSettings.Default)
        {
        }

        public Terrain(TerrainSettings settings, Func<int, int, int> initialHeight = null)
            : this(new TerrainMap(settings, initialHeight))
        {
        }

        public Terrain(TerrainMap map, bool deferGeometry = false)
        {
            this.map = map ?? throw new ArgumentNullException(nameof(map));
            try
            {
                if (!deferGeometry) SimulationWork.Complete(PrepareBaseGeometryWork());
                map.NodesChanged += OnNodesChanged;
                map.RoadDiagonalPrepared += OnRoadDiagonalPrepared;
            }
            catch { Dispose(); throw; }
        }

        public int TotalChunkCount => chunkIndex.Chunks.Count;
        public int TileWidth => data.TileSizeH;
        public int TileHeight => data.TileSizeV;
        internal TerrainSettings Settings => settings;

        /// <summary>GL-erőforrások felszabadítása (regeneráláskor a régi terep buffereihez).</summary>
        public void Dispose() => SimulationWork.Complete(DisposeWork());

        internal IEnumerable<int> DisposeWork()
        {
            if (disposed) yield break; renderOwner.VerifyAccess();
            map.NodesChanged -= OnNodesChanged;
            map.RoadDiagonalPrepared -= OnRoadDiagonalPrepared;
            foreach (VertexBuffer vbo in vbos.Values) { vbo.Dispose(); yield return 1; }
            vbos.Clear();
            foreach (int unit in DisposeForestGeometryWork()) yield return unit;
            foreach (int unit in DisposeStaticTerrainWork()) yield return unit;
            fogSources.Clear(); fogSourceOrder.Clear();
            disposed = true;
        }
    }
}
