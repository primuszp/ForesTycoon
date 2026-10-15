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

// Aliases are the names older projects used in formulas (the Hungarian tile inputs, isMacadam); they are migrated on load.
[RuleEntity("tile", Element = "tile", Help = "One map tile: its soil, water and cover. Other objects read it through their tile relation.")]
internal interface ITile : IPlace
{
    [RuleField("Hydrological map moisture used by road weathering (not root-zone water).", Unit = "0–1", Min = 0, Max = 1)]
    double Moisture { get; }
    [RuleField("Water in the root zone.", Unit = "mm", Min = 0, Aliases = new[] { "gyökérzónavíz" })]
    [RuleCause("tile.cover", -1, "water.interception", Help = "Canopy intercepts rain and transpires.")]
    double RootWater { get; }
    [RuleField("Water standing on the surface.", Unit = "mm", Min = 0, Aliases = new[] { "felszíni_víz" })] double SurfaceWater { get; }
    [RuleField("Water below the root zone.", Unit = "mm", Min = 0, Aliases = new[] { "mélyvíz" })] double DeepWater { get; }
    [RuleField("Root-zone water above field capacity.", Unit = "mm", Min = 0, Aliases = new[] { "többletvíz" })] double ExcessWater { get; }
    [RuleField("Share of plant-available water.", Unit = "0–1", Min = 0, Max = 1, Aliases = new[] { "vízellátottság" })]
    [RuleCause("tile.rootWater", +1, "water.stress")]
    double WaterSupply { get; }
    [RuleField("Share of the soil under standing water.", Unit = "0–1", Min = 0, Max = 1, Aliases = new[] { "pangóvíz" })] double Waterlogging { get; }
    [RuleField("Infiltration rate of the soil.", Unit = "mm/h", Min = 0, Aliases = new[] { "talaj.beszivárgás" })] double Infiltration { get; }
    [RuleField("Total water the soil holds when saturated.", Unit = "mm", Min = 0, Aliases = new[] { "talaj.telítettség" })] double Saturation { get; }
    [RuleField("Field capacity of the soil.", Unit = "mm", Min = 0, Aliases = new[] { "talaj.vízkapacitás" })] double FieldCapacity { get; }
    [RuleField("Wilting point of the soil.", Unit = "mm", Min = 0, Aliases = new[] { "talaj.hervadáspont" })] double WiltingPoint { get; }
    [RuleField("Drainage rate of the soil.", Unit = "mm/h", Min = 0, Aliases = new[] { "talaj.drénezés" })] double Drainage { get; }
    [RuleField("Bearing capacity of the soil.", Unit = "0–1", Min = 0, Max = 1, Aliases = new[] { "talaj.teherbírás" })] double BearingCapacity { get; }
    [RuleField("Canopy cover.", Unit = "0–1", Min = 0, Max = 1, Aliases = new[] { "lombborítás" })]
    [RuleCause("tree.height", +1, "forest.canopy", Help = "Taller trees close the canopy.")]
    double Cover { get; }
    [RuleField("A road or building covers the tile.", Aliases = new[] { "burkolt" })] bool Paved { get; }
    [RuleField("How soaked the soil is.", Unit = "0–1", Min = 0, Max = 1, Aliases = new[] { "átnedvesedés" })] double Wetness { get; }
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
}

