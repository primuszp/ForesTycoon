using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using ImGuiNET;
using Vec2 = System.Numerics.Vector2;

namespace ForesTycoon.Editor;

internal sealed partial class CurrentRulesView
{
    private readonly VehicleModelSurvey[] geometrySurveys = new VehicleModelSurvey[3];
    private readonly bool[] geometrySurveyed = new bool[3];
    private readonly string[] geometrySurveyErrors = new string[3];

    private readonly record struct BindingRow(string Label, string Profile, string Binding, double Physical);

    private BindingRow[] GeometryBindingRows(RigProfile profile)
    {
        if (geometryPreviewKind == 0) {
            var rows = new[] {
                new BindingRow("Tengelytáv", "TruckWheelbase", "TruckModelWheelbase", profile.FrontLength),
                new BindingRow("Nyomtáv", "TruckTrack", "TruckModelTrack", profile.Axles[0].Track),
                new BindingRow("Keréksugár", "TruckWheelRadius", "TruckModelWheelRadius", profile.Axles[0].Radius) };
            return profile.Trailers.Length == 0 ? rows : rows.Append(new("Vonópont", "TruckHitchOffset", "TruckModelHitch", profile.Trailers[0].HitchX))
                .Append(new("Pótkocsihossz", "TruckTrailerLength", "TruckModelTrailerLength", profile.Trailers[^1].Length)).ToArray();
        }
        string m = GeometryMachine;
        bool rigid = profile.Steering == SteeringGeometry.Axle;
        var common = new[] {
            new BindingRow("Nyomtáv", m + "Track", m + "ModelTrack", profile.Axles[0].Track),
            new BindingRow("Keréksugár", m + "WheelRadius", m + "ModelWheelRadius", profile.Axles[0].Radius) };
        return rigid ? common : new[] {
            new BindingRow("Első tengelycsoport–csukló", m + "FrontLength", m + "ModelFrontLength", profile.FrontLength),
            new BindingRow("Csukló–hátsó tengelycsoport", m + "RearLength", m + "ModelRearLength", profile.RearLength) }.Concat(common).ToArray();
    }

    private void DrawGeometryBinding(RigProfile profile, GameTuning tuning)
    {
        var survey = GeometrySurvey();
        var binding = geometryPreviewKind == 0 ? VehicleGeometryProfiles.TruckBinding(tuning) :
            VehicleGeometryProfiles.MachineBinding(tuning, geometryPreviewKind == 1 ? ForestMachineKind.Forwarder : ForestMachineKind.Harvester);
        var rows = GeometryBindingRows(profile);
        float height = Math.Max(420, ImGui.GetContentRegionAvail().Y), width = Math.Clamp(ImGui.GetContentRegionAvail().X * .42f, 400, 540);
        ImGui.BeginChild("binding-form", new Vec2(width, height), ImGuiChildFlags.Borders);
        DrawBindingModelCard(profile, survey);
        DrawBindingTable(rows);
        DrawBindingAxleGroups(profile, survey);
        ImGui.EndChild(); ImGui.SameLine();
        ImGui.BeginChild("binding-plan", new Vec2(0, height), ImGuiChildFlags.Borders, ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse);
        ImGui.PushTextWrapPos();
        EditorStyle.Text(HudTheme.Muted, "Színes kerék: a fizikai tengely. Sárga keret: a modell saját kereke a kötés szerint. Ha a kettő fedi egymást, a kerék a 3D-ben is a helyén ül.");
        ImGui.PopTextWrapPos();
        DrawRigPlan(profile, new Vec2(ImGui.GetContentRegionAvail().X, Math.Max(200, ImGui.GetContentRegionAvail().Y)), false, survey, binding);
        ImGui.EndChild();
    }

