using System;
using ImGuiNET;
using NVec2 = System.Numerics.Vector2;
using NVec4 = System.Numerics.Vector4;

namespace ForesTycoon
{
    /// <summary>
    /// Forest-lodge look for the HUD: dark spruce panels, moss-green accents, amber for the
    /// active tool. Also hosts the shared icon-button and tooltip widgets so every toolbar,
    /// sub-bar and window speaks the same visual language.
    /// </summary>
    internal static class HudTheme
    {
        // Fine-tuned "forest lodge" palette (docs/ui-ux-design.md): slightly deeper, warmer panels that
        // let the diorama show through, softer parchment, honey amber and calmer signal colours.
        internal static readonly NVec4 Panel = new(0.078f, 0.102f, 0.086f, 0.90f);      // #141A16
        internal static readonly NVec4 PanelLight = new(0.114f, 0.149f, 0.125f, 0.96f); // #1D2620
        internal static readonly NVec4 Moss = new(0.36f, 0.50f, 0.27f, 1f);             // #5C8045
        internal static readonly NVec4 MossBright = new(0.49f, 0.643f, 0.357f, 1f);     // #7DA45B
        internal static readonly NVec4 AmberAccent = new(0.902f, 0.702f, 0.353f, 1f);  // #E6B35A
        internal static readonly NVec4 Parchment = new(0.918f, 0.894f, 0.812f, 1f);     // #EAE4CF
        internal static readonly NVec4 Muted = new(0.639f, 0.667f, 0.588f, 1f);         // #A3AA96
        internal static readonly NVec4 Good = new(0.58f, 0.81f, 0.486f, 1f);            // #94CF7C
        internal static readonly NVec4 Bad = new(0.886f, 0.514f, 0.42f, 1f);            // #E2836B
        internal static readonly NVec4 Info = new(0.58f, 0.753f, 0.878f, 1f);           // #94C0E0

        internal static void Apply()
        {
            ImGuiStylePtr style = ImGui.GetStyle();
            style.WindowRounding = 10f;
            style.ChildRounding = 6f;
            style.FrameRounding = 6f;
            style.PopupRounding = 6f;
            style.GrabRounding = 5f;
            style.TabRounding = 5f;
            style.ScrollbarRounding = 6f;
            style.WindowBorderSize = 1f;
            style.FrameBorderSize = 0f;
            style.WindowPadding = new NVec2(14, 12);
            style.FramePadding = new NVec2(8, 5);
            style.ItemSpacing = new NVec2(8, 7);
            style.ItemInnerSpacing = new NVec2(6, 5);
            style.WindowTitleAlign = new NVec2(0.02f, 0.5f);
            style.SeparatorTextBorderSize = 2f;

            var colors = style.Colors;
            colors[(int)ImGuiCol.Text] = Parchment;
            colors[(int)ImGuiCol.TextDisabled] = Muted;
            colors[(int)ImGuiCol.WindowBg] = Panel;
            colors[(int)ImGuiCol.ChildBg] = new NVec4(0, 0, 0, 0.12f);
            colors[(int)ImGuiCol.PopupBg] = new NVec4(0.075f, 0.098f, 0.085f, 0.97f);
            colors[(int)ImGuiCol.Border] = new NVec4(0.36f, 0.50f, 0.27f, 0.35f);
            colors[(int)ImGuiCol.BorderShadow] = new NVec4(0, 0, 0, 0);
            colors[(int)ImGuiCol.FrameBg] = new NVec4(0.145f, 0.184f, 0.153f, 0.90f);
            colors[(int)ImGuiCol.FrameBgHovered] = new NVec4(0.24f, 0.30f, 0.22f, 0.95f);
            colors[(int)ImGuiCol.FrameBgActive] = new NVec4(0.29f, 0.37f, 0.25f, 1f);
            colors[(int)ImGuiCol.TitleBg] = new NVec4(0.098f, 0.125f, 0.102f, 1f);
            colors[(int)ImGuiCol.TitleBgActive] = new NVec4(0.129f, 0.173f, 0.118f, 1f);
            colors[(int)ImGuiCol.TitleBgCollapsed] = new NVec4(0.10f, 0.13f, 0.10f, 0.85f);
            colors[(int)ImGuiCol.MenuBarBg] = PanelLight;
            colors[(int)ImGuiCol.ScrollbarBg] = new NVec4(0, 0, 0, 0.15f);
            colors[(int)ImGuiCol.ScrollbarGrab] = new NVec4(0.30f, 0.36f, 0.27f, 1f);
            colors[(int)ImGuiCol.ScrollbarGrabHovered] = Moss;
            colors[(int)ImGuiCol.ScrollbarGrabActive] = MossBright;
            colors[(int)ImGuiCol.CheckMark] = AmberAccent;
            colors[(int)ImGuiCol.SliderGrab] = MossBright;
            colors[(int)ImGuiCol.SliderGrabActive] = AmberAccent;
            colors[(int)ImGuiCol.Button] = new NVec4(0.18f, 0.235f, 0.17f, 1f);
            colors[(int)ImGuiCol.ButtonHovered] = new NVec4(0.30f, 0.40f, 0.25f, 1f);
            colors[(int)ImGuiCol.ButtonActive] = Moss;
            colors[(int)ImGuiCol.Header] = new NVec4(0.22f, 0.29f, 0.19f, 1f);
            colors[(int)ImGuiCol.HeaderHovered] = new NVec4(0.30f, 0.40f, 0.25f, 1f);
            colors[(int)ImGuiCol.HeaderActive] = Moss;
            colors[(int)ImGuiCol.Separator] = new NVec4(0.32f, 0.38f, 0.28f, 0.6f);
            colors[(int)ImGuiCol.SeparatorHovered] = Moss;
            colors[(int)ImGuiCol.SeparatorActive] = AmberAccent;
            colors[(int)ImGuiCol.ResizeGrip] = new NVec4(0.36f, 0.50f, 0.27f, 0.25f);
            colors[(int)ImGuiCol.ResizeGripHovered] = Moss;
            colors[(int)ImGuiCol.ResizeGripActive] = AmberAccent;
            colors[(int)ImGuiCol.Tab] = new NVec4(0.16f, 0.21f, 0.15f, 1f);
            colors[(int)ImGuiCol.TabHovered] = new NVec4(0.30f, 0.40f, 0.25f, 1f);
            colors[(int)ImGuiCol.TabSelected] = Moss;
            colors[(int)ImGuiCol.PlotHistogram] = MossBright;
            colors[(int)ImGuiCol.PlotHistogramHovered] = AmberAccent;
            colors[(int)ImGuiCol.TextSelectedBg] = new NVec4(0.36f, 0.50f, 0.27f, 0.45f);
            colors[(int)ImGuiCol.NavCursor] = AmberAccent;
            colors[(int)ImGuiCol.ModalWindowDimBg] = new NVec4(0, 0, 0, 0.45f);
        }

