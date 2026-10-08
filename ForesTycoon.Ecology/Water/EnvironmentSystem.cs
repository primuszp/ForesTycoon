using System;

namespace ForesTycoon.Ecology
{
    internal readonly record struct EnvironmentCell(double Canopy, double Surface, double Soil, double Deep,
        double Drought, double Waterlogging, double GrowthFactor, double Capacity, double UptakePerHour, double DemandPerHour);

    // Bounded bucket hydrology with conserved, vegetation-driven uptake. Water stores are mm per cell.
    internal sealed partial class EnvironmentSystem : IForestEnvironment
    {
        internal const double SecondsPerForestYear = EcologyTime.SecondsPerForestYear;
        internal const double DefaultGameSecondsPerYear = EcologyTime.DefaultGameSecondsPerYear;
        internal static bool IsValidForestYearSeconds(double seconds) => EcologyTime.IsValidForestYearSeconds(seconds);
        internal const double HoursPerSecond = EcologyTime.HoursPerSecond;
        internal const double StepSeconds = EcologyTime.StepSeconds;
        private readonly IForestHabitat habitat;
        private readonly IForestCanopy forest;
        private readonly double[] canopy, surface, soil, deep, drought, wet, wetIntegral;
        private readonly SurfaceWaterFlux surfaceFlux;
        private readonly double[] demandIntegral, uptakeIntegral, uptakeRate, demandRate;
        private readonly SoilProperties[] soils;
        private readonly ForestHydrologyInputs[] vegetation;
        private ulong vegetationRevision = ulong.MaxValue;
        private readonly int[] destinations;
        private double remainder, monthSeconds, radiationIntegral;
        private readonly WeatherSystem weather;
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
        internal double InitialWater { get; }
        internal ulong Revision { get; private set; }
        internal int CellCount => soil.Length;
        internal double MeanWetness { get; private set; }
        internal double MeanSoil { get; private set; }
        internal double StoredWater
        {
            get
            {
                double total = 0;
                for (int i = 0; i < CellCount; i++) total += canopy[i] + surface[i] + soil[i] + deep[i];
                return total;
            }
        }
        internal double BalanceError => StoredWater - (InitialWater + TotalRain * CellCount - Evaporated - Transpired - Outflow);

        double IForestEnvironment.Radiation => Radiation;
        double IForestEnvironment.PeriodRadiation => PeriodRadiation;
        double IForestEnvironment.GrowthFactor(int tileId, ForestSpecies species) => GrowthFactor(tileId, species);
        double IForestEnvironment.CurrentWaterFactor(int tileId, ForestSpecies species) => CurrentWaterFactor(tileId, species);

        internal EnvironmentSystem(IForestHabitat habitat, IForestCanopy forest, double forestYearSeconds = SecondsPerForestYear)
        {
            this.habitat = habitat ?? throw new ArgumentNullException(nameof(habitat));
            this.forest = forest;
            weather = new WeatherSystem(habitat.Seed, forestYearSeconds);
            int n = habitat.TileCount;
            canopy = new double[n]; surface = new double[n]; soil = new double[n]; deep = new double[n];
            drought = new double[n]; wet = new double[n]; wetIntegral = new double[n]; surfaceFlux = new SurfaceWaterFlux(n);
            destinations = new int[n];
            demandIntegral = new double[n]; uptakeIntegral = new double[n];
            uptakeRate = new double[n]; demandRate = new double[n];
            soils = new SoilProperties[n]; vegetation = new ForestHydrologyInputs[n];
            for (int i = 0; i < n; i++)
            {
                soils[i] = habitat.GetSoilProperties(i);
                soils[i].Validate();
                soil[i] = Math.Clamp(45 + 100 * habitat.GetMoisture(i), 0, soils[i].Saturation);
                deep[i] = 20;
            }
            InitialWater = StoredWater;
            RefreshRouting(); Summarize();
        }

        internal void ForceWeather(WeatherPreset preset, int peak = -1, int duration = -1) =>
            weather.ForceWeather(preset, peak, duration);
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

        private void Advance(double seconds)
        {
            while (seconds > 1e-9)
            {
                var interval = weather.AdvanceInterval(seconds);
                WaterStep(interval);
                seconds -= interval.Seconds;
            }
            Revision++;
            Summarize();
        }
        internal void RefreshRouting()
        {
            Span<int> neighbours = stackalloc int[4];
            for (int i = 0; i < CellCount; i++)
                RefreshRoute(i, neighbours);
        }

        // Only these cells and direct neighbours can acquire a different downhill destination.
        // Re-routing never changes water stores, forcing, integrals or simulation time.
        internal void RefreshRouting(ReadOnlySpan<int> changedTiles)
        {
            foreach (int id in changedTiles)
                if ((uint)id >= (uint)CellCount) throw new ArgumentOutOfRangeException(nameof(changedTiles));
            Span<int> neighbours = stackalloc int[4], adjacent = stackalloc int[4];
            foreach (int id in changedTiles)
            {
                RefreshRoute(id, neighbours);
                int count = habitat.GetAdjacentTileIds(id, adjacent);
                for (int j = 0; j < count; j++) RefreshRoute(adjacent[j], neighbours);
            }
        }

        private void RefreshRoute(int id, Span<int> neighbours)
        {
            destinations[id] = -1;
            float low = habitat.GetNormalizedElevation(id);
            int count = habitat.GetAdjacentTileIds(id, neighbours);
            for (int j = 0; j < count; j++)
            {
                int next = neighbours[j];
                float height = habitat.GetNormalizedElevation(next);
                if (height < low) { low = height; destinations[id] = next; }
            }
        }
        private void RefreshVegetation()
        {
            if (forest == null || vegetationRevision == forest.Revision) return;
            for (int id = 0; id < CellCount; id++) vegetation[id] = forest.HydrologyInputs(id);
            vegetationRevision = forest.Revision;
        }

