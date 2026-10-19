using System;
using ForesTycoon.Rules;
using System.Collections.Generic;
using System.Linq;

namespace ForesTycoon.Ecology
{
    internal readonly record struct EnvironmentCell(double Canopy, double Surface, double Soil, double Deep,
        double Drought, double Waterlogging, double GrowthFactor, double Capacity);

    // The water stores of every cell (mm) and the weather that drives them. The stores change only through the project's
    // stock-and-flow rules (rain, interception, infiltration, evaporation, uptake, drainage, runoff); this system keeps the
    // weather, the derived drought and waterlogging, wind-drifted snow and the water balance.
    internal sealed partial class EnvironmentSystem : IForestEnvironment
    {
        internal const double SecondsPerForestYear = EcologyTime.SecondsPerForestYear;
        internal const double DefaultGameSecondsPerYear = EcologyTime.DefaultGameSecondsPerYear;
        internal static bool IsValidForestYearSeconds(double seconds) => EcologyTime.IsValidForestYearSeconds(seconds);
        internal const double HoursPerSecond = EcologyTime.HoursPerSecond;
        internal const double StepSeconds = EcologyTime.StepSeconds;
        internal double HoursPerStep => StepSeconds * weather.HoursPerSecond;
        internal double Cover(int id) => (uint)id < (uint)CellCount ? vegetation[id].Cover : 0;
        private double[] springRates;
        private double springReceived;
        /// <summary>Water springs add to these tiles' surface (mm per environment hour): sources of streams and rivers.</summary>
        internal IReadOnlyDictionary<int, double> Springs
        {
            get => springRates == null ? new Dictionary<int, double>() : Enumerable.Range(0, CellCount).Where(i => springRates[i] > 0).ToDictionary(i => i, i => springRates[i]);
            set
            {
                if (value == null || value.Count == 0) { springRates = null; return; }
                var rates = new double[CellCount];
                foreach (var (tile, rate) in value)
                    if ((uint)tile < (uint)CellCount && double.IsFinite(rate) && rate > 0) rates[tile] = rate;
                springRates = rates;
            }
        }
        internal double SpringReceived => springReceived;
        private EcologyResponseProgram responseRules;
        internal EcologyResponseProgram ResponseRules {
            get => responseRules;
            set {
                responseRules = value;
                weather.ResponseRules = value; Climate.ResponseRules = value;
                RefreshLocalClimate();
            }
        }
        private ForesTycoon.Rules.EcologyResponseProgram Responses => ResponseRules ?? ForesTycoon.Rules.EcologyResponseProgram.Default;
        private readonly IForestHabitat habitat;
        private readonly IForestCanopy forest;
        private readonly double[] canopy, surface, soil, deep, drought, wet, wetIntegral, snow;
        private readonly SurfaceWaterFlux snowFlux;
        private readonly SoilProperties[] soils;
        private readonly ForestHydrologyInputs[] vegetation;
        private ulong vegetationRevision = ulong.MaxValue;
        private double remainder, monthSeconds, radiationIntegral;
        private readonly WeatherSystem weather;
        private double regionalRainReceived;
        internal RegionalClimate Climate { get; }
        internal double RainReceived => regionalRainReceived;
        internal ClimateCell ClimateAt(int id) => Climate.Cell(id, new(Radiation, Temperature, RelativeHumidity, WindSpeed));
        internal double ForestYearSeconds => weather.ForestYearSeconds;

