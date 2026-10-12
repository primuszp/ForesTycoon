using System.Collections.Generic;

namespace ForesTycoon
{
    sealed partial class GameWorld
    {
        private CompiledRoadRule roadRule = new(RuleModel.Default());
        internal RuleModel RuleDocument => roadRule.Document;
        internal string ActiveRuleName => roadRule.Name;
        internal GameRuleCatalog DescribeCurrentRules() => CurrentGameRules.Build(ecosystem.ForestYearSeconds,
            Environment.Climate.Definition, Soils.Definition, RuleDocument, Tuning);

        /// <summary>The world's tunable numbers (logistics, upkeep, roads, truck physics); changed only by a journaled command.</summary>
        internal GameTuning Tuning { get; private set; } = GameTuning.Default;
        internal void QueueTuning(IReadOnlyDictionary<string, double> overrides) =>
            Enqueue(new SetTuningCommand(GameTuning.FromOverrides(overrides).ToJson()));
        internal void QueueRuleConfiguration(RuleModel model, IReadOnlyDictionary<string, double> overrides)
        {
            // Validate the entire editor document before queuing either change.
            var compiled = new CompiledRoadRule(model);
            compiled.ValidateRange();
            var tuning = GameTuning.FromOverrides(overrides);
            QueueRuleModel(compiled.Document);
            QueueTuning(tuning.ToOverrides());
        }
        void IWorldCommandTarget.ExecuteTuning(string json) => ApplyTuning(GameTuning.FromJson(json));

