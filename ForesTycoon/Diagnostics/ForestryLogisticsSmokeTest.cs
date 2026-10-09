using System;
using System.IO;
using System.Collections.Generic;
using OpenTK.Graphics.OpenGL;
using OpenTK.Mathematics;
using OpenTK.Windowing.Common;
using OpenTK.Windowing.Desktop;
namespace ForesTycoon
{
    internal static class ForestryLogisticsSmokeTest
    {
        internal static void Run()
        {
            using var window=new NativeWindow(new NativeWindowSettings{StartVisible=false,ClientSize=new Vector2i(1100,800),API=ContextAPI.OpenGL,APIVersion=new Version(3,3),Profile=ContextProfile.Core});
            window.Context.MakeCurrent();RenderDevice.Initialize();GL.Enable(EnableCap.DepthTest);GL.Viewport(0,0,1100,800);
            string output=Path.GetFullPath("artifacts/forestry-logistics");Directory.CreateDirectory(output);
            try {
                var map=new Terrain(TerrainSettings.Default.WithNodeSize(17,42),(_,_)=>4);
                try {
                    var snapshot=new ForestStand[256];snapshot[68]=new ForestStand(ForestSpecies.Oak,50,0.6f,1);
                    var forest=new ForestSystem(map.Map,snapshot);
                    forest.IndividualTrees.TryGet(68, out var patch);
                    float factor = MathF.Sqrt(60 / forest.AvailableTimber(68));
                    for (int i = 0; i < patch.Count; i++)
                        patch.Trees[i] = patch.Trees[i] with {
                            Dimensions = patch.Trees[i].Dimensions with { Diameter = patch.Trees[i].Dimensions.Diameter * factor },
                            AnnualGrowth = default
                        };
                    patch.Revision++; forest.NotifyIndividualVisualEdit();
                    var logistics=new ForestryLogistics(map.Map,forest);
                    Require(logistics.Designate(new[]{68})==1,"Forest designation failed.");
                    Require(forest.TryGetStand(68,out var original)&&Math.Abs(ForestSystem.TimberCubicMetres(original)-60)<0.001f,"Designation cut trees.");
                    Require(logistics.PlaceMill(150),"Sawmill placement failed: "+logistics.Status);
                    Require(!logistics.PlaceMill(150),"Overlapping mill was accepted.");
                    var inventory=new TimberCargoSystem();var vehicles=new VehicleSystem(inventory,route => VehicleRoadRoute.Create(map.Map, route));
                    vehicles.SourceLoader=logistics.Load;vehicles.DestinationReceiver=logistics.Deliver;
                    Require(!logistics.Dispatch(vehicles),"Disconnected truck route was accepted.");
                    map.Map.BuildRoadTilePath(69,149,RoadPaving.Macadam);
                    for(int id=69;id<149;id+=16)map.Map.WearRoad(id,0.35f+0.1f*((id/16)%5));
                    Require(logistics.Dispatch(vehicles),"Connected delivery route was not found: "+logistics.Status);
                    using var scene=new TerrainRenderer(map,vehicles,new WorldEffectSystem(),forest,new GraphicsSettings{Fog=false,Weather=false,Wildlife=false},logistics:logistics);
                    Capture(scene,"source-and-mill",0);
                    vehicles.Update(1);Require(logistics.Remaining>50&&logistics.Remaining<60,"Loading did not gradually extract timber.");
                    Capture(scene,"loading",1);
                    for(int tick=0;tick<18000;tick++){vehicles.Update(1.0/30);logistics.Update(1.0/30);}
                    Require(logistics.Remaining<0.001f,"Marked forest was not exhausted.");
                    Require(Math.Abs(logistics.Mills[0].Received-60)<0.01f,"Sawmill received wrong cubic metres.");
                    Require(Math.Abs(inventory.Delivered-60)<0.01f,"Delivery inventory lost timber.");
                    Require(!forest.TryGetStand(68,out _),"Depleted forest remained standing.");
                    Capture(scene,"delivered-and-depleted",601);
                    Console.WriteLine($"Logistics: 60 m³ transported from marked forest to placed mill; disconnected route and overlap rejected.");
                } finally {map.Dispose();}
                int sea=FishCase(true),pond=FishCase(false);
                Require(sea>pond&&pond is >=1 and <=2,"Fish population does not match water size.");
                using(var world=new GameWorld(TerrainSettings.Default.WithNodeSize(17,42))) {
                    int source=0;while(!world.TryGetForestStand(source,out _)&&source<255)source++;
                    world.QueueHarvestForest(source);world.ExecutePendingCommands();
                    for(int i=0;i<256&&world.Logistics.Mills.Count==0;i++){world.QueuePlaceSawmill(i);world.ExecutePendingCommands();}
                    Require(world.Logistics.Mills.Count==1,"Natural map had no usable sawmill placement.");
                    float volume=world.Logistics.Remaining;int building=world.Logistics.Mills[0].TileId;
                    for(int i=0;i<30;i++)world.Update(1.0/30);
                    volume=world.Logistics.Remaining; // Individual trees also grow between month boundaries.
                    using var save=new MemoryStream();world.Save(save);save.Position=0;world.Load(save);
                    Require(world.Logistics.Remaining==volume&&world.Logistics.Mills.Count==1&&world.Logistics.Mills[0].TileId==building,"Save/replay changed sources or buildings.");
                }
                Console.WriteLine($"Content smoke passed: sawmill model, progressive extraction, real road delivery, save/replay, sea {sea} fish vs pond {pond}, animated fish rendering.");
                Console.WriteLine("Captures: "+output);
                int FishCase(bool sea) {
                    var water=new Terrain(TerrainSettings.Default.WithNodeSize(17,71),(u,v)=>sea?-1:((u-8)*(u-8)+(v-8)*(v-8)<20?-1:4));
                    try {
                        using var scene=new TerrainRenderer(water,new VehicleSystem(),new WorldEffectSystem(),new ForestSystem(water.Map,new ForestStand[256]),new GraphicsSettings{Fog=false,Weather=false,Wildlife=false});
                        Capture(scene,sea?"sea-fish":"pond-fish",3);return scene.FishCount;
                    } finally {water.Dispose();}
                }
                void Capture(TerrainRenderer scene,string name,double time) {
                    Matrix4 camera=Matrix4.CreateRotationZ(-MathF.PI/4)*Matrix4.CreateRotationX(-MathF.PI/4)*Matrix4.CreateOrthographicOffCenter(-62,62,-42,48,-1000,1000);
                    RenderDevice.SetCamera(camera);GL.ClearColor(0.17f,0.21f,0.25f,1);GL.Clear(ClearBufferMask.ColorBufferBit|ClearBufferMask.DepthBufferBit);
                    scene.Draw(new RenderContext(time,0,0,time,(ulong)(time*30),1,false,false,1,-45,-45,-1000,-1000,1000,1000,8));GL.Finish();
                    Require(GL.GetError()==ErrorCode.NoError,"Content OpenGL error.");FramebufferCapture.SavePng(Path.Combine(output,name+".png"),1100,800);
                }
            } finally {RenderDevice.Dispose();}
        }
        private static void Require(bool value,string message){if(!value)throw new InvalidOperationException(message);}
    }
}
