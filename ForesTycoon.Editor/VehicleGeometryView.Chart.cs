using System;
using System.Linq;
using ImGuiNET;
using Vec2 = System.Numerics.Vector2;
using Vec4 = System.Numerics.Vector4;

namespace ForesTycoon.Editor;

internal sealed partial class VehicleGeometryView
{
    private double geometryViewX, geometryViewY;
    private float geometryViewScale = 8;
    private bool geometryFitPending = true;
    private Vec2[] geometryLine = new Vec2[1024];

    private void DrawStudyChart(VehiclePathStudy study, Vec2 size)
    {
        var at = ImGui.GetCursorScreenPos();
        ImGui.InvisibleButton("study-canvas", size, ImGuiButtonFlags.MouseButtonLeft | ImGuiButtonFlags.MouseButtonMiddle);
        bool hovered = ImGui.IsItemHovered();
        var current = CurrentStudySample(study);
        if (geometryFitPending) { FitStudyView(study, size); geometryFitPending = false; }
        if (hovered) {
            var mouse = ImGui.GetMousePos() - (at + size * .5f); float wheel = ImGui.GetIO().MouseWheel;
            if (wheel != 0) {
                float next = Math.Clamp(geometryViewScale * MathF.Pow(1.15f, wheel), 1.5f, 120);
                geometryViewX += mouse.X / geometryViewScale - mouse.X / next; geometryViewY -= mouse.Y / geometryViewScale - mouse.Y / next;
                geometryViewScale = next;
            }
            if (ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left)) { geometryFitPending = true; geometryFollow = false; }
            if (ImGui.IsKeyPressed(ImGuiKey.Space, false)) geometryPlaying = !geometryPlaying;
        }
        if (ImGui.IsItemActive()) {
            var delta = ImGui.GetIO().MouseDelta; geometryViewX -= delta.X / geometryViewScale; geometryViewY += delta.Y / geometryViewScale;
            if (delta != Vec2.Zero) geometryFollow = false;
        }
        if (geometryFollow) { var centre = current.Units[current.Units.Length / 2].Axle; geometryViewX = centre.X; geometryViewY = centre.Y; }
        float ppm = geometryViewScale; var middle = at + size * .5f;
        Vec2 P(PlanPoint p) => middle + new Vec2((float)(p.X - geometryViewX) * ppm, -(float)(p.Y - geometryViewY) * ppm);
        var draw = ImGui.GetWindowDrawList();
        draw.AddRectFilled(at, at + size, EditorStyle.Canvas, 5);
        draw.PushClipRect(at, at + size, true);
        DrawStudyGrid(draw, at, size, P, ppm);
        if (geometrySweep) DrawSweptBand(draw, study, current.Distance, P);
        DrawStudyRoad(draw, study, P);
        if (geometryAxles || geometryWheels) DrawStudyTraces(draw, study, Array.IndexOf(study.Samples, current), P);
        if (geometryGhosts) DrawStudyGhosts(draw, study, current, P, ppm);
        DrawStudyRig(draw, study, current, P, ppm, 1);
        if (study.Failure != null) {
            var end = P(study.Samples[^1].Axles[Array.FindIndex(study.Profile.Axles, a => a.Steered) is var steered and >= 0 ? steered : 0]);
            draw.AddLine(end + new Vec2(-9, -9), end + new Vec2(9, 9), EditorStyle.ErrorColour, 3); draw.AddLine(end + new Vec2(-9, 9), end + new Vec2(9, -9), EditorStyle.ErrorColour, 3);
        }
        DrawStudyScaleBar(draw, at, size, ppm);
        DrawStudyLegend(draw, study, at);
        draw.PopClipRect();
        var after = ImGui.GetCursorScreenPos();
        DrawStudyFailure(study, at, size);
        ImGui.SetCursorScreenPos(at + new Vec2(size.X - ImGui.CalcTextSize("Teljes pálya").X - 20, 8));
        if (ImGui.SmallButton("Teljes pálya")) { geometryFitPending = true; geometryFollow = false; }
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("Görgő: nagyítás · húzás: mozgatás · dupla kattintás: teljes pálya · szóköz: indítás/megállítás");
        ImGui.SetCursorScreenPos(after);
    }

    private void DrawStudyFailure(VehiclePathStudy study, Vec2 at, Vec2 size)
    {
        if (study.Failure == null) return;
        double smallest = VehiclePathStudy.SmallestRoadRadius(study.Profile);
        var box = at + new Vec2(10, size.Y - 92);
        ImGui.GetWindowDrawList().AddRectFilled(box, box + new Vec2(Math.Min(size.X - 20, 620), 52), EditorStyle.U(.2f, .09f, .07f, .92f), 4);
        ImGui.SetCursorScreenPos(box + new Vec2(10, 6)); EditorStyle.Text(HudTheme.Bad, study.Failure);
        ImGui.SetCursorScreenPos(box + new Vec2(10, 26));
        if (geometryRadius < smallest && ImGui.SmallButton($"Ívsugár a legkisebb járható értékre: {Math.Ceiling(smallest * 10) / 10:F1} m"))
            geometryRadius = (float)(Math.Ceiling(smallest * 10) / 10);
        else if (geometryRadius >= smallest) EditorStyle.Text(HudTheme.Muted, $"Elért út: {study.Samples[^1].Distance:F1} m. Nagyobb ívsugár vagy kisebb próbasebesség kell.");
    }

    private void FitStudyView(VehiclePathStudy study, Vec2 size)
    {
        var points = study.Envelope.SelectMany(e => {
            var g = study.Road.At(e.Station);
            return new[] { g.Point + PlanPoint.Left(g.Yaw) * Math.Max(e.Left, geometryLane * .5), g.Point - PlanPoint.Left(g.Yaw) * Math.Max(e.Right, geometryLane * .5) };
        }).ToArray();
        if (points.Length == 0) return;
        double minX = points.Min(p => p.X), maxX = points.Max(p => p.X), minY = points.Min(p => p.Y), maxY = points.Max(p => p.Y);
        geometryViewX = (minX + maxX) * .5; geometryViewY = (minY + maxY) * .5;
        geometryViewScale = (float)Math.Clamp(Math.Min((size.X - 60) / Math.Max(1, maxX - minX), (size.Y - 90) / Math.Max(1, maxY - minY)), 1.5, 120);
    }

    private static void DrawStudyGrid(ImDrawListPtr draw, Vec2 at, Vec2 size, Func<PlanPoint, Vec2> P, float ppm)
    {
        double spacing = new[] { 1d, 2, 5, 10, 20, 50, 100 }.First(s => s * ppm >= 42 || s == 100);
        var origin = P(new(0, 0));
        double firstX = Math.Floor((at.X - origin.X) / ppm / spacing) * spacing, firstY = Math.Floor((origin.Y - at.Y - size.Y) / ppm / spacing) * spacing;
        for (double x = firstX; origin.X + x * ppm < at.X + size.X; x += spacing)
            draw.AddLine(new Vec2(origin.X + (float)x * ppm, at.Y), new Vec2(origin.X + (float)x * ppm, at.Y + size.Y), EditorStyle.GridMinor);
        for (double y = firstY; origin.Y - y * ppm > at.Y; y += spacing)
            draw.AddLine(new Vec2(at.X, origin.Y - (float)y * ppm), new Vec2(at.X + size.X, origin.Y - (float)y * ppm), EditorStyle.GridMinor);
    }

    private static void DrawStudyScaleBar(ImDrawListPtr draw, Vec2 at, Vec2 size, float ppm)
    {
        double metres = new[] { 1d, 2, 5, 10, 20, 50, 100 }.First(s => s * ppm >= 70 || s == 100);
        var start = at + new Vec2(14, size.Y - 18); float length = (float)metres * ppm; uint colour = EditorStyle.U(HudTheme.Parchment);
        draw.AddLine(start, start + new Vec2(length, 0), colour, 2);
        draw.AddLine(start + new Vec2(0, -4), start + new Vec2(0, 4), colour, 2); draw.AddLine(start + new Vec2(length, -4), start + new Vec2(length, 4), colour, 2);
        draw.AddText(start + new Vec2(length + 6, -8), colour, $"{metres:0} m");
    }

    private void DrawStudyLegend(ImDrawListPtr draw, VehiclePathStudy study, Vec2 at)
    {
        var cursor = at + new Vec2(10, 8);
        foreach (string group in study.Profile.Axles.Select(a => VehiclePathStudy.AxleGroup(a.Id)).Distinct()) {
            string name = VehiclePathStudy.AxleName(group);
            draw.AddCircleFilled(cursor + new Vec2(5, 8), 4, EditorStyle.U(GeometryGroupColour(study.Profile, group)));
            draw.AddText(cursor + new Vec2(14, 0), EditorStyle.U(HudTheme.Parchment), name);
            cursor.X += ImGui.CalcTextSize(name).X + 30;
        }
    }

    private void DrawSweptBand(ImDrawListPtr draw, VehiclePathStudy study, double reached, Func<PlanPoint, Vec2> P)
    {
        var band = study.Envelope;
        uint ahead = EditorStyle.U(.13f, .24f, .17f, 1), behind = EditorStyle.U(.20f, .40f, .28f, 1), overrun = EditorStyle.U(.85f, .55f, .15f, .4f);
        double half = geometryLane * .5;
        // Neighbouring quads share their edges: anti-aliased fringes would draw every seam as a stripe.
        var flags = draw.Flags; draw.Flags &= ~ImDrawListFlags.AntiAliasedFill;
        for (int i = 1; i < band.Length; i++) {
            if (band[i].Station - band[i - 1].Station > VehiclePathStudy.EnvelopeStep * 3) continue;
            var a = study.Road.At(band[i - 1].Station); var b = study.Road.At(band[i].Station);
            PlanPoint Side(StudyGuide g, double offset) => g.Point + PlanPoint.Left(g.Yaw) * offset;
            draw.AddQuadFilled(P(Side(a, band[i - 1].Left)), P(Side(b, band[i].Left)), P(Side(b, -band[i].Right)), P(Side(a, -band[i - 1].Right)), band[i].Station <= reached ? behind : ahead);
            if (band[i].Left > half) draw.AddQuadFilled(P(Side(a, Math.Max(half, band[i - 1].Left))), P(Side(b, band[i].Left)), P(Side(b, half)), P(Side(a, half)), overrun);
            if (band[i].Right > half) draw.AddQuadFilled(P(Side(a, -Math.Max(half, band[i - 1].Right))), P(Side(b, -band[i].Right)), P(Side(b, -half)), P(Side(a, -half)), overrun);
        }
        draw.Flags = flags;
    }

    private void DrawStudyRoad(ImDrawListPtr draw, VehiclePathStudy study, Func<PlanPoint, Vec2> P)
    {
        double from = Math.Min(0, study.Envelope.FirstOrDefault().Station) - 2, to = Math.Max(study.Road.Length, study.Envelope.LastOrDefault().Station) + 2;
        int steps = Math.Clamp((int)((to - from) / .5), 60, 1000);
        uint axis = EditorStyle.U(HudTheme.Parchment), edge = EditorStyle.U(HudTheme.Muted);
        for (int i = 1; i <= steps; i++) {
            var a = study.Road.At(from + (to - from) * (i - 1) / steps); var b = study.Road.At(from + (to - from) * i / steps);
            if (i % 4 < 2) draw.AddLine(P(a.Point), P(b.Point), axis, 1.2f);
            if (geometryRoad) foreach (int sign in new[] { -1, 1 })
                draw.AddLine(P(a.Point + PlanPoint.Left(a.Yaw) * (geometryLane * .5 * sign)), P(b.Point + PlanPoint.Left(b.Yaw) * (geometryLane * .5 * sign)), edge, 1);
        }
        foreach (var arc in study.Road.Arcs) {
            var point = study.Road.Sample(arc.Start + (arc.End - arc.Start) * .125).Point;
            draw.AddLine(P(arc.Centre), P(point), EditorStyle.Fade(axis, .35f), 1);
            draw.AddCircleFilled(P(arc.Centre), 3, axis);
            draw.AddText((P(arc.Centre) + P(point)) * .5f, axis, $"R = {arc.Radius:F1} m");
        }
    }

    private void StudyPolyline(ImDrawListPtr draw, int count, uint colour, float thickness)
    {
        if (count >= 2) draw.AddPolyline(ref geometryLine[0], count, colour, ImDrawFlags.None, thickness);
    }

    private void DrawStudyTraces(ImDrawListPtr draw, VehiclePathStudy study, int reached, Func<PlanPoint, Vec2> P)
    {
        int stride = Math.Max(1, study.Samples.Length / 500);
        void Trace(Func<StudySample, PlanPoint> pick, uint colour, float thickness) {
            int count = 0;
            for (int i = 0; i <= reached; i += stride) geometryLine[count++] = P(pick(study.Samples[i]));
            geometryLine[count++] = P(pick(study.Samples[reached]));
            StudyPolyline(draw, count, colour, thickness);
            count = 0; geometryLine[count++] = P(pick(study.Samples[reached]));
            for (int i = reached + stride; i < study.Samples.Length; i += stride) geometryLine[count++] = P(pick(study.Samples[i]));
            StudyPolyline(draw, count, EditorStyle.Fade(colour, .28f), thickness * .7f);
        }
        for (int axle = 0; axle < study.Profile.Axles.Length; axle++) {
            int index = axle; uint colour = EditorStyle.U(GeometryGroupColour(study.Profile, study.Profile.Axles[axle].Id));
            if (geometryAxles) Trace(s => s.Axles[index], colour, 2);
            if (geometryWheels) { Trace(s => s.Wheels[index * 2].Position, EditorStyle.Fade(colour, .75f), 1); Trace(s => s.Wheels[index * 2 + 1].Position, EditorStyle.Fade(colour, .75f), 1); }
        }
    }

    private void DrawStudyGhosts(ImDrawListPtr draw, VehiclePathStudy study, StudySample current, Func<PlanPoint, Vec2> P, float ppm)
    {
        var bodies = StudyBody.Defaults(study.Profile);
        double spacing = Math.Max(bodies.Max(b => b.Front - b.Rear) * 1.1, study.Road.Length / 12);
        for (double at = spacing; at < current.Distance - spacing * .4; at += spacing)
            DrawStudyRig(draw, study, study.Samples[Math.Min(study.Samples.Length - 1, (int)(at / study.SamplingStep))], P, ppm, .28f);
    }

    private void DrawStudyRig(ImDrawListPtr draw, VehiclePathStudy study, StudySample sample, Func<PlanPoint, Vec2> P, float ppm, float alpha)
    {
        var profile = study.Profile; bool ghost = alpha < 1;
        for (int j = 0; j < study.Bodies.Length; j++) {
            var poly = sample.Footprints[j];
            bool outside = !ghost && poly.Any(p => Math.Abs(study.Road.SignedOffset(p)) > geometryLane * .5);
            var tone = outside ? HudTheme.AmberAccent : HudTheme.Parchment;
            if (!ghost) draw.AddQuadFilled(P(poly[0]), P(poly[1]), P(poly[2]), P(poly[3]), EditorStyle.U(tone.X * .45f, tone.Y * .45f, tone.Z * .45f, .55f));
            draw.AddQuad(P(poly[0]), P(poly[1]), P(poly[2]), P(poly[3]), EditorStyle.U(EditorStyle.Fade(tone, alpha)), ghost ? 1 : 2);
        }
        int active = profile.Steering == SteeringGeometry.Articulated ? 2 : 1;
        if (active == 2) draw.AddCircleFilled(P(sample.Units[0].Axle - PlanPoint.Forward(sample.Units[0].Yaw) * profile.FrontLength), ghost ? 2 : 4, EditorStyle.U(EditorStyle.Fade(HudTheme.AmberAccent, alpha)));
        for (int i = 0; i < profile.Trailers.Length; i++) {
            var hitch = sample.Units[active + i - 1].Axle + PlanPoint.Forward(sample.Units[active + i - 1].Yaw) * profile.Trailers[i].HitchX;
            draw.AddLine(P(hitch), P(sample.Units[active + i].Axle), EditorStyle.U(EditorStyle.Fade(HudTheme.Parchment, alpha)), ghost ? 1 : 2);
            draw.AddCircleFilled(P(hitch), ghost ? 2 : 4, EditorStyle.U(EditorStyle.Fade(HudTheme.AmberAccent, alpha)));
        }
        if (ghost) return;
        for (int w = 0; w < sample.Wheels.Length; w++) {
            var axle = profile.Axles[w / 2]; var wheel = sample.Wheels[w];
            var tyre = VehiclePathStudy.Rectangle(wheel.Position, wheel.Yaw, -axle.Radius, axle.Radius, axle.Radius * .6);
            draw.AddQuadFilled(P(tyre[0]), P(tyre[1]), P(tyre[2]), P(tyre[3]), EditorStyle.U(GeometryGroupColour(profile, axle.Id)));
        }
    }
}
