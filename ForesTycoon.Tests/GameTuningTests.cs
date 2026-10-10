using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ForesTycoon;
using ForesTycoon.Rules;

public class GameTuningTests
{
    [Fact]
    public void DefaultsMatchTheRuntimeConstants()
    {
        var t = GameTuning.Default;
        Assert.True(t.IsDefault);
        Assert.Empty(t.ToOverrides());
        Assert.Equal(ForestMachine.ForwarderCapacity, t.F(Tune.ForwarderCapacity));
        Assert.Equal(ForestryLogistics.DieselPrice, t[Tune.DieselPrice]);
        Assert.Equal(TruckSpec.Default, t.Truck);
        Assert.Equal(RoadCosts.Build(RoadPaving.Asphalt), RoadCosts.Build(RoadPaving.Asphalt, t));
        // Every spec's default lies in its own range and belongs to a rule of the catalog.
        var catalog = CurrentGameRules.Build();
        foreach (var spec in GameTuning.Specs)
        {
            Assert.InRange(spec.Default, spec.Min, spec.Max);
            Assert.Contains(catalog.Rules, r => r.Id == spec.Rule && r.Parameters.Any(p => p.Key == spec.Id));
        }
    }

    [Fact]
    public void OverridesRoundTripAndRejectUnknownOrOutOfRange()
    {
        var t = GameTuning.FromOverrides(new Dictionary<string, double> { ["DieselPrice"] = 1.5, ["ForwarderCapacity"] = 20 });
        Assert.Equal(1.5, t[Tune.DieselPrice]);
        Assert.Equal(20f, t.F(Tune.ForwarderCapacity));
        var again = GameTuning.FromJson(t.ToJson());
        Assert.Equal(t.ToOverrides(), again.ToOverrides());
        Assert.Throws<ArgumentException>(() => GameTuning.FromOverrides(new Dictionary<string, double> { ["Nope"] = 1 }));
        Assert.Throws<ArgumentException>(() => GameTuning.FromOverrides(new Dictionary<string, double> { ["DieselPrice"] = -1 }));
        Assert.Throws<ArgumentException>(() => GameTuning.FromOverrides(new Dictionary<string, double> { ["DieselPrice"] = double.NaN }));
    }

    [Fact]
    public void CommandJournalPreservesTuning()
    {
        var command = new SetTuningCommand(GameTuning.FromOverrides(new Dictionary<string, double> { ["RepairSeconds"] = 90 }).ToJson());
        var record = command.ToRecord(7);
        Assert.Equal(WorldCommandKind.SetTuning, record.Kind);
        Assert.Equal(record, WorldCommandFactory.Create(record).ToRecord(7));
        using var stream = new MemoryStream();
        WorldSaveSerializer.Write(stream, new WorldSaveData { Tick = 7, Climate = ClimateDefinition.Default,
            SoilModel = SoilModelData.From(SoilLandscapeDefinition.Default), Commands = new() { record } });
        stream.Position = 0; var save = WorldSaveSerializer.Read(stream); save.ValidateReplay();
        Assert.Equal(90, GameTuning.FromJson(save.Commands[0].RuleJson)[Tune.RepairSeconds]);
    }

    [Fact]
    public void TunedValuesDriveUpkeepTrafficAndTruckPhysics()
    {
        var t = GameTuning.FromOverrides(new Dictionary<string, double>
        {
            ["WearPerSecond"] = 0.002, ["RepairBaseCost"] = 1000, ["ReferenceWear"] = 0.003, ["RollingGravel"] = 0.05, ["WearFuelPenalty"] = 1
        });
        var tuned = new VehicleUpkeep(1); var plain = new VehicleUpkeep(1);
        tuned.Operate(10, 1, out _, t); plain.Operate(10, 1, out _);
        Assert.Equal(plain.Wear * 8, tuned.Wear, 5);
        Assert.Equal(1 + tuned.Wear, tuned.Fuel(t), 5);
        Assert.Equal(2 * RoadTrafficParameters.Load(36000), RoadTrafficParameters.Load(36000, t), 6);
        Assert.Equal(0.05f, VehicleDynamics.RollingCoefficient(t.Truck, RoadSurface.Gravel, 0));
        Assert.True(VehicleDynamics.Resistance(t.Truck, 30000, 10, 0, RoadSurface.Gravel, 0)
            > VehicleDynamics.Resistance(TruckSpec.Default, 30000, 10, 0, RoadSurface.Gravel, 0));
    }

    [Fact]
    public void MaximumLegalTrafficTuningIsAcceptedByTheExecutableGraph()
    {
        var tuning = GameTuning.FromOverrides(new Dictionary<string, double> {
            ["ReferenceWear"] = GameTuning.Spec(Tune.ReferenceWear).Max,
            ["ReferenceMass"] = GameTuning.Spec(Tune.ReferenceMass).Min,
            ["MacadamFactor"] = GameTuning.Spec(Tune.MacadamFactor).Max
        });
        var rule = new CompiledRoadRule(RuleModel.Default()); rule.ValidateRange();
        float result = rule.Evaluate(new(RoadTrafficParameters.Load(100000, tuning), RoadTrafficParameters.SurfaceFactor(RoadPaving.Macadam, tuning), 0));
        Assert.Equal(1, result);
    }

    [Fact]
    public void CatalogCarriesEditedTunablesOnly()
    {
        var catalog = CurrentGameRules.Build();
        Assert.Empty(catalog.TuningOverrides());
        var rule = catalog.Rules.Find(r => r.Id == "economy.fuel")!;
        int i = rule.Parameters.FindIndex(p => p.Key == "DieselPrice");
        rule.Parameters[i] = rule.Parameters[i] with { Value = 0.9 };
        var loaded = GameRuleCatalog.FromJson(catalog.ToJson());
        Assert.Equal(new Dictionary<string, double> { ["DieselPrice"] = 0.9 }, loaded.TuningOverrides());
        // A tuned world describes itself with its values, and the editor sees them as edits from the defaults.
        var described = CurrentGameRules.Build(tuning: GameTuning.FromOverrides(loaded.TuningOverrides()));
        Assert.Equal(loaded.TuningOverrides(), described.TuningOverrides());
        rule.Parameters[i] = rule.Parameters[i] with { Value = 99 };
        Assert.Throws<InvalidDataException>(() => catalog.ToJson());
    }
}
