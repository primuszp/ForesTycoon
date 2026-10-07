using System;
using System.Collections.Generic;
using ForesTycoon.TreeModels.Generation.Dendro.Tree;
using OpenTK.Mathematics;
using Vector3d = ForesTycoon.TreeModels.Generation.Dendro.Geom.Vector3d;

namespace ForesTycoon.TreeModels
{
    /// <summary>
    /// The complete woody structure of one tree architecture: trunk, limbs and twigs as a rooted
    /// forest of polylines in generator units, plus leaf sample points. The topology is exact:
    /// every child stem starts at a vertex of its parent, and the radii follow the pipe model
    /// (a stem carries as much cross-section as all the twigs it feeds), so a child can never be
    /// thicker than the parent at the junction and thickness only decreases towards the tips.
    /// Skeletons are immutable and shared; seasons, dieback and environment are applied to copies
    /// of the points at meshing time.
    /// </summary>
    internal sealed class TreeSkeleton
    {
        /// <summary>Radii scale with flow^(1/exponent); 2 would be the pure area-preserving pipe model.</summary>
        internal const float PipeExponent = 2.2f;

        /// <param name="Parent">Index of the stem this one grows out of; -1 for a root stem.</param>
        /// <param name="ParentVertex">Vertex of the parent at which this stem starts (its first point).</param>
        /// <param name="Fork">The stem continues the parent's own order (a split of the leader).</param>
        /// <param name="Rank">0..1 dieback priority: stems with the highest ranks die first.</param>
        internal readonly record struct Stem(int Parent, int ParentVertex, int Level, bool Fork,
            int Start, int Count, float Rank);

        internal readonly Stem[] Stems;
        internal readonly Vector3[] Points;
        /// <summary>Flow just below each vertex; the tip of every stem carries 1.</summary>
        internal readonly float[] Flow;
        internal readonly Vector3[] Leaves;
        internal readonly int[] LeafStem;
        internal readonly float Height;
        internal readonly float LeafRadius;
        internal readonly int LeafTotal;

        private TreeSkeleton(Stem[] stems, Vector3[] points, float[] flow, Vector3[] leaves, int[] leafStem,
            float height, float leafRadius, int leafTotal)
        {
            Stems = stems; Points = points; Flow = flow; Leaves = leaves; LeafStem = leafStem;
            Height = height; LeafRadius = leafRadius; LeafTotal = leafTotal;
        }

        internal int StemCount => Stems.Length;
        internal ReadOnlySpan<Vector3> PointsOf(int stem) => Points.AsSpan(Stems[stem].Start, Stems[stem].Count);
        internal ReadOnlySpan<float> FlowOf(int stem) => Flow.AsSpan(Stems[stem].Start, Stems[stem].Count);

        /// <summary>Radius in units of the twig tip radius.</summary>
        internal static float UnitRadius(float flow) => MathF.Pow(flow, 1 / PipeExponent);

        /// <summary>Unit radius of the main stem at a height, interpolated along its axis.</summary>
        internal float MainStemUnitRadiusAt(float z)
        {
            var points = PointsOf(0); var flow = FlowOf(0);
            for (int i = 1; i < points.Length; i++)
            {
                if (points[i].Z < z) continue;
                float dz = points[i].Z - points[i - 1].Z;
                float t = dz > 1e-9f ? Math.Clamp((z - points[i - 1].Z) / dz, 0, 1) : 1;
                return UnitRadius(flow[i - 1]) + (UnitRadius(flow[i]) - UnitRadius(flow[i - 1])) * t;
            }
            return UnitRadius(flow[^1]);
        }

        /// <summary>Marks every stem that is dead when the given share of the crown has died back.</summary>
        internal bool[] DeadMask(float dieback)
        {
            var dead = new bool[Stems.Length];
            if (dieback <= 0) return dead;
            float limit = 1 - Math.Clamp(dieback, 0, 1);
            for (int i = 0; i < Stems.Length; i++)
            {
                var stem = Stems[i];
                if (stem.Level == 0 || stem.Parent < 0) continue;
                dead[i] = dead[stem.Parent] || stem.Rank >= limit;
            }
            return dead;
        }

