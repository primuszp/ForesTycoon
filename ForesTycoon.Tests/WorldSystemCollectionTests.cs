namespace ForesTycoon.Tests;

public class WorldSystemCollectionTests
{
    [Fact]
    public void Update_UsesStableRegistrationOrder()
    {
        List<string> events = new();
        WorldSystemCollection systems = new WorldSystemCollection();
        systems.Add(new RecordingSystem("vehicles", events));
        systems.Add(new RecordingSystem("forestry", events));

        systems.Update(0.25);

        Assert.Equal(new[] { "vehicles", "forestry" }, events);
    }

    [Fact]
    public void Clear_UsesReverseRegistrationOrder()
    {
        List<string> events = new();
        WorldSystemCollection systems = new WorldSystemCollection();
        systems.Add(new RecordingSystem("first", events));
        systems.Add(new RecordingSystem("second", events));

        systems.Clear();

        Assert.Equal(new[] { "second:clear", "first:clear" }, events);
    }

    private sealed class RecordingSystem : IWorldSystem
    {
        private readonly string name;
        private readonly List<string> events;

        public RecordingSystem(string name, List<string> events)
        {
            this.name = name;
            this.events = events;
        }

        public void Update(double fixedDeltaSeconds) => events.Add(name);
        public void Clear() => events.Add($"{name}:clear");
    }
}
