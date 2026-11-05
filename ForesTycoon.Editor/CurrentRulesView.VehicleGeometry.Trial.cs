using System;
using System.Linq;
using System.Text.Json;
using ImGuiNET;
using Vec2 = System.Numerics.Vector2;
using Vec4 = System.Numerics.Vector4;

namespace ForesTycoon.Editor;

internal sealed partial class CurrentRulesView
{
    private int geometryManoeuvre, geometryPlayRate = 2;
    private float geometryRadius = 20, geometryLead = 20, geometryGap = 10, geometrySpeed = 1, geometryLane = 3.5f, geometryTime;
    private bool geometryRight, geometryAxles = true, geometryWheels, geometrySweep = true, geometryRoad = true, geometryGhosts = true;
    private bool geometryPlaying = true, geometryLoop = true, geometryFollow, geometryScrubbing;
    private readonly StudyBody[][] geometryBodies = new StudyBody[3][];
    private readonly bool[] geometryCustomBodies = new bool[3];
    private readonly string[] geometryBodyKeys = new string[3];
    private VehiclePathStudy geometryStudy;
    private string geometryStudyKey, geometryExportMessage;

    internal void SelectGeometryStudyForTest(int kind, StudyManoeuvre manoeuvre, float radius = 20)
    {
        geometryPreviewKind = kind; geometryManoeuvre = (int)manoeuvre; geometryTab = GeometryTrialTab; geometryPlaying = false; geometryTime = .65f; geometryRadius = radius;
    }
    internal float GeometryPlayheadForTest => geometryTime;
    internal void PlayGeometryForTest() => geometryPlaying = true;
    internal VehiclePathStudy GeometryStudyForTest => geometryStudy;

    private void DrawGeometryTrial(RigProfile profile)
    {
        DrawTrialControls(profile);
        var study = UpdateGeometryStudy(profile);
        if (study == null) return;
        AdvanceTrialPlayback(study);
        float side = Math.Clamp(ImGui.GetContentRegionAvail().X * .24f, 250, 330);
        float height = Math.Max(320, ImGui.GetContentRegionAvail().Y), timeline = 136;
        ImGui.BeginChild("trial-main", new Vec2(ImGui.GetContentRegionAvail().X - side - 8, height), ImGuiChildFlags.None, ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse);
        DrawStudyChart(study, new Vec2(ImGui.GetContentRegionAvail().X, height - timeline - 8));
        DrawTrialTimeline(study, timeline);
        ImGui.EndChild(); ImGui.SameLine();
        ImGui.BeginChild("trial-readout", new Vec2(0, height), ImGuiChildFlags.Borders);
        DrawTrialReadout(study);
        ImGui.EndChild();
    }

    private void DrawTrialControls(RigProfile profile)
    {
        ImGui.SetNextItemWidth(215);
        ImGui.Combo("##geometry-manoeuvre", ref geometryManoeuvre, "90°-os ív\0S-kanyar (90° + ellenív)\0Állandósult körpálya (3 kör)\0"); ImGui.SameLine();
        ImGui.Checkbox("Jobbra kezd", ref geometryRight);
        float right = ImGui.GetCursorStartPos().X + ImGui.GetContentRegionAvail().X, used = ImGui.GetItemRectMax().X - ImGui.GetWindowPos().X;
        void Number(string label, ref float value, float speed, float min, float max, string format) {
            const float width = 96;
            float needed = width + ImGui.CalcTextSize(label).X + 24;
            if (used + needed > right) used = 0; else ImGui.SameLine(0, 14);
            ImGui.SetNextItemWidth(width); ImGui.DragFloat(label, ref value, speed, min, max, format, ImGuiSliderFlags.AlwaysClamp);
            used = ImGui.GetItemRectMax().X - ImGui.GetWindowPos().X;
        }
        Number("Ívsugár", ref geometryRadius, .2f, 2, 100, "%.1f m");
        Number("Be-/kivezető", ref geometryLead, .2f, 5, 80, "%.1f m");
        if (geometryManoeuvre == 1) Number("Köztes egyenes", ref geometryGap, .2f, 0, 60, "%.1f m");
        Number("Sebesség", ref geometrySpeed, .02f, .1f, 5, "%.2f m/s");
        Number("Sávszélesség", ref geometryLane, .05f, 2, 12, "%.2f m");
    }

