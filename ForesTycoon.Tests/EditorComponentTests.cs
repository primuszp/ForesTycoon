using System.Reflection;
using ForesTycoon.Editor;
namespace ForesTycoon.Tests;

public sealed class EditorComponentTests
{
    [Fact]
    public void CatalogReloadInvalidatesSearchButDoesNotOverwriteDocumentTuning()
    {
        var tuning = new EditorTuning(); tuning.Set("DieselPrice", 1.2);
        using var session = new RuleCatalogSession(tuning, false);
        session.Load(CurrentGameRules.Build());
        var first = session.VisibleRules("", ""); Assert.Same(first, session.VisibleRules("", ""));
        Assert.Equal(1.2, session.Catalog.Rules.SelectMany(r => r.Parameters).First(p => p.Key == "DieselPrice").Value);
        session.Load(CurrentGameRules.Build()); Assert.NotSame(first, session.VisibleRules("", ""));
        Assert.Equal(1.2, tuning.Get("DieselPrice"));
        tuning.Set("DieselPrice", 1.3);
        Assert.Equal(1.3, session.Catalog.Rules.SelectMany(r => r.Parameters).First(p => p.Key == "DieselPrice").Value);
        var captured = session.CaptureTuning(); captured.Clear(); Assert.Equal(1.3, tuning.Get("DieselPrice"));
    }
    [Fact]
    public void ClosingCatalogDetachesItsTuningProjection()
    {
        var tuning = new EditorTuning(); using var session = new RuleCatalogSession(tuning, false);
        session.Load(CurrentGameRules.Build()); double before = session.Catalog.Rules.SelectMany(r => r.Parameters).First(p => p.Key == "DieselPrice").Value;
        session.Dispose(); tuning.Set("DieselPrice", before + .1);
        Assert.Equal(before, session.Catalog.Rules.SelectMany(r => r.Parameters).First(p => p.Key == "DieselPrice").Value);
    }
    [Fact]
    public void CatalogNavigationAndLayoutAreIndependentBetweenWindows()
    {
        using var first = new RuleCatalogSession(new(), true); using var second = new RuleCatalogSession(new(), true);
        first.Load(CurrentGameRules.Build()); second.Load(CurrentGameRules.Build());
        var a = new RuleCatalogLayout(); a.Load(first.Index); var b = new RuleCatalogLayout(); b.Load(second.Index);
        var sa = new CurrentRulesUiState(); var sb = new CurrentRulesUiState();
        var na = new RuleCatalogNavigation(first, a, sa); var nb = new RuleCatalogNavigation(second, b, sb);
        var rule = first.Index.Rules[0]; sa.Search = "old query"; na.Navigate(rule.Id);
        Assert.Equal(rule.Id, sa.Selected); Assert.Equal(rule.Module, sa.Module); Assert.Empty(sa.Search);
        Assert.Empty(sb.Module); Assert.Empty(sb.Selected);
        a.Modules.Clear(); Assert.NotEmpty(b.Modules);
        na.Reset(); Assert.Empty(sa.Selected); Assert.True(sa.FitRequested);
    }
    [Fact]
    public void ComponentsDoNotHoldTheirParentViewsOrTheDocument()
    {
        foreach (var type in new[] { typeof(RuleCatalogMapView), typeof(RuleCatalogSidebarView), typeof(RuleCatalogInspectorView),
                     typeof(RuleTuningView), typeof(TreeAppearancePreview), typeof(TreePreviewCamera) })
            Assert.DoesNotContain(type.GetFields(BindingFlags.Instance | BindingFlags.NonPublic), field =>
                field.FieldType == typeof(CurrentRulesView) || field.FieldType == typeof(TreeShapeEditorView) || field.FieldType == typeof(EditorDocument));
        Assert.Equal(typeof(EditorDocument).Assembly, typeof(EditorPreviewBuild<>).Assembly);
        Assert.Equal(typeof(EditorDocument).Assembly, typeof(RuleCatalogSession).Assembly);
    }
    [Fact]
    public void PreviewCoalescesIntermediateRequestsAndPublishesOnlyOnTheHostThread()
    {
        var scheduled = new List<TaskCompletionSource<string>>();
        using var slot = new EditorPreviewBuild<string>(_ => { var source = new TaskCompletionSource<string>(); scheduled.Add(source); return source.Task; });
        slot.Update("first", () => "first"); slot.Update("obsolete", () => "obsolete");
        Assert.Single(scheduled); Assert.Null(slot.Value);
        scheduled[0].SetResult("built first"); Assert.Null(slot.Value);
        slot.Update("newest", () => "newest"); Assert.Equal("built first", slot.Value); Assert.Equal(2, scheduled.Count);
        Assert.False(slot.Current("first")); scheduled[1].SetResult("built newest"); slot.Update("newest", () => throw new Exception());
        Assert.Equal("built newest", slot.Value); Assert.True(slot.Current("newest")); Assert.Equal(2, scheduled.Count);
    }
    [Fact]
    public void PreviewFailureKeepsTheLastValueAndRecoversForANewRequest()
    {
        var completion = new TaskCompletionSource<string>();
        using var slot = new EditorPreviewBuild<string>(_ => completion.Task);
        slot.Update("good", () => "unused"); completion.SetResult("good"); slot.Update("good", () => "unused");
        completion = new(); slot.Update("bad", () => "unused"); completion.SetException(new InvalidOperationException("bad form"));
        slot.Update("bad", () => "unused"); Assert.Equal("good", slot.Value); Assert.Equal("bad form", slot.Error); Assert.False(slot.Current("good")); Assert.False(slot.Current("bad"));
        completion = new(); slot.Update("fixed", () => "unused"); completion.SetResult("fixed"); slot.Update("fixed", () => "unused");
        Assert.Null(slot.Error); Assert.True(slot.Current("fixed"));
    }
    [Fact]
    public void DisposedPreviewDoesNotPublishOrBlockOnAnOutstandingBuild()
    {
        var completion = new TaskCompletionSource<string>(); var slot = new EditorPreviewBuild<string>(_ => completion.Task);
        slot.Update("pending", () => "unused"); slot.Dispose(); Assert.False(completion.Task.IsCompleted);
        completion.SetException(new InvalidOperationException("late failure"));
        Assert.Null(slot.Value); Assert.False(slot.Busy); slot.Dispose();
        Assert.Throws<ObjectDisposedException>(() => slot.Update("later", () => "unused"));
    }
}
