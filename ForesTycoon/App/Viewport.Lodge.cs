using System;
using System.Collections.Generic;
using ImGuiNET;
using NVec2 = System.Numerics.Vector2;
using NVec4 = System.Numerics.Vector4;

namespace ForesTycoon
{
    /// <summary>
    /// "Erdei csend" HUD (docs/ui-ux-design.md): the diorama stays the subject and the interface lives at
    /// its edges in calm forest-lodge panels — estate card with season wheel (top left), ledger (top
    /// right), forest journal (left), tempo (bottom left), the five verbs Observe · Tend · Produce · Build ·
    /// Transport as a dock (bottom centre), the estate minimap (bottom right) and a quiet season summary.
    /// Roads belong to Build: they serve tending, harvesting and transport alike.
    /// </summary>
    sealed partial class Viewport
    {
        private enum Verb { None, Observe, Tend, Produce, Build, Transport }

        private Verb verb;
        private bool journalOpen = true, seasonCards = true;
        private const float Edge = 16f;
        // Last frame's inner edges of the bottom corners, so the verb dock centres in the space between them.
        private float tempoRight = 340f, minimapLeft = float.MaxValue;
        private const int JournalLimit = 40;

        private readonly record struct JournalEntry(string Text, NVec4 Color, double Born, string When);
        private readonly List<JournalEntry> journal = new();

        private int lastSeason = -1;
        private bool showSeasonCard, resumeAfterSeasonCard;
        private (float Stock, float Delivered, float Health) seasonStart;
        private (float Stock, float Delivered, float Health, int Season, int Year) seasonReport;

        private static readonly NVec4[] SeasonColours =
        {
            new(0.553f, 0.706f, 0.353f, 1), // spring #8DB45A
            new(0.247f, 0.478f, 0.227f, 1), // summer #3F7A3A
            new(0.780f, 0.471f, 0.184f, 1), // autumn #C7782F
            new(0.498f, 0.616f, 0.690f, 1)  // winter #7F9DB0
        };
        private static readonly string[] SeasonNames = { "tavasz", "nyár", "ősz", "tél" };
        private static readonly string[] MonthNames = { "márc.", "ápr.", "máj.", "jún.", "júl.", "aug.", "szept.", "okt.", "nov.", "dec.", "jan.", "febr." };

        /// <summary>The calendar date: the forest year starts with spring (March); twelve 30-day months.</summary>
        private string DateText()
        {
            var (year, _, _, fraction) = Calendar();
            float months = fraction * 12;
            int month = Math.Clamp((int)months, 0, 11), day = 1 + Math.Clamp((int)((months - month) * 30), 0, 29);
            return $"{year}. év {MonthNames[month]} {day}.";
        }
        private static readonly string[] SeasonArrived = { "a tavasz", "a nyár", "az ősz", "a tél" };
        private static readonly string[] SeasonAdverb = { "Tavasszal", "Nyáron", "Ősszel", "Télen" };

        private const ImGuiWindowFlags PanelFlags = ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoMove
            | ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoFocusOnAppearing | ImGuiWindowFlags.NoNav
            | ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoBringToFrontOnFocus;

