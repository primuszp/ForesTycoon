using System.Text.Json;
using ForesTycoon.Audio;
using ForesTycoon.Editor;
using OpenTK.Mathematics;

namespace ForesTycoon.Tests;

public sealed class PresentationBindingTests
{
    [Fact]
    public void EditorEditsUndoRedoAndRoundTripWithoutSharingMutableData()
    {
        var document = new EditorDocument(); document.Load(SimulationWorld.DefaultRuleProject());
        var session = new PresentationSession(document); string before = document.Revision();
        session.Edit(p => { p.Animations["deer.walk"] = new() { Clip = "WalkSlow", Speed = 2 };
            p.Sounds["Thunder"] = new() { File = "storm.wav", MinimumDelay = 3, DelayRange = 4 }; });
        Assert.NotEqual(before, document.Revision());
        var saved = document.Capture();
        Assert.NotNull(document.History.Undo()); Assert.Equal(1, session.Read().Animations["deer.walk"].Speed);
        Assert.NotNull(document.History.Redo()); Assert.Equal(2, session.Read().Animations["deer.walk"].Speed);
        var copy = RuleProject.FromJson(saved.ToJson());
        Assert.Equal("storm.wav", copy.Presentation.Sounds["Thunder"].File);
        copy.Presentation.Animations.Clear();
        Assert.Equal(2, session.Read().Animations.Count);
        string revision = document.Revision();
        Assert.Throws<InvalidDataException>(() => session.Edit(p => p.Sounds["Thunder"] = new() { File = "../outside.wav" }));
        Assert.Equal(revision, document.Revision());
    }

    [Fact]
    public void VersionEightProjectReceivesLegacyPresentationDefaults()
    {
        var project = SimulationWorld.DefaultRuleProject(); project.Version = 8;
        project.Presentation = null!;
        var upgraded = SimulationWorld.NormalizeRuleProject(project);
        Assert.Equal(RuleProject.CurrentVersion, upgraded.Version);
        Assert.Equal("WalkSlow", upgraded.Presentation.Animations["deer.walk"].Clip);
        Assert.Equal("", upgraded.Presentation.Sounds["Wind"].File);
    }

    [Theory]
    [InlineData("../elk.glb")] [InlineData("/tmp/elk.glb")] [InlineData("C:/elk.glb")] [InlineData("Assets/elk.wav")]
    public void AssetPathsArePortableAndStayWithinTheirContentRoot(string path)
    {
        var data = new PresentationBindings { WildlifeModel = path };
        Assert.Throws<InvalidDataException>(data.Validate);
    }

    [Fact]
    public void QueuedBindingsAreWorldLocalAndSurviveCheckpointAndExport()
    {
        using var world = new SimulationWorld(TerrainSettings.Default.WithNodeSize(9, 42));
        using var other = new SimulationWorld(TerrainSettings.Default.WithNodeSize(9, 42));
        var project = world.RuleProjectDocument;
        project.Presentation.Sounds["Deer"] = new() { Enabled = false };
        project.Presentation.Animations["deer.walk"] = new() { Clip = "WalkSlow", Speed = 2 };
        world.QueueRuleProject(project);
        Assert.True(world.Presentation.Sounds["Deer"].Enabled);
        world.ExecutePendingCommands();
        Assert.False(world.Presentation.Sounds["Deer"].Enabled);
        Assert.True(other.Presentation.Sounds["Deer"].Enabled);
        Assert.Equal(world.RuleProjectDocument.ContentRevision(), world.ExportRuleWorld("test").ProjectRevision);
        using var saved = new MemoryStream(); world.Save(saved); saved.Position = 0; other.Load(saved);
        Assert.Equal(world.Presentation.Revision, other.Presentation.Revision);
        Assert.Equal(JsonSerializer.Serialize(world.CaptureCheckpoint()), JsonSerializer.Serialize(other.CaptureCheckpoint()));
    }

