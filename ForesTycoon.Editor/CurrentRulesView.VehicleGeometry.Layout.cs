using System;
using System.Collections.Generic;
using System.Linq;
using ImGuiNET;
using Vec2 = System.Numerics.Vector2;
using Vec4 = System.Numerics.Vector4;

namespace ForesTycoon.Editor;

internal sealed partial class CurrentRulesView
{
    private sealed record GeometryPreset(string Name, string Help, (string Key, double Value)[] Values);
    private static readonly GeometryPreset[][] GeometryPresets = {
        new GeometryPreset[] {
            new("Szóló tehergépkocsi, 4×2", "Két tengely, pótkocsi nélkül.", new[] { ("TruckTrailerLayout", 0d), ("TruckRearAxles", 1d), ("TruckWheelbase", 4d) }),
            new("Szóló tehergépkocsi, 6×4", "Tandem hátsó tengelycsoport, pótkocsi nélkül.", new[] { ("TruckTrailerLayout", 0d), ("TruckRearAxles", 2d), ("TruckAxleSpacing", 1.3), ("TruckWheelbase", 4.2) }),
            new("Nyerges 6×4 + kéttengelyes pótkocsi", "A játék alapszerelvénye.", new[] { ("TruckTrailerLayout", 1d), ("TruckRearAxles", 2d), ("TruckAxleSpacing", 1.3), ("TruckWheelbase", 4d), ("TruckHitchOffset", -1.5), ("TruckTrailerLength", 6d), ("TruckTrailerAxles", 2d), ("TruckTrailerAxleSpacing", 1.3) }),
            new("Nyerges 4×2 + háromtengelyes félpótkocsi", "Rövid vontató, a királycsap a hátsó tengely fölött.", new[] { ("TruckTrailerLayout", 1d), ("TruckRearAxles", 1d), ("TruckWheelbase", 3.8), ("TruckHitchOffset", .3), ("TruckTrailerLength", 7.7), ("TruckTrailerAxles", 3d), ("TruckTrailerAxleSpacing", 1.3) }),
            new("6×4 + vonórudas, forgózsámolyos pótkocsi", "A vonórúd és a pótkocsi két külön passzív csukló.", new[] { ("TruckTrailerLayout", 2d), ("TruckRearAxles", 2d), ("TruckAxleSpacing", 1.3), ("TruckWheelbase", 4.2), ("TruckHitchOffset", -2d), ("TruckDrawbarLength", 3d), ("TruckTrailerLength", 4.5), ("TruckTrailerAxles", 2d), ("TruckTrailerAxleSpacing", 1.3) }),
        },
        new GeometryPreset[] {
            new("6 kerekű forwarder (2 + 4)", "Egy első tengely, hátul bogi.", new[] { ("ForwarderFrontAxles", 1d), ("ForwarderRearAxles", 2d), ("ForwarderFrontLength", 2.2), ("ForwarderRearLength", 4.8), ("ForwarderAxleSpacing", 1.4) }),
            new("8 kerekű forwarder (4 + 4)", "A játék alapgépe: elöl és hátul bogi.", new[] { ("ForwarderFrontAxles", 2d), ("ForwarderRearAxles", 2d), ("ForwarderFrontLength", 2.2), ("ForwarderRearLength", 4.8), ("ForwarderAxleSpacing", 1.4) }),
            new("10 kerekű forwarder (4 + 6)", "Három hátsó tengely a nagy rakományhoz.", new[] { ("ForwarderFrontAxles", 2d), ("ForwarderRearAxles", 3d), ("ForwarderFrontLength", 2.2), ("ForwarderRearLength", 5.4), ("ForwarderAxleSpacing", 1.4) }),
        },
        new GeometryPreset[] {
            new("Merev törzsű, négykerekű processzor", "Kerékkormányzás: a játék alapgépe.", new[] { ("ProcessorSteeringModel", 0d), ("ProcessorFrontAxles", 1d), ("ProcessorRearAxles", 1d), ("ProcessorWheelbase", 3.2326) }),
            new("Csuklós, 6 kerekű harvester (4 + 2)", "Elöl bogi, derékcsuklós kormányzás.", new[] { ("ProcessorSteeringModel", 1d), ("ProcessorFrontAxles", 2d), ("ProcessorRearAxles", 1d), ("ProcessorFrontLength", 2.8), ("ProcessorRearLength", 3.2), ("ProcessorAxleSpacing", 1.4) }),
            new("Csuklós, 8 kerekű harvester (4 + 4)", "Elöl és hátul is bogi, derékcsuklós kormányzás.", new[] { ("ProcessorSteeringModel", 1d), ("ProcessorFrontAxles", 2d), ("ProcessorRearAxles", 2d), ("ProcessorFrontLength", 2.8), ("ProcessorRearLength", 3.6), ("ProcessorAxleSpacing", 1.4) }),
        },
    };

