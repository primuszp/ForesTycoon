using System;
using System.Collections.Generic;

namespace ForesTycoon.Map
{
    internal sealed partial class TerrainMap
    {
        private Node actualNode;
        private readonly TerrainElevationPlanner.Scratch elevationPlan = new();
        private HashSet<int> elevationEditTiles;

        /// <summary>The node under the editing cursor, or -1.</summary>
        public int SelectedNodeId => actualNode?.Id ?? -1;
        internal Node SelectedNode => actualNode;

        public void UpElevation() => RaiseOrLowerSelected(+1);

        public void DownElevation() => RaiseOrLowerSelected(-1);

        /// <summary>
        /// Ecsetes terepszerkesztés a kijelölt (hover) node körül: korong alakú
        /// terület, sugár = radius (0 = csak a középpont), erősség = ismétlésszám.
        /// A hidrológiát csak egyszer, a végén építi újra.
        /// </summary>
        public int[] EditElevation(int delta, int radius, int strength) { var result = new WorkResult<int[]>(); SimulationWork.Complete(EditElevationWork(delta, radius, strength, result)); return result.Value; }

        private IEnumerable<int> EditElevationWork(int delta, int radius, int strength, WorkResult<int[]> result)
        {
            result.Value = Array.Empty<int>();
            if (actualNode == null || delta == 0 || radius < 0 || strength <= 0) yield break;

            Node center = actualNode;
            int cu = center.U, cv = center.V;

            // Az ElevationManager kaszkádja tartja a TT-invariánst (szomszédos sarkok max 1
            // eltérés) → a terep mindig érvényes. Az út alatt is alakítható a terep: a
            // befagyasztott vezetőfelület (roadSurfaceW) a helyén marad, a rést a foundation
            // tölti ki — ezért itt NINCS út-freeze.
            var changedTiles = new HashSet<int>();
            elevationEditTiles = changedTiles;
            suppressHydrologyRebuild = true;
            try
            {
                for (int du = -radius; du <= radius; du++)
                    for (int dv = -radius; dv <= radius; dv++)
                    {
                        yield return 1;
                        if ((long)du * du + (long)dv * dv > (long)radius * radius) continue;
                        if (!data.CheckNode(cu + du, cv + dv)) continue;

                        Node n = GetNode(cu + du, cv + dv);
                        for (int s = 0; s < strength; s++)
                            foreach (int unit in RaiseOrLowerWork(n, delta)) yield return unit;
                    }
            }
            finally
            {
                suppressHydrologyRebuild = false;
                elevationEditTiles = null;
                if (changedTiles.Count > 0) EditsFlushed?.Invoke();
            }

            actualNode = center;
            if (changedTiles.Count == 0) yield break;
            foreach (int unit in RebuildHydrologyWork()) yield return unit;
            // Walking IDs is bounded and gives the same sorted result without a full sort.
            var ordered = new List<int>(changedTiles.Count);
            for (int id = 0; id < tiles.Length; id++) { if (changedTiles.Contains(id)) ordered.Add(id); yield return 1; }
            result.Value = ordered.ToArray();
        }

        public int[] EditElevationAtNode(int nodeId, int delta, int radius, int strength)
        {
            if (nodeId < 0 || nodeId >= nodes.Length) return Array.Empty<int>();
            actualNode = nodes[nodeId];
            return EditElevation(delta, radius, strength);
        }

        internal IEnumerable<int> EditElevationAtNodeWork(int nodeId, int delta, int radius, int strength, WorkResult<int[]> result)
        {
            result.Value = Array.Empty<int>();
            if ((uint)nodeId >= (uint)nodes.Length) yield break;
            actualNode = nodes[nodeId];
            // Clip traversal to the finite grid even for oversized brush requests.
            radius = Math.Min(radius, Math.Max(nodeCols, nodeRows) * 2);
            foreach (int unit in EditElevationWork(delta, radius, strength, result)) yield return unit;
        }

        private void RaiseOrLowerSelected(int delta)
        {
            if (actualNode != null) RaiseOrLower(actualNode, delta);
        }