    [Fact]
    public void ImmutableProfileSeparatesAudioAndAnimationRevisions()
    {
        var data = new PresentationBindings(); var original = new PresentationProfile(data);
        data.Sounds["Wind"] = new() { Gain = .2 };
        var changed = new PresentationProfile(data);
        Assert.Equal(original.AnimationRevision, changed.AnimationRevision);
        Assert.NotEqual(original.SoundRevision, changed.SoundRevision);
        Assert.Equal(1, original.Sounds["Wind"].Gain);
    }

    [Fact]
    public void GameAndPreviewSamplerHonorSelectedClipSpeedAndDisabledState()
    {
        var data = new PresentationBindings(); data.Animations["deer.walk"] = new() { Clip = "Stand_Eating_01", Speed = 2 };
        var player = new WildlifeAnimationPlayer(new(data));
        var actual = player.Model.CreatePose(); var expected = player.Model.CreatePose();
        player.Sample(actual, 0, .7, 1);
        int root = Array.FindIndex(player.Model.Nodes, n => n.Name == data.MotionRoot);
        expected.Evaluate("Stand_Eating_01", 1.4, inPlaceRoot: root);
        Assert.Equal(expected.World, actual.World);
        data.Animations["deer.walk"] = data.Animations["deer.walk"] with { Enabled = false };
        var disabled = new WildlifeAnimationPlayer(new(data));
        actual = disabled.Model.CreatePose(); expected = disabled.Model.CreatePose();
        disabled.Sample(actual, 2, 3, 1); expected.Evaluate(null, 0);
        Assert.Equal(expected.World, actual.World);
        data.Animations["deer.walk"] = new() { Clip = "missing" };
        Assert.Throws<InvalidDataException>(() => new WildlifeAnimationPlayer(new(data)));
    }

    [Fact]
    public void ExplicitSoundBindingsWorkWithoutManifestAndDisabledBindingsDoNotLoad()
    {
        string root = Path.Combine(Path.GetTempPath(), "bindings-" + Guid.NewGuid()); Directory.CreateDirectory(root);
        try {
            using (var wav = AmbientAudioTests.Wave(16)) File.WriteAllBytes(Path.Combine(root, "custom.wav"), wav.ToArray());
            var data = new PresentationBindings(); data.Sounds["Thunder"] = new() { File = "custom.wav" };
            var loaded = AudioManifest.Load(root, _ => { }, new(data)); Assert.Single(loaded); Assert.Contains(AmbientCue.Thunder, loaded.Keys);
            data.Sounds["Thunder"] = data.Sounds["Thunder"] with { Enabled = false };
            Assert.Empty(AudioManifest.Load(root, _ => { }, new(data)));
        } finally { Directory.Delete(root, true); }
    }

    private sealed class Output : IAmbientOutput
    {
        internal readonly float[] Gains = new float[8]; internal int Thunder;
        public void SetLoop(AmbientCue cue, float gain) => Gains[(int)cue] = gain;
        public void SetEventGain(AmbientCue cue, float gain) => Gains[(int)cue] = gain;
        public void PlayEvent(AmbientCue cue) { if (cue == AmbientCue.Thunder) Thunder++; }
        public void Dispose() { }
    }
    [Fact]
    public void AuthoredEventDelayGainAndDisableDriveTheMixer()
    {
        var data = new PresentationBindings();
        data.Sounds["Thunder"] = new() { Gain = .5, MinimumDelay = .1, DelayRange = 0 };
        var output = new Output(); using var mixer = new AmbientSoundscape(output, new(data));
        for (int i = 0; i < 10; i++) mixer.Update(new(0, 0, true, 0, false), .1);
        Assert.Equal(10, output.Thunder); Assert.InRange(output.Gains[6], .01f, .325f);
        data.Sounds["Thunder"] = data.Sounds["Thunder"] with { Enabled = false };
        var silent = new Output(); using var muted = new AmbientSoundscape(silent, new(data));
        for (int i = 0; i < 10; i++) muted.Update(new(0, 0, true, 0, false), .1);
        Assert.Equal(0, silent.Thunder); Assert.Equal(0, silent.Gains[6]);
    }
}
