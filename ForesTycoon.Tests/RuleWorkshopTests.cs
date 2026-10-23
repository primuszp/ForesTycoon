using ForesTycoon.Editor;
using ForesTycoon.RuleCompiler;

namespace ForesTycoon.Tests;

public sealed class RuleWorkshopTests
{
    [Fact]
    public void GameplayPolicyBindingsUseTheSharedCatalogOwnership()
    {
        var catalog = CurrentGameRules.Build();
        foreach (var entry in GameplayRules.CatalogHooks)
            foreach (string hook in entry.Value)
                Assert.True(RuleWorkshopModel.HookBelongsTo(GameplayRules.Hooks.Single(h => h.Id == hook), catalog.Rules.Single(r => r.Id == entry.Key)));
    }
    [Fact]
    public void EveryEditableEngineRuleOwnsAProjectGraphOrRule()
    {
        // A catalog rule marked editable must lead somewhere in the editor: a state rule, a bound graph, a controller or a number.
        var catalog = CurrentGameRules.Build();
        var project = GameWorld.DefaultRuleProject();
        foreach (var rule in catalog.Rules.Where(r => r.Execution is GameRuleExecution.EditableGraph or GameRuleExecution.EditableExpressions)) {
            bool owned = project.Rules.Any(r => r.Id == rule.Id || r.Template == rule.Id)
                || WorldBehaviorPolicy.Hooks.Any(h => RuleWorkshopModel.HookBelongsTo(h, rule) && project.Behaviors.Graphs.Any(g => g.Hook == h.Id && g.Kind == h.Kind))
                || rule.Parameters.Any(p => p.Tunable);
            Assert.True(owned, rule.Id + " is marked editable but nothing in the project edits it.");
        }
    }
    [Fact]
    public void EveryCurrentRuleHasExactlyOneThematicHome()
    {
        var catalog = CurrentGameRules.Build();
        Assert.All(catalog.Rules, rule => {
            Assert.Single(RuleWorkshopModel.Topics, t => t.Modules.Contains(rule.Module));
            Assert.Contains(rule.Module, RuleWorkshopModel.Topics[RuleWorkshopModel.Topic(rule)].Modules);
        });
        Assert.Equal(0, RuleWorkshopModel.Topic(catalog.Rules.Single(r => r.Id == "forest.growth")));
        Assert.Equal(1, RuleWorkshopModel.Topic(catalog.Rules.Single(r => r.Id == "water.stress")));
    }
    [Fact]
    public void SearchFindsTheSameRuleAcrossThematicBoundaries()
    {
        var index = new GameRuleIndex(CurrentGameRules.Build());
        Assert.DoesNotContain(RuleWorkshopModel.Search(index, 3, ""), r => r.Id == "forest.growth");
        var result = RuleWorkshopModel.Search(index, 3, "Fák növekedése");
        Assert.Contains(result, r => ReferenceEquals(r, index.Find("forest.growth")));
    }
    [Fact]
    public void FinishingAnOlderTrialDoesNotCertifyNewerEditsOrPublishThem()
    {
        var versions = new WorkshopVersions(); versions.Observe("original");
        versions.Observe("trial-snapshot"); versions.MarkSaved();
        versions.Observe("new-edit"); versions.CompleteTrial("trial-snapshot");
        Assert.True(versions.Unsaved); Assert.False(versions.TestedCurrent); Assert.Null(versions.Published);
        versions.MarkPublished("trial-snapshot"); versions.Acknowledge("another-runtime-project");
        Assert.NotEqual(versions.Current, versions.Published); Assert.NotEqual(versions.Published, versions.Running);
        versions.CompleteTrial("new-edit"); Assert.True(versions.TestedCurrent);
    }
    [Fact]
    public void ProjectIdentityIgnoresDictionaryOrderButDetectsRuleChanges()
    {
        var first = new RuleProject { Tuning = new() { ["a"] = 1, ["b"] = 2 } };
        var second = new RuleProject { Tuning = new() { ["b"] = 2, ["a"] = 1 } };
        Assert.Equal(first.ContentRevision(), second.ContentRevision());
        second.Rules.Add(TrafficWearRule.Default());
        Assert.NotEqual(first.ContentRevision(), second.ContentRevision());
    }
    [Fact]
    public void RepairableDraftSaveLeavesThePublishedProjectIntact()
    {
        string folder = Path.Combine(Path.GetTempPath(), "forest-workshop-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try {
            string published = Path.Combine(folder, "project.json");
            var active = GameWorld.DefaultRuleProject(); string original = active.ToJson(); File.WriteAllText(published, original);
            var incomplete = active.Clone(); incomplete.Rules.Add(new() { Id = "incomplete", Scope = "passage", Schedule = "passage", Effects = { new() { Target = "unknown", Influence = new() { PerYear = false } } } });
            string draft = WorkshopDraftStore.Save(published, incomplete);
            var saved = RuleProject.FromJson(File.ReadAllText(draft));
            Assert.Equal("unknown", saved.Rules.Single(r => r.Id == "incomplete").Effects.Single().Target);
            // Reopening a draft must preserve its publication target and permit another incomplete edit.
            incomplete.Name = "Második mentés";
            WorkshopDraftStore.Save(draft, incomplete);
            Assert.Equal(published, RuleWorkshopModel.PublishedPath(draft));
            Assert.Equal("Második mentés", RuleProject.FromJson(File.ReadAllText(draft)).Name);
            Assert.Equal(original, File.ReadAllText(published));
        } finally { Directory.Delete(folder, recursive: true); }
    }
}
