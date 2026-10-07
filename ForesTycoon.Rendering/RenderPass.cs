using System;

namespace ForesTycoon.Rendering
{
    public sealed class RenderPass
    {
        public RenderPass(RenderLayer layer, string name, Action<RenderContext> draw)
        {
            Layer = layer;
            Name = name ?? throw new ArgumentNullException(nameof(name));
            Draw = draw ?? throw new ArgumentNullException(nameof(draw));
        }

        public RenderLayer Layer { get; }
        public string Name { get; }
        public Action<RenderContext> Draw { get; }
    }
}
