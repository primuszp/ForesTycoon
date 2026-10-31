using System;
using System.Linq;
using ImGuiNET;
using Vec2 = System.Numerics.Vector2;

namespace ForesTycoon.Editor;

internal sealed partial class CurrentRulesView
{
    private void DrawStudyChart(VehiclePathStudy study)
    {
        var at = ImGui.GetCursorScreenPos(); var size = new Vec2(Math.Max(280, ImGui.GetContentRegionAvail().X), 390);
        var draw = ImGui.GetWindowDrawList(); draw.AddRectFilled(at, at + size, EditorStyle.U(EditorStyle.Surface), 5);
        var bounds = study.Sweeps.SelectMany(p => p).Concat(Enumerable.Range(0, 201).Select(i => study.Road.Sample(study.Road.Length * i / 200).Point)).ToArray();
        double minX = bounds.Min(p => p.X) - 3, maxX = bounds.Max(p => p.X) + 3, minY = bounds.Min(p => p.Y) - 3, maxY = bounds.Max(p => p.Y) + 3;
        float scale = (float)Math.Min((size.X - 65) / (maxX - minX), (size.Y - 45) / (maxY - minY));
        var origin = at + new Vec2((size.X - (float)(maxX - minX) * scale) * .5f, size.Y - 25);
        Vec2 P(PlanPoint p) => origin + new Vec2((float)(p.X - minX) * scale, -(float)(p.Y - minY) * scale);
        draw.PushClipRect(at, at + size, true);
        uint grid = EditorStyle.U(new(.22f,.28f,.24f,.7f));
        double spacing = maxX - minX > 120 ? 20 : 5;
        for (double x = Math.Ceiling(minX / spacing) * spacing; x <= maxX; x += spacing) {
            draw.AddLine(P(new(x, minY)), P(new(x, maxY)), grid); draw.AddText(P(new(x, minY)) + new Vec2(0, 3), grid, $"{x:0}");
        }
        for (double y = Math.Ceiling(minY / spacing) * spacing; y <= maxY; y += spacing) {
            draw.AddLine(P(new(minX, y)), P(new(maxX, y)), grid); draw.AddText(P(new(minX, y)), grid, $"{y:0}");
        }
        if (geometrySweep) {
            uint fill = EditorStyle.U(new(.14f,.28f,.20f,1));
            foreach (var poly in study.Sweeps) for (int i = 1; i < poly.Length - 1; i++) draw.AddTriangleFilled(P(poly[0]), P(poly[i]), P(poly[i + 1]), fill);
        }
        int guideSteps = Math.Max(100, (int)(study.Road.Length / .25));
        for (int i = 1; i <= guideSteps; i++) {
            var a = study.Road.Sample(study.Road.Length * (i - 1) / guideSteps); var b = study.Road.Sample(study.Road.Length * i / guideSteps);
            if (i % 4 < 2) draw.AddLine(P(a.Point), P(b.Point), EditorStyle.U(HudTheme.Parchment), 1.4f);
            if (geometryRoad) foreach (int sign in new[] { -1, 1 })
                draw.AddLine(P(a.Point + PlanPoint.Left(a.Yaw) * (geometryLane * .5 * sign)), P(b.Point + PlanPoint.Left(b.Yaw) * (geometryLane * .5 * sign)), EditorStyle.U(HudTheme.Muted), 1);
        }
        int stride = Math.Max(1, study.Samples.Length / 1800);
        for (int axle = 0; axle < study.Profile.Axles.Length; axle++) {
            uint colour = EditorStyle.U(GeometryColours[axle % GeometryColours.Length]);
            for (int from = 0; from + 1 < study.Samples.Length; from += stride) {
                int i = Math.Min(from + stride, study.Samples.Length - 1);
                var a = study.Samples[from]; var b = study.Samples[i];
                if (geometryAxles) draw.AddLine(P(a.Axles[axle]), P(b.Axles[axle]), colour, 1.6f);
                if (geometryWheels) {
                    draw.AddLine(P(a.Wheels[axle * 2].Position), P(b.Wheels[axle * 2].Position), colour, .8f);
                    if (i / stride % 5 < 3) draw.AddLine(P(a.Wheels[axle * 2 + 1].Position), P(b.Wheels[axle * 2 + 1].Position), colour, .8f);
                }
            }
        }
        foreach (var arc in study.Road.Arcs) {
            var point = study.Road.Sample(arc.Start + (arc.End - arc.Start) * .125).Point;
            draw.AddLine(P(arc.Centre), P(point), EditorStyle.U(HudTheme.Parchment), 1);
            draw.AddCircleFilled(P(arc.Centre), 3, EditorStyle.U(HudTheme.Parchment));
            draw.AddText((P(arc.Centre) + P(point)) * .5f, EditorStyle.U(HudTheme.Parchment), $"R = {arc.Radius:F1} m");
        }
        DrawObservedRadius(study, P);
        if (study.Samples.Length > 0) {
            var current = study.Samples[Math.Clamp((int)(geometryTime * (study.Samples.Length - 1)), 0, study.Samples.Length - 1)];
            for (int j = 0; j < study.Bodies.Length; j++) {
                var poly = current.Footprints[j];
                bool outside = poly.Any(p => Math.Abs(study.Road.SignedOffset(p)) > geometryLane * .5);
                uint colour = EditorStyle.U(outside ? HudTheme.AmberAccent : HudTheme.Parchment);
                for (int k = 0; k < poly.Length; k++) draw.AddLine(P(poly[k]), P(poly[(k + 1) % poly.Length]), colour, 2);
            }
            foreach (var wheel in current.Wheels) {
                var half = PlanPoint.Forward(wheel.Yaw) * .45;
                draw.AddLine(P(wheel.Position - half), P(wheel.Position + half), EditorStyle.U(HudTheme.Parchment), 3);
            }
        }
        draw.AddText(at + new Vec2(10, 8), EditorStyle.U(HudTheme.Parchment), "Méretek: m · fehér szaggatott: vezetett első tengely úttengelye");
        draw.PopClipRect(); ImGui.Dummy(size);
        for (int i = 0; i < study.Profile.Axles.Length; i++) {
            if (i > 0 && i % 3 != 0) ImGui.SameLine(0, 18);
            EditorStyle.Text(GeometryColours[i % GeometryColours.Length], VehiclePathStudy.AxleName(study.Profile.Axles[i].Id));
        }
    }

