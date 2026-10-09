using System;
using System.IO;
using ImGuiNET;
using OpenTK.Graphics.OpenGL;
using OpenTK.Windowing.Common;
using OpenTK.Windowing.Desktop;
using V2 = System.Numerics.Vector2;

namespace ForesTycoon
{
    // Approval-only concepts. Production GameIcons and HUD styling remain unchanged.
    internal static class TycoonIconPreview
    {
        internal static void Run()
        {
            const int width = 960, height = 470;
            using var window = new NativeWindow(new NativeWindowSettings {
                StartVisible = false, ClientSize = new(width, height), API = ContextAPI.OpenGL,
                APIVersion = new(3, 3), Profile = ContextProfile.Core });
            window.Context.MakeCurrent();
            using var ui = new ImGuiController();
            HudTheme.Apply();
            ui.Update(width, height, width, height, V2.One, 1f / 60);
            GL.Viewport(0, 0, width, height);
            GL.ClearColor(0.078f, 0.102f, 0.086f, 1);
            GL.Clear(ClearBufferMask.ColorBufferBit);
            var dl = ImGui.GetBackgroundDrawList();
            uint text = GameIcons.Color(HudTheme.Parchment), muted = GameIcons.Color(HudTheme.Muted);
            dl.AddText(new(28, 22), text, "FORESTYCOON  /  IKONPRÓBA");
            dl.AddText(new(28, 49), muted, "Izometrikus miniatűrök · moha, pergamen és mézborostyán · két mintaterv");
            Card(24, "RÖNKSZÁLLÍTÓ", GameIcon.Truck, false);
            Card(488, "ERDŐTELEPÍTÉS", GameIcon.Plant, true);
            ui.Render(); GL.Finish();
            if (GL.GetError() != ErrorCode.NoError) throw new InvalidOperationException("Icon preview OpenGL error.");
            string output = Path.GetFullPath("artifacts/icon-preview"); Directory.CreateDirectory(output);
            FramebufferCapture.SavePng(Path.Combine(output, "tycoon-concepts.png"), width, height);
            Console.WriteLine($"Two native vector icon concepts rendered: {output}/tycoon-concepts.png");

            void Card(float x, string title, GameIcon original, bool plant)
            {
                dl.AddRectFilled(new(x, 90), new(x + 448, 444), GameIcons.Rgb(29, 38, 32), 10);
                dl.AddText(new(x + 20, 108), GameIcons.Color(HudTheme.AmberAccent), title);
                dl.AddText(new(x + 26, 143), muted, "Jelenlegi");
                dl.AddText(new(x + 211, 143), muted, "Új stílus / nagyítva");
                Button(new(x + 35, 194), 96);
                GameIcons.Draw(dl, original, new(x + 45, 204), 76, text);
                Button(new(x + 226, 176), 148);
                Draw(dl, new(x + 236, 186), 128, plant);
                dl.AddText(new(x + 20, 337), muted, "Tényleges gombméretek");
                foreach (int size in new[] { 28, 36, 48 })
                {
                    float bx = x + 20 + (size == 28 ? 0 : size == 36 ? 68 : 144);
                    Button(new(bx, 365), size + 10);
                    Draw(dl, new(bx + 5, 370), size, plant);
                    dl.AddText(new(bx, 423), muted, $"{size} px");
                }
            }
            void Button(V2 p, float size)
            {
                dl.AddRectFilled(p, p + new V2(size), GameIcons.Rgb(30, 38, 31), 6);
                dl.AddRect(p, p + new V2(size), GameIcons.Rgb(73, 87, 65), 6);
                dl.AddLine(p + new V2(6, 1), p + new V2(size - 6, 1), GameIcons.Rgb(126, 138, 116, 70));
            }
        }

        private static void Draw(ImDrawListPtr dl, V2 origin, float size, bool plant)
        {
            var c = new IsoCanvas(dl, origin, size);
            if (plant)
            {
                c.Box(0.05f, 0.05f, 0, 0.91f, 0.91f, 0.08f, GameIcons.Color(HudTheme.Moss), GameIcons.Rgb(125, 91, 51), GameIcons.Rgb(87, 65, 39));
                c.Tree(0.62f, 0.64f, 0.9f);
                c.Tree(0.28f, 0.29f, 1.0f);
                c.Tree(0.70f, 0.20f, 0.57f);
                V2 badge = origin + new V2(0.84f, 0.81f) * size;
                dl.AddCircleFilled(badge + new V2(0, size * 0.018f), size * 0.106f, GameIcons.Rgb(22, 29, 21));
                dl.AddCircleFilled(badge, size * 0.10f, GameIcons.Color(HudTheme.AmberAccent));
                float r = size * 0.047f, weight = Math.Max(1.2f, size * 0.022f);
                dl.AddLine(badge - new V2(r, 0), badge + new V2(r, 0), GameIcons.Rgb(58, 69, 39), weight);
                dl.AddLine(badge - new V2(0, r), badge + new V2(0, r), GameIcons.Rgb(58, 69, 39), weight);
            }
            else
            {
                c.Box(0.03f, 0.01f, 0.13f, 0.75f, 0.42f, 0.21f, GameIcons.Rgb(100, 109, 101), GameIcons.Rgb(58, 66, 61), GameIcons.Rgb(39, 48, 45));
                c.Box(0.72f, 0.01f, 0.15f, 1.04f, 0.42f, 0.52f, GameIcons.Color(HudTheme.Info), GameIcons.Rgb(100, 158, 187), GameIcons.Rgb(65, 108, 139));
                c.Face(GameIcons.Rgb(41, 68, 77), c.P(0.77f, 0.002f, 0.45f), c.P(0.98f, 0.002f, 0.45f), c.P(0.98f, 0.002f, 0.30f), c.P(0.77f, 0.002f, 0.30f));
                c.Face(GameIcons.Rgb(52, 83, 97), c.P(1.041f, 0.06f, 0.45f), c.P(1.041f, 0.35f, 0.45f), c.P(1.041f, 0.35f, 0.31f), c.P(1.041f, 0.06f, 0.31f));
                c.Line(0.77f, 0, 0.45f, 0.95f, 0, 0.45f, GameIcons.Rgb(177, 211, 214), 0.010f);
                c.Log(0.08f, 0.68f, 0.30f, 0.28f);
                c.Log(0.08f, 0.68f, 0.10f, 0.28f);
                c.Log(0.08f, 0.68f, 0.20f, 0.40f);
                foreach (float x in new[] { 0.11f, 0.63f })
                {
                    c.Line(x, 0.005f, 0.20f, x, 0.005f, 0.48f, GameIcons.Rgb(65, 72, 66), 0.022f);
                    c.Line(x, 0.005f, 0.45f, x, 0.005f, 0.49f, GameIcons.Rgb(178, 186, 163), 0.022f);
                }
                foreach (float x in new[] { 0.19f, 0.38f, 0.86f }) c.Wheel(x, -0.014f, 0.115f);
                c.Line(1.05f, 0.05f, 0.21f, 1.05f, 0.36f, 0.21f, GameIcons.Rgb(194, 201, 186), 0.022f);
            }
        }

