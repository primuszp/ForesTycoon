using System;
using System.Drawing;
using System.Globalization;

namespace ForesTycoon
{
    /// <summary>One horizon on the diorama's side: its thickness in world units (already scaled for display) and its colour.</summary>
    internal readonly record struct SideLayer(float Depth, Color Color);

    sealed partial class GameWorld
    {
        // Per soil profile of the running world's catalog, rebuilt when the scene or the soil catalog changes.
        private SideLayer[][] sideLayers;
        private (object Scene, object Catalog) sideLayersFor;

        /// <summary>
        /// The soil profile under a tile, top down, as the diorama's side draws it: the scene's horizons for the tile's soil
        /// (the built-in profile when the scene authors none), each thickened by the scene's profile scale.
        /// </summary>
        internal SideLayer[] SideProfile(int tile)
        {
            // While a new map replaces the old one the soil raster may still be the previous size.
            if (Soils == null || tile >= Soils.ProfileIndices.Length) return Array.Empty<SideLayer>();
            var catalog = Soils.Definition.Catalog;
            if (sideLayers == null || !ReferenceEquals(sideLayersFor.Scene, ruleProjectWorld) || !ReferenceEquals(sideLayersFor.Catalog, catalog)) {
                var scene = ruleProjectWorld ?? BuiltinZones;
                sideLayers = new SideLayer[catalog.Profiles.Length][];
                for (int i = 0; i < sideLayers.Length; i++) {
                    var soil = catalog[i];
                    string colour = $"#{soil.Color & 255:X2}{(soil.Color >> 8) & 255:X2}{(soil.Color >> 16) & 255:X2}";
                    var horizons = scene.SoilTypes.Find(s => s.Id == soil.Id)?.Profile() ?? SoilHorizon.Defaults(soil.Id, colour);
                    sideLayers[i] = horizons.ConvertAll(h => new SideLayer((float)(h.Depth * scene.ProfileScale), Hex(h.Color))).ToArray();
                }
                sideLayersFor = (ruleProjectWorld, catalog);
            }
            int index = Soils.ProfileIndices[tile];
            return index < sideLayers.Length ? sideLayers[index] : Array.Empty<SideLayer>();
        }

        internal static Color Hex(string hex) =>
            hex is { Length: 7 } && uint.TryParse(hex.AsSpan(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint rgb)
                ? Color.FromArgb((int)(rgb >> 16 & 255), (int)(rgb >> 8 & 255), (int)(rgb & 255)) : Color.Gray;
    }
}
