using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using ForesTycoon.Rules;
using ImGuiNET;
using Vec2 = System.Numerics.Vector2;
using Vec4 = System.Numerics.Vector4;

namespace ForesTycoon.Editor;

internal static class WorldEditorWidgets
{
    internal static bool SoilSwatch(SoilView type, bool active, string id) => ColourSwatch(type.Name, Colour(type.Color), active, id);
    internal static bool ColourSwatch(string name, Vec4 colour, bool active, string id)
    {
        Vec2 p = ImGui.GetCursorScreenPos();
        bool clicked = ImGui.Selectable("##" + id, active, ImGuiSelectableFlags.None, new Vec2(0, 26));
        var draw = ImGui.GetWindowDrawList();
        draw.AddRectFilled(p + new Vec2(4, 4), p + new Vec2(22, 22), EditorStyle.U(colour), 3);
        draw.AddText(p + new Vec2(30, 5), EditorStyle.TextBright, name);
        return clicked;
    }
    internal static Vec4 SpeciesColour(string id)
    {
        if (!Enum.TryParse(id, out ForesTycoon.Ecology.ForestSpecies s) || s == ForesTycoon.Ecology.ForestSpecies.None) return new(.4f, .4f, .4f, 1);
        var c = ForesTycoon.Ecology.ForestSpeciesTraits.For(s).Crown;
        return new(c.R / 255f, c.G / 255f, c.B / 255f, 1);
    }

    internal static Vec4 TrafficColour(double value) => Mix(new(0.92f, 0.52f, 0.18f, 1), new(0.22f, 0.42f, 0.78f, 1), (float)Math.Clamp(value, 0, 1));
    internal static Vec4 Mix(Vec4 a, Vec4 b, float t) { t = Math.Clamp(t, 0, 1); return a + (b - a) * t; }
    internal static Vec4 Colour(string hex)
    {
        if (hex is { Length: 7 } && uint.TryParse(hex.AsSpan(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint rgb))
            return new((rgb >> 16 & 255) / 255f, (rgb >> 8 & 255) / 255f, (rgb & 255) / 255f, 1);
        return new(.5f, .5f, .5f, 1);
    }
    internal static string Hex(Vec4 c) => $"#{(int)(c.X * 255):X2}{(int)(c.Y * 255):X2}{(int)(c.Z * 255):X2}";

    /// <summary>A three-column parameter table: name, value, where it acts. Returns true when an edit was finished.</summary>
    internal static bool ParameterTable(string id, Action<Action<string, string, Action>> rows)
    {
        bool changed = false;
        if (!ImGui.BeginTable(id, 3, ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.RowBg)) return false;
        ImGui.TableSetupColumn("Paraméter", ImGuiTableColumnFlags.WidthFixed, 230);
        ImGui.TableSetupColumn("Érték", ImGuiTableColumnFlags.WidthFixed, 240);
        ImGui.TableSetupColumn("Hol hat");
        rows((label, effect, field) => {
            ImGui.TableNextRow(); ImGui.TableSetColumnIndex(0); ImGui.AlignTextToFramePadding(); ImGui.TextUnformatted(label);
            ImGui.TableSetColumnIndex(1); ImGui.SetNextItemWidth(-1); field();
            if (ImGui.IsItemDeactivatedAfterEdit()) changed = true;
            ImGui.TableSetColumnIndex(2); ImGui.PushTextWrapPos(); EditorStyle.Text(HudTheme.Muted, effect); ImGui.PopTextWrapPos();
        });
        ImGui.EndTable();
        return changed;
    }
    internal static void Number(string id, Func<double> get, Action<double> set, double step, string format, double min = 0, double max = 1e9)
    {
        double v = get(); if (ImGui.InputDouble(id, ref v, step, step * 10, format)) set(Math.Clamp(v, min, max));
    }
    internal static void Unit(string id, Func<double> get, Action<double> set)
    {
        float v = (float)get(); if (ImGui.SliderFloat(id, ref v, 0, 1, "%.2f")) set(Math.Round(v, 3));
    }

    internal static bool Matches(string filter, params string[] texts) => filter.Length == 0 || texts.Any(t => t.Contains(filter, StringComparison.OrdinalIgnoreCase));
    /// <summary>Colour swatches with labels in a row that wraps to the next line when the panel is full.</summary>
    internal static void Legend(IEnumerable<(Vec4 Colour, string Label)> items)
    {
        var draw = ImGui.GetWindowDrawList();
        float right = ImGui.GetCursorScreenPos().X + ImGui.GetContentRegionAvail().X;
        bool first = true;
        foreach (var (colour, label) in items) {
            float width = 22 + ImGui.CalcTextSize(label).X;
            if (!first) {
                ImGui.SameLine(0, 18);
                if (ImGui.GetCursorScreenPos().X + width > right) ImGui.NewLine();
            }
            first = false;
            Vec2 p = ImGui.GetCursorScreenPos();
            draw.AddRectFilled(p + new Vec2(0, 3), p + new Vec2(14, 17), EditorStyle.U(colour), 2);
            ImGui.Dummy(new Vec2(18, 20)); ImGui.SameLine(0, 4);
            ImGui.TextUnformatted(label);
        }
    }

    internal static void MapLegendTraffic()
    {
        var draw = ImGui.GetWindowDrawList();
        ImGui.TextUnformatted("nehezen járható (0)"); ImGui.SameLine();
        Vec2 at = ImGui.GetCursorScreenPos() + new Vec2(0, 3);
        draw.AddRectFilledMultiColor(at, at + new Vec2(200, 14), EditorStyle.U(TrafficColour(0)), EditorStyle.U(TrafficColour(1)), EditorStyle.U(TrafficColour(1)), EditorStyle.U(TrafficColour(0)));
        ImGui.Dummy(new Vec2(204, 20)); ImGui.SameLine(); ImGui.TextUnformatted("jól járható (1) · szürke: burkolt út");
    }

}
