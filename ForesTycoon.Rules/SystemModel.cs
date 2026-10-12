using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace ForesTycoon.Rules;

/// <summary>System dynamics role of a game quantity. Only stocks, flows and auxiliaries take part in feedback.</summary>
public enum SystemVariableKind { Stock, Flow, Auxiliary, Parameter, Structure, Input, Presentation }

/// <summary>One thematic home for rules, quantities and (later) content and scenario objects.</summary>
public sealed class SystemSubsystem
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string[] Modules { get; set; } = Array.Empty<string>();
    public string Description { get; set; } = "";
}

/// <summary>A horizontal layer of a map tile (groundwater, soil, terrain, surface, vegetation, atmosphere) or a non-spatial one.</summary>
public sealed class SystemLayer
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public bool PerTile { get; set; } = true;
    public string Description { get; set; } = "";
}

/// <summary>A named game quantity read or written by rules. <see cref="Observe"/> is its formula key, when exposed.</summary>
public sealed class SystemVariable
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Subsystem { get; set; } = "";
    public SystemVariableKind Kind { get; set; }
    public string Unit { get; set; } = "";
    public string Observe { get; set; } = "";
    public string Description { get; set; } = "";
    /// <summary>The tile layer the quantity belongs to; empty for clocks, commands and presentation.</summary>
    public string Layer { get; set; } = "";
}

/// <summary>
/// The shared vocabulary of the game's quantities. The rule catalog says which process reads and writes which field;
/// this document says what those fields are. Both people and agents extend it; the runtime never executes it.
/// </summary>
public sealed class SystemVocabulary
{
    public string Schema { get; set; } = "forest-system-model";
    public int Version { get; set; } = 1;
    public string Name { get; set; } = "";
    public List<SystemSubsystem> Subsystems { get; set; } = new();
    /// <summary>Top to bottom: what stands on a tile above what lies beneath it.</summary>
    public List<SystemLayer> Layers { get; set; } = new();
    public List<SystemVariable> Variables { get; set; } = new();

    private Dictionary<string, SystemVariable> byId, byObserve;
    private static readonly Lazy<SystemVocabulary> Embedded = new(() => {
        using var stream = typeof(SystemVocabulary).Assembly.GetManifestResourceStream("ForesTycoon.Rules.Catalog.system-model.json")
            ?? throw new InvalidDataException("Hiányzó beépített rendszermodell.");
        using var reader = new StreamReader(stream);
        return FromJson(reader.ReadToEnd());
    });
    /// <summary>The vocabulary shipped with this build of the game.</summary>
    public static SystemVocabulary Default => Embedded.Value;

    public static SystemVocabulary FromJson(string json)
    {
        var vocabulary = JsonSerializer.Deserialize<SystemVocabulary>(json, RuleProject.JsonOptions) ?? throw new InvalidDataException("Üres rendszermodell.");
        vocabulary.Validate(); return vocabulary;
    }
    public string ToJson() => JsonSerializer.Serialize(this, RuleProject.JsonOptions);

