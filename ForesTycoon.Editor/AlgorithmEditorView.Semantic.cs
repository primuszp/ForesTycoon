using System;
using System.Collections.Generic;
using System.Linq;
using ForesTycoon.RuleCompiler;
using ImGuiNET;

namespace ForesTycoon.Editor;

internal sealed partial class AlgorithmEditorView
{
    private readonly Dictionary<string, string> valueDrafts = new();
    private string operatorMessage;
    private int semanticTemplate;
    private string procedureName = "SajatSzabaly";
    private int procedureResult;
    private void DrawValue(AlgorithmNode value)
    {
        string text = valueDrafts.TryGetValue(value.Id, out string edited) ? edited : value.Text;
        ImGui.TextWrapped(AlgorithmSemantics.Name(value)); ImGui.SetNextItemWidth(-1);
        bool commit = ImGui.InputText("##value-" + value.Id, ref text, 2048, ImGuiInputTextFlags.EnterReturnsTrue);
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("Enter vagy a mező elhagyása rögzíti. Számnál tizedespontot használj: 1.1.");
        valueDrafts[value.Id] = text;
        if (commit || ImGui.IsItemDeactivatedAfterEdit()) {
            string old = value.Text;
            try {
                AlgorithmSemantics.SetValue(value, text); string next = value.Text; value.Text = old;
                if (next != old) Edit(() => value.Text = next);
                operatorMessage = null; valueDrafts.Remove(value.Id);
            } catch (Exception e) { value.Text = old; operatorMessage = e.Message; }
        }
    }
    private void SemanticInspector()
    {
        if (!index.TryGetValue(selected, out var node)) return;
        ImGui.TextWrapped(AlgorithmSemantics.Name(node));
        ImGui.TextWrapped(AlgorithmSemantics.Summary(node.Id, index, 240));
        if (operatorMessage != null) ImGui.TextWrapped(operatorMessage);
        if (focus != selected && ImGui.Button("Művelet / részgráf megnyitása")) { Enter(selected); valueDrafts.Clear(); }
        if (ImGui.Button("Ezt használom kapcsolatforrásként")) clipboard = node.Id;
        if (ImGui.Button("Független másolat készítése")) Edit(() => { clipboard = AlgorithmBundle.Duplicate(file, node.Id); selected = clipboard; });
        ImGui.TextWrapped("Kapcsolatforrás: " + (clipboard != null && index.TryGetValue(clipboard, out var source) ? AlgorithmSemantics.Summary(source.Id, index, 55) : "nincs"));
        var operators = AlgorithmSemantics.Operators(node);
        if (operators.Length > 0 && node.Children.Count == 3 && ImGui.BeginCombo("Számítás / feltétel", index[node.Children[1]].Text)) {
            foreach (string operation in operators) if (ImGui.Selectable(operation, index[node.Children[1]].Text == operation)) Edit(() => AlgorithmSemantics.SetOperator(node, index, operation));
            ImGui.EndCombo();
        }
        if (node.Kind == "PredefinedType" && node.Children.Count == 1 && ImGui.BeginCombo("Adattípus", index[node.Children[0]].Text)) {
            foreach (string type in AlgorithmSemantics.Types) if (ImGui.Selectable(type)) Edit(() => AlgorithmSemantics.SetType(node, index, type));
            ImGui.EndCombo();
        }
        if (node.Kind is "TrueLiteralExpression" or "FalseLiteralExpression") {
            bool flag = node.Kind == "TrueLiteralExpression";
            if (ImGui.Checkbox("Igaz / hamis", ref flag)) Edit(() => { node.Kind = flag ? "TrueLiteralExpression" : "FalseLiteralExpression"; var token = index[node.Children[0]]; token.Kind = flag ? "TrueKeyword" : "FalseKeyword"; token.Text = flag ? "true" : "false"; });
        }
        if (node.Kind is "JsonTrue" or "JsonFalse") { bool flag = node.Kind == "JsonTrue"; if (ImGui.Checkbox("Igaz / hamis", ref flag)) Edit(() => { node.Kind = flag ? "JsonTrue" : "JsonFalse"; node.Text = flag ? "true" : "false"; }); }
        if (node.Kind != "Block") foreach (var value in AlgorithmSemantics.Values(node.Id, index)) { ImGui.PushID(value.Id); DrawValue(value); ImGui.PopID(); }
        ImGui.Separator();
        foreach (var port in AlgorithmSemantics.Ports(node, index)) {
            ImGui.PushID(port.Position); ImGui.TextWrapped(port.Name);
            if (ImGui.Selectable(AlgorithmSemantics.Name(index[port.Target]) + " · " + AlgorithmSemantics.Summary(port.Target, index, 45))) { selected = port.Target; valueDrafts.Clear(); }
            bool canLink = clipboard != null && AlgorithmSemantics.CanConnect(port.Owner, clipboard, index);
            ImGui.BeginDisabled(!canLink);
            if (ImGui.SmallButton("Forrással helyettesítés")) Edit(() => index[port.Owner].Children[port.Position] = clipboard);
            ImGui.EndDisabled();
            if (node.Kind is "Block" or "JsonArray" or "JsonObject") {
                if (ImGui.SmallButton("Elem eltávolítása")) { Edit(() => node.Children.RemoveAt(port.Position)); ImGui.PopID(); break; }
            }
            ImGui.PopID();
        }
        if (node.Kind == "Block" && file.Format == "csharp") {
            ImGui.Separator();
            string[] labels = { "Értékadás", "Feltételes elágazás", "Ismétlés", "Objektumok bejárása", "Eljáráshívás", "Visszatérés", "Változó" };
            int[] templates = { 4, 5, 6, 7, 8, 9, 10 };
            ImGui.SetNextItemWidth(-1); ImGui.Combo("##semantic-template", ref semanticTemplate, labels, labels.Length);
            if (ImGui.Button("Művelet beszúrása ebbe a blokkba")) InsertSemanticOperation(node.Id, templates[semanticTemplate]);
            if (clipboard != null && AlgorithmSemantics.CanConnect(node.Id, clipboard, index) && ImGui.Button("Kijelölt forrás beszúrása"))
                Edit(() => node.Children.Insert(Math.Max(1, node.Children.Count - 1), clipboard));
        }
        if (node.Kind is "JsonObject" or "JsonArray") {
            if (ImGui.Button(node.Kind == "JsonObject" ? "Új adatmező" : "Új listaelem")) Edit(() => {
                var value = new AlgorithmNode { Id = "g" + Guid.NewGuid().ToString("N"), Kind = "JsonNumber", Text = "0" }; file.Nodes.Add(value);
                if (node.Kind == "JsonObject") { var property = new AlgorithmNode { Id = "g" + Guid.NewGuid().ToString("N"), Kind = "JsonProperty", Text = "ujMezo", Children = new() { value.Id } }; file.Nodes.Add(property); node.Children.Add(property.Id); selected = property.Id; }
                else { node.Children.Add(value.Id); selected = value.Id; }
            });
        }
        if (node.Kind is "ClassDeclaration" or "StructDeclaration") {
            ImGui.Separator(); ImGui.SetNextItemWidth(-1); ImGui.InputTextWithHint("##procedure-name", "Saját eljárás neve", ref procedureName, 120);
            string[] types = { "double", "bool", "void" }; ImGui.SetNextItemWidth(-1); ImGui.Combo("##procedure-result", ref procedureResult, types, 3);
            if (ImGui.Button("Új saját eljárás hozzáadása")) {
                try { Edit(() => selected = AlgorithmSemantics.InsertProcedure(file, node.Id, procedureName, types[procedureResult])); operatorMessage = null; }
                catch (Exception e) { operatorMessage = e.Message; }
            }
            ImGui.TextWrapped("Az új eljárás gráfja külön szerkeszthető. A működő folyamatba híváscsomóponttal kapcsold be.");
        }
        if (ImGui.Button("Teljes gráf és minden port")) semantic = false;
        ImGui.TextWrapped("A nézet ugyanazokat a csomópontokat szerkeszti. A fordítás ellenőrzi az eljárások és típusok helyességét.");
    }
    internal void InsertSemanticOperation(string blockId, int templateId)
    {
        var block = index[blockId]; if (block.Kind != "Block") throw new ArgumentException("Műveletet utasításblokkba lehet beszúrni.");
        Edit(() => { string added = AlgorithmBundle.InsertTemplate(file, templateId); block.Children.Insert(Math.Max(1, block.Children.Count - 1), added); selected = added; clipboard = added; });
    }
}