        internal double Time => weather.Time;
        internal double EventStart => weather.EventStart;
        internal double EventEnd => weather.EventEnd;
        internal WeatherPreset Preset => weather.Preset;
        internal double PeakRain => weather.PeakRain;
        internal double RainRate => weather.RainRate;
        internal double EventRain => weather.EventRain;
        internal double ExpectedEventRain => weather.ExpectedEventRain;
        internal double TotalRain => weather.TotalRain;
        internal double Radiation => weather.Radiation;
        internal double PeriodRadiation => monthSeconds > 0 ? radiationIntegral / monthSeconds : Radiation;
        internal double Temperature => weather.Temperature;
        internal double RelativeHumidity => weather.Humidity;
        internal double WindSpeed => weather.Wind;
        internal double Cloud => weather.Cloud;
        internal double Evaporated { get; private set; }
        internal double Transpired { get; private set; }
        internal double Outflow { get; private set; }
        internal double InitialWater { get; private set; }
        internal ulong Revision { get; private set; }
        internal int CellCount => soil.Length;
        internal double MeanWetness { get; private set; }
        internal double MeanSoil { get; private set; }
        internal double MeanSnowCover { get; private set; }
        internal double SnowWater => System.Linq.Enumerable.Sum(snow);
        internal double SnowWaterAt(int id) => snow[id];
        internal double SnowCoverAt(int id) => Math.Clamp(snow[id] / 6, 0, 1);
        internal double LiquidRainRate => Preset == WeatherPreset.Snow ? 0 : RainRate;
        internal double SnowfallRate => Preset == WeatherPreset.Snow ? RainRate : 0;
        internal double StoredWater
        {
            get
            {
                double total = 0;
                for (int i = 0; i < CellCount; i++) total += canopy[i] + surface[i] + soil[i] + deep[i] + snow[i];
                return total;
            }
        }
        internal double BalanceError => StoredWater - (InitialWater + RainReceived - rainLeftToRules + springReceived + ruleExchanged - Evaporated - Transpired - Outflow);
        /// <summary>The weather's precipitation, which the rules let in (their own entries are booked in <see cref="RuleExchanged"/>).</summary>
        private double rainLeftToRules;
        internal double RainLeftToRules => rainLeftToRules;
        /// <summary>Hours of the environment's clock in one forest year: converts the rules' hourly rates.</summary>
        internal static double HoursPerForestYear => HoursPerSecond * SecondsPerForestYear;
        /// <summary>The current weather as felt on a tile (regional climate): temperature, potential evaporation, rain share.</summary>
        /// <remarks>Rules read it for every tile several times a step, so it is kept per tile until the weather moves on
        /// (an environment step, forced weather) or <see cref="RefreshLocalClimate"/> (terrain may have changed).</remarks>
        internal ClimateCell LocalClimate(int id)
        {
            if ((uint)id >= (uint)CellCount) return Climate.Cell(id, new WeatherForcing(weather.Radiation, weather.Temperature, weather.Humidity, weather.Wind));
            localClimateStamp ??= new ulong[CellCount]; localClimate ??= new ClimateCell[CellCount];
            if (localClimateStamp[id] != localClimateEpoch)
            {
                localClimate[id] = Climate.Cell(id, new WeatherForcing(weather.Radiation, weather.Temperature, weather.Humidity, weather.Wind));
                localClimateStamp[id] = localClimateEpoch;
                if (localClimateErrors != null) localClimateErrors[id] = null;
            }
            Responses.PublishQueryError(localClimateErrors?[id]);
            return localClimate[id];
        }
        private ClimateCell[] localClimate; private ulong[] localClimateStamp; private ulong localClimateEpoch = 1;
        /// <summary>Forgets the kept local weather: the next reading of every tile computes it again.</summary>
        internal void RefreshLocalClimate() => localClimateEpoch++;
        internal bool VegetationInputsCurrent => forest == null || vegetationRevision == forest.Revision;
        internal void InvalidateVegetation() => vegetationRevision = ulong.MaxValue;
        internal ForestHydrologyInputs HydrologyInputs(int id)
        {
            if ((uint)id >= (uint)CellCount) return default;
            // A read during a suspended edit must neither rebuild the whole raster nor publish a partial cache.
            return forest != null && vegetationRevision != forest.Revision ? forest.HydrologyInputs(id) : vegetation[id];
        }
        internal double SpringRate(int id) => springRates != null && (uint)id < (uint)springRates.Length ? springRates[id] : 0;
        private double ruleExchanged;
        /// <summary>Counts direct writes to the water stores; a cached reading of a tile is stale once it changes.</summary>
        internal ulong WriteRevision { get; private set; }
        /// <summary>Water rules added (positive) or took away (negative) by writing a tile's stores directly.</summary>
        internal double RuleExchanged => ruleExchanged;

