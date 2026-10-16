using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Runtime.CompilerServices;
using ForesTycoon.Rules;

namespace ForesTycoon;

// ── The rule object model of the game ───────────────────────────────────
// Each interface is a kind of world object the editor discovers (RuleSchema.Discover). Inheritance is the hierarchy:
// a tree is a plant is a world object. Properties are fields, setters make them writable, [RuleRelation] properties
// lead to attached objects. A view class implements the interface over the live world for one element at a time.
// Adding a field here is all it takes for it to appear in the editor and to be usable in rules.

[RuleEntity("object", Display = "World object", Abstract = true, Help = "Anything in the world a rule can address.")]
internal interface IWorldObject
{
    [RuleField("Internal index of the element in this step.")] double Index { get; }
}

[RuleEntity("place", Abstract = true, Help = "A point of the map: soil, water, cover.")]
internal interface IPlace : IWorldObject { }

[RuleEntity("tile", Element = "tile", Help = "One map tile: its soil, water and cover. Other objects read it through their tile relation.")]
internal interface ITile : IPlace
{
    [RuleField("Hydrological map moisture used by road weathering (not root-zone water).", Unit = "0–1", Min = 0, Max = 1)]
    double Moisture { get; }
    [RuleField("Water in the root zone.", Unit = "mm", Min = 0)]
    [RuleCause("tile.cover", -1, "water.interception", Help = "Canopy intercepts rain and transpires.")]
    double RootWater { get; set; }
    [RuleField("Water standing on the surface.", Unit = "mm", Min = 0)] double SurfaceWater { get; set; }
    [RuleField("Water below the root zone.", Unit = "mm", Min = 0)] double DeepWater { get; set; }
    [RuleField("Root-zone water above field capacity.", Unit = "mm", Min = 0)] double ExcessWater { get; }
    [RuleField("Share of plant-available water.", Unit = "0–1", Min = 0, Max = 1)]
    [RuleCause("tile.rootWater", +1, "water.stress")]
    double WaterSupply { get; }
    [RuleField("Share of the soil under standing water.", Unit = "0–1", Min = 0, Max = 1)] double Waterlogging { get; }
    [RuleField("Infiltration rate of the soil.", Unit = "mm/h", Min = 0)] double Infiltration { get; }
    [RuleField("Total water the soil holds when saturated.", Unit = "mm", Min = 0)] double Saturation { get; }
    [RuleField("Field capacity of the soil.", Unit = "mm", Min = 0)] double FieldCapacity { get; }
    [RuleField("Wilting point of the soil.", Unit = "mm", Min = 0)] double WiltingPoint { get; }
    [RuleField("Drainage rate of the soil.", Unit = "mm/h", Min = 0)] double Drainage { get; }
    [RuleField("Bearing capacity of the soil.", Unit = "0–1", Min = 0, Max = 1)] double BearingCapacity { get; }
    [RuleField("Canopy cover.", Unit = "0–1", Min = 0, Max = 1)]
    [RuleCause("tree.height", +1, "forest.canopy", Help = "Taller trees close the canopy.")]
    double Cover { get; }
    [RuleField("A road or building covers the tile.")] bool Paved { get; }
    [RuleField("How soaked the soil is.", Unit = "0–1", Min = 0, Max = 1)] double Wetness { get; }
    [RuleField("Rain caught on the crowns.", Unit = "mm", Min = 0)] double CanopyWater { get; set; }
    [RuleField("Water stored in the snowpack.", Unit = "mm", Min = 0)] double Snow { get; set; }
    [RuleField("Rain falling on the tile now.", Unit = "mm/h", Min = 0)] double Rain { get; }
    [RuleField("Snow falling on the tile now, as water.", Unit = "mm/h", Min = 0)] double Snowfall { get; }
    [RuleField("What the air could evaporate now from a wet surface.", Unit = "mm/h", Min = 0)] double PotentialEvaporation { get; }
    [RuleField("Air temperature on the tile.", Unit = "°C")] double Temperature { get; }
    [RuleField("Leaf area of the trees per ground area.", Min = 0)] double LeafAreaIndex { get; }
    [RuleField("Rain the crowns can hold.", Unit = "mm", Min = 0)] double InterceptionCapacity { get; }
    [RuleField("How full the crowns are: crown water over what they can hold.", Unit = "0–1", Min = 0, Max = 1)] double CanopyFill { get; }
    [RuleField("How far the water surface lies above the lower neighbours', summed: what drives runoff.", Unit = "mm", Min = 0)] double Drop { get; }
    [RuleField("A sealed surface (road, building) lets little water in.")] bool Sealed { get; }
    [RuleField("Water leaves the map here (map edge, lake or river).")] bool Outlet { get; }
    [RuleField("Water a spring adds to the surface.", Unit = "mm/h", Min = 0)] double SpringRate { get; }
    [RuleField("Height of the ground above the map's base.", Unit = "m")] double Elevation { get; }
    [RuleField("Height of the water surface: ground plus standing water. Water flows towards a lower level.", Unit = "m")] double WaterLevel { get; }
    [RuleCollection("The four tiles next to this one.", Addressable = true)] IEnumerable<ITile> Neighbours { get; }
    [RuleCollection("The living trees standing on the tile.")] IEnumerable<ITree> Trees { get; }
    [RuleCollection("The stumps left on the tile.")] IEnumerable<IStump> Stumps { get; }
}

[RuleEntity("world", Element = "world",
    Help = "The whole game: time, weather and money. Every rule also reads these as world.<field> (world.rain).")]
internal interface IWorld : IWorldObject
{
    [RuleField("Forest years since the start of the game.", Unit = "forest years", Min = 0)] double Time { get; }
    [RuleField("Living trees.", Min = 0)] double TreeCount { get; }
    [RuleField("Road tiles.", Min = 0)] double RoadCount { get; }
    [RuleField("Liquid rain rate.", Unit = "mm/h", Min = 0)] double Rain { get; }
    [RuleField("Air temperature.", Unit = "°C")] double Temperature { get; }
    [RuleField("Snow water stored on the map.", Unit = "mm", Min = 0)] double Snow { get; }
    [RuleField("Water stored on the map.", Unit = "mm", Min = 0)] double Water { get; }
    [RuleField("Income so far.", Unit = "eFt")] double Income { get; }
    [RuleField("Construction and road repair spending so far.", Unit = "eFt")] double Expenses { get; }
    [RuleField("Running costs so far.", Unit = "eFt")] double RunningCosts { get; }
    [RuleField("Income minus spending.", Unit = "eFt")] double Balance { get; }
    [RuleField("Hours of the environment's clock in one forest year: hourly rates times this give yearly ones.", Unit = "h")] double HoursPerYear { get; }
}

[RuleEntity("road", Element = "road", Help = "A paved road tile; weather rules change its condition.")]
internal interface IRoad : IWorldObject
{
    [RuleField("Road condition: 1 is new, 0 is worn out.", Unit = "0–1", Min = 0, Max = 1)] double Condition { get; set; }
    [RuleField("Paving of the road.")] RoadPaving Surface { get; }
    [RuleField("Current RainRate / 20, clamped to 0–1.", Unit = "0–1", Min = 0, Max = 1)] double RainIntensity { get; }
    [RuleRelation("The road's tile and hydrological map moisture.")] ITile Tile { get; }
}

