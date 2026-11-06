using System.Text.Json;
using ForesTycoon.Editor;

namespace ForesTycoon.Tests;

public sealed class RuleBookEditorTests
{
    [Fact]
    public void EmptyRulesStartDisabledWithUniqueNamesAndAppendWithoutReorderingExistingRules()
    {
        var document = Document();
        var first = document.RuleBook.CreateEmpty(); first.Name = "Szabály 2";
        var second = document.RuleBook.CreateEmpty();
        Assert.Equal("Szabály 3", second.Name); Assert.NotEqual(first.Id, second.Id);
        Assert.Same(first, document.Rules![0]); Assert.Same(second, document.Rules[1]);
        Assert.False(first.Enabled); Assert.False(second.Enabled);
        Assert.NotNull(Assert.Single(second.Effects).Influence);
        _ = Compile(document);
    }

    [Fact]
    public void RulesCreatedFromAnObjectKeepTheirTargetModeAndUniqueDisplayName()
    {
        var document = Document();
        var scope = RuleEngine.Scopes.First(s => s.Actions.Length > 0);
        string action = scope.Actions[0].Name;
        var first = document.RuleBook.CreateForTarget(scope.Id, action, "action", "Action");
        var second = document.RuleBook.CreateForTarget(scope.Id, action, "action", "Action");
        Assert.Equal("Action 2", second.Name); Assert.False(second.Enabled);
        Assert.Equal(action, first.Effects[0].Target); Assert.Null(first.Effects[0].Influence);
        _ = Compile(document);
    }

    [Fact]
    public void NewPassageRulesUseEventSchedulingAndDoNotMultiplyTheirEffectsByTime()
    {
        var document = Document(); var scope = RuleEngine.Scope("passage");
        string target = scope.Fields.First(f => f.Write != null).Name;
        var rule = document.RuleBook.CreateForTarget(scope.Id, target, "add", "Passage");
        Assert.Equal("passage", rule.Schedule);
        Assert.False(rule.Effects[0].Influence.PerYear); Assert.False(rule.Effects[0].Influence.PerHour);
        _ = Compile(document);
    }

    [Fact]
    public void CopyKeepsAuthoredDataButHasIndependentNestedObjectsAndStartsDisabled()
    {
        var document = Document(); var source = document.RuleBook.CreateEmpty();
        source.Group = "Group"; source.Template = "starter"; source.ConditionGraph = Constant(1);
        source.Effects[0].Influence.Terms.Add(new() { Name = "Original" });
        var copy = document.RuleBook.Copy(source);
        Assert.NotEqual(source.Id, copy.Id); Assert.False(copy.Enabled); Assert.Equal(source.Group, copy.Group);
        Assert.Equal(source.Template, copy.Template); Assert.Equal(source.Scope, copy.Scope);
        copy.ConditionGraph.Nodes[0].Value = 9; copy.Effects[0].Influence.Terms[0].Name = "Changed";
        Assert.Equal(1, source.ConditionGraph.Nodes[0].Value); Assert.Equal("Original", source.Effects[0].Influence.Terms[0].Name);
        Assert.Same(source, document.Rules![0]); Assert.Same(copy, document.Rules[1]);
    }

    [Fact]
    public void AddingAStarterDoesNotMutateTheSourceAndReusesNeitherIdsNorNestedObjects()
    {
        var document = Document(); var starter = Starter(); string original = JsonSerializer.Serialize(starter);
        var first = document.RuleBook.AddStarter(starter); var second = document.RuleBook.AddStarter(starter);
        Assert.Equal(starter.Id, first.Id); Assert.NotEqual(first.Id, second.Id);
        second.Effects.Clear(); Assert.NotEmpty(first.Effects);
        Assert.Equal(original, JsonSerializer.Serialize(starter));
        Assert.Equal(new[] { first.Id, second.Id }, document.Rules!.Select(r => r.Id));
    }

