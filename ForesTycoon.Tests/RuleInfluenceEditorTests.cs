using ForesTycoon.Editor;

namespace ForesTycoon.Tests;

[Collection("SpeciesBook")]
public sealed class RuleInfluenceEditorTests
{
    [Theory]
    [InlineData("add", true, true)]
    [InlineData("set", true, true)]
    [InlineData("flow", true, true)]
    [InlineData("approach", false, true)]
    [InlineData("action", false, false)]
    public void EmptyConnectionsKeepTheExistingModeSpecificDefaults(string mode, bool decrease, bool enabled)
    {
        var (document, rule, effect) = Setup(mode);
        var term = document.RuleBook.Influences.AddTerm(rule, effect)!;
        Assert.Equal(decrease, term.Decrease); Assert.Equal(enabled, term.Enabled);
        Assert.Empty(term.Factors); Assert.Empty(term.Conditions);
    }

    [Fact]
    public void WiringCreatesAnEnabledActionOrAddsAMultiplierToTheSelectedConnection()
    {
        var (document, rule, effect) = Setup("action"); var editor = document.RuleBook.Influences;
        Assert.Equal(0, editor.ConnectSource(rule, "condition", 0, -1, "Condition"));
        var term = Assert.Single(effect.Influence.Terms);
        Assert.Equal("Condition", term.Name); Assert.True(term.Enabled); Assert.False(term.Decrease);
        Assert.Equal(0, editor.ConnectSource(rule, "tile.moisture", 0, 0, "Unused"));
        Assert.Single(effect.Influence.Terms);
        Assert.Equal(new[] { "condition", "tile.moisture" }, term.Factors.Select(f => f.Source));
    }

    [Fact]
    public void FullConnectionsStillAcceptMultipliersAndRejectedEditsLeaveTheRevisionAlone()
    {
        var (document, rule, effect) = Setup(); var editor = document.RuleBook.Influences;
        for (int i = 0; i < 16; i++) Assert.NotNull(editor.AddTerm(rule, effect));
        long version = document.Version;
        Assert.Null(editor.AddTerm(rule, effect)); Assert.Null(editor.ConnectSource(rule, "condition", 0, -1, "Full"));
        Assert.Equal(version, document.Version);
        for (int i = 0; i < 8; i++) Assert.Equal(0, editor.ConnectSource(rule, "condition", 0, 0, "Multiplier"));
        version = document.Version;
        Assert.Null(editor.ConnectSource(rule, "condition", 0, 0, "Full"));
        Assert.Null(editor.AddFactor(rule, effect, effect.Influence.Terms[0], "condition"));
        Assert.Equal(version, document.Version); Assert.Equal(16, effect.Influence.Terms.Count);
        Assert.Equal(8, effect.Influence.Terms[0].Factors.Count);
    }

    [Fact]
    public void MissingCanvasTargetsAreHarmlessAndDoNotTurnCalculationsIntoVisualEffects()
    {
        var (document, rule, effect) = Setup(); var editor = document.RuleBook.Influences;
        long version = document.Version;
        Assert.Null(editor.ConnectSource(rule, "condition", -1, -1, "Missing"));
        Assert.Null(editor.ConnectSource(rule, "condition", 1, -1, "Missing"));
        Assert.Null(editor.ConnectSource(rule, "condition", 0, 0, "Missing"));
        Assert.Equal(version, document.Version);
        effect.Influence = null; effect.Formula = "1";
        Assert.Null(editor.ConnectSource(rule, "condition", 0, -1, "Calculation"));
        Assert.Equal("1", effect.Formula); Assert.Null(effect.Influence); Assert.Equal(version, document.Version);
    }

    [Theory]
    [InlineData("step", "flow", true)]
    [InlineData("step", "add", true)]
    [InlineData("step", "set", false)]
    [InlineData("step", "approach", false)]
    [InlineData("passage", "flow", false)]
    [InlineData("growth", "add", false)]
    public void ModeChangesRetainTimeScalingAndFlowConversionRules(string schedule, string mode, bool perYear)
    {
        var (document, rule, effect) = Setup(mode == "add" ? "set" : "add"); var editor = document.RuleBook.Influences;
        rule.Schedule = schedule; effect.To = "oldDestination"; effect.Influence.PerHour = true;
        var term = editor.AddTerm(rule, effect)!; term.Decrease = true;
        editor.SetMode(rule, effect, mode, "Flow title");
        Assert.Equal(mode, effect.Mode); Assert.Equal(perYear, effect.Influence.PerYear); Assert.False(effect.Influence.PerHour);
        Assert.Equal(mode != "flow", term.Decrease);
        Assert.Equal(mode == "flow" ? "" : "oldDestination", effect.To);
        if (mode == "flow") Assert.Equal("Flow title", effect.FlowName);
        long version = document.Version;
        editor.SetMode(rule, effect, mode, "Ignored"); Assert.Equal(version, document.Version);
        Assert.Throws<ArgumentException>(() => editor.SetMode(rule, effect, "unknown", "Invalid"));
        Assert.Equal(version, document.Version); Assert.Equal(mode, effect.Mode);
    }

