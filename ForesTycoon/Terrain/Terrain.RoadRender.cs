using System;
using System.Drawing;
using OpenTK.Graphics.OpenGL;
using OpenTK.Mathematics;

namespace ForesTycoon
{
    partial class Terrain
    {
        private const float RoadShoulderWidthFactor = 1.0f;
        private const float RoadSurfaceWidthFactor = 0.62f;

        internal void DrawRoads()
        {
            if (roads.Count == 0 && previewTiles.Count == 0) return;

            if (roads.Count > 0)
            {
                DynamicPrimitiveBatch.Draw(PrimitiveType.Quads, () =>
                {
                    foreach (Tile tile in visibleTiles)
                    {
                        if (!roads.Has(tile.Id)) continue;
                        RoadSurface(tile, roads.GetEdges(tile.Id), RoadShoulderWidthFactor, RoadShoulder);
                    }
                });

                DynamicPrimitiveBatch.Draw(PrimitiveType.Quads, () =>
                {
                    foreach (Tile tile in visibleTiles)
                    {
                        if (!roads.Has(tile.Id)) continue;
                        RoadSurface(tile, roads.GetEdges(tile.Id), RoadSurfaceWidthFactor, RoadSurfaceColor);
                    }
                });
            }

            if (previewTiles.Count > 0)
            {
                Color okFill = Color.FromArgb(70, 255, 255, 255), okLine = Color.FromArgb(235, 255, 255, 255);
                Color foundationFill = Color.FromArgb(85, 245, 225, 140), foundationLine = Color.FromArgb(245, 245, 225, 140);
                Color badFill = Color.FromArgb(90, 235, 70, 70), badLine = Color.FromArgb(245, 248, 80, 80);

                using (new RenderStateScope().AlphaBlend())
                {
                    DynamicPrimitiveBatch.Draw(PrimitiveType.Quads, () =>
                    {
                        foreach (RoadPlanStep step in previewTiles)
                        {
                            RoadPlacement placement = AnalyzeRoadPlacement(tiles[step.TileId], step.Edges);
                            bool bad = previewRemove || !placement.IsValid;
                            bool foundation = !bad && placement.Kind == RoadPlacementKind.FoundationSurface;
                            RoadEdge shown = previewRemove ? step.Edges : step.Edges | roads.GetEdges(step.TileId);
                            if (bad) RoadSurface(tiles[step.TileId], shown, RoadShoulderWidthFactor, badFill);
                            else RoadSurface(tiles[step.TileId], shown, RoadShoulderWidthFactor, foundation ? foundationFill : okFill, placement);
                        }
                    });

                    using (new RenderStateScope().LineWidth(2.5f))
                    {
                        foreach (RoadPlanStep step in previewTiles)
                        {
                            Tile t = tiles[step.TileId];
                            RoadPlacement placement = AnalyzeRoadPlacement(t, step.Edges);
                            bool bad = previewRemove || !placement.IsValid;
                            bool foundation = !bad && placement.Kind == RoadPlacementKind.FoundationSurface;
                            DynamicPrimitiveBatch.Color4(bad ? badLine : (foundation ? foundationLine : okLine));
                            DynamicPrimitiveBatch.Draw(PrimitiveType.LineLoop, () =>
                            {
                                DynamicPrimitiveBatch.Vertex3(t.W.xPos, t.W.yPos, t.W.zPos);
                                DynamicPrimitiveBatch.Vertex3(t.S.xPos, t.S.yPos, t.S.zPos);
                                DynamicPrimitiveBatch.Vertex3(t.E.xPos, t.E.yPos, t.E.zPos);
                                DynamicPrimitiveBatch.Vertex3(t.N.xPos, t.N.yPos, t.N.zPos);
                            });
                        }
                    }
                }
            }
        }
        private void RoadSurface(Tile t, RoadEdge edges, float widthFactor, Color color)
        {
            // A befagyasztott vezetőfelület magasságán renderelünk (foundation), nem a
            // jelenlegi terepen; a nyitott földmű-éleken a lábnyom a rézsű felső éléig
            // húzódik be → az út a lapos platform-tetőn ül, túllógás/rés nélkül.
            RoadFootprintCorners(t, out Vector3 W, out Vector3 S, out Vector3 E, out Vector3 N);
            RoadSurface(t, edges, widthFactor, color, W, S, E, N);
        }

