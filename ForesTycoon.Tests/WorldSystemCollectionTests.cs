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

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    [InlineData(-0.1)]
    public void InvalidTimeDoesNotReachAnySystem(double seconds)
    {
        List<string> events = new();
        var systems = new WorldSystemCollection();
        systems.Add(new RecordingSystem("forest", events));
        Assert.Throws<ArgumentOutOfRangeException>(() => systems.Update(seconds));
        Assert.Empty(events);
        systems.Update(0.1);
        Assert.Equal(new[] { "forest" }, events);
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
