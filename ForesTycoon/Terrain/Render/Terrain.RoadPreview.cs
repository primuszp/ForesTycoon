using System;
using System.Collections.Generic;
using System.Drawing;
using OpenTK.Mathematics;

namespace ForesTycoon
{
    partial class Terrain
    {
        // Muted miniature materials: warm asphalt and earth shoulders in the forest palette.
        private static readonly Color RoadSurfaceColor = Color.FromArgb(98, 100, 98);
        private static readonly Color RoadShoulder     = Color.FromArgb(151, 143, 117);
        private const float ShoulderFrac = 0.16f;  // padka szélessége a középpont felé

        // Húzás közbeni előnézet csempéi (remove = bontás, piros előnézet).
        private readonly List<TerrainMap.RoadPlanStep> previewTiles = new List<TerrainMap.RoadPlanStep>();
        private bool previewRemove;
        // Repair preview: worn road tiles on the dragged path light up, the rest stay as they are.
        private bool previewRepair;

        // Skid-trail preview: the dragged path drawn as faint ruts (red when removing).
        private bool previewSkidTrail;

        public void SetSkidTrailPreview(int startTileId, int endTileId, bool remove)
        {
            SetRoadPreview(startTileId, endTileId, remove);
            previewSkidTrail = true;
        }

        public void SetRoadRepairPreview(int startTileId, int endTileId)
        {
            SetRoadPreview(startTileId, endTileId, false);
            previewRepair = true;
        }

        public void SetRoadPreview(int startTileId, int endTileId, bool remove)
        {
            Tile from = startTileId >= 0 && startTileId < tiles.Length ? tiles[startTileId] : null;
            Tile to = endTileId >= 0 && endTileId < tiles.Length ? tiles[endTileId] : null;
            SetRoadPreview(from, to, remove);
        }

        public void SetRoadPreview(Tile a, Tile b, bool remove)
        {
            previewTiles.Clear();
            previewRemove = remove; previewRepair = false; previewSkidTrail = false;
            previewTiles.AddRange(map.BuildRoadPlan(a, b));
        }
        public void ClearRoadPreview() => previewTiles.Clear();
        public int RoadPreviewCount => previewTiles.Count;

        // Az út-lábnyom sarkai megegyeznek a csempe eredeti sarkaival (nincs behúzás, hézagmentes).
        private void RoadFootprintCorners(Tile t, out Vector3 W, out Vector3 S, out Vector3 E, out Vector3 N)
        {
            W = RoadCorner(t.W);
            S = RoadCorner(t.S);
            E = RoadCorner(t.E);
            N = RoadCorner(t.N);
        }
    }
}
