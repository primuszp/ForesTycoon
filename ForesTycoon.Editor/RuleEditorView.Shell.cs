using System;
using System.Collections.Generic;
using System.Linq;
using ForesTycoon.Rules;
using ImGuiNET;
using Vec2 = System.Numerics.Vector2;
using Vec4 = System.Numerics.Vector4;

namespace ForesTycoon.Editor;

/// <summary>Every page of the editor. The sidebar groups them by the designer's task, not by where the data lives.</summary>
internal enum EditorPage
{
    Map, Soils, Species, Surfaces,
    Missions, Vehicles, Parameters,
    Objects, Rules, Responses, Processes, Controllers, Stocks, Library,
    Overview, Trial
}

/// <summary>
/// The editor's frame: a project bar on top (scene, history, save, apply), a sidebar with the pages grouped by task —
/// build the world, set up the game, author the simulation's logic, check it — and a status bar with the test world's
/// clock and the latest message. Pages draw only their own content below a common header.
/// </summary>
internal sealed partial class RuleEditorView
{
    private EditorPage page;
    private bool? navCompact;

    private sealed record NavEntry(EditorPage Page, string Label, GameIcon Icon, string Help);

    private static readonly (string Group, NavEntry[] Items)[] Navigation = {
        ("Világ", new NavEntry[] {
            new(EditorPage.Map, "Térkép", GameIcon.NewMap, "Generálás seedből, majd talaj, domborzat, víz, erdő és utak festése; rétegek és csempék."),
            new(EditorPage.Soils, "Talajtípusok", GameIcon.Lower, "Amit a térkép fest: víztartás, termékenység, teherbírás."),
            new(EditorPage.Species, "Fafajok", GameIcon.Spruce, "Növekedés, termőhely, méret és faanyagár."),
            new(EditorPage.Surfaces, "Utak és burkolatok", GameIcon.Macadam, "Utak és nyomok: költség, kopás, romlás, menetellenállás."),
        }),
        ("Játékmenet", new NavEntry[] {
            new(EditorPage.Missions, "Küldetések", GameIcon.Calendar, "A pálya céljai: amit a játék a futó világ értékeiből ellenőriz."),
            new(EditorPage.Vehicles, "Járművek", GameIcon.Truck, "Rönkszállító, processzor és forwarder műszaki adatai, fogyasztása és karbantartása."),
            new(EditorPage.Parameters, "Játékparaméterek", GameIcon.Speedometer, "Gazdaság, utak és egyéb alapértékek."),
        }),
        ("Szabályrendszer", new NavEntry[] {
            new(EditorPage.Objects, "Objektumok", GameIcon.Inspect, "A játék kódjából felderített objektumok és mezőik; innen adsz nekik viselkedést."),
            new(EditorPage.Rules, "Szabályok", GameIcon.Forestry, "Állapotot változtató szabályok: mi hasson mire, milyen feltétellel és görbe szerint."),
            new(EditorPage.Responses, "Összefüggések", GameIcon.Environment, "Értéket számoló gráfok, amelyeket a játék folyamatai kérdeznek le (termőhely, verseny, felújulás…)."),
            new(EditorPage.Processes, "Folyamatkapcsolatok", GameIcon.Sawmill, "A játék beépített folyamataiba (szállítás, feldolgozás, eladás) kötött gráfok."),
            new(EditorPage.Controllers, "Munkaciklusok", GameIcon.Forwarder, "Gépek vezérlése: mit csinál a forwarder, a processzor és a rönkszállító lépésről lépésre."),
            new(EditorPage.Stocks, "Készletek és áramlások", GameIcon.Timber, "Saját rendszerdinamikai készletek és a köztük folyó áramlások."),
            new(EditorPage.Library, "Szabálytár", GameIcon.Load, "Aktív kötések, egyedi felülírások és újrahasználható sablonok."),
        }),
        ("Ellenőrzés", new NavEntry[] {
            new(EditorPage.Overview, "Mezőhasználat", GameIcon.Graphics, "Melyik szabály melyik objektum melyik tulajdonságát olvassa és írja, és hol írja több szabály ugyanazt."),
            new(EditorPage.Trial, "Esős próba", GameIcon.Rain, "Ugyanaz a pálya, ugyanannyi eső: az alap és a szerkesztett szabályok összevetése."),
        }),
    };

