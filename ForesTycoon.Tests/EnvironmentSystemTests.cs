namespace ForesTycoon.Tests;

public class EnvironmentSystemTests
{
    [Fact]
    public void RetunedCalendarKeepsSeasonalWeatherAndAnnualRain()
    {
        var original = new WeatherSystem(821, 1200);
        var faster = new WeatherSystem(821, 120);
        for (int interval = 0; interval < 2400; interval++)
        {
            Advance(original, .5); Advance(faster, .05);
            Assert.Equal(original.Temperature, faster.Temperature, 7);
            Assert.Equal(original.Cloud, faster.Cloud, 7);
            Assert.Equal(original.TotalRain, faster.TotalRain, 7);
        }
        static void Advance(WeatherSystem weather, double seconds)
        {
            while (seconds > 1e-9) seconds -= weather.AdvanceInterval(seconds).Seconds;
        }
    }

    [Fact]
    public void RetunedForestEnvironmentSharesYearAndConservesWater()
    {
        var habitat = new Habitat(2);
        var forest = new ForestSystem(habitat, 120);
        var environment = new EnvironmentSystem(habitat, forest, 120);
        var coupled = new ForestEnvironmentCoordinator(forest, environment);
        for (int tick = 0; tick < 30 * 120; tick++) coupled.Update(1.0 / 30);
        Assert.Equal(1, forest.ForestYear, 7);
        Assert.Equal(1, environment.Time / environment.ForestYearSeconds, 7);
        Assert.InRange(Math.Abs(environment.BalanceError), 0, 1e-6);
    }

