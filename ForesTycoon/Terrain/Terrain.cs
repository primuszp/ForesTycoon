using System;
using System.Collections.Generic;
using System.Drawing;
using OpenTK.Mathematics;
using OpenTK.Graphics.OpenGL;

namespace ForesTycoon
{
    class Terrain
    {
        private readonly TerrainSettings settings;
        private Hydrology hydro;
        private HashSet<int> riverNodeIds => hydro.RiverNodeIds;
        private HashSet<int> standingWaterTileIds => hydro.StandingWaterTileIds;
        private readonly Dictionary<string, VertexBuffer> vbos = new Dictionary<string, VertexBuffer>();
        private readonly VertexBuffer edges = new VertexBuffer(PrimitiveType.Lines);
        private readonly RoadNetwork roads = new RoadNetwork();
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

            GenerateTerrain();
            
            GL.LineWidth(2.0f);
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



        private void makeBuffer(string code, int low)
        {
            string key = code + "_" + low;
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

            if (Math.Abs(wZ - eZ) <= Math.Abs(nZ - sZ))
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
            foreach (Node node in nodes)
            {
                node.zPos = node.W * tileSizeM;
                vertices[node.Id].Position.Z = node.zPos;

                // Ha a terep emelkedett, a víz nem lebeghet a magasban; ha süllyedt, marad szárazon (majd folyik bele)
                if (nodeWaterDepth != null)
                    nodeWaterDepth[node.Id] = Math.Max(0f, nodeWaterDepth[node.Id]);

                List<Tile> tiles = getTilesByNode(node);
                foreach (Tile tile in tiles)
                {
                    string code = tile.getCode();
                    tile.LowPos = tile.Low * tileSizeM;
                    if (!vbos.ContainsKey(code + "_" + tile.Low))
                        makeBuffer(code, tile.Low);
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

        private void RebuildHydrology() => hydro.Rebuild();

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

        private List<Node> getNeighbours(Node node) => data.GetNeighbours(node);

        private List<Tile> getTilesByNode(Node node) => data.GetTilesByNode(node);

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

        public void Draw(bool showNodeMarker)
        {
            // A kitöltött terep írja a depth buffert; a koplanáris overlay rétegek
            // később depth írás nélkül rajzolódnak, így nincs Z-fighting.
            foreach (Tile tile in tiles)
            {
                VertexBuffer vbo = vbos[tile.Code + "_" + tile.Low];

                GL.PushMatrix();
                {
                    GL.Translate(tile.W.xPos, tile.W.yPos, tile.LowPos);
                    vbo.DrawArray();
                }
                GL.PopMatrix();
            }
            DrawSkirts();
            DrawWater();
            DrawWaterWalls();
            DrawRivers();

            // Terrain decal pass: drawn after terrain but before props.
            // Props rendered later with depth testing naturally occlude these overlays.
            GL.Disable(EnableCap.DepthTest);
            GL.DepthMask(false);
            DrawLandGrid();

            DrawRoads();
            DrawHoveredTile();
            GL.DepthMask(true);
            GL.Enable(EnableCap.DepthTest);

            DrawTrees();

            if (showNodeMarker && onpos)
            {
                GL.PushMatrix();
                {
                    GL.Translate(actualNode.xPos, actualNode.yPos, actualNode.zPos);
                    DrawSphere(0.55f, 16, 16);
                }
                GL.PopMatrix();
            }
        }

        // ── Út-render konstansok (referencia tile-készlet: szürke aszfalt + krém padka) ─
        private static readonly Color RoadSurfaceColor = Color.FromArgb(108, 110, 112);  // szürke úttest
        private static readonly Color RoadShoulder     = Color.FromArgb(214, 210, 190);  // világos krém padka
        private const float ShoulderFrac = 0.16f;  // padka szélessége a középpont felé

        // Csempe-alapú úthálózat: a kapcsolatok a szomszédos út-csempékből adódnak.
        public Tile HoveredTile => hoveredTile;
        public int RoadCount => roads.Count;

        public bool AddRoadTile(Tile t) => IsRoadBuildable(t, RoadEdge.WS | RoadEdge.EN) && roads.Add(t.Id, RoadEdge.WS | RoadEdge.EN);


        // Vízmentes és PLANÁRIS (sík rámpa) csempére építhető út: a négy sarok egy
        // (akár ferde) síkon van, így a burkolat laposan ráfekszik. A lejtés/magasság
        // mértéke tetszőleges; csak a CSAVART (nyereg) csempét tiltjuk — a bármilyen
        // edge-konfig (egyenes, kanyar, T, +) mehet lejtőn is.
        public bool IsRoadBuildable(Tile t) => IsRoadBuildable(t, RoadEdge.WS | RoadEdge.EN);

        private bool IsRoadBuildable(Tile t, RoadEdge edges)
        {
            if (t == null) return false;
            if (hydro.ShouldDrawStandingWater(t)) return false;

            // Planáris, ha a szemközti sarkok magasság-összege egyenlő (a bilineáris
            // felület csavar-tagja nulla): hW + hE == hS + hN.
            return t.W.W + t.E.W == t.S.W + t.N.W;
        }

        public void BuildRoadTilePath(Tile a, Tile b)
        {
            foreach (RoadPlanStep step in BuildRoadPlan(a, b))
                if (IsRoadBuildable(tiles[step.TileId], step.Edges))
                    roads.Add(step.TileId, step.Edges);
        }

        public void RemoveRoadTilePath(Tile a, Tile b)
        {
            foreach (RoadPlanStep step in BuildRoadPlan(a, b))
                roads.Remove(step.TileId, step.Edges);
        }

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

        private void DrawRoads()
        {
            if (roads.Count == 0 && previewTiles.Count == 0) return;

            // Decal overlay a terep után, a közös overlay pass depth állapotával.
            if (roads.Count > 0)
            {
                // 1. réteg: világos krém padka (széles sáv) a terep átlójára illesztve
                // → lejtőn rámpaként pontosan ráfekszik a felszínre.
                GL.Begin(PrimitiveType.Quads);
                foreach (int id in roads.Tiles)
                    RoadSurface(tiles[id], roads.GetEdges(id), 0.92f, RoadShoulder);
                GL.End();

                // 2. réteg: szürke úttest (keskenyebb sáv) a padka tetején.
                GL.Begin(PrimitiveType.Quads);
                foreach (int id in roads.Tiles)
                    RoadSurface(tiles[id], roads.GetEdges(id), 0.62f, RoadSurfaceColor);
                GL.End();
            }

            // ── Előnézet csempék (kitöltés + körvonal); fehér=építés, piros=bontás.
            if (previewTiles.Count > 0)
            {
                GL.Enable(EnableCap.Blend);
                GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);

                // Csempénként: bontás → piros; építés → fehér (érvényes) / piros (vízen
                // vagy túl meredeken nem építhető).
                Color okFill = Color.FromArgb(70, 255, 255, 255), okLine = Color.FromArgb(235, 255, 255, 255);
                Color badFill = Color.FromArgb(90, 235, 70, 70), badLine = Color.FromArgb(245, 248, 80, 80);

                GL.Begin(PrimitiveType.Quads);
                foreach (RoadPlanStep step in previewTiles)
                {
                    bool bad = previewRemove || !IsRoadBuildable(tiles[step.TileId], step.Edges);
                    // Építésnél a meglévő gráf-élekkel összevont alakot mutatjuk → már
                    // húzás közben látszik a kialakuló kanyar / T / + kereszteződés.
                    RoadEdge shown = previewRemove ? step.Edges : step.Edges | roads.GetEdges(step.TileId);
                    RoadSurface(tiles[step.TileId], shown, 0.92f, bad ? badFill : okFill);
                }
                GL.End();

                GL.LineWidth(2.5f);
                foreach (RoadPlanStep step in previewTiles)
                {
                    Tile t = tiles[step.TileId];
                    bool bad = previewRemove || !IsRoadBuildable(t, step.Edges);
                    GL.Color4(bad ? badLine : okLine);
                    GL.Begin(PrimitiveType.LineLoop);
                    GL.Vertex3(t.W.xPos, t.W.yPos, t.W.zPos);
                    GL.Vertex3(t.S.xPos, t.S.yPos, t.S.zPos);
                    GL.Vertex3(t.E.xPos, t.E.yPos, t.E.zPos);
                    GL.Vertex3(t.N.xPos, t.N.yPos, t.N.zPos);
                    GL.End();
                }
                GL.LineWidth(2f);
                GL.Disable(EnableCap.Blend);
            }
        }

        private static Vector3 Corner(Node n) => new Vector3(n.xPos, n.yPos, n.zPos);

        private static int CountEdges(RoadEdge edges)
        {
            int count = 0;
            if ((edges & RoadEdge.WS) != 0) count++;
            if ((edges & RoadEdge.SE) != 0) count++;
            if ((edges & RoadEdge.EN) != 0) count++;
            if ((edges & RoadEdge.NW) != 0) count++;
            return count;
        }

        private void RoadSurface(Tile t, RoadEdge edges, float widthFactor, Color color)
        {
            Vector3 W = Corner(t.W), S = Corner(t.S), E = Corner(t.E), N = Corner(t.N);
            Vector3 C = (W + S + E + N) * 0.25f;
            float width = Math.Min(tileSizeH, tileSizeV) * widthFactor;
            int n = CountEdges(edges);

            GL.Color4(color);

            // Kanyar (2 szomszédos él): negyedív a KÖZÖS sarok körül. Az ív az élek
            // közepénél merőlegesen lép ki → érintőfolytonosan illeszkedik a szomszéd
            // egyenes úthoz (mint az OpenTTD kerek kanyar-tile).
            if (n == 2 && TryCornerArc(edges, out float startDeg))
            {
                float side = DistXY(W, S);
                float hwuv = (width * 0.5f) / side;
                RoadArcBand(W, S, E, N, startDeg, 0.5f - hwuv, 0.5f + hwuv);
                return;
            }

            // Egyenes / zsákutca / T / +: ágak minden bekötött él felé, mindegyik a
            // csempe-középponttól az él KÖZEPÉIG → a szomszéd út karjával pontosan
            // illeszkedik (nincs hézag). Két szemközti ág egy teljes átmenő sávot ad,
            // így T-nél (3 él) és +-nál (4 él) is tömör, hézagmentes a csomópont; a
            // nyitott él fűként/padkaként marad → ez adja a T/+ formát.
            if ((edges & RoadEdge.WS) != 0) RoadArm(C, (W + S) * 0.5f, width);
            if ((edges & RoadEdge.SE) != 0) RoadArm(C, (S + E) * 0.5f, width);
            if ((edges & RoadEdge.EN) != 0) RoadArm(C, (E + N) * 0.5f, width);
            if ((edges & RoadEdge.NW) != 0) RoadArm(C, (N + W) * 0.5f, width);

            // Belső lekerekítés MINDEN olyan csempe-saroknál, ahol a két szomszédos él
            // is út (T-nél 2, +-nál 4 sarok). A fűsarkot a CSEMPE-SAROK köré centrált
            // negyedkör kerekíti (sugár 0.5−hw); mindkét réteg ugyanaz a középpont →
            // koncentrikus ívek → a padka vonalai mindenhol párhuzamosak.
            float jSide = DistXY(W, S);
            float hw = (width * 0.5f) / jSide;
            float rf = 0.5f - hw;
            if ((edges & RoadEdge.SE) != 0 && (edges & RoadEdge.EN) != 0)  // E sarok
                RoadInnerFillet(W, S, E, N, 0.5f + hw, 0.5f + hw, 1f, 1f, rf, 270f, 180f);
            if ((edges & RoadEdge.EN) != 0 && (edges & RoadEdge.NW) != 0)  // N sarok
                RoadInnerFillet(W, S, E, N, 0.5f - hw, 0.5f + hw, 0f, 1f, rf, 360f, 270f);
            if ((edges & RoadEdge.NW) != 0 && (edges & RoadEdge.WS) != 0)  // W sarok
                RoadInnerFillet(W, S, E, N, 0.5f - hw, 0.5f - hw, 0f, 0f, rf, 90f, 0f);
            if ((edges & RoadEdge.WS) != 0 && (edges & RoadEdge.SE) != 0)  // S sarok
                RoadInnerFillet(W, S, E, N, 0.5f + hw, 0.5f - hw, 1f, 0f, rf, 180f, 90f);
        }

        // Belső sarok-kitöltés: a kar-négyzet sarok (apex) és a CSEMPE-SAROK (cu,cv)
        // köré rf sugárral húzott negyedív közötti rész (négyzet − negyedkör), legyezővel
        // az apexből. Quads-kontextusban fut → elfajuló quad (P,a,b,P). Additív a karokra.
        private void RoadInnerFillet(Vector3 W, Vector3 S, Vector3 E, Vector3 N,
            float apexU, float apexV, float cu, float cv, float rf, float degA, float degB)
        {
            Vector3 P = TileUV(W, S, E, N, apexU, apexV);
            const int seg = 3;
            for (int i = 0; i < seg; i++)
            {
                float t0 = (float)((degA + (degB - degA) * i / seg) * Math.PI / 180.0);
                float t1 = (float)((degA + (degB - degA) * (i + 1) / seg) * Math.PI / 180.0);
                Vector3 a = TileUV(W, S, E, N, cu + rf * (float)Math.Cos(t0), cv + rf * (float)Math.Sin(t0));
                Vector3 b = TileUV(W, S, E, N, cu + rf * (float)Math.Cos(t1), cv + rf * (float)Math.Sin(t1));
                GL.Vertex3(P); GL.Vertex3(a); GL.Vertex3(b); GL.Vertex3(P);
            }
        }

        private static float DistXY(Vector3 a, Vector3 b)
        {
            float dx = b.X - a.X, dy = b.Y - a.Y;
            return (float)Math.Sqrt(dx * dx + dy * dy);
        }

        // Pont a csempén belül (u,v)∈[0,1]² bilineáris keverésével a 4 sarokból
        // (W=(0,0), S=(1,0), E=(1,1), N=(0,1)). A z is keveredik → lejtőre fekvő ív.
        private static Vector3 TileUV(Vector3 W, Vector3 S, Vector3 E, Vector3 N, float u, float v) =>
            (1f - u) * (1f - v) * W + u * (1f - v) * S + u * v * E + (1f - u) * v * N;

        // Szomszédos élpár → a közös sarok (ív-középpont) uv-pozíciója és az ív
        // kezdőszöge (fokban). Minden ív +90°-ot söpör. Szemközti pár esetén false.
        private static bool TryCornerArc(RoadEdge edges, out float startDeg)
        {
            switch (edges)
            {
                case RoadEdge.NW | RoadEdge.WS: startDeg = 0f; return true;    // sarok = W
                case RoadEdge.WS | RoadEdge.SE: startDeg = 90f; return true;   // sarok = S
                case RoadEdge.SE | RoadEdge.EN: startDeg = 180f; return true;  // sarok = E
                case RoadEdge.EN | RoadEdge.NW: startDeg = 270f; return true;  // sarok = N
                default: startDeg = 0f; return false;
            }
        }

        // Negyedív-sáv (rInner..rOuter sugár, uv-egységben) a startDeg-tól +90°-ig.
        // Az ív középpontja a startDeg által kódolt sarok; uv-pontok → TileUV.
        private void RoadArcBand(Vector3 W, Vector3 S, Vector3 E, Vector3 N,
            float startDeg, float rInner, float rOuter)
        {
            // Az ív-középpont (sarok) uv-koordinátája a kezdőszögből.
            float cu = startDeg < 90f ? 0f : startDeg < 180f ? 1f : startDeg < 270f ? 1f : 0f;
            float cv = startDeg < 90f ? 0f : startDeg < 180f ? 0f : startDeg < 270f ? 1f : 1f;

            const int seg = 6;
            for (int i = 0; i < seg; i++)
            {
                float t0 = (float)((startDeg + 90f * i / seg) * Math.PI / 180.0);
                float t1 = (float)((startDeg + 90f * (i + 1) / seg) * Math.PI / 180.0);
                float c0 = (float)Math.Cos(t0), s0 = (float)Math.Sin(t0);
                float c1 = (float)Math.Cos(t1), s1 = (float)Math.Sin(t1);

                GL.Vertex3(TileUV(W, S, E, N, cu + rInner * c0, cv + rInner * s0));
                GL.Vertex3(TileUV(W, S, E, N, cu + rOuter * c0, cv + rOuter * s0));
                GL.Vertex3(TileUV(W, S, E, N, cu + rOuter * c1, cv + rOuter * s1));
                GL.Vertex3(TileUV(W, S, E, N, cu + rInner * c1, cv + rInner * s1));
            }
        }

        private void RoadArm(Vector3 c, Vector3 m, float width)
        {
            float dx = m.X - c.X, dy = m.Y - c.Y;
            float len = (float)Math.Sqrt(dx * dx + dy * dy);
            if (len < 1e-4f) return;
            float px = -dy / len * width * 0.5f;
            float py = dx / len * width * 0.5f;

            GL.Vertex3(c.X + px, c.Y + py, c.Z);
            GL.Vertex3(c.X - px, c.Y - py, c.Z);
            GL.Vertex3(m.X - px, m.Y - py, m.Z);
            GL.Vertex3(m.X + px, m.Y + py, m.Z);
        }

        private void DrawLandGrid()
        {
            GL.Color4(Color.FromArgb(82, 115, 38));
            GL.Begin(PrimitiveType.Lines);
            foreach (Tile tile in tiles)
            {
                if (ShouldDrawStandingWater(tile)) continue;

                GL.Vertex3(tile.W.xPos, tile.W.yPos, tile.W.zPos); GL.Vertex3(tile.S.xPos, tile.S.yPos, tile.S.zPos);
                GL.Vertex3(tile.S.xPos, tile.S.yPos, tile.S.zPos); GL.Vertex3(tile.E.xPos, tile.E.yPos, tile.E.zPos);
                GL.Vertex3(tile.E.xPos, tile.E.yPos, tile.E.zPos); GL.Vertex3(tile.N.xPos, tile.N.yPos, tile.N.zPos);
                GL.Vertex3(tile.N.xPos, tile.N.yPos, tile.N.zPos); GL.Vertex3(tile.W.xPos, tile.W.yPos, tile.W.zPos);
            }
            GL.End();
        }

        private void DrawSphere(float radius, int rings, int sectors)
        {
            Vector3 lightDir = new Vector3(0.5f, -0.5f, 1.0f);
            lightDir.Normalize();

            GL.Begin(PrimitiveType.Quads);
            for (int i = 0; i < rings; i++)
            {
                float theta1 = (float)(i     * Math.PI / rings) - (float)(Math.PI / 2);
                float theta2 = (float)((i+1) * Math.PI / rings) - (float)(Math.PI / 2);
                for (int j = 0; j < sectors; j++)
                {
                    float phi1 = (float)(j     * 2 * Math.PI / sectors);
                    float phi2 = (float)((j+1) * 2 * Math.PI / sectors);
                    Vector3 n1 = SphereNormal(theta1, phi1);
                    Vector3 n2 = SphereNormal(theta1, phi2);
                    Vector3 n3 = SphereNormal(theta2, phi2);
                    Vector3 n4 = SphereNormal(theta2, phi1);
                    GL.Color3(ShadedWhite(n1, lightDir)); GL.Vertex3(n1 * radius);
                    GL.Color3(ShadedWhite(n2, lightDir)); GL.Vertex3(n2 * radius);
                    GL.Color3(ShadedWhite(n3, lightDir)); GL.Vertex3(n3 * radius);
                    GL.Color3(ShadedWhite(n4, lightDir)); GL.Vertex3(n4 * radius);
                }
            }
            GL.End();
        }

        private Vector3 SphereNormal(float theta, float phi) => new Vector3(
            (float)(Math.Cos(theta) * Math.Cos(phi)),
            (float)(Math.Cos(theta) * Math.Sin(phi)),
            (float)Math.Sin(theta));

        private Color ShadedWhite(Vector3 normal, Vector3 light)
        {
            float i = Math.Max(0.65f, Math.Min(1.0f, Vector3.Dot(normal, light) * 0.85f + 0.25f));
            return Color.FromArgb((int)(255 * i), (int)(255 * i), (int)(255 * i));
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

        public void ClearHover()
        {
            hoveredTile = null;
            onpos = false;
        }

        private void DrawRivers()
        {
            if (riverNodeIds.Count == 0) return;

            // A vízfelszín a folyómeder legmélyebb sarka FELETT lebeg – ugyanaz az elv
            // mint a tengerné: lapos átlátszó quad, alatta látszik a meder → mélység illúzió.
            // A meder maga a vágott terep (tileSizeM=2, tehát W=1 → Z=2).
            // Vízfelszín: mederalap + RiverWaterHeight

            // Mély folyó (mind a 4 sarok river node) – sötétebb, több átlátszóság
            Color riverDeep    = Color.FromArgb(195,  38, 118, 188);
            // Sekély part (2-3 sarok river node) – világosabb cián
            Color riverShallow = Color.FromArgb(130,  68, 155, 218);
            // Rácsszín – halvány kék vonalak a vízfelszínen
            Color riverGrid    = Color.FromArgb(160, 105, 185, 238);

            float t = (float)(Environment.TickCount64 % 628318) * 0.001f;

            GL.Enable(EnableCap.Blend);
            GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
            GL.Begin(PrimitiveType.Quads);
            for (int u = 0; u < nodeCols - 1; u++)
            {
                for (int v = 0; v < nodeRows - 1; v++)
                {
                    Tile tile = getTileByCoords(u, v);
                    if (HasDynamicWater(tile) || !CanRenderFallbackRiver(tile)) continue;

                    int rc = CountRiverCorners(tile);

                    float baseZ = tile.Low * tileSizeM + RiverWaterHeight;
                    float cx = (tile.W.xPos + tile.E.xPos) * 0.5f;
                    float cy = (tile.W.yPos + tile.N.yPos) * 0.5f;
                    // Folyónál felére csökkentett amplitúdó – gyorsabb, kisebb hullám
                    float wz = ApplyClampedWave(cx, cy, baseZ, RiverWaterHeight, t * 1.4f);

                    GL.Color4(rc == 4 ? riverDeep : riverShallow);
                    GL.Vertex3(tile.W.xPos, tile.W.yPos, wz);
                    GL.Vertex3(tile.S.xPos, tile.S.yPos, wz);
                    GL.Vertex3(tile.E.xPos, tile.E.yPos, wz);
                    GL.Vertex3(tile.N.xPos, tile.N.yPos, wz);
                }
            }
            GL.End();
            // Rácsvonalak a mély folyó tile-okon – per-sarok hullám
            GL.Begin(PrimitiveType.Lines);
            for (int u = 0; u < nodeCols - 1; u++)
            {
                for (int v = 0; v < nodeRows - 1; v++)
                {
                    Tile tile = getTileByCoords(u, v);
                    if (HasDynamicWater(tile) || !CanRenderFallbackRiver(tile)) continue;

                    int rc = CountRiverCorners(tile);
                    if (rc < 4) continue;

                    float baseZ = tile.Low * tileSizeM + RiverWaterHeight;
                    float ts = t * 1.4f;
                    float zwN = ApplyClampedWave(tile.N.xPos, tile.N.yPos, baseZ, RiverWaterHeight, ts);
                    float zwS = ApplyClampedWave(tile.S.xPos, tile.S.yPos, baseZ, RiverWaterHeight, ts);
                    float zwE = ApplyClampedWave(tile.E.xPos, tile.E.yPos, baseZ, RiverWaterHeight, ts);
                    float zwW = ApplyClampedWave(tile.W.xPos, tile.W.yPos, baseZ, RiverWaterHeight, ts);

                    GL.Color4(riverGrid);
                    GL.Vertex3(tile.W.xPos, tile.W.yPos, zwW); GL.Vertex3(tile.S.xPos, tile.S.yPos, zwS);
                    GL.Vertex3(tile.S.xPos, tile.S.yPos, zwS); GL.Vertex3(tile.E.xPos, tile.E.yPos, zwE);
                    GL.Vertex3(tile.E.xPos, tile.E.yPos, zwE); GL.Vertex3(tile.N.xPos, tile.N.yPos, zwN);
                    GL.Vertex3(tile.N.xPos, tile.N.yPos, zwN); GL.Vertex3(tile.W.xPos, tile.W.yPos, zwW);
                }
            }
            GL.End();
            GL.LineWidth(2.0f);
            GL.Disable(EnableCap.Blend);
        }

        private void DrawHoveredTile()
        {
            if (hoveredTile == null) return;
            Tile t = hoveredTile;

            GL.Enable(EnableCap.Blend);
            GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
            // Sárga félig átlátszó fill
            GL.Begin(PrimitiveType.Triangles);
            GL.Color4(Color.FromArgb(90, 255, 235, 60));
            if (Math.Abs(t.W.zPos - t.E.zPos) <= Math.Abs(t.N.zPos - t.S.zPos))
            {
                GL.Vertex3(t.W.xPos, t.W.yPos, t.W.zPos);
                GL.Vertex3(t.S.xPos, t.S.yPos, t.S.zPos);
                GL.Vertex3(t.E.xPos, t.E.yPos, t.E.zPos);
                GL.Vertex3(t.W.xPos, t.W.yPos, t.W.zPos);
                GL.Vertex3(t.E.xPos, t.E.yPos, t.E.zPos);
                GL.Vertex3(t.N.xPos, t.N.yPos, t.N.zPos);
            }
            else
            {
                GL.Vertex3(t.N.xPos, t.N.yPos, t.N.zPos);
                GL.Vertex3(t.W.xPos, t.W.yPos, t.W.zPos);
                GL.Vertex3(t.S.xPos, t.S.yPos, t.S.zPos);
                GL.Vertex3(t.N.xPos, t.N.yPos, t.N.zPos);
                GL.Vertex3(t.S.xPos, t.S.yPos, t.S.zPos);
                GL.Vertex3(t.E.xPos, t.E.yPos, t.E.zPos);
            }
            GL.End();

            // Éles sárga keret
            GL.LineWidth(4.0f);
            GL.Begin(PrimitiveType.LineLoop);
            GL.Color4(Color.FromArgb(245, 255, 240, 80));
            GL.Vertex3(t.W.xPos, t.W.yPos, t.W.zPos);
            GL.Vertex3(t.S.xPos, t.S.yPos, t.S.zPos);
            GL.Vertex3(t.E.xPos, t.E.yPos, t.E.zPos);
            GL.Vertex3(t.N.xPos, t.N.yPos, t.N.zPos);
            GL.End();
            GL.LineWidth(2.0f);

            GL.Disable(EnableCap.Blend);
        }

        private void DrawSkirts()
        {
            const float BASE_Z      = -16.0f;
            const float RIM_H       =   1.5f;
            Color colorFront  = Color.FromArgb(205, 163, 112);
            Color colorSide   = Color.FromArgb(185, 148, 100);
            Color colorBottom = Color.FromArgb(130, 100,  65);
            Color colorRim    = Color.FromArgb( 68,  48,  25);

            GL.Begin(PrimitiveType.Quads);

            // ── South edge (v = 0) ────────────────────────────────────────────
            for (int u = 0; u < nodeCols - 1; u++)
            {
                Node a = getNodeByCoords(u,     0);
                Node b = getNodeByCoords(u + 1, 0);
                float rimA = a.zPos - RIM_H;
                float rimB = b.zPos - RIM_H;
                // sötét peremcsík (felső sáv)
                GL.Color3(colorRim);
                GL.Vertex3(a.xPos, a.yPos, a.zPos); GL.Vertex3(b.xPos, b.yPos, b.zPos);
                GL.Vertex3(b.xPos, b.yPos, rimB);   GL.Vertex3(a.xPos, a.yPos, rimA);
                // világos oldallap (perem alatt → aljáig)
                GL.Color3(colorFront);
                GL.Vertex3(a.xPos, a.yPos, rimA);   GL.Vertex3(b.xPos, b.yPos, rimB);
                GL.Vertex3(b.xPos, b.yPos, BASE_Z); GL.Vertex3(a.xPos, a.yPos, BASE_Z);
            }

            // ── North edge (v = nodeRows-1) ───────────────────────────────────
            for (int u = 0; u < nodeCols - 1; u++)
            {
                Node a = getNodeByCoords(u,     nodeRows - 1);
                Node b = getNodeByCoords(u + 1, nodeRows - 1);
                float rimA = a.zPos - RIM_H;
                float rimB = b.zPos - RIM_H;
                GL.Color3(colorRim);
                GL.Vertex3(b.xPos, b.yPos, b.zPos); GL.Vertex3(a.xPos, a.yPos, a.zPos);
                GL.Vertex3(a.xPos, a.yPos, rimA);   GL.Vertex3(b.xPos, b.yPos, rimB);
                GL.Color3(colorFront);
                GL.Vertex3(b.xPos, b.yPos, rimB);   GL.Vertex3(a.xPos, a.yPos, rimA);
                GL.Vertex3(a.xPos, a.yPos, BASE_Z); GL.Vertex3(b.xPos, b.yPos, BASE_Z);
            }

            // ── West edge (u = 0) ─────────────────────────────────────────────
            for (int v = 0; v < nodeRows - 1; v++)
            {
                Node a = getNodeByCoords(0, v);
                Node b = getNodeByCoords(0, v + 1);
                float rimA = a.zPos - RIM_H;
                float rimB = b.zPos - RIM_H;
                GL.Color3(colorRim);
                GL.Vertex3(a.xPos, a.yPos, a.zPos); GL.Vertex3(a.xPos, a.yPos, rimA);
                GL.Vertex3(b.xPos, b.yPos, rimB);   GL.Vertex3(b.xPos, b.yPos, b.zPos);
                GL.Color3(colorSide);
                GL.Vertex3(a.xPos, a.yPos, rimA);   GL.Vertex3(a.xPos, a.yPos, BASE_Z);
                GL.Vertex3(b.xPos, b.yPos, BASE_Z); GL.Vertex3(b.xPos, b.yPos, rimB);
            }

            // ── East edge (u = nodeCols-1) ────────────────────────────────────
            for (int v = 0; v < nodeRows - 1; v++)
            {
                Node a = getNodeByCoords(nodeCols - 1, v);
                Node b = getNodeByCoords(nodeCols - 1, v + 1);
                float rimA = a.zPos - RIM_H;
                float rimB = b.zPos - RIM_H;
                GL.Color3(colorRim);
                GL.Vertex3(b.xPos, b.yPos, b.zPos); GL.Vertex3(b.xPos, b.yPos, rimB);
                GL.Vertex3(a.xPos, a.yPos, rimA);   GL.Vertex3(a.xPos, a.yPos, a.zPos);
                GL.Color3(colorSide);
                GL.Vertex3(b.xPos, b.yPos, rimB);   GL.Vertex3(b.xPos, b.yPos, BASE_Z);
                GL.Vertex3(a.xPos, a.yPos, BASE_Z); GL.Vertex3(a.xPos, a.yPos, rimA);
            }

            // ── Aljlap ────────────────────────────────────────────────────────
            GL.Color3(colorBottom);
            Node sw = getNodeByCoords(0,            0);
            Node se = getNodeByCoords(nodeCols - 1, 0);
            Node ne = getNodeByCoords(nodeCols - 1, nodeRows - 1);
            Node nw = getNodeByCoords(0,            nodeRows - 1);
            GL.Vertex3(sw.xPos, sw.yPos, BASE_Z);
            GL.Vertex3(se.xPos, se.yPos, BASE_Z);
            GL.Vertex3(ne.xPos, ne.yPos, BASE_Z);
            GL.Vertex3(nw.xPos, nw.yPos, BASE_Z);

            GL.End();
        }

        // Valódi vízszint: a W=0 alapszint FELETT lebegő vízfelszín.
        // tileSizeM=2, tehát W=1 → Z=2 és W=2 → Z=4. A vízszint Z=3.0f:
        // ez a sárga part és a zöld terepszint közötti félmagasság, itt hullámzik a felszín.
        private bool ShouldDrawStandingWater(Tile tile) => hydro.ShouldDrawStandingWater(tile);

        private bool CanRenderFallbackRiver(Tile tile) => hydro.CanRenderFallbackRiver(tile);

        // Két egymásra szuperponált hullám egy adott (x,y) pozícióra.
        // Amplitúdó szándékosan kicsi: Transport Tycoon-szerű, finoman remegő felszín.
        private const float WAVE_MAX = 0.36f;

        private float WaveAt(float x, float y, float t)
        {
            const float A1 = 0.20f, F1x = 0.028f, F1y = 0.021f, S1 = 0.95f;
            const float A2 = 0.11f, F2x = 0.052f, F2y = 0.044f, S2 = 1.75f;
            const float A3 = 0.05f, F3x = 0.094f, F3y = 0.070f, S3 = 3.20f;
            float swell  = A1 * (float)Math.Sin(t * S1 + x * F1x + y * F1y);
            float cross  = A2 * (float)Math.Sin(t * S2 - x * F2x + y * F2y + 0.8f);
            float ripple = A3 * (float)Math.Sin(t * S3 + x * F3x - y * F3y + 1.7f);
            return swell + cross + ripple;
        }

        private float GetShoreWaveFactor(float localDepth)
        {
            float usableDepth = Math.Max(0f, localDepth - MinimumWaterDepth);
            if (usableDepth <= 0f) return 0f;
            float normalized = Math.Min(1f, usableDepth / 0.85f);
            float rise = normalized * normalized * (3f - 2f * normalized);
            float fade = 1f - Math.Min(1f, Math.Max(0f, (usableDepth - 0.55f) / 0.80f));
            fade = fade * fade * (3f - 2f * fade);
            return rise * fade;
        }

        private float GetWaveMotionBudget(float localDepth)
        {
            float usableDepth = Math.Max(0f, localDepth - MinimumWaterDepth);
            if (usableDepth <= 0f) return 0f;
            float ramp = Math.Min(1f, usableDepth / 0.90f);
            ramp = ramp * ramp * (3f - 2f * ramp);
            float shore = GetShoreWaveFactor(localDepth);
            return Math.Min(usableDepth * (0.30f + shore * 0.18f),
                            0.025f + ramp * 0.34f + shore * 0.06f);
        }

        private float ApplyClampedWave(float x, float y, float baseWaterZ, float localDepth, float t)
        {
            float wave = WaveAt(x, y, t);
            float shore = GetShoreWaveFactor(localDepth);
            if (shore > 0f)
            {
                wave += 0.09f * shore * (float)Math.Sin(t * 4.1f + x * 0.115f - y * 0.082f + 0.4f)
                      + 0.05f * shore * (float)Math.Sin(t * 5.6f - x * 0.160f + y * 0.126f + 1.3f);
            }
            float budget = GetWaveMotionBudget(localDepth);
            return baseWaterZ + Math.Max(-budget, Math.Min(budget, wave));
        }

        private void WaterVertex(Node node, float wz, float t, Color baseColor)
        {
            float wave = WaveAt(node.xPos, node.yPos, t);
            float n = Math.Max(-1f, Math.Min(1f, wave / WAVE_MAX));
            int shift = (int)(n * 20f);
            GL.Color4(Color.FromArgb(baseColor.A,
                Math.Max(0, Math.Min(255, baseColor.R + shift)),
                Math.Max(0, Math.Min(255, baseColor.G + (int)(shift * 1.4f))),
                Math.Max(0, Math.Min(255, baseColor.B + (int)(shift * 0.6f)))));
            GL.Vertex3(node.xPos, node.yPos, wz);
        }

        // Per-node vízfelszín + hullám. Száraz node-nál SeaLevel-t ad vissza (terep alá esik,
        // a mélységteszt elrejti) – így nincs szükség polygon-vágásra.
        private float NodeWaterZ(Node node, float t)
        {
            float depth = nodeWaterDepth[node.Id];
            if (depth < MinimumWaterDepth) return SeaLevel;
            return ApplyClampedWave(node.xPos, node.yPos, node.zPos + depth, depth, t);
        }

        private float GetPolygonPointDepth(Tile tile, Vector3 point)
        {
            if (Math.Abs(point.X - tile.W.xPos) < 0.001f && Math.Abs(point.Y - tile.W.yPos) < 0.001f)
                return Math.Max(MinimumWaterDepth, nodeWaterDepth[tile.W.Id]);
            if (Math.Abs(point.X - tile.S.xPos) < 0.001f && Math.Abs(point.Y - tile.S.yPos) < 0.001f)
                return Math.Max(MinimumWaterDepth, nodeWaterDepth[tile.S.Id]);
            if (Math.Abs(point.X - tile.E.xPos) < 0.001f && Math.Abs(point.Y - tile.E.yPos) < 0.001f)
                return Math.Max(MinimumWaterDepth, nodeWaterDepth[tile.E.Id]);
            if (Math.Abs(point.X - tile.N.xPos) < 0.001f && Math.Abs(point.Y - tile.N.yPos) < 0.001f)
                return Math.Max(MinimumWaterDepth, nodeWaterDepth[tile.N.Id]);

            return MinimumWaterDepth;
        }

        private void DrawWater()
        {
            if (nodeWaterDepth == null) return;

            Color waterDeep    = Color.FromArgb(200,  38, 110, 180);
            Color waterShallow = Color.FromArgb(130,  70, 155, 215);
            Color waterGrid    = Color.FromArgb(150, 110, 180, 235);

            float t = (float)(Environment.TickCount64 % 628318) * 0.001f;

            GL.Enable(EnableCap.Blend);
            GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);

            // Smooth shading: per-vertex wave-based color gives shimmer effect
            GL.ShadeModel(ShadingModel.Smooth);

            GL.Begin(PrimitiveType.Quads);
            for (int u = 0; u < nodeCols - 1; u++)
            {
                for (int v = 0; v < nodeRows - 1; v++)
                {
                    Tile tile = getTileByCoords(u, v);
                    if (!HasDynamicWater(tile)) continue;

                    float wzN = NodeWaterZ(tile.N, t);
                    float wzS = NodeWaterZ(tile.S, t);
                    float wzE = NodeWaterZ(tile.E, t);
                    float wzW = NodeWaterZ(tile.W, t);

                    int wetCount = 0;
                    if (nodeWaterDepth[tile.N.Id] >= MinimumWaterDepth) wetCount++;
                    if (nodeWaterDepth[tile.S.Id] >= MinimumWaterDepth) wetCount++;
                    if (nodeWaterDepth[tile.E.Id] >= MinimumWaterDepth) wetCount++;
                    if (nodeWaterDepth[tile.W.Id] >= MinimumWaterDepth) wetCount++;

                    Color baseColor = wetCount >= 3 ? waterDeep : waterShallow;
                    WaterVertex(tile.W, wzW, t, baseColor);
                    WaterVertex(tile.S, wzS, t, baseColor);
                    WaterVertex(tile.E, wzE, t, baseColor);
                    WaterVertex(tile.N, wzN, t, baseColor);
                }
            }
            GL.End();

            GL.ShadeModel(ShadingModel.Flat);

            // Rácsvonalak csak mélyvíz tile-okon (mind a 4 sarok nedves)
            GL.Begin(PrimitiveType.Lines);
            for (int u = 0; u < nodeCols - 1; u++)
            {
                for (int v = 0; v < nodeRows - 1; v++)
                {
                    Tile tile = getTileByCoords(u, v);
                    if (nodeWaterDepth[tile.N.Id] < MinimumWaterDepth) continue;
                    if (nodeWaterDepth[tile.S.Id] < MinimumWaterDepth) continue;
                    if (nodeWaterDepth[tile.E.Id] < MinimumWaterDepth) continue;
                    if (nodeWaterDepth[tile.W.Id] < MinimumWaterDepth) continue;

                    float zwN = NodeWaterZ(tile.N, t);
                    float zwS = NodeWaterZ(tile.S, t);
                    float zwE = NodeWaterZ(tile.E, t);
                    float zwW = NodeWaterZ(tile.W, t);

                    GL.Color4(waterGrid);
                    GL.Vertex3(tile.W.xPos, tile.W.yPos, zwW); GL.Vertex3(tile.S.xPos, tile.S.yPos, zwS);
                    GL.Vertex3(tile.S.xPos, tile.S.yPos, zwS); GL.Vertex3(tile.E.xPos, tile.E.yPos, zwE);
                    GL.Vertex3(tile.E.xPos, tile.E.yPos, zwE); GL.Vertex3(tile.N.xPos, tile.N.yPos, zwN);
                    GL.Vertex3(tile.N.xPos, tile.N.yPos, zwN); GL.Vertex3(tile.W.xPos, tile.W.yPos, zwW);
                }
            }
            GL.End();

            GL.Disable(EnableCap.Blend);
        }

        private void DrawWaterWalls()
        {
            if (nodeWaterDepth == null) return;

            float t = (float)(Environment.TickCount64 % 628318) * 0.001f;

            GL.Enable(EnableCap.Blend);
            GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
            GL.ShadeModel(ShadingModel.Smooth);

            GL.Begin(PrimitiveType.Quads);
            for (int u = 0; u < nodeCols - 1; u++)
            {
                for (int v = 0; v < nodeRows - 1; v++)
                {
                    Tile tile = getTileByCoords(u, v);
                    if (!HasDynamicWater(tile)) continue;

                    // Edge W→S: szomszéd tile (u, v-1)
                    TryDrawWaterWall(tile.W, tile.S, checkTile(u, v - 1) ? getTileByCoords(u, v - 1) : null, t);
                    // Edge S→E: szomszéd tile (u+1, v)
                    TryDrawWaterWall(tile.S, tile.E, checkTile(u + 1, v) ? getTileByCoords(u + 1, v) : null, t);
                    // Edge E→N: szomszéd tile (u, v+1)
                    TryDrawWaterWall(tile.E, tile.N, checkTile(u, v + 1) ? getTileByCoords(u, v + 1) : null, t);
                    // Edge N→W: szomszéd tile (u-1, v)
                    TryDrawWaterWall(tile.N, tile.W, checkTile(u - 1, v) ? getTileByCoords(u - 1, v) : null, t);
                }
            }
            GL.End();

            GL.ShadeModel(ShadingModel.Flat);
            GL.Disable(EnableCap.Blend);
        }

        private void TryDrawWaterWall(Node a, Node b, Tile neighbor, float t)
        {
            if (neighbor != null && HasDynamicWater(neighbor)) return;

            float dA = nodeWaterDepth[a.Id];
            float dB = nodeWaterDepth[b.Id];
            if (dA < MinimumWaterDepth && dB < MinimumWaterDepth) return;

            float wzA = dA >= MinimumWaterDepth ? a.zPos + dA + WaveAt(a.xPos, a.yPos, t) : a.zPos;
            float wzB = dB >= MinimumWaterDepth ? b.zPos + dB + WaveAt(b.xPos, b.yPos, t) : b.zPos;

            if (wzA <= a.zPos + 0.02f && wzB <= b.zPos + 0.02f) return;

            // Vízfelszínnél: félátlátszó kék; aljnál: sötét mélykék
            GL.Color4(Color.FromArgb(155,  55, 130, 195)); GL.Vertex3(a.xPos, a.yPos, wzA);
            GL.Color4(Color.FromArgb(155,  55, 130, 195)); GL.Vertex3(b.xPos, b.yPos, wzB);
            GL.Color4(Color.FromArgb(225,   8,  35,  85)); GL.Vertex3(b.xPos, b.yPos, b.zPos);
            GL.Color4(Color.FromArgb(225,   8,  35,  85)); GL.Vertex3(a.xPos, a.yPos, a.zPos);
        }

        public bool SearchPoint(double x, double y, double radius)
        {
            int nodeU = (int)Math.Round((x + offsetX) / tileSizeH, 0);
            int nodeV = (int)Math.Round((y + offsetY) / tileSizeV, 0);

            if (checkNode(nodeU, nodeV))
            {
                actualNode = getNodeByCoords(nodeU, nodeV);

                onpos = true;
                return (onpos);
            }

            onpos = false;
            return (onpos);
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
            foreach (VertexBuffer vbo in vbos.Values) vbo.Dispose();
            vbos.Clear();
            edges.Dispose();
        }

        private void ElevationManager(int delta)
        {
            List<Node>  openList  = new List<Node>();
            List<Node>  closedList = new List<Node>();
            HashSet<Node> openSet   = new HashSet<Node>();
            HashSet<Node> closedSet = new HashSet<Node>();

            if (actualNode != null)
            {
                openList.Add(actualNode);
                openSet.Add(actualNode);

                while (openList.Count > 0)
                {
                    Node oNode = openList[0];
                    openList.RemoveAt(0);
                    openSet.Remove(oNode);

                    closedList.Add(oNode);
                    closedSet.Add(oNode);
                    // A legalsó szint (W=0) alá nem süllyedhet a terep.
                    oNode.W = Math.Max(0, oNode.W + delta);

                    foreach (Node nNode in getNeighbours(oNode))
                    {
                        if (closedSet.Contains(nNode) || openSet.Contains(nNode)) continue;

                        bool enqueue = delta > 0
                            ? oNode.W - nNode.W > +1
                            : oNode.W - nNode.W < -1;

                        if (enqueue)
                        {
                            openList.Add(nNode);
                            openSet.Add(nNode);
                        }
                    }
                }
                updateNodes(closedList);
            }
        }

        private void DrawTrees()
        {
            for (int u = 1; u < nodeCols - 1; u++)
            {
                for (int v = 1; v < nodeRows - 1; v++)
                {
                    Tile tile = getTileByCoords(u, v);
                    float moisture = tileMoisture[tile.Id];

                    // Víz, homok, folyó és legmagasabb csúcsokon nincs fa
                    if (ShouldDrawStandingWater(tile)) continue;
                    if (tile.Low <= 1) continue;
                    if (tile.Low >= 5) continue;
                    int tileRiverCorners = CountRiverCorners(tile);
                    if (tileRiverCorners >= 2) continue;
                    if (moisture < 0.35f || moisture > 0.95f) continue;

                    // Determinisztikus ritka elhelyezés (~minden 5. tile-ra)
                    int hash = unchecked(u * 374761393 ^ v * 1073741827);
                    int density = moisture >= 0.7f ? 3 : 5;
                    if ((hash & 0x7FFFFFFF) % density != 0) continue;

                    float cx      = (tile.W.xPos + tile.S.xPos + tile.E.xPos + tile.N.xPos) * 0.25f;
                    float cy      = (tile.W.yPos + tile.S.yPos + tile.E.yPos + tile.N.yPos) * 0.25f;
                    float groundZ = tile.Low * tileSizeM;          // tile legmélyebb sarka
                    float surfaceZ = Math.Max(Math.Max(tile.W.zPos, tile.S.zPos),
                                             Math.Max(tile.E.zPos, tile.N.zPos));  // legmagasabb sarok

                    DrawTree(cx, cy, groundZ, surfaceZ);
                }
            }
        }

        private void DrawTree(float x, float y, float groundZ, float surfaceZ)
        {
            // ── Törzs ──────────────────────────────────────────────────────────
            float tr       = 0.32f;          // törzs félszélessége
            float trunkBot = groundZ - 1.0f; // kicsit a látható talaj alá nyúl
            float trunkTop = surfaceZ + 0.6f;

            Color trunkL = Color.FromArgb(115, 72, 32);
            Color trunkD = Color.FromArgb( 80, 50, 20);

            GL.Begin(PrimitiveType.Quads);
            // Északi oldal (napos)
            GL.Color3(trunkL);
            GL.Vertex3(x - tr, y + tr, trunkBot); GL.Vertex3(x + tr, y + tr, trunkBot);
            GL.Vertex3(x + tr, y + tr, trunkTop); GL.Vertex3(x - tr, y + tr, trunkTop);
            // Keleti oldal (árnyékos)
            GL.Color3(trunkD);
            GL.Vertex3(x + tr, y + tr, trunkBot); GL.Vertex3(x + tr, y - tr, trunkBot);
            GL.Vertex3(x + tr, y - tr, trunkTop); GL.Vertex3(x + tr, y + tr, trunkTop);
            // Déli oldal (napos)
            GL.Color3(trunkL);
            GL.Vertex3(x + tr, y - tr, trunkBot); GL.Vertex3(x - tr, y - tr, trunkBot);
            GL.Vertex3(x - tr, y - tr, trunkTop); GL.Vertex3(x + tr, y - tr, trunkTop);
            // Nyugati oldal (árnyékos)
            GL.Color3(trunkD);
            GL.Vertex3(x - tr, y - tr, trunkBot); GL.Vertex3(x - tr, y + tr, trunkBot);
            GL.Vertex3(x - tr, y + tr, trunkTop); GL.Vertex3(x - tr, y - tr, trunkTop);
            GL.End();

            // ── Lombkorona: 3 rétegű alacsony-poly kúp ─────────────────────────
            float baseRadius  = 1.8f;
            float layerHeight = 2.4f;
            Color light = Color.FromArgb(55, 128, 42);
            Color dark  = Color.FromArgb(30,  85, 25);

            for (int layer = 0; layer < 3; layer++)
            {
                float baseZ = trunkTop + layer * layerHeight * 0.65f;
                float tipZ  = baseZ + layerHeight;
                float rad   = baseRadius * (1.0f - layer * 0.22f);

                float[] px = { x,       x + rad, x,       x - rad };
                float[] py = { y + rad, y,       y - rad, y       };

                GL.Begin(PrimitiveType.Triangles);
                for (int i = 0; i < 4; i++)
                {
                    int j = (i + 1) % 4;
                    GL.Color3(i == 0 || i == 3 ? light : dark);
                    GL.Vertex3(px[i], py[i], baseZ);
                    GL.Vertex3(px[j], py[j], baseZ);
                    GL.Vertex3(x, y, tipZ);
                }
                GL.End();
            }
        }
    }
}
