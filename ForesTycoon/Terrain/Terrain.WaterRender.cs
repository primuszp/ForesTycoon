using System;
using System.Drawing;
using OpenTK.Graphics.OpenGL;
using OpenTK.Mathematics;

namespace ForesTycoon
{
    partial class Terrain
    {
        private void DrawRivers(RenderContext context)
        {
            if (riverNodeIds.Count == 0) return;

            Color riverDeep = Color.FromArgb(195, 38, 118, 188);
            Color riverShallow = Color.FromArgb(130, 68, 155, 218);
            Color riverGrid = Color.FromArgb(160, 105, 185, 238);
            float t = (float)(context.TotalTimeSeconds % 628.318);

            using (new RenderStateScope().AlphaBlend())
            {
                ImmediateRenderer.Draw(PrimitiveType.Quads, () =>
                {
                    for (int u = 0; u < nodeCols - 1; u++)
                    {
                        for (int v = 0; v < nodeRows - 1; v++)
                        {
                            Tile tile = getTileByCoords(u, v);
                            if (HasDynamicWater(tile) || !CanRenderFallbackRiver(tile)) continue;

                            int rc = CountRiverCorners(tile);
                            float baseZ = tile.Low * tileSizeM + RiverWaterHeight;
                            float cx = (tile.W.xPos + tile.E.xPos) * 0.5f;
                            float cy = (tile.W.yPos + tile.N.yPos) * 0.5f;
                            float wz = ApplyClampedWave(cx, cy, baseZ, RiverWaterHeight, t * 1.4f);

                            GL.Color4(rc == 4 ? riverDeep : riverShallow);
                            GL.Vertex3(tile.W.xPos, tile.W.yPos, wz);
                            GL.Vertex3(tile.S.xPos, tile.S.yPos, wz);
                            GL.Vertex3(tile.E.xPos, tile.E.yPos, wz);
                            GL.Vertex3(tile.N.xPos, tile.N.yPos, wz);
                        }
                    }
                });

                ImmediateRenderer.Draw(PrimitiveType.Lines, () =>
                {
                    for (int u = 0; u < nodeCols - 1; u++)
                    {
                        for (int v = 0; v < nodeRows - 1; v++)
                        {
                            Tile tile = getTileByCoords(u, v);
                            if (HasDynamicWater(tile) || !CanRenderFallbackRiver(tile)) continue;

                            int rc = CountRiverCorners(tile);
                            if (rc < 4) continue;

                            float baseZ = tile.Low * tileSizeM + RiverWaterHeight;
                            float ts = t * 1.4f;
                            float zwN = ApplyClampedWave(tile.N.xPos, tile.N.yPos, baseZ, RiverWaterHeight, ts);
                            float zwS = ApplyClampedWave(tile.S.xPos, tile.S.yPos, baseZ, RiverWaterHeight, ts);
                            float zwE = ApplyClampedWave(tile.E.xPos, tile.E.yPos, baseZ, RiverWaterHeight, ts);
                            float zwW = ApplyClampedWave(tile.W.xPos, tile.W.yPos, baseZ, RiverWaterHeight, ts);

                            GL.Color4(riverGrid);
                            GL.Vertex3(tile.W.xPos, tile.W.yPos, zwW); GL.Vertex3(tile.S.xPos, tile.S.yPos, zwS);
                            GL.Vertex3(tile.S.xPos, tile.S.yPos, zwS); GL.Vertex3(tile.E.xPos, tile.E.yPos, zwE);
                            GL.Vertex3(tile.E.xPos, tile.E.yPos, zwE); GL.Vertex3(tile.N.xPos, tile.N.yPos, zwN);
                            GL.Vertex3(tile.N.xPos, tile.N.yPos, zwN); GL.Vertex3(tile.W.xPos, tile.W.yPos, zwW);
                        }
                    }
                });
            }
        }
        private bool ShouldDrawStandingWater(Tile tile) => hydro.ShouldDrawStandingWater(tile);

        private bool CanRenderFallbackRiver(Tile tile) => hydro.CanRenderFallbackRiver(tile);

        // Két egymásra szuperponált hullám egy adott (x,y) pozícióra.
        // Amplitúdó szándékosan kicsi: Transport Tycoon-szerű, finoman remegő felszín.
        private const float WAVE_MAX = 0.36f;

        private float WaveAt(float x, float y, float t)
        {
            const float A1 = 0.20f, F1x = 0.028f, F1y = 0.021f, S1 = 0.95f;
            const float A2 = 0.11f, F2x = 0.052f, F2y = 0.044f, S2 = 1.75f;
            const float A3 = 0.05f, F3x = 0.094f, F3y = 0.070f, S3 = 3.20f;
            float swell  = A1 * (float)Math.Sin(t * S1 + x * F1x + y * F1y);
            float cross  = A2 * (float)Math.Sin(t * S2 - x * F2x + y * F2y + 0.8f);
            float ripple = A3 * (float)Math.Sin(t * S3 + x * F3x - y * F3y + 1.7f);
            return swell + cross + ripple;
        }

