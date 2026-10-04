using System;
using System.IO;
using OpenTK.Graphics.OpenGL;
using OpenTK.Mathematics;
using OpenTK.Windowing.Common;
using OpenTK.Windowing.Desktop;
namespace ForesTycoon
{
    internal static class WildlifeSmokeTest
    {
        internal static void Run()
        {
            using var window=new NativeWindow(new NativeWindowSettings{StartVisible=false,ClientSize=new Vector2i(1000,750),
                NumberOfSamples=4,API=ContextAPI.OpenGL,APIVersion=new Version(3,3),Profile=ContextProfile.Core});
            window.Context.MakeCurrent();RenderDevice.Initialize();GL.Enable(EnableCap.DepthTest);GL.Viewport(0,0,1000,750);
            string output=Path.GetFullPath("artifacts/wildlife");Directory.CreateDirectory(output);
            try {
                var model=AnimatedGlbModel.Load(Path.Combine(AppContext.BaseDirectory,"Assets","Wildlife","elk.glb"));
                using(var mesh=new AnimatedModelRenderer(model)) {
                    var settings=new GraphicsSettings{Weather=false,Shadows=false};
                    var pose=model.CreatePose();
                    Matrix4 camera=Matrix4.CreateTranslation(0,0,-1.4f)*Matrix4.CreateRotationZ(-MathF.PI/4)*
                        Matrix4.CreateRotationX(-MathF.PI/5)*Matrix4.CreateOrthographicOffCenter(-3.8f,3.8f,-2.85f,2.85f,-100,100);
                    byte[] first=Frame("Stand_Eating_01",0,"elk-grazing-0");
                    byte[] moving=Frame("Stand_Eating_01",2,"elk-grazing-2");
                    if(first.AsSpan().SequenceEqual(moving))throw new InvalidOperationException("GPU skinning did not animate.");
                    if(!moving.AsSpan().SequenceEqual(Frame("Stand_Eating_01",2,"elk-paused")))throw new InvalidOperationException("Paused animation changed.");
                    settings.WildlifeOutlines=false;
                    byte[] plain=Frame("Stand_Eating_01",2,"elk-no-outline");
                    if(moving.AsSpan().SequenceEqual(plain))throw new InvalidOperationException("Deer contour was invisible.");
                    settings.WildlifeOutlines=true;
                    if(!moving.AsSpan().SequenceEqual(Frame("Stand_Eating_01",2,"elk-outline-restored")))throw new InvalidOperationException("Deer contour toggle did not restore frame.");
                    Frame("Stand_Eating_01",5,"elk-grazing-5");Frame("WalkSlow",0.5,"elk-walk");
                    double walkDuration=model.Clips["WalkSlow"].Duration;
                    Frame("WalkSlow",walkDuration-0.001,"elk-walk-loop-before");
                    Frame("WalkSlow",walkDuration+0.001,"elk-walk-loop-after");
                    settings.Textures=false;Frame("Stand_Eating_01",2,"elk-untextured");
                    settings.Enhanced=false;Frame("Stand_Eating_01",2,"elk-original-mode");
                    byte[] Frame(string clip,double time,string name) {
                        pose.Evaluate(clip,time,inPlaceRoot:Array.FindIndex(model.Nodes,node=>node.Name=="RigRoot_01"));RenderDevice.SetCamera(camera);
                        GL.ClearColor(0.17f,0.21f,0.25f,1);GL.Clear(ClearBufferMask.ColorBufferBit|ClearBufferMask.DepthBufferBit);
                        mesh.Draw(pose,WildlifeRenderer.Axis*Matrix4.CreateScale(1.3f),settings,0.7f*7.6f/1000);GL.Finish();
                        if(GL.GetError()!=ErrorCode.NoError)throw new InvalidOperationException("Animated renderer GL error.");
                        FramebufferCapture.SavePng(Path.Combine(output,name+".png"),1000,750);
                        var pixels=new byte[1000*750*4];GL.ReadPixels(0,0,1000,750,PixelFormat.Rgba,PixelType.UnsignedByte,pixels);return pixels;
                    }
                }
                var terrain=new Terrain(TerrainSettings.Default.WithNodeSize(33,8127).WithForestPattern(ForestPattern.LargeMixed),(_,_)=>4);
                try {
                    var forest=new ForestSystem(terrain);var settings=new GraphicsSettings{Weather=false,Fog=false};
                    using var scene=new TerrainRenderer(terrain,new VehicleSystem(),new WorldEffectSystem(),forest,settings);
                    var spots=new System.Collections.Generic.List<WildlifeSpot>();terrain.CollectWildlifeSpots(forest,spots);
                    VerifyHabitat();
                    if(spots.Count==0)throw new InvalidOperationException("No deer habitat found.");
                    var animals=new WildlifeSystem();
                    animals.Update(0,terrain,forest,null);
                    void VerifyHabitat()
                    {
                        var actual = new System.Collections.Generic.List<WildlifeSpot>();
                        var reference = new System.Collections.Generic.List<WildlifeSpot>();
                        terrain.CollectWildlifeSpots(forest, actual);
                        terrain.DiagnosticCollectWildlifeSpotsReference(forest, reference);
                        if (!System.Linq.Enumerable.SequenceEqual(actual, reference))
                            throw new InvalidOperationException("Optimized habitat differs from original ranked positions.");
                        terrain.CollectWildlifeSpots(forest, actual, stopAfterFirst: true);
                        if ((actual.Count > 0) != (reference.Count > 0))
                            throw new InvalidOperationException("Early habitat existence check differs from full selection.");
                    }
                    Vector3 initial=animals.Animals[0].Position;
                    bool grazed=false,walked=false;
                    float travelled=0,maxDisplacement=0;
                    for(int tick=0;tick<5400;tick++) {
                        Vector3 previous=animals.Animals[0].Position;
                        animals.Update(1.0/30,terrain,forest,null);
                        var animal=animals.Animals[0];
                        if((animal.Position-previous).Length>WildlifeSystem.WalkingSpeed/30+0.001f)throw new InvalidOperationException("Wildlife teleported.");
                        float step=(animal.Position-previous).Length;
                        travelled+=step;maxDisplacement=Math.Max(maxDisplacement,(animal.Position-initial).Length);
                        if(step>0.001f) {
                            float turn=MathF.Abs(MathF.Atan2(MathF.Sin(animal.Yaw-animal.PreviousYaw),MathF.Cos(animal.Yaw-animal.PreviousYaw)));
                            if(turn>step/WildlifeSystem.TurningRadius+0.0001f)throw new InvalidOperationException("Wildlife turned too tightly.");
                        }
                        walked|=(animal.Position-initial).Length>1;
                        grazed|=animal.Blend<0.1f;
                    }
                    if(!walked||!grazed)throw new InvalidOperationException("Wildlife failed to roam and graze.");
                    if(travelled<25||maxDisplacement<10)throw new InvalidOperationException($"Wildlife roamed too little: {travelled:F1} m, range {maxDisplacement:F1} m.");
                    Console.WriteLine($"Wildlife roaming: {travelled:F1} m travelled, {maxDisplacement:F1} m range; turning radius at least {WildlifeSystem.TurningRadius:F1} m.");
                    Vector3 paused=animals.Animals[0].Position;
                    animals.Update(0,terrain,forest,null);
                    if(animals.Animals[0].Position!=paused)throw new InvalidOperationException("Paused wildlife moved.");
                    Vector3 target=spots[0].Position;
                    Matrix4 camera=Matrix4.CreateTranslation(-target-new Vector3(0,0,1))*Matrix4.CreateRotationZ(-MathF.PI/4)*
                        Matrix4.CreateRotationX(-MathF.PI/4)*Matrix4.CreateOrthographicOffCenter(-8,8,-6,6,-1000,1000);
                    RenderDevice.SetCamera(camera);GL.Clear(ClearBufferMask.ColorBufferBit|ClearBufferMask.DepthBufferBit);
                    scene.Draw(new RenderContext(5,0,0,5,0,0,false,false,1,-45,-45,-100,-100,100,100,62.5f));GL.Finish();
                    if(scene.WildlifeCount<1||scene.WildlifeCount>16||GL.GetError()!=ErrorCode.NoError)throw new InvalidOperationException("Forest wildlife render failed.");
                    FramebufferCapture.SavePng(Path.Combine(output,"elk-in-forest.png"),1000,750);
                    var survivors=animals.Animals.ToArray();
                    forest.Update(5);
                    VerifyHabitat();
                    animals.Update(0,terrain,forest,null);
                    if(!System.Linq.Enumerable.SequenceEqual(survivors,animals.Animals))
                        throw new InvalidOperationException("Forest month replaced existing wildlife.");
                    foreach(var spot in spots)forest.Harvest(spot.TileId,out _);
                    VerifyHabitat();
                    forest.Clear();scene.Draw(new RenderContext(5,0,1,5,0,0,false,false,1,-45,-45,-100,-100,100,100,62.5f));
                    if(scene.WildlifeCount!=0)throw new InvalidOperationException("Deer remained after removing forest habitat.");
                    VerifyHabitat();
                    animals.Update(0,terrain,forest,null);
                    if(animals.Animals.Count!=0)throw new InvalidOperationException("Wildlife habitat existence check missed an empty forest.");
                    forest.Plant(spots[0].TileId,ForestSpecies.Oak);
                    VerifyHabitat();
                }finally{terrain.Dispose();}
                Console.WriteLine("Wildlife smoke passed: GPU skinning, contour on/off/restoration, textures, antlers, walk/eating clips, pause, original mode, forest placement/shadows, habitat removal and exact habitat reference after growth/harvest/clear/replant.");
                Console.WriteLine("Captures: "+output);
            }finally{RenderDevice.Dispose();}
        }
    }
}