        /// <summary>
        /// Square icon button. Active buttons get an amber rim, like a pressed-in tool in the
        /// Transport Tycoon toolbar. Returns true on click; hovering shows the rich tooltip.
        /// </summary>
        internal static bool IconButton(string id, GameIcon icon, float size, bool active,
            string title, string shortcut = null, string description = null, bool enabled = true)
        {
            ImDrawListPtr dl = ImGui.GetWindowDrawList();
            NVec2 min = ImGui.GetCursorScreenPos();
            NVec2 max = min + new NVec2(size, size);
            if (!enabled) ImGui.BeginDisabled();
            bool clicked = ImGui.InvisibleButton(id, new NVec2(size, size));
            bool hovered = ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled);
            bool held = ImGui.IsItemActive();
            if (!enabled) ImGui.EndDisabled();

            NVec4 fill = active ? new NVec4(0.204f, 0.271f, 0.176f, 1f)
                : held ? new NVec4(0.18f, 0.24f, 0.16f, 1f)
                : hovered ? new NVec4(0.161f, 0.204f, 0.165f, 1f)
                : new NVec4(0.118f, 0.149f, 0.122f, 1f);
            dl.AddRectFilled(min, max, GameIcons.Color(fill), 6f);
            // A faint top bevel gives the buttons the slightly raised, physical feel of a model kit.
            dl.AddLine(min + new NVec2(5, 1), new NVec2(max.X - 5, min.Y + 1), GameIcons.Color(new NVec4(1, 1, 1, active ? 0.18f : 0.07f)), 1f);
            if (active) dl.AddRect(min, max, GameIcons.Color(AmberAccent), 6f, ImDrawFlags.None, 2f);
            else if (hovered) dl.AddRect(min, max, GameIcons.Color(new NVec4(0.47f, 0.63f, 0.34f, 0.8f)), 6f);

