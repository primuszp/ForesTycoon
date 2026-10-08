using System;
using System.Collections.Generic;
using OpenTK.Mathematics;

namespace ForesTycoon.TreeModels
{
    /// <summary>
    /// Turns the skeleton of a <see cref="TreeForm"/> into smooth-shaded low-poly tubes: trunk with
    /// root flare and buttress roots, limbs, and in the leafless state the whole branch system.
    /// Every child tube starts on the axis of its parent with a radius not larger than the parent's
    /// there, so it is buried in the parent's surface; the topology of the skeleton survives meshing.
    /// </summary>
    internal static class TreeWoodMesh
    {
        /// <summary>Triangle budget of the limbs and twigs of one tree; the main stem is always drawn.</summary>
        internal static int Budget(ForestLod lod, bool leafless, bool lobed = false) => lod switch
        {
            // Lobed crowns have sky gaps between their masses, where the limbs carrying them show.
            ForestLod.Near => leafless ? 1400 : lobed ? 200 : 150,
            ForestLod.Medium => leafless ? 330 : lobed ? 80 : 50,
            _ => leafless ? 90 : 0
        };

        /// <param name="leafed">A crown is drawn: only the trunk, the exposed limb roots and dead limbs are needed.</param>
        internal static void Build(TreeForm form, ForestLod lod, bool leafed, List<Vertex> trunk, List<Vertex> branches)
        {
            var spec = form.Spec;
            bool lobed = leafed && !form.Shrub && LobeCrownMesh.Applies(spec.Species, spec.Phase);
            int budget = Budget(lod, !leafed, lobed);
            if (budget == 0) return;
            var sk = form.Skeleton;
            int n = sk.StemCount;
            float refPixels = DendroCrownMesh.ReferencePixels(lod);
            float minRadius = 0.45f / refPixels;
            float exposure = form.Shrub ? 0.6f : spec.Species switch
            {
                // Fraction of a first-order limb shown below/inside the crown surface.
                ForestSpecies.Oak => 0.50f, ForestSpecies.Beech => 0.45f, ForestSpecies.Birch => 0.35f, _ => 0.25f
            };
            // In a lobed crown the limbs run on into the foliage masses they carry, stopping short of the surface.
            if (lobed) exposure = Math.Max(exposure, 0.65f);
            if (leafed && form.Foliage < 1) exposure = Math.Min(1, exposure * 1.7f);
            int limbBudget = lod == ForestLod.Near ? (spec.Species == ForestSpecies.Oak || form.Shrub ? 8 : 6) + (lobed ? 2 : 0) : 3;

            // Stems with a significant child keep that vertex so thick forks stay exact.
            var keep = new bool[sk.Points.Length];
            for (int i = 1; i < n; i++)
            {
                var s = sk.Stems[i];
                if (s.Parent < 0) continue;
                int pv = sk.Stems[s.Parent].Start + s.ParentVertex;
                if (sk.Flow[s.Start] >= 0.12f * sk.Flow[pv]) keep[pv] = true;
            }

            // What is drawn, and how far along each stem.
            var fraction = new float[n];
            var full = new bool[n];
            if (leafed)
            {
                for (int i = n - 1; i >= 0; i--)
                {
                    if (form.Dead[i] && sk.Stems[i].Level > 0) full[i] = true;
                    if (full[i] && sk.Stems[i].Parent >= 0) full[sk.Stems[i].Parent] = true;
                }
                var order1 = new List<int>();
                for (int i = 0; i < n; i++) if (sk.Stems[i].Level == 1 && !full[i]) order1.Add(i);
                order1.Sort((a, b) => sk.Flow[sk.Stems[b].Start].CompareTo(sk.Flow[sk.Stems[a].Start]));
                for (int i = 0; i < n; i++)
                {
                    int level = sk.Stems[i].Level;
                    if (level == 0 || full[i]) fraction[i] = 1;
                }
                int shown = 0;
                foreach (int i in order1)
                {
                    // Skip limbs thinner than ~0.4 px at the LOD reference zoom: invisible, not free. The
                    // thickest few always show, so even a dense crown has visible forks beneath it.
                    if (limbBudget-- <= 0) break;
                    if (form.RadiusOf(sk.Flow[sk.Stems[i].Start]) * refPixels < 0.4f && shown >= 3) continue;
                    fraction[i] = exposure; shown++;
                }
            }
            else
            {
                int maxLevel = lod == ForestLod.Far ? 1 : 2;
                for (int i = 0; i < n; i++) if (sk.Stems[i].Level <= maxLevel) fraction[i] = 1;
            }

            // Thickest first: a parent always precedes its children, so any prefix is connected.
            var order = new List<int>(n);
            for (int i = 0; i < n; i++) if (fraction[i] > 0) order.Add(i);
            order.Sort((a, b) =>
            {
                if (a == 0 || b == 0) return a == 0 ? (b == 0 ? 0 : -1) : 1; // the main stem always comes first
                int c = sk.Flow[sk.Stems[b].Start].CompareTo(sk.Flow[sk.Stems[a].Start]);
                return c != 0 ? c : a.CompareTo(b);
            });

            float crownLimit = leafed ? form.Height * (1 - form.CrownFraction * 0.45f) : float.MaxValue;
            float limbLimit = leafed && !lobed ? form.Height * (1 - form.CrownFraction * 0.15f) : float.MaxValue;
            int used = 0, forks = 0;
            var points = new List<Vector3>(); var radii = new List<float>(); var pins = new List<bool>();
            foreach (int i in order)
            {
                var stem = sk.Stems[i];
                bool root = stem.Parent < 0;
                float limit = full[i] ? float.MaxValue : stem.Level == 0 ? crownLimit : limbLimit;
                if (!Collect(form, i, fraction[i], limit, minRadius, keep, points, radii, pins)) continue;
                var kept = Simplify(points, radii, pins, (lod == ForestLod.Near ? 0.30f : lod == ForestLod.Medium ? 0.5f : 0.8f) * (stem.Level == 0 ? 3f : 1f));
                float maxRadius = 0;
                foreach (float r in radii) maxRadius = Math.Max(maxRadius, r);
                int sides = Sides(root && i == 0, stem.Level, maxRadius, lod, form.Shrub);
                int cost = (kept.Count - 1) * sides * 2 + sides;
                if (stem.Level == 0 && i != 0 && !form.Shrub && forks++ >= 4) continue; // the first few forks of the trunk
                if (i != 0 && !(stem.Level == 0 && !form.Shrub))
                {
                    if (used + cost > budget) { if (stem.Level == 0) continue; break; } // thickest first: the rest is thinner
                    used += cost;
                }
                if (Environment.GetEnvironmentVariable("TREE_DEBUG") != null) Console.WriteLine($"stem {i} L{stem.Level} kept {kept.Count} sides {sides} cost {cost} r {maxRadius}");
                uint color = form.Dead[i] && !root ? form.DeadColor : form.WoodColor;
                // Taper tips to a point on thin stems; trunks keep their open top inside the crown.
                Tube(points, radii, kept, sides, color, stem.Level == 0 ? trunk : branches, !(root && leafed));
            }
            if (lod == ForestLod.Near && !form.Shrub && spec.Phase >= TreeLifePhase.Mature) Roots(form, trunk);
        }

