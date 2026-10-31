using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace ForesTycoon.Audio;

internal static class AudioManifest
{
    internal static Dictionary<AmbientCue, PcmWave> Load(string root, Action<string> log)
    {
        var clips = new Dictionary<AmbientCue, PcmWave>();
        string manifest = Path.Combine(root, "manifest.json");
        if (!File.Exists(manifest)) return clips;
        if (new FileInfo(manifest).Length > 64 * 1024) throw new InvalidDataException("Audio manifest exceeds 64 KiB.");
        var entries = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(manifest))
            ?? throw new InvalidDataException("Audio manifest must be an object.");
        foreach (var entry in entries) {
            if (!Enum.TryParse<AmbientCue>(entry.Key, false, out var cue) || !Enum.IsDefined(cue))
                throw new InvalidDataException($"Unknown audio cue: {entry.Key}");
            string relative = entry.Value;
            if (string.IsNullOrWhiteSpace(relative) || Path.IsPathRooted(relative) || relative.Contains('\\') ||
                Array.Exists(relative.Split('/'), p => p is ".." or "." || p.Length == 0) ||
                !string.Equals(Path.GetExtension(relative), ".wav", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"Use a relative WAV path for {cue}.");
            try {
                using var input = File.OpenRead(Path.Combine(root, relative));
                clips.Add(cue, PcmWave.Read(input));
            }
            catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException) {
                log($"Audio cue {cue} skipped: {e.Message}");
            }
        }
        return clips;
    }
}
