using System;
using System.Collections.Generic;
using System.IO;
using OpenTK.Mathematics;

namespace ForesTycoon
{
    /// <summary>
    /// Owns game-state lifetime and is the boundary used by input, simulation and rendering.
    /// Viewport code must not own or replace individual world systems directly.
    /// </summary>
    sealed class GameWorld : IDisposable, IWorldCommandTarget, IWorldInteractionTarget
    {
        private Terrain terrain;
        private TerrainRenderer terrainRenderer;
        private readonly WorldCommandQueue commands = new WorldCommandQueue();
        private readonly WorldSystemCollection systems = new WorldSystemCollection();
        private readonly BackgroundJobScheduler backgroundJobs = new BackgroundJobScheduler();
        private readonly List<WorldCommandRecord> commandJournal = new List<WorldCommandRecord>();
        private readonly VehicleSystem vehicles;
        private readonly WorldEffectSystem effects;
        private readonly ForestSystem forest;
        private readonly TimberCargoSystem timberCargo;
        private ulong worldTick;
        private ForestryActionResult lastForestryAction;
        private ForestryAreaSummary lastForestryArea;

        public GameWorld(TerrainSettings settings)
        {
            terrain = new Terrain(settings ?? throw new ArgumentNullException(nameof(settings)));
            forest = systems.Add(new ForestSystem(terrain));
            timberCargo = systems.Add(new TimberCargoSystem());
            vehicles = systems.Add(new VehicleSystem(timberCargo));
            effects = systems.Add(new WorldEffectSystem());
            terrainRenderer = new TerrainRenderer(terrain, vehicles, effects, forest);
        }

        public Tile HoveredTile => terrain.HoveredTile;
        public int HoveredTileId => terrain.HoveredTile?.Id ?? -1;
        public int SelectedNodeId => terrain.SelectedNodeId;
        public int RoadCount => terrain.RoadCount;
        public int RoadPreviewCount => terrain.RoadPreviewCount;
        public int VehicleCount => vehicles.Count;
        public ForestStatistics ForestStatistics => forest.Statistics;
        public float TimberStockpile => timberCargo.Available;
        public float DeliveredTimber => timberCargo.Delivered;
        public ForestryActionResult LastForestryAction => lastForestryAction;
        public ForestryAreaSummary LastForestryArea => lastForestryArea;
        public bool TryGetForestStand(int tileId, out ForestStand stand) => forest.TryGetStand(tileId, out stand);
        public int VisibleChunkCount => terrain.VisibleChunkCount;
        public int TotalChunkCount => terrain.TotalChunkCount;
        public int TileWidth => terrain.TileWidth;
        public int TileHeight => terrain.TileHeight;
        public int MapTileColumns => terrain.Settings.TileColumns;
        public ulong SimulationTick => worldTick;

        public void Update(double fixedDeltaSeconds)
        {
            systems.Update(fixedDeltaSeconds);
            worldTick++;
        }

        public int ExecutePendingCommands()
        {
            backgroundJobs.PublishCompleted();
            return commands.ExecutePending(this);
        }
        public void QueueElevationEdit(int nodeId, int delta, int radius, int strength) =>
            Enqueue(new EditElevationCommand(nodeId, delta, radius, strength));
        public void QueueRoadPath(int startTileId, int endTileId, bool remove) =>
            Enqueue(new RoadPathCommand(startTileId, endTileId, remove));
        public void QueueSpawnVehicle() => Enqueue(new SpawnVehicleCommand());
        public void QueuePlantForest(int tileId, ForestSpecies species) =>
            Enqueue(new PlantForestCommand(tileId, species));
        public void QueueHarvestForest(int tileId) => Enqueue(new HarvestForestCommand(tileId));
        public void QueuePlantForestArea(int startTileId, int endTileId, ForestSpecies species) =>
            Enqueue(new PlantForestAreaCommand(startTileId, endTileId, species));
        public void QueueHarvestForestArea(int startTileId, int endTileId) =>
            Enqueue(new HarvestForestAreaCommand(startTileId, endTileId));

        public void SetForestryPreview(int startTileId, int endTileId, bool removal) =>
            terrain.SetForestryPreview(startTileId, endTileId, removal);
        public void ClearForestryPreview() => terrain.ClearForestryPreview();
        public int ForestryPreviewCount => terrain.ForestryPreviewCount;