        /// <summary>Structural defects, empty when the skeleton is a valid rooted tree. Used by tests and tools.</summary>
        internal List<string> Validate()
        {
            var problems = new List<string>();
            if (Stems.Length == 0) problems.Add("no stems");
            else if (Stems[0].Level != 0 || Stems[0].Parent != -1) problems.Add("stem 0 is not the main trunk");
            var childFlow = new float[Flow.Length];
            for (int i = 0; i < Stems.Length; i++)
            {
                var s = Stems[i];
                if (s.Count < 2) problems.Add($"stem {i} has {s.Count} points");
                if (s.Start < 0 || s.Start + s.Count > Points.Length) { problems.Add($"stem {i} range"); continue; }
                if (s.Parent == -1) { if (s.Level != 0) problems.Add($"stem {i} is a level {s.Level} root"); }
                else
                {
                    if (s.Parent >= i || s.Parent < 0) { problems.Add($"stem {i} parent {s.Parent} breaks the order"); continue; }
                    var p = Stems[s.Parent];
                    if (s.Level != p.Level + 1 && !(s.Fork && s.Level == p.Level)) problems.Add($"stem {i} level {s.Level} under level {p.Level}");
                    if (s.ParentVertex < 0 || s.ParentVertex >= p.Count) { problems.Add($"stem {i} attaches to missing vertex"); continue; }
                    if (Points[s.Start] != Points[p.Start + s.ParentVertex]) problems.Add($"stem {i} does not start on its parent");
                    if (Flow[p.Start + s.ParentVertex] + 1e-4f < Flow[s.Start]) problems.Add($"stem {i} is thicker than its parent at the junction");
                    childFlow[p.Start + s.ParentVertex] += Flow[s.Start];
                }
                for (int v = 0; v < s.Count; v++)
                {
                    int k = s.Start + v;
                    if (!float.IsFinite(Points[k].X + Points[k].Y + Points[k].Z) || !float.IsFinite(Flow[k]) || Flow[k] < 1 - 1e-4f)
                        problems.Add($"stem {i} vertex {v} is not finite or below the tip flow");
                    if (v > 0 && Flow[k] > Flow[k - 1] + 1e-4f) problems.Add($"stem {i} thickens towards the tip at vertex {v}");
                    if (v > 0 && (Points[k] - Points[k - 1]).LengthSquared < 1e-14f) problems.Add($"stem {i} has a zero-length segment");
                }
                if (Math.Abs(Flow[s.Start + s.Count - 1] - 1) > 1e-4f) problems.Add($"stem {i} tip does not carry the unit flow");
            }
            // The pipe model identity: flow below a vertex = flow above it + what leaves there.
            for (int i = 0; i < Stems.Length; i++)
            {
                var s = Stems[i];
                for (int v = 0; v < s.Count - 1; v++)
                {
                    int k = s.Start + v;
                    if (Math.Abs(Flow[k] - Flow[k + 1] - childFlow[k]) > 1e-3f * Math.Max(1, Flow[k]))
                        problems.Add($"stem {i} vertex {v} breaks the pipe identity");
                }
            }
            for (int i = 0; i < Leaves.Length; i++)
                if (LeafStem[i] < 0 || LeafStem[i] >= Stems.Length) problems.Add($"leaf {i} has no stem");
            return problems;
        }

        // ---------------------------------------------------------------- construction

        private sealed class RawStem
        {
            internal int Parent, Level;
            internal bool Fork;
            internal List<Vector3> Points = new();
            internal List<int> Children = new();
        }

        private sealed class Collector : DefaultTreeTraversal
        {
            internal readonly List<RawStem> Raw = new();
            internal readonly List<Vector3> Leaves = new();
            internal readonly List<int> LeafOwner = new();
            private readonly List<int> stack = new();
            internal float Height;

            public override bool EnterTree(ITree tree) { Height = (float)tree.Height; return true; }