[RuleEntity("trail", Element = "trail", Help = "A temporary forest track. Machines deepen its ruts and reset its idle time.")]
internal interface ITrail : IWorldObject
{
    [RuleField("Rut depth; 0 is smooth and 1 is deeply rutted.", Display = "Rut depth", Unit = "0–1", Min = 0, Max = 1, Pace = -1)] double Wear { get; set; }
    [RuleField("Forest years since the last machine pass.", Display = "Idle years", Unit = "forest years", Min = 0)] double IdleYears { get; set; }
    [RuleRelation("Environment of the trail tile.")] ITile Tile { get; }
    [RuleAction("The overgrown trail disappears and routes are refreshed.", Display = "Remove")] bool Remove();
}

[RuleEntity("trafficVehicle", Abstract = true, Display = "Vehicle", Help = "The vehicle at the instant it entered the tile.")]
internal interface ITrafficVehicle
{
    [RuleField("Vehicle category.", Display = "Kind")] TrafficVehicleKind Kind { get; }
    [RuleField("Stable vehicle identifier.", Display = "Vehicle ID", Min = 0)] double VehicleId { get; }
    [RuleField("Total vehicle mass including cargo.", Display = "Mass", Unit = "kg", Min = 0, Max = 1000000)] double Mass { get; }
    [RuleField("Cargo as a share of capacity.", Display = "Cargo fill", Min = 0, Max = 1)] double Fill { get; }
}

[RuleEntity("passage", Element = "passage", Display = "Passage", Help = "One vehicle entering one tile. Available only during a passage event.")]
internal interface IPassage : IWorldObject
{
    [RuleField("The tile is a paved road.", Display = "On road")] bool IsRoad { get; }
    [RuleField("The tile is a forest track.", Display = "On trail")] bool IsTrail { get; }
    [RuleRelation("Vehicle data captured when the passage occurred.", Display = "Vehicle")] ITrafficVehicle Vehicle { get; }
    [RuleRelation("Road at the passage location; writes on non-roads do nothing.", Display = "Road")] IRoad Road { get; }
    [RuleRelation("Track at the passage location; writes on non-tracks do nothing.", Display = "Trail")] ITrail Trail { get; }
    [RuleRelation("The entered tile.", Display = "Tile")] ITile Tile { get; }
}

[RuleEntity("plant", Abstract = true, Help = "A living plant: it has an age and a health, and stands on a tile.")]
internal interface IPlant : IWorldObject
{
    [RuleField("Age.", Unit = "forest years", Min = 0)] double Age { get; }
    [RuleField("Vigour; multiplies growth, a plant kept low for long dies.", Unit = "0–1", Min = 0, Max = 1)]
    double Health { get; set; }
    [RuleRelation("The tile the plant stands on.")] ITile Tile { get; }
}

[RuleEntity("tree", Element = "tree", Help = "An individual tree: size, resources and stress. Monthly growth consumes the health and structure produced by the project rules.")]
internal interface ITree : IPlant
{
    [RuleField("Stable tree identity.", Min = 0)] double TreeId { get; }
    [RuleField("Tree height.", Unit = "m", Min = 0)]
    double Height { get; set; }
    [RuleField("Diameter at breast height.", Unit = "m", Min = 0)] double Diameter { get; set; }
    [RuleField("Light reaching the tree under its neighbours.", Unit = "0–1", Min = 0, Max = 1)]
    [RuleCause("tile.cover", -1, "forest.competition", Help = "Neighbours shade the tree.")]
    double Light { get; }
    [RuleField("Water reaching the tree after root competition.", Unit = "0–1", Min = 0, Max = 1)]
    [RuleCause("tile.rootWater", +1, "forest.competition")]
    double Water { get; }
    [RuleField("Consecutive years short of resources; above 4 a weak tree dies.", Unit = "forest years", Min = 0)]
    double StressYears { get; set; }
    [RuleField("Space remaining among neighbours.", Display = "Available space", Min = 0, Max = 1)] double Space { get; }
    [RuleField("Light available relative to the species' light response.", Display = "Light response", Min = 0, Max = 1)] double LightResponse { get; }
    [RuleField("Smallest of water, space and light response. A resource query, not an imposed health change.", Display = "Resource limit", Min = 0, Max = 1)] double ResourceLimit { get; }
    [RuleField("Suitability of this tile for this species.", Display = "Site fitness", Min = 0, Max = 1)] double Fitness { get; }
    [RuleField("Maximum age from the species catalogue.", Display = "Maximum age", Unit = "forest years", Min = 0)] double MaximumAge { get; }
    [RuleAction("The living tree becomes deadwood. Its identity and physical size are preserved; no harvested timber is credited.", Display = "Die")] bool Die();
    [RuleField("Annual diameter growth.", Unit = "m/year", Min = 0, Max = 100)] double DiameterGrowth { get; set; }
    [RuleField("Annual height growth.", Unit = "m/year", Min = 0, Max = 100)] double HeightGrowth { get; set; }
    [RuleField("Annual crown radius growth.", Unit = "m/year", Min = 0, Max = 100)] double CrownGrowth { get; set; }
    [RuleField("Crown radius.", Unit = "m", Min = 0)] double CrownRadius { get; set; }
    [RuleField("Height divided by the species height envelope.", Min = 0)] double RelativeHeight { get; }
    [RuleField("Species radial growth potential.", Unit = "m/year", Min = 0)] double RadialRate { get; }
    [RuleField("Species height growth potential.", Unit = "m/year", Min = 0)] double HeightRate { get; }
    [RuleField("Species crown-to-height ratio.", Min = 0)] double CrownRatio { get; }
    [RuleField("Species crown spread per diameter increment.", Min = 0)] double CrownSpread { get; }
    [RuleField("Position in the forest year, 0 at the start and 1 at the end.", Min = 0, Max = 1)] double YearFraction { get; }
    [RuleField("Shade tolerance of the species.", Unit = "0–1", Min = 0, Max = 1)] double ShadeTolerance { get; }
}

[RuleEntity("deadTree", Element = "dead tree", Display = "Dead tree", Help = "A dead tree left in the world. Project rules decide when it disappears.")]
internal interface IDeadTree : IWorldObject
{
    [RuleField("Forest years since death.", Display = "Dead years", Unit = "forest years", Min = 0)] double DeadYears { get; }
    [RuleField("Stable identity of the original tree.", Display = "Tree ID", Min = 0)] double TreeId { get; }
    [RuleField("Diameter of the dead stem.", Unit = "m", Min = 0)] double Diameter { get; }
    [RuleRelation("The tile on which the dead tree stands.")] ITile Tile { get; }
    [RuleAction("The dead tree disappears, without crediting timber.", Display = "Remove")] bool Remove();
}

[RuleEntity("stump", Element = "stump", Help = "What is left of a felled tree. It rots away; the rule book decides when it is gone.")]
internal interface IStump : IWorldObject
{
    [RuleField("Decomposition progress; 0 is fresh, 1 is fully decomposed.", Display = "Decay progress", Unit = "0–1", Min = 0, Max = 1, Pace = +1)] double DecayProgress { get; set; }
    [RuleField("Forest years since the tree was felled.", Unit = "forest years", Min = 0)] double Age { get; }
    [RuleField("Stem diameter of the felled tree.", Unit = "m", Min = 0)] double Diameter { get; }
    [RuleRelation("The tile the stump stands on.")] ITile Tile { get; }
    [RuleAction("The stump has rotted away and disappears.")] bool Remove();
}

[RuleEntity("vehicle", Abstract = true, Help = "Anything that moves on the road network.")]
internal interface IVehicle : IWorldObject
{
    [RuleField("Current speed.", Unit = "tiles/s", Min = 0)] double Speed { get; }
    [RuleField("Distance driven so far.", Unit = "m", Min = 0)] double Distance { get; }
    [RuleField("Diesel burnt so far.", Unit = "l", Min = 0)] double Fuel { get; }
    [RuleField("Wear of the road under the vehicle.", Unit = "0–1", Min = 0, Max = 1)] double RoadDamage { get; }
}

