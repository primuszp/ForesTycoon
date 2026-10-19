using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ForesTycoon.Rules;

/// <summary>The single authoring and runtime contract shared by the game, editor and agents.</summary>
public sealed class RuleProject
{
    private static readonly Lazy<string> ShippedDocument = new(() => {
        using var stream = typeof(RuleProject).Assembly.GetManifestResourceStream("ForesTycoon.Rules.DefaultProject")
            ?? throw new InvalidDataException("Hiányzó, az Editorban létrehozott alap-szabályprojekt.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    });
    /// <summary>The authored Editor document, copied for each caller. Never synthesizes gameplay in the engine.</summary>
    public static RuleProject CreateDefault() => FromJson(ShippedDocument.Value);
    private static readonly Lazy<Dictionary<(string Hook, string Kind), BehaviorGraph>> ShippedGraphs = new(() =>
        CreateDefault().Behaviors.Graphs.Where(g => !g.ObjectId.HasValue).ToDictionary(g => (g.Hook, g.Kind)));
    /// <summary>A fresh authoring template from the Editor document, also used during legacy migration.</summary>
    public static BehaviorGraph DefaultGraph(BehaviorHook hook)
    {
        if (!ShippedGraphs.Value.TryGetValue((hook.Id, hook.Kind), out var graph))
            throw new InvalidDataException("Az Editor alap-szabályprojektjéből hiányzik a kötés: " + hook.Id + " / " + hook.Kind);
        return new BehaviorModel { Graphs = new() { graph } }.Clone().Graphs[0];
    }
    public string Schema { get; set; } = "forest-rule-project";
    public int Version { get; set; } = 4;
    public string Name { get; set; } = "ForesTycoon szabályrendszer";
    public Dictionary<string, double> Tuning { get; set; } = new();
    public BehaviorModel Behaviors { get; set; } = new();
    public DynamicsModel Dynamics { get; set; } = new();
    public AlgorithmGraphReference Algorithms { get; set; }
    /// <summary>The world map and its soil types, authored in the world editor; used when a new world is created.</summary>
    public WorldScenario World { get; set; }
    /// <summary>The rule book: rules written as schedule + scope + condition + effects, run by the game in this order.</summary>
    public List<WorldRule> Rules { get; set; } = new();
    /// <summary>Legacy migration data. Current projects execute exactly the authored rule book.</summary>
    public List<string> RemovedStarters { get; set; } = new();
    /// <summary>State variables the rules may add to a scope.</summary>
    public List<RuleFieldDefinition> Fields { get; set; } = new();
    public RuleAuthoring Authoring { get; set; } = new();
    internal static readonly JsonSerializerOptions JsonOptions = new() {
        WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };
    public string ToJson() => JsonSerializer.Serialize(this, JsonOptions);
    public static RuleProject FromJson(string json)
    {
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind == JsonValueKind.Object && document.RootElement.TryGetProperty("road", out _))
            throw new InvalidDataException("A projekt megszűnt külön útkopási modellt tartalmaz (road). A forgalmi kopást a közös szabálykönyv áthaladási gráfja szerkeszti; a régi modellt nem hagyjuk el észrevétlenül.");
        return JsonSerializer.Deserialize<RuleProject>(json, JsonOptions) ?? throw new InvalidDataException("Üres szabályprojekt.");
    }
    public RuleProject Clone() => FromJson(ToJson());
    /// <summary>Stable document identity, independent of object/dictionary property order.</summary>
    public string ContentRevision()
    {
        using var document = JsonDocument.Parse(ToJson());
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream)) WriteCanonical(writer, document.RootElement, true);
        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(stream.ToArray())).ToLowerInvariant();
    }
    private static void WriteCanonical(Utf8JsonWriter writer, JsonElement element, bool root = false)
    {
        if (element.ValueKind == JsonValueKind.Object) {
            writer.WriteStartObject();
            foreach (var property in element.EnumerateObject().OrderBy(p => p.Name, StringComparer.Ordinal)) {
                if (root && property.Name == "authoring") continue; // Editor organization is not a running game rule.
                writer.WritePropertyName(property.Name); WriteCanonical(writer, property.Value);
            }
            writer.WriteEndObject();
        } else if (element.ValueKind == JsonValueKind.Array) {
            writer.WriteStartArray(); foreach (var item in element.EnumerateArray()) WriteCanonical(writer, item); writer.WriteEndArray();
        } else element.WriteTo(writer);
    }
    public static string DefaultPath => Path.GetFullPath(Environment.GetEnvironmentVariable("FORESTYCOON_RULE_PROJECT")
        ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ForesTycoon", "rules", "project.json"));
    public static void WriteAtomic(string path, string json)
    {
        path = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { File.WriteAllText(temporary, json); File.Move(temporary, path, true); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}

/// <summary>Shared human/agent authoring data; the runtime neither executes nor hashes these labels and templates.</summary>
public sealed class RuleAuthoring
{
    public Dictionary<string, RulePresentation> Entries { get; set; } = new();
    public List<RuleTemplate> Templates { get; set; } = new();
    /// <summary>Project wording of model quantities, by quantity ID; empty fields keep the shipped vocabulary.</summary>
    public Dictionary<string, QuantityPresentation> Quantities { get; set; } = new();
    /// <summary>Rule formula source by "hook|kind"; the compiled graph in <c>behaviors</c> is what runs.</summary>
    public Dictionary<string, string> Formulas { get; set; } = new();
}
public sealed class QuantityPresentation
{
    public string Name { get; set; } = "";
    public string Unit { get; set; } = "";
    public string Description { get; set; } = "";
    public string Subsystem { get; set; } = "";
    public string Layer { get; set; } = "";
}
public sealed class RulePresentation
{
    public int Topic { get; set; }
    public string Notes { get; set; } = "";
}
public sealed class RuleTemplate
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "Szabálysablon";
    public BehaviorGraph Graph { get; set; }
    public BehaviorController Controller { get; set; }
}

/// <summary>Immutable graph bundle, compiled by the host before changing the running algorithms.</summary>
public sealed class AlgorithmGraphReference
{
    public string Revision { get; set; } = "";
    public string BundlePath { get; set; } = "";
    public string WorkspaceRoot { get; set; } = "";
    public string HostVersion { get; set; } = "graph-host/1";
}

public sealed class DynamicsStock
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "Készlet";
    public string Unit { get; set; } = "1";
    public double Initial { get; set; }
    public double Minimum { get; set; }
    public double Maximum { get; set; } = 1e12;
}

