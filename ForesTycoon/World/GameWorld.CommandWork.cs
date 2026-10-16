using System;
using System.Collections.Generic;
using OpenTK.Mathematics;

namespace ForesTycoon;

sealed partial class GameWorld
{
    internal bool CommandPending => commands.WorkPending;
    internal bool ForegroundPending => generationWork != null || commands.Count > 0 && !UpdatePending && ruleEngine?.StepPending != true;
    internal bool BoundaryPending => UpdatePending || CommandPending || generationWork != null;

    // Synchronous adapter for replay, CLI fixtures and startup. Interactive hosts use ContinueCommands.
    private IEnumerable<int> ExecuteCommandWork(IWorldCommand command)
    {
        var record = command.ToRecord(worldTick);
        switch (record.Kind)
        {
            case WorldCommandKind.EditElevation:
                {
                    var changed = new WorkResult<int[]>();
                    foreach (int unit in map.EditElevationAtNodeWork(record.A, record.B, record.C, record.D, changed)) yield return unit;
                    if (record.Flag) { foreach (int unit in forest.RefreshHabitatWork()) yield return unit; }
                    else { foreach (int unit in forest.ClearTerrainTilesWork(changed.Value)) yield return unit; }
                    if (changed.Value.Length > 0 && map.TryGetNodePosition(record.A, out var position)) effects.Spawn(WorldEffectKind.TerrainChanged, position);
                    break;
                }
            case WorldCommandKind.RoadPath:
                {
                    foreach (int unit in RoadCommandWork(record.A, record.B, record.Flag, (RoadPaving)record.C)) yield return unit;
                    break;
                }
            case WorldCommandKind.RoadRepair:
                {
                    if (!Fundable()) break;
                    var repaired = new WorkResult<(int Tile, float Damage)[]>();
                    foreach (int unit in map.RepairRoadPathWork(record.A, record.B, repaired)) yield return unit;
                    foreach (var (tile, damage) in repaired.Value) { Expenses += RoadCosts.Repair(map.GetRoadPaving(tile), damage, Tuning); yield return 1; }
                    if (map.TryGetRoadTileCenter(record.B, out var position)) effects.Spawn(WorldEffectKind.RoadChanged, position);
                    break;
                }
            case WorldCommandKind.SkidTrailPath:
                {
                    if (!record.Flag && !Fundable()) break;
                    var changed = new WorkResult<int[]>();
                    var work = record.Flag ? map.RemoveSkidTrailPathWork(record.A, record.B, changed) : map.MarkSkidTrailPathWork(record.A, record.B, changed);
                    foreach (int unit in work) yield return unit;
                    if (changed.Value.Length == 0) break;
                    if (!record.Flag) Expenses += changed.Value.Length * Tuning[Tune.TrailCost];
                    Logistics?.TrailsChanged();
                    foreach (int unit in vehicles.RemoveInvalidRoutesWork(map.IsNetworkTile, map.AreNetworkNeighbours)) yield return unit;
                    if (map.TryGetTileCenter(record.B, out var position)) effects.Spawn(WorldEffectKind.RoadChanged, position);
                    break;
                }
            case WorldCommandKind.PlantForest:
                {
                    var result = new WorkResult<ForestryActionResult>();
                    foreach (int unit in forest.PlantWork(record.A, (ForestSpecies)record.B, result)) yield return unit;
                    lastForestryAction = result.Value;
                    if (map.TryGetTileCenter(record.A, out var position)) effects.Spawn(result.Value == ForestryActionResult.Planted ? WorldEffectKind.TreePlanted : WorldEffectKind.ForestryRejected, position);
                    break;
                }
            case WorldCommandKind.PlantForestArea:
                {
                    int[] ids = AreaTiles(record.A, record.B); int planted = 0, area = forest.AllocatePlantationId();
                    foreach (int id in ids)
                    {
                        lastForestryAction = forest.PlantInArea(id, (ForestSpecies)record.C, area);
                        if (lastForestryAction == ForestryActionResult.Planted) planted++;
                        yield return 1;
                    }
                    lastForestryArea = new(ids.Length, planted, 0f);
                    if (planted > 0) foreach (int unit in forest.FinishPlantingAreaWork(area)) yield return unit;
                    SpawnAreaEffect(record.A, record.B, planted > 0 ? WorldEffectKind.TreePlanted : WorldEffectKind.ForestryRejected);
                    break;
                }
            case WorldCommandKind.HarvestForest:
            case WorldCommandKind.HarvestForestArea:
                {
                    int[] ids = AreaTiles(record.A, record.Kind == WorldCommandKind.HarvestForest ? record.A : record.B);
                    var applied = new WorkResult<int>();
                    foreach (int unit in Logistics.DesignateWork(ids, applied)) yield return unit;
                    var remaining = new WorkResult<float>();
                    foreach (int unit in Logistics.RemainingWork(remaining)) yield return unit;
                    lastForestryAction = applied.Value > 0 ? ForestryActionResult.Designated : ForestryActionResult.NoForest;
                    lastForestryArea = new(ids.Length, applied.Value, remaining.Value);
                    break;
                }
            case WorldCommandKind.SendVehicle:
                {
                    var source = Logistics.StackAt(record.B); var result = new WorkResult<bool>();
                    if (record.Flag)
                    {
                        var truck = Logistics.Trucks.Find(t => t.Id == record.A);
                        if (truck == null) break;
                        if (source == null) { Logistics.Status = "A rönkszállító forrása egy sarang legyen."; break; }
                        foreach (int unit in Logistics.AssignTruckWork(truck, source, record.C - 1, result)) yield return unit;
                    }
                    else
                    {
                        var machine = Logistics.Machines.Find(m => m.Id == record.A);
                        if (machine == null) break;
                        if (source == null) { Logistics.Status = machine.Kind == ForestMachineKind.Harvester ? "A processzort egy sarangra küldd: oda hordja a fát." : "A forwarder forrása egy sarang legyen."; break; }
                        var work = machine.Kind == ForestMachineKind.Harvester ? Logistics.AssignProcessorWork(machine, source, result)
                            : Logistics.AssignForwarderWork(machine, source, record.C - 1, result);
                        foreach (int unit in work) yield return unit;
                    }
                    break;
                }
            case WorldCommandKind.SetRuleProject:
                foreach (int unit in ApplyRuleProjectJsonWork(record.RuleJson)) yield return unit;
                break;
            default: command.Execute(this); yield return 1; break;
        }
    }

