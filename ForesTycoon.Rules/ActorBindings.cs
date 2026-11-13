using System;
using System.Collections.Generic;
using System.IO;

namespace ForesTycoon.Rules;

public sealed record ActorAnimationBinding
{
    public bool Enabled { get; init; } = true;
    public double Speed { get; init; } = 1;
}
public sealed record SpatialSoundBinding
{
    public string File { get; init; } = "";
    public bool Enabled { get; init; } = true;
    public double Gain { get; init; } = .5;
    public double ReferenceDistance { get; init; } = 5;
    public double MaxDistance { get; init; } = 150;
}
public static class ActorBindingDefaults
{
    public static Dictionary<string, ActorAnimationBinding> Animations() => new(StringComparer.Ordinal) {
        ["truck.wheels"] = new(), ["harvester.wheels"] = new(), ["forwarder.wheels"] = new(),
        ["harvester.felling"] = new(), ["harvester.unloading"] = new(),
        ["forwarder.loading"] = new(), ["forwarder.unloading"] = new()
    };
    public static Dictionary<string, SpatialSoundBinding> Sounds() => new(StringComparer.Ordinal) {
        ["truck.driving"] = new(), ["truck.loading"] = new(), ["truck.unloading"] = new(),
        ["harvester.driving"] = new(), ["harvester.felling"] = new(), ["harvester.unloading"] = new(),
        ["forwarder.driving"] = new(), ["forwarder.loading"] = new(), ["forwarder.unloading"] = new()
    };
    private static readonly HashSet<string> animationKeys = new(Animations().Keys, StringComparer.Ordinal);
    private static readonly HashSet<string> soundKeys = new(Sounds().Keys, StringComparer.Ordinal);
    public static bool Synchronized(string id) => id is "forwarder.loading" or "forwarder.unloading";
    internal static void Validate(PresentationBindings data)
    {
        if (data.ActorAnimations == null || data.MachineSounds == null || data.SpatialVoiceLimit is < 1 or > 32)
            throw new InvalidDataException("Hibás szereplő- vagy térhangprofil.");
        if (data.ActorAnimations.Count != animationKeys.Count || data.MachineSounds.Count != soundKeys.Count)
            throw new InvalidDataException("Hiányzó szereplőesemény.");
        foreach (var (id, b) in data.ActorAnimations)
            if (!animationKeys.Contains(id) || b == null || !double.IsFinite(b.Speed) || b.Speed < .01 || b.Speed > 4 || Synchronized(id) && b.Speed != 1)
                throw new InvalidDataException("Hibás animációesemény: " + id);
        foreach (var (id, b) in data.MachineSounds) {
            if (!soundKeys.Contains(id) || b == null || b.File == null || !double.IsFinite(b.Gain) || b.Gain < 0 || b.Gain > 1
                || !double.IsFinite(b.ReferenceDistance) || b.ReferenceDistance < .1 || b.ReferenceDistance > 1000
                || !double.IsFinite(b.MaxDistance) || b.MaxDistance <= b.ReferenceDistance || b.MaxDistance > 5000)
                throw new InvalidDataException("Hibás térhangkötés: " + id);
            if (b.File.Length > 0) PresentationBindings.ValidateAssetPath(b.File, ".wav");
        }
    }
}
