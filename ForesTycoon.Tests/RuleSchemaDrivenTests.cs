namespace ForesTycoon.Tests;

// A kind of object only this test knows: an interface and a view are all it takes to become a rule scope.
[RuleEntity("sampleMarker", Element = "marker", Help = "Test-only marker.")]
internal interface ISampleMarker : IWorldObject
{
    [RuleField("A test level.", Min = 0, Max = 10)] double Level { get; set; }
    [RuleField("A test flag.")] bool Lit { get; }
    [RuleField("A test kind.")] RoadPaving Kind { get; }
    [RuleAction("Clears the level.")] bool Clear();
}

internal sealed class SampleMarkerView : GameWorld.RuleView, ISampleMarker
{
    private readonly Dictionary<int, double> levels = new();
    internal override int[] Elements() => new[] { 0, 1, 2 };
    internal override int Size() => 3;
    public double Level { get => levels.GetValueOrDefault(Id); set => levels[Id] = value; }
    public bool Lit => Id == 1;
    public RoadPaving Kind => Id == 2 ? RoadPaving.Macadam : RoadPaving.Asphalt;
    public bool Clear() { levels.Remove(Id); return false; }
}

/// <summary>The rule editor and engine are driven by the discovered object model alone; nothing is listed by hand.</summary>
[Collection("SpeciesBook")]
public sealed class RuleSchemaDrivenTests
{
    private static GameWorld World() => new(TerrainSettings.Default.WithNodeSize(17, 42), enableRendering: false);

    [Fact]
    public void EveryConcreteEntityHasARuntimeScope()
    {
        var missing = GameWorld.RuleObjects.Entities.Where(e => !e.Abstract && RuleEngine.Scope(e.Id) == null).Select(e => e.Id).ToArray();
        Assert.True(missing.Length == 0, "Concrete [RuleEntity] without a RuleView: " + string.Join(", ", missing));
        Assert.All(RuleEngine.Scopes, s => Assert.Equal(s.Id, s.Entity));
        Assert.Contains(RuleEngine.Scopes, s => s.Id == "tile");
        Assert.Contains(RuleEngine.Scopes, s => s.Id == "world");
    }

    [Fact]
    public void ANewInterfaceAndViewAppearInTheSchemaAndScopesWithNoOtherChanges()
    {
        var types = typeof(GameWorld).Assembly.GetTypes().Concat(typeof(RuleSchemaDrivenTests).Assembly.GetTypes()).ToArray();
        var schema = RuleSchema.Discover(types);
        var entity = schema.Find("sampleMarker");
        Assert.NotNull(entity);
        Assert.Equal(new[] { "World object", "Sample marker" }, schema.Lineage("sampleMarker").Select(e => e.Name));
        Assert.Equal(RuleFieldKind.Bool, entity.Fields.Single(f => f.Name == "lit").Kind);
        Assert.Equal(new[] { "asphalt", "macadam" }, entity.Fields.Single(f => f.Name == "kind").Options.Select(o => o.Name));

        var scope = Assert.Single(GameWorld.DiscoverScopes(schema, types), s => s.Id == "sampleMarker");
        Assert.Equal(new[] { "index", "level", "lit", "kind" }, scope.Fields.Select(f => f.Name));
        using var world = World();
        Assert.Equal(new[] { 0, 1, 2 }, scope.Elements(world));
        var level = scope.Fields.Single(f => f.Name == "level");
        level.Write(world, 2, 25);
        Assert.Equal(10, level.Read(world, 2)); // clamped to the declared range
        Assert.Equal(1, scope.Fields.Single(f => f.Name == "lit").Read(world, 1));
        Assert.Equal(1, scope.Fields.Single(f => f.Name == "kind").Read(world, 2));
        Assert.False(Assert.Single(scope.Actions).Apply(world, 2));
        Assert.Equal(0, level.Read(world, 2));
        // The game's own scopes are untouched by a kind it does not know.
        Assert.Null(RuleEngine.Scope("sampleMarker"));
    }