    private VehiclePathStudy UpdateGeometryStudy(RigProfile profile)
    {
        try {
            string profileKey = JsonSerializer.Serialize(profile);
            int units = (profile.Steering == SteeringGeometry.Articulated ? 2 : 1) + profile.Trailers.Length;
            if (geometryBodies[geometryPreviewKind] == null || geometryBodies[geometryPreviewKind].Length != units ||
                !geometryCustomBodies[geometryPreviewKind] && geometryBodyKeys[geometryPreviewKind] != profileKey) {
                geometryBodies[geometryPreviewKind] = StudyBody.Defaults(profile); geometryBodyKeys[geometryPreviewKind] = profileKey;
            }
            var bodies = geometryBodies[geometryPreviewKind];
            string key = JsonSerializer.Serialize(new { profile, bodies, geometryManoeuvre, geometryRadius, geometryLead, geometryGap, geometrySpeed, geometryRight });
            if (key != geometryStudyKey && (!ImGui.IsAnyItemActive() || geometryStudy == null)) {
                geometryStudy = new(profile, new((StudyManoeuvre)geometryManoeuvre, geometryRadius, geometryLead, geometryGap, geometryRight), bodies, geometrySpeed);
                geometryStudyKey = key; geometryExportMessage = null; geometryFitPending = true;
                if (geometryPlaying) geometryTime = 0;
            }
        } catch (ArgumentException ex) { ImGui.TextWrapped(ex.Message); return null; }
        return geometryStudy;
    }

    private void AdvanceTrialPlayback(VehiclePathStudy study)
    {
        if (!geometryPlaying || geometryScrubbing || study.Samples.Length < 2) return;
        double covered = Math.Max(1, study.Samples[^1].Distance);
        float rate = 1 << geometryPlayRate;
        geometryTime += (float)(ImGui.GetIO().DeltaTime * geometrySpeed * rate / covered);
        if (geometryTime < 1) return;
        if (geometryLoop && study.Failure == null) geometryTime = 0;
        else { geometryTime = 1; geometryPlaying = false; }
    }

    private StudySample CurrentStudySample(VehiclePathStudy study) => study.Samples[Math.Clamp((int)(geometryTime * (study.Samples.Length - 1)), 0, study.Samples.Length - 1)];