    [Fact]
    public void AStarterIsRememberedAsDeletedOnlyWhenItsLastAuthoredCopyIsRemoved()
    {
        var document = Document(); var starter = RuleEngine.StarterRules().Single(r => r.Id == "traffic.wear");
        var first = document.RuleBook.AddStarter(starter); var copy = document.RuleBook.Copy(first);
        document.RuleBook.Delete(first); Assert.Empty(document.RemovedStarters!);
        document.RuleBook.Delete(copy); Assert.Equal(new[] { starter.Template }, document.RemovedStarters!);
        var legacy = document.Capture(); legacy.Version = 1;
        Assert.DoesNotContain(EditorProjectLoader.Prepare(legacy).Rules, r => r.Id == starter.Id || r.Template == starter.Id);
        document.RuleBook.AddStarter(starter); Assert.Empty(document.RemovedStarters!);
    }

    [Fact]
    public void VisualResetPreservesIdentityGroupingAndRunOrder()
    {
        var document = Document(); var starter = Starter();
        document.RuleBook.CreateEmpty(); var rule = document.RuleBook.AddStarter(starter);
        string id = rule.Id; rule.Name = "Custom title"; rule.Group = "Custom group"; rule.Enabled = false;
        rule.Condition = "0"; rule.Effects.Clear();
        Assert.True(document.RuleBook.RestoreVisualRule(rule));
        Assert.Equal(id, rule.Id); Assert.Equal("Custom title", rule.Name); Assert.Equal("Custom group", rule.Group);
        Assert.Equal(starter.Condition, rule.Condition); Assert.Null(rule.ConditionGraph); Assert.True(rule.Enabled); Assert.Same(rule, document.Rules![1]);
        Assert.Equal(JsonSerializer.Serialize(starter.Effects), JsonSerializer.Serialize(rule.Effects));
    }

    [Theory]
    [InlineData("step", "tree", "step")]
    [InlineData("growth", "tree", "growth")]
    [InlineData("growth", "tile", "step")]
    [InlineData("passage", "tile", "step")]
    [InlineData("month", "tile", "month")]
    [InlineData("wildlife", "tree", "wildlife")]
    [InlineData("population", "tile", "population")]
    [InlineData("birth", "tree", "birth")]
    public void ScopeChangesPreserveApplicableSchedulesAndAllAuthoredEffects(string previous, string scope, string expected)
    {
        var document = Document(); var rule = document.RuleBook.CreateEmpty(); rule.Schedule = previous;
        var effect = rule.Effects[0]; effect.Target = "deliberately-unfinished";
        document.RuleBook.SetScope(rule, scope);
        Assert.Equal(expected, rule.Schedule); Assert.Equal(scope, rule.Scope);
        Assert.Same(effect, rule.Effects[0]); Assert.Equal("deliberately-unfinished", effect.Target);
    }

    [Fact]
    public void PassageScopeClearsBothTimeMultipliersAndScheduleChoicesFollowTheScope()
    {
        var document = Document(); var rule = document.RuleBook.CreateEmpty();
        rule.Effects[0].Influence.PerYear = rule.Effects[0].Influence.PerHour = true;
        document.RuleBook.SetScope(rule, "passage");
        Assert.Equal(new[] { "passage" }, document.RuleBook.SchedulesFor(rule));
        Assert.False(rule.Effects[0].Influence.PerYear); Assert.False(rule.Effects[0].Influence.PerHour);
        document.RuleBook.SetScope(rule, "tree"); Assert.Contains("growth", document.RuleBook.SchedulesFor(rule));
        document.RuleBook.SetSchedule(rule, "growth"); Assert.Equal("growth", rule.Schedule);
        document.RuleBook.SetScope(rule, "tile"); Assert.DoesNotContain("growth", document.RuleBook.SchedulesFor(rule));
    }

