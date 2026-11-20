using System;
using System.Collections.Generic;
using OpenTK.Mathematics;

namespace ForesTycoon.TreeModels
{
    // Open, opaque shoot geometry for the stages before crown closure. All organs attach
    // to the same axes in summer and winter. Dimensions and site deformation come from TreeForm.
    internal sealed class JuvenileTreeMesh
    {
        private enum Blade { Strap, Oval, Heart, Pointed, Oak, SessileOak, NarrowOak, Maple, Needle }
        private readonly TreeForm form;
        private readonly ForestLod lod;
        private readonly ForestSpecies species;
        private readonly List<Vertex> trunk = new(), shoots = new(), leaves = new();
        private readonly float h, r, yaw, stemRadius, bendHeight;
        private readonly Vector3 bend, leaderTip;
        private readonly uint stemColor, foliageColor;
        private readonly bool conifer, opposite, seedling, leafy;
        private readonly Blade blade;

        private JuvenileTreeMesh(TreeForm form, ForestLod lod)
        {
            this.form = form; this.lod = lod;
            species = form.Spec.Species; seedling = form.Spec.Phase == TreeLifePhase.Seedling;
            conifer = ForestSpeciesTraits.For(species).Conifer;
            opposite = species is ForestSpecies.Maple or ForestSpecies.Ash or ForestSpecies.Elder;
            leafy = form.Foliage > 0 && !form.Spec.Dead;
            h = form.Height; yaw = form.Spec.Yaw + Variation(701) * MathF.Tau;
            // Cotyledons and the first leaf flush occupy much of a seedling's width.
            r = seedling ? Math.Max(form.CrownRadius, h * (conifer ? .31f : .42f)) : form.CrownRadius;
            bendHeight = seedling ? .49f : .38f;
            var bendDirection = Heading(yaw + 1.4f);
            float flex = conifer ? .06f : species == ForestSpecies.Beech ? .28f : .17f;
            bend = Scaffold(bendHeight) + bendDirection * (r * flex * (.4f + Variation(704)));
            leaderTip = Scaffold(1) - bendDirection * (r * flex * Variation(705));
            var look = ForestSpeciesBook.Look(species);
            stemColor = !seedling || form.Spec.Dead || look?.Bark != null ? form.WoodColor
                : Mix(form.WoodColor, species == ForestSpecies.Larch ? 0x354b8bu : 0x4a8b78u, .67f);
            foliageColor = form.Spec.Leaves == LeafState.Full && look?.Summer == null
                ? Mix(form.CrownColor, 0x57aa8au, seedling ? .45f : .21f) : form.CrownColor;
            stemRadius = Math.Clamp(form.BreastRadius, h * .004f, h * (seedling ? .014f : .017f));
            blade = species switch
            {
                ForestSpecies.Oak => Blade.Oak,
                ForestSpecies.SessileOak => Blade.SessileOak,
                ForestSpecies.TurkeyOak or ForestSpecies.Hawthorn => Blade.NarrowOak,
                ForestSpecies.Maple => Blade.Maple,
                ForestSpecies.Hazel => Blade.Heart,
                ForestSpecies.Birch or ForestSpecies.Ash or ForestSpecies.Elder => Blade.Pointed,
                _ => Blade.Oval
            };
        }

        internal static DendroTreeGenerator.Mesh Build(TreeForm form, ForestLod lod)
        {
            var plant = new JuvenileTreeMesh(form, lod);
            plant.Grow();
            return new(plant.trunk.ToArray(), plant.shoots.ToArray(), plant.leaves.ToArray(),
                form.Skeleton.StemCount, form.Skeleton.LeafTotal);
        }

        private float Variation(int channel) => ForestTreeVariation.Unit(form.Spec.Seed, channel);
        private static Vector3 Heading(float angle) => new(MathF.Cos(angle), MathF.Sin(angle), 0);