    private void DrawTrialTimeline(VehiclePathStudy study, float height)
    {
        var draw = ImGui.GetWindowDrawList();
        if (TrialIconButton("trial-restart", 0)) { geometryTime = 0; geometryPlaying = true; }
        ImGui.SameLine(0, 6);
        if (TrialIconButton("trial-play", geometryPlaying ? 2 : 1)) {
            if (!geometryPlaying && geometryTime >= 1) geometryTime = 0;
            geometryPlaying = !geometryPlaying;
        }
        ImGui.SameLine(0, 10); ImGui.SetNextItemWidth(70); ImGui.Combo("##play-rate", ref geometryPlayRate, "×1\0×2\0×4\0×8\0×16\0");
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("Lejátszási gyorsítás a próbasebességhez képest.");
        ImGui.SameLine(0, 10); ImGui.Checkbox("Ismétlés", ref geometryLoop);
        ImGui.SameLine(0, 10); ImGui.Checkbox("Követő nézet", ref geometryFollow);
        var current = CurrentStudySample(study);
        ImGui.SameLine(0, 14); ImGui.AlignTextToFramePadding();
        EditorStyle.Text(HudTheme.Parchment, $"{current.Distance:F1} / {study.Road.Length:F1} m");
        var at = ImGui.GetCursorScreenPos(); var size = new Vec2(ImGui.GetContentRegionAvail().X, Math.Max(60, height - ImGui.GetFrameHeightWithSpacing() - 6));
        ImGui.InvisibleButton("trial-scrub", size);
        double total = Math.Max(1e-6, study.Road.Length), reached = Math.Max(1e-6, study.Samples[^1].Distance);
        geometryScrubbing = ImGui.IsItemActive();
        if (geometryScrubbing) {
            float picked = Math.Clamp((ImGui.GetMousePos().X - at.X) / size.X, 0, 1);
            geometryTime = (float)Math.Clamp(picked * total / reached, 0, 1);
        }
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("Kattintás vagy húzás: a szerelvény a pálya adott pontjára ugrik.");
        draw.AddRectFilled(at, at + size, EditorStyle.Canvas, 4);
        float X(double distance) => at.X + (float)(distance / total) * size.X;
        foreach (var arc in study.Road.Arcs) draw.AddRectFilled(new Vec2(X(arc.Start), at.Y), new Vec2(X(arc.End), at.Y + size.Y), EditorStyle.U(1, 1, 1, .045f));
        double[] limits = TrialAngleLimits(study.Profile);
        double range = Math.Max(.1, limits.Max());
        float mid = at.Y + size.Y * .36f, span = size.Y * .32f;
        float Y(double angle) => mid - (float)(angle / range) * span;
        draw.AddLine(new Vec2(at.X, mid), new Vec2(at.X + size.X, mid), EditorStyle.GridMajor);
        draw.AddLine(new Vec2(at.X, Y(limits[0])), new Vec2(at.X + size.X, Y(limits[0])), EditorStyle.Fade(EditorStyle.Selection, .35f));
        draw.AddLine(new Vec2(at.X, Y(-limits[0])), new Vec2(at.X + size.X, Y(-limits[0])), EditorStyle.Fade(EditorStyle.Selection, .35f));
        int series = TrialAngleCount(study.Profile), stride = Math.Max(1, study.Samples.Length / Math.Max(60, (int)size.X));
        for (int s = 0; s < series; s++) {
            uint colour = EditorStyle.U(GeometryColours[s % GeometryColours.Length]);
            for (int i = stride; i < study.Samples.Length; i += stride) {
                var a = study.Samples[i - stride]; var b = study.Samples[i];
                draw.AddLine(new Vec2(X(a.Distance), Y(TrialAngle(study.Profile, a, s))), new Vec2(X(b.Distance), Y(TrialAngle(study.Profile, b, s))), colour, 1.4f);
            }
        }
        float widthBase = at.Y + size.Y - 3, widthScale = size.Y * .22f / Math.Max(geometryLane, (float)(study.LeftExtent + study.RightExtent));
        for (int i = stride; i < study.Samples.Length; i += stride) {
            double wide = study.SampleLeft[i] + study.SampleRight[i];
            draw.AddRectFilled(new Vec2(X(study.Samples[i - stride].Distance), widthBase - (float)wide * widthScale), new Vec2(X(study.Samples[i].Distance) + 1, widthBase),
                EditorStyle.U(wide > geometryLane ? HudTheme.AmberAccent : new Vec4(.3f, .55f, .4f, 1)));
        }
        draw.AddLine(new Vec2(at.X, widthBase - geometryLane * widthScale), new Vec2(at.X + size.X, widthBase - geometryLane * widthScale), EditorStyle.U(HudTheme.Muted));
        draw.AddText(at + new Vec2(6, 3), EditorStyle.TextMuted, "kormány- és csuklószögek");
        draw.AddText(new Vec2(at.X + 6, widthBase - 15), EditorStyle.TextMuted, "foglalt szélesség");
        if (study.Failure != null) draw.AddLine(new Vec2(X(reached), at.Y), new Vec2(X(reached), at.Y + size.Y), EditorStyle.ErrorColour, 2);
        float head = X(current.Distance);
        draw.AddLine(new Vec2(head, at.Y), new Vec2(head, at.Y + size.Y), EditorStyle.U(HudTheme.Parchment), 2);
    }