        private static int Sides(bool root, int level, float radius, ForestLod lod, bool shrub)
        {
            int max = shrub ? 4 : root ? (lod == ForestLod.Near ? 7 : lod == ForestLod.Medium ? 5 : 3)
                : level == 0 ? (lod == ForestLod.Near ? 5 : 3) : level <= 1 ? (lod == ForestLod.Near ? 5 : 4) : 3;
            // Spend sides on the exposed bole, where a triangular silhouette remains
            // visible even with smooth lighting. Thin branches keep their cheap tubes.
            int min = root && !shrub ? (lod == ForestLod.Near ? 5 : lod == ForestLod.Medium ? 4 : 3) : 3;
            return DendroCrownMesh.Sides(radius, lod, min, max, 0.5f);
        }

        /// <summary>Gathers world points and radii of one stem, cut at an arc fraction and at a height.</summary>
        private static bool Collect(TreeForm f, int index, float fraction, float limit, float minRadius,
            bool[] keep, List<Vector3> points, List<float> radii, List<bool> pins)
        {
            var sk = f.Skeleton; var stem = sk.Stems[index];
            points.Clear(); radii.Clear(); pins.Clear();
            bool root = stem.Parent < 0 && stem.Level == 0;
            float total = 0; Vector3 previous = default;
            if (fraction < 1)
                for (int v = 0; v < stem.Count; v++)
                {
                    Vector3 p = f.ToWorld(sk.Points[stem.Start + v]);
                    if (v > 0) total += (p - previous).Length;
                    previous = p;
                }
            float allowed = total * fraction, walked = 0;
            for (int v = 0; v < stem.Count; v++)
            {
                Vector3 p = f.ToWorld(sk.Points[stem.Start + v]);
                // Drooping limbs rest on the ground rather than diving into it; the same rule applies to
                // parent and child, so junctions stay exact.
                if (stem.Level > 0) p.Z = Math.Max(p.Z, 0.015f * f.Height);
                float r = f.RadiusOf(sk.Flow[stem.Start + v]);
                if (root)
                {
                    r *= f.Flare(p.Z);
                    // The bole is a slender cone below the crown, not a perfect cylinder.
                    if (p.Z > f.BreastHeight && p.Z < f.CrownBase)
                        r = Math.Max(r, f.BreastRadius * (1 - 0.18f * (p.Z - f.BreastHeight) / Math.Max(1e-4f, f.CrownBase - f.BreastHeight)));
                }
                r = Math.Max(r, minRadius);
                if (points.Count > 0)
                {
                    Vector3 a = points[^1]; float ra = radii[^1];
                    float seg = (p - a).Length;
                    float t = 1; bool cut = false;
                    if (fraction < 1 && walked + seg > allowed) { t = seg > 1e-9f ? (allowed - walked) / seg : 0; cut = true; }
                    if (p.Z > limit && p.Z > a.Z)
                    {
                        float tz = Math.Clamp((limit - a.Z) / (p.Z - a.Z), 0, 1);
                        if (tz < t) { t = tz; cut = true; }
                    }
                    if (cut)
                    {
                        if (t > 0.02f) { points.Add(Vector3.Lerp(a, p, t)); radii.Add(ra + (r - ra) * t); pins.Add(false); }
                        break;
                    }
                    walked += seg;
                }
                else if (p.Z > limit) return false;
                points.Add(p); radii.Add(r); pins.Add(keep[stem.Start + v]);
            }
            if (root && points.Count > 0)
            {
                // Sink the foot below ground so slopes never show a gap under the trunk.
                Vector3 down = points.Count > 1 ? Vector3.Normalize(points[0] - points[1]) : -Vector3.UnitZ;
                float sink = Math.Min(1.2f * f.BreastRadius, 0.1f * f.Height);
                points.Insert(0, points[0] + down * sink); radii.Insert(0, radii[0]); pins.Insert(0, false);
            }
            return points.Count >= 2;
        }

