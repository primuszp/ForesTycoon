using System;

namespace ForesTycoon
{
    sealed partial class GameWorld
    {
        internal WorldCheckpointData CaptureCheckpoint() => new(1, worldTick, commandJournal.Count - commands.Count,
            commands.Count, map.Capture(), ecosystem.Capture(), wildlife.Capture(), Logistics.Capture(), vehicles.Capture(),
            timberCargo.Available, timberCargo.Delivered, effects.Capture(), lastForestryAction, lastForestryArea, Expenses);

        private void RestoreCheckpoint(WorldCheckpointData s)
        {
            CheckpointGuard.Require(Enum.IsDefined(s.LastAction) && s.LastArea.TileCount >= 0 && s.LastArea.Applied >= 0 &&
                s.LastArea.Applied <= s.LastArea.TileCount, "forestry UI state");
            CheckpointGuard.NonNegative(s.LastArea.TimberVolume, "forestry volume");
            map.Restore(s.Terrain); ecosystem.Restore(s.Ecology);
            Logistics.Restore(s.Logistics); timberCargo.Restore(s.AvailableTimber, s.DeliveredTimber);
            vehicles.Restore(s.Vehicles, map.Tiles.Count); wildlife.Restore(s.Wildlife, map.Tiles.Count); effects.Restore(s.Effects);
            foreach (var v in vehicles.Vehicles)
                if (v.LocalCargo) CheckpointGuard.Require(Logistics.Mills.Exists(m => m.TileId == v.SawmillTileId), "truck destination");
            CheckpointGuard.NonNegative(s.Expenses, "expenses");
            worldTick = s.Tick; lastForestryAction = s.LastAction; lastForestryArea = s.LastArea; Expenses = s.Expenses;
            terrainRenderer.Dispose();
            terrainRenderer = new TerrainRenderer(terrain, vehicles, effects, forest, Graphics, Environment, wildlife, Logistics);
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
