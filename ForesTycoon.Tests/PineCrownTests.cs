using OpenTK.Mathematics;

namespace ForesTycoon.Tests;

[Collection("SpeciesBook")]
public class PineCrownTests
{
    [Theory]
    [InlineData(TreeLifePhase.Mature)]
    [InlineData(TreeLifePhase.Old)]
    internal void ForestPineRetainsADeepBranchingCrownInsteadOfATopCap(TreeLifePhase phase)
    {
        ForestSpeciesBook.WithContext<object?>(null, () => {
            var spec = new TreeShapeSpec(ForestSpecies.Pine, 13, phase, new(.4f, 26, 5), 1,
                new TreeSite(1), LeafState.Full, 0);
            var crown = DendroTreeGenerator.Generate(spec, ForestLod.Near).Crown;
            float height = spec.Size.Height * TreeScale.MetresToWorld;
            float bottom = crown.Min(v => v.Position.Z);
            Assert.InRange(bottom / height, .3f, .55f);
            Assert.True((crown.Max(v => v.Position.Z) - bottom) / height > .45f);
            return null;
        });
    }

    [Theory]
    [InlineData(TreeLifePhase.Mature, 13)]
    [InlineData(TreeLifePhase.Old, 42)]
    [InlineData(TreeLifePhase.Senescent, -1)]
    internal void OpenPineKeepsEveryClosedPadAndItsExposedBoleAcrossLods(TreeLifePhase phase, int seed)
    {
        ForestSpeciesBook.WithContext<object?>(null, () => {
            var spec = new TreeShapeSpec(ForestSpecies.Pine, seed, phase, new(.4f, 20, 4), 1,
                new TreeSite(1), LeafState.Full, 0);
            int? pads = null;
            var triangles = new List<int>();
            foreach (var lod in new[] { ForestLod.Near, ForestLod.Medium, ForestLod.Far })
            {
                var mesh = DendroTreeGenerator.Generate(spec, lod);
                var parts = Components(mesh.Crown);
                Assert.InRange(parts.Count, 24, 40);
                pads ??= parts.Count;
                Assert.Equal(pads.Value, parts.Count);
                foreach (var part in parts) AssertClosed(part);
                Assert.NotEmpty(mesh.Trunk);
                // Detailed views retain the exposed scaffold; distant views retain the bole.
                float height = spec.Size.Height * TreeScale.MetresToWorld;
                if (lod == ForestLod.Near)
                    Assert.InRange(mesh.Trunk.Concat(mesh.Branches).Max(v => v.Position.Z) / height, .9f, 1.05f);
                Assert.Equal(height, mesh.Crown.Max(v => v.Position.Z), 4);
                triangles.Add(mesh.Crown.Length / 3);
            }
            Assert.Equal(3068, triangles[0]);
            Assert.True(triangles[0] > triangles[1] && triangles[1] > triangles[2]);
            return null;
        });
    }

    [Fact]
    public void AdultPineKeepsShedBranchStubsBelowTheLiveCrown()
    {
        ForestSpeciesBook.WithContext<object?>(null, () => {
            var spec = new TreeShapeSpec(ForestSpecies.Pine, 13, TreeLifePhase.Old, new(.4f, 26, 5), 1,
                new TreeSite(1), LeafState.Full, 0);
            var mesh = DendroTreeGenerator.Generate(spec, ForestLod.Near);
            float height = spec.Size.Height * TreeScale.MetresToWorld;
            Assert.Contains(mesh.Branches, v => v.Position.Z > .1f * height && v.Position.Z < .3f * height);
            Assert.Equal(height, mesh.Crown.Max(v => v.Position.Z), 4);
            return null;
        });
    }

