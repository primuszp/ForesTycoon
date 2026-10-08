using System;
using System.Diagnostics;
using ImGuiNET;
using NVec2 = System.Numerics.Vector2;
using NVec4 = System.Numerics.Vector4;

namespace ForesTycoon
{
    /// <summary>What the player asked for from the management panel; the viewport carries it out.</summary>
    internal readonly record struct ManagementRequest(ManagementRequestKind Kind, int TileId, ForestSpecies Species = ForestSpecies.None);
    internal enum ManagementRequestKind : byte { None, Focus, Plant, Harvest }

    /// <summary>
    /// Management lenses (water, soil, health, stand, to-do) as a top-down tile raster with a
    /// diagnosis panel, after docs/management-view-design.md. Read-only: it never advances the
    /// simulation; actions are returned as requests and go through the normal world commands.
    /// </summary>
    internal sealed class ManagementView
    {
        internal enum Lens { Water, Soil, Health, Stand, Todo }

        private static readonly string[][] Layers =
        {
            new[] { "Elérhető gyökérzónavíz", "Aszálystressz", "Túlnedvesség" },
            new[] { "Talajtípus", "Termékenység", "Vízkapacitás" },
            new[] { "Vitalitás", "Limitáló tényező", "Elhalt fák aránya" },
            new[] { "Fafaj", "Érettség", "Fakészlet", "Sűrűség" },
            new[] { "Javasolt beavatkozás" }
        };
        private static readonly string[] LensNames = { "Víz", "Talaj", "Egészség", "Állomány", "Teendők" };
        // Colour-blind safe ramps: purple–grey–green for vitality, brown–sand–blue for water.
        private static readonly NVec4[] WaterRamp = { new(.59f, .35f, .18f, 1), new(.88f, .80f, .59f, 1), new(.24f, .55f, .84f, 1), new(.10f, .27f, .63f, 1) };
        private static readonly NVec4[] VitalityRamp = { new(.59f, .24f, .63f, 1), new(.67f, .67f, .65f, 1), new(.24f, .59f, .27f, 1) };
        private static readonly NVec4[] SequentialRamp = { new(.93f, .93f, .84f, 1), new(.55f, .73f, .40f, 1), new(.13f, .33f, .18f, 1) };
        private static readonly NVec4 NoData = new(.32f, .34f, .33f, 1), Land = new(.80f, .79f, .74f, 1);

        private Lens lens = Lens.Todo;
        private int layer;
        private int selected = -1;
        private ForestTileSurvey[] survey;
        private ulong surveyRevision = ulong.MaxValue;
        private readonly Stopwatch age = new();

        internal Lens Current => lens;
        internal void Select(Lens value, int sublayer = 0) { lens = value; layer = sublayer; }
        internal void SelectTile(int tileId) => selected = tileId;

        internal ManagementRequest Draw(GameWorld world)
        {
            var request = default(ManagementRequest);
            var soils = world.Soils;
            if (soils == null) { ImGui.TextDisabled("Nincs talajmodell ezen a térképen."); return request; }
            Refresh(world);
            var grid = soils.Grid;

            for (int i = 0; i < LensNames.Length; i++)
            {
                if (i > 0) ImGui.SameLine();
                bool active = (int)lens == i;
                if (active) ImGui.PushStyleColor(ImGuiCol.Button, HudTheme.Moss);
                if (ImGui.Button(LensNames[i])) Select((Lens)i);
                if (active) ImGui.PopStyleColor();
            }
            var names = Layers[(int)lens];
            if (names.Length > 1)
            {
                layer = Math.Clamp(layer, 0, names.Length - 1);
                ImGui.SetNextItemWidth(220);
                ImGui.Combo("Réteg", ref layer, string.Join('\0', names) + '\0');
            }
            if (lens == Lens.Todo) DrawTodoSummary();

            const float MapSize = 380;
            ImGui.BeginGroup();
            DrawMap(world, grid, MapSize, ref request);
            DrawLegend(soils);
            ImGui.EndGroup();
            ImGui.SameLine();
            ImGui.BeginGroup();
            ImGui.PushItemWidth(230);
            DrawPanel(world, soils, ref request);
            ImGui.PopItemWidth();
            ImGui.EndGroup();
            return request;
        }

        private void Refresh(GameWorld world)
        {
            // Monthly forest state drives the survey; water changes continuously, so refresh it at
            // most once a second while the window is open.
            if (survey != null && surveyRevision == world.ForestRevision && age.IsRunning && age.ElapsedMilliseconds < 1000) return;
            survey = world.BuildManagementSurvey();
            surveyRevision = world.ForestRevision;
            age.Restart();
        }