public sealed class DynamicsFlow
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "Áramlás";
    // Empty endpoints are the environment: a source or a sink.
    public string From { get; set; } = "";
    public string To { get; set; } = "";
    public BehaviorGraph Rate { get; set; } = new();
}

public sealed class DynamicsModel
{
    public List<DynamicsStock> Stocks { get; set; } = new();
    public List<DynamicsFlow> Flows { get; set; } = new();
}

/// <summary>Simultaneous Euler integration. Feedback reads the previous tick; transfers conserve stocks.</summary>
public sealed class DynamicsRuntime
{
    private readonly DynamicsModel model;
    private readonly CompiledBehaviorModel rates;
    private readonly Dictionary<string, double> values = new(StringComparer.Ordinal);
    private readonly Func<string, double> worldValue;
    private readonly double[] amounts, incoming, outgoing, outScale, inScale, changes;
    private readonly int[] sources, targets;
    public string LastError { get; private set; }
    public DynamicsModel Document => RuleProject.FromJson(new RuleProject { Dynamics = model }.ToJson()).Dynamics;
    public IReadOnlyDictionary<string, double> Values => new System.Collections.ObjectModel.ReadOnlyDictionary<string, double>(values);
    public static HashSet<string> Keys(DynamicsModel model, IEnumerable<string> worldKeys) =>
        new(worldKeys.Concat(model.Stocks.Select(s => "stock." + s.Id)), StringComparer.Ordinal);
    public static BehaviorHook FlowHook(DynamicsFlow flow) => new("flow." + flow.Id, flow.Name, "dynamics",
        "készletegység / szimuláció-s", 0, 1e12, "Nem használt", "Nem használt", "Fix tick eleje; előző készletállapot", false);

