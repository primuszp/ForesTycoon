using System;
using System.Collections.Generic;
using System.Drawing;
using OpenTK;
using OpenTK.Graphics.OpenGL;

namespace ForesTycoon
{
    class Terrain
    {
        private readonly TerrainSettings settings;
        private readonly HashSet<int> riverNodeIds = new HashSet<int>();
        private readonly HashSet<int> standingWaterTileIds = new HashSet<int>();
        private readonly Dictionary<string, VertexBuffer> vbos = new Dictionary<string, VertexBuffer>();
        private readonly VertexBuffer edges = new VertexBuffer(PrimitiveType.Lines);
        private readonly List<uint> indices = new List<uint>();

        private Node[] nodes = null;
        private Tile[] tiles = null;

        private readonly int nodeRows;
        private readonly int nodeCols;

        private readonly int tileSizeH;
        private readonly int tileSizeV;
        private readonly int tileSizeM;

        private readonly int offsetX;
        private readonly int offsetY;

        private bool onpos = false;
        private Node actualNode;
        private Tile hoveredTile = null;

        private float[] tileMoisture = Array.Empty<float>();
        private bool suppressHydrologyRebuild = false;

        private float[] nodeWaterDepth;
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
            nodeRows = settings.NodeRows;
            nodeCols = settings.NodeColumns;
            tileSizeH = settings.TileWidth;
            tileSizeV = settings.TileHeight;
            tileSizeM = settings.HeightScale;
            offsetX = settings.OffsetX;
            offsetY = settings.OffsetY;

            makeNodes();
            makeTiles();
            makeQuads();

            GenerateTerrain();
            
            GL.LineWidth(2.0f);
        }

        // ── Perlin Noise – egyszerű 2D smooth noise .NET 3.5 kompatibilis ────────────────
        private int[] perm;

        private void InitNoise(int seed)
        {
            perm = new int[512];
            int[] p = new int[256];
            for (int i = 0; i < 256; i++) p[i] = i;
            Random rnd = new Random(seed);
            for (int i = 255; i > 0; i--)
            {
                int j = rnd.Next(i + 1);
                int tmp = p[i]; p[i] = p[j]; p[j] = tmp;
            }
            for (int i = 0; i < 512; i++) perm[i] = p[i & 255];
        }

        private double Fade(double t) { return t * t * t * (t * (t * 6 - 15) + 10); }
        private double Lerp(double a, double b, double t) { return a + t * (b - a); }
        private double Grad(int hash, double x, double y)
        {
            int h = hash & 3;
            double u = h < 2 ? x : y;
            double v = h < 2 ? y : x;
            return ((h & 1) == 0 ? u : -u) + ((h & 2) == 0 ? v : -v);
        }

        private double Noise2D(double x, double y)
        {
            int xi = (int)Math.Floor(x) & 255;
            int yi = (int)Math.Floor(y) & 255;
            double xf = x - Math.Floor(x);
            double yf = y - Math.Floor(y);
            double u = Fade(xf), v = Fade(yf);
            int aa = perm[perm[xi    ] + yi    ];
            int ab = perm[perm[xi    ] + yi + 1];
            int ba = perm[perm[xi + 1] + yi    ];
            int bb = perm[perm[xi + 1] + yi + 1];
            return Lerp(Lerp(Grad(aa, xf,     yf    ), Grad(ba, xf - 1, yf    ), u),
                        Lerp(Grad(ab, xf,     yf - 1), Grad(bb, xf - 1, yf - 1), u), v);
        }

        /// <summary>
        /// Fractál Brownian Motion: több oktavon összegzett zaj → termeszetesebb domborzat.
        /// </summary>
        private double FBM(double x, double y, int octaves, double persistence)
        {
            double val = 0, amp = 1, freq = 1, max = 0;
            for (int o = 0; o < octaves; o++)
            {
                val  += Noise2D(x * freq, y * freq) * amp;
                max  += amp;
                amp  *= persistence;
                freq *= 2.0;
            }
            return val / max;  // normált: kb. [-1, 1]
        }