        private void DrawTodoSummary()
        {
            Span<int> counts = stackalloc int[7];
            foreach (var s in survey) counts[(int)s.Issue]++;
            bool any = false;
            for (int i = 1; i < counts.Length; i++)
            {
                if (counts[i] == 0) continue;
                if (any) ImGui.SameLine();
                any = true;
                ImGui.TextColored(IssueColor((ManagementIssue)i), $"{IssueShort((ManagementIssue)i)}: {counts[i]}");
            }
            if (!any) ImGui.TextColored(HudTheme.Good, "Nincs sürgős teendő.");
        }

        private void DrawMap(GameWorld world, RasterGrid grid, float mapSize, ref ManagementRequest request)
        {
            float scale = mapSize / Math.Max(grid.Columns, grid.Rows);
            var size = new NVec2(grid.Columns * scale, grid.Rows * scale);
            // At most 96×96 rectangles; larger maps draw representative centre samples.
            int columns = Math.Min(96, grid.Columns), rows = Math.Min(96, grid.Rows);
            var start = ImGui.GetCursorScreenPos();
            ImGui.InvisibleButton("##management-map", size);
            bool hovered = ImGui.IsItemHovered();
            var draw = ImGui.GetWindowDrawList();
            for (int x = 0; x < columns; x++)
                for (int y = 0; y < rows; y++)
                {
                    int id = grid.TileId((int)((x + .5) * grid.Columns / columns), (int)((y + .5) * grid.Rows / rows));
                    draw.AddRectFilled(start + new NVec2(x * size.X / columns, y * size.Y / rows),
                        start + new NVec2((x + 1) * size.X / columns, (y + 1) * size.Y / rows), ImGui.ColorConvertFloat4ToU32(Colour(world, id)));
                }
            if (lens == Lens.Todo) DrawIssueIcons(draw, grid, start, size);
            if (hovered)
            {
                var position = ImGui.GetMousePos() - start;
                int column = Math.Clamp((int)(position.X / size.X * grid.Columns), 0, grid.Columns - 1);
                int row = Math.Clamp((int)(position.Y / size.Y * grid.Rows), 0, grid.Rows - 1);
                int id = grid.TileId(column, row);
                if (ImGui.IsMouseClicked(ImGuiMouseButton.Left)) selected = id;
                if (ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left)) request = new(ManagementRequestKind.Focus, id);
                Tooltip(world, id, column, row);
            }
            if ((uint)selected < (uint)grid.Count)
            {
                int column = selected / grid.Rows, row = selected % grid.Rows;
                var p = start + new NVec2(column * size.X / grid.Columns, row * size.Y / grid.Rows);
                var q = p + new NVec2(Math.Max(3, size.X / grid.Columns), Math.Max(3, size.Y / grid.Rows));
                draw.AddRect(p - NVec2.One, q + NVec2.One, 0xff000000, 0, ImDrawFlags.None, 3);
                draw.AddRect(p, q, 0xffffffff, 0, ImDrawFlags.None, 1.5f);
            }
        }

        // TTD-style markers: an icon only where something needs doing. Neighbouring markers are
        // thinned on dense maps so the raster stays readable.
        private void DrawIssueIcons(ImDrawListPtr draw, RasterGrid grid, NVec2 start, NVec2 size)
        {
            float cell = size.X / grid.Columns;
            int stride = Math.Max(1, (int)MathF.Ceiling(12 / Math.Max(1, cell)));
            float icon = Math.Clamp(cell * stride * 0.9f, 9, 16);
            for (int column = 0; column < grid.Columns; column += stride)
                for (int row = 0; row < grid.Rows; row += stride)
                {
                    // Show the most severe issue in the block.
                    int best = -1;
                    for (int dx = 0; dx < stride && column + dx < grid.Columns; dx++)
                        for (int dy = 0; dy < stride && row + dy < grid.Rows; dy++)
                        {
                            int id = grid.TileId(column + dx, row + dy);
                            if (survey[id].Issue != ManagementIssue.None && (best < 0 || survey[id].Severity > survey[best].Severity)) best = id;
                        }
                    if (best < 0 || survey[best].Issue == ManagementIssue.Regenerate && stride > 1) continue;
                    var centre = start + new NVec2((column + stride * 0.5f) * cell, (row + stride * 0.5f) * cell);
                    var colour = IssueColor(survey[best].Issue);
                    // Urgent problems stand out; opportunities (a mature stand) stay small.
                    float scale = survey[best].Issue == ManagementIssue.Harvestable ? 0.7f : 0.8f + 0.1f * survey[best].Severity;
                    float marker = icon * scale;
                    draw.AddCircleFilled(centre, marker * 0.55f, ImGui.ColorConvertFloat4ToU32(colour with { W = 0.92f }));
                    GameIcons.Draw(draw, IssueIcon(survey[best].Issue), centre - new NVec2(marker * 0.4f), marker * 0.8f, 0xffffffff);
                }
        }

