using System;
using ForesTycoon.Rules;

namespace ForesTycoon.Ecology
{
    internal readonly record struct WeatherForcing(double Radiation, double Temperature, double Humidity, double Wind)
    {
        internal double Evaporation(EcologyResponseProgram rules) => rules.Environment(EnvironmentResponse.Evaporation,
            [Radiation, Temperature, Humidity, Wind]);
    }

    internal readonly record struct WeatherInterval(double Seconds, double Rain, WeatherForcing Forcing, bool Snow = false);

    // Seeded event timeline and atmospheric forcing; independent of soil, forest and rendering.
    internal sealed partial class WeatherSystem
    {
        private uint random;
        private readonly double timeScale;
        internal double ForestYearSeconds { get; }
        internal double HoursPerSecond => EnvironmentSystem.HoursPerSecond / timeScale;
        private double previousCloud;
        internal double Time { get; private set; }
        internal double EventStart { get; private set; }
        internal double EventEnd { get; private set; }
        internal WeatherPreset Preset { get; private set; }
        internal double PeakRain { get; private set; }
        internal double EventRain { get; private set; }
        internal double TotalRain { get; private set; }
        internal double RainRate => RateAt(Time);
        internal EcologyResponseProgram ResponseRules { get; set; }
        private EcologyResponseProgram Responses => ResponseRules ?? EcologyResponseProgram.Default;
        private double Ramp => Math.Clamp(Responses.Environment(EnvironmentResponse.Ramp,
            [EventEnd - EventStart, timeScale]), 0.000000001, Math.Max(0.000000001, (EventEnd - EventStart) / 2));
        internal double ExpectedEventRain => PeakRain * (EventEnd - EventStart - Ramp) * HoursPerSecond;
        internal int Season => (int)Math.Floor(Time / ForestYearSeconds * 4 + 1e-9) % 4;
        internal double Temperature => Responses.Environment(EnvironmentResponse.Temperature,
            [Math.Sin(2 * Math.PI * (Time / ForestYearSeconds - .125)), (int)Preset,
                Time / ForestYearSeconds - Math.Floor(Time / ForestYearSeconds), Season]);
        internal double Humidity => Responses.Environment(EnvironmentResponse.Humidity, [(int)Preset]);
        internal double Wind => Responses.Environment(EnvironmentResponse.Wind, [(int)Preset, RainRate, PeakRain]);
        internal double Radiation => Responses.Environment(EnvironmentResponse.Radiation, [Cloud]);
        internal double Cloud => Responses.Environment(EnvironmentResponse.Cloud, [(int)Preset, Time - EventStart, timeScale, previousCloud]);

        internal WeatherSystem(int seed, double forestYearSeconds = EnvironmentSystem.SecondsPerForestYear, EcologyResponseProgram responseRules = null)
        {
            if (!EnvironmentSystem.IsValidForestYearSeconds(forestYearSeconds))
                throw new ArgumentOutOfRangeException(nameof(forestYearSeconds));
            ForestYearSeconds = forestYearSeconds;
            ResponseRules = responseRules;
            timeScale = forestYearSeconds / EnvironmentSystem.SecondsPerForestYear;
            random = unchecked((uint)seed) ^ 0x73A94F21u;
            if (random == 0) random = 1;
            var initial = (WeatherPreset)(int)Math.Round(Responses.Environment(EnvironmentResponse.InitialType, []));
            StartEvent(initial, Math.Max(.001, Responses.Environment(EnvironmentResponse.InitialDuration, [])) * timeScale,
                initial is WeatherPreset.Rain or WeatherPreset.Snow or WeatherPreset.Storm
                    ? Responses.Environment(EnvironmentResponse.InitialPeak, []) : 0);
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
            if (!Enum.IsDefined(preset)) throw new ArgumentOutOfRangeException(nameof(preset));
            if (peak < -1 || peak > 100) throw new ArgumentOutOfRangeException(nameof(peak));
            if (duration < -1 || (duration != -1 && duration < 20) || duration > 600)
                throw new ArgumentOutOfRangeException(nameof(duration));
            StartEvent(preset, duration < 0 ? Math.Max(.001, Responses.Environment(EnvironmentResponse.ForcedDuration, [(int)preset])) * timeScale : duration,
                preset is WeatherPreset.Rain or WeatherPreset.Storm or WeatherPreset.Snow
                    ? (peak < 0 ? Responses.Environment(EnvironmentResponse.ForcedPeak, [(int)preset]) : peak) : 0);
        }

        private void NextEvent()
        {
            var next = (WeatherPreset)(int)Math.Round(Responses.Environment(EnvironmentResponse.EventType, [Season, Random()]));
            double duration = Responses.Environment(EnvironmentResponse.EventDuration, [Season, (int)next, Random()]);
            double quarter = ForestYearSeconds / 4;
            double boundary = (Math.Floor(Time / quarter + 1e-9) + 1) * quarter;
            // Keep the seeded stream's draw count stable: dry events have no intensity draw.
            double peak = next is WeatherPreset.Snow or WeatherPreset.Storm or WeatherPreset.Rain
                ? Responses.Environment(EnvironmentResponse.EventPeak, [Season, (int)next, Random()]) : 0;
            StartEvent(next, Math.Min(Math.Max(0.001, duration) * timeScale, boundary - Time), peak);
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
            double rain = (RateAt(Time) + RateAt(Time + dt)) * 0.5 * dt * HoursPerSecond;
            Time += dt; TotalRain += rain; EventRain += rain;
            return new(dt, rain, forcing, Preset == WeatherPreset.Snow);
        }
    }
}
