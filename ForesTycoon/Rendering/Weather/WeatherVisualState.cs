using System;

namespace ForesTycoon
{
    // A visual weather timeline, deliberately independent from the 30-second forestry year.
    // Driven by simulation time so pause and quickload have predictable behaviour.
    internal sealed class WeatherVisualState
    {
        internal float Cloud { get; private set; }
        internal float Storm { get; private set; }
        internal float Flash { get; private set; }
        internal OpenTK.Mathematics.Vector2 Wind { get; private set; }
        internal float Rain { get; private set; }
        internal float Snowfall { get; private set; }
        internal float Wetness { get; private set; }
        internal float SnowCover { get; private set; }
        private int seenLightningRequest;
        private double requestedLightningTime = double.NegativeInfinity;
        private bool manualLightning;
        internal long LightningEvent => manualLightning ? -1L-seenLightningRequest : (long)(Time / 14);
        internal static double LightningOnset(long slot)
        {
            double hash = Math.Sin(slot * 127.1 + 311.7) * 43758.5453;
            return slot * 14 + 3 + 7 * (hash - Math.Floor(hash));
        }
        internal double Time { get; private set; }
        private double previousTime;
        private bool started;

        internal void Update(EnvironmentSystem environment,GraphicsSettings settings,double? visualTime=null)
        {
            // Existing lightning/particle clocks remain driven by simulation time.
            Update(visualTime??environment.Time,settings);
            Cloud=(float)environment.Cloud;
            Rain=(float)Math.Clamp(environment.RainRate/25,0,1);
            Storm=environment.Preset==WeatherPreset.Storm?(float)Math.Clamp(environment.RainRate/25,0,1):0;
            Wind=new OpenTK.Mathematics.Vector2(1,0.35f)*(float)(environment.WindSpeed*0.45);
            Wetness=(float)environment.MeanWetness;Snowfall=SnowCover=0;
            double age=manualLightning?Time-requestedLightningTime:Time-LightningOnset((long)Math.Floor(Time/14));
            Flash=settings.Weather&&settings.Lightning&&age>=0&&age<0.65
                ? (manualLightning?1:Storm)*0.22f*(MathF.Exp(-(float)age*12)+(age>=0.15?0.35f*MathF.Exp(-(float)(age-0.15)*18):0)):0;
        }

        internal void Update(double simulationTime, GraphicsSettings settings)
        {
            if (!double.IsFinite(simulationTime) || simulationTime < 0) throw new ArgumentOutOfRangeException(nameof(simulationTime));
            double elapsed = started ? Math.Clamp(simulationTime - previousTime, 0, 1) : 0;
            if (started && simulationTime < previousTime) Reset();
            started = true;
            previousTime = Time = simulationTime;
            WeatherPreset preset = settings.AutomaticWeather ? AutomaticPreset(simulationTime) : settings.Preset;
            if (preset == WeatherPreset.Snow && !settings.ExperimentalSnow) preset = WeatherPreset.Cloudy;
            float cloud = settings.Weather && preset != WeatherPreset.Sunny ? 1 : 0;
            float rain = settings.Weather && (preset == WeatherPreset.Rain || preset == WeatherPreset.Storm) ? 1 : 0;
            float snow = settings.Weather && preset == WeatherPreset.Snow ? 1 : 0;
            float blend = 1 - MathF.Exp(-(float)elapsed * 0.7f);
            Cloud += (cloud - Cloud) * blend;
            Rain += (rain - Rain) * blend;
            Snowfall += (snow - Snowfall) * blend;
            Storm += ((settings.Weather && preset == WeatherPreset.Storm ? 1 : 0) - Storm) * blend;
            float gust = 0.8f + 0.2f * MathF.Sin((float)(simulationTime * 0.43));
            Wind = settings.Weather ? new OpenTK.Mathematics.Vector2(1, 0.35f) * (Rain * 1.4f + Storm * 4) * gust : OpenTK.Mathematics.Vector2.Zero;
            double slot = Math.Floor(simulationTime / 14);
            if(settings.LightningRequest != seenLightningRequest)
            {
                seenLightningRequest = settings.LightningRequest;
                requestedLightningTime = simulationTime;
            }
            double requestedAge = simulationTime-requestedLightningTime;
            manualLightning = requestedAge >= 0 && requestedAge < 0.65;
            double age = manualLightning ? requestedAge : simulationTime - LightningOnset((long)slot);
            Flash = settings.Weather && settings.Lightning && age >= 0 && age < 0.65
                ? (manualLightning ? 1 : Storm) * 0.22f * (MathF.Exp(-(float)age * 12) + (age >= 0.15 ? 0.35f * MathF.Exp(-(float)(age - 0.15) * 18) : 0)) : 0;
            Wetness = Math.Clamp(Wetness + (float)elapsed * (Rain * 0.14f + (snow == 0 ? SnowCover * 0.018f : 0) - (1 - Rain) * 0.025f * (1 - SnowCover * 0.8f)), 0, 1);
            SnowCover = Math.Clamp(SnowCover + (float)elapsed * (Snowfall * 0.055f - (snow == 0 ? 0.014f : 0)), 0, 1);
            if (!settings.ExperimentalSnow) Snowfall = SnowCover = 0;
        }

        internal static WeatherPreset AutomaticPreset(double time) => ((int)(time / 45) % 6) switch
        {
            0 or 5 => WeatherPreset.Sunny,
            1 or 3 => WeatherPreset.Cloudy,
            2 => WeatherPreset.Rain,
            _ => WeatherPreset.Storm
        };

        internal void Reset()
        {
            Cloud = Rain = Snowfall = Wetness = SnowCover = Storm = Flash = 0;
            Wind = OpenTK.Mathematics.Vector2.Zero;
            requestedLightningTime = double.NegativeInfinity; manualLightning = false;
            Time = previousTime = 0;
            started = false;
        }
    }
}
