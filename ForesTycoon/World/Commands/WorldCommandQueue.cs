using System;
using System.Collections.Generic;

namespace ForesTycoon
{
    sealed class WorldCommandQueue
    {
        private readonly Queue<IWorldCommand> pending = new Queue<IWorldCommand>();
        private bool isExecuting;

        public int Count => pending.Count;
        internal IWorldCommand[] Snapshot() => pending.ToArray();

        public void Enqueue(IWorldCommand command) =>
            pending.Enqueue(command ?? throw new ArgumentNullException(nameof(command)));

        public int ExecutePending(IWorldCommandTarget world)
        {
            if (world == null) throw new ArgumentNullException(nameof(world));
            if (isExecuting) throw new InvalidOperationException("World commands cannot be executed recursively.");

            if (WorkPending) throw new InvalidOperationException("Finish the active sliced command batch before synchronous execution.");
            int count = pending.Count;
            isExecuting = true;
            try
            {
                // Snapshot the count: commands produced by commands belong to the next batch.
                for (int i = 0; i < count; i++)
                    pending.Dequeue().Execute(world);
            }
            finally
            {
                isExecuting = false;
            }
            return count;
        }

        private IEnumerator<int> active;
        private int batchRemaining;
        internal bool WorkPending => active != null || batchRemaining > 0;
        internal int Continue(IWorldCommandTarget world, Func<IWorldCommand, IEnumerable<int>> execute, int budget)
        {
            ArgumentNullException.ThrowIfNull(world); ArgumentNullException.ThrowIfNull(execute);
            if (budget <= 0) throw new ArgumentOutOfRangeException(nameof(budget));
            if (isExecuting) throw new InvalidOperationException("World commands cannot be executed recursively.");
            if (!WorkPending) batchRemaining = pending.Count;
            int completed = 0, units = 0;
            isExecuting = true;
            try {
                while (batchRemaining > 0 && units < budget) {
                    active ??= execute(pending.Peek()).GetEnumerator();
                    if (active.MoveNext()) { units += Math.Max(1, active.Current); continue; }
                    active.Dispose(); active = null;
                    pending.Dequeue(); batchRemaining--; completed++; units++;
                }
            } finally { isExecuting = false; }
            return completed;
        }

        public void Clear() { active?.Dispose(); active = null; batchRemaining = 0; pending.Clear(); }
    }
}
