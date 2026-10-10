using System;
using System.Collections.Generic;
using OpenTK.Mathematics;
namespace ForesTycoon
{
    // CPU rig shared by live rendering and the GLB animation exporter. Coordinates are glTF (+Y up).
    internal sealed class ForwarderCraneRig
    {
        private readonly AnimatedGlbModel model;
        private readonly Matrix4[] rest;
        private readonly int[] joints = new int[7];
        private readonly Dictionary<int,Vector3> previousAngles = new();
        internal float Error { get; private set; }
        internal bool Available => Array.TrueForAll(joints, i => i >= 0);
        internal ForwarderCraneRig(AnimatedGlbModel model)
        {
            this.model=model;
            string[] names={"knee_1","knee_2","knee_3","knee_4","knee_5","calw.L","claw,R"};
            for(int i=0;i<names.Length;i++) joints[i]=Array.FindIndex(model.Nodes,n=>n.Name==names[i]);
            var p=model.CreatePose();p.Evaluate(null,0);rest=(Matrix4[])p.World.Clone();
        }
        private Vector3 Apply(AnimatedGlbModel.Pose p, Vector3 angles, float jaw)
        {
            foreach(int i in model.Order) {
                var n=model.Nodes[i];Matrix4 local=n.Matrix??Matrix4.CreateScale(n.Scale)*Matrix4.CreateFromQuaternion(n.Rotation)*Matrix4.CreateTranslation(n.Translation);
                if(i==joints[0])local=Matrix4.CreateRotationY(angles.X)*local;
                if(i==joints[1])local=Matrix4.CreateRotationZ(angles.Y)*local;
                if(i==joints[2])local=Matrix4.CreateRotationZ(angles.Z)*local;
                if(i==joints[5])local=Matrix4.CreateRotationX(jaw)*local;
                if(i==joints[6])local=Matrix4.CreateRotationX(-jaw)*local;
                Matrix4 world=n.Parent<0?local:local*p.World[n.Parent];
                if(i==joints[3]||i==joints[4]) {
                    var at=world.Row3;
                    world=rest[i];world.Row3=new Vector4(0,0,0,1);
                    if(i==joints[3])world*=Matrix4.CreateRotationY(angles.X);
                    world.Row3=at;
                }
                p.World[i]=world;
            }
            return p.World[joints[4]].Row3.Xyz;
        }
        internal Vector3 Solve(AnimatedGlbModel.Pose pose, Vector3 logCenter, float jaw, int instance=0)
        {
            if(!Available){pose.Evaluate(null,0);Error=float.PositiveInfinity;return logCenter;}
            Vector3 target=logCenter+new Vector3(0,.6f,0);
            previousAngles.TryGetValue(instance,out Vector3 angles);
            for(int iteration=0;iteration<48;iteration++) {
                Vector3 head=Apply(pose,angles,jaw),e=target-head;
                if(e.Length<.001f)break;
                Vector3 a=angles;a.X+=.001f;Vector3 x=(Apply(pose,a,jaw)-head)/.001f;
                a=angles;a.Y+=.001f;Vector3 y=(Apply(pose,a,jaw)-head)/.001f;
                a=angles;a.Z+=.001f;Vector3 z=(Apply(pose,a,jaw)-head)/.001f;
                float det=Vector3.Dot(x,Vector3.Cross(y,z));
                Vector3 step=Math.Abs(det)<.001f ? new Vector3(Vector3.Dot(e,x),Vector3.Dot(e,y),Vector3.Dot(e,z))*.03f :
                    new Vector3(Vector3.Dot(e,Vector3.Cross(y,z)),Vector3.Dot(e,Vector3.Cross(z,x)),Vector3.Dot(e,Vector3.Cross(x,y)))/det;
                if(step.Length>.2f)step*=.2f/step.Length;
                angles+=step;
            }
            previousAngles[instance]=angles;
            Vector3 result=Apply(pose,angles,jaw)-new Vector3(0,.6f,0);Error=(result-logCenter).Length;return result;
        }
    }
}