        private void WaterStep(WeatherInterval interval)
        {
            double dt = interval.Seconds, rain = interval.Rain;
            RefreshVegetation();
            double hours = dt * weather.HoursPerSecond;
            var runoffStep = SurfaceRunoffLaw.Legacy.Prepare(hours);
            // Shared atmospheric forcing drives both soil evaporation and leaf-water demand.
            double potential = interval.Forcing.PotentialEvaporationPerHour * hours;
            monthSeconds += dt;
            radiationIntegral += interval.Forcing.Radiation * dt;
            surfaceFlux.BeginStep();
            for (int id = 0; id < CellCount; id++)
            {
                var profile = soils[id];
                var trees = vegetation[id];
                double held = Math.Min(rain, Math.Max(0, trees.InterceptionCapacity - canopy[id]));
                canopy[id] += held; surface[id] += rain - held;
                // Lost crown cover transfers intercepted water to the ground, preserving the balance.
                double drip = Math.Max(0, canopy[id] - trees.InterceptionCapacity);
                StockFlows.Transfer(ref canopy[id], ref surface[id], drip);
                bool sealedSurface = habitat.IsImpervious(id);
                double infiltration = Math.Min(surface[id], Math.Min(
                    (sealedSurface ? 0.5 : profile.InfiltrationPerHour) * hours, Math.Max(0, profile.Saturation - soil[id])));
                StockFlows.Transfer(ref surface[id], ref soil[id], infiltration);

                double budget = potential;
                double loss = StockFlows.Withdraw(ref canopy[id], budget);
                budget -= loss; Evaporated += loss;
                loss = StockFlows.Withdraw(ref surface[id], budget);
                budget -= loss; Evaporated += loss;
                double available = Math.Max(0, soil[id] - profile.WiltingPoint);
                loss = Math.Min(available, budget * profile.Availability(soil[id]) * (1 - trees.Cover) * 0.65);
                StockFlows.Withdraw(ref soil[id], loss); Evaporated += loss;

                // Allocate the shared root-zone budget proportionally to living leaf-area demand.
                // Every tree in this cell gets the same fulfilled-demand fraction; no second root-load penalty.
                double requested = sealedSurface ? 0 : potential * trees.LeafAreaIndex * 0.8;
                available = Math.Max(0, soil[id] - profile.WiltingPoint);
                double uptake = Math.Min(available, requested * profile.Availability(soil[id]));
                StockFlows.Withdraw(ref soil[id], uptake); Transpired += uptake;
                demandIntegral[id] += requested; uptakeIntegral[id] += uptake;
                demandRate[id] = requested / hours; uptakeRate[id] = uptake / hours;

                double drainage = Math.Min(Math.Max(0, soil[id] - profile.FieldCapacity), profile.DrainagePerHour * hours);
                StockFlows.Transfer(ref soil[id], ref deep[id], drainage);
                double baseflow = StockFlows.Withdraw(ref deep[id], 0.15 * hours);
                Outflow += baseflow;
                double dry = 1 - profile.Availability(soil[id]), waterlogged = profile.Waterlogging(soil[id]);
                double blend = 1 - Math.Exp(-dt / 90);
                drought[id] += (dry - drought[id]) * blend; wet[id] += (waterlogged - wet[id]) * blend;
                wetIntegral[id] += wet[id] * dt;
                int to = destinations[id];
                double exported = surfaceFlux.Schedule(id, to, to < 0 && habitat.IsWaterOutlet(id),
                    surface[id], runoffStep);
                if (exported != 0) Outflow += exported;
            }
            surfaceFlux.Commit(surface);
        }

        private void Summarize()
        {
            double water = 0, root = 0;
            for (int i = 0; i < CellCount; i++)
            {
                water += Math.Clamp(surface[i] / 2 + canopy[i] / 4, 0, 1);
                root += soil[i] / soils[i].Saturation;
            }
            MeanWetness = CellCount > 0 ? water / CellCount : 0;
            MeanSoil = CellCount > 0 ? root / CellCount : 0;
        }
        internal double GrowthFactor(int id, ForestSpecies species)
        {
            if ((uint)id >= (uint)CellCount) return 1;
            double supply = demandIntegral[id] > 1e-12
                ? uptakeIntegral[id] / demandIntegral[id] : soils[id].Availability(soil[id]);
            double waterlogging = monthSeconds > 0 ? wetIntegral[id] / monthSeconds : wet[id];
            return WaterResponse(supply, waterlogging, species);
        }

        internal double CurrentWaterFactor(int id, ForestSpecies species) => (uint)id < (uint)CellCount
            ? WaterResponse(soils[id].Availability(soil[id]), wet[id], species) : 1;

        private static double WaterResponse(double supply, double waterlogging, ForestSpecies species)
        {
            double drySensitivity = ForestSpeciesTraits.For(species).DrySensitivity;
            return Math.Clamp(Math.Pow(Math.Clamp(supply, 0, 1), drySensitivity) * (1 - waterlogging * 0.65), 0, 1);
        }

        internal void FinishForestMonth()
        {
            Array.Clear(wetIntegral); Array.Clear(demandIntegral); Array.Clear(uptakeIntegral);
            monthSeconds = radiationIntegral = 0;
        }

        internal EnvironmentCell Cell(int id)
        {
            var species = forest != null && forest.TryGetSpecies(id, out var stand) ? stand : ForestSpecies.Beech;
            return new(canopy[id], surface[id], soil[id], deep[id], drought[id], wet[id],
                GrowthFactor(id, species), soils[id].Saturation, uptakeRate[id], demandRate[id]);
        }
    }
}
