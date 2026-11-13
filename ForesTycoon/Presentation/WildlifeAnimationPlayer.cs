using System;
using System.IO;
using ForesTycoon.Rules;

namespace ForesTycoon;

/// <summary>The same CPU sampler drives game poses and editor previews.</summary>
internal sealed class WildlifeAnimationPlayer
{
    internal AnimatedGlbModel Model { get; }
    internal PresentationProfile Profile { get; }
    private readonly int motionRoot;
    internal WildlifeAnimationPlayer(PresentationProfile profile, string contentRoot = null)
        : this(profile, AnimatedGlbModel.Load(Path.Combine(contentRoot ?? AppContext.BaseDirectory, profile.WildlifeModel))) { }

    internal WildlifeAnimationPlayer(PresentationProfile profile, AnimatedGlbModel model)
    {
        Profile = profile;
        Model = model;
        motionRoot = profile.MotionRoot.Length == 0 ? -1 : Array.FindIndex(Model.Nodes, n => n.Name == profile.MotionRoot);
        if (profile.MotionRoot.Length > 0 && motionRoot < 0) throw new InvalidDataException("Animációs gyökér nem található: " + profile.MotionRoot);
        foreach (var (eventId, binding) in profile.Animations)
            if (binding.Enabled && !Model.Clips.ContainsKey(binding.Clip))
                throw new InvalidDataException("Hiányzó animációklip: " + eventId + " → " + binding.Clip);
    }

    internal void Sample(AnimatedGlbModel.Pose pose, double idleTime, double walkTime, float blend)
    {
        if (!ReferenceEquals(pose.Model, Model)) throw new ArgumentException("A póz másik modellhez tartozik.", nameof(pose));
        var idle = Profile.Animations["deer.idle"]; var walk = Profile.Animations["deer.walk"];
        if (walk.Enabled)
            pose.Evaluate(idle.Enabled ? idle.Clip : null, idleTime * idle.Speed,
                walk.Clip, walkTime * walk.Speed, blend, motionRoot);
        else
            pose.Evaluate(null, 0, idle.Enabled ? idle.Clip : null, idleTime * idle.Speed, 1 - blend, motionRoot);
    }
}
