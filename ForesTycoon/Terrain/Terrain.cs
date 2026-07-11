using System;
using System.Collections.Generic;
using System.Drawing;
using OpenTK.Mathematics;
using OpenTK.Graphics.OpenGL;

namespace ForesTycoon
{
    partial class Terrain
    {
        private readonly TerrainSettings settings;
        private Hydrology hydro;
        private HashSet<int> riverNodeIds => hydro.RiverNodeIds;
        private HashSet<int> standingWaterTileIds => hydro.StandingWaterTileIds;
        private readonly Dictionary<string, VertexBuffer> vbos = new Dictionary<string, VertexBuffer>();
        // Kanyar belső sarokcsempe: a normál átló-irány rossz élt ad, ezért flip-verzióban rendereljük.
        private readonly HashSet<int> flippedDiagonalTiles = new HashSet<int>();
        private readonly VertexBuffer edges = new VertexBuffer(PrimitiveType.Lines, BufferUsageHint.DynamicDraw);
        private readonly RoadNetwork roads = new RoadNetwork();
        private readonly RenderPipeline renderPipeline = new RenderPipeline();
        private readonly TerrainChunkIndex chunkIndex;
        private readonly List<Tile> visibleTiles = new List<Tile>();
        private readonly HashSet<int> visibleTileIds = new HashSet<int>();
        private int visibleChunkCount;
        private RenderStateScope terrainDecalState;

        // Foundation-réteg: az út VEZETŐFELÜLETÉNEK befagyasztott magassága sarkonként
        // (nodeId → W az építés pillanatában). A terep alatta szabadon alakítható, de az
        // út felülete itt marad; a kettő közti rést a foundation-fal tölti ki (OpenTTD-elv).
        private readonly Dictionary<int, int> roadSurfaceW = new Dictionary<int, int>();
        private static readonly Color RoadFoundationColor = Color.FromArgb(154, 120, 72);
        private static readonly Color RoadFoundationSlopeColor = Color.FromArgb(126, 88, 48);
        private static readonly Color RoadFoundationLineColor = Color.FromArgb(74, 43, 20);
        private static readonly Color TerrainTopColor = Color.FromArgb(141, 184, 75);  // fű (terep tető)
        private readonly List<uint> indices = new List<uint>();

        private readonly TerrainData data;
        private Node[] nodes => data.Nodes;
        private Tile[] tiles => data.Tiles;

        private int nodeRows => data.NodeRows;
        private int nodeCols => data.NodeCols;

        private int tileSizeH => data.TileSizeH;
        private int tileSizeV => data.TileSizeV;
        private int tileSizeM => data.TileSizeM;

        private int offsetX => data.OffsetX;
        private int offsetY => data.OffsetY;

        private bool onpos = false;
        private Node actualNode;
        private Tile hoveredTile = null;

        private float[] tileMoisture => hydro.TileMoisture;
        private bool suppressHydrologyRebuild = false;

        private float[] nodeWaterDepth => hydro.NodeWaterDepth;
        private Vertex[] vertices = null;

        private float MinimumWaterDepth => settings.MinimumWaterDepth;
        private float RiverWaterHeight => settings.RiverWaterHeight;
        private float SeaLevel => settings.SeaLevel;

        public Terrain()
            : this(TerrainSettings.Default)
        {
        }

        public Terrain(TerrainSettings settings)
        {
            this.settings = settings ?? throw new ArgumentNullException(nameof(settings));
            data = new TerrainData(settings);
            hydro = new Hydrology(data, settings);
            chunkIndex = new TerrainChunkIndex(data);

            makeTiles();
            makeQuads();
            BuildRenderPipeline();

            GenerateTerrain();
        }

