using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace ForesTycoon.Engine
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
        private readonly object gate = new object();
        private bool disposed;
        private bool cancellationCompleted;

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
            CancellationToken token;
            lock (gate)
            {
                ObjectDisposedException.ThrowIf(disposed, this);
                token = shutdown.Token;
                Interlocked.Increment(ref pendingCount);
            }
            _ = Task.Run(async () =>
            {
                try
                {
                    await concurrency.WaitAsync(token).ConfigureAwait(false);
                    try
                    {
                        token.ThrowIfCancellationRequested();
                        T result = work(token);
                        lock (gate)
                            if (!disposed) completed.Enqueue(() => publish(result));
                    }
                    finally
                    {
                        concurrency.Release();
                    }
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                }
                catch (Exception error)
                {
                    lock (gate)
                        if (!disposed) completed.Enqueue(() => throw new InvalidOperationException("Background job failed.", error));
                }
                finally
                {
                    lock (gate)
                        if (Interlocked.Decrement(ref pendingCount) == 0 && cancellationCompleted)
                            DisposeResources();
                }
            });
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
            lock (gate)
            {
                if (disposed) return;
                disposed = true;
                while (completed.TryDequeue(out _)) { }
            }
            try
            {
                shutdown.Cancel();
            }
            finally
            {
                lock (gate)
                {
                    cancellationCompleted = true;
                    // Workers still need the semaphore to release their slots.
                    if (pendingCount == 0) DisposeResources();
                }
            }
        }

        private void DisposeResources()
        {
            shutdown.Dispose();
            concurrency.Dispose();
        }
    }
}