    [Fact]
    public void ConditionsRespectTheirLimitAndDeletingOneLeavesItsNeighborsIntact()
    {
        var (document, rule, effect) = Setup(); var editor = document.RuleBook.Influences;
        var term = editor.AddTerm(rule, effect)!;
        for (int i = 0; i < 8; i++) Assert.NotNull(editor.AddCondition(rule, effect, term, "field" + i));
        long version = document.Version;
        Assert.Null(editor.AddCondition(rule, effect, term, "overflow")); Assert.Equal(version, document.Version);
        var deleted = term.Conditions[3]; editor.DeleteCondition(rule, effect, term, deleted);
        Assert.Equal(new[] { "field0", "field1", "field2", "field4", "field5", "field6", "field7" }, term.Conditions.Select(c => c.Field));
        Assert.Throws<InvalidOperationException>(() => editor.EditCondition(rule, effect, term, deleted, c => c.Value = 9));
        Assert.NotNull(editor.AddCondition(rule, effect, term, "replacement"));
    }

    [Fact]
    public void OptionConditionsClearNumericSourcesAndNormalizationReportsOnlyActualChanges()
    {
        var (document, rule, effect) = Setup(); var editor = document.RuleBook.Influences;
        var term = editor.AddTerm(rule, effect)!; var condition = editor.AddCondition(rule, effect, term, "number")!;
        condition.Comparison = "greater"; condition.Value = 42; condition.ValueField = "other";
        condition.ValueCurve = new();
        RuleFieldOption[] options = { new("off", "Off", 0), new("on", "On", 1) };
        long version = document.Version;
        Assert.True(editor.NormalizeCondition(rule, effect, term, condition, options));
        Assert.True(document.Version > version); Assert.Equal("equal", condition.Comparison); Assert.Equal(1, condition.Value);
        Assert.Empty(condition.ValueField); Assert.Null(condition.ValueCurve);
        version = document.Version;
        Assert.False(editor.NormalizeCondition(rule, effect, term, condition, options)); Assert.Equal(version, document.Version);
        editor.EditCondition(rule, effect, term, condition, c => { c.Comparison = "notEqual"; c.Value = 0; });
        editor.SetConditionField(rule, effect, term, condition, "flag", options);
        Assert.Equal("notEqual", condition.Comparison); Assert.Equal(0, condition.Value);
        editor.SetConditionField(rule, effect, term, condition, "number", Array.Empty<RuleFieldOption>());
        Assert.Equal("atLeast", condition.Comparison);
    }

    [Fact]
    public void ThresholdSwitchingKeepsExactlyOneSourceAndRemovedCurvesCannotBeEdited()
    {
        var (document, rule, effect) = Setup(); var editor = document.RuleBook.Influences;
        var term = editor.AddTerm(rule, effect)!; var condition = editor.AddCondition(rule, effect, term, "condition")!;
        string[] fields = { "condition", "shadeTolerance", "tile.moisture" };
        Assert.True(editor.SetThreshold(rule, effect, term, condition, InfluenceThresholdKind.Field, fields));
        Assert.Equal("shadeTolerance", condition.ValueField); Assert.Null(condition.ValueCurve);
        Assert.True(editor.SetThreshold(rule, effect, term, condition, InfluenceThresholdKind.Curve, fields));
        var curve = Assert.IsType<InfluenceFactor>(condition.ValueCurve); Assert.Empty(condition.ValueField); Assert.Equal("shadeTolerance", curve.Source);
        editor.EditFactor(rule, effect, term, curve, f => f.Source = "tile.moisture");
        Assert.True(editor.SetThreshold(rule, effect, term, condition, InfluenceThresholdKind.Constant, fields));
        Assert.Empty(condition.ValueField); Assert.Null(condition.ValueCurve);
        long version = document.Version;
        Assert.Throws<InvalidOperationException>(() => editor.EditFactor(rule, effect, term, curve, f => f.Source = "stale"));
        Assert.False(editor.SetThreshold(rule, effect, term, condition, InfluenceThresholdKind.Field, Array.Empty<string>()));
        Assert.Throws<ArgumentOutOfRangeException>(() => editor.SetThreshold(rule, effect, term, condition, (InfluenceThresholdKind)99, fields));
        Assert.Equal(version, document.Version); Assert.Equal("tile.moisture", curve.Source);
    }

