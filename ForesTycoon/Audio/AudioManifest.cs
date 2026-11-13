using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using ForesTycoon.Rules;

namespace ForesTycoon.Audio;

internal static class AudioManifest
{
    internal static Dictionary<AmbientCue, PcmWave> Load(string root, Action<string> log, PresentationProfile profile = null)
    {
        var clips = new Dictionary<AmbientCue, PcmWave>();
        string manifest = Path.Combine(root, "manifest.json");
        if (File.Exists(manifest) && new FileInfo(manifest).Length > 64 * 1024) throw new InvalidDataException("Audio manifest exceeds 64 KiB.");
        var entries = File.Exists(manifest)
            ? JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(manifest))
                ?? throw new InvalidDataException("Audio manifest must be an object.")
            : new Dictionary<string, string>();
        if (profile != null) foreach (var (cue, binding) in profile.Sounds) {
            if (!binding.Enabled) entries.Remove(cue);
            else if (binding.File.Length > 0) entries[cue] = binding.File;
        }
        foreach (var entry in entries) {
            if (!Enum.TryParse<AmbientCue>(entry.Key, false, out var cue) || !Enum.IsDefined(cue))
                throw new InvalidDataException($"Unknown audio cue: {entry.Key}");
            var wave = AudioContent.TryRead(root, entry.Value, cue.ToString(), log);
            if (wave != null) clips.Add(cue, wave);
        }
        return clips;
    }
}