[RuleEntity("truck", Element = "truck", Help = "Log truck. (Read-only fields; effects come later.)")]
internal interface ITruck : IVehicle
{
    [RuleField("Timber carried.", Unit = "m³", Min = 0)] double Cargo { get; }
    [RuleField("Cargo as a share of capacity.", Unit = "0–1", Min = 0, Max = 1)] double Fill { get; }
    [RuleField("Mass without cargo.", Unit = "kg", Min = 0)] double EmptyMass { get; }
    [RuleField("Total mass with cargo.", Unit = "kg", Min = 0)] double Mass { get; }
    [RuleField("Engine power at the wheels.", Unit = "W", Min = 0)] double Power { get; }
    [RuleField("Grip-limited pulling force.", Unit = "N", Min = 0)] double MaxTractiveForce { get; }
    [RuleField("Drivetrain efficiency.", Unit = "0–1", Min = 0, Max = 1)] double Drivetrain { get; }
    [RuleField("Braking deceleration.", Unit = "m/s²", Min = 0)] double Braking { get; }
    [RuleField("Drag coefficient times frontal area.", Unit = "m²", Min = 0)] double DragArea { get; }
    [RuleField("Fresh round wood density used for the cargo mass.", Unit = "kg/m³", Min = 0)] double TimberDensity { get; }
    [RuleField("Diesel per engine kWh.", Unit = "l/kWh", Min = 0)] double FuelPerKilowattHour { get; }
    [RuleField("Diesel while idling.", Unit = "l/h", Min = 0)] double IdleFuelPerHour { get; }
}

[RuleEntity("machine", Element = "machine", Help = "A forest machine: a processor fells and cuts, a forwarder carries logs to the roadside.")]
internal interface IForestMachine : IWorldObject
{
    [RuleField("Processor or forwarder.")] ForestMachineKind Kind { get; }
    [RuleField("What the machine is doing.")] ForestMachineState State { get; }
    [RuleField("Timber carried.", Unit = "m³", Min = 0)] double Cargo { get; }
    [RuleField("Timber the machine can carry.", Unit = "m³", Min = 0)] double Capacity { get; }
    [RuleField("Cargo as a share of capacity.", Unit = "0–1", Min = 0, Max = 1)] double Fill { get; }
    [RuleField("Diesel burnt so far.", Unit = "l", Min = 0)] double Fuel { get; }
    [RuleField("Wear of the machine; 1 is worn out.", Unit = "0–1", Min = 0, Max = 1)] double Wear { get; set; }
    [RuleRelation("The tile the machine is on.")] ITile Tile { get; }
}

sealed partial class GameWorld
{
    // ── Views: one reusable instance per kind and world, bound to an element before each read ──
    // A view is any concrete RuleView that implements a concrete [RuleEntity] interface; it says which elements a rule
    // visits. Discovery pairs them (DiscoverScopes): a new interface and its view are all a new kind of object needs.
    internal abstract class RuleView
    {
        protected GameWorld World; protected int Id;
        internal void Attach(GameWorld world) => World = world;
        internal void Bind(GameWorld world, int id) { World = world; Id = id; }
        public double Index => Id;
        /// <summary>The elements a rule visits in this step, in a fixed order.</summary>
        internal abstract int[] Elements();
        /// <summary>Element IDs are below this; custom fields keep one value per ID.</summary>
        internal abstract int Size();
        internal virtual bool HasRelation(string relation) => true;
        internal int[] PreparedElements { get; set; }
        internal virtual IEnumerable<int> PrepareElementsWork()
        {
            PreparedElements = Elements();
            yield return 1;
        }
    }

    private sealed class TileView : RuleView, ITile
    {
        // The soil of one tile is looked up once for all the fields read from it (trees of a tile are visited together).
        // Water stores are read directly; trafficability is computed only when a rule reads it.
        private int cached = -1, trafficable = -1; private bool known; private ulong cachedRevision;
        private SoilProperties soil; private double wetness, bearing;
        internal void Invalidate() { cached = -1; trafficable = -1; }
        private bool Inside => World.Environment != null && Id >= 0 && Id < World.Environment.CellCount;
        private void Load()
        {
            if (cached == Id) return;
            cached = Id; known = Inside;
            if (known) soil = World.Soils?.Profile(Id).Properties ?? SoilProperties.Standard;
        }
        private double Read(Func<TileView, double> value) { Load(); return known ? value(this) : 0; }
        private double Store(Ecology.EnvironmentSystem.WaterStore store) => Inside ? World.Environment.Water(Id, store) : 0;
        private (double Wetness, double Bearing) Traffic()
        {
            // Another view may have written this tile's water since: then it is read again.
            ulong revision = World.Environment?.WriteRevision ?? 0;
            if (trafficable != Id || cachedRevision != revision) {
                trafficable = Id; cachedRevision = revision;
                (wetness, bearing) = Inside ? World.Environment.Trafficability(Id) : (0, 0);
            }
            return (wetness, bearing);
        }
        // Every tile, in ID order; the list is kept, as a step asks for it again in every tick it runs.
        private int[] elements = Array.Empty<int>();
        internal override int[] Elements()
        {
            Invalidate();
            if (elements.Length != World.Map.Tiles.Count) elements = Enumerable.Range(0, World.Map.Tiles.Count).ToArray();
            return elements;
        }
        internal override int Size() => World.Map.Tiles.Count;
        public double Moisture => World.Map.GetRuleMoisture(Id);
        // Water stores are state: a rule may set them; the environment books the change in its water balance.
        public double RootWater { get => Store(Ecology.EnvironmentSystem.WaterStore.Soil); set => Write(Ecology.EnvironmentSystem.WaterStore.Soil, value); }
        public double SurfaceWater { get => Store(Ecology.EnvironmentSystem.WaterStore.Surface); set => Write(Ecology.EnvironmentSystem.WaterStore.Surface, value); }
        public double DeepWater { get => Store(Ecology.EnvironmentSystem.WaterStore.Deep); set => Write(Ecology.EnvironmentSystem.WaterStore.Deep, value); }
        private void Write(Ecology.EnvironmentSystem.WaterStore store, double value)
        {
            if (World.Environment == null || Id < 0 || Id >= World.Environment.CellCount) return;
            World.Environment.SetWater(Id, store, value); Invalidate();
        }
        public double ExcessWater => Read(t => Math.Max(0, t.RootWater - t.soil.FieldCapacity));
        public double WaterSupply => Read(t => Math.Clamp(t.RootWater <= t.soil.WiltingPoint ? 0 : t.soil.Availability(t.RootWater), 0, 1));
        public double Waterlogging => Inside ? World.Environment.Waterlogging(Id) : 0;
        public double Infiltration => Read(t => t.soil.InfiltrationPerHour);
        public double Saturation => Read(t => t.soil.Saturation);
        public double FieldCapacity => Read(t => t.soil.FieldCapacity);
        public double WiltingPoint => Read(t => t.soil.WiltingPoint);
        public double Drainage => Read(t => t.soil.DrainagePerHour);
        public double BearingCapacity => Traffic().Bearing;
        public double Cover => Read(t => t.World.Environment.Cover(t.Id));
        public bool Paved => Read(t => t.World.Map.IsRoadTile(t.Id) || t.World.Map.IsBuildingTile(t.Id) ? 1 : 0) != 0;
        public double Wetness => Traffic().Wetness;
        public double CanopyWater { get => Store(Ecology.EnvironmentSystem.WaterStore.Canopy); set => Write(Ecology.EnvironmentSystem.WaterStore.Canopy, value); }
        public double Snow { get => Store(Ecology.EnvironmentSystem.WaterStore.Snow);
            set => Write(Ecology.EnvironmentSystem.WaterStore.Snow, value); }
        // The local weather changes with the environment's steps only; the environment keeps it per tile.
        private Ecology.ClimateCell Climate => World.Environment.LocalClimate(Id);
        public double Rain => Inside ? World.Environment.LiquidRainRate * Climate.RainMultiplier : 0;
        public double Snowfall => Inside ? World.Environment.SnowfallRate * Climate.RainMultiplier : 0;
        public double PotentialEvaporation => Inside ? Climate.Forcing.PotentialEvaporationPerHour : 0;
        public double Temperature => Inside ? Climate.Forcing.Temperature : 0;
        public double LeafAreaIndex => Inside ? World.Environment.HydrologyInputs(Id).LeafAreaIndex : 0;
        public double InterceptionCapacity => Inside ? World.Environment.HydrologyInputs(Id).InterceptionCapacity : 0;
        public bool Sealed => World.Map.IsValidTileId(Id) && ((Ecology.IForestHabitat)World.Map).IsImpervious(Id);
        public bool Outlet => World.Map.IsValidTileId(Id) && ((Ecology.IForestHabitat)World.Map).IsWaterOutlet(Id);
        public double SpringRate => Inside ? World.Environment.SpringRate(Id) : 0;
        public double CanopyFill => InterceptionCapacity > 0 ? Math.Clamp(CanopyWater / InterceptionCapacity, 0, 1) : 1;
        public double Drop
        {
            get {
                double level = WaterLevel, drop = 0;
                foreach (var n in Neighbours) drop += Math.Max(0, level - n.WaterLevel);
                return drop * 1000;
            }
        }
        public double Elevation => World.Map.IsValidTileId(Id) ? ((Ecology.IForestHabitat)World.Map).GetGroundHeight(Id) : 0;
        public double WaterLevel => Elevation + SurfaceWater / 1000;

