using System;
using System.Collections.Generic;
using System.Drawing;
using OpenTK.Mathematics;

namespace ForesTycoon
{
    // Broadleaves use a closed crown surface; spruce has separate whorled branch masses.
    internal static class ForestCrownMesh
    {
        private static readonly Vector3 Light = Vector3.Normalize(new Vector3(0.45f, 0.65f, 1.05f));
        internal static void Append(List<Vertex> vertices, ForestSpecies species, Vector3 origin,
            float radius, float height, float yaw, int seed, Color color, ForestLod lod)
        {
            if (species == ForestSpecies.Spruce)
            {
                SpruceCrownMesh.Append(vertices, origin, radius, height, yaw, seed, color, lod);
                return;
            }
            int sides = lod == ForestLod.Near ? 12 : lod == ForestLod.Medium ? 8 : 6;
            int rings = lod == ForestLod.Near ? 10 : lod == ForestLod.Medium ? 6 : 4;
            float fullness = ForestTreeVariation.Range(seed, 101, -0.16f, 0.18f);
            float width = ForestTreeVariation.Range(seed, 102, 0.84f, 1.18f);
            float leanAngle = ForestTreeVariation.Range(seed, 103, 0, MathF.Tau);
            float leanAmount = ForestTreeVariation.Range(seed, 104, 0.02f, 0.20f);
            float phase = ForestTreeVariation.Range(seed, 105, 0, MathF.Tau);
            float verticalBias = ForestTreeVariation.Range(seed, 106, -0.22f, 0.22f);
            int lobeCount = 3 + (int)(ForestTreeVariation.Unit(seed, 107) * 3);
            float amplitude = (species == ForestSpecies.Oak ? 0.12f : species == ForestSpecies.Birch ? 0.075f : 0.055f)
                * ForestTreeVariation.Range(seed, 108, 0.65f, 1.35f);
            // Shared grid vertices used to be evaluated four times per quad, including
            // numerical normals and trigonometry. Evaluate each seam vertex once.
            Span<Vertex> grid = stackalloc Vertex[(rings + 1) * sides];
            for (int ring = 0; ring <= rings; ring++)
                for (int side = 0; side < sides; side++) grid[ring * sides + side] = Make(ring, side);
            for (int ring = 0; ring < rings; ring++)
                for (int side = 0; side < sides; side++)
                {
                    Vertex a = grid[ring * sides + side], b = grid[ring * sides + (side + 1) % sides];
                    Vertex c = grid[(ring + 1) * sides + (side + 1) % sides], d = grid[(ring + 1) * sides + side];
                    if (ring != 0) { vertices.Add(a); vertices.Add(b); vertices.Add(c); }
                    if (ring != rings - 1) { vertices.Add(a); vertices.Add(c); vertices.Add(d); }
                }

            Vertex Make(int ring, int side)
            {
                float t = ring / (float)rings;
                float angle = yaw + MathF.Tau * (side % sides) / sides;
                Vector3 local = Point(t, angle);
                Vector3 normal;
                if (ring == 0) normal = -Vector3.UnitZ;
                else if (ring == rings) normal = Vector3.UnitZ;
                else
                {
                    Vector3 along = Point(t, angle + 0.002f) - Point(t, angle - 0.002f);
                    Vector3 up = Point(t + 0.002f, angle) - Point(t - 0.002f, angle);
                    normal = Vector3.Normalize(Vector3.Cross(along, up));
                }
                float light = Math.Max(0, Vector3.Dot(normal, Light));
                // Broad matte tones with a small smooth transition, no hard polygon outlines.
                float tone = 0.69f + 0.18f * Smooth(0.05f, 0.40f, light) + 0.15f * Smooth(0.55f, 0.90f, light);
                float pigment = 0.98f + 0.025f * MathF.Sin(angle * 5 + t * 17 + seed % 31);
                // Alpha carries the species code for the surface shader's foliage pattern.
                uint packed = (uint)color.A << 24 | (uint)Channel(color.R * tone * pigment)
                    | (uint)Channel(color.G * tone * pigment) << 8 | (uint)Channel(color.B * tone * pigment) << 16;
                return new Vertex(origin + local, normal, packed);
            }

            Vector3 Point(float t, float angle)
            {
                t = Math.Clamp(t, 0, 1);
                float envelope = MathF.Sin(MathF.PI * t);
                float profile = MathF.Pow(envelope, (species == ForestSpecies.Oak ? 0.62f : 0.80f) + fullness)
                        * (species == ForestSpecies.Beech ? 0.78f + 0.30f * t
                            : species == ForestSpecies.Birch ? 1.12f - 0.32f * t : 1)
                        * (1 + verticalBias * (2 * t - 1));
                if (t == 0 || t == 1) profile = 0;
                // Coherent lobes describe leaf masses without allocating separate meshes.
                float lobes = 1 + amplitude * MathF.Sin(angle * lobeCount + t * 4 + phase)
                    + envelope * amplitude * 0.45f * MathF.Sin(angle * 2 - phase)
                        * MathF.Sin(t * MathF.PI * 3 + phase);
                float lean = radius * leanAmount * envelope;
                return new Vector3(MathF.Cos(angle) * radius * profile * lobes * width + lean * MathF.Cos(leanAngle),
                    MathF.Sin(angle) * radius * profile * lobes / width + lean * MathF.Sin(leanAngle), t * height);
            }
        }

        private static int Channel(float value) => Math.Clamp((int)value, 0, 255);
        private static float Smooth(float low, float high, float x)
        {
            float t = Math.Clamp((x - low) / (high - low), 0, 1);
            return t * t * (3 - 2 * t);
        }
    }
}
