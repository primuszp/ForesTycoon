using System;
using System.Collections.Generic;
using ForesTycoon.Rules;
using OpenTK.Mathematics;

namespace ForesTycoon.Audio;

internal readonly record struct SpatialVoice(long Id, string Event, Vector3 Position, float Gain);
internal interface ISpatialOutput : IDisposable { void Apply(ReadOnlySpan<SpatialVoice> voices); }

/// <summary>Bounded loudest-source selection. Coordinates and distances are metres in listener space.</summary>
internal sealed class SpatialMachineAudio(ISpatialOutput output, PresentationProfile profile) : IDisposable
{
    internal ISpatialOutput Output => output;
    private readonly SpatialVoice[] selected = new SpatialVoice[profile.SpatialVoiceLimit];
    internal static float Attenuation(float distance, SpatialSoundBinding b)
    {
        if (distance >= b.MaxDistance) return 0;
        // Keep the near field constant even when the authored maximum is close to the reference distance.
        double fadeStart = Math.Max(b.ReferenceDistance, b.MaxDistance - b.ReferenceDistance);
        double fade = Math.Clamp((b.MaxDistance - distance) / (b.MaxDistance - fadeStart), 0, 1);
        return (float)(b.ReferenceDistance / Math.Max(b.ReferenceDistance, distance) * fade);
    }
    internal void Update(IReadOnlyList<ActorSoundSample> emitters, Vector3 listener, Matrix4 orientation, float volume, bool active)
    {
        int count = 0;
        if (active) for (int item = 0; item < emitters.Count; item++) {
            var e = emitters[item];
            if (!profile.MachineSounds.TryGetValue(e.Event, out var b) || !b.Enabled || b.File.Length == 0) continue;
            var delta = (e.Position - listener) / Engine.WorldScale.MetresToWorld;
            float gain = AmbientSoundscape.Unit(volume) * (float)b.Gain * Attenuation(delta.Length, b);
            if (!float.IsFinite(gain) || gain <= .0001f) continue;
            // OpenAL listener looks toward -Z; screen X/Y keep the camera's left/right and up.
            var relative = Vector3.TransformVector(delta, orientation); relative.Z = -relative.Z;
            int slot = count;
            while (slot > 0 && (gain > selected[slot - 1].Gain || gain == selected[slot - 1].Gain && e.Id < selected[slot - 1].Id)) slot--;
            if (slot >= selected.Length) continue;
            for (int i = Math.Min(count, selected.Length - 1); i > slot; i--) selected[i] = selected[i - 1];
            selected[slot] = new(e.Id, e.Event, relative, gain); count = Math.Min(count + 1, selected.Length);
        }
        output.Apply(selected.AsSpan(0, count));
    }
    public void Dispose() => output.Dispose();
}
