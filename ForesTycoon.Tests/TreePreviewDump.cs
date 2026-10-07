using System.Globalization;
using System.Text;

namespace ForesTycoon.Tests;

// Diagnostic only: set TREE_PREVIEW_DIR to dump meshes for tools/render_tree_dump.py.
public class TreePreviewDump
{
    internal static void Write(string path, string label, params (DendroTreeGenerator.Mesh Mesh, float Offset)[] trees)
    {
        var sb = new StringBuilder("{\"label\":\"" + label + "\",\"trees\":[");
        bool first = true;
        foreach (var (mesh, offset) in trees)
        {
            if (!first) sb.Append(','); first = false;
            sb.Append("{\"dx\":").Append(offset.ToString(CultureInfo.InvariantCulture)).Append(",\"parts\":[");
            Part("wood", mesh.Trunk.Concat(mesh.Branches).ToArray(), sb); sb.Append(',');
            Part("crown", mesh.Crown, sb);
            sb.Append("]}");
        }
        sb.Append("]}");
        File.WriteAllText(path, sb.ToString());
    }

    private static void Part(string name, Vertex[] v, StringBuilder sb)
    {
        sb.Append("{\"name\":\"").Append(name).Append("\",\"tris\":[");
        for (int i = 0; i + 2 < v.Length; i += 3)
        {
            if (i > 0) sb.Append(',');
            uint c = v[i].Color;
            sb.Append('[');
            for (int j = 0; j < 3; j++)
                sb.Append(string.Join(",", new[] { v[i + j].Position.X, v[i + j].Position.Y, v[i + j].Position.Z }
                    .Select(f => f.ToString("0.####", CultureInfo.InvariantCulture)))).Append(j < 2 ? "," : "");
            sb.Append(',').Append(c & 0xff).Append(',').Append((c >> 8) & 0xff).Append(',').Append((c >> 16) & 0xff).Append(']');
        }
        sb.Append("]}");
    }

    [Fact]
    public void DumpPhases()
    {
        string dir = Environment.GetEnvironmentVariable("TREE_PREVIEW_DIR");
        if (string.IsNullOrEmpty(dir)) return;
        Directory.CreateDirectory(dir);
        foreach (var species in new[] { ForestSpecies.Spruce, ForestSpecies.Birch, ForestSpecies.Oak, ForestSpecies.Beech })
            foreach (var leaves in new[] { LeafState.Full, LeafState.Bare })
            {
                var trees = new List<(DendroTreeGenerator.Mesh, float)>();
                var profile = ForestSpeciesProfile.For(species);
                int i = 0;
                foreach (var phase in Enum.GetValues<TreeLifePhase>())
                {
                    float age = phase switch { TreeLifePhase.Seedling => 1f, TreeLifePhase.Sapling => profile.MatureAgeYears * 0.2f,
                        TreeLifePhase.Young => profile.MatureAgeYears * 0.5f, TreeLifePhase.Mature => profile.MatureAgeYears * 1.5f,
                        TreeLifePhase.Old => profile.MaximumAgeYears * 0.75f, _ => profile.MaximumAgeYears * 0.92f };
                    var size = ForestTreeGrowth.Initial(species, age, 1);
                    var spec = new TreeShapeSpec(species, 42, phase, size, phase == TreeLifePhase.Senescent ? 0.4f : 1, new TreeSite(0.85f),
                        leaves, 0.4f);
                    trees.Add((DendroTreeGenerator.Generate(spec, ForestLod.Near), i++ * 14f));
                }
                Write(Path.Combine(dir, $"ph-{species}-{leaves}.json"), species.ToString(), trees.ToArray());
                Write(Path.Combine(dir, $"one-{species}-{leaves}.json"), species.ToString(), trees[3]);
            }
    }

    [Fact]
    public void DumpStates()
    {
        string dir = Environment.GetEnvironmentVariable("TREE_PREVIEW_DIR");
        if (string.IsNullOrEmpty(dir)) return;
        foreach (var species in new[] { ForestSpecies.Oak, ForestSpecies.Beech })
        {
            var profile = ForestSpeciesProfile.For(species);
            var size = ForestTreeGrowth.Initial(species, profile.MatureAgeYears * 1.5f, 1);
            TreeShapeSpec S(LeafState l, float vigor = 1, TreeSite? site = null, TreeLifePhase ph = TreeLifePhase.Mature) =>
                new(species, 42, ph, size, vigor, site ?? new TreeSite(0.85f), l, 0.3f);
            var specs = new[] { S(LeafState.Budding), S(LeafState.Full), S(LeafState.Autumn), S(LeafState.Falling), S(LeafState.Bare),
                S(LeafState.Full, 0.4f, null, TreeLifePhase.Old), S(LeafState.Full, 0.1f, null, TreeLifePhase.Senescent),
                S(LeafState.Full, 1, new TreeSite(0.85f, 1, 0, 0, 0, 1)), S(LeafState.Full, 1, new TreeSite(0.85f, 1, 1, 0, 0, 0)) };
            Write(Path.Combine(dir, $"st-{species}.json"), "states", specs.Select((sp, i) => (DendroTreeGenerator.Generate(sp, ForestLod.Near), i * 14f)).ToArray());
        }
    }
}