    private sealed class Habitat(int count=2,float moisture=0.5f,bool slope=false) : IForestHabitat
    {
        public int TileCount=>count;
        public int Seed=>821;
        public bool CanSupportForest(int id)=>true;
        public float GetMoisture(int id)=>moisture;
        public float GetNormalizedElevation(int id)=>slope?(count-id)/(float)count:0.5f;
        public int GetAdjacentTileIds(int id,Span<int> destination){int n=0;if(id>0)destination[n++]=id-1;if(id<count-1)destination[n++]=id+1;return n;}
    }
    [Fact] public void EventIntensityIntegratesRampExactly()
    {
        var environment=new EnvironmentSystem(new Habitat(),null);
        environment.ForceWeather(WeatherPreset.Rain,12,90);environment.Update(90);
        Assert.Equal(16,environment.TotalRain,8);Assert.Equal(16,environment.EventRain,8);
        Assert.Equal(0,environment.RainRate,8);Assert.True(Math.Abs(environment.BalanceError)<1e-7);
    }
    [Fact] public void RainIntensityControlsWaterQuantity()
    {
        var light=new EnvironmentSystem(new Habitat(),null);var heavy=new EnvironmentSystem(new Habitat(),null);
        light.ForceWeather(WeatherPreset.Rain,6,90);heavy.ForceWeather(WeatherPreset.Rain,24,90);
        light.Update(90);heavy.Update(90);Assert.Equal(light.TotalRain*4,heavy.TotalRain,8);
        Assert.True(heavy.Cell(0).Soil>light.Cell(0).Soil);
    }
    [Fact] public void EventsRemainStableAndTransitionSmoothly()
    {
        var environment=new EnvironmentSystem(new Habitat(),null);environment.Update(149);
        Assert.Equal(WeatherPreset.Sunny,environment.Preset);
        environment.Update(1);double before=environment.Cloud;environment.Update(0.5);
        Assert.InRange(Math.Abs(environment.Cloud-before),0,0.01);
        double end=environment.EventEnd;environment.Update(10);Assert.Equal(end,environment.EventEnd);
    }
    [Fact] public void LargeUpdateMatchesSmallUpdatesAcrossEvents()
    {
        var first=new EnvironmentSystem(new Habitat(10),null);var second=new EnvironmentSystem(new Habitat(10),null);
        first.Update(1200);for(int i=0;i<36000;i++)second.Update(1.0/30);
        Assert.Equal(first.Time,second.Time);Assert.Equal(first.Preset,second.Preset);
        Assert.Equal(first.EventEnd,second.EventEnd);Assert.Equal(first.TotalRain,second.TotalRain);
        for(int id=0;id<10;id++)Assert.Equal(first.Cell(id),second.Cell(id));
        Assert.True(Math.Abs(first.BalanceError)<1e-6);
    }
    [Fact] public void SaturatedCellsRemainBoundedAndRunoffMovesDownhill()
    {
        var environment=new EnvironmentSystem(new Habitat(2,1,true),null);
        environment.ForceWeather(WeatherPreset.Storm,100,300);environment.Update(300);
        Assert.InRange(environment.Cell(0).Soil,0,180);Assert.InRange(environment.Cell(1).Soil,0,180);
        Assert.True(environment.Cell(1).Surface>environment.Cell(0).Surface);
        Assert.True(Math.Abs(environment.BalanceError)<1e-7);
    }
    [Fact] public void DryPeriodDepletesWaterWithoutInventingRain()
    {
        var environment=new EnvironmentSystem(new Habitat(),null);double initial=environment.StoredWater;
        environment.ForceWeather(WeatherPreset.Sunny,0,600);environment.Update(600);
        Assert.Equal(0,environment.TotalRain);Assert.True(environment.StoredWater<initial);
        Assert.True(environment.Evaporated>0);Assert.True(Math.Abs(environment.BalanceError)<1e-7);
    }
    [Fact] public void DroughtReducesActualForestGrowthAndHealth()
    {
        var habitat=new Habitat(1,0.55f);
        ForestStand[] stands=[new ForestStand(ForestSpecies.Oak,20,0.5f,0.3f)];
        var stressed=new ForestSystem(habitat,stands);stressed.UseEnvironmentTempo();
        var control=new ForestSystem(habitat,stands);control.UseEnvironmentTempo();
        var environment=new EnvironmentSystem(habitat,stressed);var coupled=new ForestEnvironmentCoordinator(stressed,environment);
        stressed.TryGetStand(0, out var before);
        for (int i = 0; i < 40; i++) {
            environment.ForceWeather(WeatherPreset.Sunny,0,600);
            coupled.Update(600);
            control.Update(600);
        }
        bool survived = stressed.TryGetStand(0,out var dry);
        Assert.True(control.TryGetStand(0,out var normal));
        Assert.True(stressed.Statistics.TotalBiomass < normal.Biomass, $"dry={stressed.Statistics.TotalBiomass} wet={normal.Biomass} health={dry.Health}/{normal.Health} count={stressed.IndividualTreeCount}/{control.IndividualTreeCount} water={environment.GrowthFactor(0, ForestSpecies.Oak)}");
        Assert.True(!survived || dry.Health < normal.Health);
        Assert.True(environment.GrowthFactor(0, ForestSpecies.Oak) < 1);
        if (survived) Assert.Equal(before.AgeYears + 20, dry.AgeYears, 3);
        else Assert.True(stressed.IndividualTreeCount < control.IndividualTreeCount);
    }
    [Fact] public void PauseAndGraphicsOptionsDoNotModifyWater()
    {
        var environment=new EnvironmentSystem(new Habitat(),null);environment.ForceWeather(WeatherPreset.Storm);
        environment.Update(25);var before=environment.Cell(0);double rain=environment.TotalRain;
        var visual=new WeatherVisualState();var settings=new GraphicsSettings{AutomaticWeather=true};
        visual.Update(environment,settings);settings.Weather=false;settings.Enhanced=false;settings.Quality=GraphicsQuality.Low;
        visual.Update(environment,settings);environment.Update(0);
        Assert.Equal(before,environment.Cell(0));Assert.Equal(rain,environment.TotalRain);
    }
    [Fact] public void WeatherCommandRoundTrip()
    {
        var command=new SetWeatherCommand(WeatherPreset.Storm,32,60);
        var save=new WorldSaveData{ Climate = ClimateDefinition.Legacy,SoilModel=SoilModelData.From(SoilLandscapeDefinition.Default),Commands=[command.ToRecord(17)]};
        using var stream=new MemoryStream();WorldSaveSerializer.Write(stream,save);stream.Position=0;
        var loaded=WorldSaveSerializer.Read(stream);Assert.Equal(WorldSaveData.CurrentVersion,loaded.Version);
        Assert.Equal(command.ToRecord(17),WorldCommandFactory.Create(loaded.Commands[0]).ToRecord(17));
    }
    [Theory] [InlineData(-1)] [InlineData(double.NaN)] [InlineData(double.PositiveInfinity)]
    public void InvalidTimeIsRejected(double seconds)=>Assert.Throws<ArgumentOutOfRangeException>(()=>new EnvironmentSystem(new Habitat(),null).Update(seconds));
}
