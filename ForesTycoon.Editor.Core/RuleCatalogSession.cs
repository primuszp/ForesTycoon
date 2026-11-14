using System;
using System.Collections.Generic;
using ForesTycoon.Rules;
namespace ForesTycoon.Editor.Core;

/// <summary>Catalog index, search cache and projection of document-owned tuning.</summary>
internal sealed class RuleCatalogSession : IDisposable
{
    private readonly EditorTuning tuning;
    private readonly bool ownsTuning;
    private GameRuleDefinition[] visibleRules;
    private string visibleModule, visibleSearch;
    internal GameRuleCatalog Catalog { get; private set; }
    internal GameRuleIndex Index { get; private set; }
    internal RuleCatalogSession(EditorTuning tuning, bool ownsTuning)
    {
        this.tuning = tuning; this.ownsTuning = ownsTuning;
        tuning.Changed += RefreshTuning;
    }
    internal void Load(GameRuleCatalog loaded, string file = null, GameRuleCatalog current = null)
    {
        loaded = CurrentGameRules.Refresh(loaded, current ?? (file != null ? CurrentGameRules.Build() : loaded));
        var nextIndex = new GameRuleIndex(loaded);
        Catalog = loaded; Index = nextIndex;
        if (ownsTuning) tuning.Replace(loaded.TuningOverrides());
        RefreshTuning(); visibleRules = null;
    }
    internal GameRuleDefinition[] VisibleRules(string module, string search)
    {
        if (visibleRules == null || visibleModule != module || visibleSearch != search)
        {
            visibleRules = Index.Search(module, search); visibleModule = module; visibleSearch = search;
        }
        return visibleRules;
    }
    internal Dictionary<string, double> CaptureTuning() => tuning.Capture();
    internal int UnappliedTuning(GameTuning live)
    {
        int count = 0;
        foreach (var spec in GameTuning.Specs) if (tuning.Get(spec.Id) != live[spec.Key]) count++;
        return count;
    }
    private void RefreshTuning()
    {
        foreach (var rule in Catalog?.Rules ?? new())
            for (int i = 0; i < rule.Parameters.Count; i++)
            {
                var parameter = rule.Parameters[i];
                if (parameter.Tunable) rule.Parameters[i] = parameter with { Value = tuning.Get(parameter.Key) };
            }
    }

    public void Dispose() => tuning.Changed -= RefreshTuning;
}