    private static bool TrialIconButton(string id, int icon)
    {
        float side = ImGui.GetFrameHeight();
        var at = ImGui.GetCursorScreenPos();
        bool clicked = ImGui.Button("##" + id, new Vec2(side * 1.4f, side));
        var c = at + new Vec2(side * .7f, side * .5f); uint colour = EditorStyle.U(HudTheme.Parchment); var draw = ImGui.GetWindowDrawList();
        if (icon == 1) draw.AddTriangleFilled(c + new Vec2(-5, -6), c + new Vec2(-5, 6), c + new Vec2(6, 0), colour);
        else if (icon == 2) { draw.AddRectFilled(c + new Vec2(-5, -6), c + new Vec2(-1, 6), colour); draw.AddRectFilled(c + new Vec2(2, -6), c + new Vec2(6, 6), colour); }
        else { draw.AddRectFilled(c + new Vec2(-6, -6), c + new Vec2(-3, 6), colour); draw.AddTriangleFilled(c + new Vec2(6, -6), c + new Vec2(6, 6), c + new Vec2(-3, 0), colour); }
        return clicked;
    }

    private static int TrialAngleCount(RigProfile profile) => 1 + profile.Trailers.Length;
    private static string TrialAngleName(RigProfile profile, int index) =>
        index == 0 ? (profile.Steering == SteeringGeometry.Axle ? "Kormányszög" : "Derékcsukló") : index == 1 && profile.Trailers.Length > 1 ? "Vonórúd" : "Pótkocsicsukló";
    private static double[] TrialAngleLimits(RigProfile profile) =>
        new[] { profile.EquivalentSteerLimit }.Concat(profile.Trailers.Select(t => t.MaxAngle)).ToArray();
    private static double TrialAngle(RigProfile profile, StudySample sample, int index)
    {
        if (index == 0) return sample.Steer;
        int unit = (profile.Steering == SteeringGeometry.Articulated ? 2 : 1) + index - 1;
        return VehicleKinematics.Wrap(sample.Units[unit - 1].Yaw - sample.Units[unit].Yaw);
    }

    private void DrawTrialReadout(VehiclePathStudy study)
    {
        var current = CurrentStudySample(study); var profile = study.Profile; int index = Array.IndexOf(study.Samples, current);
        EditorStyle.Section("Pillanatnyi állapot");
        var limits = TrialAngleLimits(profile);
        for (int i = 0; i < limits.Length; i++) {
            double angle = TrialAngle(profile, current, i);
            ImGui.TextUnformatted($"{TrialAngleName(profile, i)}: {angle * 180 / Math.PI:+0.0;-0.0;0.0}°");
            var at = ImGui.GetCursorScreenPos(); var size = new Vec2(ImGui.GetContentRegionAvail().X, 8); var draw = ImGui.GetWindowDrawList();
            float share = (float)Math.Clamp(Math.Abs(angle) / limits[i], 0, 1), mid = at.X + size.X * .5f, tip = mid + Math.Sign(angle) * share * size.X * .5f;
            draw.AddRectFilled(at, at + size, EditorStyle.Canvas, 3);
            draw.AddRectFilled(new Vec2(Math.Min(mid, tip), at.Y), new Vec2(Math.Max(mid, tip), at.Y + size.Y), EditorStyle.U(share > .9f ? HudTheme.AmberAccent : GeometryColours[i % GeometryColours.Length]), 3);
            ImGui.Dummy(size);
        }
        ImGui.Spacing();
        double used = study.SampleLeft[index] + study.SampleRight[index];
        EditorStyle.Text(used > geometryLane ? HudTheme.AmberAccent : HudTheme.Parchment, $"Foglalt szélesség most: {used:F2} m");
        double total = study.LeftExtent + study.RightExtent;
        ImGui.PushTextWrapPos();
        EditorStyle.Text(total > geometryLane ? HudTheme.AmberAccent : HudTheme.Muted, $"Legnagyobb: {study.LeftExtent:F2} m balra + {study.RightExtent:F2} m jobbra = {total:F2} m");
        ImGui.PopTextWrapPos();
        EditorStyle.Section("Tengelyek a sávban");
        foreach (var group in profile.Axles.Select((a, i) => (a, i)).GroupBy(g => VehiclePathStudy.AxleGroup(g.a.Id))) {
            double offset = group.Average(g => study.Road.SignedOffset(current.Axles[g.i]));
            EditorStyle.Text(GeometryGroupColour(profile, group.Key), $"{VehiclePathStudy.AxleName(group.Key)}: {offset:+0.00;-0.00;0.00} m");
            if (ImGui.IsItemHovered()) ImGui.SetTooltip("A tengelycsoport közepének távolsága az úttengelytől. Bal oldal pozitív.");
        }
        EditorStyle.Section("Megjelenítés");
        ImGui.Checkbox("Söpört terület", ref geometrySweep);
        ImGui.Checkbox("Sávszegélyek", ref geometryRoad);
        ImGui.Checkbox("Tengelypályák", ref geometryAxles);
        ImGui.Checkbox("Keréknyomok", ref geometryWheels);
        ImGui.Checkbox("Szellemképek", ref geometryGhosts);
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("A szerelvény korábbi helyzetei a megtett út mentén.");
        ImGui.Spacing();
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
        ImGui.Spacing();
        DrawStudyContours(profile);
        DrawStudyRadii(study);
    }

