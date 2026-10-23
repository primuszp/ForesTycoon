using System;
using ImGuiNET;
using Vec2 = System.Numerics.Vector2;
using Vec4 = System.Numerics.Vector4;

namespace ForesTycoon.Editor
{
    /// <summary>
    /// The editor's look: the game's "forest lodge" palette (HudTheme) plus the few elements an authoring tool needs —
    /// a title, chips, a status pill, a primary button and the node colours of the graphs.
    /// </summary>
    internal static class EditorStyle
    {
        /// <summary>
        /// The game's theme with authoring metrics: roomier rows and frames for the larger editor face, flatter corners,
        /// and visible frame borders so inputs read as inputs.
        /// </summary>
        internal static void Apply()
        {
            HudTheme.Apply();
            var style = ImGui.GetStyle();
            style.WindowRounding = 0f; style.ChildRounding = 8f; style.FrameRounding = 6f; style.PopupRounding = 8f;
            style.GrabRounding = 4f; style.ScrollbarRounding = 8f; style.TabRounding = 6f;
            style.FrameBorderSize = 1f; style.WindowBorderSize = 0f; style.ChildBorderSize = 1f; style.PopupBorderSize = 1f;
            style.WindowPadding = new Vec2(16, 14);
            style.FramePadding = new Vec2(10, 6);
            style.ItemSpacing = new Vec2(10, 8);
            style.ItemInnerSpacing = new Vec2(8, 6);
            style.CellPadding = new Vec2(8, 5);
            style.IndentSpacing = 18;
            style.ScrollbarSize = 11;
            style.GrabMinSize = 10;
            style.SelectableTextAlign = new Vec2(0, 0.5f);
            // Calm, neutral surfaces: the moss and honey accents are kept for state and selection, not for every frame.
            var c = style.Colors;
            c[(int)ImGuiCol.WindowBg] = Background;
            c[(int)ImGuiCol.ChildBg] = new Vec4(0, 0, 0, 0);
            c[(int)ImGuiCol.PopupBg] = new Vec4(0.090f, 0.110f, 0.098f, 0.98f);
            c[(int)ImGuiCol.Border] = Hairline;
            c[(int)ImGuiCol.FrameBg] = new Vec4(0.118f, 0.145f, 0.129f, 1f);
            c[(int)ImGuiCol.FrameBgHovered] = new Vec4(0.153f, 0.188f, 0.161f, 1f);
            c[(int)ImGuiCol.FrameBgActive] = new Vec4(0.176f, 0.220f, 0.184f, 1f);
            c[(int)ImGuiCol.Button] = new Vec4(0.141f, 0.176f, 0.153f, 1f);
            c[(int)ImGuiCol.ButtonHovered] = new Vec4(0.204f, 0.259f, 0.200f, 1f);
            c[(int)ImGuiCol.ButtonActive] = new Vec4(0.27f, 0.36f, 0.24f, 1f);
            // Headers (collapsing sections, selected rows) stay neutral; the active moment gets the accent.
            c[(int)ImGuiCol.Header] = new Vec4(0.62f, 0.74f, 0.56f, 0.10f);
            c[(int)ImGuiCol.HeaderHovered] = new Vec4(0.62f, 0.74f, 0.56f, 0.16f);
            c[(int)ImGuiCol.HeaderActive] = Fade(HudTheme.Moss, 0.45f);
            c[(int)ImGuiCol.Separator] = Hairline;
            c[(int)ImGuiCol.TableBorderLight] = Hairline;
            c[(int)ImGuiCol.TableBorderStrong] = Hairline;
            c[(int)ImGuiCol.TableHeaderBg] = new Vec4(0.110f, 0.137f, 0.122f, 1f);
            c[(int)ImGuiCol.TableRowBgAlt] = new Vec4(1, 1, 1, 0.025f);
            c[(int)ImGuiCol.ScrollbarBg] = new Vec4(0, 0, 0, 0);
            c[(int)ImGuiCol.ScrollbarGrab] = new Vec4(1, 1, 1, 0.10f);
            c[(int)ImGuiCol.ScrollbarGrabHovered] = new Vec4(1, 1, 1, 0.18f);
            c[(int)ImGuiCol.ScrollbarGrabActive] = Fade(HudTheme.MossBright, 0.8f);
        }

