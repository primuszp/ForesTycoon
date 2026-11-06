#nullable enable
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ForesTycoon.Rules;

namespace ForesTycoon.Editor.Core;

internal sealed record WorldTrialResult(double[] Baseline, double[] Edited, double[] Water, bool[] Unpaved,
    string Summary, int Columns, int Rows, string SourceRevision)
{
    internal bool Fits(int columns, int rows) => Columns == columns && Rows == rows
        && Baseline.Length == (long)columns * rows && Edited.Length == Baseline.Length
        && Water.Length == Baseline.Length && Unpaved.Length == Baseline.Length;
}

/// <summary>Owns the rain comparison lifecycle; results cannot cross a document invalidation.</summary>
internal sealed class TrialService : IDisposable
{
    private Task<WorldTrialResult>? task;
    private CancellationTokenSource? cancellation;
    private long generation, runningGeneration;
    private string? sourceRevision;
    internal bool IsBusy => task != null;
    internal bool HasSource => !string.IsNullOrEmpty(sourceRevision);
    internal WorldTrialResult? Result { get; private set; }
    internal string? Error { get; private set; }

    internal void Start(RuleProject baseline, RuleProject edited, int intensity, int seconds, string revision)
    {
        if (IsBusy) throw new InvalidOperationException("A próba már folyamatban van.");
        var before = baseline.Clone(); var after = edited.Clone();
        cancellation = new(); var token = cancellation.Token;
        runningGeneration = generation; Result = null; Error = null;
        sourceRevision = revision;
        task = Task.Run(() => Run(before, after, intensity, seconds, revision, token), token);
    }

    internal void Poll()
    {
        if (task?.IsCompleted != true) return;
        try {
            var result = task.GetAwaiter().GetResult();
            if (runningGeneration == generation) Result = result;
        } catch (OperationCanceledException) { }
        catch (Exception error) { if (runningGeneration == generation) Error = error.GetBaseException().Message; }
        finally { task = null; cancellation?.Dispose(); cancellation = null; }
    }

    internal WorldTrialResult? ResultFor(int columns, int rows) => Result?.Fits(columns, rows) == true ? Result : null;
    internal void Show(WorldTrialResult result) { Invalidate(); Result = result; sourceRevision = result.SourceRevision; }
    internal void ValidateSource(string revision) { if (HasSource && sourceRevision != revision) Invalidate(); }
    internal void Invalidate() { generation++; Result = null; Error = null; sourceRevision = null; cancellation?.Cancel(); }
    public void Dispose()
    {
        Invalidate();
        try { task?.GetAwaiter().GetResult(); } catch (OperationCanceledException) { } catch (Exception) { /* Observed by the owner at shutdown. */ }
        task = null; cancellation?.Dispose(); cancellation = null;
    }

    /// <summary>Same map, same rain, two rule sets; trafficability of every unpaved tile at the end.</summary>
    internal static WorldTrialResult Run(RuleProject baseline, RuleProject edited, int intensity, int seconds, string revision = "", CancellationToken cancellation = default)
    {
        (double[] Traffic, double[] Water, bool[] Unpaved, double Mean, double Soft, int Columns, int Rows) Run(RuleProject project) =>
            ForestSpeciesBook.WithContext<(double[] Traffic, double[] Water, bool[] Unpaved, double Mean, double Soft, int Columns, int Rows)>(null, () => {
            cancellation.ThrowIfCancellationRequested();
            using var world = SimulationLoader.CreateProject(EditorDocument.ForSandbox(project));
            if (intensity > 0) { world.QueueWeather(ForesTycoon.Ecology.WeatherPreset.Rain, intensity, seconds); world.ExecutePendingCommands(); }
            for (int i = 0; i < seconds * 30; i++) { cancellation.ThrowIfCancellationRequested(); world.Update(1.0 / 30); }
            var (columns, rows) = world.TileGrid; int n = columns * rows;
            var traffic = new double[n]; var water = new double[n]; var unpaved = new bool[n];
            for (int t = 0; t < n; t++) { unpaved[t] = world.IsUnpaved(t); traffic[t] = world.Trafficability(t); water[t] = world.TileSurfaceWater(t); }
            var open = Enumerable.Range(0, n).Where(t => unpaved[t]).ToArray();
            return (traffic, water, unpaved, open.Length == 0 ? double.NaN : open.Average(t => traffic[t]),
                open.Length == 0 ? double.NaN : open.Count(t => traffic[t] < .5) / (double)open.Length, columns, rows);
        });
        int Flooded(double[] water) => water.Count(w => w > 50);
        var a = Run(baseline); var b = Run(edited);
        static string Format(double value, string format) => double.IsNaN(value) ? "nincs burkolatlan csempe" : value.ToString(format);
        string summary = $"Átlagos járhatóság: alap {Format(a.Mean, "0.00")}, tiéd {Format(b.Mean, "0.00")}\n"
            + $"Nehezen járható (< 0,5) burkolatlan csempe: alap {Format(a.Soft * 100, "0'%'")}, tiéd {Format(b.Soft * 100, "0'%'")}\n"
            + $"Elöntött csempe (50 mm-nél több felszíni víz): alap {Flooded(a.Water)}, tiéd {Flooded(b.Water)}\n"
            + $"Eső: {intensity}, {seconds} s. A térképek a próba végét mutatják.";
        return new(a.Traffic, b.Traffic, b.Water, b.Unpaved, summary, b.Columns, b.Rows, revision);
    }
}