    [Fact]
    public void DeletedOrForeignNestedObjectsCannotChangeTheDocument()
    {
        var (document, rule, effect) = Setup(); var editor = document.RuleBook.Influences;
        var term = editor.AddTerm(rule, effect)!; var other = editor.AddTerm(rule, effect)!;
        var factor = editor.AddFactor(rule, effect, term, "condition")!;
        var condition = editor.AddCondition(rule, effect, term, "condition")!;
        long version = document.Version;
        Assert.Throws<InvalidOperationException>(() => editor.DeleteFactor(rule, effect, other, factor));
        Assert.Throws<InvalidOperationException>(() => editor.DeleteCondition(rule, effect, other, condition));
        Assert.Equal(version, document.Version);
        editor.DeleteFactor(rule, effect, term, factor); editor.DeleteTerm(rule, effect, term); version = document.Version;
        Assert.Throws<InvalidOperationException>(() => editor.EditFactor(rule, effect, term, factor, f => f.Source = "stale"));
        Assert.Throws<InvalidOperationException>(() => editor.AddCondition(rule, effect, term, "stale"));
        Assert.Equal(version, document.Version); Assert.Same(other, Assert.Single(effect.Influence.Terms));
    }

    [Fact]
    public void RestoringDocumentHistoryKeepsTheEditorButRejectsOldReferences()
    {
        var (document, rule, effect) = Setup(); var editor = document.RuleBook.Influences;
        var history = new EditorHistory<string>(); history.Record(document.Capture().ToJson(), "open");
        string revision = document.Revision();
        editor.ConnectSource(rule, "condition", 0, -1, "Recovery");
        Assert.NotEqual(revision, document.Revision());
        history.Record(document.Capture().ToJson(), "wire"); history.MoveTo(0);
        document.Load(RuleProject.FromJson(history.Current!.State));
        Assert.Same(editor, document.RuleBook.Influences);
        Assert.Equal(revision, document.Revision());
        long version = document.Version;
        Assert.Throws<InvalidOperationException>(() => editor.ConnectSource(rule, "condition", 0, -1, "Stale"));
        Assert.Equal(version, document.Version);
        history.MoveTo(1); document.Load(RuleProject.FromJson(history.Current!.State));
        var restored = document.Rules![0]; var restoredTerm = Assert.Single(restored.Effects[0].Influence.Terms);
        Assert.Equal("Recovery", restoredTerm.Name);
        editor.EditTerm(restored, restored.Effects[0], restoredTerm, t => t.Amount = .4);
        Assert.Equal(.4, restoredTerm.Amount); Assert.Equal(.1, effect.Influence.Terms[0].Amount);
    }

    [Fact]
    public void AuthoredWiringCurvesAndConditionsSurviveSerializationAndKeepRuntimeResults()
    {
        using var world = new GameWorld(TerrainSettings.Default.WithNodeSize(17, 42), enableRendering: false);
        for (int i = 0; i < 255 && world.RoadCount == 0; i++) world.Map.BuildRoadTilePath(i, i + 1, RoadPaving.Asphalt);
        int road = world.Map.RoadTiles.First(); world.Map.SetRoadCondition(road, .5f);
        var (document, rule, effect) = Setup(); var editor = document.RuleBook.Influences;
        rule.Enabled = true; editor.ConnectSource(rule, "condition", 0, -1, "Recovery");
        var term = effect.Influence.Terms[0];
        editor.EditTerm(rule, effect, term, t => { t.Amount = .4; t.Decrease = false; });
        editor.EditFactor(rule, effect, term, term.Factors[0], f => { f.OutputLow = 1; f.OutputHigh = 0; });
        var condition = editor.AddCondition(rule, effect, term, "condition")!;
        editor.EditCondition(rule, effect, term, condition, c => { c.Comparison = "less"; c.Value = .6; });
        var project = world.RuleProjectDocument; project.Algorithms = null;
        project.Rules = RuleProject.FromJson(document.Capture().ToJson()).Rules;
        world.QueueRuleProject(project); world.ExecutePendingCommands();
        var row = Assert.Single(world.RuleBook.PreviewInfluences(world, rule.Id, road, .5));
        Assert.Equal(.1, Assert.Single(row.Contributions).Change, 7);
        world.RuleBook.Run(world, .5, 0); Assert.Null(world.RuleBook.LastError);
        Assert.Equal(.6, world.Map.GetRoadCondition(road), 6);
    }

    private static (EditorDocument Document, WorldRule Rule, RuleEffect Effect) Setup(string mode = "add")
    {
        var document = new EditorDocument { Rules = new(), Fields = new(), RemovedStarters = new() };
        var rule = document.RuleBook.CreateForTarget("road", "condition", mode, "Road");
        var effect = rule.Effects[0]; effect.Influence ??= new();
        return (document, rule, effect);
    }
}
