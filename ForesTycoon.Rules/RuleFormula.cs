using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace ForesTycoon.Rules;

/// <summary>A name a formula may use, bound to one input of the graph runtime.</summary>
public sealed record FormulaName(string Name, BehaviorOperation Operation, string Key = "", string Help = "");

public sealed class FormulaException(string message, int line, int column) : FormatException(message)
{
    public int Line { get; } = line;
    public int Column { get; } = column;
}

/// <summary>
/// Readable rule formulas, compiled to the same bounded expression graph the game already runs. Spreadsheet style:
/// <c>átnedvesedés = max(víz; 0,2)</c> lines, <c>;</c> between function arguments, decimal comma or point,
/// <c>#</c> comments. The last line is the result. Functions: min, max, abs, ha(feltétel; akkor; különben), korlát(x; alsó; felső).
/// </summary>
public static class RuleFormula
{
    /// <summary>The names every rule formula may use for its hook: the built-in result, its two inputs, time, world fields, stocks.</summary>
    public static IReadOnlyList<FormulaName> Names(BehaviorHook hook, IEnumerable<string> stockIds = null)
    {
        var (state, amount) = InputNames(hook);
        var names = new List<FormulaName> {
            new("beépített", BehaviorOperation.Native, Help: "A játék eddigi számítása erre a szabályra."),
            new(state, BehaviorOperation.State, Help: hook.State),
            new(amount, BehaviorOperation.Amount, Help: hook.Amount),
            new("lépés", BehaviorOperation.Delta, Help: "Az aktuális lépés hossza."),
            new("idő", BehaviorOperation.Time, Help: "Eltelt idő (erdőév)."),
        };
        if (hook.Kind is "truck" or "forwarder" or "processor")
            names.AddRange(new FormulaName[] {
                new("kapacitás", BehaviorOperation.Capacity, Help: "Rakománykapacitás (m³)."), new("elérhető", BehaviorOperation.Available, Help: "Elérhető mennyiség (m³)."),
                new("honnan", BehaviorOperation.From, Help: "Kiinduló csempe."), new("hová", BehaviorOperation.To, Help: "Célcsempe."),
                new("burkolat", BehaviorOperation.Surface, Help: "0 terep, 1 aszfalt, 2 makadám, 3 nyom.") });
        foreach (string input in hook.Inputs ?? Array.Empty<string>()) names.Add(new(input, BehaviorOperation.Input, input, InputHelp(input)));
        foreach (var (key, name, help) in WorldNames) names.Add(new(name, BehaviorOperation.NamedValue, key, help));
        foreach (string id in stockIds ?? Enumerable.Empty<string>()) names.Add(new("készlet." + id, BehaviorOperation.NamedValue, "stock." + id, "Saját készlet."));
        if (hook.Kind == "world") names.RemoveAll(n => n.Operation is not (BehaviorOperation.Native or BehaviorOperation.NamedValue));
        return names;
    }

    private static readonly (string Key, string Name, string Help)[] WorldNames = {
        ("world.time", "világ.erdőév", "Eltelt erdőév."), ("world.treeCount", "világ.fák", "Élő fák száma."),
        ("world.roadCount", "világ.utak", "Útcsempék száma."), ("world.rain", "világ.csapadék", "Folyékony csapadék intenzitása."),
        ("world.temperature", "világ.hőmérséklet", "Hőmérséklet (°C)."), ("world.snow", "világ.hó", "Hókészlet (mm)."),
        ("world.water", "világ.víz", "Tárolt víz összesen."), ("world.income", "világ.bevétel", "Eddigi bevétel (eFt)."),
        ("world.expenses", "világ.kiadás", "Eddigi kiadás (eFt)."), ("world.runningCosts", "világ.üzemeltetés", "Üzemeltetési költség (eFt)."),
        ("world.balance", "világ.egyenleg", "Egyenleg (eFt).") };

    // Hooks whose inputs read better with a hand-picked name than one derived from the label.
    private static readonly Dictionary<string, (string State, string Amount)> Preferred = new() {
        ["trail.trafficability"] = ("átnedvesedés", "talaj.teherbírás"),
        ["road.weather"] = ("útállapot", "csapadék"),
    };

    private static string InputHelp(string name) => name switch {
        "óra" => "A lépés hossza (környezeti óra).",
        "talaj.beszivárgás" => "A csempe talajtípusának beszivárgása (mm/óra).",
        "talaj.telítettség" => "A talajtípus teljes víztartása (mm).",
        "talaj.vízkapacitás" => "Szabadföldi vízkapacitás (mm): e fölött a víz elszivárog.",
        "talaj.hervadáspont" => "Hervadáspont (mm): ez alatt nincs felvehető víz.",
        "talaj.drénezés" => "A talajtípus drénezése (mm/óra).",
        "lombborítás" => "A csempe lombborítása (0–1).",
        "burkolt" => "1, ha a csempe burkolt (út, épület); különben 0.",
        _ => "A szabály bemenete."
    };

    private static (string State, string Amount) InputNames(BehaviorHook hook) =>
        Preferred.TryGetValue(hook.Id, out var names) ? names : (Derive(hook.State, "állapot"), Derive(hook.Amount, "mennyiség"));

    private static string Derive(string label, string fallback)
    {
        if (string.IsNullOrWhiteSpace(label) || label.StartsWith("Nem használt")) return fallback;
        string text = label.Split('(', ';')[0].Trim().ToLowerInvariant();
        var builder = new StringBuilder();
        foreach (char c in text) builder.Append(char.IsLetterOrDigit(c) ? c : '_');
        string name = builder.ToString().Trim('_');
        while (name.Contains("__")) name = name.Replace("__", "_");
        return name.Length > 0 && char.IsLetter(name[0]) ? name : fallback;
    }

