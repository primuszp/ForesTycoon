using System;

namespace ForesTycoon
{
    internal readonly record struct WeatherForcing(double Radiation, double Temperature, double Humidity, double Wind)
    {
        // Gameplay approximation, mm/environment hour, shared by soil evaporation and tree demand.
        internal double PotentialEvaporationPerHour => (0.12 + Math.Max(0, Temperature) * 0.015)
            * Radiation * (1 - Humidity * 0.5) * (1 + Wind * 0.035);
    }

    internal readonly record struct WeatherInterval(double Seconds, double Rain, WeatherForcing Forcing);

    // Seeded event timeline and atmospheric forcing; independent of soil, forest and rendering.
    internal sealed class WeatherSystem
    {
        private uint random;
        private double previousCloud;
        internal double Time { get; private set; }
        internal double EventStart { get; private set; }
        internal double EventEnd { get; private set; }
        internal WeatherPreset Preset { get; private set; }
        internal double PeakRain { get; private set; }
        internal double EventRain { get; private set; }
        internal double TotalRain { get; private set; }
        internal double RainRate => RateAt(Time);
        private double Ramp => Math.Min(10, (EventEnd - EventStart) * 0.2);
        internal double ExpectedEventRain => PeakRain * (EventEnd - EventStart - Ramp) * EnvironmentSystem.HoursPerSecond;
        internal double Temperature => 12 + 9 * Math.Sin(2 * Math.PI * Time / EnvironmentSystem.SecondsPerForestYear);
        internal double Humidity => Preset is WeatherPreset.Rain or WeatherPreset.Storm ? 0.9 : 0.55;
        internal double Wind => 1.5 + (Preset == WeatherPreset.Storm ? 8.5 : Preset == WeatherPreset.Rain ? 1.5 : 0)
            * Math.Clamp(RainRate / Math.Max(1, PeakRain), 0, 1);
        internal double Radiation => 1 - Cloud * 0.75;
        internal double Cloud
        {
            get
            {
                double target = Preset == WeatherPreset.Sunny ? 0.12 : Preset == WeatherPreset.Cloudy ? 0.65 : 1;
                double t = Math.Clamp((Time - EventStart) / 10, 0, 1);
                t = t * t * (3 - 2 * t);
                return previousCloud + (target - previousCloud) * t;
            }
        }

        internal WeatherSystem(int seed)
        {
            random = unchecked((uint)seed) ^ 0x73A94F21u;
            if (random == 0) random = 1;
            StartEvent(WeatherPreset.Sunny, 150, 0);
        }

        private double Random()
        {
            random ^= random << 13; random ^= random >> 17; random ^= random << 5;
            return random / 4294967296.0;
        }

        private void StartEvent(WeatherPreset preset, double duration, double peak)
        {
            previousCloud = Cloud;
            Preset = preset; EventStart = Time; EventEnd = Time + duration; PeakRain = peak; EventRain = 0;
        }

        internal void ForceWeather(WeatherPreset preset, int peak = -1, int duration = -1)
        {
            if (preset == WeatherPreset.Snow || !Enum.IsDefined(preset)) throw new ArgumentOutOfRangeException(nameof(preset));
            if (peak < -1 || peak > 100) throw new ArgumentOutOfRangeException(nameof(peak));
            if (duration < -1 || (duration != -1 && duration < 20) || duration > 600)
                throw new ArgumentOutOfRangeException(nameof(duration));
            StartEvent(preset, duration < 0 ? (preset == WeatherPreset.Storm ? 45 : preset == WeatherPreset.Rain ? 90 : 180) : duration,
                preset is WeatherPreset.Rain or WeatherPreset.Storm ? (peak < 0 ? (preset == WeatherPreset.Storm ? 32 : 12) : peak) : 0);
        }

        private void NextEvent()
        {
            double r = Random();
            WeatherPreset next;
            if (Preset is WeatherPreset.Rain or WeatherPreset.Storm) next = r < 0.65 ? WeatherPreset.Cloudy : WeatherPreset.Sunny;
            else if (Preset == WeatherPreset.Cloudy) next = r < 0.15 ? WeatherPreset.Storm : r < 0.65 ? WeatherPreset.Rain : WeatherPreset.Sunny;
            else next = r < 0.75 ? WeatherPreset.Cloudy : WeatherPreset.Sunny;
            double duration = next switch
            {
                WeatherPreset.Sunny => 120 + Random() * 180,
                WeatherPreset.Cloudy => 60 + Random() * 120,
                WeatherPreset.Rain => 45 + Random() * 75,
                _ => 20 + Random() * 40
            };
            StartEvent(next, duration, next == WeatherPreset.Rain ? 6 + Random() * 12 : next == WeatherPreset.Storm ? 20 + Random() * 20 : 0);
        }

        private double RateAt(double time) => PeakRain * Math.Clamp(
            Math.Min((time - EventStart) / Ramp, (EventEnd - time) / Ramp), 0, 1);

        internal WeatherInterval AdvanceInterval(double seconds)
        {
            if (Time >= EventEnd - 1e-9) NextEvent();
            double dt = Math.Min(seconds, EventEnd - Time);
            // Exact precipitation integration by splitting at the linear-ramp corners.
            if (Time < EventStart + Ramp - 1e-9) dt = Math.Min(dt, EventStart + Ramp - Time);
            else if (Time < EventEnd - Ramp - 1e-9) dt = Math.Min(dt, EventEnd - Ramp - Time);
            var forcing = new WeatherForcing(Radiation, Temperature, Humidity, Wind);
            double rain = (RateAt(Time) + RateAt(Time + dt)) * 0.5 * dt * EnvironmentSystem.HoursPerSecond;
            Time += dt; TotalRain += rain; EventRain += rain;
            return new(dt, rain, forcing);
        }
    }
}