        private float GetShoreWaveFactor(float localDepth)
        {
            float usableDepth = Math.Max(0f, localDepth - MinimumWaterDepth);
            if (usableDepth <= 0f) return 0f;
            float normalized = Math.Min(1f, usableDepth / 0.85f);
            float rise = normalized * normalized * (3f - 2f * normalized);
            float fade = 1f - Math.Min(1f, Math.Max(0f, (usableDepth - 0.55f) / 0.80f));
            fade = fade * fade * (3f - 2f * fade);
            return rise * fade;
        }

        private float GetWaveMotionBudget(float localDepth)
        {
            float usableDepth = Math.Max(0f, localDepth - MinimumWaterDepth);
            if (usableDepth <= 0f) return 0f;
            float ramp = Math.Min(1f, usableDepth / 0.90f);
            ramp = ramp * ramp * (3f - 2f * ramp);
            float shore = GetShoreWaveFactor(localDepth);
            return Math.Min(usableDepth * (0.30f + shore * 0.18f),
                            0.025f + ramp * 0.34f + shore * 0.06f);
        }

        private float ApplyClampedWave(float x, float y, float baseWaterZ, float localDepth, float t)
        {
            float wave = WaveAt(x, y, t);
            float shore = GetShoreWaveFactor(localDepth);
            if (shore > 0f)
            {
                wave += 0.09f * shore * (float)Math.Sin(t * 4.1f + x * 0.115f - y * 0.082f + 0.4f)
                      + 0.05f * shore * (float)Math.Sin(t * 5.6f - x * 0.160f + y * 0.126f + 1.3f);
            }
            float budget = GetWaveMotionBudget(localDepth);
            return baseWaterZ + Math.Max(-budget, Math.Min(budget, wave));
        }

        private void WaterVertex(Node node, float wz, float t, Color baseColor)
        {
            float wave = WaveAt(node.xPos, node.yPos, t);
            float n = Math.Max(-1f, Math.Min(1f, wave / WAVE_MAX));
            int shift = (int)(n * 20f);
            GL.Color4(Color.FromArgb(baseColor.A,
                Math.Max(0, Math.Min(255, baseColor.R + shift)),
                Math.Max(0, Math.Min(255, baseColor.G + (int)(shift * 1.4f))),
                Math.Max(0, Math.Min(255, baseColor.B + (int)(shift * 0.6f)))));
            GL.Vertex3(node.xPos, node.yPos, wz);
        }

        private float NodeWaterZ(Node node, float t)
        {
            float depth = nodeWaterDepth[node.Id];
            if (depth < MinimumWaterDepth) return SeaLevel;
            return ApplyClampedWave(node.xPos, node.yPos, node.zPos + depth, depth, t);
        }

        private float GetPolygonPointDepth(Tile tile, Vector3 point)
        {
            if (Math.Abs(point.X - tile.W.xPos) < 0.001f && Math.Abs(point.Y - tile.W.yPos) < 0.001f)
                return Math.Max(MinimumWaterDepth, nodeWaterDepth[tile.W.Id]);
            if (Math.Abs(point.X - tile.S.xPos) < 0.001f && Math.Abs(point.Y - tile.S.yPos) < 0.001f)
                return Math.Max(MinimumWaterDepth, nodeWaterDepth[tile.S.Id]);
            if (Math.Abs(point.X - tile.E.xPos) < 0.001f && Math.Abs(point.Y - tile.E.yPos) < 0.001f)
                return Math.Max(MinimumWaterDepth, nodeWaterDepth[tile.E.Id]);
            if (Math.Abs(point.X - tile.N.xPos) < 0.001f && Math.Abs(point.Y - tile.N.yPos) < 0.001f)
                return Math.Max(MinimumWaterDepth, nodeWaterDepth[tile.N.Id]);

            return MinimumWaterDepth;
        }

