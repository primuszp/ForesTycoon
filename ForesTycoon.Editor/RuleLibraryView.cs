using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using ForesTycoon.Rules;
using ImGuiNET;

namespace ForesTycoon.Editor;

internal sealed class RuleLibraryView(RuleLibraryContext context)
{
    private RuleWorkspaceUiState state => context.State;
    private BehaviorEditorView behaviors => context.Behaviors;
    private string message { set => context.SetMessage(value); }
    private bool Try(Action action) => context.Try(action);
    private void ShowCommonRule(string key) => context.OpenRule(key);
    private ref bool templateSpecific => ref state.TemplateSpecific;
    private ref string librarySearch => ref state.LibrarySearch;
    private ref string templateTarget => ref state.TemplateTarget;
    private ref string templateId => ref state.TemplateId;
    private ref string transferPath => ref state.TransferPath;
    private void AuthoringHistory(bool redo)
    {
        if (redo) context.Library.Redo(); else context.Library.Undo();
    }
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
    internal void Draw()
    {
        ImGui.TextWrapped("Aktív kötések és újrahasználható sablonok. A sablon másolásával nem keletkezik második, ütköző típusszabály.");
        ImGui.SetNextItemWidth(-1); ImGui.InputTextWithHint("##library-search", "Név, folyamat, típus vagy objektumazonosító", ref librarySearch, 160);
        bool Match(string value) => value.Contains(librarySearch, StringComparison.OrdinalIgnoreCase);
        if (ImGui.Button("Besorolás / sablon visszavonása")) AuthoringHistory(false);
        ImGui.SameLine(); if (ImGui.Button("Újra##library")) AuthoringHistory(true);
        if (ImGui.CollapsingHeader("Aktív képletek")) {
        foreach (var graph in behaviors.Document.Graphs.Where(g => Match(g.Name + " " + g.Hook + " " + g.Kind + " " + g.ObjectId)).ToArray()) {
            ImGui.PushID(graph.Id);
            if (ImGui.Selectable(graph.Name + " · " + RuleWorkshopModel.KindName(graph.Kind) + (graph.ObjectId.HasValue ? " #" + graph.ObjectId : " · típus"))) OpenLibraryGraph(graph);
            if (ImGui.SmallButton("Sablonként másolom")) { context.Library.Store(graph); }
            ImGui.SameLine(); if (ImGui.SmallButton(graph.ObjectId.HasValue ? "Egyedi felülírás eltávolítása" : EcologyResponseProgram.IsPureKind(graph.Kind) ? "Alapgráf visszaállítása" : "Beépített alap visszaállítása")) behaviors.RestoreDefault(graph.Hook, graph.Kind, graph.ObjectId);
            ImGui.PopID();
        }
        }
        if (ImGui.CollapsingHeader("Aktív munkaciklusok")) {
        foreach (var controller in behaviors.Document.Controllers.Where(c => Match(c.Name + " " + c.Kind + " " + c.ObjectId)).ToArray()) {
            ImGui.PushID(controller.Id);
            if (ImGui.Selectable(controller.Name + (controller.ObjectId.HasValue ? " #" + controller.ObjectId : " · típus"))) { behaviors.SelectControllerById(controller.Id); ShowCommonRule("controllerId:" + controller.Id); }
            if (ImGui.SmallButton("Sablonként másolom")) { context.Library.Store(controller); }
            ImGui.SameLine(); if (ImGui.SmallButton("Vissza az alapvezérléshez")) behaviors.RestoreController(controller.Kind, controller.ObjectId);
            ImGui.PopID();
        }
        }
        EditorStyle.Section("Újrahasználható sablonok");
        foreach (var template in context.Library.Templates.Where(t => Match(t.Name)).ToArray()) {
            ImGui.PushID(template.Id);
            string name = template.Name; ImGui.SetNextItemWidth(260); if (ImGui.InputText("##template-name", ref name, 120)) { context.Library.Rename(template, name); }
            ImGui.SameLine(); if (ImGui.Button("Kötés létrehozása / cseréje")) { templateId = template.Id; templateSpecific = false; ImGui.OpenPopup("template-apply"); }
            ImGui.SameLine(); if (ImGui.Button("Sablon törlése")) { context.Library.Delete(template); }
            if (ImGui.BeginPopup("template-apply")) {
                bool local = template.SupportsObjects;
                var target = TargetScope(local); ImGui.TextWrapped("A célkötés meglévő szabályát ezzel a független másolattal helyettesíted. A többi objektum egyedi kötése megmarad.");
                ImGui.BeginDisabled(templateSpecific && target == null);
                if (ImGui.Button("Másolat kötése")) { behaviors.UseTemplate(template.Capture(), target); if (template.HasGraph) OpenLibraryGraph(behaviors.Document.Graphs.Last()); else ShowCommonRule("controllerId:" + behaviors.Document.Controllers.Last().Id); ImGui.CloseCurrentPopup(); }
                ImGui.EndDisabled(); ImGui.EndPopup();
            }
            ImGui.PopID();
        }
        EditorStyle.Section("Import / export a felhasználó és az AI számára");
        ImGui.SetNextItemWidth(-1); ImGui.InputText("##transfer-file", ref transferPath, 1024);
        if (ImGui.Button("Képletek és munkaciklusok importja sablonként")) Try(() => ImportRuleTemplates(transferPath));
        if (ImGui.Button("Sablonok exportja")) Try(() => { RuleProject.WriteAtomic(transferPath, context.Library.Export()); message = "Sablonok exportálva: " + transferPath; });
    }
    private void ImportRuleTemplates(string file)
    {
        context.Library.Import(File.ReadAllText(file));
        message = "Sablonok beolvasva. Az aktív kötések és a játék még nem változtak.";
    }
}
