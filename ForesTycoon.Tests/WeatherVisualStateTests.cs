namespace ForesTycoon.Tests;

public class WeatherVisualStateTests
{
    [Fact]
    public void RainWetsSurface_AndSunnyWeatherDriesItGradually()
    {
        var state = new WeatherVisualState();
        var settings = new GraphicsSettings { Preset = WeatherPreset.Rain };
        for(int i = 0; i <= 20; i++) state.Update(i, settings);
        Assert.True(state.Rain > 0.99f);
        Assert.True(state.Wetness > 0.9f);
        settings.Preset = WeatherPreset.Sunny;
        state.Update(21, settings);
        Assert.True(state.Wetness > 0.5f);
        for(int i = 22; i <= 100; i++) state.Update(i, settings);
        Assert.True(state.Wetness < 0.01f);
    }

    [Fact]
    public void SnowAccumulationAndThaw_AreIndependentFromFallingParticles()
    {
        var state = new WeatherVisualState();
        var settings = new GraphicsSettings { Preset = WeatherPreset.Snow, ExperimentalSnow = true };
        for(int i = 0; i <= 30; i++) state.Update(i, settings);
        Assert.True(state.SnowCover > 0.95f);
        settings.Preset = WeatherPreset.Sunny;
        for(int i = 31; i <= 40; i++) state.Update(i, settings);
        Assert.True(state.Snowfall < 0.01f);
        Assert.True(state.SnowCover > 0.7f);
        Assert.True(state.Wetness > 0);
        for(int i = 41; i <= 140; i++) state.Update(i, settings);
        Assert.Equal(0, state.SnowCover);
    }

    [Fact]
    public void PausingFreezesWeather_AndTimeRewindResetsVisualAccumulation()
    {
        var state = new WeatherVisualState();
        var settings = new GraphicsSettings { Preset = WeatherPreset.Snow, ExperimentalSnow = true };
        for(int i = 0; i <= 15; i++) state.Update(i, settings);
        float cover = state.SnowCover, snowfall = state.Snowfall;
        for(int i = 0; i < 100; i++) state.Update(15, settings);
        Assert.Equal(cover, state.SnowCover); Assert.Equal(snowfall, state.Snowfall);
        state.Update(0, settings);
        Assert.Equal(0, state.SnowCover);
    }

    [Fact]
    public void DisplayTogglesDoNotChangeWeatherTimeline()
    {
        var first = new WeatherVisualState(); var second = new WeatherVisualState();
        var a = new GraphicsSettings { AutomaticWeather = true };
        var b = new GraphicsSettings { AutomaticWeather = true, Enhanced = false, Textures = false };
        for(int i = 0; i <= 300; i++) { first.Update(i, a); second.Update(i, b); }
        Assert.Equal(first.SnowCover, second.SnowCover);
        Assert.Equal(first.Wetness, second.Wetness);
        Assert.Equal(first.Cloud, second.Cloud);
    }

    [Fact]
    public void NormalGameNeverAccumulatesSnow_EvenWithAStaleSnowPreset()
    {
        var settings = new GraphicsSettings { Preset = WeatherPreset.Snow };
        var state = new WeatherVisualState();
        for(int i = 0; i <= 300; i++)
        {
            state.Update(i, settings);
            Assert.Equal(0, state.Snowfall); Assert.Equal(0, state.SnowCover);
            Assert.NotEqual(WeatherPreset.Snow, WeatherVisualState.AutomaticPreset(i));
        }
    }

    [Fact]
    public void StormHasWindAndLimitedDeterministicFlashes_AndPauseFreezesThem()
    {
        var state = new WeatherVisualState();
        var twin = new WeatherVisualState();
        var settings = new GraphicsSettings { Preset = WeatherPreset.Storm };
        float peak=0;
        for(int i=0;i<4000;i++)
        {
            double t=i*0.01;
            state.Update(t,settings); twin.Update(t,settings);
            Assert.Equal(twin.Flash,state.Flash);
            Assert.InRange(state.Flash,0,0.35f);
            Assert.Equal(0,state.SnowCover);
            peak=Math.Max(peak,state.Flash);
        }
        Assert.True(peak>0.1f);
        Assert.True(state.Wind.Length>3);
        var wind=state.Wind; var flash=state.Flash;
        state.Update(39.99,settings);
        Assert.Equal(wind,state.Wind); Assert.Equal(flash,state.Flash);
        settings.Lightning=false; state.Update(39.99,settings);
        Assert.Equal(0,state.Flash);
        settings.Weather=false; state.Update(40,settings);
        Assert.Equal(OpenTK.Mathematics.Vector2.Zero,state.Wind);
    }

    [Fact]
    public void RequestedLightningAppearsImmediatelyInSunnyWeather_EvenWhenPaused()
    {
        var state = new WeatherVisualState();
        var settings = new GraphicsSettings { Preset = WeatherPreset.Sunny };
        state.Update(10,settings);
        Assert.Equal(0,state.Flash);
        settings.LightningRequest++;
        state.Update(10,settings);
        Assert.True(state.Flash>0.2f);
        long id=state.LightningEvent;
        state.Update(10,settings);
        Assert.Equal(id,state.LightningEvent);
        Assert.True(state.Flash>0.2f);
        state.Update(11,settings);
        Assert.Equal(0,state.Flash);
        settings.LightningRequest++;
        state.Update(11,settings);
        Assert.NotEqual(id,state.LightningEvent);
        Assert.True(state.Flash>0.2f);
        settings.Lightning=false;
        state.Update(11,settings);
        Assert.Equal(0,state.Flash);
    }
}

