using System;
using ForesTycoon.Rules;
using ImGuiNET;
using Vec2 = System.Numerics.Vector2;
namespace ForesTycoon.Editor;

internal static class RuleCatalogGapsView
{
    internal static void Draw(GameRuleCatalog catalog)
    {
        ImGui.SetCursorPos(ImGui.GetCursorPos() + new Vec2(8, 6));
        EditorStyle.Title("Hiányzó játékfolyamatok és korlátok");
        EditorStyle.Text(HudTheme.Muted, "Amit a jelenlegi szabályrendszer még nem modellez. Tervezési teendőlista.");
        ImGui.Spacing();
        int i = 1;
        foreach (string gap in catalog.Gaps)
        {
            var draw = ImGui.GetWindowDrawList();
            Vec2 p = ImGui.GetCursorScreenPos();
            float width = ImGui.GetContentRegionAvail().X - 8;
            ImGui.SetCursorScreenPos(p + new Vec2(40, 8));
            ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + width - 56);
            EditorStyle.Text(HudTheme.Parchment, gap);
            ImGui.PopTextWrapPos();
            float height = ImGui.GetItemRectMax().Y - p.Y + 8;
            draw.AddRect(p, p + new Vec2(width, height), EditorStyle.U(EditorStyle.Fade(HudTheme.Bad, 0.3f)), 6);
            draw.AddText(p + new Vec2(12, 8), EditorStyle.ErrorColour, $"{i++:00}");
            ImGui.SetCursorScreenPos(p + new Vec2(0, height + 6));
            ImGui.Dummy(new Vec2(1, 1));
        }
    }

}
