using System;
using OpenTK.Mathematics;

namespace ForesTycoon
{
    /// <summary>
    /// Answers the simulation's questions about the shape the generator would give a tree, without
    /// building any mesh: wood volume, form factor, live-crown geometry and foliage. The numbers
    /// come from the same skeleton and the same pipe-model radii that are meshed.
    /// </summary>
    internal static class TreeShapeModel
    {
        internal static TreeShapeMetrics Measure(in TreeShapeSpec spec) => Measure(new TreeForm(spec));

        internal static TreeShapeMetrics Measure(TreeForm f)
        {
            var sk = f.Skeleton; var spec = f.Spec;
            float toMetres = 1 / Terrain.TreeMetresToWorld;
            double trunk = 0, wood = 0;
            int limbs = 0;
            float crownBase = f.Height;
            for (int i = 0; i < sk.StemCount; i++)
            {
                var stem = sk.Stems[i];
                double volume = 0;
                Vector3 previous = f.ToWorld(sk.Points[stem.Start]);
                for (int v = 1; v < stem.Count; v++)
                {
                    Vector3 p = f.ToWorld(sk.Points[stem.Start + v]);
                    float ra = f.RadiusOf(sk.Flow[stem.Start + v - 1]), rb = f.RadiusOf(sk.Flow[stem.Start + v]);
                    volume += Math.PI / 3 * (p - previous).Length * (ra * ra + ra * rb + rb * rb);
                    previous = p;
                }
                wood += volume;
                if (stem.Level == 0) trunk += volume;
                if (stem.Level == 1) { limbs++; if (!f.Dead[i]) crownBase = Math.Min(crownBase, f.ToWorld(sk.Points[stem.Start]).Z); }
            }
            double cubic = Math.Pow(toMetres, 3);
            // Live leaves outline the crown; a polar sampling keeps asymmetry honest.
            const int Sectors = 16;
            var reach = new float[Sectors];
            Vector2 centre = Vector2.Zero; int live = 0;
            float top = 0;
            for (int i = 0; i < sk.Leaves.Length; i++)
            {
                if (f.Dead[sk.LeafStem[i]]) continue;
                Vector3 p = f.ToWorld(sk.Leaves[i]);
                float r = p.Xy.Length;
                if (r > 1e-6f)
                {
                    float angle = MathF.Atan2(p.Y, p.X); if (angle < 0) angle += MathF.Tau;
                    int s = (int)(angle / MathF.Tau * Sectors) % Sectors;
                    reach[s] = Math.Max(reach[s], r);
                }
                centre += p.Xy; live++; top = Math.Max(top, p.Z);
            }
            float scaleBack = 1 / 0.88f; // the crown mesh is scaled so its widest point equals the crown radius
            float area = 0, maxReach = 0;
            for (int s = 0; s < Sectors; s++)
            {
                float r = reach[s] * scaleBack * (0.72f + 0.28f * f.Foliage);
                area += 0.5f * r * r * MathF.Tau / Sectors; maxReach = Math.Max(maxReach, r);
            }
            if (live > 0) centre /= live;
            float crownLength = Math.Max(0, (Math.Max(top, crownBase) - crownBase) * toMetres);
            float shape = spec.Species == ForestSpecies.Spruce ? 0.38f : 0.55f;
            float breast = f.BreastRadius * 2 * toMetres;
            double cylinder = Math.PI * breast * breast / 4 * spec.Size.Height;
            float foliage = f.Foliage * (live == 0 ? 0 : 1);
            return new((float)(trunk * cubic), (float)(wood * cubic), cylinder > 0 ? (float)(trunk * cubic / cylinder) : 0,
                crownBase * toMetres, maxReach * toMetres, area * toMetres * toMetres, area * toMetres * toMetres * crownLength * shape,
                centre * toMetres, foliage, f.Dieback, limbs);
        }
    }
}
