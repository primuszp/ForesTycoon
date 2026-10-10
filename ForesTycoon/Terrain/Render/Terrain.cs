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
        private readonly List<Tile> visibleTiles = new List<Tile>();
        private readonly List<TerrainChunk> visibleChunks = new List<TerrainChunk>();
        private int visibleChunkCount;

        private static readonly Color RoadFoundationColor = Color.FromArgb(154, 120, 72);
        private static readonly Color RoadFoundationSlopeColor = Color.FromArgb(126, 88, 48);
        private static readonly Color RoadFoundationLineColor = Color.FromArgb(74, 43, 20);
        private static readonly Color TerrainTopColor = Color.FromArgb(141, 184, 75);  // fű (terep tető)

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
        private HashSet<int> standingWaterTileIds => hydro.StandingWaterTileIds;
        private float[] tileMoisture => hydro.TileMoisture;
        private float[] nodeWaterDepth => hydro.NodeWaterDepth;
        private float MinimumWaterDepth => settings.MinimumWaterDepth;
        private float RiverWaterHeight => settings.RiverWaterHeight;
        private float SeaLevel => settings.SeaLevel;
        internal static void SurfacePoint(Tile tile, float u, float v, out float x, out float y, out float z) =>
            TerrainMap.SurfacePoint(tile, u, v, out x, out y, out z);
        private Node getNodeByCoords(int u, int v) => map.GetNode(u, v);
        private Tile getTileByCoords(int u, int v) => map.GetTile(u, v);
        private bool checkNode(int u, int v) => data.CheckNode(u, v);
        private bool checkTile(int u, int v) => map.CheckTile(u, v);
        private int CountRiverCorners(Tile tile) => map.CountRiverCorners(tile);
        private bool HasDynamicWater(Tile tile) => map.HasDynamicWater(tile);
        private bool ShouldDrawStandingWater(Tile tile) => map.ShouldDrawStandingWater(tile);
        private bool IsValidTileId(int tileId) => map.IsValidTileId(tileId);
        private bool IsBuildingTile(int tileId) => map.IsBuildingTile(tileId);
        private TileSurface GetTileSurface(Tile tile) => map.GetTileSurface(tile);
        private TileSurfaceMaterial GetTileSurfaceMaterial(Tile tile) => map.GetTileSurfaceMaterial(tile);
        private bool UseTileDiagonalWE(Tile tile) => map.UseTileDiagonalWE(tile);
        private bool UseTileDiagonalWE(Tile tile, TileSurface visual) => map.UseTileDiagonalWE(tile, visual);
        private bool TileEdgeHasFoundation(Tile tile, TileSurface visual, RoadEdge edge) => map.TileEdgeHasFoundation(tile, visual, edge);
        private float RoadSurfaceZ(Node n) => map.RoadSurfaceZ(n);
        private Vector3 RoadCorner(Node n) => map.RoadCorner(n);
        private static Vector3 Corner(Node n) => TerrainMap.Corner(n);
        private static Vector3 Corner(Tile tile, TileCorner corner) => TerrainMap.Corner(tile, corner);
        private bool TryGetFoundationFaceForRoadEdge(int u, int v, Node a, Node b, RoadEdge edge, out FoundationFaceData face) =>
            map.TryGetFoundationFaceForRoadEdge(u, v, a, b, edge, out face);
        private bool TryGetFoundationFace(int u, int v, Node a, Node b, Node oa, Node ob, out FoundationFaceData face) =>
            map.TryGetFoundationFace(u, v, a, b, oa, ob, out face);

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

        public Terrain(TerrainMap map)
        {
            this.map = map ?? throw new ArgumentNullException(nameof(map));
            try
            {
                makeTiles();
                map.NodesChanged += OnNodesChanged;
                map.RoadDiagonalsChanged += OnRoadDiagonalsChanged;
            }
            catch { Dispose(); throw; }
        }

        public int VisibleChunkCount => visibleChunkCount;
        public int TotalChunkCount => chunkIndex.Chunks.Count;
        public int TileWidth => data.TileSizeH;
        public int TileHeight => data.TileSizeV;
        internal TerrainSettings Settings => settings;

        internal void UpdateVisibleTiles(RenderContext context)
        {
            visibleTiles.Clear();
            visibleChunks.Clear();
            visibleChunkCount = 0;
            const double margin = 12.0;
            foreach (TerrainChunk chunk in chunkIndex.Chunks)
            {
                GetChunkViewBounds(chunk, context.CameraTilt, context.CameraYaw,
                    out float minX, out float minY, out float maxX, out float maxY);
                if (maxX < context.ViewMinX - margin || minX > context.ViewMaxX + margin
                    || maxY < context.ViewMinY - margin || minY > context.ViewMaxY + margin) continue;

                visibleChunkCount++;
                visibleChunks.Add(chunk);

                for (int i = 0; i < chunk.TileIds.Length; i++)
                {
                    int tileId = chunk.TileIds[i];
                    visibleTiles.Add(tiles[tileId]);
                }
            }
        }

        private static void GetChunkViewBounds(TerrainChunk chunk, float tilt, float yaw,
            out float minX, out float minY, out float maxX, out float maxY)
        {
            minX = minY = float.MaxValue;
            maxX = maxY = float.MinValue;
            for (int x = 0; x < 2; x++) for (int y = 0; y < 2; y++) for (int z = 0; z < 2; z++)
            {
                Vector3 p = new Vector3(x == 0 ? chunk.Min.X : chunk.Max.X,
                    y == 0 ? chunk.Min.Y : chunk.Max.Y, z == 0 ? chunk.Min.Z : chunk.Max.Z);
                Vector3 view = WorldToView(p, tilt, yaw);
                minX = Math.Min(minX, view.X); minY = Math.Min(minY, view.Y);
                maxX = Math.Max(maxX, view.X); maxY = Math.Max(maxY, view.Y);
            }
        }

        private static Vector3 WorldToView(Vector3 point, float tiltDegrees, float yawDegrees)
        {
            double rz = yawDegrees * Math.PI / 180.0, rx = tiltDegrees * Math.PI / 180.0;
            double x = Math.Cos(rz) * point.X - Math.Sin(rz) * point.Y;
            double y = Math.Sin(rz) * point.X + Math.Cos(rz) * point.Y;
            return new Vector3((float)x,
                (float)(Math.Cos(rx) * y - Math.Sin(rx) * point.Z),
                (float)(Math.Sin(rx) * y + Math.Cos(rx) * point.Z));
        }






        /// <summary>GL-erőforrások felszabadítása (regeneráláskor a régi terep buffereihez).</summary>
        public void Dispose()
        {
            if (disposed) return; renderOwner.VerifyAccess();
            map.NodesChanged -= OnNodesChanged;
            map.RoadDiagonalsChanged -= OnRoadDiagonalsChanged;
            foreach (VertexBuffer vbo in vbos.Values) vbo.Dispose();
            vbos.Clear();
            DisposeForestGeometry();
            DisposeStaticTerrain();
            fogSources.Clear(); fogSourceOrder.Clear();
            disposed = true;
        }
    }
}
