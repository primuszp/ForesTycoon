using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using OpenTK.Audio.OpenAL;

namespace ForesTycoon.Audio;

// One owner, one render thread. Audio is optional: absent files/device never block game startup.
internal sealed class OpenAlAmbientOutput : IAmbientOutput
{
    private readonly Dictionary<AmbientCue, (int Source, int Buffer)> voices = new();
    private readonly int thread = Environment.CurrentManagedThreadId;
    private ALDevice device;
    private ALContext context;
    private bool disposed;

    internal static AmbientSoundscape TryCreate(string root, Action<string> log)
    {
        OpenAlAmbientOutput output = null;
        try {
            var clips = AudioManifest.Load(root, log);
            if (clips.Count == 0) return null;
            output = new OpenAlAmbientOutput();
            output.Initialize(clips);
            return new AmbientSoundscape(output);
        }
        catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException or JsonException or
            DllNotFoundException or EntryPointNotFoundException or TypeInitializationException or InvalidOperationException) {
            output?.Dispose();
            log($"Ambient audio disabled: {e.Message}");
            return null;
        }
    }

    private void Initialize(Dictionary<AmbientCue, PcmWave> clips)
    {
        device = ALC.OpenDevice(null);
        if (device == ALDevice.Null) throw new InvalidOperationException("No OpenAL playback device.");
        context = ALC.CreateContext(device, new int[] { 0 });
        if (context == ALContext.Null) throw new InvalidOperationException("Cannot create OpenAL context.");
        InContext(() => {
            foreach (var (cue, wave) in clips) {
                int buffer = AL.GenBuffer();
                // Record each resource immediately so partial initialization can be disposed.
                voices.Add(cue, (0, buffer));
                AL.BufferData(buffer, wave.Channels == 1 ? ALFormat.Mono16 : ALFormat.Stereo16, wave.Samples, wave.SampleRate);
                int source = AL.GenSource(); voices[cue] = (source, buffer);
                AL.Source(source, ALSourcei.Buffer, buffer);
                AL.Source(source, ALSourceb.SourceRelative, true);
                AL.Source(source, ALSourcef.RolloffFactor, 0);
                AL.Source(source, ALSourcef.Gain, 0);
                bool loop = cue < AmbientCue.Thunder;
                AL.Source(source, ALSourceb.Looping, loop);
                if (loop) AL.SourcePlay(source);
                CheckError();
            }
        });
    }

    private static void CheckError()
    {
        var error = AL.GetError();
        if (error != ALError.NoError) throw new InvalidOperationException($"OpenAL: {error}");
    }

    private void InContext(Action action)
    {
        if (Environment.CurrentManagedThreadId != thread) throw new InvalidOperationException("Audio belongs to the window thread.");
        var previous = ALC.GetCurrentContext();
        if (!ALC.MakeContextCurrent(context)) throw new InvalidOperationException("Cannot activate audio context.");
        try { action(); }
        finally { ALC.MakeContextCurrent(previous); }
    }

    public void SetLoop(AmbientCue cue, float gain) => SetEventGain(cue, gain);
    public void SetEventGain(AmbientCue cue, float gain)
    {
        if (disposed || !voices.TryGetValue(cue, out var voice)) return;
        InContext(() => { AL.Source(voice.Source, ALSourcef.Gain, AmbientSoundscape.Unit(gain)); CheckError(); });
    }
    public void PlayEvent(AmbientCue cue)
    {
        if (disposed || !voices.TryGetValue(cue, out var voice)) return;
        InContext(() => {
            AL.GetSource(voice.Source, ALGetSourcei.SourceState, out int state);
            if ((ALSourceState)state != ALSourceState.Playing) AL.SourcePlay(voice.Source);
            CheckError();
        });
    }

    public void Dispose()
    {
        if (disposed) return;
        if (Environment.CurrentManagedThreadId != thread) throw new InvalidOperationException("Audio belongs to the window thread.");
        disposed = true;
        if (context != ALContext.Null) {
            // A disconnected device may no longer activate. Still destroy its context and close it.
            var previous = ALC.GetCurrentContext();
            try {
                if (ALC.MakeContextCurrent(context)) {
                    foreach (var voice in voices.Values) {
                        if (voice.Source != 0) { AL.SourceStop(voice.Source); AL.DeleteSource(voice.Source); }
                        if (voice.Buffer != 0) AL.DeleteBuffer(voice.Buffer);
                    }
                }
            }
            finally {
                ALC.MakeContextCurrent(previous == context ? ALContext.Null : previous);
                ALC.DestroyContext(context); context = ALContext.Null;
            }
        }
        if (device != ALDevice.Null) { ALC.CloseDevice(device); device = ALDevice.Null; }
        voices.Clear();
    }
}
