using System;
using System.Collections.Generic;
using System.IO;
using OpenTK.Mathematics;
namespace ForesTycoon
{
    // Ambient wildlife: deterministic from habitat/seed and simulation time, no frame-time AI.
    internal sealed class WildlifeRenderer : IDisposable
    {
        private AnimatedGlbModel model;
        private AnimatedModelRenderer renderer;
        private int motionRoot=-1;
        private readonly Dictionary<int,AnimatedGlbModel.Pose> poses=new();
        private readonly WildlifeSystem simulation;
        internal WildlifeRenderer(WildlifeSystem simulation) { this.simulation = simulation; }
        internal int Count => simulation.Animals.Count;
        internal bool TryGetPosition(out Vector3 position) {
            position = Count > 0 ? simulation.Animals[0].Position : Vector3.Zero; return Count > 0;
        }
        internal static readonly Matrix4 Axis=Matrix4.CreateRotationX(MathF.PI/2)*Matrix4.CreateRotationZ(MathF.PI/2);
        internal void Draw(Terrain terrain,ForestSystem forest,GraphicsSettings settings,RenderContext context)
        {
            if(!settings.Wildlife)return;
            if(Count==0)return;
            if(model==null) {
                model=AnimatedGlbModel.Load(Path.Combine(AppContext.BaseDirectory,"Assets","Wildlife","elk.glb"));
                motionRoot=Array.FindIndex(model.Nodes,node=>node.Name=="RigRoot_01");
                if(motionRoot<0)throw new InvalidDataException("Elk locomotion root is missing.");
            }
            renderer??=new AnimatedModelRenderer(model);
            int outlineBudget=settings.Enhanced&&settings.WildlifeOutlines&&context.PixelsPerWorldUnit>=7&&RenderDevice.Visuals?.ShadowPass!=true
                ? settings.Quality==GraphicsQuality.High?8:settings.Quality==GraphicsQuality.Medium?4:0 :0;
            foreach(var animal in simulation.Animals)
            {
                float alpha=(float)Math.Clamp(context.InterpolationAlpha,0,1);
                float turn=MathF.Atan2(MathF.Sin(animal.Yaw-animal.PreviousYaw),MathF.Cos(animal.Yaw-animal.PreviousYaw));
                float yaw=animal.PreviousYaw+turn*alpha;
                Vector3 position=Vector3.Lerp(animal.PreviousPosition,animal.Position,alpha);
                if(!terrain.TryGetSurfaceZ(position.X,position.Y,out float z))continue;position.Z=z+0.02f;
                if(RenderDevice.Visuals?.ShadowPass!=true&&!RenderVisibility.SphereVisible(position+new Vector3(0,0,1.5f),3.5f,RenderDevice.ViewProjection))continue;
                if(!poses.TryGetValue(animal.Id,out var pose))poses.Add(animal.Id,pose=model.CreatePose());
                // Cross-fade per-node TRS; rigid antlers follow their animated parent too.
                pose.Evaluate("Stand_Eating_01",animal.Age+animal.Seed%100,
                    "WalkSlow",animal.WalkTime,animal.Blend,inPlaceRoot:motionRoot);
                Vector3 forward=new(MathF.Cos(yaw),MathF.Sin(yaw),0),left=new(-MathF.Sin(yaw),MathF.Cos(yaw),0);
                if(terrain.TryGetSurfaceZ(position.X+forward.X*0.8f,position.Y+forward.Y*0.8f,out float front)&&
                    terrain.TryGetSurfaceZ(position.X-forward.X*0.8f,position.Y-forward.Y*0.8f,out float back))forward.Z=(front-back)/1.6f;
                if(terrain.TryGetSurfaceZ(position.X+left.X*0.45f,position.Y+left.Y*0.45f,out float side)&&
                    terrain.TryGetSurfaceZ(position.X-left.X*0.45f,position.Y-left.Y*0.45f,out float other))left.Z=(side-other)/0.9f;
                forward.Normalize();Vector3 up=Vector3.Cross(forward,left).Normalized();left=Vector3.Cross(up,forward).Normalized();
                Matrix4 placement=new(new Vector4(forward,0),new Vector4(left,0),new Vector4(up,0),new Vector4(position,1));
                Matrix4 transform=Axis*Matrix4.CreateScale(1.3f)*placement;
                renderer.Draw(pose,transform,settings,outlineBudget>0?0.7f/context.PixelsPerWorldUnit:0);
                if(outlineBudget>0)outlineBudget--;
            }
        }
        public void Dispose(){renderer?.Dispose();renderer=null;model=null;poses.Clear();}
    }
}
