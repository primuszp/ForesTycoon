using OpenTK.Graphics.OpenGL;
using OpenTK.Mathematics;

namespace ForesTycoon.Tests;

[Collection("Primitive geometry capture")]
public class ForestStemGeometryTests
{
    [Theory]
    [InlineData(ForestSpecies.Spruce)]
    [InlineData(ForestSpecies.Birch)]
    [InlineData(ForestSpecies.Oak)]
    [InlineData(ForestSpecies.Beech)]
    internal void CutRimMatchesBoleTaperAndSharesEdgesWithTheBark(ForestSpecies species)
    {
        foreach (float scale in new[] { 0.03f, 0.2f, 1f, 1.8f })
        {
            var stand = new ForestStand(species, 20, 0.5f, 1);
            var tree = new Terrain.TreeInstance(stand, 42, 2, 3, 4, scale, 0.7f, 0, 1, 1, 2);
            var model = Terrain.TreeModel.For(species);
            var profile = new ForestTrunkProfile(model.TrunkHeight * scale, model.TrunkRadius * scale);
            var vertices = DynamicPrimitiveBatch.BuildGeometry(PrimitiveTopology.Quads, () => Terrain.DrawStump(tree, 0));
            var barkRim = new HashSet<Vector3>();
            var cutRim = new HashSet<Vector3>();
            for (int i = 0; i < vertices.Length; i += 3)
            {
                Vector3 face = Vector3.Cross(vertices[i + 1].Position - vertices[i].Position,
                    vertices[i + 2].Position - vertices[i].Position);
                Assert.True(float.IsFinite(face.LengthSquared));
                Assert.True(face.LengthSquared > 0);
            }
            foreach (var vertex in vertices)
            {
                Vector3 p = vertex.Position;
                if (p.Z < tree.BaseZ) continue;
                float radius = new Vector2(p.X - tree.X, p.Y - tree.Y).Length;
                if (radius < 0.000001f) continue; // Fan center.
                Assert.Equal(profile.RadiusAt(p.Z - tree.BaseZ), radius, 5);
                if ((vertex.Color >> 24) == Terrain.CutWoodCode) cutRim.Add(p);
                else barkRim.Add(p);
            }
            Assert.Equal(6, cutRim.Count);
            Assert.True(barkRim.SetEquals(cutRim));
            Assert.True(profile.RadiusAt(profile.CutHeight) < profile.BottomRadius);
        }
    }
}