    [Fact]
    public void InvalidScopeScheduleAndStaleRulesDoNotChangeTheDocumentOrItsVersion()
    {
        var document = Document(); var stale = document.RuleBook.CreateEmpty();
        document.Load(document.Capture()); var rule = document.Rules![0];
        string before = document.Capture().ToJson(); long version = document.Version;
        Assert.Throws<ArgumentException>(() => document.RuleBook.SetScope(rule, "missing"));
        Assert.Throws<ArgumentException>(() => document.RuleBook.SetSchedule(rule, "missing"));
        Assert.Throws<InvalidOperationException>(() => document.RuleBook.Delete(stale));
        Assert.Throws<InvalidOperationException>(() => document.RuleBook.Copy(stale));
        Assert.Throws<InvalidOperationException>(() => document.RuleBook.Edit(stale, r => r.Name = "Wrong"));
        Assert.Equal(version, document.Version); Assert.Equal(before, document.Capture().ToJson());
    }

    [Fact]
    public void GroupsChangeOnlyTheSelectedMembersWithoutChangingExecutionOrder()
    {
        var document = Document(); var first = document.RuleBook.CreateEmpty(); var second = document.RuleBook.CreateEmpty();
        document.RuleBook.MoveToGroup(first, "Group"); document.RuleBook.MoveToGroup(second, "Group");
        document.RuleBook.ClearGroups(new[] { first });
        Assert.Equal("", first.Group); Assert.Equal("Group", second.Group);
        Assert.Same(first, document.Rules![0]); Assert.Same(second, document.Rules[1]);
        string before = document.Capture().ToJson(); long version = document.Version;
        Assert.Throws<InvalidOperationException>(() => document.RuleBook.ClearGroups(new[] { second, new WorldRule() }));
        Assert.Equal(before, document.Capture().ToJson()); Assert.Equal(version, document.Version);
    }

    [Fact]
    public void OwnFieldsAreValidatedTrimmedAndUniqueAcrossScopesBeforeTheyAreAdded()
    {
        var document = Document(); var field = document.RuleBook.AddField("tile", "  saját.counter  ", 2, "m");
        Assert.Equal("saját.counter", field.Name); Assert.Equal("tile", field.Scope); Assert.Equal(2, field.Initial);
        long version = document.Version;
        Assert.Throws<InvalidOperationException>(() => document.RuleBook.AddField("tree", field.Name, 0, ""));
        Assert.Throws<InvalidDataException>(() => document.RuleBook.AddField("tile", "invalid name", 0, ""));
        Assert.Throws<InvalidDataException>(() => document.RuleBook.AddField("tile", "saját.bad", double.NaN, ""));
        Assert.Throws<ArgumentException>(() => document.RuleBook.AddField("missing", "saját.bad", 0, ""));
        Assert.Single(document.Fields!); Assert.Equal(version, document.Version);
    }

    [Fact]
    public void FieldDeletionKeepsReferencesVisibleAndTheCompilerRejectsThemUntilRepaired()
    {
        var document = Document(); var field = document.RuleBook.AddField("tile", "saját.counter", 0, "");
        var rule = document.RuleBook.CreateForTarget("tile", field.Name, "set", "Custom field");
        _ = Compile(document); document.RuleBook.DeleteField(field);
        Assert.Equal(field.Name, rule.Effects[0].Target);
        Assert.Throws<InvalidDataException>(() => Compile(document));
        Assert.Throws<InvalidOperationException>(() => document.RuleBook.DeleteField(field));
    }

    [Fact]
    public void VisualTargetsAreReusedAndActionAndPassageEffectsKeepTheirOriginalUnits()
    {
        var document = Document(); var rule = document.RuleBook.CreateEmpty(); rule.Effects.Clear();
        var scope = RuleEngine.Scopes.First(s => s.Actions.Length > 0); document.RuleBook.SetScope(rule, scope.Id);
        var action = document.RuleBook.EnsureVisualTarget(rule, scope.Actions[0].Name);
        Assert.Equal("action", action.Mode); Assert.False(action.Influence.PerYear);
        long version = document.Version;
        Assert.Same(action, document.RuleBook.EnsureVisualTarget(rule, action.Target)); Assert.Equal(version, document.Version);
        document.RuleBook.SetScope(rule, "passage");
        var effect = document.RuleBook.EnsureVisualTarget(rule, "unfinished-target");
        Assert.Equal("add", effect.Mode); Assert.False(effect.Influence.PerYear);
    }