        /// <summary>Douglas-Peucker on the axis with a tolerance relative to the local radius.</summary>
        private static List<int> Simplify(List<Vector3> p, List<float> r, List<bool> pins, float tolerance)
        {
            int n = p.Count;
            var flag = new bool[n]; flag[0] = flag[n - 1] = true;
            for (int i = 0; i < n; i++) if (pins[i]) flag[i] = true;
            var stack = new Stack<(int, int)>(); stack.Push((0, n - 1));
            while (stack.Count > 0)
            {
                var (a, b) = stack.Pop();
                int worst = -1; float worstRatio = 1;
                Vector3 ab = p[b] - p[a]; float length = ab.LengthSquared;
                for (int k = a + 1; k < b; k++)
                {
                    float t = length > 1e-14f ? Math.Clamp(Vector3.Dot(p[k] - p[a], ab) / length, 0, 1) : 0;
                    float d = (p[a] + ab * t - p[k]).Length / Math.Max(1e-6f, tolerance * r[k]);
                    if (d > worstRatio) { worstRatio = d; worst = k; }
                }
                if (worst < 0) continue;
                flag[worst] = true; stack.Push((a, worst)); stack.Push((worst, b));
            }
            var kept = new List<int>();
            for (int i = 0; i < n; i++) if (flag[i]) kept.Add(i);
            return kept;
        }

