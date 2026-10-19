using System.Diagnostics;
using System.Text.Json;
using System.Xml.Linq;
using System.Security.Cryptography;
using System.Text;
using ForesTycoon.Rules;

namespace ForesTycoon.RuleCompiler;

public sealed record AlgorithmBuildResult(string Revision, string AssemblyPath, string Log);
public static class AlgorithmBuild
{
    public static string CacheRoot => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ForesTycoon", "algorithm-runtime");
    public static string CurrentRevision {
        get {
            string path = Path.Combine(AppContext.BaseDirectory, "algorithm-runtime.json");
            return File.Exists(path) ? JsonSerializer.Deserialize<AlgorithmGraphReference>(File.ReadAllText(path))?.Revision ?? "" : "";
        }
    }
    public static string? FindWorkspace(string? start = null)
    {
        return FindWorkspaceCore(start);
    }
    public static AlgorithmGraphReference? CurrentReference {
        get {
            string path = Path.Combine(AppContext.BaseDirectory, "algorithm-runtime.json");
            return File.Exists(path) ? JsonSerializer.Deserialize<AlgorithmGraphReference>(File.ReadAllText(path)) : null;
        }
    }
    private static string? FindWorkspaceCore(string? start)
    {
        var directory = new DirectoryInfo(start ?? AppContext.BaseDirectory);
        while (directory != null) {
            if (File.Exists(Path.Combine(directory.FullName, "ForesTycoon.sln"))) return directory.FullName;
            directory = directory.Parent;
        }
        return null;
    }
    public static AlgorithmGraphReference Store(AlgorithmBundle bundle, string workspace, string projectPath)
    {
        string revision = bundle.Revision(); string folder = Path.GetFullPath(projectPath) + ".algorithms";
        string json = bundle.ToJson();
        string layout = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json)))[..16].ToLowerInvariant();
        string path = Path.Combine(folder, revision + "-" + layout + ".json");
        if (!File.Exists(path)) RuleProject.WriteAtomic(path, json);
        return new() { Revision = revision, BundlePath = path, WorkspaceRoot = Path.GetFullPath(workspace) };
    }
    public static AlgorithmBundle Read(AlgorithmGraphReference reference)
    {
        var bundle = AlgorithmBundle.FromJson(File.ReadAllText(reference.BundlePath));
        if (reference.Revision.Length != 64 || bundle.Revision() != reference.Revision) throw new InvalidDataException("Az algoritmusgráf tartalma nem egyezik a projekt revíziójával.");
        return bundle;
    }
    /// <summary>
    /// A bundle that no longer describes the code: some source file is missing from it (it predates the file), it cannot
    /// be read, or it renders exactly the current sources (a plain snapshot). Such a bundle must not replace the built game:
    /// running it would silently revert later code changes. Returns the reason, or null for a real, edited bundle.
    /// </summary>
    public static string? StaleReason(AlgorithmGraphReference? reference)
    {
        if (reference == null || reference.Revision == CurrentRevision) return null;
        try {
            var rendered = Read(reference).RenderAll();
            string workspace = Path.GetFullPath(reference.WorkspaceRoot);
            if (!File.Exists(Path.Combine(workspace, "ForesTycoon/ForesTycoon.csproj"))) return "a munkaterület nem található";
            var sources = AlgorithmBundle.SourcePaths(workspace).ToArray();
            var missing = sources.Where(p => !rendered.ContainsKey(p)).ToArray();
            if (missing.Length > 0) return "régebbi a kódnál; hiányzik belőle: " + string.Join(", ", missing.Take(4)) + (missing.Length > 4 ? " …" : "");
            static string Normal(string text) => text.Replace("\r\n", "\n").TrimEnd();
            if (sources.All(p => Normal(rendered[p]) == Normal(File.ReadAllText(Path.Combine(workspace, p))))) return "a jelenlegi kód változatlan másolata";
            return null;
        } catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException or System.Text.Json.JsonException) {
            return "nem olvasható: " + e.Message;
        }
    }
    public static async Task<AlgorithmBuildResult> CompileAsync(AlgorithmGraphReference reference, CancellationToken cancellation = default)
    {
        var bundle = Read(reference); var rendered = bundle.RenderAll();
        string workspace = Path.GetFullPath(reference.WorkspaceRoot);
        if (!File.Exists(Path.Combine(workspace, "ForesTycoon/ForesTycoon.csproj"))) throw new InvalidDataException("A fordításhoz hiányzik a ForesTycoon munkaterület.");
        var missing = AlgorithmBundle.SourcePaths(workspace).Where(p => !rendered.ContainsKey(p)).Take(8).ToArray();
        if (missing.Length > 0) throw new InvalidDataException("A teljes algoritmusprojektből gráfok hiányoznak: " + string.Join(", ", missing));
        // Host changes must rebuild even when gameplay graph semantics stay identical.
        using var hostHash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (string name in AlgorithmBundle.Projects.Append("ForesTycoon.RuleCompiler"))
            foreach (string path in Directory.EnumerateFiles(Path.Combine(workspace, name), "*", SearchOption.AllDirectories)
                .Where(p => p.EndsWith(".cs") || p.EndsWith(".csproj")).Order(StringComparer.Ordinal)) {
                string relative = Path.GetRelativePath(workspace, path).Replace('\\', '/');
                if (relative.Split('/').Any(p => p is "obj" or "bin") || rendered.ContainsKey(relative)) continue;
                hostHash.AppendData(Encoding.UTF8.GetBytes(relative + "\0" + File.ReadAllText(path)));
            }
        foreach (string path in new[] { "Directory.Build.props", "Directory.Build.targets", "global.json", "NuGet.Config", "ForesTycoon.Editor/GameRules/default.project.json" })
            if (File.Exists(Path.Combine(workspace, path))) hostHash.AppendData(File.ReadAllBytes(Path.Combine(workspace, path)));
        string hostRevision = Convert.ToHexString(hostHash.GetHashAndReset())[..16].ToLowerInvariant();
        string directory = Path.Combine(CacheRoot, reference.Revision + "-" + hostRevision);
        Directory.CreateDirectory(directory);
        // One builder per revision, including independent editor/game processes.
        FileStream? gate = null;
        for (int attempt = 0; gate == null; attempt++) {
            cancellation.ThrowIfCancellationRequested();
            try { gate = new FileStream(Path.Combine(directory, "build.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
            catch (IOException) { if (attempt >= 600) throw; await Task.Delay(500, cancellation); }
        }
        using (gate) {
            string assembly = Path.Combine(directory, "bin", "ForesTycoon", "Release", "net8.0", "ForesTycoon.dll");
            string marker = Path.Combine(Path.GetDirectoryName(assembly)!, "algorithm-runtime.json");
            if (File.Exists(assembly) && File.Exists(marker)) return new(reference.Revision, assembly, "Gyorsítótárból betöltve.");
            string generated = Path.Combine(directory, "graphs");
            foreach (var pair in rendered) {
                string path = Path.Combine(generated, pair.Key); Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllText(path, pair.Value);
            }
            string props = Path.Combine(directory, "Graph.props"), targets = Path.Combine(directory, "Graph.targets");
            var properties = new XElement("Project", new XElement("PropertyGroup",
                new XElement("BaseIntermediateOutputPath", Path.Combine(directory, "obj") + "/$(MSBuildProjectName)/"),
                new XElement("MSBuildProjectExtensionsPath", Path.Combine(directory, "obj") + "/$(MSBuildProjectName)/"),
                new XElement("BaseOutputPath", Path.Combine(directory, "bin") + "/$(MSBuildProjectName)/"),
                new XElement("DefaultItemExcludes", "$(DefaultItemExcludes);$(MSBuildProjectDirectory)/bin/**;$(MSBuildProjectDirectory)/obj/**")));
            string originalProps = Path.Combine(workspace, "Directory.Build.props");
            if (File.Exists(originalProps)) properties.AddFirst(new XElement("Import", new XAttribute("Project", originalProps)));
            File.WriteAllText(props, properties.ToString());
            var project = new XElement("Project");
            string originalTargets = Path.Combine(workspace, "Directory.Build.targets");
            if (File.Exists(originalTargets)) project.Add(new XElement("Import", new XAttribute("Project", originalTargets)));
            var target = new XElement("Target", new XAttribute("Name", "UseAlgorithmGraphs"), new XAttribute("BeforeTargets", "AssignTargetPaths;CoreCompile"));
            foreach (var group in rendered.GroupBy(p => p.Key.Split('/')[0])) {
                var items = new XElement("ItemGroup", new XAttribute("Condition", "'$(MSBuildProjectName)' == '" + group.Key + "'"));
                foreach (var pair in group) items.Add(new XElement(pair.Key.EndsWith(".cs") ? "_GraphCodeOriginals" : "_GraphDataOriginals",
                    new XAttribute("Include", Path.GetFullPath(Path.Combine(workspace, pair.Key)))));
                items.Add(new XElement("Compile", new XAttribute("Remove", "@(_GraphCodeOriginals)"), new XAttribute("MatchOnMetadata", "FullPath"), new XAttribute("MatchOnMetadataOptions", "PathLike")));
                items.Add(new XElement("EmbeddedResource", new XAttribute("Remove", "@(_GraphDataOriginals)"), new XAttribute("MatchOnMetadata", "FullPath"), new XAttribute("MatchOnMetadataOptions", "PathLike")));
                foreach (var pair in group) {
                    string relative = pair.Key[(group.Key.Length + 1)..]; string source = Path.Combine(generated, pair.Key);
                    bool code = relative.EndsWith(".cs"); string item = code ? "Compile" : "EmbeddedResource";
                    var include = new XElement(item, new XAttribute("Include", source), new XElement("Link", relative));
                    if (!code) include.Add(new XElement("LogicalName", group.Key + "." + relative.Replace('/', '.')));
                    items.Add(include);
                }
                target.Add(items);
            }
            project.Add(target); File.WriteAllText(targets, project.ToString());
            var info = new ProcessStartInfo("dotnet") { WorkingDirectory = workspace, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
            // This is a captured child process, not an interactive build server. Reused
            // MSBuild workers can retain its redirected pipes after the build exits.
            foreach (string arg in new[] { "build", "ForesTycoon/ForesTycoon.csproj", "-c", "Release", "--nologo", "-v:q", "-m:1", "-nr:false", "-p:DirectoryBuildPropsPath=" + props,
                "-p:DirectoryBuildTargetsPath=" + targets, "-p:UseSharedCompilation=false" }) info.ArgumentList.Add(arg);
            using var process = Process.Start(info) ?? throw new IOException("A gráffordító nem indult el.");
            var output = process.StandardOutput.ReadToEndAsync(cancellation); var error = process.StandardError.ReadToEndAsync(cancellation);
            try { await process.WaitForExitAsync(cancellation); }
            catch { if (!process.HasExited) process.Kill(true); throw; }
            string log = await output + "\n" + await error;
            File.WriteAllText(Path.Combine(directory, "build.log"), log);
            if (process.ExitCode != 0 || !File.Exists(assembly)) throw new InvalidDataException("A gráf nem fordítható.\n" + log[..Math.Min(log.Length, 12000)]);
            RuleProject.WriteAtomic(marker, JsonSerializer.Serialize(reference));
            return new(reference.Revision, assembly, log);
        }
    }
    public static Process StartRuntime(AlgorithmBuildResult runtime, IEnumerable<string> arguments, string projectPath)
    {
        var info = new ProcessStartInfo("dotnet") { WorkingDirectory = Path.GetDirectoryName(runtime.AssemblyPath)!, UseShellExecute = false, CreateNoWindow = true };
        info.ArgumentList.Add(runtime.AssemblyPath);
        foreach (string arg in arguments) info.ArgumentList.Add(arg);
        info.Environment["FORESTYCOON_RULE_PROJECT"] = Path.GetFullPath(projectPath);
        return Process.Start(info) ?? throw new IOException("A gráfokból épített játék nem indult el.");
    }
}