        private NVec4 Colour(GameWorld world, int id)
        {
            var s = survey[id];
            switch (lens)
            {
                case Lens.Water:
                    return layer switch
                    {
                        0 => Ramp(WaterRamp, s.WaterAvailability * 0.66f + s.Waterlogging * 0.34f),
                        1 => Ramp(WaterRamp, 0.5f * (1 - s.Drought)),
                        _ => Ramp(WaterRamp, 0.5f + 0.5f * s.Waterlogging)
                    };
                case Lens.Soil:
                    var profile = world.Soils.Profile(id);
                    return layer switch
                    {
                        0 => ImGui.ColorConvertU32ToFloat4(profile.Color),
                        1 => Ramp(SequentialRamp, profile.Properties.Fertility),
                        _ => Ramp(SequentialRamp, (float)Math.Clamp((profile.Properties.FieldCapacity - profile.Properties.WiltingPoint) / 200, 0, 1))
                    };
                case Lens.Health:
                    if (!s.IsForest) return s.Forestable ? Land : NoData;
                    return layer switch
                    {
                        0 => Ramp(VitalityRamp, s.Health),
                        1 => LimitColor(s.Limit),
                        _ => Ramp(VitalityRamp, 1 - Math.Clamp(s.DeadShare * 4, 0, 1))
                    };
                case Lens.Stand:
                    if (!s.IsForest) return s.Forestable ? Land : NoData;
                    return layer switch
                    {
                        0 => SpeciesColor(s.Species),
                        1 => Ramp(SequentialRamp, Math.Clamp(s.Maturity, 0, 1)),
                        2 => Ramp(SequentialRamp, Math.Clamp(s.VolumeCubicMetres / 400, 0, 1)),
                        _ => Ramp(SequentialRamp, Math.Clamp(s.Stocking, 0, 1))
                    };
                default:
                    // Muted base so the issue markers carry the message.
                    if (!s.IsForest) return s.Forestable ? new(.86f, .85f, .80f, 1) : NoData;
                    var tint = s.Issue == ManagementIssue.None ? new NVec4(.70f, .80f, .66f, 1) : IssueColor(s.Issue);
                    return NVec4.Lerp(new(.78f, .79f, .74f, 1), tint, s.Issue == ManagementIssue.None ? 1 : 0.35f + 0.15f * s.Severity);
            }
        }

        private void DrawLegend(SoilLandscape soils)
        {
            string text = lens switch
            {
                Lens.Water => layer switch
                {
                    0 => "Barna: száraz · homok: optimális · kék: telített",
                    1 => "Kék: nincs aszály · barna: erős aszálystressz",
                    _ => "Homok: nincs pangóvíz · sötétkék: tartósan telített"
                },
                Lens.Soil when layer == 0 => null,
                Lens.Soil => "Világos: alacsony · sötétzöld: magas",
                Lens.Health => layer switch
                {
                    0 => "Bíbor: pusztuló · szürke: stresszes · zöld: vitális",
                    1 => null,
                    _ => "Zöld: nincs elhalt fa · bíbor: 25% fölött"
                },
                Lens.Stand when layer == 0 => null,
                Lens.Stand => layer switch
                {
                    1 => "Világos: fiatal · sötét: vágásérett",
                    2 => "Világos: 0 · sötét: 400 m³ csempénként",
                    _ => "Világos: ritka · sötét: teljesen zárt"
                },
                _ => "Az ikon a csempe legsürgősebb teendőjét mutatja"
            };
            if (text != null) { ImGui.TextDisabled(text); return; }
            if (lens == Lens.Soil)
                foreach (var profile in soils.Definition.Catalog.Profiles) Swatch(ImGui.ColorConvertU32ToFloat4(profile.Color), profile.Name);
            else if (lens == Lens.Health)
                foreach (var limit in new[] { ForestLimit.None, ForestLimit.Light, ForestLimit.Water, ForestLimit.Space })
                    Swatch(LimitColor(limit), LimitName(limit));
            else
            {
                var seen = new System.Collections.Generic.HashSet<ForestSpecies>();
                foreach (var s in survey) if (s.IsForest) seen.Add(s.Species);
                foreach (var species in seen) Swatch(SpeciesColor(species), ForestSpeciesTraits.For(species).Name);
            }

            static void Swatch(NVec4 colour, string name)
            {
                ImGui.ColorButton("##legend-" + name, colour, ImGuiColorEditFlags.NoTooltip | ImGuiColorEditFlags.NoDragDrop, new NVec2(12, 12));
                ImGui.SameLine(); ImGui.TextUnformatted(name);
            }
        }

