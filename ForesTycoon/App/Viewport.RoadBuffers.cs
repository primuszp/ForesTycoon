namespace ForesTycoon;

sealed partial class Viewport
{
    private readonly SlicedPreviewCache<int, RoadTimberSurvey> roadSurveys = new(1, 300);
    private RoadTimberSurvey roadSurvey;
    private bool showRoadBuffers;

    private void UpdateRoadSurvey()
    {
        roadSurveys.BeginFrame((world.Map, world.Map.NavigationVersion, world.Forest.EditRevision, world.Tuning));
        if (!showRoadBuffers && !showForestry && !world.Map.IsNetworkTile(world.HoveredTileId)) return;
        if (roadSurveys.Request(0, world.RoadTimberSurveyWork, out var ready)) roadSurvey = ready;
        else roadSurvey = null;
        roadSurveys.Advance(1024);
    }

    private void DrawRoadBufferInfo(int tile)
    {
        HudTheme.KeyValue("Elérési puffer", $"{world.RoadBufferMetres(tile):0.#} m");
        HudTheme.KeyValue("Elérhető élő faanyag", roadSurvey != null && roadSurvey.TimberByRoad.TryGetValue(tile, out double volume)
            ? $"{volume:F1} m³" : "Számítás…");
    }
}