    private static NavEntry Entry(EditorPage p) => Navigation.SelectMany(g => g.Items).First(e => e.Page == p);

    // The last item opened on each list page, reopened when the page is shown again.
    private readonly Dictionary<EditorPage, string> lastCommon = new();

    private void ShowPage(EditorPage target)
    {
        page = target;
        bool listPage = target is EditorPage.Responses or EditorPage.Processes or EditorPage.Controllers or EditorPage.Stocks or EditorPage.Library;
        if (listPage && (commonSelection == null || CommonPage(commonSelection) != target))
            commonSelection = target == EditorPage.Library ? "library" : lastCommon.GetValueOrDefault(target);
    }

    /// <summary>The page that lists a common rule key ("behavior:…", "controller:…", "stock:…", "flow:…", "library").</summary>
    private EditorPage CommonPage(string key)
    {
        if (key == "library") return EditorPage.Library;
        if (key.StartsWith("controller", StringComparison.Ordinal)) return EditorPage.Controllers;
        if (key.StartsWith("stock:", StringComparison.Ordinal) || key.StartsWith("flow:", StringComparison.Ordinal)) return EditorPage.Stocks;
        var graph = behaviors.Document.Graphs.FirstOrDefault(g => "behavior:" + g.Id == key);
        return graph != null && !EcologyResponseProgram.IsPureKind(graph.Kind) ? EditorPage.Processes : EditorPage.Responses;
    }

    // ── Frame ────────────────────────────────────────────────────────────
    // Bars hold one row of frames plus their padding, whatever the font size.
    private static float TopBarHeight => ImGui.GetFrameHeight() + 22;
    private static float StatusBarHeight => ImGui.GetFrameHeight() + 12;

    private void Shell(GameWorld world, bool standalone)
    {
        RefreshStatus(world);
        TopBar(world);
        // The bars and the body touch: no item spacing between the shell's rows.
        ImGui.SetCursorPosY(ImGui.GetCursorPosY() - ImGui.GetStyle().ItemSpacing.Y);
        ImGui.BeginChild("editor-main", new Vec2(0, -StatusBarHeight), ImGuiChildFlags.None, ImGuiWindowFlags.NoScrollbar);
        Sidebar(world);
        ImGui.SameLine(0, 0);
        ImGui.BeginChild("editor-body", Vec2.Zero, ImGuiChildFlags.AlwaysUseWindowPadding, ImGuiWindowFlags.NoScrollbar);
        if (projectLoadError != null) {
            ImGui.PushTextWrapPos(0);
            EditorStyle.Text(HudTheme.Bad, "A projektet nem sikerült megnyitni: " + projectLoadError);
            EditorStyle.Text(HudTheme.Muted, "A fájl megmaradt; az Editor a tesztvilág aktuális szabályait mutatja.");
            ImGui.PopTextWrapPos();
        }
        if (sceneSaveTask != null) Waiting("Jelenet ellenőrzése és mentése…");
        else if (world.BoundaryPending) Waiting(world.GenerationPending ? world.GenerationStage + "…" : "Szimulációs vagy szerkesztési munka folyamatban…");
        else PageBody(world, standalone);
        if (pendingCommonChange && !world.BoundaryPending && !ImGui.IsAnyItemActive()) {
            Try(() => ApplyProject(world, false)); pendingCommonChange = false;
        }
        if (world.GenerationCanCancel && ImGui.Button("Pályaépítés megszakítása")) world.CancelGeneration();
        if (!string.IsNullOrEmpty(world.GenerationError)) EditorStyle.Text(HudTheme.Bad, "A pályaépítés nem sikerült: " + world.GenerationError);
        ImGui.EndChild();
        ImGui.EndChild();
        ImGui.SetCursorPosY(ImGui.GetCursorPosY() - ImGui.GetStyle().ItemSpacing.Y);
        StatusBar(world);
    }

