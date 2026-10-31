using System;
using System.Collections.Generic;

namespace ForesTycoon;

internal sealed class RoadTimberSurvey
{
    internal readonly Dictionary<int, double> TimberByRoad = new();
    internal uint[] Overlay;
    internal double TotalTimber;
    internal int CoveredTiles;
}

partial class SimulationWorld
{
    internal double RoadBufferMetres(int tile) => map.IsRoadTile(tile)
        ? RoadCosts.Buffer(map.GetRoadPaving(tile), Tuning) : Tuning[Tune.TrailBufferMetres];

    /// <summary>Horizontal, centre-to-centre access estimate. Overlapping buffers count timber once in the total.
    /// Presentation query only: never reserves or harvests wood. Work is yielded for every examined cell.</summary>
    internal IEnumerable<int> RoadTimberSurveyWork(WorkResult<RoadTimberSurvey> result)
    {
        var survey = new RoadTimberSurvey { Overlay = new uint[map.Tiles.Count] };
        var timber = new double[map.Tiles.Count]; var network = new List<int>();
        for (int id = 0; id < timber.Length; id++) {
            double depot = forest.IndividualTrees.TryGet(id, out var patch) ? patch.Depot : 0;
            timber[id] = Math.Max(0, forest.AvailableTimber(id) - depot);
            if (map.IsNetworkTile(id)) network.Add(id);
            yield return 1;
        }
        int rows = map.Settings.TileRows, columns = map.Settings.TileColumns;
        map.TryGetTileCenter(0, out var origin);
        map.TryGetTileCenter(rows, out var u); u -= origin;
        map.TryGetTileCenter(1, out var v); v -= origin;
        double determinant = u.X * v.Y - u.Y * v.X;
        foreach (int road in network) {
            double radius = RoadBufferMetres(road) * WorldScale.MetresToWorld;
            int du = (int)Math.Ceiling(radius * Math.Sqrt(v.X * v.X + v.Y * v.Y) / Math.Abs(determinant));
            int dv = (int)Math.Ceiling(radius * Math.Sqrt(u.X * u.X + u.Y * u.Y) / Math.Abs(determinant));
            map.TryGetTileCenter(road, out var centre);
            double volume = 0;
            for (int x = Math.Max(0, road / rows - du); x <= Math.Min(columns - 1, road / rows + du); x++)
                for (int y = Math.Max(0, road % rows - dv); y <= Math.Min(rows - 1, road % rows + dv); y++) {
                    int id = x * rows + y; map.TryGetTileCenter(id, out var at);
                    double dx = at.X - centre.X, dy = at.Y - centre.Y;
                    if (dx * dx + dy * dy <= radius * radius + 1e-6) {
                        volume += timber[id];
                        if (survey.Overlay[id] == 0) {
                            survey.Overlay[id] = 0x6639BD75;
                            survey.TotalTimber += timber[id]; survey.CoveredTiles++;
                        }
                    }
                    yield return 1;
                }
            survey.TimberByRoad.Add(road, volume); yield return 1;
        }
        result.Value = survey;
    }
}