    private void DrawStudyContours(RigProfile profile)
    {
        if (!ImGui.CollapsingHeader("Karosszéria-kontúrok")) return;
        ImGui.TextWrapped("A söpört terület téglalapjai, a tengelycsoporthoz képest méterben. Rakománytúlnyúlás a kontúr kiterjesztésével vizsgálható. A játék modelljeit nem méretezik át.");
        var bodies = geometryBodies[geometryPreviewKind];
        if (EditorStyle.QuietButton("Kontúrok a tengelyadatokból", "Újra felveszi a tengelyekből és csuklókból származó kezdeti kontúrokat.")) {
            geometryBodies[geometryPreviewKind] = bodies = StudyBody.Defaults(profile); geometryCustomBodies[geometryPreviewKind] = false;
        }
        for (int i = 0; i < bodies.Length; i++) {
            EditorStyle.Text(HudTheme.Parchment, GeometryUnitName(profile, i));
            float rear = (float)bodies[i].Rear, front = (float)bodies[i].Front, width = (float)bodies[i].Width;
            ImGui.SetNextItemWidth(-1); bool changed = ImGui.DragFloat("##body-rear" + i, ref rear, .05f, -40, front - .1f, "hátul %.2f m", ImGuiSliderFlags.AlwaysClamp);
            ImGui.SetNextItemWidth(-1); changed |= ImGui.DragFloat("##body-front" + i, ref front, .05f, rear + .1f, 40, "elöl %.2f m", ImGuiSliderFlags.AlwaysClamp);
            ImGui.SetNextItemWidth(-1); changed |= ImGui.DragFloat("##body-width" + i, ref width, .02f, .2f, 10, "szélesség %.2f m", ImGuiSliderFlags.AlwaysClamp);
            if (changed) { bodies[i] = new(rear, front, width); geometryCustomBodies[geometryPreviewKind] = true; }
        }
    }

    private void DrawStudyRadii(VehiclePathStudy study)
    {
        if (!ImGui.CollapsingHeader("Tengely- és keréksugarak")) return;
        ImGui.TextWrapped("Mért: helyi simulókör az első ív kilépése előtt 0,5 m-rel. Elméleti: állandósult körmozgás. A valódi egyezéshez a háromkörös próba kell; a 90°-os ívben és az S-kanyarban az átmeneti eltérés természetes.");
        if (!ImGui.BeginTable("study-radii", 3, ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerV | ImGuiTableFlags.SizingStretchProp)) return;
        ImGui.TableSetupColumn("Tengely / kerék"); ImGui.TableSetupColumn("Mért"); ImGui.TableSetupColumn("Elméleti"); ImGui.TableHeadersRow();
        foreach (var row in study.Radii) {
            ImGui.TableNextRow(); ImGui.TableNextColumn(); EditorStyle.Text(GeometryGroupColour(study.Profile, study.Profile.Axles[row.Axle].Id), row.Name);
            ImGui.TableNextColumn(); ImGui.TextUnformatted(double.IsFinite(row.Observed) ? row.Observed.ToString("F3") : "—");
            ImGui.TableNextColumn(); ImGui.TextUnformatted(double.IsFinite(row.Steady) ? row.Steady.ToString("F3") : "nem létezik");
        }
        ImGui.EndTable();
    }
}
