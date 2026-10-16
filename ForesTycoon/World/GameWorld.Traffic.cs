using System;
using System.Collections.Generic;

namespace ForesTycoon;

internal enum TrafficVehicleKind { Truck, Processor, Forwarder }
internal sealed record TrafficPassage(int TileId, TrafficVehicleKind Kind, int VehicleId, double Mass, double Fill)
{
    internal void Validate()
    {
        if (TileId < 0 || !Enum.IsDefined(Kind) || VehicleId < 0 || !double.IsFinite(Mass) || Mass < 0 || Mass > 1000000
            || !double.IsFinite(Fill) || Fill < 0 || Fill > 1) throw new ArgumentException("Invalid traffic passage.");
    }
}

sealed partial class GameWorld
{
    private Queue<TrafficPassage> trafficPassages = new();
    private TrafficPassage activePassage;
    internal void QueuePassage(TrafficPassage passage) => Enqueue(new TrafficPassageCommand(passage));
    void IWorldCommandTarget.ExecuteTrafficPassage(string json) { RecordPassage(System.Text.Json.JsonSerializer.Deserialize<TrafficPassage>(json)); SimulationWork.Complete(TrafficWork()); }
    internal int PendingTrafficPassages => trafficPassages.Count;
    internal string LastRuleSample { get; private set; } = "Még nem történt áthaladás.";
    internal IReadOnlyList<RuleEngine.InfluencePreview> PreviewPassage(string ruleId, TrafficPassage passage)
    {
        if (BoundaryPending) throw new InvalidOperationException("Wait for the current world operation before previewing a passage.");
        ArgumentNullException.ThrowIfNull(passage); passage.Validate();
        if (!map.IsValidTileId(passage.TileId)) throw new ArgumentException("Invalid passage tile.");
        activePassage = passage;
        try { return ruleEngine.PreviewInfluences(this, ruleId, passage.TileId, 0); }
        finally { activePassage = null; }
    }
    internal void RecordPassage(TrafficPassage passage)
    {
        ArgumentNullException.ThrowIfNull(passage);
        passage.Validate();
        if (!map.IsValidTileId(passage.TileId)) throw new ArgumentException("Invalid passage tile.", nameof(passage));
        if (map.IsRoadTile(passage.TileId) || map.IsSkidTrail(passage.TileId)) trafficPassages.Enqueue(passage);
    }

    /// <summary>Events stay in occurrence order. No snapshot or project replacement may overtake this work.</summary>
    internal IEnumerable<int> TrafficWork()
    {
        while (trafficPassages.TryPeek(out var passage)) {
            activePassage = passage;
            double before = map.IsRoadTile(passage.TileId) ? map.GetRoadCondition(passage.TileId) : map.GetSkidTrailWear(passage.TileId);
            ruleEngine.Begin(this, 0, forest.ForestYear, "passage", passage.TileId);
            foreach (int unit in ruleEngine.ContinueWork(this)) yield return unit;
            double after = map.IsRoadTile(passage.TileId) ? map.GetRoadCondition(passage.TileId) : map.GetSkidTrailWear(passage.TileId);
            LastRuleSample = $"Áthaladás: {passage.Kind} #{passage.VehicleId}, csempe {passage.TileId}; állapot {before:F4} → {after:F4}.";
            trafficPassages.Dequeue(); activePassage = null;
            yield return 1;
        }
    }
}