        // ── Direct access: the same values as the properties above, without binding the view ──
        // Rules visit every tile several times a step, so the fields the environment keeps in arrays are read (and the
        // water stores written) straight from it. Each must compute exactly what its property does: the results are
        // compared bit for bit (RuleStepBenchmark, RuleStepPerformanceTests).
        private static bool Within(GameWorld w, int id) => w.Environment != null && id >= 0 && id < w.Environment.CellCount;
        private static double Store(GameWorld w, int id, Ecology.EnvironmentSystem.WaterStore store) => Within(w, id) ? w.Environment.Water(id, store) : 0;
        private static SoilProperties SoilAt(GameWorld w, int id) => w.Soils?.Profile(id).Properties ?? SoilProperties.Standard;
        private static double Interception(GameWorld w, int id) => Within(w, id) ? w.Environment.HydrologyInputs(id).InterceptionCapacity : 0;
        private static double Height(GameWorld w, int id) => w.Map.IsValidTileId(id) ? ((Ecology.IForestHabitat)w.Map).GetGroundHeight(id) : 0;
        private static double Level(GameWorld w, int id) => Height(w, id) + Store(w, id, Ecology.EnvironmentSystem.WaterStore.Surface) / 1000;
        private static double Flag(bool value) => value ? 1 : 0;

        /// <summary>A reader of the property named <paramref name="property"/> that skips the view; null when it has none.</summary>
        internal static Func<GameWorld, int, double> DirectRead(string property) => property switch {
            nameof(Index) => (w, id) => id,
            nameof(Moisture) => (w, id) => w.Map.GetRuleMoisture(id),
            nameof(RootWater) => (w, id) => Store(w, id, Ecology.EnvironmentSystem.WaterStore.Soil),
            nameof(SurfaceWater) => (w, id) => Store(w, id, Ecology.EnvironmentSystem.WaterStore.Surface),
            nameof(DeepWater) => (w, id) => Store(w, id, Ecology.EnvironmentSystem.WaterStore.Deep),
            nameof(CanopyWater) => (w, id) => Store(w, id, Ecology.EnvironmentSystem.WaterStore.Canopy),
            nameof(Snow) => (w, id) => Store(w, id, Ecology.EnvironmentSystem.WaterStore.Snow),
            nameof(ExcessWater) => (w, id) => Within(w, id) ? Math.Max(0, Store(w, id, Ecology.EnvironmentSystem.WaterStore.Soil) - SoilAt(w, id).FieldCapacity) : 0,
            nameof(WaterSupply) => (w, id) => {
                if (!Within(w, id)) return 0;
                var soil = SoilAt(w, id); double root = Store(w, id, Ecology.EnvironmentSystem.WaterStore.Soil);
                return Math.Clamp(root <= soil.WiltingPoint ? 0 : soil.Availability(root), 0, 1);
            },
            nameof(Waterlogging) => (w, id) => Within(w, id) ? w.Environment.Waterlogging(id) : 0,
            nameof(Infiltration) => (w, id) => Within(w, id) ? SoilAt(w, id).InfiltrationPerHour : 0,
            nameof(Saturation) => (w, id) => Within(w, id) ? SoilAt(w, id).Saturation : 0,
            nameof(FieldCapacity) => (w, id) => Within(w, id) ? SoilAt(w, id).FieldCapacity : 0,
            nameof(WiltingPoint) => (w, id) => Within(w, id) ? SoilAt(w, id).WiltingPoint : 0,
            nameof(Drainage) => (w, id) => Within(w, id) ? SoilAt(w, id).DrainagePerHour : 0,
            nameof(Cover) => (w, id) => Within(w, id) ? w.Environment.Cover(id) : 0,
            nameof(Paved) => (w, id) => Within(w, id) && (w.Map.IsRoadTile(id) || w.Map.IsBuildingTile(id)) ? 1 : 0,
            nameof(Rain) => (w, id) => Within(w, id) ? w.Environment.LiquidRainRate * w.Environment.LocalClimate(id).RainMultiplier : 0,
            nameof(Snowfall) => (w, id) => Within(w, id) ? w.Environment.SnowfallRate * w.Environment.LocalClimate(id).RainMultiplier : 0,
            nameof(PotentialEvaporation) => (w, id) => Within(w, id) ? w.Environment.LocalClimate(id).Forcing.PotentialEvaporationPerHour : 0,
            nameof(Temperature) => (w, id) => Within(w, id) ? w.Environment.LocalClimate(id).Forcing.Temperature : 0,
            nameof(LeafAreaIndex) => (w, id) => Within(w, id) ? w.Environment.HydrologyInputs(id).LeafAreaIndex : 0,
            nameof(InterceptionCapacity) => Interception,
            nameof(CanopyFill) => (w, id) => Interception(w, id) > 0
                ? Math.Clamp(Store(w, id, Ecology.EnvironmentSystem.WaterStore.Canopy) / Interception(w, id), 0, 1) : 1,
            nameof(Drop) => (w, id) => {
                double level = Level(w, id), drop = 0;
                Span<int> ids = stackalloc int[4];
                int count = w.Map.IsValidTileId(id) ? w.Map.GetTileNeighbours(id, ids) : 0;
                for (int i = 0; i < count; i++) drop += Math.Max(0, level - Level(w, ids[i]));
                return drop * 1000;
            },
            nameof(Sealed) => (w, id) => Flag(w.Map.IsValidTileId(id) && ((Ecology.IForestHabitat)w.Map).IsImpervious(id)),
            nameof(Outlet) => (w, id) => Flag(w.Map.IsValidTileId(id) && ((Ecology.IForestHabitat)w.Map).IsWaterOutlet(id)),
            nameof(SpringRate) => (w, id) => Within(w, id) ? w.Environment.SpringRate(id) : 0,
            nameof(Elevation) => Height,
            nameof(WaterLevel) => Level,
            _ => null,
        };

