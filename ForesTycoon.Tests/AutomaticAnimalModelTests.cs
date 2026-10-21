using System.ComponentModel;
using System.Text.Json;

namespace ForesTycoon.Tests;

// No RuleEntity attribute, no RuleView, no editor registration, no persistence adapter.
[DisplayName("Próbaróka")]
internal sealed class SchemaFox : WildlifeSystem.Animal
{
    [DisplayName("Éberség")]
    [Description("A róka saját, a kód által hozzáadott készlete.")]
    public double Alertness { get; set; }
    [RuleCollection("Az állat kapcsolatai.", Display = "Társak", Addressable = true)]
    public List<SchemaFox> Friends { get; } = new();
}

[Collection("SpeciesBook")]
public sealed class AutomaticAnimalModelTests
{
    private static GameWorld World() => new(TerrainSettings.Default.WithNodeSize(17, 42), enableRendering: false);
    private static RuleSchema Schema() => RuleSchema.Discover(typeof(GameWorld).Assembly.GetTypes().Concat(typeof(SchemaFox).Assembly.GetTypes()), RuleEntityTexts.Hungarian);
    private static string State(GameWorld world) => JsonSerializer.Serialize(world.CaptureCheckpoint());

    [Fact]
    public void DerivedAnimalAndItsOwnPropertiesBecomeExecutableWithoutAnAdapter()
    {
        var schema = Schema(); var foxType = schema.Find("schemaFox");
        Assert.NotNull(foxType); Assert.Equal("Próbaróka", foxType.Name);
        Assert.Equal(new[] { "Világobjektum", "Állat", "Próbaróka" }, schema.Lineage("schemaFox").Select(e => e.Name));
        Assert.Equal("Éberség", foxType.Fields.Single(f => f.Name == "alertness").Display);
        Assert.Contains(foxType.Fields, f => f.Name == "hunger" && f.Writable && f.DeclaredBy == "animal");
        Assert.Contains(foxType.Relations, r => r.Name == "tile" && r.Target == "tile");
        Assert.Equal(foxType.Actions.Select(a => a.Name).Distinct(), foxType.Actions.Select(a => a.Name));
        Assert.Contains(foxType.Summaries, f => f.Name == "friends.avg.alertness");
        var scopes = GameWorld.DiscoverScopes(schema, typeof(GameWorld).Assembly.GetTypes().Concat(typeof(SchemaFox).Assembly.GetTypes()));
        using var world = World(); var fox = new SchemaFox { Id = 7, TileId = 5, Hunger = .5f }; world.Wildlife.Animals.Add(fox);
        var rule = new WorldRule { Id = "fox", Name = "Róka ébersége", Scope = "schemaFox", Schedule = "wildlife",
            Effects = { new() { Target = "alertness", Mode = "set", Formula = "hunger * 2" }, new() { Target = "hunger", Mode = "set", Formula = "0.25" } } };
        var engine = new RuleEngine(new() { rule }, new(), Array.Empty<string>(), _ => 0, scopes: scopes);
        engine.Run(world, .1, 0, "wildlife");
        Assert.Equal(1, fox.Alertness); Assert.Equal(.25f, fox.Hunger); Assert.Null(engine.LastError);
        engine.Run(world, .1, 0, "wildlife"); Assert.Equal(.5, fox.Alertness);
        var snapshot = GameWorld.CaptureObjectFields(fox, schema);
        var restored = new SchemaFox(); GameWorld.RestoreObjectFields(restored, snapshot, schema);
        Assert.Equal(fox.Alertness, restored.Alertness); Assert.Equal(fox.Hunger, restored.Hunger);
    }

