using System;
using System.Collections.Generic;

namespace ForesTycoon.Rendering
{
    public sealed class RenderPipeline
    {
        private readonly List<RenderPass> passes = new List<RenderPass>();
        private bool sorted = true;
        private sealed class ProbeState { internal Action<string, bool> Callback; }
        private static readonly object probeKey = new();
        internal static Action<string, bool> PassProbe {
            get => RenderDevice.GetState(probeKey, () => new ProbeState()).Callback;
            set => RenderDevice.GetState(probeKey, () => new ProbeState()).Callback = value;
        }

        public void Add(RenderLayer layer, string name, Action<RenderContext> draw)
        {
            for (int i = 0; i < passes.Count; i++)
            {
                if (passes[i].Name == name)
                    throw new InvalidOperationException($"A render pass with the name '{name}' is already registered.");
            }

            passes.Add(new RenderPass(layer, name, draw));
            sorted = false;
        }

        public void Render(RenderContext context)
        {
            EnsureSorted();
            for (int i = 0; i < passes.Count; i++)
            {
                PassProbe?.Invoke(passes[i].Name, true);
                try
                {
                    passes[i].Draw(context);
                }
                finally
                {
                    PassProbe?.Invoke(passes[i].Name, false);
                }
            }
        }

        private void EnsureSorted()
        {
            if (sorted) return;
            // Stable insertion sort runs only after registration changes. Equal-layer
            // passes retain registration order, including after another pass is added.
            for (int i = 1; i < passes.Count; i++)
            {
                RenderPass pass = passes[i];
                int previous = i - 1;
                while (previous >= 0 && passes[previous].Layer > pass.Layer)
                {
                    passes[previous + 1] = passes[previous];
                    previous--;
                }
                passes[previous + 1] = pass;
            }
            sorted = true;
        }
    }
}
