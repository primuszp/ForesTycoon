namespace ForesTycoon.Tests;

public sealed class EditorProjectionTests
{
    [Fact]
    public void TextQueriesMatchIndependentCapturesAndFollowEditsWithoutLeakingDtoReferences()
    {
        var document = new EditorDocument(); var rule = document.RuleBook.AddStarter(RuleEngine.StarterRules().Single(r => r.Id == "traffic.wear"));
        var book = new RuleBookSession(document); var view = book.Entries[0]; var effect = view.Effects.First(e => e.Influence != null);
        var term = book.Influences.AddTerm(view, effect)!; var factor = book.Influences.AddFactor(view, effect, term, "tuning.DieselPrice")!;
        book.Influences.EditTerm(view, effect, term, value => value.AmountField = "tuning.DieselPrice");
        double current = 1.2;
        var sentence = new RuleSentence("út", field => new(field, Current: field.StartsWith("tuning.") ? current : null));
        long version = document.Version;
        Assert.Equal(sentence.Rule(view.Capture()), book.Describe(view, sentence));
        Assert.Equal(sentence.Term(effect.Capture(), term.Capture()), book.DescribeTerm(effect, term, sentence));
        Assert.Equal(sentence.Factor(factor.Capture()), book.DescribeFactor(factor, sentence));
        Assert.Equal(version, document.Version);
        string before = book.DescribeTerm(effect, term, sentence); current = 2.4;
        Assert.NotEqual(before, book.DescribeTerm(effect, term, sentence));
        book.Influences.EditTerm(view, effect, term, value => value.Amount = .75);
        Assert.Equal(sentence.Rule(view.Capture()), book.Describe(view, sentence));
        Assert.True(((ICollection<string>)book.Describe(view, sentence)).IsReadOnly);
    }
    [Fact]
    public void BorrowedHashMatchesFullCaptureAcrossAllDocumentSectionsAndUndo()
    {
        var project = SimulationWorld.DefaultRuleProject(); project.World = SimulationWorld.DefaultScenario(42, 16);
        project.World.SoilPaint[4] = project.World.SoilTypes[0].Id;
        project.World.SoilPaint[1] = project.World.SoilTypes[1].Id;
        var document = new EditorDocument(); document.Load(project);
        string initial = document.Revision(); Assert.Equal(EditorDocument.RevisionOf(document.Capture()), initial);
        document.EditAuthoring(authoring => authoring.Entries["test"] = new() { Notes = "test" });
        Assert.NotEqual(initial, document.Revision()); Assert.Equal(EditorDocument.RevisionOf(document.Capture()), document.Revision());
        document.Scene.SetTuning("DieselPrice", 1.2);
        Assert.Equal(EditorDocument.RevisionOf(document.Capture()), document.Revision());
        var rule = document.RuleBook.AddStarter(RuleEngine.StarterRules().Single(r => r.Id == "traffic.wear"));
        document.RuleBook.Edit(rule, value => value.Name = "Changed");
        Assert.Equal(EditorDocument.RevisionOf(document.Capture()), document.Revision());
        document.History.Undo(); Assert.Equal(EditorDocument.RevisionOf(document.Capture()), document.Revision());
        document.History.Redo(); Assert.Equal(EditorDocument.RevisionOf(document.Capture()), document.Revision());
        var captured = document.Capture(); captured.World!.SoilPaint.Clear(); captured.Rules.Clear();
        Assert.NotEmpty(document.World!.SoilPaint); Assert.NotEmpty(document.Rules);
    }
    [Fact]
    public void TransactionRollbackPreservesAnUnrecordedChangeAndItsRedoHistory()
    {
        int value = 1; var history = new EditorHistoryService<int>(() => value, saved => value = saved); history.Reset();
        value = 2;
        Assert.Throws<InvalidOperationException>(() => history.Edit("failed", () => { value = 9; throw new InvalidOperationException(); }));
        Assert.Equal(2, value); Assert.True(history.Undo()); Assert.Equal(1, value);
        Assert.True(history.Redo()); Assert.Equal(2, value);
        history.Edit("valid", () => value = 3); Assert.True(history.Undo()); Assert.Equal(2, value);
    }
}
