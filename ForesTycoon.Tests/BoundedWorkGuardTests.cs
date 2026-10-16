namespace ForesTycoon.Tests;

[Collection("SpeciesBook")]
public class BoundedWorkGuardTests
{
    private static IEnumerable<int> Unit(Action action) { action(); yield return 1; }

    [Fact]
    public void NestedSynchronousDrainIsRejectedBeforeItExecutesAnyWork()
    {
        bool ran = false;
        using var outer = Unit(() => SimulationWork.Complete(Unit(() => ran = true))).GetEnumerator();
        Assert.Throws<InvalidOperationException>(() => SimulationWork.MoveNext(outer));
        Assert.False(ran);
        SimulationWork.Complete(Unit(() => ran = true)); // The thread guard is restored after an exception.
        Assert.True(ran);
    }

    [Fact]
    public void NestedResumableWorkIsAllowedAndPreservesSuspensionPoints()
    {
        int completed = 0;
        IEnumerable<int> Parent() {
            foreach (int unit in Unit(() => completed++)) yield return unit;
            foreach (int unit in Unit(() => completed++)) yield return unit;
        }
        using var work = Parent().GetEnumerator();
        Assert.True(SimulationWork.MoveNext(work)); Assert.Equal(1, completed);
        Assert.True(SimulationWork.MoveNext(work)); Assert.Equal(2, completed);
        Assert.False(SimulationWork.MoveNext(work));
    }

    [Fact]
    public void ACommandCannotBypassItsBudgetByDrainingNestedWork()
    {
        var queue = new WorldCommandQueue();
        using var world = new GameWorld(TerrainSettings.Default.WithNodeSize(9, 42), enableRendering: false);
        queue.Enqueue(new SetWeatherCommand(WeatherPreset.Rain, 1, 20));
        Assert.Throws<InvalidOperationException>(() => queue.Continue(world,
            _ => Unit(() => SimulationWork.Complete(Unit(() => { }))), 1));
    }

}
