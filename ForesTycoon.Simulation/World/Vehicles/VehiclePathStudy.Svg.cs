using System;
using System.Globalization;
using System.Linq;
using System.Security;
using System.Text;

namespace ForesTycoon;

internal sealed partial class VehiclePathStudy
{
    internal string ToSvg(double laneWidth)
    {
        if (!double.IsFinite(laneWidth) || laneWidth <= 0 || laneWidth > 20) throw new ArgumentException("Hibás sávszélesség.");
        string N(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);
        string E(string value) => SecurityElement.Escape(value) ?? "";
        string[] colours = ["#2678b2", "#b87512", "#4c8527", "#9558b0", "#cc5146", "#1d8b80", "#997322", "#596cc4"];
        const double width = 1200, chartHeight = 700;
        var bounds = Sweeps.SelectMany(p => p).Concat(Enumerable.Range(0, 301).Select(i => Road.Sample(Road.Length * i / 300).Point)).ToArray();
        double minX = bounds.Min(p => p.X) - 3, maxX = bounds.Max(p => p.X) + 3, minY = bounds.Min(p => p.Y) - 3, maxY = bounds.Max(p => p.Y) + 3;
        double scale = Math.Min((width - 100) / (maxX - minX), (chartHeight - 100) / (maxY - minY));
        double originX = (width - (maxX - minX) * scale) * .5, originY = chartHeight - 35;
        string P(PlanPoint p) => N(originX + (p.X - minX) * scale) + "," + N(originY - (p.Y - minY) * scale);
        var svg = new StringBuilder();
        svg.Append($"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"1200\" height=\"{N(chartHeight + 140 + Radii.Length * 22 + Bodies.Length * 22)}\" viewBox=\"0 0 1200 {N(chartHeight + 140 + Radii.Length * 22 + Bodies.Length * 22)}\">");
        svg.Append("<rect width=\"100%\" height=\"100%\" fill=\"white\"/><g font-family=\"sans-serif\" font-size=\"14\" fill=\"#203024\">");
        void Text(double x, double y, string content, string colour = "#203024") => svg.Append($"<text x=\"{N(x)}\" y=\"{N(y)}\" fill=\"{colour}\">{E(content)}</text>");
        string title = Road.Manoeuvre == StudyManoeuvre.QuarterTurn ? "90°-os ív" : Road.Manoeuvre == StudyManoeuvre.SCurve ? "S-kanyar" : "Állandósult körpálya – 3 kör";
        Text(25, 24, $"Tankönyvi pályavizsgálat · {title} · R={N(Road.Radius)} m · egy sáv: {N(laneWidth)} m");
        Text(25, 47, $"Első tengely vezérgörbéje · tengelypályák és bal/jobb keréknyomok · mintalépés {N(SamplingStep)} m");
        svg.Append("<g id=\"swept-area\" fill=\"#d9eadb\" stroke=\"none\">");
        foreach (var poly in Sweeps) svg.Append($"<polygon points=\"{string.Join(' ', poly.Select(P))}\"/>");
        svg.Append("</g>");
        void Line(string id, System.Collections.Generic.IEnumerable<PlanPoint> points, string colour, double thickness, string dash = "") =>
            svg.Append($"<polyline id=\"{id}\" points=\"{string.Join(' ', points.Select(P))}\" fill=\"none\" stroke=\"{colour}\" stroke-width=\"{N(thickness)}\"{(dash.Length == 0 ? "" : " stroke-dasharray=\"" + dash + "\"")}/>");
        var guide = Enumerable.Range(0, Math.Max(200, (int)(Road.Length / .1)) + 1).Select(i => Road.Sample(Road.Length * i / Math.Max(200, (int)(Road.Length / .1)))).ToArray();
        Line("guide", guide.Select(g => g.Point), "#263b31", 1.4, "8 5");
        foreach (int side in new[] { -1, 1 }) Line("lane-" + side, guide.Select(g => g.Point + PlanPoint.Left(g.Yaw) * (laneWidth * .5 * side)), "#8b9590", 1);
        for (int a = 0; a < Profile.Axles.Length; a++) {
            string colour = colours[a % colours.Length];
            Line("axle-" + a, Samples.Select(s => s.Axles[a]), colour, 1.7);
            Line("wheel-left-" + a, Samples.Select(s => s.Wheels[a * 2].Position), colour, .9);
            Line("wheel-right-" + a, Samples.Select(s => s.Wheels[a * 2 + 1].Position), colour, .9, "4 3");
        }
        foreach (var arc in Road.Arcs) {
            var point = Road.Sample(arc.Start + (arc.End - arc.Start) * .125).Point;
            Line("radius-" + N(arc.Start), [arc.Centre, point], "#59635c", 1);
            svg.Append($"<text x=\"{P((arc.Centre + point) * .5).Split(',')[0]}\" y=\"{P((arc.Centre + point) * .5).Split(',')[1]}\">R={N(arc.Radius)} m</text>");
        }
        foreach (double fraction in new[] { 0d, .35, .65, 1 }) {
            var frame = Samples[Math.Min(Samples.Length - 1, (int)((Samples.Length - 1) * fraction))];
            for (int j = 0; j < Bodies.Length; j++) {
                var poly = frame.Footprints[j]; Line("body-" + N(fraction) + "-" + j, poly.Append(poly[0]), "#485b50", 1);
            }
        }
        double y = chartHeight + 5;
        Text(25, y, $"Söpört szélesség: balra {N(LeftExtent)} m + jobbra {N(RightExtent)} m = {N(LeftExtent + RightExtent)} m (referenciatengelyhez)"); y += 23;
        Text(25, y, "Söpört terület: téglalap-kontúrok, kerekek, kapcsolórudak; a lépések közötti konvex burkolók uniója."); y += 23;
        if (Failure != null) { Text(25, y, Failure, "#ad382f"); y += 23; }
        Text(25, y, "Tengely / kerék"); Text(600, y, "Helyi R az első ív végén (m)"); Text(910, y, "Elméleti állandósult R (m)"); y += 22;
        foreach (var radius in Radii) {
            Text(25, y, radius.Name, colours[radius.Axle % colours.Length]);
            Text(600, y, double.IsFinite(radius.Observed) ? N(radius.Observed) : "—");
            Text(910, y, double.IsFinite(radius.Steady) ? N(radius.Steady) : "nem létezik"); y += 22;
        }
        for (int i = 0; i < Bodies.Length; i++) {
            var b = Bodies[i]; Text(25, y, $"{i + 1}. egység vizsgálati kontúr: X={N(b.Rear)} … {N(b.Front)} m; szélesség={N(b.Width)} m"); y += 22;
        }
        svg.Append("</g></svg>"); return svg.ToString();
    }
}