    /// <summary>The formula of a built-in rule as the game computes it, where the rule has been moved into the editor.</summary>
    public static string Default(BehaviorHook hook) => hook.Formula ?? "beépített";

    public static BehaviorGraph Compile(string text, BehaviorGraph target, IReadOnlyList<FormulaName> names)
    {
        ArgumentNullException.ThrowIfNull(target);
        var parser = new Parser(text ?? "", names);
        var graph = new BehaviorGraph { Id = target.Id, Name = target.Name, Hook = target.Hook, Kind = target.Kind, ObjectId = target.ObjectId, Nodes = new() };
        graph.Output = parser.Parse(graph.Nodes);
        Layout(graph);
        return graph;
    }

    /// <summary>Text for an existing graph; shared nodes repeat in place. Unknown inputs keep their raw key.</summary>
    public static string Decompile(BehaviorGraph graph, IReadOnlyList<FormulaName> names)
    {
        var byId = graph.Nodes.ToDictionary(n => n.Id, StringComparer.Ordinal);
        string Name(BehaviorNode n) => names.FirstOrDefault(x => x.Operation == n.Operation && (n.Operation is not (BehaviorOperation.NamedValue or BehaviorOperation.Input) || x.Key == n.Key))?.Name ?? n.Key;
        string Write(string id, int parent, int depth)
        {
            if (depth > 64 || !byId.TryGetValue(id ?? "", out var n)) return "?";
            int own = Precedence(n.Operation);
            string a(int p) => Write(n.A, p, depth + 1);
            string b(int p) => Write(n.B, p, depth + 1);
            string text = n.Operation switch {
                BehaviorOperation.Constant => n.Value.ToString("0.######", CultureInfo.InvariantCulture),
                BehaviorOperation.Add => a(own) + " + " + b(own), BehaviorOperation.Subtract => a(own) + " - " + b(own + 1),
                BehaviorOperation.Multiply => a(own) + " * " + b(own), BehaviorOperation.Divide => a(own) + " / " + b(own + 1),
                BehaviorOperation.Greater => a(own) + " > " + b(own), BehaviorOperation.Less => a(own) + " < " + b(own),
                BehaviorOperation.Minimum => $"min({a(0)}; {b(0)})", BehaviorOperation.Maximum => $"max({a(0)}; {b(0)})",
                BehaviorOperation.Absolute => $"abs({a(0)})", BehaviorOperation.Negate => "-" + a(9),
                BehaviorOperation.Exp => $"exp({a(0)})", BehaviorOperation.Power => $"hatvány({a(0)}; {b(0)})",
                BehaviorOperation.Select => $"ha({a(0)}; {b(0)}; {Write(n.C, 0, depth + 1)})",
                BehaviorOperation.Curve => BehaviorCurves.Read(n.Key).ToFormula().Replace(BehaviorCurves.Input, "(" + a(0) + ")", StringComparison.Ordinal),
                _ => Name(n)
            };
            return own > 0 && own < parent ? "(" + text + ")" : text;
        }
        return Write(graph.Output, 0, 0);
    }

    private static int Precedence(BehaviorOperation op) => op switch {
        BehaviorOperation.Greater or BehaviorOperation.Less => 1, BehaviorOperation.Add or BehaviorOperation.Subtract => 2,
        BehaviorOperation.Multiply or BehaviorOperation.Divide => 3, _ => 0 };

    private static void Layout(BehaviorGraph graph)
    {
        var byId = graph.Nodes.ToDictionary(n => n.Id, StringComparer.Ordinal);
        var depth = new Dictionary<string, int>(StringComparer.Ordinal);
        int Depth(string id, int guard) {
            if (guard > 300 || !byId.TryGetValue(id ?? "", out var n)) return 0;
            if (depth.TryGetValue(id, out int d)) return d;
            int inputs = BehaviorModel.Inputs(n.Operation);
            d = 1 + Math.Max(inputs > 0 ? Depth(n.A, guard + 1) : -1, Math.Max(inputs > 1 ? Depth(n.B, guard + 1) : -1, inputs > 2 ? Depth(n.C, guard + 1) : -1));
            return depth[id] = d;
        }
        foreach (var n in graph.Nodes) Depth(n.Id, 0);
        foreach (var column in graph.Nodes.GroupBy(n => depth[n.Id]))
            foreach (var (n, i) in column.Select((n, i) => (n, i))) { n.X = 30 + column.Key * 250; n.Y = 30 + i * 120; }
    }

    private sealed class Parser
    {
        private readonly string text;
        private readonly Dictionary<string, FormulaName> names;
        private readonly Dictionary<string, string> locals = new(StringComparer.Ordinal);
        private readonly Dictionary<string, string> inputs = new(StringComparer.Ordinal);
        private List<BehaviorNode> nodes;
        private int position, line = 1, lineStart;

        internal Parser(string text, IReadOnlyList<FormulaName> names)
        {
            this.text = text; this.names = names.ToDictionary(n => n.Name, StringComparer.Ordinal);
        }

        internal string Parse(List<BehaviorNode> into)
        {
            nodes = into; string result = null;
            while (true) {
                SkipBlank(newlines: true);
                if (position >= text.Length) break;
                int start = position;
                string name = TryAssignment();
                string value = Expression();
                SkipBlank(newlines: false);
                if (position < text.Length && text[position] != '\n' && text[position] != '\r') throw Error("Váratlan jel: " + text[position]);
                if (name != null) {
                    if (names.ContainsKey(name)) throw Error($"A „{name}” a szabály bemenete; adj más nevet a köztes értéknek.", start);
                    locals[name] = value; Node(value).Name = name;
                }
                result = value;
            }
            if (result == null) throw Error("Üres képlet: írd le, mit számoljon a szabály (például: beépített).");
            if (nodes.Count > 256) throw Error("Legfeljebb 256 lépésből álló képlet lehet.");
            return result;
        }

