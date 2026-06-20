using System;
using System.Runtime.CompilerServices;
using ImGuiNET;
using OpenTK.Graphics.OpenGL;
using OpenTK.Mathematics;
using Vector2 = System.Numerics.Vector2;

namespace ForesTycoon
{
    /// <summary>
    /// Minimális Dear ImGui (ImGui.NET) renderer + egér-input híd OpenTK 4-hez,
    /// WinForms GLControl alá. A terep immediate-mode renderje mellett fut: a
    /// compatibility kontextus a modern shader/VAO hívásokat is támogatja.
    /// </summary>
    sealed class ImGuiController : IDisposable
    {
        private bool frameBegun;

        private int vao, vbo, ebo;
        private int vboSize, eboSize;
        private int shader;
        private int projLoc, texLoc;
        private int fontTexture;

        private static readonly int VertSize = Unsafe.SizeOf<ImDrawVert>();

        public ImGuiController()
        {
            IntPtr ctx = ImGui.CreateContext();
            ImGui.SetCurrentContext(ctx);

            ImGuiIOPtr io = ImGui.GetIO();
            io.Fonts.AddFontDefault();
            io.BackendFlags |= ImGuiBackendFlags.RendererHasVtxOffset;
            io.ConfigFlags |= ImGuiConfigFlags.NavEnableKeyboard;

            CreateDeviceResources();
            // Az első frame-et az Update indítja, miután a DisplaySize be van állítva
            // (különben az ImGui "Invalid DisplaySize" assert-et dob).
        }

        private void CreateDeviceResources()
        {
            vbo = GL.GenBuffer();
            ebo = GL.GenBuffer();
            vboSize = 10000 * VertSize;
            eboSize = 2000 * sizeof(ushort);
            GL.BindBuffer(BufferTarget.ArrayBuffer, vbo);
            GL.BufferData(BufferTarget.ArrayBuffer, vboSize, IntPtr.Zero, BufferUsageHint.DynamicDraw);
            GL.BindBuffer(BufferTarget.ElementArrayBuffer, ebo);
            GL.BufferData(BufferTarget.ElementArrayBuffer, eboSize, IntPtr.Zero, BufferUsageHint.DynamicDraw);

            shader = BuildShader();
            projLoc = GL.GetUniformLocation(shader, "projection_matrix");
            texLoc = GL.GetUniformLocation(shader, "in_fontTexture");

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

            RecreateFontDeviceTexture();
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
            int v = CompileShader(ShaderType.VertexShader, vs);
            int f = CompileShader(ShaderType.FragmentShader, fs);
            int prog = GL.CreateProgram();
            GL.AttachShader(prog, v);
            GL.AttachShader(prog, f);
            GL.LinkProgram(prog);
            GL.GetProgram(prog, GetProgramParameterName.LinkStatus, out int ok);
            if (ok == 0) throw new Exception("ImGui shader link failed: " + GL.GetProgramInfoLog(prog));
            GL.DetachShader(prog, v);
            GL.DetachShader(prog, f);
            GL.DeleteShader(v);
            GL.DeleteShader(f);
            return prog;
        }

        private static int CompileShader(ShaderType type, string src)
        {
            int s = GL.CreateShader(type);
            GL.ShaderSource(s, src);
            GL.CompileShader(s);
            GL.GetShader(s, ShaderParameter.CompileStatus, out int ok);
            if (ok == 0) throw new Exception($"ImGui {type} compile failed: " + GL.GetShaderInfoLog(s));
            return s;
        }

        private void RecreateFontDeviceTexture()
        {
            ImGuiIOPtr io = ImGui.GetIO();
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

        // ── Per-frame ────────────────────────────────────────────────────────
        public void Update(int width, int height, float deltaSeconds)
        {
            if (frameBegun) ImGui.Render();

            ImGuiIOPtr io = ImGui.GetIO();
            io.DisplaySize = new Vector2(width, height);
            io.DisplayFramebufferScale = new Vector2(1f, 1f);
            io.DeltaTime = deltaSeconds > 0 ? deltaSeconds : 1f / 60f;

            ImGui.NewFrame();
            frameBegun = true;
        }

        public void Render()
        {
            if (!frameBegun) return;
            frameBegun = false;
            ImGui.Render();
            RenderDrawData(ImGui.GetDrawData());
        }

        // ── Egér-input ───────────────────────────────────────────────────────
        public bool WantCaptureMouse => ImGui.GetIO().WantCaptureMouse;
        public void MouseMove(int x, int y) => ImGui.GetIO().AddMousePosEvent(x, y);
        public void MouseButton(int index, bool down) => ImGui.GetIO().AddMouseButtonEvent(index, down);
        public void MouseScroll(float wheel) => ImGui.GetIO().AddMouseWheelEvent(0f, wheel);

        private void RenderDrawData(ImDrawDataPtr drawData)
        {
            if (drawData.CmdListsCount == 0) return;

            // ── GL állapot mentése a terep-renderhez ─────────────────────────
            GL.Enable(EnableCap.Blend);
            GL.BlendEquation(BlendEquationMode.FuncAdd);
            GL.BlendFuncSeparate(BlendingFactorSrc.SrcAlpha, BlendingFactorDest.OneMinusSrcAlpha,
                                 BlendingFactorSrc.One, BlendingFactorDest.OneMinusSrcAlpha);
            GL.Disable(EnableCap.CullFace);
            GL.Disable(EnableCap.DepthTest);
            GL.Enable(EnableCap.ScissorTest);

            int width = (int)drawData.DisplaySize.X;
            int height = (int)drawData.DisplaySize.Y;
            Matrix4 mvp = Matrix4.CreateOrthographicOffCenter(0f, width, height, 0f, -1f, 1f);

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

                    GL.BindTexture(TextureTarget.Texture2D, (int)cmd.TextureId);
                    GL.Scissor((int)clip.X, height - (int)clip.W, (int)(clip.Z - clip.X), (int)(clip.W - clip.Y));
                    GL.DrawElementsBaseVertex(PrimitiveType.Triangles, (int)cmd.ElemCount,
                        DrawElementsType.UnsignedShort, (IntPtr)(cmd.IdxOffset * sizeof(ushort)), (int)cmd.VtxOffset);
                }
            }

            GL.BindVertexArray(0);
            GL.UseProgram(0);
            GL.Disable(EnableCap.ScissorTest);
            GL.Disable(EnableCap.Blend);
        }

        public void Dispose()
        {
            GL.DeleteVertexArray(vao);
            GL.DeleteBuffer(vbo);
            GL.DeleteBuffer(ebo);
            GL.DeleteTexture(fontTexture);
            GL.DeleteProgram(shader);
        }
    }
}