    [Fact]
    public void CalculationGraphsUseOneRepresentationAndDoNotShareNodesWithTheEquationEditor()
    {
        var document = Document(); var rule = document.RuleBook.CreateEmpty();
        var effect = document.RuleBook.AddCalculation(rule); Assert.Equal("0", effect.Formula);
        var graph = Constant(2); document.RuleBook.SetCalculation(rule, effect, graph);
        Assert.Equal("", effect.Formula); Assert.Null(effect.Influence); Assert.NotNull(effect.Computation);
        graph.Nodes[0].Value = 9; Assert.Equal(2, effect.Computation.Nodes[0].Value);
        rule.Condition = "0"; document.RuleBook.SetCalculation(rule, null, graph);
        Assert.Equal("", rule.Condition); Assert.Equal(9, rule.ConditionGraph.Nodes[0].Value);
        document.RuleBook.ClearCondition(rule); Assert.Null(rule.ConditionGraph); Assert.Equal("", rule.Condition);
        document.RuleBook.DeleteEffect(rule, effect); Assert.DoesNotContain(effect, rule.Effects);
    }

    [Fact]
    public void ForeignEffectsAndDeletedEffectsCannotChangeTheCurrentRule()
    {
        var document = Document(); var first = document.RuleBook.CreateEmpty(); var second = document.RuleBook.CreateEmpty();
        var effect = document.RuleBook.AddCalculation(first); document.RuleBook.DeleteEffect(first, effect);
        string before = document.Capture().ToJson(); long version = document.Version;
        Assert.Throws<InvalidOperationException>(() => document.RuleBook.DeleteEffect(first, effect));
        Assert.Throws<InvalidOperationException>(() => document.RuleBook.SetCalculation(first, effect, Constant(1)));
        Assert.Throws<InvalidOperationException>(() => document.RuleBook.DeleteEffect(first, second.Effects[0]));
        Assert.Equal(version, document.Version); Assert.Equal(before, document.Capture().ToJson());
    }

    [Fact]
    public void RuleBookEditsInvalidateTheRevisionAndRestoredSnapshotsUseTheSameEditorInstance()
    {
        var document = Document(); var editor = document.RuleBook; var rule = editor.CreateEmpty();
        var history = new EditorHistory<string>();
        history.Record(document.Capture().ToJson(), "open");
        string original = document.Revision();
        editor.Edit(rule, r => r.Name = "Changed");
        Assert.NotEqual(original, document.Revision());
        history.Record(document.Capture().ToJson(), "edit"); history.MoveTo(0);
        document.Load(RuleProject.FromJson(history.Current!.State));
        Assert.Same(editor, document.RuleBook);
        Assert.Equal(original, document.Revision());
        Assert.Throws<InvalidOperationException>(() => editor.Delete(rule));
        editor.Edit(document.Rules![0], r => r.Name = "Restored edit"); Assert.Equal("Restored edit", document.Rules[0].Name);
    }

    private static EditorDocument Document() => new() { Rules = new(), Fields = new(), RemovedStarters = new() };
    private static WorldRule Starter() => RuleEngine.StarterRules().First(r => r.Template.Length > 0);
    private static RuleEngine Compile(EditorDocument document) => new(document.Rules!, document.Fields!, Array.Empty<string>(), _ => 0);
    private static BehaviorGraph Constant(double value) => new() { Id = "formula", Hook = "formula", Kind = "world", Output = "value",
        Nodes = new() { new() { Id = "value", Name = "Value", Operation = BehaviorOperation.Constant, Value = value } } };
}