    [Fact]
    public void GapControlChangesTheTopologyAndZeroRestoresOneClosedShell()
    {
        ForestSpeciesBook.WithContext<object?>(null, () => {
            var spec = new TreeShapeSpec(ForestSpecies.Pine, 13, TreeLifePhase.Mature, new(.4f, 20, 4), 1,
                new TreeSite(1), LeafState.Full, 0);
            Assert.True(Components(DendroTreeGenerator.Generate(spec, ForestLod.Near).Crown).Count > 1);
            var scenario = GameWorld.DefaultScenario();
            scenario.Species.Single(s => s.Id == "Pine").Shape = new() { All = { ["CrownGaps"] = 0 } };
            ForestSpeciesBook.Apply(GameWorld.SpeciesEdits(scenario));
            var joined = DendroTreeGenerator.Generate(spec, ForestLod.Near).Crown;
            Assert.Single(Components(joined));
            AssertClosed(joined);
            Assert.Equal(3068, joined.Length / 3);
            return null;
        });
    }

    [Fact]
    public void AdultScaffoldDefaultsRemainEditableAndYoungPineStaysConical()
    {
        ForestSpeciesBook.WithContext<object?>(null, () => {
            Assert.Equal(2, TreeArchitecture.EffectiveParameters(ForestSpecies.Pine, TreeLifePhase.Mature, 2)["Shape"]);
            Assert.Equal(0, TreeArchitecture.EffectiveParameters(ForestSpecies.Pine, TreeLifePhase.Young, 2)["Shape"]);
            var scenario = GameWorld.DefaultScenario();
            scenario.Species.Single(s => s.Id == "Pine").Shape = new() {
                All = { ["Shape"] = 3, ["1CurveV"] = 100 },
                Phases = { ["Old"] = new() { ["Shape"] = 2, ["1CurveV"] = 120 } }
            };
            ForestSpeciesBook.Apply(GameWorld.SpeciesEdits(scenario));
            var mature = TreeArchitecture.EffectiveParameters(ForestSpecies.Pine, TreeLifePhase.Mature, 2);
            var old = TreeArchitecture.EffectiveParameters(ForestSpecies.Pine, TreeLifePhase.Old, 2);
            Assert.Equal(3, mature["Shape"]); Assert.Equal(100, mature["1CurveV"]);
            Assert.Equal(2, old["Shape"]); Assert.Equal(120, old["1CurveV"]);
            return null;
        });
    }

    private static List<Vertex[]> Components(Vertex[] crown)
    {
        var neighbours = new Dictionary<Vector3, HashSet<Vector3>>();
        for (int i = 0; i < crown.Length; i += 3)
            for (int j = 0; j < 3; j++)
            {
                var p = crown[i + j].Position;
                if (!neighbours.TryGetValue(p, out var adjacent)) neighbours[p] = adjacent = new();
                adjacent.Add(crown[i + (j + 1) % 3].Position);
            }
        var result = new List<Vertex[]>();
        var unvisited = neighbours.Keys.ToHashSet();
        while (unvisited.Count > 0)
        {
            var component = new HashSet<Vector3>(); var queue = new Stack<Vector3>();
            queue.Push(unvisited.First());
            while (queue.TryPop(out var p))
                if (unvisited.Remove(p)) { component.Add(p); foreach (var q in neighbours[p]) queue.Push(q); }
            result.Add(crown.Where(v => component.Contains(v.Position)).ToArray());
        }
        return result;
    }

    private static void AssertClosed(Vertex[] part)
    {
        var edges = new Dictionary<(Vector3, Vector3), int>();
        for (int i = 0; i < part.Length; i += 3)
            for (int j = 0; j < 3; j++)
            {
                var edge = (part[i + j].Position, part[i + (j + 1) % 3].Position);
                edges[edge] = edges.GetValueOrDefault(edge) + 1;
            }
        Assert.All(edges, pair => {
            Assert.Equal(1, pair.Value);
            Assert.Equal(1, edges.GetValueOrDefault((pair.Key.Item2, pair.Key.Item1)));
        });
    }
}
