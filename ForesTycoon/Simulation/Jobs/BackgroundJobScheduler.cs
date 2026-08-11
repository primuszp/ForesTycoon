using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace ForesTycoon
{
    /// <summary>
    /// Executes CPU-only jobs away from the render thread and publishes results at a
    /// controlled frame boundary. Jobs must not access OpenGL or mutate live world state.
    /// </summary>
    sealed class BackgroundJobScheduler : IDisposable
    {
        private readonly SemaphoreSlim concurrency;
        private readonly CancellationTokenSource shutdown = new CancellationTokenSource();
        private readonly ConcurrentQueue<Action> completed = new ConcurrentQueue<Action>();
        private int pendingCount;
        private bool disposed;

        public BackgroundJobScheduler(int maximumConcurrency = 0)
        {
            int workers = maximumConcurrency > 0
                ? maximumConcurrency
                : Math.Max(1, Environment.ProcessorCount - 1);
            concurrency = new SemaphoreSlim(workers, workers);
        }

        public int PendingCount => Volatile.Read(ref pendingCount);

        public void Schedule<T>(Func<CancellationToken, T> work, Action<T> publish)
        {
            if (work == null) throw new ArgumentNullException(nameof(work));
            if (publish == null) throw new ArgumentNullException(nameof(publish));
            ObjectDisposedException.ThrowIf(disposed, this);

            Interlocked.Increment(ref pendingCount);
            _ = Task.Run(async () =>
            {
                try
                {
                    await concurrency.WaitAsync(shutdown.Token).ConfigureAwait(false);
                    try
                    {
                        T result = work(shutdown.Token);
                        if (!shutdown.IsCancellationRequested)
                            completed.Enqueue(() => publish(result));
                    }
                    finally
                    {
                        concurrency.Release();
                    }
                }
                catch (OperationCanceledException) when (shutdown.IsCancellationRequested)
                {
                }
                finally
                {
                    Interlocked.Decrement(ref pendingCount);
                }
            }, shutdown.Token);
        }

        public int PublishCompleted(int maximumResults = 8)
        {
            if (maximumResults <= 0) throw new ArgumentOutOfRangeException(nameof(maximumResults));
            int published = 0;
            while (published < maximumResults && completed.TryDequeue(out Action action))
            {
                action();
                published++;
            }
            return published;
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            shutdown.Cancel();
            while (completed.TryDequeue(out _)) { }
            shutdown.Dispose();
            concurrency.Dispose();
        }
    }
}