        private string TryAssignment()
        {
            int saved = position, savedLine = line, savedStart = lineStart;
            if (TryIdentifier(out string name)) {
                SkipBlank(newlines: false);
                if (position < text.Length && text[position] == '=') { position++; return name; }
            }
            position = saved; line = savedLine; lineStart = savedStart;
            return null;
        }

        private string Expression()
        {
            string left = Sum();
            SkipBlank(false);
            if (Peek('>') || Peek('<')) {
                var op = text[position++] == '>' ? BehaviorOperation.Greater : BehaviorOperation.Less;
                return Add(op, op == BehaviorOperation.Greater ? "nagyobb" : "kisebb", left, Sum());
            }
            return left;
        }
        private string Sum()
        {
            string left = Product();
            while (true) {
                SkipBlank(false);
                if (Peek('+')) { position++; left = Add(BehaviorOperation.Add, "összeg", left, Product()); }
                else if (Peek('-') || Peek('−')) { position++; left = Add(BehaviorOperation.Subtract, "különbség", left, Product()); }
                else return left;
            }
        }
        private string Product()
        {
            string left = Unary();
            while (true) {
                SkipBlank(false);
                if (Peek('*') || Peek('×')) { position++; left = Add(BehaviorOperation.Multiply, "szorzat", left, Unary()); }
                else if (Peek('/')) { position++; left = Add(BehaviorOperation.Divide, "hányados", left, Unary()); }
                else return left;
            }
        }
        private string Unary()
        {
            SkipBlank(false);
            if (Peek('-') || Peek('−')) { position++; return Add(BehaviorOperation.Negate, "ellentett", Unary()); }
            return Primary();
        }
        private string Primary()
        {
            SkipBlank(false);
            if (position >= text.Length) throw Error("Hiányzó érték a képlet végén.");
            if (Peek('(')) { position++; string inner = Expression(); Expect(')'); return inner; }
            if (char.IsDigit(text[position])) return Number();
            int start = position;
            if (!TryIdentifier(out string name)) throw Error("Itt értéket, nevet vagy zárójelet vártam.");
            SkipBlank(false);
            if (Peek('(')) return Call(name, start);
            if (locals.TryGetValue(name, out string local)) return local;
            if (names.TryGetValue(name, out var known)) {
                string key = known.Operation + ":" + known.Key;
                if (!inputs.TryGetValue(key, out string id)) inputs[key] = id = Add(known.Operation, known.Name, key: known.Key);
                return id;
            }
            var close = names.Keys.Concat(locals.Keys).OrderBy(k => Distance(k, name)).FirstOrDefault();
            throw Error($"Ismeretlen név: „{name}”." + (close != null && Distance(close, name) <= 3 ? $" Erre gondoltál: „{close}”?" : ""), start);
        }
        private string Call(string name, int start)
        {
            Expect('(');
            var args = new List<string>();
            SkipBlank(true);
            if (!Peek(')')) while (true) { args.Add(Expression()); SkipBlank(true); if (Peek(';')) { position++; continue; } break; }
            Expect(')');
            void Count(int n) { if (args.Count != n) throw Error($"A {name} függvénynek {n} értéket kell kapnia, pontosvesszővel elválasztva.", start); }
            switch (name) {
                case "min": Count(2); return Add(BehaviorOperation.Minimum, "kisebbik", args[0], args[1]);
                case "max": Count(2); return Add(BehaviorOperation.Maximum, "nagyobbik", args[0], args[1]);
                case "abs": Count(1); return Add(BehaviorOperation.Absolute, "abszolút érték", args[0]);
                case "exp": Count(1); return Add(BehaviorOperation.Exp, "exponenciális", args[0]);
                case "hatvány": Count(2); return Add(BehaviorOperation.Power, "hatvány", args[0], args[1]);
                case "ha": Count(3); return Add(BehaviorOperation.Select, "ha", args[0], args[1], args[2]);
                case "korlát": Count(3);
                    return Add(BehaviorOperation.Maximum, "korlátozva", args[1], Add(BehaviorOperation.Minimum, "felső korlát", args[0], args[2]));
                default:
                    if (name.StartsWith("curve.", StringComparison.Ordinal)) return Curve(name[6..], args, start);
                    throw Error($"Ismeretlen függvény: „{name}”. Használható: min, max, abs, exp, hatvány, ha, korlát, curve.*.", start);
            }
        }
        // Curve families (see RuleCurve): built from the bounded operations, so the runtime needs nothing new.
        private string Curve(string family, List<string> a, int start)
        {
            var f = RuleCurve.Find(family) ?? throw Error($"Ismeretlen görbecsalád: „{family}”. Használható: {string.Join(", ", RuleCurve.Families.Select(x => x.Id))}.", start);
            if (f.Id == "points" ? a.Count < 5 || a.Count % 2 == 0 : a.Count != f.Arguments + 1)
                throw Error(f.Id == "points" ? "A curve.points: bemenet, majd legalább két x; y pár." : $"A curve.{f.Id} függvénynek {f.Arguments + 1} értéke van: bemenet; {f.ArgumentHelp}.", start);
            string C(double v) => Add(BehaviorOperation.Constant, v.ToString(CultureInfo.InvariantCulture), value: v);
            string Sub(string x, string y) => Add(BehaviorOperation.Subtract, "különbség", x, y);
            string Mul(string x, string y) => Add(BehaviorOperation.Multiply, "szorzat", x, y);
            string Lerp(string y0, string y1, string t) => Add(BehaviorOperation.Add, "görbe", y0, Mul(Sub(y1, y0), t));
            string T(string x, string x0, string x1) => Add(BehaviorOperation.Divide, "arány", Sub(x, x0), Sub(x1, x0));
            string Clamp01(string v) => Add(BehaviorOperation.Maximum, "korlát", C(0), Add(BehaviorOperation.Minimum, "korlát", v, C(1)));
            string x = a[0];
            switch (f.Id) {
                case "root": return Lerp(a[2], a[4], Add(BehaviorOperation.Power, "gyökválasz", Clamp01(T(x, a[1], a[3])), C(.5)));
                case "inverse": return Lerp(a[4], a[2], Add(BehaviorOperation.Divide, "inverz válasz", C(1), Add(BehaviorOperation.Add, "eltolás", C(1), Add(BehaviorOperation.Maximum, "korlát", C(0), T(x, a[1], a[3])))));
                case "linear": return Lerp(a[2], a[4], Clamp01(T(x, a[1], a[3])));
                case "step": return Add(BehaviorOperation.Select, "küszöb", Add(BehaviorOperation.Less, "kisebb", x, a[1]), a[2], a[3]);
                case "smooth": { string t = Clamp01(T(x, a[1], a[3])); return Lerp(a[2], a[4], Mul(Mul(t, t), Sub(C(3), Mul(C(2), t)))); }
                case "saturate": {
                    string t = Add(BehaviorOperation.Maximum, "korlát", C(0), T(x, a[1], a[3]));
                    string e = Sub(C(1), Add(BehaviorOperation.Exp, "exponenciális", Mul(C(-3), t)));
                    return Lerp(a[2], a[4], Add(BehaviorOperation.Divide, "normálva", e, C(1 - Math.Exp(-3))));
                }
                case "decay": {
                    string t = Add(BehaviorOperation.Maximum, "korlát", C(0), T(x, a[1], a[3]));
                    return Lerp(a[4], a[2], Add(BehaviorOperation.Exp, "exponenciális", Mul(C(-3), t)));
                }
                case "bell": {
                    string u = Add(BehaviorOperation.Divide, "távolság", Sub(x, a[1]), Mul(C(0.5), Sub(a[3], a[1])));
                    return Lerp(a[4], a[2], Add(BehaviorOperation.Exp, "harang", Mul(C(-0.5), Mul(u, u))));
                }
                default: { // points: linear between breakpoints, flat outside
                    int n = (a.Count - 1) / 2;
                    string result = a[2 * n];
                    for (int i = n - 1; i >= 1; i--) {
                        string seg = Lerp(a[2 * i], a[2 * i + 2], Clamp01(T(x, a[2 * i - 1], a[2 * i + 1])));
                        result = Add(BehaviorOperation.Select, "szakasz", Add(BehaviorOperation.Less, "kisebb", x, a[2 * i + 1]), seg, result);
                    }
                    return result;
                }
            }
        }
        private string Number()
        {
            int start = position;
            while (position < text.Length && char.IsDigit(text[position])) position++;
            if (position + 1 < text.Length && (text[position] == '.' || text[position] == ',') && char.IsDigit(text[position + 1])) {
                position++; while (position < text.Length && char.IsDigit(text[position])) position++;
            }
            double value = double.Parse(text[start..position].Replace(',', '.'), CultureInfo.InvariantCulture);
            return Add(BehaviorOperation.Constant, text[start..position], value: value);
        }
        private bool TryIdentifier(out string name)
        {
            int start = position;
            while (position < text.Length && (char.IsLetterOrDigit(text[position]) || text[position] is '_' or '.')
                   && (position > start || char.IsLetter(text[position]))) position++;
            name = text[start..position].TrimEnd('.');
            position = start + name.Length;
            return name.Length > 0;
        }
        private string Add(BehaviorOperation op, string label, string a = "", string b = "", string c = "", double value = 0, string key = "")
        {
            string id = "f" + nodes.Count;
            nodes.Add(new BehaviorNode { Id = id, Name = label, Operation = op, A = a, B = b, C = c, Value = value, Key = key });
            return id;
        }
        private BehaviorNode Node(string id) => nodes.Find(n => n.Id == id);
        private bool Peek(char c) => position < text.Length && text[position] == c;
        private void Expect(char c) { SkipBlank(true); if (!Peek(c)) throw Error($"Itt „{c}” jelet vártam."); position++; }
        private void SkipBlank(bool newlines)
        {
            while (position < text.Length) {
                char c = text[position];
                if (c == '#' || c == '/' && position + 1 < text.Length && text[position + 1] == '/') {
                    while (position < text.Length && text[position] != '\n') position++;
                } else if (c == ' ' || c == '\t' || newlines && (c == '\r' || c == '\n')) {
                    if (c == '\n') { line++; lineStart = position + 1; }
                    position++;
                } else return;
            }
        }
        private FormulaException Error(string message, int at = -1)
        {
            if (at < 0) at = position;
            int l = 1, start = 0;
            for (int i = 0; i < Math.Min(at, text.Length); i++) if (text[i] == '\n') { l++; start = i + 1; }
            return new FormulaException($"{l}. sor, {at - start + 1}. oszlop: {message}", l, at - start + 1);
        }
        private static int Distance(string a, string b)
        {
            var d = new int[a.Length + 1, b.Length + 1];
            for (int i = 0; i <= a.Length; i++) d[i, 0] = i;
            for (int j = 0; j <= b.Length; j++) d[0, j] = j;
            for (int i = 1; i <= a.Length; i++)
                for (int j = 1; j <= b.Length; j++)
                    d[i, j] = Math.Min(Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1), d[i - 1, j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1));
            return d[a.Length, b.Length];
        }
    }
}

