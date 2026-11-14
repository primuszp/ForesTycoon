#nullable enable
using System;
using System.Threading.Tasks;
namespace ForesTycoon.Editor.Core;

/// <summary>Serial background preview slot; coalesces requests while keeping the last completed value visible.</summary>
internal sealed class EditorPreviewBuild<T> : IDisposable where T : class
{
    private readonly Func<Func<T>, Task<T>> schedule;
    private Task<T>? task;
    private string? taskKey, valueKey;
    private bool disposed;
    internal T? Value { get; private set; }
    internal string? Error { get; private set; }
    internal bool Busy => task != null;
    internal bool Current(string? key) => Error == null && Value != null && valueKey == key && task == null;
    internal string Why(string? key) => Error ?? (task != null ? $"épül ({taskKey}; kész érték kulcsa: {valueKey ?? "nincs"})" : Value == null ? "még nem indult" : $"más kulcs: {valueKey} ≠ {key}");
    internal EditorPreviewBuild(Func<Func<T>, Task<T>>? schedule = null) => this.schedule = schedule ?? (build => Task.Run(build));
    internal void Update(string key, Func<T> build)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (task is { IsCompleted: true })
        {
            try { Value = task.GetAwaiter().GetResult(); Error = null; }
            catch (Exception error) { Error = error.GetBaseException().Message; }
            valueKey = taskKey; task = null;
        }
        if (task == null && valueKey != key) { taskKey = key; task = schedule(build); }
    }
    public void Dispose()
    {
        if (disposed) return;
        disposed = true; Value = null;
        if (task != null) _ = task.ContinueWith(completed => { _ = completed.Exception; },
            System.Threading.CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        task = null;
    }
}