    /// <summary>Every template must be a buildable vehicle that drives a 25 m bend.</summary>
    internal string[] GeometryPresetProblemsForTest()
    {
        var problems = new System.Collections.Generic.List<string>();
        int kind = geometryPreviewKind;
        for (geometryPreviewKind = 0; geometryPreviewKind < GeometryPresets.Length; geometryPreviewKind++)
            foreach (var preset in GeometryPresets[geometryPreviewKind]) {
                var saved = preset.Values.Select(v => (v.Key, Value: GetGeometry(v.Key))).ToArray();
                foreach (var (key, value) in preset.Values) SetGeometry(key, value);
                var profile = GeometryProfile(GeometryTuning(out _)); profile.Validate();
                problems.AddRange(profile.Warnings().Select(w => preset.Name + ": " + w));
                string failure = new VehiclePathStudy(profile, new(StudyManoeuvre.QuarterTurn, 25), StudyBody.Defaults(profile)).Failure;
                if (failure != null) problems.Add(preset.Name + ": " + failure);
                foreach (var (key, value) in saved) SetGeometry(key, value);
            }
        geometryPreviewKind = kind;
        return problems.ToArray();
    }

    private bool geometrySteered;
    private float geometryPlanScale; private Vec2 geometryPlanOrigin; private string geometryPlanDrag; private double geometryPlanDragStart;

    private void DrawGeometryLayout(RigProfile profile, string problem)
    {
        float height = Math.Max(420, ImGui.GetContentRegionAvail().Y), width = Math.Clamp(ImGui.GetContentRegionAvail().X * .34f, 330, 430);
        ImGui.BeginChild("geometry-form", new Vec2(width, height), ImGuiChildFlags.Borders);
        DrawGeometryForm(profile);
        ImGui.EndChild(); ImGui.SameLine();
        ImGui.BeginChild("geometry-plan", new Vec2(0, height), ImGuiChildFlags.Borders, ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse);
        if (profile == null) { ImGui.PushTextWrapPos(); EditorStyle.Text(HudTheme.Bad, problem); ImGui.PopTextWrapPos(); }
        else {
            var warnings = profile.Warnings();
            ImGui.Checkbox("Teljes kormányállás", ref geometrySteered);
            if (ImGui.IsItemHovered()) ImGui.SetTooltip("Az állóhelyzetben elért legnagyobb kormány- vagy csuklószöget mutatja.");
            ImGui.SameLine(0, 18); ImGui.AlignTextToFramePadding();
            EditorStyle.Text(HudTheme.Muted, "A sárga fogók húzásával is áthelyezhetők a tengelyek és csuklók.");
            float footer = (ImGui.GetTextLineHeightWithSpacing() + 2) * (2 + warnings.Length);
            DrawRigPlan(profile, new Vec2(ImGui.GetContentRegionAvail().X, Math.Max(180, ImGui.GetContentRegionAvail().Y - footer)), true, null, null);
            DrawGeometryFacts(profile, warnings);
        }
        ImGui.EndChild();
    }

    private void DrawGeometryForm(RigProfile profile)
    {
        var presets = GeometryPresets[geometryPreviewKind];
        EditorStyle.Section("Kiindulás");
        ImGui.SetNextItemWidth(-1);
        if (ImGui.BeginCombo("##geometry-preset", "Sablon betöltése…")) {
            foreach (var preset in presets) {
                if (ImGui.Selectable(preset.Name)) foreach (var (key, value) in preset.Values) SetGeometry(key, value);
                if (ImGui.IsItemHovered()) ImGui.SetTooltip(preset.Help);
            }
            ImGui.EndCombo();
        }
        if (geometryPreviewKind == 0) TruckFields(); else MachineFields(profile);
    }

