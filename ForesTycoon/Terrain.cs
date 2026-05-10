using System;
using System.Collections.Generic;
using System.Drawing;
using OpenTK;
using OpenTK.Graphics.OpenGL;

namespace ForesTycoon
{
    class Terrain
    {
        Node[] nodes = null;
        Tile[] tiles = null;

        int nodeRows = 65;   // 2^6 + 1  – Diamond-Square feltétel
        int nodeCols = 65;

        int tileSizeH = 5; //horizontal size
        int tileSizeV = 5; //vertical size
        int tileSizeM = 2; //mountain

        int offsetX = 50;
        int offsetY = 50;

        bool onpos = false;
        Node actualNode;
        Tile hoveredTile = null;

        HashSet<int> riverNodeIds = new HashSet<int>();
        HashSet<int> standingWaterTileIds = new HashSet<int>();
        float[] tileMoisture = Array.Empty<float>();
        bool suppressHydrologyRebuild = false;

        float[] nodeWaterDepth;
        private const float MIN_WATER_DEPTH = 0.04f;
        private const float WATER_EQUALIZE  = 0.45f;   // <0.5 → stabil, nem lő túl

        Dictionary<string, VertexBuffer> vbos = new Dictionary<string, VertexBuffer>();

        VertexBuffer edges = new VertexBuffer(PrimitiveType.Lines);
        List<uint> indices = new List<uint>();
        Vertex[] vertices = null;

        public Terrain()
        {
            offsetX = (tileSizeH * nodeCols) / 2;
            offsetY = (tileSizeV * nodeRows) / 2;

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
                    nodeWaterDepth[node.Id] = Math.Max(0f,
                        Math.Min(nodeWaterDepth[node.Id], WATER_Z - node.zPos));

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

                if (!touchesBorder || basin.Count >= 24)
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

            if (nodeWaterDepth == null)
                InitWaterFromHydrology();
        }

        private void InitWaterFromHydrology()
        {
            nodeWaterDepth = new float[nodes.Length];
            bool[] visited = new bool[nodes.Length];
            Queue<Node> queue = new Queue<Node>();

            // Seed only border nodes that are below ocean level
            for (int u = 0; u < nodeCols; u++)
            {
                TryAddOceanSeed(getNodeByCoords(u, 0),            queue, visited);
                TryAddOceanSeed(getNodeByCoords(u, nodeRows - 1), queue, visited);
            }
            for (int v = 1; v < nodeRows - 1; v++)
            {
                TryAddOceanSeed(getNodeByCoords(0,            v), queue, visited);
                TryAddOceanSeed(getNodeByCoords(nodeCols - 1, v), queue, visited);
            }

            // BFS: spread water only through nodes connected to the ocean border
            while (queue.Count > 0)
            {
                Node n = queue.Dequeue();
                nodeWaterDepth[n.Id] = WATER_Z - n.zPos;
                foreach (Node nb in getNeighbours(n))
                {
                    if (!visited[nb.Id] && nb.zPos < WATER_Z)
                    {
                        visited[nb.Id] = true;
                        queue.Enqueue(nb);
                    }
                }
            }
        }

        private void TryAddOceanSeed(Node n, Queue<Node> queue, bool[] visited)
        {
            if (!visited[n.Id] && n.zPos < WATER_Z)
            {
                visited[n.Id] = true;
                queue.Enqueue(n);
            }
        }

        private float GetTileWaterSurface(Tile tile)
        {
            float dN = nodeWaterDepth[tile.N.Id];
            float dS = nodeWaterDepth[tile.S.Id];
            float dE = nodeWaterDepth[tile.E.Id];
            float dW = nodeWaterDepth[tile.W.Id];

            if (dN < MIN_WATER_DEPTH && dS < MIN_WATER_DEPTH &&
                dE < MIN_WATER_DEPTH && dW < MIN_WATER_DEPTH)
                return float.NaN;

            float sum = 0f; int count = 0;
            if (dN >= MIN_WATER_DEPTH) { sum += tile.N.zPos + dN; count++; }
            if (dS >= MIN_WATER_DEPTH) { sum += tile.S.zPos + dS; count++; }
            if (dE >= MIN_WATER_DEPTH) { sum += tile.E.zPos + dE; count++; }
            if (dW >= MIN_WATER_DEPTH) { sum += tile.W.zPos + dW; count++; }
            return sum / count;
        }

        private bool HasDynamicWater(Tile tile)
        {
            return nodeWaterDepth[tile.N.Id] >= MIN_WATER_DEPTH
                || nodeWaterDepth[tile.S.Id] >= MIN_WATER_DEPTH
                || nodeWaterDepth[tile.E.Id] >= MIN_WATER_DEPTH
                || nodeWaterDepth[tile.W.Id] >= MIN_WATER_DEPTH;
        }