    [Fact]
    public void DiscoveredClassRelationshipsSupportConservedFlowsAndPublishedDisplayNames()
    {
        var schema = Schema(); var scopes = GameWorld.DiscoverScopes(schema, typeof(GameWorld).Assembly.GetTypes().Concat(typeof(SchemaFox).Assembly.GetTypes()));
        using var world = World();
        var from = new SchemaFox { Id = 7, Alertness = 10 }; var to = new SchemaFox { Id = 9, Alertness = 1 };
        from.Friends.Add(to); world.Wildlife.Animals.AddRange(new[] { from, to });
        var rule = new WorldRule { Id = "flow", Name = "Társak kapcsolata", Scope = "schemaFox", Schedule = "wildlife", Condition = "index < 8",
            Effects = { new() { Target = "alertness", Mode = "flow", To = "friends.alertness", Formula = "4", FlowName = "Átadás" } } };
        var engine = new RuleEngine(new() { rule }, new(), Array.Empty<string>(), _ => 0, scopes: scopes);
        engine.Run(world, 1, 0, "wildlife");
        Assert.Null(engine.LastError); Assert.Equal(6, from.Alertness); Assert.Equal(5, to.Alertness);
        var manifest = RuleObjectModel.Describe(schema);
        Assert.Equal(manifest.Revision, RuleObjectModel.Describe(Schema()).Revision);
        Assert.NotEqual(RuleObjectModel.Describe(GameWorld.RuleObjects).Revision, manifest.Revision);
        var fox = manifest.Types.Single(t => t.Id == "schemaFox"); Assert.Equal("Próbaróka", fox.Display);
        Assert.Contains(fox.Fields, f => f.Name == "alertness" && f.Display == "Éberség" && f.Writable);
        Assert.Equal("schemaFox", Assert.Single(fox.Collections).Target);
        var published = RuleWorldCatalog.FromJson(new RuleWorldCatalog { ObjectModel = manifest }.ToJson());
        Assert.Equal(manifest.Revision, published.ObjectModel.Revision);
        Assert.Equal("Éberség", published.ObjectModel.Types.Single(t => t.Id == "schemaFox").Fields.Single(f => f.Name == "alertness").Display);
    }

    [Fact]
    public void CustomStocksFollowIdentityInsteadOfPositionsInTheAnimalList()
    {
        var schema = Schema(); var scopes = GameWorld.DiscoverScopes(schema, typeof(GameWorld).Assembly.GetTypes().Concat(typeof(SchemaFox).Assembly.GetTypes()));
        using var world = World();
        world.Wildlife.Animals.Add(new SchemaFox { Id = 7, TileId = 5 }); world.Wildlife.Animals.Add(new SchemaFox { Id = 9, TileId = 6 });
        var rule = new WorldRule { Id = "memory", Name = "Emlékezet", Scope = "schemaFox", Schedule = "wildlife", Effects = { new() { Target = "saját.memory", Mode = "add", Formula = "1" } } };
        var engine = new RuleEngine(new() { rule }, new() { new() { Name = "saját.memory", Scope = "schemaFox" } }, Array.Empty<string>(), _ => 0, scopes: scopes);
        engine.Run(world, .1, 0, "wildlife"); world.Wildlife.Animals.RemoveAt(0);
        world.Wildlife.Animals.Add(new SchemaFox { Id = 10, TileId = 7 }); engine.Run(world, .1, 0, "wildlife");
        Assert.Equal(2, engine.ReadCustom(world, "saját.memory", 9)); Assert.Equal(1, engine.ReadCustom(world, "saját.memory", 10));
    }

    [Fact]
    public void RemovingAnimalStateRulesStopsThoseProcessesWithoutNativeFallback()
    {
        using var world = World(); var p = world.RuleProjectDocument;
        p.Rules.RemoveAll(r => r.Id is "wildlife.reconcile" or "wildlife.grazing");
        world.QueueRuleProject(p); world.ExecutePendingCommands();
        var deer = new WildlifeSystem.Deer { Id = 1, TileId = 5, TargetTile = 5, Hunger = .2f }; world.Wildlife.Animals.Add(deer);
        world.Update(1d / 30); Assert.True(deer.Age > 0); Assert.True(deer.Hunger > .2f);
        p = world.RuleProjectDocument; p.Rules.RemoveAll(r => r.Schedule == "wildlife");
        world.QueueRuleProject(p); world.ExecutePendingCommands();
        double age = deer.Age; float hunger = deer.Hunger;
        world.Update(1d / 30); Assert.Equal(age, deer.Age); Assert.Equal(hunger, deer.Hunger);
    }

