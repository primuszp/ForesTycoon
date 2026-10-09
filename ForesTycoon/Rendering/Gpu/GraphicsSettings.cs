namespace ForesTycoon
{

    // Display options never change forestry, transport, or replay state.
    internal sealed class GraphicsSettings : IPostProcessSettings, IShadingSettings, IWeatherSettings
    {
        internal GraphicsQuality Quality = GraphicsQuality.High;
        internal int ShadowResolution => Quality == GraphicsQuality.Low ? 512 : Quality == GraphicsQuality.Medium ? 1024 : 2048;
        internal int FogSourceBudget => Quality == GraphicsQuality.Low ? 256 : Quality == GraphicsQuality.Medium ? 512 : 768;
        internal int FogLayers => Quality == GraphicsQuality.Low ? 2 : 3;
        internal int RainBudget => Quality == GraphicsQuality.Low ? 1500 : Quality == GraphicsQuality.Medium ? 3000 : 6000;
        internal int CloudSteps => Quality == GraphicsQuality.Low ? 6 : Quality == GraphicsQuality.Medium ? 8 : 12;
        internal bool Enhanced = true;
        internal bool Textures = true;
        internal bool ShowPlantations = true;
        internal bool VehicleOutlines = true;
        internal bool Wildlife = true;
        internal bool WildlifeOutlines = true;
        internal bool Lighting = true;
        internal bool Shadows = true;
        internal bool SawmillPreview;
        // Transient map view of the management lenses: trees hidden (their floor patch stays), so the tinted tiles show.
        internal bool HideTrees;
        internal bool ShowGrid = true;
        internal bool Weather = true;
        internal bool AutomaticWeather;
        internal bool Clouds = true;
        internal bool Lightning = true;
        internal bool Fog = true;
        internal int LightningRequest;
        internal float FogDensity = 0.65f;
        // Manual weather preview can enable snowfall; the simulated climate remains rain-based.
        internal bool ExperimentalSnow;
        internal WeatherPreset Preset = WeatherPreset.Sunny;
        internal float SunAzimuth = 135;
        internal float SunElevation = 48;
        /// <summary>Times of day and season colours drive the light from the calendar (the sun sliders then only show it).</summary>
        internal bool TimeOfDay = true;
        // Diorama post-processing: a miniature-photography look over the finished frame.
        internal bool Diorama = true;
        internal bool TiltShift = true;
        internal float TiltShiftStrength = 0.55f;
        internal bool AmbientOcclusion = true;
        internal float AmbientOcclusionStrength = 0.8f;
        internal bool ColorGrading = true;
        internal bool Vignette = true;
        internal bool StudioBackdrop = true;
        internal int TiltShiftTaps => Quality == GraphicsQuality.Low ? 8 : Quality == GraphicsQuality.Medium ? 12 : 20;
        internal int OcclusionPairs => Quality == GraphicsQuality.Low ? 0 : Quality == GraphicsQuality.Medium ? 4 : 8;

        bool IWeatherSettings.Weather => Weather;
        bool IWeatherSettings.Lightning => Lightning;
        int IWeatherSettings.LightningRequest => LightningRequest;
        bool IWeatherSettings.AutomaticWeather => AutomaticWeather;
        WeatherPreset IWeatherSettings.Preset => Preset;
        bool IWeatherSettings.ExperimentalSnow => ExperimentalSnow;
        int IWeatherSettings.RainBudget => RainBudget;
        int IWeatherSettings.CloudSteps => CloudSteps;
        bool IPostProcessSettings.Enhanced => Enhanced;
        bool IShadingSettings.Enhanced => Enhanced;
        bool IShadingSettings.Lighting => Lighting;
        bool IShadingSettings.Textures => Textures;
        bool IShadingSettings.ModelOutlines => WildlifeOutlines;
        float IShadingSettings.SunAzimuth => SunAzimuth;
        float IShadingSettings.SunElevation => SunElevation;
        bool IPostProcessSettings.Diorama => Diorama;
        bool IPostProcessSettings.StudioBackdrop => StudioBackdrop;
        bool IPostProcessSettings.AmbientOcclusion => AmbientOcclusion;
        float IPostProcessSettings.AmbientOcclusionStrength => AmbientOcclusionStrength;
        int IPostProcessSettings.OcclusionPairs => OcclusionPairs;
        bool IPostProcessSettings.ColorGrading => ColorGrading;
        bool IPostProcessSettings.TiltShift => TiltShift;
        float IPostProcessSettings.TiltShiftStrength => TiltShiftStrength;
        int IPostProcessSettings.TiltShiftTaps => TiltShiftTaps;
        bool IPostProcessSettings.Vignette => Vignette;
    }
}
