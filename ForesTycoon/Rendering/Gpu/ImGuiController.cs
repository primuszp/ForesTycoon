using System;
using System.IO;
using System.Runtime.InteropServices;
using ImGuiNET;
using Vector2 = System.Numerics.Vector2;

namespace ForesTycoon
{
    /// <summary>
    /// Dear ImGui context, font selection and input bridge; rendering belongs to the selected backend.
    /// </summary>
    sealed class ImGuiController : IDisposable
    {
        private bool frameBegun;

        private readonly IUiRenderBackend backend;
        private GCHandle glyphRangeHandle;
        private readonly IntPtr context;
        private bool disposed;
        public ImGuiController(IUiRenderBackend backend = null)
        {
            this.backend = backend ?? SceneRenderBackends.Current.CreateUi();
            context = ImGui.CreateContext();
            ImGui.SetCurrentContext(context);
            try
            {
                ImGuiIOPtr io = ImGui.GetIO();
                LoadUIFont(io);
                io.ConfigFlags |= ImGuiConfigFlags.NavEnableKeyboard;
                this.backend.Initialize(io);
            }
            catch { Dispose(); throw; }
            // Az első frame-et az Update indítja, miután a DisplaySize be van állítva
            // (különben az ImGui "Invalid DisplaySize" assert-et dob).
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
            ushort[] ranges = { 0x0020, 0x017F, 0x2000, 0x206F, 0 };
            glyphRangeHandle = GCHandle.Alloc(ranges, GCHandleType.Pinned);
            io.Fonts.AddFontFromFileTTF(fontPath, 16f, IntPtr.Zero, glyphRangeHandle.AddrOfPinnedObject());
            // A serif face for estate names, seasons and panel titles (the HUD's calm "voice").
            string title = FindTitleFont() ?? fontPath;
            TitleFont = io.Fonts.AddFontFromFileTTF(title, 22f, IntPtr.Zero, glyphRangeHandle.AddrOfPinnedObject());
            LargeTitleFont = io.Fonts.AddFontFromFileTTF(title, 34f, IntPtr.Zero, glyphRangeHandle.AddrOfPinnedObject());
            HasTitleFonts = true;
        }

        internal static bool HasTitleFonts { get; private set; }

        /// <summary>Serif title fonts; null pointers when only ImGui's default font is available.</summary>
        internal static ImFontPtr TitleFont { get; private set; }
        internal static ImFontPtr LargeTitleFont { get; private set; }

        private static string FindTitleFont()
        {
            string fonts = Environment.GetFolderPath(Environment.SpecialFolder.Fonts);
            string[] candidates =
            {
                Path.Combine(fonts, "georgiab.ttf"),
                Path.Combine(fonts, "georgia.ttf"),
                Path.Combine(fonts, "cambriab.ttf"),
                "/System/Library/Fonts/Supplemental/Georgia Bold.ttf",
                "/System/Library/Fonts/Supplemental/Georgia.ttf",
                "/usr/share/fonts/truetype/dejavu/DejaVuSerif-Bold.ttf"
            };
            foreach (string candidate in candidates)
                if (File.Exists(candidate)) return candidate;
            return null;
        }

        private static string FindUIFont()
        {
            string fonts = Environment.GetFolderPath(Environment.SpecialFolder.Fonts);
            string[] candidates =
            {
                Path.Combine(fonts, "segoeui.ttf"),
                Path.Combine(fonts, "arial.ttf"),
                Path.Combine(fonts, "tahoma.ttf"),
                // macOS keeps its Latin fonts here, not in SpecialFolder.Fonts.
                "/System/Library/Fonts/Supplemental/Arial.ttf",
                "/Library/Fonts/Arial.ttf",
                "/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf",
                "/usr/share/fonts/truetype/liberation2/LiberationSans-Regular.ttf"
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
            backend.Draw(ImGui.GetDrawData());
        }

        // ── Egér-input ───────────────────────────────────────────────────────
        public bool WantCaptureMouse => ImGui.GetIO().WantCaptureMouse;
        public void MouseMove(int x, int y) => ImGui.GetIO().AddMousePosEvent(x, y);
        public void MouseButton(int index, bool down) => ImGui.GetIO().AddMouseButtonEvent(index, down);
        public void MouseScroll(float wheel) => ImGui.GetIO().AddMouseWheelEvent(0f, wheel);

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            try { backend.Dispose(); }
            finally
            {
                ImGui.DestroyContext(context);
                if (glyphRangeHandle.IsAllocated) glyphRangeHandle.Free();
            }
        }
    }
}
