using OpenTK.Mathematics;

namespace ForesTycoon.Tests;

public class WorldEffectSystemTests
{
    [Fact]
    public void Update_AdvancesAndExpiresTransientEffects()
    {
        WorldEffectSystem effects = new WorldEffectSystem();
        effects.Spawn(WorldEffectKind.RoadChanged, new Vector3(1, 2, 3), lifetimeSeconds: 0.5);

        effects.Update(0.2);
        Assert.Single(effects.Active);
        Assert.Equal(0.4f, effects.Active[0].Progress, 3);

        effects.Update(0.3);
        Assert.Empty(effects.Active);
    }

    [Fact]
    public void Update_RejectsNegativeSimulationTime()
    {
        WorldEffectSystem effects = new WorldEffectSystem();

        Assert.Throws<ArgumentOutOfRangeException>(() => effects.Update(-0.01));
    }
}