    private void TruckFields()
    {
        EditorStyle.Section("Felépítés");
        GeometryChoice("TruckTrailerLayout", "Hány passzív csukló követi a vontatót.", "Szóló tehergépkocsi", "Nyerges / középtengelyes pótkocsi", "Vonórudas, forgózsámolyos pótkocsi");
        int layout = (int)Math.Round(GetGeometry("TruckTrailerLayout"));
        EditorStyle.Section("Vontató");
        GeometryNumber("TruckWheelbase", "Tengelytáv", "Az első tengely és a hátsó tengelycsoport közepe közti távolság.");
        GeometryCount("TruckRearAxles", "Hátsó tengelyek", "1: egyes tengely, 2: tandem, 3: tridem.");
        if (GetGeometry("TruckRearAxles") > 1) GeometryNumber("TruckAxleSpacing", "Tengelyosztás", "A hátsó tengelycsoporton belüli távolság.");
        GeometryNumber("TruckTrack", "Nyomtáv", "A két oldal kerekeinek középtávolsága.");
        GeometryNumber("TruckWheelRadius", "Keréksugár", "A kerék gördülési sugara.");
        EditorStyle.Section("Kormányzás");
        GeometryNumber("TruckMaxSteer", "Legnagyobb kerékszög", "A belső kormányzott kerék mechanikai ütközője.", .2f);
        GeometryNumber("TruckSteerRate", "Kormányzási sebesség", "Ennél gyorsabban nem fordulhat a kormány.", .5f);
        if (layout == 0) return;
        EditorStyle.Section("Kapcsolás");
        GeometryNumber("TruckHitchOffset", "Vonópont helye", "A hátsó tengelycsoporthoz képest: előre pozitív, hátra negatív.");
        if (layout == 2) GeometryNumber("TruckDrawbarLength", "Vonórúd hossza", "A vonóhorog és a forgózsámoly tengelye közti távolság.");
        GeometryNumber("TruckTrailerMaxAngle", "Csukló szöghatára", "Ennél a szögnél a szerelvény becsuklottnak számít és megáll.", .2f);
        EditorStyle.Section("Pótkocsi");
        GeometryNumber("TruckTrailerLength", layout == 2 ? "Pótkocsi tengelytávja" : "Királycsap–tengelycsoport", "A kapcsolópont és a pótkocsi tengelycsoportjának közepe közti távolság.");
        GeometryCount("TruckTrailerAxles", "Tengelyek", "A pótkocsi hátsó tengelycsoportja.");
        if (GetGeometry("TruckTrailerAxles") > 1) GeometryNumber("TruckTrailerAxleSpacing", "Tengelyosztás", "A pótkocsi tengelycsoportján belüli távolság.");
    }

    private void MachineFields(RigProfile profile)
    {
        string m = GeometryMachine;
        bool wheelSteered = geometryPreviewKind == 2 && GetGeometry("ProcessorSteeringModel") < .5;
        if (geometryPreviewKind == 2) {
            EditorStyle.Section("Kormányzás módja");
            GeometryChoice("ProcessorSteeringModel", "Merev törzs kormányzott tengellyel, vagy két váz aktív derékcsuklóval.", "Kerékkormányzás, merev törzs", "Derékcsuklós kormányzás");
        }
        if (wheelSteered) {
            EditorStyle.Section("Törzs");
            GeometryNumber("ProcessorWheelbase", "Tengelytáv", "A két tengelycsoport közepe közti távolság.");
        } else {
            EditorStyle.Section("Első váz");
            GeometryNumber(m + "FrontLength", "Tengelycsoport–csukló", "Az első tengelycsoport közepe és a derékcsukló távolsága.");
        }
        GeometryCount(m + "FrontAxles", "Első tengelyek", "1: egyes tengely, 2: bogi.");
        if (!wheelSteered) {
            EditorStyle.Section("Hátsó váz");
            GeometryNumber(m + "RearLength", "Csukló–tengelycsoport", "A derékcsukló és a hátsó tengelycsoport közepe közti távolság.");
        }
        GeometryCount(m + "RearAxles", "Hátsó tengelyek", "1: egyes tengely, 2: bogi, 3: hármas.");
        EditorStyle.Section("Kerekek");
        if (GetGeometry(m + "FrontAxles") > 1 || GetGeometry(m + "RearAxles") > 1) GeometryNumber(m + "AxleSpacing", "Tengelyosztás", "A bogin belüli tengelytávolság.");
        GeometryNumber(m + "Track", "Nyomtáv", "A két oldal kerekeinek középtávolsága.");
        GeometryNumber(m + "WheelRadius", "Keréksugár", "A kerék gördülési sugara.");
        EditorStyle.Section("Kormányzás");
        GeometryNumber(m + "MaxSteer", wheelSteered ? "Legnagyobb kerékszög" : "Legnagyobb csuklószög", "A kormányzás mechanikai ütközője.", .2f);
        GeometryNumber(m + "SteerRate", "Kormányzási sebesség", "Ennél gyorsabban nem fordulhat a kormány vagy a csukló.", .5f);
    }

