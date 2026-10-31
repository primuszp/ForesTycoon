namespace ForesTycoon;

internal sealed record RigPresentationSample(RigState State, bool Moving);

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
