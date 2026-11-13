using ForesTycoon.Audio;
using ForesTycoon.Editor;
using OpenTK.Mathematics;
namespace ForesTycoon.Tests;

public sealed class SpatialMachineAudioTests
{
    private sealed class Output : ISpatialOutput
    {
        internal SpatialVoice[] Voices = new SpatialVoice[32]; internal int Count;
        public void Apply(ReadOnlySpan<SpatialVoice> voices) { Count = voices.Length; voices.CopyTo(Voices); }
        public void Dispose() { }
    }
    private static PresentationBindings Bindings() {
        var data = new PresentationBindings { SpatialVoiceLimit = 2 };
        data.MachineSounds["truck.driving"] = new() { File = "engine.wav", Gain = 1, ReferenceDistance = 5, MaxDistance = 100 };
        return data;
    }
    [Fact] public void PoolSelectsLoudestWithStableTieBreakAndStopsOnPause()
    {
        var output = new Output(); using var mixer = new SpatialMachineAudio(output, new(Bindings()));
        ActorSoundSample[] emitters = [new(3,"truck.driving",new(3.2f,0,0)), new(2,"truck.driving",new(-3.2f,0,0)), new(1,"truck.driving",new(16,0,0))];
        mixer.Update(emitters,Vector3.Zero,Matrix4.Identity,1,true);
        Assert.Equal(2,output.Count); Assert.Equal(2,output.Voices[0].Id); Assert.Equal(3,output.Voices[1].Id);
        Assert.True(output.Voices[0].Position.X < 0); Assert.True(output.Voices[1].Position.X > 0);
        Assert.InRange(output.Voices[0].Gain,.499f,.501f);
        mixer.Update(emitters,Vector3.Zero,Matrix4.CreateRotationZ(MathF.PI),1,true);
        Assert.True(output.Voices[0].Position.X > 0);
        mixer.Update(emitters,Vector3.Zero,Matrix4.Identity,1,false); Assert.Equal(0,output.Count);
        mixer.Update(emitters,Vector3.Zero,Matrix4.Identity,0,true); Assert.Equal(0,output.Count);
        mixer.Update(emitters,new(1000,0,0),Matrix4.Identity,1,true); Assert.Equal(0,output.Count);
    }
    [Fact] public void MixerHasNoPerFrameManagedAllocation()
    {
        var output = new Output(); using var mixer = new SpatialMachineAudio(output,new(Bindings()));
        ActorSoundSample[] emitters = [new(1,"truck.driving",new(1,0,0))];
        for(int i=0;i<100;i++) mixer.Update(emitters,Vector3.Zero,Matrix4.Identity,1,true);
        long start=GC.GetAllocatedBytesForCurrentThread();
        for(int i=0;i<1000;i++) mixer.Update(emitters,Vector3.Zero,Matrix4.Identity,1,true);
        Assert.Equal(0,GC.GetAllocatedBytesForCurrentThread()-start);
    }
    [Fact] public void ShortAudibleRangeKeepsTheNearFieldAtFullGain()
    {
        var binding = new SpatialSoundBinding { ReferenceDistance = 100, MaxDistance = 101 };
        Assert.Equal(1, SpatialMachineAudio.Attenuation(50, binding));
        Assert.Equal(1, SpatialMachineAudio.Attenuation(100, binding));
        Assert.InRange(SpatialMachineAudio.Attenuation(100.5f, binding), .497f, .498f);
        Assert.Equal(0, SpatialMachineAudio.Attenuation(101, binding));
    }
    [Fact] public void StereoDownmixDoesNotOverflow()
    {
        Assert.Equal(new short[]{32767,-32768,0},AudioContent.Mono(new([32767,32767,-32768,-32768,1000,-1000],2,48000)));
    }
    [Fact] public void AnimationEventsRespectEnabledSpeedAndTransferSynchronization()
    {
        var data=Bindings(); data.ActorAnimations["harvester.felling"]=new(){Speed=2};
        Assert.Equal(6,ActorAnimationEvents.Time(new(data),ActorAnimationEvents.Work(ForestMachineKind.Harvester,ForestMachineState.Felling),3));
        data.ActorAnimations["harvester.felling"]=new(){Enabled=false};
        Assert.Equal(0,ActorAnimationEvents.Time(new(data),"harvester.felling",3));
        Assert.Equal(3,ActorAnimationEvents.Time(new(data),"forwarder.loading",3));
        data.ActorAnimations["forwarder.loading"]=new(){Speed=2}; Assert.Throws<InvalidDataException>(data.Validate);
    }
    [Theory]
    [InlineData((int)ForestMachineKind.Harvester,(int)ForestMachineState.Driving,"harvester.driving")]
    [InlineData((int)ForestMachineKind.Harvester,(int)ForestMachineState.Felling,"harvester.felling")]
    [InlineData((int)ForestMachineKind.Forwarder,(int)ForestMachineState.Loading,"forwarder.loading")]
    [InlineData((int)ForestMachineKind.Forwarder,(int)ForestMachineState.Unloading,"forwarder.unloading")]
    public void MachineStateMapsToEvent(int kindValue,int stateValue,string key)
    {
        var kind=(ForestMachineKind)kindValue; var state=(ForestMachineState)stateValue;
        Assert.Equal(key,ActorPresentationEvents.Machine(kind,state,false));
        Assert.Null(ActorPresentationEvents.Machine(kind,state,true));
        Assert.Null(ActorPresentationEvents.Machine(kind,ForestMachineState.Parked,false));
    }
    [Fact] public void EditorUndoProjectMigrationAndCheckpointCarryAllBindings()
    {
        var doc=new EditorDocument(); doc.Load(SimulationWorld.DefaultRuleProject()); var session=new PresentationSession(doc);
        session.Edit(p=>{p.MachineSounds["truck.driving"]=new(){File="engine.wav"};p.ActorAnimations["truck.wheels"]=new(){Speed=2};p.SpatialVoiceLimit=3;});
        Assert.NotNull(doc.History.Undo()); Assert.Equal("",session.Read().MachineSounds["truck.driving"].File);
        Assert.NotNull(doc.History.Redo());
        using var world=new SimulationWorld(TerrainSettings.Default.WithNodeSize(9,42));
        world.QueueRuleProject(doc.Capture()); world.ExecutePendingCommands();
        using var save=new MemoryStream();world.Save(save);save.Position=0;world.Load(save);
        Assert.Equal("engine.wav",world.Presentation.MachineSounds["truck.driving"].File);
        Assert.Equal(2,world.Presentation.ActorAnimations["truck.wheels"].Speed);Assert.Equal(3,world.Presentation.SpatialVoiceLimit);
        var old=SimulationWorld.DefaultRuleProject();old.Version=9;Assert.Equal(10,SimulationWorld.NormalizeRuleProject(old).Version);
        var invalid=world.RuleProjectDocument;invalid.Presentation.MachineSounds["truck.driving"]=new(){ReferenceDistance=100,MaxDistance=5};
        Assert.Throws<InvalidDataException>(()=>world.QueueRuleProject(invalid));
        Assert.Equal("engine.wav",world.Presentation.MachineSounds["truck.driving"].File);
    }
}