        internal enum WaterStore { Surface, Soil, Deep, Canopy, Snow }
        /// <summary>Sets one water store of a tile (mm, not below 0), as a rule writes it; the change is booked in the balance.</summary>
        internal void SetWater(int id, WaterStore store, double millimetres)
        {
            if ((uint)id >= (uint)CellCount || !double.IsFinite(millimetres)) return;
            var target = store switch { WaterStore.Surface => surface, WaterStore.Soil => soil, WaterStore.Canopy => canopy, WaterStore.Snow => snow, _ => deep };
            // The root zone holds at most its saturation, the crowns their interception capacity.
            double cap = WaterCapacity(id, store);
            double value = NormalizeWater(target[id], millimetres, cap);
            ruleExchanged += value - target[id];
            target[id] = value;
            WriteRevision++;
        }

        internal double WaterCapacity(int id, WaterStore store) => store switch {
            WaterStore.Soil => soils[id].Saturation, WaterStore.Canopy => HydrologyInputs(id).InterceptionCapacity,
            _ => double.PositiveInfinity
        };
        internal static double NormalizeWater(double current, double requested, double capacity) =>
            double.IsFinite(requested) ? Math.Max(0, Math.Min(requested, capacity)) : current;

        double IForestEnvironment.Radiation => Radiation;
        double IForestEnvironment.PeriodRadiation => PeriodRadiation;
        double IForestEnvironment.GrowthFactor(int tileId, ForestSpecies species) => GrowthFactor(tileId, species);
        double IForestEnvironment.CurrentWaterFactor(int tileId, ForestSpecies species) => CurrentWaterFactor(tileId, species);

        internal EnvironmentSystem(IForestHabitat habitat, IForestCanopy forest, double forestYearSeconds = SecondsPerForestYear,
            ClimateDefinition climate = null, bool deferInitialization = false, EcologyResponseProgram responseRules = null)
        {
            this.habitat = habitat ?? throw new ArgumentNullException(nameof(habitat));
            this.forest = forest;
            Climate = new RegionalClimate(habitat, climate ?? ClimateDefinition.Legacy, deferInitialization: true);
            weather = new WeatherSystem(habitat.Seed, forestYearSeconds, responseRules);
            ResponseRules = responseRules;
            int n = habitat.TileCount;
            canopy = new double[n]; surface = new double[n]; soil = new double[n]; deep = new double[n];
            snow = new double[n];
            drought = new double[n]; wet = new double[n]; wetIntegral = new double[n];
            snowFlux = new SurfaceWaterFlux(n);
            soils = new SoilProperties[n]; vegetation = new ForestHydrologyInputs[n];
            if (!deferInitialization) SimulationWork.Complete(InitializeWork());
        }

        internal IEnumerable<int> InitializeWork()
        {
            foreach (int unit in Climate.InitializeWork()) yield return unit;
            for (int i = 0; i < CellCount; i++)
            {
                yield return 1;
                soils[i] = habitat.GetSoilProperties(i);
                soils[i].Validate();
                soil[i] = Math.Clamp(45 + 100 * habitat.GetMoisture(i), 0, soils[i].Saturation);
                deep[i] = 20;
            }
            double total = 0;
            for (int i = 0; i < CellCount; i++) { total += canopy[i] + surface[i] + soil[i] + deep[i] + snow[i]; yield return 1; }
            InitialWater = total;
            foreach (int unit in SummarizeWork()) yield return unit;
        }

        internal void ForceWeather(WeatherPreset preset, int peak = -1, int duration = -1)
        {
            weather.ForceWeather(preset, peak, duration);
            RefreshLocalClimate();
        }
        internal void Update(double seconds)
        {
            if (!double.IsFinite(seconds) || seconds < 0) throw new ArgumentOutOfRangeException(nameof(seconds));
            remainder += seconds;
            while (remainder + 1e-10 >= StepSeconds)
            {
                remainder -= StepSeconds;
                Advance(StepSeconds);
            }
        }
        internal void AdvanceStep(double seconds) => Advance(seconds);
        internal IEnumerable<int> AdvanceStepWork(double seconds) => AdvanceWork(seconds);