        private static void BeginPanel(string id, NVec2 position, NVec2 pivot, float width = 0)
        {
            ImGui.SetNextWindowPos(position, ImGuiCond.Always, pivot);
            if (width > 0) ImGui.SetNextWindowSize(new NVec2(width, 0), ImGuiCond.Always);
            ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 14f);
            ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new NVec2(14, 12));
            ImGui.Begin(id, width > 0 ? PanelFlags & ~ImGuiWindowFlags.AlwaysAutoResize : PanelFlags);
        }

        private static void EndPanel()
        {
            ImGui.End();
            ImGui.PopStyleVar(2);
        }

        /// <summary>Serif "voice" for names and seasons; falls back to the UI font.</summary>
        private static void TitleText(string text, bool large = false, NVec4? colour = null)
        {
            bool pushed = ImGuiController.HasTitleFonts;
            if (pushed) ImGui.PushFont(large ? ImGuiController.LargeTitleFont : ImGuiController.TitleFont);
            if (colour.HasValue) ImGui.TextColored(colour.Value, text); else ImGui.TextUnformatted(text);
            if (pushed) ImGui.PopFont();
        }

        private (int Year, int Season, float SeasonFraction, float YearFraction) Calendar()
        {
            var environment = world.Environment;
            if (environment == null) return (1, 0, 0, 0);
            double years = environment.Time / environment.ForestYearSeconds;
            float fraction = (float)(years - Math.Floor(years));
            int season = Math.Clamp((int)(fraction * 4), 0, 3);
            return (1 + (int)years, season, fraction * 4 - season, fraction);
        }

        private string CalendarText()
        {
            var (year, season, part, _) = Calendar();
            string phase = part < 0.34f ? "eleje" : part < 0.67f ? "közepe" : "vége";
            return $"{year}. erdőév · {SeasonNames[season]} {phase}";
        }

        // ── Estate card ──────────────────────────────────────────────────────
        private void DrawEstateCard()
        {
            BeginPanel("##estate", new NVec2(Edge, Edge), NVec2.Zero, 340);
            if (HudTheme.IconButton("game", GameIcon.GameMenu, 30, ImGui.IsPopupOpen("game-menu"), "Játék menü",
                    "Esc", "Új térkép, mentés, betöltés, beállítások és kilépés."))
                ImGui.OpenPopup("game-menu");
            DrawGameMenuPopup();
            ImGui.SameLine(0, 10);
            ImGui.SetCursorPosY(ImGui.GetCursorPosY() + 2);
            TitleText("Erdőbirtok");

            var (_, season, _, yearFraction) = Calendar();
            var draw = ImGui.GetWindowDrawList();
            var origin = ImGui.GetCursorScreenPos();
            const float Wheel = 50f;
            var centre = origin + new NVec2(Wheel / 2, Wheel / 2 + 4);
            for (int s = 0; s < 4; s++)
            {
                float a0 = -MathF.PI / 2 + s * MathF.PI / 2 + 0.06f, a1 = a0 + MathF.PI / 2 - 0.12f;
                draw.PathArcTo(centre, Wheel / 2 - 5, a0, a1, 12);
                draw.PathStroke(ImGui.ColorConvertFloat4ToU32(s == season ? SeasonColours[s] : SeasonColours[s] with { W = 0.35f }),
                    ImDrawFlags.None, 7f);
            }
            float marker = -MathF.PI / 2 + yearFraction * MathF.Tau;
            var dot = centre + new NVec2(MathF.Cos(marker), MathF.Sin(marker)) * (Wheel / 2 - 5);
            draw.AddCircleFilled(dot, 5.5f, ImGui.ColorConvertFloat4ToU32(HudTheme.Panel with { W = 1 }));
            draw.AddCircle(dot, 5.5f, ImGui.ColorConvertFloat4ToU32(HudTheme.AmberAccent), 0, 2f);
            ImGui.Dummy(new NVec2(Wheel, Wheel + 8));
            ImGui.SameLine(0, 12);
            ImGui.BeginGroup();
            ImGui.SetCursorPosY(ImGui.GetCursorPosY() + 6);
            ImGui.TextUnformatted(CalendarText());
            var environment = world.Environment;
            if (environment != null)
                HudTheme.IconText(WeatherIcon(environment.Preset),
                    $"{WeatherName(environment.Preset)} · {environment.Temperature:0} °C", HudTheme.Muted);
            ImGui.EndGroup();
            toolbarBottom = ImGui.GetWindowPos().Y + ImGui.GetWindowSize().Y;
            EndPanel();
        }

        // ── Ledger and window shortcuts ──────────────────────────────────────
        private void DrawLedger()
        {
            var forest = world.ForestStatistics;
            BeginPanel("##ledger", new NVec2(Width - Edge, Edge), new NVec2(1, 0));
            Chip(GameIcon.Timber, "Élőfakészlet", $"{forest.TotalBiomass * 100:N0} m³");
            ImGui.SameLine(0, 22);
            Chip(GameIcon.Truck, "Leszállítva", $"{world.DeliveredTimber:N0} m³");
            ImGui.SameLine(0, 22);
            Chip(GameIcon.Timber, "Egyenleg", $"{world.Balance:N0} eFt");
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip($"Bevétel a malmokból: {world.Income:N0} eFt\nÉpítés: {world.Expenses:N0} eFt\nÜzemanyag, javítás: {world.Logistics?.RunningCosts ?? 0:N0} eFt");
            ImGui.SameLine(0, 22);
            HealthRing(forest.StandCount > 0 ? forest.AverageHealth : 0);
            ImGui.Spacing();
            const float Small = 28f;
            WindowToggle("vehicles", GameIcon.Vehicles, ref showVehicles, Small, "Járművek", "V", "Teherautók állapota és rakománya.");
            ImGui.SameLine();
            WindowToggle("forestry", GameIcon.Forestry, ref showForestry, Small, "Erdészet", "F", "Erdőállomány, kitermelés, fűrészmalmok és faanyagmérleg.");
            ImGui.SameLine();
            WindowToggle("environment", GameIcon.Environment, ref showEnvironment, Small, "Környezet", "K", "Időjárás, talaj, gyökérzónavíz csempénként.");
            ImGui.SameLine();
            WindowToggle("graphics", GameIcon.Graphics, ref showGraphics, Small, "Grafika", "G", "Diorama hatások, fény, árnyék és minőség.");
            ImGui.SameLine();
            if (HudTheme.IconButton("camera", GameIcon.Camera, Small, false, "Kamera alaphelyzet", "Home", "Visszaállítja a nézetet a teljes birtokra."))
                ResetCamera();
            ImGui.SameLine();
            WindowToggle("help", GameIcon.Help, ref showHelp, Small, "Súgó", "F1", "Irányítás, gyorsbillentyűk és az első lépések.");
            EndPanel();

            static void Chip(GameIcon icon, string label, string value)
            {
                ImGui.BeginGroup();
                var p = ImGui.GetCursorScreenPos();
                GameIcons.Draw(ImGui.GetWindowDrawList(), icon, p + new NVec2(0, 4), 26, GameIcons.Color(HudTheme.Parchment));
                ImGui.Dummy(new NVec2(30, 34));
                ImGui.SameLine(0, 6);
                ImGui.BeginGroup();
                ImGui.TextColored(HudTheme.Muted, label);
                ImGui.TextUnformatted(value);
                ImGui.EndGroup();
                ImGui.EndGroup();
            }

            static void HealthRing(float health)
            {
                ImGui.BeginGroup();
                var p = ImGui.GetCursorScreenPos() + new NVec2(17, 17);
                var dl = ImGui.GetWindowDrawList();
                dl.AddCircle(p, 13, ImGui.ColorConvertFloat4ToU32(new NVec4(0.17f, 0.21f, 0.18f, 1)), 32, 5f);
                dl.PathArcTo(p, 13, -MathF.PI / 2, -MathF.PI / 2 + MathF.Tau * Math.Clamp(health, 0, 1), 32);
                dl.PathStroke(ImGui.ColorConvertFloat4ToU32(health > 0.6f ? HudTheme.MossBright : health > 0.4f ? HudTheme.AmberAccent : HudTheme.Bad),
                    ImDrawFlags.None, 5f);
                ImGui.Dummy(new NVec2(34, 34));
                ImGui.SameLine(0, 6);
                ImGui.BeginGroup();
                ImGui.TextColored(HudTheme.Muted, "Erdőállapot");
                ImGui.TextUnformatted($"{health:P0}");
                ImGui.EndGroup();
                ImGui.EndGroup();
            }
        }

        // ── Forest journal ───────────────────────────────────────────────────
        private void AddJournal(string text, NVec4 colour)
        {
            journal.Insert(0, new(text, colour, frameClock.TotalTimeSeconds, world.Environment != null ? CalendarText() : ""));
            if (journal.Count > JournalLimit) journal.RemoveAt(journal.Count - 1);
        }

        private void DrawJournal()
        {
            BeginPanel("##journal", new NVec2(Edge, toolbarBottom + 10), NVec2.Zero, 340);
            var start = ImGui.GetCursorPos();
            TitleText("Erdőnapló");
            ImGui.SameLine(ImGui.GetWindowWidth() - 14 - ImGui.GetFrameHeight());
            if (ImGui.ArrowButton("##journal-toggle", journalOpen ? ImGuiDir.Up : ImGuiDir.Down)) journalOpen = !journalOpen;
            if (!journalOpen)
            {
                EndPanel();
                return;
            }
            if (journal.Count == 0) ImGui.TextColored(HudTheme.Muted, "Csend van az erdőben.");
            double now = frameClock.TotalTimeSeconds;
            var draw = ImGui.GetWindowDrawList();
            for (int i = 0; i < Math.Min(4, journal.Count); i++)
            {
                var entry = journal[i];
                var p = ImGui.GetCursorScreenPos();
                // A new entry briefly glows like a card, then settles into the journal.
                float glow = (float)Math.Clamp(1 - (now - entry.Born) / ToastSeconds, 0, 1);
                if (glow > 0)
                    draw.AddRectFilled(p - new NVec2(6, 3), p + new NVec2(ImGui.GetContentRegionAvail().X + 6, ImGui.GetTextLineHeight() * 2.4f),
                        ImGui.ColorConvertFloat4ToU32(HudTheme.AmberAccent with { W = 0.12f * glow }), 8f);
                draw.AddCircleFilled(p + new NVec2(4, ImGui.GetTextLineHeight() * 0.55f), 4f, ImGui.ColorConvertFloat4ToU32(entry.Color));
                ImGui.SetCursorPosX(ImGui.GetCursorPosX() + 16);
                ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + 290);
                ImGui.TextUnformatted(entry.Text);
                ImGui.PopTextWrapPos();
                if (entry.When.Length > 0)
                {
                    ImGui.SetCursorPosX(ImGui.GetCursorPosX() + 16);
                    ImGui.TextColored(HudTheme.Muted, entry.When);
                }
                ImGui.Spacing();
            }
            _ = start;
            EndPanel();
        }

        // ── Tempo ────────────────────────────────────────────────────────────
        private void DrawTimeControl()
        {
            BeginPanel("##tempo", new NVec2(Edge, Height - Edge), new NVec2(0, 1));
            const float Size = 36f;
            if (HudTheme.IconButton("pause", GameIcon.Pause, Size, simulationClock.IsPaused, "Szünet", "Space",
                    "Megállítja az időt. A kamera és a tervezés működik."))
                simulationClock.IsPaused = !simulationClock.IsPaused;
            ImGui.SameLine();
            SpeedButton("speed1", GameIcon.Play, 1.0, Size, "Normál tempó");
            ImGui.SameLine();
            SpeedButton("speed2", GameIcon.Fast, 2.0, Size, "Gyorsabb tempó");
            ImGui.SameLine();
            DrawFastSpeedButton(Size);
            ImGui.SameLine(0, 14);
            var (_, season, part, _) = Calendar();
            ImGui.BeginGroup();
            ImGui.TextColored(HudTheme.Muted, (simulationClock.IsPaused ? "Szünet" : $"{simulationClock.Speed:0}×") + $" · {DateText()}");
            var p = ImGui.GetCursorScreenPos();
            var dl = ImGui.GetWindowDrawList();
            dl.AddRectFilled(p + new NVec2(0, 4), p + new NVec2(110, 9), ImGui.ColorConvertFloat4ToU32(new NVec4(0.17f, 0.21f, 0.18f, 1)), 3f);
            dl.AddRectFilled(p + new NVec2(0, 4), p + new NVec2(110 * part, 9), ImGui.ColorConvertFloat4ToU32(SeasonColours[season]), 3f);
            ImGui.Dummy(new NVec2(110, 12));
            ImGui.EndGroup();
            tempoRight = ImGui.GetWindowPos().X + ImGui.GetWindowSize().X;
            EndPanel();
        }

        // ── Verb dock ────────────────────────────────────────────────────────
        private void SyncVerbWithTool()
        {
            var tool = interaction.ActiveTool;
            verb = tool switch
            {
                TerrainEditTool.PlantForest => Verb.Tend,
                TerrainEditTool.HarvestForest or TerrainEditTool.SkidTrail or TerrainEditTool.SkidTrailRemove
                    or TerrainEditTool.PlaceStack or TerrainEditTool.RemoveStack => Verb.Produce,
                TerrainEditTool.Road or TerrainEditTool.RoadRemove or TerrainEditTool.RoadRepair or TerrainEditTool.PlaceSawmill or TerrainEditTool.PlaceDepot
                    or TerrainEditTool.Raise or TerrainEditTool.Lower => Verb.Build,
                // Observe and Transport work with the inspect tool and keep their own state.
                _ => verb == Verb.Transport ? Verb.Transport : verb == Verb.Observe || showManagement ? Verb.Observe : Verb.None
            };
        }

        private void ChooseVerb(Verb chosen)
        {
            if (verb == chosen)
            {
                if (chosen == Verb.Observe) showManagement = false;
                if (chosen == Verb.Transport) showVehicles = false;
                verb = Verb.None;
                SelectTool(TerrainEditTool.Inspect);
                return;
            }
            verb = chosen;
            switch (chosen)
            {
                case Verb.Observe: SelectTool(TerrainEditTool.Inspect); showManagement = true; break;
                case Verb.Tend: SelectTool(TerrainEditTool.PlantForest); break;
                case Verb.Produce: SelectTool(TerrainEditTool.HarvestForest); break;
                case Verb.Build: SelectTool(TerrainEditTool.Road); break;
                case Verb.Transport: SelectTool(TerrainEditTool.Inspect); showVehicles = true; break;
            }
        }

        private void DrawVerbDock()
        {
            SyncVerbWithTool();
            float left = tempoRight + 12, right = Math.Min(Width - Edge, minimapLeft) - 12;
            float centre = right > left ? (left + right) * 0.5f : Width * 0.5f;
            BeginPanel("##verbs", new NVec2(centre, Height - Edge), new NVec2(0.5f, 1));
            VerbButton(Verb.Observe, GameIcon.Inspect, "Megfigyel", "lencsék · Q");
            ImGui.SameLine();
            VerbButton(Verb.Tend, GameIcon.Plant, "Gondoz", "ültetés · W");
            ImGui.SameLine();
            VerbButton(Verb.Produce, GameIcon.Harvest, "Termel", "kitermelés · E");
            ImGui.SameLine();
            VerbButton(Verb.Build, GameIcon.Road, "Épít", "út, malom, terep · R");
            ImGui.SameLine();
            VerbButton(Verb.Transport, GameIcon.Truck, "Szállít", "rönkszállítók · T");
            float dockTop = ImGui.GetWindowPos().Y;
            dockCentre = ImGui.GetWindowPos().X + ImGui.GetWindowSize().X * 0.5f;
            EndPanel();
            if (verb != Verb.None) DrawVerbTools(dockTop);
        }

        private float dockCentre;

        private void VerbButton(Verb which, GameIcon icon, string label, string hint)
        {
            bool active = verb == which;
            var size = new NVec2(Width < 1500 ? 124 : 160, 52);
            var p = ImGui.GetCursorScreenPos();
            bool clicked = ImGui.InvisibleButton("verb" + (int)which, size);
            bool hovered = ImGui.IsItemHovered();
            var dl = ImGui.GetWindowDrawList();
            var fill = active ? new NVec4(0.204f, 0.271f, 0.176f, 1f) : hovered ? new NVec4(0.161f, 0.204f, 0.165f, 1f) : new NVec4(0.118f, 0.149f, 0.122f, 1f);
            dl.AddRectFilled(p, p + size, ImGui.ColorConvertFloat4ToU32(fill), 10f);
            if (active) dl.AddRect(p, p + size, ImGui.ColorConvertFloat4ToU32(HudTheme.AmberAccent), 10f, ImDrawFlags.None, 2f);
            GameIcons.Draw(dl, icon, p + new NVec2(12, 12), 28, GameIcons.Color(HudTheme.Parchment));
            dl.AddText(p + new NVec2(50, 8), ImGui.ColorConvertFloat4ToU32(HudTheme.Parchment), label);
            // Narrow screens keep only the shortcut letter of the hint.
            dl.AddText(p + new NVec2(50, 28), ImGui.ColorConvertFloat4ToU32(HudTheme.Muted), Width < 1500 ? hint[^1..] : hint);
            if (clicked) ChooseVerb(which);
        }

        // The verb's tools and the active tool's options, in one strip just above the dock.
        private void DrawVerbTools(float dockTop)
        {
            BeginPanel("##verb-tools", new NVec2(dockCentre, dockTop - 8), new NVec2(0.5f, 1));
            const float Size = 36f;
            switch (verb)
            {
                case Verb.Observe:
                    if (HudTheme.LabeledIconButton("Térképasztal és lencsék", GameIcon.Environment, showManagement, Size,
                            "Víz, talaj, egészség, állomány és teendők térképe (M).")) showManagement = !showManagement;
                    ImGui.SameLine();
                    if (HudTheme.LabeledIconButton("Vizsgálat", GameIcon.Inspect, interaction.ActiveTool == TerrainEditTool.Inspect, Size,
                            "Egérrel mutatott csempe adatai.")) SelectTool(TerrainEditTool.Inspect);
                    break;
                case Verb.Tend:
                    ToolIcon(GameIcon.Plant, TerrainEditTool.PlantForest, Size, "Ültetés", "6", "Csemeték ültetése húzással.");
                    break;
                case Verb.Produce:
                    ToolIcon(GameIcon.Harvest, TerrainEditTool.HarvestForest, Size, "Kitermelés", "7", "Húzással jelöld ki a vágásterületet.");
                    ImGui.SameLine();
                    ToolIcon(GameIcon.SkidTrail, TerrainEditTool.SkidTrail, Size, "Közelítő nyom", "0",
                        "Húzással jelöld ki a nyomot az úttól a vágásig: ezen jár a processzor és a forwarder. Csak keréknyom: használat nélkül benő.");
                    ImGui.SameLine();
                    ToolIcon(GameIcon.SkidTrailRemove, TerrainEditTool.SkidTrailRemove, Size, "Nyom törlése", "", "Húzással megszünteti a közelítő nyomot.");
                    ImGui.SameLine();
                    ToolIcon(GameIcon.Timber, TerrainEditTool.PlaceStack, Size, "Sarang", "",
                        "Sarang helye nyom vagy út mellett. A processzor a legközelebbihez hordja a fát: messze sok üzemanyag, kevés teljesítmény.");
                    ImGui.SameLine();
                    ToolIcon(GameIcon.Timber, TerrainEditTool.RemoveStack, Size, "Sarang törlése", "", "Üres sarang helyének törlése.");
                    break;
                case Verb.Build:
                    ToolIcon(GameIcon.Road, TerrainEditTool.Road, Size, "Útépítés", "4",
                        "Az erdei út minden munkát szolgál: ültetést, ápolást, kitermelést és szállítást.");
                    ImGui.SameLine();
                    ToolIcon(GameIcon.RoadRemove, TerrainEditTool.RoadRemove, Size, "Útbontás", "5", "Húzással eltávolítja az utat.");
                    ImGui.SameLine();
                    ToolIcon(GameIcon.RoadRepair, TerrainEditTool.RoadRepair, Size, "Útjavítás", "9",
                        "Húzással jelöld ki a kopott szakaszt: a hibás csempék újjá válnak. A makadám gyorsabban romlik.");
                    ImGui.SameLine();
                    ToolIcon(GameIcon.Sawmill, TerrainEditTool.PlaceSawmill, Size, "Fűrészmalom", "8", "2×2 sík, száraz csempére, út mellé.");
                    ImGui.SameLine();
                    ToolIcon(GameIcon.Depot, TerrainEditTool.PlaceDepot, Size, "Telephely", "",
                        "Gépudvar 2×2 csempén, út mellett. Innen indulnak a járművek, és ide térnek haza.");
                    ImGui.SameLine(0, 14);
                    HudTheme.GroupDivider(Size);
                    ImGui.SameLine();
                    ToolIcon(GameIcon.Raise, TerrainEditTool.Raise, Size, "Terep emelése", "2", "Kattintással emeli a terepet.");
                    ImGui.SameLine();
                    ToolIcon(GameIcon.Lower, TerrainEditTool.Lower, Size, "Terep süllyesztése", "3", "Kattintással süllyeszti a terepet.");
                    break;
                case Verb.Transport:
                    if (HudTheme.LabeledIconButton("Járművek", GameIcon.Vehicles, showVehicles, Size,
                            "A járműpark: küldd a gépeket és a rönkszállítót dolgozni, vagy hívd haza őket (V).")) showVehicles = !showVehicles;
                    if (world.Logistics != null)
                    {
                        ImGui.SameLine(0, 14);
                        ImGui.AlignTextToFramePadding();
                        ImGui.TextColored(HudTheme.Muted, world.Logistics.Status);
                    }
                    break;
            }
            if (verb is Verb.Tend or Verb.Produce or Verb.Build && interaction.ActiveTool != TerrainEditTool.Inspect)
            {
                ImGui.SameLine(0, 14);
                HudTheme.GroupDivider(Size);
                DrawToolOptions(interaction.ActiveTool);
            }
            EndPanel();
        }

        // ── Estate minimap ───────────────────────────────────────────────────
        private void DrawMinimap()
        {
            var grid = world.Soils?.Grid;
            if (grid == null) return;
            management.EnsureSurvey(world);
            if (management.Survey == null || management.Survey.Length != grid.Count) return;
            BeginPanel("##minimap", new NVec2(Width - Edge, Height - Edge), new NVec2(1, 1));
            ImGui.TextUnformatted("Birtoktérkép");
            ImGui.SameLine(ImGui.GetWindowWidth() - 14 - ImGui.CalcTextSize("M").X);
            ImGui.TextColored(HudTheme.Muted, "M");
            const float MapSize = 176f;
            int cells = Math.Min(48, Math.Max(grid.Columns, grid.Rows));
            var start = ImGui.GetCursorScreenPos();
            ImGui.InvisibleButton("##minimap-area", new NVec2(MapSize, MapSize));
            var dl = ImGui.GetWindowDrawList();
            float cell = MapSize / cells;
            for (int x = 0; x < cells; x++)
                for (int y = 0; y < cells; y++)
                {
                    int id = grid.TileId((int)((x + 0.5f) * grid.Columns / cells), (int)((y + 0.5f) * grid.Rows / cells));
                    dl.AddRectFilled(start + new NVec2(x * cell, y * cell), start + new NVec2((x + 1) * cell, (y + 1) * cell),
                        ImGui.ColorConvertFloat4ToU32(management.MiniColour(id)));
                }
            dl.AddRect(start, start + new NVec2(MapSize), ImGui.ColorConvertFloat4ToU32(new NVec4(0.36f, 0.50f, 0.27f, 0.5f)), 4f);
            minimapLeft = ImGui.GetWindowPos().X;
            if (ImGui.IsItemHovered())
            {
                var local = ImGui.GetMousePos() - start;
                int column = Math.Clamp((int)(local.X / MapSize * grid.Columns), 0, grid.Columns - 1);
                int row = Math.Clamp((int)(local.Y / MapSize * grid.Rows), 0, grid.Rows - 1);
                if (ImGui.IsMouseClicked(ImGuiMouseButton.Left) && world.TryGetTileCenter(grid.TileId(column, row), out var centre))
                {
                    FocusOn(centre, Math.Max(zoom, 30));
                    RequestFrame();
                }
                ImGui.SetTooltip("Kattintás: odarepülés");
            }
            EndPanel();
        }

        // ── Season summary ───────────────────────────────────────────────────
        private void TrackSeason()
        {
            if (world.Environment == null) return;
            var (year, season, _, _) = Calendar();
            int index = year * 4 + season;
            var forest = world.ForestStatistics;
            var now = (forest.TotalBiomass * 100, world.DeliveredTimber, forest.AverageHealth);
            if (lastSeason < 0) { lastSeason = index; seasonStart = now; return; }
            if (index == lastSeason) return;
            seasonReport = (now.Item1 - seasonStart.Stock, now.DeliveredTimber - seasonStart.Delivered, now.AverageHealth, season, year);
            seasonStart = now;
            lastSeason = index;
            AddJournal($"Beköszöntött {SeasonArrived[season]}.", SeasonColours[season]);
            // Scripted captures and smoke tests must never stop on a card.
            if (!seasonCards || captureDirectory != null || smokeTestFrameLimit.HasValue || simulationClock.Speed > 16) return;
            showSeasonCard = true;
            resumeAfterSeasonCard = !simulationClock.IsPaused;
            simulationClock.IsPaused = true;
        }

        private void DrawSeasonCard()
        {
            if (!showSeasonCard) return;
            var (stock, delivered, health, season, year) = seasonReport;
            ImGui.SetNextWindowPos(new NVec2(Width * 0.5f, Height * 0.45f), ImGuiCond.Always, new NVec2(0.5f, 0.5f));
            ImGui.SetNextWindowSize(new NVec2(520, 0), ImGuiCond.Always);
            ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 18f);
            ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new NVec2(28, 24));
            ImGui.PushStyleColor(ImGuiCol.WindowBg, HudTheme.Panel with { W = 0.97f });
            ImGui.Begin("##season-card", ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoSavedSettings);
            ImGui.TextColored(SeasonColours[season], $"{year}. ERDŐÉV");
            TitleText($"Beköszöntött {SeasonArrived[season]}", large: true);
            ImGui.Spacing();
            ImGui.TextColored(HudTheme.Muted, "Az elmúlt évszak");
            Stat("Élőfakészlet", $"{(stock >= 0 ? "+" : "")}{stock:N0} m³", stock >= 0 ? HudTheme.Good : HudTheme.Bad);
            Stat("Leszállított faanyag", $"{delivered:N0} m³", HudTheme.Parchment);
            Stat("Erdőállapot", $"{health:P0}", health > 0.6f ? HudTheme.Good : HudTheme.AmberAccent);
            ImGui.Spacing();
            ImGui.TextColored(HudTheme.Muted, $"{SeasonAdverb[season]} érdemes");
            ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + 460);
            ImGui.TextUnformatted(season switch
            {
                0 => "Ültetni, amíg a talaj nedves; figyelni a tavaszi fagyokra.",
                1 => "Figyelni az aszályt a Megfigyel lencséin; a sűrű állományokat gyéríteni.",
                2 => "Ültetni és gyéríteni: a csemeték ősszel eresztenek gyökeret a legbiztosabban.",
                _ => "Kitermelni és szállítani: lombtalan erdőben kíméletesebb a vágás."
            });
            ImGui.PopTextWrapPos();
            ImGui.Spacing(); ImGui.Spacing();
            if (ImGui.Button($"Tovább: {SeasonNames[season]}", new NVec2(220, 40)))
            {
                showSeasonCard = false;
                if (resumeAfterSeasonCard) simulationClock.IsPaused = false;
            }
            ImGui.SameLine(0, 18);
            ImGui.SetCursorPosY(ImGui.GetCursorPosY() + 10);
            bool show = seasonCards;
            if (ImGui.Checkbox("Évszakkártyák", ref show)) seasonCards = show;
            ImGui.End();
            ImGui.PopStyleColor();
            ImGui.PopStyleVar(2);

            static void Stat(string label, string value, NVec4 colour)
            {
                ImGui.TextUnformatted(label);
                ImGui.SameLine(240);
                ImGui.TextColored(colour, value);
            }
        }
    }
}