        /// <summary>A writer of the water store named <paramref name="property"/> that skips the view; null for other fields.</summary>
        internal static Action<GameWorld, int, double> DirectWrite(string property)
        {
            Ecology.EnvironmentSystem.WaterStore? store = property switch {
                nameof(RootWater) => Ecology.EnvironmentSystem.WaterStore.Soil, nameof(SurfaceWater) => Ecology.EnvironmentSystem.WaterStore.Surface,
                nameof(DeepWater) => Ecology.EnvironmentSystem.WaterStore.Deep, nameof(CanopyWater) => Ecology.EnvironmentSystem.WaterStore.Canopy,
                nameof(Snow) => Ecology.EnvironmentSystem.WaterStore.Snow, _ => null };
            if (store is not { } s) return null;
            return (w, id, value) => { if (Within(w, id)) w.Environment.SetWater(id, s, value); };
        }

        /// <summary>The members of a collection without binding the view: <see cref="Neighbours"/> by tile ID, in the same order.</summary>
        internal static Func<GameWorld, int, int[]> DirectMembers(string property) => property != nameof(Neighbours) ? null : (w, id) => {
            Span<int> ids = stackalloc int[4];
            int count = w.Map.IsValidTileId(id) ? w.Map.GetTileNeighbours(id, ids) : 0;
            return ids[..count].ToArray();
        };

        // Collections hand out views of their own, so reading them never rebinds the view a rule is visiting.
        private TileView neighbour; private TreeView trees; private StumpView stumps;
        public IEnumerable<ITile> Neighbours
        {
            get {
                var ids = new int[4];
                int count = World.Map.IsValidTileId(Id) ? World.Map.GetTileNeighbours(Id, ids) : 0;
                neighbour ??= new();
                for (int i = 0; i < count; i++) { neighbour.Bind(World, ids[i]); neighbour.Invalidate(); yield return neighbour; }
            }
        }
        public IEnumerable<ITree> Trees
        {
            get {
                if (World.forest == null || !World.forest.IndividualTrees.TryGet(Id, out var patch)) yield break;
                trees ??= new(); int tile = Id;
                trees.Trees = Enumerable.Range(0, patch.Count).Select(i => (tile, i)).ToArray();
                for (int i = 0; i < trees.Trees.Length; i++) { trees.Bind(World, i); yield return trees; }
            }
        }
        public IEnumerable<IStump> Stumps
        {
            get {
                if (World.forest == null || !World.forest.IndividualTrees.TryGet(Id, out var patch) || patch.Stumps == null) yield break;
                stumps ??= new(); int tile = Id;
                stumps.Stumps = Enumerable.Range(0, patch.Stumps.Count).Select(i => (tile, i)).ToArray();
                for (int i = 0; i < stumps.Stumps.Length; i++) { stumps.Bind(World, i); yield return stumps; }
            }
        }
    }

    private sealed class WorldView : RuleView, IWorld
    {
        internal override int[] Elements() => new[] { 0 };
        internal override int Size() => 1;
        public double Time => World.RuleEvaluationYear;
        public double TreeCount => World.ForestTreeCount;
        public double RoadCount => World.RoadCount;
        public double Rain => World.Environment.LiquidRainRate;
        public double Temperature => World.Environment.Temperature;
        public double Snow => World.Environment.SnowWater;
        public double Water => World.Environment.StoredWater;
        public double Income => World.Income;
        public double Expenses => World.Expenses;
        public double RunningCosts => World.Logistics.RunningCosts;
        public double Balance => World.Balance;
        public double HoursPerYear => Ecology.EnvironmentSystem.HoursPerForestYear;
    }

    private sealed class TrafficVehicleView : ITrafficVehicle
    {
        internal TrafficPassage Passage;
        public TrafficVehicleKind Kind => Passage.Kind;
        public double VehicleId => Passage.VehicleId;
        public double Mass => Passage.Mass;
        public double Fill => Passage.Fill;
    }
    private sealed class PassageView : RuleView, IPassage
    {
        private readonly RoadView road = new(); private readonly TrailView trail = new();
        private readonly TileView tile = new(); private readonly TrafficVehicleView vehicle = new();
        internal override int[] Elements() => World.activePassage == null ? Array.Empty<int>() : new[] { World.activePassage.TileId };
        internal override int Size() => World.Map.Tiles.Count;
        public bool IsRoad => World.Map.IsRoadTile(Id);
        public bool IsTrail => World.Map.IsSkidTrail(Id);
        internal override bool HasRelation(string relation) => relation switch { "road" => IsRoad, "trail" => IsTrail, _ => true };
        public ITrafficVehicle Vehicle { get { vehicle.Passage = World.activePassage ?? throw new InvalidOperationException("No passage event is active."); return vehicle; } }
        public IRoad Road { get { road.Bind(World, Id); return road; } }
        public ITrail Trail { get { trail.Bind(World, Id); return trail; } }
        public ITile Tile { get { tile.Bind(World, Id); tile.Invalidate(); return tile; } }
    }

    private sealed class RoadView : RuleView, IRoad
    {
        private readonly TileView tile = new();
        internal override int[] Elements() => World.Map.RoadTiles.OrderBy(id => id).ToArray();
        internal override IEnumerable<int> PrepareElementsWork()
        {
            if (World.Map.RoadCount == 0) { PreparedElements = Array.Empty<int>(); yield break; }
            var ids = new List<int>();
            for (int id = 0; id < World.Map.Tiles.Count; id++) {
                if (World.Map.IsRoadTile(id)) ids.Add(id);
                yield return 1;
            }
            PreparedElements = ids.ToArray();
        }
        internal override int Size() => World.Map.Tiles.Count;
        public double Condition { get => World.Map.GetRoadCondition(Id); set => World.Map.SetRoadCondition(Id, (float)value); }
        public RoadPaving Surface => World.Map.GetRoadPaving(Id);
        public double RainIntensity => (float)Math.Clamp(World.Environment.RainRate / 20, 0, 1);
        public ITile Tile { get { tile.Bind(World, Id); tile.Invalidate(); return tile; } }
    }

    private sealed class TrailView : RuleView, ITrail
    {
        private readonly TileView tile = new();
        internal override int[] Elements() => World.Map.SkidTrailTiles.OrderBy(id => id).ToArray();
        internal override IEnumerable<int> PrepareElementsWork()
        {
            if (World.Map.SkidTrailCount == 0) { PreparedElements = Array.Empty<int>(); yield break; }
            var ids = new List<int>();
            for (int id = 0; id < World.Map.Tiles.Count; id++) {
                if (World.Map.IsSkidTrail(id)) ids.Add(id);
                yield return 1;
            }
            PreparedElements = ids.ToArray();
        }
        internal override int Size() => World.Map.Tiles.Count;
        public double Wear { get => World.Map.GetSkidTrailWear(Id); set => World.Map.SetSkidTrailState(Id, (float)value, World.Map.GetSkidTrailIdle(Id)); }
        public double IdleYears { get => World.Map.GetSkidTrailIdle(Id); set => World.Map.SetSkidTrailState(Id, World.Map.GetSkidTrailWear(Id), (float)value); }
        public ITile Tile { get { tile.Bind(World, Id); tile.Invalidate(); return tile; } }
        public bool Remove() => World.Map.RemoveSkidTrail(Id);
    }

