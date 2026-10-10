using OpenTK.Mathematics;
namespace ForesTycoon.Tests;
public class ForwarderLoadingTests
{
    private static ForestMachine Machine(float logs) => new() {Kind=ForestMachineKind.Forwarder,State=ForestMachineState.Loading,
        Source=new TimberStack{Volume=logs*ForwarderLoading.LogVolume,Value=logs*100},Tuning=GameTuning.Default};
    [Theory] [InlineData(2)] [InlineData(3)] [InlineData(10)]
    public void DurationCountsLogsAndConservesVolumeAndValue(int logs)
    {
        var m=Machine(logs);double seconds=0;bool done=false;
        while(!done && seconds<100){done=ForwarderLoading.Step(m,1.0/30,(_,_)=>throw new Exception());seconds+=1.0/30;}
        Assert.True(done);Assert.InRange(seconds,logs*8-.001,logs*8+.1);
        Assert.InRange(Math.Abs(m.Cargo-logs*ForwarderLoading.LogVolume),0,.0001f);
        Assert.InRange(Math.Abs(m.CargoValue-logs*100),0,.001);Assert.Equal(0,m.LogTransferVolume);
        m.State=ForestMachineState.Unloading;float received=0;double value=0;done=false;
        for(int i=0;i<logs*250&&!done;i++)done=ForwarderLoading.Step(m,1.0/30,(a,v)=>{received+=a;value+=v;});
        Assert.True(done);Assert.InRange(Math.Abs(received-logs*ForwarderLoading.LogVolume),0,.0001f);
        Assert.InRange(Math.Abs(value-logs*100),0,.001);Assert.InRange(m.Cargo,0,.0001f);
    }
    [Fact] public void GraspReservesAndReleaseDeliversOneLog()
    {
        var m=Machine(3);ForwarderLoading.Step(m,2.57,(_,_)=>{});
        Assert.Equal(2,ForwarderLoading.Count(m.Source.Volume));Assert.Equal(0,m.Cargo);
        Assert.Equal(ForwarderLoading.LogVolume,m.LogTransferVolume);
        ForwarderLoading.Step(m,3.6,(_,_)=>{});
        Assert.Equal(1,ForwarderLoading.Count(m.Cargo));Assert.Equal(0,m.LogTransferVolume);
    }
    [Fact] public void BunkBuildsBottomUpAndHighestSlotIsRemovedFirst()
    {
        float previous=0;
        for(int i=0;i<23;i++){Vector3 slot=ForwarderLoading.Slot(i,true);Assert.True(slot.Z>=previous);previous=slot.Z;}
        var m=Machine(0);m.State=ForestMachineState.Unloading;m.Cargo=2.5f*ForwarderLoading.LogVolume;m.CargoValue=250;
        ForwarderLoading.Step(m,2.57,(_,_)=>{});
        Assert.InRange(m.LogTransferVolume,.49f*ForwarderLoading.LogVolume,.51f*ForwarderLoading.LogVolume);
        Assert.Equal(2,ForwarderLoading.Count(m.Cargo));
    }
    [Fact] public void PartialCapacityDoesNotLoseTimber()
    {
        var m=Machine(2);m.Cargo=m.Capacity-.1f;float before=m.Cargo+m.Source.Volume;
        bool done=false;for(int i=0;i<250&&!done;i++)done=ForwarderLoading.Step(m,1.0/30,(_,_)=>{});
        Assert.True(done);Assert.Equal(m.Capacity,m.Cargo,4);Assert.Equal(before,m.Cargo+m.Source.Volume,4);
    }
}