/// <summary>
/// An elevation zone of a world (coast, plain, hills, mountains): it reaches from the zone below it up to
/// <see cref="MaxLevel"/>, the highest terrain level it covers; the last zone is open upwards.
/// </summary>
public sealed class ElevationZoneDefinition
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Color { get; set; } = "#7FA05A";
    /// <summary>The highest terrain level (the tile's lowest corner) that still belongs to this zone.</summary>
    public int MaxLevel { get; set; }

    /// <summary>The built-in zones: sea-level coast, lowland plain, hills, mountains.</summary>
    public static List<ElevationZoneDefinition> Defaults() => new() {
        new() { Id = "coast", Name = "Tengerpart", Color = "#D8C690", MaxLevel = 1 },
        new() { Id = "plain", Name = "Síkság", Color = "#86B35A", MaxLevel = 3 },
        new() { Id = "hills", Name = "Dombvidék", Color = "#A69A52", MaxLevel = 5 },
        new() { Id = "mountain", Name = "Hegyvidék", Color = "#B4ADA2", MaxLevel = 99 } };
}

/// <summary>One horizon of a soil profile, from the surface down: what the diorama's sides show under a tile of the soil.</summary>
public sealed class SoilHorizon
{
    public string Name { get; set; } = "";
    /// <summary>Thickness in metres.</summary>
    public double Depth { get; set; } = .3;
    public string Color { get; set; } = "#7A5A3A";