    private static void Waiting(string text)
    {
        Vec2 room = ImGui.GetContentRegionAvail();
        ImGui.SetCursorPos(ImGui.GetCursorPos() + new Vec2(0, Math.Max(0, room.Y / 2 - 20)));
        float width = ImGui.CalcTextSize(text).X;
        ImGui.SetCursorPosX(Math.Max(0, (room.X - width) / 2));
        EditorStyle.Text(HudTheme.Muted, text);
    }

    private void PageBody(GameWorld world, bool standalone)
    {
        Scenario(world);
        if (standalone && !scenarioBuilt) { scenarioBuilt = true; if (appliedScenario != ScenarioKey()) ApplyScenario(world, "A tesztvilág a pályából épült."); }
        var entry = Entry(page);
        EditorStyle.PageHeader(entry.Label, entry.Help);
        ImGui.Dummy(new Vec2(0, 2));
        switch (page) {
            case EditorPage.Soils: SoilTypesTab(world); break;
            case EditorPage.Species: SpeciesTab(world); break;
            case EditorPage.Surfaces: SurfacesTab(world); break;
            case EditorPage.Missions: MissionsTab(world); break;
            case EditorPage.Vehicles: VehiclesTab(world); break;
            case EditorPage.Parameters: ParametersTab(world); break;
            case EditorPage.Objects: ObjectsTab(world); break;
            case EditorPage.Rules: RuleBookArea(world); break;
            case EditorPage.Responses: CommonListPage(world, ResponseList, "Válassz egy összefüggést a listából."); break;
            case EditorPage.Processes: CommonListPage(world, ProcessList, "Válassz egy folyamatkapcsolatot, vagy köss újat a játék egy folyamatába."); break;
            case EditorPage.Controllers: CommonListPage(world, ControllerList, "Válassz egy munkaciklust."); break;
            case EditorPage.Stocks: CommonListPage(world, StockList, "Még nincs saját készlet. Az „+ Készlet” gomb indít egyet."); break;
            case EditorPage.Library: DrawCommonSelection(world); break;
            case EditorPage.Overview: RuleOverviewArea(world, RuleBookRules(world)); break;
            case EditorPage.Trial: TrialTab(world); break;
            default: MapTab(world); break;
        }
    }

    // ── Project bar ──────────────────────────────────────────────────────
    private void TopBar(GameWorld world)
    {
        ImGui.PushStyleColor(ImGuiCol.ChildBg, EditorStyle.Sidebar);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vec2(16, 11));
        ImGui.BeginChild("editor-top", new Vec2(0, TopBarHeight), ImGuiChildFlags.AlwaysUseWindowPadding, ImGuiWindowFlags.NoScrollbar);
        ImGui.PopStyleVar(); ImGui.PopStyleColor();
        var draw = ImGui.GetWindowDrawList();
        Vec2 origin = ImGui.GetWindowPos(), size = ImGui.GetWindowSize();
        draw.AddLine(origin + new Vec2(0, size.Y - 1), origin + size - new Vec2(0, 1), EditorStyle.U(EditorStyle.Hairline));

