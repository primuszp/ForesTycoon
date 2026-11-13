using System;
using System.Collections.Generic;
using System.IO;
using ForesTycoon.Rules;
using OpenTK.Audio.OpenAL;

namespace ForesTycoon.Audio;

internal sealed partial class OpenAlAudioOutput
{
    internal int SpatialStarts { get; private set; }
    internal int ActiveSpatialVoices { get { int count = 0; foreach (bool active in occupied) if (active) count++; return count; } }
    private readonly Dictionary<string, int> spatialBuffers = new(StringComparer.Ordinal);
    private int[] spatialSources = Array.Empty<int>();
    private SpatialVoice[] owners = Array.Empty<SpatialVoice>();
    private bool[] occupied = Array.Empty<bool>();
    internal static SpatialMachineAudio TryCreateSpatial(string root, Action<string> log, PresentationProfile profile)
    {
        OpenAlAudioOutput output = null;
        try {
            var waves = new Dictionary<string, PcmWave>();
            foreach (var (key, b) in profile.MachineSounds) {
                if (!b.Enabled || b.File.Length == 0) continue;
                var wave = AudioContent.TryRead(root, b.File, key, log);
                if (wave != null) waves[key] = wave;
            }
            if (waves.Count == 0) return null;
            output = new(); output.Initialize(new());
            using var lease = output.Activate();
            foreach (var (key, wave) in waves) {
                int buffer = AL.GenBuffer(); output.spatialBuffers.Add(key, buffer);
                var mono = AudioContent.Mono(wave);
                AL.BufferData(buffer, ALFormat.Mono16, mono, wave.SampleRate); CheckError();
            }
            output.spatialSources = new int[profile.SpatialVoiceLimit];
            output.owners = new SpatialVoice[profile.SpatialVoiceLimit]; output.occupied = new bool[profile.SpatialVoiceLimit];
            for (int i = 0; i < output.spatialSources.Length; i++) {
                int source = AL.GenSource(); output.spatialSources[i] = source;
                AL.Source(source, ALSourceb.SourceRelative, true); AL.Source(source, ALSourceb.Looping, true);
                AL.Source(source, ALSourcef.RolloffFactor, 0); // Shared CPU attenuation also drives source priority.
                CheckError();
            }
            var effective = profile.Capture();
            foreach (string key in new List<string>(effective.MachineSounds.Keys))
                if (!waves.ContainsKey(key)) effective.MachineSounds[key] = effective.MachineSounds[key] with { Enabled = false };
            return new(output, new(effective));
        } catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException or DllNotFoundException or EntryPointNotFoundException or TypeInitializationException or InvalidOperationException) {
            output?.Dispose(); log("Machine audio disabled: " + e.Message); return null;
        }
    }
    public void Apply(ReadOnlySpan<SpatialVoice> voicesToPlay)
    {
        if (disposed) return;
        using var lease = Activate();
        // Retain identities when priority order changes; a loop must not restart every frame.
        for (int i = 0; i < owners.Length; i++) {
            if (!occupied[i]) continue;
            bool keep = false;
            foreach (var voice in voicesToPlay) if (voice.Id == owners[i].Id && voice.Event == owners[i].Event) { keep = true; break; }
            if (!keep) { AL.SourceStop(spatialSources[i]); occupied[i] = false; }
        }
        foreach (var voice in voicesToPlay) {
            if (!spatialBuffers.TryGetValue(voice.Event, out int buffer)) continue;
            int slot = -1;
            for (int i = 0; i < owners.Length; i++) if (occupied[i] && owners[i].Id == voice.Id && owners[i].Event == voice.Event) { slot = i; break; }
            bool start = slot < 0;
            if (start) for (int i = 0; i < occupied.Length; i++) if (!occupied[i]) { slot = i; break; }
            if (slot < 0) continue;
            int source = spatialSources[slot];
            if (start) AL.Source(source, ALSourcei.Buffer, buffer);
            AL.Source(source, ALSourcef.Gain, voice.Gain);
            AL.Source(source, ALSource3f.Position, voice.Position.X, voice.Position.Y, voice.Position.Z);
            if (start) { AL.SourcePlay(source); SpatialStarts++; }
            owners[slot] = voice; occupied[slot] = true;
        }
        CheckError();
    }
    private void DisposeSpatialResources()
    {
        foreach (int source in spatialSources) if (source != 0) { AL.SourceStop(source); AL.DeleteSource(source); }
        foreach (int buffer in spatialBuffers.Values) AL.DeleteBuffer(buffer);
        spatialBuffers.Clear(); spatialSources = Array.Empty<int>();
    }
}