    /// <summary>The colour of the rock under every profile.</summary>
    public const string Bedrock = "#6E6A64";

    /// <summary>
    /// The built-in profile of a soil: litter (O), humus topsoil (A), subsoil (B) and parent material (C), after the soil
    /// classes of Hungarian forest sites. A soil the game does not know gets a generic profile darkened from its colour.
    /// </summary>
    public static List<SoilHorizon> Defaults(string soilId, string soilColor) => soilId switch {
        "sand" => new() { new() { Name = "O · avar", Depth = .03, Color = "#4A3B28" }, new() { Name = "A · humuszos homok", Depth = .15, Color = "#8A7550" },
            new() { Name = "B · rozsdabarna homok", Depth = .5, Color = "#C9A66A" }, new() { Name = "C · futóhomok", Depth = 1.2, Color = "#E2CF98" } },
        "loam" => new() { new() { Name = "O · avar", Depth = .05, Color = "#3B2C1C" }, new() { Name = "A · morzsás humusz", Depth = .25, Color = "#5C4430" },
            new() { Name = "B · barna vályog", Depth = .6, Color = "#8E6A45" }, new() { Name = "C · lösz", Depth = 1.5, Color = "#C2A578" } },
        "clay" => new() { new() { Name = "O · avar", Depth = .04, Color = "#3A2D22" }, new() { Name = "A · agyagos humusz", Depth = .2, Color = "#5E4636" },
            new() { Name = "Bt · agyagbemosódás", Depth = .7, Color = "#9A5B3C" }, new() { Name = "C · tömör agyag", Depth = 1.5, Color = "#B98A62" } },
        "organic" => new() { new() { Name = "O · nyers avar", Depth = .1, Color = "#2A1F16" }, new() { Name = "H · tőzeg", Depth = .6, Color = "#3E2E22" },
            new() { Name = "G · glejes réteg", Depth = .8, Color = "#5E6B6A" }, new() { Name = "C · iszap", Depth = 1.0, Color = "#8C8C80" } },
        _ => new() { new() { Name = "O · avar", Depth = .05, Color = "#3B2C1C" }, new() { Name = "A · feltalaj", Depth = .25, Color = Shade(soilColor, .55) },
            new() { Name = "B · altalaj", Depth = .6, Color = Shade(soilColor, .8) }, new() { Name = "C · alapkőzet felett", Depth = 1.2, Color = soilColor } }
    };

    private static string Shade(string hex, double factor)
    {
        if (hex is not { Length: 7 } || !uint.TryParse(hex.AsSpan(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint rgb)) return "#7A5A3A";
        int C(int shift) => (int)Math.Round(((rgb >> shift) & 255) * factor);
        return $"#{C(16):X2}{C(8):X2}{C(0):X2}";
    }
}

/// <summary>One soil type of a world: what the map paints and what the rules read as <c>talaj.*</c>.</summary>
public sealed class SoilTypeDefinition
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Color { get; set; } = "#A98A62";
    /// <summary>Where the generator places it: soil moisture and texture it prefers (0–1).</summary>
    public double PreferredMoisture { get; set; } = .5;
    public double PreferredTexture { get; set; } = .5;
    public double Saturation { get; set; } = 180;
    public double FieldCapacity { get; set; } = 130;
    public double WiltingPoint { get; set; } = 25;
    public double InfiltrationPerHour { get; set; } = 12;
    public double DrainagePerHour { get; set; } = 2;
    public double Fertility { get; set; } = 1;
    public double BearingCapacity { get; set; } = .5;
    /// <summary>The soil profile from the surface down; empty means the built-in profile of the soil (<see cref="Profile"/>).</summary>
    public List<SoilHorizon> Horizons { get; set; } = new();
    /// <summary>The horizons the diorama shows: the authored ones, or the built-in profile when none are authored.</summary>
    public List<SoilHorizon> Profile() => Horizons is { Count: > 0 } ? Horizons : SoilHorizon.Defaults(Id, Color);
}

