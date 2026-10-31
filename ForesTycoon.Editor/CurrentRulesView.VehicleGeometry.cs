using System;
using System.Linq;
using System.Text.Json;
using ImGuiNET;
using Vec4 = System.Numerics.Vector4;

namespace ForesTycoon.Editor;

internal sealed partial class CurrentRulesView
{
    private int geometryPreviewKind, geometryManoeuvre, geometryRadiusAxle;
    private float geometryRadius = 20, geometryLead = 20, geometryGap = 10, geometrySpeed = 1, geometryLane = 3.5f, geometryTime = .65f;
    private bool geometryRight, geometryAxles = true, geometryWheels = true, geometrySweep = true, geometryRoad = true;
    private readonly StudyBody[][] geometryBodies = new StudyBody[3][];
    private readonly bool[] geometryCustomBodies = new bool[3];
    private readonly string[] geometryBodyKeys = new string[3];
    private VehiclePathStudy geometryStudy;
    private string geometryStudyKey, geometryExportMessage;
    private static readonly Vec4[] GeometryColours = {
        new(.42f,.76f,1,1), new(1,.72f,.28f,1), new(.62f,.88f,.47f,1), new(.85f,.58f,.95f,1),
        new(1,.5f,.44f,1), new(.45f,.9f,.85f,1), new(.91f,.83f,.58f,1), new(.72f,.75f,1,1) };

    internal void SelectGeometryStudyForTest(int kind, StudyManoeuvre manoeuvre)
    {
        geometryPreviewKind = kind; geometryManoeuvre = (int)manoeuvre;
    }
    internal VehiclePathStudy GeometryStudyForTest => geometryStudy;