        private void Tooltip(GameWorld world, int id, int column, int row)
        {
            var s = survey[id];
            ImGui.BeginTooltip();
            ImGui.Text($"Csempe {column}/{row} · {world.Soils.Profile(id).Name}");
            if (s.IsForest)
                ImGui.Text($"{ForestSpeciesTraits.For(s.Species).Name} · {s.AgeYears:0} év · {s.Trees} fa · {s.VolumeCubicMetres:0} m³");
            ImGui.Text($"Elérhető víz: {s.WaterAvailability:P0} · aszály: {s.Drought:P0}");
            if (s.IsForest) ImGui.Text($"Egészség: {s.Health:P0} · korlát: {LimitName(s.Limit)}");
            if (s.Issue != ManagementIssue.None) ImGui.TextColored(IssueColor(s.Issue), IssueText(s.Issue));
            ImGui.TextDisabled("Kattintás: kijelölés · dupla kattintás: odaugrás");
            ImGui.EndTooltip();
        }

        private void DrawPanel(GameWorld world, SoilLandscape soils, ref ManagementRequest request)
        {
            if ((uint)selected >= (uint)survey.Length)
            {
                ImGui.TextDisabled("Válassz egy csempét a térképen.");
                return;
            }
            var s = survey[selected];
            int column = selected / soils.Grid.Rows, row = selected % soils.Grid.Rows;
            ImGui.SeparatorText($"Erdőrészlet {column}/{row}");
            if (s.IsForest)
            {
                ImGui.TextColored(HudTheme.AmberAccent, $"{ForestSpeciesTraits.For(s.Species).Name} · {s.AgeYears:0} év");
                HudTheme.Meter("Egészség", s.Health, $"{s.Health:P0}", s.Health > 0.6f ? null : HudTheme.Bad);
            }
            else ImGui.TextDisabled(s.Forestable ? "Erdősíthető, jelenleg üres" : "Nem erdősíthető (víz, út, épület)");
            HudTheme.Meter("Elérhető víz", s.WaterAvailability, $"{s.WaterAvailability:P0}", new NVec4(0.36f, 0.62f, 0.86f, 1f));
            if (s.IsForest)
            {
                HudTheme.Meter("Sűrűség", Math.Clamp(s.Stocking, 0, 1), $"{s.Trees} fa", null);
                HudTheme.KeyValue("Fakészlet", $"{s.VolumeCubicMetres:0} m³");
                HudTheme.KeyValue("Érettség", $"{Math.Min(s.Maturity, 1):P0}");
                HudTheme.KeyValue("Limitáló tényező", LimitName(s.Limit));
                HudTheme.KeyValue("Stresszes / elhalt", $"{s.StressedShare:P0} / {s.DeadShare:P0}");
            }
            HudTheme.KeyValue("Talaj", $"{soils.Profile(selected).Name} · {s.Fertility:P0}");

            ImGui.SeparatorText("Diagnózis");
            if (s.Issue == ManagementIssue.None) ImGui.TextColored(HudTheme.Good, "Nincs teendő.");
            else
            {
                ImGui.TextColored(IssueColor(s.Issue), $"{IssueText(s.Issue)} ({new string('!', s.Severity)})");
                ImGui.TextWrapped(IssueAdvice(s.Issue));
            }

            var recommended = world.RecommendSpecies(selected);
            if (s.Forestable && recommended.Length > 0)
            {
                ImGui.SeparatorText("Ide illő fafajok");
                foreach (var (species, suitability) in recommended)
                    HudTheme.Meter(ForestSpeciesTraits.For(species).Name, suitability, $"{suitability:P0}", HudTheme.MossBright);
            }

            ImGui.Spacing();
            if (ImGui.Button("Odaugrás")) request = new(ManagementRequestKind.Focus, selected);
            if (s.Forestable && recommended.Length > 0 && (!s.IsForest || s.Issue is ManagementIssue.Drought or ManagementIssue.Waterlogging or ManagementIssue.Dieback))
            {
                ImGui.SameLine();
                if (ImGui.Button($"Ültetés: {ForestSpeciesTraits.For(recommended[0].Species).Name}"))
                    request = new(ManagementRequestKind.Plant, selected, recommended[0].Species);
            }
            if (s.IsForest && s.Issue is ManagementIssue.Harvestable or ManagementIssue.Dieback or ManagementIssue.Overstocked)
            {
                ImGui.SameLine();
                if (ImGui.Button("Kitermelés")) request = new(ManagementRequestKind.Harvest, selected);
            }
        }