    [Fact]
    public void TheTileIsATypedEntityAndRulesUseItsCurrentFields()
    {
        var tile = RuleEngine.Scope("tile");
        Assert.Contains(tile.Fields, f => f.Name == "rootWater");
        Assert.DoesNotContain(tile.Fields, f => f.Name == "gyökérzónavíz");
        Assert.Equal(RuleFieldKind.Bool, GameWorld.RuleObjects.Find("tile").Fields.Single(f => f.Name == "paved").Kind);

        using var world = World();
        var project = world.RuleProjectDocument; project.Algorithms = null;
        project.Fields.Add(new RuleFieldDefinition { Name = "saját.wet", Scope = "tile", Max = 100 });
        project.Rules.Add(new WorldRule { Id = "typed-fields", Name = "Typed fields", Scope = "tile",
            Condition = "rootWater > wiltingPoint", Effects = { new() { Target = "saját.wet", Mode = "set", Formula = "waterSupply + paved" } } });
        project.Rules.Add(new WorldRule { Id = "typed-road", Name = "Typed road", Scope = "road",
            Effects = { new() { Target = "condition", Mode = "add", Influence = new() { Terms = {
                new() { Name = "Macadam", Amount = 0, Conditions = { new() { Field = "surface", Comparison = "greater", Value = .5 } } } } } } } });
        world.QueueRuleProject(project); world.ExecutePendingCommands();

        var saved = world.RuleProjectDocument.Rules;
        var authored = saved.Single(r => r.Id == "typed-fields");
        Assert.Equal("rootWater > wiltingPoint", authored.Condition);
        Assert.Equal("waterSupply + paved", authored.Effects[0].Formula);
        Assert.Equal("surface", saved.Single(r => r.Id == "typed-road").Effects[0].Influence.Terms[0].Conditions[0].Field);

        world.RuleBook.Run(world, .1, 0);
        Assert.Null(world.RuleBook.LastError);
        var supply = tile.Fields.Single(f => f.Name == "waterSupply"); var root = tile.Fields.Single(f => f.Name == "rootWater");
        var wilting = tile.Fields.Single(f => f.Name == "wiltingPoint"); var paved = tile.Fields.Single(f => f.Name == "paved");
        int id = tile.Elements(world).First(i => root.Read(world, i) > wilting.Read(world, i));
        Assert.Equal(supply.Read(world, id) + paved.Read(world, id), world.RuleBook.ReadCustom(world, "saját.wet", id), 9);
    }

    [Fact]
    public void AnEnumConditionPicksOneOptionOfTheRoadSurface()
    {
        using var world = World();
        for (int id = 0; id < 255 && world.RoadCount == 0; id++) world.Map.BuildRoadTilePath(id, id + 1, RoadPaving.Asphalt);
        for (int id = world.Map.Tiles.Count / 2; id < world.Map.Tiles.Count - 1 && !world.Map.RoadTiles.Any(t => world.Map.GetRoadPaving(t) == RoadPaving.Macadam); id++)
            world.Map.BuildRoadTilePath(id, id + 1, RoadPaving.Macadam);
        int asphalt = world.Map.RoadTiles.First(t => world.Map.GetRoadPaving(t) == RoadPaving.Asphalt);
        int macadam = world.Map.RoadTiles.First(t => world.Map.GetRoadPaving(t) == RoadPaving.Macadam);
        var surface = GameWorld.RuleObjects.Find("road").Fields.Single(f => f.Name == "surface");
        Assert.Equal(RuleFieldKind.Enum, surface.Kind);
        Assert.Equal(new[] { ("asphalt", 0d), ("macadam", 1d) }, surface.Options.Select(o => (o.Name, o.Value)));

        var project = world.RuleProjectDocument; project.Algorithms = null;
        var rule = project.Rules.Single(r => r.Id == "road.weather");
        rule.Effects = new() { new() { Target = "condition", Mode = "add", Influence = new() { PerYear = false, Terms = {
            new() { Name = "Macadam only", Amount = .25, Conditions = { new() { Field = "surface", Comparison = "equal", Value = 1 } } } } } } };
        world.QueueRuleProject(project); world.ExecutePendingCommands();
        world.Map.SetRoadCondition(asphalt, 1); world.Map.SetRoadCondition(macadam, 1);
        world.RuleBook.Run(world, 1, 0);
        Assert.Null(world.RuleBook.LastError);
        Assert.Equal(1, world.Map.GetRoadCondition(asphalt), 6);
        Assert.Equal(.75, world.Map.GetRoadCondition(macadam), 6);
    }

    [Fact]
    public void WorldValuesAreTheWorldEntityFields()
    {
        var entity = GameWorld.RuleObjects.Find("world");
        Assert.Equal(entity.Fields.Where(f => f.DeclaredBy == "world").Concat(entity.Summaries).Select(f => "world." + f.Name), GameWorld.RuleWorldFields);
        Assert.Contains("world.animals.avg.hunger", GameWorld.RuleWorldFields);
        Assert.Contains("world.machines.avg.wear", GameWorld.RuleWorldFields);
        using var world = World();
        for (int i = 0; i < 30; i++) world.Update(1d / 30);
        var scope = RuleEngine.Scope("world");
        Assert.Equal(new[] { 0 }, scope.Elements(world));
        foreach (string key in GameWorld.RuleWorldFields)
            Assert.Equal(world.ReadRuleValue(key), scope.Fields.Single(f => f.Name == key[6..]).Read(world, 0));
        Assert.True(world.ReadRuleValue("world.time") > 0);
    }
}