        // Surfaces of the editor shell, darkest to lightest.
        internal static readonly Vec4 Background = new(0.055f, 0.067f, 0.060f, 1f);
        internal static readonly Vec4 Sidebar = new(0.071f, 0.086f, 0.078f, 1f);
        internal static readonly Vec4 Surface = new(0.086f, 0.106f, 0.094f, 1f);
        internal static readonly Vec4 Hairline = new(1f, 1f, 1f, 0.075f);

        /// <summary>A bordered panel on the raised surface colour; pair with <see cref="EndPanel"/>.</summary>
        internal static bool BeginPanel(string id, Vec2 size, ImGuiWindowFlags flags = ImGuiWindowFlags.None)
        {
            ImGui.PushStyleColor(ImGuiCol.ChildBg, Surface);
            bool visible = ImGui.BeginChild(id, size, ImGuiChildFlags.Borders, flags);
            ImGui.PopStyleColor();
            return visible;
        }
        internal static void EndPanel() => ImGui.EndChild();

        /// <summary>The title row of a page: a heading and, on the same line, what the page is for.</summary>
        internal static void PageHeader(string title, string help)
        {
            bool pushed = ImGuiController.HasTitleFonts;
            if (pushed) ImGui.PushFont(ImGuiController.TitleFont);
            ImGui.AlignTextToFramePadding();
            Text(HudTheme.Parchment, title);
            if (pushed) ImGui.PopFont();
            if (string.IsNullOrEmpty(help)) return;
            ImGui.SameLine(0, 14);
            ImGui.AlignTextToFramePadding();
            float room = ImGui.GetContentRegionAvail().X;
            string shown = help;
            if (ImGui.CalcTextSize(shown).X > room) {
                while (shown.Length > 8 && ImGui.CalcTextSize(shown + "…").X > room) shown = shown[..^1];
                shown = shown.TrimEnd() + "…";
            }
            Text(HudTheme.Muted, shown);
            if (shown != help && ImGui.IsItemHovered()) ImGui.SetTooltip(help);
        }

        /// <summary>A search field filling the row; returns the trimmed filter.</summary>
        internal static string Search(string id, string hint, ref string filter)
        {
            ImGui.SetNextItemWidth(-1);
            ImGui.InputTextWithHint(id, hint, ref filter, 80);
            return filter.Trim();
        }

        /// <summary>A small on/off switch; the item rectangle is the switch, so callers can anchor to it.</summary>
        internal static bool Toggle(string id, ref bool value)
        {
            float h = ImGui.GetFrameHeight() * 0.62f, w = h * 1.8f;
            Vec2 p = ImGui.GetCursorScreenPos() + new Vec2(0, (ImGui.GetFrameHeight() - h) / 2);
            ImGui.SetCursorScreenPos(p);
            bool clicked = ImGui.InvisibleButton(id, new Vec2(w, h));
            if (clicked) value = !value;
            bool hover = ImGui.IsItemHovered();
            var draw = ImGui.GetWindowDrawList();
            Vec4 track = value ? Dim(HudTheme.MossBright, hover ? 1f : 0.85f) : new Vec4(1, 1, 1, hover ? 0.18f : 0.12f);
            draw.AddRectFilled(p, p + new Vec2(w, h), U(track), h / 2);
            float r = h / 2 - 2.5f;
            Vec2 knob = p + new Vec2(value ? w - h / 2 : h / 2, h / 2);
            draw.AddCircleFilled(knob, r, U(value ? HudTheme.Parchment : HudTheme.Muted));
            return clicked;
        }

