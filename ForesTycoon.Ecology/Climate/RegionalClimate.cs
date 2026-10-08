using System;
using System.Text.Json.Serialization;
using System.Text.Json;

namespace ForesTycoon.Ecology
{
    /// <summary>Versioned gameplay climate configuration. Contrasts are amplitudes, not measured climatology.</summary>
    internal sealed record ClimateDefinition
    {
        public int GeneratorVersion { get; }
        public int RegionSizeTiles { get; }
        public double TemperatureContrast { get; }
        public double RainContrast { get; }
        public double ElevationCooling { get; }
        public double HumidityContrast { get; }
        internal static ClimateDefinition Legacy { get; } = new(0, 24, 0, 0, 0, 0);
        internal static ClimateDefinition Default { get; } = LoadDefault();

        private static ClimateDefinition LoadDefault()
        {
            using var stream = typeof(ClimateDefinition).Assembly.GetManifestResourceStream("ForesTycoon.Ecology.Climate.default.json")
                ?? throw new InvalidOperationException("Missing climate configuration.");
            return JsonSerializer.Deserialize<ClimateDefinition>(stream) ?? throw new InvalidOperationException("Empty climate configuration.");
        }

        [JsonConstructor]
        public ClimateDefinition(int generatorVersion, int regionSizeTiles, double temperatureContrast,
            double rainContrast, double elevationCooling, double humidityContrast)
        {
            if (generatorVersion != 0 && generatorVersion != 1) throw new NotSupportedException("Unsupported climate generator.");
            if (regionSizeTiles < 1 || regionSizeTiles > 4096) throw new ArgumentOutOfRangeException(nameof(regionSizeTiles));
            if (!double.IsFinite(temperatureContrast) || temperatureContrast < 0 || temperatureContrast > 30 ||
                !double.IsFinite(rainContrast) || rainContrast < 0 || rainContrast > .95 ||
                !double.IsFinite(elevationCooling) || elevationCooling < 0 || elevationCooling > 30 ||
                !double.IsFinite(humidityContrast) || humidityContrast < 0 || humidityContrast > .4)
                throw new ArgumentOutOfRangeException(nameof(temperatureContrast), "Climate contrasts are outside supported ranges.");
            if (generatorVersion == 0 && (regionSizeTiles != 24 || temperatureContrast != 0 || rainContrast != 0 ||
                elevationCooling != 0 || humidityContrast != 0)) throw new ArgumentException("Legacy climate must be uniform.");
            GeneratorVersion = generatorVersion; RegionSizeTiles = regionSizeTiles;
            TemperatureContrast = temperatureContrast; RainContrast = rainContrast;
            ElevationCooling = elevationCooling; HumidityContrast = humidityContrast;
        }
    }

    internal readonly record struct ClimateCell(WeatherForcing Forcing, double RainMultiplier);

    /// <summary>
    /// Persistent smooth regional anomalies, driven by the shared weather timeline. Terrain elevation
    /// is read live: terraform changes only local forcing, never the climate seed or simulation clock.
    /// </summary>
    internal sealed class RegionalClimate
    {
        private readonly IForestHabitat habitat;
        private readonly double[] temperature, humidity, rain;
        internal ClimateDefinition Definition { get; }
        internal bool Uniform => Definition.GeneratorVersion == 0;
        internal RasterGrid Grid { get; }
        internal RasterField<double> RainMultipliers { get; }
        internal RasterField<double> TemperatureAnomalies { get; }

        internal RegionalClimate(IForestHabitat habitat, ClimateDefinition definition)
        {
            this.habitat = habitat ?? throw new ArgumentNullException(nameof(habitat));
            Definition = definition ?? throw new ArgumentNullException(nameof(definition));
            temperature = new double[habitat.TileCount]; humidity = new double[habitat.TileCount]; rain = new double[habitat.TileCount];
            Array.Fill(rain, 1);
            var dimensions = habitat.TileGrid;
            if (dimensions.Columns <= 0 || dimensions.Rows <= 0 || (long)dimensions.Columns * dimensions.Rows != habitat.TileCount)
            {
                if (!Uniform) throw new ArgumentException("Regional climate requires a complete tile grid.");
                return;
            }
            var geometry = habitat.GetForestTileGeometry(0);
            Grid = new(dimensions.Columns, dimensions.Rows, geometry.Width, geometry.Height);
            if (!Uniform)
                for (int id = 0; id < habitat.TileCount; id++)
                {
                    int x = id / Grid.Rows, y = id % Grid.Rows;
                    double wetness = Pattern(habitat.Seed, x, y, 0x7261696eu);
                    temperature[id] = Pattern(habitat.Seed, x, y, 0x74656d70u) * definition.TemperatureContrast;
                    humidity[id] = wetness * definition.HumidityContrast;
                    rain[id] = 1 + wetness * definition.RainContrast;
                }
            TemperatureAnomalies = new(Grid, new("climate.temperature-anomaly", "Regionális hőmérséklet-eltérés", "°C", "climate", false), temperature);
            RainMultipliers = new(Grid, new("climate.rain-multiplier", "Regionális csapadékszorzó", "ratio", "climate", false), rain);
        }

        internal ClimateCell Cell(int id, WeatherForcing forcing)
        {
            if ((uint)id >= (uint)rain.Length) throw new ArgumentOutOfRangeException(nameof(id));
            if (Uniform) return new(forcing, 1);
            return new(forcing with {
                Temperature = forcing.Temperature + temperature[id] - habitat.GetNormalizedElevation(id) * Definition.ElevationCooling,
                Humidity = Math.Clamp(forcing.Humidity + humidity[id], 0, 1)
            }, rain[id]);
        }

        private double Pattern(int seed, int column, int row, uint channel)
        {
            int size = Definition.RegionSizeTiles, x = column / size, y = row / size;
            double u = column % size / (double)size, v = row % size / (double)size;
            u = u * u * (3 - 2 * u); v = v * v * (3 - 2 * v);
            double a = Noise(seed, x, y, channel), b = Noise(seed, x + 1, y, channel);
            double c = Noise(seed, x, y + 1, channel), d = Noise(seed, x + 1, y + 1, channel);
            return (a + (b - a) * u) * (1 - v) + (c + (d - c) * u) * v;
        }
        private static double Noise(int seed, int x, int y, uint channel)
        {
            uint h = unchecked((uint)seed ^ (uint)x * 0x9e3779b9u ^ (uint)y * 0x85ebca6bu ^ channel);
            h ^= h >> 16; h = unchecked(h * 0x7feb352du); h ^= h >> 15;
            h = unchecked(h * 0x846ca68bu); h ^= h >> 16;
            return h / (double)uint.MaxValue * 2 - 1;
        }
    }
}