        private readonly struct IsoCanvas
        {
            private readonly ImDrawListPtr dl;
            private readonly V2 origin;
            private readonly float size;
            internal IsoCanvas(ImDrawListPtr dl, V2 origin, float size) { this.dl = dl; this.origin = origin; this.size = size; }
            internal V2 P(float x, float y, float z) => origin + new V2(0.48f + (x - y) * 0.44f, 0.87f - (x + y) * 0.21f - z * 0.61f) * size;
            internal void Face(uint color, params V2[] points)
            {
                float area = 0;
                for (int i = 0; i < points.Length; i++)
                {
                    V2 a = points[i], b = points[(i + 1) % points.Length];
                    area += a.X * b.Y - b.X * a.Y;
                }
                // ImGui's antialiased convex fill expects clockwise screen-space winding.
                if (area < 0) Array.Reverse(points);
                foreach (var p in points) dl.PathLineTo(p);
                dl.PathFillConvex(color);
            }
            internal void Line(float x, float y, float z, float xx, float yy, float zz, uint color, float weight) =>
                dl.AddLine(P(x, y, z), P(xx, yy, zz), color, Math.Max(1, size * weight));
            internal void Box(float x, float y, float z, float xx, float yy, float zz, uint top, uint side, uint end)
            {
                Face(side, P(x, y, z), P(xx, y, z), P(xx, y, zz), P(x, y, zz));
                Face(end, P(xx, y, z), P(xx, yy, z), P(xx, yy, zz), P(xx, y, zz));
                Face(top, P(x, y, zz), P(xx, y, zz), P(xx, yy, zz), P(x, yy, zz));
            }
            internal void Log(float x, float xx, float y, float z)
            {
                Line(x, y, z, xx, y, z, GameIcons.Rgb(120, 79, 46), 0.072f);
                Line(x, y, z + 0.028f, xx, y, z + 0.028f, GameIcons.Rgb(186, 135, 78), 0.026f);
                dl.AddEllipseFilled(P(xx, y, z), new V2(size * 0.025f, size * 0.038f), GameIcons.Rgb(221, 181, 122));
                dl.AddEllipseFilled(P(xx, y, z), new V2(size * 0.011f, size * 0.017f), GameIcons.Rgb(175, 124, 69));
            }
            internal void Wheel(float x, float y, float z)
            {
                dl.AddEllipseFilled(P(x, y, z), new V2(size * 0.050f, size * 0.066f), GameIcons.Rgb(20, 27, 25));
                dl.AddEllipseFilled(P(x, y, z), new V2(size * 0.024f, size * 0.033f), GameIcons.Rgb(129, 140, 132));
                dl.AddEllipseFilled(P(x, y, z), new V2(size * 0.010f, size * 0.015f), GameIcons.Rgb(61, 75, 69));
            }
            internal void Tree(float x, float y, float scale)
            {
                Box(x - 0.024f, y - 0.024f, 0.08f, x + 0.024f, y + 0.024f, 0.24f * scale,
                    GameIcons.Rgb(184, 137, 77), GameIcons.Rgb(155, 111, 59), GameIcons.Rgb(91, 69, 42));
                for (int i = 0; i < 3; i++)
                {
                    float r = (0.24f - i * 0.052f) * scale;
                    float bottom = (0.19f + i * 0.12f) * scale, peak = (0.47f + i * 0.115f) * scale;
                    V2 tip = P(x, y, peak);
                    Face(GameIcons.Color(HudTheme.Moss), tip, P(x - r, y, bottom), P(x, y + r, bottom));
                    Face(GameIcons.Color(HudTheme.MossBright), tip, P(x - r, y, bottom), P(x, y - r, bottom));
                    Face(GameIcons.Rgb(57, 100, 51), tip, P(x, y - r, bottom), P(x + r, y, bottom));
                }
            }
        }
    }
}