        private void Advance(double seconds) => SimulationWork.Complete(AdvanceWork(seconds));

        private IEnumerable<int> AdvanceWork(double seconds)
        {
            while (seconds > 1e-9)
            {
                var interval = weather.AdvanceInterval(seconds);
                foreach (int work in WaterStepWork(interval)) yield return work;
                seconds -= interval.Seconds;
            }
            Revision++;
            RefreshLocalClimate();
            foreach (int work in SummarizeWork()) yield return work;
        }
        internal IEnumerable<int> RefreshVegetationWork()
        {
            if (forest == null || vegetationRevision == forest.Revision) yield break;
            var result = new WorkResult<ForestHydrologyInputs>();
            for (int id = 0; id < CellCount; id++) {
                yield return 1;
                foreach (int unit in forest.HydrologyInputsWork(id, result)) yield return unit;
                vegetation[id] = result.Value;
            }
            vegetationRevision = forest.Revision;
        }

        private IEnumerable<int> WaterStepWork(WeatherInterval interval)
        {
            double dt = interval.Seconds, hours = dt * weather.HoursPerSecond;
            foreach (int unit in RefreshVegetationWork()) yield return unit;
            monthSeconds += dt;
            radiationIntegral += interval.Forcing.Radiation * dt;
            for (int id = 0; id < CellCount; id++)
            {
                yield return 1;
                double rain = interval.Rain * Climate.Cell(id, interval.Forcing).RainMultiplier;
                regionalRainReceived += rain;
                rainLeftToRules += rain;
                // Drought and waterlogging follow the root zone smoothly: tree growth and trafficability read them.
                var profile = soils[id];
                drought[id] = Responses.Drought(profile.Availability(soil[id]), drought[id], dt);
                wet[id] = Responses.Waterlogging(profile.Waterlogging(soil[id]), wet[id], dt);
                wetIntegral[id] += wet[id] * dt;
            }
            if (interval.Snow || MeanSnowCover > 0)
                foreach (int work in RedistributeSnowWork(interval.Forcing, hours)) yield return work;
        }

        // Conserved downwind transport; cover and upwind relief shelter retained snow.
        // Inspired by SnowTran-3D's terrain/vegetation coupling, with gameplay coefficients.
        private IEnumerable<int> RedistributeSnowWork(WeatherForcing forcing, double hours)
        {
            foreach (int work in snowFlux.BeginStepWork()) yield return work;
            int[] adjacent = new int[4];
            for (int id = 0; id < CellCount; id++)
            {
                yield return 1;
                if (snow[id] <= 0) continue;
                var cell = habitat.GetForestTileGeometry(id);
                double height = habitat.GetNormalizedElevation(id), lee = 0;
                int target = -1, count = habitat.GetAdjacentTileIds(id, adjacent);
                double best = 0;
                for (int j = 0; j < count; j++)
                {
                    int next = adjacent[j]; var neighbour = habitat.GetForestTileGeometry(next);
                    double dx = neighbour.X - cell.X, dy = neighbour.Y - cell.Y;
                    double length = Math.Sqrt(dx * dx + dy * dy);
                    if (length <= 0) continue;
                    double downwind = Responses.Environment(EnvironmentResponse.SnowDirection, [dx / length, dy / length]);
                    if (downwind < 0) lee = Math.Max(lee, habitat.GetNormalizedElevation(next) - height);
                    if (downwind > best) { best = downwind; target = next; }
                }
                if (target < 0) continue; // Closed map edge: retain the snow, never discard water.
                double shelter = Responses.Environment(EnvironmentResponse.SnowShelter, [vegetation[id].Cover, lee]);
                var step = new SurfaceRunoffStep(Responses.Environment(EnvironmentResponse.SnowRetention, [shelter]),
                    Responses.Environment(EnvironmentResponse.SnowRelease, [forcing.Temperature, forcing.Wind, shelter, hours, snow[id]]));
                snowFlux.Schedule(id, target, false, snow[id], step);
            }
            foreach (int work in snowFlux.CommitWork(snow)) yield return work;
        }

