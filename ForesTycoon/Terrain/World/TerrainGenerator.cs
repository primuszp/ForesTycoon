using System;
using System.Collections.Generic;

namespace ForesTycoon
{
    /// <summary>
    /// Tisztán számítási domborzatgenerátor: Diamond-Square + FBM + sziget-falloff
    /// + flow-accumulation folyók + teraszolás. Nem ismeri sem a node-okat, sem az
    /// OpenGL-t; csak egy kvantált magasság- (targetW) és folyó-maszkot (isRiver) ad.
    /// </summary>
    sealed class TerrainGenerator
    {
        private const double Roughness = 0.58;   // kisebb = simább (TT-szerű)
        private const int RiverThreshold = 55;   // min upstream node-szám a folyóhoz
        private const double RiverCarve = 0.14;  // mennyit vágunk le a magasságból

        private readonly int seed;
        private int[] perm;

        public TerrainGenerator(int seed)
        {
            this.seed = seed;
        }

        /// <summary>Legenerálja a kvantált magasságot és a folyó-maszkot. size = nodeCols (négyzetes rács, 2^n+1).</summary>
        public void Generate(int nodeCols, int nodeRows, int maxHeight, out int[,] targetW, out bool[,] isRiver)
        {
            int size = nodeCols;
            Random rnd = new Random(seed);
            double[,] h = new double[size, size];
            InitNoise(seed);

            // ── 1. Diamond-Square ─────────────────────────────────────────────
            h[0, 0] = rnd.NextDouble();
            h[0, size - 1] = rnd.NextDouble();
            h[size - 1, 0] = rnd.NextDouble();
            h[size - 1, size - 1] = rnd.NextDouble();

            double range = 1.0;
            for (int step = size - 1; step > 1; step /= 2)
            {
                int half = step / 2;
                range *= Roughness;

                for (int x = 0; x < size - 1; x += step)
                    for (int y = 0; y < size - 1; y += step)
                    {
                        double avg = (h[x, y] + h[x + step, y] +
                                      h[x, y + step] + h[x + step, y + step]) * 0.25;
                        h[x + half, y + half] = avg + (rnd.NextDouble() * 2 - 1) * range;
                    }

                for (int x = 0; x < size; x += half)
                    for (int y = ((x / half) % 2 == 0) ? half : 0; y < size; y += step)
                    {
                        double sum = 0; int cnt = 0;
                        if (x - half >= 0) { sum += h[x - half, y]; cnt++; }
                        if (x + half < size) { sum += h[x + half, y]; cnt++; }
                        if (y - half >= 0) { sum += h[x, y - half]; cnt++; }
                        if (y + half < size) { sum += h[x, y + half]; cnt++; }
                        h[x, y] = sum / cnt + (rnd.NextDouble() * 2 - 1) * range;
                    }
            }

            // ── 2. Normalizálás [0, 1]-re ─────────────────────────────────────
            double hMin = double.MaxValue, hMax = double.MinValue;
            for (int x = 0; x < size; x++)
                for (int y = 0; y < size; y++)
                {
                    if (h[x, y] < hMin) hMin = h[x, y];
                    if (h[x, y] > hMax) hMax = h[x, y];
                }
            double hRange = hMax - hMin;
            for (int x = 0; x < size; x++)
                for (int y = 0; y < size; y++)
                    h[x, y] = (h[x, y] - hMin) / hRange;

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
                    h[x, y] = Math.Max(0.0, Math.Min(1.0, h[x, y] * 0.78 + ridge + inlandRise - coastDrop - basinCarve));
                }

            // ── 4. 3× simítás ─────────────────────────────────────────────────
            for (int pass = 0; pass < 3; pass++)
            {
                double[,] s = new double[size, size];
                for (int x = 0; x < size; x++)
                    for (int y = 0; y < size; y++)
                    {
                        double sum = h[x, y]; int cnt = 1;
                        if (x > 0) { sum += h[x - 1, y]; cnt++; }
                        if (x < size - 1) { sum += h[x + 1, y]; cnt++; }
                        if (y > 0) { sum += h[x, y - 1]; cnt++; }
                        if (y < size - 1) { sum += h[x, y + 1]; cnt++; }
                        s[x, y] = sum / cnt;
                    }
                h = s;
            }

