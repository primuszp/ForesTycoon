using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Security.Cryptography;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace ForesTycoon.RuleCompiler;

// Structural graphs expose the complete algorithms. Source text is a compiler product,
// never an authoring field: control flow, expressions, declarations and tokens have IDs.
public sealed class AlgorithmNode
{
    public string Id { get; set; } = "";
    public string Kind { get; set; } = "";
    public bool Token { get; set; }
    public string Text { get; set; } = "";
    public string Leading { get; set; } = "";
    public string Trailing { get; set; } = "";
    public List<string> Children { get; set; } = new();
    public float X { get; set; }
    public float Y { get; set; }
}
public sealed record AlgorithmEntry(string Id, string Name);
public sealed class AlgorithmFile
{
    public string Path { get; set; } = "";
    public string Format { get; set; } = "csharp";
    public string Root { get; set; } = "";
    public List<AlgorithmEntry> Entries { get; set; } = new();
    public List<AlgorithmNode> Nodes { get; set; } = new();
    public Dictionary<string, AlgorithmNode> Index() => Nodes.ToDictionary(n => n.Id, StringComparer.Ordinal);
}
public sealed class AlgorithmBundle
{
    public string Schema { get; set; } = "forest-algorithm-graphs";
    public int Version { get; set; } = 1;
    public string BaseRevision { get; set; } = "";
    public List<AlgorithmFile> Files { get; set; } = new();
    private static readonly JsonSerializerOptions Options = new() {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault
    };
    public string ToJson() => JsonSerializer.Serialize(this, Options);
    public static AlgorithmBundle FromJson(string json) => JsonSerializer.Deserialize<AlgorithmBundle>(json, Options)
        ?? throw new InvalidDataException("Hiányzó algoritmusgráf.");
    public AlgorithmBundle Clone() => FromJson(ToJson());
    public static AlgorithmFile Import(string path, string text)
    {
        var file = new AlgorithmFile { Path = path.Replace('\\', '/') };
        int next = 0;
        string Add(SyntaxNodeOrToken element) {
            var node = new AlgorithmNode { Id = "n" + next++, Kind = element.Kind().ToString(), Token = element.IsToken };
            file.Nodes.Add(node);
            if (element.IsToken) {
                var token = element.AsToken(); node.Text = token.Text;
                node.Leading = token.LeadingTrivia.ToFullString(); node.Trailing = token.TrailingTrivia.ToFullString();
            } else {
                foreach (var child in element.AsNode()!.ChildNodesAndTokens()) node.Children.Add(Add(child));
                if (element.AsNode() is MemberDeclarationSyntax member) {
                    string? name = member switch {
                        BaseMethodDeclarationSyntax m => m switch { MethodDeclarationSyntax method => method.Identifier.Text,
                            ConstructorDeclarationSyntax ctor => ctor.Identifier.Text + " (létrehozás)", _ => m.Kind().ToString() },
                        BaseTypeDeclarationSyntax type => type.Identifier.Text,
                        PropertyDeclarationSyntax property => property.Identifier.Text,
                        FieldDeclarationSyntax field => string.Join(", ", field.Declaration.Variables.Select(v => v.Identifier.Text)),
                        _ => null
                    };
                    if (name != null) file.Entries.Add(new(node.Id, name));
                }
            }
            return node.Id;
        }
        file.Root = Add(CSharpSyntaxTree.ParseText(text).GetRoot()); return file;
    }
    public static AlgorithmFile ImportData(string path, string json)
    {
        using var document = JsonDocument.Parse(json);
        var file = new AlgorithmFile { Path = path.Replace('\\', '/'), Format = "json" }; int next = 0;
        string Add(JsonElement value, string name) {
            var node = new AlgorithmNode { Id = "n" + next++, Kind = "Json" + value.ValueKind, Text = name };
            file.Nodes.Add(node);
            if (value.ValueKind == JsonValueKind.Object) foreach (var property in value.EnumerateObject()) {
                var key = new AlgorithmNode { Id = "n" + next++, Kind = "JsonProperty", Text = property.Name };
                file.Nodes.Add(key); key.Children.Add(Add(property.Value, property.Name)); node.Children.Add(key.Id);
            }
            else if (value.ValueKind == JsonValueKind.Array) foreach (var item in value.EnumerateArray()) node.Children.Add(Add(item, ""));
            else node.Text = value.ValueKind == JsonValueKind.String ? value.GetString()! : value.GetRawText();
            return node.Id;
        }
        file.Root = Add(document.RootElement, ""); file.Entries.Add(new(file.Root, "Adatdefiníció")); return file;
    }
    public static readonly string[] Projects = { "ForesTycoon", "ForesTycoon.Simulation", "ForesTycoon.Ecology", "ForesTycoon.Map", "ForesTycoon.Engine",
        "ForesTycoon.TreeModels", "ForesTycoon.Effects", "ForesTycoon.Models", "ForesTycoon.Rendering", "ForesTycoon.Rules" };
    public static AlgorithmBundle ImportWorkspace(string root)
    {
        root = System.IO.Path.GetFullPath(root); var bundle = new AlgorithmBundle();
        foreach (string relative in SourcePaths(root))
            bundle.Files.Add(relative.EndsWith(".cs") ? Import(relative, File.ReadAllText(System.IO.Path.Combine(root, relative)))
                : ImportData(relative, File.ReadAllText(System.IO.Path.Combine(root, relative))));
        bundle.BaseRevision = SourceRevision(bundle.RenderAll());
        return bundle;
    }
    internal static string WorkspaceRevision(string root) => SourceRevision(SourcePaths(root)
        .Select(path => new KeyValuePair<string, string>(path, File.ReadAllText(System.IO.Path.Combine(root, path)))));

