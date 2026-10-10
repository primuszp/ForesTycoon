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

        /// <summary>Stop the shaft at the head's base: its antialiased stroke must not round off the tip.</summary>
        internal static void BezierArrow(ImDrawListPtr draw, Vec2 start, Vec2 control1, Vec2 control2, Vec2 tip,
            float headSize, uint colour, float width, Vec2? headDirection = null)
        {
            Vec2 direction = headDirection ?? tip - control2;
            float length = direction.Length();
            if (length < 1e-4f) return;
            Vec2 unit = direction / length;
            // Keep the original curve until the last head-sized section (de Casteljau subdivision).
            // The extra half-stroke accounts for the rounded/antialiased end of the line.
            float setback = headSize + width * .5f;
            float low = 0, high = 1;
            for (int i = 0; i < 18; i++) {
                float t = (low + high) * .5f, s = 1 - t;
                Vec2 point = s * s * s * start + 3 * s * s * t * control1 + 3 * s * t * t * control2 + t * t * t * tip;
                if (Vec2.Dot(tip - point, unit) > setback) low = t; else high = t;
            }
            float end = low;
            Vec2 a = Vec2.Lerp(start, control1, end), b = Vec2.Lerp(control1, control2, end), c = Vec2.Lerp(control2, tip, end);
            Vec2 d = Vec2.Lerp(a, b, end), e = Vec2.Lerp(b, c, end);
            if (end > 0) draw.AddBezierCubic(start, a, d, Vec2.Lerp(d, e, end), colour, width);
            Arrow(draw, tip, direction, headSize, colour);
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
