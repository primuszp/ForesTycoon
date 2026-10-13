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

[RuleEntity("tileEnvironment", Element = "tile", Abstract = true,
    Help = "Soil and water of a tile. Write tile rules on the Tile scope; here it is read through relations.")]
internal interface ITileEnvironment : IPlace
{
    [RuleField("Water in the root zone.", Unit = "mm")]
    [RuleCause("tileEnvironment.cover", -1, "water.interception", Help = "Canopy intercepts rain and transpires.")]
    double RootWater { get; }
    [RuleField("Share of plant-available water.", Unit = "0–1", Min = 0, Max = 1)]
    [RuleCause("tileEnvironment.rootWater", +1, "water.stress")]
    double WaterSupply { get; }
    [RuleField("Share of the soil under standing water.", Unit = "0–1", Min = 0, Max = 1)] double Waterlogging { get; }
    [RuleField("Canopy cover.", Unit = "0–1", Min = 0, Max = 1)]
    [RuleCause("tree.height", +1, "forest.canopy", Help = "Taller trees close the canopy.")]
    double Cover { get; }
    [RuleField("Field capacity of the soil.", Unit = "mm")] double FieldCapacity { get; }
}

[RuleEntity("plant", Abstract = true, Help = "A living plant: it has an age and a health, and stands on a tile.")]
internal interface IPlant : IWorldObject
{
    [RuleField("Age.", Unit = "forest years", Min = 0)] double Age { get; }
    [RuleField("Vigour; multiplies growth, a plant kept low for long dies.", Unit = "0–1", Min = 0, Max = 1)]
    [RuleCause("tree.light", +1, "forest.health")]
    [RuleCause("tree.water", +1, "forest.health")]
    double Health { get; set; }
    [RuleRelation("The tile the plant stands on.")] ITileEnvironment Tile { get; }
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
    [RuleCause("tileEnvironment.cover", -1, "forest.competition", Help = "Neighbours shade the tree.")]
    double Light { get; }
    [RuleField("Water reaching the tree after root competition.", Unit = "0–1", Min = 0, Max = 1)]
    [RuleCause("tileEnvironment.rootWater", +1, "forest.competition")]
    double Water { get; }
    [RuleField("Consecutive years short of resources; above 4 a weak tree dies.", Unit = "forest years", Min = 0)]
    [RuleCause("tree.light", -1, "forest.mortality")]
    [RuleCause("tree.water", -1, "forest.mortality")]
    double StressYears { get; }
    [RuleField("Shade tolerance of the species.", Unit = "0–1", Min = 0, Max = 1)] double ShadeTolerance { get; }
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
    // ── Views: one reusable instance per kind, bound to an element before each read ──
    private abstract class RuleView
    {
        protected GameWorld World; protected int Id;
        internal void Bind(GameWorld world, int id) { World = world; Id = id; }
        public double Index => Id;
    }

    private sealed class TileView : RuleView, ITileEnvironment
    {
        private Dictionary<string, double> cache; private int cached = -1;
        private double Get(string name)
        {
            // Trees of one tile are visited together: one environment lookup serves them all.
            if (cached != Id || cache == null) { cache = World.TileRuleInputs(Id); cached = Id; }
            return cache.TryGetValue(name, out double v) ? v : 0;
        }
        internal void Invalidate() => cached = -1;
        public double RootWater => Get("gyökérzónavíz");
        public double WaterSupply => Get("vízellátottság");
        public double Waterlogging => Get("pangóvíz");
        public double Cover => Get("lombborítás");
        public double FieldCapacity => Get("talaj.vízkapacitás");
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
        public ITileEnvironment Tile { get { tile.Bind(World, Trees[Id].Tile); return tile; } }
        public double Height => Tree.At(World.forest.ForestYear).Height;
        public double Diameter => Tree.At(World.forest.ForestYear).Diameter;
        public double Light => Tree.Resources.Light;
        public double Water => Tree.Resources.Water;
        public double StressYears => Tree.StressYears;
        public double ShadeTolerance => ForestSpeciesProfile.For(Tree.Species).ShadeTolerance;