        private void BuildRenderPipeline()
        {
            renderPipeline.Add(RenderLayer.TerrainBase, "terrain-base", _ => DrawTerrainBase());
            renderPipeline.Add(RenderLayer.TerrainSkirts, "terrain-skirts", _ => DrawSkirts());
            renderPipeline.Add(RenderLayer.WaterSurface, "water-surface", context => DrawWater(context));
            renderPipeline.Add(RenderLayer.WaterWalls, "water-walls", context => DrawWaterWalls(context));
            renderPipeline.Add(RenderLayer.RiverFallback, "river-fallback", context => DrawRivers(context));
            renderPipeline.Add(RenderLayer.Foundations, "road-foundations", _ => DrawRoadFoundations());
            renderPipeline.Add(RenderLayer.DecalBegin, "decal-state-begin", _ => BeginTerrainDecals());
            renderPipeline.Add(RenderLayer.Grid, "terrain-grid", _ => DrawTerrainDecals());
            renderPipeline.Add(RenderLayer.Roads, "roads", _ => DrawRoads());
            renderPipeline.Add(RenderLayer.HoverOverlay, "hover-overlay", context =>
            {
                if (context.ShowTileHighlight) DrawHoveredTile();
            });
            renderPipeline.Add(RenderLayer.DecalEnd, "decal-state-end", _ => EndTerrainDecals());
            renderPipeline.Add(RenderLayer.Props, "props", _ => DrawTrees());
            renderPipeline.Add(RenderLayer.DebugOverlay, "debug-overlay", context =>
            {
                if (context.ShowNodeMarker) DrawNodeMarker(context.NodeMarkerRadius);
            });
        }

        private void GenerateTerrain()
        {
            int maxHeight = settings.MaxHeight;
            new TerrainGenerator(settings.Seed)
                .Generate(nodeCols, nodeRows, maxHeight, out int[,] targetW, out bool[,] isRiver);

            // ── ElevationManager – szomszéd-meredekség szabály ────────────────
            suppressHydrologyRebuild = true;
            try
            {
                for (int pass = 0; pass < maxHeight; pass++)
                    for (int u = 0; u < nodeCols; u++)
                        for (int v = 0; v < nodeRows; v++)
                        {
                            Node node = getNodeByCoords(u, v);
                            if (node.W < targetW[u, v])
                            {
                                actualNode = node;
                                ElevationManager(+1);
                            }
                        }
            }
            finally
            {
                suppressHydrologyRebuild = false;
            }

            // ── River node-ok megjelölése ─────────────────────────────────────
            riverNodeIds.Clear();
            for (int u = 0; u < nodeCols; u++)
                for (int v = 0; v < nodeRows; v++)
                    if (isRiver[u, v])
                        riverNodeIds.Add(getNodeByCoords(u, v).Id);

            RebuildHydrology();
            actualNode = null;
        }



        private Node getNodeByCoords(int u, int v) => data.GetNode(u, v);

        private Tile getTileByCoords(int u, int v) => data.GetTile(u, v);

        private bool checkNode(int u, int v) => data.CheckNode(u, v);

        private bool checkTile(int u, int v) => data.CheckTile(u, v);

        private int CountRiverCorners(Tile tile) => hydro.CountRiverCorners(tile);

        private void RebuildHydrology()
        {
            // A hidrológia node.zPos-t használ; mielőtt fut, MINDEN node zPos-át a friss
            // magasságból állítjuk be, hogy se betöltéskor, se szerkesztés után ne a régi
            // (stale) érték alapján higgyen vizet a magas terepre.
            foreach (Node node in data.Nodes)
                node.zPos = node.W * tileSizeM;

            hydro.Rebuild();
        }

        private bool HasDynamicWater(Tile tile) => hydro.HasDynamicWater(tile);

        public void Draw(RenderContext context)
        {
            UpdateVisibleTiles(context);
            renderPipeline.Render(context);
        }

        public int VisibleChunkCount => visibleChunkCount;
        public int TotalChunkCount => chunkIndex.Chunks.Count;

        private bool IsTileVisible(int tileId) => visibleTileIds.Contains(tileId);

        private void UpdateVisibleTiles(RenderContext context)
        {
            visibleTiles.Clear();
            visibleTileIds.Clear();
            visibleChunkCount = 0;
            const double margin = 12.0;
            foreach (TerrainChunk chunk in chunkIndex.Chunks)
            {
                GetChunkViewBounds(chunk, context.CameraTilt, context.CameraYaw,
                    out float minX, out float minY, out float maxX, out float maxY);
                if (maxX < context.ViewMinX - margin || minX > context.ViewMaxX + margin
                    || maxY < context.ViewMinY - margin || minY > context.ViewMaxY + margin) continue;

                visibleChunkCount++;

                for (int i = 0; i < chunk.TileIds.Length; i++)
                {
                    int tileId = chunk.TileIds[i];
                    visibleTiles.Add(tiles[tileId]);
                    visibleTileIds.Add(tileId);
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
            terrainDecalState?.Dispose();
            terrainDecalState = null;
            foreach (VertexBuffer vbo in vbos.Values) vbo.Dispose();
            vbos.Clear();
            edges.Dispose();
        }
    }
}