    public DynamicsRuntime(DynamicsModel source, IEnumerable<string> worldKeys, Func<string, double> worldValue)
    {
        if (source == null) throw new InvalidDataException("Hiányzó dinamika.");
        model = RuleProject.FromJson(new RuleProject { Dynamics = source }.ToJson()).Dynamics;
        this.worldValue = worldValue;
        if (model.Stocks == null || model.Flows == null || model.Stocks.Count > 256 || model.Flows.Count > 512)
            throw new InvalidDataException("Legfeljebb 256 készlet és 512 áramlás engedélyezett.");
        foreach (var stock in model.Stocks) {
            if (stock == null || string.IsNullOrWhiteSpace(stock.Id) || stock.Id.Contains('.') || string.IsNullOrWhiteSpace(stock.Name)
                || string.IsNullOrWhiteSpace(stock.Unit) || !double.IsFinite(stock.Initial) || !double.IsFinite(stock.Minimum)
                || !double.IsFinite(stock.Maximum) || stock.Minimum > stock.Initial || stock.Initial > stock.Maximum
                || stock.Maximum - stock.Minimum > 1e12 || !double.IsFinite(stock.Maximum - stock.Minimum)
                || !values.TryAdd(stock.Id, stock.Initial)) throw new InvalidDataException("Érvénytelen vagy ismétlődő készlet.");
        }
        var ids = new HashSet<string>();
        foreach (var flow in model.Flows) {
            if (flow == null || string.IsNullOrWhiteSpace(flow.Id) || !ids.Add(flow.Id) || string.IsNullOrWhiteSpace(flow.Name)
                || flow.From == null || flow.To == null || flow.From == flow.To || flow.Rate == null
                || flow.From.Length > 0 && !values.ContainsKey(flow.From) || flow.To.Length > 0 && !values.ContainsKey(flow.To))
                throw new InvalidDataException("Hibás áramlás vagy hiányzó végpont.");
            if (flow.From.Length > 0 && flow.To.Length > 0 && model.Stocks.Find(s => s.Id == flow.From)!.Unit != model.Stocks.Find(s => s.Id == flow.To)!.Unit)
                throw new InvalidDataException("Az átadás két készletének egysége egyezzen.");
            if (flow.Rate.Hook != "flow." + flow.Id || flow.Rate.Kind != "dynamics" || flow.Rate.ObjectId.HasValue)
                throw new InvalidDataException("Az áramlás képletének kötése hibás.");
        }
        rates = new(new BehaviorModel { Graphs = model.Flows.Select(f => f.Rate).ToList() },
            model.Flows.Select(FlowHook), Keys(model, worldKeys), Read);
        amounts = new double[model.Flows.Count]; sources = new int[amounts.Length]; targets = new int[amounts.Length];
        incoming = new double[model.Stocks.Count]; outgoing = new double[incoming.Length];
        outScale = new double[incoming.Length]; inScale = new double[incoming.Length]; changes = new double[incoming.Length];
        var indices = model.Stocks.Select((s, i) => (s.Id, i)).ToDictionary(p => p.Id, p => p.i);
        for (int i = 0; i < model.Flows.Count; i++) {
            sources[i] = model.Flows[i].From.Length == 0 ? -1 : indices[model.Flows[i].From];
            targets[i] = model.Flows[i].To.Length == 0 ? -1 : indices[model.Flows[i].To];
        }
    }
    public double Read(string key) => key.StartsWith("stock.", StringComparison.Ordinal)
        ? values.TryGetValue(key[6..], out var value) ? value : throw new ArithmeticException("Hiányzó készlet: " + key)
        : worldValue(key);
    public Dictionary<string, double> Capture() => new(values, StringComparer.Ordinal);
    public void Restore(IReadOnlyDictionary<string, double> snapshot)
    {
        if (snapshot == null || snapshot.Count != values.Count || model.Stocks.Any(s => !snapshot.TryGetValue(s.Id, out double v)
            || !double.IsFinite(v) || v < s.Minimum || v > s.Maximum)) throw new InvalidDataException("Hibás dinamikai készletállapot.");
        foreach (var stock in model.Stocks) values[stock.Id] = snapshot[stock.Id];
    }
    public void Preserve(DynamicsRuntime previous)
    {
        foreach (var stock in model.Stocks)
            if (previous.model.Stocks.Any(s => s.Id == stock.Id && s.Unit == stock.Unit))
                values[stock.Id] = Math.Clamp(previous.values[stock.Id], stock.Minimum, stock.Maximum);
    }
    public void Step(double delta, double time) { foreach (int _ in StepWork(delta, time)) { } }
    public IEnumerable<int> StepWork(double delta, double time)
    {
        if (!double.IsFinite(delta) || delta < 0 || !double.IsFinite(time)) throw new ArgumentOutOfRangeException(nameof(delta));
        LastError = null;
        rates.ClearError();
        if (delta == 0 || model.Flows.Count == 0) yield break;
        Array.Clear(incoming); Array.Clear(outgoing); Array.Clear(changes);
        for (int i = 0; i < amounts.Length; i++) {
            yield return 1;
            var flow = model.Flows[i];
            amounts[i] = rates.Evaluate(flow.Rate.Hook, "dynamics", 0, new(0, delta, time, 0, 0)) * delta;
            if (rates.LastError != null || !double.IsFinite(amounts[i])) { LastError = rates.LastError ?? "Áramlási túlcsordulás."; yield break; }
            if (sources[i] >= 0) outgoing[sources[i]] += amounts[i];
            if (targets[i] >= 0) incoming[targets[i]] += amounts[i];
        }
        for (int i = 0; i < model.Stocks.Count; i++) {
            yield return 1;
            if (!double.IsFinite(incoming[i]) || !double.IsFinite(outgoing[i])) { LastError = "Összesített áramlási túlcsordulás."; yield break; }
            var s = model.Stocks[i];
            outScale[i] = outgoing[i] == 0 ? 1 : Math.Min(1, (values[s.Id] - s.Minimum) / outgoing[i]);
            inScale[i] = incoming[i] == 0 ? 1 : Math.Min(1, (s.Maximum - values[s.Id]) / incoming[i]);
        }
        for (int i = 0; i < amounts.Length; i++) {
            yield return 1;
            double amount = amounts[i] * Math.Min(sources[i] < 0 ? 1 : outScale[sources[i]], targets[i] < 0 ? 1 : inScale[targets[i]]);
            if (sources[i] >= 0) changes[sources[i]] -= amount;
            if (targets[i] >= 0) changes[targets[i]] += amount;
        }
        for (int i = 0; i < model.Stocks.Count; i++) {
            yield return 1;
            var stock = model.Stocks[i]; values[stock.Id] = Math.Clamp(values[stock.Id] + changes[i], stock.Minimum, stock.Maximum);
        }
    }
}