    private void DrawGeometryFacts(RigProfile profile, string[] warnings)
    {
        var bodies = StudyBody.Defaults(profile);
        int units = bodies.Length;
        double radius = VehiclePathStudy.SmallestRoadRadius(profile);
        EditorStyle.Text(HudTheme.Parchment, $"Legkisebb fordulósugár az első tengelyen: {radius:F2} m · {profile.Axles.Length} tengely · {units} merev egység");
        string reference = profile.Steering == SteeringGeometry.Axle ? "a hátsó" : "az első";
        EditorStyle.Text(HudTheme.Muted, $"A fehér háromszög {reference} tengelycsoport közepe: ezt vezeti a pályakövető.");
        foreach (string warning in warnings) EditorStyle.Text(HudTheme.AmberAccent, warning);
    }

    // ── The plan view, to scale, shared by the layout and the binding tab ──
    private readonly record struct PlanHandle(string Key, double X, double Sign, string Help);
    private readonly Dictionary<string, Vec2> geometryHandlePoints = new();
    internal Vec2 GeometryHandleForTest(string key) => geometryHandlePoints[key];

    private List<PlanHandle> GeometryHandles(RigProfile profile, VehicleKinematics rig)
    {
        var result = new List<PlanHandle>();
        if (geometryPreviewKind == 0) {
            result.Add(new("TruckWheelbase", profile.FrontLength, 1, "Tengelytáv"));
            if (profile.Trailers.Length > 0) {
                result.Add(new("TruckHitchOffset", profile.Trailers[0].HitchX, 1, "Vonópont helye"));
                if (profile.Trailers.Length > 1) result.Add(new("TruckDrawbarLength", rig.Units[1].Axle.X, -1, "Vonórúd hossza"));
                result.Add(new("TruckTrailerLength", rig.Units[^1].Axle.X, -1, "Pótkocsi hossza"));
            }
        } else if (profile.Steering == SteeringGeometry.Axle) result.Add(new("ProcessorWheelbase", profile.FrontLength, 1, "Tengelytáv"));
        else {
            result.Add(new(GeometryMachine + "FrontLength", -profile.FrontLength, -1, "Tengelycsoport–csukló"));
            result.Add(new(GeometryMachine + "RearLength", rig.Units[1].Axle.X, -1, "Csukló–hátsó tengelycsoport"));
        }
        return result;
    }

