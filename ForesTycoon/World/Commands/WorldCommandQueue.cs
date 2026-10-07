using System;
using System.Collections.Generic;

namespace ForesTycoon
{
    sealed class WorldCommandQueue
    {
        private readonly Queue<IWorldCommand> pending = new Queue<IWorldCommand>();
        private bool isExecuting;

        public int Count => pending.Count;

        public void Enqueue(IWorldCommand command) =>
            pending.Enqueue(command ?? throw new ArgumentNullException(nameof(command)));

        public int ExecutePending(IWorldCommandTarget world)
        {
            if (world == null) throw new ArgumentNullException(nameof(world));
            if (isExecuting) throw new InvalidOperationException("World commands cannot be executed recursively.");

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

        public void Clear() => pending.Clear();
    }
}
