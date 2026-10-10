using OpenTK.Mathematics;
namespace ForesTycoon.Tests;
public class ForwarderCraneTests
{
    [Fact] public void IkFollowsBothRoadSidesAndExportReplaysTheSamePose()
    {
        string asset=Path.Combine(AppContext.BaseDirectory,"Assets","Licensed","forwarder.glb");
        if(!File.Exists(asset))return;
        var model=AnimatedGlbModel.Load(asset);var rig=new ForwarderCraneRig(model);var pose=model.CreatePose();
        foreach(float side in new[]{-1f,1f})
            for(int frame=0;frame<=240;frame++) {
                var p=ForwarderLoading.Target(new Vector3(-4.8f,side*3.5f,.2f),ForwarderLoading.Slot(0,true),frame/240f,out float jaw);
                rig.Solve(pose,new Vector3(p.X,p.Z,-p.Y),jaw);
                Assert.InRange(rig.Error,0,.01f);
            }
        // A full 14 m³ load uses 23 slots, including the upper rows and both directions.
        for (int slot = 0; slot < 23; slot++)
            foreach (bool unloading in new[] { false, true })
                for (int frame = 0; frame <= 120; frame++)
                {
                    Vector3 ground = new(-4.8f, -3.5f, .2f), bunk = ForwarderLoading.Slot(slot, true);
                    var target = ForwarderLoading.Target(unloading ? bunk : ground, unloading ? ground : bunk, frame / 120f, out float jaw);
                    rig.Solve(pose, new Vector3(target.X, target.Z, -target.Y), jaw);
                    Assert.InRange(rig.Error, 0, .01f);
                }
        string output=Path.Combine(Path.GetTempPath(),Guid.NewGuid()+".glb");
        try {
            void Sample(AnimatedGlbModel.Pose p,float t) {
                var target=ForwarderLoading.Target(new Vector3(-4.8f,-3.5f,.2f),ForwarderLoading.Slot(0,true),t/8,out float jaw);
                rig.Solve(p,new Vector3(target.X,target.Z,-target.Y),jaw);
            }
            GlbAnimationWriter.Write(asset,output,"Roundtrip",model,8,30,Sample);
            var copy=AnimatedGlbModel.Load(output);var playback=copy.CreatePose();
            foreach(float time in new[]{0f,2.56f,4f,6.16f,7.5f}) {
                Sample(pose,time);playback.Evaluate("Roundtrip",time);
                int grip=Array.FindIndex(model.Nodes,n=>n.Name=="knee_5");
                float error=(pose.World[grip].Row3.Xyz-playback.World[grip].Row3.Xyz).Length;
                Assert.True(error<.005f,$"At {time}: {error}; live {pose.World[grip].Row3}, playback {playback.World[grip].Row3}");
            }
        } finally {File.Delete(output);}
    }
}