    public void Validate()
    {
        if (Schema != "forest-system-model" || Version != 1 || Subsystems == null || Subsystems.Count == 0 || Variables == null)
            throw new InvalidDataException("Ismeretlen rendszermodell-szótár.");
        var subsystems = new HashSet<string>(StringComparer.Ordinal); var modules = new HashSet<string>(StringComparer.Ordinal);
        foreach (var s in Subsystems)
            if (s == null || string.IsNullOrWhiteSpace(s.Id) || string.IsNullOrWhiteSpace(s.Name) || !subsystems.Add(s.Id)
                || s.Modules == null || s.Modules.Any(m => string.IsNullOrWhiteSpace(m) || !modules.Add(m)))
                throw new InvalidDataException("Hibás vagy ismétlődő alrendszer: " + s?.Id);
        var layers = new HashSet<string>(StringComparer.Ordinal);
        foreach (var l in Layers ?? throw new InvalidDataException("Hiányzó rétegek."))
            if (l == null || string.IsNullOrWhiteSpace(l.Id) || string.IsNullOrWhiteSpace(l.Name) || l.Description == null || !layers.Add(l.Id))
                throw new InvalidDataException("Hibás vagy ismétlődő réteg: " + l?.Id);
        byId = new(StringComparer.Ordinal); byObserve = new(StringComparer.Ordinal);
        foreach (var v in Variables) {
            if (v == null || string.IsNullOrWhiteSpace(v.Id) || string.IsNullOrWhiteSpace(v.Name) || !subsystems.Contains(v.Subsystem)
                || !Enum.IsDefined(v.Kind) || v.Unit == null || v.Observe == null || v.Description == null || v.Layer == null
                || v.Layer.Length > 0 && !layers.Contains(v.Layer) || !byId.TryAdd(v.Id, v))
                throw new InvalidDataException("Hibás vagy ismétlődő rendszerváltozó: " + v?.Id);
            if (v.Observe.Length > 0 && !byObserve.TryAdd(v.Observe, v)) throw new InvalidDataException("Ismétlődő mérési kulcs: " + v.Observe);
        }
    }
    /// <summary>A copy with a project's wording applied; unknown quantities and subsystems are ignored.</summary>
    public SystemVocabulary With(IReadOnlyDictionary<string, QuantityPresentation> overrides)
    {
        if (overrides == null || overrides.Count == 0) return this;
        var copy = FromJson(ToJson());
        foreach (var (id, o) in overrides) {
            var v = copy.Variables.Find(x => x.Id == id);
            if (v == null || o == null) continue;
            if (!string.IsNullOrWhiteSpace(o.Name)) v.Name = o.Name.Trim();
            if (!string.IsNullOrWhiteSpace(o.Unit)) v.Unit = o.Unit.Trim();
            if (!string.IsNullOrWhiteSpace(o.Description)) v.Description = o.Description.Trim();
            if (copy.Subsystems.Any(s => s.Id == o.Subsystem)) v.Subsystem = o.Subsystem;
            if (o.Layer != null && copy.Layers.Any(l => l.Id == o.Layer)) v.Layer = o.Layer;
        }
        copy.Validate(); return copy;
    }
    public SystemVariable Find(string id) => id != null && byId.TryGetValue(id, out var v) ? v : null;
    /// <summary>The quantity a formula key (for example <c>world.treeCount</c>) observes.</summary>
    public SystemVariable Observed(string key) => key != null && byObserve.TryGetValue(key, out var v) ? v : null;
    public int SubsystemIndex(string id) => Subsystems.FindIndex(s => s.Id == id);
    public int LayerIndex(string id) => Layers.FindIndex(l => l.Id == id);
    /// <summary>Index of the subsystem owning a catalog module; -1 for an unknown module.</summary>
    public int SubsystemOfModule(string module) => Subsystems.FindIndex(s => s.Modules.Contains(module));
    public static bool Endogenous(SystemVariableKind kind) => kind is SystemVariableKind.Stock or SystemVariableKind.Flow or SystemVariableKind.Auxiliary;
    public static string KindName(SystemVariableKind kind) => kind switch {
        SystemVariableKind.Stock => "Készlet", SystemVariableKind.Flow => "Áramlás", SystemVariableKind.Auxiliary => "Segédváltozó",
        SystemVariableKind.Parameter => "Paraméter / adat", SystemVariableKind.Structure => "Térkép és szerkezet",
        SystemVariableKind.Input => "Külső bemenet / óra", _ => "Megjelenítés"
    };
    public static string KindHelp(SystemVariableKind kind) => kind switch {
        SystemVariableKind.Stock => "Felhalmozódó állapot: értéke a be- és kiáramlások összegeként változik, és a következő lépésben visszahat.",
        SystemVariableKind.Flow => "Időegységre jutó mennyiség, amely készletet tölt vagy apaszt.",
        SystemVariableKind.Auxiliary => "Más értékekből lépésenként újraszámolt közbenső mennyiség.",
        SystemVariableKind.Parameter => "Rögzített adat vagy definíció (faj, talaj, beállítás); szerkesztése tartalmi kérdés.",
        SystemVariableKind.Structure => "Térkép, hálózat vagy nyilvántartás: a játékos és a pálya alakítja, nem folytonos dinamika.",
        SystemVariableKind.Input => "A modellen kívülről érkezik: óra, seed vagy játékosparancs.",
        _ => "Csak megjelenítés; nem hat vissza a szimulációra."
    };
}

