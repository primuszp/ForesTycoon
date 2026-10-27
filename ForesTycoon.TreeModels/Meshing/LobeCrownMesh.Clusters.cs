using System;
using System.Collections.Generic;
using OpenTK.Mathematics;

namespace ForesTycoon.TreeModels
{
    internal static partial class LobeCrownMesh
    {
        // Farthest-point seeds + a fixed Lloyd iteration count: deterministic spatial
        // clusters, independent of mesh LOD and with no extra random stream.
        private static List<Lobe> Cluster(List<Vector3> leaves, int requested, Profile p, int seed, float aspect)
        {
            // Only joined crowns use a central spine. Open crowns keep their actual leaf clusters.
            float flatten = p.Flatten * aspect;
            bool pads = p.Gaps > 0;
            int count = Math.Min(requested, leaves.Count);
            // About three masses ring the spine per tier. Tall, narrow crowns need masses tall
            // enough for the tiers to meet; broad crowns keep the world-space flattening.
            int tiers = Math.Max(1, (count + 2) / 3);
            float tierHalf = (p.MaxZ - p.MinZ) / (2 * tiers) * 1.25f;
            // The spine always starts low in the crown, so masses held high (a hazel vase) widen the
            // upper crown rather than being re-centred.
            // Its top stops a little above the highest mass centre: it still pierces the top masses,
            // but the masses (not a bare pole) form the crown top.
            float spineBottom = Math.Min(p.MinZ, 0.2f) - tierHalf * 0.6f, spineTop = p.MaxZ + tierHalf * 0.4f;
            Vector3 core = new(0, 0, (spineBottom + spineTop) * 0.5f);
            float spineHalf = Math.Max(p.CoreRadius * flatten, (spineTop - spineBottom) * 0.5f);
            // Open foliage consists exclusively of actual leaf clusters. A synthetic upper
            // centre would create an unsupported floating tuft above the real scaffold.
            var lobes = new List<Lobe>();
            if (!pads || leaves.Count == 0) lobes.Add(new(core, new(p.CoreRadius, p.CoreRadius, spineHalf), 1, -0.08f));
            if (leaves.Count == 0) return lobes;
            // Cluster in world-isotropic space: a crown height spans several crown radii, so tall crowns
            // split into masses along their height rather than leaving gaps between few tall groups.
            var metric = new Vector3(1, 1, Math.Clamp(1 / aspect, 1, 4));
            var centres = new Vector3[count]; centres[0] = leaves[0];
            for (int k = 1; k < count; k++)
            {
                float best = -1;
                foreach (var leaf in leaves)
                {
                    float nearest = float.MaxValue;
                    for (int j = 0; j < k; j++) nearest = Math.Min(nearest, ((leaf - centres[j]) * metric).LengthSquared);
                    if (nearest > best) { best = nearest; centres[k] = leaf; }
                }
            }
            var assignment = new int[leaves.Count]; var totals = new int[count];
            for (int iteration = 0; iteration < 6; iteration++)
            {
                var sums = new Vector3[count]; Array.Clear(totals);
                for (int i = 0; i < leaves.Count; i++)
                {
                    float nearest = float.MaxValue; int winner = 0;
                    for (int k = 0; k < count; k++)
                    {
                        float distance = ((leaves[i] - centres[k]) * metric).LengthSquared;
                        if (distance < nearest) { nearest = distance; winner = k; }
                    }
                    assignment[i] = winner; sums[winner] += leaves[i]; totals[winner]++;
                }
                for (int k = 0; k < count; k++) if (totals[k] > 0) centres[k] = sums[k] / totals[k];
            }
            for (int k = 0; k < count; k++)
            {
                if (totals[k] == 0) continue;
                var centre = centres[k];
                centre.Z = Math.Clamp(centre.Z, p.MinZ, p.MaxZ);
                // Layered sprays: pull the centre part of the way to the nearest tier.
                if (p.Layers > 1)
                {
                    float step = (p.MaxZ - p.MinZ) / (p.Layers - 1);
                    float tier = p.MinZ + step * MathF.Round((centre.Z - p.MinZ) / step);
                    centre.Z += (tier - centre.Z) * 0.45f;
                }
                float spread = 0, xx = 0, yy = 0, xy = 0;
                for (int i = 0; i < leaves.Count; i++) if (assignment[i] == k)
                {
                    var delta = leaves[i] - centres[k];
                    spread += delta.LengthSquared;
                    xx += delta.X * delta.X; yy += delta.Y * delta.Y; xy += delta.X * delta.Y;
                }
                float radius = Math.Clamp(0.28f + MathF.Sqrt(spread / totals[k]), p.MinRadius, p.MaxRadius);
                if (pads) radius = Math.Clamp(.12f + 1.2f * MathF.Sqrt((xx + yy) / totals[k]), p.MinRadius, p.MaxRadius)
                    * (1 - .25f * p.Gaps);
                if (pads) radius *= 1 + (p.CoreRadius - .3f) * Math.Max(0, 1 - centre.Xy.Length / p.Reach);
                float rise = (centre.Z - p.MinZ) / Math.Max(1e-3f, p.MaxZ - p.MinZ) - 0.5f;
                radius *= 1 + p.Vase * rise * 0.6f;
                var footprint = Footprint(xx, yy, xy, p.Anisotropy);
                var size = new Vector3(radius * footprint.Stretch, radius / footprint.Stretch,
                    Math.Max(radius * flatten, pads ? .035f : tierHalf));
                // Radial extent of the oriented ellipse towards the trunk, not its major radius.
                var inward = centre.Xy.LengthSquared > 1e-10f ? centre.Xy.Normalized() : Vector2.UnitX;
                float alongMajor = Vector2.Dot(inward, footprint.Axis);
                float radialRadius = 1 / MathF.Sqrt(alongMajor * alongMajor / (size.X * size.X)
                    + (1 - alongMajor * alongMajor) / (size.Y * size.Y));
                float along = (centre.Z - core.Z) / spineHalf;
                float spine = p.CoreRadius * MathF.Sqrt(Math.Max(0, 1 - along * along));
                float reach = p.Reach * (1 + p.Vase * rise);
                float compact = Math.Min(reach, 0.68f * (spine + radialRadius));
                float limit = compact + (reach - compact) * p.BranchFollow;
                float axis = centre.Xy.Length;
                if (axis > limit) centre = new(centre.X * limit / axis, centre.Y * limit / axis, centre.Z);
                // Lower masses of a weeping crown hang further; upper masses of a flat-based one stay compact.
                float bottom = p.Bottom > 1 ? 1 + (p.Bottom - 1) * Math.Clamp((core.Z + 0.2f - centre.Z) * 2.5f, 0.2f, 1) : p.Bottom;
                float tint = 0.05f * (2 * ForestTreeVariation.Unit(seed, 9000 + k) - 1);
                // Keep the same single connected field, while allowing the supported lobes to reach
                // further out. This tests the oriented ellipsoid, including its flattened underside.
                for (int step = 0; !pads && step < 16 && !Touches(centre, size, bottom, footprint.Axis); step++)
                    centre = new(centre.X * 0.85f, centre.Y * 0.85f, core.Z + (centre.Z - core.Z) * 0.92f);
                lobes.Add(new(centre, size, bottom, tint, footprint.Axis));
            }
            return lobes;

            bool Touches(Vector3 centre, Vector3 size, float bottom, Vector2 major)
            {
                const float Inscribed = 0.75f;
                Vector3 axis = new(0, 0, Math.Clamp(centre.Z, core.Z - Inscribed * spineHalf * 0.9f, core.Z + Inscribed * spineHalf * 0.9f));
                for (int i = 0; i <= 8; i++)
                {
                    Vector3 q = Vector3.Lerp(centre, axis, i / 8f);
                    Vector3 delta = q - centre;
                    Vector3 own = new Vector3(delta.X * major.X + delta.Y * major.Y,
                        -delta.X * major.Y + delta.Y * major.X, delta.Z) / (size * Inscribed);
                    if (q.Z < centre.Z) own.Z /= bottom;
                    Vector3 spine = (q - core) / (new Vector3(p.CoreRadius, p.CoreRadius, spineHalf) * Inscribed);
                    if (own.LengthSquared <= 1 && spine.LengthSquared <= 1) return true;
                }
                return false;
            }
        }

        // Principal axes of the leaf cloud's horizontal covariance. Preserve footprint area so
        // elongated sprays change the shape, not foliage volume or the simulated crown dimensions.
        internal static (Vector2 Axis, float Stretch) Footprint(float xx, float yy, float xy, float strength)
        {
            float gap = MathF.Sqrt((xx - yy) * (xx - yy) + 4 * xy * xy);
            float angle = .5f * MathF.Atan2(2 * xy, xx - yy);
            float major = Math.Max(0, (xx + yy + gap) * .5f);
            float minor = Math.Max(major * .25f, (xx + yy - gap) * .5f);
            float ratio = major > 1e-10f ? Math.Clamp(major / minor, 1, 4) : 1;
            return (new(MathF.Cos(angle), MathF.Sin(angle)), MathF.Pow(ratio, .25f * strength));
        }

    }
}