        // Brand and document.
        Vec2 p = ImGui.GetCursorScreenPos();
        float frame = ImGui.GetFrameHeight();
        GameIcons.Draw(draw, GameIcon.Spruce, p + new Vec2(0, (frame - 26) / 2), 26, EditorStyle.U(1, 1, 1, 1));
        ImGui.Dummy(new Vec2(30, frame)); ImGui.SameLine(0, 6);
        bool pushed = ImGuiController.HasTitleFonts;
        if (pushed) ImGui.PushFont(ImGuiController.TitleFont);
        ImGui.AlignTextToFramePadding(); EditorStyle.Text(HudTheme.Parchment, "ForesTycoon");
        if (pushed) ImGui.PopFont();
        ImGui.SameLine(0, 6); ImGui.AlignTextToFramePadding(); EditorStyle.Text(HudTheme.Muted, "Szerkesztő");
        ImGui.SameLine(0, 18);
        Vec2 bar = ImGui.GetCursorScreenPos();
        draw.AddLine(bar + new Vec2(0, 4), bar + new Vec2(0, frame - 4), EditorStyle.U(EditorStyle.Hairline));
        ImGui.Dummy(new Vec2(1, frame)); ImGui.SameLine(0, 18);
        ImGui.AlignTextToFramePadding(); EditorStyle.Text(HudTheme.Parchment, scenario?.Name ?? projectName);
        ImGui.SameLine(0, 8); ImGui.AlignTextToFramePadding(); EditorStyle.Text(HudTheme.Muted, System.IO.Path.GetFileName(scenePath));
        if (ImGui.IsItemHovered()) ImGui.SetTooltip(System.IO.Path.GetFullPath(scenePath) + (EditingGameScene ? "\nEz a játék indulójelenete: mentés után a játék ezt futtatja." : ""));
        ImGui.SameLine(0, 12);
        float chipY = ImGui.GetCursorPosY();
        ImGui.SetCursorPosY(chipY + (frame - ImGui.GetTextLineHeight() - 6) / 2);
        EditorStyle.Chip(statusCache.SceneSaved ? "mentve" : "mentetlen", statusCache.SceneSaved ? HudTheme.Good : HudTheme.AmberAccent, "chip-save");
        if (ImGui.IsItemHovered()) ImGui.SetTooltip(statusCache.SceneSaved ? "A jelenetfájl megegyezik a szerkesztett állapottal." : "Mentetlen módosítás. Ctrl+S menti a jelenetfájlba.");