[RuleEntity("road", Element = "road", Help = "A paved road tile; weather rules change its condition.")]
internal interface IRoad : IWorldObject
{
    [RuleField("Road condition: 1 is new, 0 is worn out.", Unit = "0–1", Min = 0, Max = 1)] double Condition { get; set; }
    [RuleField("Paving of the road.", Aliases = new[] { "isMacadam" })] RoadPaving Surface { get; }
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

[RuleEntity("plant", Abstract = true, Help = "A living plant: it has an age and a health, and stands on a tile.")]
internal interface IPlant : IWorldObject
{
    [RuleField("Age.", Unit = "forest years", Min = 0)] double Age { get; }
    [RuleField("Vigour; multiplies growth, a plant kept low for long dies.", Unit = "0–1", Min = 0, Max = 1)]
    [RuleCause("tree.light", +1, "forest.health")]
    [RuleCause("tree.water", +1, "forest.health")]
    double Health { get; set; }
    [RuleRelation("The tile the plant stands on.")] ITile Tile { get; }
}

[RuleEntity("tree", Element = "tree", Help = "An individual tree: size, resources and stress. Monthly growth runs natively and sees what rules change.")]
internal interface ITree : IPlant
{
    [RuleField("Tree height.", Unit = "m", Min = 0)]
    [RuleCause("tree.health", +1, "forest.growth")]
    [RuleCause("tree.light", +1, "forest.growth")]
    [RuleCause("tree.water", +1, "forest.growth")]
    double Height { get; }
    [RuleField("Diameter at breast height.", Unit = "m", Min = 0)] double Diameter { get; }
    [RuleField("Light reaching the tree under its neighbours.", Unit = "0–1", Min = 0, Max = 1)]
    [RuleCause("tile.cover", -1, "forest.competition", Help = "Neighbours shade the tree.")]
    double Light { get; }
    [RuleField("Water reaching the tree after root competition.", Unit = "0–1", Min = 0, Max = 1)]
    [RuleCause("tile.rootWater", +1, "forest.competition")]
    double Water { get; }
    [RuleField("Consecutive years short of resources; above 4 a weak tree dies.", Unit = "forest years", Min = 0)]
    [RuleCause("tree.light", -1, "forest.mortality")]
    [RuleCause("tree.water", -1, "forest.mortality")]
    double StressYears { get; set; }
    [RuleField("Shade tolerance of the species.", Unit = "0–1", Min = 0, Max = 1)] double ShadeTolerance { get; }
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
    }

    private sealed class TileView : RuleView, ITile
    {
        // Trees of one tile are visited together: one environment lookup serves them all.
        private int cached = -1; private bool known;
        private Ecology.EnvironmentCell cell; private SoilProperties soil; private double wetness, bearing;
        internal void Invalidate() => cached = -1;
        private void Load()
        {
            if (cached == Id) return;
            cached = Id; var environment = World.Environment;
            known = environment != null && Id >= 0 && Id < environment.CellCount;
            if (!known) return;
            cell = environment.Cell(Id); soil = World.Soils?.Profile(Id).Properties ?? SoilProperties.Standard;
            (wetness, bearing) = environment.Trafficability(Id);
        }
        private double Read(Func<TileView, double> value) { Load(); return known ? value(this) : 0; }
        internal override int[] Elements() { Invalidate(); return Enumerable.Range(0, World.Map.Tiles.Count).ToArray(); }
        internal override int Size() => World.Map.Tiles.Count;
        public double Moisture => World.Map.GetRuleMoisture(Id);
        public double RootWater => Read(t => t.cell.Soil);
        public double SurfaceWater => Read(t => t.cell.Surface);
        public double DeepWater => Read(t => t.cell.Deep);
        public double ExcessWater => Read(t => Math.Max(0, t.cell.Soil - t.soil.FieldCapacity));
        public double WaterSupply => Read(t => Math.Clamp(t.cell.Soil <= t.soil.WiltingPoint ? 0 : t.soil.Availability(t.cell.Soil), 0, 1));
        public double Waterlogging => Read(t => t.cell.Waterlogging);
        public double Infiltration => Read(t => t.soil.InfiltrationPerHour);
        public double Saturation => Read(t => t.soil.Saturation);
        public double FieldCapacity => Read(t => t.soil.FieldCapacity);
        public double WiltingPoint => Read(t => t.soil.WiltingPoint);
        public double Drainage => Read(t => t.soil.DrainagePerHour);
        public double BearingCapacity => Read(t => t.bearing);
        public double Cover => Read(t => t.World.Environment.Cover(t.Id));
        public bool Paved => Read(t => t.World.Map.IsRoadTile(t.Id) || t.World.Map.IsBuildingTile(t.Id) ? 1 : 0) != 0;
        public double Wetness => Read(t => t.wetness);
    }

    private sealed class WorldView : RuleView, IWorld
    {
        internal override int[] Elements() => new[] { 0 };
        internal override int Size() => 1;
        public double Time => World.forest.ForestYear;
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
    }

