using System;
using System.Diagnostics;
using System.Threading;

namespace ForesTycoon;

/// <summary>Exercises the real query-join path under allocation and child-exit signals on macOS.</summary>
internal static class BackgroundWaitStress
{
    internal static void Run(int seconds)
    {
        if (seconds < 1 || seconds > 600) throw new ArgumentOutOfRangeException(nameof(seconds));
        using var stop = new CancellationTokenSource();
        Exception churnFailure = null;
        Thread churn = null;
        if (OperatingSystem.IsMacOS()) {
            churn = new Thread(() => {
                try {
                    while (!stop.IsCancellationRequested) {
                        using var child = Process.Start(new ProcessStartInfo("/usr/bin/true") { UseShellExecute = false });
                        child.WaitForExit();
                    }
                } catch (Exception e) { churnFailure = e; }
            }) { IsBackground = true };
            churn.Start();
        }
        var watch = Stopwatch.StartNew(); int rounds = 0, completed = 0;
        var batch = new SlicedQueryBatch();
        Console.WriteLine($"wait stress: pid={Environment.ProcessId}, runtime={Environment.Version}, child churn={churn != null}");
        try {
            while (watch.Elapsed.TotalSeconds < seconds) {
                SimulationWork.Complete(batch.RunWork(2, _ => {
                    for (int i = 0; i < 10000; i++) GC.KeepAlive(new byte[1024]);
                    Interlocked.Increment(ref completed);
                }));
                rounds++;
                if (rounds % 100 == 0) Console.WriteLine($"round={rounds}, seconds={watch.Elapsed.TotalSeconds:F1}");
            }
        } finally { stop.Cancel(); churn?.Join(); }
        if (churnFailure != null) throw new InvalidOperationException("Child process churn failed", churnFailure);
        if (completed != rounds * 2 || batch.Pending) throw new InvalidOperationException("Query completion lost work");
        Console.WriteLine($"Background wait stress passed: {rounds} rounds, {completed} joined jobs.");
    }
}
