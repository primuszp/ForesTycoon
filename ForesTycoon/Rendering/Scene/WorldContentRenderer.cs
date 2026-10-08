using System;
using System.Collections.Generic;
using System.Drawing;
using OpenTK.Graphics.OpenGL;
using OpenTK.Mathematics;
namespace ForesTycoon
{
    internal sealed class WorldContentRenderer : IDisposable
    {
        private ImportedSceneAsset mill,fish;
        private readonly List<FishHabitat> habitats=new();
        private ulong fishRevision=ulong.MaxValue;
        internal int FishCount=>habitats.Count;
        internal void DrawFish(Terrain terrain,GraphicsSettings settings,RenderContext context)
        {
            if(fishRevision!=terrain.WeatherSurfaceRevision){terrain.Map.CollectFishHabitats(habitats, node => terrain.NodeWaterZ(node, 0));fishRevision=terrain.WeatherSurfaceRevision;}
            if(habitats.Count==0)return;
            fish??=new ImportedSceneAsset("Assets/Wildlife/fish.glb",DioramaScale.FishLength,true);
            // All fish share one asset and draw consecutively: capture/restore GL state
            // once for the pass instead of querying it again for every instance.
            using var state=new RenderStateScope().Enable(EnableCap.DepthTest).Disable(EnableCap.CullFace);
            int index=0;
            foreach(var habitat in habitats){
                double phase=context.SimulationTimeSeconds*0.22+habitat.Seed%628*0.01;
                float radius=Math.Min(terrain.TileWidth,terrain.TileHeight)*0.22f;
                Vector3 position=habitat.Position+new Vector3(MathF.Cos((float)phase)*radius,MathF.Sin((float)phase)*radius,MathF.Sin((float)phase*1.3f)*0.05f);
                if(!RenderVisibility.SphereVisible(position,2,RenderDevice.ViewProjection)){index++;continue;}
                fish.Pose.Evaluate("ArmatureAction",context.SimulationTimeSeconds+index*0.71);
                fish.Draw(Matrix4.CreateRotationZ((float)phase+MathF.PI/2)*Matrix4.CreateTranslation(position),settings,state);index++;
            }
        }
        internal void DrawMills(Terrain terrain,ForestryLogistics logistics,GraphicsSettings settings)
        {
            if(logistics==null||logistics.Mills.Count==0)return;
            mill??=new ImportedSceneAsset("Assets/Buildings/sawmill.glb",DioramaScale.SawmillWidth(terrain.TileWidth,terrain.TileHeight),
                footprint:DioramaScale.SawmillFootprint(terrain.TileWidth,terrain.TileHeight));
              using var state=new RenderStateScope().Enable(EnableCap.DepthTest).Disable(EnableCap.CullFace);
            foreach(var building in logistics.Mills){
                  mill.Draw(Matrix4.CreateTranslation(building.Position),settings,state);
            }
        }
        internal void DrawBuildingPreview(Terrain terrain,ForestSystem forest,ForestryLogistics logistics,bool enabled)
        {
            if(!enabled||terrain.Map.HoveredTile==null)return;
            bool valid=terrain.Map.TryGetSawmillFootprint(terrain.Map.HoveredTile.Id,out int[] footprint,out _);
            foreach(int id in footprint)if(forest.TryGetStand(id,out _)||logistics?.ContainsTile(id)==true)valid=false;
            using var state=new RenderStateScope().AlphaBlend().DepthWrite(false);
            DynamicPrimitiveBatch.Draw(PrimitiveType.LineLoop,()=>{
                DynamicPrimitiveBatch.Color4(valid?Color.FromArgb(120,245,120):Color.FromArgb(245,90,70));
                var tile=terrain.Map.HoveredTile;float width=terrain.TileWidth*2,height=terrain.TileHeight*2;
                DynamicPrimitiveBatch.Vertex3(tile.W.xPos,tile.W.yPos,tile.W.zPos);
                DynamicPrimitiveBatch.Vertex3(tile.W.xPos+width,tile.W.yPos,tile.W.zPos);
                DynamicPrimitiveBatch.Vertex3(tile.W.xPos+width,tile.W.yPos+height,tile.W.zPos);
                DynamicPrimitiveBatch.Vertex3(tile.W.xPos,tile.W.yPos+height,tile.W.zPos);
            });
        }
        public void Dispose(){fish?.Dispose();mill?.Dispose();}
    }
}