        private void ApplyTuning(GameTuning tuning)
        {
            Tuning = (tuning ?? GameTuning.Default).WithBehaviors(behaviorPolicy.HasGraphs ? behaviorPolicy : null);
            if (Logistics != null) Logistics.Tuning = Tuning;
            vehicles.TimeScale = Tuning[Tune.VehicleTimeScale];
            vehicles.Spec = Tuning.Truck;
            foreach (var vehicle in vehicles.Vehicles) vehicle.Spec = Tuning.Truck;
            map.SkidTrailOvergrowYears = Tuning.F(Tune.SkidTrailOvergrowYears);
        }
        internal string ObserveRuleField(string field) => field switch
        {
            "calendar.time" => $"Erdőév: {forest.ForestYear:F3}; tick: {SimulationTick}",
            "forest.trees" => $"Élő egyedek: {ForestTreeCount}",
            "forest.volume" => $"Előző évi növedék: {LastAnnualForestGrowth:F3} m³ (nem teljes készlet)",
            "road.network" => $"Útcsempék: {RoadCount}",
            "trail.network" => $"Nyomcsempék: {map.SkidTrailCount}",
            "weather.rain" => $"Esőintenzitás: {Environment.LiquidRainRate:F3} mm/környezeti óra",
            "weather.snowfall" => $"Havazás: {Environment.SnowfallRate:F3} mm vízegyenérték/környezeti óra",
            "water.snow" => $"Hókészlet: {Environment.SnowWater:F3} mm-cella; átlagos hóborítás: {Environment.MeanSnowCover:P1}",
            "weather.forcing" => $"Hőmérséklet: {Environment.Temperature:F2} °C; sugárzás: {Environment.Radiation:F3}",
            "water.balance" => $"Vízmérleghiba: {Environment.BalanceError:G6}; teljes tárolt víz: {Environment.StoredWater:F3} mm-cella",
            "water.evaporated" => $"Összes elpárolgott víz: {Environment.Evaporated:F3} mm-cella",
            "water.transpired" => $"Összes növényzeti vízfelvétel: {Environment.Transpired:F3} mm-cella",
            "water.outflow" => $"Összes külső kifolyás: {Environment.Outflow:F3} mm-cella",
            "economy.expenses" => $"Építési és útjavítási kiadás: {Expenses:F3} eFt",
            "economy.runningCosts" => $"Működési költség: {Logistics.RunningCosts:F3} eFt",
            "economy.income" => $"Bevétel: {Income:F3} eFt",
            "economy.result" => $"Eredmény: {Balance:F3} eFt",
            "timber.stacks" => $"Sarangok: {Logistics.Stacks.Count}",
            "fleet.state" => $"Telephely: {Logistics.Depots.Count}; gép: {Logistics.Machines.Count}; teherautó: {Logistics.Trucks.Count}",
            "machine.controllerState" => $"Aktív gépvezérlési példányok: {BehaviorStates.Length}",
            "wildlife.state" => $"Állatok: {wildlife.Animals.Count}",
            _ => null
        };
        private string lastRuleSample = "Még nem történt közúti áthaladás.";
        private (int Tile, float Load, float Surface, float Wear, float Before, float After) trafficSample;
        internal string LastRuleSample
        {
            get => lastRuleSample ??= $"Útcsempe {trafficSample.Tile}: terhelés {trafficSample.Load:F6} × burkolat {trafficSample.Surface:F2}; kimenet {trafficSample.Wear:F6}; állapot {trafficSample.Before:F4} -> {trafficSample.After:F4}";
            private set => lastRuleSample = value;
        }
        internal void QueueRuleModel(RuleModel model) => Enqueue(new SetRuleModelCommand(model.ToJson()));
        void IWorldCommandTarget.ExecuteRuleModel(string json)
        {
            var next = new CompiledRoadRule(RuleModel.FromJson(json));
            next.ValidateRange(); roadRule = next;
            LastRuleSample = "Szabály alkalmazva; a következő áthaladás már ezt használja.";
        }
        private void BindRoadRules()
        {
            vehicles.RoadState = id => map.IsRoadTile(id)
                ? (map.GetRoadPaving(id) == RoadPaving.Asphalt ? RoadSurface.Asphalt : RoadSurface.Gravel, map.GetRoadCondition(id))
                : (RoadSurface.Dirt, (0.2f - 0.2f * map.GetSkidTrailWear(id)) * Trafficability(id));
            vehicles.RoadWear = ApplyTrafficWear;
            vehicles.RoadLoad = mass => RoadTrafficParameters.Load(mass, Tuning);
        }
        /// <summary>Soaked soil with low bearing capacity makes dirt tracks rough: lower condition, more rolling resistance.</summary>
        internal float Trafficability(int id)
        {
            if (Environment == null) return 1;
            var (wetness, bearing) = Environment.Trafficability(id);
            double native = 1 - wetness * (1 - bearing);
            if (behaviorPolicy.Overrides("trail.trafficability", "tile"))
                native = behaviorPolicy.Evaluate(new("trail.trafficability", "tile", (ulong)id, native, Time: forest.ForestYear, State: wetness, Amount: bearing));
            return (float)System.Math.Clamp(native, 0, 1);
        }
        /// <summary>A vertical section of one tile: the current value of its per-tile quantities, by system-model ID.</summary>
        internal Dictionary<string, string> DescribeTile(int id)
        {
            var result = new Dictionary<string, string>(System.StringComparer.Ordinal);
            if (id < 0 || id >= map.Tiles.Count) return result;
            if (Soils != null) {
                var profile = Soils.Profile(id);
                result["soil.profile"] = profile.Name;
                result["soil.fertility"] = profile.Properties.Fertility.ToString("0.00");
                result["soil.bearing"] = EnvironmentSystem.BearingCapacity(profile.Properties).ToString("0.00");
            }
            if (Environment != null) {
                var cell = Environment.Cell(id);
                result["water.root"] = $"{cell.Soil:0.#} / {cell.Capacity:0} mm";
                result["water.deep"] = $"{cell.Deep:0.#} mm";
                result["water.surface"] = $"{cell.Surface:0.##} mm";
                result["water.canopy"] = $"{cell.Canopy:0.##} mm";
                result["water.growthFactor"] = cell.GrowthFactor.ToString("0.00");
            }
            if (forest.TryGetStand(id, out var stand) && stand.Species != ForestSpecies.None) {
                result["forest.trees"] = $"{ForestSpeciesTraits.For(stand.Species).Name}, {stand.AgeYears:0} év";
                result["forest.health"] = stand.Health.ToString("0.00");
                result["forest.volume"] = $"{ForestSystem.TimberCubicMetres(stand):0.#} m³";
            }
            if (map.IsRoadTile(id)) result["road.condition"] = map.GetRoadCondition(id).ToString("0.00") + " · " + (map.GetRoadPaving(id) == RoadPaving.Asphalt ? "aszfalt" : "makadám");
            else result["trail.trafficability"] = Trafficability(id).ToString("0.00") + (map.IsSkidTrail(id) ? " · közelítőnyom" : " · földfelszín");
            if (map.IsSkidTrail(id)) result["trail.wear"] = map.GetSkidTrailWear(id).ToString("0.00");
            return result;
        }
        internal void ApplyTrafficWear(int id, float amount)
        {
            if (map.IsSkidTrail(id)) { map.DriveSkidTrail(id, amount * Tuning.F(Tune.TrailLoadMultiplier)); return; }
            if (!map.IsRoadTile(id)) return;
            float factor = RoadTrafficParameters.SurfaceFactor(map.GetRoadPaving(id), Tuning);
            float before = map.GetRoadCondition(id);
            float wear = roadRule.Evaluate(new(amount, factor, 1 - before));
            wear = (float)behaviorPolicy.Evaluate(new("road.trafficWear", "road", (ulong)id, wear,
                Time: forest.ForestYear, State: 1 - before, Amount: amount));
            map.WearRoad(id, wear);
            // Store numeric diagnostics; format text only when a panel reads it.
            trafficSample = (id, amount, factor, wear, before, map.GetRoadCondition(id));
            lastRuleSample = null;
        }
    }
}