    private sealed class RoadView : RuleView, IRoad
    {
        private readonly TileView tile = new();
        internal override int[] Elements() => World.Map.RoadTiles.OrderBy(id => id).ToArray();
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
        public double Age => Tree.Age(World.forest.ForestYear);
        public double Health { get => Tree.Health; set => Tree = Tree with { Health = (float)Math.Clamp(value, 0, 1) }; }
        public ITile Tile { get { tile.Bind(World, Trees[Id].Tile); return tile; } }
        public double Height => Tree.At(World.forest.ForestYear).Height;
        public double Diameter => Tree.At(World.forest.ForestYear).Diameter;
        public double Light => Tree.Resources.Light;
        public double Water => Tree.Resources.Water;
        public double StressYears { get => Tree.StressYears; set => Tree = Tree with { StressYears = (float)Math.Max(0, value) }; }
        public double ShadeTolerance => ForestSpeciesProfile.For(Tree.Species).ShadeTolerance;

        /// <summary>A fixed visiting order: tiles ascending, trees in store order.</summary>
        internal override int[] Elements()
        {
            var list = new List<(int, int)>(World.forest.IndividualTreeCount);
            foreach (var (id, patch) in World.forest.IndividualTrees.Patches.OrderBy(p => p.Key))
                for (int i = 0; i < patch.Count; i++) list.Add((id, i));
            Trees = list.ToArray(); tile.Invalidate();
            return Enumerable.Range(0, Trees.Length).ToArray();
        }
        internal override int Size() => Trees.Length;
    }

    private sealed class StumpView : RuleView, IStump
    {
        internal (int Tile, int Index)[] Stumps = Array.Empty<(int, int)>();
        private readonly TileView tile = new();
        private ForestTreeStump Stump { get { var (t, i) = Stumps[Id]; World.forest.IndividualTrees.TryGet(t, out var p); return p.Stumps[i]; } }
        public double DecayProgress {
            get => Stump.DecayProgress ?? Math.Clamp((World.forest.ForestYear - Stump.FelledYear) / ForestTreeStump.LifetimeYears, 0, 1);
            set { var (t, i) = Stumps[Id]; World.forest.IndividualTrees.TryGet(t, out var patch);
                patch.Stumps[i] = patch.Stumps[i] with { DecayProgress = Math.Clamp(value, 0, 1) }; }
        }
        public double Age => Math.Max(0, World.forest.ForestYear - Stump.FelledYear);
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
    }

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
        RuleView Bound(GameWorld w, int id) { var v = View(w, viewType); v.Bind(w, id); return v; }
        var fields = new List<RuleScopeField>();
        foreach (var f in entity.Fields) {
            var get = Getter(f.Property); var set = f.Writable ? Setter(f.Property) : null;
            fields.Add(new(f.Name, $"{f.Display}: {Help(f)}", (w, id) => get(Bound(w, id)), set == null ? null : (w, id, v) => set(Bound(w, id), Math.Clamp(v, f.Min, f.Max))));
        }
        foreach (var r in entity.Relations) {
            var target = schema.Find(r.Target); var follow = r.Property.GetMethod;
            foreach (var f in target.Fields) {
                var get = Getter(f.Property);
                fields.Add(new(r.Name + "." + f.Name, $"{r.Display} › {f.Display}: {Help(f)}", (w, id) => get(follow.Invoke(Bound(w, id), null))));
            }
        }
        var actions = entity.Actions.Select(a => new RuleScopeAction(a.Name, a.Help, (w, id) => (bool)a.Method.Invoke(Bound(w, id), null))).ToArray();
        return new(entity.Id, entity.Name, entity.Element, w => View(w, viewType).Elements(), w => View(w, viewType).Size(), fields.ToArray(), actions) { Entity = entity.Id };
    }

    /// <summary>Former field names on a scope (own and relation fields) mapped to the current ones, for migrating saved rules.</summary>
    internal static IReadOnlyDictionary<string, string> FieldAliases(string scope)
    {
        var names = new Dictionary<string, string>(StringComparer.Ordinal);
        var entity = RuleObjects.Find(scope);
        if (entity == null) return names;
        foreach (var f in entity.Fields) foreach (string alias in f.Aliases) names.TryAdd(alias, f.Name);
        foreach (var r in entity.Relations)
            foreach (var f in RuleObjects.Find(r.Target).Fields) foreach (string alias in f.Aliases) names.TryAdd(r.Name + "." + alias, r.Name + "." + f.Name);
        return names;
    }

    private static string Help(RuleFieldSchema f)
    {
        string options = f.Kind == RuleFieldKind.Number ? "" : " [" + string.Join(", ", f.Options.Select(o => $"{o.Display} = {o.Value}")) + "]";
        return (f.Unit.Length == 0 ? f.Help : $"{f.Help} ({f.Unit})") + options;
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