    private sealed class TreeView : RuleView, ITree
    {
        internal (int Tile, int Index)[] Trees = Array.Empty<(int, int)>();
        private readonly TileView tile = new();
        private ref ForestTree Tree
        {
            get {
                var (t, i) = Trees[Id];
                World.forest.IndividualTrees.TryGet(t, out var patch);
                return ref patch.Trees[i];
            }
        }
        public double TreeId => Tree.Id;
        public double Age => Tree.Age(World.RuleEvaluationYear);
        public double Health { get => Tree.Health; set => Tree = Tree with { Health = (float)Math.Clamp(value, 0, 1) }; }
        public ITile Tile { get { tile.Bind(World, Trees[Id].Tile); return tile; } }
        // Size is state anchored at a year; writing it re-anchors the tree there, and growth continues from the new size.
        public double Height { get => Tree.At(World.RuleEvaluationYear).Height; set => Resize(d => d with { Height = (float)Math.Max(0, value) }); }
        public double Diameter { get => Tree.At(World.RuleEvaluationYear).Diameter; set => Resize(d => d with { Diameter = (float)Math.Max(0, value) }); }
        private void Resize(Func<ForestTreeDimensions, ForestTreeDimensions> change)
        {
            var (t, i) = Trees[Id];
            if (!World.forest.IndividualTrees.TryGet(t, out var patch)) return;
            var settled = patch.Trees[i].Settle(World.RuleEvaluationYear);
            patch.Trees[i] = settled with { Dimensions = change(settled.Dimensions) };
            patch.Revision++;
        }
        public double Light => Tree.Resources.Light;
        public double Water => Tree.Resources.Water;
        public double StressYears { get => Tree.StressYears; set => Tree = Tree with { StressYears = (float)Math.Max(0, value) }; }
        public double Space => Tree.Resources.Space;
        public double LightResponse => Tree.Resources.LightResponse(Tree.Species);
        public double ResourceLimit => Tree.Resources.Limitation(Tree.Species);
        public double Fitness => World.forest.SiteFitness(Tree.Species, Trees[Id].Tile);
        public double MaximumAge => ForestSpeciesProfile.For(Tree.Species).MaximumAgeYears;
        public bool Die() { var (tile, index) = Trees[Id]; World.forest.KillIndividual(tile, index, World.RuleEvaluationYear); return false; }
        public double DiameterGrowth { get => Tree.AnnualGrowth.Diameter; set => SetRate(d => d with { Diameter = (float)Math.Clamp(value, 0, 100) }); }
        public double HeightGrowth { get => Tree.AnnualGrowth.Height; set => SetRate(d => d with { Height = (float)Math.Clamp(value, 0, 100) }); }
        public double CrownGrowth { get => Tree.AnnualGrowth.CrownRadius; set => SetRate(d => d with { CrownRadius = (float)Math.Clamp(value, 0, 100) }); }
        private void SetRate(Func<ForestTreeDimensions, ForestTreeDimensions> change) {
            var (t, i) = Trees[Id]; World.forest.SetIndividualRate(t, i, World.RuleEvaluationYear, change);
        }
        public double CrownRadius { get => Tree.At(World.RuleEvaluationYear).CrownRadius; set => Resize(d => d with { CrownRadius = (float)Math.Max(0, value) }); }
        public double RelativeHeight => Height / ForestSpeciesTraits.For(Tree.Species).MaxHeight;
        public double RadialRate => ForestSpeciesTraits.For(Tree.Species).RadialRate;
        public double HeightRate => ForestSpeciesTraits.For(Tree.Species).HeightRate;
        public double CrownRatio => ForestSpeciesTraits.For(Tree.Species).CrownRatio;
        public double CrownSpread => ForestSpeciesTraits.For(Tree.Species).CrownSpread;
        public double YearFraction => World.RuleEvaluationYear - Math.Floor(World.RuleEvaluationYear);
        public double ShadeTolerance => ForestSpeciesProfile.For(Tree.Species).ShadeTolerance;

        /// <summary>A fixed visiting order: tiles ascending, trees in reverse store order (safe deletion).</summary>
        internal override int[] Elements()
        {
            var list = new List<(int, int)>(World.forest.IndividualTreeCount);
            foreach (var (id, patch) in World.forest.IndividualTrees.Patches.OrderBy(p => p.Key))
                for (int i = patch.Count - 1; i >= 0; i--) list.Add((id, i));
            Trees = list.ToArray(); tile.Invalidate();
            return Enumerable.Range(0, Trees.Length).ToArray();
        }
        internal override IEnumerable<int> PrepareElementsWork()
        {
            Trees = new (int, int)[World.forest.IndividualTreeCount];
            PreparedElements = new int[Trees.Length]; int n = 0;
            for (int id = 0; id < World.Map.Tiles.Count; id++) {
                yield return 1;
                if (!World.forest.IndividualTrees.TryGet(id, out var patch)) continue;
                for (int i = patch.Count - 1; i >= 0; i--) {
                    Trees[n] = (id, i); PreparedElements[n] = n; n++; yield return 1;
                }
            }
            tile.Invalidate();
        }
        internal override int Size() => Trees.Length;
    }

    private readonly Dictionary<int, ((int Tile, int Index)[] Trees, int[] Elements)> growthBindings = new();
    private (TreeView View, (int Tile, int Index)[] Trees, int[] Elements) BindGrowthPatch(int tile) {
        var view = (TreeView)View(this, typeof(TreeView));
        var previous = (view, view.Trees, view.PreparedElements);
        forest.IndividualTrees.TryGet(tile, out var p); int count = p?.Count ?? 0;
        if (!growthBindings.TryGetValue(count, out var buffers)) {
            buffers = (new (int, int)[count], new int[count]); growthBindings.Add(count, buffers);
            for (int i = 0; i < count; i++) buffers.Elements[i] = i;
        }
        for (int i = 0; i < count; i++) buffers.Trees[i] = (tile, i);
        view.Trees = buffers.Trees; view.PreparedElements = buffers.Elements;
        return previous;
    }

    private sealed class DeadTreeView : RuleView, IDeadTree
    {
        private (int Tile, int Index)[] trees = System.Array.Empty<(int, int)>();
        private readonly TileView tile = new();
        private ForestDeadTree Tree { get { var (t, i) = trees[Id]; World.forest.IndividualTrees.TryGet(t, out var patch); return patch.DeadTrees[i]; } }
        public double DeadYears => System.Math.Max(0, World.RuleEvaluationYear - Tree.DeathYear);
        public double TreeId => Tree.Tree.Id;
        public double Diameter => Tree.Tree.Dimensions.Diameter;
        public ITile Tile { get { tile.Bind(World, trees[Id].Tile); return tile; } }
        public bool Remove() { var (t, i) = trees[Id]; World.forest.IndividualTrees.TryGet(t, out var patch); patch.DeadTrees.RemoveAt(i); World.forest.IndividualTrees.NotifyTopologyChanged(patch); return false; }
        internal override int Size() => trees.Length;
        internal override int[] Elements() { SimulationWork.Complete(PrepareElementsWork()); return PreparedElements; }
        internal override IEnumerable<int> PrepareElementsWork()
        {
            int count = 0;
            for (int t = 0; t < World.Map.Tiles.Count; t++) {
                if (World.forest.IndividualTrees.TryGet(t, out var p)) count += p.DeadTrees?.Count ?? 0;
                yield return 1;
            }
            trees = new (int, int)[count]; PreparedElements = new int[count]; int n = 0;
            for (int t = 0; t < World.Map.Tiles.Count; t++) {
                yield return 1;
                if (!World.forest.IndividualTrees.TryGet(t, out var patch) || patch.DeadTrees == null) continue;
                for (int i = patch.DeadTrees.Count - 1; i >= 0; i--) {
                    trees[n] = (t, i); PreparedElements[n] = n; n++; yield return 1;
                }
            }
            tile.Invalidate();
        }
    }