        public void WaterFlowStep()
        {
            if (nodeWaterDepth == null) return;

            int[] du = { 0, 1, 0, -1 };
            int[] dv = { -1, 0, 1, 0 };

            for (int pass = 0; pass < 4; pass++)
            {
                for (int i = 0; i < nodes.Length; i++)
                {
                    float depthA = nodeWaterDepth[i];
                    if (depthA < MIN_WATER_DEPTH) continue;

                    Node a = nodes[i];
                    float surfA = a.zPos + depthA;

                    for (int d = 0; d < 4; d++)
                    {
                        int nu = a.U + du[d], nv = a.V + dv[d];
                        if (!checkNode(nu, nv)) continue;

                        Node b = getNodeByCoords(nu, nv);
                        float surfB = b.zPos + nodeWaterDepth[b.Id];
                        if (surfA <= surfB + 0.001f) continue;

                        float diff = surfA - surfB;
                        float flow = Math.Min(diff * WATER_EQUALIZE,
                                              nodeWaterDepth[i] - MIN_WATER_DEPTH * 0.5f);
                        if (flow <= 0.001f) continue;

                        nodeWaterDepth[i]     -= flow;
                        nodeWaterDepth[b.Id]  += flow;
                        surfA -= flow;
                    }
                }
            }
        }

        private bool IsBelowWater(Node node, float waterLevel)
        {
            return node.zPos < waterLevel;
        }