/// <summary>One tree species: its biology, its size and the price of its timber. The ID is the game's species name.</summary>
public sealed class SpeciesDefinition
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public double MaximumAgeYears { get; set; } = 200;
    public double MatureAgeYears { get; set; } = 35;
    public double MaximumBiomass { get; set; } = 1;
    public double AnnualGrowthRate { get; set; } = .06;
    /// <summary>Site the species prefers and how far from it it still thrives (0–1).</summary>
    public double PreferredMoisture { get; set; } = .6;
    public double MoistureTolerance { get; set; } = .3;
    public double PreferredElevation { get; set; } = .5;
    public double ElevationTolerance { get; set; } = .4;
    /// <summary>0 = light-demanding pioneer, 1 = regenerates under a closed canopy.</summary>
    public double ShadeTolerance { get; set; } = .5;
    public double WoodDensity { get; set; } = 1;
    /// <summary>Exponent on the water supply: lower is more drought tolerant.</summary>
    public double DrySensitivity { get; set; } = .85;
    public double MatureHeight { get; set; } = 30;
    public double MaxHeight { get; set; } = 36;
    public int Capacity { get; set; } = 6;
    /// <summary>Timber value, thousand forints per cubic metre.</summary>
    public double TimberPrice { get; set; } = 30;
    /// <summary>The tree form authored over the built-in architecture; null keeps the built-in form.</summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public TreeShapeDefinition Shape { get; set; }
    /// <summary>The colours of the foliage through the year, of the bark and of dead wood, and the bark pattern; null keeps the built-in look.</summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public TreeColoursDefinition Colours { get; set; }

    internal void Validate()
    {
        Shape?.Validate(Name);
        Colours?.Validate(Name);
        bool Unit(double v) => v is >= 0 and <= 1;
        if (string.IsNullOrWhiteSpace(Id) || string.IsNullOrWhiteSpace(Name) || Description == null)
            throw new InvalidDataException("Minden fafajnak azonosítója és neve legyen.");
        if (!(MatureAgeYears > 0 && MaximumAgeYears > MatureAgeYears && MaximumAgeYears <= 2000 && MaximumBiomass is > 0 and <= 10
              && AnnualGrowthRate is > 0 and <= 1 && Unit(PreferredMoisture) && Unit(MoistureTolerance) && MoistureTolerance > 0
              && Unit(PreferredElevation) && Unit(ElevationTolerance) && ElevationTolerance > 0 && Unit(ShadeTolerance)
              && WoodDensity is > 0 and <= 5 && DrySensitivity is > 0 and <= 5 && MatureHeight > 0 && MaxHeight >= MatureHeight
              && MaxHeight <= 100 && Capacity is >= 1 and <= 64 && TimberPrice is >= 0 and <= 10000))
            throw new InvalidDataException($"{Name}: érvénytelen paraméter (kor, magasság, arányok 0–1, ár nem negatív).");
    }
}

/// <summary>
/// A species' tree form as tree-generator parameters (Weber–Penn names such as "1Branches" or "AttractionUp") over its
/// built-in architecture. <see cref="All"/> applies in every life phase; a phase's own set in <see cref="Phases"/> applies on
/// top of it, after the game's light and age adaptation, so it is the final word for that phase. The <see cref="DeadState"/>
/// set applies last, to a dead tree of any phase: what the standing dead tree keeps of the living one.
/// </summary>
public sealed class TreeShapeDefinition
{
    /// <summary>The life phases, in order, as the keys of <see cref="Phases"/>.</summary>
    public static readonly string[] PhaseNames = { "Seedling", "Sapling", "Young", "Mature", "Old", "Senescent" };
    /// <summary>The key in <see cref="Phases"/> of the dead tree's set.</summary>
    public const string DeadState = "Dead";
    public Dictionary<string, double> All { get; set; } = new();
    public Dictionary<string, Dictionary<string, double>> Phases { get; set; } = new();

    public bool IsEmpty => (All?.Count ?? 0) == 0 && (Phases?.Values.All(p => p == null || p.Count == 0) ?? true);

    internal void Validate(string species)
    {
        All ??= new(); Phases ??= new();
        foreach (var (phase, values) in Phases)
            if (Array.IndexOf(PhaseNames, phase) < 0 && phase != DeadState || values == null)
                throw new InvalidDataException($"{species}: ismeretlen életfázis a famegjelenésben: {phase}.");
        foreach (var (name, value) in All.Concat(Phases.Values.SelectMany(p => p)))
            if (string.IsNullOrWhiteSpace(name) || !double.IsFinite(value))
                throw new InvalidDataException($"{species}: érvénytelen famegjelenési paraméter: {name}.");
    }
}

/// <summary>
/// A species' colours as "#RRGGBB": the foliage at bud burst, in summer and in autumn, the bark and dead wood, and the bark
/// pattern by name. A missing value keeps the built-in one. Spring and autumn are the colours the crown reaches, not a blend.
/// </summary>
public sealed class TreeColoursDefinition
{
    /// <summary>The bark patterns the game draws: general, scaly (spruce), white with dark marks (birch), furrowed (oak), smooth (beech).</summary>
    public static readonly string[] BarkPatterns = { "general", "scaly", "birch", "furrowed", "smooth" };
    public string Spring { get; set; }
    public string Summer { get; set; }
    public string Autumn { get; set; }
    public string Bark { get; set; }
    public string Dead { get; set; }
    public string BarkPattern { get; set; }

    public bool IsEmpty => Spring == null && Summer == null && Autumn == null && Bark == null && Dead == null && BarkPattern == null;