    [Fact]
    public void AnimalTypeFieldsAndUserStocksSurviveSaveAndContinuation()
    {
        using var world = World(); var p = world.RuleProjectDocument; p.Rules.RemoveAll(r => r.Id == "wildlife.reconcile");
        p.Fields.Add(new() { Name = "saját.attention", Scope = "deer" });
        p.Rules.Add(new() { Id = "attention", Name = "Figyelem", Scope = "deer", Schedule = "wildlife", Effects = { new() { Target = "saját.attention", Mode = "add", Formula = "lépés" } } });
        world.QueueRuleProject(p); world.ExecutePendingCommands(); world.Wildlife.Animals.Add(new WildlifeSystem.Deer { Id = 4, TileId = 5, TargetTile = 5 });
        world.Wildlife.NextAnimalId = 5; world.Update(1d / 30);
        world.Wildlife.Animals[0].DistanceMoved = 1.25;
        using var saved = new MemoryStream(); world.Save(saved); saved.Position = 0;
        using var restored = World(); restored.Load(saved);
        Assert.IsType<WildlifeSystem.Deer>(Assert.Single(restored.Wildlife.Animals)); Assert.Equal(State(world), State(restored));
        for (int i = 0; i < 15; i++) { world.Update(1d / 30); restored.Update(1d / 30); }
        Assert.Equal(State(world), State(restored));
        Assert.Equal(world.RuleBook.ReadCustom(world, "saját.attention", 4), restored.RuleBook.ReadCustom(restored, "saját.attention", 4));
    }

    [Fact]
    public void PolymorphicAnimalRulesAndWritesThroughRelationsAppearOnTheCausalMap()
    {
        var map = RuleCausalMap.Build(GameWorld.RuleObjects, RuleProject.CreateDefault().Rules);
        Assert.Contains(map.Edges, e => e.Rule == "wildlife.grazing" && e.From == "animal.food" && e.To == "animal.hunger");
        Assert.Contains(map.Edges, e => e.Rule == "wildlife.grazing" && e.From == "animal.arrived" && e.To == "tile.forageDepletion");
    }

    [Fact]
    public void AnimalSchedulingKeepsTheCommonClockAndDoesNotHideErrorsFromOtherSchedules()
    {
        using var world = World(); var p = world.RuleProjectDocument;
        p.Rules.RemoveAll(r => r.Id == "wildlife.reconcile");
        p.Fields.Add(new() { Name = "saját.clock", Scope = "deer" });
        p.Rules.Add(new() { Id = "clock", Name = "Közös óra", Scope = "deer", Schedule = "wildlife",
            Effects = { new() { Target = "saját.clock", Mode = "set", Formula = "idő" } } });
        p.Rules.Add(new() { Id = "bad", Name = "Hibás képlet", Scope = "deer", Schedule = "step",
            Effects = { new() { Target = "hunger", Mode = "set", Formula = "1 / 0" } } });
        world.QueueRuleProject(p); world.ExecutePendingCommands();
        world.Wildlife.Animals.Add(new WildlifeSystem.Deer { Id = 1, TileId = 5, TargetTile = 5 }); world.Wildlife.NextAnimalId = 2;
        for (int i = 0; i < 18; i++) world.Update(1d / 30);
        Assert.Equal(world.Forest.ForestYear, world.RuleBook.ReadCustom(world, "saját.clock", 1));
        Assert.NotNull(world.RuleBook.LastError);
    }

    [Fact]
    public void LegacyHungerGraphBecomesAnEditableObjectEquationExactlyOnce()
    {
        var p = RuleProject.CreateDefault(); p.Version = 2; p.Rules.RemoveAll(r => r.Schedule == "wildlife");
        p.Behaviors.Graphs.Add(new() { Id = "old-hunger", Name = "Saját régi éhség", Kind = "gameplay", Hook = "wildlife.hungerRate", Output = "rate",
            Nodes = new() { new() { Id = "rate", Name = "Ütem", Operation = BehaviorOperation.Constant, Value = .5 } } });
        p = GameWorld.NormalizeRuleProject(p); Assert.Equal(RuleProject.CurrentVersion, p.Version);
        Assert.NotNull(p.Rules.Single(r => r.Id == "wildlife.ageAndHunger").Effects.Single(e => e.Target == "hunger").Computation);
        Assert.DoesNotContain(p.Behaviors.Graphs, g => g.Hook == "wildlife.hungerRate");
        p.Rules.RemoveAll(r => r.Schedule == "wildlife");
        Assert.DoesNotContain(GameWorld.NormalizeRuleProject(p).Rules, r => r.Schedule == "wildlife");
    }
}
