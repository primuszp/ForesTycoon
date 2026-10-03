using System;
using System.Collections.Generic;

namespace ForesTycoon
{
    partial class Terrain
    {
        public void UpElevation()
        {
            ElevationManager(+1);
        }

        public void DownElevation()
        {
            ElevationManager(-1);
        }

        /// <summary>
        /// Ecsetes terepszerkesztés a kijelölt (hover) node körül: korong alakú
        /// terület, sugár = radius (0 = csak a középpont), erősség = ismétlésszám.
        /// A hidrológiát csak egyszer, a végén építi újra.
        /// </summary>
        public void EditElevation(int delta, int radius, int strength)
        {
            if (actualNode == null || delta == 0 || radius < 0 || strength <= 0) return;

            Node center = actualNode;
            int cu = center.U, cv = center.V;

            // Az ElevationManager kaszkádja tartja a TT-invariánst (szomszédos sarkok max 1
            // eltérés) → a terep mindig érvényes. Az út alatt is alakítható a terep: a
            // befagyasztott vezetőfelület (roadSurfaceW) a helyén marad, a rést a foundation
            // tölti ki — ezért itt NINCS út-freeze.
            suppressHydrologyRebuild = true;
            try
            {
                for (int du = -radius; du <= radius; du++)
                    for (int dv = -radius; dv <= radius; dv++)
                    {
                        if (du * du + dv * dv > radius * radius) continue;
                        if (!checkNode(cu + du, cv + dv)) continue;

                        Node n = getNodeByCoords(cu + du, cv + dv);
                        for (int s = 0; s < strength; s++)
                        {
                            actualNode = n;
                            ElevationManager(delta);
                        }
                    }
            }
            finally
            {
                suppressHydrologyRebuild = false;
            }

            actualNode = center;
            RebuildHydrology();
        }

        public int SelectedNodeId => actualNode?.Id ?? -1;

        public void EditElevationAtNode(int nodeId, int delta, int radius, int strength)
        {
            if (nodeId < 0 || nodeId >= nodes.Length) return;
            actualNode = nodes[nodeId];
            EditElevation(delta, radius, strength);
        }

        // OpenTTD-stílusú terraform (terraform_cmd.cpp): egy sarkot delta-val mozdít, és
        // rekurzívan a cél felé 1-gyel közelíti a szomszéd-sarkokat, amíg minden ÉL-
        // szomszédos sarok eltérése ≤1 (a szemközti sarok 2-vel is → meredek, érvényes).
        // A változásokat előbb egy pending-térképbe gyűjti; ha bármelyik a [0, MaxHeight]
        // korláton kívülre esne, az EGÉSZ művelet elbukik és semmi nem változik (atomikus).
        private void ElevationManager(int delta)
        {
            if (actualNode == null) return;

            if (!TerrainElevationPlanner.TryCreate(data, actualNode.Id, delta, settings.MaxHeight,
                out Dictionary<int, int> pending)) return;
            if (!ValidateRoadsAgainstPendingTerrain(pending)) return;

            List<Node> changed = new List<Node>(pending.Count);
            foreach (KeyValuePair<int, int> kv in pending)
            {
                Node nd = data.Nodes[kv.Key];
                nd.W = kv.Value;
                nd.zPos = nd.W * tileSizeM;   // zPos szinkron a hidrológiához
                changed.Add(nd);
            }
            updateNodes(changed);
        }

        private bool ValidateRoadsAgainstPendingTerrain(Dictionary<int, int> pending)
        {
            if (roads.Count == 0 && buildingTiles.Count == 0) return true;

            int HeightOf(Node nd) => pending.TryGetValue(nd.Id, out int v) ? v : nd.W;
            Tile[] nodeTiles = new Tile[4];

            // Út-csempe csomópontok (Node) magasságának módosítása tilos!
            foreach (KeyValuePair<int, int> kv in pending)
            {
                Node node = data.Nodes[kv.Key];
                if (kv.Value != node.W)
                {
                    int nodeTileCount = data.GetTilesByNode(node, nodeTiles);
                    for (int i = 0; i < nodeTileCount; i++)
                    {
                        Tile tile = nodeTiles[i];
                        if (roads.Has(tile.Id) || IsBuildingTile(tile.Id))
                            return false;
                    }
                }
            }

            HashSet<int> affectedRoadTiles = new HashSet<int>();

            foreach (int nodeId in pending.Keys)
            {
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
                    return false;

            return true;
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
