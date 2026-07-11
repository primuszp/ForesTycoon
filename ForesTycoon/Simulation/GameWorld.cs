using System;
using OpenTK.Mathematics;

namespace ForesTycoon
{
    /// <summary>
    /// Owns game-state lifetime and is the boundary used by input, simulation and rendering.
    /// Viewport code must not own or replace individual world systems directly.
    /// </summary>
    sealed class GameWorld : IDisposable, IWorldCommandTarget
    {
        private Terrain terrain;
        private readonly WorldCommandQueue commands = new WorldCommandQueue();
        private readonly VehicleSystem vehicles = new VehicleSystem();

        public GameWorld(TerrainSettings settings)
        {
            terrain = new Terrain(settings ?? throw new ArgumentNullException(nameof(settings)));
        }

        public Tile HoveredTile => terrain.HoveredTile;
        public int HoveredTileId => terrain.HoveredTile?.Id ?? -1;
        public int SelectedNodeId => terrain.SelectedNodeId;
        public int RoadCount => terrain.RoadCount;
        public int RoadPreviewCount => terrain.RoadPreviewCount;
        public int VehicleCount => vehicles.Count;

        public void Update(double fixedDeltaSeconds)
        {
            vehicles.Update(fixedDeltaSeconds);
        }

        public int ExecutePendingCommands() => commands.ExecutePending(this);
        public void QueueElevationEdit(int nodeId, int delta, int radius, int strength) =>
            commands.Enqueue(new EditElevationCommand(nodeId, delta, radius, strength));
        public void QueueRoadPath(int startTileId, int endTileId, bool remove) =>
            commands.Enqueue(new RoadPathCommand(startTileId, endTileId, remove));
        public void QueueSpawnVehicle() => commands.Enqueue(new SpawnVehicleCommand());

        void IWorldCommandTarget.ExecuteElevationEdit(int nodeId, int delta, int radius, int strength) =>
            terrain.EditElevationAtNode(nodeId, delta, radius, strength);

        void IWorldCommandTarget.ExecuteRoadPath(int startTileId, int endTileId, bool remove)
        {
            if (remove) terrain.RemoveRoadTilePath(startTileId, endTileId);
            else terrain.BuildRoadTilePath(startTileId, endTileId);
            if (remove) vehicles.RemoveInvalidRoutes(terrain.IsRoadTile);
        }

        void IWorldCommandTarget.ExecuteSpawnVehicle()
        {
            int[] route = terrain.FindDemoRoadRoute();
            if (route.Length >= 2) vehicles.Spawn(route);
        }

        public void Draw(RenderContext context)
        {
            terrain.Draw(context);
            VehicleRenderer.Draw(vehicles, terrain, context.InterpolationAlpha);
        }
        public void GetWorldBounds(out Vector3 min, out Vector3 max) => terrain.GetWorldBounds(out min, out max);
        public bool TryGetSurfaceZ(double x, double y, out float z) => terrain.TryGetSurfaceZ(x, y, out z);
        public bool SearchScreenPoint(double x, double y, double radius, double[] model, double[] projection, int[] viewport) =>
            terrain.SearchScreenPoint(x, y, radius, model, projection, viewport);
        public bool SearchTile(double x, double y) => terrain.SearchTile(x, y);
        public void ClearHover() => terrain.ClearHover();
        public void ClearTileHover() => terrain.ClearTileHover();
        public void SetRoadPreview(Tile from, Tile to, bool remove) => terrain.SetRoadPreview(from, to, remove);
        public void ClearRoadPreview() => terrain.ClearRoadPreview();

        public void Regenerate(TerrainSettings settings)
        {
            commands.Clear();
            vehicles.Clear();
            terrain.Dispose();
            terrain = new Terrain(settings ?? throw new ArgumentNullException(nameof(settings)));
        }

        public void Dispose() => terrain?.Dispose();
    }
}
