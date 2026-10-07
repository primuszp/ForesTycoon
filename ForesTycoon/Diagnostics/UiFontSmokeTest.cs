using System;
using System.IO;
using ImGuiNET;
using OpenTK.Graphics.OpenGL;
using OpenTK.Windowing.Common;
using OpenTK.Windowing.Desktop;
using Vector2 = System.Numerics.Vector2;

namespace ForesTycoon
{
    // Uses the actual UI font loader and renderer, including the uploaded font atlas.
    internal static class UiFontSmokeTest
    {
        internal static void Run()
        {
            const int width = 760, height = 180;
            using var window = new NativeWindow(new NativeWindowSettings {
                StartVisible = false, ClientSize = new(width, height), API = ContextAPI.OpenGL,
                APIVersion = new(3, 3), Profile = ContextProfile.Core });
            window.Context.MakeCurrent();
            using var ui = new ImGuiController();
            ui.Update(width, height, width, height, Vector2.One, 1f / 60);
            var font = ImGui.GetFont();
            const string accents = "áéíóöőúüűÁÉÍÓÖŐÚÜŰ";
            foreach (char c in accents + "–—…")
            {
                if (c >= font.IndexLookup.Size || font.IndexLookup[c] == ushort.MaxValue)
                    throw new InvalidOperationException($"UI font is missing U+{(int)c:X4} ({c}).");
            }
            GL.Viewport(0, 0, width, height);
            GL.ClearColor(0.08f, 0.10f, 0.12f, 1);
            GL.Clear(ClearBufferMask.ColorBufferBit);
            var draw = ImGui.GetBackgroundDrawList();
            draw.AddText(new(24, 24), 0xffffffff, "Árvíztűrő tükörfúrógép – Erdőgazdálkodás");
            draw.AddText(new(24, 60), 0xffffffff, accents);
            draw.AddText(new(24, 96), 0xffffffff, "Tölgy, bükk, nyír – Eljárásos fák (kor és fény)");
            ui.Render();
            if (GL.GetError() != ErrorCode.NoError)
                throw new InvalidOperationException("UI font rendering produced an OpenGL error.");
            string output = Path.GetFullPath("artifacts/ui-font");
            Directory.CreateDirectory(output);
            FramebufferCapture.SavePng(Path.Combine(output, "hungarian.png"), width, height);
            Console.WriteLine($"UI font smoke passed: all 18 Hungarian accented glyphs and dash/ellipsis glyphs present; rendered {output}/hungarian.png");
        }
    }
}
