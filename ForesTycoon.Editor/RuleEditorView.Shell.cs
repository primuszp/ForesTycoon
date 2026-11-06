using System;
using System.Collections.Generic;
using System.Linq;
using ForesTycoon.Rules;
using ImGuiNET;
using Vec2 = System.Numerics.Vector2;
using Vec4 = System.Numerics.Vector4;

namespace ForesTycoon.Editor;

/// <summary>
/// The editor's frame: a project bar on top (scene, history, save, apply), a sidebar with the pages grouped by task —
/// build the world, set up the game, author the simulation's logic, check it — and a status bar with the test world's
/// clock and the latest message. Pages draw only their own content below a common header.
/// </summary>
internal sealed partial class RuleEditorView
{
    private ref EditorPage page => ref ui.Page;
    private ref bool? navCompact => ref ui.NavigationCompact;

    private sealed record NavEntry(EditorPage Page, string Label, GameIcon Icon, string Help);

    private static readonly (string Group, NavEntry[] Items)[] Navigation = {
        ("Világ", new NavEntry[] {
            new(EditorPage.Map, "Térkép", GameIcon.NewMap, "Generálás seedből, majd talaj, domborzat, víz, erdő és utak festése; rétegek és csempék."),
            new(EditorPage.Soils, "Talajtípusok", GameIcon.Lower, "Amit a térkép fest: víztartás, termékenység, teherbírás."),
            new(EditorPage.Biomes, "Biomok", GameIcon.Environment, "Tájtípusok magassági övenként: tengerpart, ártér, dombság, hegyvidék."),
            new(EditorPage.Species, "Fafajok", GameIcon.Spruce, "Növekedés, termőhely, méret és faanyagár."),
            new(EditorPage.Surfaces, "Utak és burkolatok", GameIcon.Macadam, "Utak és nyomok: költség, kopás, romlás, menetellenállás."),
        }),
        ("Játékmenet", new NavEntry[] {
            new(EditorPage.Missions, "Küldetések", GameIcon.Calendar, "A pálya céljai: amit a játék a futó világ értékeiből ellenőriz."),
            new(EditorPage.Vehicles, "Járművek", GameIcon.Truck, "Tengelyelrendezés, modellkötés és pályapróba; a rönkszállító, a processzor és a forwarder üzemi adatai."),
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
            new(EditorPage.SystemMap, "Rendszertérkép", GameIcon.Lightning, "A játék rendszerei és kapcsolataik: mit végez a motor magától, mit futtat a projekt szabályaiból, és hol szerkeszthető."),
            new(EditorPage.Overview, "Mezőhasználat", GameIcon.Graphics, "Melyik szabály melyik objektum melyik tulajdonságát olvassa és írja, és hol írja több szabály ugyanazt."),
            new(EditorPage.Trial, "Esős próba", GameIcon.Rain, "Ugyanaz a pálya, ugyanannyi eső: az alap és a szerkesztett szabályok összevetése."),
        }),
    };

    internal void ShowSystemMap() => ShowPage(EditorPage.SystemMap);

    private static NavEntry Entry(EditorPage p) => Navigation.SelectMany(g => g.Items).First(e => e.Page == p);

