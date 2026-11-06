#nullable enable
namespace ForesTycoon.Editor.Core;

/// <summary>Trial and publish results belong to the captured draft, never to whatever is open on completion.</summary>
internal sealed class WorkshopVersions
{
    internal string? Current { get; private set; }
    internal string? Saved { get; private set; }
    internal string? Tested { get; private set; }
    internal string? Published { get; private set; }
    internal string? Running { get; private set; }
    internal bool Unsaved => Current != Saved;
    internal bool TestedCurrent => Tested != null && Tested == Current;
    internal void Observe(string revision) { if (Current == null) Saved = revision; Current = revision; }
    internal void MarkSaved() => Saved = Current;
    internal void MarkSaved(string capturedRevision) => Saved = capturedRevision;
    internal void CompleteTrial(string capturedRevision) => Tested = capturedRevision;
    internal void MarkPublished(string capturedRevision) => Published = capturedRevision;
    internal void Acknowledge(string? runtimeRevision) => Running = runtimeRevision;
    internal void Reset() { Current = Saved = Tested = Published = Running = null; }
}
