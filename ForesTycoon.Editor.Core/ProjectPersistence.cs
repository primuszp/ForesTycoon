#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
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
        Observe(file); string text = File.ReadAllText(file); RequireUnchanged(file); return text;
    }

    internal void Observe(string file)
    {
        string path = Path.GetFullPath(file);
        lock (gate) revisions[path] = ProjectFileGuard.Revision(path);
    }

    internal void RequireUnchanged(string file)
    {
        string path = Path.GetFullPath(file);
        lock (gate) RequireUnchanged(path, ExpectedRevision(path));
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
        if (!revisions.TryGetValue(path, out string? revision)) revisions[path] = revision = ProjectFileGuard.Revision(path);
        return revision;
    }

    private static void RequireUnchanged(string path, string? expected)
    {
        if (ProjectFileGuard.Revision(path) != expected)
            throw new IOException("A fájlt közben másik editor vagy AI módosította. Nyisd meg az új változatot, vagy ments másik útvonalra. Nem írtam felül: " + path);
    }

    private string Write(string path, string json, string? expected)
    {
        RequireUnchanged(path, expected);
        ProjectHistory.Archive(path);
        RequireUnchanged(path, expected);
        RuleProject.WriteAtomic(path, json);
        revisions[path] = ProjectFileGuard.Revision(path);
        return path;
    }
}

/// <summary>Detect external human/agent edits before overwriting the shared project or draft.</summary>
internal sealed class ProjectFileGuard
{
    private string? path, revision;
    internal static string? Revision(string file) => File.Exists(file) ? Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file))) : null;
    internal void Observe(string file) { path = Path.GetFullPath(file); revision = Revision(path); }
    internal bool Changed(string file) => path != Path.GetFullPath(file) || Revision(file) != revision;
    internal void RequireUnchanged(string file) { if (Changed(file)) throw new IOException("A fájlt közben másik editor vagy AI módosította. Nyisd meg az új változatot, vagy ments másik projektútvonalra. Nem írtam felül: " + file); }
}

internal static class ProjectHistory
{
    internal static void Archive(string file)
    {
        if (!File.Exists(file)) return;
        string json = File.ReadAllText(file); string hash = Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(json)));
        string saved = Path.Combine(Path.GetFullPath(file) + ".history", hash + ".json");
        if (!File.Exists(saved)) RuleProject.WriteAtomic(saved, json);
    }
}