    private void ShowPage(EditorPage target)
    {
        if (page != target) { CompleteEdits(); if (stroke.Count > 0) CancelStroke(); }
        page = target;
        bool listPage = target is EditorPage.Responses or EditorPage.Processes or EditorPage.Controllers or EditorPage.Stocks or EditorPage.Library;
        if (listPage) ruleWorkspace.RestorePageSelection(target);
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
        if (savingScene) Waiting("Jelenet ellenőrzése és mentése…");
        else if (world.BoundaryPending) Waiting(world.GenerationPending ? world.GenerationStage + "…" : "Szimulációs vagy szerkesztési munka folyamatban…");
        else {
            if (!history.IsEditing && (ImGui.IsMouseDown(ImGuiMouseButton.Left) || ImGui.IsAnyItemActive()))
                history.BeginEdit("szerkesztés");
            PageBody(world, standalone);
        }
        ruleWorkspace.FlushChanges(world);
        if (!ImGui.IsMouseDown(ImGuiMouseButton.Left) && !ImGui.IsAnyItemActive()) CompleteEdits();
        TrackHistory(world);
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
            case EditorPage.Biomes: biomeEditor.Draw(world); break;
            case EditorPage.Species: SpeciesTab(world); break;
            case EditorPage.Surfaces: tuningEditor.SurfacesTab(world); break;
            case EditorPage.Missions: missionEditor.Draw(world); break;
            case EditorPage.Vehicles: tuningEditor.VehiclesTab(world); break;
            case EditorPage.Parameters: tuningEditor.ParametersTab(world); break;
            case EditorPage.Objects or EditorPage.Rules or EditorPage.Responses or EditorPage.Processes
                or EditorPage.Controllers or EditorPage.Stocks or EditorPage.Library or EditorPage.Overview:
                ruleWorkspace.Draw(world); break;
            case EditorPage.SystemMap: currentRules.Draw(world, ruleWorkspace.OpenHook, ruleWorkspace.OpenController); break;
            case EditorPage.Trial: trialEditor.Draw(world); break;
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
        ImGui.AlignTextToFramePadding(); EditorStyle.Text(HudTheme.Parchment, document.World?.Name ?? document.Name);
        ImGui.SameLine(0, 8); ImGui.AlignTextToFramePadding(); EditorStyle.Text(HudTheme.Muted, System.IO.Path.GetFileName(scenePath));
        if (ImGui.IsItemHovered()) ImGui.SetTooltip(System.IO.Path.GetFullPath(scenePath) + (EditingGameScene ? "\nEz a játék indulójelenete: mentés után a játék ezt futtatja." : ""));
        ImGui.SameLine(0, 12);
        float chipY = ImGui.GetCursorPosY();
        ImGui.SetCursorPosY(chipY + (frame - ImGui.GetTextLineHeight() - 6) / 2);
        EditorStyle.Chip(statusCache.SceneSaved ? "mentve" : "mentetlen", statusCache.SceneSaved ? HudTheme.Good : HudTheme.AmberAccent, "chip-save");
        if (ImGui.IsItemHovered()) ImGui.SetTooltip(statusCache.SceneSaved ? "A jelenetfájl megegyezik a szerkesztett állapottal." : "Mentetlen módosítás. Ctrl+S menti a jelenetfájlba.");

        // Actions, right-aligned: history, scene, save, apply.
        string apply = publishing ? "Alkalmazás folyamatban…" : "Alkalmazás a játékban";
        var style = ImGui.GetStyle();
        float right = EditorStyle.ButtonWidth("Vissza") + EditorStyle.ButtonWidth("Újra") + EditorStyle.ButtonWidth("Jelenet…")
            + EditorStyle.ButtonWidth("Mentés") + EditorStyle.ButtonWidth(apply) + style.ItemSpacing.X * 3 + 4 + 20;
        ImGui.SameLine();
        ImGui.SetCursorPos(new Vec2(Math.Max(ImGui.GetCursorPosX(), ImGui.GetWindowWidth() - style.WindowPadding.X - right), chipY));
        UndoButtons(world);
        ImGui.SameLine(0, 20);
        SceneMenu(world);
        ImGui.SameLine();
        ImGui.BeginDisabled(savingScene || publishing);
        if (ImGui.Button("Mentés")) Try(() => SaveAll(world));
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("Ctrl+S · menti a munkapéldányt és a jelenetfájlt (pálya, küldetések, szabályok):\n" + scenePath);
        if (!savingScene && !publishing && ImGui.GetIO().KeyCtrl && ImGui.IsKeyPressed(ImGuiKey.S)) Try(() => SaveAll(world));
        ImGui.EndDisabled();
        ImGui.SameLine();
        if (EditorStyle.PrimaryButton(apply, !publishing && !savingScene,
            "Menti a jelenetet, és a futó játék azonnal átveszi a szabályokat; a következő indulás is ezt futtatja.")) Try(() => ApplyProject(world, true));
        ImGui.EndChild();
    }

    // ── Sidebar ──────────────────────────────────────────────────────────
    private void Sidebar(GameWorld world)
    {
        bool compact = navCompact ?? ImGui.GetWindowWidth() < 1400;
        ImGui.PushStyleColor(ImGuiCol.ChildBg, EditorStyle.Sidebar);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vec2(8, 10));
        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vec2(4, 2));
        ImGui.BeginChild("editor-nav", new Vec2(compact ? 56 : ImGui.GetFontSize() * 15f, 0), ImGuiChildFlags.AlwaysUseWindowPadding);
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
                var rules = document.Rules; return $"{rules.Count(r => r.Enabled)}/{rules.Count}";
            }
            case EditorPage.Responses: {
                var graphs = behaviors.Document.Graphs.Where(g => EcologyResponseProgram.IsPureKind(g.Kind)).ToList();
                return $"{graphs.Count(g => !g.Disabled)}/{graphs.Count}";
            }
            case EditorPage.Processes: return ruleWorkspace.ProcessCount.ToString();
            case EditorPage.Controllers: return behaviors.Document.Controllers.Count.ToString();
            case EditorPage.Stocks: return (dynamicsEditor.Stocks.Count + dynamicsEditor.Flows.Count).ToString();
            case EditorPage.Missions: return document.World?.Missions.Count.ToString();
            case EditorPage.Soils: return document.World?.SoilTypes.Count.ToString();
            case EditorPage.Biomes: return document.World?.Biomes.Count.ToString();
            case EditorPage.Species: return document.World?.Species.Count.ToString();
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
        if (ImGui.Button(sandbox.IsPaused ? "Indítás##sim" : "Szünet##sim")) sandbox.TogglePause();
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled)) ImGui.SetTooltip("A tesztvilág szimulációjának indítása vagy megállítása.");
        void Advance(double hours) => sandbox.RequestTime(hours * world.SecondsPerEnvironmentHour);
        ImGui.SameLine(0, 4); if (ImGui.Button("+1 óra")) Advance(1);
        ImGui.SameLine(0, 4); if (ImGui.Button("+1 nap")) Advance(24);
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled)) ImGui.SetTooltip($"{24 * world.SecondsPerEnvironmentHour:0} játékmásodperc; nagy pályán néhány másodpercig tart.");
        ImGui.EndDisabled();
        ImGui.SameLine(0, 10); ImGui.AlignTextToFramePadding();
        double spring = world.RuleBook.FlowTotals.GetValueOrDefault("forrásvíz") / 1000 * world.TileArea;
        EditorStyle.Text(sandbox.IsPaused ? HudTheme.Muted : HudTheme.Good, (sandbox.IsPaused ? "áll" : "fut") + (spring > 0 ? $" · forrásvíz {spring:0} m³" : ""));
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
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("„Alkalmazás a játékban” menti a jelenetet, és a futó játék átveszi a szabályokat.");
        ImGui.EndChild();
    }

}
