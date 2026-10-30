using System;
using System.Collections.Generic;
using OpenTK.Mathematics;

namespace ForesTycoon;

/// <summary>Game host adapter: presentation resources surround the independent simulation world.</summary>
sealed partial class GameWorld : SimulationWorld, IWorldInteractionTarget
{
    private Terrain terrain;
    private TerrainRenderer terrainRenderer;
    internal GraphicsSettings Graphics { get; }
    internal override bool HasPresentation => terrainRenderer != null;

    internal GameWorld(TerrainSettings settings, double forestYearSeconds = EcologyTime.DefaultGameSecondsPerYear,
        SoilLandscapeDefinition soils = null, ClimateDefinition climate = null, bool enableRendering = true,
        EcologyResponseProgram responseRules = null, WorldScenario scene = null)
        : base(settings, forestYearSeconds, soils, climate, responseRules, scene)
    {
        Graphics = new GraphicsSettings { AutomaticWeather = true };
        try { if (enableRendering) AttachRendering(); } catch { Dispose(); throw; }
    }

    internal GameWorld(TerrainMap map, double forestYearSeconds = EcologyTime.DefaultGameSecondsPerYear)
        : base(map, forestYearSeconds) => Graphics = new GraphicsSettings { AutomaticWeather = true };

    private GameWorld(TerrainMap map, GraphicsSettings graphics, double yearSeconds, SoilLandscapeDefinition soils,
        ClimateDefinition climate, Ecosystem ecosystem, bool deferRules, EcologyResponseProgram responses, WorldScenario scene)
        : base(map, yearSeconds, soils, climate, ecosystem, deferRules, responses, scene) => Graphics = graphics;

    protected override SimulationWorld CreateCandidate(TerrainMap map, double yearSeconds, SoilLandscapeDefinition soils,
        ClimateDefinition climate, Ecosystem preparedEcosystem = null, bool deferRules = false,
        EcologyResponseProgram initialResponses = null, WorldScenario scene = null)
        => new GameWorld(map, Graphics, yearSeconds, soils, climate, preparedEcosystem, deferRules, initialResponses, scene);

    protected override void VerifyPresentationAccess() => terrainRenderer?.VerifyAccess();
    protected override void InvalidatePresentationGround() => terrain?.InvalidateGround();
    protected override void BindPresentationQueries()
    {
        if (terrainRenderer == null) return;
        terrainRenderer.FloodDepth = id => Environment?.SurfaceDepth(id) ?? 0;
        terrain.SideProfile = SideProfile;
        terrain.GroundOf = TileGround;
        terrain.InvalidateGround();
    }
    internal void AttachRendering() => WithSpecies(() => { SimulationWork.Complete(AttachRenderingWork()); return 0; });
    protected override void AttachPresentation() => AttachRendering();
    private IEnumerable<int> AttachRenderingWork()
    {
        EnsureAvailable();
        if (terrainRenderer != null) { terrainRenderer.VerifyAccess(); yield break; }
        var scene = new Terrain(Map, deferGeometry: true);
        bool attached = false;
        try {
            foreach (int unit in scene.PrepareBaseGeometryWork()) yield return unit;
            var renderer = new TerrainRenderer(scene, VehicleState, EffectState, Forest, Graphics, Environment, Wildlife, Logistics);
            terrain = scene; terrainRenderer = renderer; attached = true;
            BindPresentationQueries();
        } finally { if (!attached) scene.Dispose(); }
    }
    protected override IEnumerable<int> PreparePresentationWork()
    {
        foreach (int unit in AttachRenderingWork()) yield return unit;
        foreach (float progress in PrepareMapGeometry()) yield return 64;
    }
    protected override void SwapPresentation(SimulationWorld candidate)
    {
        var host = (GameWorld)candidate;
        (terrain, host.terrain) = (host.terrain, terrain);
        (terrainRenderer, host.terrainRenderer) = (host.terrainRenderer, terrainRenderer);
    }
    protected override IEnumerable<int> DisposePresentationWork()
    {
        terrainRenderer?.Dispose(); terrainRenderer = null;
        if (terrain != null) foreach (int unit in terrain.DisposeWork()) yield return unit;
        terrain = null;
    }
    internal int ReadyVisibleForestChunks(ForestLod lod) => terrain?.ReadyVisibleForestChunks(lod) ?? 0;

    public int RoadPreviewCount => terrain?.RoadPreviewCount ?? 0;