        private static NVec4 Ramp(NVec4[] stops, float t)
        {
            t = Math.Clamp(t, 0, 1) * (stops.Length - 1);
            int i = Math.Min((int)t, stops.Length - 2);
            return NVec4.Lerp(stops[i], stops[i + 1], t - i);
        }

        private static NVec4 SpeciesColor(ForestSpecies species)
        {
            var c = ForestSpeciesTraits.For(species).Crown;
            // Lift the dark crown colours so they read on the map.
            return new(Math.Min(1, c.R / 255f * 1.5f), Math.Min(1, c.G / 255f * 1.5f), Math.Min(1, c.B / 255f * 1.5f), 1);
        }

        private static NVec4 LimitColor(ForestLimit limit) => limit switch
        {
            ForestLimit.Light => new(.93f, .76f, .30f, 1),
            ForestLimit.Water => new(.30f, .55f, .86f, 1),
            ForestLimit.Space => new(.56f, .40f, .74f, 1),
            _ => new(.45f, .68f, .42f, 1)
        };

        private static string LimitName(ForestLimit limit) => limit switch
        {
            ForestLimit.Light => "fény", ForestLimit.Water => "víz", ForestLimit.Space => "hely", _ => "nincs"
        };

        internal static NVec4 IssueColor(ManagementIssue issue) => issue switch
        {
            ManagementIssue.Dieback => new(.64f, .18f, .18f, 1),
            ManagementIssue.Waterlogging => new(.22f, .54f, .87f, 1),
            ManagementIssue.Drought => new(.73f, .46f, .09f, 1),
            ManagementIssue.Overstocked => new(.33f, .29f, .72f, 1),
            ManagementIssue.Harvestable => new(.62f, .58f, .50f, 1),
            ManagementIssue.Regenerate => new(.11f, .62f, .46f, 1),
            _ => HudTheme.Muted
        };

        private static GameIcon IssueIcon(ManagementIssue issue) => issue switch
        {
            ManagementIssue.Dieback => GameIcon.Lightning,
            ManagementIssue.Waterlogging => GameIcon.Rain,
            ManagementIssue.Drought => GameIcon.Sun,
            ManagementIssue.Overstocked => GameIcon.Forestry,
            ManagementIssue.Harvestable => GameIcon.Harvest,
            _ => GameIcon.Plant
        };

        private static string IssueShort(ManagementIssue issue) => issue switch
        {
            ManagementIssue.Dieback => "Pusztulás", ManagementIssue.Waterlogging => "Pangóvíz", ManagementIssue.Drought => "Aszály",
            ManagementIssue.Overstocked => "Túlsűrű", ManagementIssue.Harvestable => "Vágásérett", ManagementIssue.Regenerate => "Felújítandó", _ => ""
        };

        private static string IssueText(ManagementIssue issue) => issue switch
        {
            ManagementIssue.Dieback => "Sok elhalt vagy legyengült fa",
            ManagementIssue.Waterlogging => "Pangóvíz a gyökérzónában",
            ManagementIssue.Drought => "Aszálystressz, a fák víz-limitáltak",
            ManagementIssue.Overstocked => "Túlsűrű, tér-limitált állomány",
            ManagementIssue.Harvestable => "Vágásérett állomány",
            ManagementIssue.Regenerate => "Üres, erdősíthető terület",
            _ => ""
        };

        private static string IssueAdvice(ManagementIssue issue) => issue switch
        {
            ManagementIssue.Dieback => "Egészségügyi termelés, majd a termőhelyhez illő fafaj ültetése.",
            ManagementIssue.Waterlogging => "Nedvességtűrő fafaj (nyír, kőris); később vízelvezető árok.",
            ManagementIssue.Drought => "Gyérítés csökkenti a párologtatást; szárazságtűrő fafaj (tölgy, erdeifenyő).",
            ManagementIssue.Overstocked => "Gyérítés: a megmaradó fák több fényt, vizet és helyet kapnak.",
            ManagementIssue.Harvestable => "Véghasználat vagy fokozatos felújító vágás; az értéknövekedés lassul.",
            ManagementIssue.Regenerate => "Csemeteültetés a talajhoz és vízhez illő fafajjal.",
            _ => ""
        };
    }
}
