using System;

namespace ForesTycoon
{
    sealed partial class GameWorld
    {
        internal WorldCheckpointData CaptureCheckpoint()
        {
            EnsureAvailable();
            return new(2, worldTick, commandJournal.Count - commands.Count,
                commands.Count, map.Capture(), ecosystem.Capture(), wildlife.Capture(), Logistics.Capture(), vehicles.Capture(),
                timberCargo.Available, timberCargo.Delivered, effects.Capture(), lastForestryAction, lastForestryArea, Expenses, RuleDocument,
                Tuning.IsDefault ? null : Tuning.ToOverrides(), roadWeatherSeconds, BehaviorDocument, BehaviorStates);
        }

        private void RestoreCheckpoint(WorldCheckpointData s)
        {
            roadRule = new CompiledRoadRule(s.Rules ?? RuleModel.Default());
            roadRule.ValidateRange();
            CheckpointGuard.Require(Enum.IsDefined(s.LastAction) && s.LastArea.TileCount >= 0 && s.LastArea.Applied >= 0 &&
                s.LastArea.Applied <= s.LastArea.TileCount, "forestry UI state");
            CheckpointGuard.NonNegative(s.LastArea.TimberVolume, "forestry volume");
            map.Restore(s.Terrain); ecosystem.Restore(s.Ecology);
            Logistics.Restore(s.Logistics); timberCargo.Restore(s.AvailableTimber, s.DeliveredTimber);
            vehicles.Restore(s.Vehicles, map.Tiles.Count); Logistics.BindVehicles(vehicles);
            wildlife.Restore(s.Wildlife, map.Tiles.Count); effects.Restore(s.Effects);
            foreach (var v in vehicles.Vehicles)
                if (v.LocalCargo) CheckpointGuard.Require(Logistics.Mills.Exists(m => m.TileId == v.SawmillTileId)
                    || Logistics.StackAt(v.SawmillTileId) != null, "truck destination");
            CheckpointGuard.NonNegative(s.Expenses, "expenses");
            CheckpointGuard.Require(double.IsFinite(s.RoadWeatherSeconds) && s.RoadWeatherSeconds >= 0 &&
                s.RoadWeatherSeconds < 0.5, "road weather remainder");
            roadWeatherSeconds = s.RoadWeatherSeconds;
            worldTick = s.Tick; lastForestryAction = s.LastAction; lastForestryArea = s.LastArea; Expenses = s.Expenses;
            behaviorPolicy = new WorldBehaviorPolicy(s.Behaviors ?? new BehaviorModel());
            behaviorPolicy.Controllers.Restore(s.BehaviorStates ?? Array.Empty<BehaviorControllerSnapshot>());
            foreach (var state in BehaviorStates)
                CheckpointGuard.Require(state.Kind == "truck" ? Logistics.Trucks.Exists(t => (ulong)t.Id == state.ObjectId)
                    : Logistics.Machines.Exists(m => (ulong)m.Id == state.ObjectId &&
                    (m.Kind == ForestMachineKind.Harvester ? "processor" : "forwarder") == state.Kind), "controller target");
            BindBehaviors();
            ApplyTuning(GameTuning.FromOverrides(s.Tuning));
        }

        private void ReplayTail(WorldSaveData save)
        {
            var s = save.Checkpoint;
            int index = s.CommandCursor;
            for (int i = 0; i < s.PendingCommands; i++) commands.Enqueue(WorldCommandFactory.Create(save.Commands[index++]));
            // A checkpoint at the target tick restores pending input without applying it early.
            if (s.Tick == save.Tick) {
                CheckpointGuard.Require(index == save.Commands.Count, "commands after terminal checkpoint");
                return;
            }
            double dt = 1.0 / save.TickRate;
            for (ulong tick = s.Tick; ; tick++)
            {
                while (index < save.Commands.Count && save.Commands[index].Tick == tick)
                    commands.Enqueue(WorldCommandFactory.Create(save.Commands[index++]));
                CheckpointGuard.Require(index == save.Commands.Count || save.Commands[index].Tick > tick, "command predates checkpoint");
                commands.ExecutePending(this);
                if (tick == save.Tick) break;
                Update(dt);
            }
        }
    }
}