    internal void DrawVehicleGeometryPreview(GameWorld world)
    {
        if (catalog == null) Load(world.DescribeCurrentRules());
        if (!ImGui.CollapsingHeader("Tankönyvi pályavizsgálat – tengelyek, kerekek és söpört terület")) return;
        ImGui.SetNextItemWidth(190);
        ImGui.Combo("##geometry-kind", ref geometryPreviewKind, "Tehergépkocsi\0Forwarder\0Harvester / processzor\0"); ImGui.SameLine();
        ImGui.SetNextItemWidth(235);
        ImGui.Combo("##geometry-manoeuvre", ref geometryManoeuvre, "90°-os ív\0S-kanyar (90° + ellenív)\0Állandósult körpálya (3 kör)\0"); ImGui.SameLine();
        ImGui.Checkbox("Jobbra kezd", ref geometryRight);
        ImGui.SetNextItemWidth(160); ImGui.DragFloat("Ívsugár (m)", ref geometryRadius, .2f, 2, 100, "%.1f", ImGuiSliderFlags.AlwaysClamp); ImGui.SameLine();
        ImGui.SetNextItemWidth(145); ImGui.DragFloat("Be-/kivezető (m)", ref geometryLead, .2f, 5, 80, "%.1f", ImGuiSliderFlags.AlwaysClamp); ImGui.SameLine();
        ImGui.SetNextItemWidth(115); ImGui.DragFloat("Próbasebesség (m/s)", ref geometrySpeed, .02f, .1f, 5, "%.2f", ImGuiSliderFlags.AlwaysClamp);
        if (geometryManoeuvre == 1) { ImGui.SetNextItemWidth(160); ImGui.DragFloat("Közbenső egyenes (m)", ref geometryGap, .2f, 0, 60, "%.1f", ImGuiSliderFlags.AlwaysClamp); ImGui.SameLine(); }
        ImGui.SetNextItemWidth(145); ImGui.DragFloat("Egy sáv szélessége (m)", ref geometryLane, .05f, 2, 12, "%.2f", ImGuiSliderFlags.AlwaysClamp);
        ImGui.TextWrapped("Az első tengely / első bogiecsoport követi az előírt úttengelyt. A számítás méteres, síkbeli gördülési modell; a játékbeli járműméreteket nem változtatja meg.");
        try {
            var values = catalog.Rules.SelectMany(r => r.Parameters).Where(p => p.Tunable).ToDictionary(p => p.Key, p => p.Value);
            var tuning = GameTuning.FromOverrides(values);
            var profile = geometryPreviewKind == 0 ? VehicleGeometryProfiles.Truck(tuning) :
                VehicleGeometryProfiles.Machine(tuning, geometryPreviewKind == 1 ? ForestMachineKind.Forwarder : ForestMachineKind.Harvester);
            profile.Validate();
            string profileKey = JsonSerializer.Serialize(profile);
            int units = (profile.Steering == SteeringGeometry.Articulated ? 2 : 1) + profile.Trailers.Length;
            if (geometryBodies[geometryPreviewKind] == null || geometryBodies[geometryPreviewKind].Length != units ||
                !geometryCustomBodies[geometryPreviewKind] && geometryBodyKeys[geometryPreviewKind] != profileKey) {
                geometryBodies[geometryPreviewKind] = StudyBody.Defaults(profile); geometryBodyKeys[geometryPreviewKind] = profileKey;
            }
            DrawStudyContours(profile);
            var bodies = geometryBodies[geometryPreviewKind];
            string key = JsonSerializer.Serialize(new { profile, bodies, geometryManoeuvre, geometryRadius, geometryLead, geometryGap, geometrySpeed, geometryRight });
            if (key != geometryStudyKey && (!ImGui.IsAnyItemActive() || geometryStudy == null)) {
                geometryStudy = new(profile, new((StudyManoeuvre)geometryManoeuvre, geometryRadius, geometryLead, geometryGap, geometryRight), bodies, geometrySpeed);
                geometryStudyKey = key; geometryExportMessage = null;
            }
            if (key != geometryStudyKey) EditorStyle.Text(HudTheme.Muted, "Az új számítás a mező elengedésekor készül el.");
            var study = geometryStudy; if (study == null) return;
            ImGui.Checkbox("Tengelypályák", ref geometryAxles); ImGui.SameLine();
            ImGui.Checkbox("Keréknyomok", ref geometryWheels); ImGui.SameLine();
            ImGui.Checkbox("Söpört terület", ref geometrySweep); ImGui.SameLine(); ImGui.Checkbox("Sávszegélyek", ref geometryRoad);
            ImGui.SetNextItemWidth(190);
            geometryRadiusAxle = Math.Clamp(geometryRadiusAxle, 0, study.Profile.Axles.Length - 1);
            ImGui.Combo("Kiemelt tengely sugara", ref geometryRadiusAxle, study.Profile.Axles.Select(a => VehiclePathStudy.AxleName(a.Id)).ToArray(), study.Profile.Axles.Length);
            ImGui.SameLine(); ImGui.SetNextItemWidth(180); ImGui.SliderFloat("Jármű helyzete", ref geometryTime, 0, 1, "%.2f");
            if (study.Failure != null) EditorStyle.Text(HudTheme.AmberAccent, study.Failure);
            float total = (float)(study.LeftExtent + study.RightExtent);
            EditorStyle.Text(total > geometryLane ? HudTheme.AmberAccent : HudTheme.Muted,
                $"Söpört szélesség: balra {study.LeftExtent:F2} + jobbra {study.RightExtent:F2} = {total:F2} m · mintalépés: {study.SamplingStep:F3} m");
            ImGui.TextWrapped("Zöld: karosszériák, kerekek és vonórudak söpört uniója. Tengely: folytonos; bal kerék: vékony folytonos; jobb kerék: szaggatott. Az átmeneti ívsugár eltérhet az állandósult értéktől.");
            DrawStudyChart(study);
            if (EditorStyle.QuietButton("Pályarajz mentése SVG-be", "Méretezett vektoros rajz a tengely-, kerékpályákkal és ívsugártáblával.")) {
                try {
                string dir = System.IO.Path.GetFullPath("artifacts/path-studies"); System.IO.Directory.CreateDirectory(dir);
                string file = System.IO.Path.Combine(dir, $"palya-{geometryPreviewKind}-{geometryManoeuvre}-{DateTime.Now:yyyyMMdd-HHmmssfff}.svg");
                System.IO.File.WriteAllText(file, study.ToSvg(geometryLane)); geometryExportMessage = file;
                } catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException) {
                    geometryExportMessage = "Nem sikerült menteni: " + ex.Message;
                }
            }
            if (geometryExportMessage != null) ImGui.TextWrapped(geometryExportMessage);
            DrawStudyRadii(study);
        } catch (ArgumentException ex) { ImGui.TextWrapped(ex.Message); }
        ImGui.Spacing();
    }

    private void DrawStudyContours(RigProfile profile)
    {
        if (!ImGui.CollapsingHeader("Vizsgálati karosszéria-kontúrok és túlnyúlások")) return;
        ImGui.TextWrapped("Téglalap-kontúrok a tengelycsoporthoz képest, méterben. Ezek a pályavizsgálat adatai; nem méretezik át a játék modelljeit. Rakománytúlnyúlás a kontúr kiterjesztésével vizsgálható.");
        var bodies = geometryBodies[geometryPreviewKind];
        if (EditorStyle.QuietButton("Kontúrok a tengelyadatokból", "Újra felveszi a tengelyekből és csuklókból származó kezdeti kontúrokat.")) {
            geometryBodies[geometryPreviewKind] = bodies = StudyBody.Defaults(profile); geometryCustomBodies[geometryPreviewKind] = false;
        }
        if (!ImGui.BeginTable("study-bodies", 4, ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingStretchProp)) return;
        foreach (string label in new[] { "Egység", "Hátsó vég X (m)", "Első vég X (m)", "Szélesség (m)" }) ImGui.TableSetupColumn(label);
        ImGui.TableHeadersRow();
        for (int i = 0; i < bodies.Length; i++) {
            ImGui.TableNextRow(); ImGui.TableNextColumn(); ImGui.TextUnformatted($"{i + 1}. egység");
            float rear = (float)bodies[i].Rear, front = (float)bodies[i].Front, width = (float)bodies[i].Width;
            ImGui.TableNextColumn(); ImGui.SetNextItemWidth(-1); bool changed = ImGui.DragFloat("##body-rear" + i, ref rear, .05f, -40, front - .1f, "%.2f", ImGuiSliderFlags.AlwaysClamp);
            ImGui.TableNextColumn(); ImGui.SetNextItemWidth(-1); changed |= ImGui.DragFloat("##body-front" + i, ref front, .05f, rear + .1f, 40, "%.2f", ImGuiSliderFlags.AlwaysClamp);
            ImGui.TableNextColumn(); ImGui.SetNextItemWidth(-1); changed |= ImGui.DragFloat("##body-width" + i, ref width, .02f, .2f, 10, "%.2f", ImGuiSliderFlags.AlwaysClamp);
            if (changed) { bodies[i] = new(rear, front, width); geometryCustomBodies[geometryPreviewKind] = true; }
        }
        ImGui.EndTable();
    }

    private static void DrawStudyRadii(VehiclePathStudy study)
    {
        if (!ImGui.CollapsingHeader("Tengely- és keréksugarak (m)", ImGuiTreeNodeFlags.DefaultOpen)) return;
        ImGui.TextWrapped("Mért: helyi simulókör az első ív kilépése előtt 0,5 m-rel. Elméleti: állandósult körmozgás. A 90°-os ívben és az S-kanyarban az átmeneti eltérés természetes; az állandósult próbához válaszd a háromkörös vizsgálatot.");
        if (!ImGui.BeginTable("study-radii", 3, ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerV | ImGuiTableFlags.SizingStretchProp)) return;
        ImGui.TableSetupColumn("Tengely / kerék"); ImGui.TableSetupColumn("Mért helyi R (m)"); ImGui.TableSetupColumn("Elméleti állandósult R (m)"); ImGui.TableHeadersRow();
        foreach (var row in study.Radii) {
            ImGui.TableNextRow(); ImGui.TableNextColumn(); EditorStyle.Text(GeometryColours[row.Axle % GeometryColours.Length], row.Name);
            ImGui.TableNextColumn(); ImGui.TextUnformatted(double.IsFinite(row.Observed) ? row.Observed.ToString("F3") : "—");
            ImGui.TableNextColumn(); ImGui.TextUnformatted(double.IsFinite(row.Steady) ? row.Steady.ToString("F3") : "nem létezik");
        }
        ImGui.EndTable();
    }
}

