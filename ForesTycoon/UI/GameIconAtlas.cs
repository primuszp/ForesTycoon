using System;
using System.Collections.Generic;
using ImGuiNET;
using V2 = System.Numerics.Vector2;

namespace ForesTycoon
{
    // Artwork is row-major, but generated spacing is optical rather than an exact grid.
    // Explicit source regions prevent neighbouring symbols bleeding into small toolbar icons.
    internal static class GameIconAtlas
    {
        internal const int Columns = 8, Rows = 6, IconCount = 47;
        private static readonly Dictionary<IntPtr, IntPtr> textures = new();
        private static readonly (V2 Min, V2 Max)[] regions = BuildRegions();

        private static (V2, V2)[] BuildRegions()
        {
            (int X, int Y, int Right, int Bottom)[] bounds = {
                (45,82,165,191), (220,65,357,200), (404,81,548,197), (588,47,735,211),
                (764,79,922,214), (973,78,1078,191), (1152,78,1243,189), (1302,89,1435,182),
                (38,277,184,354), (203,244,348,392), (394,252,544,384), (570,253,725,384),
                (754,258,922,391), (939,246,1104,392), (1125,241,1275,391), (1290,244,1440,391),
                (16,411,184,569), (200,418,364,562), (380,419,550,562), (567,432,734,554),
                (764,412,921,569), (965,421,1089,568), (1126,425,1270,561), (1294,425,1437,560),
                (48,598,144,740), (209,610,363,731), (398,581,539,751), (582,580,704,751),
                (766,587,892,751), (935,587,1097,751), (1124,592,1258,751), (1289,601,1439,751),
                (35,803,176,898), (209,778,352,920), (390,781,544,923), (604,778,681,915),
                (739,782,868,908), (902,794,1076,916), (1134,786,1219,924), (1286,782,1426,921),
                (27,948,191,1075), (208,948,371,1078), (388,956,547,1082), (562,944,719,1081),
                (736,939,902,1082), (913,938,1092,1078)
            };
            var result = new (V2, V2)[bounds.Length];
            for (int i = 0; i < result.Length; i++)
            {
                var b = bounds[i];
                // Measured from alpha-connected components, including disconnected bars/drops.
                // Two transparent source pixels retain antialiased edges and sampling clearance.
                result[i] = (new V2(b.X - 2, b.Y - 2), new V2(b.Right + 2, b.Bottom + 2));
            }
            return result;
        }

        internal static void Register(IntPtr context, IntPtr texture) => textures[context] = texture;
        internal static void Unregister(IntPtr context) => textures.Remove(context);

        internal static bool Draw(ImDrawListPtr draw, GameIcon icon, V2 origin, float size, uint ink)
        {
            if (!textures.TryGetValue(ImGui.GetCurrentContext(), out IntPtr texture)) return false;
            bool removeTimber = icon == GameIcon.TimberRemove;
            int index = (int)(removeTimber ? GameIcon.Timber : icon);
            if (index < 0 || index >= regions.Length) return false;
            var region = regions[index];
            var extent = region.Max - region.Min;
            var dimensions = new V2(1448, 1086);
            var uv0 = region.Min / dimensions;
            var uv1 = region.Max / dimensions;
            var drawSize = extent * (size / Math.Max(extent.X, extent.Y));
            var position = origin + (new V2(size) - drawSize) * 0.5f;
            // Artwork already carries semantic colours; the caller's opacity still applies.
            draw.AddImage(texture, position, position + drawSize, uv0, uv1, (ink & 0xff000000) | 0x00ffffff);
            if (removeTimber) GameIcons.DrawRemovalBadge(draw, origin, size, ink);
            return true;
        }
    }
}
