using System;
using System.Collections.Generic;
using System.Drawing;
using OpenTK.Mathematics;

namespace ForesTycoon
{
    /// <summary>Separated, drooping branch fans around a slender upright leader.</summary>
    internal static class SpruceCrownMesh
    {
        internal const int NearTierCount = 9;
        private static readonly Vector3 Light = Vector3.Normalize(new(0.4f, 0.6f, 1));
        internal readonly record struct Branch(Vector3 Root, Vector3 Tip, float Width, float Thickness, float Bend);

        internal static int BranchCount(int seed) => 4 + (int)(ForestTreeVariation.Unit(seed, 201) * 3);

        // Wood and foliage share the layout so exposed limbs carry their own needle masses.
        internal static Branch BranchAt(float radius, float height, float yaw, int seed, int tier, int branch, int tiers)
        {
            float level = tier / (float)tiers;
            float angle = yaw + tier * ForestTreeVariation.Range(seed, 202, 2.1f, 2.7f) + MathF.Tau * branch / BranchCount(seed)
                + (Random(seed, tier * 37 + branch) - 0.5f) * 0.30f;
            float reach = radius * MathF.Pow(1 - level, ForestTreeVariation.Range(seed, 203, 0.75f, 1.20f))
                * (0.82f + 0.22f * Random(seed, tier * 37 + branch + 11));
            float crownBase = ForestTreeVariation.Range(seed, 204, 0.08f, 0.17f);
            float z = height * (crownBase + level * (0.90f - crownBase))
                + height / tiers * (Random(seed, tier * 13 + 19) - 0.5f) * 0.16f;
            float drop = Math.Min(z * 0.7f, reach * (ForestTreeVariation.Range(seed, 205, 0.07f, 0.22f)
                + Random(seed, tier * 37 + branch + 23) * 0.07f));
            var root = new Vector3(0, 0, z);
            var tip = new Vector3(MathF.Cos(angle) * reach, MathF.Sin(angle) * reach, z - drop);
            return new(root, tip, reach * ForestTreeVariation.Range(seed, 206, 0.14f, 0.22f),
                Math.Min(height / tiers * 0.10f, reach * 0.07f), reach * 0.05f);
        }