            public override bool EnterStem(IStem stem)
            {
                var points = new List<Vector3>(); int index = -1;
                foreach (var section in stem.Sections())
                {
                    if (index == section.Index) continue;
                    index = section.Index;
                    if (points.Count == 0) points.Add(Convert(section.LowerPosition));
                    points.Add(Convert(section.UpperPosition));
                }
                int parent = stack.Count > 0 ? stack[^1] : -1;
                if (points.Count < 2) { stack.Add(parent); return true; } // degenerate stem: its children adopt the grandparent
                int id = Raw.Count;
                Raw.Add(new RawStem { Parent = parent, Level = stem.Level, Points = points, Fork = parent >= 0 && Raw[parent].Level == stem.Level });
                if (parent >= 0) Raw[parent].Children.Add(id);
                stack.Add(id);
                return true;
            }

            public override bool LeaveStem(IStem stem) { stack.RemoveAt(stack.Count - 1); return true; }

            public override bool VisitLeaf(ILeaf leaf)
            {
                if (stack.Count > 0 && stack[^1] >= 0) { Leaves.Add(Convert(leaf.Transform.GetT())); LeafOwner.Add(stack[^1]); }
                return true;
            }

            private static Vector3 Convert(Vector3d p) => new((float)p.X, (float)p.Y, (float)p.Z);
        }

