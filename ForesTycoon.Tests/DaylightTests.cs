using ForesTycoon.Rendering;

namespace ForesTycoon.Tests;

public class DaylightTests
{
    // Mid-season visible day at the given hour of the day (0 midnight, 0.5 noon).
    private static DaylightState At(int season, float hour) =>
        Daylight.At((System.Math.Floor((season * 0.25 + 0.125) * Daylight.DaysPerYear) + hour - Daylight.StartHour) / Daylight.DaysPerYear);

    [Fact]
    public void ANewGameOpensInTheMorning() => Assert.Equal(0, Daylight.At(0).Night, 3);

    [Fact]
    public void NoonIsHighAndBrightDawnLowAndWarmNightBlue()
    {
        var noon = At(1, 0.5f); var dusk = At(1, 0.78f); var night = At(1, 0.02f);
        Assert.Equal(0, noon.Night, 3);
        Assert.True(noon.Elevation > 55 && noon.Elevation > dusk.Elevation);
        Assert.True(dusk.SunTint.X > dusk.SunTint.Z * 1.6f, "Dusk light is not warm.");
        Assert.True(night.Night > 0.9f);
        Assert.True(night.SkyTint.Z > night.SkyTint.X && night.SkyTint.Length < noon.SkyTint.Length * 0.6f);
        Assert.True(night.Elevation >= 8, "The light must stay above the horizon.");
    }

    [Fact]
    public void SunPathAndDayLengthFollowTheSeason()
    {
        Assert.True(At(1, 0.5f).Elevation > At(3, 0.5f).Elevation + 30, "Midsummer noon is not higher than midwinter noon.");
        Assert.True(At(1, 0.25f).Night < 0.01f, "A summer morning should be light.");
        Assert.True(At(3, 0.22f).Night > 0.5f, "A winter morning at the same hour should still be dark.");
        // East in the morning, west in the evening.
        Assert.InRange(At(1, 0.3f).Azimuth, 300, 360); Assert.InRange(At(1, 0.5f).Azimuth, 265, 275); Assert.InRange(At(1, 0.7f).Azimuth, 180, 240);
    }

    [Fact]
    public void GroundTurnsStrawInAutumnAndFreshInSpring()
    {
        var spring = At(0, 0.5f).GroundTint; var autumn = At(2, 0.5f).GroundTint;
        Assert.True(spring.Y > spring.X, "Spring ground is not fresh green.");
        Assert.True(autumn.X > autumn.Y && autumn.Z < 0.5f, "Autumn ground is not straw coloured.");
    }
}