/// <summary>A process of the model: a catalogued rule or a project-defined flow or formula.</summary>
public sealed record SystemProcess(string Id, string Name, string Subsystem, IReadOnlyList<string> Reads, IReadOnlyList<string> Writes, string RuleId = null);

/// <summary>Data influence from one quantity to another through the listed processes. It carries no proven sign.</summary>
public sealed record SystemInfluence(string From, string To, IReadOnlyList<string> Processes);

/// <summary>A closed chain: <c>Variables[i]</c> influences <c>Variables[(i+1) % n]</c> through <c>Steps[i]</c>.</summary>
public sealed record SystemLoop(IReadOnlyList<string> Variables, IReadOnlyList<IReadOnlyList<string>> Steps)
{
    public int Length => Variables.Count;
    /// <summary>Rotation-independent identity.</summary>
    public string Key
    {
        get {
            int first = 0;
            for (int i = 1; i < Variables.Count; i++) if (string.CompareOrdinal(Variables[i], Variables[first]) < 0) first = i;
            return string.Join(">", Enumerable.Range(0, Variables.Count).Select(i => Variables[(first + i) % Variables.Count]));
        }
    }
}

/// <summary>
/// Stock-and-flow view of the catalog: which processes change which stocks, which quantity influences which, the
/// strongly coupled groups and the shortest feedback loops among endogenous quantities. Catalog edges document data
/// access, not execution order or polarity; loops are therefore reported without a reinforcing/balancing label.
/// </summary>
public sealed class SystemModelAnalysis
{
    private readonly Dictionary<string, SystemProcess> processes = new(StringComparer.Ordinal);
    private readonly Dictionary<string, SystemVariable> variables = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<SystemProcess>> writers = new(StringComparer.Ordinal), readers = new(StringComparer.Ordinal);
    private readonly Dictionary<(string, string), List<string>> edges = new();
    private readonly Dictionary<string, List<string>> next = new(StringComparer.Ordinal);
    public SystemVocabulary Vocabulary { get; }
    public IReadOnlyList<SystemVariable> Variables { get; }
    public IReadOnlyList<SystemProcess> Processes { get; }
    public IReadOnlyList<SystemInfluence> Influences { get; }
    /// <summary>Groups in which every quantity feeds back to every other (strongly connected, two or more members).</summary>
    public IReadOnlyList<IReadOnlyList<string>> CoupledGroups { get; }
    /// <summary>The shortest loop through each stock, without duplicates; loops spanning more subsystems first.</summary>
    public IReadOnlyList<SystemLoop> KeyLoops { get; }
    /// <summary>Catalog fields without a vocabulary entry; shown as unclassified auxiliaries.</summary>
    public IReadOnlyList<string> Unclassified { get; }

