namespace ForesTycoon;

internal sealed record RigPresentationSample(RigState State, bool Moving);

internal readonly record struct WorkAnimationSample(double Time, ForestMachineState State);
internal sealed class WorkAnimationPresentation
{
    private WorkAnimationSample? previous, current;
    internal void Publish(WorkAnimationSample next) { previous = current ?? next; current = next; }
    internal double Sample(double fallback, float alpha)
    {
        if (current is not { } now) return fallback;
        var before = previous.Value;
        double start = before.State == now.State && before.Time <= now.Time ? before.Time : 0;
        return start + (now.Time - start) * System.Math.Clamp(alpha, 0, 1);
    }
}

/// <summary>Only complete world ticks become visible. Cooperative, unfinished motion never leaks into interpolation.</summary>
internal sealed class RigPresentation
{
    internal RigState Previous { get; private set; }
    internal RigPresentationSample Current { get; private set; }
    internal void Reset() { Previous = null; Current = null; }
    internal void Publish(RigPresentationSample next)
    {
        Previous = Current?.State ?? next.State; Current = next;
    }
    internal VehicleKinematics Sample(float alpha) => Current == null ? null :
        VehicleKinematics.FromPresentation(Current.State).Interpolated(Previous, alpha, Current.Moving ? 2 : 1);
}