        /// <summary>
        /// A navigation row: icon, label and an optional count, with an amber marker when active. In compact mode only the
        /// icon is drawn and the label becomes the tooltip.
        /// </summary>
        internal static bool NavItem(string id, GameIcon icon, string label, string help, bool active, bool compact, string badge = null)
        {
            float height = 34, width = ImGui.GetContentRegionAvail().X;
            Vec2 p = ImGui.GetCursorScreenPos();
            bool clicked = ImGui.InvisibleButton(id, new Vec2(width, height));
            bool hover = ImGui.IsItemHovered();
            var draw = ImGui.GetWindowDrawList();
            if (active || hover)
                draw.AddRectFilled(p, p + new Vec2(width, height), U(active ? Fade(HudTheme.Moss, 0.32f) : new Vec4(1, 1, 1, 0.045f)), 7);
            if (active) draw.AddRectFilled(p + new Vec2(0, 8), p + new Vec2(3, height - 8), U(HudTheme.AmberAccent), 2);
            float glyph = 20;
            float x = compact ? (width - glyph) / 2 : 12;
            GameIcons.Draw(draw, icon, p + new Vec2(x, (height - glyph) / 2), glyph, U(1, 1, 1, active || hover ? 1f : 0.78f));
            if (!compact) {
                float textY = (height - ImGui.GetTextLineHeight()) / 2;
                draw.AddText(p + new Vec2(x + glyph + 10, textY), U(active ? HudTheme.Parchment : hover ? HudTheme.Parchment : Fade(HudTheme.Parchment, 0.82f)), label);
                if (!string.IsNullOrEmpty(badge)) {
                    Vec2 size = ImGui.CalcTextSize(badge) + new Vec2(12, 2);
                    Vec2 at = p + new Vec2(width - size.X - 8, (height - size.Y) / 2);
                    draw.AddRectFilled(at, at + size, U(1, 1, 1, active ? 0.10f : 0.06f), size.Y / 2);
                    draw.AddText(at + new Vec2(6, 1), TextMuted, badge);
                }
            }
            if (hover) {
                if (compact) HudTheme.Tooltip(label, badge, help);
                else if (help != null) ImGui.SetTooltip(help);
            }
            return clicked;
        }

        /// <summary>A row of text tabs with an amber underline under the selected one, above a hairline; returns true on change.</summary>
        internal static bool Tabs(string id, string[] labels, ref int selected)
        {
            int before = selected;
            var draw = ImGui.GetWindowDrawList();
            Vec2 start = ImGui.GetCursorScreenPos();
            float height = ImGui.GetTextLineHeight() + 14, right = start.X + ImGui.GetContentRegionAvail().X;
            for (int i = 0; i < labels.Length; i++) {
                if (i > 0) ImGui.SameLine(0, 4);
                Vec2 p = ImGui.GetCursorScreenPos(), size = new(ImGui.CalcTextSize(labels[i]).X + 20, height);
                if (ImGui.InvisibleButton(id + i, size)) selected = i;
                bool hover = ImGui.IsItemHovered(), active = selected == i;
                if (hover && !active) draw.AddRectFilled(p, p + size, U(1, 1, 1, 0.04f), 6, ImDrawFlags.RoundCornersTop);
                draw.AddText(p + new Vec2(10, 6), U(active ? HudTheme.Parchment : hover ? Fade(HudTheme.Parchment, 0.9f) : HudTheme.Muted), labels[i]);
                if (active) draw.AddRectFilled(p + new Vec2(6, size.Y - 2), p + new Vec2(size.X - 6, size.Y), U(HudTheme.AmberAccent), 1);
            }
            draw.AddLine(new Vec2(start.X, start.Y + height), new Vec2(right, start.Y + height), U(Hairline));
            ImGui.Dummy(new Vec2(0, 4));
            return selected != before;
        }

        /// <summary>A muted, upper-case label above a group of navigation rows.</summary>
        internal static void NavGroup(string text, bool compact)
        {
            ImGui.Dummy(new Vec2(0, 4));
            if (compact) {
                Vec2 p = ImGui.GetCursorScreenPos(); float w = ImGui.GetContentRegionAvail().X;
                ImGui.GetWindowDrawList().AddLine(p + new Vec2(10, 4), p + new Vec2(w - 10, 4), U(Hairline));
                ImGui.Dummy(new Vec2(0, 8)); return;
            }
            ImGui.SetCursorPosX(ImGui.GetCursorPosX() + 12);
            Text(Fade(HudTheme.Muted, 0.75f), text.ToUpperInvariant());
        }

