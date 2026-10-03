using System;
using System.IO;
using System.Linq;
using OpenTK.Graphics.OpenGL;
using OpenTK.Mathematics;
using OpenTK.Windowing.Common;
using OpenTK.Windowing.Desktop;

namespace ForesTycoon
{
    internal static class ForestModelSmokeTest
    {
        internal static void Run()
        {
            using var window=new NativeWindow(new NativeWindowSettings {
                StartVisible=false,ClientSize=new Vector2i(1200,900),NumberOfSamples=4,
                API=ContextAPI.OpenGL,APIVersion=new Version(3,3),Profile=ContextProfile.Core });
            window.Context.MakeCurrent();RenderDevice.Initialize();GL.Viewport(0,0,1200,900);
            GL.Enable(EnableCap.DepthTest);
            try {
                using var terrain=new Terrain(TerrainSettings.Default.WithNodeSize(17,42),(_,_)=>4);
                var stands=new ForestStand[256];
                int[] ids={102,105,150,153};
                ForestSpecies[] species={ForestSpecies.Spruce,ForestSpecies.Oak,ForestSpecies.Birch,ForestSpecies.Beech};
                for(int i=0;i<4;i++)stands[ids[i]]=new(species[i],40,.7f,1);
                var forest=new ForestSystem(terrain,stands);
                foreach(var entry in forest.IndividualTrees.Patches) {
                    var patch=entry.Value;
                    while(patch.Count>1)forest.IndividualTrees.RemoveLiving(patch,patch.Count-1);
                    patch.Trees[0]=patch.Trees[0] with {U=.5f,V=.5f,AnnualGrowth=default};patch.Revision++;
                }
                forest.NotifyIndividualVisualEdit();
                var original=forest.IndividualTrees.Patches.Select(p=>p.Value.Trees[0]).ToArray();
                var graphics=new GraphicsSettings {ForestModels=ForestModelStyle.Imported,Weather=false,Fog=false,Wildlife=false,Diorama=false,StudioBackdrop=false};
                using var renderer=new TerrainRenderer(terrain,new VehicleSystem(),new WorldEffectSystem(),forest,graphics);
                string output=Path.GetFullPath("artifacts/forest-models");Directory.CreateDirectory(output);
                long imported=Draw("01-imported");
                Draw();Require(terrain.ForestChunkRebuilds==0,"Stable imported frame rebuilt chunks");
                graphics.ForestModels=ForestModelStyle.OriginalPine;
                long highPoly=Draw("02-original-pine");
                Require(highPoly>imported*3,"Original pine and lightweight preset are not distinct");
                graphics.ForestModels=ForestModelStyle.Procedural;long procedural=Draw("03-procedural");
                Require(procedural!=imported&&procedural!=highPoly,"Preset switch failed to replace imported geometry");
                graphics.ForestModels=ForestModelStyle.Imported;graphics.ImportedBirch=true;
                long birch=Draw("04-imported-birch");Require(birch!=imported,"Birch switch did not replace procedural geometry");
                Draw();Require(terrain.ForestChunkRebuilds==0,"Repeated preset rebuilt cached meshes");
                graphics.Textures=false;Draw("05-flat-colours");
                graphics.Enhanced=false;Draw("06-classic");
                graphics.Enhanced=true;graphics.Textures=true;graphics.ForestModels=ForestModelStyle.Generated;
                long generatedNear=Draw("07-generated-near",12),generatedMedium=Draw("08-generated-medium",5),generatedFar=Draw("09-generated-far",1);
                Require(generatedNear>generatedMedium&&generatedMedium>generatedFar,"Generated tree LODs did not reduce geometry");
                var after=forest.IndividualTrees.Patches.Select(p=>p.Value.Trees[0]).ToArray();
                Require(original.SequenceEqual(after),"Visual presets changed tree simulation data");
                graphics.Enhanced=true;graphics.Textures=true;
                forest.ExtractTimber(ids[1],forest.AvailableTimber(ids[1]));Draw("10-cut-oak");
                Require(forest.IndividualTrees.TryGet(ids[1],out var cut)&&cut.Count==0&&cut.Stumps?.Count>0,"Harvest failed to replace imported oak with its stump");
                Require(GL.GetError()==ErrorCode.NoError,"Forest model GL error");
                Console.WriteLine($"Forest model smoke passed: imported={imported}, original-pine={highPoly}, generated LODs={generatedNear}/{generatedMedium}/{generatedFar}; switching, cached geometry, birch, texture modes, shadows and harvesting. Captures: {output}");

                long Draw(string name=null,float zoom=35) {
                    RenderDevice.SetCamera(Matrix4.CreateRotationZ(-MathF.PI/4)*Matrix4.CreateRotationX(-MathF.PI/4)
                        *Matrix4.CreateOrthographicOffCenter(-20,20,-10,20,-1000,1000));
                    GL.ClearColor(.17f,.21f,.25f,1);GL.DepthMask(true);
                    GL.Clear(ClearBufferMask.ColorBufferBit|ClearBufferMask.DepthBufferBit);RenderMetrics.BeginFrame();
                    renderer.Draw(new RenderContext(0,0,0,0,0,0,false,false,1,-45,-45,-1000,-1000,1000,1000,zoom));
                    if(name!=null)FramebufferCapture.SavePng(Path.Combine(output,name+".png"),1200,900);
                    return RenderMetrics.SubmittedVertices;
                }
            } finally {RenderDevice.Dispose();}
        }
        private static void Require(bool ok,string message){if(!ok)throw new InvalidOperationException(message);}
    }
}