        private Vector3 IntersectWaterEdge(Node a, Node b, float waterLevel)
        {
            float zA = a.zPos;
            float zB = b.zPos;
            float delta = zB - zA;
            float t = Math.Abs(delta) < 0.0001f ? 0.5f : (waterLevel - zA) / delta;
            t = Math.Max(0.0f, Math.Min(1.0f, t));

            return new Vector3(
                a.xPos + (b.xPos - a.xPos) * t,
                a.yPos + (b.yPos - a.yPos) * t,
                waterLevel);
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

        private List<Vector3> BuildClippedWaterPolygon(Tile tile, float waterLevel)
        {
            List<Vector3> polygon = new List<Vector3>(8);

            void AddEdge(Node start, Node end)
            {
                bool startWet = IsBelowWater(start, waterLevel);
                bool endWet = IsBelowWater(end, waterLevel);

                if (startWet)
                    AddWaterPolygonPoint(polygon, new Vector3(start.xPos, start.yPos, waterLevel));

                if (startWet != endWet)
                    AddWaterPolygonPoint(polygon, IntersectWaterEdge(start, end, waterLevel));
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
            return (((uint)color.A) << 24) | (((uint)color.B) << 16) + (((uint)color.G) << 8) + ((uint)color.R);
        }

        /// <summary>Kettő szín lin. interpolációja t ∈ [0,1] alapján.</summary>
        private uint LerpColor(Color a, Color b, float t)
        {
            int r = (int)(a.R + (b.R - a.R) * t);
            int g = (int)(a.G + (b.G - a.G) * t);
            int bl= (int)(a.B + (b.B - a.B) * t);
            return ColorToUInt(Color.FromArgb(255, r, g, bl));
        }

        public void Draw()
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
            DrawRivers();
            DrawTrees();

            // Rácsvonal réteg – csúcspontok GRID_Z_BIAS-szal emelve, nincs Z-fighting
            edges.DrawElements();

            DrawHoveredTile();

            if (onpos)
            {
                GL.PushMatrix();
                {
                    GL.Translate(actualNode.xPos, actualNode.yPos, actualNode.zPos);
                    DrawSphere(0.55f, 16, 16);
                }
                GL.PopMatrix();
                onpos = false;
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

        private void DrawRivers()
        {
            if (riverNodeIds.Count == 0) return;

            // A vízfelszín a folyómeder legmélyebb sarka FELETT lebeg – ugyanaz az elv
            // mint a tengerné: lapos átlátszó quad, alatta látszik a meder → mélység illúzió.
            // A meder maga a vágott terep (tileSizeM=2, tehát W=1 → Z=2).
            // Vízfelszín: mederalap + RIVER_WATER_H
            const float RIVER_WATER_H  = 1.2f;   // vízoszlop magassága a meder felett

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
                    if (ShouldDrawStandingWater(tile)) continue;

                    int rc = (riverNodeIds.Contains(tile.N.Id) ? 1 : 0)
                           + (riverNodeIds.Contains(tile.S.Id) ? 1 : 0)
                           + (riverNodeIds.Contains(tile.E.Id) ? 1 : 0)
                           + (riverNodeIds.Contains(tile.W.Id) ? 1 : 0);
                    if (rc < 2) continue;
                    if (rc == 2 && !HasAdjacentRiverBankPair(tile)) continue;

                    float baseZ = tile.Low * tileSizeM + RIVER_WATER_H;
                    float cx = (tile.W.xPos + tile.E.xPos) * 0.5f;
                    float cy = (tile.W.yPos + tile.N.yPos) * 0.5f;
                    // Folyónál felére csökkentett amplitúdó – gyorsabb, kisebb hullám
                    float wz = baseZ + WaveAt(cx, cy, t * 1.4f) * 0.45f;

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
                    if (ShouldDrawStandingWater(tile)) continue;

                    int rc = (riverNodeIds.Contains(tile.N.Id) ? 1 : 0)
                           + (riverNodeIds.Contains(tile.S.Id) ? 1 : 0)
                           + (riverNodeIds.Contains(tile.E.Id) ? 1 : 0)
                           + (riverNodeIds.Contains(tile.W.Id) ? 1 : 0);
                    if (rc < 4) continue;

                    float baseZ = tile.Low * tileSizeM + RIVER_WATER_H;
                    float ts = t * 1.4f;
                    float zwN = baseZ + WaveAt(tile.N.xPos, tile.N.yPos, ts) * 0.45f;
                    float zwS = baseZ + WaveAt(tile.S.xPos, tile.S.yPos, ts) * 0.45f;
                    float zwE = baseZ + WaveAt(tile.E.xPos, tile.E.yPos, ts) * 0.45f;
                    float zwW = baseZ + WaveAt(tile.W.xPos, tile.W.yPos, ts) * 0.45f;

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
            hoveredTile = null;

            GL.Enable(EnableCap.Blend);
            GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
            GL.Enable(EnableCap.PolygonOffsetFill);
            GL.PolygonOffset(-2.0f, -2.0f);

            // Sárga félig átlátszó fill
            GL.Begin(PrimitiveType.Quads);
            GL.Color4(Color.FromArgb(90, 255, 235, 60));
            GL.Vertex3(t.W.xPos, t.W.yPos, t.W.zPos);
            GL.Vertex3(t.S.xPos, t.S.yPos, t.S.zPos);
            GL.Vertex3(t.E.xPos, t.E.yPos, t.E.zPos);
            GL.Vertex3(t.N.xPos, t.N.yPos, t.N.zPos);
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
        // tileSizeM=2, tehát W=1 → Z=2. A vízszint Z=1.0f: a meder alján (Z=0) mélyen,
        // a parton (W=1, Z=2) meg sem jelenik — valódi sekélység/mélység átmenet.
        private const float WATER_Z = 1.0f;

        private int CountCornersBelowWater(Tile tile)
        {
            int count = 0;
            if (tile.W.zPos < WATER_Z) count++;
            if (tile.S.zPos < WATER_Z) count++;
            if (tile.E.zPos < WATER_Z) count++;
            if (tile.N.zPos < WATER_Z) count++;
            return count;
        }

        private bool HasAdjacentWetBankPair(Tile tile)
        {
            bool westWet = tile.W.zPos < WATER_Z;
            bool southWet = tile.S.zPos < WATER_Z;
            bool eastWet = tile.E.zPos < WATER_Z;
            bool northWet = tile.N.zPos < WATER_Z;

            return (westWet && southWet)
                || (southWet && eastWet)
                || (eastWet && northWet)
                || (northWet && westWet);
        }

        private bool HasWaterSurfaceCandidate(Tile tile)
        {
            int wetCorners = CountCornersBelowWater(tile);
            if (wetCorners < 2) return false;

            if (wetCorners == 2 && !HasAdjacentWetBankPair(tile)) return false;

            return true;
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

        // Két egymásra szuperponált hullám egy adott (x,y) pozícióra.
        // Amplitúdó szándékosan kicsi: Transport Tycoon-szerű, finoman remegő felszín.
        private float WaveAt(float x, float y, float t)
        {
            const float A1 = 0.30f, F1x = 0.040f, F1y = 0.028f, S1 = 1.3f;
            const float A2 = 0.14f, F2x = 0.065f, F2y = 0.058f, S2 = 2.1f;
            return A1 * (float)Math.Sin(t * S1 + x * F1x + y * F1y)
                 + A2 * (float)Math.Sin(t * S2 - x * F2x + y * F2y);
        }

        // Per-node vízfelszín + hullám. Száraz node-nál WATER_Z-t ad vissza (terep alá esik,
        // a mélységteszt elrejti) – így nincs szükség polygon-vágásra.
        private float NodeWaterZ(Node node, float t)
        {
            float depth = nodeWaterDepth[node.Id];
            if (depth < MIN_WATER_DEPTH) return WATER_Z;
            return node.zPos + depth + WaveAt(node.xPos, node.yPos, t);
        }

        private void DrawWater()
        {
            if (nodeWaterDepth == null) return;

            Color waterDeep    = Color.FromArgb(200,  45, 120, 190);
            Color waterShallow = Color.FromArgb(140,  80, 165, 220);
            Color waterGrid    = Color.FromArgb(180, 120, 190, 240);

            float t = (float)(Environment.TickCount64 % 628318) * 0.001f;

            GL.Enable(EnableCap.Blend);
            GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);

            // Nincs PolygonOffset a vízre: a terep PolygonOffset(+1,+1)-je miatt
            // a víz természetesen nyeri a partvonalnál a mélységtesztet,
            // a magasabb terepnél pedig a terep nyeri – automatikus, hézagmentes vágás.
            GL.Begin(PrimitiveType.Quads);
            for (int u = 0; u < nodeCols - 1; u++)
            {
                for (int v = 0; v < nodeRows - 1; v++)
                {
                    Tile tile = getTileByCoords(u, v);
                    if (!HasDynamicWater(tile)) continue;

                    // Per-NODE magasság: osztott csúcsok szomszéd tile-oknál
                    // AZONOS értéket kapnak → nulla hézag a tile határain
                    float wzN = NodeWaterZ(tile.N, t);
                    float wzS = NodeWaterZ(tile.S, t);
                    float wzE = NodeWaterZ(tile.E, t);
                    float wzW = NodeWaterZ(tile.W, t);

                    int wetCount = 0;
                    if (nodeWaterDepth[tile.N.Id] >= MIN_WATER_DEPTH) wetCount++;
                    if (nodeWaterDepth[tile.S.Id] >= MIN_WATER_DEPTH) wetCount++;
                    if (nodeWaterDepth[tile.E.Id] >= MIN_WATER_DEPTH) wetCount++;
                    if (nodeWaterDepth[tile.W.Id] >= MIN_WATER_DEPTH) wetCount++;

                    GL.Color4(wetCount >= 3 ? waterDeep : waterShallow);
                    GL.Vertex3(tile.W.xPos, tile.W.yPos, wzW);
                    GL.Vertex3(tile.S.xPos, tile.S.yPos, wzS);
                    GL.Vertex3(tile.E.xPos, tile.E.yPos, wzE);
                    GL.Vertex3(tile.N.xPos, tile.N.yPos, wzN);
                }
            }
            GL.End();

            // Rácsvonalak csak mélyvíz tile-okon (mind a 4 sarok nedves)
            GL.Begin(PrimitiveType.Lines);
            for (int u = 0; u < nodeCols - 1; u++)
            {
                for (int v = 0; v < nodeRows - 1; v++)
                {
                    Tile tile = getTileByCoords(u, v);
                    if (nodeWaterDepth[tile.N.Id] < MIN_WATER_DEPTH) continue;
                    if (nodeWaterDepth[tile.S.Id] < MIN_WATER_DEPTH) continue;
                    if (nodeWaterDepth[tile.E.Id] < MIN_WATER_DEPTH) continue;
                    if (nodeWaterDepth[tile.W.Id] < MIN_WATER_DEPTH) continue;

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
            List<Node> openList = new List<Node>();
            List<Node> closedList = new List<Node>();

            if (actualNode != null)
            {
                openList.Add(actualNode);

                while (openList.Count > 0)
                {
                    Node oNode = openList[0];
                    openList.Remove(oNode);

                    closedList.Add(oNode);
                    oNode.W = oNode.W + delta;

                    List<Node> Neighbours = getNeighbours(oNode);

                    foreach (Node nNode in Neighbours)
                    {
                        if (closedList.IndexOf(nNode) < 0 && openList.IndexOf(nNode) < 0)
                        {
                            if (delta > 0)
                            {
                                if (oNode.W - nNode.W > +1)
                                    openList.Add(nNode);
                            }
                            else
                            {
                                if (oNode.W - nNode.W < -1)
                                    openList.Add(nNode);
                            }
                        }
                    }
                }
                updateNodes(closedList);

                // Azonnali egyensúlyosítás: gyorsan feltölti az új mélyedéseket
                // ill. elvezeti a magasabbra emelt területről a vizet
                if (nodeWaterDepth != null)
                    for (int i = 0; i < 30; i++) WaterFlowStep();
            }
        }

        private void DrawTrees()
        {
            for (int u = 1; u < nodeCols - 2; u++)
            {
                for (int v = 1; v < nodeRows - 2; v++)
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
