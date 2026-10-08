using System;

namespace ForesTycoon.Ecology
{
    internal sealed class SoilLandscapeDefinition
    {
        internal int GeneratorVersion { get; }
        internal SoilCatalog Catalog { get; }
        internal SoilLandscapeDefinition(int generatorVersion, SoilCatalog catalog)
        {
            if (generatorVersion != 0 && generatorVersion != 1) throw new NotSupportedException("Unsupported soil generator.");
            Catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            if (generatorVersion == 0 && (catalog.Profiles.Length != 1 || catalog[0].Properties != SoilProperties.Standard))
                throw new ArgumentException("The legacy soil model requires the standard profile.");
            GeneratorVersion = generatorVersion;
        }
        internal static SoilLandscapeDefinition Default { get; } = new(1, SoilCatalog.Default);
        internal static SoilLandscapeDefinition Legacy { get; } = new(0, SoilCatalog.Legacy);
    }

    /// <summary>Persistent categorical soil raster, generated once. Terrain edits never regenerate it.</summary>
    internal sealed class SoilLandscape
    {
        internal RasterField<byte> Profiles { get; }
        internal SoilLandscapeDefinition Definition { get; }
        internal ReadOnlySpan<byte> ProfileIndices => Profiles.Values;
        internal RasterGrid Grid => Profiles.Grid;
        internal SoilProfile Profile(int id) => Definition.Catalog[Profiles[id]];

        internal SoilLandscape(IForestHabitat habitat, SoilLandscapeDefinition definition)
        {
            ArgumentNullException.ThrowIfNull(habitat);
            Definition = definition ?? throw new ArgumentNullException(nameof(definition));
            var dimensions = habitat.TileGrid;
            if (dimensions.Columns <= 0 || dimensions.Rows <= 0 || (long)dimensions.Columns * dimensions.Rows != habitat.TileCount)
                throw new ArgumentException("Soil raster requires a complete column-major tile grid.");
            var geometry = habitat.GetForestTileGeometry(0);
            var grid = new RasterGrid(dimensions.Columns, dimensions.Rows, geometry.Width, geometry.Height);
            var profileIndices = new byte[habitat.TileCount];
            for (int id = 0; definition.GeneratorVersion != 0 && id < profileIndices.Length; id++)
            {
                double moisture = habitat.GetMoisture(id);
                if (!double.IsFinite(moisture) || moisture < 0 || moisture > 1)
                    throw new ArgumentException("Initial habitat moisture must be between 0 and 1.");
                double texture = Texture(habitat.Seed, id / grid.Rows, id % grid.Rows);
                double best = double.PositiveInfinity;
                for (int p = 0; p < definition.Catalog.Profiles.Length; p++)
                {
                    var profile = definition.Catalog[p];
                    double m = moisture - profile.PreferredMoisture, t = texture - profile.PreferredTexture;
                    double score = m * m + t * t;
                    if (score < best) { best = score; profileIndices[id] = (byte)p; }
                }
            }
            Profiles = new RasterField<byte>(grid,
                new("soil.profile", "Talajtípus", "profile-id", "soil.landscape", true), profileIndices);
        }

        private static double Texture(int seed, int column, int row)
        {
            // Generator v1: smoothly interpolate independent 12-tile geological patches.
            int x = column / 12, y = row / 12;
            double u = column % 12 / 12.0, v = row % 12 / 12.0;
            u = u * u * (3 - 2 * u); v = v * v * (3 - 2 * v);
            double a = Noise(seed, x, y), b = Noise(seed, x + 1, y);
            double c = Noise(seed, x, y + 1), d = Noise(seed, x + 1, y + 1);
            return (a + (b - a) * u) * (1 - v) + (c + (d - c) * u) * v;
        }
        private static double Noise(int seed, int x, int y)
        {
            uint h = unchecked((uint)seed ^ (uint)x * 0x9e3779b9u ^ (uint)y * 0x85ebca6bu ^ 0x736f696cu);
            h ^= h >> 16; h = unchecked(h * 0x7feb352du); h ^= h >> 15;
            h = unchecked(h * 0x846ca68bu); h ^= h >> 16;
            return h / (double)uint.MaxValue;
        }
    }

    /// <summary>Composes soil with live terrain queries without copying or owning terrain state.</summary>
    internal sealed class SoilHabitat(IForestHabitat terrain, SoilLandscape soils) : IForestHabitat
    {
        public int TileCount => terrain.TileCount;
        public int Seed => terrain.Seed;
        public ForestPattern ForestPattern => terrain.ForestPattern;
        public (int Columns, int Rows) TileGrid => terrain.TileGrid;
        public ForestTileGeometry GetForestTileGeometry(int id) => terrain.GetForestTileGeometry(id);
        public bool CanSupportForest(int id) => terrain.CanSupportForest(id);
        public float GetMoisture(int id) => terrain.GetMoisture(id);
        public SoilProperties GetSoilProperties(int id) => soils.Profile(id).Properties;
        public bool IsImpervious(int id) => terrain.IsImpervious(id);
        public bool IsWaterOutlet(int id) => terrain.IsWaterOutlet(id);
        public float GetNormalizedElevation(int id) => terrain.GetNormalizedElevation(id);
        public int GetAdjacentTileIds(int id, Span<int> destination) => terrain.GetAdjacentTileIds(id, destination);
    }
}
