using System;
namespace ForesTycoon
{
    internal sealed class SetWeatherCommand : IWorldCommand
    {
        private readonly WeatherPreset preset;
        private readonly int intensity,duration;
        internal SetWeatherCommand(WeatherPreset preset,int intensity,int duration)
        {
            if(!Enum.IsDefined(preset)||preset==WeatherPreset.Snow||intensity<0||intensity>100||duration<20||duration>600)
                throw new ArgumentOutOfRangeException(nameof(preset));
            this.preset=preset;this.intensity=intensity;this.duration=duration;
        }
        public void Execute(IWorldCommandTarget world)=>world.ExecuteWeather(preset,intensity,duration);
        public WorldCommandRecord ToRecord(ulong tick)=>new(tick,WorldCommandKind.SetWeather,(int)preset,intensity,duration,0,false);
    }
}