        // Actions, right-aligned: history, scene, save, apply.
        string apply = algorithmBuild != null ? "Alkalmazás folyamatban…" : "Alkalmazás a játékban";
        var style = ImGui.GetStyle();
        float right = EditorStyle.ButtonWidth("Vissza") + EditorStyle.ButtonWidth("Újra") + EditorStyle.ButtonWidth("Jelenet…")
            + EditorStyle.ButtonWidth("Mentés") + EditorStyle.ButtonWidth(apply) + style.ItemSpacing.X * 3 + 4 + 20;
        ImGui.SameLine();
        ImGui.SetCursorPos(new Vec2(Math.Max(ImGui.GetCursorPosX(), ImGui.GetWindowWidth() - style.WindowPadding.X - right), chipY));
        UndoButtons(world);
        ImGui.SameLine(0, 20);
        SceneMenu(world);
        ImGui.SameLine();
        if (ImGui.Button("Mentés")) Try(() => SaveAll(world));
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("Ctrl+S · menti a munkapéldányt és a jelenetfájlt (pálya, küldetések, szabályok):\n" + scenePath);
        if (ImGui.GetIO().KeyCtrl && ImGui.IsKeyPressed(ImGuiKey.S)) Try(() => SaveAll(world));
        ImGui.SameLine();
        if (EditorStyle.PrimaryButton(apply, algorithmBuild == null,
            "A futó játék azonnal átveszi a szabályokat; az új világ ebből a pályából épül.")) Try(() => ApplyProject(world, true));
        ImGui.EndChild();
    }

    // ── Sidebar ──────────────────────────────────────────────────────────
    private void Sidebar(GameWorld world)
    {
        bool compact = navCompact ?? ImGui.GetWindowWidth() < 1400;
        ImGui.PushStyleColor(ImGuiCol.ChildBg, EditorStyle.Sidebar);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vec2(8, 10));
        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vec2(4, 2));
        ImGui.BeginChild("editor-nav", new Vec2(compact ? 56 : 224, 0), ImGuiChildFlags.AlwaysUseWindowPadding);
        var draw = ImGui.GetWindowDrawList();
        Vec2 origin = ImGui.GetWindowPos(), size = ImGui.GetWindowSize();
        draw.AddLine(origin + new Vec2(size.X - 1, 0), origin + size - new Vec2(1, 0), EditorStyle.U(EditorStyle.Hairline));
        foreach (var (group, items) in Navigation) {
            EditorStyle.NavGroup(group, compact);
            foreach (var item in items)
                if (EditorStyle.NavItem("nav-" + item.Page, item.Icon, item.Label, item.Help, page == item.Page, compact, Badge(world, item.Page)))
                    ShowPage(item.Page);
        }
        float toggle = ImGui.GetFrameHeight();
        ImGui.SetCursorPosY(Math.Max(ImGui.GetCursorPosY() + 8, ImGui.GetWindowHeight() - toggle - 10));
        if (EditorStyle.QuietButton(compact ? "»##nav-size" : "« Összecsukás##nav-size", compact ? "Oldalsáv kinyitása" : null)) navCompact = !compact;
        ImGui.EndChild();
        ImGui.PopStyleVar(2); ImGui.PopStyleColor();
    }

    /// <summary>A small count next to a page: how many of its items there are (enabled/all for switchable ones).</summary>
    private string Badge(GameWorld world, EditorPage p)
    {
        switch (p) {
            case EditorPage.Rules: {
                var rules = RuleBookRules(world); return $"{rules.Count(r => r.Enabled)}/{rules.Count}";
            }
            case EditorPage.Responses: {
                var graphs = behaviors.Document.Graphs.Where(g => EcologyResponseProgram.IsPureKind(g.Kind)).ToList();
                return $"{graphs.Count(g => !g.Disabled)}/{graphs.Count}";
            }
            case EditorPage.Processes: return ProcessGraphs().Count().ToString();
            case EditorPage.Controllers: return behaviors.Document.Controllers.Count.ToString();
            case EditorPage.Stocks: return (dynamicsEditor.Draft.Stocks.Count + dynamicsEditor.Draft.Flows.Count).ToString();
            case EditorPage.Missions: return scenario?.Missions.Count.ToString();
            case EditorPage.Soils: return scenario?.SoilTypes.Count.ToString();
            case EditorPage.Species: return scenario?.Species.Count.ToString();
            default: return null;
        }
    }

    // ── Status bar: the test world's clock, the latest message, where the work stands ──
    private void StatusBar(GameWorld world)
    {
        ImGui.PushStyleColor(ImGuiCol.ChildBg, EditorStyle.Sidebar);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vec2(12, 5));
        ImGui.BeginChild("editor-status", Vec2.Zero, ImGuiChildFlags.AlwaysUseWindowPadding, ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse);
        ImGui.PopStyleVar(); ImGui.PopStyleColor();
        var draw = ImGui.GetWindowDrawList();
        Vec2 origin = ImGui.GetWindowPos();
        draw.AddLine(origin, origin + new Vec2(ImGui.GetWindowWidth(), 0), EditorStyle.U(EditorStyle.Hairline));

        // Test world clock.
        bool ready = !world.BoundaryPending;
        ImGui.BeginDisabled(!ready);
        if (ImGui.Button(behaviors.IsPaused ? "Indítás##sim" : "Szünet##sim")) behaviors.TogglePause();
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled)) ImGui.SetTooltip("A tesztvilág szimulációjának indítása vagy megállítása.");
        void Advance(double hours) => behaviors.RequestTime(hours * world.SecondsPerEnvironmentHour);
        ImGui.SameLine(0, 4); if (ImGui.Button("+1 óra")) Advance(1);
        ImGui.SameLine(0, 4); if (ImGui.Button("+1 nap")) Advance(24);
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled)) ImGui.SetTooltip($"{24 * world.SecondsPerEnvironmentHour:0} játékmásodperc; nagy pályán néhány másodpercig tart.");
        ImGui.EndDisabled();
        ImGui.SameLine(0, 10); ImGui.AlignTextToFramePadding();
        double spring = world.RuleBook.FlowTotals.GetValueOrDefault("forrásvíz") / 1000 * world.TileArea;
        EditorStyle.Text(behaviors.IsPaused ? HudTheme.Muted : HudTheme.Good, (behaviors.IsPaused ? "áll" : "fut") + (spring > 0 ? $" · forrásvíz {spring:0} m³" : ""));
        ImGui.SameLine(0, 16);
        Vec2 bar = ImGui.GetCursorScreenPos(); float frame = ImGui.GetFrameHeight();
        draw.AddLine(bar + new Vec2(0, 5), bar + new Vec2(0, frame - 5), EditorStyle.U(EditorStyle.Hairline));
        ImGui.Dummy(new Vec2(1, frame)); ImGui.SameLine(0, 16);

        // Chips on the right; the message gets the room in between.
        string worldChip = statusCache.WorldCurrent ? "tesztvilág naprakész" : "tesztvilág: újraépítés kell";
        bool running = publishedProjectRevision != null && workshopVersions.Running == publishedProjectRevision && liveObjects?.Error == null;
        string gameChip = publishedProjectRevision == null ? "játék: korábbi szabályok" : running ? "játék: átvette" : "játék: átvételre vár";
        float chips = ImGui.CalcTextSize(worldChip).X + ImGui.CalcTextSize(gameChip).X + 48 + 8;
        float messageRoom = ImGui.GetContentRegionAvail().X - chips - 24;
        string status = message.Split('\n')[0];
        bool details = message.Length > 0;
        if (details) messageRoom -= EditorStyle.ButtonWidth("Részletek") + 8;
        if (ImGui.CalcTextSize(status).X > messageRoom) {
            while (status.Length > 8 && ImGui.CalcTextSize(status + "…").X > messageRoom) status = status[..^1];
            status = status.TrimEnd() + "…";
        }
        ImGui.AlignTextToFramePadding(); EditorStyle.Text(HudTheme.Muted, status);
        if (details) {
            ImGui.SameLine(0, 8);
            if (EditorStyle.QuietButton("Részletek")) ImGui.OpenPopup("workshop-message");
        }
        if (ImGui.BeginPopupModal("workshop-message", ImGuiWindowFlags.AlwaysAutoResize)) {
            ImGui.InputTextMultiline("##workshop-diagnostic", ref message, 30000, new Vec2(820, 420), ImGuiInputTextFlags.ReadOnly);
            if (ImGui.Button("Bezárás")) ImGui.CloseCurrentPopup(); ImGui.EndPopup();
        }

        ImGui.SameLine();
        ImGui.SetCursorPosX(Math.Max(ImGui.GetCursorPosX(), ImGui.GetWindowWidth() - 12 - chips));
        ImGui.SetCursorPosY(ImGui.GetCursorPosY() + (frame - ImGui.GetTextLineHeight() - 6) / 2);
        if (EditorStyle.Chip(worldChip, statusCache.WorldCurrent ? HudTheme.Good : HudTheme.Info, "chip-world", !statusCache.WorldCurrent)
            && !statusCache.WorldCurrent) { ApplyScenario(world, ""); statusFrame = -1000; }
        if (ImGui.IsItemHovered()) ImGui.SetTooltip(statusCache.WorldCurrent ? "A tesztvilág ebből a pályából épült." : "A pálya megváltozott. Kattintás: a tesztvilág újraépítése.");
        ImGui.SameLine(0, 8);
        EditorStyle.Chip(gameChip, running ? HudTheme.Good : HudTheme.Muted, "chip-game");
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("„Alkalmazás a játékban” után a futó játék átveszi a szabályokat.");
        ImGui.EndChild();
    }

    // ── Pages listing the common rules: a list on the left, the selected item's workspace on the right ──
    private const float ListWidth = 300;
    private string commonFilter = "";

    private void CommonListPage(GameWorld world, Func<GameWorld, string, string> list, string empty)
    {
        EditorStyle.BeginPanel("common-list", new Vec2(ListWidth, 0));
        string filter = EditorStyle.Search("##common-filter", "Keresés…", ref commonFilter);
        ImGui.Spacing();
        // The list returns its first item, opened when nothing of this page is selected yet.
        string first = list(world, filter);
        EditorStyle.EndPanel();
        if ((commonSelection == null || CommonPage(commonSelection) != page) && first != null) ShowCommonRule(first);
        ImGui.SameLine();
        if (commonSelection != null && CommonPage(commonSelection) == page) { DrawCommonSelection(world); return; }
        EditorStyle.BeginPanel("common-empty", Vec2.Zero);
        EditorStyle.Text(HudTheme.Muted, empty);
        EditorStyle.EndPanel();
    }

    private static bool Matches(string filter, params string[] texts) => filter.Length == 0 || texts.Any(t => Contains(t, filter));

    internal static string ResponseFamily(string kind) => kind switch {
        "site" => "Termőhely és víz",
        "competition" or "competitionPair" or "resource" => "Verseny, fény és erőforrás",
        "regeneration" => "Felújulás",
        "initialTree" => "Kezdő állomány",
        "environment" => "Környezet és időjárás",
        "vehicleResponse" => "Járművek",
        "gameplay" => "Játékmenet és gazdaság",
        _ when LogisticsRules.IsKind(kind) => "Logisztika",
        _ => kind
    };

    private string ResponseList(GameWorld world, string filter)
    {
        string first = null;
        var groups = behaviors.Document.Graphs.Where(g => EcologyResponseProgram.IsPureKind(g.Kind) && Matches(filter, g.Name, g.Hook))
            .GroupBy(g => ResponseFamily(g.Kind)).OrderBy(g => g.Key, StringComparer.CurrentCulture);
        foreach (var group in groups) {
            var members = group.ToList();
            if (filter.Length > 0 || members.Any(g => commonSelection == "behavior:" + g.Id)) ImGui.SetNextItemOpen(true, filter.Length > 0 ? ImGuiCond.Always : ImGuiCond.Once);
            bool open = ImGui.TreeNodeEx($"{group.Key}  ({members.Count(g => !g.Disabled)}/{members.Count})##family-{group.Key}",
                ImGuiTreeNodeFlags.FramePadding | ImGuiTreeNodeFlags.SpanAvailWidth);
            first ??= "behavior:" + members[0].Id;
            if (!open) continue;
            foreach (var g in members)
                RuleCardEntry("behavior:" + g.Id, g.Name + (g.ObjectId.HasValue ? " · #" + g.ObjectId : ""), "Értéket számol", commonSelection == "behavior:" + g.Id, !g.Disabled,
                    () => ShowCommonRule("behavior:" + g.Id), enabled => RequestResponseAvailability(g, enabled),
                    g.Disabled ? "Fix eredményt ad. Kattintás: az összefüggés visszakapcsolása." : "Kikapcsoláskor te választod ki a helyettesítő eredményt.");
            ImGui.TreePop();
        }
        if (first == null) EditorStyle.Text(HudTheme.Muted, "Nincs találat.");
        return first;
    }

    private IEnumerable<BehaviorGraph> ProcessGraphs() => behaviors.Document.Graphs.Where(g => !EcologyResponseProgram.IsPureKind(g.Kind)
        && (!g.Hook.StartsWith("tuning.", StringComparison.Ordinal) || g.Nodes.Any(n => n.Operation != BehaviorOperation.Native)));

    private string ProcessList(GameWorld world, string filter)
    {
        if (ImGui.Button("+ Új folyamatkapcsolat", new Vec2(-1, 0))) ImGui.OpenPopup("new-process");
        if (ImGui.BeginPopup("new-process")) {
            EditorStyle.Text(HudTheme.Muted, "Melyik folyamatba kössön?");
            foreach (var hook in WorldBehaviorPolicy.Hooks.Where(h => !h.Id.StartsWith("tuning.", StringComparison.Ordinal)))
                if (ImGui.Selectable(hook.Name + " · " + hook.Kind)) {
                    behaviors.SelectHook(hook);
                    ShowCommonRule("behavior:" + behaviors.Document.Graphs.Last(g => g.Hook == hook.Id && g.Kind == hook.Kind && g.ObjectId == null).Id);
                }
            ImGui.EndPopup();
        }
        ImGui.Spacing();
        string first = null;
        foreach (var g in ProcessGraphs().Where(g => Matches(filter, g.Name, g.Hook))) {
            string key = "behavior:" + g.Id; first ??= key;
            if (ImGui.Selectable(g.Name + "##common-" + key, commonSelection == key)) ShowCommonRule(key);
            if (ImGui.IsItemHovered()) ImGui.SetTooltip(g.Hook + " · " + g.Kind);
        }
        if (first == null) EditorStyle.Text(HudTheme.Muted, filter.Length > 0 ? "Nincs találat." : "Még nincs folyamatkapcsolat.");
        return first;
    }

    private string ControllerList(GameWorld world, string filter)
    {
        string first = null;
        var model = behaviors.Document;
        foreach (var c in model.Controllers.Where(c => Matches(filter, c.Name, c.Kind))) {
            string key = "controllerId:" + c.Id; first ??= key;
            if (ImGui.Selectable(c.Name + (c.ObjectId.HasValue ? " · #" + c.ObjectId : "") + "##common-" + key, commonSelection == key)) ShowCommonRule(key);
        }
        var missing = new[] { "forwarder", "processor", "truck" }.Where(kind => !model.Controllers.Any(c => c.Kind == kind && c.ObjectId == null)).ToArray();
        if (missing.Length > 0) {
            ImGui.Spacing(); EditorStyle.Section("Új munkaciklus");
            foreach (string kind in missing) {
                string key = "controller:" + kind;
                if (ImGui.Selectable("+ " + RuleWorkshopModel.KindName(kind) + " munkaciklusa##common-" + key, commonSelection == key)) ShowCommonRule(key);
            }
        }
        return first;
    }

    private string StockList(GameWorld world, string filter)
    {
        float half = (ImGui.GetContentRegionAvail().X - ImGui.GetStyle().ItemSpacing.X) / 2;
        if (ImGui.Button("+ Készlet", new Vec2(half, 0))) ShowCommonRule("stock:" + dynamicsEditor.CreateStock());
        ImGui.SameLine();
        ImGui.BeginDisabled(dynamicsEditor.Draft.Stocks.Count == 0);
        if (ImGui.Button("+ Áramlás", new Vec2(half, 0))) ShowCommonRule("flow:" + dynamicsEditor.CreateFlow());
        ImGui.EndDisabled();
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled)) ImGui.SetTooltip("Áramlás két készlet között; előbb készlet kell.");
        string first = null;
        void Item(string key, string label) {
            first ??= key;
            if (ImGui.Selectable(label + "##common-" + key, commonSelection == key)) ShowCommonRule(key);
        }
        var stocks = dynamicsEditor.Draft.Stocks.Where(s => Matches(filter, s.Name, s.Id)).ToList();
        var flows = dynamicsEditor.Draft.Flows.Where(f => Matches(filter, f.Name, f.Id)).ToList();
        if (stocks.Count > 0) { EditorStyle.Section("Készletek"); foreach (var s in stocks) Item("stock:" + s.Id, s.Name); }
        if (flows.Count > 0) { EditorStyle.Section("Áramlások"); foreach (var f in flows) Item("flow:" + f.Id, f.Name); }
        return first;
    }
}