    internal static string SourceRevision(IEnumerable<KeyValuePair<string, string>> sources)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var pair in sources.OrderBy(pair => pair.Key, StringComparer.Ordinal)) {
            string text = pair.Key.EndsWith(".json", StringComparison.Ordinal) ? Render(ImportData(pair.Key, pair.Value)) : pair.Value;
            string content = text.Replace("\r\n", "\n").TrimEnd();
            hash.AppendData(Encoding.UTF8.GetBytes(pair.Key + "\0" + content + "\0"));
        }
        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }
    public static IEnumerable<string> SourcePaths(string root)
    {
        foreach (string project in Projects) {
            string directory = System.IO.Path.Combine(root, project);
            foreach (string path in Directory.EnumerateFiles(directory, "*.cs", SearchOption.AllDirectories).Order()) {
                string relative = System.IO.Path.GetRelativePath(root, path).Replace('\\', '/');
                if (relative.Split('/').Any(s => s is "obj" or "bin")) continue;
                // The host, compiler and editor form the stable execution boundary.
                if (project == "ForesTycoon") continue;
                if (relative.EndsWith("/RuleProjectLink.cs") || relative.EndsWith("/GameWorld.RuleProject.cs") || relative.EndsWith("/SimulationWorld.RuleProject.cs")) continue;
                yield return relative;
            }
        }
        foreach (string path in new[] { "ForesTycoon.Ecology/Soil/profiles.json", "ForesTycoon.Ecology/Climate/default.json" })
            yield return path;
    }
    public static string Render(AlgorithmFile file)
    {
        if (file.Nodes == null || file.Nodes.Count > 250000) throw new InvalidDataException("Túl nagy algoritmusgráf.");
        Dictionary<string, AlgorithmNode> index;
        try { index = file.Index(); } catch (ArgumentException) { throw new InvalidDataException("Ismétlődő csomópontazonosító."); }
        var active = new HashSet<string>(); var output = new StringBuilder(); int visits = 0;
        void Visit(string id, int depth) {
            if (++visits > 1000000 || depth > 512 || !index.TryGetValue(id, out var node) || !active.Add(id))
                throw new InvalidDataException("Hiányzó kapcsolat, ciklus vagy túl mély gráf: " + id);
            if (!float.IsFinite(node.X) || !float.IsFinite(node.Y) || node.Children == null) throw new InvalidDataException("Hibás csomópont.");
            if (file.Format == "json") {
                switch (node.Kind) {
                    case "JsonObject": output.Append('{'); Children(","); output.Append('}'); break;
                    case "JsonArray": output.Append('['); Children(","); output.Append(']'); break;
                    case "JsonProperty":
                        if (node.Children.Count != 1) throw new InvalidDataException("Az adatmező egy értéket kér.");
                        output.Append(JsonSerializer.Serialize(node.Text)).Append(':'); Children(""); break;
                    case "JsonString": output.Append(JsonSerializer.Serialize(node.Text)); break;
                    case "JsonNumber":
                        using (var value = JsonDocument.Parse(node.Text))
                            if (value.RootElement.ValueKind != JsonValueKind.Number) throw new InvalidDataException("Hibás szám.");
                        output.Append(node.Text); break;
                    case "JsonTrue": output.Append("true"); break;
                    case "JsonFalse": output.Append("false"); break;
                    case "JsonNull": output.Append("null"); break;
                    default: throw new InvalidDataException("Ismeretlen adatcsomópont.");
                }
            } else if (file.Format == "csharp") {
                if (node.Token) {
                    if (node.Children.Count != 0 || !Enum.TryParse<SyntaxKind>(node.Kind, out var kind)) throw new InvalidDataException("Hibás terminál.");
                    var parsed = SyntaxFactory.ParseTokens(node.Text).Where(t => !t.IsKind(SyntaxKind.EndOfFileToken)).ToArray();
                    bool interpolation = node.Kind.StartsWith("InterpolatedString", StringComparison.Ordinal) || node.Kind.Contains("Interpolated", StringComparison.Ordinal);
                    bool empty = kind == SyntaxKind.EndOfFileToken || node.Kind.StartsWith("Omitted", StringComparison.Ordinal);
                    bool fixedToken = SyntaxFacts.GetText(kind) is string fixedText && fixedText.Length > 0 && fixedText == node.Text;
                    if (!interpolation && !fixedToken && (empty ? node.Text.Length != 0 : parsed.Length != 1 || parsed[0].Text != node.Text || parsed[0].ContainsDiagnostics))
                        throw new InvalidDataException(file.Path + ": a terminál egyetlen érték, név vagy operátor lehet: " + id + " (" + node.Kind + ": " + node.Text + ")");
                    // Original trivia carries comments and preprocessor directives, never editable code.
                    output.Append(node.Leading).Append(node.Text).Append(node.Trailing);
                } else Children("");
            } else throw new InvalidDataException("Ismeretlen gráfformátum.");
            active.Remove(id);
            void Children(string separator) {
                for (int i = 0; i < node.Children.Count; i++) { if (i > 0) output.Append(separator); Visit(node.Children[i], depth + 1); }
            }
        }
        Visit(file.Root, 0); string text = output.ToString();
        if (file.Format == "csharp") {
            var errors = CSharpSyntaxTree.ParseText(text).GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).Take(8).ToArray();
            if (errors.Length > 0) throw new InvalidDataException(file.Path + ": " + string.Join("; ", errors.Select(e => e.ToString())));
        } else using (JsonDocument.Parse(text)) { }
        return text;
    }
    public Dictionary<string, string> RenderAll()
    {
        if (Schema != "forest-algorithm-graphs" || Version != 1 || Files == null || Files.Count > 3000)
            throw new InvalidDataException("Ismeretlen algoritmusprojekt.");
        var output = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in Files) {
            string[] parts = file.Path.Replace('\\', '/').Split('/');
            if (parts.Length < 2 || !Projects.Contains(parts[0]) || parts.Any(p => p.Length == 0 || p is "." or ".." || p.Contains(':'))
                || !file.Path.EndsWith(file.Format == "csharp" ? ".cs" : ".json", StringComparison.Ordinal) || !output.TryAdd(file.Path, Render(file)))
                throw new InvalidDataException("Hibás vagy ismétlődő gráfútvonal: " + file.Path);
        }
        return output;
    }
    public string Revision()
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var pair in RenderAll().OrderBy(p => p.Key, StringComparer.Ordinal)) {
            hash.AppendData(Encoding.UTF8.GetBytes(pair.Key + "\0" + pair.Value + "\0"));
        }
        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }
    public static readonly string[] Templates = { "Szám", "Összeg", "Szorzat", "Összehasonlítás", "Értékadás", "Feltétel", "Ciklus", "Bejárás", "Hívás", "Visszatérés", "Változó", "Blokk" };
    public static readonly string[] NodeKinds = Enum.GetNames<SyntaxKind>()
        .Where(k => !k.EndsWith("Trivia", StringComparison.Ordinal) && k is not ("None" or "List" or "BadToken"))
        .Order(StringComparer.Ordinal).ToArray();
    public static string InsertNode(AlgorithmFile file, string kind)
    {
        if (file.Format != "csharp" || !Enum.TryParse<SyntaxKind>(kind, out var syntaxKind) || !NodeKinds.Contains(kind)) throw new ArgumentException("Ismeretlen művelet.");
        bool token = kind.EndsWith("Token", StringComparison.Ordinal) || kind.EndsWith("Keyword", StringComparison.Ordinal);
        var node = new AlgorithmNode { Id = "g" + Guid.NewGuid().ToString("N"), Kind = kind, Token = token,
            Text = token ? SyntaxFacts.GetText(syntaxKind) ?? "" : "", Trailing = token ? " " : "" };
        if (node.Text.Length == 0) node.Text = kind switch { "IdentifierToken" => "value", "NumericLiteralToken" => "0",
            "StringLiteralToken" => "\"\"", "CharacterLiteralToken" => "'x'", _ => "" };
        file.Nodes.Add(node); return node.Id;
    }
    public static string InsertTemplate(AlgorithmFile file, int template)
    {
        string[] definitions = { "1.0", "a + b", "a * b", "a > b", "value = 0;", "if (condition) { } else { }", "while (condition) { }",
            "foreach (var item in items) { }", "Target(argument);", "return value;", "double value = 0;", "{ }" };
        if (template < 0 || template >= definitions.Length || file.Format != "csharp") throw new ArgumentException("Hibás sablon.");
        SyntaxNode syntax = template < 4 ? SyntaxFactory.ParseExpression(definitions[template]) : SyntaxFactory.ParseStatement(definitions[template]);
        // Parse a statement/expression directly to avoid a CompilationUnit wrapper.
        string Add(SyntaxNodeOrToken item) {
            var token = item.AsToken(); var node = new AlgorithmNode { Id = "g" + Guid.NewGuid().ToString("N"), Kind = item.Kind().ToString(), Token = item.IsToken,
                Text = item.IsToken ? token.Text : "", Leading = item.IsToken ? token.LeadingTrivia.ToFullString() : "",
                Trailing = item.IsToken ? token.TrailingTrivia.ToFullString() : "" };
            file.Nodes.Add(node);
            if (item.IsNode) foreach (var child in item.AsNode()!.ChildNodesAndTokens()) node.Children.Add(Add(child));
            return node.Id;
        }
        return Add(syntax.NormalizeWhitespace());
    }
    public static string Duplicate(AlgorithmFile file, string root)
    {
        var index = file.Index(); var active = new HashSet<string>();
        string Copy(string id, int depth) {
            if (depth > 512 || !active.Add(id) || !index.TryGetValue(id, out var original)) throw new InvalidDataException("Hibás másolási gráf.");
            var clone = new AlgorithmNode { Id = "g" + Guid.NewGuid().ToString("N"), Kind = original.Kind, Token = original.Token,
                Text = original.Text, Leading = original.Leading, Trailing = original.Trailing, X = original.X + 30, Y = original.Y + 30 };
            foreach (string child in original.Children) clone.Children.Add(Copy(child, depth + 1));
            file.Nodes.Add(clone); active.Remove(id); return clone.Id;
        }
        return Copy(root, 0);
    }
}