        /// <summary>Builds the skeleton of a generated DendroKit tree; <paramref name="maxLeaves"/> thins the leaf samples.</summary>
        internal static TreeSkeleton From(TreeImpl tree, int variant, int maxLeaves, int maxLimbs = 70, int maxTwigs = 200)
        {
            var collector = new Collector();
            tree.TraverseTree(collector);
            var raw = Prune(collector, maxLimbs, maxTwigs);
            int count = raw.Count;
            // 1. Give every parent a vertex exactly where each child leaves it.
            var attach = new int[count];
            var points = new List<Vector3>[count];
            for (int i = 0; i < count; i++) points[i] = raw[i].Points;
            for (int i = 0; i < count; i++)
            {
                var children = raw[i].Children;
                if (children.Count == 0) continue;
                var source = raw[i].Points;
                var hits = new List<(int Segment, float T, int Child)>(children.Count);
                foreach (int child in children)
                {
                    Vector3 start = raw[child].Points[0];
                    NearestOnPolyline(source, start, out int segment, out float t);
                    hits.Add((segment, t, child));
                }
                hits.Sort((a, b) => a.Segment != b.Segment ? a.Segment.CompareTo(b.Segment) : a.T != b.T ? a.T.CompareTo(b.T) : a.Child.CompareTo(b.Child));
                var rebuilt = new List<Vector3>(source.Count + hits.Count) { source[0] };
                const float Eps = 1e-4f;
                int h = 0;
                for (int segment = 0; segment < source.Count - 1; segment++)
                {
                    int startIndex = rebuilt.Count - 1;
                    List<int> atEnd = null;
                    while (h < hits.Count && hits[h].Segment == segment)
                    {
                        var hit = hits[h++];
                        if (hit.T <= Eps) attach[hit.Child] = startIndex;
                        else if (hit.T >= 1 - Eps) (atEnd ??= new()).Add(hit.Child);
                        else
                        {
                            Vector3 position = Vector3.Lerp(source[segment], source[segment + 1], hit.T);
                            // Two children leaving at the same spot share one vertex.
                            if ((rebuilt[^1] - position).LengthSquared > 1e-12f) rebuilt.Add(position);
                            attach[hit.Child] = rebuilt.Count - 1;
                        }
                    }
                    rebuilt.Add(source[segment + 1]);
                    if (atEnd != null) foreach (int child in atEnd) attach[child] = rebuilt.Count - 1;
                }
                points[i] = rebuilt;
                // Children start exactly on the parent vertex they chose.
                foreach (int child in children) raw[child].Points[0] = rebuilt[attach[child]];
            }

            // 1b. Rich parameter sets carry far more vertices than a low-poly mesh can show: keep the
            // vertices that bend the axis by more than a sliver and every vertex a child leaves from.
            float tolerance = 0.012f * Math.Max(collector.Height, 1e-4f);
            for (int i = 0; i < count; i++)
            {
                var source = points[i];
                var pinned = new bool[source.Count];
                foreach (int child in raw[i].Children) pinned[attach[child]] = true;
                var kept = SimplifyAxis(source, pinned, tolerance);
                if (kept.Count == source.Count) continue;
                var remap = new int[source.Count];
                var shorter = new List<Vector3>(kept.Count);
                for (int k = 0, next = 0; k < source.Count; k++)
                {
                    if (next < kept.Count && kept[next] == k) { remap[k] = shorter.Count; shorter.Add(source[k]); next++; }
                    else remap[k] = shorter.Count - 1;
                }
                points[i] = shorter;
                foreach (int child in raw[i].Children) attach[child] = remap[attach[child]];
            }

            // 2. Flatten.
            int total = 0;
            for (int i = 0; i < count; i++) total += points[i].Count;
            var flatPoints = new Vector3[total]; var flow = new float[total];
            var starts = new int[count];
            int cursor = 0;
            for (int i = 0; i < count; i++)
            {
                starts[i] = cursor;
                for (int v = 0; v < points[i].Count; v++) flatPoints[cursor++] = points[i][v];
            }
            // 3. Pipe model, distal stems first (children always have larger indices than parents).
            for (int i = count - 1; i >= 0; i--)
            {
                int n = points[i].Count;
                var childAtVertex = new float[n];
                foreach (int child in raw[i].Children) childAtVertex[attach[child]] += flow[starts[child]];
                float running = 1;
                for (int v = n - 1; v >= 0; v--)
                {
                    running += childAtVertex[v];
                    flow[starts[i] + v] = running;
                }
            }
            // 4. Leaves: deterministic stride thinning keeps the spatial spread of the cloud.
            int leafTotal = collector.Leaves.Count;
            int keep = Math.Min(leafTotal, maxLeaves);
            var leaves = new Vector3[keep]; var owners = new int[keep];
            for (int i = 0; i < keep; i++)
            {
                int source = keep == leafTotal ? i : (int)((long)i * leafTotal / keep);
                leaves[i] = collector.Leaves[source]; owners[i] = collector.LeafOwner[source];
            }
            // The outer reach is the mean of the farthest tenth of the leaves: one stray shoot must not
            // shrink the rest of the crown when the tree is scaled to its simulated crown radius.
            var reach = new float[keep];
            for (int i = 0; i < keep; i++) reach[i] = leaves[i].Xy.Length;
            Array.Sort(reach);
            int top = Math.Max(1, keep / 10);
            float leafRadius = 0;
            for (int i = keep - top; i < keep; i++) leafRadius += reach[i] / top;
            float height = Math.Max(collector.Height, 1e-4f);
            // 5. Dieback ranking: the upper, outer and (randomly) unlucky branches go first.
            var stems = new Stem[count];
            float maxReach = Math.Max(leafRadius, 1e-4f);
            for (int i = 0; i < count; i++)
            {
                var r = raw[i];
                Vector3 tip = points[i][^1];
                float exposure = Math.Clamp(0.65f * tip.Z / height + 0.35f * tip.Xy.Length / maxReach, 0, 1);
                float rank = r.Level == 0 ? 0 : Math.Clamp(0.55f * exposure + 0.45f * ForestTreeVariation.Unit(variant, 5000 + i), 0, 0.9999f);
                stems[i] = new(r.Parent, r.Parent >= 0 ? attach[i] : 0, r.Level, r.Fork, starts[i], points[i].Count, rank);
            }
            return new TreeSkeleton(stems, flatPoints, flow, leaves, owners, height, leafRadius, leafTotal);
        }

        /// <summary>Indices to keep so that no dropped vertex is farther than the tolerance from the axis.</summary>
        private static List<int> SimplifyAxis(List<Vector3> p, bool[] pinned, float tolerance)
        {
            int n = p.Count;
            var flag = new bool[n]; flag[0] = flag[n - 1] = true;
            for (int i = 0; i < n; i++) if (pinned[i]) flag[i] = true;
            var work = new Stack<(int, int)>(); work.Push((0, n - 1));
            while (work.Count > 0)
            {
                var (a, b) = work.Pop();
                int worst = -1; float worstDistance = tolerance;
                Vector3 ab = p[b] - p[a]; float length = ab.LengthSquared;
                for (int k = a + 1; k < b; k++)
                {
                    float t = length > 1e-14f ? Math.Clamp(Vector3.Dot(p[k] - p[a], ab) / length, 0, 1) : 0;
                    float d = (p[a] + ab * t - p[k]).Length;
                    if (d > worstDistance) { worstDistance = d; worst = k; }
                }
                if (worst < 0) continue;
                flag[worst] = true; work.Push((a, worst)); work.Push((worst, b));
            }
            var kept = new List<int>();
            for (int i = 0; i < n; i++) if (flag[i]) kept.Add(i);
            return kept;
        }