    private void DrawRigPlan(RigProfile profile, Vec2 size, bool interactive, VehicleModelSurvey survey, RigModelBinding binding)
    {
        var rig = new VehicleKinematics(profile, default, 0);
        var straight = rig.Units.ToArray();
        if (geometrySteered && interactive) rig.Advance(0, profile.EquivalentSteerLimit, profile.EquivalentSteerLimit / profile.SteerRate + .05);
        var bodies = StudyBody.Defaults(profile);
        var outline = new List<PlanPoint>();
        for (int i = 0; i < bodies.Length; i++) outline.AddRange(VehiclePathStudy.Rectangle(straight[i].Axle, straight[i].Yaw, bodies[i].Rear, bodies[i].Front, bodies[i].Width));
        double minX = outline.Min(p => p.X) - .6, maxX = outline.Max(p => p.X) + .6, halfWidth = outline.Max(p => Math.Abs(p.Y)) + (geometrySteered && interactive ? 1.6 : .4);
        var at = ImGui.GetCursorScreenPos();
        var draw = ImGui.GetWindowDrawList();
        draw.AddRectFilled(at, at + size, EditorStyle.Canvas, 4);
        draw.PushClipRect(at, at + size, true);
        if (geometryPlanDrag == null || !interactive) {
            geometryPlanScale = (float)Math.Min((size.X - 50) / (maxX - minX), (size.Y - 120) / (halfWidth * 2));
            geometryPlanOrigin = at + new Vec2((size.X - (float)(maxX - minX) * geometryPlanScale) * .5f - (float)minX * geometryPlanScale, size.Y * .43f);
        }
        float ppm = geometryPlanScale; Vec2 origin = geometryPlanOrigin;
        Vec2 S(PlanPoint p) => origin + new Vec2((float)p.X * ppm, -(float)p.Y * ppm);
        for (int metres = (int)Math.Floor(minX); metres <= maxX; metres++) {
            float x = S(new(metres, 0)).X;
            draw.AddLine(new Vec2(x, at.Y), new Vec2(x, at.Y + size.Y), metres % 5 == 0 ? EditorStyle.GridMajor : EditorStyle.GridMinor);
        }
        draw.AddLine(S(new(minX, 0)), S(new(maxX, 0)), EditorStyle.U(HudTheme.Muted), 1);
        DrawPlanBodies(draw, profile, rig, bodies, S, ppm);
        var dimensions = GeometryDimensions(profile, straight);
        float below = S(new(0, -halfWidth)).Y + 12;
        foreach (var (from, to, row, text) in dimensions) {
            float y = below + row * 22, x0 = S(new(from, 0)).X, x1 = S(new(to, 0)).X; uint colour = EditorStyle.U(HudTheme.Parchment);
            draw.AddLine(new Vec2(x0, y), new Vec2(x1, y), colour); draw.AddLine(new Vec2(x0, y - 4), new Vec2(x0, y + 4), colour); draw.AddLine(new Vec2(x1, y - 4), new Vec2(x1, y + 4), colour);
            var label = ImGui.CalcTextSize(text);
            draw.AddRectFilled(new Vec2((x0 + x1 - label.X) * .5f - 3, y - 8), new Vec2((x0 + x1 + label.X) * .5f + 3, y + 8), EditorStyle.Canvas);
            draw.AddText(new Vec2((x0 + x1 - label.X) * .5f, y - 8), colour, text);
        }
        if (survey != null && binding != null) DrawModelWheels(draw, profile, survey, binding, S, ppm);
        draw.AddText(S(new(maxX, 0)) + new Vec2(-48, -18), EditorStyle.TextMuted, "előre");
        draw.PopClipRect();
        if (interactive) DrawPlanHandles(draw, profile, rig, S, at, size);
        ImGui.SetCursorScreenPos(at); ImGui.Dummy(size);
    }

    private void DrawPlanBodies(ImDrawListPtr draw, RigProfile profile, VehicleKinematics rig, StudyBody[] bodies, Func<PlanPoint, Vec2> S, float ppm)
    {
        for (int i = 0; i < bodies.Length; i++) {
            var corners = VehiclePathStudy.Rectangle(rig.Units[i].Axle, rig.Units[i].Yaw, bodies[i].Rear, bodies[i].Front, bodies[i].Width);
            draw.AddQuadFilled(S(corners[0]), S(corners[1]), S(corners[2]), S(corners[3]), EditorStyle.U(.22f, .3f, .25f, .75f));
            draw.AddQuad(S(corners[0]), S(corners[1]), S(corners[2]), S(corners[3]), EditorStyle.U(HudTheme.Muted), 1);
            draw.AddText(S(corners[3]) + new Vec2(2, -20), EditorStyle.TextMuted, GeometryUnitName(profile, i));
        }
        int start = profile.Steering == SteeringGeometry.Articulated ? 2 : 1;
        if (start == 2) draw.AddCircle(S(rig.Units[0].Axle - PlanPoint.Forward(rig.Units[0].Yaw) * profile.FrontLength), 6, EditorStyle.Selection, 16, 2);
        for (int i = 0; i < profile.Trailers.Length; i++) {
            var hitch = rig.Units[start + i - 1].Axle + PlanPoint.Forward(rig.Units[start + i - 1].Yaw) * profile.Trailers[i].HitchX;
            draw.AddLine(S(hitch), S(rig.Units[start + i].Axle), EditorStyle.U(HudTheme.Parchment), 2);
            draw.AddCircle(S(hitch), 5, EditorStyle.Selection, 16, 2);
        }
        var wheels = rig.Wheels();
        for (int a = 0; a < profile.Axles.Length; a++) {
            var axle = profile.Axles[a]; var unit = rig.Units[axle.Unit]; uint colour = EditorStyle.U(GeometryGroupColour(profile, axle.Id));
            draw.AddLine(S(wheels[a * 2].Position), S(wheels[a * 2 + 1].Position), colour, 1.5f);
            foreach (var wheel in new[] { wheels[a * 2], wheels[a * 2 + 1] }) {
                var tyre = VehiclePathStudy.Rectangle(wheel.Position, wheel.Yaw, -axle.Radius, axle.Radius, axle.Radius * .6);
                draw.AddQuadFilled(S(tyre[0]), S(tyre[1]), S(tyre[2]), S(tyre[3]), colour);
            }
            if (axle.Steered) draw.AddText(S(unit.Axle + PlanPoint.Forward(unit.Yaw) * axle.X) + new Vec2(-22, -(float)axle.Track * .5f * ppm - 26), colour, "kormányzott");
        }
        var reference = rig.Units[0].Axle;
        draw.AddTriangleFilled(S(reference) + new Vec2(-6, 10), S(reference) + new Vec2(6, 10), S(reference), EditorStyle.U(HudTheme.Parchment));
    }