        private void DrawWater(RenderContext context)
        {
            if (nodeWaterDepth == null) return;

            Color waterDeep = Color.FromArgb(200, 38, 110, 180);
            Color waterShallow = Color.FromArgb(130, 70, 155, 215);
            Color waterGrid = Color.FromArgb(150, 110, 180, 235);
            float t = (float)(context.TotalTimeSeconds % 628.318);

            using (new RenderStateScope().AlphaBlend().ShadeModel(ShadingModel.Smooth))
            {
                ImmediateRenderer.Draw(PrimitiveType.Quads, () =>
                {
                    for (int u = 0; u < nodeCols - 1; u++)
                    {
                        for (int v = 0; v < nodeRows - 1; v++)
                        {
                            Tile tile = getTileByCoords(u, v);
                            if (!HasDynamicWater(tile)) continue;

                            float wzN = NodeWaterZ(tile.N, t);
                            float wzS = NodeWaterZ(tile.S, t);
                            float wzE = NodeWaterZ(tile.E, t);
                            float wzW = NodeWaterZ(tile.W, t);

                            int wetCount = 0;
                            if (nodeWaterDepth[tile.N.Id] >= MinimumWaterDepth) wetCount++;
                            if (nodeWaterDepth[tile.S.Id] >= MinimumWaterDepth) wetCount++;
                            if (nodeWaterDepth[tile.E.Id] >= MinimumWaterDepth) wetCount++;
                            if (nodeWaterDepth[tile.W.Id] >= MinimumWaterDepth) wetCount++;

                            Color baseColor = wetCount >= 3 ? waterDeep : waterShallow;
                            WaterVertex(tile.W, wzW, t, baseColor);
                            WaterVertex(tile.S, wzS, t, baseColor);
                            WaterVertex(tile.E, wzE, t, baseColor);
                            WaterVertex(tile.N, wzN, t, baseColor);
                        }
                    }
                });
            }

            using (new RenderStateScope().AlphaBlend())
            {
                ImmediateRenderer.Draw(PrimitiveType.Lines, () =>
                {
                    for (int u = 0; u < nodeCols - 1; u++)
                    {
                        for (int v = 0; v < nodeRows - 1; v++)
                        {
                            Tile tile = getTileByCoords(u, v);
                            if (nodeWaterDepth[tile.N.Id] < MinimumWaterDepth) continue;
                            if (nodeWaterDepth[tile.S.Id] < MinimumWaterDepth) continue;
                            if (nodeWaterDepth[tile.E.Id] < MinimumWaterDepth) continue;
                            if (nodeWaterDepth[tile.W.Id] < MinimumWaterDepth) continue;

                            float zwN = NodeWaterZ(tile.N, t);
                            float zwS = NodeWaterZ(tile.S, t);
                            float zwE = NodeWaterZ(tile.E, t);
                            float zwW = NodeWaterZ(tile.W, t);

                            GL.Color4(waterGrid);
                            GL.Vertex3(tile.W.xPos, tile.W.yPos, zwW); GL.Vertex3(tile.S.xPos, tile.S.yPos, zwS);
                            GL.Vertex3(tile.S.xPos, tile.S.yPos, zwS); GL.Vertex3(tile.E.xPos, tile.E.yPos, zwE);
                            GL.Vertex3(tile.E.xPos, tile.E.yPos, zwE); GL.Vertex3(tile.N.xPos, tile.N.yPos, zwN);
                            GL.Vertex3(tile.N.xPos, tile.N.yPos, zwN); GL.Vertex3(tile.W.xPos, tile.W.yPos, zwW);
                        }
                    }
                });
            }
        }
        private void DrawWaterWalls(RenderContext context)
        {
            if (nodeWaterDepth == null) return;

            float t = (float)(context.TotalTimeSeconds % 628.318);

            using (new RenderStateScope().AlphaBlend().ShadeModel(ShadingModel.Smooth))
            {
                ImmediateRenderer.Draw(PrimitiveType.Quads, () =>
                {
                    for (int u = 0; u < nodeCols - 1; u++)
                    {
                        for (int v = 0; v < nodeRows - 1; v++)
                        {
                            Tile tile = getTileByCoords(u, v);
                            if (!HasDynamicWater(tile)) continue;

                            TryDrawWaterWall(tile.W, tile.S, checkTile(u, v - 1) ? getTileByCoords(u, v - 1) : null, t);
                            TryDrawWaterWall(tile.S, tile.E, checkTile(u + 1, v) ? getTileByCoords(u + 1, v) : null, t);
                            TryDrawWaterWall(tile.E, tile.N, checkTile(u, v + 1) ? getTileByCoords(u, v + 1) : null, t);
                            TryDrawWaterWall(tile.N, tile.W, checkTile(u - 1, v) ? getTileByCoords(u - 1, v) : null, t);
                        }
                    }
                });
            }
        }
        private void TryDrawWaterWall(Node a, Node b, Tile neighbor, float t)
        {
            if (neighbor != null && HasDynamicWater(neighbor)) return;

            float dA = nodeWaterDepth[a.Id];
            float dB = nodeWaterDepth[b.Id];
            if (dA < MinimumWaterDepth && dB < MinimumWaterDepth) return;

            float wzA = dA >= MinimumWaterDepth ? a.zPos + dA + WaveAt(a.xPos, a.yPos, t) : a.zPos;
            float wzB = dB >= MinimumWaterDepth ? b.zPos + dB + WaveAt(b.xPos, b.yPos, t) : b.zPos;

            if (wzA <= a.zPos + 0.02f && wzB <= b.zPos + 0.02f) return;

            // Vízfelszínnél: félátlátszó kék; aljnál: sötét mélykék
            GL.Color4(Color.FromArgb(155,  55, 130, 195)); GL.Vertex3(a.xPos, a.yPos, wzA);
            GL.Color4(Color.FromArgb(155,  55, 130, 195)); GL.Vertex3(b.xPos, b.yPos, wzB);
            GL.Color4(Color.FromArgb(225,   8,  35,  85)); GL.Vertex3(b.xPos, b.yPos, b.zPos);
            GL.Color4(Color.FromArgb(225,   8,  35,  85)); GL.Vertex3(a.xPos, a.yPos, a.zPos);
        }

    }
}



