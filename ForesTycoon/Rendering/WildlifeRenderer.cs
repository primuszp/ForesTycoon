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
        private readonly Dictionary<int,AnimatedGlbModel.Pose> poses=new();
        private readonly List<WildlifeSpot> spots=new();
        private readonly List<int> obsolete=new();
        private ulong forestRevision=ulong.MaxValue,terrainRevision=ulong.MaxValue;
        internal int Count=>spots.Count;
        internal bool TryGetPosition(out Vector3 position) {
            position=spots.Count>0?spots[0].Position:Vector3.Zero;return spots.Count>0;
        }
        internal static readonly Matrix4 Axis=Matrix4.CreateRotationX(MathF.PI/2)*Matrix4.CreateRotationZ(MathF.PI/2);
        internal static (bool Walking,float Blend,double ClipTime,float Travel) Activity(double time,uint seed)
        {
            double phase=(time+seed%240)*1.0, cycle=(phase%40+40)%40;
            if(cycle<30)return(false,0,phase,0);
            double walk=cycle-30;
            float blend=(float)Math.Min(1,Math.Min(walk/1.0,(10-walk)/1.0));
            float travel=(float)(walk*Math.PI/5);
            return(true,blend,walk,travel);
        }
        internal void Draw(Terrain terrain,ForestSystem forest,GraphicsSettings settings,RenderContext context)
        {
            if(!settings.Wildlife)return;
            if(forestRevision!=forest.Revision||terrainRevision!=terrain.WeatherSurfaceRevision) {
                terrain.CollectWildlifeSpots(forest,spots);forestRevision=forest.Revision;terrainRevision=terrain.WeatherSurfaceRevision;
                obsolete.Clear();
                foreach(int id in poses.Keys) {
                    bool remains=false;foreach(var spot in spots)if(spot.TileId==id){remains=true;break;}
                    if(!remains)obsolete.Add(id);
                }
                foreach(int id in obsolete)poses.Remove(id);
            }
            if(spots.Count==0)return;
            model??=AnimatedGlbModel.Load(Path.Combine(AppContext.BaseDirectory,"Assets","Wildlife","elk.glb"));
            renderer??=new AnimatedModelRenderer(model);
            int outlineBudget=settings.Enhanced&&settings.WildlifeOutlines&&context.PixelsPerWorldUnit>=7&&RenderDevice.Visuals?.ShadowPass!=true
                ? settings.Quality==GraphicsQuality.High?8:settings.Quality==GraphicsQuality.Medium?4:0 :0;
            foreach(var spot in spots)
            {
                var activity=Activity(context.SimulationTimeSeconds,spot.Rank);
                float yaw=(spot.Rank%6283)*0.001f;
                Vector2 local=new Vector2(MathF.Sin(activity.Travel),1-MathF.Cos(activity.Travel))*0.35f;
                Vector3 position=spot.Position+new Vector3(local.X*MathF.Cos(yaw)-local.Y*MathF.Sin(yaw),local.X*MathF.Sin(yaw)+local.Y*MathF.Cos(yaw),0);
                yaw+=activity.Travel;
                if(!terrain.TryGetSurfaceZ(position.X,position.Y,out float z))continue;position.Z=z+0.02f;
                if(RenderDevice.Visuals?.ShadowPass!=true&&!RenderVisibility.SphereVisible(position+new Vector3(0,0,1.5f),3.5f,RenderDevice.ViewProjection))continue;
                if(!poses.TryGetValue(spot.TileId,out var pose))poses.Add(spot.TileId,pose=model.CreatePose());
                // Cross-fade per-node TRS; rigid antlers follow their animated parent too.
                pose.Evaluate("Stand_Eating_01",context.SimulationTimeSeconds+spot.Rank%100,
                    activity.Walking?"WalkSlow":null,activity.ClipTime,activity.Blend);
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
