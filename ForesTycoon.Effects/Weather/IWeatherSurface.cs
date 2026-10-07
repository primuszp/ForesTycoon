using OpenTK.Mathematics;

namespace ForesTycoon.Effects
{
    /// <summary>The ground that weather falls on: its extent and a height field for rain to land on.</summary>
    internal interface IWeatherSurface
    {
        int Columns { get; }
        int Rows { get; }
        /// <summary>Changes whenever the heights do (terrain edits, growing vegetation).</summary>
        ulong Revision { get; }
        void GetBounds(out Vector3 min, out Vector3 max);
        void GetVisibleBounds(out Vector2 min, out Vector2 max);
        /// <summary>Writes one height per column x row cell: ground or canopy top, whichever is higher.</summary>
        void FillHeights(float[] heights);
    }

    /// <summary>Display options of the weather and cloud effects.</summary>
    internal interface IWeatherSettings
    {
        bool Weather { get; }
        bool Lightning { get; }
        int LightningRequest { get; }
        bool AutomaticWeather { get; }
        Ecology.WeatherPreset Preset { get; }
        bool ExperimentalSnow { get; }
        int RainBudget { get; }
        int CloudSteps { get; }
    }
}
