using System;
using System.Linq;
using ForesTycoon.Rules;

namespace ForesTycoon;

internal readonly record struct LogisticsInputs(
    double StepSeconds = 0,
    double MachineKind = 0,
    double MachineState = 0,
    double MachineCargo = 0,
    double MachineCapacity = 0,
    double MachineWear = 0,
    double MachinePace = 0,
    double MachineHomeRequested = 0,
    double SourceStock = 0,
    double SourceFed = 0,
    double SourceAvailableTimber = 0,
    double SourceReachable = 0,
    double SpecFellingRate = 0,
    double SpecUnloadRate = 0,
    double SpecSpeed = 0,
    double SpecLoadedSpeed = 0,
    double SpecFuelDriving = 0,
    double SpecFuelLoaded = 0,
    double SpecFuelCrane = 0,
    double SpecFuelFelling = 0,
    double SpecEmptyMass = 0,
    double SpecTimberDensity = 0,
    double SpecLoadRate = 0,
    double LogVolume = 0,
    double LogLastVolume = 0,
    double TransferUnloading = 0,
    double TransferRequested = 0,
    double TransferLoaded = 0,
    double RouteDamage = 0,
    double RouteSurface = 0,
    double RouteFrom = 0,
    double RouteTo = 0,
    double MillStock = 0,
    double MillProcessed = 0,
    double SiteForest = 0, double SiteHarvest = 0, double SiteRoadAccess = 0, double DepotCount = 0,
    double OrderHasTimber = 0, double OrderDestinationIsMill = 0,
    double WorkflowStage = 0, double DecisionContinue = 0, double DecisionDeliver = 0, double DecisionFetch = 0, double DecisionReturn = 0,
    double WorkflowArrived = 0, double WorkflowAtSource = 0,
    double MachineControl = 0, double MachineWorking = 0, double MachineBroken = 0, double MachineHasVehicle = 0,
    double MachineTransit = 0, double MachineTransportState = 0, double MachineSpeed = 0, double DecisionAllow = 0, double TransferHeld = 0)
{
    internal void Write(double[] buffer, string[] keys)
    {
        for (int i = 0; i < keys.Length; i++) buffer[i] = keys[i] switch {
            "transfer.held" => TransferHeld,
            "machine.control" => MachineControl, "machine.working" => MachineWorking, "machine.broken" => MachineBroken,
            "machine.hasVehicle" => MachineHasVehicle, "machine.transit" => MachineTransit, "machine.transportState" => MachineTransportState,
            "machine.speed" => MachineSpeed, "decision.allow" => DecisionAllow,
            "workflow.stage" => WorkflowStage, "workflow.arrived" => WorkflowArrived, "workflow.atSource" => WorkflowAtSource,
            "decision.continue" => DecisionContinue, "decision.deliver" => DecisionDeliver, "decision.fetch" => DecisionFetch, "decision.return" => DecisionReturn,
            "site.forest" => SiteForest, "site.harvest" => SiteHarvest, "site.roadAccess" => SiteRoadAccess, "depot.count" => DepotCount,
            "order.hasTimber" => OrderHasTimber, "order.destinationIsMill" => OrderDestinationIsMill,
            "step.seconds" => StepSeconds,
            "machine.kind" => MachineKind,
            "machine.state" => MachineState,
            "machine.cargo" => MachineCargo,
            "machine.capacity" => MachineCapacity,
            "machine.wear" => MachineWear,
            "machine.pace" => MachinePace,
            "machine.homeRequested" => MachineHomeRequested,
            "source.stock" => SourceStock,
            "source.fed" => SourceFed,
            "source.availableTimber" => SourceAvailableTimber,
            "source.reachable" => SourceReachable,
            "spec.fellingRate" => SpecFellingRate,
            "spec.unloadRate" => SpecUnloadRate,
            "spec.speed" => SpecSpeed,
            "spec.loadedSpeed" => SpecLoadedSpeed,
            "spec.fuelDriving" => SpecFuelDriving,
            "spec.fuelLoaded" => SpecFuelLoaded,
            "spec.fuelCrane" => SpecFuelCrane,
            "spec.fuelFelling" => SpecFuelFelling,
            "spec.emptyMass" => SpecEmptyMass,
            "spec.timberDensity" => SpecTimberDensity,
            "spec.loadRate" => SpecLoadRate,
            "log.volume" => LogVolume,
            "log.lastVolume" => LogLastVolume,
            "transfer.unloading" => TransferUnloading,
            "transfer.requested" => TransferRequested,
            "transfer.loaded" => TransferLoaded,
            "route.damage" => RouteDamage,
            "route.surface" => RouteSurface,
            "route.from" => RouteFrom,
            "route.to" => RouteTo,
            "mill.stock" => MillStock,
            "mill.processed" => MillProcessed,
            _ => throw new InvalidOperationException("Unknown logistics input")
        };
    }
}

internal sealed class LogisticsRuleEvaluator
{
    private static readonly System.Collections.Generic.Dictionary<(string, string), BehaviorHook> hooks = LogisticsRules.Hooks.ToDictionary(h => (h.Id, h.Kind));
    [ThreadStatic] private static CompiledBehaviorModel defaults;
    private readonly double[] buffer = new double[LogisticsRules.InputKeys.Length];
    internal double Evaluate(IBehaviorPolicy policy, string hook, string kind, ulong id, in LogisticsInputs inputs)
    {
        kind = LogisticsRules.IsKind(kind) ? kind : LogisticsRules.Kind(kind);
        var definition = hooks[(hook, kind)];
        inputs.Write(buffer, definition.Inputs);
        if (policy != null) return policy.Evaluate(new(hook, kind, id, 0, Extras: buffer));
        defaults ??= new(new() { Graphs = RuleProject.CreateDefault().Behaviors.Graphs.Where(g => LogisticsRules.IsKind(g.Kind)).ToList() }, LogisticsRules.Hooks);
        return defaults.Evaluate(hook, kind, id, new(), buffer);
    }
}