        internal static void Append(List<Vertex> output, Vector3 origin, float radius, float height,
            float yaw, int seed, Color color, ForestLod lod)
        {
            int tiers = lod == ForestLod.Near ? NearTierCount : lod == ForestLod.Medium ? 7 : 5;
            int sides = lod == ForestLod.Near ? 6 : 4;
            const int rings = 3;
            int branches = BranchCount(seed);
            for (int tier = 0; tier < tiers; tier++)
                for (int branch = 0; branch < branches; branch++)
                {
                    Branch layout = BranchAt(radius, height, yaw, seed, tier, branch, tiers);
                    Vector3 radial = Vector3.Normalize(new Vector3(layout.Tip.X, layout.Tip.Y, 0));
                    Vector3 tangent = new(-radial.Y, radial.X, 0);
                    // Alternate fans overlap near the trunk, with open sky between their tips.
                    AppendFan(layout, tangent, radial, tier, branch);
                    if (lod == ForestLod.Far) continue;
                    float reach = new Vector2(layout.Tip.X, layout.Tip.Y).Length;
                    for (int shoot = 0; shoot < 2; shoot++)
                    {
                        float t = 0.38f + shoot * 0.29f;
                        Vector3 root = Vector3.Lerp(layout.Root, layout.Tip, t);
                        float side = shoot == 0 ? 1 : -1;
                        Vector3 tip = root + radial * (reach * 0.22f) + tangent * (reach * 0.27f * side)
                            - Vector3.UnitZ * Math.Min(reach * 0.12f, root.Z * 0.40f);
                        Vector3 shootDirection = Vector3.Normalize(new Vector3(tip.X - root.X, tip.Y - root.Y, 0));
                        AppendFan(new(root, tip, reach * 0.14f, reach * 0.045f, 0),
                            new(-shootDirection.Y, shootDirection.X, 0), shootDirection, tier, branch);
                    }
                }
            // Small terminal whorls continue the branching silhouette right up to the leader.
            for (int tier = 0; tier < 3; tier++)
                for (int branch = 0; branch < branches; branch++)
                {
                    float angle = yaw + tier * 2.399963f + branch * MathF.Tau / branches;
                    float reach = radius * (0.16f - tier * 0.06f);
                    Vector3 root = new(0, 0, height * (0.84f + tier * 0.06f));
                    Vector3 radial = new(MathF.Cos(angle), MathF.Sin(angle), 0);
                    AppendFan(new(root, root + radial * reach - Vector3.UnitZ * reach * 0.10f,
                        reach * 0.23f, reach * 0.08f, 0), new(-radial.Y, radial.X, 0), radial, tiers - 1, branch);
                }
            var leader = new Branch(new(0, 0, height * 0.97f), new(0, 0, height), radius * 0.02f, radius * 0.015f, 0);
            AppendFan(leader, Vector3.UnitX, Vector3.UnitY, tiers, 0);

            void AppendFan(Branch layout, Vector3 across, Vector3 thicknessAxis, int tier, int branch)
            {
                bool leader = tier == tiers;
                Vector3 direction = Vector3.Normalize(layout.Tip - layout.Root);
                // Cross section lies perpendicular to the branch, preserving outward normals.
                Vector3 up = leader ? thicknessAxis : Vector3.Normalize(Vector3.Cross(direction, across));
                Span<Vertex> grid = stackalloc Vertex[(rings + 1) * sides];
                for (int ring = 0; ring <= rings; ring++)
                    for (int side = 0; side < sides; side++)
                    {
                        float t = ring / (float)rings, angle = MathF.Tau * side / sides;
                        Vector3 point = Point(t, angle);
                        grid[ring * sides + side] = new(origin + point, Vector3.Zero, 0);
                    }
                for (int ring = 0; ring < rings; ring++)
                    for (int side = 0; side < sides; side++)
                    {
                        Vertex a = grid[ring * sides + side], b = grid[ring * sides + (side + 1) % sides];
                        Vertex c = grid[(ring + 1) * sides + (side + 1) % sides], d = grid[(ring + 1) * sides + side];
                        if (ring != 0) Emit(a, b, c);
                        if (ring != rings - 1) Emit(a, c, d);
                    }

                void Emit(Vertex a, Vertex b, Vertex c)
                {
                    // Flat fan facets keep narrow folds crisp and match the actual triangle winding.
                    Vector3 normal = Vector3.Normalize(Vector3.Cross(b.Position - a.Position, c.Position - a.Position));
                    float tone = 0.76f + 0.24f * Math.Max(0, Vector3.Dot(normal, Light))
                        + 0.06f * Random(seed, tier * 19 + branch);
                    uint packed = (uint)color.A << 24 | (uint)Channel(color.R * tone)
                        | (uint)Channel(color.G * tone) << 8 | (uint)Channel(color.B * tone) << 16;
                    output.Add(new(a.Position, normal, packed));
                    output.Add(new(b.Position, normal, packed));
                    output.Add(new(c.Position, normal, packed));
                }

                Vector3 Point(float t, float angle)
                {
                    t = Math.Clamp(t, 0, 1);
                    float profile = t == 0 || t == 1 ? 0 : MathF.Sin(MathF.PI * t) * (1 - t * 0.40f);
                    // Pointed side shoots break the rim into a needle-bearing branch fan.
                    float fringe = 1 + 0.18f * MathF.Cos(angle * 4 + tier + branch);
                    return Vector3.Lerp(layout.Root, layout.Tip, t)
                        + (leader ? Vector3.Zero : Vector3.UnitZ * (layout.Bend * MathF.Sin(MathF.PI * t)))
                        + across * (MathF.Cos(angle) * layout.Width * profile * fringe)
                        + up * (MathF.Sin(angle) * layout.Thickness * profile);
                }
            }
        }

        private static float Random(int seed, int index) => ForestTreeStore.Unit(
            ForestTreeStore.Random(unchecked((uint)seed ^ (uint)index * 0x9E3779B9u)));
        private static int Channel(float value) => Math.Clamp((int)value, 0, 255);
    }
}