    private static List<(double From, double To, int Row, string Text)> GeometryDimensions(RigProfile profile, RigUnitPose[] units)
    {
        var result = new List<(double, double, int, string)>();
        if (profile.Steering == SteeringGeometry.Axle) result.Add((0, profile.FrontLength, 0, $"{profile.FrontLength:F2} m"));
        else {
            double joint = -profile.FrontLength;
            result.Add((joint, 0, 0, $"{profile.FrontLength:F2} m")); result.Add((units[1].Axle.X, joint, 0, $"{profile.RearLength:F2} m"));
        }
        int start = profile.Steering == SteeringGeometry.Articulated ? 2 : 1;
        for (int i = 0; i < profile.Trailers.Length; i++) {
            var t = profile.Trailers[i]; double hitch = units[start + i - 1].Axle.X + t.HitchX;
            if (i == 0 && Math.Abs(t.HitchX) > .01) result.Add((Math.Min(hitch, units[start + i - 1].Axle.X), Math.Max(hitch, units[start + i - 1].Axle.X), 1, $"{Math.Abs(t.HitchX):F2} m"));
            result.Add((units[start + i].Axle.X, hitch, 0, $"{t.Length:F2} m"));
        }
        double min = profile.Axles.Min(a => units[a.Unit].Axle.X + a.X), max = profile.Axles.Max(a => units[a.Unit].Axle.X + a.X);
        result.Add((min, max, 2, $"Szélső tengelyek távolsága: {max - min:F2} m"));
        return result;
    }

    private void DrawPlanHandles(ImDrawListPtr draw, RigProfile profile, VehicleKinematics rig, Func<PlanPoint, Vec2> S, Vec2 at, Vec2 size)
    {
        bool anyActive = false;
        foreach (var handle in GeometryHandles(profile, rig)) {
            float x = S(new(handle.X, 0)).X;
            geometryHandlePoints[handle.Key] = new Vec2(x, at.Y + 30);
            ImGui.SetCursorScreenPos(new Vec2(x - 9, at.Y + 4));
            ImGui.InvisibleButton("plan-handle" + handle.Key, new Vec2(18, size.Y * .75f));
            bool active = ImGui.IsItemActive(), hovered = ImGui.IsItemHovered();
            if (hovered || active) ImGui.SetMouseCursor(ImGuiMouseCursor.ResizeEW);
            if (hovered && !active) ImGui.SetTooltip(handle.Help + ": " + GetGeometry(handle.Key).ToString("F2") + " m");
            if (ImGui.IsItemActivated()) { geometryPlanDrag = handle.Key; geometryPlanDragStart = GetGeometry(handle.Key); }
            if (active && geometryPlanDrag == handle.Key) {
                anyActive = true;
                double dragged = geometryPlanDragStart + handle.Sign * ImGui.GetMouseDragDelta(ImGuiMouseButton.Left, 0).X / geometryPlanScale;
                SetGeometry(handle.Key, Math.Round(dragged / .05) * .05);
            }
            var colour = EditorStyle.U(active || hovered ? HudTheme.Parchment : HudTheme.AmberAccent);
            draw.AddLine(new Vec2(x, at.Y + 14), new Vec2(x, at.Y + size.Y * .75f), EditorStyle.Fade(colour, .45f));
            draw.AddTriangleFilled(new Vec2(x - 7, at.Y + 6), new Vec2(x + 7, at.Y + 6), new Vec2(x, at.Y + 16), colour);
        }
        if (!anyActive) geometryPlanDrag = null;
    }
}