        /// <summary>A button sized to <paramref name="label"/>, as <see cref="ImGui.Button(string)"/> would draw it.</summary>
        internal static float ButtonWidth(string label)
        {
            int hash = label.IndexOf("##", StringComparison.Ordinal);
            return ImGui.CalcTextSize(hash >= 0 ? label[..hash] : label).X + ImGui.GetStyle().FramePadding.X * 2;
        }

        /// <summary>Room kept for the label to the right of a field in inspector panels.</summary>
        internal const float LabelColumn = 210;
        /// <summary>The label column for the current panel: <see cref="LabelColumn"/>, or 40% of a narrow panel.</summary>
        internal static float LabelWidth() => Math.Min(LabelColumn, ImGui.GetContentRegionAvail().X * 0.4f);

        internal static uint U(Vec4 c) => ImGui.ColorConvertFloat4ToU32(c);
        internal static uint U(float r, float g, float b, float a = 1) => ImGui.ColorConvertFloat4ToU32(new Vec4(r, g, b, a));

        // Canvas and node palette.
        internal static readonly uint Canvas = U(0.067f, 0.086f, 0.074f);
        internal static readonly uint GridMinor = U(0.11f, 0.14f, 0.12f);
        internal static readonly uint GridMajor = U(0.14f, 0.18f, 0.15f);
        internal static readonly uint NodeBody = U(0.115f, 0.145f, 0.125f, 0.97f);
        internal static readonly uint NodeBodyHover = U(0.14f, 0.18f, 0.15f, 0.98f);
        internal static readonly uint NodeShadow = U(0, 0, 0, 0.35f);
        internal static readonly uint TextBright = U(HudTheme.Parchment);
        internal static readonly uint TextMuted = U(HudTheme.Muted);
        internal static readonly uint Selection = U(HudTheme.AmberAccent);
        internal static readonly uint ErrorColour = U(HudTheme.Bad);

        // Kinds: inputs from the world (moss), constants (amber), operations (blue), the output (honey), native rules, presentation.
        internal static readonly Vec4 InputKind = new(0.40f, 0.62f, 0.33f, 1);
        internal static readonly Vec4 OperationKind = new(0.38f, 0.58f, 0.78f, 1);
        internal static readonly Vec4 OutputKind = new(0.93f, 0.75f, 0.36f, 1);
        internal static readonly Vec4 NativeKind = new(0.42f, 0.58f, 0.40f, 1);
        internal static readonly Vec4 GraphKind = new(0.93f, 0.72f, 0.33f, 1);
        internal static readonly Vec4 PresentationKind = new(0.55f, 0.62f, 0.82f, 1);

        // One colour per subsystem, in the vocabulary's order: forest, weather/water, harvesting, roads, economy, world, visuals.
        private static readonly Vec4[] TopicColours =
        {
            new(0.47f, 0.70f, 0.38f, 1), new(0.45f, 0.68f, 0.82f, 1), new(0.80f, 0.62f, 0.40f, 1), new(0.88f, 0.55f, 0.32f, 1),
            new(0.90f, 0.78f, 0.40f, 1), new(0.62f, 0.64f, 0.58f, 1), new(0.62f, 0.60f, 0.85f, 1),
        };
        internal static Vec4 TopicColour(int topic) => TopicColours[((topic % TopicColours.Length) + TopicColours.Length) % TopicColours.Length];

        internal static Vec4 Dim(Vec4 c, float f) => new(c.X * f, c.Y * f, c.Z * f, c.W);
        internal static Vec4 Fade(Vec4 c, float a) => new(c.X, c.Y, c.Z, a);

        /// <summary>Coloured text that is never treated as a format string (rule text may contain %).</summary>
        internal static void Text(Vec4 colour, string text)
        {
            ImGui.PushStyleColor(ImGuiCol.Text, colour); ImGui.TextUnformatted(text); ImGui.PopStyleColor();
        }

