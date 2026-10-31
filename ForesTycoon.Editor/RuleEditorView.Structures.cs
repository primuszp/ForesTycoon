using System;
using System.Linq;
using ForesTycoon.Rules;
using ImGuiNET;
using Vec2 = System.Numerics.Vector2;

namespace ForesTycoon.Editor;

/// <summary>
/// The scene's starting estate on the map: roads drawn with clicks, sawmills and depots placed with one. The test world
/// is rebuilt after each change, so the map always shows what the game will build.
/// </summary>
internal sealed partial class RuleEditorView
{
    private enum StructureTool { Macadam, Asphalt, Dirt, SkidTrail, Sawmill, Depot }
    private StructureTool structureTool;
    private int roadStart = -1;
    private string highlighted; // "road:i", "mill:i" or "depot:i" while its list row is hovered
    private (Vec2 At, float Cell, int Rows) mapGeometry;
    private bool showStructureBuffers = true;
    private readonly SlicedPreviewCache<int, RoadTimberSurvey> structureSurveys = new(1, 300);
    private RoadTimberSurvey structureSurvey;

    private Vec2 TileCentre(int tile) => mapGeometry.At + new Vec2(tile / mapGeometry.Rows + .5f, tile % mapGeometry.Rows + .5f) * mapGeometry.Cell;

    private void FootprintOutline(ImDrawListPtr draw, int tile, uint colour, float thickness = 2)
    {
        Vec2 min = mapGeometry.At + new Vec2(tile / mapGeometry.Rows, tile % mapGeometry.Rows) * mapGeometry.Cell;
        draw.AddRect(min, min + new Vec2(2, 2) * mapGeometry.Cell, colour, 2, ImDrawFlags.None, thickness);
    }

    private bool BuildingFits(GameWorld world, int tile, bool needsRoad) =>
        world.Map.TryGetSawmillFootprint(tile, out var footprint, out _)
        && footprint.All(t => !world.Map.IsRoadTile(t) && !world.Map.IsBuildingTile(t) && !world.Forest.HasPhysicalObjects(t))
        && (!needsRoad || world.Map.FindRoadDocks(footprint).Count > 0);