        /// <summary>
        /// Drops stems beyond the third order and thins limbs and twigs evenly (by generation order) down to
        /// a budget. Leaves of dropped stems are reassigned to the nearest surviving ancestor.
        /// </summary>
        private static List<RawStem> Prune(Collector collector, int maxLimbs, int maxTwigs)
        {
            var raw = collector.Raw; int n = raw.Count;
            var keep = new bool[n];
            int limbs = 0, twigs = 0;
            foreach (var r in raw) { if (r.Level == 1) limbs++; }
            int limbSeen = 0, limbKept = 0;
            for (int i = 0; i < n; i++)
                if (raw[i].Level <= 1 && (raw[i].Level == 0 || raw[i].Fork || KeepEvery(limbSeen++, limbs, maxLimbs, ref limbKept)))
                    keep[i] = raw[i].Parent < 0 || keep[raw[i].Parent];
            // Forks of limbs (same level) follow their original.
            for (int i = 0; i < n; i++)
                if (raw[i].Level == 2 && raw[i].Parent >= 0 && keep[raw[i].Parent]) twigs++;
            int twigSeen = 0, twigKept = 0;
            for (int i = 0; i < n; i++)
                if (raw[i].Level == 2 && raw[i].Parent >= 0 && keep[raw[i].Parent] && KeepEvery(twigSeen++, twigs, maxTwigs, ref twigKept))
                    keep[i] = true;
            // Forked limbs/twigs (same level as parent) survive with their parent.
            for (int i = 0; i < n; i++)
                if (!keep[i] && raw[i].Fork && raw[i].Level >= 1 && raw[i].Level <= 2 && raw[i].Parent >= 0 && keep[raw[i].Parent]) keep[i] = true;
            if (limbs <= maxLimbs && twigs <= maxTwigs)
                for (int i = 0; i < n; i++) keep[i] = raw[i].Level <= 2;
            var map = new int[n]; var result = new List<RawStem>(n); var ancestor = new int[n];
            for (int i = 0; i < n; i++)
            {
                if (keep[i]) { map[i] = result.Count; ancestor[i] = i; result.Add(raw[i]); }
                else ancestor[i] = raw[i].Parent >= 0 ? ancestor[raw[i].Parent] : -1;
            }
            foreach (var r in result)
            {
                r.Parent = r.Parent >= 0 ? map[r.Parent] : -1;
                r.Children = new List<int>();
            }
            for (int i = 0; i < result.Count; i++) if (result[i].Parent >= 0) result[result[i].Parent].Children.Add(i);
            for (int i = 0; i < collector.LeafOwner.Count; i++)
            {
                int owner = collector.LeafOwner[i];
                int a = ancestor[owner];
                collector.LeafOwner[i] = a >= 0 ? map[a] : 0;
            }
            return result;

            static bool KeepEvery(int index, int total, int budget, ref int kept)
            {
                if (total <= budget) return true;
                bool take = (long)(index + 1) * budget / total > kept;
                if (take) kept++;
                return take;
            }
        }

        private static void NearestOnPolyline(List<Vector3> polyline, Vector3 p, out int segment, out float t)
        {
            segment = 0; t = 0; float best = float.MaxValue;
            for (int i = 0; i < polyline.Count - 1; i++)
            {
                Vector3 a = polyline[i], ab = polyline[i + 1] - a;
                float length = ab.LengthSquared;
                float u = length > 1e-14f ? Math.Clamp(Vector3.Dot(p - a, ab) / length, 0, 1) : 0;
                float d = (a + ab * u - p).LengthSquared;
                if (d < best) { best = d; segment = i; t = u; }
            }
        }
    }
}