        /// <summary>A heading in the game's title face (Georgia) when it is loaded.</summary>
        internal static void Title(string text, bool large = false, Vec4? colour = null)
        {
            bool pushed = ImGuiController.HasTitleFonts;
            if (pushed) ImGui.PushFont(large ? ImGuiController.LargeTitleFont : ImGuiController.TitleFont);
            ImGui.PushStyleColor(ImGuiCol.Text, colour ?? HudTheme.Parchment);
            if (!large) ImGui.PushTextWrapPos();
            ImGui.TextUnformatted(text);
            if (!large) ImGui.PopTextWrapPos();
            ImGui.PopStyleColor();
            if (pushed) ImGui.PopFont();
        }

        /// <summary>A small rounded label with a coloured dot; returns true when clicked.</summary>
        internal static bool Chip(string text, Vec4 colour, string id = null, bool clickable = false)
        {
            var draw = ImGui.GetWindowDrawList();
            Vec2 size = ImGui.CalcTextSize(text) + new Vec2(24, 6);
            Vec2 p = ImGui.GetCursorScreenPos();
            bool clicked = ImGui.InvisibleButton(id ?? ("chip" + text), size);
            bool hover = clickable && ImGui.IsItemHovered();
            if (hover) ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
            draw.AddRectFilled(p, p + size, U(Fade(Dim(colour, 0.35f), hover ? 0.95f : 0.7f)), size.Y / 2);
            draw.AddRect(p, p + size, U(Fade(colour, hover ? 1f : 0.55f)), size.Y / 2);
            draw.AddCircleFilled(p + new Vec2(10, size.Y / 2), 3.2f, U(colour));
            draw.AddText(p + new Vec2(18, 3), TextBright, text);
            return clickable && clicked;
        }


