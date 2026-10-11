using Microsoft.CodeAnalysis.CSharp;

namespace ForesTycoon.RuleCompiler;

public sealed record AlgorithmPort(string Name, string Owner, int Position, string Target);

/// <summary>A lossless presentation of existing graph IDs. No inferred operation replaces the original graph.</summary>
public static class AlgorithmSemantics
{
    private static readonly Dictionary<string, string> Names = new() {
        ["Block"] = "Műveletek sorrendben", ["IfStatement"] = "Feltételes elágazás", ["ElseClause"] = "Különben",
        ["ForStatement"] = "Számlálós ismétlés", ["ForEachStatement"] = "Objektumok bejárása", ["WhileStatement"] = "Ismétlés feltétellel",
        ["DoStatement"] = "Ismétlés, majd ellenőrzés", ["ReturnStatement"] = "Eredmény visszaadása",
        ["ExpressionStatement"] = "Művelet", ["InvocationExpression"] = "Eljárás meghívása", ["ArgumentList"] = "Hívás bemenetei",
        ["Argument"] = "Bemeneti érték", ["SimpleAssignmentExpression"] = "Állapot / érték beállítása",
        ["AddAssignmentExpression"] = "Érték növelése", ["SubtractAssignmentExpression"] = "Érték csökkentése", ["MultiplyAssignmentExpression"] = "Érték szorzása", ["DivideAssignmentExpression"] = "Érték osztása",
        ["VariableDeclaration"] = "Változók", ["VariableDeclarator"] = "Változó létrehozása", ["EqualsValueClause"] = "Kezdőérték",
        ["LocalDeclarationStatement"] = "Helyi változó", ["ArrowExpressionClause"] = "Eredmény képlete",
        ["ConditionalExpression"] = "Választás feltétel alapján", ["IdentifierName"] = "Változó / adat", ["NumericLiteralExpression"] = "Szám",
        ["StringLiteralExpression"] = "Szöveg", ["MemberAccessExpression"] = "Objektum adata / művelete",
        ["SimpleMemberAccessExpression"] = "Objektum adata / művelete", ["ObjectCreationExpression"] = "Új objektum",
        ["MethodDeclaration"] = "Eljárás", ["ClassDeclaration"] = "Objektumtípus", ["PropertyDeclaration"] = "Tulajdonság",
        ["ParameterList"] = "Eljárás bemenetei", ["Parameter"] = "Bemenet", ["SwitchStatement"] = "Többágú döntés",
        ["SwitchSection"] = "Döntési ág", ["TryStatement"] = "Művelet hibakezeléssel", ["CatchClause"] = "Hiba esetén",
        ["JsonObject"] = "Adatdefiníció", ["JsonArray"] = "Adatlista", ["JsonProperty"] = "Adatmező", ["JsonNumber"] = "Számadat",
        ["JsonString"] = "Szövegadat", ["JsonTrue"] = "Igaz", ["JsonFalse"] = "Hamis", ["JsonNull"] = "Üres adat",
        ["AddExpression"] = "Összeg", ["SubtractExpression"] = "Különbség", ["MultiplyExpression"] = "Szorzat", ["DivideExpression"] = "Hányados",
        ["GreaterThanExpression"] = "Nagyobb-e?", ["LessThanExpression"] = "Kisebb-e?", ["EqualsExpression"] = "Egyenlő-e?",
        ["LogicalAndExpression"] = "Mindkét feltétel", ["LogicalOrExpression"] = "Legalább egy feltétel", ["ParenthesizedExpression"] = "Részképlet"
    };
    private static readonly Dictionary<string, string> Binary = new() { ["+"] = "AddExpression", ["-"] = "SubtractExpression", ["*"] = "MultiplyExpression", ["/"] = "DivideExpression", ["%"] = "ModuloExpression",
        [">"] = "GreaterThanExpression", ["<"] = "LessThanExpression", [">="] = "GreaterThanOrEqualExpression", ["<="] = "LessThanOrEqualExpression", ["=="] = "EqualsExpression", ["!="] = "NotEqualsExpression",
        ["&&"] = "LogicalAndExpression", ["||"] = "LogicalOrExpression", ["&"] = "BitwiseAndExpression", ["|"] = "BitwiseOrExpression", ["^"] = "ExclusiveOrExpression", ["<<"] = "LeftShiftExpression", [">>"] = "RightShiftExpression", ["??"] = "CoalesceExpression" };
    private static readonly Dictionary<string, string> Assignments = new() { ["="] = "SimpleAssignmentExpression", ["+="] = "AddAssignmentExpression", ["-="] = "SubtractAssignmentExpression", ["*="] = "MultiplyAssignmentExpression", ["/="] = "DivideAssignmentExpression", ["%="] = "ModuloAssignmentExpression" };
    public static readonly string[] Types = { "double", "float", "int", "long", "bool", "string", "decimal", "object", "byte", "short", "uint", "ulong" };
    public static string[] Operators(AlgorithmNode node) => Binary.ContainsValue(node.Kind) ? Binary.Keys.ToArray() : Assignments.ContainsValue(node.Kind) ? Assignments.Keys.ToArray() : Array.Empty<string>();
    public static void SetOperator(AlgorithmNode node, IReadOnlyDictionary<string, AlgorithmNode> index, string operation)
    {
        var options = Binary.ContainsValue(node.Kind) ? Binary : Assignments;
        if (!options.TryGetValue(operation, out string? kind) || node.Children.Count != 3 || !index[node.Children[1]].Token) throw new ArgumentException("Ez nem támogatott kétbemenetes művelet.");
        var token = SyntaxFactory.ParseToken(operation); node.Kind = kind;
        index[node.Children[1]].Kind = token.Kind().ToString(); index[node.Children[1]].Text = operation;
    }
    public static void SetType(AlgorithmNode node, IReadOnlyDictionary<string, AlgorithmNode> index, string type)
    {
        if (node.Kind != "PredefinedType" || node.Children.Count != 1 || !Types.Contains(type)) throw new ArgumentException("Nem támogatott egyszerű típus.");
        var token = SyntaxFactory.ParseToken(type); var target = index[node.Children[0]]; target.Kind = token.Kind().ToString(); target.Text = type;
    }
    public static string Name(AlgorithmNode node) => Names.TryGetValue(node.Kind, out var name) ? name
        : node.Token ? node.Kind switch { "NumericLiteralToken" => "Szám", "IdentifierToken" => "Név", "StringLiteralToken" => "Szöveg", _ => "Jel / érték" }
        : "Részletes művelet · " + node.Kind;
    public static AlgorithmPort[] Ports(AlgorithmNode node, IReadOnlyDictionary<string, AlgorithmNode> index)
    {
        var children = node.Children.Select((id, i) => new AlgorithmPort("", node.Id, i, id))
            .Where(p => index.TryGetValue(p.Target, out var child) && !child.Token).ToArray();
        string[] names = node.Kind switch {
            "IfStatement" => new[] { "Feltétel", "Ha teljesül", "Különben" },
            "ConditionalExpression" => new[] { "Feltétel", "Igaz esetben", "Hamis esetben" },
            "WhileStatement" or "DoStatement" => node.Kind == "WhileStatement" ? new[] { "Feltétel", "Ismételt művelet" } : new[] { "Ismételt művelet", "Feltétel" },
            "ForEachStatement" => new[] { "Elem típusa", "Bejárt lista", "Művelet minden elemre" },
            "InvocationExpression" => new[] { "Eljárás", "Bemeneti értékek" },
            "SimpleAssignmentExpression" or "AddAssignmentExpression" or "SubtractAssignmentExpression" => new[] { "Céladat", "Új érték / változás" },
            "ReturnStatement" or "EqualsValueClause" or "ArrowExpressionClause" => new[] { "Eredmény" },
            "SimpleMemberAccessExpression" => new[] { "Objektum", "Adat / művelet" },
            _ => Array.Empty<string>()
        };
        return children.Select((p, i) => p with { Name = i < names.Length ? names[i]
            : node.Kind == "Block" ? "Művelet " + (i + 1)
            : node.Kind == "JsonObject" ? index[p.Target].Text
            : node.Kind == "JsonProperty" ? "Érték"
            : node.Kind.EndsWith("Expression") && children.Length == 2 ? (i == 0 ? "Bal oldali érték" : "Jobb oldali érték")
            : "Elem " + (i + 1) }).ToArray();
    }
    public static string Summary(string id, IReadOnlyDictionary<string, AlgorithmNode> index, int limit = 110)
    {
        var tokens = new List<string>(); var active = new HashSet<string>(); int count = 0;
        void Visit(string target, int depth) {
            if (depth > 40 || count++ > 80 || !active.Add(target) || !index.TryGetValue(target, out var node)) return;
            if (node.Token || node.Kind.StartsWith("Json")) { if (node.Text.Length > 0) tokens.Add(node.Text); }
            if (!node.Token) foreach (string child in node.Children) Visit(child, depth + 1);
            active.Remove(target);
        }
        Visit(id, 0); string text = string.Join(" ", tokens).Replace('\n', ' ').Replace('\r', ' ');
        return text.Length > limit ? text[..limit] + "…" : text;
    }
    public static AlgorithmNode[] Values(string id, IReadOnlyDictionary<string, AlgorithmNode> index)
    {
        var result = new List<AlgorithmNode>(); var visited = new HashSet<string>();
        void Visit(string target, int depth) {
            if (depth > 24 || result.Count >= 24 || !visited.Add(target) || !index.TryGetValue(target, out var node)) return;
            if (node.Kind is "NumericLiteralToken" or "StringLiteralToken" or "IdentifierToken" or "CharacterLiteralToken"
                or "JsonNumber" or "JsonString" or "JsonProperty") result.Add(node);
            // Large bodies are edited by opening their own block, not by listing all their variables here.
            if (depth > 0 && node.Kind is "Block" or "MethodDeclaration" or "ClassDeclaration") return;
            foreach (string child in node.Children) Visit(child, depth + 1);
        }
        Visit(id, 0); return result.ToArray();
    }
    public static void SetValue(AlgorithmNode node, string text)
    {
        if (node.Token) {
            var token = SyntaxFactory.ParseToken(text);
            if (token.ContainsDiagnostics || token.Text != text || token.Kind().ToString() != node.Kind)
                throw new InvalidDataException("Az érték egyetlen, azonos típusú szám, név vagy szöveg lehet.");
        } else if (node.Kind == "JsonNumber") {
            using var value = System.Text.Json.JsonDocument.Parse(text);
            if (value.RootElement.ValueKind != System.Text.Json.JsonValueKind.Number) throw new InvalidDataException("Szám szükséges.");
        }
        node.Text = text;
    }
    public static bool CanConnect(string owner, string target, IReadOnlyDictionary<string, AlgorithmNode> index)
    {
        var pending = new Stack<string>(); var seen = new HashSet<string>(); pending.Push(target);
        while (pending.Count > 0) {
            string id = pending.Pop(); if (id == owner || !index.TryGetValue(id, out var node)) return false;
            if (!seen.Add(id)) continue; foreach (string child in node.Children) pending.Push(child);
        }
        return true;
    }
    public static string InsertProcedure(AlgorithmFile file, string parentId, string name, string result)
    {
        var parent = file.Index()[parentId];
        if (parent.Kind is not ("ClassDeclaration" or "StructDeclaration") || !SyntaxFacts.IsValidIdentifier(name)
            || result is not ("double" or "bool" or "void")) throw new ArgumentException("Érvényes eljárásnév, eredménytípus és osztály / struktúra szükséges.");
        string body = result == "void" ? "" : result == "bool" ? "return true;" : "return value;";
        var source = AlgorithmBundle.Import("ForesTycoon/World/Template.cs", "class Template { " + result + " " + name + "(double value) { " + body + " } }");
        var sourceIndex = source.Index(); var method = source.Nodes.Single(n => n.Kind == "MethodDeclaration");
        string Copy(string id) {
            var original = sourceIndex[id]; var node = new AlgorithmNode { Id = "g" + Guid.NewGuid().ToString("N"), Kind = original.Kind, Text = original.Text,
                Token = original.Token, Leading = original.Leading, Trailing = original.Trailing };
            foreach (string child in original.Children) node.Children.Add(Copy(child)); file.Nodes.Add(node); return node.Id;
        }
        string added = Copy(method.Id); parent.Children.Insert(parent.Children.Count - 1, added); file.Entries.Add(new(added, name)); return added;
    }
}