        private void RoadSurface(Tile t, RoadEdge edges, float widthFactor, Color color, RoadPlacement placement)
        {
            Vector3 W = RoadCorner(t.W, placement.W);
            Vector3 S = RoadCorner(t.S, placement.S);
            Vector3 E = RoadCorner(t.E, placement.E);
            Vector3 N = RoadCorner(t.N, placement.N);
            RoadSurface(t, edges, widthFactor, color, W, S, E, N);
        }

        private void RoadSurface(Tile t, RoadEdge edges, float widthFactor, Color color, Vector3 W, Vector3 S, Vector3 E, Vector3 N)
        {
            Vector3 C = (W + S + E + N) * 0.25f;
            float width = Math.Min(tileSizeH, tileSizeV) * widthFactor;
            int n = CountEdges(edges);

            DynamicPrimitiveBatch.Color4(color);

            // Kanyar (2 szomszédos él): negyedív a KÖZÖS sarok körül. Az ív az élek
            // közepénél merőlegesen lép ki → érintőfolytonosan illeszkedik a szomszéd
            // egyenes úthoz (mint az OpenTTD kerek kanyar-tile).
            if (n == 2 && TryCornerArc(edges, out float startDeg))
            {
                float side = DistXY(W, S);
                float hwuv = (width * 0.5f) / side;
                RoadArcBand(W, S, E, N, startDeg, 0.5f - hwuv, 0.5f + hwuv);
                return;
            }

            // Egyenes / zsákutca / T / +: ágak minden bekötött él felé, mindegyik a
            // csempe-középponttól az él KÖZEPÉIG → a szomszéd út karjával pontosan
            // illeszkedik (nincs hézag). Két szemközti ág egy teljes átmenő sávot ad,
            // így T-nél (3 él) és +-nál (4 él) is tömör, hézagmentes a csomópont; a
            // nyitott él fűként/padkaként marad → ez adja a T/+ formát.
            if ((edges & RoadEdge.WS) != 0) RoadArm(C, (W + S) * 0.5f, width);
            if ((edges & RoadEdge.SE) != 0) RoadArm(C, (S + E) * 0.5f, width);
            if ((edges & RoadEdge.EN) != 0) RoadArm(C, (E + N) * 0.5f, width);
            if ((edges & RoadEdge.NW) != 0) RoadArm(C, (N + W) * 0.5f, width);

            // Belső lekerekítés MINDEN olyan csempe-saroknál, ahol a két szomszédos él
            // is út (T-nél 2, +-nál 4 sarok). A fűsarkot a CSEMPE-SAROK köré centrált
            // negyedkör kerekíti (sugár 0.5−hw); mindkét réteg ugyanaz a középpont →
            // koncentrikus ívek → a padka vonalai mindenhol párhuzamosak.
            float jSide = DistXY(W, S);
            float hw = (width * 0.5f) / jSide;
            float rf = 0.5f - hw;
            if ((edges & RoadEdge.SE) != 0 && (edges & RoadEdge.EN) != 0)  // E sarok
                RoadInnerFillet(W, S, E, N, 0.5f + hw, 0.5f + hw, 1f, 1f, rf, 270f, 180f);
            if ((edges & RoadEdge.EN) != 0 && (edges & RoadEdge.NW) != 0)  // N sarok
                RoadInnerFillet(W, S, E, N, 0.5f - hw, 0.5f + hw, 0f, 1f, rf, 360f, 270f);
            if ((edges & RoadEdge.NW) != 0 && (edges & RoadEdge.WS) != 0)  // W sarok
                RoadInnerFillet(W, S, E, N, 0.5f - hw, 0.5f - hw, 0f, 0f, rf, 90f, 0f);
            if ((edges & RoadEdge.WS) != 0 && (edges & RoadEdge.SE) != 0)  // S sarok
                RoadInnerFillet(W, S, E, N, 0.5f + hw, 0.5f - hw, 1f, 0f, rf, 180f, 90f);
        }

