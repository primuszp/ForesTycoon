using System.Text.Json;
using System.Text.Json.Nodes;
using ForesTycoon.Rules;

namespace ForesTycoon.Tests;

public class BehaviorControllerTests
{
    private static CompiledBehaviorControllers Compile(params BehaviorController[] controllers) => new(new() { Controllers = controllers.ToList() });
    private static ControllerInputs Input(double dt = 1, double cargo = 0, int state = 0, bool working = true, bool broken = false) => new(dt, cargo, state, working, broken);

    [Fact]
    public void TypeAndObjectControllersKeepIndependentTimersAndDoNotChangeTheSource()
    {
        var type = BehaviorController.TimedPause("forwarder");
        var specific = BehaviorController.TimedPause("forwarder"); specific.ObjectId = 7;
        specific.States[0].Transitions[0].Conditions[0].Value = 2;
        var runtime = Compile(type, specific);
        specific.States[0].Action = BehaviorAction.ReturnHome;
        Assert.Equal(BehaviorAction.Wait, runtime.Step("forwarder", 7, Input()));
        Assert.Equal(BehaviorAction.Autonomous, runtime.Step("forwarder", 7, Input()));
        Assert.Equal(BehaviorAction.Wait, runtime.Step("forwarder", 8, Input()));
        Assert.Equal(BehaviorAction.Autonomous, runtime.Step("processor", 7, Input()));
        Assert.Equal(2, runtime.Capture().Length);
        Assert.Equal(1, runtime.Capture().Single(s => s.ObjectId == 8).Elapsed);
    }
    [Fact]
    public void ConditionsAreConjoinedAndTransitionsHaveStablePriorityWithBoundedLoops()
    {
        var controller = BehaviorController.TimedPause("forwarder");
        controller.States[0].Transitions[0].Conditions = new() { new() { Trigger = BehaviorTrigger.CargoAtLeast, Value = 2 }, new() { Trigger = BehaviorTrigger.MachineState, Value = 3 } };
        controller.States[0].Transitions.Add(new() { Target = "wait", Conditions = new() { new() { Trigger = BehaviorTrigger.Always } } });
        controller.States[1].Transitions.Add(new() { Target = "wait", Conditions = new() { new() } });
        var runtime = Compile(controller);
        Assert.Equal(BehaviorAction.Wait, runtime.Step("forwarder", 1, Input(cargo: 3, state: 1)));
        Assert.Equal(BehaviorAction.Autonomous, runtime.Step("forwarder", 1, Input(cargo: 3, state: 3)));
        Assert.Equal(BehaviorAction.Wait, runtime.Step("forwarder", 1, Input(cargo: 3, state: 3)));
    }
    [Fact]
    public void EnteredStateEventIsNotRepeatedAfterSaveOrWhileStateIsUnchanged()
    {
        var controller = BehaviorController.TimedPause("forwarder");
        controller.States[0].Transitions[0].Conditions = new() { new() { Trigger = BehaviorTrigger.EnteredMachineState, Value = 3 } };
        controller.States[1].Transitions.Add(new() { Target = "wait", Conditions = new() { new() } });
        var runtime = Compile(controller);
        Assert.Equal(BehaviorAction.Autonomous, runtime.Step("forwarder", 1, Input(state: 3)));
        Assert.Equal(BehaviorAction.Wait, runtime.Step("forwarder", 1, Input(state: 3)));
        var restored = Compile(controller); restored.Restore(runtime.Capture());
        Assert.Equal(BehaviorAction.Wait, restored.Step("forwarder", 1, Input(state: 3)));
        restored.Step("forwarder", 1, Input(state: 1));
        Assert.Equal(BehaviorAction.Autonomous, restored.Step("forwarder", 1, Input(state: 3)));
    }
    [Theory]
    [InlineData(BehaviorTrigger.WorkAssigned)]
    [InlineData(BehaviorTrigger.WorkFinished)]
    [InlineData(BehaviorTrigger.Repaired)]
    public void LifecycleEventsSurviveRestoration(BehaviorTrigger trigger)
    {
        var controller = BehaviorController.TimedPause("processor");
        controller.States[0].Transitions[0].Conditions = new() { new() { Trigger = trigger } };
        var runtime = Compile(controller);
        runtime.Step("processor", 1, Input(working: trigger == BehaviorTrigger.WorkFinished, broken: trigger == BehaviorTrigger.Repaired));
        var restored = Compile(controller); restored.Restore(runtime.Capture());
        Assert.Equal(BehaviorAction.Autonomous, restored.Step("processor", 1, Input(working: trigger != BehaviorTrigger.WorkFinished)));
    }
    [Fact]
    public void InvalidBindingsStatesAndSnapshotsAreRejectedAtomically()
    {
        var controller = BehaviorController.TimedPause("forwarder");
        Assert.Throws<InvalidDataException>(() => Compile(controller, controller));
        controller.States[0].Transitions[0].Target = "missing";
        Assert.Throws<InvalidDataException>(() => Compile(controller));
        controller.States[0].Transitions[0].Target = "work";
        var runtime = Compile(controller); runtime.Step("forwarder", 1, Input()); var before = runtime.Capture();
        Assert.Throws<InvalidDataException>(() => runtime.Restore(new[] { before[0] with { Elapsed = double.NaN } }));
        Assert.Throws<InvalidDataException>(() => runtime.Restore(new[] { before[0], before[0] }));
        Assert.Equal(before, runtime.Capture());
        controller.States[0].Transitions[0].Conditions[0].Value = double.PositiveInfinity;
        Assert.Throws<InvalidDataException>(() => Compile(controller));
    }
    [Fact]
    public void CosmeticEditsKeepTimersButChangedLogicRestartsItsOwnInstance()
    {
        var type = BehaviorController.TimedPause("forwarder"); var specific = BehaviorController.TimedPause("forwarder"); specific.ObjectId = 7;
        var old = Compile(type, specific); old.Step("forwarder", 7, Input()); old.Step("forwarder", 8, Input());
        type.States[0].Name = "New title"; type.States[0].X = 200;
        specific.States[0].Transitions[0].Conditions[0].Value = 10;
        var updated = Compile(type, specific); updated.PreserveUnchanged(old);
        Assert.Equal(8UL, Assert.Single(updated.Capture()).ObjectId);
        updated.Step("forwarder", 8, Input()); Assert.Equal(2, updated.Capture()[0].Elapsed);
        updated.Step("forwarder", 7, Input()); Assert.Equal(1, updated.Capture().Single(s => s.ObjectId == 7).Elapsed);
    }
    [Fact]
    public void WaitingMidGrapplePreservesWoodAndFuelThenResumesLoading()
    {
        var map = new TerrainMap(TerrainSettings.Default.WithNodeSize(9, 42), (_, _) => 4);
        var logistics = new ForestryLogistics(map, new ForestSystem(map)) { MachinesEnabled = true };
        var machine = new ForestMachine { Id = 1, Kind = ForestMachineKind.Forwarder, Path = new[] { 0 }, State = ForestMachineState.Loading,
            Source = new() { Volume = 10, Value = 100 }, Upkeep = new(1) };
        logistics.Machines.Add(machine);
        ForwarderLoading.Step(machine, 3, (_, _) => throw new Exception());
        Assert.True(machine.LogTransferVolume > 0);
        double time = machine.WorkTime, wood = machine.Source.Volume + machine.Cargo + machine.LogTransferVolume, value = machine.Source.Value + machine.CargoValue + machine.LogTransferValue;
        logistics.Controllers = Compile(BehaviorController.TimedPause("forwarder"));
        logistics.Update(4);
        Assert.Equal(time, machine.WorkTime); Assert.Equal(0, machine.FuelUsed); Assert.Equal(0, machine.Upkeep.Wear);
        logistics.Update(2);
        Assert.True(machine.WorkTime > time); Assert.True(machine.FuelUsed > 0);
        Assert.Equal(wood, machine.Source.Volume + machine.Cargo + machine.LogTransferVolume, 4);
        Assert.Equal(value, machine.Source.Value + machine.CargoValue + machine.LogTransferValue, 4);
    }
    [Fact]
    public void WaitingDoesNotPreventRepair()
    {
        var map = new TerrainMap(TerrainSettings.Default.WithNodeSize(9, 42), (_, _) => 4);
        var logistics = new ForestryLogistics(map, new ForestSystem(map)) { MachinesEnabled = true, Controllers = Compile(BehaviorController.TimedPause("forwarder")) };
        var machine = new ForestMachine { Id = 1, Kind = ForestMachineKind.Forwarder, Path = new[] { 0 }, State = ForestMachineState.Loading,
            Source = new() { Volume = 10 }, Upkeep = new(1) { Broken = true, RepairLeft = .5f } };
        logistics.Machines.Add(machine); logistics.Update(1);
        Assert.False(machine.Upkeep.Broken); Assert.Equal(0, machine.WorkTime); Assert.True(logistics.RunningCosts > 0);
    }
    [Fact]
    public void WorldSaveRestoresTimerAndGrappleAndRejectsInvalidStateWithoutChangingWorld()
    {
        var settings = TerrainSettings.Default.WithNodeSize(9, 42);
        using var world = new GameWorld(settings, enableRendering: false);
        // Install a valid checkpoint fixture with a loading forwarder and a stable stack identity.
        var checkpoint = world.CaptureCheckpoint();
        world.Logistics.Restore(checkpoint.Logistics with {
            NextMachineId = 2, NextStackId = 2, Stacks = new[] { new StackCheckpoint(1, 0, 10, 100) },
            Machines = new[] { new ForestMachineCheckpoint(1, ForestMachineKind.Forwarder, -1, new[] { 0 }, 0, 0, ForestMachineState.Loading,
                ForestMachineState.Loading, 0, 0, Source: 1) }
        });
        var machine = world.Logistics.Machines[0]; ForwarderLoading.Step(machine, 3, (_, _) => throw new Exception());
        var controller = BehaviorController.TimedPause("forwarder");
        world.QueueBehaviors(new() { Controllers = new() { controller } }); world.ExecutePendingCommands(); world.Update(1.0 / 30);
        Assert.True(Assert.Single(world.BehaviorStates).Elapsed > 0);
        using var saved = new MemoryStream(); world.Save(saved);
        using var restored = new GameWorld(settings, enableRendering: false); saved.Position = 0; restored.Load(saved);
        Assert.Equal(JsonSerializer.Serialize(world.CaptureCheckpoint()), JsonSerializer.Serialize(restored.CaptureCheckpoint()));
        for (int i = 0; i < 70; i++) { world.Update(1.0 / 30); restored.Update(1.0 / 30); }
        Assert.Equal("work", Assert.Single(restored.BehaviorStates).State);
        Assert.True(restored.Logistics.Machines[0].Cargo > 0);
        Assert.Equal(JsonSerializer.Serialize(world.CaptureCheckpoint()), JsonSerializer.Serialize(restored.CaptureCheckpoint()));
        saved.Position = 0; var invalid = JsonNode.Parse(saved)!;
        invalid["checkpoint"]!["behaviorStates"]![0]!["state"] = "missing";
        string before = JsonSerializer.Serialize(restored.CaptureCheckpoint());
        using var corrupt = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(invalid.ToJsonString()));
        Assert.Throws<InvalidDataException>(() => restored.Load(corrupt));
        Assert.Equal(before, JsonSerializer.Serialize(restored.CaptureCheckpoint()));
    }
    [Fact]
    public void ControllerEditsKeepPendingBoundaryAndReplayFromJournal()
    {
        var settings = TerrainSettings.Default.WithNodeSize(9, 42);
        using var world = new GameWorld(settings, enableRendering: false);
        using var restored = new GameWorld(settings, enableRendering: false);
        var controller = BehaviorController.TimedPause("forwarder");
        var model = new BehaviorModel { Controllers = new() { controller } };
        world.QueueBehaviors(model); world.ExecutePendingCommands(); world.Update(1.0 / 30);
        controller.States[0].Transitions[0].Conditions[0].Value = 12;
        world.QueueBehaviors(model);
        using var pending = new MemoryStream(); world.Save(pending); pending.Position = 0; restored.Load(pending);
        Assert.Equal(5, restored.BehaviorDocument.Controllers[0].States[0].Transitions[0].Conditions[0].Value);
        restored.ExecutePendingCommands(); world.ExecutePendingCommands();
        Assert.Equal(model.ToJson(), restored.BehaviorDocument.ToJson());
        using var saved = new MemoryStream(); world.Save(saved); saved.Position = 0; var data = WorldSaveSerializer.Read(saved);
        using var journal = new MemoryStream();
        WorldSaveSerializer.Write(journal, new WorldSaveData { Terrain = data.Terrain, SoilModel = data.SoilModel, Climate = data.Climate,
            Tick = data.Tick, TickRate = data.TickRate, ForestYearSeconds = data.ForestYearSeconds, Commands = data.Commands });
        journal.Position = 0; restored.Load(journal);
        Assert.Equal(model.ToJson(), restored.BehaviorDocument.ToJson());
        Assert.Equal(JsonSerializer.Serialize(world.CaptureCheckpoint()), JsonSerializer.Serialize(restored.CaptureCheckpoint()));
    }
    [Fact]
    public void VersionOneExpressionsAndVersionFourteenSavesMigrateWithoutControllers()
    {
        _ = new CompiledBehaviorModel(BehaviorModel.FromJson("{\"schema\":\"forest-behaviors\",\"version\":1,\"graphs\":[]}"), WorldBehaviorPolicy.Hooks);
        using var world = new GameWorld(TerrainSettings.Default.WithNodeSize(9, 42), enableRendering: false);
        using var saved = new MemoryStream(); world.Save(saved); saved.Position = 0;
        var legacy = JsonNode.Parse(saved)!; legacy["version"] = 14; legacy["runtimeRulesVersion"] = "forestycoon-simulation/2026-10-10.4";
        legacy["checkpoint"]!.AsObject().Remove("behaviorStates");
        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(legacy.ToJsonString())); world.Load(stream);
        Assert.Empty(world.BehaviorStates);
    }
}
