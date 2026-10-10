using System.IO;
using ForesTycoon;

public class RuleModelTests
{
    [Theory]
    [InlineData(0.0015, 1)]
    [InlineData(0.003, 0.2)]
    [InlineData(0, 1)]
    public void DefaultModelPreservesTrafficWear(double traffic, double surface)
    {
        var rule = new CompiledRoadRule(RuleModel.Default());
        Assert.Equal((float)(traffic * surface), rule.Evaluate(new(traffic, surface, 0.8)));
    }

    [Fact]
    public void CompiledSnapshotIsIndependentOfDraftAndNodeOrder()
    {
        var draft = RuleModel.Default(); draft.Nodes.Reverse();
        var compiled = new CompiledRoadRule(draft);
        draft.Nodes.Find(n => n.Id == "scale")!.Value = 4;
        Assert.Equal(0.0015f, compiled.Evaluate(new(0.0015, 1, 0)));
        Assert.Equal(0.006f, new CompiledRoadRule(draft).Evaluate(new(0.0015, 1, 0)));
    }

    [Fact]
    public void FeedbackReadsCurrentRoadStateAndClampsExtremeWear()
    {
        var model = RuleModel.Default();
        model.Nodes.Add(new() { Id = "damage", Name = "Sérültség", Operation = RuleOperation.RoadDamage });
        model.Nodes.Find(n => n.Id == "wear")!.B = "damage";
        var rule = new CompiledRoadRule(model);
        Assert.Equal(0, rule.Evaluate(new(0.01, 1, 0)));
        Assert.Equal(0.005f, rule.Evaluate(new(0.01, 1, 0.5)));
        model.Nodes.Find(n => n.Id == "wear")!.B = "scale";
        model.Nodes.Find(n => n.Id == "scale")!.Value = 10000;
        Assert.Equal(1, new CompiledRoadRule(model).Evaluate(new(0.01, 1, 0)));
    }

    [Fact]
    public void RejectsCyclesMissingInputsAndInvalidUnits()
    {
        var model = RuleModel.Default();
        model.Nodes.Find(n => n.Id == "base")!.A = "wear";
        Assert.Throws<InvalidDataException>(() => new CompiledRoadRule(model));
        model = RuleModel.Default(); model.Nodes.RemoveAll(n => n.Id == "traffic");
        Assert.Throws<InvalidDataException>(() => new CompiledRoadRule(model));
        model = RuleModel.Default(); model.Nodes.Find(n => n.Id == "base")!.Operation = RuleOperation.Add;
        Assert.Throws<InvalidDataException>(() => new CompiledRoadRule(model));
        model = RuleModel.Default(); model.Output = "scale";
        Assert.Throws<InvalidDataException>(() => new CompiledRoadRule(model));
    }

    [Fact]
    public void RejectsDisconnectedInvalidNodesAndOverflowBeforeInstallation()
    {
        var model = RuleModel.Default();
        model.Nodes.Add(new() { Id = "broken", Name = "Hibás", Operation = RuleOperation.Multiply });
        Assert.Throws<InvalidDataException>(() => new CompiledRoadRule(model));
        model = RuleModel.Default(); model.Nodes.Find(n => n.Id == "scale")!.Value = double.MaxValue;
        model.Nodes.Add(new() { Id = "overflow", Name = "Túlcsordulás", Operation = RuleOperation.Add, A = "scale", B = "scale" });
        Assert.Throws<InvalidDataException>(() => new SetRuleModelCommand(model.ToJson()));
    }

    [Fact]
    public void JsonAndCommandJournalPreserveModelAndLayout()
    {
        var model = RuleModel.Default(); model.Nodes.Find(n => n.Id == "scale")!.Value = 3;
        model.Nodes[0].X = 123; model.Name = "Nedves erdei út";
        var command = new SetRuleModelCommand(model.ToJson());
        var record = command.ToRecord(42);
        var recreated = WorldCommandFactory.Create(record).ToRecord(42);
        Assert.Equal(record, recreated);
        var loaded = RuleModel.FromJson(recreated.RuleJson);
        Assert.Equal(123, loaded.Nodes[0].X);
        Assert.Equal(model.Name, loaded.Name);
        Assert.Equal(0.0045f, new CompiledRoadRule(loaded).Evaluate(new(0.0015, 1, 0)));
        using var stream = new MemoryStream();
        WorldSaveSerializer.Write(stream, new WorldSaveData { Tick = 42, Climate = ClimateDefinition.Default,
            SoilModel = SoilModelData.From(SoilLandscapeDefinition.Default), Commands = new() { record } });
        stream.Position = 0; var save = WorldSaveSerializer.Read(stream); save.ValidateReplay();
        Assert.Equal(record, save.Commands[0]);
    }

    [Theory]
    [InlineData(-1, 1, 0)]
    [InlineData(0.01, 2, 0)]
    [InlineData(0.01, 1, 2)]
    public void RejectsInvalidWorldInputs(double traffic, double surface, double damage) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new CompiledRoadRule(RuleModel.Default()).Evaluate(new(traffic, surface, damage)));
}
