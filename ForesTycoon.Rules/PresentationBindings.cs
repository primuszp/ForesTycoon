using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace ForesTycoon.Rules;

public sealed record AnimationEventBinding
{
    public string Clip { get; init; } = "";
    public double Speed { get; init; } = 1;
    public bool Enabled { get; init; } = true;
}

public sealed record SoundEventBinding
{
    /// <summary>Relative to the audio content root; empty inherits the installed manifest entry.</summary>
    public string File { get; init; } = "";
    public bool Enabled { get; init; } = true;
    public double Gain { get; init; } = 1;
    public double MinimumDelay { get; init; } = 8;
    public double DelayRange { get; init; } = 18;
}

/// <summary>Serializable presentation policy shared by authoring, game and checkpoints.</summary>
public sealed class PresentationBindings
{
    public Dictionary<string, ActorAnimationBinding> ActorAnimations { get; set; } = ActorBindingDefaults.Animations();
    public Dictionary<string, SpatialSoundBinding> MachineSounds { get; set; } = ActorBindingDefaults.Sounds();
    public int SpatialVoiceLimit { get; set; } = 16;
    public string WildlifeModel { get; set; } = "Assets/Wildlife/elk.glb";
    public string MotionRoot { get; set; } = "RigRoot_01";
    public Dictionary<string, AnimationEventBinding> Animations { get; set; } = new(StringComparer.Ordinal) {
        ["deer.idle"] = new() { Clip = "Stand_Eating_01" }, ["deer.walk"] = new() { Clip = "WalkSlow" }
    };
    public Dictionary<string, SoundEventBinding> Sounds { get; set; } = new(StringComparer.Ordinal) {
        ["Wind"] = new(), ["Rain"] = new(), ["Spring"] = new(), ["Summer"] = new(),
        ["Autumn"] = new(), ["Winter"] = new(), ["Thunder"] = new(),
        ["Deer"] = new() { MinimumDelay = 20, DelayRange = 45 }
    };
    public PresentationBindings Clone() => new() { WildlifeModel = WildlifeModel, MotionRoot = MotionRoot,
        Animations = new(Animations, StringComparer.Ordinal), Sounds = new(Sounds, StringComparer.Ordinal),
        ActorAnimations = new(ActorAnimations, StringComparer.Ordinal), MachineSounds = new(MachineSounds, StringComparer.Ordinal), SpatialVoiceLimit = SpatialVoiceLimit };

    private static readonly HashSet<string> animationKeys = new(new PresentationBindings().Animations.Keys, StringComparer.Ordinal);
    private static readonly HashSet<string> soundKeys = new(new PresentationBindings().Sounds.Keys, StringComparer.Ordinal);

    public void Validate()
    {
        ActorBindingDefaults.Validate(this);
        ValidateAssetPath(WildlifeModel, ".glb");
        if (MotionRoot == null || MotionRoot.Length > 256) throw new InvalidDataException("Hibás animációs gyökérnév.");
        if (Animations == null || Sounds == null || Animations.Count != animationKeys.Count || Sounds.Count != soundKeys.Count
            || Animations.Keys.Any(key => !animationKeys.Contains(key)) || Sounds.Keys.Any(key => !soundKeys.Contains(key)))
            throw new InvalidDataException("Ismeretlen vagy hiányzó megjelenítési eseménykötés.");
        foreach (var pair in Animations) {
            var value = pair.Value;
            if (value == null || value.Clip == null || value.Clip.Length > 256 || value.Enabled && string.IsNullOrWhiteSpace(value.Clip)
                || !double.IsFinite(value.Speed) || value.Speed < .01 || value.Speed > 20)
                throw new InvalidDataException("Hibás animációkötés: " + pair.Key);
        }
        foreach (var pair in Sounds) {
            var value = pair.Value;
            if (value == null || value.File == null || !double.IsFinite(value.Gain) || value.Gain < 0 || value.Gain > 1
                || !double.IsFinite(value.MinimumDelay) || value.MinimumDelay < .1 || value.MinimumDelay > 600
                || !double.IsFinite(value.DelayRange) || value.DelayRange < 0 || value.DelayRange > 600)
                throw new InvalidDataException("Hibás hangkötés: " + pair.Key);
            if (value.File.Length > 0) ValidateAssetPath(value.File, ".wav");
        }
    }

    public static void ValidateAssetPath(string path, string extension)
    {
        if (string.IsNullOrWhiteSpace(path) || path.Length > 1024 || Path.IsPathRooted(path) || path.Contains('\\') || path.Contains(':')
            || path.Split('/').Any(p => p is ".." or "." || p.Length == 0)
            || !string.Equals(Path.GetExtension(path), extension, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Relatív " + extension + " tartalomútvonal szükséges: " + path);
    }
}

/// <summary>Immutable, world-owned snapshot. Reading it never evaluates simulation rules.</summary>
public sealed class PresentationProfile
{
    private readonly PresentationBindings data;
    public string WildlifeModel => data.WildlifeModel;
    public string MotionRoot => data.MotionRoot;
    public IReadOnlyDictionary<string, AnimationEventBinding> Animations { get; }
    public IReadOnlyDictionary<string, SoundEventBinding> Sounds { get; }
    public IReadOnlyDictionary<string, ActorAnimationBinding> ActorAnimations { get; }
    public IReadOnlyDictionary<string, SpatialSoundBinding> MachineSounds { get; }
    public int SpatialVoiceLimit => data.SpatialVoiceLimit;
    public string SpatialRevision { get; }
    public string Revision { get; }
    public string AnimationRevision { get; }
    public string SoundRevision { get; }
    public PresentationProfile(PresentationBindings source)
    {
        if (source == null) throw new InvalidDataException("Hiányzó megjelenítési kötések.");
        source.Validate(); data = source.Clone();
        Animations = new ReadOnlyDictionary<string, AnimationEventBinding>(data.Animations);
        Sounds = new ReadOnlyDictionary<string, SoundEventBinding>(data.Sounds);
        ActorAnimations = new ReadOnlyDictionary<string, ActorAnimationBinding>(data.ActorAnimations);
        MachineSounds = new ReadOnlyDictionary<string, SpatialSoundBinding>(data.MachineSounds);
        SpatialRevision = JsonSerializer.Serialize(new { data.SpatialVoiceLimit, Sounds = data.MachineSounds.OrderBy(p => p.Key, StringComparer.Ordinal) });
        AnimationRevision = JsonSerializer.Serialize(new { data.WildlifeModel, data.MotionRoot,
            Animations = data.Animations.OrderBy(p => p.Key, StringComparer.Ordinal) });
        SoundRevision = JsonSerializer.Serialize(data.Sounds.OrderBy(p => p.Key, StringComparer.Ordinal));
        Revision = AnimationRevision + SoundRevision + SpatialRevision + JsonSerializer.Serialize(data.ActorAnimations.OrderBy(p => p.Key, StringComparer.Ordinal));
    }
    public PresentationBindings Capture() => data.Clone();
    public static PresentationProfile Default { get; } = new(new());
}
