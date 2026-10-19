using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using ForesTycoon.Rules;
using ImGuiNET;

namespace ForesTycoon.Editor;

internal sealed partial class RuleEditorView
{
    private bool templateSpecific;
    private string librarySearch = "", templateTarget = "", templateId,
        transferPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ForesTycoon", "rules", "sablonok.json");
    private readonly Stack<string> authoringUndo = new(), authoringRedo = new();
    private int TopicOf(string id, int fallback) => authoring.Entries.TryGetValue(id, out var entry) ? Math.Clamp(entry.Topic, 0, 6) : fallback;
    private string RecoveryStem() => projectPath + ".recovery-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N")[..8];
    private void RememberAuthoring() { authoringUndo.Push(JsonSerializer.Serialize(authoring)); authoringRedo.Clear(); }
    private void AuthoringHistory(Stack<string> from, Stack<string> to) { if (from.Count == 0) return; to.Push(JsonSerializer.Serialize(authoring)); authoring = JsonSerializer.Deserialize<RuleAuthoring>(from.Pop()); }
    private void SetTopic(string id, int topic) { RememberAuthoring(); if (!authoring.Entries.TryGetValue(id, out var entry)) authoring.Entries[id] = entry = new(); entry.Topic = topic; }
    private ulong? TargetScope(bool supportsObject)
    {
        if (!supportsObject) { templateSpecific = false; ImGui.TextWrapped("Hatókör: teljes világ / típusszabály."); return null; }
        ImGui.Checkbox("Egyedi objektumhoz kötöm", ref templateSpecific);
        if (!templateSpecific) { ImGui.TextWrapped("Hatókör: minden ilyen típusú objektum, az új objektumokra is."); return null; }
        ImGui.SetNextItemWidth(240); ImGui.InputText("Objektumazonosító", ref templateTarget, 24);
        if (ulong.TryParse(templateTarget, out ulong id)) return id;
        ImGui.TextWrapped("Válassz létező objektumot az objektumböngészőben, vagy írd be a saját világában érvényes ID-ját."); return null;
    }
    private void OpenLibraryGraph(BehaviorGraph graph)
    {
        behaviors.SelectGraph(graph.Id); ShowCommonRule("behavior:" + graph.Id);
    }
    private void DrawRuleLibrary(GameWorld world)
    {
        ImGui.TextWrapped("Aktív kötések és újrahasználható sablonok. A sablon másolásával nem keletkezik második, ütköző típusszabály.");
        ImGui.SetNextItemWidth(-1); ImGui.InputTextWithHint("##library-search", "Név, folyamat, típus vagy objektumazonosító", ref librarySearch, 160);
        bool Match(string value) => value.Contains(librarySearch, StringComparison.OrdinalIgnoreCase);
        if (ImGui.Button("Besorolás / sablon visszavonása")) AuthoringHistory(authoringUndo, authoringRedo);
        ImGui.SameLine(); if (ImGui.Button("Újra##library")) AuthoringHistory(authoringRedo, authoringUndo);
        if (ImGui.CollapsingHeader("Aktív képletek")) {
        foreach (var graph in behaviors.Document.Graphs.Where(g => Match(g.Name + " " + g.Hook + " " + g.Kind + " " + g.ObjectId)).ToArray()) {
            ImGui.PushID(graph.Id);
            if (ImGui.Selectable(graph.Name + " · " + RuleWorkshopModel.KindName(graph.Kind) + (graph.ObjectId.HasValue ? " #" + graph.ObjectId : " · típus"))) OpenLibraryGraph(graph);
            if (ImGui.SmallButton("Sablonként másolom")) { RememberAuthoring(); authoring.Templates.Add(AuthoringOperations.Template(graph)); }
            ImGui.SameLine(); if (ImGui.SmallButton(graph.ObjectId.HasValue ? "Egyedi felülírás eltávolítása" : EcologyResponseProgram.IsPureKind(graph.Kind) ? "Alapgráf visszaállítása" : "Beépített alap visszaállítása")) behaviors.RestoreDefault(graph.Hook, graph.Kind, graph.ObjectId);
            ImGui.PopID();
        }
        }
        if (ImGui.CollapsingHeader("Aktív munkaciklusok")) {
        foreach (var controller in behaviors.Document.Controllers.Where(c => Match(c.Name + " " + c.Kind + " " + c.ObjectId)).ToArray()) {
            ImGui.PushID(controller.Id);
            if (ImGui.Selectable(controller.Name + (controller.ObjectId.HasValue ? " #" + controller.ObjectId : " · típus"))) { behaviors.SelectControllerById(controller.Id); ShowCommonRule("controllerId:" + controller.Id); }
            if (ImGui.SmallButton("Sablonként másolom")) { RememberAuthoring(); authoring.Templates.Add(AuthoringOperations.Template(controller)); }
            ImGui.SameLine(); if (ImGui.SmallButton("Vissza az alapvezérléshez")) behaviors.RestoreController(controller.Kind, controller.ObjectId);
            ImGui.PopID();
        }
        }
        EditorStyle.Section("Újrahasználható sablonok");
        foreach (var template in authoring.Templates.Where(t => Match(t.Name)).ToArray()) {
            ImGui.PushID(template.Id);
            string name = template.Name; ImGui.SetNextItemWidth(260); if (ImGui.InputText("##template-name", ref name, 120)) { RememberAuthoring(); template.Name = name; }
            ImGui.SameLine(); if (ImGui.Button("Kötés létrehozása / cseréje")) { templateId = template.Id; templateSpecific = false; ImGui.OpenPopup("template-apply"); }
            ImGui.SameLine(); if (ImGui.Button("Sablon törlése")) { RememberAuthoring(); authoring.Templates.Remove(template); }
            if (ImGui.BeginPopup("template-apply")) {
                bool local = template.Controller != null || WorldBehaviorPolicy.Hooks.FirstOrDefault(h => h.Id == template.Graph?.Hook && h.Kind == template.Graph?.Kind)?.SupportsObjects == true;
                var target = TargetScope(local); ImGui.TextWrapped("A célkötés meglévő szabályát ezzel a független másolattal helyettesíted. A többi objektum egyedi kötése megmarad.");
                ImGui.BeginDisabled(templateSpecific && target == null);
                if (ImGui.Button("Másolat kötése")) { behaviors.UseTemplate(template, target); if (template.Graph != null) OpenLibraryGraph(behaviors.Document.Graphs.Last()); else ShowCommonRule("controllerId:" + behaviors.Document.Controllers.Last().Id); ImGui.CloseCurrentPopup(); }
                ImGui.EndDisabled(); ImGui.EndPopup();
            }
            ImGui.PopID();
        }
        EditorStyle.Section("Import / export a felhasználó és az AI számára");
        ImGui.SetNextItemWidth(-1); ImGui.InputText("##transfer-file", ref transferPath, 1024);
        if (ImGui.Button("Képletek és munkaciklusok importja sablonként")) Try(() => ImportRuleTemplates(transferPath));
        if (ImGui.Button("Sablonok exportja")) Try(() => { RuleProject.WriteAtomic(transferPath, JsonSerializer.Serialize(new { schema = "forest-rule-template-library", version = 1, templates = authoring.Templates }, new JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase, Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() } })); message = "Sablonok exportálva: " + transferPath; });
    }
    private void ImportRuleTemplates(string file)
    {
        using var json = JsonDocument.Parse(File.ReadAllText(file));
        if (!json.RootElement.TryGetProperty("schema", out var schema)) throw new InvalidDataException("Szabályprojekt vagy viselkedésdokumentum szükséges.");
        if (schema.GetString() == "forest-rule-template-library") {
            var templates = JsonSerializer.Deserialize<List<RuleTemplate>>(json.RootElement.GetProperty("templates").GetRawText(), new JsonSerializerOptions { PropertyNameCaseInsensitive = true, Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() } });
            if (templates == null || templates.Count + authoring.Templates.Count > 1024 || templates.Any(t => t == null || t.Graph == null && t.Controller == null)) throw new InvalidDataException("Hibás vagy túl nagy sablontár.");
            RememberAuthoring(); authoring.Templates.AddRange(templates.Select(t => { var copy = t.Graph != null ? AuthoringOperations.Template(t.Graph) : AuthoringOperations.Template(t.Controller); copy.Name = t.Name; return copy; })); message = "Sablontár importálva; aktív kötés nem változott."; return;
        }
        BehaviorModel model = schema.GetString() switch { "forest-rule-project" => RuleProject.FromJson(json.RootElement.GetRawText()).Behaviors,
            "forest-behaviors" => BehaviorModel.FromJson(json.RootElement.GetRawText()), _ => throw new InvalidDataException("Nem támogatott sablonfájl.") };
        if (model.Graphs.Count + model.Controllers.Count + authoring.Templates.Count > 1024) throw new InvalidDataException("Túl sok sablon.");
        RememberAuthoring(); authoring.Templates.AddRange(model.Graphs.Select(AuthoringOperations.Template)); authoring.Templates.AddRange(model.Controllers.Select(AuthoringOperations.Template));
        message = "Sablonok beolvasva. Az aktív kötések és a játék még nem változtak.";
    }
    internal void ShowRuleLibrary() => ShowCommonRule("library");
    internal void ShowThematicStock(string id, int topic) { SetTopic("stock." + id, topic); ShowCommonRule("stock:" + id); }
}