            float pad = size * 0.14f;
            uint ink = GameIcons.Color(enabled ? Parchment : Muted);
            GameIcons.Draw(dl, icon, min + new NVec2(pad, pad + (held ? 1 : 0)), size - pad * 2, ink);
            if (!enabled) dl.AddRectFilled(min, max, GameIcons.Color(new NVec4(0.05f, 0.06f, 0.05f, 0.55f)), 6f);

            if (hovered) Tooltip(title, shortcut, description);
            return clicked && enabled;
        }

        /// <summary>Wide button with an icon and a label, used in sub-bars and windows.</summary>
        internal static bool LabeledIconButton(string label, GameIcon icon, bool active, float height = 30f, string tooltip = null)
        {
            ImDrawListPtr dl = ImGui.GetWindowDrawList();
            NVec2 text = ImGui.CalcTextSize(label);
            float iconSize = height - 8f;
            NVec2 size = new(iconSize + text.X + 22f, height);
            NVec2 min = ImGui.GetCursorScreenPos();
            bool clicked = ImGui.InvisibleButton("##" + label, size);
            bool hovered = ImGui.IsItemHovered();
            NVec4 fill = active ? new NVec4(0.204f, 0.271f, 0.176f, 1f)
                : hovered ? new NVec4(0.161f, 0.204f, 0.165f, 1f) : new NVec4(0.118f, 0.149f, 0.122f, 1f);
            dl.AddRectFilled(min, min + size, GameIcons.Color(fill), 5f);
            if (active) dl.AddRect(min, min + size, GameIcons.Color(AmberAccent), 5f, ImDrawFlags.None, 1.6f);
            GameIcons.Draw(dl, icon, min + new NVec2(5, 4), iconSize, GameIcons.Color(Parchment));
            dl.AddText(min + new NVec2(iconSize + 12f, (height - text.Y) * 0.5f), GameIcons.Color(Parchment), label);
            if (hovered && tooltip != null) Tooltip(label, null, tooltip);
            return clicked;
        }

        internal static void Tooltip(string title, string shortcut, string description)
        {
            ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new NVec2(10, 8));
            ImGui.BeginTooltip();
            ImGui.TextColored(AmberAccent, title);
            if (!string.IsNullOrEmpty(shortcut))
            {
                ImGui.SameLine();
                ImGui.TextDisabled($"[{shortcut}]");
            }
            if (!string.IsNullOrEmpty(description))
            {
                ImGui.PushTextWrapPos(ImGui.GetFontSize() * 20f);
                ImGui.TextUnformatted(description);
                ImGui.PopTextWrapPos();
            }
            ImGui.EndTooltip();
            ImGui.PopStyleVar();
        }

        /// <summary>Thin vertical divider between toolbar groups.</summary>
        internal static void GroupDivider(float height)
        {
            ImGui.SameLine(0, 7);
            NVec2 p = ImGui.GetCursorScreenPos();
            ImGui.GetWindowDrawList().AddLine(p + new NVec2(0, 4), p + new NVec2(0, height - 4),
                GameIcons.Color(new NVec4(0.40f, 0.46f, 0.34f, 0.55f)), 1f);
            ImGui.Dummy(new NVec2(1, height));
            ImGui.SameLine(0, 7);
        }

        /// <summary>Icon followed by text on one line, aligned to the text baseline.</summary>
        internal static void IconText(GameIcon icon, string text, NVec4? color = null)
        {
            float size = ImGui.GetTextLineHeight() + 2f;
            NVec2 p = ImGui.GetCursorScreenPos();
            GameIcons.Draw(ImGui.GetWindowDrawList(), icon, p + new NVec2(0, -1), size, GameIcons.Color(Parchment));
            ImGui.Dummy(new NVec2(size, ImGui.GetTextLineHeight()));
            ImGui.SameLine(0, 6);
            if (color.HasValue) ImGui.TextColored(color.Value, text);
            else ImGui.TextUnformatted(text);
        }

        /// <summary>Labelled progress bar with the value printed inside.</summary>
        internal static void Meter(string label, float fraction, string overlay, NVec4? color = null)
        {
            ImGui.TextUnformatted(label);
            if (color.HasValue) ImGui.PushStyleColor(ImGuiCol.PlotHistogram, color.Value);
            ImGui.ProgressBar(Math.Clamp(fraction, 0f, 1f), new NVec2(-1, 0), overlay);
            if (color.HasValue) ImGui.PopStyleColor();
        }

        internal static void KeyValue(string key, string value)
        {
            ImGui.TextDisabled(key);
            ImGui.SameLine(ImGui.GetFontSize() * 11f);
            ImGui.TextUnformatted(value);
        }
    }
}