    private void DrawObservedRadius(VehiclePathStudy study, Func<PlanPoint, Vec2> screen)
    {
        int i = Array.FindLastIndex(study.Samples, s => s.Distance < study.Road.FirstArcEnd - .5);
        if (i < 1 || i + 1 >= study.Samples.Length) return;
        var a = study.Samples[i - 1].Axles[geometryRadiusAxle]; var b = study.Samples[i].Axles[geometryRadiusAxle]; var c = study.Samples[i + 1].Axles[geometryRadiusAxle];
        var ab = b - a; var ac = c - a; double cross = 2 * (ab.X * ac.Y - ab.Y * ac.X);
        if (Math.Abs(cross) < 1e-10) return;
        var centre = a + new PlanPoint((ac.Y * ab.Dot(ab) - ab.Y * ac.Dot(ac)) / cross, (ab.X * ac.Dot(ac) - ac.X * ab.Dot(ab)) / cross);
        double radius = (b - centre).Length;
        if (!double.IsFinite(radius) || radius > 500) return;
        var draw = ImGui.GetWindowDrawList(); uint colour = EditorStyle.U(GeometryColours[geometryRadiusAxle % GeometryColours.Length]);
        draw.AddLine(screen(centre), screen(b), colour, 1.5f); draw.AddCircleFilled(screen(centre), 3, colour);
        draw.AddText((screen(centre) + screen(b)) * .5f + new Vec2(0, 15), colour, $"R tengely = {radius:F2} m");
    }
}