        private IEnumerable<int> SummarizeWork()
        {
            double water = 0, root = 0, cover = 0;
            for (int i = 0; i < CellCount; i++)
            {
                yield return 1;
                water += Math.Clamp(surface[i] / 2 + canopy[i] / 4, 0, 1);
                root += soil[i] / soils[i].Saturation;
                cover += Math.Clamp(snow[i] / 6, 0, 1);
            }
            MeanWetness = CellCount > 0 ? water / CellCount : 0;
            MeanSoil = CellCount > 0 ? root / CellCount : 0;
            MeanSnowCover = CellCount > 0 ? cover / CellCount : 0;
        }
        internal double GrowthFactor(int id, ForestSpecies species)
        {
            if ((uint)id >= (uint)CellCount) return 1;
            double supply = soils[id].Availability(soil[id]);
            double waterlogging = monthSeconds > 0 ? wetIntegral[id] / monthSeconds : wet[id];
            return Responses.WaterFactor(supply, waterlogging, ForestSpeciesTraits.For(species).DrySensitivity);
        }

        /// <summary>
        /// How firmly an unpaved surface carries vehicles: soaked soil with low bearing capacity gives way. The capacity is
        /// the soil type's parameter (older catalogs: from drainage); wetness is root-zone waterlogging or standing water.
        /// </summary>
        internal (double Wetness, double Bearing) Trafficability(int id)
        {
            if ((uint)id >= (uint)CellCount) return (0, 1);
            var profile = soils[id];
            // Soil softens well before saturation: wetness rises from half the field capacity to full saturation.
            return (Responses.Environment(EnvironmentResponse.SoilWetness,
                [soil[id], surface[id], profile.FieldCapacity, profile.Saturation]), BearingCapacity(profile, Responses));
        }
        internal static double BearingCapacity(SoilProperties profile, EcologyResponseProgram rules = null) =>
            (rules ?? EcologyResponseProgram.Default).Environment(EnvironmentResponse.SoilBearing,
                [profile.BearingCapacity ?? 0, profile.BearingCapacity.HasValue ? 1 : 0, profile.DrainagePerHour]);

        internal double CurrentWaterFactor(int id, ForestSpecies species) => (uint)id < (uint)CellCount
            ? Responses.WaterFactor(soils[id].Availability(soil[id]), wet[id], ForestSpeciesTraits.For(species).DrySensitivity) : 1;

        internal void FinishForestMonth() => SimulationWork.Complete(FinishForestMonthWork());
        internal IEnumerable<int> FinishForestMonthWork()
        {
            for (int first = 0; first < wetIntegral.Length; first += 1024) {
                Array.Clear(wetIntegral, first, Math.Min(1024, wetIntegral.Length - first)); yield return 1;
            }
            monthSeconds = radiationIntegral = 0;
        }

        /// <summary>One water store of a tile (mm), read without building the whole cell.</summary>
        internal double Water(int id, WaterStore store) => (uint)id >= (uint)CellCount ? 0 : store switch
        {
            WaterStore.Surface => surface[id],
            WaterStore.Soil => soil[id],
            WaterStore.Deep => deep[id],
            WaterStore.Canopy => canopy[id],
            _ => snow[id]
        };
        /// <summary>Waterlogging of a tile's root zone, smoothed (0–1).</summary>
        internal double Waterlogging(int id) => (uint)id < (uint)CellCount ? wet[id] : 0;
        /// <summary>Surface water standing on a tile (mm).</summary>
        internal double SurfaceDepth(int id) => (uint)id < (uint)surface.Length ? surface[id] : 0;

        internal EnvironmentCell Cell(int id)
        {
            var species = forest != null && forest.TryGetSpecies(id, out var stand) ? stand : ForestSpecies.Beech;
            return new(canopy[id], surface[id], soil[id], deep[id], drought[id], wet[id],
                GrowthFactor(id, species), soils[id].Saturation);
        }
    }
}
