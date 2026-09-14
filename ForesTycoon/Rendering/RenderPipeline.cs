using System;
using System.Collections.Generic;

namespace ForesTycoon
{
    public sealed class RenderPipeline
    {
        private readonly List<RenderPass> passes = new List<RenderPass>();
        private bool sorted = true;
        internal static Action<string, bool> PassProbe;

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
                passes[i].Draw(context);
                PassProbe?.Invoke(passes[i].Name, false);
            }
        }

        private void EnsureSorted()
        {
            if (sorted) return;
            passes.Sort((a, b) => a.Layer.CompareTo(b.Layer));
            sorted = true;
        }
    }
}
