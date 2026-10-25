namespace ForesTycoon.Tests;

public sealed class GroundMaterialTests
{
    [Fact]
    public void ABiomesGroundTurnsWetOnMoistTilesAndRockyOnSlopes()
    {
        var scene = new WorldScenario();
        int mountain = scene.Biomes.FindIndex(b => b.Id == "beech-spruce");
        int Layer(string pattern) => Array.IndexOf(GroundMaterialDefinition.Patterns, pattern);

        var dry = scene.Ground(mountain, moisture: .2, slope: 0);
        Assert.Equal(Layer("grass"), dry.Pattern); Assert.Equal(0, dry.Steep);
        Assert.Equal(0x88 / 255f, dry.R, 3);                        // alpine grass

        var wet = scene.Ground(mountain, moisture: .95, slope: 0);
        Assert.Equal(Layer("moss"), wet.Pattern);
        Assert.Equal(0x4E / 255f, wet.R, 3);                        // moss

        var gentle = scene.Ground(mountain, .2, 1); var steep = scene.Ground(mountain, .2, 2);
        Assert.Equal(.35f, gentle.Steep); Assert.Equal(1, steep.Steep);
        Assert.Equal(Layer("rock"), steep.SteepPattern);
        Assert.Equal(0x87 / 255f, steep.R, 3);                      // rock

        // No biome: plain meadow.
        Assert.Equal(Layer("grass"), scene.Ground(-1, .2, 0).Pattern);
    }

    [Fact]
    public void BiomesMayOnlyUseTheScenesMaterialsAndMaterialsNeedAKnownPattern()
    {
        var scenario = GameWorld.DefaultScenario();
        scenario.Validate();
        scenario.Biomes[0].WetGround = "lava";
        Assert.Throws<InvalidDataException>(scenario.Validate);
        scenario.Biomes = BiomeDefinition.Defaults();
        scenario.GroundMaterials[0].Pattern = "plaid";
        Assert.Throws<InvalidDataException>(scenario.Validate);
    }
}