    private sealed class StumpView : RuleView, IStump
    {
        internal (int Tile, int Index)[] Stumps = Array.Empty<(int, int)>();
        private readonly TileView tile = new();
        private ForestTreeStump Stump { get { var (t, i) = Stumps[Id]; World.forest.IndividualTrees.TryGet(t, out var p); return p.Stumps[i]; } }
        public double DecayProgress {
            get => Stump.DecayProgress;
            set { var (t, i) = Stumps[Id]; World.forest.IndividualTrees.TryGet(t, out var patch);
                patch.Stumps[i] = patch.Stumps[i] with { DecayProgress = Math.Clamp(value, 0, 1) }; }
        }
        public double Age => Math.Max(0, World.RuleEvaluationYear - Stump.FelledYear);
        public double Diameter => Stump.Felled.Dimensions.Diameter;
        public ITile Tile { get { tile.Bind(World, Stumps[Id].Tile); return tile; } }
        public bool Remove()
        {
            var (t, i) = Stumps[Id];
            World.forest.IndividualTrees.TryGet(t, out var patch);
            patch.Stumps.RemoveAt(i); World.forest.IndividualTrees.NotifyTopologyChanged(patch);
            return false; // vegetation only: the road network is unchanged
        }

        /// <summary>Tiles ascending, stumps of a tile last to first: removing one never moves a stump still to be visited.</summary>
        internal override int[] Elements()
        {
            var list = new List<(int, int)>();
            foreach (var (id, patch) in World.forest.IndividualTrees.Patches.OrderBy(p => p.Key))
                if (patch.Stumps != null) for (int i = patch.Stumps.Count - 1; i >= 0; i--) list.Add((id, i));
            Stumps = list.ToArray(); tile.Invalidate();
            return Enumerable.Range(0, Stumps.Length).ToArray();
        }
        internal override IEnumerable<int> PrepareElementsWork()
        {
            var list = new List<(int, int)>();
            for (int id = 0; id < World.Map.Tiles.Count; id++) {
                yield return 1;
                if (!World.forest.IndividualTrees.TryGet(id, out var patch) || patch.Stumps == null) continue;
                for (int i = patch.Stumps.Count - 1; i >= 0; i--) { list.Add((id, i)); yield return 1; }
            }
            Stumps = list.ToArray(); tile.Invalidate();
            PreparedElements = Enumerable.Range(0, Stumps.Length).ToArray();
        }
        internal override int Size() => Stumps.Length;
    }

    private sealed class TruckView : RuleView, ITruck
    {
        private Vehicle V => World.vehicles.Vehicles[Id];
        internal override int[] Elements() => Enumerable.Range(0, World.vehicles.Vehicles.Count).ToArray();
        internal override int Size() => World.vehicles.Vehicles.Count;
        public double Speed => V.CurrentSpeed;
        public double Distance => V.Distance;
        public double Fuel => V.FuelUsed;
        public double RoadDamage => V.RoadDamage;
        public double Cargo => V.CargoAmount;
        public double Fill => V.CargoFill;
        public double EmptyMass => V.Spec.EmptyMass;
        public double Mass => V.Mass;
        public double Power => V.Spec.Power;
        public double MaxTractiveForce => V.Spec.MaxTractiveForce;
        public double Drivetrain => V.Spec.Drivetrain;
        public double Braking => V.Spec.Braking;
        public double DragArea => V.Spec.DragArea;
        public double TimberDensity => V.Spec.TimberDensity;
        public double FuelPerKilowattHour => V.Spec.FuelPerKilowattHour;
        public double IdleFuelPerHour => V.Spec.IdleFuelPerHour;
    }

    private sealed class MachineView : RuleView, IForestMachine
    {
        private readonly TileView tile = new();
        private ForestMachine M => World.Logistics.Machines[Id];
        internal override int[] Elements() => Enumerable.Range(0, World.Logistics.Machines.Count).ToArray();
        internal override int Size() => World.Logistics.Machines.Count;
        public ForestMachineKind Kind => M.Kind;
        public ForestMachineState State => M.State;
        public double Cargo => M.Cargo;
        public double Capacity => M.Capacity;
        public double Fill => M.CargoFill;
        public double Fuel => M.FuelUsed;
        public double Wear { get => M.Upkeep.Wear; set => M.Upkeep.Wear = (float)Math.Clamp(value, 0, 1); }
        public ITile Tile { get { tile.Bind(World, M.Path is { Length: > 0 } ? M.Tile : -1); tile.Invalidate(); return tile; } }
    }

    private sealed record BoundView(GameWorld World, RuleView View);

    // Views hold per-world element snapshots; each world gets its own instance of every view.
    private static readonly ConditionalWeakTable<GameWorld, Dictionary<Type, RuleView>> Views = new();
    private static RuleView View(GameWorld world, Type type)
    {
        var views = Views.GetValue(world, _ => new());
        lock (views) {
            if (!views.TryGetValue(type, out var view)) {
                views[type] = view = (RuleView)Activator.CreateInstance(type, nonPublic: true);
                view.Attach(world);
            }
            return view;
        }
    }

    private static readonly Lazy<RuleSchema> ruleObjects = new(() => RuleSchema.Discover(RuleEntityTexts.Hungarian, typeof(GameWorld).Assembly));
    /// <summary>The object model of the game, as the editor sees it.</summary>
    internal static RuleSchema RuleObjects => ruleObjects.Value;

    /// <summary>Rule scopes of the game: every concrete kind of the object model with the view that implements it.</summary>
    internal static RuleScope[] DiscoveredScopes() => DiscoverScopes(RuleObjects, typeof(GameWorld).Assembly.GetTypes());

    /// <summary>
    /// Pairs each concrete entity of <paramref name="schema"/> with the concrete <see cref="RuleView"/> among
    /// <paramref name="types"/> implementing it. A kind without a view gets no scope (a test guards against that).
    /// </summary>
    internal static RuleScope[] DiscoverScopes(RuleSchema schema, IEnumerable<Type> types)
    {
        var views = types.Where(t => t.IsClass && !t.IsAbstract && typeof(RuleView).IsAssignableFrom(t)).ToArray();
        var scopes = new List<RuleScope>();
        foreach (var entity in schema.Entities.Where(e => !e.Abstract)) {
            // The view of a kind implements it, and no more specific concrete kind it also implements.
            var matches = views.Where(v => entity.Type.IsAssignableFrom(v) && !schema.Entities.Any(o => !o.Abstract && o != entity
                && entity.Type.IsAssignableFrom(o.Type) && o.Type.IsAssignableFrom(v))).ToArray();
            if (matches.Length > 1) throw new InvalidOperationException($"Több nézet valósítja meg ugyanazt a szabály-objektumot ({entity.Id}): "
                + string.Join(", ", matches.Select(m => m.Name)));
            if (matches.Length == 1) scopes.Add(EntityScope(schema, entity, matches[0]));
        }
        return scopes.ToArray();
    }

