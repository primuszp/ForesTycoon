using System;
using System.IO;
using ImGuiNET;
using OpenTK.Graphics.OpenGL;
using OpenTK.Windowing.Common;
using OpenTK.Windowing.Desktop;
using V2 = System.Numerics.Vector2;

namespace ForesTycoon
{
    internal static class ForestIconSmokeTest
    {
        internal static void Run()
        {
            var icons = Enum.GetValues<GameIcon>();
            if (icons.Length != GameIconAtlas.IconCount) throw new InvalidOperationException("Icon atlas coverage changed.");
            const int width = 1200, height = 860;
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
            uint ink = GameIcons.Color(HudTheme.Parchment);
            dl.AddText(new(24, 16), ink, "FORESTYCOON / TRANSPORT IKONKÉSZLET / 46 IKON");
            dl.AddText(new(24, 40), GameIcons.Color(HudTheme.Muted), "ImageGen · 64 px és 28 px · a játék tényleges megjelenítőjével");
            foreach (var icon in icons)
            {
                int index = (int)icon;
                var p = new V2(20 + index % 8 * 146, 78 + index / 8 * 127);
                dl.AddRectFilled(p, p + new V2(138, 119), GameIcons.Color(HudTheme.PanelLight), 8);
                if (!GameIconAtlas.Draw(dl, icon, p + new V2(8, 10), 64, ink))
                    throw new InvalidOperationException($"Atlas missing {icon}.");
                GameIcons.Draw(dl, icon, p + new V2(91, 28), 28, ink);
                dl.AddText(p + new V2(8, 91), GameIcons.Color(HudTheme.Muted), icon.ToString());
            }
            ui.Render();
            GL.Finish();
            if (GL.GetError() != ErrorCode.NoError) throw new InvalidOperationException("Icon rendering OpenGL error.");
            string output = Path.GetFullPath("artifacts/icon-preview/forest-icons-transport.png");
            Directory.CreateDirectory(Path.GetDirectoryName(output));
            FramebufferCapture.SavePng(output, width, height);
            Console.WriteLine($"All {icons.Length} forest icons rendered at 64 and 28 px: {output}");
        }
    }
}
