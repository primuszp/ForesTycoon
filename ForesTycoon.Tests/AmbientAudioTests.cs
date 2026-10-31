using System.Text;
using ForesTycoon.Audio;

namespace ForesTycoon.Tests;

public sealed class AmbientAudioTests
{
    private sealed class Output : IAmbientOutput
    {
        internal readonly Dictionary<AmbientCue, float> Gains = new();
        internal readonly List<AmbientCue> Events = new();
        internal int Disposals;
        public void SetLoop(AmbientCue cue, float gain) => Gains[cue] = gain;
        public void SetEventGain(AmbientCue cue, float gain) => Gains[cue] = gain;
        public void PlayEvent(AmbientCue cue) => Events.Add(cue);
        public void Dispose() => Disposals++;
    }

    [Fact]
    public void WeatherChangesFadeAndSnowHasNoLiquidRain()
    {
        var output = new Output(); using var mixer = new AmbientSoundscape(output);
        mixer.Update(new(0, 0, false, 0, false), .1);
        float before = output.Gains[AmbientCue.Rain];
        mixer.Update(new(15, 12, true, 0, false), .1);
        float first = output.Gains[AmbientCue.Rain];
        Assert.True(first > before && first < .55f * .7f * .7f);
        for (int i = 0; i < 100; i++) mixer.Update(new(15, 12, true, 0, false), .1);
        Assert.True(output.Gains[AmbientCue.Rain] > first);
        var snow = new AmbientState(8, 0, false, .75, false);
        Assert.Equal(0, AmbientSoundscape.Targets(snow)[(int)AmbientCue.Rain]);
    }

    [Fact]
    public void FadeIsIndependentOfFrameRate()
    {
        var a = new Output(); var b = new Output();
        using var x = new AmbientSoundscape(a); using var y = new AmbientSoundscape(b);
        var state = new AmbientState(10, 8, true, .3, true);
        for (int i = 0; i < 60; i++) x.Update(state, 1.0 / 60);
        for (int i = 0; i < 10; i++) y.Update(state, .1);
        foreach (var cue in Enum.GetValues<AmbientCue>()) Assert.Equal(a.Gains[cue], b.Gains[cue], 5);
    }

    [Theory]
    [InlineData(.25, 3)]
    [InlineData(.5, 4)]
    [InlineData(.75, 5)]
    [InlineData(1, 2)]
    public void SeasonalTargetsAreContinuousAtBoundaries(double year, int cue)
    {
        var before = AmbientSoundscape.Targets(new(0, 0, false, year - 1e-8, false));
        var after = AmbientSoundscape.Targets(new(0, 0, false, year, false));
        Assert.Equal(.3f, after[(int)cue], 5);
        for (int i = 0; i < 6; i++) Assert.Equal(before[i], after[i], 5);
    }

    [Fact]
    public void MuteFocusAndSlidersControlExistingEventsAsWellAsLoops()
    {
        var output = new Output(); using var mixer = new AmbientSoundscape(output);
        var state = new AmbientState(15, 12, true, .4, true);
        for (int i = 0; i < 120; i++) mixer.Update(state, .1);
        Assert.Contains(AmbientCue.Thunder, output.Events);
        mixer.Muted = true; mixer.Update(state, .1);
        Assert.All(output.Gains.Values, gain => Assert.Equal(0, gain));
        mixer.Muted = false; mixer.Update(state, .1, focused: false);
        Assert.All(output.Gains.Values, gain => Assert.Equal(0, gain));
        mixer.MasterVolume = 0; mixer.Update(state, .1);
        Assert.All(output.Gains.Values, gain => Assert.Equal(0, gain));
    }

    [Fact]
    public void DeerCallsRequireLiveDeerAndCalmWeatherAndPausePreventsEvents()
    {
        var output = new Output(); using var mixer = new AmbientSoundscape(output);
        for (int i = 0; i < 400; i++) mixer.Update(new(0, 0, false, .5, false), .1);
        Assert.Empty(output.Events);
        for (int i = 0; i < 400; i++) mixer.Update(new(0, 0, false, .5, true, Paused: true), .1);
        Assert.Empty(output.Events);
        for (int i = 0; i < 250; i++) mixer.Update(new(0, 0, false, .5, true), .1);
        Assert.Contains(AmbientCue.Deer, output.Events);
        Assert.Equal(0, AmbientSoundscape.Targets(new(0, 1, true, 0, true))[7]);
    }