    private void StructureCanvas(GameWorld world, int hovered)
    {
        var draw = ImGui.GetWindowDrawList();
        structureSurveys.BeginFrame((world.Map, world.Map.NavigationVersion, world.Forest.EditRevision, world.Tuning));
        if (showStructureBuffers) {
            if (structureSurveys.Request(0, world.RoadTimberSurveyWork, out var survey)) structureSurvey = survey;
            else structureSurvey = null;
            structureSurveys.Advance(1024);
            if (structureSurvey != null) for (int tile = 0; tile < structureSurvey.Overlay.Length; tile++) {
                if (structureSurvey.Overlay[tile] == 0) continue;
                Vec2 at = mapGeometry.At + new Vec2(tile / mapGeometry.Rows, tile % mapGeometry.Rows) * mapGeometry.Cell;
                draw.AddRectFilled(at, at + new Vec2(mapGeometry.Cell), structureSurvey.Overlay[tile]);
            }
        }
        uint mill = EditorStyle.U(0.95f, 0.70f, 0.35f, 1), depot = EditorStyle.U(0.45f, 0.75f, 1f, 1), hot = EditorStyle.U(1, 1, 1, 1);
        uint end = EditorStyle.U(0.3f, 0.3f, 0.28f, 1);
        for (int i = 0; i < scenario.Roads.Count; i++) {
            var r = scenario.Roads[i]; bool lit = highlighted == "road:" + i;
            float radius = Math.Max(2, mapGeometry.Cell * (lit ? .45f : .22f));
            draw.AddCircleFilled(TileCentre(r.From), radius, lit ? hot : end);
            draw.AddCircleFilled(TileCentre(r.To), radius, lit ? hot : end);
            if (lit) draw.AddLine(TileCentre(r.From), TileCentre(r.To), hot, 1);
        }
        for (int i = 0; i < scenario.Sawmills.Count; i++) { bool lit = highlighted == "mill:" + i; FootprintOutline(draw, scenario.Sawmills[i], lit ? hot : mill, lit ? 3 : 2); }
        for (int i = 0; i < scenario.Depots.Count; i++) { bool lit = highlighted == "depot:" + i; FootprintOutline(draw, scenario.Depots[i], lit ? hot : depot, lit ? 3 : 2); }

        if (ImGui.IsKeyPressed(ImGuiKey.Escape) || ImGui.IsMouseClicked(ImGuiMouseButton.Right)) roadStart = -1;
        bool road = structureTool is StructureTool.Macadam or StructureTool.Asphalt or StructureTool.Dirt or StructureTool.SkidTrail;
        if (road && roadStart >= 0) {
            draw.AddCircleFilled(TileCentre(roadStart), Math.Max(3, mapGeometry.Cell * .4f), EditorStyle.Selection);
            if (hovered >= 0) draw.AddLine(TileCentre(roadStart), TileCentre(hovered), EditorStyle.Selection, 2);
        }
        if (hovered < 0) return;
        bool fits = false;
        if (!road) {
            fits = BuildingFits(world, hovered, structureTool == StructureTool.Depot);
            FootprintOutline(draw, hovered, EditorStyle.U(fits ? HudTheme.Good : HudTheme.Bad), 3);
            ImGui.SetTooltip(fits ? (structureTool == StructureTool.Depot ? "Telephely ide: kattints." : "Fűrészmalom ide: kattints.")
                : structureTool == StructureTool.Depot ? "A telephelyhez 2×2 sík, száraz, szabad hely kell út mellett." : "A malomhoz 2×2 sík, száraz, szabad hely kell.");
        }
        else ImGui.SetTooltip(roadStart < 0 ? "Kattints az út kezdőcsempéjére." : "Kattints a végcsempére · jobb gomb / Esc: befejezés");
        if (!ImGui.IsMouseClicked(ImGuiMouseButton.Left)) return;
        if (road) {
            if (roadStart < 0) { roadStart = hovered; return; }
            if (hovered == roadStart) return;
            scenario.Roads.Add(new RoadSegment { From = roadStart, To = hovered, Surface = structureTool.ToString() });
            int from = roadStart; roadStart = hovered; // the next click continues the road from here
            ApplyScenario(world, $"Út: #{from} → #{hovered}. Kattints tovább a folytatáshoz; jobb gomb vagy Esc: befejezés.");
        }
        else if (fits) {
            (structureTool == StructureTool.Depot ? scenario.Depots : scenario.Sawmills).Add(hovered);
            ApplyScenario(world, structureTool == StructureTool.Sawmill ? "Fűrészmalom elhelyezve."
                : scenario.Depots.Count == 1 ? "Telephely a kezdő járműparkkal (processzor, forwarder, rönkszállító)." : "Újabb telephely.");
        }
    }

