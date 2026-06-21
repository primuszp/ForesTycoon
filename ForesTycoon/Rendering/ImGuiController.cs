using System;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
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
        private GCHandle glyphRangeHandle;

        private static readonly int VertSize = Unsafe.SizeOf<ImDrawVert>();

        public ImGuiController()
        {
            IntPtr ctx = ImGui.CreateContext();
            ImGui.SetCurrentContext(ctx);

            ImGuiIOPtr io = ImGui.GetIO();
            LoadUIFont(io);
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
        private void LoadUIFont(ImGuiIOPtr io)
        {
            string fontPath = FindUIFont();
            if (fontPath == null)
            {
                io.Fonts.AddFontDefault();
                return;
            }

            // Latin Extended-A contains Hungarian double acute glyphs: ő/Ő and ű/Ű.
            ushort[] ranges = { 0x0020, 0x00FF, 0x0100, 0x017F, 0 };
            glyphRangeHandle = GCHandle.Alloc(ranges, GCHandleType.Pinned);
            io.Fonts.AddFontFromFileTTF(fontPath, 16f, IntPtr.Zero, glyphRangeHandle.AddrOfPinnedObject());
        }

        private static string FindUIFont()
        {
            string fonts = Environment.GetFolderPath(Environment.SpecialFolder.Fonts);
            string[] candidates =
            {
                Path.Combine(fonts, "segoeui.ttf"),
                Path.Combine(fonts, "arial.ttf"),
                Path.Combine(fonts, "tahoma.ttf")
            };

            foreach (string candidate in candidates)
                if (File.Exists(candidate))
                    return candidate;

            return null;
        }

        // Per-frame ---------------------------------------------------------
        public void Update(int width, int height, int framebufferWidth, int framebufferHeight, Vector2 framebufferScale, float deltaSeconds)
        {
            if (frameBegun) ImGui.Render();

            ImGuiIOPtr io = ImGui.GetIO();
            io.DisplaySize = new Vector2(width, height);
            io.DisplayFramebufferScale = framebufferScale;
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
                    }
                }
            }

            GL.BindVertexArray(0);
            GL.UseProgram(0);
            GL.BindTexture(TextureTarget.Texture2D, 0);
            GL.Disable(EnableCap.ScissorTest);
            GL.Disable(EnableCap.Blend);

            // Állapot visszaállítása a terep immediate-mode renderjéhez: a depth-test
            // nélkül a barna skirt/aljlapok ráfestődnének a terepre (a jelenet "elcsúszik").
            GL.Enable(EnableCap.DepthTest);
        }

        public void Dispose()
        {
            GL.DeleteVertexArray(vao);
            GL.DeleteBuffer(vbo);
            GL.DeleteBuffer(ebo);
            GL.DeleteTexture(fontTexture);
            GL.DeleteProgram(shader);
            if (glyphRangeHandle.IsAllocated) glyphRangeHandle.Free();
        }
    }
}
