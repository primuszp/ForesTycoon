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



        private void makeBuffer(string code, int low, bool flip = false)
        {
            string key = code + "_" + low + (flip ? "_f" : "");
            if (vbos.ContainsKey(key)) return;

            // Sarokmagasságok a kód alapján (N=0, E=1, S=2, W=3)
            float nZ = (float)char.GetNumericValue(code[0]) * tileSizeM;
            float eZ = (float)char.GetNumericValue(code[1]) * tileSizeM;
            float sZ = (float)char.GetNumericValue(code[2]) * tileSizeM;
            float wZ = (float)char.GetNumericValue(code[3]) * tileSizeM;

            Vector3 nPos = new Vector3(0,         tileSizeV, nZ);
            Vector3 ePos = new Vector3(tileSizeH, tileSizeV, eZ);
            Vector3 sPos = new Vector3(tileSizeH, 0,         sZ);
            Vector3 wPos = new Vector3(0,         0,         wZ);

            Vector3 cNor  = new Vector3(0, 0, 1);
            uint    color = ColorToUInt(Color.FromArgb(141, 184, 75));

            // Átlós felezés: amelyik átló végpontjai közelebb vannak egymáshoz
            // (laposabb átló), azt választjuk – simább felszín, kevesebb "tető-él"
            List<Vertex> data = new List<Vertex>();
            VertexBuffer vbo  = new VertexBuffer(PrimitiveType.Triangles);

            bool useWE = Math.Abs(wZ - eZ) <= Math.Abs(nZ - sZ);
            if (flip) useWE = !useWE;
            if (useWE)
            {
                // W–E átló: (W,S,E) + (W,E,N)
                data.Add(new Vertex(wPos, cNor, color));
                data.Add(new Vertex(sPos, cNor, color));
                data.Add(new Vertex(ePos, cNor, color));
                data.Add(new Vertex(wPos, cNor, color));
                data.Add(new Vertex(ePos, cNor, color));
                data.Add(new Vertex(nPos, cNor, color));
            }
            else
            {
                // N–S átló: (N,W,S) + (N,S,E)
                data.Add(new Vertex(nPos, cNor, color));
                data.Add(new Vertex(wPos, cNor, color));
                data.Add(new Vertex(sPos, cNor, color));
                data.Add(new Vertex(nPos, cNor, color));
                data.Add(new Vertex(sPos, cNor, color));
                data.Add(new Vertex(ePos, cNor, color));
            }

            fillColor(code, low, ref data);

            vbo.SetData(data.ToArray());
            vbos.Add(key, vbo);
        }

        private void fillColor(string code, int low, ref List<Vertex> data)
        {
            int sumH = 0;
            for (int i = 0; i < code.Length; i++) sumH += (int)char.GetNumericValue(code[i]);
            // t: relative slope steepness within the tile (0=flat, 1=max slope)
            float t = Math.Min(sumH / 8.0f, 1.0f);

            // Biome base colors by absolute elevation (low = tile's minimum corner W)
            Color baseLow, baseHigh;
            if (low <= 1)
            {
                // Homok / part
                baseLow  = Color.FromArgb(200, 178, 108);
                baseHigh = Color.FromArgb(222, 202, 138);
            }
            else if (low == 2)
            {
                // Friss fű – élénk zöld
                baseLow  = Color.FromArgb( 95, 148, 42);
                baseHigh = Color.FromArgb(145, 195, 68);
            }
            else if (low == 3)
            {
                // Magasabb fű – sárgásabb zöld
                baseLow  = Color.FromArgb(118, 158, 45);
                baseHigh = Color.FromArgb(168, 205, 72);
            }
            else if (low == 4)
            {
                // Száraz fű / legelő – sárgás-barna
                baseLow  = Color.FromArgb(155, 155, 55);
                baseHigh = Color.FromArgb(192, 185, 80);
            }
            else if (low == 5)
            {
                // Magas legelő / bokros – olajzöld-barna átmenet
                baseLow  = Color.FromArgb(138, 138, 72);
                baseHigh = Color.FromArgb(170, 162, 90);
            }
            else
            {
                // Legmagasabb csúcs – szikla/hó
                baseLow  = Color.FromArgb(175, 165, 148);
                baseHigh = Color.FromArgb(230, 228, 222);
            }

            // Light direction: slightly from above-front in world space
            Vector3 light = Vector3.Normalize(new Vector3(0.4f, 0.6f, 1.5f));

            // Minden primitív háromszög (stride = 3)
            int stride = 3;
            for (int start = 0; start < data.Count; start += stride)
            {
                Vector3 p0 = data[start    ].Position;
                Vector3 p1 = data[start + 1].Position;
                Vector3 p2 = data[start + 2].Position;
                Vector3 normal = Vector3.Normalize(Vector3.Cross(p1 - p0, p2 - p0));
                float shade = Math.Max(0.55f, Math.Min(1.0f, Vector3.Dot(normal, light)));

                int r = (int)((baseLow.R + (baseHigh.R - baseLow.R) * t) * shade);
                int g = (int)((baseLow.G + (baseHigh.G - baseLow.G) * t) * shade);
                int b = (int)((baseLow.B + (baseHigh.B - baseLow.B) * t) * shade);
                uint color = ColorToUInt(Color.FromArgb(255, r, g, b));

                int end = Math.Min(start + stride, data.Count);
                for (int i = start; i < end; i++)
                    data[i] = setColor(data[i], color);
            }
        }

        private Vertex setColor(Vertex vertex, uint color)
        {
            vertex.Color = color;
            return vertex;
        }


        private void makeQuads()
        {
            vertices = new Vertex[nodes.Length];

            // Rácsvonalak: halvány, visszafogott zöld
            uint gridColor = ColorToUInt(Color.FromArgb(82, 115, 38));

            for (int i = 0; i < nodes.Length; i++)
            {
                vertices[i] = new Vertex(
                    new Vector3(nodes[i].xPos, nodes[i].yPos, nodes[i].zPos),
                    Vector3.Zero,
                    gridColor);
            }

            foreach (Tile tile in tiles)
            {
                indices.Add((uint)tile.W.Id);
                indices.Add((uint)tile.S.Id);
                indices.Add((uint)tile.S.Id);
                indices.Add((uint)tile.E.Id);
                indices.Add((uint)tile.E.Id);
                indices.Add((uint)tile.N.Id);
                indices.Add((uint)tile.N.Id);
                indices.Add((uint)tile.W.Id);
            }

            edges.SetData(vertices);
            edges.SetElements(indices.ToArray());
        }

        private void makeTiles()
        {
            foreach (Tile tile in tiles)
                makeBuffer(tile.Code, tile.Low);
        }

        private void updateNodes(List<Node> nodes)
        {
            Tile[] nodeTiles = new Tile[4];
            foreach (Node node in nodes)
            {
                node.zPos = node.W * tileSizeM;
                vertices[node.Id].Position.Z = node.zPos;

                // Ha a terep emelkedett, a víz nem lebeghet a magasban; ha süllyedt, marad szárazon (majd folyik bele)
                if (nodeWaterDepth != null)
                    nodeWaterDepth[node.Id] = Math.Max(0f, nodeWaterDepth[node.Id]);

                int nodeTileCount = data.GetTilesByNode(node, nodeTiles);
                for (int i = 0; i < nodeTileCount; i++)
                {
                    Tile tile = nodeTiles[i];
                    string code = tile.getCode();
                    tile.LowPos = tile.Low * tileSizeM;
                    if (!vbos.ContainsKey(code + "_" + tile.Low))
                        makeBuffer(code, tile.Low);
                    if (flippedDiagonalTiles.Contains(tile.Id) && !vbos.ContainsKey(code + "_" + tile.Low + "_f"))
                        makeBuffer(code, tile.Low, true);
                }
            }

            edges.SetData(vertices);
            if (!suppressHydrologyRebuild)
                RebuildHydrology();
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

        private float NodeWaterSurfaceNoWave(Node node)
        {
            return node.zPos + nodeWaterDepth[node.Id];
        }

        private bool IsBelowWater(Node node)
        {
            return nodeWaterDepth[node.Id] >= MinimumWaterDepth;
        }

        private Vector3 IntersectWaterEdge(Node a, Node b)
        {
            float depthA = nodeWaterDepth[a.Id];
            float depthB = nodeWaterDepth[b.Id];
            float delta = depthB - depthA;
            float t = Math.Abs(delta) < 0.0001f
                ? 0.5f
                : (MinimumWaterDepth - depthA) / delta;
            t = Math.Max(0.0f, Math.Min(1.0f, t));

            float surfaceA = NodeWaterSurfaceNoWave(a);
            float surfaceB = NodeWaterSurfaceNoWave(b);
            float waterZ = surfaceA + (surfaceB - surfaceA) * t;

            return new Vector3(
                a.xPos + (b.xPos - a.xPos) * t,
                a.yPos + (b.yPos - a.yPos) * t,
                waterZ);
        }

        private void AddWaterPolygonPoint(List<Vector3> polygon, Vector3 point)
        {
            if (polygon.Count == 0)
            {
                polygon.Add(point);
                return;
            }

            Vector3 last = polygon[polygon.Count - 1];
            if ((last - point).LengthSquared < 0.0001f) return;

            polygon.Add(point);
        }

        private List<Vector3> BuildClippedWaterPolygon(Tile tile)
        {
            List<Vector3> polygon = new List<Vector3>(8);

            void AddEdge(Node start, Node end)
            {
                bool startWet = IsBelowWater(start);
                bool endWet = IsBelowWater(end);

                if (startWet)
                    AddWaterPolygonPoint(polygon, new Vector3(start.xPos, start.yPos, NodeWaterSurfaceNoWave(start)));

                if (startWet != endWet)
                    AddWaterPolygonPoint(polygon, IntersectWaterEdge(start, end));
            }

            AddEdge(tile.W, tile.S);
            AddEdge(tile.S, tile.E);
            AddEdge(tile.E, tile.N);
            AddEdge(tile.N, tile.W);

            if (polygon.Count > 1)
            {
                Vector3 first = polygon[0];
                Vector3 last = polygon[polygon.Count - 1];
                if ((first - last).LengthSquared < 0.0001f)
                    polygon.RemoveAt(polygon.Count - 1);
            }

            return polygon;
        }

        private uint ColorToUInt(Color color)
        {
            return ((uint)color.A << 24) | ((uint)color.B << 16) | ((uint)color.G << 8) | (uint)color.R;
        }

        /// <summary>Kettő szín lin. interpolációja t ∈ [0,1] alapján.</summary>
        private uint LerpColor(Color a, Color b, float t)
        {
            int r = (int)(a.R + (b.R - a.R) * t);
            int g = (int)(a.G + (b.G - a.G) * t);
            int bl= (int)(a.B + (b.B - a.B) * t);
            return ColorToUInt(Color.FromArgb(255, r, g, bl));
        }

        public void Draw(RenderContext context)
        {
            renderPipeline.Render(context);
        }





        // ── Út-render konstansok (referencia tile-készlet: szürke aszfalt + krém padka) ─
        private static readonly Color RoadSurfaceColor = Color.FromArgb(108, 110, 112);  // szürke úttest
        private static readonly Color RoadShoulder     = Color.FromArgb(214, 210, 190);  // világos krém padka
        private const float ShoulderFrac = 0.16f;  // padka szélessége a középpont felé

        // Csempe-alapú úthálózat: a kapcsolatok a szomszédos út-csempékből adódnak.
        private enum TileSlopeKind
        {
            Flat,
            OneCornerRaised,
            TwoAdjacentRaised,
            TwoOppositeRaised,
            ThreeCornersRaised,
            Steep
        }



        private enum RoadPlacementKind
        {
            Invalid,
            NaturalSurface,
            FoundationSurface
        }

        private readonly struct TileSlopeInfo
        {
            public readonly TileSlopeKind Kind;
            public readonly int Min;
            public readonly int Max;
            public readonly bool WRaised;
            public readonly bool SRaised;
            public readonly bool ERaised;
            public readonly bool NRaised;

            public TileSlopeInfo(TileSlopeKind kind, int min, int max, bool wRaised, bool sRaised, bool eRaised, bool nRaised)
            {
                Kind = kind;
                Min = min;
                Max = max;
                WRaised = wRaised;
                SRaised = sRaised;
                ERaised = eRaised;
                NRaised = nRaised;
            }
        }

        private readonly struct RoadPlacement
        {
            public readonly RoadPlacementKind Kind;
            public readonly int W;
            public readonly int S;
            public readonly int E;
            public readonly int N;

            public RoadPlacement(RoadPlacementKind kind, int w, int s, int e, int n)
            {
                Kind = kind;
                W = w;
                S = s;
                E = e;
                N = n;
            }

            public bool IsValid => Kind != RoadPlacementKind.Invalid;
        }

        private static readonly RoadPlacement InvalidRoadPlacement =
            new RoadPlacement(RoadPlacementKind.Invalid, 0, 0, 0, 0);

        public Tile HoveredTile => hoveredTile;
        public int RoadCount => roads.Count;

        public bool AddRoadTile(Tile t)
        {
            RoadEdge edges = RoadEdge.WS | RoadEdge.EN;
            RoadPlacement placement = AnalyzeRoadPlacement(t, edges);
            if (!placement.IsValid) return false;

            bool added = roads.Add(t.Id, edges);
            if (added) CaptureRoadSurface(t, placement);
            return added;
        }


        // OpenTTD-stílusú útépítés: az elemzés eldönti, hogy az út a természetes
        // terepre ülhet-e, vagy flat foundation vezetőfelületet kell befagyasztani.
        public bool IsRoadBuildable(Tile t) => IsRoadBuildable(t, RoadEdge.WS | RoadEdge.EN);

        private bool IsRoadBuildable(Tile t, RoadEdge edges)
        {
            return AnalyzeRoadPlacement(t, edges).IsValid;
        }

        private RoadPlacement AnalyzeRoadPlacement(Tile t, RoadEdge requestedEdges)
        {
            return AnalyzeRoadPlacement(t, requestedEdges, n => n.W);
        }

        private RoadPlacement AnalyzeRoadPlacement(Tile t, RoadEdge requestedEdges, Func<Node, int> heightOf)
        {
            if (t == null) return InvalidRoadPlacement;
            if (hydro.ShouldDrawStandingWater(t)) return InvalidRoadPlacement;

            RoadEdge mergedEdges = requestedEdges | roads.GetEdges(t.Id);
            if (mergedEdges == RoadEdge.None) return InvalidRoadPlacement;

            int w = heightOf(t.W);
            int s = heightOf(t.S);
            int e = heightOf(t.E);
            int n = heightOf(t.N);
            if (!RoadTerrainStaysAboveWater(w, s, e, n)) return InvalidRoadPlacement;

            if (roads.Has(t.Id) && TryGetFullLockedRoadSurface(t, out int lw, out int ls, out int le, out int ln))
                return ValidateLockedRoadPlacement(mergedEdges, w, s, e, n, lw, ls, le, ln);

            TileSlopeInfo slope = ClassifyTileSlope(w, s, e, n);
            if (slope.Kind == TileSlopeKind.Steep
                || slope.Kind == TileSlopeKind.TwoOppositeRaised
                || slope.Kind == TileSlopeKind.OneCornerRaised
                || slope.Kind == TileSlopeKind.ThreeCornersRaised)
                return InvalidRoadPlacement;

            // TwoAdjacentRaised only valid when the ramp aligns with the road direction
            if (slope.Kind == TileSlopeKind.TwoAdjacentRaised && !IsRampAligned(slope, mergedEdges))
                return InvalidRoadPlacement;

            bool naturalAllowed = slope.Kind == TileSlopeKind.Flat
                || (slope.Kind == TileSlopeKind.TwoAdjacentRaised && IsSimpleRoadShape(mergedEdges));
            if (naturalAllowed && RoadSurfaceLocksMatch(t, w, s, e, n))
                return new RoadPlacement(RoadPlacementKind.NaturalSurface, w, s, e, n);

            // Foundation (platform) only on flat terrain
            if (slope.Kind != TileSlopeKind.Flat)
                return InvalidRoadPlacement;

            if (!TryResolveFlatFoundationLevel(t, slope.Max, w, s, e, n, out int level))
                return InvalidRoadPlacement;

            return new RoadPlacement(RoadPlacementKind.FoundationSurface, level, level, level, level);
        }

        private RoadPlacement ValidateLockedRoadPlacement(
            RoadEdge edges,
            int terrainW, int terrainS, int terrainE, int terrainN,
            int surfaceW, int surfaceS, int surfaceE, int surfaceN)
        {
            if (surfaceW < terrainW || surfaceS < terrainS || surfaceE < terrainE || surfaceN < terrainN)
                return InvalidRoadPlacement;
            if (surfaceW - terrainW > 1 || surfaceS - terrainS > 1 || surfaceE - terrainE > 1 || surfaceN - terrainN > 1)
                return InvalidRoadPlacement;
            if (!IsPlanarSurface(surfaceW, surfaceS, surfaceE, surfaceN))
                return InvalidRoadPlacement;
            if (!IsFlatSurface(surfaceW, surfaceS, surfaceE, surfaceN) && !IsSimpleRoadShape(edges))
                return InvalidRoadPlacement;

            RoadPlacementKind kind =
                surfaceW == terrainW && surfaceS == terrainS && surfaceE == terrainE && surfaceN == terrainN
                    ? RoadPlacementKind.NaturalSurface
                    : RoadPlacementKind.FoundationSurface;
            return new RoadPlacement(kind, surfaceW, surfaceS, surfaceE, surfaceN);
        }

        private TileSlopeInfo ClassifyTileSlope(int w, int s, int e, int n)
        {
            int min = Math.Min(Math.Min(w, s), Math.Min(e, n));
            int max = Math.Max(Math.Max(w, s), Math.Max(e, n));
            if (max - min > 1)
                return new TileSlopeInfo(TileSlopeKind.Steep, min, max, false, false, false, false);

            bool wr = w > min;
            bool sr = s > min;
            bool er = e > min;
            bool nr = n > min;
            int raised = (wr ? 1 : 0) + (sr ? 1 : 0) + (er ? 1 : 0) + (nr ? 1 : 0);

            TileSlopeKind kind;
            if (raised == 0) kind = TileSlopeKind.Flat;
            else if (raised == 1) kind = TileSlopeKind.OneCornerRaised;
            else if (raised == 3) kind = TileSlopeKind.ThreeCornersRaised;
            else if ((wr && er) || (sr && nr)) kind = TileSlopeKind.TwoOppositeRaised;
            else kind = TileSlopeKind.TwoAdjacentRaised;

            return new TileSlopeInfo(kind, min, max, wr, sr, er, nr);
        }

        private static bool IsPlanarSurface(int w, int s, int e, int n)
        {
            return w + e == s + n;
        }

        private static bool IsFlatSurface(int w, int s, int e, int n)
        {
            return w == s && s == e && e == n;
        }

        private bool RoadTerrainStaysAboveWater(int w, int s, int e, int n)
        {
            int min = Math.Min(Math.Min(w, s), Math.Min(e, n));
            return min * tileSizeM >= SeaLevel;
        }

        private static bool IsSimpleRoadShape(RoadEdge edges)
        {
            int count = CountEdges(edges);
            if (count <= 1) return true;
            return edges == (RoadEdge.WS | RoadEdge.EN) || edges == (RoadEdge.SE | RoadEdge.NW);
        }

        // Csak TwoAdjacentRaised esetén: a rámpa iránya egyezik-e az út irányával.
        // WS+EN irány: W==S és E==N kell (WS él vízszintes, EN él vízszintes).
        // SE+NW irány: S==E és N==W kell (SE él vízszintes, NW él vízszintes).
        private static bool IsRampAligned(TileSlopeInfo slope, RoadEdge edges)
        {
            bool hasWsEn = (edges & (RoadEdge.WS | RoadEdge.EN)) != 0;
            bool hasSeNw = (edges & (RoadEdge.SE | RoadEdge.NW)) != 0;
            if (hasWsEn && !hasSeNw)
                return slope.WRaised == slope.SRaised && slope.ERaised == slope.NRaised;
            if (hasSeNw && !hasWsEn)
                return slope.SRaised == slope.ERaised && slope.NRaised == slope.WRaised;
            return false;
        }

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

        public void BuildRoadTilePath(Tile a, Tile b)
        {
            foreach (RoadPlanStep step in BuildRoadPlan(a, b))
            {
                Tile tile = tiles[step.TileId];
                RoadPlacement placement = AnalyzeRoadPlacement(tile, step.Edges);
                if (placement.IsValid)
                {
                    roads.Add(step.TileId, step.Edges);
                    CaptureRoadSurface(tile, placement);
                }
            }
            RebuildFlippedDiagonalTiles();
        }

        public void RemoveRoadTilePath(Tile a, Tile b)
        {
            foreach (RoadPlanStep step in BuildRoadPlan(a, b))
            {
                if (roads.Remove(step.TileId, step.Edges))
                    ReleaseRoadSurface(tiles[step.TileId]);
            }
            RebuildFlippedDiagonalTiles();
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
        }

        private void AddFlipTile(int iu, int iv)
        {
            if (!checkTile(iu, iv)) return;
            Tile inner = getTileByCoords(iu, iv);
            if (roads.Has(inner.Id)) return;
            flippedDiagonalTiles.Add(inner.Id);
            string fkey = inner.Code + "_" + inner.Low + "_f";
            if (!vbos.ContainsKey(fkey)) makeBuffer(inner.Code, inner.Low, true);
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
        private float RoadSurfaceZ(Node n) =>
            (roadSurfaceW.TryGetValue(n.Id, out int w) ? w : n.W) * tileSizeM;

        private Vector3 RoadCorner(Node n) => new Vector3(n.xPos, n.yPos, RoadSurfaceZ(n));

        private Vector3 RoadCorner(Node n, int w) => new Vector3(n.xPos, n.yPos, w * tileSizeM);

        // Húzás közbeni előnézet csempéi (remove = bontás, piros előnézet).
        private readonly List<RoadPlanStep> previewTiles = new List<RoadPlanStep>();
        private bool previewRemove;
        public void SetRoadPreview(Tile a, Tile b, bool remove)
        {
            previewTiles.Clear();
            previewRemove = remove;
            previewTiles.AddRange(BuildRoadPlan(a, b));
        }
        public void ClearRoadPreview() => previewTiles.Clear();
        public int RoadPreviewCount => previewTiles.Count;

        // Csempe-útvonal bejárása a rácson (egyenes lépcsős út a → b között).
        private readonly struct RoadPlanStep
        {
            public readonly int TileId;
            public readonly RoadEdge Edges;

            public RoadPlanStep(int tileId, RoadEdge edges)
            {
                TileId = tileId;
                Edges = edges;
            }
        }

        private List<RoadPlanStep> BuildRoadPlan(Tile a, Tile b)
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


        private static Vector3 Corner(Node n) => new Vector3(n.xPos, n.yPos, n.zPos);

        // ── Földmű (platform-rézsű): lokális, csempénként ───────────────────────
        // Az út a befagyasztott vezetőfelületen ül. Ahol a szomszéd NEM út ÉS a terep a
        // felület alá süllyedt, ott a csempe NYITOTT éle fél csempényi, legfeljebb 1:1
        // rézsűvel csatlakozik a terephez (terep-stílusú lap). A megosztott (út-szomszéd)
        // éleken nincs rézsű → folytonos platform; ezeken az út-lábnyom sem húzódik be.

        // Egy él behúzási hányada [0..1]: a rés (felület−terep) / fél csempe, 1:1-ig.
        // 0, ha az él megosztott (szomszéd is út) vagy nincs rés.

        // Az út-lábnyom sarkai megegyeznek a csempe eredeti sarkaival (nincs behúzás, hézagmentes).
        private void RoadFootprintCorners(Tile t, out Vector3 W, out Vector3 S, out Vector3 E, out Vector3 N)
        {
            W = RoadCorner(t.W);
            S = RoadCorner(t.S);
            E = RoadCorner(t.E);
            N = RoadCorner(t.N);
        }

        private static int CountEdges(RoadEdge edges)
        {
            int count = 0;
            if ((edges & RoadEdge.WS) != 0) count++;
            if ((edges & RoadEdge.SE) != 0) count++;
            if ((edges & RoadEdge.EN) != 0) count++;
            if ((edges & RoadEdge.NW) != 0) count++;
            return count;
        }


        public bool SearchPoint(double x, double y, double radius)
        {
            int nodeU = (int)Math.Round((x + offsetX) / tileSizeH, 0);
            int nodeV = (int)Math.Round((y + offsetY) / tileSizeV, 0);

            if (checkNode(nodeU, nodeV))
            {
                actualNode = getNodeByCoords(nodeU, nodeV);
                onpos = true;
                return true;
            }

            onpos = false;
            return false;
        }


        public bool SearchTile(double x, double y)
        {
            int u = (int)Math.Floor((x + offsetX) / tileSizeH);
            int v = (int)Math.Floor((y + offsetY) / tileSizeV);
            if (checkTile(u, v))
            {
                hoveredTile = getTileByCoords(u, v);
                return true;
            }
            hoveredTile = null;
            return false;
        }

        public bool TryGetSurfaceZ(double x, double y, out float z)
        {
            int u = (int)Math.Floor((x + offsetX) / tileSizeH);
            int v = (int)Math.Floor((y + offsetY) / tileSizeV);
            if (!checkTile(u, v))
            {
                z = 0.0f;
                return false;
            }

            Tile tile = getTileByCoords(u, v);
            double localX = ((x + offsetX) / tileSizeH) - u;
            double localY = ((y + offsetY) / tileSizeV) - v;
            float fx = (float)Math.Max(0.0, Math.Min(1.0, localX));
            float fy = (float)Math.Max(0.0, Math.Min(1.0, localY));

            float south = tile.W.zPos + (tile.S.zPos - tile.W.zPos) * fx;
            float north = tile.N.zPos + (tile.E.zPos - tile.N.zPos) * fx;
            z = south + (north - south) * fy;
            return true;
        }

        public void GetWorldBounds(out Vector3 min, out Vector3 max)
        {
            min = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
            max = new Vector3(float.MinValue, float.MinValue, float.MinValue);

            foreach (Node node in nodes)
            {
                min.X = Math.Min(min.X, node.xPos);
                min.Y = Math.Min(min.Y, node.yPos);
                min.Z = Math.Min(min.Z, node.zPos);
                max.X = Math.Max(max.X, node.xPos);
                max.Y = Math.Max(max.Y, node.yPos);
                max.Z = Math.Max(max.Z, node.zPos);
            }
        }

        public void ClearHover()
        {
            hoveredTile = null;
            onpos = false;
        }





        public void UpElevation()
        {
            ElevationManager(+1);
        }

        public void DownElevation()
        {
            ElevationManager(-1);
        }

        /// <summary>
        /// Ecsetes terepszerkesztés a kijelölt (hover) node körül: korong alakú
        /// terület, sugár = radius (0 = csak a középpont), erősség = ismétlésszám.
        /// A hidrológiát csak egyszer, a végén építi újra.
        /// </summary>
        public void EditElevation(int delta, int radius, int strength)
        {
            if (actualNode == null) return;

            Node center = actualNode;
            int cu = center.U, cv = center.V;

            // Az ElevationManager kaszkádja tartja a TT-invariánst (szomszédos sarkok max 1
            // eltérés) → a terep mindig érvényes. Az út alatt is alakítható a terep: a
            // befagyasztott vezetőfelület (roadSurfaceW) a helyén marad, a rést a foundation
            // tölti ki — ezért itt NINCS út-freeze.
            suppressHydrologyRebuild = true;
            try
            {
                for (int du = -radius; du <= radius; du++)
                    for (int dv = -radius; dv <= radius; dv++)
                    {
                        if (du * du + dv * dv > radius * radius) continue;
                        if (!checkNode(cu + du, cv + dv)) continue;

                        Node n = getNodeByCoords(cu + du, cv + dv);
                        for (int s = 0; s < strength; s++)
                        {
                            actualNode = n;
                            ElevationManager(delta);
                        }
                    }
            }
            finally
            {
                suppressHydrologyRebuild = false;
            }

            actualNode = center;
            RebuildHydrology();
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

        // OpenTTD-stílusú terraform (terraform_cmd.cpp): egy sarkot delta-val mozdít, és
        // rekurzívan a cél felé 1-gyel közelíti a szomszéd-sarkokat, amíg minden ÉL-
        // szomszédos sarok eltérése ≤1 (a szemközti sarok 2-vel is → meredek, érvényes).
        // A változásokat előbb egy pending-térképbe gyűjti; ha bármelyik a [0, MaxHeight]
        // korláton kívülre esne, az EGÉSZ művelet elbukik és semmi nem változik (atomikus).
        private void ElevationManager(int delta)
        {
            if (actualNode == null) return;

            int maxHeight = settings.MaxHeight;
            Dictionary<int, int> pending = new Dictionary<int, int>();
            bool ok = true;

            int HeightOf(Node nd) => pending.TryGetValue(nd.Id, out int v) ? v : nd.W;
            void Set(Node nd, int h)
            {
                if (!ok) return;
                if (h < 0 || h > maxHeight) { ok = false; return; }   // korláton kívül → bukás
                if (HeightOf(nd) == h) return;
                pending[nd.Id] = h;
                TryRelaxNeighbor(nd.U, nd.V - 1, h);
                TryRelaxNeighbor(nd.U + 1, nd.V, h);
                TryRelaxNeighbor(nd.U, nd.V + 1, h);
                TryRelaxNeighbor(nd.U - 1, nd.V, h);
            }

            void TryRelaxNeighbor(int u, int v, int h)
            {
                if (!checkNode(u, v)) return;
                Node nb = getNodeByCoords(u, v);
                int diff = h - HeightOf(nb);
                if (Math.Abs(diff) > 1) Set(nb, h - Math.Sign(diff));
            }

            Set(actualNode, actualNode.W + delta);
            if (!ok || pending.Count == 0) return;  // érvénytelen vagy nincs változás → atomikus elvetés
            if (!ValidateRoadsAgainstPendingTerrain(pending)) return;

            List<Node> changed = new List<Node>(pending.Count);
            foreach (KeyValuePair<int, int> kv in pending)
            {
                Node nd = data.Nodes[kv.Key];
                nd.W = kv.Value;
                nd.zPos = nd.W * tileSizeM;   // zPos szinkron a hidrológiához
                changed.Add(nd);
            }
            updateNodes(changed);
        }

        private bool ValidateRoadsAgainstPendingTerrain(Dictionary<int, int> pending)
        {
            if (roads.Count == 0) return true;

            int HeightOf(Node nd) => pending.TryGetValue(nd.Id, out int v) ? v : nd.W;
            Tile[] nodeTiles = new Tile[4];

            // Út-csempe csomópontok (Node) magasságának módosítása tilos!
            foreach (KeyValuePair<int, int> kv in pending)
            {
                Node node = data.Nodes[kv.Key];
                if (kv.Value != node.W)
                {
                    int nodeTileCount = data.GetTilesByNode(node, nodeTiles);
                    for (int i = 0; i < nodeTileCount; i++)
                    {
                        Tile tile = nodeTiles[i];
                        if (roads.Has(tile.Id))
                            return false;
                    }
                }
            }

            HashSet<int> affectedRoadTiles = new HashSet<int>();

            foreach (int nodeId in pending.Keys)
            {
                Node node = data.Nodes[nodeId];
                int nodeTileCount = data.GetTilesByNode(node, nodeTiles);
                for (int i = 0; i < nodeTileCount; i++)
                {
                    Tile tile = nodeTiles[i];
                    if (roads.Has(tile.Id)) affectedRoadTiles.Add(tile.Id);
                }
            }

            foreach (int tileId in affectedRoadTiles)
                if (!ValidateExistingRoadAgainstTerrain(tiles[tileId], HeightOf))
                    return false;

            return true;
        }

        private bool ValidateExistingRoadAgainstTerrain(Tile t, Func<Node, int> heightOf)
        {
            int w = heightOf(t.W);
            int s = heightOf(t.S);
            int e = heightOf(t.E);
            int n = heightOf(t.N);
            if (!RoadTerrainStaysAboveWater(w, s, e, n)) return false;

            if (TryGetFullLockedRoadSurface(t, out int sw, out int ss, out int se, out int sn))
                return ValidateLockedRoadPlacement(roads.GetEdges(t.Id), w, s, e, n, sw, ss, se, sn).IsValid;

            return AnalyzeRoadPlacement(t, roads.GetEdges(t.Id), heightOf).IsValid;
        }
    }
}
