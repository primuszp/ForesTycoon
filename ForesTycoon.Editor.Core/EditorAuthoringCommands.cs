#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using ForesTycoon.Rules;
namespace ForesTycoon.Editor.Core;

/// <summary>Local template-library commands. Import prepares every copy before changing the document.</summary>
internal sealed class EditorAuthoringCommands(EditorDocument document)
{
    private static readonly JsonSerializerOptions options = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true, Converters = { new JsonStringEnumConverter() } };
    internal IReadOnlyList<RuleTemplate> Templates => document.Authoring.Templates.AsReadOnly();
    internal bool Undo() => document.UndoAuthoring();
    internal bool Redo() => document.RedoAuthoring();
    internal string Notes(string id) => document.Authoring.Entries.TryGetValue(id, out var entry) ? entry.Notes : "";
    internal void SetNotes(string id, string notes) => document.EditAuthoring(authoring => {
        if (!authoring.Entries.TryGetValue(id, out var entry)) authoring.Entries[id] = entry = new();
        entry.Notes = notes;
    });
    internal void Store(BehaviorGraph graph) => Add(new[] { AuthoringOperations.Template(graph) });
    internal void Store(BehaviorController controller) => Add(new[] { AuthoringOperations.Template(controller) });
    internal void SetTopic(string id, int topic) => document.EditAuthoring(authoring => {
        if (!authoring.Entries.TryGetValue(id, out var entry)) authoring.Entries[id] = entry = new();
        entry.Topic = topic;
    });
    internal void Rename(RuleTemplate template, string name)
    {
        Require(template); document.EditAuthoring(_ => template.Name = name);
    }
    internal void Delete(RuleTemplate template)
    {
        Require(template); document.EditAuthoring(authoring => authoring.Templates.Remove(template));
    }
    private void Require(RuleTemplate template)
    {
        if (!document.Authoring.Templates.Contains(template)) throw new InvalidOperationException("A sablon már nem az aktuális könyvtár része.");
    }
    private void Add(IEnumerable<RuleTemplate> templates)
    {
        var prepared = templates.ToList();
        if (prepared.Count + Templates.Count > 1024) throw new InvalidDataException("Túl sok sablon.");
        document.EditAuthoring(authoring => authoring.Templates.AddRange(prepared));
    }
    internal string Export() => JsonSerializer.Serialize(new { schema = "forest-rule-template-library", version = 1, templates = Templates }, options);
    internal void Import(string json)
    {
        using var parsed = JsonDocument.Parse(json);
        if (!parsed.RootElement.TryGetProperty("schema", out var schema)) throw new InvalidDataException("Szabályprojekt vagy viselkedésdokumentum szükséges.");
        List<RuleTemplate> templates;
        if (schema.GetString() == "forest-rule-template-library") {
            var source = JsonSerializer.Deserialize<List<RuleTemplate>>(parsed.RootElement.GetProperty("templates").GetRawText(), options);
            if (source == null || source.Any(t => t == null || t.Graph == null && t.Controller == null)) throw new InvalidDataException("Hibás sablontár.");
            templates = source.Select(t => {
                var copy = t.Graph != null ? AuthoringOperations.Template(t.Graph) : AuthoringOperations.Template(t.Controller!);
                copy.Name = t.Name; return copy;
            }).ToList();
        } else {
            var model = schema.GetString() switch {
                "forest-rule-project" => RuleProject.FromJson(json).Behaviors,
                "forest-behaviors" => BehaviorModel.FromJson(json),
                _ => throw new InvalidDataException("Nem támogatott sablonfájl.")
            };
            templates = model.Graphs.Select(AuthoringOperations.Template).Concat(model.Controllers.Select(AuthoringOperations.Template)).ToList();
        }
        Add(templates);
    }
}
