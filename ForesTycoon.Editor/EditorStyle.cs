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
        internal static readonly Vec4 ConstantKind = new(0.85f, 0.62f, 0.28f, 1);
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

        /// <summary>The validation/state pill of the status bar.</summary>
        internal static void Pill(string text, bool ok)
        {
            Chip(text, ok ? HudTheme.Good : HudTheme.Bad, "pill");
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
            ImGui.TableSetupColumn("k", ImGuiTableColumnFlags.WidthFixed, 110);
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
