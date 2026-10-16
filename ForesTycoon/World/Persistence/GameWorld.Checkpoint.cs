using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace ForesTycoon
{
    sealed partial class GameWorld
    {
        internal WorldCheckpointData CaptureCheckpoint()
        {
            EnsureAvailable();
            if (BoundaryPending) throw new InvalidOperationException("A checkpoint requires a complete logical tick; queue it with AtTickBoundary.");
            return new(2, worldTick, commandJournal.Count - commands.Count,
                commands.Count, map.Capture(), ecosystem.Capture(), wildlife.Capture(), Logistics.Capture(), vehicles.Capture(),
                timberCargo.Available, timberCargo.Delivered, effects.Capture(), lastForestryAction, lastForestryArea, Expenses,
                Tuning.IsDefault ? null : Tuning.ToOverrides(), roadWeatherSeconds, BehaviorDocument, BehaviorStates,
                dynamics.Document, dynamics.Capture(), ruleProjectName, AlgorithmSnapshot(), ruleEngine.Capture(), ruleEngine.CaptureStep(), pendingForestRateRefresh);
        }

        private void RestoreCheckpoint(WorldCheckpointData s) => SimulationWork.Complete(RestoreCheckpointWork(s));

        private IEnumerable<int> RestoreCheckpointWork(WorldCheckpointData s)
        {
            var programs = Task.Run(() => PrepareCheckpointPrograms(s));
            CheckpointGuard.Require(Enum.IsDefined(s.LastAction) && s.LastArea.TileCount >= 0 && s.LastArea.Applied >= 0 &&
                s.LastArea.Applied <= s.LastArea.TileCount, "forestry UI state");
            CheckpointGuard.NonNegative(s.LastArea.TimberVolume, "forestry volume");
            foreach (int unit in map.RestoreWork(s.Terrain)) yield return unit;
            foreach (int unit in ecosystem.RestoreWork(s.Ecology)) yield return unit;
            foreach (int unit in Logistics.RestoreWork(s.Logistics)) yield return unit; timberCargo.Restore(s.AvailableTimber, s.DeliveredTimber);
            foreach (int unit in vehicles.RestoreWork(s.Vehicles, map.Tiles.Count)) yield return unit; Logistics.BindVehicles(vehicles);
            foreach (int unit in wildlife.RestoreWork(s.Wildlife, map.Tiles.Count)) yield return unit; effects.Restore(s.Effects);
            foreach (var v in vehicles.Vehicles)
            {
                yield return 1; if (v.LocalCargo) CheckpointGuard.Require(Logistics.Mills.Exists(m => m.TileId == v.SawmillTileId)
                || Logistics.StackAt(v.SawmillTileId) != null, "truck destination");
            }
            CheckpointGuard.NonNegative(s.Expenses, "expenses");
            CheckpointGuard.Require(double.IsFinite(s.RoadWeatherSeconds) && s.RoadWeatherSeconds >= 0 &&
                s.RoadWeatherSeconds < 0.5, "road weather remainder");
            roadWeatherSeconds = s.RoadWeatherSeconds;
            pendingForestRateRefresh = s.PendingForestRateRefresh;
            worldTick = s.Tick; lastForestryAction = s.LastAction; lastForestryArea = s.LastArea; Expenses = s.Expenses;
            while (!programs.IsCompleted) yield return 64;
            var compiled = programs.GetAwaiter().GetResult();
            dynamics = compiled.Dynamics; ruleEngine = compiled.Engine; behaviorPolicy = compiled.Policy;
            ruleProjectName = s.RuleProjectName ?? "ForesTycoon szabályrendszer";
            ruleEngine.RestoreStep(this, s.RuleStep);
            algorithmGraphs = s.Algorithms;
            foreach (var state in BehaviorStates)
            {
                yield return 1; CheckpointGuard.Require(state.Kind == "truck" ? Logistics.Trucks.Exists(t => (ulong)t.Id == state.ObjectId)
                : Logistics.Machines.Exists(m => (ulong)m.Id == state.ObjectId &&
                (m.Kind == ForestMachineKind.Harvester ? "processor" : "forwarder") == state.Kind), "controller target");
            }
            BindBehaviors();
            ApplyTuning(GameTuning.FromOverrides(s.Tuning));
        }

        private (DynamicsRuntime Dynamics, RuleEngine Engine, WorldBehaviorPolicy Policy) PrepareCheckpointPrograms(WorldCheckpointData s)
        {
            var runtime = new DynamicsRuntime(s.Dynamics ?? new DynamicsModel(), RuleWorldFields, ruleValues.ReadWorld);
            if (s.DynamicsValues != null) runtime.Restore(s.DynamicsValues);
            var rules = s.RuleBook?.Rules ?? RuleEngine.StarterRules();
            var removed = s.RuleBook?.RemovedStarters ?? new(); RuleEngine.AddMissingStarters(rules, removed);
            var engine = new RuleEngine(rules, s.RuleBook?.Fields ?? new(), runtime.Document.Stocks.Select(x => x.Id), ruleValues.ReadRule) { RemovedStarters = removed };
            engine.Restore(s.RuleBook?.Values); engine.RestoreFlows(s.RuleBook?.FlowTotals);
            var policy = new WorldBehaviorPolicy(s.Behaviors ?? WorldBehaviorPolicy.DefaultDocument(), DynamicsRuntime.Keys(runtime.Document, RuleWorldFields), ruleValues.ReadRule);
            policy.Controllers.Restore(s.BehaviorStates ?? Array.Empty<BehaviorControllerSnapshot>());
            return (runtime, engine, policy);
        }

        private void ReplayTail(WorldSaveData save)
        {
            var s = save.Checkpoint;
            int index = s.CommandCursor;
            for (int i = 0; i < s.PendingCommands; i++) commands.Enqueue(WorldCommandFactory.Create(save.Commands[index++]));
            // A checkpoint at the target tick restores pending input without applying it early.
            if (s.Tick == save.Tick)
            {
                CheckpointGuard.Require(index == save.Commands.Count, "commands after terminal checkpoint");
                return;
            }
            double dt = 1.0 / save.TickRate;
            for (ulong tick = s.Tick; ; tick++)
            {
                while (index < save.Commands.Count && save.Commands[index].Tick == tick)
                    commands.Enqueue(WorldCommandFactory.Create(save.Commands[index++]));
                CheckpointGuard.Require(index == save.Commands.Count || save.Commands[index].Tick > tick, "command predates checkpoint");
                ExecutePendingCommands();
                if (tick == save.Tick) break;
                Update(dt);
            }
        }
    }
}
