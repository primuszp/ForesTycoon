using ForesTycoon.Rules;
namespace ForesTycoon;

internal static class ActorAnimationEvents
{
    internal static string Wheels(ForestMachineKind kind) => kind == ForestMachineKind.Harvester ? "harvester.wheels" : "forwarder.wheels";
    internal static string Work(ForestMachineKind kind, ForestMachineState state) =>
        state is ForestMachineState.Parked or ForestMachineState.Driving ? null : ActorPresentationEvents.Machine(kind, state, false);
    internal static double Time(PresentationProfile profile, string key, double time)
    {
        if (key == null) return time;
        var b = profile.ActorAnimations[key]; return b.Enabled ? time * b.Speed : 0;
    }
}