        // Keep the lean and bends of the generated leader; shrubs initially have one visible stem.
        private Vector3 Scaffold(float t)
        {
            var points = form.Skeleton.PointsOf(0);
            float z = points[^1].Z * t;
            int i = 1;
            while (i < points.Length - 1 && points[i].Z < z) i++;
            float blend = Math.Clamp((z - points[i - 1].Z) / Math.Max(1e-6f, points[i].Z - points[i - 1].Z), 0, 1);
            var point = form.ToFrame(Vector3.Lerp(points[i - 1], points[i], blend) - points[0]);
            return new(point.X * .55f, point.Y * .55f, t * h);
        }

        private static uint Mix(uint from, uint to, float amount)
        {
            uint channel(int shift) => (uint)(((from >> shift) & 255) * (1 - amount) + ((to >> shift) & 255) * amount);
            return (from & 0xff000000) | channel(0) | (channel(8) << 8) | (channel(16) << 16);
        }

        private Vector3 Leader(float t) => t < bendHeight
            ? bend * (t / bendHeight)
            : Vector3.Lerp(bend, leaderTip, (t - bendHeight) / (1 - bendHeight));

        private void Grow()
        {
            if (lod == ForestLod.Far)
            {
                // At a few pixels, retain the visible stem and three separated foliage groups.
                Twig(trunk, Leader(0), Leader(1), stemRadius * 1.3f, stemColor);
                for (int i = 0; i < 3; i++)
                {
                    float t = seedling ? .64f + .08f * i : .30f + .21f * i;
                    if (seedling && species is ForestSpecies.Pine or ForestSpecies.Fir)
                        t = i == 2 ? .86f : species == ForestSpecies.Pine ? .77f : .65f;
                    var start = Leader(t); var d = Heading(yaw + (i == 1 ? MathF.PI : i * 2.4f));
                    var tip = start + d * r * (i == 2 ? .36f : 1);
                    float rise = species == ForestSpecies.Fir ? .015f : species == ForestSpecies.Pine && !seedling ? .24f : seedling ? .045f : .17f;
                    tip.Z = i == 2 ? h : start.Z + h * rise;
                    if (!seedling && !TreePhenology.Evergreen(species))
                        Twig(shoots, start, tip, stemRadius * .5f, stemColor);
                    if (leafy) Foil(start, tip, r * (conifer ? .24f : .35f), Blade.Strap);
                }
                return;
            }

            float neck = species switch { ForestSpecies.Pine => .77f, ForestSpecies.Fir => .65f, _ => conifer ? .54f : .39f };
            Vector3 basePoint = Leader(0), middle = bend, top = Leader(seedling ? .92f : .97f);
            StemSection(basePoint, middle, stemRadius, stemRadius * .78f);
            for (int i = 0; i < 3; i++)
            {
                var edge = Heading(yaw + MathF.Tau * i / 3);
                var next = Heading(yaw + MathF.Tau * ((i + 1) % 3) / 3);
                Triangle(trunk, middle + edge * (stemRadius * .78f), middle + next * (stemRadius * .78f), top, stemColor);
            }
            if (seedling) FirstLeaves(neck);
            else SideShoots();
            if (!leafy) return;

            if (conifer && !seedling && lod == ForestLod.Near)
            {
                int ranks = species == ForestSpecies.Juniper ? 4 : 7;
                for (int i = 0; i < ranks; i++)
                {
                    float node = i / (float)(ranks - 1);
                    var start = Leader(species == ForestSpecies.Pine ? .43f + .48f * node : .26f + .60f * node);
                    for (int j = 0; j < 3; j++)
                    {
                        var d = Heading(yaw + i * 2.4f + j * MathF.Tau / 3);
                        float lift = species == ForestSpecies.Pine ? 1.25f : species == ForestSpecies.Fir ? .22f : .60f;
                        var end = start + (d + Vector3.UnitZ * lift) * r * (.28f - .10f * i / ranks);
                        if (species == ForestSpecies.Pine) end.Z = Math.Min(end.Z, .98f * h);
                        Foil(start, end, r * .025f, Blade.Needle);
                    }
                }
            }
            int tips = conifer && lod == ForestLod.Near ? 6
                : species == ForestSpecies.Juniper && lod == ForestLod.Medium ? 1 : 2;
            for (int i = 0; i < tips; i++)
            {
                var d = Heading(yaw + MathF.Tau * i / tips);
                var start = Leader(seedling && conifer ? .79f + .06f * (i % 2) : .86f);
                var end = top + d * r * (seedling ? .46f : .22f);
                end.Z = i == 0 ? h : h * (.97f + .025f * Variation(830 + i));
                if (seedling && species is ForestSpecies.Pine or ForestSpecies.Fir)
                {
                    // Emerging primary needles rise above the cotyledon whorl on a very short axis.
                    start = Leader(neck + .025f * i);
                    end = start + d * r * (species == ForestSpecies.Pine ? .38f : .55f);
                    end.Z = i == 0 ? h : Math.Min(h * .98f, start.Z + h * (.06f + .09f * Variation(830 + i)));
                }
                Foil(start, end, r * (conifer ? .048f : .14f), conifer ? Blade.Needle : blade);
            }
        }

