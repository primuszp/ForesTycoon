using System;
using System.Collections.Generic;
using System.Drawing;
using OpenTK.Mathematics;

namespace ForesTycoon
{
    partial class Terrain
    {
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
            VertexBuffer vbo  = new VertexBuffer(PrimitiveTopology.Triangles);

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

            try
            {
                vbo.SetData(data.ToArray());
                vbos.Add(key, vbo);
            }
            catch { vbo.Dispose(); throw; }
        }

        private void fillColor(string code, int low, ref List<Vertex> data)
        {
            Color surfaceColor = TerrainSurfaceColor(code, low);

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

                int r = (int)(surfaceColor.R * shade);
                int g = (int)(surfaceColor.G * shade);
                int b = (int)(surfaceColor.B * shade);
                uint color = ColorToUInt(Color.FromArgb(255, r, g, b));

                int end = Math.Min(start + stride, data.Count);
                for (int i = start; i < end; i++)
                    data[i] = setColor(data[i], color);
            }
        }

        private static Color TerrainSurfaceColor(string code, int low)
        {
            int sumH = 0;
            for (int i = 0; i < code.Length; i++) sumH += (int)char.GetNumericValue(code[i]);
            float t = Math.Min(sumH / 8.0f, 1.0f);

            GetTerrainColorRange(low, out Color baseLow, out Color baseHigh);
            int r = (int)(baseLow.R + (baseHigh.R - baseLow.R) * t);
            int g = (int)(baseLow.G + (baseHigh.G - baseLow.G) * t);
            int b = (int)(baseLow.B + (baseHigh.B - baseLow.B) * t);
            return Color.FromArgb(255, r, g, b);
        }

        private static void GetTerrainColorRange(int low, out Color baseLow, out Color baseHigh)
        {
            // Biome base colors by absolute elevation (low = tile's minimum corner W)
            if (low <= 1)
            {
                baseLow  = Color.FromArgb(200, 178, 108);
                baseHigh = Color.FromArgb(222, 202, 138);
            }
            else if (low == 2)
            {
                baseLow  = Color.FromArgb( 95, 148, 42);
                baseHigh = Color.FromArgb(145, 195, 68);
            }
            else if (low == 3)
            {
                baseLow  = Color.FromArgb(118, 158, 45);
                baseHigh = Color.FromArgb(168, 205, 72);
            }
            else if (low == 4)
            {
                baseLow  = Color.FromArgb(155, 155, 55);
                baseHigh = Color.FromArgb(192, 185, 80);
            }
            else if (low == 5)
            {
                baseLow  = Color.FromArgb(138, 138, 72);
                baseHigh = Color.FromArgb(170, 162, 90);
            }
            else
            {
                baseLow  = Color.FromArgb(175, 165, 148);
                baseHigh = Color.FromArgb(230, 228, 222);
            }
        }

        private Vertex setColor(Vertex vertex, uint color)
        {
            vertex.Color = color;
            return vertex;
        }

        private void makeTiles()
        {
            foreach (Tile tile in tiles)
                makeBuffer(tile.Code, tile.Low);
        }

        // The map re-derived its own data; here only the GPU copies follow the new heights.
        private void OnNodesChanged(IReadOnlyList<Node> changed)
        {
            Tile[] nodeTiles = new Tile[4];
            foreach (Node node in changed)
            {
                int nodeTileCount = data.GetTilesByNode(node, nodeTiles);
                for (int i = 0; i < nodeTileCount; i++)
                {
                    Tile tile = nodeTiles[i];
                    string code = tile.Code;
                    if (!vbos.ContainsKey(code + "_" + tile.Low))
                        makeBuffer(code, tile.Low);
                    if (map.IsDiagonalFlipped(tile.Id) && !vbos.ContainsKey(code + "_" + tile.Low + "_f"))
                        makeBuffer(code, tile.Low, true);
                }
            }
        }

        private void OnRoadDiagonalsChanged()
        {
            foreach (int id in map.FlippedDiagonalTiles)
            {
                Tile inner = tiles[id];
                if (!vbos.ContainsKey(inner.Code + "_" + inner.Low + "_f"))
                    makeBuffer(inner.Code, inner.Low, true);
            }
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
    }
}
