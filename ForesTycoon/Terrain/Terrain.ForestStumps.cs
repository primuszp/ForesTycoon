using System;
using System.Drawing;
using OpenTK.Mathematics;

namespace ForesTycoon
{
    partial class Terrain
    {
        /// <summary>Closed cut bole; bark and end grain share the exact same tilted rim.</summary>
        internal static void DrawStump(in TreeInstance tree, float decay)
        {
            decay = Math.Clamp(decay, 0, 1);
            TreeModel model = TreeModel.For(tree.Stand.Species);
            var profile = new ForestTrunkProfile(model.TrunkHeight * tree.Scale, model.TrunkRadius * tree.Scale);
            float height = profile.CutHeight;
            float sink = height * 0.45f * decay;
            Color bark = Weather(Tinted(model.TrunkColor, tree.Tint * 0.5f), tree.Stand.Health);
            bark = Color.FromArgb(Mix(bark.R, 64, decay * 0.6f), Mix(bark.G, 70, decay * 0.6f), Mix(bark.B, 42, decay * 0.6f));
            Color cut = Color.FromArgb(Mix(222, 112, decay), Mix(184, 104, decay), Mix(128, 78, decay));
            if (decay > 0.45f)
                cut = Color.FromArgb(Mix(cut.R, 84, decay - 0.45f), Mix(cut.G, 112, decay - 0.45f), Mix(cut.B, 52, decay - 0.45f));

            int sides = tree.Sides >= 8 ? 6 : 4;
            Span<Vector3> bottom = stackalloc Vector3[sides];
            Span<Vector3> rim = stackalloc Vector3[sides];
            Vector3 center = new Vector3(tree.X, tree.Y, tree.BaseZ + height - sink);
            float tiltX = (TreeRandom((uint)tree.Seed, 23u) - 0.5f) * 0.12f;
            float tiltY = (TreeRandom((uint)tree.Seed, 29u) - 0.5f) * 0.12f;
            for (int side = 0; side < sides; side++)
            {
                // DrawLimb's vertical cross-section basis starts along negative Y.
                float angle = tree.Yaw - MathF.PI / 2 + MathF.Tau * side / sides;
                float x = MathF.Cos(angle), y = MathF.Sin(angle);
                float cutRadius = profile.RadiusAt(height);
                float cutZ = height + (x * tiltX + y * tiltY) * cutRadius;
                float radius = profile.RadiusAt(cutZ);
                rim[side] = new Vector3(tree.X + x * radius, tree.Y + y * radius, tree.BaseZ + cutZ - sink);
                bottom[side] = new Vector3(tree.X + x * profile.BottomRadius,
                    tree.Y + y * profile.BottomRadius, tree.BaseZ - ForestTrunkProfile.RootDepth - sink);
            }
            for (int side = 0; side < sides; side++)
            {
                int next = (side + 1) % sides;
                Vector3 normal = Vector3.Normalize(Vector3.Cross(bottom[next] - bottom[side], rim[next] - bottom[side]));
                DynamicPrimitiveBatch.Color4(Color.FromArgb(SurfaceSpeciesCode(tree.Stand.Species), Shade(bark, Diffuse(normal))));
                DynamicPrimitiveBatch.Vertex3(bottom[side]);
                DynamicPrimitiveBatch.Vertex3(bottom[next]);
                DynamicPrimitiveBatch.Vertex3(rim[next]);
                DynamicPrimitiveBatch.Vertex3(rim[side]);
            }
            DynamicPrimitiveBatch.Color4(Color.FromArgb(CutWoodCode, cut));
            for (int side = 0; side < sides; side += 2)
            {
                DynamicPrimitiveBatch.Vertex3(center);
                DynamicPrimitiveBatch.Vertex3(rim[side]);
                DynamicPrimitiveBatch.Vertex3(rim[side + 1]);
                DynamicPrimitiveBatch.Vertex3(rim[(side + 2) % sides]);
            }
        }
    }
}
