using System;
using System.IO;
using ForesTycoon.Rules;

namespace ForesTycoon.Audio;

/// <summary>Shared content resolution and PCM policy for game, editor validation and audition.</summary>
internal static class AudioContent
{
    internal static string Root {
        get {
            string configured = Environment.GetEnvironmentVariable("FORESTYCOON_AUDIO_DIR");
            return string.IsNullOrWhiteSpace(configured) ? Path.Combine(AppContext.BaseDirectory, "Assets", "Licensed", "Audio") : configured;
        }
    }
    internal static PcmWave Read(string root, string relative)
    {
        PresentationBindings.ValidateAssetPath(relative, ".wav");
        using var stream = File.OpenRead(Path.Combine(root, relative));
        return PcmWave.Read(stream);
    }
    internal static PcmWave TryRead(string root, string relative, string eventId, Action<string> log)
    {
        // Malformed bindings are errors; optional missing/corrupt assets are logged and skipped.
        PresentationBindings.ValidateAssetPath(relative, ".wav");
        try { return Read(root, relative); }
        catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException) {
            log($"Audio cue {eventId} skipped: {e.Message}"); return null;
        }
    }
    internal static short[] Mono(PcmWave wave)
    {
        if (wave.Channels == 1) return wave.Samples;
        var mono = new short[wave.Samples.Length / 2];
        for (int i = 0; i < mono.Length; i++) mono[i] = (short)(((int)wave.Samples[i * 2] + wave.Samples[i * 2 + 1]) / 2);
        return mono;
    }
}