            // ── 5. Folyó generálás ────────────────────────────────────────────
            int[,] flowAcc = ComputeFlowAccumulation(h, size);
            isRiver = new bool[size, size];
            for (int x = 0; x < size; x++)
                for (int y = 0; y < size; y++)
                    if (flowAcc[x, y] >= RiverThreshold && h[x, y] > 0.08)
                    {
                        isRiver[x, y] = true;
                        h[x, y] = Math.Max(0.02, h[x, y] - RiverCarve);
                    }

            // ── 6. Terasz + kvantizálás ───────────────────────────────────────
            targetW = new int[nodeCols, nodeRows];
            for (int u = 0; u < nodeCols; u++)
                for (int v = 0; v < nodeRows; v++)
                {
                    double val = TerraceMap(h[u, v], maxHeight);
                    int height = (int)Math.Round(val * maxHeight);
                    targetW[u, v] = Math.Max(0, Math.Min(maxHeight, height));
                }
        }

        // ── D4 flow accumulation ──────────────────────────────────────────────
        private static int[,] ComputeFlowAccumulation(double[,] h, int size)
        {
            int[,] acc = new int[size, size];
            for (int x = 0; x < size; x++)
                for (int y = 0; y < size; y++)
                    acc[x, y] = 1;

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

        private static double TerraceMap(double t, int levels)
        {
            double scaled = t * levels;
            double floor = Math.Floor(scaled);
            double frac = scaled - floor;
            double smooth = frac * frac * (3.0 - 2.0 * frac);
            return (floor + smooth) / levels;
        }

        // ── Perlin noise ──────────────────────────────────────────────────────
        private void InitNoise(int s)
        {
            perm = new int[512];
            int[] p = new int[256];
            for (int i = 0; i < 256; i++) p[i] = i;
            Random rnd = new Random(s);
            for (int i = 255; i > 0; i--)
            {
                int j = rnd.Next(i + 1);
                int tmp = p[i]; p[i] = p[j]; p[j] = tmp;
            }
            for (int i = 0; i < 512; i++) perm[i] = p[i & 255];
        }

        private static double Fade(double t) => t * t * t * (t * (t * 6 - 15) + 10);
        private static double Lerp(double a, double b, double t) => a + t * (b - a);

        private static double Grad(int hash, double x, double y)
        {
            int hh = hash & 3;
            double u = hh < 2 ? x : y;
            double v = hh < 2 ? y : x;
            return ((hh & 1) == 0 ? u : -u) + ((hh & 2) == 0 ? v : -v);
        }

        private double Noise2D(double x, double y)
        {
            int xi = (int)Math.Floor(x) & 255;
            int yi = (int)Math.Floor(y) & 255;
            double xf = x - Math.Floor(x);
            double yf = y - Math.Floor(y);
            double u = Fade(xf), v = Fade(yf);
            int aa = perm[perm[xi] + yi];
            int ab = perm[perm[xi] + yi + 1];
            int ba = perm[perm[xi + 1] + yi];
            int bb = perm[perm[xi + 1] + yi + 1];
            return Lerp(Lerp(Grad(aa, xf, yf), Grad(ba, xf - 1, yf), u),
                        Lerp(Grad(ab, xf, yf - 1), Grad(bb, xf - 1, yf - 1), u), v);
        }

        private double FBM(double x, double y, int octaves, double persistence)
        {
            double val = 0, amp = 1, freq = 1, max = 0;
            for (int o = 0; o < octaves; o++)
            {
                val += Noise2D(x * freq, y * freq) * amp;
                max += amp;
                amp *= persistence;
                freq *= 2.0;
            }
            return val / max;
        }
    }
}