        private void Enqueue(IWorldCommand command)
        {
            commandJournal.Add(command.ToRecord(worldTick));
            commands.Enqueue(command);
        }

        void IWorldCommandTarget.ExecuteElevationEdit(int nodeId, int delta, int radius, int strength)
        {
            terrain.EditElevationAtNode(nodeId, delta, radius, strength);
            forest.RefreshHabitat();
            if (terrain.TryGetNodePosition(nodeId, out Vector3 position))
                effects.Spawn(WorldEffectKind.TerrainChanged, position);
        }

        void IWorldCommandTarget.ExecuteRoadPath(int startTileId, int endTileId, bool remove)
        {
            if (remove) terrain.RemoveRoadTilePath(startTileId, endTileId);
            else terrain.BuildRoadTilePath(startTileId, endTileId);
            forest.RefreshHabitat();
            if (remove) vehicles.RemoveInvalidRoutes(terrain.IsRoadTile);
            if (terrain.TryGetRoadTileCenter(endTileId, out Vector3 position))
                effects.Spawn(WorldEffectKind.RoadChanged, position);
        }

        void IWorldCommandTarget.ExecuteSpawnVehicle()
        {
            int[] route = terrain.FindDemoRoadRoute();
            if (route.Length >= 2)
            {
                vehicles.Spawn(route);
                if (terrain.TryGetRoadTileCenter(route[0], out Vector3 position))
                    effects.Spawn(WorldEffectKind.VehicleSpawned, position);
            }
        }

        void IWorldCommandTarget.ExecutePlantForest(int tileId, ForestSpecies species)
        {
            lastForestryAction = forest.Plant(tileId, species);
            if (terrain.TryGetTileCenter(tileId, out Vector3 position))
                effects.Spawn(lastForestryAction == ForestryActionResult.Planted
                    ? WorldEffectKind.TreePlanted
                    : WorldEffectKind.ForestryRejected, position);
        }

        void IWorldCommandTarget.ExecuteHarvestForest(int tileId)
        {
            lastForestryAction = forest.Harvest(tileId, out ForestHarvest harvest);
            if (lastForestryAction != ForestryActionResult.Harvested)
            {
                if (terrain.TryGetTileCenter(tileId, out Vector3 rejectedPosition))
                    effects.Spawn(WorldEffectKind.ForestryRejected, rejectedPosition);
                return;
            }
            timberCargo.AddHarvested(harvest.TimberVolume);
            if (terrain.TryGetTileCenter(tileId, out Vector3 position))
                effects.Spawn(WorldEffectKind.ForestHarvested, position);
        }

        void IWorldCommandTarget.ExecutePlantForestArea(int startTileId, int endTileId, ForestSpecies species)
        {
            Span<int> tileIds = stackalloc int[Terrain.MaximumAreaTiles];
            int count = terrain.GetTileRectangle(startTileId, endTileId, tileIds);

            int planted = 0;
            for (int i = 0; i < count; i++)
            {
                lastForestryAction = forest.Plant(tileIds[i], species);
                if (lastForestryAction == ForestryActionResult.Planted) planted++;
            }

            lastForestryArea = new ForestryAreaSummary(count, planted, 0f);
            SpawnAreaEffect(startTileId, endTileId,
                planted > 0 ? WorldEffectKind.TreePlanted : WorldEffectKind.ForestryRejected);
        }

        void IWorldCommandTarget.ExecuteHarvestForestArea(int startTileId, int endTileId)
        {
            Span<int> tileIds = stackalloc int[Terrain.MaximumAreaTiles];
            int count = terrain.GetTileRectangle(startTileId, endTileId, tileIds);

            int felled = 0;
            float volume = 0f;
            for (int i = 0; i < count; i++)
            {
                lastForestryAction = forest.Harvest(tileIds[i], out ForestHarvest harvest);
                if (lastForestryAction != ForestryActionResult.Harvested) continue;
                felled++;
                volume += harvest.TimberVolume;
                timberCargo.AddHarvested(harvest.TimberVolume);
            }

            lastForestryArea = new ForestryAreaSummary(count, felled, volume);
            SpawnAreaEffect(startTileId, endTileId,
                felled > 0 ? WorldEffectKind.ForestHarvested : WorldEffectKind.ForestryRejected);
        }

