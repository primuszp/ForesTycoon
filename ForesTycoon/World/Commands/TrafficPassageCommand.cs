using System.Text.Json;

namespace ForesTycoon;

/// <summary>An editor test passage is journaled just like every other world edit.</summary>
internal sealed class TrafficPassageCommand : IWorldCommand
{
    private readonly TrafficPassage passage;
    internal TrafficPassageCommand(TrafficPassage passage)
    {
        System.ArgumentNullException.ThrowIfNull(passage); passage.Validate(); this.passage = passage;
    }
    public void Execute(IWorldCommandTarget world) => world.ExecuteTrafficPassage(JsonSerializer.Serialize(passage));
    public WorldCommandRecord ToRecord(ulong tick) => new(tick, WorldCommandKind.TrafficPassage, 0, 0, 0, 0, false, JsonSerializer.Serialize(passage));
}
