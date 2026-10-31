using System;
using System.Collections.Generic;

namespace ForesTycoon;

/// <summary>A field of a scope: read for every element, writable when <see cref="Write"/> is set.</summary>
internal sealed record RuleScopeField(string Name, string Help, Func<SimulationWorld, int, double> Read, Action<SimulationWorld, int, double> Write = null)
{
    /// <summary>The range the field keeps; a flow never takes a stock below <see cref="Min"/> or fills it above <see cref="Max"/>.</summary>
    public double Min { get; init; } = double.NegativeInfinity;
    public double Max { get; init; } = double.PositiveInfinity;
    /// <summary>When <see cref="Read"/> skips the view (a direct reader), the reading through the view; tests compare the two.</summary>
    public Func<SimulationWorld, int, double> ViewRead { get; init; }
    /// <summary>False when an optional relation does not exist for this event's subject.</summary>
    public Func<SimulationWorld, int, bool> Available { get; init; }
    public bool LocalRead { get; init; }
    public RuleStockPlan LocalStock { get; init; }
}
internal sealed record RuleStockPlan(Func<SimulationWorld, int, double> Capacity, Func<double, double, double, double> Normalize);
/// <summary>A named action of a scope; returns true when it changed the world's structure (routes must be refreshed).</summary>
internal sealed record RuleScopeAction(string Name, string Help, Func<SimulationWorld, int, bool> Apply);
/// <summary>An entity set a rule runs on: its elements, fields and actions. Custom fields are kept per element ID.</summary>
internal sealed record RuleScope(string Id, string Name, string Element, Func<SimulationWorld, int[]> Elements, Func<SimulationWorld, int> Size,
    RuleScopeField[] Fields, RuleScopeAction[] Actions)
{
    /// <summary>The discovered object kind (<see cref="SimulationWorld.RuleObjects"/>) behind the scope.</summary>
    public string Entity { get; init; }
    public Type ObjectType { get; init; }
    public Func<SimulationWorld, int, string> Label { get; init; }
    public Func<SimulationWorld, int, int> StateIndex { get; init; } = (_, id) => id;
    public Func<SimulationWorld, int> StateSize { get; init; }
    public Func<SimulationWorld, IEnumerable<int>> PrepareElements { get; init; }
    public Func<SimulationWorld, int[]> PreparedElements { get; init; }
    public Action<SimulationWorld> Release { get; init; }
    /// <summary>Collections whose members are elements of a scope, so flows can be shared out among them.</summary>
    public RuleScopeCollection[] Collections { get; init; } = Array.Empty<RuleScopeCollection>();

}
/// <summary>A collection of a scope's element (“neighbours”) whose members are elements of scope <paramref name="Target"/>.</summary>
internal sealed record RuleScopeCollection(string Name, string Target, Func<SimulationWorld, int, int[]> Members)
{
    /// <summary>Optional bounded reader; writes members in the same order as <see cref="Members"/>.</summary>
    public RuleScopeBufferedMembers BufferedMembers { get; init; }
}
internal delegate int RuleScopeMemberReader(SimulationWorld world, int id, Span<int> destination);
internal sealed record RuleScopeBufferedMembers(int Capacity, RuleScopeMemberReader Read);
