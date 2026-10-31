using System;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Collections.Generic;
using System.Threading;
using ForesTycoon.Audio;

namespace ForesTycoon;

// Generates its own test tone. No purchased assets, graphics window or external content needed.
internal static class AudioSmokeTest
{
    internal static void Run()
    {
        string root = Path.Combine(Path.GetTempPath(), "forestycoon-audio-" + Guid.NewGuid());
        Directory.CreateDirectory(root);
        try {
            using (var w = new BinaryWriter(File.Create(Path.Combine(root, "tone.wav")), Encoding.ASCII)) {
                const int frames = 4800;
                void Tag(string text) => w.Write(Encoding.ASCII.GetBytes(text));
                Tag("RIFF"); w.Write(36 + frames * 2); Tag("WAVE"); Tag("fmt "); w.Write(16);
                w.Write((ushort)1); w.Write((ushort)1); w.Write(48000); w.Write(96000); w.Write((ushort)2); w.Write((ushort)16);
                Tag("data"); w.Write(frames * 2);
                for (int i = 0; i < frames; i++) w.Write((short)(800 * Math.Sin(i * 2 * Math.PI * 440 / 48000)));
            }
            var entries = new Dictionary<string, string>();
            foreach (var cue in Enum.GetValues<AmbientCue>()) entries[cue.ToString()] = "tone.wav";
            File.WriteAllText(Path.Combine(root, "manifest.json"), JsonSerializer.Serialize(entries));
            // Two owners verify that context restoration and disposal do not invalidate the survivor.
            using var first = OpenAlAmbientOutput.TryCreate(root, Console.Error.WriteLine)
                ?? throw new InvalidOperationException("Native audio smoke could not open OpenAL.");
            using (var second = OpenAlAmbientOutput.TryCreate(root, Console.Error.WriteLine)
                ?? throw new InvalidOperationException("Second audio context failed.")) {
                for (int i = 0; i < 310; i++) {
                    first.Update(new(12, 8, true, .23, false), .1);
                    second.Update(new(2, 0, false, .5, true), .1);
                }
                second.Muted = true; second.Update(new(0, 0, false, .75, true), .1);
            }
            first.Update(new(5, 0, false, .75, true), .1);
            Thread.Sleep(150);
            Console.WriteLine("Audio smoke passed: eight cues, two contexts, loop/event gain, mute and cleanup.");
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