    public SystemModelAnalysis(GameRuleCatalog catalog, SystemVocabulary vocabulary = null, IEnumerable<SystemProcess> extraProcesses = null,
        IEnumerable<SystemVariable> extraVariables = null)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        Vocabulary = vocabulary ?? SystemVocabulary.Default;
        foreach (var v in Vocabulary.Variables) variables[v.Id] = v;
        foreach (var v in extraVariables ?? Enumerable.Empty<SystemVariable>()) variables[v.Id] = v;
        var list = catalog.Rules.Select(r => new SystemProcess(r.Id, r.Name,
            Vocabulary.Subsystems.ElementAtOrDefault(Math.Max(0, Vocabulary.SubsystemOfModule(r.Module)))?.Id ?? "", r.Reads, r.Writes, r.Id)).ToList();
        list.AddRange(extraProcesses ?? Enumerable.Empty<SystemProcess>());
        var unclassified = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var p in list) {
            if (!processes.TryAdd(p.Id, p)) throw new InvalidDataException("Ismétlődő folyamat: " + p.Id);
            foreach (string field in p.Reads.Concat(p.Writes))
                if (!variables.ContainsKey(field)) {
                    unclassified.Add(field);
                    variables[field] = new SystemVariable { Id = field, Name = field, Subsystem = p.Subsystem, Kind = SystemVariableKind.Auxiliary };
                }
            foreach (string field in p.Writes) Add(writers, field, p);
            foreach (string field in p.Reads) Add(readers, field, p);
        }
        Unclassified = unclassified.ToArray();
        Processes = list;
        Variables = variables.Values.OrderBy(v => Vocabulary.SubsystemIndex(v.Subsystem)).ThenBy(v => v.Kind).ThenBy(v => v.Name, StringComparer.CurrentCulture).ToArray();

        foreach (var p in list)
            foreach (string from in p.Reads.Distinct())
                foreach (string to in p.Writes.Distinct())
                    if (from != to) {
                        if (!edges.TryGetValue((from, to), out var via)) edges[(from, to)] = via = new();
                        if (!via.Contains(p.Id)) via.Add(p.Id);
                    }
        Influences = edges.Select(e => new SystemInfluence(e.Key.Item1, e.Key.Item2, e.Value)).ToArray();
        foreach (var v in variables.Values.Where(v => SystemVocabulary.Endogenous(v.Kind))) next[v.Id] = new();
        foreach (var (from, to) in edges.Keys) if (next.ContainsKey(from) && next.ContainsKey(to)) next[from].Add(to);
        foreach (var l in next.Values) l.Sort(StringComparer.Ordinal);
        CoupledGroups = StronglyConnected();
        KeyLoops = Distinct(variables.Values.Where(v => v.Kind == SystemVariableKind.Stock).OrderBy(v => v.Id, StringComparer.Ordinal)
                .Select(v => LoopsThroughVariable(v.Id).FirstOrDefault()).Where(l => l != null))
            .OrderByDescending(Span).ThenBy(l => l.Length).ThenBy(l => l.Key, StringComparer.Ordinal).ToArray();
    }

    public SystemVariable Variable(string id) => id != null && variables.TryGetValue(id, out var v) ? v : null;
    public SystemProcess Process(string id) => id != null && processes.TryGetValue(id, out var p) ? p : null;
    public string Name(string id) => Variable(id)?.Name ?? Process(id)?.Name ?? id;
    /// <summary>Number of subsystems a loop crosses.</summary>
    public int Span(SystemLoop loop) => loop.Variables.Select(v => Variable(v)?.Subsystem).Distinct().Count();
    public IReadOnlyList<string> Via(string from, string to) => edges.TryGetValue((from, to), out var l) ? l : Array.Empty<string>();
    public IReadOnlyList<SystemProcess> Writers(string variable) => writers.TryGetValue(variable, out var l) ? l : Array.Empty<SystemProcess>();
    public IReadOnlyList<SystemProcess> Readers(string variable) => readers.TryGetValue(variable, out var l) ? l : Array.Empty<SystemProcess>();
    /// <summary>Stocks this process fills or drains: a process that writes a stock is one of its flows.</summary>
    public IReadOnlyList<SystemVariable> StocksChangedBy(string process) =>
        Process(process)?.Writes.Select(Variable).Where(v => v?.Kind == SystemVariableKind.Stock).ToArray() ?? Array.Empty<SystemVariable>();
    /// <summary>The stock limits its own change (for example drainage depends on the stored water).</summary>
    public bool ReadsOwnStock(string process) => Process(process) is SystemProcess p && p.Writes.Any(w => p.Reads.Contains(w) && Variable(w)?.Kind == SystemVariableKind.Stock);
    /// <summary>A quantity drawn in a layer: it has one, and it is not a clock, command or picture.</summary>
    public static bool OnLayer(SystemVariable v) => v.Layer.Length > 0 && v.Kind is not (SystemVariableKind.Input or SystemVariableKind.Presentation);
    /// <summary>Rules whose inputs and outputs lie in different layers, by (from, to) layer pair in stack order.</summary>
    public IEnumerable<(string From, string To, SystemProcess[] Processes)> CrossLayerRules() =>
        Processes.SelectMany(p => p.Reads.Select(Variable).Where(v => v != null && OnLayer(v)).Select(v => v.Layer).Distinct()
                .SelectMany(from => p.Writes.Select(Variable).Where(v => v != null && OnLayer(v)).Select(v => v.Layer).Distinct()
                    .Where(to => to != from).Select(to => (From: from, To: to, Process: p))))
            .GroupBy(x => (x.From, x.To))
            .OrderBy(g => Vocabulary.LayerIndex(g.Key.From)).ThenBy(g => Vocabulary.LayerIndex(g.Key.To))
            .Select(g => (g.Key.From, g.Key.To, g.Select(x => x.Process).Distinct().ToArray()));
    public IReadOnlyList<string> CoupledGroupOf(string variable) => CoupledGroups.FirstOrDefault(g => g.Contains(variable));

    /// <summary>For each quantity this one influences, the shortest way back to it; shortest loops first.</summary>
    public IReadOnlyList<SystemLoop> LoopsThroughVariable(string variable)
    {
        if (variable == null || !next.TryGetValue(variable, out var targets)) return Array.Empty<SystemLoop>();
        return Distinct(targets.Select(t => ShortestPath(t, variable) is string[] back ? Loop(new[] { variable }.Concat(back.SkipLast(1)).ToArray()) : null)
            .Where(l => l != null)).OrderBy(l => l.Length).ThenBy(l => l.Key, StringComparer.Ordinal).ToArray();
    }
    /// <summary>For each quantity the process reads and each it writes, the shortest way from the written one back.</summary>
    public IReadOnlyList<SystemLoop> LoopsThroughProcess(string process)
    {
        if (Process(process) is not SystemProcess p) return Array.Empty<SystemLoop>();
        var loops = new List<SystemLoop>();
        foreach (string read in p.Reads.Where(next.ContainsKey).Distinct())
            foreach (string written in p.Writes.Where(w => w != read && next.ContainsKey(w)).Distinct())
                if (ShortestPath(written, read) is string[] back) loops.Add(Loop(new[] { read }.Concat(back.SkipLast(1)).ToArray()));
        return Distinct(loops).OrderBy(l => l.Length).ThenBy(l => l.Key, StringComparer.Ordinal).ToArray();
    }

    private SystemLoop Loop(string[] cycle) =>
        new(cycle, cycle.Select((v, i) => (IReadOnlyList<string>)edges[(v, cycle[(i + 1) % cycle.Length])].ToArray()).ToArray());
    private static IEnumerable<SystemLoop> Distinct(IEnumerable<SystemLoop> loops) => loops.GroupBy(l => l.Key).Select(g => g.First());

    /// <summary>Breadth-first path from one quantity to another (both included), deterministic.</summary>
    private string[] ShortestPath(string from, string to)
    {
        if (from == to) return new[] { from };
        var parent = new Dictionary<string, string>(StringComparer.Ordinal) { [from] = null };
        var queue = new Queue<string>(); queue.Enqueue(from);
        while (queue.Count > 0) {
            string at = queue.Dequeue();
            foreach (string n in next[at]) {
                if (parent.ContainsKey(n)) continue;
                parent[n] = at;
                if (n == to) {
                    var path = new List<string>();
                    for (string x = to; x != null; x = parent[x]) path.Add(x);
                    path.Reverse(); return path.ToArray();
                }
                queue.Enqueue(n);
            }
        }
        return null;
    }

    private static void Add(Dictionary<string, List<SystemProcess>> index, string key, SystemProcess p)
    {
        if (!index.TryGetValue(key, out var l)) index[key] = l = new();
        if (!l.Contains(p)) l.Add(p);
    }

    /// <summary>Tarjan's strongly connected components, largest first.</summary>
    private IReadOnlyList<IReadOnlyList<string>> StronglyConnected()
    {
        int counter = 0;
        var index = new Dictionary<string, int>(StringComparer.Ordinal); var low = new Dictionary<string, int>(StringComparer.Ordinal);
        var stack = new Stack<string>(); var onStack = new HashSet<string>(StringComparer.Ordinal); var groups = new List<IReadOnlyList<string>>();
        void Visit(string v)
        {
            index[v] = low[v] = counter++; stack.Push(v); onStack.Add(v);
            foreach (string w in next[v]) {
                if (!index.ContainsKey(w)) { Visit(w); low[v] = Math.Min(low[v], low[w]); }
                else if (onStack.Contains(w)) low[v] = Math.Min(low[v], index[w]);
            }
            if (low[v] != index[v]) return;
            var group = new List<string>(); string x;
            do { x = stack.Pop(); onStack.Remove(x); group.Add(x); } while (x != v);
            if (group.Count > 1) groups.Add(group.OrderBy(id => id, StringComparer.Ordinal).ToArray());
        }
        foreach (string v in next.Keys.OrderBy(id => id, StringComparer.Ordinal)) if (!index.ContainsKey(v)) Visit(v);
        return groups.OrderByDescending(g => g.Count).ThenBy(g => g[0], StringComparer.Ordinal).ToArray();
    }
}
