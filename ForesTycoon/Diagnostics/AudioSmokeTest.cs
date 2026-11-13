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
            using var first = OpenAlAudioOutput.TryCreate(root, Console.Error.WriteLine)
                ?? throw new InvalidOperationException("Native audio smoke could not open OpenAL.");
            using (var second = OpenAlAudioOutput.TryCreate(root, Console.Error.WriteLine)
                ?? throw new InvalidOperationException("Second audio context failed.")) {
                for (int i = 0; i < 310; i++) {
                    first.Update(new(12, 8, true, .23, false), .1);
                    second.Update(new(2, 0, false, .5, true), .1);
                }
                second.Muted = true; second.Update(new(0, 0, false, .75, true), .1);
            }
            first.Update(new(5, 0, false, .75, true), .1);
            var data = new Rules.PresentationBindings { SpatialVoiceLimit = 2 };
            data.MachineSounds["truck.driving"] = new() { File = "tone.wav", Gain = .1 };
            using (var spatial = OpenAlAudioOutput.TryCreateSpatial(root, Console.Error.WriteLine, new(data))
                ?? throw new InvalidOperationException("Spatial output failed.")) {
                var backend = (OpenAlAudioOutput)spatial.Output;
                ActorSoundSample[] emitters = { new(1, "truck.driving", new(1, 0, 0)), new(2, "truck.driving", new(-2, 0, 0)), new(3, "truck.driving", new(10, 0, 0)) };
                for (int i = 0; i < 20; i++) {
                    spatial.Update(emitters, OpenTK.Mathematics.Vector3.Zero, OpenTK.Mathematics.Matrix4.Identity, .1f, true);
                    first.Update(new(5, 0, false, .75, true), .1);
                }
                spatial.Update(emitters, new(-1, 0, 0), OpenTK.Mathematics.Matrix4.Identity, .1f, true);
                if (backend.ActiveSpatialVoices != 2 || backend.SpatialStarts != 2) throw new InvalidOperationException("Source cap or loop identity failed.");
                spatial.Update(emitters, OpenTK.Mathematics.Vector3.Zero, OpenTK.Mathematics.Matrix4.Identity, .1f, false);
                if (backend.ActiveSpatialVoices != 0) throw new InvalidOperationException("Spatial pause failed.");
                spatial.Update(emitters, OpenTK.Mathematics.Vector3.Zero, OpenTK.Mathematics.Matrix4.Identity, .1f, true);
                if (backend.SpatialStarts != 4) throw new InvalidOperationException("Spatial resume failed.");
            }
            first.Update(new(5, 0, false, .75, true), .1);
            Thread.Sleep(150);
            Console.WriteLine("Audio smoke passed: eight ambient cues, spatial pool, stable loops, pause/resume, interleaved contexts and cleanup.");
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