    private static RuleScope EntityScope(RuleSchema schema, RuleEntitySchema entity, Type viewType)
    {
        // Every field read binds the kind's view; remembering the last world's view skips the per-world lookup and its lock.
        // The pair is one reference, swapped atomically, so worlds on other threads never see a mixed pair.
        BoundView last = null;
        RuleView Bound(GameWorld w, int id)
        {
            var cached = last;
            if (cached == null || cached.World != w) last = cached = new(w, View(w, viewType));
            cached.View.Bind(w, id); return cached.View;
        }
        // A view may read some fields (and members) without being bound: its static DirectRead/DirectWrite/DirectMembers.
        T Direct<T>(string method, string property) where T : Delegate =>
            (T)viewType.GetMethod(method, System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)?.Invoke(null, new object[] { property });
        var fields = new List<RuleScopeField>();
        foreach (var f in entity.Fields) {
            var get = Getter(f.Property); var set = f.Writable ? Setter(f.Property) : null;
            var directGet = Direct<Func<GameWorld, int, double>>("DirectRead", f.Property.Name);
            var directSet = set == null ? null : Direct<Action<GameWorld, int, double>>("DirectWrite", f.Property.Name);
            fields.Add(new(f.Name, $"{f.Display}: {Help(f)}", directGet ?? ((w, id) => get(Bound(w, id))), set == null ? null
                : directSet != null ? (w, id, v) => directSet(w, id, Math.Clamp(v, f.Min, f.Max)) : (w, id, v) => set(Bound(w, id), Math.Clamp(v, f.Min, f.Max))) {
                Min = f.Min, Max = f.Max, ViewRead = directGet == null ? null : (w, id) => get(Bound(w, id)) });
        }
        // Summaries of the object's own collections (“trees.avg.height”), then the attached objects' fields and summaries.
        foreach (var s in entity.Summaries) {
            var get = Summary(s);
            fields.Add(new(s.Name, $"{s.Display}: {Help(s)}", (w, id) => get(Bound(w, id))));
        }
        foreach (var r in entity.Relations) {
            var target = schema.Find(r.Target); var follow = r.Property.GetMethod;
            foreach (var f in target.Fields) {
                // A writable field of an attached object is writable through the relation: a tree may take its tile's water.
                var get = Getter(f.Property); var set = f.Writable ? Setter(f.Property) : null;
                fields.Add(new(r.Name + "." + f.Name, $"{r.Display} › {f.Display}: {Help(f)}", (w, id) => get(follow.Invoke(Bound(w, id), null)),
                    set == null ? null : (w, id, v) => set(follow.Invoke(Bound(w, id), null), Math.Clamp(v, f.Min, f.Max))) {
                        Min = f.Min, Max = f.Max, Available = (w, id) => Bound(w, id).HasRelation(r.Name) });
            }
            foreach (var s in target.Summaries) {
                var get = Summary(s);
                fields.Add(new(r.Name + "." + s.Name, $"{r.Display} › {s.Display}: {Help(s)}", (w, id) => get(follow.Invoke(Bound(w, id), null))));
            }
        }
        var actions = entity.Actions.Select(a => new RuleScopeAction(a.Name, a.Help, (w, id) => (bool)a.Method.Invoke(Bound(w, id), null))).ToArray();
        // Collections whose members are elements of a kind (a tile's neighbours): flows can write into them by element ID.
        var collections = entity.Collections.Where(c => c.Addressable).Select(c => {
            var items = c.Property.GetMethod;
            return new RuleScopeCollection(c.Name, c.Target, Direct<Func<GameWorld, int, int[]>>("DirectMembers", c.Property.Name)
                ?? ((w, id) => ((IEnumerable<object>)items.Invoke(Bound(w, id), null)).Select(m => (int)((RuleView)m).Index).ToArray()));
        }).ToArray();
        return new(entity.Id, entity.Name, entity.Element, w => View(w, viewType).Elements(), w => View(w, viewType).Size(), fields.ToArray(), actions) {
            PrepareElements = w => View(w, viewType).PrepareElementsWork(), PreparedElements = w => View(w, viewType).PreparedElements,
            Entity = entity.Id, Collections = collections,
            StateIndex = entity.Id == "tree" ? (w, id) => checked((int)((TreeView)Bound(w, id)).TreeId) : (_, id) => id,
            StateSize = entity.Id == "tree" ? w => w.forest.IndividualTrees.StateSize : null };
    }

    private static string Help(RuleFieldSchema f)
    {
        string options = f.Kind == RuleFieldKind.Number ? "" : " [" + string.Join(", ", f.Options.Select(o => $"{o.Display} = {o.Value}")) + "]";
        return (f.Unit.Length == 0 ? f.Help : $"{f.Help} ({f.Unit})") + options;
    }

    /// <summary>Reads a collection summary: how many elements, or the average, smallest, largest or total of one field (0 when empty).</summary>
    private static Func<object, double> Summary(RuleFieldSchema s)
    {
        var items = s.Collection.Property.GetMethod;
        var get = s.Aggregate == "count" ? null : Getter(s.Property);
        return o => {
            double n = 0, sum = 0, min = double.PositiveInfinity, max = double.NegativeInfinity;
            foreach (object item in (System.Collections.IEnumerable)items.Invoke(o, null)) {
                n++;
                if (get == null) continue;
                double v = get(item); sum += v; min = Math.Min(min, v); max = Math.Max(max, v);
            }
            return s.Aggregate switch {
                "count" => n, "sum" => sum, "avg" => n > 0 ? sum / n : 0, "min" => n > 0 ? min : 0, _ => n > 0 ? max : 0 };
        };
    }

    private static Func<object, double> Getter(System.Reflection.PropertyInfo p)
    {
        var o = Expression.Parameter(typeof(object));
        System.Linq.Expressions.Expression value = Expression.Property(Expression.Convert(o, p.DeclaringType), p);
        if (p.PropertyType == typeof(bool)) value = Expression.Condition(value, Expression.Constant(1d), Expression.Constant(0d));
        else if (p.PropertyType.IsEnum) value = Expression.Convert(value, Enum.GetUnderlyingType(p.PropertyType));
        return Expression.Lambda<Func<object, double>>(Expression.Convert(value, typeof(double)), o).Compile();
    }

    private static Action<object, double> Setter(System.Reflection.PropertyInfo p)
    {
        var o = Expression.Parameter(typeof(object)); var v = Expression.Parameter(typeof(double));
        System.Linq.Expressions.Expression value;
        if (p.PropertyType == typeof(bool)) value = Expression.NotEqual(v, Expression.Constant(0d));
        else if (p.PropertyType.IsEnum) value = Expression.Convert(Expression.Convert(
            Expression.Call(typeof(Math).GetMethod(nameof(Math.Round), new[] { typeof(double) }), v), Enum.GetUnderlyingType(p.PropertyType)), p.PropertyType);
        else value = Expression.Convert(v, p.PropertyType);
        var body = Expression.Assign(Expression.Property(Expression.Convert(o, p.DeclaringType), p), value);
        return Expression.Lambda<Action<object, double>>(body, o, v).Compile();
    }

    /// <summary>The world values every formula reads as named values (<c>world.time</c>, <c>world.rain</c>…): the world entity's own fields.</summary>
    internal static string[] RuleWorldFields => worldReaders.Value.Keys;
    private static readonly Lazy<(string[] Keys, Dictionary<string, Func<object, double>> Read)> worldReaders = new(() => {
        var fields = RuleObjects.Find("world").Fields.Where(f => f.DeclaredBy == "world").ToArray();
        return (fields.Select(f => "world." + f.Name).ToArray(), fields.ToDictionary(f => "world." + f.Name, f => Getter(f.Property), StringComparer.Ordinal));
    });
    private double ReadWorldValue(string key) => worldReaders.Value.Read.TryGetValue(key, out var read)
        ? read(View(this, typeof(WorldView))) : throw new ArithmeticException("Ismeretlen világmező: " + key);
}