    internal void Validate(string species)
    {
        foreach (string colour in new[] { Spring, Summer, Autumn, Bark, Dead })
            if (colour != null && !(colour.Length == 7 && colour[0] == '#' && colour.Skip(1).All(Uri.IsHexDigit)))
                throw new InvalidDataException($"{species}: a színt #RRGGBB alakban add meg ({colour}).");
        if (BarkPattern != null && Array.IndexOf(BarkPatterns, BarkPattern) < 0)
            throw new InvalidDataException($"{species}: ismeretlen kéregminta: {BarkPattern}.");
    }
}

/// <summary>
/// A world as authored in the editor: generated from a seed, then painted. The game builds new worlds from it;
/// a running world keeps the map it was created with.
/// </summary>
public sealed class WorldScenario
{
    public string Name { get; set; } = "Új pálya";
    public int Seed { get; set; } = 42;
    /// <summary>Tiles per side of the square map.</summary>
    public int Size { get; set; } = 64;
    /// <summary>Optional portable gzip/base64 world checkpoint. Paint is applied over this exact scene.</summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string InitialState { get; set; }
    public List<SoilTypeDefinition> SoilTypes { get; set; } = new();
    /// <summary>Elevation zones from low to high; a scene saved before zones existed reads the built-in ones.</summary>
    public List<ElevationZoneDefinition> ElevationZones { get; set; } = ElevationZoneDefinition.Defaults();
    /// <summary>How much deeper the soil horizons are drawn on the diorama's sides than they are, so thin horizons can be seen.</summary>
    public double ProfileScale { get; set; } = 6;
    /// <summary>Painted soil: soil type ID by tile ID, over the generated map.</summary>
    public Dictionary<int, string> SoilPaint { get; set; } = new();
    /// <summary>Painted terrain: absolute height by corner (node) ID; water lies where corners are below sea level.</summary>
    public Dictionary<int, int> HeightPaint { get; set; } = new();
    /// <summary>Painted initial forest: species ID by tile ID; an empty ID clears the tile.</summary>
    public Dictionary<int, string> ForestPaint { get; set; } = new();
    /// <summary>Springs: water added to a tile's surface, mm per environment hour. Streams and rivers start here.</summary>
    public Dictionary<int, double> Springs { get; set; } = new();
    /// <summary>The species of this world; empty means the built-in species unchanged.</summary>
    public List<SpeciesDefinition> Species { get; set; } = new();
    /// <summary>Roads built with the scene, in order, at no cost: each runs from one tile to another along the game's road path.</summary>
    public List<RoadSegment> Roads { get; set; } = new();
    /// <summary>Sawmills by top-left tile of their 2×2 footprint.</summary>
    public List<int> Sawmills { get; set; } = new();
    /// <summary>Depots by top-left tile; the first one receives the starting fleet (processor, forwarder, log truck).</summary>
    public List<int> Depots { get; set; } = new();
    /// <summary>The scene's missions: goals the game checks against world values while it runs.</summary>
    public List<MissionGoal> Missions { get; set; } = new();