    private void DrawBindingModelCard(RigProfile profile, VehicleModelSurvey survey)
    {
        EditorStyle.Section("A típus modellje");
        ImGui.PushTextWrapPos();
        if (survey != null) {
            EditorStyle.Text(HudTheme.Parchment, $"{survey.File} · {survey.Wheels.Length} kerékcsomópont · {(survey.Licensed ? "helyi, licencelt modell" : "a repó saját modellje")}");
            if (!survey.Jointed && profile.Axles.Any(a => a.Unit > 0))
                EditorStyle.Text(HudTheme.AmberAccent, "Ez a modell egyetlen merev test. A pótkocsi a szimulációban és a pályapróbában valódi csuklóval mozog, de a 3D-ben nem válik le a vontatóról.");
        } else if (geometrySurveyErrors[geometryPreviewKind] != null) EditorStyle.Text(HudTheme.Bad, geometrySurveyErrors[geometryPreviewKind]);
        else EditorStyle.Text(HudTheme.AmberAccent, $"Nincs telepítve modell ({(geometryPreviewKind == 1 ? "forwarder" : "harvester")}.glb), ezért a játék egyszerű helyettesítő gépet rajzol. A kötés értékei a modell telepítésekor lépnek életbe.");
        EditorStyle.Text(HudTheme.Muted, geometryPreviewKind == 0 ?
            "Modellcsere: Assets/Licensed/log-truck-textured.glb, csomópontok: wheel_front*, wheel_*, trailer, trailer_wheel_*, cargo_*." :
            $"Modellcsere: Assets/Licensed/{(geometryPreviewKind == 1 ? "forwarder" : "harvester")}.glb, csomópontok: wheel*.L, wheel*.R, trailer (a hátsó váz gyökere).");
        ImGui.PopTextWrapPos();
        if (EditorStyle.QuietButton("Modell újraolvasása", "A modellfájl újbóli felmérése, például csere után.")) geometrySurveyed[geometryPreviewKind] = false;
    }

    private void DrawBindingTable(BindingRow[] rows)
    {
        EditorStyle.Section("Méretek kötése");
        ImGui.PushTextWrapPos();
        EditorStyle.Text(HudTheme.Muted, "A kötés megadja, milyen fizikai méretet ábrázol a modell. A karosszéria nem nyúlik meg; az eltérés annyit jelent, hogy a kerék vagy a csukló ennyivel a modell saját helyétől eltérve jelenik meg.");
        ImGui.PopTextWrapPos();
        if (ImGui.BeginTable("binding-rows", 4, ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.PadOuterX)) {
            foreach (string label in new[] { "Méret", "Profil", "A modell ezt ábrázolja", "Eltérés" }) ImGui.TableSetupColumn(label);
            ImGui.TableHeadersRow();
            foreach (var row in rows) {
                double bound = GetGeometry(row.Binding), gap = row.Physical - bound;
                ImGui.TableNextRow(); ImGui.TableNextColumn(); ImGui.AlignTextToFramePadding(); ImGui.TextUnformatted(row.Label);
                ImGui.TableNextColumn(); ImGui.AlignTextToFramePadding(); ImGui.TextUnformatted($"{row.Physical:F2} m");
                ImGui.TableNextColumn(); ImGui.SetNextItemWidth(-1); FindGeometry(row.Binding, out var rule, out int index); var p = rule.Parameters[index];
                float value = (float)p.Value;
                if (ImGui.DragFloat("##" + row.Binding, ref value, .02f, (float)p.Min, (float)p.Max, "%.2f m", ImGuiSliderFlags.AlwaysClamp)) SetGeometry(row.Binding, Math.Round(value, 2));
                ImGui.TableNextColumn(); ImGui.AlignTextToFramePadding();
                if (Math.Abs(gap) < .005) EditorStyle.Text(HudTheme.Good, "illeszkedik"); else EditorStyle.Text(HudTheme.AmberAccent, $"{gap:+0.00;-0.00} m");
            }
            ImGui.EndTable();
        }
        bool bound2 = rows.All(r => Math.Abs(r.Physical - GetGeometry(r.Binding)) < .005);
        if (EditorStyle.PrimaryButton("Modell kötése ehhez az elrendezéshez", !bound2, "A modell ezentúl a jelenlegi tengelyméreteket ábrázolja: minden kerék a modell saját kerekének helyére kerül."))
            foreach (var row in rows) SetGeometry(row.Binding, row.Physical);
        ImGui.SameLine();
        if (EditorStyle.QuietButton("Profil a kötés szerint", "Fordított irány: a fizikai tengelyméretek vegyék fel a kötés értékeit."))
            foreach (var row in rows) SetGeometry(row.Profile, GetGeometry(row.Binding));
    }