        private void FirstLeaves(float neck)
        {
            if (!leafy) return;
            var node = Leader(neck);
            if (conifer)
            {
                int count = species switch { ForestSpecies.Juniper => 2, ForestSpecies.Larch => 7,
                    ForestSpecies.Spruce => 8, ForestSpecies.Fir => 5, _ => 6 };
                for (int i = 0; i < count; i++)
                {
                    var d = Heading(yaw + MathF.Tau * (i + .14f * Variation(710 + i)) / count);
                    float rise = species switch { ForestSpecies.Larch => .23f, ForestSpecies.Fir => -.035f,
                        ForestSpecies.Pine => -.10f, _ => .09f };
                    Foil(node, node + d * r * (.84f + .20f * Variation(720 + i))
                        + Vector3.UnitZ * h * (rise + .12f * Variation(730 + i)),
                        r * (species == ForestSpecies.Fir ? .095f : species == ForestSpecies.Pine ? .052f : .038f), Blade.Needle);
                }
                return;
            }

            bool oak = species is ForestSpecies.Oak or ForestSpecies.SessileOak or ForestSpecies.TurkeyOak;
            if (!oak && species != ForestSpecies.Hazel)
            {
                Blade cotyledon = species == ForestSpecies.Beech ? Blade.Heart : opposite ? Blade.Strap : Blade.Oval;
                float span = species == ForestSpecies.Beech || opposite ? .95f : .50f;
                for (int i = 0; i < 2; i++)
                {
                    var d = Heading(yaw + i * MathF.PI);
                    Foil(node, node + d * r * span + Vector3.UnitZ * (.025f * h),
                        r * (species == ForestSpecies.Beech ? .44f : opposite ? .11f : .18f), cotyledon);
                }
            }
            int slots = oak ? 5 : species == ForestSpecies.Beech ? 2 : 4;
            for (int i = 0; i < slots; i++)
            {
                float t = oak ? .44f + .092f * i : opposite ? .64f + .14f * (i / 2) : .58f + .10f * i;
                float angle = opposite ? MathF.PI * (i % 2) + MathF.PI * .5f * (1 + i / 2)
                    : species == ForestSpecies.Beech ? .85f + i * MathF.PI : .68f + i * 2.4f;
                var d = Heading(yaw + angle);
                var start = Leader(t);
                var tip = start + d * r * (.90f - .065f * i) + Vector3.UnitZ * h * (.035f + .055f * Variation(740 + i));
                Foil(start, tip, r * LeafWidth() * (1 - .065f * i), blade);
            }
        }

        private float LeafWidth() => species switch
        {
            ForestSpecies.Maple or ForestSpecies.Hazel => .42f,
            ForestSpecies.Blackthorn or ForestSpecies.Ash or ForestSpecies.Elder => .22f,
            ForestSpecies.SessileOak => .28f,
            _ => .32f
        };

