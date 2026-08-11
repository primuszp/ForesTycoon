using System;

namespace ForesTycoon
{
    /// <summary>
    /// Owns terrain-specific GPU draw ordering and transient render state.
    /// Terrain remains the authoritative simulation model; this class is its rendering adapter.
    /// </summary>
    sealed class TerrainRenderer : IDisposable
    {
        private readonly Terrain terrain;
        private readonly VehicleSystem vehicles;
        private readonly WorldEffectSystem effects;
        private readonly ForestSystem forest;
        private readonly RenderPipeline pipeline = new RenderPipeline();
        private RenderStateScope decalState;

        public TerrainRenderer(Terrain terrain, VehicleSystem vehicles, WorldEffectSystem effects, ForestSystem forest)
        {
            this.terrain = terrain ?? throw new ArgumentNullException(nameof(terrain));
            this.vehicles = vehicles ?? throw new ArgumentNullException(nameof(vehicles));
            this.effects = effects ?? throw new ArgumentNullException(nameof(effects));
            this.forest = forest ?? throw new ArgumentNullException(nameof(forest));
            RegisterPasses();
        }

        public void Draw(RenderContext context)
        {
            terrain.UpdateVisibleTiles(context);
            pipeline.Render(context);
        }

        private void RegisterPasses()
        {
            pipeline.Add(RenderLayer.TerrainBase, "terrain-base", _ => terrain.DrawTerrainBase());
            pipeline.Add(RenderLayer.TerrainSkirts, "terrain-skirts", _ => terrain.DrawSkirts());
            pipeline.Add(RenderLayer.WaterSurface, "water-surface", terrain.DrawWater);
            pipeline.Add(RenderLayer.WaterWalls, "water-walls", terrain.DrawWaterWalls);
            pipeline.Add(RenderLayer.RiverFallback, "river-fallback", terrain.DrawRivers);
            pipeline.Add(RenderLayer.Foundations, "road-foundations", _ => terrain.DrawRoadFoundations());
            pipeline.Add(RenderLayer.DecalBegin, "decal-state-begin", _ => BeginDecals());
            pipeline.Add(RenderLayer.Grid, "terrain-grid", _ => terrain.DrawTerrainDecals());
            pipeline.Add(RenderLayer.Roads, "roads", _ => terrain.DrawRoads());
            pipeline.Add(RenderLayer.HoverOverlay, "hover-overlay", context =>
            {
                if (context.ShowTileHighlight) terrain.DrawHoveredTile();
            });
            pipeline.Add(RenderLayer.DecalEnd, "decal-state-end", _ => EndDecals());
            pipeline.Add(RenderLayer.Props, "props", _ => terrain.DrawTrees(forest));
            pipeline.Add(RenderLayer.Vehicles, "vehicles", context =>
                VehicleRenderer.Draw(vehicles, terrain, context.InterpolationAlpha));
            pipeline.Add(RenderLayer.Effects, "world-effects", context =>
                EffectRenderer.Draw(effects, context.InterpolationAlpha));
            pipeline.Add(RenderLayer.DebugOverlay, "debug-overlay", context =>
            {
                if (context.ShowNodeMarker) terrain.DrawNodeMarker(context.NodeMarkerRadius);
            });
        }

        private void BeginDecals()
        {
            decalState?.Dispose();
            decalState = new RenderStateScope().Disable(OpenTK.Graphics.OpenGL.EnableCap.DepthTest).DepthWrite(false);
        }

        private void EndDecals()
        {
            decalState?.Dispose();
            decalState = null;
        }

        public void Dispose() => EndDecals();
    }
}