    [Fact]
    public void InvalidInputsAndLongFramesCannotProduceInvalidGainsOrEventBursts()
    {
        var output = new Output(); var mixer = new AmbientSoundscape(output);
        mixer.MasterVolume = float.NaN; mixer.AmbienceVolume = float.PositiveInfinity;
        mixer.Update(new(double.NaN, double.PositiveInfinity, true, double.NaN, true), double.NaN);
        Assert.All(output.Gains.Values, gain => Assert.True(float.IsFinite(gain) && gain >= 0 && gain <= 1));
        mixer.Update(new(10, 12, true, 0, true), 10000);
        Assert.Empty(output.Events);
        mixer.Dispose(); mixer.Dispose(); Assert.Equal(1, output.Disposals);
    }

    internal static MemoryStream Wave(int bits, int channels = 1, bool oddMetadata = false)
    {
        var stream = new MemoryStream();
        using var w = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: true);
        void Tag(string s) => w.Write(Encoding.ASCII.GetBytes(s));
        Tag("RIFF"); w.Write(0); Tag("WAVE");
        if (oddMetadata) { Tag("JUNK"); w.Write(1); w.Write((byte)5); w.Write((byte)0); }
        Tag("fmt "); w.Write(16); w.Write((ushort)1); w.Write((ushort)channels); w.Write(48000);
        w.Write(48000 * channels * bits / 8); w.Write((ushort)(channels * bits / 8)); w.Write((ushort)bits);
        Tag("data"); w.Write(4 * channels * bits / 8);
        for (int c = 0; c < channels; c++) foreach (short sample in new short[] { short.MinValue, -1, 0, short.MaxValue }) {
            if (bits == 24) w.Write((byte)0);
            w.Write(sample);
        }
        stream.Position = 4; w.Write((int)stream.Length - 8); stream.Position = 0; return stream;
    }

    [Theory]
    [InlineData(16, 1)] [InlineData(24, 1)] [InlineData(16, 2)] [InlineData(24, 2)]
    public void DecodesSignedPcmAndSkipsPaddedMetadata(int bits, int channels)
    {
        using var stream = Wave(bits, channels, oddMetadata: true);
        var wave = PcmWave.Read(stream);
        Assert.Equal(channels, wave.Channels); Assert.Equal(48000, wave.SampleRate);
        Assert.Equal(new short[] { -32768, -1, 0, 32767 }, wave.Samples.Take(4));
        Assert.Equal(4 * channels, wave.Samples.Length);
    }

    [Fact]
    public void RejectsTruncatedWavAndUnsupportedFormat()
    {
        using var truncated = Wave(24); truncated.SetLength(truncated.Length - 1);
        Assert.Throws<InvalidDataException>(() => PcmWave.Read(truncated));
        using var floating = Wave(16); floating.Position = 20; floating.WriteByte(3); floating.Position = 0;
        Assert.Throws<InvalidDataException>(() => PcmWave.Read(floating));
    }

    [Fact]
    public void MissingManifestDoesNotInitializeNativeAudio()
    {
        Assert.Null(OpenAlAmbientOutput.TryCreate(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString()), _ => { }));
    }

    [Fact]
    public void ManifestRejectsTraversalAndSkipsMissingClips()
    {
        string root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString()); Directory.CreateDirectory(root);
        try {
            File.WriteAllText(Path.Combine(root, "manifest.json"), "{\"Wind\":\"../private.wav\"}");
            Assert.Throws<InvalidDataException>(() => AudioManifest.Load(root, _ => { }));
            File.WriteAllText(Path.Combine(root, "manifest.json"), "{\"Wind\":\"missing.wav\"}");
            var messages = new List<string>(); Assert.Empty(AudioManifest.Load(root, messages.Add)); Assert.Single(messages);
            using (var wave = Wave(24)) File.WriteAllBytes(Path.Combine(root, "wind.wav"), wave.ToArray());
            File.WriteAllText(Path.Combine(root, "manifest.json"), "{\"Wind\":\"wind.wav\"}");
            Assert.Single(AudioManifest.Load(root, _ => { }));
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