        private void SideShoots()
        {
            // The early crown is organised around nodes on a leader. Oaks flush near shoot
            // ends, beech spreads in two ranks, maple, ash and elder have opposite nodes.
            int tiers = conifer || opposite ? 3 : 6;
            // Use the generated architecture for the number of lateral nodes as well as the
            // leader. A sparse or edited skeleton must not acquire a full set of shoots.
            int perTier = conifer ? 3 : opposite ? 2 : 1;
            Span<int> lateralStems = stackalloc int[tiers * perTier];
            int lateral = 0;
            for (int i = 1; i < form.Skeleton.Stems.Length && lateral < lateralStems.Length; i++)
                if (form.Skeleton.Stems[i].Level == 1) lateralStems[lateral++] = i;
            tiers = Math.Min(tiers, (lateral + perTier - 1) / perTier);
            bool shrub = form.Shrub;
            float baseT = Math.Clamp(form.CrownBase / h + (shrub ? .07f : .10f), .08f, .53f);
            for (int tier = 0; tier < tiers; tier++)
            {
                float fraction = tier / (float)tiers;
                float t = baseT + (.91f - baseT) * (fraction + .28f * (Variation(780 + tier) - .5f) / tiers);
                for (int j = 0; j < perTier; j++)
                {
                    int index = tier * perTier + j;
                    if (index >= lateral) break;
                    float angle = yaw + (opposite ? tier * MathF.PI * .5f
                        : species == ForestSpecies.Beech ? tier * 2.95f : tier * 2.4f)
                        + j * MathF.Tau / perTier + .28f * (Variation(750 + index) - .5f);
                    var d = Heading(angle);
                    var start = Leader(t);
                    float reach = r * (1 - .48f * fraction) * (.78f + .31f * Variation(760 + index));
                    float rise = species switch { ForestSpecies.Fir => .015f + .055f * fraction, ForestSpecies.Pine => .23f, ForestSpecies.Spruce => .09f,
                        ForestSpecies.Beech => .075f, ForestSpecies.Hazel or ForestSpecies.Juniper => .30f, _ => .17f };
                    if (shrub && tier == 0) start = Leader(.07f + .05f * Variation(788));
                    if (shrub && tier < 2) rise *= 1.6f;
                    int stem = lateralStems[index];
                    var points = form.Skeleton.PointsOf(stem);
                    var span = form.ToFrame(points[^1] - points[0]);
                    reach *= .81f + .26f * Math.Clamp(span.Xy.Length / r, 0, 1.6f);
                    var end = start + d * reach + Vector3.UnitZ * h * rise * (1 - .5f * fraction) * (.78f + .48f * Variation(770 + index));
                    Twig(shoots, start, end, stemRadius * (shrub ? .64f : .48f), stemColor);
                    if (!leafy || form.Dead[stem]) continue;
                    if (lod == ForestLod.Medium && conifer)
                    {
                        bool pine = species == ForestSpecies.Pine;
                        var tip = end + (d + Vector3.UnitZ * (pine ? .75f : 0)) * r * .20f;
                        Foil(Vector3.Lerp(start, end, pine ? .48f : .16f), tip, r * (species == ForestSpecies.Fir ? .18f : .13f), Blade.Needle);
                    }
                    else if (conifer) NeedleSpray(start, end, angle);
                    else LeafyShoot(start, end, angle);
                }
            }
        }

        private void LeafyShoot(Vector3 start, Vector3 end, float angle)
        {
            bool compound = species is ForestSpecies.Ash or ForestSpecies.Elder;
            bool oak = species is ForestSpecies.Oak or ForestSpecies.SessileOak or ForestSpecies.TurkeyOak;
            int nodes = compound || lod != ForestLod.Near ? 2 : 3;
            for (int i = 0; i < nodes; i++)
            {
                float t = (oak ? .50f : .18f) + (oak ? .45f : .77f) * i / (nodes - 1);
                var at = Vector3.Lerp(start, end, t);
                int count = opposite ? 2 : 1;
                for (int j = 0; j < count; j++)
                {
                    float sign = ((i + j) & 1) == 0 ? 1 : -1;
                    float turn = angle + sign * (oak ? 1.04f : 1.27f);
                    var d = Heading(turn);
                    float length = r * (compound ? .65f : .58f) * (.8f + .2f * t);
                    var tip = at + d * length + Vector3.UnitZ * length * (.30f - .25f * t + .24f * MathF.Sin(angle + i));
                    if (compound && lod == ForestLod.Near)
                    {
                        // The narrow central strip carries two pairs of leaflets and a terminal leaflet.
                        Foil(at, tip, length * .035f, Blade.Needle);
                        for (int k = 0; k < 4; k++)
                        {
                            var basePoint = Vector3.Lerp(at, tip, k < 2 ? .31f : .61f);
                            Foil(basePoint, basePoint + Heading(turn + (k % 2 == 0 ? .86f : -.86f)) * length * .44f
                                + Vector3.UnitZ * length * .08f, length * .13f, Blade.Needle);
                        }
                        Foil(Vector3.Lerp(at, tip, .70f), tip + d * length * .16f, length * .12f, Blade.Needle);
                    }
                    else Foil(at, tip, length * LeafWidth(), blade);
                }
            }
        }

