using System;
using System.IO;
using OpenTK.Mathematics;
namespace ForesTycoon
{
    internal static class ForwarderAnimationExport
    {
        internal static void Run(string output)
        {
            string source=Path.Combine(AppContext.BaseDirectory,"Assets","Licensed","forwarder.glb");
            var model=AnimatedGlbModel.Load(source);var rig=new ForwarderCraneRig(model);
            if(!rig.Available)throw new InvalidOperationException("Forwarder crane rig is missing.");
            float maxError=0;
            foreach (bool unloading in new[] { false, true })
            {
                rig = new ForwarderCraneRig(model);
                Vector3 ground = new(-4.8f,-3.5f,.2f), bunk = ForwarderLoading.Slot(0,true);
                GlbAnimationWriter.Write(unloading ? output : source, output,
                    unloading ? "Unload_Log_IK" : "Load_Log_IK", model,8,30,(pose,time)=> {
                    Vector3 target=ForwarderLoading.Target(unloading ? bunk : ground, unloading ? ground : bunk,time/8,out float jaw);
                    rig.Solve(pose,new Vector3(target.X,target.Z,-target.Y),jaw);maxError=Math.Max(maxError,rig.Error);
                }, append: unloading);
            }
            Console.WriteLine($"Forwarder IK exported to {Path.GetFullPath(output)}. Maximum target error: {maxError:F5} m.");
        }
    }
}
