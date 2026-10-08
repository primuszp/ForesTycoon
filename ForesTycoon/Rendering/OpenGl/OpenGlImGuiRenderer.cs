using System;
using System.Runtime.CompilerServices;
using ImGuiNET;
using OpenTK.Graphics.OpenGL;
using OpenTK.Mathematics;
namespace ForesTycoon.OpenGl
{
    internal sealed class OpenGlImGuiRenderer : IUiRenderBackend
    {
        private int vao, vbo, ebo;
        private int vboSize, eboSize;
        private int shader;
        private int projLoc, texLoc;
        private int fontTexture;
        private static readonly int VertSize = Unsafe.SizeOf<ImDrawVert>();
        public void Initialize(ImGuiIOPtr io)
        {
            io.BackendFlags |= ImGuiBackendFlags.RendererHasVtxOffset;
            vbo = GL.GenBuffer();
            ebo = GL.GenBuffer();
            vboSize = 10000 * VertSize;
            eboSize = 2000 * sizeof(ushort);
            GL.BindBuffer(BufferTarget.ArrayBuffer, vbo);
            GL.BufferData(BufferTarget.ArrayBuffer, vboSize, IntPtr.Zero, BufferUsageHint.DynamicDraw);
            GL.BindBuffer(BufferTarget.ElementArrayBuffer, ebo);
            GL.BufferData(BufferTarget.ElementArrayBuffer, eboSize, IntPtr.Zero, BufferUsageHint.DynamicDraw);

            shader = BuildShader();
            projLoc = GlProgram.Uniform(shader, "projection_matrix");
            texLoc = GlProgram.Uniform(shader, "in_fontTexture");

            vao = GL.GenVertexArray();
            GL.BindVertexArray(vao);
            GL.BindBuffer(BufferTarget.ArrayBuffer, vbo);
            GL.BindBuffer(BufferTarget.ElementArrayBuffer, ebo);
            GL.EnableVertexAttribArray(0);
            GL.VertexAttribPointer(0, 2, VertexAttribPointerType.Float, false, VertSize, 0);
            GL.EnableVertexAttribArray(1);
            GL.VertexAttribPointer(1, 2, VertexAttribPointerType.Float, false, VertSize, 8);
            GL.EnableVertexAttribArray(2);
            GL.VertexAttribPointer(2, 4, VertexAttribPointerType.UnsignedByte, true, VertSize, 16);
            GL.BindVertexArray(0);

            RecreateFontDeviceTexture(io);
        }

        private static int BuildShader()
        {
            const string vs = @"#version 330 core
uniform mat4 projection_matrix;
layout(location=0) in vec2 in_position;
layout(location=1) in vec2 in_texCoord;
layout(location=2) in vec4 in_color;
out vec4 color;
out vec2 texCoord;
void main()
{
    gl_Position = projection_matrix * vec4(in_position, 0, 1);
    color = in_color;
    texCoord = in_texCoord;
}";
            const string fs = @"#version 330 core
uniform sampler2D in_fontTexture;
in vec4 color;
in vec2 texCoord;
out vec4 outputColor;
void main()
{
    outputColor = color * texture(in_fontTexture, texCoord);
}";
            return GlProgram.Create(vs, fs);
        }

        private void RecreateFontDeviceTexture(ImGuiIOPtr io)
        {
            io.Fonts.GetTexDataAsRGBA32(out IntPtr pixels, out int width, out int height, out _);

            fontTexture = GL.GenTexture();
            GL.BindTexture(TextureTarget.Texture2D, fontTexture);
            GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba, width, height, 0,
                PixelFormat.Rgba, PixelType.UnsignedByte, pixels);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);