        /// <summary>The one prominent action of a view (honey-coloured).</summary>
        internal static bool PrimaryButton(string label, bool enabled = true, string tooltip = null)
        {
            ImGui.BeginDisabled(!enabled);
            ImGui.PushStyleColor(ImGuiCol.Button, Dim(HudTheme.AmberAccent, 0.62f));
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, Dim(HudTheme.AmberAccent, 0.8f));
            ImGui.PushStyleColor(ImGuiCol.ButtonActive, HudTheme.AmberAccent);
            ImGui.PushStyleColor(ImGuiCol.Text, new Vec4(0.10f, 0.08f, 0.05f, 1));
            bool clicked = ImGui.Button(label);
            ImGui.PopStyleColor(4);
            ImGui.EndDisabled();
            if (tooltip != null && ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled)) ImGui.SetTooltip(tooltip);
            return clicked;
        }

        /// <summary>A quiet toolbar button (text only until hovered).</summary>
        internal static bool QuietButton(string label, string tooltip = null)
        {
            ImGui.PushStyleColor(ImGuiCol.Button, new Vec4(0, 0, 0, 0));
            bool clicked = ImGui.Button(label);
            ImGui.PopStyleColor();
            if (tooltip != null && ImGui.IsItemHovered()) ImGui.SetTooltip(tooltip);
            return clicked;
        }

        /// <summary>A labelled section with an amber tick and a thin rule, more readable than a plain separator.</summary>
        internal static void Section(string text)
        {
            ImGui.Spacing();
            var draw = ImGui.GetWindowDrawList();
            Vec2 p = ImGui.GetCursorScreenPos();
            draw.AddRectFilled(p + new Vec2(0, 3), p + new Vec2(3, ImGui.GetTextLineHeight() - 1), U(HudTheme.AmberAccent), 1);
            ImGui.SetCursorScreenPos(p + new Vec2(10, 0));
            Text(HudTheme.Muted, text.ToUpperInvariant());
            ImGui.Spacing();
        }

        /// <summary>Two-column key/value row inside a table started by <see cref="BeginFacts"/>.</summary>
        internal static bool BeginFacts(string id)
        {
            if (!ImGui.BeginTable(id, 2, ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.RowBg)) return false;
            ImGui.TableSetupColumn("k", ImGuiTableColumnFlags.WidthFixed, 140);
            ImGui.TableSetupColumn("v", ImGuiTableColumnFlags.WidthStretch);
            return true;
        }

        internal static void Fact(string key, string value, Vec4? colour = null)
        {
            ImGui.TableNextRow();
            ImGui.TableSetColumnIndex(0); Text(HudTheme.Muted, key);
            ImGui.TableSetColumnIndex(1);
            ImGui.PushTextWrapPos(); Text(colour ?? HudTheme.Parchment, value); ImGui.PopTextWrapPos();
        }

        /// <summary>Dotted canvas grid.</summary>
        internal static void Grid(ImDrawListPtr draw, Vec2 origin, Vec2 size, Vec2 pan, float zoom)
        {
            draw.AddRectFilled(origin, origin + size, Canvas);
            float step = 24 * zoom;
            if (step < 8) return;
            float startX = ((pan.X % step) + step) % step, startY = ((pan.Y % step) + step) % step;
            int ix = (int)Math.Floor(-pan.X / step), iy0 = (int)Math.Floor(-pan.Y / step);
            for (float x = startX; x < size.X; x += step, ix++)
            {
                int iy = iy0;
                for (float y = startY; y < size.Y; y += step, iy++)
                    draw.AddCircleFilled(origin + new Vec2(x, y), ix % 4 == 0 && iy % 4 == 0 ? 1.6f : 1f, ix % 4 == 0 && iy % 4 == 0 ? GridMajor : GridMinor);
            }
        }

        /// <summary>A tangent approach followed by a straight shaft ending inside the head, never at its tip.</summary>
        internal static void BezierArrow(ImDrawListPtr draw, Vec2 start, Vec2 control1, Vec2 control2, Vec2 tip,
            float headSize, uint colour, float width, Vec2? headDirection = null)
        {
            Vec2 direction = headDirection ?? tip - control2;
            float length = direction.Length();
            if (length < 1e-4f) return;
            Vec2 unit = direction / length;
            headSize = Math.Max(headSize, width * 3);
            // The curve ends before the head. Only a straight, axial segment enters the triangle.
            Vec2 join = tip - unit * (headSize + Math.Max(width * 2, headSize * .5f));
            float available = Vec2.Dot(join - start, unit);
            float pull = Math.Max(headSize, length);
            if (available > 0) {
                pull = Math.Min(pull, available * .5f);
                // Short column gaps must not make the control polygon overshoot the join.
                float overshoot = Vec2.Dot(control1 - join, unit);
                if (overshoot > 0) control1 -= unit * overshoot;
            }
            draw.PathClear();
            draw.PathLineTo(start);
            draw.PathBezierCubicCurveTo(control1, join - unit * pull, join);
            draw.PathLineTo(tip - unit * (headSize * .5f));
            draw.PathStroke(colour, ImDrawFlags.None, width);
            Arrow(draw, tip, unit, headSize, colour);
        }

        /// <summary><paramref name="colour"/> with its alpha scaled by <paramref name="alpha"/> (0–1).</summary>
        internal static uint Fade(uint colour, float alpha) =>
            (colour & 0x00FFFFFFu) | (uint)Math.Clamp((colour >> 24) * alpha, 0, 255) << 24;

        /// <summary>
        /// A node-editor wire from an output port <paramref name="from"/> to an input port <paramref name="to"/>: a cubic curve
        /// leaving and entering horizontally, on a dark halo so it stays readable where wires cross, ending in a small
        /// arrowhead that touches the port. A wire running backwards loops out wider instead of folding onto itself.
        /// </summary>
        /// <summary>Shared visual card for effect and response graphs; only the data and ports differ.</summary>
        internal static void GraphBox(ImDrawListPtr draw, Vec2 p, Vec2 size, uint border, string head, uint headColour,
            string middle, uint middleColour, string bottom = "", uint bottomColour = 0, bool lit = false, float scale = 1)
        {
            draw.AddRectFilled(p + new Vec2(0, 3) * scale, p + size + new Vec2(0, 3) * scale, NodeShadow, 6 * scale);
            draw.AddRectFilled(p, p + size, lit ? NodeBodyHover : NodeBody, 6 * scale);
            draw.AddRect(p, p + size, border, 6 * scale, ImDrawFlags.None, (lit ? 2.2f : 1.5f) * scale);
            draw.PushClipRect(p + new Vec2(8, 0) * scale, p + size - new Vec2(8, 0) * scale, true);
            float y = p.Y + (size.Y - 62 * scale) / 2;
            void Line(float offset, uint colour, string value) => draw.AddText(ImGui.GetFont(), ImGui.GetFontSize() * scale,
                new Vec2(p.X + 12 * scale, y + offset * scale), colour, value);
            Line(5, headColour, head); Line(24, middleColour, middle);
            if (bottom.Length > 0) Line(43, bottomColour == 0 ? TextMuted : bottomColour, bottom);
            draw.PopClipRect();
        }

        internal static void Wire(ImDrawListPtr draw, Vec2 from, Vec2 to, uint colour, float width, bool dashed = false, uint halo = 0, float scale = 1)
        {
            float head = 8 * scale;
            Vec2 end = to - new Vec2(head, 0);
            float dx = end.X - from.X;
            float pull = dx > 0 ? Math.Max(Math.Min(24, dx), dx * .55f) : 60 + Math.Min(120, -dx * .5f + Math.Abs(to.Y - from.Y) * .25f);
            Vec2 c1 = from + new Vec2(pull, 0), c2 = end - new Vec2(pull, 0);
            const int steps = 32;
            var points = new Vec2[steps + 1];
            for (int i = 0; i <= steps; i++) {
                float t = i / (float)steps, u = 1 - t;
                points[i] = u * u * u * from + 3 * u * u * t * c1 + 3 * u * t * t * c2 + t * t * t * end;
            }
            void Stroke(uint c, float w, bool dash)
            {
                if (!dash) {
                    draw.PathClear();
                    foreach (var p in points) draw.PathLineTo(p);
                    draw.PathLineTo(to - new Vec2(head * .6f, 0));
                    draw.PathStroke(c, ImDrawFlags.None, w);
                    return;
                }
                // Dashes of even screen length, whatever the curve's parameter speed.
                float on = 6, off = 4, along = 0;
                for (int i = 0; i < steps; i++) {
                    Vec2 a = points[i], b = points[i + 1]; float length = Vec2.Distance(a, b);
                    for (float s = 0; s < length;) {
                        float phase = along % (on + off), step = Math.Min(length - s, phase < on ? on - phase : on + off - phase);
                        if (phase < on) draw.AddLine(Vec2.Lerp(a, b, s / length), Vec2.Lerp(a, b, (s + step) / length), c, w);
                        s += step; along += step;
                    }
                }
            }
            if (halo != 0) Stroke(halo, width + 3.5f, false);
            Stroke(colour, width, dashed);
            if (halo != 0) Arrow(draw, to + new Vec2(1.5f, 0), Vec2.UnitX, head + 3, halo);
            Arrow(draw, to, Vec2.UnitX, head, colour);
        }

        /// <summary>A port: a ring in the node's colour, filled when something is plugged in.</summary>
        internal static void Port(ImDrawListPtr draw, Vec2 at, uint colour, bool connected, float scale = 1)
        {
            draw.AddCircleFilled(at, 5.5f * scale, NodeBody);
            if (connected) draw.AddCircleFilled(at, 3.5f * scale, colour);
            draw.AddCircle(at, 5.5f * scale, colour, 0, 1.5f * scale);
        }

        /// <summary>An arrowhead at <paramref name="tip"/> pointing along <paramref name="direction"/>.</summary>
        internal static void Arrow(ImDrawListPtr draw, Vec2 tip, Vec2 direction, float size, uint colour)
        {
            float l = MathF.Sqrt(direction.X * direction.X + direction.Y * direction.Y);
            if (l < 1e-4f) return;
            Vec2 d = direction / l, n = new(-d.Y, d.X);
            draw.AddTriangleFilled(tip, tip - d * size + n * size * 0.55f, tip - d * size - n * size * 0.55f, colour);
        }
    }
}