        /// <summary>
        /// One effect per area gesture. Spawning per tile would bury the effect system under a
        /// single drag and tell the player nothing a marker at the corners does not.
        /// </summary>
        private void SpawnAreaEffect(int startTileId, int endTileId, WorldEffectKind kind)
        {
            if (terrain.TryGetTileCenter(startTileId, out Vector3 start))
                effects.Spawn(kind, start);
            if (endTileId != startTileId && terrain.TryGetTileCenter(endTileId, out Vector3 end))
                effects.Spawn(kind, end);
        }

        public void Draw(RenderContext context)
        {
            terrainRenderer.Draw(context);
        }
        public void GetWorldBounds(out Vector3 min, out Vector3 max) => terrain.GetWorldBounds(out min, out max);
        public bool TryGetSurfaceZ(double x, double y, out float z) => terrain.TryGetSurfaceZ(x, y, out z);
        public bool TryRaycastTerrain(Vector3 rayNear, Vector3 rayFar, out Vector3 hit) =>
            terrain.TryRaycast(rayNear, rayFar, out hit);
        public bool SearchScreenPoint(double x, double y, double radius, double[] model, double[] projection, int[] viewport) =>
            terrain.SearchScreenPoint(x, y, radius, model, projection, viewport);
        public bool SearchTile(double x, double y) => terrain.SearchTile(x, y);
        public void ClearHover() => terrain.ClearHover();
        public void ClearTileHover() => terrain.ClearTileHover();
        public void SetRoadPreview(int startTileId, int endTileId, bool remove) =>
            terrain.SetRoadPreview(startTileId, endTileId, remove);
        public void ClearRoadPreview() => terrain.ClearRoadPreview();

        public void Regenerate(TerrainSettings settings)
        {
            commands.Clear();
            commandJournal.Clear();
            systems.Clear();
            worldTick = 0;
            lastForestryAction = ForestryActionResult.None;
            ReplaceTerrain(settings);
        }

        public void Save(Stream destination, double tickRate = 30.0)
        {
            WorldSaveSerializer.Write(destination, new WorldSaveData
            {
                TickRate = tickRate,
                Tick = worldTick,
                Terrain = TerrainSettingsData.From(terrain.Settings),
                Commands = new List<WorldCommandRecord>(commandJournal)
            });
        }

        public void Load(Stream source)
        {
            WorldSaveData save = WorldSaveSerializer.Read(source);
            commands.Clear();
            commandJournal.Clear();
            systems.Clear();
            worldTick = 0;
            lastForestryAction = ForestryActionResult.None;
            ReplaceTerrain(save.Terrain.ToSettings());

            int commandIndex = 0;
            ulong previousTick = 0;
            for (int i = 0; i < save.Commands.Count; i++)
            {
                WorldCommandRecord record = save.Commands[i];
                if (record.Tick > save.Tick || (i > 0 && record.Tick < previousTick))
                    throw new InvalidDataException("Save commands are not in deterministic tick order.");
                previousTick = record.Tick;
            }

            double fixedDelta = 1.0 / save.TickRate;
            for (ulong tick = 0; tick <= save.Tick; tick++)
            {
                while (commandIndex < save.Commands.Count && save.Commands[commandIndex].Tick == tick)
                    commands.Enqueue(WorldCommandFactory.Create(save.Commands[commandIndex++]));
                commands.ExecutePending(this);
                if (tick < save.Tick) Update(fixedDelta);
            }

            commandJournal.AddRange(save.Commands);
        }

        private void ReplaceTerrain(TerrainSettings settings)
        {
            terrainRenderer.Dispose();
            terrain.Dispose();
            terrain = new Terrain(settings ?? throw new ArgumentNullException(nameof(settings)));
            forest.Reset(terrain);
            terrainRenderer = new TerrainRenderer(terrain, vehicles, effects, forest);
        }

        public void Dispose()
        {
            backgroundJobs.Dispose();
            terrainRenderer?.Dispose();
            terrain?.Dispose();
        }
    }
}
