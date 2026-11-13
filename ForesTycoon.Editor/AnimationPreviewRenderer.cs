using System;
using ForesTycoon.Models;
using OpenTK.Graphics.OpenGL;
using OpenTK.Mathematics;

namespace ForesTycoon.Editor;

/// <summary>Editor-only offscreen adapter using the game's model renderer and animation sampler.</summary>
internal sealed class AnimationPreviewRenderer : IDisposable
{
    private readonly WildlifeAnimationPlayer player;
    private readonly AnimatedGlbModel.Pose pose;
    private readonly AnimatedModelRenderer renderer;
    private int framebuffer, colour, depth;
    private readonly int[] viewport = new int[4];
    private readonly float[] clear = new float[4];
    internal int Texture => colour;
    internal const int Width = 640, Height = 400;
    internal AnimationPreviewRenderer(WildlifeAnimationPlayer player)
    {
        this.player = player; pose = player.Model.CreatePose(); renderer = new(player.Model);
    }
    internal void Render(double time, float blend)
    {
        player.Sample(pose, time, time, blend);
        GL.GetInteger(GetPName.DrawFramebufferBinding, out int draw);
        GL.GetInteger(GetPName.ReadFramebufferBinding, out int read);
        GL.GetInteger(GetPName.Viewport, viewport);
        GL.GetInteger(GetPName.TextureBinding2D, out int texture);
        GL.GetInteger(GetPName.RenderbufferBinding, out int renderbuffer);
        bool scissor = GL.IsEnabled(EnableCap.ScissorTest);
        bool depthTest = GL.IsEnabled(EnableCap.DepthTest);
        GL.GetBoolean(GetPName.DepthWritemask, out bool depthMask);
        GL.GetFloat(GetPName.ColorClearValue, clear);
        try {
            if (framebuffer == 0) {
                framebuffer = GL.GenFramebuffer(); colour = GL.GenTexture(); depth = GL.GenRenderbuffer();
                GL.BindTexture(TextureTarget.Texture2D, colour);
                GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba8, Width, Height, 0, PixelFormat.Rgba, PixelType.UnsignedByte, IntPtr.Zero);
                GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
                GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
                GL.BindRenderbuffer(RenderbufferTarget.Renderbuffer, depth);
                GL.RenderbufferStorage(RenderbufferTarget.Renderbuffer, RenderbufferStorage.DepthComponent24, Width, Height);
                GL.BindFramebuffer(FramebufferTarget.Framebuffer, framebuffer);
                GL.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0, TextureTarget.Texture2D, colour, 0);
                GL.FramebufferRenderbuffer(FramebufferTarget.Framebuffer, FramebufferAttachment.DepthAttachment, RenderbufferTarget.Renderbuffer, depth);
                if (GL.CheckFramebufferStatus(FramebufferTarget.Framebuffer) != FramebufferErrorCode.FramebufferComplete)
                    throw new InvalidOperationException("Nem hozható létre az animáció-előnézet.");
            }
            GL.BindFramebuffer(FramebufferTarget.Framebuffer, framebuffer); GL.Viewport(0, 0, Width, Height);
            GL.Disable(EnableCap.ScissorTest); GL.Enable(EnableCap.DepthTest); GL.DepthMask(true);
            GL.ClearColor(.07f, .09f, .075f, 1); GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);
            Matrix4 camera = Matrix4.CreateTranslation(0, 0, -1.5f) * Matrix4.CreateRotationZ(-.6f)
                * Matrix4.CreateRotationX(-MathF.PI / 3) * Matrix4.CreateOrthographic(8, 5, -100, 100);
            var frame = new ModelRenderFrame(new(camera, Matrix4.Identity, Vector4.Zero, 45, 55, true, false, false, true), true, 0);
            renderer.Draw(pose, WildlifeRenderer.Axis, frame);
        } finally {
            GL.BindFramebuffer(FramebufferTarget.DrawFramebuffer, draw); GL.BindFramebuffer(FramebufferTarget.ReadFramebuffer, read);
            GL.Viewport(viewport[0], viewport[1], viewport[2], viewport[3]);
            GL.BindTexture(TextureTarget.Texture2D, texture); GL.BindRenderbuffer(RenderbufferTarget.Renderbuffer, renderbuffer);
            if (scissor) GL.Enable(EnableCap.ScissorTest); else GL.Disable(EnableCap.ScissorTest);
            if (depthTest) GL.Enable(EnableCap.DepthTest); else GL.Disable(EnableCap.DepthTest);
            GL.DepthMask(depthMask); GL.ClearColor(clear[0], clear[1], clear[2], clear[3]);
        }
    }
    public void Dispose()
    {
        renderer.Dispose();
        if (framebuffer != 0) { GL.DeleteFramebuffer(framebuffer); GL.DeleteTexture(colour); GL.DeleteRenderbuffer(depth); framebuffer = colour = depth = 0; }
    }
}
