#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace ForesTycoon.Editor.Core;

/// <summary>The document's tunable values. Catalogs are projections; captures are independent overrides.</summary>
internal sealed class EditorTuning
{
    private GameTuning values = GameTuning.Default;
    internal event Action? Changed;

    internal double Get(string key) => values[(GameTuning.Spec(key) ?? throw new ArgumentException("Ismeretlen hangolási paraméter: " + key, nameof(key))).Key];
    internal Dictionary<string, double> Capture() => values.ToOverrides();

    internal void Set(string key, double value)
    {
        var next = Capture(); next[key] = value; Replace(next);
    }

    internal void Replace(IReadOnlyDictionary<string, double> overrides)
    {
        var next = GameTuning.FromOverrides(overrides);
        if (GameTuning.Specs.All(spec => values[spec.Key] == next[spec.Key])) return;
        values = next; Changed?.Invoke();
    }
}