    /// <summary>The index of the elevation zone a terrain level belongs to: the first zone that reaches it, else the highest.</summary>
    public int ZoneOf(int level)
    {
        for (int i = 0; i < ElevationZones.Count - 1; i++) if (level <= ElevationZones[i].MaxLevel) return i;
        return ElevationZones.Count - 1;
    }

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Name) || SoilTypes == null || SoilTypes.Count is 0 or > 64 || SoilPaint == null)
            throw new InvalidDataException("A pályának neve és 1–64 talajtípusa legyen.");
        if (Size is < 16 or > 128) throw new InvalidDataException("A pálya mérete 16–128 csempe lehet.");
        if (InitialState != null && (!InitialState.StartsWith("world-gzip-v1:", StringComparison.Ordinal) || InitialState.Length > 32 * 1024 * 1024))
            throw new InvalidDataException("Ismeretlen vagy túl nagy kiinduló jelenet.");
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var s in SoilTypes) {
            if (s == null || string.IsNullOrWhiteSpace(s.Id) || string.IsNullOrWhiteSpace(s.Name) || !ids.Add(s.Id))
                throw new InvalidDataException("Minden talajtípusnak egyedi azonosítója és neve legyen.");
            if (!(s.WiltingPoint >= 0 && s.FieldCapacity > s.WiltingPoint && s.Saturation > s.FieldCapacity))
                throw new InvalidDataException($"{s.Name}: hervadáspont < szabadföldi vízkapacitás < telítettség legyen.");
            if (!(s.InfiltrationPerHour >= 0 && s.DrainagePerHour >= 0 && s.Fertility is >= 0 and <= 1 && s.BearingCapacity is >= 0 and <= 1
                  && s.PreferredMoisture is >= 0 and <= 1 && s.PreferredTexture is >= 0 and <= 1))
                throw new InvalidDataException($"{s.Name}: érvénytelen paraméter (arányok 0–1, ráták nem negatívak).");
            s.Horizons ??= new();
            if (s.Horizons.Count > 8) throw new InvalidDataException($"{s.Name}: legfeljebb 8 talajszint lehet.");
            foreach (var h in s.Horizons)
                if (h == null || string.IsNullOrWhiteSpace(h.Name) || !(h.Depth is > 0 and <= 5)
                    || h.Color is not { Length: 7 } || h.Color[0] != '#' || !uint.TryParse(h.Color.AsSpan(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out _))
                    throw new InvalidDataException($"{s.Name}: minden talajszintnek neve, 0–5 m vastagsága és #RRGGBB színe legyen.");
        }
        HeightPaint ??= new(); ForestPaint ??= new(); Species ??= new(); Springs ??= new(); Missions ??= new();
        ElevationZones ??= ElevationZoneDefinition.Defaults();
        if (!(ProfileScale is >= 1 and <= 30)) throw new InvalidDataException("A talajszelvény nagyítása 1–30 legyen.");
        if (ElevationZones.Count is 0 or > 8) throw new InvalidDataException("A pályának 1–8 magassági öve legyen.");
        var zones = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < ElevationZones.Count; i++) {
            var z = ElevationZones[i];
            if (z == null || string.IsNullOrWhiteSpace(z.Id) || string.IsNullOrWhiteSpace(z.Name) || !zones.Add(z.Id))
                throw new InvalidDataException("Minden magassági övnek egyedi azonosítója és neve legyen.");
            if (z.MaxLevel < 0 || i > 0 && z.MaxLevel <= ElevationZones[i - 1].MaxLevel)
                throw new InvalidDataException($"{z.Name}: a magassági övek felső szintje alulról felfelé nőjön.");
            if (z.Color is not { Length: 7 } || z.Color[0] != '#' || !uint.TryParse(z.Color.AsSpan(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out _))
                throw new InvalidDataException($"{z.Name}: a szín #RRGGBB legyen.");
        }
        Roads ??= new(); Sawmills ??= new(); Depots ??= new();
        if (Roads.Count > 512 || Sawmills.Count > 32 || Depots.Count > 32) throw new InvalidDataException("Túl sok út, malom vagy telephely a pályán.");
        foreach (var r in Roads)
            if (r == null || r.From < 0 || r.To < 0 || r.From >= Size * Size || r.To >= Size * Size || Array.IndexOf(RoadSegment.Surfaces, r.Surface) < 0)
                throw new InvalidDataException("Egy út érvénytelen csempére vagy burkolatra mutat.");
        foreach (int tile in Sawmills.Concat(Depots))
            if (tile < 0 || tile >= Size * Size) throw new InvalidDataException("Egy malom vagy telephely érvénytelen csempére mutat.");
        if (Missions.Count > 32) throw new InvalidDataException("Egy pályán legfeljebb 32 küldetés lehet.");
        var missions = new HashSet<string>(StringComparer.Ordinal);
        foreach (var m in Missions) {
            if (m == null || string.IsNullOrWhiteSpace(m.Id) || !missions.Add(m.Id)) throw new InvalidDataException("Minden küldetésnek egyedi azonosítója legyen.");
            m.Validate();
        }
        foreach (var (tile, rate) in Springs)
            if (tile < 0 || tile >= Size * Size || !(rate > 0 && rate <= 100000)) throw new InvalidDataException("Egy forrás érvénytelen csempére mutat, vagy a hozama nem 0 és 100 000 mm/óra közötti.");
        var species = new HashSet<string>(StringComparer.Ordinal);
        foreach (var s in Species) {
            if (s == null || !species.Add(s.Id ?? "")) throw new InvalidDataException("Ismétlődő fafaj.");
            s.Validate();
        }
        foreach (var (node, height) in HeightPaint)
            if (node < 0 || node >= (Size + 1) * (Size + 1) || height < 0) throw new InvalidDataException("A festett domborzat érvénytelen pontra vagy magasságra mutat.");
        foreach (var (tile, kind) in ForestPaint)
            if (tile < 0 || tile >= Size * Size || kind == null || kind.Length > 0 && species.Count > 0 && !species.Contains(kind))
                throw new InvalidDataException("A festett erdő ismeretlen fafajra vagy csempére mutat.");
        foreach (var (tile, soil) in SoilPaint)
            if (tile < 0 || tile >= Size * Size || !ids.Contains(soil)) throw new InvalidDataException("A festett talaj ismeretlen talajtípusra vagy csempére mutat.");
    }
}

/// <summary>
/// One mission of a scene: a world value (a <c>world.*</c> field or a dynamics stock) compared with a target, optionally
/// before a deadline forest year. The game evaluates it from the current state; nothing is stored beyond the scene.
/// </summary>
public sealed class MissionGoal
{
    public static readonly string[] Comparisons = { ">=", "<=" };
    public string Id { get; set; } = "kuldetes";
    public string Title { get; set; } = "Új küldetés";
    public string Description { get; set; } = "";
    /// <summary>The value checked: e.g. <c>world.balance</c>, <c>world.deliveredTimber</c>, <c>world.treeCount</c>, <c>stock.x</c>.</summary>
    public string Metric { get; set; } = "world.balance";
    public string Comparison { get; set; } = ">=";
    public double Target { get; set; }
    /// <summary>The forest year (<c>world.time</c>) by which the goal must be reached; 0 means no deadline.</summary>
    public double DeadlineYear { get; set; }

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Title) || Description == null || string.IsNullOrWhiteSpace(Metric)
            || !(Metric.StartsWith("world.", StringComparison.Ordinal) || Metric.StartsWith("stock.", StringComparison.Ordinal)))
            throw new InvalidDataException($"{Title}: a küldetésnek címe és world.* vagy stock.* mérőszáma legyen.");
        if (Array.IndexOf(Comparisons, Comparison) < 0 || !double.IsFinite(Target) || !(DeadlineYear is >= 0 and <= 1000))
            throw new InvalidDataException($"{Title}: érvénytelen feltétel, cél vagy határidő (0–1000 év).");
    }
}

/// <summary>A road of the scene: the game's road path from one tile to another, with its surface.</summary>
public sealed class RoadSegment
{
    public static readonly string[] Surfaces = { "Asphalt", "Macadam" };
    public int From { get; set; }
    public int To { get; set; }
    public string Surface { get; set; } = "Macadam";
}