        private void RoadInnerFillet(Vector3 W, Vector3 S, Vector3 E, Vector3 N,
            float apexU, float apexV, float cu, float cv, float rf, float degA, float degB)
        {
            Vector3 P = TileUV(W, S, E, N, apexU, apexV);
            const int seg = 3;
            for (int i = 0; i < seg; i++)
            {
                float t0 = (float)((degA + (degB - degA) * i / seg) * Math.PI / 180.0);
                float t1 = (float)((degA + (degB - degA) * (i + 1) / seg) * Math.PI / 180.0);
                Vector3 a = TileUV(W, S, E, N, cu + rf * (float)Math.Cos(t0), cv + rf * (float)Math.Sin(t0));
                Vector3 b = TileUV(W, S, E, N, cu + rf * (float)Math.Cos(t1), cv + rf * (float)Math.Sin(t1));
                DynamicPrimitiveBatch.Vertex3(P); DynamicPrimitiveBatch.Vertex3(a); DynamicPrimitiveBatch.Vertex3(b); DynamicPrimitiveBatch.Vertex3(P);
            }
        }

        private static float DistXY(Vector3 a, Vector3 b)
        {
            float dx = b.X - a.X, dy = b.Y - a.Y;
            return (float)Math.Sqrt(dx * dx + dy * dy);
        }

        private static Vector3 TileUV(Vector3 W, Vector3 S, Vector3 E, Vector3 N, float u, float v) =>
            (1f - u) * (1f - v) * W + u * (1f - v) * S + u * v * E + (1f - u) * v * N;

        // Szomszédos élpár → a közös sarok (ív-középpont) uv-pozíciója és az ív
        // kezdőszöge (fokban). Minden ív +90°-ot söpör. Szemközti pár esetén false.
        private static bool TryCornerArc(RoadEdge edges, out float startDeg)
        {
            switch (edges)
            {
                case RoadEdge.NW | RoadEdge.WS: startDeg = 0f; return true;    // sarok = W
                case RoadEdge.WS | RoadEdge.SE: startDeg = 90f; return true;   // sarok = S
                case RoadEdge.SE | RoadEdge.EN: startDeg = 180f; return true;  // sarok = E
                case RoadEdge.EN | RoadEdge.NW: startDeg = 270f; return true;  // sarok = N
                default: startDeg = 0f; return false;
            }
        }


        private void RoadArcBand(Vector3 W, Vector3 S, Vector3 E, Vector3 N,
            float startDeg, float rInner, float rOuter)
        {
            // Az ív-középpont (sarok) uv-koordinátája a kezdőszögből.
            float cu = startDeg < 90f ? 0f : startDeg < 180f ? 1f : startDeg < 270f ? 1f : 0f;
            float cv = startDeg < 90f ? 0f : startDeg < 180f ? 0f : startDeg < 270f ? 1f : 1f;

            const int seg = 6;
            for (int i = 0; i < seg; i++)
            {
                float t0 = (float)((startDeg + 90f * i / seg) * Math.PI / 180.0);
                float t1 = (float)((startDeg + 90f * (i + 1) / seg) * Math.PI / 180.0);
                float c0 = (float)Math.Cos(t0), s0 = (float)Math.Sin(t0);
                float c1 = (float)Math.Cos(t1), s1 = (float)Math.Sin(t1);

                DynamicPrimitiveBatch.Vertex3(TileUV(W, S, E, N, cu + rInner * c0, cv + rInner * s0));
                DynamicPrimitiveBatch.Vertex3(TileUV(W, S, E, N, cu + rOuter * c0, cv + rOuter * s0));
                DynamicPrimitiveBatch.Vertex3(TileUV(W, S, E, N, cu + rOuter * c1, cv + rOuter * s1));
                DynamicPrimitiveBatch.Vertex3(TileUV(W, S, E, N, cu + rInner * c1, cv + rInner * s1));
            }
        }

        private void RoadArm(Vector3 c, Vector3 m, float width)
        {
            float dx = m.X - c.X, dy = m.Y - c.Y;
            float len = (float)Math.Sqrt(dx * dx + dy * dy);
            if (len < 1e-4f) return;
            float px = -dy / len * width * 0.5f;
            float py = dx / len * width * 0.5f;

            DynamicPrimitiveBatch.Vertex3(c.X + px, c.Y + py, c.Z);
            DynamicPrimitiveBatch.Vertex3(c.X - px, c.Y - py, c.Z);
            DynamicPrimitiveBatch.Vertex3(m.X - px, m.Y - py, m.Z);
            DynamicPrimitiveBatch.Vertex3(m.X + px, m.Y + py, m.Z);
        }

    }
}

