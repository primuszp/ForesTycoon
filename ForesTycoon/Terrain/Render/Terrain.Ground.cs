using System;
using System.Drawing;
using OpenTK.Mathematics;

namespace ForesTycoon
{
    /// <summary>
    /// The ground's look: each tile's surface (colour and detail patterns) comes from the scene's biomes and ground materials;
    /// corners blend the colours of the tiles that meet there, so materials fade into each other instead of ending at tile
    /// edges, and a broad world-space variation breaks up large single-material areas.
    /// </summary>
    partial class Terrain
    {
        /// <summary>The ground of a tile; set by the world. Without it the ground keeps the height-band colours.</summary>
        internal Func<int, WorldScenario.GroundSurface> GroundOf;
        /// <summary>Changes whenever any tile's ground may have changed: the shader's ground map follows it.</summary>
        internal ulong GroundRevision { get; private set; } = 1;

        /// <summary>The scene's biomes or materials changed: recolour every chunk and resend the ground map.</summary>
        internal void InvalidateGround()
        {
            GroundRevision++;
            foreach (var geometry in staticTerrain.Values) geometry.Version = ulong.MaxValue;
        }

        private Color GroundColor(Tile tile)
        {
            if (GroundOf == null) return TerrainSurfaceColor(tile.Code, tile.Low);
            var g = GroundOf(tile.Id);
            return Color.FromArgb(255, (int)(g.R * 255), (int)(g.G * 255), (int)(g.B * 255));
        }

        /// <summary>The ground colour at a corner: the mean of the tiles meeting there, varied by a broad world-space noise.</summary>
        private Color CornerColor(Node node)
        {
            var shared = cornerTiles;
            int count = data.GetTilesByNode(node, shared);
            float r = 0, g = 0, b = 0;
            for (int i = 0; i < count; i++) { var c = GroundColor(shared[i]); r += c.R; g += c.G; b += c.B; }
            if (count == 0) return TerrainSurfaceColor("0000", 2);
            r /= count; g /= count; b /= count;
            // Two octaves over ~60 and ~23 world units: brightness ±9 %, a slight warm/cool drift.
            float n = .65f * ValueNoise(node.xPos / 60f, node.yPos / 60f, 11) + .35f * ValueNoise(node.xPos / 23f, node.yPos / 23f, 29);
            float light = .91f + .18f * n, warm = (ValueNoise(node.xPos / 85f, node.yPos / 85f, 47) - .5f) * .08f;
            return Color.FromArgb(255, Byte(r * light * (1 + warm)), Byte(g * light), Byte(b * light * (1 - warm)));
        }
        private readonly Tile[] cornerTiles = new Tile[4];
        private static int Byte(float v) => Math.Clamp((int)v, 0, 255);

        private static float ValueNoise(float x, float y, int seed)
        {
            int ix = (int)MathF.Floor(x), iy = (int)MathF.Floor(y);
            float tx = x - ix, ty = y - iy;
            tx = tx * tx * (3 - 2 * tx); ty = ty * ty * (3 - 2 * ty);
            float Corner(int cx, int cy)
            {
                uint h = unchecked((uint)(cx * 374761393 + cy * 668265263 + seed * 144269));
                h = unchecked((h ^ (h >> 13)) * 1274126177);
                return (h ^ (h >> 16)) / (float)uint.MaxValue;
            }
            float a = Corner(ix, iy), b = Corner(ix + 1, iy), c = Corner(ix, iy + 1), d = Corner(ix + 1, iy + 1);
            return (a + (b - a) * tx) * (1 - ty) + (c + (d - c) * tx) * ty;
        }

        /// <summary>
        /// The ground map for the surface shader, one RGBA texel per tile (x = column, y = row): the texture layers of the ground
        /// and the steep pattern, and the steep blend. Without a world the patterns are the plain grass layer.
        /// </summary>
        internal void FillGroundMap(byte[] rgba)
        {
            int columns = settings.TileColumns, rows = settings.TileRows;
            for (int id = 0; id < tiles.Length && id < columns * rows; id++)
            {
                var g = GroundOf?.Invoke(id) ?? default;
                int offset = ((id % rows) * columns + id / rows) * 4;
                rgba[offset] = (byte)Rendering.ProceduralSurfaceTextures.GroundLayer(g.Pattern);
                rgba[offset + 1] = (byte)Rendering.ProceduralSurfaceTextures.GroundLayer(g.SteepPattern);
                rgba[offset + 2] = (byte)Math.Clamp((int)(g.Steep * 255), 0, 255); rgba[offset + 3] = 255;
            }
        }
    }
}