        /// <summary>One tube through the kept points with parallel-transported rings and smooth normals.</summary>
        private static void Tube(List<Vector3> p, List<float> r, List<int> kept, int sides, uint color,
            List<Vertex> target, bool closeTip)
        {
            // Drop coincident points so every segment has a direction.
            var idx = new List<int>(kept.Count);
            foreach (int k in kept)
                if (idx.Count == 0 || (p[k] - p[idx[^1]]).LengthSquared > 1e-12f) idx.Add(k);
            if (idx.Count < 2) return;
            int m = idx.Count;
            var dir = new Vector3[m];
            for (int i = 0; i < m; i++)
            {
                Vector3 d = p[idx[Math.Min(m - 1, i + 1)]] - p[idx[Math.Max(0, i - 1)]];
                dir[i] = Vector3.Normalize(d);
            }
            var u = new Vector3[m]; var v = new Vector3[m];
            u[0] = Vector3.Cross(dir[0], Math.Abs(dir[0].Z) > 0.9f ? Vector3.UnitX : Vector3.UnitZ).Normalized();
            v[0] = Vector3.Cross(dir[0], u[0]);
            for (int i = 1; i < m; i++)
            {
                Vector3 t = u[i - 1] - dir[i] * Vector3.Dot(u[i - 1], dir[i]);
                u[i] = t.LengthSquared > 1e-10f ? t.Normalized() : Vector3.Cross(dir[i], Vector3.UnitX).Normalized();
                v[i] = Vector3.Cross(dir[i], u[i]);
            }
            var cos = new float[sides + 1]; var sin = new float[sides + 1];
            for (int s = 0; s <= sides; s++) { cos[s] = MathF.Cos(MathF.Tau * s / sides); sin[s] = MathF.Sin(MathF.Tau * s / sides); }
            Vector3 Radial(int ring, int s) => u[ring] * cos[s] + v[ring] * sin[s];
            float tipRadius = r[idx[m - 1]];
            Vertex Vtx(int ring, int s, float radius) { var d = Radial(ring, s); return new(p[idx[ring]] + d * radius, d, color); }
            for (int i = 0; i < m - 1; i++)
            {
                float ra = r[idx[i]], rb = r[idx[i + 1]];
                // Closed tips narrow over the final segment so the end is a point, not a hole.
                if (closeTip && i == m - 2) rb *= 0.45f;
                for (int s = 0; s < sides; s++)
                {
                    var a0 = Vtx(i, s, ra); var a1 = Vtx(i, s + 1, ra);
                    var b0 = Vtx(i + 1, s, rb); var b1 = Vtx(i + 1, s + 1, rb);
                    Add(target, a0, a1, b1); Add(target, a0, b1, b0);
                }
            }
            if (closeTip)
            {
                int top = m - 1; Vector3 apex = p[idx[top]] + dir[top] * tipRadius * 0.9f;
                var apexNormal = dir[top];
                for (int s = 0; s < sides; s++)
                    Add(target, Vtx(top, s, tipRadius * 0.45f), Vtx(top, s + 1, tipRadius * 0.45f), new Vertex(apex, apexNormal, color));
            }
        }

        private static void Add(List<Vertex> target, Vertex a, Vertex b, Vertex c)
        {
            if (Vector3.Cross(b.Position - a.Position, c.Position - a.Position).LengthSquared < 1e-20f) return;
            target.Add(a); target.Add(b); target.Add(c);
        }

        /// <summary>Surface roots: tapered arms that leave the lower bole and dive into the soil.</summary>
        private static void Roots(TreeForm f, List<Vertex> target)
        {
            var spec = f.Spec;
            int count = spec.Species switch { ForestSpecies.Beech => 5, ForestSpecies.Oak => 4, ForestSpecies.Spruce => 4, _ => 3 };
            float baseRadius = f.BreastRadius * (1 + f.FlareStrength);
            var radii = new List<float>(3); var points = new List<Vector3>(3);
            var kept = new List<int> { 0, 1, 2 };
            for (int k = 0; k < count; k++)
            {
                float angle = MathF.Tau * (k + ForestTreeVariation.Range(spec.Seed, 900 + k, -0.3f, 0.3f)) / count + spec.Yaw;
                float length = ForestTreeVariation.Range(spec.Seed, 920 + k, 0.8f, 1.5f);
                Vector2 d = new(MathF.Cos(angle), MathF.Sin(angle));
                points.Clear(); radii.Clear();
                points.Add(new(d.X * 0.5f * baseRadius, d.Y * 0.5f * baseRadius, 0.35f * baseRadius)); radii.Add(0.5f * baseRadius);
                points.Add(new(d.X * 1.25f * baseRadius, d.Y * 1.25f * baseRadius, 0.04f * baseRadius)); radii.Add(0.34f * baseRadius);
                points.Add(new(d.X * (1.25f + length) * baseRadius, d.Y * (1.25f + length) * baseRadius, -0.25f * baseRadius)); radii.Add(0.12f * baseRadius);
                Tube(points, radii, kept, 3, f.WoodColor, target, true);
            }
        }
    }
}
