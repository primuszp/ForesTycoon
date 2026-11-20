using System;
using System.Collections.Generic;

namespace ForesTycoon.Ecology;

/// <summary>Optional editor recording. Each monthly frame is reused after two years.</summary>
internal sealed class ForestTreeHistory
{
    internal const int Months = 24;
    private readonly Dictionary<ulong, TreeMonthSample>[] frames = new Dictionary<ulong, TreeMonthSample>[Months + 1];
    private readonly ulong[] frameMonths = new ulong[Months + 1];
    private ForestSystem source;
    private ulong completedMonth;

    internal void Observe(ForestSystem forest)
    {
        if (ReferenceEquals(source, forest)) return;
        if (source != null) { source.MonthlyTreeObserved -= Record; source.ObservationReset -= Clear; source.ObservationMonthClosed -= Close; }
        Clear();
        source = forest;
        if (source != null) { source.MonthlyTreeObserved += Record; source.ObservationReset += Clear; source.ObservationMonthClosed += Close; }
    }

    private void Close(ulong month) => completedMonth = month;
    private void Clear() { completedMonth = 0; Array.Clear(frames); Array.Clear(frameMonths); }

    private void Record(TreeMonthSample sample)
    {
        int index = (int)(sample.Month % (Months + 1));
        var frame = frames[index] ??= new(source.IndividualTreeCount);
        if (frameMonths[index] != sample.Month) { frame.Clear(); frameMonths[index] = sample.Month; }
        frame[sample.Id] = sample;
    }

    internal int Read(ulong id, Span<TreeMonthSample> result)
    {
        if (source == null) return 0;
        if (result.Length < Months) throw new ArgumentException("A full history buffer is required.", nameof(result));
        ulong end = completedMonth;
        int count = 0;
        for (ulong month = end >= Months ? end - Months + 1 : 1; month <= end; month++) {
            int index = (int)(month % (Months + 1));
            if (frameMonths[index] == month && frames[index].TryGetValue(id, out var sample))
                result[count++] = sample;
        }
        return count;
    }
}
