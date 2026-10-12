using System;
using System.IO;
using ImGuiNET;
using OpenTK.Graphics.OpenGL;
using OpenTK.Windowing.Common;
using OpenTK.Windowing.Desktop;
using Vector2 = System.Numerics.Vector2;

namespace ForesTycoon;

internal static class UiRenderSmokeTest
{
    internal static void Run()
    {
        const int width = 640, height = 480;
        using var window = new NativeWindow(new NativeWindowSettings {
            StartVisible = false, ClientSize = new(width, height), API = ContextAPI.OpenGL,
            APIVersion = new(3, 3), Profile = ContextProfile.Core });
        window.Context.MakeCurrent();
        RenderDevice.Initialize();
        using var ui = new ImGuiController();
        byte[] reference = null;
        for (int frame = 0; frame < 12; frame++)
        {
            GL.Enable(EnableCap.DepthTest);
            GL.Disable(EnableCap.ScissorTest);
            GL.Viewport(0, 0, width, height);
            GL.ClearColor(.1f, .15f, .2f, 1);
            GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);
            ui.MouseMove(frame % 2 == 0 ? 100 : 550, 100);
            ui.Update(width, height, width, height, Vector2.One, 1f / 60);
            var background = ImGui.GetBackgroundDrawList();
            // Exceed both the initial UI buffer capacity and the 16-bit vertex-index range.
            for (int i = 0; i < 18000; i++)
                background.AddRectFilled(new(10 + i % 200, 10 + i / 200), new(11 + i % 200, 11 + i / 200), 0xff40c060);
            var foreground = ImGui.GetForegroundDrawList();
            foreground.AddRectFilled(new(300, 100), new(500, 300), 0xffe0a040);
            if (frame % 2 != 0)
                foreground.AddRectFilled(new(520, 20), new(620, 80), 0xff4080e0);
            ui.Render();
            string output = Path.GetFullPath("artifacts/ui-render");
            Directory.CreateDirectory(output);
            FramebufferCapture.SavePng(Path.Combine(output, $"frame-{frame:D2}.png"), width, height);
            if (!GL.IsEnabled(EnableCap.DepthTest))
                throw new InvalidOperationException("UI rendering did not restore depth testing.");
            if (GL.GetError() != ErrorCode.NoError)
                throw new InvalidOperationException("UI rendering produced an OpenGL error.");
            var pixels = new byte[width * height * 4];
            GL.ReadPixels(0, 0, width, height, PixelFormat.Rgba, PixelType.UnsignedByte, pixels);
            if (frame == 2) reference = pixels;
            if (frame > 2 && frame % 2 == 0 && !reference.AsSpan().SequenceEqual(pixels))
                throw new InvalidOperationException("Identical UI frames differ after changing draw-list size.");
        }
        Console.WriteLine("UI render smoke passed: changing draw lists, buffer growth, 16-bit index rollover, stable pixels and depth restoration.");
    }
}