            io.Fonts.SetTexID((IntPtr)fontTexture);
            io.Fonts.ClearTexData();
        }

        public void Draw(ImDrawDataPtr drawData)
        {
            if (drawData.CmdListsCount == 0) return;

            using (new ImGuiRenderStateScope())
            {
                GL.Enable(EnableCap.Blend);
                GL.BlendEquation(BlendEquationMode.FuncAdd);
                GL.BlendFuncSeparate(BlendingFactorSrc.SrcAlpha, BlendingFactorDest.OneMinusSrcAlpha,
                                     BlendingFactorSrc.One, BlendingFactorDest.OneMinusSrcAlpha);
                GL.Disable(EnableCap.CullFace);
                GL.Disable(EnableCap.DepthTest);
                GL.Enable(EnableCap.ScissorTest);

                int framebufferWidth = (int)(drawData.DisplaySize.X * drawData.FramebufferScale.X);
                int framebufferHeight = (int)(drawData.DisplaySize.Y * drawData.FramebufferScale.Y);
                if (framebufferWidth <= 0 || framebufferHeight <= 0) return;

                GL.Viewport(0, 0, framebufferWidth, framebufferHeight);

                float left = drawData.DisplayPos.X;
                float right = drawData.DisplayPos.X + drawData.DisplaySize.X;
                float top = drawData.DisplayPos.Y;
                float bottom = drawData.DisplayPos.Y + drawData.DisplaySize.Y;
                Matrix4 mvp = Matrix4.CreateOrthographicOffCenter(left, right, bottom, top, -1f, 1f);

                GL.UseProgram(shader);
                GL.UniformMatrix4(projLoc, false, ref mvp);
                GL.Uniform1(texLoc, 0);
                GL.ActiveTexture(TextureUnit.Texture0);
                GL.BindVertexArray(vao);

                for (int n = 0; n < drawData.CmdListsCount; n++)
                {
                    ImDrawListPtr cmdList = drawData.CmdLists[n];

                    int vtxBytes = cmdList.VtxBuffer.Size * VertSize;
                    int idxBytes = cmdList.IdxBuffer.Size * sizeof(ushort);

                    GL.BindBuffer(BufferTarget.ArrayBuffer, vbo);
                    if (vtxBytes > vboSize)
                    {
                        vboSize = Math.Max(vboSize * 2, vtxBytes);
                        GL.BufferData(BufferTarget.ArrayBuffer, vboSize, IntPtr.Zero, BufferUsageHint.DynamicDraw);
                    }
                    GL.BufferSubData(BufferTarget.ArrayBuffer, IntPtr.Zero, vtxBytes, cmdList.VtxBuffer.Data);

                    GL.BindBuffer(BufferTarget.ElementArrayBuffer, ebo);
                    if (idxBytes > eboSize)
                    {
                        eboSize = Math.Max(eboSize * 2, idxBytes);
                        GL.BufferData(BufferTarget.ElementArrayBuffer, eboSize, IntPtr.Zero, BufferUsageHint.DynamicDraw);
                    }
                    GL.BufferSubData(BufferTarget.ElementArrayBuffer, IntPtr.Zero, idxBytes, cmdList.IdxBuffer.Data);

                    for (int c = 0; c < cmdList.CmdBuffer.Size; c++)
                    {
                        ImDrawCmdPtr cmd = cmdList.CmdBuffer[c];
                        System.Numerics.Vector4 clip = cmd.ClipRect;
                        float clipX = (clip.X - drawData.DisplayPos.X) * drawData.FramebufferScale.X;
                        float clipY = (clip.Y - drawData.DisplayPos.Y) * drawData.FramebufferScale.Y;
                        float clipZ = (clip.Z - drawData.DisplayPos.X) * drawData.FramebufferScale.X;
                        float clipW = (clip.W - drawData.DisplayPos.Y) * drawData.FramebufferScale.Y;

                        if (clipX < framebufferWidth && clipY < framebufferHeight && clipZ >= 0.0f && clipW >= 0.0f)
                        {
                            int scissorX = Math.Max(0, (int)clipX);
                            int scissorY = Math.Max(0, framebufferHeight - (int)clipW);
                            int scissorW = Math.Min(framebufferWidth, (int)clipZ) - scissorX;
                            int scissorH = Math.Min(framebufferHeight, framebufferHeight - (int)clipY) - scissorY;
                            if (scissorW <= 0 || scissorH <= 0) continue;

                            GL.BindTexture(TextureTarget.Texture2D, (int)cmd.TextureId);
                            GL.Scissor(scissorX, scissorY, scissorW, scissorH);
                            GL.DrawElementsBaseVertex(PrimitiveType.Triangles, (int)cmd.ElemCount,
                                DrawElementsType.UnsignedShort, (IntPtr)(cmd.IdxOffset * sizeof(ushort)), (int)cmd.VtxOffset);
                            RenderMetrics.RecordDraw((int)cmd.ElemCount);
                        }
                    }
                }
            }
        }

        public void Dispose()
        {
            GL.DeleteVertexArray(vao);
            GL.DeleteBuffer(vbo);
            GL.DeleteBuffer(ebo);
            GL.DeleteTexture(fontTexture);
            GlProgram.Delete(shader);
        }

        private sealed class ImGuiRenderStateScope : IDisposable
        {
            private readonly bool depthTest;
            private readonly bool blend;
            private readonly bool cullFace;
            private readonly bool scissorTest;
            private readonly int currentProgram;
            private readonly int vertexArray;
            private readonly int arrayBuffer;
            private readonly int elementArrayBuffer;
            private readonly int textureBinding2D;
            private readonly int activeTexture;
            private readonly int blendEquationRgb;
            private readonly int blendEquationAlpha;
            private readonly int blendSrcRgb;
            private readonly int blendDstRgb;
            private readonly int blendSrcAlpha;
            private readonly int blendDstAlpha;
            private readonly int[] viewport = new int[4];
            private readonly int[] scissorBox = new int[4];
            private bool disposed;

            public ImGuiRenderStateScope()
            {
                depthTest = GL.IsEnabled(EnableCap.DepthTest);
                blend = GL.IsEnabled(EnableCap.Blend);
                cullFace = GL.IsEnabled(EnableCap.CullFace);
                scissorTest = GL.IsEnabled(EnableCap.ScissorTest);
                GL.GetInteger(GetPName.CurrentProgram, out currentProgram);
                GL.GetInteger(GetPName.VertexArrayBinding, out vertexArray);
                GL.GetInteger(GetPName.ArrayBufferBinding, out arrayBuffer);
                GL.GetInteger(GetPName.ElementArrayBufferBinding, out elementArrayBuffer);
                GL.GetInteger(GetPName.ActiveTexture, out activeTexture);
                GL.ActiveTexture(TextureUnit.Texture0);
                GL.GetInteger(GetPName.TextureBinding2D, out textureBinding2D);
                GL.ActiveTexture((TextureUnit)activeTexture);
                GL.GetInteger(GetPName.BlendEquationRgb, out blendEquationRgb);
                GL.GetInteger(GetPName.BlendEquationAlpha, out blendEquationAlpha);
                GL.GetInteger(GetPName.BlendSrcRgb, out blendSrcRgb);
                GL.GetInteger(GetPName.BlendDstRgb, out blendDstRgb);
                GL.GetInteger(GetPName.BlendSrcAlpha, out blendSrcAlpha);
                GL.GetInteger(GetPName.BlendDstAlpha, out blendDstAlpha);
                GL.GetInteger(GetPName.Viewport, viewport);
                GL.GetInteger(GetPName.ScissorBox, scissorBox);
            }

            public void Dispose()
            {
                if (disposed) return;
                disposed = true;

                Restore(EnableCap.DepthTest, depthTest);
                Restore(EnableCap.Blend, blend);
                Restore(EnableCap.CullFace, cullFace);
                Restore(EnableCap.ScissorTest, scissorTest);
                GL.UseProgram(currentProgram);
                GL.BindVertexArray(vertexArray);
                GL.BindBuffer(BufferTarget.ArrayBuffer, arrayBuffer);
                GL.BindBuffer(BufferTarget.ElementArrayBuffer, elementArrayBuffer);
                GL.ActiveTexture(TextureUnit.Texture0);
                GL.BindTexture(TextureTarget.Texture2D, textureBinding2D);
                GL.ActiveTexture((TextureUnit)activeTexture);
                GL.BlendEquationSeparate((BlendEquationMode)blendEquationRgb, (BlendEquationMode)blendEquationAlpha);
                GL.BlendFuncSeparate(
                    (BlendingFactorSrc)blendSrcRgb,
                    (BlendingFactorDest)blendDstRgb,
                    (BlendingFactorSrc)blendSrcAlpha,
                    (BlendingFactorDest)blendDstAlpha);
                GL.Viewport(viewport[0], viewport[1], viewport[2], viewport[3]);
                GL.Scissor(scissorBox[0], scissorBox[1], scissorBox[2], scissorBox[3]);
            }

            private static void Restore(EnableCap cap, bool enabled)
            {
                if (enabled) GL.Enable(cap);
                else GL.Disable(cap);
            }
        }
    }
}
