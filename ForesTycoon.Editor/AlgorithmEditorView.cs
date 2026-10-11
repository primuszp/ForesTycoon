using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using ForesTycoon.RuleCompiler;
using ForesTycoon.Rules;
using ImGuiNET;
using Vec2 = System.Numerics.Vector2;

namespace ForesTycoon.Editor;

/// <summary>Structural programming through nodes and ordered ports; there is no source-text authoring surface.</summary>
internal sealed partial class AlgorithmEditorView
{
    private AlgorithmBundle bundle;
    private AlgorithmFile file;
    private Dictionary<string, AlgorithmNode> index;
    private string focus, selected, clipboard, search = "", workspace = AlgorithmBuild.FindWorkspace() ?? "", status = "";
    private int template;
    private int nodeKind;
    private RuleSource pendingSource;
    private string sourceNote;
    private bool sourceMissing;
    private bool semantic = true;
    private string nodeSearch = "";
    private readonly Stack<string> back = new();
    private Stack<string> undo = new(), redo = new();
    private readonly Dictionary<string, (Stack<string> Undo, Stack<string> Redo)> histories = new();
    private Task<AlgorithmBundle> importing;
    private Vec2 pan = new(30, 30);
    private float zoom = .85f;
    private string dragging;
    internal bool HasGraphs => bundle != null;
    internal bool IsLoading => importing != null;
    internal long ChangeVersion { get; private set; }
    internal AlgorithmGraphReference Reference { get; private set; }
    internal string Status => status;
    internal string Workspace => workspace;
    internal AlgorithmBundle Document => bundle;
    internal void LoadDocument(AlgorithmBundle document, string root) { importing = null; pendingSource = null; ChangeVersion++; histories.Clear(); bundle = document; workspace = root; Open(document.Files.First()); }
    internal void Refresh() => CompleteImport();
    internal void Load(AlgorithmGraphReference reference)
    {
        ChangeVersion++; Reference = reference; bundle = null; file = null;
        importing = null; histories.Clear(); pendingSource = null; sourceNote = null; sourceMissing = false;
        if (reference != null) { workspace = reference.WorkspaceRoot ?? ""; importing = Task.Run(() => reference.Revision == "draft"
            ? AlgorithmBundle.FromJson(File.ReadAllText(reference.BundlePath)) : AlgorithmBuild.Read(reference)); }
        else if (AlgorithmBuild.FindWorkspace() is string root) ImportWorkspace(root);
    }
    internal AlgorithmGraphReference Store(string projectPath)
    {
        CompleteImport();
        if (importing != null) throw new InvalidOperationException("Várd meg az algoritmusgráfok beolvasását.");
        if (bundle == null) return Reference;
        return Reference = AlgorithmBuild.Store(bundle, workspace, projectPath);
    }
    internal void ImportWorkspace(string root)
    {
        ChangeVersion++;
        workspace = Path.GetFullPath(root); importing = Task.Run(() => AlgorithmBundle.ImportWorkspace(workspace));
        status = "A teljes játékmenet beolvasása gráfokként…";
    }
    private void CompleteImport()
    {
        if (importing?.IsCompleted != true) return;
        try {
            bundle = importing.GetAwaiter().GetResult(); status = $"{bundle.Files.Count} algoritmus- és adatgráf, {bundle.Files.Sum(f => f.Nodes.Count)} csomópont.";
            histories.Clear(); Open(bundle.Files.First());
        } catch (Exception e) { status = e.Message; }
        importing = null;
        if (bundle != null && pendingSource != null) { var request = pendingSource; pendingSource = null; SelectSource(request); }
    }
    internal void SelectSource(RuleSource source)
    {
        CompleteImport();
        sourceNote = null; sourceMissing = false;
        if (bundle == null) { pendingSource = source; if (importing == null && workspace.Length > 0) ImportWorkspace(workspace); return; }
        var match = bundle.Files.Find(f => f.Path.Equals(source.File.Replace('\\', '/'), StringComparison.OrdinalIgnoreCase));
        if (match == null) { sourceMissing = true; sourceNote = status = "Az algoritmusfájl nincs a gráfprojektben: " + source.File; return; }
        Open(match);
        string symbol = source.Symbol.Split('.').Last().Split('(')[0];
        var matches = file.Entries.Where(e => e.Name == symbol).ToArray(); var entry = matches.Length == 1 ? matches[0] : null;
        if (entry != null) {
            var member = index[entry.Id];
            focus = selected = member.Children.FirstOrDefault(id => index[id].Kind is "Block" or "ArrowExpressionClause") ?? entry.Id;
        }
        else sourceNote = matches.Length > 1 ? "Több eljárás tartozik ehhez a névhez. Válaszd ki a megfelelő belépési pontot; most a teljes fájlgráf látható."
            : "A katalógus szimbóluma nem található külön belépési pontként: " + source.Symbol + ". A hozzá tartozó teljes fájlgráf látható.";
    }
    private void Open(AlgorithmFile document)
    {
        valueDrafts.Clear();
        sourceMissing = false; sourceNote = null;
        file = document; index = file.Index(); focus = selected = file.Root; back.Clear(); pan = new(30, 30);
        if (!histories.TryGetValue(file.Path, out var history)) histories[file.Path] = history = (new(), new());
        undo = history.Undo; redo = history.Redo;
    }
    private void BeforeEdit() {
        ChangeVersion++;
        undo.Push(JsonSerializer.Serialize(file)); redo.Clear();
        if (undo.Count > 32) { var snapshots = undo.Take(32).Reverse().ToArray(); undo.Clear(); foreach (string snapshot in snapshots) undo.Push(snapshot); }
    }
    private void Restore(Stack<string> from, Stack<string> to)
    {
        if (from.Count == 0) return;
        ChangeVersion++;
        valueDrafts.Clear();
        to.Push(JsonSerializer.Serialize(file)); int position = bundle.Files.IndexOf(file);
        file = JsonSerializer.Deserialize<AlgorithmFile>(from.Pop()); bundle.Files[position] = file; index = file.Index();
        if (!index.ContainsKey(focus)) focus = file.Root; if (!index.ContainsKey(selected)) selected = focus;
    }
    private void Enter(string id) { back.Push(focus); focus = selected = id; pan = new(30, 30); }
    private void Edit(Action action) { BeforeEdit(); action(); index = file.Index(); status = "Gráf módosítva; mentéskor és fordításkor ellenőrizzük."; }
    private string Label(AlgorithmNode node)
    {
        string kind = node.Kind switch {
            "Block" => "Utasításblokk", "IfStatement" => "Feltétel", "WhileStatement" => "Ismétlés", "ForStatement" => "Számlálós ciklus",
            "ForEachStatement" => "Objektumok bejárása", "ExpressionStatement" => "Művelet / állapotírás", "ReturnStatement" => "Eredmény",
            "TryStatement" => "Hibakezelés", "VariableDeclaration" => "Változó", "InvocationExpression" => "Eljáráshívás",
            "ArrowExpressionClause" => "Képlet", "IdentifierName" => "Változó / mező", "IdentifierToken" => "Név",
            "NumericLiteralToken" => "Szám", "StringLiteralToken" => "Szöveg", "OpenBraceToken" => "Blokk kezdete", "CloseBraceToken" => "Blokk vége",
            "JsonObject" => "Adatobjektum", "JsonProperty" => "Adatmező", "JsonNumber" => "Számadat", "JsonString" => "Szövegadat", "JsonArray" => "Adatlista",
            _ => node.Kind
        };
        if (node.Token || node.Kind.StartsWith("Json")) return kind + (node.Text.Length > 0 ? " · " + node.Text[..Math.Min(node.Text.Length, 55)].Replace('\n', ' ') : "");
        var entry = file.Entries.Find(e => e.Id == node.Id);
        return entry == null ? kind : kind + " · " + entry.Name;
    }
    internal void Draw(bool embedded = false)
    {
        CompleteImport();
        if (embedded && sourceNote != null) { ImGui.TextWrapped(sourceNote); if (sourceMissing) return; }
        if (!embedded) ImGui.TextWrapped("Minden algoritmus szerkezeti gráfként szerkeszthető: feltételek, ciklusok, műveletek, állapotírások, típusok és adatok. Az algoritmusok fordítása után a játék mentett állapotból indul újra.");
        if (bundle == null) {
            ImGui.SetNextItemWidth(650); ImGui.InputText("Munkaterület", ref workspace, 1024);
            ImGui.BeginDisabled(importing != null);
            if (ImGui.Button("Teljes játék beolvasása gráfokba")) { try { ImportWorkspace(workspace); } catch (Exception e) { status = e.Message; } }
            ImGui.EndDisabled(); ImGui.TextWrapped(status); return;
        }
        if (!embedded) {
        ImGui.TextWrapped(status); ImGui.SetNextItemWidth(300); ImGui.InputText("Algoritmus keresése", ref search, 120);
        ImGui.BeginChild("algorithm-files", new Vec2(280, 0), ImGuiChildFlags.Borders);
        ImGui.SetNextItemWidth(-1); ImGui.InputText("##algorithm-node-search", ref nodeSearch, 128);
        if (nodeSearch.Length > 0 && file != null) {
            ImGui.TextWrapped("Csomópontok az aktív fájlban (ID, érték, művelet):");
            foreach (var node in file.Nodes.Where(n => n.Id == nodeSearch || n.Text.Contains(nodeSearch, StringComparison.OrdinalIgnoreCase)
                || n.Kind.Contains(nodeSearch, StringComparison.OrdinalIgnoreCase)).Take(100))
                if (ImGui.Selectable(Label(node) + "##search-" + node.Id)) Enter(node.Id);
            ImGui.Separator();
        }
        foreach (var candidate in bundle.Files.Where(f => search.Length == 0 || f.Path.Contains(search, StringComparison.OrdinalIgnoreCase)
            || f.Entries.Any(e => e.Name.Contains(search, StringComparison.OrdinalIgnoreCase)))) {
            if (ImGui.TreeNode(candidate.Path)) {
                if (ImGui.Selectable("Teljes gráf##" + candidate.Path, candidate == file && focus == candidate.Root)) Open(candidate);
                foreach (var entry in candidate.Entries.Where(e => search.Length == 0 || candidate.Path.Contains(search, StringComparison.OrdinalIgnoreCase) || e.Name.Contains(search, StringComparison.OrdinalIgnoreCase)))
                    if (ImGui.Selectable(entry.Name + "##" + candidate.Path + entry.Id, candidate == file && entry.Id == focus)) { Open(candidate); focus = selected = entry.Id; }
                ImGui.TreePop();
            }
        }
        ImGui.EndChild(); ImGui.SameLine();
        }
        ImGui.BeginChild("algorithm-work", new Vec2(0, 0));
        if (file == null) { ImGui.EndChild(); return; }
        if (!embedded) ImGui.TextUnformatted(file.Path);
        if (embedded && sourceNote != null && ImGui.BeginCombo("Belépési pont", "Válassz a fájl eljárásai közül")) {
            foreach (var entry in file.Entries) if (ImGui.Selectable(entry.Name + "##entry" + entry.Id)) { focus = selected = entry.Id; sourceNote = null; }
            ImGui.EndCombo();
        }
        if (ImGui.RadioButton("Műveletnézet", semantic)) semantic = true;
        ImGui.SameLine(); if (ImGui.RadioButton("Teljes szerkezeti gráf", !semantic)) semantic = false;
        if (ImGui.Button("Vissza") && back.Count > 0) { focus = selected = back.Pop(); pan = new(30, 30); }
        ImGui.SameLine(); if (ImGui.Button("Gyökér")) { focus = selected = file.Root; back.Clear(); }
        ImGui.SameLine(); if (ImGui.Button("Visszavonás")) Restore(undo, redo);
        ImGui.SameLine(); if (ImGui.Button("Újra")) Restore(redo, undo);
        ImGui.SameLine(); if (ImGui.Button("Gráf ellenőrzése")) {
            try { AlgorithmBundle.Render(file); status = "A kapcsolatok és a szintaxis érvényesek; a típuskontrollt a teljes fordítás ellenőrzi."; }
            catch (Exception e) { status = e.Message; }
        }
        ImGui.BeginChild("algorithm-canvas", new Vec2(Math.Max(180, ImGui.GetContentRegionAvail().X - Math.Min(330, ImGui.GetContentRegionAvail().X * .42f) - 8), 0), ImGuiChildFlags.Borders);
        Canvas(); ImGui.EndChild(); ImGui.SameLine();
        ImGui.BeginChild("algorithm-inspector", new Vec2(0, 0), ImGuiChildFlags.Borders);
        if (semantic) SemanticInspector(); else Inspector(); ImGui.EndChild(); ImGui.EndChild();
    }
    private void Canvas()
    {
        Vec2 origin = ImGui.GetCursorScreenPos(); Vec2 size = ImGui.GetContentRegionAvail();
        ImGui.InvisibleButton("algorithm-background", size, ImGuiButtonFlags.MouseButtonMiddle);
        if (ImGui.IsItemHovered()) {
            if (ImGui.IsMouseDragging(ImGuiMouseButton.Middle)) pan += ImGui.GetIO().MouseDelta;
            if (ImGui.GetIO().MouseWheel != 0) zoom = Math.Clamp(zoom + ImGui.GetIO().MouseWheel * .08f, .25f, 1.5f);
        }
        var draw = ImGui.GetWindowDrawList(); var root = index[focus];
        var ports = semantic ? AlgorithmSemantics.Ports(root, index)
            : root.Children.Select((id, i) => new AlgorithmPort("Port " + i, root.Id, i, id)).ToArray();
        int columns = Math.Max(1, Math.Min(3, (int)((size.X - 30) / (290 * zoom))));
        Vec2 start = origin + pan + new Vec2((columns - 1) * 145, 10) * zoom;
        Card(root, start, true);
        for (int i = 0; i < ports.Length; i++) {
            string id = ports[i].Target; if (!index.TryGetValue(id, out var node)) continue;
            Vec2 point = origin + pan + (new Vec2((i % columns) * 290, 145 + i / columns * 120) + new Vec2(node.X, node.Y)) * zoom;
            draw.AddLine(start + new Vec2(125, 75) * zoom, point + new Vec2(125, 0) * zoom, 0xff7b9b66, 2);
            draw.AddText(point - new Vec2(0, 18), 0xffa9b8bb, ports[i].Name); Card(node, point, false);
        }
        if (!ImGui.IsMouseDown(ImGuiMouseButton.Left)) dragging = null;
        void Card(AlgorithmNode node, Vec2 position, bool parent) {
            Vec2 end = position + new Vec2(260, 90) * zoom;
            draw.AddRectFilled(position, end, selected == node.Id ? 0xff526f43 : node.Token ? 0xff343b48 : 0xff344d42, 6);
            draw.AddRect(position, end, 0xffa2b18c, 6);
            draw.PushClipRect(position + new Vec2(5, 4), end - new Vec2(5, 4), true);
            draw.AddText(position + new Vec2(7, 7), 0xffe3eadf, semantic ? AlgorithmSemantics.Name(node) : Label(node));
            draw.AddText(position + new Vec2(7, 32), 0xffb1c6ab, semantic ? AlgorithmSemantics.Summary(node.Id, index, 70) : node.Token ? "érték / név / operátor" : node.Children.Count + " kapcsolat · dupla kattintás: kibontás");
            draw.AddText(position + new Vec2(7, 54), 0xff91a18a, semantic ? "Kijelölés: kattintás · belépés: dupla kattintás" : node.Id); draw.PopClipRect();
            bool hovered = ImGui.IsWindowHovered() && ImGui.IsMouseHoveringRect(position, end);
            if (hovered && ImGui.IsMouseClicked(ImGuiMouseButton.Left)) { selected = node.Id; if (!parent) { BeforeEdit(); dragging = node.Id; } }
            if (hovered && ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left) && !parent && !node.Token) Enter(node.Id);
            if (dragging == node.Id && ImGui.IsMouseDragging(ImGuiMouseButton.Left)) { node.X += ImGui.GetIO().MouseDelta.X / zoom; node.Y += ImGui.GetIO().MouseDelta.Y / zoom; }
        }
    }
    private void Inspector()
    {
        if (!index.TryGetValue(selected, out var node)) return;
        ImGui.TextWrapped(Label(node)); ImGui.TextWrapped("Azonosító: " + node.Id);
        if (ImGui.Button("Kijelölés kibontása") && focus != selected) Enter(selected);
        if (node.Token || node.Kind is "JsonString" or "JsonNumber" or "JsonProperty") {
            string text = node.Text; ImGui.SetNextItemWidth(-1);
            DrawValue(node);
            ImGui.TextWrapped("Egyetlen érték, változónév vagy operátor. A gráfkapcsolatokat az alábbi portokkal módosítsd.");
        }
        if (node.Kind is "JsonTrue" or "JsonFalse") {
            bool value = node.Kind == "JsonTrue"; if (ImGui.Checkbox("Érték", ref value)) Edit(() => node.Kind = value ? "JsonTrue" : "JsonFalse");
        }
        if (ImGui.Button("Kapcsolat forrásának kijelölése")) clipboard = node.Id;
        if (ImGui.Button("Részgráf másolása")) Edit(() => { clipboard = AlgorithmBundle.Duplicate(file, node.Id); selected = clipboard; });
        ImGui.TextWrapped("Kijelölt forrás: " + (clipboard ?? "nincs"));
        if (!node.Token) {
            ImGui.Separator(); ImGui.TextUnformatted("Rendezett bemeneti / utasításportok");
            for (int i = 0; i < node.Children.Count; i++) {
                int port = i; string child = node.Children[i]; ImGui.PushID(i);
                if (ImGui.Selectable(i + ": " + (index.TryGetValue(child, out var n) ? Label(n) : child))) selected = child;
                if (ImGui.SmallButton("Csere") && clipboard != null) Edit(() => node.Children[port] = clipboard);
                ImGui.SameLine(); if (ImGui.SmallButton("Fel") && i > 0) Edit(() => (node.Children[port - 1], node.Children[port]) = (node.Children[port], node.Children[port - 1]));
                ImGui.SameLine(); if (ImGui.SmallButton("Le") && i + 1 < node.Children.Count) Edit(() => (node.Children[port + 1], node.Children[port]) = (node.Children[port], node.Children[port + 1]));
                ImGui.SameLine(); if (ImGui.SmallButton("Törlés")) { Edit(() => node.Children.RemoveAt(port)); ImGui.PopID(); break; }
                ImGui.PopID();
            }
            if (ImGui.Button("Forrás csatolása") && clipboard != null) Edit(() => node.Children.Insert(node.Kind == "Block" ? Math.Max(1, node.Children.Count - 1) : node.Children.Count, clipboard));
        }
        if (file.Format == "csharp") {
            ImGui.Separator(); ImGui.Combo("Új csomópont", ref template, AlgorithmBundle.Templates, AlgorithmBundle.Templates.Length);
            if (ImGui.Button("Sablon létrehozása")) Edit(() => { clipboard = AlgorithmBundle.InsertTemplate(file, template); selected = clipboard; });
            ImGui.Combo("További műveletek", ref nodeKind, AlgorithmBundle.NodeKinds, AlgorithmBundle.NodeKinds.Length);
            if (ImGui.Button("Művelet létrehozása")) Edit(() => { clipboard = AlgorithmBundle.InsertNode(file, AlgorithmBundle.NodeKinds[nodeKind]); selected = clipboard; });
            ImGui.TextWrapped("A sablon egy új részgráf. Jelöld ki a célblokkot vagy műveletet, és csatold a forrást, vagy cseréld vele valamelyik portot.");
        } else if (ImGui.Button("Új adatérték")) Edit(() => {
            var fresh = new AlgorithmNode { Id = "g" + Guid.NewGuid().ToString("N"), Kind = "JsonNumber", Text = "0" };
            file.Nodes.Add(fresh); clipboard = selected = fresh.Id;
        });
    }
}
