using System;
using System.Collections.Generic;
using ForesTycoon.TreeModels;
using OpenTK.Graphics.OpenGL;
using OpenTK.Mathematics;

namespace ForesTycoon.Editor;

/// <summary>
/// Draws tree meshes, as the game builds them, into a texture the editor shows as an image: depth-tested, lit by a sun
/// and a sky, supersampled, with the crown optionally see-through so the branches inside stay visible.
/// </summary>
internal sealed class TreePreviewRenderer : IDisposable
{
    private const string VertexSource = @"#version 330 core
layout(location = 0) in vec3 in_position;
layout(location = 1) in vec3 in_normal;
layout(location = 2) in vec4 in_color;
uniform mat4 view_projection;
uniform vec3 offset;
out vec3 normal;
out vec3 color;
void main()
{
    normal = in_normal;
    color = in_color.rgb;
    gl_Position = view_projection * vec4(in_position + offset, 1.0);
}";
    private const string FragmentSource = @"#version 330 core
in vec3 normal;
in vec3 color;
uniform vec3 sun;
uniform float alpha;
out vec4 fragment;
void main()
{
    vec3 n = length(normal) > 1e-4 ? normalize(normal) : vec3(0.0, 0.0, 1.0);
    if (!gl_FrontFacing) n = -n;
    float direct = max(dot(n, sun), 0.0);
    float sky = 0.5 + 0.5 * n.z;
    fragment = vec4(color * (0.30 + 0.28 * sky + 0.62 * direct), alpha);
}";

    private int program, vao, vbo, framebuffer, colour, depth, width, height;
    private readonly List<(DendroTreeGenerator.Mesh Mesh, int Wood, int WoodCount, int Crown, int CrownCount)> uploaded = new();
    private object uploadedKey;

    internal int Texture => colour;