    internal int FishCount=>terrainRenderer?.FishCount ?? 0;
    internal int FloodTilesDrawn => terrainRenderer?.FloodTilesDrawn ?? 0;
    internal int WildlifeCount => terrainRenderer?.WildlifeCount ?? 0;
    internal bool TryGetWildlifePosition(out Vector3 position)
    {
        if (terrainRenderer != null) return terrainRenderer.TryGetWildlifePosition(out position);
        position = default; return false;
    }

    /// <summary>Per-tile RGBA tint of the active management lens drawn onto the terrain; null clears it.</summary>
    internal void SetManagementOverlay(uint[] colours) => terrain?.SetManagementOverlay(colours);


    public int VisibleChunkCount => terrain?.VisibleChunkCount ?? 0;
    public int ForestChunkRebuilds => terrain?.ForestChunkRebuilds ?? 0;
    public long ForestGpuPayloadBytes => terrain?.ForestGpuPayloadBytes ?? 0;
    public long ForestCpuPayloadBytes => terrain?.ForestCpuPayloadBytes ?? 0;
    public long ForestBudgetExcessBytes => terrain?.ForestBudgetExcessBytes ?? 0;
    public int ForestResidentLods => terrain?.ForestResidentLods ?? 0;
    public int ForestCacheEvictions => terrain?.ForestCacheEvictions ?? 0;
    public long StaticGpuPayloadBytes => terrain?.StaticGpuPayloadBytes ?? 0;
    public long StaticCpuPayloadBytes => terrain?.StaticCpuPayloadBytes ?? 0;
    public long StaticBudgetExcessBytes => terrain?.StaticBudgetExcessBytes ?? 0;
    public int WeatherParticleCount => terrainRenderer?.WeatherParticleCount ?? 0;
    public int WeatherCloudSteps => terrainRenderer?.WeatherCloudSteps ?? 0;
    public long WeatherCpuPayloadBytes => terrainRenderer?.WeatherCpuPayloadBytes ?? 0;
    public long WeatherGpuPayloadBytes => terrainRenderer?.WeatherGpuPayloadBytes ?? 0;
    public double WeatherCpuMilliseconds => terrainRenderer?.WeatherCpuMilliseconds ?? 0;
    public int FogParticleCount => terrainRenderer?.FogParticleCount ?? 0;
    public bool FogDepthFallback => terrainRenderer?.FogDepthFallback ?? false;
    public int MarkerRenderedCount => terrainRenderer?.MarkerRenderedCount ?? 0;

    public void SetSkidTrailPreview(int startTileId, int endTileId, bool remove) =>
        terrain?.SetSkidTrailPreview(startTileId, endTileId, remove);

    /// <summary>Order-tool marks drawn on the ground (targets, cursor tile, routes).</summary>
    internal OrderOverlay Orders => terrain?.Orders ?? throw new InvalidOperationException("Attach rendering before using visual overlays.");
    public void SetRoadRepairPreview(int startTileId, int endTileId) => terrain?.SetRoadRepairPreview(startTileId, endTileId);

    public void SetForestryPreview(int startTileId, int endTileId, bool removal) =>
        terrain?.SetForestryPreview(startTileId, endTileId, removal);
    public void ClearForestryPreview() => terrain?.ClearForestryPreview();
    public int ForestryPreviewCount => terrain?.ForestryPreviewCount ?? 0;


    public void Draw(RenderContext context)
    {
        EnsureAvailable();
        if (terrainRenderer == null) throw new InvalidOperationException("Attach rendering before drawing a headless world.");
        ForestSpeciesBook.WithContext(SpeciesContext, () => { terrainRenderer.Draw(context); return 0; });
    }
    internal bool MapGeometryPrepared => terrainRenderer?.MapGeometryPrepared == true;
    internal System.Collections.Generic.IEnumerable<float> PrepareMapGeometry()
    {
        EnsureAvailable();
        if (terrainRenderer == null) throw new InvalidOperationException("Attach rendering before preparing a map.");
        return PrepareMapGeometryWithSpecies();
    }
    private IEnumerable<float> PrepareMapGeometryWithSpecies()
    {
        using var work = terrainRenderer.PrepareMapGeometry().GetEnumerator();
        while (ForestSpeciesBook.WithContext(SpeciesContext, () => work.MoveNext())) yield return work.Current;
    }

    public void SetRoadPreview(int startTileId, int endTileId, bool remove) =>
        terrain?.SetRoadPreview(startTileId, endTileId, remove);
    public void ClearRoadPreview() => terrain?.ClearRoadPreview();


}
