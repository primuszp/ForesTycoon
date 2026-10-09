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
        private readonly GraphicsSettings graphics;
        private readonly WeatherVisualState weather = new WeatherVisualState();
        private readonly EnvironmentSystem environment;
        private readonly SurfaceVisualRenderer surfaces;
        private readonly ForestryLogistics logistics;
        private readonly WorldContentRenderer content=new WorldContentRenderer();
        private readonly ForestMachineRenderer machines=new ForestMachineRenderer();
        internal int FishCount=>content.FishCount;
        private readonly ForestWeatherRenderer forestWeather = new ForestWeatherRenderer();
        private readonly CloudRenderer clouds = new CloudRenderer();
        private readonly WildlifeRenderer wildlife;
        private readonly WildlifeSystem wildlifeSimulation;
        private readonly bool ownsWildlife;
        private double wildlifeTime;
        internal int WildlifeCount => graphics.Wildlife ? wildlife.Count : 0;
        internal bool TryGetWildlifePosition(out OpenTK.Mathematics.Vector3 position) => wildlife.TryGetPosition(out position);
        private readonly WeatherRenderer precipitation = new WeatherRenderer();
        private readonly TerrainWeatherSurface weatherSurface;

        public TerrainRenderer(Terrain terrain, VehicleSystem vehicles, WorldEffectSystem effects, ForestSystem forest, GraphicsSettings graphics = null,EnvironmentSystem environment=null, WildlifeSystem wildlifeSystem=null,ForestryLogistics logistics=null)
        {
            this.environment=environment;
            this.logistics=logistics;
            ownsWildlife = wildlifeSystem == null;
            wildlifeSimulation = wildlifeSystem ?? new WildlifeSystem();
            wildlife = new WildlifeRenderer(wildlifeSimulation);
            this.terrain = terrain ?? throw new ArgumentNullException(nameof(terrain));
            this.vehicles = vehicles ?? throw new ArgumentNullException(nameof(vehicles));
            this.effects = effects ?? throw new ArgumentNullException(nameof(effects));
            this.forest = forest ?? throw new ArgumentNullException(nameof(forest));
            this.graphics = graphics ?? new GraphicsSettings { Enhanced = false };
            weatherSurface = new TerrainWeatherSurface(terrain, forest);
            surfaces = new SurfaceVisualRenderer(this.graphics, weather,environment,terrain);
            terrain.WarmStaticGeometry();
            terrain.WarmIndividualForest(forest, this.graphics);
            RegisterPasses();
        }

        public void Draw(RenderContext context)
        {
            RenderPipeline.PassProbe?.Invoke("frame-preparation", true);
            try
            {
                if (ownsWildlife) { wildlifeSimulation.Update(Math.Max(0, context.SimulationTimeSeconds-wildlifeTime), terrain.Map, forest, environment); wildlifeTime=context.SimulationTimeSeconds; }
                terrain.UpdateVisibleTiles(context);
                if(environment!=null && graphics.AutomaticWeather)weather.Update(environment,graphics,context.SimulationTimeSeconds);
                else weather.Update(context.SimulationTimeSeconds, graphics);
                surfaces.BeginFrame();
                VehicleRenderer.BeginFrame(context, graphics);
            }
            finally { RenderPipeline.PassProbe?.Invoke("frame-preparation", false); }
            var previous = RenderDevice.Visuals;
            RenderDevice.Visuals = surfaces;
            try
            {
                bool shadowDrawn = false;
                RenderPipeline.PassProbe?.Invoke("shadow-map", true);
                try
                {
                    surfaces.RenderShadows(terrain, () =>

                    {
                        shadowDrawn = true;
                        // Expanded camera footprint includes nearby off-screen shadow casters.
                        var shadowContext = new RenderContext(context.TotalTimeSeconds, context.DeltaTimeSeconds,
                            context.FrameIndex, context.SimulationTimeSeconds, context.SimulationTick, context.InterpolationAlpha,
                            false, false, 1, context.CameraTilt, context.CameraYaw,
                            context.ViewMinX - 40, context.ViewMinY - 40, context.ViewMaxX + 40, context.ViewMaxY + 40,
                            context.PixelsPerWorldUnit);
                        terrain.UpdateVisibleTiles(shadowContext);
                        terrain.DrawTerrainBase();
                        terrain.DrawTrees(forest, shadowContext, graphics);
                        content.DrawMills(terrain,logistics,graphics);
                        wildlife.Draw(terrain, forest, graphics, context);
                        VehicleRenderer.Draw(vehicles, terrain, context.InterpolationAlpha);
                    });
                }
                finally { RenderPipeline.PassProbe?.Invoke("shadow-map", false); }
                if (shadowDrawn) terrain.UpdateVisibleTiles(context);
                RenderPipeline.PassProbe?.Invoke("clouds", true);
                try
                {
                    if(graphics.Enhanced && graphics.Weather && graphics.Clouds) clouds.Draw(weatherSurface, weather, graphics);
                }
                finally { RenderPipeline.PassProbe?.Invoke("clouds", false); }
                pipeline.Render(context);
            }
            finally { EndDecals(); RenderDevice.Visuals = previous; }
        }

        private void RegisterPasses()
        {
            pipeline.Add(RenderLayer.TerrainBase, "terrain-base", _ => DrawSurface(SurfaceKind.Ground, terrain.DrawTerrainBase));
            pipeline.Add(RenderLayer.TerrainSkirts, "terrain-skirts", _ => DrawSurface(SurfaceKind.Skirt, terrain.DrawSkirts));
            pipeline.Add(RenderLayer.Fish,"fish", context=>content.DrawFish(terrain,graphics,context));
            pipeline.Add(RenderLayer.WaterSurface, "water-surface", context => DrawSurface(SurfaceKind.Water, () => terrain.DrawWater(context)));
            pipeline.Add(RenderLayer.WaterWalls, "water-walls", context => DrawSurface(SurfaceKind.Water, () => terrain.DrawWaterWalls(context)));
            pipeline.Add(RenderLayer.RiverFallback, "river-fallback", context => DrawSurface(SurfaceKind.Water, () => terrain.DrawRivers(context)));
            pipeline.Add(RenderLayer.Foundations, "road-foundations", _ => DrawSurface(SurfaceKind.Road, terrain.DrawRoadFoundations));
            pipeline.Add(RenderLayer.DecalBegin, "decal-state-begin", _ => BeginDecals());
            pipeline.Add(RenderLayer.Grid, "terrain-grid", context => { if (!graphics.Enhanced || graphics.ShowGrid) DrawSurface(SurfaceKind.Plain, () => terrain.DrawTerrainDecals(context)); });
            pipeline.Add(RenderLayer.Roads, "roads", _ => DrawSurface(SurfaceKind.Road, terrain.DrawRoads));
            pipeline.Add(RenderLayer.Roads, "skid-trails", _ => DrawSurface(SurfaceKind.SkidTrail, terrain.DrawSkidTrails));
            pipeline.Add(RenderLayer.HoverOverlay, "hover-overlay", context =>
            {
                surfaces.Kind = SurfaceKind.Plain;
                if (context.ShowTileHighlight) terrain.DrawHoveredTile();
            });
            pipeline.Add(RenderLayer.ForestryPreview, "forestry-preview", _ => DrawSurface(SurfaceKind.Plain,()=> {
                terrain.DrawManagementOverlay();
                terrain.DrawPlantations(forest,graphics);
                terrain.DrawHarvestSites(logistics);terrain.DrawForestryPreview();
                content.DrawBuildingPreview(terrain,forest,logistics,graphics.SawmillPreview);
            }));
            pipeline.Add(RenderLayer.DecalEnd, "decal-state-end", _ => EndDecals());
            pipeline.Add(RenderLayer.Props, "props", context => terrain.DrawTrees(forest, context, graphics));
            pipeline.Add(RenderLayer.Buildings,"buildings", _=>content.DrawMills(terrain,logistics,graphics));
            pipeline.Add(RenderLayer.Wildlife, "wildlife", context => wildlife.Draw(terrain, forest, graphics, context));
            pipeline.Add(RenderLayer.Vehicles, "vehicles", context =>
                DrawSurface(SurfaceKind.Vehicle, () => VehicleRenderer.Draw(vehicles, terrain, context.InterpolationAlpha)));
            pipeline.Add(RenderLayer.Vehicles, "forest-machines", context =>
                DrawSurface(SurfaceKind.Vehicle, () => machines.Draw(terrain, logistics, graphics, (float)context.InterpolationAlpha)));
            pipeline.Add(RenderLayer.Effects, "world-effects", context =>
                DrawSurface(SurfaceKind.Plain, () => EffectRenderer.Draw(effects, context.InterpolationAlpha)));
            pipeline.Add(RenderLayer.Weather, "weather", context =>
            {
                if (graphics.Enhanced && graphics.Weather)
                {
                    forestWeather.Draw(terrain, forest, weather, graphics, context,environment);
                    precipitation.Draw(weatherSurface, weather, context, graphics);
                }
            });
            pipeline.Add(RenderLayer.DebugOverlay, "debug-overlay", context =>
            {
                surfaces.Kind = SurfaceKind.Plain;
                if (context.ShowNodeMarker) terrain.DrawNodeMarker(context.NodeMarkerRadius);
            });
        }

        private void BeginDecals()
        {
            decalState?.Dispose();
            decalState = RenderDevice.CreateStateScope().Disable(RenderCapability.DepthTest).DepthWrite(false);
        }

        private void EndDecals()
        {
            decalState?.Dispose();
            decalState = null;
        }

        private void DrawSurface(SurfaceKind kind, Action draw) { surfaces.Kind = kind; draw(); }

        public void Dispose()
        {
            EndDecals(); content.Dispose(); machines.Dispose(); wildlife.Dispose(); forestWeather.Dispose(); clouds.Dispose(); precipitation.Dispose(); surfaces.Dispose();
        }
    }
}