    private void StructurePanel(GameWorld world)
    {
        ImGui.Checkbox("Faanyag-elérési pufferek", ref showStructureBuffers);
        if (showStructureBuffers) ImGui.TextWrapped(structureSurvey == null ? "Elérhető készlet számítása…"
            : $"Hálózatról elérhető élő faanyag: {structureSurvey.TotalTimber:F1} m³ (átfedések egyszer). A távolságok a Burkolatok lapon állíthatók.");
        EditorStyle.Section("Eszköz");
        foreach (var (tool, label, help) in new[] {
            (StructureTool.Macadam, "Makadámút", "Olcsóbb, gyorsabban kopik."), (StructureTool.Asphalt, "Aszfaltút", "Drágább, tartósabb."),
            (StructureTool.Dirt, "Földút", "Olcsó épített út; esőben a talaj korlátozza."),
            (StructureTool.SkidTrail, "Közelítőnyom", "Az úthálózatból az erdőbe vezető nyom."),
            (StructureTool.Sawmill, "Fűrészmalom", "Ide szállítják a fát; 2×2 hely."),
            (StructureTool.Depot, "Telephely", "A gépek otthona; az első a kezdő járműparkot adja. 2×2 hely út mellett.") }) {
            if (ImGui.Selectable(label, structureTool == tool)) { structureTool = tool; roadStart = -1; }
            if (ImGui.IsItemHovered()) ImGui.SetTooltip(help);
        }
        ImGui.PushTextWrapPos(0);
        EditorStyle.Text(HudTheme.Muted, structureTool is StructureTool.Macadam or StructureTool.Asphalt or StructureTool.Dirt or StructureTool.SkidTrail
            ? "Kattints a kezdő-, majd a végcsempére; az utat a játék útkeresése vezeti. A további kattintások folytatják. Jobb gomb vagy Esc: befejezés."
            : "Mutass a térképre: zöld keret – ide építhető, piros – nem. Az épület helyének szabadnak kell lennie.");
        EditorStyle.Text(HudTheme.Muted, "A kezdő építmények ingyen épülnek: a játék nulla egyenleggel indul.");
        ImGui.PopTextWrapPos();

        highlighted = null;
        EditorStyle.Section($"Építmények ({scenario.Roads.Count + scenario.Sawmills.Count + scenario.Depots.Count})");
        int remove = -1; string removeKind = null;
        void Row(string kind, int i, string text)
        {
            ImGui.AlignTextToFramePadding(); ImGui.TextUnformatted(text);
            if (ImGui.IsItemHovered()) highlighted = kind + ":" + i;
            ImGui.SameLine(Math.Max(ImGui.GetCursorPosX() + 8, ImGui.GetWindowWidth() - 64));
            if (EditorStyle.QuietButton("törlés##" + kind + i, "Eltávolítás a pályáról")) { remove = i; removeKind = kind; }
            if (ImGui.IsItemHovered()) highlighted = kind + ":" + i;
        }
        for (int i = 0; i < scenario.Depots.Count; i++) Row("depot", i, $"Telephely #{scenario.Depots[i]}" + (i == 0 ? " · járműpark" : ""));
        for (int i = 0; i < scenario.Sawmills.Count; i++) Row("mill", i, $"Fűrészmalom #{scenario.Sawmills[i]}");
        for (int i = 0; i < scenario.Roads.Count; i++)
            Row("road", i, $"{(scenario.Roads[i].Surface switch { "Asphalt" => "Aszfalt", "Macadam" => "Makadám", "Dirt" => "Földút", _ => "Közelítőnyom" })} #{scenario.Roads[i].From} → #{scenario.Roads[i].To}");
        if (scenario.Roads.Count + scenario.Sawmills.Count + scenario.Depots.Count == 0) {
            ImGui.PushTextWrapPos(0);
            EditorStyle.Text(HudTheme.Muted, scenario.InitialState != null ? "A rögzített jelenet meglévő útjai és épületei megmaradnak; itt az újakat veszed fel." : "Még nincs út vagy épület.");
            ImGui.PopTextWrapPos();
        }
        if (removeKind != null) {
            if (removeKind == "road") scenario.Roads.RemoveAt(remove);
            else (removeKind == "mill" ? scenario.Sawmills : scenario.Depots).RemoveAt(remove);
            highlighted = null;
            ApplyScenario(world, "Építmény eltávolítva.");
        }
        if (world.SceneProblems.Count > 0) {
            EditorStyle.Section("Nem épült meg");
            ImGui.PushTextWrapPos(0);
            foreach (string problem in world.SceneProblems) EditorStyle.Text(HudTheme.Bad, problem);
            ImGui.PopTextWrapPos();
        }
    }
}
