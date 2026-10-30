using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace ForesTycoon;

partial class SimulationWorld
{
    private IEnumerator<int> ruleExportWork;
    private Action<Task<string>> ruleExportReady;
    internal bool RuleExportPending => ruleExportWork != null;
    internal int LastRuleExportWorkUnits { get; private set; }

    /// <summary>Owns a completed tick while gathering a detached editor snapshot in bounded slices.</summary>
    internal void RequestRuleWorldExport(string source, string error, Action<Task<string>> ready)
    {
        EnsureAvailable(); ArgumentNullException.ThrowIfNull(ready);
        if (BoundaryPending || ForegroundPending) throw new InvalidOperationException("An editor snapshot requires a completed world boundary.");
        ruleExportReady = ready;
        ruleExportWork = CaptureRuleWorldWork(source, error, (catalog, revision) =>
            ready(Task.Run(() => { catalog.ProjectRevision = revision(); return catalog.ToJson(); }))).GetEnumerator();
    }

    private void ContinueRuleExport(int budget)
    {
        if (budget <= 0) throw new ArgumentOutOfRangeException(nameof(budget));
        LastRuleExportWorkUnits = 0;
        try {
            while (LastRuleExportWorkUnits < budget) {
                if (!SimulationWork.MoveNext(ruleExportWork, budget - LastRuleExportWorkUnits)) {
                    CancelRuleExport(); break;
                }
                LastRuleExportWorkUnits += Math.Max(1, ruleExportWork.Current);
            }
        }
        catch (Exception error) {
            var failed = ruleExportReady; CancelRuleExport();
            failed(Task.FromException<string>(error));
        }
        if (!RuleExportPending) PublishBoundaryActions();
    }

    private void CancelRuleExport() { ruleExportWork?.Dispose(); ruleExportWork = null; ruleExportReady = null; }

    private IEnumerable<int> CaptureRuleWorldWork(string source, string error, Action<RuleWorldCatalog, Func<string>> ready)
    {
        var catalog = new RuleWorldCatalog {
            Source = source, Tick = SimulationTick, Seed = map.Settings.Seed,
            AlgorithmRevision = algorithmGraphs?.Revision ?? "", ObjectModel = RuleModel,
            PublishedUtc = DateTime.UtcNow.ToString("O"), ProjectName = ruleProjectName,
            Error = error ?? DynamicsError ?? BehaviorError, Hooks = WorldBehaviorPolicy.Hooks.ToList()
        };
        foreach (string kind in RuleObjectKinds) {
            if (RuleObjects.Find(kind) is { Type.IsClass: true } entity && RuleEngine.Scope(kind) is { } scope) {
                foreach (int unit in scope.PrepareElements(this)) yield return unit;
                foreach (int id in scope.PreparedElements(this)) {
                    catalog.Objects.Add(new(kind, (ulong)id, scope.Label?.Invoke(this, id) ?? entity.Name + " #" + id));
                    yield return 1;
                }
            } else if (kind is "tile" or "road") {
                for (int id = 0; id < map.Tiles.Count; id++) {
                    if (kind == "tile" || map.IsRoadTile(id)) catalog.Objects.Add(new(kind, (ulong)id, $"{kind} #{id}"));
                    yield return 1;
                }
            } else {
                foreach (var target in BehaviorTargets(kind)) {
                    catalog.Objects.Add(new(kind, target.Id, target.Label)); yield return 1;
                }
            }
        }
        foreach (string key in RuleWorldFields) { catalog.Fields.Add(key, ReadWorldValue(key)); yield return 1; }
        catalog.Stocks = dynamics.Capture();
        // Published program documents are immutable during simulation. Capture their owners, not the live world;
        // cloning, canonical hashing and JSON serialization can then run after this boundary has been released.
        var policy = behaviorPolicy; var model = dynamics; var engine = ruleEngine;
        var scene = ruleProjectWorld; var tuning = Tuning.ToValues(); var algorithms = AlgorithmSnapshot();
        string name = ruleProjectName;
        string Revision() => new RuleProject {
            Name = name, Tuning = tuning, Behaviors = policy.Document, Dynamics = model.Document, Algorithms = algorithms,
            Rules = RuleEngine.Clone(engine.Rules), RemovedStarters = engine.RemovedStarters.ToList(),
            Fields = RuleEngine.CloneFields(engine.Fields), World = scene
        }.ContentRevision();
        ready(catalog, Revision);
    }

    /// <summary>Synchronous adapter for diagnostics; interactive publication uses RequestRuleWorldExport.</summary>
    internal RuleWorldCatalog ExportRuleWorld(string source, string error = null)
    {
        EnsureAvailable();
        if (BoundaryPending) throw new InvalidOperationException("An editor snapshot requires a completed world boundary.");
        RuleWorldCatalog result = null;
        SimulationWork.Complete(CaptureRuleWorldWork(source, error, (catalog, revision) => {
            catalog.ProjectRevision = revision(); result = catalog;
        }));
        return result;
    }
}