        /// <summary>A fixed visiting order: tiles ascending, trees in store order.</summary>
        internal int[] Snapshot(GameWorld world)
        {
            var list = new List<(int, int)>(world.forest.IndividualTreeCount);
            foreach (var (id, patch) in world.forest.IndividualTrees.Patches.OrderBy(p => p.Key))
                for (int i = 0; i < patch.Count; i++) list.Add((id, i));
            Trees = list.ToArray(); tile.Invalidate();
            return Enumerable.Range(0, Trees.Length).ToArray();
        }
    }

    private sealed class TruckView : RuleView, ITruck
    {
        private Vehicle V => World.vehicles.Vehicles[Id];
        public double Speed => V.CurrentSpeed;
        public double Distance => V.Distance;
        public double Fuel => V.FuelUsed;
        public double RoadDamage => V.RoadDamage;
        public double Cargo => V.CargoAmount;
        public double Fill => V.CargoFill;
        public double EmptyMass => V.Spec.EmptyMass;
    }

    // Views hold per-world element snapshots; each world gets its own.
    private static readonly ConditionalWeakTable<GameWorld, TreeView> TreeViews = new();
    private static readonly ConditionalWeakTable<GameWorld, TruckView> TruckViews = new();

    /// <summary>The object model of the game, as the editor sees it.</summary>
    internal static RuleSchema RuleObjects { get; } = RuleSchema.Discover(typeof(GameWorld).Assembly);

    /// <summary>Rule scopes generated from the object model: fields, relation fields (“csempe.x”) and actions.</summary>
    internal static RuleScope[] DiscoveredScopes() => new[] {
        EntityScope(RuleObjects.Find("tree"), w => { var v = TreeViews.GetValue(w, _ => new()); return v.Snapshot(w); },
            w => TreeViews.GetValue(w, _ => new()).Trees.Length, (w, id) => { var v = TreeViews.GetValue(w, _ => new()); v.Bind(w, id); return v; }),
        EntityScope(RuleObjects.Find("truck"), w => Enumerable.Range(0, w.vehicles.Vehicles.Count).ToArray(), w => w.vehicles.Vehicles.Count,
            (w, id) => { var v = TruckViews.GetValue(w, _ => new()); v.Bind(w, id); return v; }),
    };

    private static RuleScope EntityScope(RuleEntitySchema entity, Func<GameWorld, int[]> elements, Func<GameWorld, int> size, Func<GameWorld, int, object> bind)
    {
        var fields = new List<RuleScopeField>();
        foreach (var f in entity.Fields) {
            var get = Getter(f.Property); var set = f.Writable ? Setter(f.Property) : null;
            fields.Add(new(f.Name, $"{f.Display}: {Help(f)}", (w, id) => get(bind(w, id)), set == null ? null : (w, id, v) => set(bind(w, id), Math.Clamp(v, f.Min, f.Max))));
        }
        foreach (var r in entity.Relations) {
            var target = RuleObjects.Find(r.Target); var follow = r.Property.GetMethod;
            foreach (var f in target.Fields) {
                var get = Getter(f.Property);
                fields.Add(new(r.Name + "." + f.Name, $"{r.Display} › {f.Display}: {Help(f)}", (w, id) => get(follow.Invoke(bind(w, id), null))));
            }
        }
        var actions = entity.Actions.Select(a => new RuleScopeAction(a.Name, a.Help, (w, id) => (bool)a.Method.Invoke(bind(w, id), null))).ToArray();
        return new(entity.Id, entity.Name, entity.Element, elements, size, fields.ToArray(), actions) { Entity = entity.Id };
    }

    private static string Help(RuleFieldSchema f) => f.Unit.Length == 0 ? f.Help : $"{f.Help} ({f.Unit})";

    private static Func<object, double> Getter(System.Reflection.PropertyInfo p)
    {
        var o = Expression.Parameter(typeof(object));
        var body = Expression.Convert(Expression.Property(Expression.Convert(o, p.DeclaringType), p), typeof(double));
        return Expression.Lambda<Func<object, double>>(body, o).Compile();
    }

    private static Action<object, double> Setter(System.Reflection.PropertyInfo p)
    {
        var o = Expression.Parameter(typeof(object)); var v = Expression.Parameter(typeof(double));
        var body = Expression.Assign(Expression.Property(Expression.Convert(o, p.DeclaringType), p), Expression.Convert(v, p.PropertyType));
        return Expression.Lambda<Action<object, double>>(body, o, v).Compile();
    }
}
