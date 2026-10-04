namespace ForesTycoon
{
    internal enum GraphicsQuality { Low, Medium, High }
    internal enum WeatherPreset { Sunny, Cloudy, Rain, Snow, Storm }
    internal enum ForestModelStyle { Imported, OriginalPine, Procedural, Generated }

    // Display options never change forestry, transport, or replay state.
    internal sealed class GraphicsSettings
    {
        internal GraphicsQuality Quality = GraphicsQuality.High;
        internal int ShadowResolution => Quality == GraphicsQuality.Low ? 512 : Quality == GraphicsQuality.Medium ? 1024 : 2048;
        internal int FogSourceBudget => Quality == GraphicsQuality.Low ? 256 : Quality == GraphicsQuality.Medium ? 512 : 768;
        internal int FogLayers => Quality == GraphicsQuality.Low ? 2 : 3;
        internal int RainBudget => Quality == GraphicsQuality.Low ? 1500 : Quality == GraphicsQuality.Medium ? 3000 : 6000;
        internal int CloudSteps => Quality == GraphicsQuality.Low ? 6 : Quality == GraphicsQuality.Medium ? 8 : 12;
        internal bool Enhanced = true;
        internal bool Textures = true;
        internal ForestModelStyle ForestModels = ForestModelStyle.Procedural;
        internal bool ImportedBirch;
        internal bool ShowPlantations = true;
        internal bool VehicleOutlines = true;
        internal bool Wildlife = true;
        internal bool WildlifeOutlines = true;
        internal bool Lighting = true;
        internal bool Shadows = true;
        internal bool SawmillPreview;
        internal bool ShowGrid = false;
        internal bool Weather = true;
        internal bool AutomaticWeather;
        internal bool Clouds = true;
        internal bool Lightning = true;
        internal bool Fog = true;
        internal int LightningRequest;
        internal float FogDensity = 0.65f;
        // Snow remains an isolated experiment; the normal game never enables it.
        internal bool ExperimentalSnow;
        internal WeatherPreset Preset = WeatherPreset.Sunny;
        internal float SunAzimuth = 135;
        internal float SunElevation = 48;
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
    }
}