        private void NeedleSpray(Vector3 start, Vector3 end, float angle)
        {
            if (species == ForestSpecies.Fir)
            {
                // Two flat, forked sprays share the same 14 tetrahedra as a row of needles.
                for (int j = 0; j < 2; j++)
                {
                    float turn = angle + (j == 0 ? -.74f : .68f);
                    var at = Vector3.Lerp(start, end, .28f + .24f * j);
                    var tip = end + Heading(turn) * r * .47f;
                    Foil(at, tip, r * .027f, Blade.Needle);
                    for (int k = 0; k < 6; k++)
                    {
                        float along = .16f + .33f * (k / 2);
                        var origin = Vector3.Lerp(at, tip, along);
                        float length = r * (.39f - .13f * along);
                        var direction = Heading(turn + (k % 2 == 0 ? -1 : 1) * (1.30f - .43f * along));
                        Foil(origin, origin + length * (direction + Vector3.UnitZ * .10f), length * .15f, Blade.Needle);
                    }
                }
                return;
            }
            bool tuft = species == ForestSpecies.Larch;
            int nodes = species switch { ForestSpecies.Juniper => 2, ForestSpecies.Larch => 4, _ => 7 };
            for (int i = 0; i < nodes; i++)
            {
                float along = i / (float)(nodes - 1);
                var at = Vector3.Lerp(start, end, species == ForestSpecies.Pine ? .48f + .48f * along : .12f + .84f * along);
                int needles = tuft ? 5 : species == ForestSpecies.Juniper ? 3 : 2;
                for (int j = 0; j < needles; j++)
                {
                    float side = j == 0 ? -1 : 1;
                    float turn = species switch
                    {
                        ForestSpecies.Larch => MathF.Tau * j / needles + i * .4f,
                        ForestSpecies.Juniper => MathF.Tau * j / 3 + i * .7f,
                        ForestSpecies.Pine => i * 2.4f + side * .21f,
                        _ => side * (.75f + .40f * (i % 2))
                    };
                    float lift = species switch { ForestSpecies.Pine => .72f,
                        ForestSpecies.Larch => .84f, ForestSpecies.Juniper => .53f, _ => .29f + .42f * (i % 2) };
                    float length = r * (species == ForestSpecies.Pine ? .48f : tuft ? .23f : .32f);
                    var tip = at + length * (Heading(angle + turn) + Vector3.UnitZ * lift);
                    Foil(at, tip, length * (tuft ? .085f : .105f), Blade.Needle);
                }
            }
        }

        private void StemSection(Vector3 a, Vector3 b, float ra, float rb)
        {
            Triangle(trunk, a + Heading(yaw) * ra, a + Heading(yaw + 2 * MathF.Tau / 3) * ra,
                a + Heading(yaw + MathF.Tau / 3) * ra, stemColor);
            for (int i = 0; i < 3; i++)
            {
                var d = Heading(yaw + MathF.Tau * i / 3);
                var next = Heading(yaw + MathF.Tau * ((i + 1) % 3) / 3);
                Triangle(trunk, a + d * ra, a + next * ra, b + d * rb, stemColor);
                Triangle(trunk, a + next * ra, b + next * rb, b + d * rb, stemColor);
            }
        }

        private void Twig(List<Vertex> part, Vector3 a, Vector3 b, float radius, uint color)
        {
            var axis = (b - a).Normalized();
            var u = MathF.Abs(axis.Z) > .90f ? Vector3.UnitX : Vector3.Cross(axis, Vector3.UnitZ).Normalized();
            u = (u - axis * Vector3.Dot(u, axis)).Normalized();
            var v = Vector3.Cross(axis, u);
            Vector3 p = a + u * radius;
            Vector3 q = a + (-.5f * u + .8660254f * v) * radius;
            Vector3 s = a + (-.5f * u - .8660254f * v) * radius;
            Triangle(part, p, q, b, color); Triangle(part, q, s, b, color);
            Triangle(part, s, p, b, color); Triangle(part, s, q, p, color);
        }