    /// <summary>
    /// Renders the meshes side by side (one per offset) at <paramref name="pixels"/>; <paramref name="crownAlpha"/> below 1
    /// draws the crowns see-through after the wood.
    /// </summary>
    internal void Render(IReadOnlyList<(DendroTreeGenerator.Mesh Mesh, Vector3 Offset)> trees, Vector2i pixels, Matrix4 viewProjection, float crownAlpha)
    {
        if (pixels.X < 4 || pixels.Y < 4) return;
        if (program == 0) Create();
        Resize(pixels);
        Upload(trees);

        GL.GetInteger(GetPName.DrawFramebufferBinding, out int previousFramebuffer);
        var viewport = new int[4]; GL.GetInteger(GetPName.Viewport, viewport);
        GL.GetInteger(GetPName.CurrentProgram, out int previousProgram);
        GL.GetInteger(GetPName.VertexArrayBinding, out int previousArray);
        bool depthTest = GL.IsEnabled(EnableCap.DepthTest), blend = GL.IsEnabled(EnableCap.Blend), cull = GL.IsEnabled(EnableCap.CullFace),
            scissor = GL.IsEnabled(EnableCap.ScissorTest);
        try {
            GL.BindFramebuffer(FramebufferTarget.Framebuffer, framebuffer);
            GL.Viewport(0, 0, width, height);
            GL.Disable(EnableCap.ScissorTest); GL.Disable(EnableCap.CullFace); GL.Disable(EnableCap.Blend);
            GL.Enable(EnableCap.DepthTest); GL.DepthMask(true); GL.DepthFunc(DepthFunction.Less);
            GL.ClearColor(.105f, .135f, .15f, 1);
            GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);
            GL.UseProgram(program);
            GL.UniformMatrix4(GL.GetUniformLocation(program, "view_projection"), false, ref viewProjection);
            GL.Uniform3(GL.GetUniformLocation(program, "sun"), Vector3.Normalize(new Vector3(-.45f, -.55f, .75f)));
            int offsetLocation = GL.GetUniformLocation(program, "offset"), alphaLocation = GL.GetUniformLocation(program, "alpha");
            GL.BindVertexArray(vao);
            GL.Uniform1(alphaLocation, 1f);
            for (int i = 0; i < uploaded.Count; i++) {
                GL.Uniform3(offsetLocation, trees[i].Offset);
                GL.DrawArrays(PrimitiveType.Triangles, uploaded[i].Wood, uploaded[i].WoodCount);
                if (crownAlpha >= 1) GL.DrawArrays(PrimitiveType.Triangles, uploaded[i].Crown, uploaded[i].CrownCount);
            }
            if (crownAlpha < 1) {
                // See-through crowns: blended over the wood, writing no depth.
                GL.Enable(EnableCap.Blend); GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha); GL.DepthMask(false);
                // Only the near side of a closed crown: stacked layers would turn it opaque again.
                GL.Enable(EnableCap.CullFace); GL.CullFace(CullFaceMode.Back);
                GL.Uniform1(alphaLocation, crownAlpha);
                for (int i = 0; i < uploaded.Count; i++) {
                    GL.Uniform3(offsetLocation, trees[i].Offset);
                    GL.DrawArrays(PrimitiveType.Triangles, uploaded[i].Crown, uploaded[i].CrownCount);
                }
                GL.DepthMask(true); GL.Disable(EnableCap.CullFace);
            }
            GL.BindTexture(TextureTarget.Texture2D, colour);
            GL.GenerateMipmap(GenerateMipmapTarget.Texture2D);
        } finally {
            GL.BindFramebuffer(FramebufferTarget.Framebuffer, previousFramebuffer);
            GL.Viewport(viewport[0], viewport[1], viewport[2], viewport[3]);
            GL.UseProgram(previousProgram); GL.BindVertexArray(previousArray);
            Set(EnableCap.DepthTest, depthTest); Set(EnableCap.Blend, blend); Set(EnableCap.CullFace, cull); Set(EnableCap.ScissorTest, scissor);
        }

        static void Set(EnableCap cap, bool on) { if (on) GL.Enable(cap); else GL.Disable(cap); }
    }

    private void Create()
    {
        program = ForesTycoon.Rendering.GlProgram.Create(VertexSource, FragmentSource);
        vao = GL.GenVertexArray(); vbo = GL.GenBuffer();
        GL.BindVertexArray(vao);
        GL.BindBuffer(BufferTarget.ArrayBuffer, vbo);
        GL.EnableVertexAttribArray(0); GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, Vertex.Stride, 0);
        GL.EnableVertexAttribArray(1); GL.VertexAttribPointer(1, 3, VertexAttribPointerType.Float, false, Vertex.Stride, 12);
        GL.EnableVertexAttribArray(2); GL.VertexAttribPointer(2, 4, VertexAttribPointerType.UnsignedByte, true, Vertex.Stride, 24);
        GL.BindVertexArray(0);
        framebuffer = GL.GenFramebuffer(); colour = GL.GenTexture(); depth = GL.GenRenderbuffer();
    }

    private void Resize(Vector2i pixels)
    {
        if (pixels.X == width && pixels.Y == height) return;
        width = pixels.X; height = pixels.Y;
        GL.BindTexture(TextureTarget.Texture2D, colour);
        GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba8, width, height, 0, PixelFormat.Rgba, PixelType.UnsignedByte, IntPtr.Zero);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.LinearMipmapLinear);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);
        GL.BindRenderbuffer(RenderbufferTarget.Renderbuffer, depth);
        GL.RenderbufferStorage(RenderbufferTarget.Renderbuffer, RenderbufferStorage.DepthComponent24, width, height);
        GL.GetInteger(GetPName.DrawFramebufferBinding, out int previous);
        GL.BindFramebuffer(FramebufferTarget.Framebuffer, framebuffer);
        GL.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0, TextureTarget.Texture2D, colour, 0);
        GL.FramebufferRenderbuffer(FramebufferTarget.Framebuffer, FramebufferAttachment.DepthAttachment, RenderbufferTarget.Renderbuffer, depth);
        var status = GL.CheckFramebufferStatus(FramebufferTarget.Framebuffer);
        GL.BindFramebuffer(FramebufferTarget.Framebuffer, previous);
        if (status != FramebufferErrorCode.FramebufferComplete) throw new InvalidOperationException("Fa-előnézet: " + status);
    }

    private void Upload(IReadOnlyList<(DendroTreeGenerator.Mesh Mesh, Vector3 Offset)> trees)
    {
        // Re-upload only when the set of meshes changes; the camera moves every frame.
        bool same = uploaded.Count == trees.Count;
        for (int i = 0; same && i < trees.Count; i++) same = ReferenceEquals(uploaded[i].Mesh, trees[i].Mesh);
        if (same && uploadedKey != null) return;
        var all = new List<Vertex>(); uploaded.Clear();
        foreach (var (mesh, _) in trees) {
            int wood = all.Count; all.AddRange(mesh.Trunk); all.AddRange(mesh.Branches);
            int crown = all.Count; all.AddRange(mesh.Crown);
            uploaded.Add((mesh, wood, crown - wood, crown, all.Count - crown));
        }
        GL.BindBuffer(BufferTarget.ArrayBuffer, vbo);
        GL.BufferData(BufferTarget.ArrayBuffer, all.Count * Vertex.Stride, all.ToArray(), BufferUsageHint.StaticDraw);
        uploadedKey = trees;
    }

    public void Dispose()
    {
        if (program == 0) return;
        ForesTycoon.Rendering.GlProgram.Delete(program);
        GL.DeleteVertexArray(vao); GL.DeleteBuffer(vbo);
        GL.DeleteFramebuffer(framebuffer); GL.DeleteTexture(colour); GL.DeleteRenderbuffer(depth);
        program = 0;
    }
}