        private void GenerateTerrain()
        {
            const int    MAX_HEIGHT = 6;
            const int    SEED       = 42;
            const double ROUGHNESS  = 0.58;  // kisebb = simább (TT-szerű)

            int size = nodeCols;  // 65 = 2^6 + 1
            Random rnd = new Random(SEED);
            double[,] h = new double[size, size];
            InitNoise(SEED);

            // ── 1. Diamond-Square ─────────────────────────────────────────────
            h[0,      0     ] = rnd.NextDouble();
            h[0,      size-1] = rnd.NextDouble();
            h[size-1, 0     ] = rnd.NextDouble();
            h[size-1, size-1] = rnd.NextDouble();

            double range = 1.0;
            for (int step = size - 1; step > 1; step /= 2)
            {
                int half = step / 2;
                range *= ROUGHNESS;

                // Diamond lépés: négyzetek közepei
                for (int x = 0; x < size - 1; x += step)
                    for (int y = 0; y < size - 1; y += step)
                    {
                        double avg = (h[x,y] + h[x+step,y] +
                                      h[x,y+step] + h[x+step,y+step]) * 0.25;
                        h[x+half, y+half] = avg + (rnd.NextDouble() * 2 - 1) * range;
                    }

                // Square lépés: rombuszok oldal-közepei
                for (int x = 0; x < size; x += half)
                    for (int y = ((x / half) % 2 == 0) ? half : 0; y < size; y += step)
                    {
                        double sum = 0; int cnt = 0;
                        if (x - half >= 0)   { sum += h[x-half, y]; cnt++; }
                        if (x + half < size) { sum += h[x+half, y]; cnt++; }
                        if (y - half >= 0)   { sum += h[x, y-half]; cnt++; }
                        if (y + half < size) { sum += h[x, y+half]; cnt++; }
                        h[x, y] = sum / cnt + (rnd.NextDouble() * 2 - 1) * range;
                    }
            }

            // ── 2. Normalizálás [0, 1]-re ─────────────────────────────────────
            double hMin = double.MaxValue, hMax = double.MinValue;
            for (int x = 0; x < size; x++)
                for (int y = 0; y < size; y++)
                {
                    if (h[x,y] < hMin) hMin = h[x,y];
                    if (h[x,y] > hMax) hMax = h[x,y];
                }
            double hRange = hMax - hMin;
            for (int x = 0; x < size; x++)
                for (int y = 0; y < size; y++)
                    h[x,y] = (h[x,y] - hMin) / hRange;

            // ── 3. Sziget falloff ─────────────────────────────────────────────
            for (int x = 0; x < size; x++)
                for (int y = 0; y < size; y++)
                {
                    double nx = x / (double)(size - 1);
                    double ny = y / (double)(size - 1);
                    double coastDrop = Math.Max(0.0, 0.32 - nx) * 2.4;
                    double mountainBand = FBM(nx * 2.2 + 4.0, ny * 2.8 + 9.0, 5, 0.55);
                    double ridge = Math.Max(0.0, mountainBand - 0.05) * 0.35;
                    double basinNoise = FBM(nx * 3.6 + 11.0, ny * 3.8 + 17.0, 4, 0.55);
                    double basinCarve = Math.Max(0.0, -basinNoise - 0.12) * 0.28;
                    double inlandRise = Math.Max(0.0, nx - 0.25) * 0.18;
                    h[x,y] = Math.Max(0.0, Math.Min(1.0, h[x,y] * 0.78 + ridge + inlandRise - coastDrop - basinCarve));
                }

            // ── 4. 3× simítás (TT cikk ajánlása) ─────────────────────────────
            for (int pass = 0; pass < 3; pass++)
            {
                double[,] s = new double[size, size];
                for (int x = 0; x < size; x++)
                    for (int y = 0; y < size; y++)
                    {
                        double sum = h[x,y]; int cnt = 1;
                        if (x > 0)       { sum += h[x-1,y]; cnt++; }
                        if (x < size-1)  { sum += h[x+1,y]; cnt++; }
                        if (y > 0)       { sum += h[x,y-1]; cnt++; }
                        if (y < size-1)  { sum += h[x,y+1]; cnt++; }
                        s[x,y] = sum / cnt;
                    }
                h = s;
            }

            // ── 5. Folyó generálás ────────────────────────────────────────────
            // Flow accumulation: minden node gyűjti a felette lefolyó vizet
            int[,] flowAcc  = ComputeFlowAccumulation(h, size);
            bool[,] isRiver = new bool[size, size];

            const int    RIVER_THRESHOLD = 55;   // min upstream node-szám a folyóhoz
            const double RIVER_CARVE     = 0.14; // mennyit vágunk le a magasságból

            for (int x = 0; x < size; x++)
                for (int y = 0; y < size; y++)
                    if (flowAcc[x,y] >= RIVER_THRESHOLD && h[x,y] > 0.08)
                    {
                        isRiver[x,y] = true;
                        h[x,y] = Math.Max(0.02, h[x,y] - RIVER_CARVE);
                    }

            // ── 6. Terasz + kvantizálás ───────────────────────────────────────
            int[,] targetW = new int[nodeCols, nodeRows];
            for (int u = 0; u < nodeCols; u++)
                for (int v = 0; v < nodeRows; v++)
                {
                    double val    = TerraceMap(h[u,v], MAX_HEIGHT);
                    int    height = (int)Math.Round(val * MAX_HEIGHT);
                    targetW[u,v] = Math.Max(0, Math.Min(MAX_HEIGHT, height));
                }

            // ── 7. ElevationManager – szomszéd-meredekség szabály ─────────────
            suppressHydrologyRebuild = true;
            try
            {
                for (int pass = 0; pass < MAX_HEIGHT; pass++)
                    for (int u = 0; u < nodeCols; u++)
                        for (int v = 0; v < nodeRows; v++)
                        {
                            Node node = getNodeByCoords(u, v);
                            if (node.W < targetW[u,v])
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

            // ── 8. River node-ok megjelölése ──────────────────────────────────
            riverNodeIds.Clear();
            for (int u = 0; u < nodeCols; u++)
                for (int v = 0; v < nodeRows; v++)
                    if (isRiver[u, v])
                        riverNodeIds.Add(getNodeByCoords(u, v).Id);

            RebuildHydrology();
            actualNode = null;
        }

        // D4 flow accumulation: minden node a legmélyebb szomszédjának adja a vizet
        private int[,] ComputeFlowAccumulation(double[,] h, int size)
        {
            int[,] acc = new int[size, size];
            for (int x = 0; x < size; x++)
                for (int y = 0; y < size; y++)
                    acc[x, y] = 1;

            // Magasság szerint csökkenő sorrend
            var order = new List<(int x, int y)>(size * size);
            for (int x = 0; x < size; x++)
                for (int y = 0; y < size; y++)
                    order.Add((x, y));
            order.Sort((a, b) => h[b.x, b.y].CompareTo(h[a.x, a.y]));

            int[] dx = { 0, 0, 1, -1 };
            int[] dy = { 1, -1, 0, 0 };

            foreach (var (x, y) in order)
            {
                double lowest = h[x, y];
                int bx = -1, by = -1;
                for (int d = 0; d < 4; d++)
                {
                    int nx = x + dx[d], ny = y + dy[d];
                    if (nx >= 0 && nx < size && ny >= 0 && ny < size && h[nx, ny] < lowest)
                    {
                        lowest = h[nx, ny];
                        bx = nx; by = ny;
                    }
                }
                if (bx >= 0) acc[bx, by] += acc[x, y];
            }
            return acc;
        }

        // Smooth-step terasz: síkokat kiszélesíti, lejtőket élesíti
        private double TerraceMap(double t, int levels)
        {
            double scaled = t * levels;
            double floor  = Math.Floor(scaled);
            double frac   = scaled - floor;
            double smooth = frac * frac * (3.0 - 2.0 * frac);
            return (floor + smooth) / levels;
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

        private void makeNodes()
        {
            nodes = new Node[nodeCols * nodeRows];

            for (int u = 0; u < nodeCols; u++)
            {
                for (int v = 0; v < nodeRows; v++)
                {
                    Node node = new Node(u * nodeRows + v);
                    node.U = u;
                    node.V = v;
                    node.W = 0;
                    node.xPos = (u * tileSizeH) - offsetX;
                    node.yPos = (v * tileSizeV) - offsetY;
                    node.zPos = (0 * tileSizeM);

                    nodes[node.Id] = node;
                }
            }
        }

        private void makeTiles()
        {
            tiles = new Tile[(nodeCols - 1) * (nodeRows - 1)];
            tileMoisture = new float[tiles.Length];

            for (var u = 0; u < nodeCols - 1; u++)
            {
                for (var v = 0; v < nodeRows - 1; v++)
                {
                    Node n = getNodeByCoords(u + 0, v + 1);
                    Node s = getNodeByCoords(u + 1, v + 0);
                    Node e = getNodeByCoords(u + 1, v + 1);
                    Node w = getNodeByCoords(u + 0, v + 0);

                    Tile tile = new Tile(n, s, e, w)
                    {
                        Id = u * (nodeRows - 1) + v
                    };

                    makeBuffer(tile.Code, tile.Low);
                    tiles[tile.Id] = tile;
                }
            }
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

        private Node getNodeByCoords(int u, int v)
        {
            return nodes[u * nodeRows + v];
        }

        private Tile getTileByCoords(int u, int v)
        {
            return tiles[u * (nodeRows - 1) + v];
        }

        private bool checkNode(int u, int v)
        {
            if ((u >= 0 && u < nodeCols) && (v >= 0 && v < nodeRows))
            {
                return (true);
            }
            return (false);
        }

        private bool checkTile(int u, int v)
        {
            if ((u >= 0 && u < (nodeCols - 1) && (v >= 0 && v < (nodeRows - 1))))
            {
                return (true);
            }
            return (false);
        }

        private int CountRiverCorners(Tile tile)
        {
            int count = 0;
            if (riverNodeIds.Contains(tile.W.Id)) count++;
            if (riverNodeIds.Contains(tile.S.Id)) count++;
            if (riverNodeIds.Contains(tile.E.Id)) count++;
            if (riverNodeIds.Contains(tile.N.Id)) count++;
            return count;
        }

        private IEnumerable<Tile> GetAdjacentTiles(Tile tile)
        {
            int tilesPerColumn = nodeRows - 1;
            int u = tile.Id / tilesPerColumn;
            int v = tile.Id % tilesPerColumn;

            if (checkTile(u - 1, v)) yield return getTileByCoords(u - 1, v);
            if (checkTile(u + 1, v)) yield return getTileByCoords(u + 1, v);
            if (checkTile(u, v - 1)) yield return getTileByCoords(u, v - 1);
            if (checkTile(u, v + 1)) yield return getTileByCoords(u, v + 1);
        }

        private bool IsBorderTile(Tile tile)
        {
            int tilesPerColumn = nodeRows - 1;
            int u = tile.Id / tilesPerColumn;
            int v = tile.Id % tilesPerColumn;
            return u == 0 || v == 0 || u == nodeCols - 2 || v == nodeRows - 2;
        }

        private void RebuildHydrology()
        {
            standingWaterTileIds.Clear();
            Array.Clear(tileMoisture, 0, tileMoisture.Length);

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
                    if (IsBorderTile(current)) touchesBorder = true;

                    foreach (Tile adjacent in GetAdjacentTiles(current))
                    {
                        if (!waterCandidates[adjacent.Id] || visited[adjacent.Id]) continue;
                        visited[adjacent.Id] = true;
                        open.Enqueue(adjacent);
                    }
                }

                if (!touchesBorder)
                {
                    foreach (Tile basinTile in basin)
                        standingWaterTileIds.Add(basinTile.Id);
                }
            }

            for (int i = 0; i < tiles.Length; i++)
            {
                Tile tile = tiles[i];
                float moisture = ShouldDrawStandingWater(tile) ? 1.0f : 0.0f;

                int riverCorners = CountRiverCorners(tile);
                if (riverCorners >= 2) moisture = Math.Max(moisture, 0.8f);

                foreach (Tile adjacent in GetAdjacentTiles(tile))
                {
                    if (ShouldDrawStandingWater(adjacent))
                        moisture = Math.Max(moisture, 0.6f);
                    else if (CountRiverCorners(adjacent) >= 2)
                        moisture = Math.Max(moisture, 0.45f);
                }

                if (tile.Low <= 2) moisture = Math.Max(moisture, 0.35f);
                tileMoisture[i] = moisture;
            }

            InitWaterFromHydrology();
        }

        private void InitWaterFromHydrology()
        {
            nodeWaterDepth = new float[nodes.Length];

            BuildGravityWaterFromTerrain();
        }

        private void BuildGravityWaterFromTerrain()
        {
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

            for (int u = 0; u < nodeCols; u++)
            {
                AddBoundary(getNodeByCoords(u, 0));
                AddBoundary(getNodeByCoords(u, nodeRows - 1));
            }
            for (int v = 1; v < nodeRows - 1; v++)
            {
                AddBoundary(getNodeByCoords(0, v));
                AddBoundary(getNodeByCoords(nodeCols - 1, v));
            }

            while (open.Count > 0)
            {
                Node node = open.Dequeue();
                if (settled[node.Id]) continue;

                settled[node.Id] = true;
                float currentLevel = spillLevel[node.Id];

                foreach (Node neighbor in getNeighbours(node))
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
                    nodeWaterDepth[i] = depth;
            }
        }

        private bool HasDynamicWater(Tile tile)
        {
            return nodeWaterDepth[tile.N.Id] >= MinimumWaterDepth
                || nodeWaterDepth[tile.S.Id] >= MinimumWaterDepth
                || nodeWaterDepth[tile.E.Id] >= MinimumWaterDepth
                || nodeWaterDepth[tile.W.Id] >= MinimumWaterDepth;
        }

        public void WaterFlowStep()
        {
            // A vizszintet a hidrologia ujraepitese szamolja a terepbol.
            // Ez a tick csak az animacio miatt marad meg; nem pumpal vagy mozgat vizet.
        }

        private bool CanWaterFlowBetween(Node a, Node b, float waterLevel)
        {
            float edgeLevel = GetEdgeBarrierLevel(a, b);
            if (waterLevel <= edgeLevel) return false;

            if (b.zPos < waterLevel)
                return true;

            foreach (Tile tile in GetSharedTiles(a, b))
            {
                if (IsRiverWaterTile(tile))
                {
                    if (waterLevel <= tile.Low * tileSizeM + RiverWaterHeight + 0.001f)
                        return true;
                    continue;
                }

                if (a.zPos < waterLevel && b.zPos < waterLevel)
                    return true;
            }

            return false;
        }

        private float GetEdgeBarrierLevel(Node a, Node b)
        {
            float edgeLevel = Math.Max(a.zPos, b.zPos) + 0.001f;

            foreach (Tile tile in GetSharedTiles(a, b))
            {
                if (!IsRiverWaterTile(tile)) continue;
                edgeLevel = Math.Min(edgeLevel, tile.Low * tileSizeM + RiverWaterHeight + 0.001f);
            }

            return edgeLevel;
        }

        private List<Tile> GetSharedTiles(Node a, Node b)
        {
            List<Tile> shared = new List<Tile>(2);
            foreach (Tile tile in getTilesByNode(a))
            {
                if (TileContainsNode(tile, b))
                    shared.Add(tile);
            }
            return shared;
        }

        private bool TileContainsNode(Tile tile, Node node)
        {
            return tile.W.Id == node.Id
                || tile.S.Id == node.Id
                || tile.E.Id == node.Id
                || tile.N.Id == node.Id;
        }

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

        private List<Node> getNeighbours(Node node)
        {
            List<Node> neighbors = new List<Node>();

            if (checkNode(node.U, node.V - 1)) neighbors.Add(getNodeByCoords(node.U, node.V - 1));
            if (checkNode(node.U + 1, node.V)) neighbors.Add(getNodeByCoords(node.U + 1, node.V));
            if (checkNode(node.U, node.V + 1)) neighbors.Add(getNodeByCoords(node.U, node.V + 1));
            if (checkNode(node.U - 1, node.V)) neighbors.Add(getNodeByCoords(node.U - 1, node.V));

            return (neighbors);
        }

        private List<Tile> getTilesByNode(Node node)
        {
            List<Tile> tiles = new List<Tile>();

            if (checkTile(node.U - 1, node.V - 1))
                tiles.Add(getTileByCoords(node.U - 1, node.V - 1));

            if (checkTile(node.U - 1, node.V - 0))
                tiles.Add(getTileByCoords(node.U - 1, node.V - 0));

            if (checkTile(node.U - 0, node.V - 1))
                tiles.Add(getTileByCoords(node.U - 0, node.V - 1));

            if (checkTile(node.U - 0, node.V - 0))
                tiles.Add(getTileByCoords(node.U - 0, node.V - 0));

            return (tiles);
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

        public void Draw(bool showNodeMarker)
        {
            // Bekapcsoljuk a PolygonOffsetFill-t, hogy a kitöltött poligonokat
            // kicsit "hátrafelé" tolja a mélységpufferben (Z-buffer), 
            // így a rájuk rajzolt vonalak nem fognak villogni (nincs Z-fighting).
            GL.Enable(EnableCap.PolygonOffsetFill);
            GL.PolygonOffset(1.0f, 1.0f);

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
            
            GL.Disable(EnableCap.PolygonOffsetFill);

            DrawSkirts();
            DrawWater();
            DrawWaterWalls();
            DrawRivers();
            DrawTrees();

            // Rácsvonal réteg – csúcspontok GRID_Z_BIAS-szal emelve, nincs Z-fighting
            edges.DrawElements();

            DrawHoveredTile();

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
            GL.Enable(EnableCap.PolygonOffsetFill);
            GL.PolygonOffset(1.0f, 1.0f);

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
            GL.Disable(EnableCap.PolygonOffsetFill);

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
            GL.Enable(EnableCap.PolygonOffsetFill);
            GL.PolygonOffset(-2.0f, -2.0f);

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

            GL.Disable(EnableCap.PolygonOffsetFill);

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
            foreach (Tile adjacent in GetAdjacentTiles(tile))
            {
                if (adjacent.Low < tile.Low) return false;
                if (adjacent.Low > tile.Low) hasHigherBank = true;
            }

            return hasHigherBank;
        }

        private bool ShouldDrawStandingWater(Tile tile)
        {
            return standingWaterTileIds.Contains(tile.Id) || tile.Low < 0
                || (nodeWaterDepth != null && HasDynamicWater(tile));
        }

        private bool HasAdjacentRiverBankPair(Tile tile)
        {
            bool westRiver = riverNodeIds.Contains(tile.W.Id);
            bool southRiver = riverNodeIds.Contains(tile.S.Id);
            bool eastRiver = riverNodeIds.Contains(tile.E.Id);
            bool northRiver = riverNodeIds.Contains(tile.N.Id);

            return (westRiver && southRiver)
                || (southRiver && eastRiver)
                || (eastRiver && northRiver)
                || (northRiver && westRiver);
        }

        private bool CanRenderFallbackRiver(Tile tile)
        {
            if (!IsRiverWaterTile(tile)) return false;
            if (tile.Low * tileSizeM >= SeaLevel) return false;
            return CountCornersBelowWater(tile) >= 2 && HasAdjacentWetBankPair(tile);
        }

        // Két egymásra szuperponált hullám egy adott (x,y) pozícióra.
        // Amplitúdó szándékosan kicsi: Transport Tycoon-szerű, finoman remegő felszín.
        private const float WAVE_MAX = 0.36f;

        private bool IsRiverWaterTile(Tile tile)
        {
            if (standingWaterTileIds.Contains(tile.Id)) return false;
            int rc = CountRiverCorners(tile);
            if (rc < 2) return false;
            if (rc == 2 && !HasAdjacentRiverBankPair(tile)) return false;
            return true;
        }

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
                    oNode.W = oNode.W + delta;

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

                if (nodeWaterDepth != null)
                    for (int i = 0; i < 30; i++) WaterFlowStep();
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