    private void DrawBindingAxleGroups(RigProfile profile, VehicleModelSurvey survey)
    {
        if (survey == null || survey.Wheels.Length == 0) return;
        EditorStyle.Section("Tengelyszámok");
        bool jointed = survey.Jointed;
        (string Label, string Key, int Physical, int Model)[] groups = geometryPreviewKind == 0 ?
            new[] { ("Hátsó tengelyek", "TruckRearAxles", profile.Axles.Count(a => a.Id.StartsWith("truck.rear")), Math.Max(1, survey.Axles(0).Length - 1)),
                ("Pótkocsitengelyek", "TruckTrailerAxles", profile.Axles.Count(a => a.Id.StartsWith("trailer.rear")), jointed ? survey.Axles(1).Length : 0) } :
            new[] { ("Első tengelyek", GeometryMachine + "FrontAxles", profile.Axles.Count(a => a.Id.StartsWith("front")), survey.Axles(0).Length),
                ("Hátsó tengelyek", GeometryMachine + "RearAxles", profile.Axles.Count(a => a.Id.StartsWith("rear")), jointed ? survey.Axles(1).Length : 0) };
        var measured = groups.Where(g => g.Model > 0 && g.Physical > 0).ToArray();
        foreach (var group in measured)
            EditorStyle.Text(group.Physical == group.Model ? HudTheme.Muted : HudTheme.AmberAccent, $"{group.Label}: profil {group.Physical} · modell {group.Model}");
        if (measured.Any(g => g.Physical != g.Model) && EditorStyle.QuietButton("Tengelyszámok átvétele a modellből", "A profil tengelyszámai igazodnak a modell kerékcsomópontjaihoz."))
            foreach (var group in measured) SetGeometry(group.Key, group.Model);
    }

    private VehicleModelSurvey GeometrySurvey()
    {
        int kind = geometryPreviewKind;
        if (geometrySurveyed[kind]) return geometrySurveys[kind];
        geometrySurveyed[kind] = true; geometrySurveyErrors[kind] = null; geometrySurveys[kind] = null;
        try { geometrySurveys[kind] = kind == 0 ? VehicleModelSurvey.Truck() : VehicleModelSurvey.Machine(kind == 1 ? ForestMachineKind.Forwarder : ForestMachineKind.Harvester); }
        catch (Exception ex) when (ex is IOException or InvalidDataException or NotSupportedException or JsonException or InvalidOperationException or System.Collections.Generic.KeyNotFoundException) {
            geometrySurveyErrors[kind] = "A modell nem olvasható: " + ex.Message;
        }
        return geometrySurveys[kind];
    }

    /// <summary>The model's wheels where the binding puts them in the physical plan: exactly on the axles when the two agree.</summary>
    private void DrawModelWheels(ImDrawListPtr draw, RigProfile profile, VehicleModelSurvey survey, RigModelBinding binding, Func<PlanPoint, Vec2> S, float ppm)
    {
        double frontX = survey.Wheels.Where(w => w.Unit == 0).Select(w => w.X).DefaultIfEmpty().Max();
        double rearX = geometryPreviewKind == 0 ? survey.Wheels.Where(w => w.Unit == 0 && w.X < frontX - VehicleModelSurvey.GroupTolerance).Select(w => w.X).DefaultIfEmpty(frontX).Average() : 0;
        double scale = geometryPreviewKind == 0 && frontX > rearX ? binding.FrontLength / (frontX - rearX) : 1;
        double trackScale = binding.Track / Math.Max(.01, survey.Track(0));
        double radius = binding.WheelRadius;
        double trailerCentre = survey.Centre(1), rearCentre = survey.Centre(1);
        double trailerScale = survey.Joint is { } joint && joint > trailerCentre && binding.TrailerLength > 0 ? binding.TrailerLength / (joint - trailerCentre) : 1;
        foreach (var wheel in survey.Wheels) {
            double x;
            if (geometryPreviewKind == 0) x = wheel.Unit == 0 ? (wheel.X - rearX) * scale : binding.Hitch - (survey.Joint.Value - wheel.X) * trailerScale;
            else if (wheel.Unit == 0) x = wheel.X - survey.Centre(0);
            else x = wheel.X - rearCentre - binding.FrontLength - binding.RearLength;
            double side = wheel.Side * trackScale;
            // A twin axle flattened to one centre pivot still has two sides on the road.
            foreach (double y in Math.Abs(side) < .01 ? new[] { -binding.Track * .5, binding.Track * .5 } : new[] { side }) {
                var centre = S(new(x, y)); var half = new Vec2((float)radius * ppm, (float)radius * .3f * ppm);
                draw.AddRect(centre - half, centre + half, EditorStyle.Selection, 2, ImDrawFlags.None, 1.6f);
            }
        }
    }
}