    private int[] AreaTiles(int start, int end)
    {
        var ids = new int[TerrainMap.MaximumAreaTiles];
        int count = map.GetTileRectangle(start, end, ids);
        Array.Resize(ref ids, count); return ids;
    }

    private IEnumerable<int> RoadCommandWork(int start, int end, bool remove, RoadPaving surface)
    {
        if (!remove && !Fundable()) yield break;
        var changed = new WorkResult<int[]>();
        foreach (int unit in map.RoadPathWork(start, end, remove, surface, changed)) yield return unit;
        if (changed.Value.Length == 0) yield break;
        if (!remove) Expenses += changed.Value.Length * RoadCosts.Build(surface, Tuning);
        var unavailable = new List<int>();
        foreach (int id in changed.Value) { if (!((IForestHabitat)map).CanSupportForest(id)) unavailable.Add(id); yield return 1; }
        foreach (int unit in forest.ClearTerrainTilesWork(unavailable.ToArray())) yield return unit;
        foreach (int unit in vehicles.RemoveInvalidRoutesWork(map.IsNetworkTile, map.AreNetworkNeighbours)) yield return unit;
        if (map.TryGetRoadTileCenter(end, out var position)) effects.Spawn(WorldEffectKind.RoadChanged, position);
    }

    internal int ContinueCommands(int budget = 16)
    {
        EnsureAvailable(allowFaulted: GenerationPending); terrainRenderer?.VerifyAccess();
        if (UpdatePending || ruleEngine?.StepPending == true) return 0;
        if (generationWork != null) { ContinueGeneration(budget); return 0; }
        int completed;
        try { backgroundJobs.PublishCompleted(); completed = commands.Continue(this, ExecuteCommandWork, budget); }
        catch { faulted = true; throw; }
        if (!BoundaryPending) PublishBoundaryActions();
        return completed;
    }

    private void PublishBoundaryActions()
    {
        int count = tickBoundaryActions.Count;
        for (int i = 0; i < count && !BoundaryPending; i++) tickBoundaryActions.Dequeue()();
    }
}