        // Closed folded blades with an upper and a lower face. The outline is cut in
        // geometry, so an individual leaf is visible from the side without alpha cards.
        private void Foil(Vector3 at, Vector3 tip, float width, Blade shape)
        {
            var delta = (tip - at) * form.Foliage;
            // The terminal bud stays at the simulated height while its blade opens.
            if (tip.Z == h) delta.Z = h - at.Z;
            var side = new Vector3(-delta.Y, delta.X, 0);
            var across = (side.LengthSquared > 0 ? side.Normalized() : Vector3.UnitX) * width * form.Foliage;
            var centre = at + delta * (shape == Blade.Needle && species == ForestSpecies.Fir ? .68f : .50f);
            var ridge = Vector3.UnitZ * (across.Length * .24f);
            uint color = foliageColor;
            if (lod != ForestLod.Near || shape == Blade.Needle)
            {
                // A triangular section keeps the far shoot and each near needle solid with four faces.
                var left = centre + across - ridge; var right = centre - across - ridge;
                var apex = at + delta;
                Triangle(leaves, at, right, apex, color); Triangle(leaves, at, apex, left, color);
                Triangle(leaves, at, left, right, color); Triangle(leaves, apex, right, left, color);
                return;
            }
            ReadOnlySpan<Vector2> outline = shape switch
            {
                Blade.Oak => [new(.09f,.68f), new(.22f,.49f), new(.32f,.92f), new(.40f,.63f), new(.54f,1), new(.62f,.70f), new(.77f,.97f), new(.89f,.70f)],
                Blade.SessileOak => [new(.13f,.05f), new(.32f,.61f), new(.38f,.54f), new(.49f,.91f), new(.56f,.73f), new(.68f,1), new(.76f,.78f), new(.85f,.80f)],
                Blade.NarrowOak => [new(.19f,.30f), new(.35f,.96f), new(.43f,.54f), new(.61f,1), new(.69f,.58f), new(.85f,.70f)],
                Blade.Maple => [new(.22f,.64f), new(.19f,1.12f), new(.43f,.68f), new(.61f,.96f), new(.69f,.41f)],
                Blade.Heart => [new(.16f,.90f), new(.37f,1), new(.70f,.80f), new(.91f,.40f)],
                Blade.Strap => [new(.25f,.88f), new(.72f,1), new(.90f,.65f)],
                Blade.Pointed => [new(.30f,.9f), new(.48f,1), new(.74f,.59f)],
                _ => [new(.22f,.72f), new(.46f,1), new(.76f,.84f), new(.91f,.45f)]
            };
            across.Z = across.Length * .29f * MathF.Sin(at.Z / h * 7 + Vector3.Dot(delta, Heading(yaw)) / r);
            var upper = centre + ridge * 1.35f; var lower = centre + ridge * .55f;
            Vector3 previousLeft = at, previousRight = at;
            for (int i = 0; i <= outline.Length; i++)
            {
                Vector2 station = i == outline.Length ? new(1, 0) : outline[i];
                var mid = at + delta * station.X;
                var left = mid + across * station.Y; var right = mid - across * station.Y;
                Triangle(leaves, previousRight, right, upper, color); Triangle(leaves, left, previousLeft, upper, color);
                Triangle(leaves, right, previousRight, lower, color); Triangle(leaves, previousLeft, left, lower, color);
                previousLeft = left; previousRight = right;
            }
        }

        private void Triangle(List<Vertex> part, Vector3 a, Vector3 b, Vector3 c, uint color)
        {
            a = form.Warp.Apply(a); b = form.Warp.Apply(b); c = form.Warp.Apply(c);
            var normal = Vector3.Cross(b - a, c - a).Normalized();
            part.Add(new(a, normal, color)); part.Add(new(b, normal, color)); part.Add(new(c, normal, color));
        }
    }
}
