#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using ForesTycoon.Rules;

namespace ForesTycoon.Editor.Core;

/// <summary>One guarded, archived write path for published projects, drafts and scenes.</summary>
internal sealed class ProjectPersistence
{
    private readonly object gate = new();
    private readonly Dictionary<string, string?> revisions = new(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
    internal Task<string>? SceneSave { get; private set; }

    internal string ReadText(string file)
    {
        string path = Path.GetFullPath(file);
        lock (gate) {
            var snapshot = RuleProjectFile.Read(path);
            if (snapshot.Text == null) throw new FileNotFoundException("A projektfájl nem található.", path);
            revisions[path] = snapshot.Revision;
            return snapshot.Text;
        }
    }

    internal void Observe(string file)
    {
        string path = Path.GetFullPath(file);
        lock (gate) revisions[path] = RuleProjectFile.Revision(path);
    }

    internal void RequireUnchanged(string file, string? expectedFileRevision)
    {
        string path = Path.GetFullPath(file);
        lock (gate) {
            string? expected = ExpectedRevision(path);
            if (expectedFileRevision != null && (expected ?? "missing") != expectedFileRevision)
                throw new IOException("A célprojekt verziója eltér az elvárttól; nem írtam felül.");
            RuleProjectFile.RequireRevision(path, expected);
        }
    }

    internal string Save(string file, RuleProject project)
    {
        string path = Path.GetFullPath(file), json = project.ToJson();
        lock (gate) return Write(path, json, ExpectedRevision(path));
    }

    internal void BeginSceneSave(string file, RuleProject project, Action<RuleProject> validate)
    {
        lock (gate) {
            EnsureSceneIdle();
            string path = Path.GetFullPath(file);
            string? expected = ExpectedRevision(path);
            // Capture both the document and the expected file revision before background validation begins.
            var snapshot = project.Clone();
            SceneSave = Task.Run(() => {
                validate(snapshot);
                string json = snapshot.ToJson();
                lock (gate) return Write(path, json, expected);
            });
        }
    }

    internal void EnsureSceneIdle()
    {
        if (SceneSave != null) throw new InvalidOperationException("A jelenet mentése még folyamatban van. Várd meg a befejezését az újabb mentés vagy megnyitás előtt.");
    }

    internal string? CompleteSceneSave()
    {
        var task = SceneSave;
        if (task == null) return null;
        try { return task.GetAwaiter().GetResult(); }
        finally { SceneSave = null; }
    }

    private string? ExpectedRevision(string path)
    {
        if (!revisions.TryGetValue(path, out string? revision)) revisions[path] = revision = RuleProjectFile.Revision(path);
        return revision;
    }

    private string Write(string path, string json, string? expected)
    {
        revisions[path] = RuleProjectFile.Write(path, json, expected);
        return path;
    }
}