        // OpenTTD-stílusú terraform (terraform_cmd.cpp): egy sarkot delta-val mozdít, és
        // rekurzívan a cél felé 1-gyel közelíti a szomszéd-sarkokat, amíg minden ÉL-
        // szomszédos sarok eltérése ≤1 (a szemközti sarok 2-vel is → meredek, érvényes).
        // A változásokat előbb egy pending-térképbe gyűjti; ha bármelyik a [0, MaxHeight]
        // korláton kívülre esne, az EGÉSZ művelet elbukik és semmi nem változik (atomikus).
        private void RaiseOrLower(Node corner, int delta) => SimulationWork.Complete(RaiseOrLowerWork(corner, delta));

        private IEnumerable<int> RaiseOrLowerWork(Node corner, int delta)
        {
            var planned = new WorkResult<Dictionary<int, int>>();
            foreach (int unit in TerrainElevationPlanner.CreateWork(data, corner.Id, delta, settings.MaxHeight, planned, elevationPlan)) yield return unit;
            var pending = planned.Value;
            if (pending == null) yield break;
            var valid = new WorkResult<bool>();
            foreach (int unit in ValidateRoadsAgainstPendingTerrainWork(pending, valid)) yield return unit;
            if (!valid.Value) yield break;

            List<Node> changed = generatingTerrain ? null : new List<Node>(pending.Count);
            foreach (KeyValuePair<int, int> kv in pending)
            {
                yield return 1;
                Node nd = data.Nodes[kv.Key];
                if (nd.W == kv.Value) continue;
                nd.W = kv.Value;
                nd.zPos = nd.W * tileSizeM;   // zPos szinkron a hidrológiához
                changed?.Add(nd);
            }
            if (!generatingTerrain) foreach (int unit in ApplyNodeChangesWork(changed)) yield return unit;
        }



        private IEnumerable<int> ValidateRoadsAgainstPendingTerrainWork(Dictionary<int, int> pending, WorkResult<bool> result)
        {
            result.Value = true;
            if (roads.Count == 0 && buildingTiles.Count == 0 && GroundReserved == null && VegetationOccupied == null && MobileOccupied == null) yield break;

            int HeightOf(Node nd) => pending.TryGetValue(nd.Id, out int v) ? v : nd.W;
            Tile[] nodeTiles = new Tile[4];

            // Út-csempe csomópontok (Node) magasságának módosítása tilos!
            foreach (KeyValuePair<int, int> kv in pending)
            {
                yield return 1;
                Node node = data.Nodes[kv.Key];
                if (kv.Value != node.W)
                {
                    int nodeTileCount = data.GetTilesByNode(node, nodeTiles);
                    for (int i = 0; i < nodeTileCount; i++)
                    {
                        Tile tile = nodeTiles[i];
                        if (IsTerrainEditBlocked(tile.Id))
                        { result.Value = false; yield break; }
                    }
                }
            }

            HashSet<int> affectedRoadTiles = new HashSet<int>();

            foreach (int nodeId in pending.Keys)
            {
                yield return 1;
                Node node = data.Nodes[nodeId];
                int nodeTileCount = data.GetTilesByNode(node, nodeTiles);
                for (int i = 0; i < nodeTileCount; i++)
                {
                    Tile tile = nodeTiles[i];
                    if (roads.Has(tile.Id)) affectedRoadTiles.Add(tile.Id);
                }
            }

            foreach (int tileId in affectedRoadTiles)
                if (!ValidateExistingRoadAgainstTerrain(tiles[tileId], HeightOf))
                { result.Value = false; yield break; }

            yield break;
        }

        private bool ValidateExistingRoadAgainstTerrain(Tile t, Func<Node, int> heightOf)
        {
            int w = heightOf(t.W);
            int s = heightOf(t.S);
            int e = heightOf(t.E);
            int n = heightOf(t.N);
            if (!RoadTerrainStaysAboveWater(w, s, e, n)) return false;

            if (TryGetFullLockedRoadSurface(t, out int sw, out int ss, out int se, out int sn))
                return ValidateLockedRoadPlacement(roads.GetEdges(t.Id), w, s, e, n, sw, ss, se, sn).IsValid;

            return AnalyzeRoadPlacement(t, roads.GetEdges(t.Id), heightOf).IsValid;
        }
    }
}
